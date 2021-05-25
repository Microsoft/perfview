using FastSerialization;    // For IStreamReader
using Graphs;
using Microsoft.Diagnostics.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Address = System.UInt64;

// Graph contains generic Graph-Node traversal algorithms (spanning tree etc).
namespace Graphs
{
    /// <summary>
    /// A graph is representation of a node-arc graph.    It tries to be very space efficient.   It is a little
    /// more complex than the  most basic node-arc graph in that each node can have a code:NodeType associated with it 
    /// that contains information that is shared among many nodes.   
    /// 
    /// While the 'obvious' way of representing a graph is to have a 'Node' object that has arcs, we don't do this. 
    /// Instead each node is give an unique code:NodeIndex which represents the node and each node has a list of
    /// NodeIndexes for each of the children.   Using indexes instead of object pointers is valuable because
    /// 
    ///     * You can save 8 bytes (on 32 bit) of .NET object overhead and corresponding cost at GC time by using
    ///       indexes.   This is significant because there can be 10Meg of objects, so any expense adds up
    ///     * Making the nodes be identified by index is more serialization friendly.   It is easier to serialize
    ///       the graph if it has this representation.  
    ///     * It easily allows 3rd parties to 'attach' their own information to each node.  All they need is to
    ///       create an array of the extra information indexed by NodeIndex.   The 'NodeIndexLimit' is designed
    ///       specifically for this purpose.  
    ///       
    /// Because we anticipate VERY large graphs (e.g. dumps of the GC heap) the representation for the nodes is 
    /// very space efficient and we don't have code:Node class object for most of the nodes in the graph.  However
    /// it IS useful to have code:Node objects for the nodes that are being manipulated locally.  
    ///
    /// To avoid creating lots of code:Node objects that die quickly the API adopts the convention that the
    /// CALLer provides a code:Node class as 'storage' when the API needs to return a code:Node.   That way
    /// APIs that return code:Node never allocate.    This allows most graph algorithms to work without having
    /// to allocate more than a handful of code:Node classes, reducing overhead.   You allocate these storage
    /// nodes with the code:Graph.AllocNodeStorage call
    /// 
    /// Thus the basic flow is you call code:Graph.AllocNodeStorage to allocate storage, then call code:Graph.GetRoot
    /// to get your first node.  If you need to 'hang' additional information off he nodes, you allocate an array
    /// of Size code:Graph.NodeIndexLimit to hold it (for example a 'visited' bit).   Then repeatedly call 
    /// code:Node.GetFirstChild, code:Node.GetNextChild to get the children of a node to traverse the graph.
    /// 
    /// OVERHEAD
    ///
    ///     1) 4 bytes per Node for the pointer to the stream for the rest of the data (thus we can have at most 4Gig nodes)
    ///     2) For each node, the number of children, the nodeId, and children are stored as compressed (relative) indexes 
    ///        (figure 1 byte for # of children, 2 bytes per type id, 2-3 bytes per child)
    ///     3) Variable length nodes also need a compressed int for the Size of the node (1-3 bytes)
    ///     4) Types store the name (2 bytes per character), and Size (4 bytes), but typically don't dominate 
    ///        Size of graph.  
    ///
    /// Thus roughly 7 bytes per node + 3 bytes per reference.   Typically nodes on average have 2-3 references, so
    /// figure 13-16 bytes per node.  That gives you 125 Million nodes in a 2 Gig of Memory. 
    /// 
    /// The important point here is that representation of a node is always smaller than the Memory it represents, and
    /// and often significantly smaller (since it does not hold non-GC data, null pointers and even non-null pointers 
    /// are typically half the Size).   For 64 bit heaps, the Size reduction is even more dramatic.  
    /// 
    /// see code:Graph.SizeOfGraphDescription to determine the overhead for any particular graph.
    /// 
    /// </summary>
    public class Graph : IFastSerializable, IFastSerializableVersion
    {
        /// <summary>
        /// Given an arbitrary code:NodeIndex that identifies the node, Get a code:Node object.  
        /// 
        /// This routine does not allocated but uses the space passed in by 'storage.  
        /// 'storage' should be allocated with coode:AllocNodeStorage, and should be aggressively reused.  
        /// </summary>
        public Node GetNode(NodeIndex nodeIndex, Node storage)
        {
            Debug.Assert(storage.m_graph == this);
            storage.m_index = nodeIndex;
            return storage;
        }
        /// <summary>
        /// returns true if SetNode has been called on this node (it is not an undefined object).  
        /// TODO FIX NOW used this instead of the weird if node index grows technique. 
        /// </summary>
        public bool IsDefined(NodeIndex nodeIndex) { return m_nodes[(int)nodeIndex] != m_undefinedObjDef; }
        /// <summary>
        /// Given an arbitrary code:NodeTypeIndex that identifies the nodeId of the node, Get a code:NodeType object.  
        /// 
        /// This routine does not allocated but overwrites the space passed in by 'storage'.  
        /// 'storage' should be allocated with coode:AllocNodeTypeStorage, and should be aggressively reused.  
        /// 
        /// Note that this routine does not get used much, instead Node.GetType is normal way of getting the nodeId.  
        /// </summary>
        public NodeType GetType(NodeTypeIndex nodeTypeIndex, NodeType storage)
        {
            storage.m_index = nodeTypeIndex;
            Debug.Assert(storage.m_graph == this);
            return storage;
        }

        // Storage allocation
        /// <summary>
        /// Allocates nodes to be used as storage for methods like code:GetRoot, code:Node.GetFirstChild and code:Node.GetNextChild
        /// </summary>
        /// <returns></returns>
        public virtual Node AllocNodeStorage()
        {
            return new Node(this);
        }
        /// <summary>
        /// Allocates nodes to be used as storage for methods like code:GetType
        /// </summary>
        public virtual NodeType AllocTypeNodeStorage()
        {
            return new NodeType(this);
        }

        /// <summary>
        /// It is expected that users will want additional information associated with nodes of the graph.  They can
        /// do this by allocating an array of code:NodeIndexLimit and then indexing this by code:NodeIndex
        /// </summary>
        public NodeIndex NodeIndexLimit { get { return (NodeIndex)m_nodes.Count; } }
        /// <summary>
        /// Same as NodeIndexLimit, just cast to an integer.  
        /// </summary>
        public int NodeCount { get { return m_nodes.Count; } }
        /// <summary>
        /// It is expected that users will want additional information associated with TYPES of the nodes of the graph.  They can
        /// do this by allocating an array of code:NodeTypeIndexLimit and then indexing this by code:NodeTypeIndex
        /// </summary>
        public NodeTypeIndex NodeTypeIndexLimit { get { return (NodeTypeIndex)m_types.Count; } }
        /// <summary>
        /// Same as NodeTypeIndex cast as an integer.  
        /// </summary>
        public int NodeTypeCount { get { return m_types.Count; } }
        /// <summary>
        /// When a Node is created, you specify how big it is.  This the sum of all those sizes.  
        /// </summary>
        public long TotalSize { get { return m_totalSize; } }
        /// <summary>
        /// The number of references (arcs) in the graph
        /// </summary>
        public int TotalNumberOfReferences { get { return m_totalRefs; } }
        /// <summary>
        /// Specifies the size of each segment in the segmented list.
        /// However, this value must be a power of two or the list will throw an exception.
        /// Considering this requirement and the size of each element as 8 bytes,
        /// the current value will keep its size at approximately 64K.
        /// Having a lesser size than 85K will keep the segments out of the Large Object Heap,
        /// permitting the GC to free up memory by compacting the segments within the heap.
        /// </summary>
        protected const int SegmentSize = 8_192;

        // Creation methods.  
        /// <summary>
        /// Create a new graph from 'nothing'.  Note you are not allowed to read from the graph
        /// until you execute 'AllowReading'.  
        /// 
        /// You can actually continue to write after executing 'AllowReading' however you should
        /// any additional nodes you write should not be accessed until you execute 'AllowReading'
        /// again.  
        /// 
        /// TODO I can eliminate the need for AllowReading.  
        /// </summary>
        public Graph(int expectedNodeCount)
        {
            m_expectedNodeCount = expectedNodeCount;
            m_types = new GrowableArray<TypeInfo>(Math.Max(expectedNodeCount / 100, 2000));
            m_nodes = new SegmentedList<StreamLabel>(SegmentSize, m_expectedNodeCount);
            RootIndex = NodeIndex.Invalid;
            ClearWorker();
        }
        /// <summary>
        /// The NodeIndex of the root node of the graph.   It must be set sometime before calling AllowReading
        /// </summary>
        public NodeIndex RootIndex;
        /// <summary>
        /// Create a new nodeId with the given name and return its node nodeId index.   No interning is done (thus you can
        /// have two distinct NodeTypeIndexes that have exactly the same name.  
        /// 
        /// By default the size = -1 which indicates we will set the type size to the first 'SetNode' for this type.  
        /// </summary>
        public virtual NodeTypeIndex CreateType(string name, string moduleName = null, int size = -1)
        {
            var ret = (NodeTypeIndex)m_types.Count;
            TypeInfo typeInfo = new TypeInfo();
            typeInfo.Name = name;
            typeInfo.ModuleName = moduleName;
            typeInfo.Size = size;
            m_types.Add(typeInfo);
            return ret;
        }
        /// <summary>
        /// Create a new node and return its index.   It is undefined until code:SetNode is called.   We allow undefined nodes
        /// because graphs have loops in them, and thus you need to refer to a node, before you know all the data in the node.
        /// 
        /// It is really expected that every node you did code:CreateNode on you also ultimately do a code:SetNode on.  
        /// </summary>
        /// <returns></returns>
        public virtual NodeIndex CreateNode()
        {
            var ret = (NodeIndex)m_nodes.Count;
            m_nodes.Add(m_undefinedObjDef);
            return ret;
        }
        /// <summary>
        /// Sets the information associated with the node at 'nodeIndex' (which was created via code:CreateNode).  Nodes
        /// have a nodeId, Size and children.  (TODO: should Size be here?)
        /// </summary>
        public void SetNode(NodeIndex nodeIndex, NodeTypeIndex typeIndex, int sizeInBytes, GrowableArray<NodeIndex> children)
        {
            SetNodeTypeAndSize(nodeIndex, typeIndex, sizeInBytes);

            Node.WriteCompressedInt(m_writer, children.Count);
            for (int i = 0; i < children.Count; i++)
            {
                Node.WriteCompressedInt(m_writer, (int)children[i] - (int)nodeIndex);
            }
            m_totalRefs += children.Count;
        }

        /// <summary>
        /// When a graph is constructed with the default constructor, it is in 'write Mode'  You can't read from it until 
        /// you call 'AllowReading' which puts it in 'read mode'.  
        /// </summary>
        public virtual void AllowReading()
        {
            Debug.Assert(m_reader == null && m_writer != null);
            Debug.Assert(RootIndex != NodeIndex.Invalid);
            m_reader = m_writer.GetReader();
            m_writer = null;
            if (RootIndex == NodeIndex.Invalid)
            {
                throw new ApplicationException("RootIndex not set.");
            }
#if false
            // Validate that any referenced node was actually defined and that all node indexes are within range;
            var nodeStorage = AllocNodeStorage();
            for (NodeIndex nodeIndex = 0; nodeIndex < NodeIndexLimit; nodeIndex++)
            {
                var node = GetNode(nodeIndex, nodeStorage);
                Debug.Assert(node.Index != NodeIndex.Invalid);
                Debug.Assert(node.TypeIndex < NodeTypeIndexLimit);
                for (var childIndex = node.GetFirstChildIndex(); childIndex != null; childIndex = node.GetNextChildIndex())
                    Debug.Assert(0 <= childIndex && childIndex < NodeIndexLimit);
                if (!node.Defined)
                    Debug.WriteLine("Warning: undefined object " + nodeIndex);
            }
#endif
        }
        /// <summary>
        /// Used for debugging, returns the node Count and typeNode Count. 
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return string.Format("Graph of {0} nodes and {1} types.  Size={2:f3}MB SizeOfDescription={3:f3}MB",
                NodeIndexLimit, NodeTypeIndexLimit, TotalSize / 1000000.0, SizeOfGraphDescription() / 1000000.0);
        }
        // Performance 
        /// <summary>
        /// A pretty good estimate of the how many bytes of Memory it takes just to represent the graph itself. 
        /// 
        /// TODO: Currently this is only correct for the 32 bit version.  
        /// </summary>
        public virtual long SizeOfGraphDescription()
        {
            if (m_reader == null)
            {
                return 0;
            }

            int sizeOfTypes = 0;
            int sizeOfTypeInfo = 8;
            for (int i = 0; i < m_types.Count; i++)
            {
                var typeName = m_types[i].Name;
                var typeNameLen = 0;
                if (typeName != null)
                {
                    typeNameLen = typeName.Length * 2;
                }

                sizeOfTypes += sizeOfTypeInfo + typeNameLen;
            }

            return sizeOfTypes + m_reader.Length + m_nodes.Count * 4;
        }

