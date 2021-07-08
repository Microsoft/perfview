using FastSerialization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Address = System.UInt64;

namespace Graphs
{
    public class MemoryGraph : Graph, IFastSerializable
    {
        public MemoryGraph(int expectedSize)
            : base(expectedSize)
        {
            m_addressToNodeIndex = new Dictionary<nuint, NodeIndex>(expectedSize, NativeIntegerEqualityComparer.Instance);
            m_nodeAddresses = new SegmentedList<nuint>(SegmentSize, expectedSize);
        }

        public void WriteAsBinaryFile(string outputFileName)
        {
            Serializer serializer = new Serializer(outputFileName, this);
            serializer.Close();
        }
        public static MemoryGraph ReadFromBinaryFile(string inputFileName)
        {
            Deserializer deserializer = new Deserializer(inputFileName);
            deserializer.TypeResolver = typeName => System.Type.GetType(typeName);  // resolve types in this assembly (and mscorlib)
            deserializer.RegisterFactory(typeof(MemoryGraph), delegate () { return new MemoryGraph(1); });
            deserializer.RegisterFactory(typeof(Graphs.Module), delegate () { return new Graphs.Module(0); });
            return (MemoryGraph)deserializer.GetEntryObject();
        }

        /// <summary>
        /// Indicates whether the memory addresses are 64 bit or not.   Note that this is not set
        /// as part of normal graph processing, it needs to be set by the caller.   MemoryGraph is only 
        /// acting as storage.  
        /// </summary>
        public bool Is64Bit { get; set; }
        public Address GetAddress(NodeIndex nodeIndex)
        {
            if (nodeIndex == NodeIndex.Invalid)
            {
                return 0;
            }

            return m_nodeAddresses[(int)nodeIndex];
        }
        public void SetAddress(NodeIndex nodeIndex, Address nodeAddress)
        {
            Debug.Assert(m_nodeAddresses[(int)nodeIndex] == 0, "Calling SetAddress twice for node index " + nodeIndex);
            m_nodeAddresses[(int)nodeIndex] = checked((nuint)nodeAddress);
        }
        public override NodeIndex CreateNode()
        {
            var ret = base.CreateNode();
            m_nodeAddresses.Add(0);
            Debug.Assert(m_nodeAddresses.Count == m_nodes.Count);
            return ret;
        }
        public override Node AllocNodeStorage()
        {
            return new MemoryNode(this);
        }
        public override long SizeOfGraphDescription()
        {
            return base.SizeOfGraphDescription() + 8 * (long)m_nodeAddresses.Count;
        }
        /// <summary>
        /// Returns the number of distinct references in the graph so far (the size of the interning table).  
        /// </summary>
        public int DistinctRefCount { get { return m_addressToNodeIndex.Count; } }

        #region protected
        /// <summary>
        /// Clear puts it back into the state that existed after the constructor returned
        /// </summary>
        protected override void Clear()
        {
            base.Clear();
            m_addressToNodeIndex.Clear();
            m_nodeAddresses.Count = 0;
        }

        public override void AllowReading()
        {
            m_addressToNodeIndex = null;            // We are done with this, abandon it.  
            base.AllowReading();
        }

        /// <summary>
        /// GetNodeIndex maps an Memory address of an object (used by CLRProfiler), to the NodeIndex we have assigned to it
        /// It is essentially an interning table (we assign it new index if we have  not seen it before)
        /// </summary>
        public NodeIndex GetNodeIndex(Address objectAddress)
        {
            nuint nativeObjectAddress = ToNativeAddress(objectAddress);
            NodeIndex nodeIndex;
            if (!m_addressToNodeIndex.TryGetValue(nativeObjectAddress, out nodeIndex))
            {
                nodeIndex = CreateNode();
                m_nodeAddresses[(int)nodeIndex] = nativeObjectAddress;
                m_addressToNodeIndex.Add(nativeObjectAddress, nodeIndex);
            }
            Debug.Assert(m_nodeAddresses[(int)nodeIndex] == ToNativeAddress(objectAddress));
            return nodeIndex;
        }
        public bool IsInGraph(Address objectAddress)
        {
            return m_addressToNodeIndex.ContainsKey(ToNativeAddress(objectAddress));
        }

