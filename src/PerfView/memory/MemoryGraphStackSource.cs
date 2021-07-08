using Diagnostics.Tracing.StackSources;
using Microsoft.Diagnostics.Tracing.Stacks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Address = System.UInt64;

namespace Graphs
{
    /// <summary>
    /// code:MemorySampleSource hooks up a Memory graph to become a Sample source.  Currently we do
    /// a breadth-first traversal to form a spanning tree, and then create samples for each node
    /// where the 'stack' is the path to the root of this spanning tree.
    /// 
    /// This is just a first cut...
    /// </summary>
    public class MemoryGraphStackSource : StackSource
    {
        /// <summary>
        /// Create a stack source from 'graph'.   samplingRatio is the ratio of size of the graph to 
        /// the size of the actual heap (if you only sampled part of it).   Counts are scaled by the 
        /// inverse of this so that the expected size of the graph works out. 
        /// 
        /// log is were to send diagnostic messages (can be null)
        /// 
        /// countMultipliers is an array (indexed by type Index), that will be used to multiply the
        /// counts in 'graph' when generating the stack source (thus if Type T has count 5 and 
        /// countMultipliers[T] = 10 then the stack source will return 50.   This is used to scale
        /// sampled graphs.   
        /// </summary>
        public MemoryGraphStackSource(Graph graph, TextWriter log, double[] countMultipliers = null)
        {
            m_asMemoryGraph = graph as MemoryGraph;
            m_graph = graph;
            m_log = log;
            m_nodeStorage = graph.AllocNodeStorage();
            m_childStorage = graph.AllocNodeStorage();
            m_typeStorage = graph.AllocTypeNodeStorage();
            m_sampleStorage = new StackSourceSample(this);
            m_countMultipliers = countMultipliers;

            // We need to reduce the graph to a tree.   Each node is assigned a unique 'parent' which is its 
            // parent in a spanning tree of the graph.  
            // The +1 is for orphan node support.  
            m_parent = new NodeIndex[(int)graph.NodeIndexLimit + 1];

            // If it is a memory stack source (it pretty much always is), figure out the maximum address.
            // We use addresses as 'time' for stacks so that the 'when' field in perfView is meaningful.  
            MemoryGraph asMemoryGraph = graph as MemoryGraph;
            if (asMemoryGraph != null)
            {
                for (NodeIndex idx = 0; idx < asMemoryGraph.NodeIndexLimit; idx++)
                {
                    Address endAddress = asMemoryGraph.GetAddress(idx) + (uint)asMemoryGraph.GetNode(idx, m_nodeStorage).Size;
                    if (m_maxAddress < endAddress)
                    {
                        m_maxAddress = endAddress;
                    }
                }
            }
        }