        /* APIs for deferred lookup of type names */
        /// <summary>
        /// Graph supports the ability to look up the names of a type at a later time.   You use this by 
        /// calling this overload in which you give a type ID (e.g. an RVA) and a module index (from 
        /// CreateModule) to this API.   If later you override the 'ResolveTypeName' delegate below
        /// then when type names are requested you will get back the typeID and module which you an
        /// then use to look up the name (when you do have the PDB). 
        /// 
        /// The Module passed should be reused as much as possible to avoid bloated files.  
        /// </summary>
        public NodeTypeIndex CreateType(int typeID, Module module, int size = -1, string typeNameSuffix = null)
        {
            // make sure the m_types and m_deferedTypes arrays are in sync.  
            while (m_deferedTypes.Count < m_types.Count)
            {
                m_deferedTypes.Add(new DeferedTypeInfo());
            }

            var ret = (NodeTypeIndex)m_types.Count;
            // We still use the m_types array for the size. 
            m_types.Add(new TypeInfo() { Size = size });

            // but we put the real information into the m_deferedTypes.  
            m_deferedTypes.Add(new DeferedTypeInfo() { Module = module, TypeID = typeID, TypeNameSuffix = typeNameSuffix });
            Debug.Assert(m_deferedTypes.Count == m_types.Count);
            return ret;
        }
        /// <summary>
        /// In advanced scenarios you may not be able to provide a type name when you create the type.  YOu can pass null
        /// for the type name to 'CreateType'   If you provide this callback, later you can provide the mapping from 
        /// type index to name (e.g. when PDBs are available).    Note that this field is NOT serialized.   
        /// </summary>
        public Func<int, Module, string> ResolveTypeName { get; set; }
        /// <summary>
        /// Where any types in the graph creates with the CreateType(int typeID, Module module, int size) overload?
        /// </summary>
        public bool HasDeferedTypeNames { get { return m_deferedTypes.Count > 0; } }

        /* See GraphUtils class for more things you can do with a Graph. */
        // TODO move these to GraphUtils. 
        // Utility (could be implemented using public APIs).  
        public void BreadthFirstVisit(Action<Node> visitor)
        {
            var nodeStorage = AllocNodeStorage();
            var visited = new bool[(int)NodeIndexLimit];
            var work = new Queue<NodeIndex>();
            work.Enqueue(RootIndex);
            while (work.Count > 0)
            {
                var nodeIndex = work.Dequeue();
                var node = GetNode(nodeIndex, nodeStorage);
                visitor(node);
                for (var childIndex = node.GetFirstChildIndex(); childIndex != NodeIndex.Invalid; childIndex = node.GetNextChildIndex())
                {
                    if (!visited[(int)childIndex])
                    {
                        visited[(int)childIndex] = true;
                        work.Enqueue(childIndex);
                    }
                }
            }
        }

        public SizeAndCount[] GetHistogramByType()
        {
            var ret = new SizeAndCount[(int)NodeTypeIndexLimit];
            for (int i = 0; i < ret.Length; i++)
            {
                ret[i] = new SizeAndCount((NodeTypeIndex)i);
            }

            var nodeStorage = AllocNodeStorage();
            for (NodeIndex idx = 0; idx < NodeIndexLimit; idx++)
            {
                var node = GetNode(idx, nodeStorage);
                var sizeAndCount = ret[(int)node.TypeIndex];
                sizeAndCount.Count++;
                sizeAndCount.Size += node.Size;
            }

            Array.Sort(ret, delegate (SizeAndCount x, SizeAndCount y)
            {
                return y.Size.CompareTo(x.Size);
            });
#if DEBUG
            int totalCount = 0;
            long totalSize = 0;
            foreach (var sizeAndCount in ret)
            {
                totalCount += sizeAndCount.Count;
                totalSize += sizeAndCount.Size;
            }
            Debug.Assert(TotalSize == totalSize);
            Debug.Assert((int)NodeIndexLimit == totalCount);
#endif
            return ret;
        }
        public class SizeAndCount
        {
            public SizeAndCount(NodeTypeIndex typeIdx) { TypeIdx = typeIdx; }
            public readonly NodeTypeIndex TypeIdx;
            public long Size;
            public int Count;
        }
        public string HistogramByTypeXml(long minSize = 0)
        {
            var sizeAndCounts = GetHistogramByType();
            StringWriter sw = new StringWriter();
            sw.WriteLine("<HistogramByType Size=\"{0}\" Count=\"{1}\">", TotalSize, (int)NodeIndexLimit);
            var typeStorage = AllocTypeNodeStorage();
            foreach (var sizeAndCount in sizeAndCounts)
            {
                if (sizeAndCount.Size <= minSize)
                {
                    break;
                }

                sw.WriteLine("  <Type Name=\"{0}\" Size=\"{1}\" Count=\"{2}\"/>",
                    XmlUtilities.XmlEscape(GetType(sizeAndCount.TypeIdx, typeStorage).Name), sizeAndCount.Size, sizeAndCount.Count);
            }
            sw.WriteLine("</HistogramByType>");
            return sw.ToString();
        }

        #region private

        internal void SetNodeTypeAndSize(NodeIndex nodeIndex, NodeTypeIndex typeIndex, int sizeInBytes)
        {
            Debug.Assert(m_nodes[(int)nodeIndex] == m_undefinedObjDef, "Calling SetNode twice for node index " + nodeIndex);
            m_nodes[(int)nodeIndex] = m_writer.GetLabel();

            Debug.Assert(sizeInBytes >= 0);
            // We are going to assume that if this is negative it is because it is a large positive number.  
            if (sizeInBytes < 0)
            {
                sizeInBytes = int.MaxValue;
            }

            int typeAndSize = (int)typeIndex << 1;
            TypeInfo typeInfo = m_types[(int)typeIndex];
            if (typeInfo.Size < 0)
            {
                typeInfo.Size = sizeInBytes;
                m_types[(int)typeIndex] = typeInfo;
            }
            if (typeInfo.Size == sizeInBytes)
            {
                Node.WriteCompressedInt(m_writer, typeAndSize);
            }
            else
            {
                typeAndSize |= 1;
                Node.WriteCompressedInt(m_writer, typeAndSize);
                Node.WriteCompressedInt(m_writer, sizeInBytes);
            }

            m_totalSize += sizeInBytes;
        }

        /// <summary>
        /// Clear handles puts it back into the state that existed after the constructor returned
        /// </summary>
        protected virtual void Clear()
        {
            ClearWorker();
        }

        /// <summary>
        /// ClearWorker does only that part of clear needed for this level of the hierarchy (and needs
        /// to be done by the constructor too). 
        /// </summary>
        private void ClearWorker()
        {
            RootIndex = NodeIndex.Invalid;
            if (m_writer == null)
            {
                m_writer = new SegmentedMemoryStreamWriter(m_expectedNodeCount * 8);
            }

            m_totalSize = 0;
            m_totalRefs = 0;
            m_types.Count = 0;
            m_writer.Clear();
            m_nodes.Count = 0;

            // Create an undefined node, kind of gross because SetNode expects to have an entry
            // in the m_nodes table, so we make a fake one and then remove it.  
            m_undefinedObjDef = m_writer.GetLabel();
            m_nodes.Add(m_undefinedObjDef);
            SetNode(0, CreateType("UNDEFINED"), 0, new GrowableArray<NodeIndex>());
            Debug.Assert(m_nodes[0] == m_undefinedObjDef);
            m_nodes.Count = 0;
        }

        // To support very space efficient encodings, and to allow for easy serialiation (persistence to file)
        // Types are given an index and their data is stored in a m_types array.  TypeInfo is the data in this
        // array.  
        internal struct TypeInfo
        {
            public string Name;                         // If DeferredTypeInfo.Module != null then this is a type name suffix.  
            public int Size;
            public string ModuleName;                   // The name of the module which contains the type (if known).  
        }
        internal struct DeferedTypeInfo
        {
            public int TypeID;
            public Module Module;                       // The name of the module which contains the type (if known).
            public string TypeNameSuffix;               // if non-null it is added to the type name as a suffix.   
        }

        public virtual void ToStream(Serializer serializer)
        {
            serializer.Write(m_totalSize);
            serializer.Write((int)RootIndex);
            // Write out the Types 
            serializer.Write(m_types.Count);
            for (int i = 0; i < m_types.Count; i++)
            {
                serializer.Write(m_types[i].Name);
                serializer.Write(m_types[i].Size);
                serializer.Write(m_types[i].ModuleName);
            }

            // Write out the Nodes 
            serializer.Write(m_nodes.Count);
            for (int i = 0; i < m_nodes.Count; i++)
            {
                serializer.Write((int)m_nodes[i]);
            }

            // Write out the Blob stream.  
            // TODO this is inefficient.  Also think about very large files.  
            int readerLen = (int)m_reader.Length;
            serializer.Write(readerLen);
            m_reader.Goto((StreamLabel)0);
            for (uint i = 0; i < readerLen; i++)
            {
                serializer.Write(m_reader.ReadByte());
            }

            // Are we writing a format for 1 or greater?   If so we can use the new (breaking) format, otherwise
            // to allow old readers to read things, we give up on the new data.  
            if (1 <= ((IFastSerializableVersion)this).MinimumReaderVersion)
            {
                // Because Graph has superclass, you can't add objects to the end of it (since it is not 'the end' of the object)
                // which is a problem if we want to add new fields.  We could have had a worker object but another way of doing
                // it is create a deferred (lazy region).   The key is that ALL readers know how to skip this region, which allows
                // you to add new fields 'at the end' of the region (just like for sealed objects).  
                DeferedRegion expansion = new DeferedRegion();
                expansion.Write(serializer, delegate ()
                {
                    // I don't need to use Tagged types for my 'first' version of this new region 
                    serializer.Write(m_deferedTypes.Count);
                    for (int i = 0; i < m_deferedTypes.Count; i++)
                    {
                        serializer.Write(m_deferedTypes[i].TypeID);
                        serializer.Write(m_deferedTypes[i].Module);
                        serializer.Write(m_deferedTypes[i].TypeNameSuffix);
                    }

                    // You can place tagged values in here always adding right before the WriteTaggedEnd
                    // for any new fields added after version 1 
                    serializer.WriteTaggedEnd(); // This insures tagged things don't read junk after the region.  
                });
            }
        }

        public void FromStream(Deserializer deserializer)
        {
            deserializer.Read(out m_totalSize);
            RootIndex = (NodeIndex)deserializer.ReadInt();

            // Read in the Types 
            TypeInfo info = new TypeInfo();
            int typeCount = deserializer.ReadInt();
            m_types = new GrowableArray<TypeInfo>(typeCount);
            for (int i = 0; i < typeCount; i++)
            {
                deserializer.Read(out info.Name);
                deserializer.Read(out info.Size);
                deserializer.Read(out info.ModuleName);
                m_types.Add(info);
            }

            // Read in the Nodes 
            int nodeCount = deserializer.ReadInt();
            m_nodes = new SegmentedList<StreamLabel>(SegmentSize, nodeCount);

            for (int i = 0; i < nodeCount; i++)
            {
                m_nodes.Add((StreamLabel)(uint)deserializer.ReadInt());
            }

            // Read in the Blob stream.  
            // TODO be lazy about reading in the blobs.  
            int blobCount = deserializer.ReadInt();
            SegmentedMemoryStreamWriter writer = new SegmentedMemoryStreamWriter(blobCount);
            while (8 <= blobCount)
            {
                writer.Write(deserializer.ReadInt64());
                blobCount -= 8;
            }
            while(0 < blobCount)
            {
                writer.Write(deserializer.ReadByte());
                --blobCount;
            }

            m_reader = writer.GetReader();

            // Stuff added in version 1.   See Version below 
            if (1 <= deserializer.MinimumReaderVersionBeingRead)
            {
                // Because Graph has superclass, you can't add objects to the end of it (since it is not 'the end' of the object)
                // which is a problem if we want to add new fields.  We could have had a worker object but another way of doing
                // it is create a deferred (lazy region).   The key is that ALL readers know how to skip this region, which allows
                // you to add new fields 'at the end' of the region (just like for sealed objects).  
                DeferedRegion expansion = new DeferedRegion();
                expansion.Read(deserializer, delegate ()
                {
                    // I don't need to use Tagged types for my 'first' version of this new region 
                    int count = deserializer.ReadInt();
                    for (int i = 0; i < count; i++)
                    {
                        m_deferedTypes.Add(new DeferedTypeInfo()
                        {
                            TypeID = deserializer.ReadInt(),
                            Module = (Module)deserializer.ReadObject(),
                            TypeNameSuffix = deserializer.ReadString()
                        });
                    }

                    // You can add any tagged objects here after version 1.   You can also use the deserializer.VersionBeingRead
                    // to avoid reading non-existent fields, but the tagging is probably better.   
                });
                expansion.FinishRead(true);  // Immediately read in the fields, preserving the current position in the stream.     
            }
        }

        // These three members control the versioning of the Graph format on disk.   
        public int Version { get { return 1; } }                            // The version of what was written.  It is in the file.       
        public int MinimumVersionCanRead { get { return 0; } }              // Declaration of the oldest format this code can read
        public int MinimumReaderVersion                                     // Will cause readers to fail if their code version is less than this.  
        {
            get
            {
                if (m_deferedTypes.Count != 0)
                {
                    return 1;    // We require that you upgrade to version 1 if you use m_deferedTypes (e.g. projectN)   
                }

                return 0;
            }
        }