        private static nuint ToNativeAddress(Address objectAddress)
        {
            if (IntPtr.Size == 8)
            {
                return checked((nuint)objectAddress);
            }
            else
            {
                const ulong Mask32 = ~(ulong)uint.MaxValue;
                const ulong Mask33 = Mask32 | 0x80000000;
                if ((objectAddress & Mask32) == 0)
                {
                    // not a sign-extended address
                    return checked((nuint)objectAddress);
                }
                else if ((objectAddress & Mask33) == Mask33)
                {
                    // This is an incorrectly sign-extended 32-bit address
                    return unchecked((nuint)objectAddress);
                }
                else
                {
                    throw new ArgumentException("Unsupported object address", nameof(objectAddress));
                }
            }
        }

        /// <summary>
        /// ClrProfiler identifes nodes  using the physical address in Memory.  'Graph' needs it to be an NodeIndex.   
        /// THis table maps the ID that CLRProfiler uses (an address), to the NodeIndex we have assigned to it.  
        /// It is only needed while the file is being read in.  
        /// </summary>
        protected Dictionary<nuint, NodeIndex> m_addressToNodeIndex;    // This field is only used during construction

        #endregion
        #region private
        void IFastSerializable.ToStream(Serializer serializer)
        {
            base.ToStream(serializer);
            // Write out the Memory addresses of each object 
            serializer.Write(m_nodeAddresses.Count);

            // Write m_nodeAddresses as a sequence of groups of addresses near each other. The following assumptions are
            // made for this process:
            //
            // 1. It is common for multiple objects to have addresses within ushort.MaxValue of each other
            // 2. It is common for an element of m_nodeAddresses to have the address 0
            // 3. The address at index 'i+1' will never be the same value as index 'i', unless that value is 0
            //
            // Assumption (3) allows '0' values in m_nodeAddresses to be written with the differential value '0', which
            // is efficient and does not interrupt the grouping of a segment of otherwise-similar addresses.
            int offset = 0;
            foreach (var pair in GroupNodeAddresses(m_nodeAddresses))
            {
                // A group is written as:
                //
                // 1. Int32: The number of elements in the group
                // 2. Int64: The address of the first element in the group
                // 3. UInt16 (repeat N times, where N = #Group - 1):
                //    a. 0, if the nth element of the group has the address 0
                //    b. Otherwise, the offset of the nth relative to the address of the first element in the group
                serializer.Write(pair.Value);
                serializer.Write(pair.Key);
                Debug.Assert(pair.Key == m_nodeAddresses[offset]);

                for (int i = 1; i < pair.Value; i++)
                {
                    Address current = m_nodeAddresses[i + offset];
                    if (current == 0)
                    {
                        serializer.Write((short)0);
                        continue;
                    }

                    ushort relativeAddress = (ushort)(current - pair.Key);
                    serializer.Write(relativeAddress);
                }

                offset += pair.Value;
            }

            serializer.WriteTagged(Is64Bit);

            IEnumerable<KeyValuePair<Address, int>> GroupNodeAddresses(SegmentedList<nuint> nodeAddresses)
            {
                if (nodeAddresses.Count == 0)
                    yield break;

                var baseAddress = nodeAddresses[0];
                var startIndex = 0;
                for (int i = 1; i < nodeAddresses.Count; i++)
                {
                    var current = nodeAddresses[i];
                    if (current == 0)
                    {
                        continue;
                    }

                    if (unchecked(current - baseAddress) <= ushort.MaxValue)
                    {
                        continue;
                    }

                    var count = i - startIndex;
                    yield return new KeyValuePair<Address, int>(baseAddress, count);

                    baseAddress = current;
                    startIndex = i;
                }

                yield return new KeyValuePair<Address, int>(baseAddress, nodeAddresses.Count - startIndex);
            }
        }