        /// <summary>
        /// These methods let you get from the stack source abstraction to the underlying graph
        /// </summary>
        public MemoryGraph Graph { get { return (MemoryGraph)m_graph; } }
        public RefGraph RefGraph
        {
            get
            {
                if (m_refGraph == null)
                {
                    m_refGraph = new RefGraph(m_graph);
                }

                return m_refGraph;
            }
        }
        public NodeIndex GetNodeIndexForSample(StackSourceSampleIndex sampleIdx) { return (NodeIndex)sampleIdx; }

#if false // TODO FIX NOW REMOVE 
        // We support sampling rate.  
        public override double? SamplingRate
        {
            get { return m_samplingRate; }
            set
            {
                var newRate = 1.0F;
                if (value.HasValue && 1 <= value.Value)
                    newRate = value.Value;
                else
                    m_samplingRate = 1;

                // Also update the count multiplier.  
                CountMultiplier = CountMultiplier / m_samplingRate * newRate;
                m_samplingRate = newRate;
            }
        }
#endif

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
                return SpanningTree.DefaultPriorities;
            }
        }

        /// <summary>
        /// Returns the number of references to the node represented by 'nodeIndex'.  The count will max out at 255 (but not wrap)
        /// </summary>
        public byte RefCount(NodeIndex nodeIndex)
        {
            if (m_refCounts == null)
            {
                // Do the expensive operation of walking the graph collecting refcounts.  
                m_refCounts = new byte[(int)m_graph.NodeIndexLimit];
                m_refCounts[(int)m_graph.RootIndex] = 1;

                var nodeStorage = m_graph.AllocNodeStorage();       // Need for enumeration below.  

                var nodesToVisit = new Queue<NodeIndex>(1000);
                nodesToVisit.Enqueue(m_graph.RootIndex);
                while (nodesToVisit.Count > 0)
                {
                    var curNodeIndex = nodesToVisit.Dequeue();
                    var curNode = m_graph.GetNode(curNodeIndex, nodeStorage);
                    for (var childIndex = curNode.GetFirstChildIndex(); childIndex != NodeIndex.Invalid; childIndex = curNode.GetNextChildIndex())
                    {
                        byte val = m_refCounts[(int)childIndex];
                        if (val == 0)
                        {
                            nodesToVisit.Enqueue(childIndex);
                        }

                        val++; if (val == 0)
                        {
                            val = 255;     // increment, but never wrap-around. 
                        }

                        m_refCounts[(int)childIndex] = val;
                    }
                }

                var singletons = 0;
                var orphans = 0;
                var max = 0;
                var totalRefs = 0.0;
                foreach (var refCount in m_refCounts)
                {
                    totalRefs += refCount;
                    if (refCount == 1)
                    {
                        singletons++;
                    }
                    else if (refCount == 0)
                    {
                        orphans++;
                    }
                    else if (refCount == 255)
                    {
                        max++;
                    }
                }
                Trace.WriteLine("Total = " + m_refCounts.Length);
                Trace.WriteLine("Singletons = " + singletons);
                Trace.WriteLine("Orphans = " + orphans);
                Trace.WriteLine("Max = " + max);
                Trace.WriteLine("Average = " + (totalRefs / m_refCounts.Length).ToString("f2"));
                Trace.WriteLine("");
            }
            return m_refCounts[(int)nodeIndex];
        }

        public void ForEachUnordered(Action<StackSourceSample, bool> callback)
        {
            // Initialize the breadth-first work queue.
            var nodesToVisit = new Queue<NodeIndex>(1024);
            nodesToVisit.Enqueue(m_graph.RootIndex);

            // reset the visited information.
            for (int i = 0; i < m_parent.Length; i++)
            {
                m_parent[i] = NodeIndex.Invalid;
            }

            MemoryGraph asMemoryGraph = m_graph as MemoryGraph;

            bool scanedForOrphans = false;
            for (int i = 0; ; i++)
            {
                if ((i & 0x1FFF) == 0)  // Every 8K
                {
                    System.Threading.Thread.Sleep(0);       // Allow interruption.  
                }

                NodeIndex nodeIndex;
                if (nodesToVisit.Count == 0)
                {
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

                nodeIndex = nodesToVisit.Dequeue();

                // Insert any children that have not already been visited (had a parent assigned) into the work queue). 
                Node node = m_graph.GetNode(nodeIndex, m_nodeStorage);
                for (var childIndex = node.GetFirstChildIndex(); childIndex != NodeIndex.Invalid; childIndex = node.GetNextChildIndex())
                {
                    if (m_parent[(int)childIndex] == NodeIndex.Invalid && childIndex != m_graph.RootIndex) 
                    {
                        m_parent[(int)childIndex] = nodeIndex;
                        nodesToVisit.Enqueue(childIndex);
                    }
                }

                // Return the node.  
                m_sampleStorage.Metric = node.Size;
                // We use the address as the timestamp.  This allows you to pick particular instances
                // and see where particular instances are in memory by looking at the 'time'.  
                if (asMemoryGraph != null)
                {
                    m_sampleStorage.TimeRelativeMSec = asMemoryGraph.GetAddress(node.Index);
                }

                m_sampleStorage.SampleIndex = (StackSourceSampleIndex)node.Index;
                m_sampleStorage.StackIndex = (StackSourceCallStackIndex)node.Index;
                if (m_countMultipliers != null)
                {
                    m_sampleStorage.Count = m_countMultipliers[(int)node.TypeIndex];
                    m_sampleStorage.Metric = m_sampleStorage.Metric * m_sampleStorage.Count;
                }

                Debug.Assert(m_sampleStorage.Metric >= 0);
                Debug.Assert(m_sampleStorage.Count >= 0);
                Debug.Assert(0 < m_sampleStorage.Count && m_sampleStorage.Count <= float.MaxValue);
                Debug.Assert(0 <= m_sampleStorage.Metric && m_sampleStorage.Metric <= float.MaxValue);
                callback(m_sampleStorage, !scanedForOrphans);
            }
        }

        public override void ForEach(Action<StackSourceSample> callback)
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

            // We keep track of node depth so that we can limit it.   
            int[] nodeDepth = new int[m_parent.Length];
            double[] nodePriorities = new double[m_parent.Length];
            MemoryGraph asMemoryGraph = m_graph as MemoryGraph;

            bool scanedForOrphans = false;
            var epsilon = 1E-7;            // Something that is big enough not to bet lost in roundoff error.  
            double order = 0;
            for (int i = 0; ; i++)
            {
                if ((i & 0x1FFF) == 0)  // Every 8K
                {
                    System.Threading.Thread.Sleep(0);       // Allow interruption.  
                }

                NodeIndex nodeIndex;
                double nodePriority;
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
                Node node = m_graph.GetNode(nodeIndex, m_nodeStorage);
                var parentPriority = nodePriorities[(int)node.Index];
                for (var childIndex = node.GetFirstChildIndex(); childIndex != NodeIndex.Invalid; childIndex = node.GetNextChildIndex())
                {
                    if (m_parent[(int)childIndex] == NodeIndex.Invalid && childIndex != m_graph.RootIndex)
                    {
                        m_parent[(int)childIndex] = nodeIndex;
                        int parentDepth = nodeDepth[(int)nodeIndex];
                        nodeDepth[(int)childIndex] = checked(parentDepth + 1);

                        // the priority of the child is determined by its type and 1/10 by its parent.  
                        var child = m_graph.GetNode(childIndex, m_childStorage);
                        var childPriority = m_typePriorities[(int)child.TypeIndex] + parentPriority / 10;
                        nodePriorities[(int)childIndex] = childPriority;

                        // Subtract a small increasing value to keep the queue in order if the priorities are the same. 
                        // This is a bit of a hack since it can get big and perturb the user-defined order.  
                        order += epsilon;
                        nodesToVisit.Enqueue(childIndex, childPriority - order);
                    }
                }

                // Return the node.  
                m_sampleStorage.Metric = node.Size;
                // We use the address as the timestamp.  This allows you to pick particular instances
                // and see where particular instances are in memory by looking at the 'time'.  
                if (asMemoryGraph != null)
                {
                    m_sampleStorage.TimeRelativeMSec = asMemoryGraph.GetAddress(node.Index);
                }

                m_sampleStorage.SampleIndex = (StackSourceSampleIndex)node.Index;
                m_sampleStorage.StackIndex = (StackSourceCallStackIndex)node.Index;
                if (m_countMultipliers != null)
                {
                    m_sampleStorage.Count = m_countMultipliers[(int)node.TypeIndex];
                    m_sampleStorage.Metric = node.Size * m_sampleStorage.Count;
                }
                Debug.Assert(m_sampleStorage.Metric >= 0);
                Debug.Assert(m_sampleStorage.Count >= 0);
                Debug.Assert(0 < m_sampleStorage.Count && m_sampleStorage.Count <= float.MaxValue);
                Debug.Assert(0 <= m_sampleStorage.Metric && m_sampleStorage.Metric <= float.MaxValue);
                callback(m_sampleStorage);
            }
        }
        public override StackSourceCallStackIndex GetCallerIndex(StackSourceCallStackIndex callStackIndex)
        {
            NodeIndex nodeIndex = (NodeIndex)callStackIndex;
            return (StackSourceCallStackIndex)m_parent[(int)nodeIndex];
        }
        public override StackSourceFrameIndex GetFrameIndex(StackSourceCallStackIndex callStackIndex)
        {
            NodeIndex nodeIndex = (NodeIndex)callStackIndex;

            // Orphan node support 
            if (nodeIndex == m_graph.NodeIndexLimit)
            {
                return (StackSourceFrameIndex)m_graph.NodeTypeIndexLimit;
            }

            NodeTypeIndex typeIndex = m_graph.GetNode(nodeIndex, m_nodeStorage).TypeIndex;
            return (StackSourceFrameIndex)typeIndex;
        }

        public override string GetFrameName(StackSourceFrameIndex frameIndex, bool verboseName)
        {
            NodeTypeIndex typeIndex = (NodeTypeIndex)frameIndex;

            // Orphan node support 
            if (typeIndex == m_graph.NodeTypeIndexLimit)
            {
                return "[not reachable from roots]";
            }

            string[] typeNames = verboseName ? m_verboseTypeNames : m_typeNames;
            if (typeNames == null)
            {
                m_typeNames = new string[(int)m_graph.NodeTypeIndexLimit];
                m_verboseTypeNames = new string[(int)m_graph.NodeTypeIndexLimit];
                typeNames = verboseName ? m_verboseTypeNames : m_typeNames;
            }

            if (typeNames[(int)typeIndex] == null)
            {
                var type = m_graph.GetType(typeIndex, m_typeStorage);
                var moduleName = type.ModuleName;

                var ret = type.Name;
                if (moduleName != null)
                {
                    if (verboseName)
                    {
                        int length = moduleName.Length - 4;
                        if ((length >= 0) && (moduleName[length] == '.'))
                        {
                            moduleName = moduleName.Substring(0, length);
                        }
                    }
                    else if (moduleName.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) >= 0)
                    {
                        moduleName = Path.GetFileNameWithoutExtension(moduleName);
                    }

                    if (moduleName.Length == 0)
                    {
                        moduleName = "?";
                    }

                    ret = moduleName + "!" + ShortenNameSpaces(type);
                }

                // TODO FIX NOW remove priority
                // ret +=  " " + m_typePriorities[(int)type.Index].ToString("f1");
                // TODO FIX NOW hack for CLRProfiler comparison 
                // ret = Regex.Replace(ret, @" *\[\]", "[]");
                // ret = Regex.Replace(ret, @"`\d+", "");
                typeNames[(int)typeIndex] = ret;
            }

            return typeNames[(int)typeIndex];
        }

        public override StackSourceSample GetSampleByIndex(StackSourceSampleIndex sampleIndex)
        {
            var nodeIndex = (NodeIndex)sampleIndex;
            var node = m_graph.GetNode(nodeIndex, m_nodeStorage);
            m_sampleStorage.Metric = node.Size;
            m_sampleStorage.SampleIndex = (StackSourceSampleIndex)node.Index;
            m_sampleStorage.StackIndex = (StackSourceCallStackIndex)node.Index;
            if (m_asMemoryGraph != null)
            {
                m_sampleStorage.TimeRelativeMSec = m_asMemoryGraph.GetAddress(node.Index);
            }
            else
            {
                m_sampleStorage.TimeRelativeMSec = 0;
            }

            return m_sampleStorage;
        }

        public override int CallStackIndexLimit
        {
            get { return (int)m_graph.NodeIndexLimit + 1; } // +1 is for orphans.  
        }
        public override int CallFrameIndexLimit
        {
            get { return (int)m_graph.NodeTypeIndexLimit + 1; }
        }
        public override int SampleIndexLimit { get { return (int)m_graph.NodeIndexLimit; } }

        public override bool IsGraphSource { get { return true; } }
        public override void GetReferences(StackSourceSampleIndex nodeIndex, RefDirection dir, Action<StackSourceSampleIndex> callback)
        {
            NodeIndex index = (NodeIndex)nodeIndex;
            // This is the special node that represents 'orphans'.   
            // TODO we simply give up for now.  This is OK because we only use GetRefs in places were we are AUGMENTING
            // the normal call tree (thus by returning nothing we just get the tree nodes).   You an imagine cases where 
            // we really do need to report the correct data. 
            if (index == m_graph.NodeIndexLimit)
            {
                return;
            }

            if (dir == RefDirection.From)
            {
                var node = m_graph.GetNode(index, AllocNodeStorage());
                for (var childIndex = node.GetFirstChildIndex(); childIndex != NodeIndex.Invalid; childIndex = node.GetNextChildIndex())
                {
                    callback((StackSourceSampleIndex)childIndex);
                }

                FreeNodeStorage(node);
            }
            else
            {
                Debug.Assert(dir == RefDirection.To);

                // Compute the references if we have not already done so.  
                var refGraph = RefGraph;
                if (m_refNodeStorage == null)
                {
                    m_refNodeStorage = m_refGraph.AllocNodeStorage();
                }

                // If this code blows up, it could be because m_refNodeStorage is being reused inappropriately (reentrant) 
                // Just make the storage a local var
                var node = refGraph.GetNode(index, m_refNodeStorage);
                for (var childIndex = node.GetFirstChildIndex(); childIndex != NodeIndex.Invalid; childIndex = node.GetNextChildIndex())
                {
                    callback((StackSourceSampleIndex)childIndex);
                }
            }
        }
        /// <summary>
        /// For graphs we use the address as a time, thus this returns the largest address in the memory heap.  
        /// </summary>
        public override double SampleTimeRelativeMSecLimit { get { return m_maxAddress; } }

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
                    MarkDecendentsIgnoringCycles((NodeIndex)i);
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
                        // The root index has no parent but is reachable from the root. 
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
        /// Add any unreachable nodes to the 'nodesToVisit'.   Note that we do this in a 'smart' way
        /// where we only add orphans that are not reachable from other orphans.   That way we get a 
        /// minimal set of orphan 'roots'.  
        /// </summary>
        /// <param name="nodesToVisit"></param>
        private void AddOrphansToQueue(Queue<NodeIndex> nodesToVisit)
        {
            for (int i = 0; i < (int)m_graph.NodeIndexLimit; i++)
            {
                if (m_parent[i] == NodeIndex.Invalid)
                {
                    MarkDecendentsIgnoringCycles((NodeIndex)i);
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
                        // The root index has no parent but is reachable from the root. 
                        if (nodeIndex != m_graph.RootIndex)
                        {
                            var node = m_graph.GetNode(nodeIndex, m_nodeStorage);
                            nodesToVisit.Enqueue(nodeIndex);
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
        /// Mark all decedents (but not nodeIndex itself) as being visited.    Any arcs that form
        /// cycles are ignored, so nodeIndex is guaranteed to NOT be marked.     
        /// </summary>
        private void MarkDecendentsIgnoringCycles(NodeIndex entryNodeIndex)
        {
            // This marks that there is a path from another orphan to this one (thus it is not a good root)
            const NodeIndex orphanVisitedMarker = NodeIndex.Invalid - 1;

            // To detect cycles we mark all nodes we not committed to (we are visiting, rather than visited)
            // If we detect this mark we understand it is a loop and ignore the arc.  
            const NodeIndex orphanVisitingMarker = NodeIndex.Invalid - 2;

            Stack<NodeIndex> workList = new Stack<NodeIndex>();
            workList.Push(entryNodeIndex);
            while (workList.Count > 0)
            {
                var nodeIndex = workList.Peek();
                switch (m_parent[(int)nodeIndex])
                {
                    case orphanVisitingMarker:
                        m_parent[(int)nodeIndex] = orphanVisitedMarker;
                        goto case orphanVisitedMarker;

                    case orphanVisitedMarker:
                        workList.Pop();
                        continue;

                    case NodeIndex.Invalid:
                        break;

                    default:
                        throw new InvalidOperationException();
                }

                m_parent[(int)nodeIndex] = orphanVisitingMarker;        // We are now visitING

                // Mark all nodes as being visited.  
                var node = m_graph.GetNode(nodeIndex, AllocNodeStorage());
                for (var childIndex = node.GetFirstChildIndex(); childIndex != NodeIndex.Invalid; childIndex = node.GetNextChildIndex())
                {
                    // Has this child not been seen at all?  If so mark it.  
                    // Skip it if we are visiting (it would form a cycle) or visited (or not an orphan)
                    if (m_parent[(int)childIndex] == NodeIndex.Invalid)
                    {
                        workList.Push(childIndex);
                    }
                }
                FreeNodeStorage(node);
            }

            // We set this above, and should not have changed it.  
            Debug.Assert(m_parent[(int)entryNodeIndex] == orphanVisitedMarker);
            // Now that we are finished, we reset the visiting bit.  
            m_parent[(int)entryNodeIndex] = NodeIndex.Invalid;
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

        private void SetTypePriorities(string priorityPats)
        {
            if (m_typePriorities == null)
            {
                m_typePriorities = new double[(int)m_graph.NodeTypeIndexLimit];
            }

            string[] priorityPatArray = priorityPats.Split(';');
            Regex[] priorityRegExArray = new Regex[priorityPatArray.Length];
            double[] priorityArray = new double[priorityPatArray.Length];
            for (int i = 0; i < priorityPatArray.Length; i++)
            {
                var m = Regex.Match(priorityPatArray[i], @"(.*)->(-?\d+.?\d*)");
                if (!m.Success)
                {
                    if (string.IsNullOrWhiteSpace(priorityPatArray[i]))
                    {
                        continue;
                    }

                    throw new ApplicationException("Priority pattern " + priorityPatArray[i] + " is not of the form Pat->Num.");
                }

                var dotNetRegEx = FilterStackSource.ToDotNetRegEx(m.Groups[1].Value.Trim());
                priorityRegExArray[i] = new Regex(dotNetRegEx, RegexOptions.IgnoreCase);
                priorityArray[i] = double.Parse(m.Groups[2].Value);
            }

            // Assign every type index a priority in m_typePriorities based on if they match a pattern.  
            NodeType typeStorage = m_graph.AllocTypeNodeStorage();
            for (NodeTypeIndex typeIdx = 0; typeIdx < m_graph.NodeTypeIndexLimit; typeIdx++)
            {
                var type = m_graph.GetType(typeIdx, typeStorage);

                var fullName = type.Name;
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

        /// <summary>
        /// Simulates having a 'default' name space.  This allows names to be significantly shorter. 
        /// </summary>
        private string ShortenNameSpaces(NodeType type)
        {
            var shortTypeName = type.Name;
            // TODO generalize this, it is also pretty expensive...
            for (; ; )
            {
                var systemIdx = shortTypeName.IndexOf("System.");
                if (0 <= systemIdx)
                {
                    if (string.Compare(shortTypeName, systemIdx + 7, "Collections.", 0, 12, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        if (string.Compare(shortTypeName, systemIdx + 19, "Generic.", 0, 8, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            shortTypeName = shortTypeName.Substring(0, systemIdx) + shortTypeName.Substring(systemIdx + 27);
                        }
                        else if (string.Compare(shortTypeName, systemIdx + 19, "Concurrent.", 0, 11, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            shortTypeName = shortTypeName.Substring(0, systemIdx) + shortTypeName.Substring(systemIdx + 30);
                        }
                        else if (string.Compare(shortTypeName, systemIdx + 19, "ObjectModel.", 0, 12, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            shortTypeName = shortTypeName.Substring(0, systemIdx) + shortTypeName.Substring(systemIdx + 31);
                        }
                        else
                        {
                            shortTypeName = shortTypeName.Substring(0, systemIdx) + shortTypeName.Substring(systemIdx + 19);
                        }
                    }
                    else if (string.Compare(shortTypeName, systemIdx + 7, "Threading.", 0, 10, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        shortTypeName = shortTypeName.Substring(0, systemIdx) + shortTypeName.Substring(systemIdx + 17);
                    }
                    else
                    {
                        shortTypeName = shortTypeName.Substring(0, systemIdx) + shortTypeName.Substring(systemIdx + 7);
                    }

                    continue;
                }
                return shortTypeName;
            }
        }

        private RefGraph m_refGraph;
        private RefNode m_refNodeStorage;
        private MemoryGraph m_asMemoryGraph;
        private Graph m_graph;
        private string[] m_typeNames;
        private string[] m_verboseTypeNames;
        private NodeIndex[] m_parent;               // We keep track of the parents of each node in our breadth-first scan. 
        private byte[] m_refCounts;                 // Used to implemented the 'RefCounts' property. 

        // We give each type a priority (using the m_priority Regular expressions) which guide the breadth-first scan. 
        private string m_priorityRegExs;
        private double[] m_typePriorities;
        private NodeType m_typeStorage;
        private Node m_nodeStorage;                 // Only for things that can't be reentrant
        private Node m_childStorage;
        private Node m_cachedNodeStorage;           // Used when it could be reentrant
        private StackSourceSample m_sampleStorage;
        private double[] m_countMultipliers;
        private Address m_maxAddress;               // The maximum memory address in the graph (needed by SampleTimeRelativeMSecLimit)     
        private TextWriter m_log;                   // processing messages 
        #endregion
    }

}