        private int m_expectedNodeCount;                // Initial guess at graph Size. 
        private long m_totalSize;                       // Total Size of all the nodes in the graph.  
        internal int m_totalRefs;                       // Total Number of references in the graph
        internal GrowableArray<TypeInfo> m_types;       // We expect only thousands of these
        internal GrowableArray<DeferedTypeInfo> m_deferedTypes; // Types that we only have IDs and module image bases.
        internal SegmentedList<StreamLabel> m_nodes;    // We expect millions of these.  points at a serialize node in m_reader
        internal SegmentedMemoryStreamReader m_reader; // This is the actual data for the nodes.  Can be large
        internal StreamLabel m_undefinedObjDef;         // a node of nodeId 'Unknown'.   New nodes start out pointing to this
        // and then can be set to another nodeId (needed when there are cycles).
        // There should not be any of these left as long as every node referenced
        // by another node has a definition.
        internal SegmentedMemoryStreamWriter m_writer; // Used only during construction to serialize the nodes.
        #endregion
    }

    /// <summary>
    /// Node represents a single node in the code:Graph.  These are created lazily and follow a pattern were the 
    /// CALLER provides the storage for any code:Node or code:NodeType value that are returned.   Thus the caller
    /// is responsible for determine when nodes can be reused to minimize GC cost.  
    /// 
    /// A node implicitly knows where the 'next' child is (that is it is an iterator).  
    /// </summary>
    public class Node
    {
        public int Size
        {
            get
            {
                m_graph.m_reader.Goto(m_graph.m_nodes[(int)m_index]);
                var typeAndSize = ReadCompressedInt(m_graph.m_reader);
                if ((typeAndSize & 1) != 0)     // low bit indicates if Size is encoded explicitly
                {
                    return ReadCompressedInt(m_graph.m_reader);
                }

                // Then it is in the type;
                typeAndSize >>= 1;
                return m_graph.m_types[typeAndSize].Size;
            }
        }
        public bool Defined { get { return m_graph.IsDefined(Index); } }
        public NodeType GetType(NodeType storage)
        {
            return m_graph.GetType(TypeIndex, storage);
        }

        /// <summary>
        /// Reset the internal state so that 'GetNextChildIndex; will return the first child.  
        /// </summary>
        public void ResetChildrenEnumeration()
        {
            m_graph.m_reader.Goto(m_graph.m_nodes[(int)m_index]);
            if ((ReadCompressedInt(m_graph.m_reader) & 1) != 0)        // Skip nodeId and Size
            {
                ReadCompressedInt(m_graph.m_reader);
            }

            m_numChildrenLeft = ReadCompressedInt(m_graph.m_reader);
            Debug.Assert(m_numChildrenLeft < 1660000);     // Not true in general but good enough for unit testing.
            m_current = m_graph.m_reader.Current;
        }

        /// <summary>
        /// Gets the index of the first child of node.  Will return NodeIndex.Invalid if there are no children. 
        /// </summary>
        /// <returns>The index of the child </returns>
        public NodeIndex GetFirstChildIndex()
        {
            ResetChildrenEnumeration();
            return GetNextChildIndex();
        }
        public NodeIndex GetNextChildIndex()
        {
            if (m_numChildrenLeft == 0)
            {
                return NodeIndex.Invalid;
            }

            m_graph.m_reader.Goto(m_current);

            var ret = (NodeIndex)(ReadCompressedInt(m_graph.m_reader) + (int)m_index);
            Debug.Assert((uint)ret < (uint)m_graph.NodeIndexLimit);

            m_current = m_graph.m_reader.Current;
            --m_numChildrenLeft;
            return ret;
        }

        /// <summary>
        /// Returns the number of children this node has.  
        /// </summary>
        public int ChildCount
        {
            get
            {
                m_graph.m_reader.Goto(m_graph.m_nodes[(int)m_index]);
                if ((ReadCompressedInt(m_graph.m_reader) & 1) != 0)        // Skip nodeId and Size
                {
                    ReadCompressedInt(m_graph.m_reader);
                }

                return ReadCompressedInt(m_graph.m_reader);
            }
        }
        public NodeTypeIndex TypeIndex
        {
            get
            {
                m_graph.m_reader.Goto(m_graph.m_nodes[(int)m_index]);
                var ret = (NodeTypeIndex)(ReadCompressedInt(m_graph.m_reader) >> 1);
                return ret;
            }
        }
        public NodeIndex Index { get { return m_index; } }
        public Graph Graph { get { return m_graph; } }
        /// <summary>
        /// Returns true if 'node' is a child of 'this'.  childStorage is simply used as temp space 
        /// as was allocated by Graph.AllocateNodeStorage
        /// </summary>
        public bool Contains(NodeIndex nodeIndex)
        {
            for (NodeIndex childIndex = GetFirstChildIndex(); childIndex != NodeIndex.Invalid; childIndex = GetNextChildIndex())
            {
                if (childIndex == nodeIndex)
                {
                    return true;
                }
            }
            return false;
        }

        public override string ToString()
        {
            StringWriter sw = new StringWriter();
            WriteXml(sw, includeChildren: false);
            return sw.ToString();
        }
        public virtual void WriteXml(TextWriter writer, bool includeChildren = true, string prefix = "", NodeType typeStorage = null, string additinalAttribs = "")
        {
            Debug.Assert(Index != NodeIndex.Invalid);
            if (typeStorage == null)
            {
                typeStorage = m_graph.AllocTypeNodeStorage();
            }

            if (m_graph.m_nodes[(int)Index] == StreamLabel.Invalid)
            {
                writer.WriteLine("{0}<Node Index=\"{1}\" Undefined=\"true\"{2}/>", prefix, (int)Index, additinalAttribs);
                return;
            }

            writer.Write("{0}<Node Index=\"{1}\" TypeIndex=\"{2}\" Size=\"{3}\" Type=\"{4}\" NumChildren=\"{5}\"{6}",
                prefix, (int)Index, TypeIndex, Size, XmlUtilities.XmlEscape(GetType(typeStorage).Name),
                ChildCount, additinalAttribs);
            var childIndex = GetFirstChildIndex();
            if (childIndex != NodeIndex.Invalid)
            {
                writer.WriteLine(">");
                if (includeChildren)
                {
                    writer.Write(prefix);
                    int i = 0;
                    do
                    {
                        writer.Write(" {0}", childIndex);
                        childIndex = GetNextChildIndex();
                        i++;
                        if (i >= 32)
                        {
                            writer.WriteLine();
                            writer.Write(prefix);
                            i = 0;
                        }
                    } while (childIndex != NodeIndex.Invalid);
                }
                else
                {
                    writer.Write(prefix);
                    writer.WriteLine($"<!-- {ChildCount} children omitted... -->");
                }
                writer.WriteLine(" </Node>");
            }
            else
            {
                writer.WriteLine("/>");
            }
        }
        #region private
        protected internal Node(Graph graph)
        {
            m_graph = graph;
            m_index = NodeIndex.Invalid;
        }

        // Node information is stored in a compressed form because we have a lot of them. 
        internal static int ReadCompressedInt(SegmentedMemoryStreamReader reader)
        {
            int ret = 0;
            byte b = reader.ReadByte();
            ret = b << 25 >> 25;
            if ((b & 0x80) == 0)
            {
                return ret;
            }

            ret <<= 7;
            b = reader.ReadByte();
            ret += (b & 0x7f);
            if ((b & 0x80) == 0)
            {
                return ret;
            }

            ret <<= 7;
            b = reader.ReadByte();
            ret += (b & 0x7f);
            if ((b & 0x80) == 0)
            {
                return ret;
            }

            ret <<= 7;
            b = reader.ReadByte();
            ret += (b & 0x7f);
            if ((b & 0x80) == 0)
            {
                return ret;
            }

            ret <<= 7;
            b = reader.ReadByte();
            Debug.Assert((b & 0x80) == 0);
            ret += b;
            return ret;
        }

        internal static void WriteCompressedInt(SegmentedMemoryStreamWriter writer, int value)
        {
            if (value << 25 >> 25 == value)
            {
                goto oneByte;
            }

            if (value << 18 >> 18 == value)
            {
                goto twoBytes;
            }

            if (value << 11 >> 11 == value)
            {
                goto threeBytes;
            }

            if (value << 4 >> 4 == value)
            {
                goto fourBytes;
            }

            writer.Write((byte)((value >> 28) | 0x80));
            fourBytes:
            writer.Write((byte)((value >> 21) | 0x80));
            threeBytes:
            writer.Write((byte)((value >> 14) | 0x80));
            twoBytes:
            writer.Write((byte)((value >> 7) | 0x80));
            oneByte:
            writer.Write((byte)(value & 0x7F));
        }

        internal NodeIndex m_index;
        internal Graph m_graph;
        private StreamLabel m_current;          // My current child in the enumerable.
        private int m_numChildrenLeft;          // count of my children
        #endregion
    }

    /// <summary>
    /// Represents the nodeId of a particular node in the graph.  
    /// </summary>
    public class NodeType
    {
        /// <summary>
        /// Every nodeId has a name, this is it.  
        /// </summary>
        public string Name
        {
            get
            {
                var ret = m_graph.m_types[(int)m_index].Name;
                if (ret == null && (int)m_index < m_graph.m_deferedTypes.Count)
                {
                    var info = m_graph.m_deferedTypes[(int)m_index];
                    if (m_graph.ResolveTypeName != null)
                    {
                        ret = m_graph.ResolveTypeName(info.TypeID, info.Module);
                        if (info.TypeNameSuffix != null)
                        {
                            ret += info.TypeNameSuffix;
                        }

                        m_graph.m_types.UnderlyingArray[(int)m_index].Name = ret;
                    }
                    if (ret == null)
                    {
                        ret = "TypeID(0x" + info.TypeID.ToString("x") + ")";
                    }
                }
                return ret;
            }
        }
        /// <summary>
        /// This is the ModuleName ! Name (or just Name if ModuleName does not exist)  
        /// </summary>
        public string FullName
        {
            get
            {
                var moduleName = ModuleName;
                if (moduleName == null)
                {
                    return Name;
                }

                if (moduleName.Length == 0) // TODO should we have this convention?   
                {
                    moduleName = "?";
                }

                return moduleName + "!" + Name;
            }
        }
        /// <summary>
        /// Size is defined as the Size of the first node in the graph of a given nodeId.   
        /// For types that always have the same Size this is useful, but for types (like arrays or strings)
        /// that have variable Size, it is not useful.  
        /// 
        /// TODO keep track if the nodeId is of variable Size
        /// </summary>
        public int Size { get { return m_graph.m_types[(int)m_index].Size; } }
        public NodeTypeIndex Index { get { return m_index; } }
        public Graph Graph { get { return m_graph; } }
        /// <summary>
        /// The module associated with the type.  Can be null.  Typically this is the full path name.  
        /// </summary>
        public string ModuleName
        {
            get
            {
                var ret = m_graph.m_types[(int)m_index].ModuleName;
                if (ret == null && (int)m_index < m_graph.m_deferedTypes.Count)
                {
                    var module = m_graph.m_deferedTypes[(int)m_index].Module;
                    if (module != null)
                    {
                        ret = module.Path;
                    }
                }
                return ret;
            }
            set
            {
                var typeInfo = m_graph.m_types[(int)m_index];
                typeInfo.ModuleName = value;
                m_graph.m_types[(int)m_index] = typeInfo;
            }
        }
        public Module Module { get { return m_graph.m_deferedTypes[(int)m_index].Module; } }
        public int RawTypeID { get { return m_graph.m_deferedTypes[(int)m_index].TypeID; } }

        public override string ToString()
        {
            StringWriter sw = new StringWriter();
            WriteXml(sw);
            return sw.ToString();
        }
        public void WriteXml(TextWriter writer, string prefix = "")
        {
            writer.WriteLine("{0}<NodeType Index=\"{1}\" Name=\"{2}\"/>", prefix, (int)Index, XmlUtilities.XmlEscape(Name));
        }
        #region private
        protected internal NodeType(Graph graph)
        {
            m_graph = graph;
            m_index = NodeTypeIndex.Invalid;
        }

        internal Graph m_graph;
        internal NodeTypeIndex m_index;
        #endregion
    }

    /// <summary>
    /// Holds all interesting data about a module (in particular enough to look up PDB information)
    /// </summary>
    public class Module : IFastSerializable
    {
        /// <summary>
        /// Create new module.  You must have at least a image base.   Everything else is optional.
        /// </summary>
        public Module(Address imageBase) { ImageBase = imageBase; }

        /// <summary>
        /// The path to the Module (can be null if not known)
        /// </summary>
        public string Path;
        /// <summary>
        /// The location where the image was loaded into memory
        /// </summary>
        public Address ImageBase;
        /// <summary>
        /// The size of the image when loaded in memory
        /// </summary>
        public int Size;
        /// <summary>
        /// The time when this image was built (There is a field in the PE header).   May be MinimumValue if unknonwn. 
        /// </summary>
        public DateTime BuildTime;      // From in the PE header
        /// <summary>
        /// The name of hte PDB file associated with this module.   Ma bye null if unknown
        /// </summary>
        public string PdbName;
        /// <summary>
        /// The GUID that uniquely identfies this PDB for symbol server lookup.  May be Guid.Empty it not known.  
        /// </summary>
        public Guid PdbGuid;            // PDB Guid 
        /// <summary>
        /// The age (version number) that is used for symbol server lookup.  
        /// </summary>T
        public int PdbAge;

        #region private
        /// <summary>
        /// Implementing IFastSerializable interface.  
        /// </summary>
        public void ToStream(Serializer serializer)
        {
            serializer.Write(Path);
            serializer.Write((long)ImageBase);
            serializer.Write(Size);
            serializer.Write(BuildTime.Ticks);
            serializer.Write(PdbName);
            serializer.Write(PdbGuid);
            serializer.Write(PdbAge);
        }
        /// <summary>
        /// Implementing IFastSerializable interface.  
        /// </summary>
        public void FromStream(Deserializer deserializer)
        {
            deserializer.Read(out Path);
            ImageBase = (Address)deserializer.ReadInt64();
            deserializer.Read(out Size);
            BuildTime = new DateTime(deserializer.ReadInt64());
            deserializer.Read(out PdbName);
            deserializer.Read(out PdbGuid);
            deserializer.Read(out PdbAge);
        }
        #endregion
    }

    /// <summary>
    /// Each node is given a unique index (which is dense: an array is a good lookup structure).   
    /// To avoid passing the wrong indexes to methods, we make an enum for each index.   This does
    /// mean you need to cast away this strong typing occasionally (e.g. when you index arrays)
    /// However on the whole it is a good tradeoff.  
    /// </summary>
    public enum NodeIndex { Invalid = -1 }
    /// <summary>
    /// Each node nodeId is given a unique index (which is dense: an array is a good lookup structure).   
    /// To avoid passing the wrong indexes to methods, we make an enum for each index.   This does
    /// mean you need to cast away this strong typing occasionally (e.g. when you index arrays)
    /// However on the whole it is a good tradeoff.  
    /// </summary>    
    public enum NodeTypeIndex { Invalid = -1 }

    /// <summary>
    /// Stuff that is useful but does not need to be in Graph.   
    /// </summary>
    public static class GraphUtils
    {
        /// <summary>
        /// Write the graph as XML to a string and return it (useful for debugging small graphs).  
        /// </summary>
        /// <returns></returns>
        public static string PrintGraph(this Graph graph)
        {
            StringWriter sw = new StringWriter();
            graph.WriteXml(sw);
            return sw.ToString();
        }
        public static string PrintNode(this Graph graph, NodeIndex nodeIndex)
        {
            return graph.GetNode(nodeIndex, graph.AllocNodeStorage()).ToString();
        }
        public static string PrintNode(this Graph graph, int nodeIndex)
        {
            return graph.PrintNode((NodeIndex)nodeIndex);
        }
        public static string PrintNodes(this Graph graph, List<NodeIndex> nodes)
        {
            var sw = new StringWriter();
            sw.WriteLine("<NodeList>");
            var node = graph.AllocNodeStorage();
            var type1 = graph.AllocTypeNodeStorage();

            foreach (var nodeIndex in nodes)
            {
                node = graph.GetNode(nodeIndex, node);
                node.WriteXml(sw, prefix: "  ", typeStorage: type1);
            }
            sw.WriteLine("<NodeList>");
            return sw.ToString();
        }
        public static string PrintChildren(this Graph graph, NodeIndex nodeIndex)
        {
            return graph.PrintNodes(graph.NodeChildren(nodeIndex));
        }
        public static string PrintChildren(this Graph graph, int nodeIndex)
        {
            return graph.PrintChildren((NodeIndex)nodeIndex);
        }
        // Debuggging. 
        /// <summary>
        /// Writes the graph as XML to 'writer'.  Don't use on big graphs.  
        /// </summary>
        public static void WriteXml(this Graph graph, TextWriter writer)
        {
            writer.WriteLine("<MemoryGraph NumNodes=\"{0}\" NumTypes=\"{1}\" TotalSize=\"{2}\" SizeOfGraphDescription=\"{3}\">",
                graph.NodeIndexLimit, graph.NodeTypeIndexLimit, graph.TotalSize, graph.SizeOfGraphDescription());
            writer.WriteLine(" <RootIndex>{0}</RootIndex>", graph.RootIndex);
            writer.WriteLine(" <NodeTypes Count=\"{0}\">", graph.NodeTypeIndexLimit);
            var typeStorage = graph.AllocTypeNodeStorage();
            for (NodeTypeIndex typeIndex = 0; typeIndex < graph.NodeTypeIndexLimit; typeIndex++)
            {
                var type = graph.GetType(typeIndex, typeStorage);
                type.WriteXml(writer, "  ");
            }
            writer.WriteLine(" </NodeTypes>");

            writer.WriteLine(" <Nodes Count=\"{0}\">", graph.NodeIndexLimit);
            var nodeStorage = graph.AllocNodeStorage();
            for (NodeIndex nodeIndex = 0; nodeIndex < graph.NodeIndexLimit; nodeIndex++)
            {
                var node = graph.GetNode(nodeIndex, nodeStorage);
                node.WriteXml(writer, prefix: "  ");
            }
            writer.WriteLine(" </Nodes>");
            writer.WriteLine("</MemoryGraph>");
        }
        public static void DumpNormalized(this MemoryGraph graph, TextWriter writer)
        {
            MemoryNode nodeStorage = (MemoryNode)graph.AllocNodeStorage();
            NodeType typeStorage = graph.AllocTypeNodeStorage();
            Node node;

#if false 
            // Compute reachability info
            bool[] reachable = new bool[(int)graph.NodeIndexLimit];
            Queue<NodeIndex> workQueue = new Queue<NodeIndex>();
            workQueue.Enqueue(graph.RootIndex);
            while (workQueue.Count > 0)
            {
                var nodeIdx = workQueue.Dequeue();
                if (!reachable[(int)nodeIdx])
                {
                    reachable[(int)nodeIdx] = true;
                    node = graph.GetNode(nodeIdx, nodeStorage);
                    for (var childIndex = node.GetFirstChildIndex(); childIndex != NodeIndex.Invalid; childIndex = node.GetNextChildIndex())
                        workQueue.Enqueue(childIndex);
                }
            }

            // Get Reachability count. 
            int reachableCount = 0;
            for (int i = 0; i < reachable.Length; i++)
                if (reachable[i])
                    reachableCount++;
#endif

            // Sort the nodes by virtual address 
            NodeIndex[] sortedNodes = new NodeIndex[(int)graph.NodeIndexLimit];
            for (int i = 0; i < sortedNodes.Length; i++)
            {
                sortedNodes[i] = (NodeIndex)i;
            }

            Array.Sort<NodeIndex>(sortedNodes, delegate (NodeIndex x, NodeIndex y)
            {
                // Sort first by address
                int ret = graph.GetAddress(x).CompareTo(graph.GetAddress(y));
                if (ret != 0)
                {
                    return ret;
                }
                // Then by name
                return graph.GetNode(x, nodeStorage).GetType(typeStorage).Name.CompareTo(graph.GetNode(y, nodeStorage).GetType(typeStorage).Name);
            });

            node = graph.GetNode(graph.RootIndex, nodeStorage);
            writer.WriteLine("<GraphDump RootNode=\"{0}\" NumNodes=\"{1}\" NumTypes=\"{2}\" TotalSize=\"{3}\" SizeOfGraphDescription=\"{4}\">",
                XmlUtilities.XmlEscape(node.GetType(typeStorage).Name),
                graph.NodeIndexLimit,
                graph.NodeTypeIndexLimit,
                graph.TotalSize,
                graph.SizeOfGraphDescription());
            writer.WriteLine(" <Nodes Count=\"{0}\">", graph.NodeIndexLimit);

            SortedDictionary<ulong, bool> roots = new SortedDictionary<ulong, bool>();
            foreach (NodeIndex nodeIdx in sortedNodes)
            {
                // if (!reachable[(int)nodeIdx]) continue;

                node = graph.GetNode(nodeIdx, nodeStorage);
                string name = node.GetType(typeStorage).Name;

                writer.Write("  <Node Address=\"{0:x}\" Size=\"{1}\" Type=\"{2}\"> ", graph.GetAddress(nodeIdx), node.Size, XmlUtilities.XmlEscape(name));
                bool isRoot = graph.GetAddress(node.Index) == 0;
                int childCnt = 0;
                for (var childIndex = node.GetFirstChildIndex(); childIndex != NodeIndex.Invalid; childIndex = node.GetNextChildIndex())
                {
                    if (isRoot)
                    {
                        roots[graph.GetAddress(childIndex)] = true;
                    }

                    childCnt++;
                    if (childCnt % 8 == 0)
                    {
                        writer.WriteLine();
                        writer.Write("    ");
                    }
                    writer.Write("{0:x} ", graph.GetAddress(childIndex));
                }
                writer.WriteLine(" </Node>");
            }
            writer.WriteLine(" <Roots>");
            foreach (ulong root in roots.Keys)
            {
                writer.WriteLine("  {0:x}", root);
            }
            writer.WriteLine(" </Roots>");
            writer.WriteLine(" </Nodes>");
            writer.WriteLine("</GraphDump>");
        }

        public static List<NodeIndex> NodeChildren(this Graph graph, NodeIndex nodeIndex)
        {
            var node = graph.GetNode(nodeIndex, graph.AllocNodeStorage());
            var ret = new List<NodeIndex>();
            for (var childIndex = node.GetFirstChildIndex(); childIndex != NodeIndex.Invalid; childIndex = node.GetNextChildIndex())
            {
                ret.Add(childIndex);
            }

            return ret;
        }
        public static List<NodeIndex> NodesOfType(this Graph graph, string regExpression)
        {
            var typeSet = new Dictionary<NodeTypeIndex, NodeTypeIndex>();
            var type = graph.AllocTypeNodeStorage();
            for (NodeTypeIndex typeId = 0; typeId < graph.NodeTypeIndexLimit; typeId = typeId + 1)
            {
                type = graph.GetType(typeId, type);
                if (Regex.IsMatch(type.Name, regExpression))
                {
                    typeSet.Add(typeId, typeId);
                }
            }

            var ret = new List<NodeIndex>();
            var node = graph.AllocNodeStorage();
            for (NodeIndex nodeId = 0; nodeId < graph.NodeIndexLimit; nodeId = nodeId + 1)
            {
                node = graph.GetNode(nodeId, node);
                if (typeSet.ContainsKey(node.TypeIndex))
                {
                    ret.Add(nodeId);
                }
            }
            return ret;
        }
    }
}

