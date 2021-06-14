using ClrProfiler;
using FastSerialization;
using System.Collections.Generic;
using Address = System.UInt64;

namespace Graphs
{
    /// <summary>
    /// A MemoryGraph extends the basic Graph functionality to add the Memory addresses for each of the nodes, as well as understands
    /// how to read in CLRProfiler logs to form the graph.  
    /// 
    /// See code:Graph for details on the general philosophy of design (keeping the graph small...)
    /// </summary>
    public class ClrProfilerMemoryGraph : MemoryGraph, IFastSerializable
    {
        public ClrProfilerMemoryGraph(string profilerFile)
            : base(10000)
        {
            // Needed for the callback in ReadFile 
            m_tempChildren = new GrowableArray<NodeIndex>(1000);
            ClearWorker();

            m_clrProfilerParser = new ClrProfilerParser();
            m_clrProfilerParser.ObjectDescription += OnObjectDescription;
            m_clrProfilerParser.GCRoot += OnGCRoot;
            m_clrProfilerParser.HeapDump += OnHeapDump;
            m_clrProfilerParser.StaticVar += OnStaticVar;
            m_clrProfilerParser.LocalVar += OnLocalVar;

            m_clrProfilerParser.ReadFile(profilerFile);

            // set the module names on every type if present.
            var nodeTypeStorage = AllocTypeNodeStorage();
            for (var profilerTypeId = 0; profilerTypeId < (int)m_clrProfilerParser.TypeIdLimit; profilerTypeId++)
            {
                var profilerType = m_clrProfilerParser.GetTypeById((ProfilerTypeID)profilerTypeId);
                if (profilerType == null)
                {
                    continue;
                }

                var module = profilerType.Module;
                if (module != null && profilerTypeId < m_profilerTypeToNodeType.Count)
                {
                    var nodeTypeId = m_profilerTypeToNodeType[profilerTypeId];
                    if (nodeTypeId != NodeTypeIndex.Invalid)
                    {
                        var nodeType = GetType((NodeTypeIndex)nodeTypeId, nodeTypeStorage);
                        nodeType.ModuleName = profilerType.Module.name;
                    }
                }
            }

            // Now we have module information, process the defer local and static processing
            foreach (var deferedRoot in m_deferedRoots)
            {
                ProfilerType profilerType = m_clrProfilerParser.GetTypeById(deferedRoot.typeID);
                var appDomainNode = m_rootNode.FindOrCreateChild("[appdomain " + deferedRoot.appDomainName + "]");
                var varKindNode = appDomainNode.FindOrCreateChild("[" + deferedRoot.prefix + " vars]");
                var moduleName = System.IO.Path.GetFileNameWithoutExtension(profilerType.ModuleName);
                var moduleNode = varKindNode.FindOrCreateChild("[" + deferedRoot.prefix + " vars " + moduleName + "]");
                var typeNode = moduleNode.FindOrCreateChild("[" + deferedRoot.prefix + " vars " + profilerType.name + "]");
                var node = typeNode.FindOrCreateChild(
                    profilerType.name + "+" + deferedRoot.name + " [" + deferedRoot.prefix + " var]", profilerType.ModuleName);
                node.AddChild(deferedRoot.nodeIndex);
            }

            // finish off the root nodes.  
            RootIndex = m_rootNode.Build();
            AllowReading();

            // These are only needed for the callbacks in 'ReadFile' save space by clearing them out.  
            m_clrProfilerParser = null;
            m_tempChildren = new GrowableArray<NodeIndex>();                // Clear the array
            m_profilerTypeToNodeType = new GrowableArray<NodeTypeIndex>();  // Clear the array
            m_rootNode = null;
            m_rootNodeForUnknownRoot = null;
            m_addressToNodeIndex = null;
            m_deferedRoots = null;
        }

        #region private
        /// <summary>
        /// Clear handles puts it back into the state that existed after the constructor returned
        /// </summary>
        protected override void Clear()
        {
            base.Clear();
            ClearWorker();
        }