        void IFastSerializable.FromStream(Deserializer deserializer)
        {
            base.FromStream(deserializer);
            // Read in the Memory addresses of each object 
            int addressCount = deserializer.ReadInt();
            m_nodeAddresses = new SegmentedList<nuint>(SegmentSize, addressCount);

            // See ToStream above for a description of the differential compression process
            int offset = 0;
            while (offset < addressCount)
            {
                int groupCount = deserializer.ReadInt();
                nuint baseAddress = checked((nuint)deserializer.ReadUInt64());
                m_nodeAddresses.Add(baseAddress);
                for (int i = 1; i < groupCount; i++)
                {
                    ushort relativeAddress = deserializer.ReadUInt16();
                    if (relativeAddress == 0)
                    {
                        m_nodeAddresses.Add(0);
                    }
                    else
                    {
                        m_nodeAddresses.Add(baseAddress + relativeAddress);
                    }
                }

                offset += groupCount;
            }

            bool is64bit = false;
            deserializer.TryReadTagged(ref is64bit);
            Is64Bit = is64bit;
        }

        // This array survives after the constructor completes
        // TODO Fold this into the existing blob. Currently this dominates the Size cost of the graph!
        protected SegmentedList<nuint> m_nodeAddresses;
        #endregion
    }

    internal sealed class NativeIntegerEqualityComparer : IEqualityComparer<nuint>, IEqualityComparer<nint>
    {
        public static readonly NativeIntegerEqualityComparer Instance = new();

        private NativeIntegerEqualityComparer()
        {
        }

        public bool Equals(nint x, nint y)
        {
            return x == y;
        }

        public bool Equals(nuint x, nuint y)
        {
            return x == y;
        }

        public int GetHashCode(nint obj)
        {
            return obj.GetHashCode();
        }

        public int GetHashCode(nuint obj)
        {
            return obj.GetHashCode();
        }
    }

    /// <summary>
    /// Support class for code:MemoryGraph
    /// </summary>
    public class MemoryNode : Node
    {
        public Address Address { get { return m_memoryGraph.GetAddress(Index); } }
        #region private
        internal MemoryNode(MemoryGraph graph)
            : base(graph)
        {
            m_memoryGraph = graph;
        }

        public override void WriteXml(System.IO.TextWriter writer, bool includeChildren = true, string prefix = "", NodeType typeStorage = null, string additinalAttribs = "")
        {
            Address end = Address + (uint)Size;
            // base.WriteXml(writer, prefix, storage, typeStorage, additinalAttribs + " Address=\"0x" + Address.ToString("x") + "\"");
            base.WriteXml(writer, includeChildren, prefix, typeStorage,
                additinalAttribs + " Address=\"0x" + Address.ToString("x") + "\""
                                 + " End=\"0x" + end.ToString("x") + "\"");
        }

        private MemoryGraph m_memoryGraph;
        #endregion
    }

    /// <summary>
    /// MemoryNodeBuilder is helper class for building a MemoryNode graph.   Unlike
    /// MemoryNode you don't have to know the complete set of children at the time
    /// you create the node.  Instead you can keep adding children to it incrementally
    /// and when you are done you call Build() which finalizes it (and all its children)
    /// </summary>
    public class MemoryNodeBuilder
    {
        public MemoryNodeBuilder(MemoryGraph graph, string typeName, string moduleName = null, NodeIndex nodeIndex = NodeIndex.Invalid)
        {
            Debug.Assert(typeName != null);
            m_graph = graph;
            TypeName = typeName;
            Index = nodeIndex;
            if (Index == NodeIndex.Invalid)
            {
                Index = m_graph.CreateNode();
            }

            Debug.Assert((StreamLabel)m_graph.m_nodes[(int)Index] == m_graph.m_undefinedObjDef, "SetNode cannot be called on the nodeIndex passed");
            ModuleName = moduleName;
            m_mutableChildren = new List<MemoryNodeBuilder>();
            m_unmutableChildren = new HashSet<NodeIndex>();
            m_typeIndex = NodeTypeIndex.Invalid;
        }