/// <summary>
/// A RefGraph is derived graph where each node's children are the set of nodes in the original graph 
/// which refer that node (that is A -> B then in refGraph B -> A).   
/// 
/// The NodeIndexes in the refGraph match the NodeIndexes in the original graph.  Thus after creating
/// a refGraph it is easy to answer the question 'who points at me' of the original graph.  
/// 
/// When create the RefGraph the whole reference graph is generated on the spot (thus it must traverse
/// the whole of the orignal graph) and the size of the resulting RefGraph is  about the same size as the  
/// original graph. 
/// 
/// Thus this is a fairly expensive thing to create.  
/// </summary>
public class RefGraph
{
    public RefGraph(Graph graph)
    {
        m_refsForNodes = new NodeListIndex[(int)graph.NodeIndexLimit];
        // We guess that we need about 1.5X as many slots as there are nodes.   This seems a concervative estimate. 
        m_links = new GrowableArray<RefElem>((int)graph.NodeIndexLimit * 3 / 2);

        var nodeStorage = graph.AllocNodeStorage();
        for (NodeIndex nodeIndex = 0; nodeIndex < graph.NodeIndexLimit; nodeIndex++)
        {
            var node = graph.GetNode(nodeIndex, nodeStorage);
            for (var childIndex = node.GetFirstChildIndex(); childIndex != NodeIndex.Invalid; childIndex = node.GetNextChildIndex())
            {
                AddRefsTo(childIndex, nodeIndex);
            }
        }

        // Sadly, this check is too expensive even for DEBUG 
#if false 
        CheckConsistancy(graph);
#endif
    }
    /// <summary>
    /// Allocates nodes to be used as storage for methods like code:GetNode, code:RefNode.GetFirstChild and code:RefNode.GetNextChild
    /// </summary>
    public RefNode AllocNodeStorage() { return new RefNode(this); }

    /// <summary>
    /// Given an arbitrary code:NodeIndex that identifies the node, Get a code:Node object.  
    /// 
    /// This routine does not allocated but uses the space passed in by 'storage.  
    /// 'storage' should be allocated with coode:AllocNodeStorage, and should be aggressively reused.  
    /// </summary>
    public RefNode GetNode(NodeIndex nodeIndex, RefNode storage)
    {
        Debug.Assert(storage.m_graph == this);
        storage.m_index = nodeIndex;
        return storage;
    }