        /// <summary>
        /// ClearWorker does only that part of clear needed for this level of the hierarchy (and needs
        /// to be done by the constructor too). 
        /// </summary>
        private void ClearWorker()
        {
            m_tempChildren.Clear();
            m_profilerTypeToNodeType.Clear();
            m_hasGCRootInfo = false;
            m_seenObjects = false;
            m_rootNode = new MemoryNodeBuilder(this, "[root]");
            var otherRoots = m_rootNode.FindOrCreateChild("[other roots]");
            // We need unknown roots to not be a direct child of [other roots] so that their scanning priority is low
            m_rootNodeForUnknownRoot = otherRoots.FindOrCreateChild("[unknown roots]");
            m_deferedRoots = new List<DeferedRoot>();
        }

        // Callbacks from various records in the ClrProfiler log.  
        private void OnStaticVar(Address objectAddress, string fieldName, ProfilerTypeID typeID, uint threadID, string appDomainName)
        {

            // Unfortunately we don't know the module name until late in the trace, so 
            // we have to defer the work until then.  So just remember what we needs to do. 
            m_deferedRoots.Add(new DeferedRoot
            {
                name = fieldName,
                nodeIndex = GetNodeIndex(objectAddress),
                typeID = typeID,
                prefix = threadID == 0 ? "static" : "threadStatic",
                appDomainName = appDomainName,
            });
        }
        private void OnLocalVar(Address objectAddress, string localVarName, string methodName, ProfilerTypeID typeID, uint threadID, string appDomainName)
        {
            m_deferedRoots.Add(new DeferedRoot
            {
                // Unfortunately we don't know the module name until late in the trace, so 
                // we have to defer the work until then.  So just remember what we needs to do. 
                name = methodName + " " + localVarName,
                nodeIndex = GetNodeIndex(objectAddress),
                typeID = typeID,
                prefix = "local",
                appDomainName = appDomainName,
            });
        }
        private void OnGCRoot(Address objectAddress, GcRootKind rootKind, GcRootFlags rootFlags, Address rootID)
        {
            // There may be more than one heap dump in the trace (if CLRProfiler collected it).
            // For now we simply choose the last by starting over if we see another heap dump
            if (m_seenObjects)
            {
                Clear();
            }

            m_hasGCRootInfo = true;
            if (objectAddress == 0)
            {
                return;
            }

            // Ignore weak references as they are not keeping things alive. 
            if ((rootFlags & GcRootFlags.WeakRef) != 0)
            {
                return;
            }

            // TODO we can do better by looking at flags.  
            m_rootNodeForUnknownRoot.AddChild(GetNodeIndex(objectAddress));
        }
        private void OnHeapDump(List<Address> roots)
        {
            // SOS does not generate GCRoot information, so we fall back to using the roots passed to use on HeapDump
            // in that case.   Otherwise we use the information gathered from the GCRoots (ClrProfiler)
            if (m_hasGCRootInfo)
            {
                return;
            }

            // There may be more than one heap dump in the trace (if CLRProfiler collected it).
            // For now we simply choose the last by starting over if we see another heap dump
            if (m_seenObjects)
            {
                Clear();
            }

            foreach (var root in roots)
            {
                m_rootNodeForUnknownRoot.AddChild(GetNodeIndex(root));
            }
        }

        private void OnObjectDescription(Address objectAddress, ProfilerTypeID typeId, uint size, List<Address> pointsTo)
        {
            var nodeIndex = GetNodeIndex(objectAddress);
            m_tempChildren.Clear();
            for (int i = 0; i < pointsTo.Count; i++)
            {
                m_tempChildren.Add(GetNodeIndex(pointsTo[i]));
            }

            var typeIndex = GetNodeTypeIndex(typeId);
            SetNode(nodeIndex, typeIndex, (int)size, m_tempChildren);
        }
        private NodeTypeIndex GetNodeTypeIndex(ProfilerTypeID typeId)
        {
            var typeIdasInt = (int)typeId;

            NodeTypeIndex ret = NodeTypeIndex.Invalid;
            if (typeIdasInt >= m_profilerTypeToNodeType.Count)
            {
                int prevSize = m_profilerTypeToNodeType.Count;
                int newSize = typeIdasInt + 100;
                m_profilerTypeToNodeType.Count = newSize;
                for (int i = prevSize; i < newSize; i++)
                {
                    m_profilerTypeToNodeType[i] = NodeTypeIndex.Invalid;
                }
            }
            else
            {
                ret = m_profilerTypeToNodeType[typeIdasInt];
            }

            if (ret == NodeTypeIndex.Invalid)
            {
                ProfilerType profilerType = m_clrProfilerParser.GetTypeById(typeId);
                ret = CreateType(profilerType.name);
                m_profilerTypeToNodeType[typeIdasInt] = ret;

                // TODO FIX NOW don't allocate every time
                GetType(ret, AllocTypeNodeStorage()).ModuleName = profilerType.ModuleName;
            }
            return ret;
        }