        public string TypeName { get; private set; }
        public string ModuleName { get; private set; }
        public int Size { get; set; }
        public NodeIndex Index { get; private set; }

        /// <summary>
        /// Looks for a child with the type 'childTypeName' and returns it.  If it is not
        /// present, it will be created.  Note it will ONLY find MutableNode children
        /// (not children added with AddChild(NodeIndex).  
        /// </summary>
        public MemoryNodeBuilder FindOrCreateChild(string childTypeName, string childModuleName = null)
        {
            foreach (var child in m_mutableChildren)
            {
                if (child.TypeName == childTypeName)
                {
                    return child;
                }
            }

            var ret = new MemoryNodeBuilder(m_graph, childTypeName, childModuleName);
            AddChild(ret);
            return ret;
        }
        public void AddChild(MemoryNodeBuilder child)
        {
            m_unmutableChildren.Add(child.Index);
            m_mutableChildren.Add(child);
        }
        public void AddChild(NodeIndex child)
        {
            m_unmutableChildren.Add(child);
        }

        /// <summary>
        /// This is optional phase, if you don't do it explicitly, it gets done at Build time. 
        /// </summary>
        public void AllocateTypeIndexes()
        {
            AllocateTypeIndexes(new Dictionary<string, NodeTypeIndex>());
        }

        public NodeIndex Build()
        {
            if (m_typeIndex == NodeTypeIndex.Invalid)
            {
                AllocateTypeIndexes();
            }

            if (m_mutableChildren != null)
            {
                Debug.Assert(m_unmutableChildren.Count >= m_mutableChildren.Count);
                m_graph.SetNode(Index, m_typeIndex, Size, m_unmutableChildren);
                var mutableChildren = m_mutableChildren;
                m_mutableChildren = null;           // Signals that I have been built
                foreach (var child in mutableChildren)
                {
                    child.Build();
                }
            }
            return Index;
        }

        #region private
        private void AllocateTypeIndexes(Dictionary<string, NodeTypeIndex> types)
        {
            if (m_mutableChildren != null)
            {
                Debug.Assert(m_unmutableChildren.Count >= m_mutableChildren.Count);
                if (!types.TryGetValue(TypeName, out m_typeIndex))
                {
                    m_typeIndex = m_graph.CreateType(TypeName, ModuleName);
                    types.Add(TypeName, m_typeIndex);
                }
                foreach (var child in m_mutableChildren)
                {
                    child.AllocateTypeIndexes(types);
                }
            }
        }

        private NodeTypeIndex m_typeIndex;
        private List<MemoryNodeBuilder> m_mutableChildren;
        private HashSet<NodeIndex> m_unmutableChildren;
        private MemoryGraph m_graph;
        #endregion
    }
}

#if false 
namespace Graphs.Samples
{
    class Sample
    {
        static void Main()
        {
            int expectedNumberOfNodes = 1000;
            MemoryGraph memoryGraph = new MemoryGraph(expectedNumberOfNodes);

            GrowableArray<NodeIndex> tempForChildren = new GrowableArray<NodeIndex>();


            // We can make a new Node index
            NodeIndex newNodeIdx = memoryGraph.CreateNode();

            NodeIndex childIdx = memoryGraph.CreateNode();


            // 
            NodeTypeIndex newNodeType = memoryGraph.CreateType("MyChild");



            memoryGraph.SetNode(childIdx, newType, 100, tempForChildren);





            memoryGraph.AllowReading();

            // Serialize to a file
            memoryGraph.WriteAsBinaryFile("file.gcHeap");



            // Can unserialize easily. 
            // var readBackIn = MemoryGraph.ReadFromBinaryFile("file.gcHeap");
        }

    }
}
#endif