    /// <summary>
    /// This is for debugging 
    /// </summary>
    /// <param name="nodeIndex"></param>
    /// <returns></returns>
    public RefNode GetNode(NodeIndex nodeIndex)
    {
        return GetNode(nodeIndex, AllocNodeStorage());
    }

    #region private
#if DEBUG
    private void CheckConsitancy(Graph graph)
    {
        // This double check is pretty expensive for large graphs (nodes that have large fan-in or fan-out).  
        var nodeStorage = graph.AllocNodeStorage();
        var refStorage = AllocNodeStorage();
        for (NodeIndex nodeIdx = 0; nodeIdx < graph.NodeIndexLimit; nodeIdx++)
        {
            // If Node -> Ref then the RefGraph has a pointer from Ref -> Node 
            var node = graph.GetNode(nodeIdx, nodeStorage);
            for (var childIndex = node.GetFirstChildIndex(); childIndex != NodeIndex.Invalid; childIndex = node.GetNextChildIndex())
            {
                var refsForChild = GetNode(childIndex, refStorage);
                if (!refsForChild.Contains(nodeIdx))
                {
                    var nodeStr = node.ToString();
                    var refStr = refsForChild.ToString();
                    Debug.Assert(false);
                }
            }

            // If the refs graph has a pointer from Ref -> Node then the original graph has a arc from Node ->Ref
            var refNode = GetNode(nodeIdx, refStorage);
            for (var childIndex = refNode.GetFirstChildIndex(); childIndex != NodeIndex.Invalid; childIndex = refNode.GetNextChildIndex())
            {
                var nodeForChild = graph.GetNode(childIndex, nodeStorage);
                if (!nodeForChild.Contains(nodeIdx))
                {
                    var nodeStr = nodeForChild.ToString();
                    var refStr = refNode.ToString();
                    Debug.Assert(false);
                }
            }
        }
    }
#endif

    /// <summary>
    /// Add the fact that 'refSource' refers to refTarget.
    /// </summary>
    private void AddRefsTo(NodeIndex refTarget, NodeIndex refSource)
    {
        NodeListIndex refsToList = m_refsForNodes[(int)refTarget];

        // We represent singles as the childIndex itself.  This is a very common case, so it is good that it is efficient. 
        if (refsToList == NodeListIndex.Empty)
        {
            m_refsForNodes[(int)refTarget] = (NodeListIndex)(refSource + 1);
        }
        else if (refsToList > 0)        // One element list
        {
            var existingChild = (NodeIndex)(refsToList - 1);
            m_refsForNodes[(int)refTarget] = (NodeListIndex)(-AddLink(refSource, AddLink(existingChild)) - 1);
        }
        else // refsToList < 0          more than one element.  
        {
            var listIndex = -(int)refsToList - 1;
            m_refsForNodes[(int)refTarget] = (NodeListIndex)(-AddLink(refSource, listIndex) - 1);
        }
    }

    /// <summary>
    /// A helper function for AddRefsTo.  Allocates a new cell from m_links and initializes its two fields 
    /// (the child index field and 'rest' field), and returns the index (pointer) to the new cell.  
    /// </summary>
    private int AddLink(NodeIndex refIdx, int nextIdx = -1)
    {
        var ret = m_links.Count;
        m_links.Add(new RefElem(refIdx, nextIdx));
        return ret;
    }

    /// <summary>
    ///  Logically a NodeListIndex represents a list of node indexes.   However it is heavily optimized
    ///  to avoid overhead.   0 means empty, a positive number is the NodeIndex+1 and a negative number 
    ///  is index in m_links - 1;.  
    /// </summary>
    internal enum NodeListIndex { Empty = 0 };

    /// <summary>
    /// RefElem is a linked list cell that is used to store lists of childrens that are larger than 1.
    /// </summary>
    internal struct RefElem
    {
        public RefElem(NodeIndex refIdx, int nextIdx) { RefIdx = refIdx; NextIdx = nextIdx; }
        public NodeIndex RefIdx;           // The reference
        public int NextIdx;                // The index to the next element in  m_links.   a negative number when done. 
    }

    /// <summary>
    /// m_refsForNodes maps the NodeIndexs of the reference graph to the children information.   However unlike
    /// a normal Graph RefGraph needs to support incremental addition of children.  Thus we can't use the normal
    /// compression (which assumed you know all the children when you define the node).  
    /// 
    /// m_refsForNodes points at a NodeListIndex which is a compressed list that is tuned for the case where
    /// a node has exactly one child (a very common case).   If that is not true we 'overflow' into a 'linked list'
    /// of RefElems that is stored in m_links.   See NodeListIndex for more on the exact encoding.   
    /// 
    /// </summary>
    internal NodeListIndex[] m_refsForNodes;

    /// <summary>
    /// If the number of children for a node is > 1 then we need to store the data somewhere.  m_links is array
    /// of linked list cells that hold the overflow case.  
    /// </summary>
    internal GrowableArray<RefElem> m_links;      // The rest of the list.  
    #endregion
}

public class RefNode
{
    /// <summary>
    /// Gets the first child for the node.  Will return null if there are no children.  
    /// </summary>
    public NodeIndex GetFirstChildIndex()
    {
        var refsToList = m_graph.m_refsForNodes[(int)m_index];

        if (refsToList == RefGraph.NodeListIndex.Empty)
        {
            return NodeIndex.Invalid;
        }

        if (refsToList > 0)        // One element list
        {
            m_cur = -1;
            return (NodeIndex)(refsToList - 1);
        }
        else // refsToList < 0          more than one element.  
        {
            var listIndex = -(int)refsToList - 1;
            var refElem = m_graph.m_links[listIndex];
            m_cur = refElem.NextIdx;
            return refElem.RefIdx;
        }
    }
    /// <summary>
    /// Returns the next child for the node.   Will return NodeIndex.Invalid if there are no more children 
    /// </summary>
    public NodeIndex GetNextChildIndex()
    {
        if (m_cur < 0)
        {
            return NodeIndex.Invalid;
        }

        var refElem = m_graph.m_links[m_cur];
        m_cur = refElem.NextIdx;
        return refElem.RefIdx;
    }

    /// <summary>
    /// Returns the count of children (nodes that reference this node). 
    /// </summary>
    public int ChildCount
    {
        get
        {
            var ret = 0;
            for (NodeIndex childIndex = GetFirstChildIndex(); childIndex != NodeIndex.Invalid; childIndex = GetNextChildIndex())
            {
                ret++;
            }

            return ret;
        }
    }

    public RefGraph Graph { get { return m_graph; } }
    public NodeIndex Index { get { return m_index; } }

    /// <summary>
    /// Returns true if 'node' is a child of 'this'.  childStorage is simply used as temp space 
    /// as was allocated by RefGraph.AllocateNodeStorage
    /// </summary>
    public bool Contains(NodeIndex node)
    {
        for (NodeIndex childIndex = GetFirstChildIndex(); childIndex != NodeIndex.Invalid; childIndex = GetNextChildIndex())
        {
            if (childIndex == node)
            {
                return true;
            }
        }
        return false;
    }

    public override string ToString()
    {
        StringWriter sw = new StringWriter();
        WriteXml(sw);
        return sw.ToString();
    }
    public void WriteXml(TextWriter writer, string prefix = "")
    {
        Debug.Assert(Index != NodeIndex.Invalid);


        writer.Write("{0}<Node Index=\"{1}\" NumChildren=\"{2}\"", prefix, (int)Index, ChildCount);
        var childIndex = GetFirstChildIndex();
        if (childIndex != NodeIndex.Invalid)
        {
            writer.WriteLine(">");
            writer.Write(prefix);
            int i = 0;
            do
            {
                writer.Write(" {0}", childIndex);
                childIndex = GetNextChildIndex();
                i++;
                if (i >= 32)
                {
                    writer.WriteLine();
                    writer.Write(prefix);
                    i = 0;
                }
            } while (childIndex != NodeIndex.Invalid);
            writer.WriteLine(" </Node>");
        }
        else
        {
            writer.WriteLine("/>");
        }
    }

    #region private
    internal RefNode(RefGraph refGraph)
    {
        m_graph = refGraph;
    }

    internal RefGraph m_graph;
    internal NodeIndex m_index;     // My index.  
    internal int m_cur;             // A pointer to where we are in the list of elements (index into m_links)
    #endregion
}

/// <summary>
/// code:MemorySampleSource hooks up a Memory graph to become a Sample source.  Currently we do
/// a breadth-first traversal to form a spanning tree, and then create samples for each node
/// where the 'stack' is the path to the root of this spanning tree.
/// 
/// This is just a first cut...
/// </summary>
public class SpanningTree
{
    public SpanningTree(Graph graph, TextWriter log)
    {
        m_graph = graph;
        m_log = log;
        m_nodeStorage = graph.AllocNodeStorage();
        m_childStorage = graph.AllocNodeStorage();
        m_typeStorage = graph.AllocTypeNodeStorage();

        // We need to reduce the graph to a tree.   Each node is assigned a unique 'parent' which is its 
        // parent in a spanning tree of the graph.  
        // The +1 is for orphan node support.  
        m_parent = new NodeIndex[(int)graph.NodeIndexLimit + 1];
    }
    public Graph Graph { get { return m_graph; } }

    /// <summary>
    /// Every type is given a priority of 0 unless the type name matches one of 
    /// the patterns in PriorityRegExs.  If it does that type is assigned that priority.
    /// 
    /// A node's priority is defined to be the priority of the type of the node
    /// (as given by PriorityRegExs), plus 1/10 the priority of its parent.  
    /// 
    /// Thus priorities 'decay' by 1/10 through pointers IF the prioirty of the node's
    /// type is 0 (the default).   
    ///
    /// By default the framework has a priority of -1 which means that you explore all
    /// high priority and user defined types before any framework type.
    /// 
    /// Types with the same priority are enumerate breath-first.  
    /// </summary>
    public string PriorityRegExs
    {
        get
        {
            if (m_priorityRegExs == null)
            {
                PriorityRegExs = DefaultPriorities;
            }

            return m_priorityRegExs;
        }
        set
        {
            m_priorityRegExs = value;
            SetTypePriorities(value);
        }
    }
    public static string DefaultPriorities
    {
        get
        {
            return
                // By types (including user defined types) are 0
                @"v4.0.30319\%!->-1;" +     // Framework is less than default
                @"v2.0.50727\%!->-1;" +     // Framework is less than default
                @"[*local vars]->-1000;" +  // Local variables are not that interesting, since they tend to be transient
                @"mscorlib!Runtime.CompilerServices.ConditionalWeakTable->-10000;" + // We prefer not to use Conditional weak table references even more. 
                @"[COM/WinRT Objects]->-1000000;" + // We prefer to Not use the CCW roots. 
                @"[*handles]->-2000000;" +
                @"[other roots]->-2000000";
        }
    }

    public NodeIndex Parent(NodeIndex node) { return m_parent[(int)node]; }

    public void ForEach(Action<NodeIndex> callback)
    {
        // Initialize the priority 
        if (m_typePriorities == null)
        {
            PriorityRegExs = DefaultPriorities;
        }

        Debug.Assert(m_typePriorities != null);

        // Initialize the breadth-first work queue.
        var nodesToVisit = new PriorityQueue(1024);
        nodesToVisit.Enqueue(m_graph.RootIndex, 0.0F);

        // reset the visited information.
        for (int i = 0; i < m_parent.Length; i++)
        {
            m_parent[i] = NodeIndex.Invalid;
        }

        float[] nodePriorities = new float[m_parent.Length];
        bool scanedForOrphans = false;
        var epsilon = 1E-7F;            // Something that is big enough not to bet lost in roundoff error.  
        float order = 0;
        for (int i = 0; ; i++)
        {
            if ((i & 0x1FFF) == 0)  // Every 8K
            {
                System.Threading.Thread.Sleep(0);       // Allow interruption.  
            }

            NodeIndex nodeIndex;
            float nodePriority;
            if (nodesToVisit.Count == 0)
            {
                nodePriority = 0;
                if (!scanedForOrphans)
                {
                    scanedForOrphans = true;
                    AddOrphansToQueue(nodesToVisit);
                }
                if (nodesToVisit.Count == 0)
                {
                    return;
                }
            }
            nodeIndex = nodesToVisit.Dequeue(out nodePriority);

            // Insert any children that have not already been visited (had a parent assigned) into the work queue). 
            var node = m_graph.GetNode(nodeIndex, m_nodeStorage);
            var parentPriority = nodePriorities[(int)node.Index];
            for (var childIndex = node.GetFirstChildIndex(); childIndex != NodeIndex.Invalid; childIndex = node.GetNextChildIndex())
            {
                if (m_parent[(int)childIndex] == NodeIndex.Invalid)
                {
                    m_parent[(int)childIndex] = nodeIndex;

                    // the priority of the child is determined by its type and 1/10 by its parent.  
                    var child = m_graph.GetNode(childIndex, m_childStorage);
                    var childPriority = m_typePriorities[(int)child.TypeIndex] + parentPriority / 10;
                    nodePriorities[(int)childIndex] = childPriority;

                    // Subtract a small increasing value to keep the queue in order if the priorities are the same. 
                    // This is a bit of a hack since it can get big and purtub the user-defined order.  
                    order += epsilon;
                    nodesToVisit.Enqueue(childIndex, childPriority - order);
                }
            }

            // Return the node.  
            callback?.Invoke(node.Index);
        }
    }