        private class DeferedRoot
        {
            public string name;
            public ProfilerTypeID typeID;
            public NodeIndex nodeIndex;
            public string prefix;
            public string appDomainName;

        }

        // These fields are only used during MemoryGraph construction.  
        private ClrProfilerParser m_clrProfilerParser;                  // Object use to parse the records from a CLRProfiler log.  
        private GrowableArray<NodeTypeIndex> m_profilerTypeToNodeType;  // maps profilers types to node types. 
        private GrowableArray<NodeIndex> m_tempChildren;                        // The array is reused again and again when processing children.  

        private bool m_seenObjects;
        private bool m_hasGCRootInfo;
        private MemoryNodeBuilder m_rootNode;
        private MemoryNodeBuilder m_rootNodeForUnknownRoot;
        private List<DeferedRoot> m_deferedRoots;

        // TODO decide if this is worth it 
#if false
    /// <summary>
    /// AddressDictionary is a optimized Dictionary<Address, NodeIndex>.   It expect its caller
    /// to be able to reload all the key-value pairs at any time.  AddressDictionary indicates
    /// it wants this by calling the reload() delegate.   It uses this whenever it needs to rehash
    /// 
    /// By forcing the caller to be ablet to do this, AddressDictionary does not need to store the
    /// key of the key-value pairs, which saves memory.   As a result AddressDictionary is very lean
    /// (roughly 4 bytes per entry * 1/HashLoadFactor).  
    /// </summary>
    class AddressDictionary
    {
        public AddressDictionary(int size, MemoryGraph graph)
        {
            m_hashArray = new NodeIndex[size];
            m_collisions = new Dictionary<Address, NodeIndex>(size / 20 + 1);
            m_graph = graph;
        }
        public bool TryGetValue(Address key, out NodeIndex value)
        {
            int hash = ((int)((ulong)key / 16)) % m_hashArray.Length;
            value = m_hashArray[hash];
            if ((uint) value < (uint) m_graph.m_nodeAddresses.Count && m_graph.m_nodeAddresses[(int) value] == key)
                return true;
            return m_collisions.TryGetValue(key, out value);
        }
        public void Add(Address key, NodeIndex value)
        {
            // We go until 60 % fill factor
            if (m_count / 10 > m_hashArray.Length / 8)
            {
                // Clear the Address to NodeIndex table.
                m_collisions.Clear();

                var newLen = m_hashArray.Length * 2;
                if (m_hashArray.Length > 10000000)
                    newLen = m_hashArray.Length * 3 / 2;
                m_hashArray = new NodeIndex[newLen];
                m_count = 0;

                // Reload all the values.  
                for (int i = 0; i < m_graph.m_nodeAddresses.Count-1; i++) // -1 is because we have already increased the count
                    m_graph.m_addressToNodeIndex.Add(m_graph.m_nodeAddresses[i], (NodeIndex)i);
            }
            m_count++;
            int hash = ((int)((ulong)key / 16)) % m_hashArray.Length;
            var hashVal = m_hashArray[hash];
            if (hashVal == 0 && value != 0)
                m_hashArray[hash] = value;
            else
                m_collisions.Add(key, value);
        }

        #region private
        MemoryGraph m_graph;
        NodeIndex[] m_hashArray;
        Dictionary<Address, NodeIndex> m_collisions;
        int m_count;
        #endregion
    }
#endif
        void IFastSerializable.ToStream(Serializer serializer)
        {
            base.ToStream(serializer);
            // Write out the Memory addresses of each object 
            serializer.Write(m_nodeAddresses.Count);
            for (int i = 0; i < m_nodeAddresses.Count; i++)
            {
                serializer.Write((long)m_nodeAddresses[i]);
            }
        }
        void IFastSerializable.FromStream(Deserializer deserializer)
        {
            base.FromStream(deserializer);
            // Read in the Memory addresses of each object 
            m_nodeAddresses.Clear();
            int addressCount = deserializer.ReadInt32();
            for (int i = 0; i < addressCount; i++)
            {
                m_nodeAddresses.Add((Address)deserializer.ReadInt64());
            }
        }

        #endregion
    }
}