    #region private
    /// <summary>
    /// Add any unreachable nodes to the 'nodesToVisit'.   Note that we do this in a 'smart' way
    /// where we only add orphans that are not reachable from other orphans.   That way we get a 
    /// minimal set of orphan 'roots'.  
    /// </summary>
    /// <param name="nodesToVisit"></param>
    private void AddOrphansToQueue(PriorityQueue nodesToVisit)
    {

        for (int i = 0; i < (int)m_graph.NodeIndexLimit; i++)
        {
            if (m_parent[i] == NodeIndex.Invalid)
            {
                MarkDecendentsIgnoringCycles((NodeIndex)i, 0);
            }
        }

        // Collect up all the nodes that are not reachable from other nodes as the roots of the
        // orphans.  Also reset orphanVisitedMarker back to NodeIndex.Invalid.
        for (int i = 0; i < (int)m_graph.NodeIndexLimit; i++)
        {
            var nodeIndex = (NodeIndex)i;
            var parent = m_parent[(int)nodeIndex];
            if (parent <= NodeIndex.Invalid)
            {
                if (parent == NodeIndex.Invalid)
                {
                    // Thr root index has no parent but is reachable from the root. 
                    if (nodeIndex != m_graph.RootIndex)
                    {
                        var node = m_graph.GetNode(nodeIndex, m_nodeStorage);
                        var priority = m_typePriorities[(int)node.TypeIndex];
                        nodesToVisit.Enqueue(nodeIndex, priority);
                        m_parent[(int)nodeIndex] = m_graph.NodeIndexLimit;               // This is the 'not reachable' parent. 
                    }
                }
                else
                {
                    m_parent[(int)nodeIndex] = NodeIndex.Invalid;
                }
            }
        }
    }

    /// <summary>
    /// A helper for AddOrphansToQueue, so we only add orphans that are not reachable from other orphans.  
    /// 
    /// Mark all descendants (but not nodeIndex itself) as being visited.    Any arcs that form
    /// cycles are ignored, so nodeIndex is guaranteed to NOT be marked.     
    /// </summary>
    private void MarkDecendentsIgnoringCycles(NodeIndex nodeIndex, int recursionCount)
    {
        // TODO We give up if the chains are larger than 10K long (because we stack overflow otherwise)
        // We could have an explicit stack and avoid this...
        if (recursionCount > 10000)
        {
            return;
        }

        Debug.Assert(m_parent[(int)nodeIndex] == NodeIndex.Invalid);

        // This marks that there is a path from another ophan to this one (thus it is not a good root)
        NodeIndex orphanVisitedMarker = NodeIndex.Invalid - 1;

        // To detect cycles we mark all nodes we not commmited to (we are visiting, rather than visited)
        // If we detect this mark we understand it is a loop and ignore the arc.  
        NodeIndex orphanVisitingMarker = NodeIndex.Invalid - 2;
        m_parent[(int)nodeIndex] = orphanVisitingMarker;        // We are now visitING

        // Mark all nodes as being visited.  
        var node = m_graph.GetNode(nodeIndex, AllocNodeStorage());
        for (var childIndex = node.GetFirstChildIndex(); childIndex != NodeIndex.Invalid; childIndex = node.GetNextChildIndex())
        {
            // Has this child not been seen at all?  If so mark it.  
            // Skip it if we are visiting (it would form a cycle) or visited (or not an orphan)
            if (m_parent[(int)childIndex] == NodeIndex.Invalid)
            {
                MarkDecendentsIgnoringCycles(childIndex, recursionCount + 1);
                m_parent[(int)childIndex] = orphanVisitedMarker;
            }
        }
        FreeNodeStorage(node);

        // We set this above, and should not have changed it.  
        Debug.Assert(m_parent[(int)nodeIndex] == orphanVisitingMarker);
        // Now that we are finished, we reset the visiting bit.  
        m_parent[(int)nodeIndex] = NodeIndex.Invalid;
    }

    /// <summary>
    /// Gives back nodes that are no longer in use.  This is a memory optimization. 
    /// </summary>
    private void FreeNodeStorage(Node node)
    {
        m_cachedNodeStorage = node;
    }
    /// <summary>
    /// Gets a node that can be written on.  It is a simple cache
    /// </summary>
    /// <returns></returns>
    private Node AllocNodeStorage()
    {
        var ret = m_cachedNodeStorage;                // See if we have a free node. 
        if (ret == null)
        {
            ret = m_graph.AllocNodeStorage();
        }
        else
        {
            m_cachedNodeStorage = null;               // mark that that node is in use.  
        }

        return ret;
    }

    /// <summary>
    /// Convert a string from my regular expression format (where you only have * and {  } as grouping operators
    /// and convert them to .NET regular expressions string
    /// TODO FIX NOW cloned code (also in FilterStackSource)
    /// </summary>
    internal static string ToDotNetRegEx(string str)
    {
        // A leading @ sign means the rest is a .NET regular expression.  (Undocumented, not really needed yet.)
        if (str.StartsWith("@"))
        {
            return str.Substring(1);
        }

        str = Regex.Escape(str);                // Assume everything is ordinary
        str = str.Replace(@"%", @"[.\w\d?]*");  // % means any number of alpha-numeric chars. 
        str = str.Replace(@"\*", @".*");        // * means any number of any characters.  
        str = str.Replace(@"\^", @"^");         // ^ means anchor at the begining.  
        str = str.Replace(@"\|", @"|");         // | means is the or operator  
        str = str.Replace(@"\{", "(");
        str = str.Replace("}", ")");
        return str;
    }

    private void SetTypePriorities(string priorityPats)
    {
        if (m_typePriorities == null)
        {
            m_typePriorities = new float[(int)m_graph.NodeTypeIndexLimit];
        }

        string[] priorityPatArray = priorityPats.Split(';');
        Regex[] priorityRegExArray = new Regex[priorityPatArray.Length];
        float[] priorityArray = new float[priorityPatArray.Length];
        for (int i = 0; i < priorityPatArray.Length; i++)
        {
            var m = Regex.Match(priorityPatArray[i], @"(.*)->(-?\d+.?\d*)");
            if (!m.Success)
            {
                if (StringUtilities.IsNullOrWhiteSpace(priorityPatArray[i]))
                {
                    continue;
                }

                throw new ApplicationException("Priority pattern " + priorityPatArray[i] + " is not of the form Pat->Num.");
            }

            var dotNetRegEx = ToDotNetRegEx(m.Groups[1].Value.Trim());
            priorityRegExArray[i] = new Regex(dotNetRegEx, RegexOptions.IgnoreCase);
            priorityArray[i] = float.Parse(m.Groups[2].Value);
        }

        // Assign every type index a priority in m_typePriorities based on if they match a pattern.  
        NodeType typeStorage = m_graph.AllocTypeNodeStorage();
        for (NodeTypeIndex typeIdx = 0; typeIdx < m_graph.NodeTypeIndexLimit; typeIdx++)
        {
            var type = m_graph.GetType(typeIdx, typeStorage);

            var fullName = type.FullName;
            for (int regExIdx = 0; regExIdx < priorityRegExArray.Length; regExIdx++)
            {
                var priorityRegEx = priorityRegExArray[regExIdx];
                if (priorityRegEx == null)
                {
                    continue;
                }

                var m = priorityRegEx.Match(fullName);
                if (m.Success)
                {
                    m_typePriorities[(int)typeIdx] = priorityArray[regExIdx];
                    // m_log.WriteLine("Type {0} assigned priority {1:f3}", fullName, priorityArray[regExIdx]);
                    break;
                }
            }
        }
    }

    private Graph m_graph;
    private NodeIndex[] m_parent;               // We keep track of the parents of each node in our breadth-first scan. 

    // We give each type a priority (using the m_priority Regular expressions) which guide the breadth-first scan. 
    private string m_priorityRegExs;
    private float[] m_typePriorities;
    private NodeType m_typeStorage;
    private Node m_nodeStorage;                 // Only for things that can't be reentrant
    private Node m_childStorage;
    private Node m_cachedNodeStorage;           // Used when it could be reentrant
    private TextWriter m_log;                   // processing messages 
    #endregion
}

/// <summary>
/// TODO FIX NOW put in its own file.  
/// A priority queue, specialized to be a bit more efficient than a generic version would be. 
/// </summary>
internal class PriorityQueue
{
    public PriorityQueue(int initialSize = 32)
    {
        m_heap = new DataItem[initialSize];
    }
    public int Count { get { return m_count; } }
    public void Enqueue(NodeIndex item, float priority)
    {
        var idx = m_count;
        if (idx >= m_heap.Length)
        {
            var newArray = new DataItem[m_heap.Length * 3 / 2 + 8];
            Array.Copy(m_heap, newArray, m_heap.Length);
            m_heap = newArray;
        }
        m_heap[idx].value = item;
        m_heap[idx].priority = priority;
        m_count = idx + 1;
        for (; ; )
        {
            var parent = idx / 2;
            if (m_heap[parent].priority >= m_heap[idx].priority)
            {
                break;
            }

            // swap parent and idx
            var temp = m_heap[idx];
            m_heap[idx] = m_heap[parent];
            m_heap[parent] = temp;

            if (parent == 0)
            {
                break;
            }

            idx = parent;
        }
        // CheckInvariant();
    }
    public NodeIndex Dequeue(out float priority)
    {
        Debug.Assert(Count > 0);

        var ret = m_heap[0].value;
        priority = m_heap[0].priority;
        --m_count;
        m_heap[0] = m_heap[m_count];
        var idx = 0;
        for (; ; )
        {
            var childIdx = idx * 2;
            var largestIdx = idx;
            if (childIdx < Count && m_heap[childIdx].priority > m_heap[largestIdx].priority)
            {
                largestIdx = childIdx;
            }

            childIdx++;
            if (childIdx < Count && m_heap[childIdx].priority > m_heap[largestIdx].priority)
            {
                largestIdx = childIdx;
            }

            if (largestIdx == idx)
            {
                break;
            }

            // swap idx and smallestIdx
            var temp = m_heap[idx];
            m_heap[idx] = m_heap[largestIdx];
            m_heap[largestIdx] = temp;

            idx = largestIdx;
        }
        // CheckInvariant();
        return ret;
    }

    #region private
#if DEBUG
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<PriorityQueue Count=\"").Append(m_count).Append("\">").AppendLine();

        // Sort the items in descending order 
        var items = new List<DataItem>(m_count);
        for (int i = 0; i < m_count; i++)
            items.Add(m_heap[i]);
        items.Sort((x, y) => y.priority.CompareTo(x.priority));
        if (items.Count > 0)
            Debug.Assert(items[0].value == m_heap[0].value);

        foreach (var item in items)
            sb.Append("{").Append((int)item.value).Append(", ").Append(item.priority.ToString("f1")).Append("}").AppendLine();
        sb.AppendLine("</PriorityQueue>");
        return sb.ToString();
    }
#endif

    private struct DataItem
    {
        public DataItem(NodeIndex value, float priority) { this.value = value; this.priority = priority; }
        public float priority;
        public NodeIndex value;
    }
    [Conditional("DEBUG")]
    private void CheckInvariant()
    {
        for (int idx = 1; idx < Count; idx++)
        {
            var parentIdx = idx / 2;
            Debug.Assert(m_heap[parentIdx].priority >= m_heap[idx].priority);
        }
    }

    // In this array form a tree where each child of i is at 2i and 2i+1.   Each child is 
    // less than or equal to its parent.  
    private DataItem[] m_heap;
    private int m_count;
    #endregion
}

/// <summary>
/// This class is responsible for taking a graph and generating a smaller graph that
/// is a reasonable proxy.   In particular
///     
///     1) A spanning tree is formed, and if a node is selected so are all its 
///        parents in that spanning tree.
///        
///     2) We try hard to keep scale each object type by the count by which the whole
///        graph was reduced.  
/// </summary>
public class GraphSampler
{
    /// <summary>
    /// 
    /// </summary>
    public GraphSampler(MemoryGraph graph, int targetNodeCount, TextWriter log)
    {
        m_graph = graph;
        m_log = log;
        m_targetNodeCount = targetNodeCount;
        m_filteringRatio = (float)graph.NodeCount / targetNodeCount;
        m_nodeStorage = m_graph.AllocNodeStorage();
        m_childNodeStorage = m_graph.AllocNodeStorage();
        m_nodeTypeStorage = m_graph.AllocTypeNodeStorage();
    }

    /// <summary>
    /// Creates a new graph from 'graph' which has the same type statistics as the original
    /// graph but keeps the node count roughly at 'targetNodeCount'
    /// </summary>
    public MemoryGraph GetSampledGraph()
    {
        m_log.WriteLine("************* SAMPLING GRAPH TO REDUCE SIZE ***************");
        m_log.WriteLine("Original graph object count {0:n0}, targetObjectCount {1:n0} targetRatio {2:f2}", m_graph.NodeCount, m_targetNodeCount, m_filteringRatio);
        m_log.WriteLine("Original graph Size MB {0:n0} TypeCount {1:n0}", m_graph.TotalSize, m_graph.NodeTypeCount);

        // Get the spanning tree
        m_spanningTree = new SpanningTree(m_graph, m_log);
        m_spanningTree.ForEach(null);

        // Make the new graph 
        m_newGraph = new MemoryGraph(m_targetNodeCount + m_graph.NodeTypeCount * 2);
        m_newGraph.Is64Bit = m_graph.Is64Bit;

        // Initialize the object statistics
        m_statsByType = new SampleStats[m_graph.NodeTypeCount];
        for (int i = 0; i < m_statsByType.Length; i++)
        {
            m_statsByType[i] = new SampleStats();
        }

        // And initialize the mapping from old nodes to new nodes.  (TODO: this can be a hash table to save size?  )
        m_newIndex = new NodeIndex[m_graph.NodeCount];
        for (int i = 0; i < m_newIndex.Length; i++)
        {
            m_newIndex[i] = NodeIndex.Invalid;
        }

        ValidateStats(false);

        VisitNode(m_graph.RootIndex, true, false); // visit the root for sure.  
        // Sample the nodes, trying to keep the 
        for (NodeIndex nodeIdx = 0; nodeIdx < m_graph.NodeIndexLimit; nodeIdx++)
        {
            VisitNode(nodeIdx, false, false);
        }

        ValidateStats(true);

        // See if we need to flesh out the potential node to become truly sampled node to hit our quota.  
        int[] numSkipped = new int[m_statsByType.Length];       // The number of times we have skipped a potential node.  
        for (NodeIndex nodeIdx = 0; nodeIdx < (NodeIndex)m_newIndex.Length; nodeIdx++)
        {
            var newIndex = m_newIndex[(int)nodeIdx];
            if (newIndex == PotentialNode)
            {
                var node = m_graph.GetNode(nodeIdx, m_nodeStorage);
                var stats = m_statsByType[(int)node.TypeIndex];
                int quota = (int)((stats.TotalCount / m_filteringRatio) + .5);
                int needed = quota - stats.SampleCount;
                if (needed > 0)
                {
                    // If we have not computed the frequency of sampling do it now.  
                    if (stats.SkipFreq == 0)
                    {
                        var available = stats.PotentialCount - stats.SampleCount;
                        Debug.Assert(0 <= available);
                        Debug.Assert(needed <= available);
                        stats.SkipFreq = Math.Max(available / needed, 1);
                    }

                    // Sample every Nth time.  
                    stats.SkipCtr++;
                    if (stats.SkipFreq <= stats.SkipCtr)
                    {
                        // Sample a new node 
                        m_newIndex[(int)nodeIdx] = m_newGraph.CreateNode();
                        stats.SampleCount++;
                        stats.SampleMetric += node.Size;
                        stats.SkipCtr = 0;
                    }
                }
            }
        }

        // OK now m_newIndex tell us which nodes we want.  actually define the selected nodes. 

        // Initialize the mapping from old types to new types.
        m_newTypeIndexes = new NodeTypeIndex[m_graph.NodeTypeCount];
        for (int i = 0; i < m_newTypeIndexes.Length; i++)
        {
            m_newTypeIndexes[i] = NodeTypeIndex.Invalid;
        }

        GrowableArray<NodeIndex> children = new GrowableArray<NodeIndex>(100);
        for (NodeIndex nodeIdx = 0; nodeIdx < (NodeIndex)m_newIndex.Length; nodeIdx++)
        {
            // Add all sampled nodes to the new graph.  
            var newIndex = m_newIndex[(int)nodeIdx];
            if (IsSampledNode(newIndex))
            {
                var node = m_graph.GetNode(nodeIdx, m_nodeStorage);
                // Get the children that are part of the sample (ignore ones that are filter)
                children.Clear();
                for (var childIndex = node.GetFirstChildIndex(); childIndex != NodeIndex.Invalid; childIndex = node.GetNextChildIndex())
                {
                    var newChildIndex = m_newIndex[(int)childIndex];
                    if (0 <= newChildIndex)                 // the child is not filtered out. 
                    {
                        children.Add(newChildIndex);
                    }
                }
                // define the node
                var newTypeIndex = GetNewTypeIndex(node.TypeIndex);
                m_newGraph.SetNode(newIndex, newTypeIndex, node.Size, children);
                m_newGraph.SetAddress(newIndex, m_graph.GetAddress(nodeIdx));
            }
        }

        ValidateStats(true, true);

        // Set the root.
        m_newGraph.RootIndex = m_newIndex[(int)m_graph.RootIndex];
        Debug.Assert(0 <= m_newGraph.RootIndex);            // RootIndex in the tree.  

        m_newGraph.AllowReading();

        // At this point we are done.  The rest is just to report the result to the user.  

        // Sort the m_statsByType
        var sortedTypes = new int[m_statsByType.Length];
        for (int i = 0; i < sortedTypes.Length; i++)
        {
            sortedTypes[i] = i;
        }

        Array.Sort(sortedTypes, delegate (int x, int y)
        {
            var ret = m_statsByType[y].TotalMetric.CompareTo(m_statsByType[x].TotalMetric);
            return ret;
        });

        m_log.WriteLine("Stats of the top types (out of {0:n0})", m_newGraph.NodeTypeCount);
        m_log.WriteLine("OrigSizeMeg SampleSizeMeg   Ratio   |   OrigCnt  SampleCnt    Ratio   | Ave Size | Type Name");
        m_log.WriteLine("---------------------------------------------------------------------------------------------");

        for (int i = 0; i < Math.Min(m_statsByType.Length, 30); i++)
        {
            int typeIdx = sortedTypes[i];
            NodeType type = m_graph.GetType((NodeTypeIndex)typeIdx, m_nodeTypeStorage);
            var stats = m_statsByType[typeIdx];

            m_log.WriteLine("{0,12:n6} {1,11:n6}  {2,9:f2} | {3,10:n0} {4,9:n0}  {5,9:f2} | {6,8:f0} | {7}",
                stats.TotalMetric / 1000000.0, stats.SampleMetric / 1000000.0, (stats.SampleMetric == 0 ? 0.0 : (double)stats.TotalMetric / stats.SampleMetric),
                stats.TotalCount, stats.SampleCount, (stats.SampleCount == 0 ? 0.0 : (double)stats.TotalCount / stats.SampleCount),
                (double)stats.TotalMetric / stats.TotalCount, type.Name);
        }

        m_log.WriteLine("Sampled Graph node count {0,11:n0} (reduced by {1:f2} ratio)", m_newGraph.NodeCount,
            (double)m_graph.NodeCount / m_newGraph.NodeCount);
        m_log.WriteLine("Sampled Graph type count {0,11:n0} (reduced by {1:f2} ratio)", m_newGraph.NodeTypeCount,
            (double)m_graph.NodeTypeCount / m_newGraph.NodeTypeCount);
        m_log.WriteLine("Sampled Graph node size  {0,11:n0} (reduced by {1:f2} ratio)", m_newGraph.TotalSize,
            (double)m_graph.TotalSize / m_newGraph.TotalSize);
        return m_newGraph;
    }

    /// <summary>
    /// returns an array of scaling factors.  This array is indexed by the type index of
    /// the returned graph returned by GetSampledGraph.   If the sampled count for that type multiplied
    /// by this scaling factor, you end up with the count for that type of the original unsampled graph.  
    /// </summary>
    public float[] CountScalingByType
    {
        get
        {
            var ret = new float[m_newGraph.NodeTypeCount];
            for (int i = 0; i < m_statsByType.Length; i++)
            {
                var newTypeIndex = MapTypeIndex((NodeTypeIndex)i);
                if (newTypeIndex != NodeTypeIndex.Invalid)
                {
                    float scale = 1;
                    if (m_statsByType[i].SampleMetric != 0)
                    {
                        scale = (float)((double)m_statsByType[i].TotalMetric / m_statsByType[i].SampleMetric);
                    }

                    ret[(int)newTypeIndex] = scale;
                }
            }
            for (int i = 1; i < ret.Length; i++)
            {
                Debug.Assert(0 < ret[i] && ret[i] <= float.MaxValue);
            }

            return ret;
        }
    }

    /// <summary>
    /// Maps 'oldTypeIndex' to its type index in the output graph
    /// </summary>
    /// <returns>New type index, will be Invalid if the type is not in the output graph</returns>
    public NodeTypeIndex MapTypeIndex(NodeTypeIndex oldTypeIndex)
    {
        return m_newTypeIndexes[(int)oldTypeIndex];
    }

    /// <summary>
    /// Maps 'oldNodeIndex' to its new node index in the output graph
    /// </summary>
    /// <returns>New node index, will be less than 0 if the node is not in the output graph</returns>
    public NodeIndex MapNodeIndex(NodeIndex oldNodeIndex)
    {
        return m_newIndex[(int)oldNodeIndex];
    }

    #region private
    /// <summary>
    /// Visits 'nodeIdx', if already visited, do nothing.  If unvisited determine if 
    /// you should add this node to the graph being built.   If 'mustAdd' is true or
    /// if we need samples it keep the right sample/total ratio, then add the sample.  
    /// </summary>
    private void VisitNode(NodeIndex nodeIdx, bool mustAdd, bool dontAddAncestors)
    {
        var newNodeIdx = m_newIndex[(int)nodeIdx];
        // If this node has been selected already, there is nothing to do.    
        if (IsSampledNode(newNodeIdx))
        {
            return;
        }
        // If we have visted this node and reject it and we are not forced to add it, we are done.
        if (newNodeIdx == RejectedNode && !mustAdd)
        {
            return;
        }

        Debug.Assert(newNodeIdx == NodeIndex.Invalid || newNodeIdx == PotentialNode || (newNodeIdx == RejectedNode && mustAdd));

        var node = m_graph.GetNode(nodeIdx, m_nodeStorage);
        var stats = m_statsByType[(int)node.TypeIndex];

        // If we have never seen this node before, add to our total count.  
        if (newNodeIdx == NodeIndex.Invalid)
        {
            if (stats.TotalCount == 0)
            {
                m_numDistictTypes++;
            }

            stats.TotalCount++;
            stats.TotalMetric += node.Size;
        }

        // Also insure that if there are a large number of types, that we sample them at least some. 
        if (stats.SampleCount == 0 && !mustAdd && (m_numDistictTypesWithSamples + .5F) * m_filteringRatio <= m_numDistictTypes)
        {
            mustAdd = true;
        }

        // We sample if we are forced (it is part of a parent chain), we need it to 
        // mimic the the original statistics, or if it is a large object (we include 
        // all large objects, since the affect overall stats so much).  
        if (mustAdd ||
            (stats.PotentialCount + .5f) * m_filteringRatio <= stats.TotalCount ||
            85000 < node.Size)
        {
            if (stats.SampleCount == 0)
            {
                m_numDistictTypesWithSamples++;
            }

            stats.SampleCount++;
            stats.SampleMetric += node.Size;
            if (newNodeIdx != PotentialNode)
            {
                stats.PotentialCount++;
            }

            m_newIndex[(int)nodeIdx] = m_newGraph.CreateNode();

            // Add all direct children as potential nodes (Potential nodes I can add without adding any other node)
            for (var childIndex = node.GetFirstChildIndex(); childIndex != NodeIndex.Invalid; childIndex = node.GetNextChildIndex())
            {
                var newChildIndex = m_newIndex[(int)childIndex];
                // Already a sampled or potential node.  Nothing to do.  
                if (IsSampledNode(newChildIndex) || newChildIndex == PotentialNode)
                {
                    continue;
                }

                var childNode = m_graph.GetNode(childIndex, m_childNodeStorage);
                var childStats = m_statsByType[(int)childNode.TypeIndex];

                if (newChildIndex == NodeIndex.Invalid)
                {
                    if (stats.TotalCount == 0)
                    {
                        m_numDistictTypes++;
                    }

                    childStats.TotalCount++;
                    childStats.TotalMetric += childNode.Size;
                }
                else
                {
                    Debug.Assert(newChildIndex == RejectedNode);
                }

                m_newIndex[(int)childIndex] = PotentialNode;
                childStats.PotentialCount++;
            }

            // For all ancestors, require them to be in the list
            if (!dontAddAncestors)
            {
                for (; ; )
                {
                    nodeIdx = m_spanningTree.Parent(nodeIdx);
                    if (nodeIdx == NodeIndex.Invalid || m_newIndex.Length == (int)nodeIdx) // The last index represents the 'orphan' node.  
                    {
                        break;
                    }

                    // Indicate that you should not add ancestors (since I will do this).  
                    // We do the adding in a loop (rather than letting recursion do it) to avoid stack overflows
                    // for long chains of objects.  
                    VisitNode(nodeIdx, true, true);
                }
                            }
        }
        else
        {
            if (newNodeIdx != PotentialNode)
            {
                m_newIndex[(int)nodeIdx] = RejectedNode;
            }
        }
    }

    /// <summary>
    /// Maps 'oldTypeIndex' to its type index in the output graph
    /// </summary>
    /// <param name="oldTypeIndex"></param>
    /// <returns></returns>
    private NodeTypeIndex GetNewTypeIndex(NodeTypeIndex oldTypeIndex)
    {
        var ret = m_newTypeIndexes[(int)oldTypeIndex];
        if (ret == NodeTypeIndex.Invalid)
        {
            var oldType = m_graph.GetType(oldTypeIndex, m_nodeTypeStorage);
            ret = m_newGraph.CreateType(oldType.Name, oldType.ModuleName, oldType.Size);
            m_newTypeIndexes[(int)oldTypeIndex] = ret;
        }
        return ret;
    }


    [Conditional("DEBUG")]
    private void ValidateStats(bool allNodesVisited, bool completed = false)
    {
        var statsCheckByType = new SampleStats[m_statsByType.Length];
        for (int i = 0; i < statsCheckByType.Length; i++)
        {
            statsCheckByType[i] = new SampleStats();
        }

        int total = 0;
        long totalSize = 0;
        int sampleTotal = 0;
        var typeStorage = m_graph.AllocTypeNodeStorage();
        for (NodeIndex nodeIdx = 0; nodeIdx < (NodeIndex)m_newIndex.Length; nodeIdx++)
        {
            var node = m_graph.GetNode(nodeIdx, m_nodeStorage);
            var stats = statsCheckByType[(int)node.TypeIndex];
            var type = node.GetType(typeStorage);
            var typeName = type.Name;
            var newNodeIdx = m_newIndex[(int)nodeIdx];

            if (newNodeIdx == NodeIndex.Invalid)
            {
                // We should have visted every node, so there should be no Invalid nodes. 
                Debug.Assert(!allNodesVisited);
            }
            else
            {
                total++;
                stats.TotalCount++;
                stats.TotalMetric += node.Size;
                totalSize += node.Size;
                Debug.Assert(node.Size != 0 || typeName.StartsWith("[") || typeName == "UNDEFINED");
                if (IsSampledNode(newNodeIdx) || newNodeIdx == PotentialNode)
                {
                    if (nodeIdx != m_graph.RootIndex)
                    {
                        Debug.Assert(IsSampledNode(m_spanningTree.Parent(nodeIdx)));
                    }

                    stats.PotentialCount++;
                    if (IsSampledNode(newNodeIdx))
                    {
                        stats.SampleCount++;
                        sampleTotal++;
                        stats.SampleMetric += node.Size;
                    }
                }
                else
                {
                    Debug.Assert(newNodeIdx == RejectedNode);
                }
            }
            statsCheckByType[(int)node.TypeIndex] = stats;
        }

        float[] scalings = null;
        if (completed)
        {
            scalings = CountScalingByType;
        }

        for (NodeTypeIndex typeIdx = 0; typeIdx < m_graph.NodeTypeIndexLimit; typeIdx++)
        {
            var type = m_graph.GetType(typeIdx, typeStorage);
            var typeName = type.Name;
            var statsCheck = statsCheckByType[(int)typeIdx];
            var stats = m_statsByType[(int)typeIdx];

            Debug.Assert(stats.TotalMetric == statsCheck.TotalMetric);
            Debug.Assert(stats.TotalCount == statsCheck.TotalCount);
            Debug.Assert(stats.SampleCount == statsCheck.SampleCount);
            Debug.Assert(stats.SampleMetric == statsCheck.SampleMetric);
            Debug.Assert(stats.PotentialCount == statsCheck.PotentialCount);

            Debug.Assert(stats.PotentialCount <= statsCheck.TotalCount);
            Debug.Assert(stats.SampleCount <= statsCheck.PotentialCount);

            // We should be have at least m_filterRatio of Potential objects 
            Debug.Assert(!((stats.PotentialCount + .5f) * m_filteringRatio <= stats.TotalCount));

            // If we completed, then we converted potentials to true samples.   
            if (completed)
            {
                Debug.Assert(!((stats.SampleCount + .5f) * m_filteringRatio <= stats.TotalCount));

                // Make sure that scalings that we finally output were created correctly
                if (stats.SampleMetric > 0)
                {
                    var newTypeIdx = MapTypeIndex(typeIdx);
                    var estimatedTotalMetric = scalings[(int)newTypeIdx] * stats.SampleMetric;
                    Debug.Assert(Math.Abs(estimatedTotalMetric - stats.TotalMetric) / stats.TotalMetric < .01);
                }
            }

            if (stats.SampleCount == 0)
            {
                Debug.Assert(stats.SampleMetric == 0);
            }

            if (stats.TotalMetric == 0)
            {
                Debug.Assert(stats.TotalMetric == 0);
            }
        }

        if (allNodesVisited)
        {
            Debug.Assert(total == m_graph.NodeCount);
            // TODO FIX NOW enable Debug.Assert(totalSize == m_graph.TotalSize);
            Debug.Assert(Math.Abs(totalSize - m_graph.TotalSize) / totalSize < .01);     // TODO FIX NOW lame, replace with assert above
        }
        Debug.Assert(sampleTotal == m_newGraph.NodeCount);
    }

    private class SampleStats
    {
        public int TotalCount;          // The number of objects of this type in the original graph
        public int SampleCount;         // The number of objects of this type we have currently added to the new graph
        public int PotentialCount;      // SampleCount + The number of objects of this type that can be added without needing to add other nodes
        public long TotalMetric;
        public long SampleMetric;
        public int SkipFreq;          // When sampling potentials, take every Nth one where this is the N
        public int SkipCtr;             // This remembers our last N.  
    };

    /// <summary>
    /// This value goes in the m_newIndex[].   If we accept the node into the sampled graph, we put the node
    /// index in the NET graph in m_newIndex.   If we reject the node we use the special RejectedNode value
    /// below
    /// </summary>
    private const NodeIndex RejectedNode = (NodeIndex)(-2);

    /// <summary>
    /// This value also goes in m_newIndex[].   If we can add this node without needing to add any other nodes
    /// to the new graph (that is it is one hop from an existing accepted node, then we mark it specially as
    /// a PotentialNode).   We add these in a second pass over the data.  
    /// </summary>
    private const NodeIndex PotentialNode = (NodeIndex)(-3);

    private bool IsSampledNode(NodeIndex nodeIdx) { return 0 <= nodeIdx; }

    private MemoryGraph m_graph;
    private int m_targetNodeCount;
    private TextWriter m_log;
    private Node m_nodeStorage;
    private Node m_childNodeStorage;
    private NodeType m_nodeTypeStorage;
    private float m_filteringRatio;
    private SampleStats[] m_statsByType;
    private int m_numDistictTypesWithSamples;
    private int m_numDistictTypes;
    private NodeIndex[] m_newIndex;
    private NodeTypeIndex[] m_newTypeIndexes;
    private SpanningTree m_spanningTree;
    private MemoryGraph m_newGraph;
    #endregion
}

#if false
namespace Experimental
{
    /// <summary>
    /// code:PagedGrowableArray is an array (has an index operation) but can efficiently represent
    /// either very large arrays as well as sparse arrays.  
    /// </summary>
    public struct PagedGrowableArray<T>
    {
        public PagedGrowableArray(int initialSize)
        {
            Debug.Assert(initialSize > 0);
            var numPages = (initialSize + pageSize - 1) / pageSize;
            m_count = 0;
            m_pages = new T[numPages][];
        }
        public T this[int index]
        {
            get
            {
                Debug.Assert((uint)index < (uint)m_count);
                return m_pages[index / pageSize][index % pageSize];
            }
            set
            {
                Debug.Assert((uint)index < (uint)m_count);
                m_pages[index / pageSize][index % pageSize] = value;
            }
        }
        public int Count
        {
            get { return m_count; }
            set
            {
                Debug.Assert(false, "Not completed");
                if (value > m_count)
                {
                    var onLastPage = m_count % pageSize;
                    if (onLastPage != 0)
                    {
                        var lastPage = m_pages[m_count / pageSize];
                        var nullOnLastPage = Math.Min(value - m_count, pageSize);
                        while (nullOnLastPage > onLastPage)
                        {
                            --nullOnLastPage;
                            lastPage[nullOnLastPage] = default(T);
                        }
                    }
                }
                else
                {
                    // Release unused pages
                    while (m_count > value)
                    {

                    }
                }
                m_count = value;
            }
        }
        /// <summary>
        /// Append the value to the end of the array.  
        /// </summary>
        /// <param name="value"></param>
        public void Add(T value)
        {
            if (m_count % pageSize == 0)
            {
                var pageIndex = m_count / pageSize;
                if (pageIndex >= m_pages.Length)
                {
                    var newPageLength = m_pages.Length * 2;
                    var newPages = new T[newPageLength][];
                    Array.Copy(m_pages, newPages, m_pages.Length);
                    m_pages = newPages;
                }
                if (m_pages[pageIndex] == null)
                    m_pages[pageIndex] = new T[pageSize];
            }

            m_pages[m_count / pageSize][m_count % pageSize] = value;
            m_count++;
        }

#region private
        const int pageSize = 4096;

        T[][] m_pages;
        int m_count;
#endregion
    }

    class CompressedGrowableArray : IFastSerializable
    {
        public CompressedGrowableArray()
        {
            m_pages = new Page[256];
        }
        public long this[int index]
        {
            get
            {
                return m_pages[index >> 8][(byte)index];
            }
        }
        /// <summary>
        /// Append the value to the end of the array.  
        /// </summary>
        /// <param name="value"></param>
        public void Add(long value)
        {
            if (m_numPages >= m_pages.Length)
            {
                int newLength = m_pages.Length * 2;
                var newArray = new Page[newLength];
                Array.Copy(m_pages, newArray, m_pages.Length);
                m_pages = newArray;

            }
            // m_pages[m_numPages-1].Add(value);
        }

#region private
        void IFastSerializable.ToStream(Serializer serializer)
        {
            serializer.Write(m_numPages);
            for (int i = 0; i < m_numPages; i++)
                serializer.Write(m_pages[i]);
        }
        void IFastSerializable.FromStream(Deserializer deserializer)
        {
            deserializer.Read(out m_numPages);
            for (int i = 0; i < m_numPages; i++)
                deserializer.Read(out m_pages[i]);
        }

        /// <summary>
        /// A page represents 256 entries in the table.   For each page we remember a 'm_baseValue' and 
        /// we delta encode.  If the offset fit 15 bits you simply add the offset to the base value
        /// Otherwise what is in the table is an offset into the 'm_compressedValues' blob and the offset
        /// is encoded as a variable length signed number.  
        /// </summary>
        class Page : IFastSerializable
        {
            Page(long baseValue)
            {
                m_indexOrOffset = new short[256];
                m_baseValue = baseValue;
            }
            public long this[byte index]
            {
                get
                {
                    short val = m_indexOrOffset[index];
                    if ((val & 0x8000) != 0)
                        return val + m_baseValue;
                    return ValueFromIndex(val);
                }
            }

#region private
            private long ValueFromIndex(short val)
            {
                return m_baseValue + ReadCompressedInt(val & ~0x8000);
            }
            private long ReadCompressedInt(int blobIndex)
            {
                long ret = 0;
                byte b = m_compressedValues[blobIndex++];
                int asInt = b << 25 >> 25;
                ret = asInt;
#if DEBUG
                for (int i = 0; ; i++)
                {
                    Debug.Assert(i < 5);
#else
                for (; ; )
                {
#endif
                    if ((b & 0x80) == 0)
                        return ret;
                    ret <<= 7;
                    b = m_compressedValues[blobIndex++];
                    ret += (b & 0x7f);
                }
            }
            private int WriteCompressedInt(long value)
            {
                throw new NotImplementedException();
            }

            void IFastSerializable.ToStream(Serializer serializer)
            {
                serializer.Write(m_baseValue);
                for (int i = 0; i < 256; i++)
                    serializer.Write(m_indexOrOffset[i]);
                serializer.Write(m_compressedValuesIndex);
                for (int i = 0; i < m_compressedValuesIndex; i++)
                    serializer.Write(m_compressedValues[i]);
            }
            void IFastSerializable.FromStream(Deserializer deserializer)
            {
                deserializer.Read(out m_baseValue);
                for (int i = 0; i < 256; i++)
                    m_indexOrOffset[i] = deserializer.ReadInt16();

                deserializer.Read(out m_compressedValuesIndex);
                if (m_compressedValuesIndex != 0)
                {
                    m_compressedValues = new byte[m_compressedValuesIndex];
                    for (int i = 0; i < m_compressedValuesIndex; i++)
                        m_compressedValues[i] = deserializer.ReadByte();
                }
            }

            long m_baseValue;                  // All values are relative to this.  
            short[] m_indexOrOffset;           // table of value (either offsets or indexes into the compressed blobs)

            byte[] m_compressedValues;          // If all the values are not within 32K of the base, then store them here.  
            int m_compressedValuesIndex;        // Next place to write to in m_compressedValues
#endregion
        }

        int m_numPages;
        Page[] m_pages;
#endregion
    }
}

#endif
