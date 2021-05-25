// Copyright (c) Microsoft Corporation.  All rights reserved
// This file is best viewed using outline mode (Ctrl-M Ctrl-O)
// 
using Microsoft.Diagnostics.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;                        // For TextWriter.  
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tracing.Stacks
{
    /// <summary>
    /// SampleInfos of a set of stackSource by eventToStack.  This represents the entire call tree.   You create an empty one in using
    /// the default constructor and use 'AddSample' to add stackSource to it.   You traverse it by 
    /// </summary>
    public class CallTree
    {
        /// <summary>
        /// Creates an empty call tree, indicating the scaling policy of the metric.   You populate it by assigning a StackSOurce to the tree.  
        /// </summary>
        public CallTree(ScalingPolicyKind scalingPolicy)
        {
            m_root = new CallTreeNode("ROOT", StackSourceFrameIndex.Root, null, this);
            ScalingPolicy = scalingPolicy;
        }

        /// <summary>
        /// A CallTree is generated from a StackSource.  Setting the StackSource causes the tree to become populated.   
        /// </summary>
        public StackSource StackSource
        {
            get { return m_SampleInfo; }
            set
            {
                if (m_SampleInfo != null)
                {
                    m_root = new CallTreeNode("ROOT", StackSourceFrameIndex.Root, null, this);
                }

                m_SampleInfo = value;
                m_sumByID = null;
                if (TimeHistogramController != null)
                {
                    TimeHistogramController.InvalidateScale();
                }

                m_TreeForStack = new TreeCacheEntry[StackInfoCacheSize];
                m_frameIntern = new ConcurrentDictionary<string, StackSourceFrameIndex>(1, value.CallFrameIndexLimit);
                m_canonicalID = new StackSourceFrameIndex[value.CallFrameIndexLimit];

                // If it is a graph source, keep track of the mapping (so GetRefs works)
                if (m_SampleInfo.IsGraphSource)
                {
                    m_samplesToTreeNodes = new CallTreeNode[m_SampleInfo.SampleIndexLimit];
                }

                if (DisableParallelism)
                {
                    value.ForEach(AddSample);
                }
                else
                {
                    value.ParallelForEach(AddSample);
                }
                // And the basis for forming the % is total metric of stackSource.  
                PercentageBasis = Math.Abs(Root.InclusiveMetric);       // People get confused if this swaps. 

                // By default sort by inclusive Metric
                SortInclusiveMetricDecending();
                m_TreeForStack = null;
                m_frameIntern = null;
                m_canonicalID = null;
                m_calleeLookups = null;
            }
        }

        /// <summary>
        /// When calculating percentages, the PercentageBasis do we use as 100%.  By default we use the
        /// Inclusive time for the root, but that can be changed here.  
        /// </summary>
        public float PercentageBasis { get; set; }

        /// <summary>
        /// Returns the root node of the call tree.  
        /// </summary>
        public CallTreeNode Root { get { return m_root; } }

        /// <summary>
        /// An upper bound for the node indexes in the call tree.  (All indexes
        /// are strictly less than this number)   Thus ASSSUMING YOU DON'T ADD
        /// NEW NODES, an array of this size can be used to index the nodes (and 
        /// thus lookup nodes by index or to store additional information about a node).  
        /// </summary>
        public CallTreeNodeIndex NodeIndexLimit
        {
            get
            {
                return (CallTreeNodeIndex)m_nextNodeIndex;
            }
        }

        /// <summary>
        /// Get a CallerCalleeNode for the nodes in the call tree named 'nodeName'
        /// </summary>
        public CallerCalleeNode CallerCallee(string nodeName)
        {
            return new CallerCalleeNode(nodeName, this);
        }

        /// <summary>
        /// Returns a list of nodes that have statistics rolled up by treeNode by ID.  It is not
        /// sorted by anything in particular.   Note that ID is not quite the same thing as the 
        /// name.  You can have two nodes that have different IDs but the same Name.  These 
        /// will show up as two distinct entries in the resulting list.  
        /// </summary>
        public IEnumerable<CallTreeNodeBase> ByID { get { return GetSumByID().Values; } }
        /// <summary>
        /// Returns the list returned by the ByID property sorted by exclusive metric.  
        /// </summary>
        public List<CallTreeNodeBase> ByIDSortedExclusiveMetric()
        {
            var ret = new List<CallTreeNodeBase>(ByID);
            ret.Sort((x, y) => Math.Abs(y.ExclusiveMetric).CompareTo(Math.Abs(x.ExclusiveMetric)));
            return ret;
        }

        /// <summary>
        /// If there are any nodes that have strictly less than to 'minInclusiveMetric'
        /// then remove the node, placing its samples into its parent (thus the parent's
        /// exclusive metric goes up).  
        /// 
        /// If useWholeTraceMetric is true, nodes are only folded if their inclusive metric
        /// OVER THE WHOLE TRACE is less than 'minInclusiveMetric'.  If false, then a node
        /// is folded if THAT NODE has less than the 'minInclusiveMetric'  
        /// 
        /// Thus if 'useWholeTraceMetric' == false then after calling this routine no
        /// node will have less than minInclusiveMetric.  
        /// 
        /// </summary>
        public int FoldNodesUnder(float minInclusiveMetric, bool useWholeTraceMetric)
        {
            m_root.CheckClassInvarients();

            // If we filter by whole trace metric we need to cacluate the byID sums.  
            Dictionary<int, CallTreeNodeBase> sumByID = null;
            if (useWholeTraceMetric)
            {
                sumByID = GetSumByID();
            }

            int ret = m_root.FoldNodesUnder(minInclusiveMetric, sumByID);

            m_root.CheckClassInvarients();
            m_sumByID = null;   // Force a recalculation of the list by ID
            return ret;
        }

        /// <summary>
        /// Cause the children of each CallTreeNode in the CallTree to be sorted (accending) based on comparer
        /// </summary>
        public void Sort(IComparer<CallTreeNode> comparer)
        {
            m_root.SortAll(comparer);
        }
        /// <summary>
        /// Sorting by InclusiveMetric Decending is so common, provide a shortcut.  
        /// </summary>
        public void SortInclusiveMetricDecending()
        {
            var comparer = new FunctorComparer<CallTreeNode>(delegate (CallTreeNode x, CallTreeNode y)
            {
                int ret = Math.Abs(y.InclusiveMetric).CompareTo(Math.Abs(x.InclusiveMetric));
                if (ret != 0)
                {
                    return ret;
                }
                // Sort by first sample time (assending) if the counts are the same.  
                return x.FirstTimeRelativeMSec.CompareTo(y.FirstTimeRelativeMSec);
            });

            Sort(comparer);
        }

        /// <summary>
        /// When converting the InclusiveMetricByTime to a InclusiveMetricByTimeString you have to decide 
        /// how to scale the samples to the digits displayed in the string.  This enum indicates this policy
        /// </summary>
        public ScalingPolicyKind ScalingPolicy { get; private set; }
        /// <summary>
        /// The nodes in the calltree have histograms in time, all of these histograms share a controller that
        /// contains sharable information.   This propertly returns that TimeHistogramController
        /// </summary>
        public TimeHistogramController TimeHistogramController
        {
            get { return m_timeHistogram; }
            set
            {
                Debug.Assert(Root == null || Root.HasChildren == false);
                m_timeHistogram = value;
                if (value != null)
                {
                    Root.m_inclusiveMetricByTime = new Histogram(value);
                }
            }
        }
        /// <summary>
        /// The nodes in the calltree have histograms indexed by scenario (which is user defiend), 
        /// all of these histograms share a controller that contains sharable information.   
        /// This propertly returns that ScenarioHistogramController
        /// </summary>
        public ScenarioHistogramController ScenarioHistogram
        {
            get { return m_scenarioHistogram; }
            set
            {
                Debug.Assert(Root == null || Root.HasChildren == false);
                m_scenarioHistogram = value;
                if (value != null)
                {
                    Root.m_inclusiveMetricByScenario = new Histogram(value);
                }
            }
        }

        /// <summary>
        /// Turns off logic for computing call trees in parallel.   Safer but slower.  
        /// </summary>
        /// <remarks>
        /// <para>This is off by default following indications of race conditions.</para>
        /// </remarks>
        public bool DisableParallelism { get; set; } = true;

        /// <summary>
        /// Break all links in the call tree to free as much memory as possible.   
        /// </summary>
        public virtual void FreeMemory()
        {
            if (m_sumByID != null)
            {
                foreach (var node in m_sumByID.Values)
                {
                    node.FreeMemory();
                }

                m_sumByID = null;
            }
            m_root.FreeMemory();
            m_root = null;
            m_SampleInfo = null;
        }

        /// <summary>
        /// Write an XML representtaion of the CallTree to 'writer'
        /// </summary>
        public void ToXml(TextWriter writer)
        {
            writer.WriteLine("<CallTree TotalMetric=\"{0:f1}\">", Root.InclusiveMetric);
            Root.ToXml(writer, "");
            writer.WriteLine("</CallTree>");
        }
        /// <summary>
        /// An XML representtaion of the CallTree (for debugging)
        /// </summary>
        public override string ToString()
        {
            StringWriter sw = new StringWriter();
            ToXml(sw);
            return sw.ToString();
        }

        #region private
        private CallTree(CallTreeNode root)
        {
            m_root = root;
        }

        // This keeps track of stacks that I have used in the past
        private const int StackInfoCacheSize = 128;          // Must be a power of 2
        private TreeCacheEntry[] m_TreeForStack;

        // Maps frame IDs to their canonical one (we group all frame IDs)
        internal StackSourceFrameIndex[] m_canonicalID;        // Maps frame IDs to their canonical one
        internal ConcurrentDictionary<string, StackSourceFrameIndex> m_frameIntern;        // Maps strings to their canonical frame ID
        // For nodes with many children, store a map 
        internal ConcurrentDictionary<CallTreeNode, ConcurrentDictionary<StackSourceFrameIndex, CallTreeNode>> m_calleeLookups;

        private struct TreeCacheEntry
        {
            public volatile StackSourceCallStackIndex StackIndex;
            public CallTreeNode Tree;
        }

        private CallTreeNode FindTreeNode(StackSourceCallStackIndex stack, RecursionGuard recursionGuard = default(RecursionGuard))
        {
            if (recursionGuard.RequiresNewThread)
            {
                // Avoid capturing method parameters for use in the lambda to reduce fast-path allocation costs
                var capturedThis = this;
                var capturedStack = stack;
                var capturedRecursionGuard = recursionGuard;
                Task<CallTreeNode> result = Task.Factory.StartNew(
                    () => capturedThis.FindTreeNode(capturedStack, capturedRecursionGuard.ResetOnNewThread),
                    TaskCreationOptions.LongRunning);

                return result.GetAwaiter().GetResult();
            }

            // Is it in our cache?
            int hash = (((int)stack) & (StackInfoCacheSize - 1));
            var entry = m_TreeForStack[hash];
            if (entry.StackIndex == stack && entry.Tree != null)
            {
                return entry.Tree;
            }

            if (stack == StackSourceCallStackIndex.Invalid)
            {
                return m_root;
            }

            var callerIndex = m_SampleInfo.GetCallerIndex(stack);
            var callerNode = FindTreeNode(callerIndex, recursionGuard.Recurse);

            var frameIndex = m_SampleInfo.GetFrameIndex(stack);
            var retNode = callerNode.FindCallee(frameIndex);

            // Update the cache.
            m_TreeForStack[hash].Tree = null;              // Clear the entry to avoid races if run on multiple threads. 
            m_TreeForStack[hash].StackIndex = stack;
            m_TreeForStack[hash].Tree = retNode;

            return retNode;
        }

        private void AddSample(StackSourceSample sample)
        {
            var callTreeNode = FindTreeNode(sample.StackIndex);
            if (m_samplesToTreeNodes != null)
            {
                m_samplesToTreeNodes[(int)sample.SampleIndex] = callTreeNode;
            }

            // TODO see can be more concurrent than this.    
            lock (this)
            {
                AddSampleToTreeNode(callTreeNode, sample);
            }
        }

        private void AddSampleToTreeNode(CallTreeNode treeNode, StackSourceSample sample)
        {
            // Add the sample to treeNode.
            treeNode.m_exclusiveCount += sample.Count;
            treeNode.m_exclusiveMetric += sample.Metric;
            if (sample.SampleIndex != StackSourceSampleIndex.Invalid)
            {
                treeNode.m_samples.Add(sample.SampleIndex);
            }

            if (sample.StackIndex != StackSourceCallStackIndex.Invalid)
            {
                // Increment the folded count
                var numFoldedNodes = m_SampleInfo.GetNumberOfFoldedFrames(sample.StackIndex);
                if (numFoldedNodes > 0)
                {
                    treeNode.m_exclusiveFoldedCount += sample.Count;
                    treeNode.m_exclusiveFoldedMetric += sample.Metric;
                }
            }

            var sampleEndTime = sample.TimeRelativeMSec;
            if (ScalingPolicy == ScalingPolicyKind.TimeMetric)
            {
                // The sample ends at the end of its metric, however we trucate at the end of the range.  

                // The Math.Abs is a bit of a hack.  The problem is that that sample does not
                // represent time for a DIFF (because we negated it) but I rely on the fact 
                // that we only negate it so I can undo it 
                sampleEndTime += Math.Abs(sample.Metric);
                if (TimeHistogramController != null && sampleEndTime > TimeHistogramController.End)
                {
                    sampleEndTime = TimeHistogramController.End;
                }
            }

            // And update all the inclusive times up the tree to the root (including this node)
            while (treeNode != null)
            {
                treeNode.m_inclusiveCount += sample.Count;
                treeNode.m_inclusiveMetric += sample.Metric;

                if (treeNode.InclusiveMetricByTime != null)
                {
                    treeNode.InclusiveMetricByTime.AddSample(sample);
                }

                if (treeNode.InclusiveMetricByScenario != null)
                {
                    treeNode.InclusiveMetricByScenario.AddSample(sample);
                }

                if (sample.TimeRelativeMSec < treeNode.m_firstTimeRelativeMSec)
                {
                    treeNode.m_firstTimeRelativeMSec = sample.TimeRelativeMSec;
                }

                if (sampleEndTime > treeNode.m_lastTimeRelativeMSec)
                {
                    treeNode.m_lastTimeRelativeMSec = sampleEndTime;
                }

                Debug.Assert(treeNode.m_firstTimeRelativeMSec <= treeNode.m_lastTimeRelativeMSec);

                treeNode = treeNode.Caller;
            }
        }

        internal Dictionary<int, CallTreeNodeBase> GetSumByID()
        {
            if (m_sumByID == null)
            {
                m_sumByID = new Dictionary<int, CallTreeNodeBase>();
                var callersOnStack = new Dictionary<int, CallTreeNodeBase>();       // This is just a set
                AccumulateSumByID(m_root, callersOnStack);
            }
            return m_sumByID;
        }
        /// <summary>
        /// Traverse the subtree of 'treeNode' into the m_sumByID dictionary.   We don't want to
        /// double-count inclusive times, so we have to keep track of all callers currently on the
        /// stack and we only add inclusive times for nodes that are not already on the stack.  
        /// </summary>
        private void AccumulateSumByID(CallTreeNode treeNode, Dictionary<int, CallTreeNodeBase> callersOnStack, RecursionGuard recursionGuard = default(RecursionGuard))
        {
            if (recursionGuard.RequiresNewThread)
            {
                // Avoid capturing method parameters for use in the lambda to reduce fast-path allocation costs
                var capturedThis = this;
                var capturedTreeNode = treeNode;
                var capturedCallersOnStack = callersOnStack;
                var capturedRecursionGuard = recursionGuard;
                Task result = Task.Factory.StartNew(
                    () => capturedThis.AccumulateSumByID(capturedTreeNode, capturedCallersOnStack, capturedRecursionGuard.ResetOnNewThread),
                    TaskCreationOptions.LongRunning);

                result.GetAwaiter().GetResult();
                return;
            }

            CallTreeNodeBase byIDNode;
            if (!m_sumByID.TryGetValue((int)treeNode.m_id, out byIDNode))
            {
                byIDNode = new CallTreeNodeBase(treeNode.Name, treeNode.m_id, this);
                byIDNode.m_isByIdNode = true;
                m_sumByID.Add((int)treeNode.m_id, byIDNode);
            }

            bool newOnStack = !callersOnStack.ContainsKey((int)treeNode.m_id);
            // Add in the tree treeNode's contribution
            byIDNode.CombineByIdSamples(treeNode, newOnStack);

            // TODO FIX NOW
            // Debug.Assert(treeNode.m_nextSameId == null);
            treeNode.m_nextSameId = byIDNode.m_nextSameId;
            byIDNode.m_nextSameId = treeNode;
            if (treeNode.Callees != null)
            {
                if (newOnStack)
                {
                    callersOnStack.Add((int)treeNode.m_id, null);
                }

                foreach (var child in treeNode.m_callees)
                {
                    AccumulateSumByID(child, callersOnStack, recursionGuard.Recurse);
                }

                if (newOnStack)
                {
                    callersOnStack.Remove((int)treeNode.m_id);
                }
            }
        }

        internal StackSource m_SampleInfo;
        private CallTreeNode m_root;
        private TimeHistogramController m_timeHistogram;
        private ScenarioHistogramController m_scenarioHistogram;
        private Dictionary<int, CallTreeNodeBase> m_sumByID;              // These nodes hold the roll up by Frame ID (name)
        internal CallTreeNode[] m_samplesToTreeNodes;             // Used for the graph support.  Maps a sample index to a the tree node that includes it. 
        internal int m_nextNodeIndex;                             // Used to give out unique indexes for CallTreeNodes in this callTree. 
        #endregion
    }

    /// <summary>
    /// ScalingPolicyKind represents the desired way to scale the metric in the samples.  
    /// </summary>
    public enum ScalingPolicyKind
    {
        /// <summary>
        /// This is the default.  In this policy, 100% is chosen so that the histogram is scaled as best it can.   
        /// </summary>
        ScaleToData,
        /// <summary>
        /// It assumes that the metric represents time 
        /// </summary>
        TimeMetric
    }

    /// <summary>
    /// Represents a unique ID for a node in a call tree.  Can be used to look up a call tree node easily.  
    /// It is a dense value (from 0 up to a maximum).  
    /// </summary>
    public enum CallTreeNodeIndex
    {
        /// <summary>
        /// An Invalid Node Index.
        /// </summary>
        Invalid = -1
    };

    /// <summary>
    /// A  CallTreeNodeBase is the inforation in a CallTreeNode without parent or child relationships.  
    /// ByName nodes and Caller-Callee nodes need this because they either don't have or need different 
    /// parent-child relationships. 
    /// </summary>
    public class CallTreeNodeBase
    {
        /// <summary>
        /// Returns a unique small, dense number (suitable for looking up in an array) that represents 
        /// this call tree node (unlike the ID, which more like the name of the frame of the node), so you
        /// can have many nodes with the same name, but only one with the same index.    See CallTree.GetNodeIndexLimit.
        /// </summary>
        public CallTreeNodeIndex Index { get { return m_index; } }

        /// <summary>
        /// Create a CallTreeNodeBase (a CallTreeNode without children) which is a copy of another one.  
        /// </summary>
        public CallTreeNodeBase(CallTreeNodeBase template)
        {
            m_id = template.m_id;
            m_name = template.m_name;
            m_callTree = template.m_callTree;
            m_inclusiveMetric = template.m_inclusiveMetric;
            m_inclusiveCount = template.m_inclusiveCount;
            m_exclusiveMetric = template.m_exclusiveMetric;
            m_exclusiveCount = template.m_exclusiveCount;
            m_exclusiveFoldedMetric = template.m_exclusiveFoldedMetric;
            m_exclusiveFoldedCount = template.m_exclusiveFoldedCount;
            m_firstTimeRelativeMSec = template.m_firstTimeRelativeMSec;
            m_lastTimeRelativeMSec = template.m_lastTimeRelativeMSec;
            // m_samples left out intentionally
            // m_nextSameId
            // m_isByIdNode
            if (template.m_inclusiveMetricByTime != null)
            {
                m_inclusiveMetricByTime = template.m_inclusiveMetricByTime.Clone();
            }

            if (template.m_inclusiveMetricByScenario != null)
            {
                m_inclusiveMetricByScenario = template.m_inclusiveMetricByScenario.Clone();
            }
        }

        /// <summary>
        /// The Frame name that this tree node represents.   
        /// </summary>
        public string Name { get { return m_name; } }
        /// <summary>
        /// Currently the same as Name, but could contain additional info.  
        /// Suitable for display but not for programmatic comparison.  
        /// </summary>
        public string DisplayName
        {
            get
            {
                if (m_isGraphNode)
                {
                    return Name + " {MinDepth " + m_minDepth + "}";
                }

                return Name;
            }
        }
        /// <summary>
        /// The ID represents a most fine grained uniqueness associated with this node.   It can represent
        /// a method, but for sources that support 'goto source' functionality these IDs actually represent
        /// particular lines (or more precisely program counter locations), within the method.    Thus it is 
        /// very likely that there are call tree nodes that have the same name but different IDs.  
        /// 
        /// This can be StackSourceFrameIndex.Invalid for Caller-callee nodes (which have names, but no useful ID) 
        ///
        /// If ID != Invalid, and the IDs are the same then the names are guaranteed to be the same.  
        /// </summary>
        public StackSourceFrameIndex ID { get { return m_id; } }
        /// <summary>
        /// The sum of the metric of all samples that are in this node or any child of this node (recursively)
        /// </summary>
        public float InclusiveMetric { get { return m_inclusiveMetric; } }
        /// <summary>
        /// The average metric of all samples that are in this node or any child of this node (recursively).
        /// This is simply InclusiveMetric / InclusiveCount.
        /// </summary>
        public float AverageInclusiveMetric { get { return m_inclusiveMetric / m_inclusiveCount; } }
        /// <summary>
        /// The sum of the metric of all samples that are in this node 
        /// </summary>
        public float ExclusiveMetric { get { return m_exclusiveMetric; } }
        /// <summary>
        /// The sum of the metric of all samples in this node that are there because they were folded (inlined).   It is always less than or equal to ExclusiveMetric.  
        /// </summary>
        public float ExclusiveFoldedMetric { get { return m_exclusiveFoldedMetric; } }
        /// <summary>
        /// The sum of the count of all samples that are in this node or any child of this node (recursively)
        /// </summary>
        public float InclusiveCount { get { return m_inclusiveCount; } }
        /// <summary>
        /// The sum of the count of all samples that are in this node 
        /// </summary>
        public float ExclusiveCount { get { return m_exclusiveCount; } }
        /// <summary>
        /// The sum of the count of all samples in this node that are there because they were folded (inlined).   It is always less than or equal to ExclusiveCount.  
        /// </summary>
        public float ExclusiveFoldedCount { get { return m_exclusiveFoldedCount; } }

        /// <summary>
        /// The inclusive metric, normalized to the total metric for the entire tree.  
        /// </summary>
        public float InclusiveMetricPercent { get { return m_inclusiveMetric * 100 / m_callTree.PercentageBasis; } }
        /// <summary>
        /// The exclusive metric, normalized to the total metric for the entire tree.  
        /// </summary>
        public float ExclusiveMetricPercent { get { return m_exclusiveMetric * 100 / m_callTree.PercentageBasis; } }
        /// <summary>
        /// The exclusive folded metric, normalized to the total metric for the entire tree.  
        /// </summary>
        public float ExclusiveFoldedMetricPercent { get { return m_exclusiveFoldedMetric * 100 / m_callTree.PercentageBasis; } }

        /// <summary>
        /// The time of the first sample for this node or any of its children (recursively)
        /// </summary>
        public double FirstTimeRelativeMSec { get { return m_firstTimeRelativeMSec; } }

        /// <summary>
        /// The time of the first sample for this node or any of its children (recursively)
        /// </summary>
        [Obsolete("Use FirstTimeRelativeMSec")]
        public double FirstTimeRelMSec { get { return m_firstTimeRelativeMSec; } }

        /// <summary>
        /// The time of the last sample for this node or any of its children (recursively)
        /// </summary>
        public double LastTimeRelativeMSec { get { return m_lastTimeRelativeMSec; } }
        /// <summary>
        /// The time of the last sample for this node or any of its children (recursively)
        /// </summary>
        [Obsolete("Use LastTimeRelativeMSec")]
        public double LastTimeRelMSec { get { return m_lastTimeRelativeMSec; } }

        /// <summary>
        /// The difference between the first and last sample (in MSec).  
        /// </summary>
        public double DurationMSec { get { return m_lastTimeRelativeMSec - m_firstTimeRelativeMSec; } }
        /// <summary>
        /// The call tree that contains this node.  
        /// </summary>
        public CallTree CallTree { get { return m_callTree; } }

        /// <summary>
        /// Returns the histogram that groups of samples associated with this node or any of its children by time buckets
        /// </summary>
        public Histogram InclusiveMetricByTime { get { return m_inclusiveMetricByTime; } }
        /// <summary>
        /// Returns a string that represents the InclusiveMetricByTime Histogram by using character for every bucket (like PerfView)
        /// </summary>
        public string InclusiveMetricByTimeString
        {
            get
            {
                if (m_inclusiveMetricByTime != null)
                {
                    return m_inclusiveMetricByTime.ToString();
                }
                else
                {
                    return null;
                }
            }
            set { }
        }
        /// <summary>
        /// Returns the histogram that groups of samples associated with this node or any of its children by scenario buckets
        /// </summary>
        public Histogram InclusiveMetricByScenario { get { return m_inclusiveMetricByScenario; } }
        /// <summary>
        /// Returns a string that represents the InclusiveMetricByScenario Histogram by using character for every bucket (like PerfView)
        /// </summary>
        public string InclusiveMetricByScenarioString
        {
            get
            {
                if (m_inclusiveMetricByScenario != null)
                {
                    return m_inclusiveMetricByScenario.ToString();
                }
                else
                {
                    return null;
                }
            }
            set { }
        }

        /// <summary>
        /// Returns all the original stack samples in this node.  If exclusive==true then just he
        /// sample exclusively in this node are returned, otherwise it is the inclusive samples.   
        /// 
        /// If the original stack source that was used to create this CodeTreeNode was a FilterStackSource
        /// then that filtering is removed in the returned Samples.  
        /// 
        /// Returns the total number of samples (the number of times 'callback' is called)
        /// 
        /// If the callback returns false, the iteration over samples stops. 
        /// </summary>
        public virtual int GetSamples(bool exclusive, Func<StackSourceSampleIndex, bool> callback)
        {
            // Graph nodes don't care about trees, they just return the samples 'directly'.   They don't have a notion of 'inclusive'  
            if (m_isGraphNode)
            {
                return GetSamplesForTreeNode((CallTreeNode)this, true, callback, StackSourceFrameIndex.Invalid);
            }

            int count = 0;
            var excludeChildrenID = GetExcludeChildID();

            GetTrees(delegate (CallTreeNode node)
            {
                count += GetSamplesForTreeNode(node, exclusive, callback, excludeChildrenID);
            });
#if DEBUG
            if (exclusive)
            {
                if (count != ExclusiveCount)
                {
                    // Exclusive counts for caller nodes are always 0
                    var agg = this as AggregateCallTreeNode;
                    Debug.Assert(agg != null && !agg.IsCalleeTree && ExclusiveCount == 0);
                }
            }
            else
                Debug.Assert(count == InclusiveCount);
#endif
            return count;
        }
        /// <summary>
        /// While 'GetSamples' can return all the samples in the tree, this is a relatively
        /// inefficient way of representing the samples.   Instead you can return a list of
        /// trees whose samples represent all the samples.   This is what GetTrees does.
        /// It calls 'callback' on a set of trees that taken as a whole have all the samples
        /// in 'node'.  
        /// 
        /// Note you ave to be careful when using this for inclusive summation of byname nodes because 
        /// you will get trees that 'overlap' (bname nodes might refer into the 'middle' of another
        /// call tree).   This can be avoided pretty easily by simply stopping inclusive traversal 
        /// whenever a tree node with that ID occurs (see GetSamples for an example). 
        /// </summary>
        public virtual void GetTrees(Action<CallTreeNode> callback)
        {
            // if we are a treeNode 
            var asTreeNode = this as CallTreeNode;
            if (asTreeNode != null)
            {
                callback(asTreeNode);
                return;
            };
            if (!m_isByIdNode)
            {
                Debug.Assert(false, "Error: unexpected CallTreeNodeBase");
                return;
            }
            for (var curNode = m_nextSameId; curNode != null; curNode = curNode.m_nextSameId)
            {
                Debug.Assert(curNode is CallTreeNode);
                callback(curNode as CallTreeNode);
            }
        }

        /// <summary>
        /// Returns a string representing the set of XML attributes that can be added to another XML element.  
        /// </summary>
        public void ToXmlAttribs(TextWriter writer)
        {
            writer.Write(" Name=\"{0}\"", XmlUtilities.XmlEscape(Name ?? "", false));
            writer.Write(" ID=\"{0}\"", (int)m_id);
            writer.Write(" InclusiveMetric=\"{0}\"", InclusiveMetric);
            writer.Write(" ExclusiveMetric=\"{0}\"", ExclusiveMetric);
            writer.Write(" InclusiveCount=\"{0}\"", InclusiveCount);
            writer.Write(" ExclusiveCount=\"{0}\"", ExclusiveCount);
            writer.Write(" FirstTimeRelativeMSec=\"{0:f4}\"", FirstTimeRelativeMSec);
            writer.Write(" LastTimeRelativeMSec=\"{0:f4}\"", LastTimeRelativeMSec);
        }

        /// <summary>
        /// An XML representation of the CallTreeNodeBase (for debugging)
        /// </summary>
        public override string ToString()
        {
            StringWriter sw = new StringWriter();
            sw.Write("<Node");
            ToXmlAttribs(sw);
            sw.Write("/>");
            return sw.ToString();
        }

        /// <summary>
        /// The GUI sadly holds on to Call things in the model in its cache, and call tree nodes have linkes to whole
        /// call tree.  To avoid the GUI cache from holding on to the ENTIRE MODEL, we neuter the nodes when we are
        /// done with them so that even if they are pointed to by the GUI cache it does not hold onto most of the 
        /// (dead) model.    FreeMemory does this neutering.  
        /// </summary>
        public void FreeMemory()
        {
            var nodesToFree = new Stack<CallTreeNodeBase>();
            nodesToFree.Push(this);
            while (nodesToFree.Count > 0)
            {
                nodesToFree.Pop().FreeMemory(nodesToFree);
            }
        }

        protected virtual void FreeMemory(Stack<CallTreeNodeBase> nodesToFree)
        {
            m_samples.Clear();
            m_nextSameId = null;
            m_name = null;
            m_callTree = null;
            m_inclusiveMetricByTime = null;
            m_inclusiveMetricByScenario = null;
        }

        #region private
        internal CallTreeNodeBase(string name, StackSourceFrameIndex id, CallTree container)
        {
            // We use {} to express things that are not logically part of the name, so strip any 'real' {}
            // because it confuses the upper level logic TODO: this is kind of a hack.
            var idx = name.IndexOf('{');
            if (0 < idx)
            {
                name = name.Substring(0, idx);
            }

            m_name = name;
            m_callTree = container;
            m_id = id;
            m_firstTimeRelativeMSec = Double.PositiveInfinity;
            m_lastTimeRelativeMSec = Double.NegativeInfinity;
            if (container.TimeHistogramController != null)
            {
                m_inclusiveMetricByTime = new Histogram(container.TimeHistogramController);
            }

            if (container.ScenarioHistogram != null)
            {
                m_inclusiveMetricByScenario = new Histogram(container.ScenarioHistogram);
            }

            m_index = (CallTreeNodeIndex)container.m_nextNodeIndex++;
        }

        /// <summary>
        /// Combines the 'this' node with 'otherNode'.   If 'newOnStack' is true, then the inclusive
        /// metrics are also updated.  
        /// 
        /// Note that I DON'T accumulate other.m_samples into this.m_samples.   This is because we want to share
        /// samples as much a possible.  Thus nodes remember their samples by pointing at other call trees
        /// and you fetch the samples by an inclusive walk of the tree.  
        /// </summary>
        internal void CombineByIdSamples(CallTreeNodeBase other, bool addInclusive, double weight = 1.0, bool addExclusive = true)
        {
            if (addInclusive)
            {
                m_inclusiveMetric += (float)(other.m_inclusiveMetric * weight);
                m_inclusiveCount += (float)(other.m_inclusiveCount * weight);
                if (m_inclusiveMetricByTime != null && other.m_inclusiveMetricByTime != null)
                {
                    m_inclusiveMetricByTime.AddScaled(other.m_inclusiveMetricByTime, weight);
                }

                if (m_inclusiveMetricByScenario != null && other.m_inclusiveMetricByScenario != null)
                {
                    m_inclusiveMetricByScenario.AddScaled(other.m_inclusiveMetricByScenario, weight);
                }
            }

            if (addExclusive)
            {
                m_exclusiveMetric += (float)(other.m_exclusiveMetric * weight);
                m_exclusiveCount += (float)(other.m_exclusiveCount * weight);
                m_exclusiveFoldedMetric += (float)(other.m_exclusiveFoldedMetric * weight);
                m_exclusiveFoldedCount += (float)(other.m_exclusiveFoldedCount * weight);
            }

            if (other.m_firstTimeRelativeMSec < m_firstTimeRelativeMSec)
            {
                m_firstTimeRelativeMSec = other.m_firstTimeRelativeMSec;
            }

            if (other.m_lastTimeRelativeMSec > m_lastTimeRelativeMSec)
            {
                m_lastTimeRelativeMSec = other.m_lastTimeRelativeMSec;
            }

            Debug.Assert(m_firstTimeRelativeMSec <= m_lastTimeRelativeMSec || double.IsInfinity(m_firstTimeRelativeMSec));
        }

        /// <summary>
        /// To avoid double-counting for byname nodes, with we can be told to exclude any children with a particular ID 
        /// (the ID of the ByName node itself) if are doing the inclusive case.   The goal is to count every reachable
        /// tree exactly once.  We do this by conceptually 'marking' each node with ID at the top level (when they are 
        /// enumerated as children of the Byname node), and thus any node with that excludeChildrenWithID is conceptually
        /// marked if you encounter it as a child in the tree itself (so you should exclude it).  The result is that 
        /// every node is visited exactly once (without the expense of having a 'visited' bit).  
        /// </summary>
        protected static int GetSamplesForTreeNode(CallTreeNode curNode, bool exclusive, Func<StackSourceSampleIndex, bool> callback, StackSourceFrameIndex excludeChildrenWithID)
        {
            // Include any nodes from myself. 
            int count = 0;
            for (int i = 0; i < curNode.m_samples.Count; i++)
            {
                count++;
                if (!callback(curNode.m_samples[i]))
                {
                    return count;
                }
            }

            if (!exclusive)
            {
                if (curNode.Callees != null)
                {
                    foreach (var callee in curNode.Callees)
                    {
                        Debug.Assert(callee.ID != StackSourceFrameIndex.Invalid);
                        // 
                        if (callee.ID != excludeChildrenWithID)
                        {
                            count += GetSamplesForTreeNode(callee, exclusive, callback, excludeChildrenWithID);
                        }
                    }
                }
            }
#if DEBUG
            // The number of samples does not equal the InclusiveCount on intermediate nodes if we have 
            // recursion because we are excluding some of the samples to avoid double counting
            if (exclusive)
                Debug.Assert(count == curNode.ExclusiveCount);
            else
                Debug.Assert(count == curNode.InclusiveCount || excludeChildrenWithID != StackSourceFrameIndex.Invalid);
#endif
            return count;
        }

        internal /*protected*/ virtual StackSourceFrameIndex GetExcludeChildID()
        {
            var excludeChildrenID = StackSourceFrameIndex.Invalid;
            if (m_isByIdNode)
            {
                excludeChildrenID = ID;
            }

            return excludeChildrenID;
        }

        internal StackSourceFrameIndex m_id;
        internal string m_name;
        internal CallTree m_callTree;                                   // The call tree this node belongs to. 
        internal float m_inclusiveMetric;
        internal float m_inclusiveCount;
        internal float m_exclusiveMetric;
        internal float m_exclusiveCount;
        internal float m_exclusiveFoldedMetric;
        internal float m_exclusiveFoldedCount;
        internal double m_firstTimeRelativeMSec;
        internal double m_lastTimeRelativeMSec;
        private CallTreeNodeIndex m_index;

        internal GrowableArray<StackSourceSampleIndex> m_samples;       // The actual samples.  
        internal Histogram m_inclusiveMetricByTime;                     // histogram by time. Can be null if no histogram is needed.
        internal Histogram m_inclusiveMetricByScenario;                 // Histogram by scenario. Can be null if we're only dealing with one scenario.
        internal CallTreeNodeBase m_nextSameId;                         // We keep a linked list of tree nodes with the same ID (name)
        internal bool m_isByIdNode;                                     // Is this a node representing a rollup by ID (name)?  

        // TODO FIX NOW should this be a separate sub-type?
        internal bool m_isGraphNode;                                    // Children represent memory graph references
        internal bool m_isCallerTree;
        internal int m_minDepth;                                        // Only used by Graph nodes, it is the minimum of the depth of all samples
        #endregion
    }

    /// <summary>
    /// Represents a single treeNode in a CallTree 
    /// 
    /// Each node keeps all the sample with the same path to the root.  
    /// Each node also remembers its parent (caller) and children (callees).
    /// The nodes also keeps the IDs of all its samples (so no information
    /// is lost, just sorted by stack).   You get at this through the
    /// CallTreeNodeBase.GetSamples method.  
    /// </summary>
    public class CallTreeNode : CallTreeNodeBase
    {
        /// <summary>
        /// The caller (parent) of this node
        /// </summary>
        public CallTreeNode Caller { get { return m_caller; } }
        /// <summary>
        /// The nodes this node calls (its children). 
        /// </summary>
        public IList<CallTreeNode> Callees
        {
            get
            {
                if (m_callees == null)
                {
                    m_callees = GetCallees();
                }
                return m_callees;
            }
        }
        /// <summary>
        /// Returns true if Callees is empty.  
        /// </summary>
        public bool IsLeaf { get { return Callees == null; } }

        /// <summary>
        /// AllCallees is an extension of CallTreesNodes to support graphs (e.g. memory heaps).   
        /// It always starts with the 'normal' Callees, however in addition if we are
        /// displaying a Graph, it will also children that were 'pruned' when the graph was 
        /// transformed into a tree.  (by using StackSource.GetRefs).   
        /// </summary>
        public IList<CallTreeNode> AllCallees
        {
            get
            {
                if (m_displayCallees == null)
                {
                    m_displayCallees = Callees;
                    if (CallTree.StackSource.IsGraphSource)
                    {
                        m_displayCallees = GetAllChildren();
                    }
                }
                return m_displayCallees;
            }
        }
        /// <summary>
        /// Returns true if AllCallees is non-empty.  
        /// </summary>
        public virtual bool HasChildren
        {
            get
            {
                // We try to be very lazy since HasChildren is called just to determine a check box is available.  
                var callees = Callees;
                if (callees != null && callees.Count != 0)
                {
                    return true;
                }

                var stackSource = CallTree.StackSource;
                if (stackSource == null)
                {
                    return false;
                }

                if (!stackSource.IsGraphSource)
                {
                    return false;
                }

                callees = AllCallees;
                return (callees != null && callees.Count != 0);
            }
        }

        /// <summary>
        /// Returns true if the call trees came from a graph (thus AllCallees may be strictly larger than Callees)
        /// </summary>
        public bool IsGraphNode { get { return m_isGraphNode; } }

        /// <summary>
        /// Writes an XML representation of the call tree Node  it 'writer'
        /// </summary>
        public void ToXml(TextWriter writer, string indent = "")
        {

            writer.Write("{0}<CallTree ", indent);
            ToXmlAttribs(writer);
            writer.WriteLine(">");

            var childIndent = indent + " ";
            if (Callees != null)
            {
                foreach (CallTreeNode callee in m_callees)
                {
                    callee.ToXml(writer, childIndent);
                }
            }
            writer.WriteLine("{0}</CallTree>", indent);
        }
        /// <summary>
        /// Returns an XML representation of the call tree Node (for debugging);
        /// </summary>
        public override string ToString()
        {
            StringWriter sw = new StringWriter();
            ToXml(sw, "");
            return sw.ToString();
        }

        /// <summary>
        /// Adds up the counts of all nodes called 'BROKEN' nodes in a particular tree node
        /// 
        /// This is a utility function.  
        /// </summary>
        public float GetBrokenStackCount()
        {
            return GetBrokenStackCount(4);
        }

        /// <summary>
        /// Creates a string that has spaces | and + signs that represent the indentation level 
        /// for the tree node.  (Called from XAML)
        /// </summary>
        public string IndentString(bool displayPrimaryOnly)
        {
            if (m_indentString == null || m_indentStringForPrimary != displayPrimaryOnly)
            {
                var depth = Depth();
                var chars = new char[depth];
                var i = depth - 1;
                if (0 <= i)
                {
                    chars[i] = '+';
                    var ancestor = Caller;
                    --i;
                    while (i >= 0)
                    {
                        chars[i] = ancestor.IsLastChild(displayPrimaryOnly) ? ' ' : '|';
                        ancestor = ancestor.Caller;
                        --i;
                    }
                }

                m_indentString = new string(chars);
                m_indentStringForPrimary = displayPrimaryOnly;
            }
            return m_indentString;
        }

        #region overrides

        /// <summary>
        /// Implements CallTreeNodesBase interface
        /// </summary>
        protected override void FreeMemory(Stack<CallTreeNodeBase> nodesToFree)
        {
            if (m_callees != null)
            {
                foreach (var node in m_callees)
                {
                    nodesToFree.Push(node);
                }

                m_callees.Clear();
            }
            m_caller = null;
            base.FreeMemory(nodesToFree);
        }
        #endregion
        #region private
        internal CallTreeNode(string name, StackSourceFrameIndex id, CallTreeNode caller, CallTree container)
            : base(name, id, container)
        {
            m_caller = caller;
        }

        /// <summary>
        /// Sort the childre of every node in the te
        /// </summary>
        /// <param name="comparer"></param>
        internal void SortAll(IComparer<CallTreeNode> comparer, RecursionGuard recursionGuard = default(RecursionGuard))
        {
            if (recursionGuard.RequiresNewThread)
            {
                // Avoid capturing method parameters for use in the lambda to reduce fast-path allocation costs
                var capturedThis = this;
                var capturedComparer = comparer;
                var capturedRecursionGuard = recursionGuard;
                Task result = Task.Factory.StartNew(
                    () => capturedThis.SortAll(capturedComparer, capturedRecursionGuard.ResetOnNewThread),
                    TaskCreationOptions.LongRunning);

                result.GetAwaiter().GetResult();
                return;
            }

            if (Callees != null)
            {
                m_callees.Sort(comparer);
                for (int i = 0; i < m_callees.Count; i++)
                {
                    m_callees[i].SortAll(comparer, recursionGuard.Recurse);
                }

                m_displayCallees = null;    // Recompute
            }

        }

        // Graph support.  
        private IList<CallTreeNode> GetAllChildren()
        {
            var source = CallTree.StackSource;
            CallTreeNode[] samplesToNodes = CallTree.m_samplesToTreeNodes;
            Debug.Assert(source.IsGraphSource);
            Debug.Assert(samplesToNodes != null);

            var childrenSet = new Dictionary<string, CallTreeNode>();
            // Exclude myself
            childrenSet[Name] = null;
            // Exclude the primary children
            if (Callees != null)
            {
                foreach (var callee in Callees)
                {
                    childrenSet[callee.Name] = null;
                }
            }

            // TODO FIX NOW.  This is a hack, we know every type of CallTreeNode.     
            var asAgg = this as AggregateCallTreeNode;
            var dir = IsCalleeTree ? RefDirection.From : RefDirection.To;

            bool[] sampleSet = new bool[128];
            GetSamples(true, delegate (StackSourceSampleIndex sampleIndex)
            {
                while (sampleSet.Length <= (int)sampleIndex)
                {
                    Array.Resize(ref sampleSet, sampleSet.Length * 2);
                }

                sampleSet[(int)sampleIndex] = true;
                return true;
            });

            GetSamples(true, delegate (StackSourceSampleIndex sampleIndex)
            {
                // TODO FIX NOW too subtle!  This tracing back up the stack is tricky.  
                if (!IsCalleeTree && asAgg != null)
                {
                    // For Caller nodes, you need to move 'toward the root' a certain number of call frames.  
                    // especially because recursive nodes are folded in the tree by not in the graph.  
                    var sample = CallTree.StackSource.GetSampleByIndex(sampleIndex);
                    StackSourceCallStackIndex samplePath = sample.StackIndex;
                    Debug.Assert(asAgg != null);
                    for (int i = 0; i < asAgg.m_callerOffset; i++)
                    {
                        samplePath = CallTree.StackSource.GetCallerIndex(samplePath);
                    }

                    if (samplePath == StackSourceCallStackIndex.Invalid)
                    {
                        return true;
                    }
                    // This is where we break abstraction.   We know that the callStackIndex is in fact a sample index
                    // so we can simply cast.   TODO FIX NOW decide how to not break the abstraction.  
                    sampleIndex = (StackSourceSampleIndex)samplePath;
                }
                source.GetReferences(sampleIndex, dir, delegate (StackSourceSampleIndex childIndex)
                {
                    // Ignore samples to myself.  
                    if (childIndex < 0 || ((int)childIndex < sampleSet.Length && sampleSet[(int)childIndex]))
                    {
                        return;
                    }

                    var childNode = samplesToNodes[(int)childIndex];
                    if (childNode != null)       // TODO FIX NOW: I would not think this check would be needed.  
                    {
                        CallTreeNode graphChild;
                        if (!childrenSet.TryGetValue(childNode.Name, out graphChild))
                        {
                            childrenSet[childNode.Name] = graphChild = new CallTreeNode(childNode.Name, childNode.ID, this, CallTree);
                            graphChild.IsCalleeTree = IsCalleeTree;
                            graphChild.m_isGraphNode = true;
                            graphChild.m_minDepth = int.MaxValue;
                        }

                        // Add the sample 
                        if (graphChild != null)
                        {
                            graphChild.m_minDepth = Math.Min(childNode.Depth(), graphChild.m_minDepth);
                            graphChild.m_samples.Add(childIndex);
                            // TODO FIX NOW, these are arc counts, they should be node counts.  (need interning).  
                            graphChild.m_exclusiveCount++;
                            graphChild.m_exclusiveMetric += source.GetSampleByIndex(childIndex).Metric;
                        }
                    }
                });
                return true;
            });

            // Sort by min depth then name.  
            var ret = new List<CallTreeNode>();
            foreach (var val in childrenSet.Values)
            {
                if (val != null)
                {
                    ret.Add(val);
                }
            }
            ret.Sort(delegate (CallTreeNode x, CallTreeNode y)
            {
                var cmp = x.m_minDepth - y.m_minDepth;
                if (cmp != 0)
                {
                    return cmp;
                }

                return x.Name.CompareTo(y.Name);
            });

            // Put the true callees first.  
            if (Callees != null)
            {
                ret.InsertRange(0, Callees);
            }

            return ret;
        }

        /// <summary>
        /// Some calltrees already fill in their children, others do so lazily, in which case they 
        /// override this method.  
        /// </summary>
        protected virtual List<CallTreeNode> GetCallees() { return null; }

        /// <summary>
        /// Fold away any nodes having less than 'minInclusiveMetric'.  If 'sumByID' is non-null then the 
        /// only nodes that have a less then the minInclusiveMetric for the whole trace are folded. 
        /// </summary>
        internal int FoldNodesUnder(float minInclusiveMetric, Dictionary<int, CallTreeNodeBase> sumByID)
        {
            int nodesFolded = 0;
            if (Callees != null)
            {
                int to = 0;
                for (int from = 0; from < m_callees.Count; from++)
                {
                    var callee = m_callees[from];
                    // We don't fold away Broken stacks ever.  
                    if (Math.Abs(callee.InclusiveMetric) < minInclusiveMetric && callee.m_id != StackSourceFrameIndex.Broken &&
                    (sumByID == null || callee.IsFoldable(minInclusiveMetric, sumByID)))
                    {
                        // TODO the samples are no longer in time order, do we care?
                        nodesFolded++;
                        m_exclusiveCount += callee.m_inclusiveCount;
                        m_exclusiveMetric += callee.m_inclusiveMetric;
                        m_exclusiveFoldedMetric += callee.m_inclusiveMetric;
                        m_exclusiveFoldedCount += callee.m_inclusiveCount;

                        // Transfer the samples to the caller 
                        TransferInclusiveSamplesToList(callee, ref m_samples, RecursionGuard.Entry);
                    }
                    else
                    {
                        nodesFolded += callee.FoldNodesUnder(minInclusiveMetric, sumByID);
                        if (to != from)
                        {
                            m_callees[to] = m_callees[from];
                        }

                        to++;
                    }
                }

                if (to == 0)
                {
                    m_callees = null;
                }
                else if (to != m_callees.Count)
                {
                    m_callees.RemoveRange(to, m_callees.Count - to);
                }

                Debug.Assert((to == 0 && m_callees == null) || to == m_callees.Count);
            }

            Debug.Assert(Math.Abs(InclusiveMetric - ExclusiveMetric) >= -Math.Abs(InclusiveMetric) * .001);
            Debug.Assert(m_callees != null || Math.Abs(ExclusiveMetric - InclusiveMetric) <= .001 * Math.Abs(ExclusiveMetric));
            return nodesFolded;
        }

        // TODO FIX NOW: decide what to do here, we originally did a recursive IsFolable but that causes very little folding. 
        private bool IsFoldable(float minInclusiveMetric, Dictionary<int, CallTreeNodeBase> sumByID)
        {
            return Math.Abs(sumByID[(int)m_id].InclusiveMetric) < minInclusiveMetric;
        }

        // Transfer all samples (inclusively from 'fromNode' to 'toList'.  
        private static void TransferInclusiveSamplesToList(CallTreeNode fromNode, ref GrowableArray<StackSourceSampleIndex> toList, RecursionGuard recursionGuard)
        {
            if (recursionGuard.RequiresNewThread)
            {
                var boxedList = new StrongBox<GrowableArray<StackSourceSampleIndex>>(toList);
                Task result = Task.Factory.StartNew(
                    () => TransferInclusiveSamplesToList(fromNode, ref boxedList.Value, recursionGuard.ResetOnNewThread),
                    TaskCreationOptions.LongRunning);

                result.GetAwaiter().GetResult();
                toList = boxedList.Value;
                return;
            }

            // Transfer the exclusive samples.
            for (int i = 0; i < fromNode.m_samples.Count; i++)
            {
                toList.Add(fromNode.m_samples[i]);
            }

            // And now all the samples from children
            if (fromNode.Callees != null)
            {
                for (int i = 0; i < fromNode.m_callees.Count; i++)
                {
                    TransferInclusiveSamplesToList(fromNode.m_callees[i], ref toList, recursionGuard.Recurse);
                }
            }
        }

        internal CallTreeNode FindCallee(StackSourceFrameIndex frameID)
        {
            var canonicalFrameID = m_callTree.m_canonicalID[(int)frameID];
            string frameName = null;
            if (canonicalFrameID == 0)
            {
                frameName = m_callTree.m_SampleInfo.GetFrameName(frameID, false);
                canonicalFrameID = m_callTree.m_frameIntern.GetOrAdd(frameName, frameID);
                m_callTree.m_canonicalID[(int)frameID] = canonicalFrameID;
            }

            // TODO see if taking the lock in the read case is expensive or not.  
            CallTreeNode callee;
            ConcurrentDictionary<StackSourceFrameIndex, CallTreeNode> calleeLookup = null;
            lock (this)
            {
                if (m_callees != null)
                {
                    // Optimization.   If we have large fanout, we create a dictionary 
                    if (m_callees.Count > 16)
                    {
                        // Do we have the dictionary of dictionaries? If not make one
                        var calleeLookups = m_callTree.m_calleeLookups;
                        if (calleeLookups == null)
                        {
                            calleeLookups = m_callTree.m_calleeLookups = new ConcurrentDictionary<CallTreeNode, ConcurrentDictionary<StackSourceFrameIndex, CallTreeNode>>();
                        }

                        // Find the lookup table for this particular node (we don't expect many nodes to have this so we have a side table to look them up)
                        calleeLookup = calleeLookups.GetOrAdd(this, delegate (CallTreeNode nodeToAddToCache)
                            {
                                // Make up a new table for this node.  
                                var newCalleeLookup = new ConcurrentDictionary<StackSourceFrameIndex, CallTreeNode>();
                                foreach (var node in nodeToAddToCache.m_callees)
                                {
                                    newCalleeLookup[node.m_id] = node;
                                }

                                return newCalleeLookup;
                            });

                        // Finally look up the child quickly using the table
                        if (calleeLookup.TryGetValue(canonicalFrameID, out callee))
                        {
                            Debug.Assert(callee.Caller == this);
                            return callee;
                        }
                    }
                    else
                    {
                        for (int i = m_callees.Count; 0 < i;)
                        {
                            --i;
                            callee = m_callees[i];
                            if (callee != null && callee.m_id == canonicalFrameID)
                            {
                                return callee;
                            }
                        }
                    }
                }

                // No luck, add a new node. 
                if (frameName == null)
                {
                    frameName = m_callTree.m_SampleInfo.GetFrameName(canonicalFrameID, false);
                }

                callee = new CallTreeNode(frameName, canonicalFrameID, this, m_callTree);

                if (m_callees == null)
                {
                    m_callees = new List<CallTreeNode>();
                }

                m_callees.Add(callee);

                // If this node had large fanout also add it to that lookup table.  
                if (calleeLookup != null)
                {
                    calleeLookup[callee.m_id] = callee;
                    Debug.Assert(calleeLookup.Count == m_callees.Count);
                }
            }
            return callee;
        }

        private bool IsLastChild(bool displayPrimaryOnly)
        {
            var parentCallees = displayPrimaryOnly ? Caller.Callees : Caller.AllCallees;
            return (parentCallees[parentCallees.Count - 1] == this);
        }

        private int Depth()
        {
            int ret = 0;
            CallTreeNode ptr = Caller;
            while (ptr != null)
            {
                ret++;
                ptr = ptr.Caller;
            }
            return ret;
        }

        private float GetBrokenStackCount(int depth = 4)
        {
            if (depth <= 0)
            {
                return 0;
            }

            if (Name == "BROKEN")          // TODO use ID instead
            {
                return InclusiveCount;
            }

            float ret = 0;
            if (Callees != null)
            {
                foreach (var child in Callees)
                {
                    ret += child.GetBrokenStackCount(depth - 1);
                }
            }

            return ret;
        }

        [Conditional("DEBUG")]
        internal void CheckClassInvarients()
        {
            float sum = m_exclusiveMetric;
            float count = m_exclusiveCount;
            if (m_callees != null)
            {
                for (int i = 0; i < Callees.Count; i++)
                {
                    var callee = m_callees[i];
                    callee.CheckClassInvarients();
                    sum += callee.m_inclusiveMetric;
                    count += callee.m_inclusiveCount;
                }
            }
            Debug.Assert(Math.Abs(sum - m_inclusiveMetric) <= (Math.Abs(sum) + Math.Abs(m_inclusiveMetric) + .001) * .001);
            Debug.Assert(count == m_inclusiveCount);
        }

        // state;
        private CallTreeNode m_caller;
        internal List<CallTreeNode> m_callees;
        private IList<CallTreeNode> m_displayCallees;           // Might contain more 'nodes' that are not in the tree proper
        private string m_indentString;
        private bool m_indentStringForPrimary;

        internal /*protected*/ virtual bool IsCalleeTree { get { return !m_isCallerTree; } set { m_isCallerTree = !value; } }
        #endregion
    }

    /// <summary>
    /// A CallerCalleeNode gives statistics that focus on a NAME.  (unlike calltrees that use ID)
    /// It takes all stackSource that have callStacks that include that treeNode and compute the metrics for
    /// all the callers and all the callees for that treeNode.  
    /// </summary>
    public class CallerCalleeNode : CallTreeNodeBase
    {
        /// <summary>
        /// Given a complete call tree, and a Name within that call tree to focus on, create a
        /// CallerCalleeNode that represents the single Caller-Callee view for that treeNode. 
        /// </summary>
        public CallerCalleeNode(string nodeName, CallTree callTree)
            : base(nodeName, StackSourceFrameIndex.Invalid, callTree)
        {
            m_callersByName = new Dictionary<string, CallTreeNodeBase>();
            m_callers = new List<CallTreeNodeBase>();

            m_calleesByName = new Dictionary<string, CallTreeNodeBase>();
            m_callees = new List<CallTreeNodeBase>();

            var accumulated = AccumulateSamplesForNode(callTree.Root, 0);
            CallTreeNodeBase weightedSummary = accumulated.WeightedSummary;
            double weightedSummaryScale = accumulated.WeightedSummaryScale;
            bool isUniform = accumulated.IsUniform;

            m_callees.AddRange(m_calleesByName.Values);
            m_callees.Sort((x, y) => Math.Abs(y.InclusiveMetric).CompareTo(Math.Abs(x.InclusiveMetric)));
            m_calleesByName = null;

            m_callers.AddRange(m_callersByName.Values);
            m_callersByName = null;
            m_callers.Sort((x, y) => Math.Abs(y.InclusiveMetric).CompareTo(Math.Abs(x.InclusiveMetric)));
#if DEBUG
            float callerSum = 0;
            foreach (var caller in m_callers)
                callerSum += caller.m_inclusiveMetric;

            float calleeSum = 0;
            foreach (var callee in m_callees)
                calleeSum += callee.m_inclusiveMetric;

            if (this.Name != m_callTree.Root.Name)
                Debug.Assert(Math.Abs(callerSum - m_inclusiveMetric) <= .001);
            Debug.Assert(Math.Abs(calleeSum + m_exclusiveMetric - m_inclusiveMetric) <= .001 * Math.Abs(m_inclusiveMetric));

            // We should get he same stats as the byID view
            CallTreeNodeBase byID = null;
            foreach (var sumNode in callTree.ByID)
            {
                if (sumNode.Name == this.Name)
                {
                    if (byID != null)
                    {
                        byID = null; // TODO right now we might get duplicates that have the same  name but different ID.  Give up.  
                        break;
                    }
                    byID = sumNode;
                }
            }
            if (byID != null)
            {
                Debug.Assert(Math.Abs(byID.InclusiveCount - InclusiveCount) < .001);
                Debug.Assert(Math.Abs(byID.InclusiveMetric - InclusiveMetric) < .001);
                Debug.Assert(byID.InclusiveMetricByTimeString == InclusiveMetricByTimeString);
                Debug.Assert(byID.FirstTimeRelativeMSec == FirstTimeRelativeMSec);
                Debug.Assert(byID.LastTimeRelativeMSec == LastTimeRelativeMSec);
                // Because of the weighting (caused by splitting samples) exclusive metric and count may
                // not be the same as the the ByID exclusive metric and count 
            }
#endif
        }

        /// <summary>
        /// The list of CallTreeNodeBase nodes that called the method represented by this CallerCalleeNode
        /// </summary>
        public IList<CallTreeNodeBase> Callers { get { return m_callers; } }
        /// <summary>
        /// The list of CallTreeNodeBase nodes that where called by the method represented by this CallerCalleeNode
        /// </summary>
        public IList<CallTreeNodeBase> Callees { get { return m_callees; } }

        /// <summary>
        /// wrtites an XML representation of the call tree Node  it 'writer'
        /// </summary>
        public void ToXml(TextWriter writer, string indent)
        {
            writer.Write("{0}<CallerCallee", indent); ToXmlAttribs(writer); writer.WriteLine(">");
            writer.WriteLine("{0} <Callers Count=\"{1}\">", indent, m_callers.Count);
            foreach (CallTreeNodeBase caller in m_callers)
            {
                writer.Write("{0}  <Node", indent);
                caller.ToXmlAttribs(writer);
                writer.WriteLine("/>");
            }
            writer.WriteLine("{0} </Callers>", indent);
            writer.WriteLine("{0} <Callees Count=\"{1}\">", indent, m_callees.Count);
            foreach (CallTreeNodeBase callees in m_callees)
            {
                writer.Write("{0}  <Node", indent);
                callees.ToXmlAttribs(writer);
                writer.WriteLine("/>");
            }
            writer.WriteLine("{0} </Callees>", indent);
            writer.WriteLine("{0}</CallerCallee>", indent);
        }
        /// <summary>
        /// Returns an XML representation of the CallerCalleeNode (for debugging);
        /// </summary>
        public override string ToString()
        {
            StringWriter sw = new StringWriter();
            ToXml(sw, "");
            return sw.ToString();
        }
        #region overrides

        /// <summary>
        /// Implements CallTreeNodesBase interface
        /// </summary>
        protected override void FreeMemory(Stack<CallTreeNodeBase> nodesToFree)
        {
            foreach (var node in m_callers)
            {
                nodesToFree.Push(node);
            }

            m_callers = null;
            foreach (var node in m_callees)
            {
                nodesToFree.Push(node);
            }

            m_callees = null;
            base.FreeMemory(nodesToFree);
        }
        #endregion
        #region private
        /// <summary>
        /// A caller callee view is a summation which centers around one 'focus' node which is represented by the CallerCalleeNode.
        /// This node has a caller and callee list, and these nodes (as well as the CallerCalleNode itself) represent the aggregation
        /// over the entire tree.
        /// 
        /// AccumulateSamplesForNode is the routine that takes a part of a aggregated call tree (represented by 'treeNode' and adds
        /// in the statistics for that call tree into the CallerCalleeNode aggregations (and its caller and callee lists).  
        /// 
        /// 'recursionsCount' is the number of times the focus node name has occurred in the path from 'treeNode' to the root.   In 
        /// addition to setting the CallerCalleeNode aggregation, it also returns a 'weightedSummary' inclusive aggregation 
        /// FOR JUST treeNode (the CallerCalleNode is an aggregation over the entire call tree accumulated so far).  
        /// 
        /// The key problem for this routine to avoid is double counting of inclusive samples in the face of recursive functions. 
        /// Thus all samples are weighted by the recursion count before being included in 'weightedSummaryRet (as well as in
        /// the CallerCalleeNode and its Callers and Callees).    
        /// 
        /// An important optimization is the ability to NOT create (but rather reuse) CallTreeNodes when returning weightedSummaryRet.
        /// To accomplish this the weightedSummaryScaleRet is needed.  To get the correct numerical value for weightedSummaryRet, you 
        /// actually have to scale values by weightedSummaryScaleRet before use.   This allows us to represent weights of 0 (subtree has 
        /// no calls to the focus node), or cases where the subtree is completely uniform in its weighting (the subtree does not contain
        /// any additional focus nodes), by simply returning the tree node itself and scaling it by the recursion count).  
        /// 
        /// isUniformRet is set to false if anyplace in 'treeNode' does not have the scaling factor weightedSummaryScaleRet.  This
        /// means the the caller cannot simply scale 'treeNode' by a weight to get weightedSummaryRet.  
        /// </summary>
        private AccumulateSamplesResult AccumulateSamplesForNode(CallTreeNode treeNode, int recursionCount, RecursionGuard recursionGuard = default(RecursionGuard))
        {
            if (recursionGuard.RequiresNewThread)
            {
                // Avoid capturing method parameters for use in the lambda to reduce fast-path allocation costs
                var capturedThis = this;
                var capturedTreeNode = treeNode;
                var capturedRecursionCount = recursionCount;
                var capturedRecursionGuard = recursionGuard;
                Task<AccumulateSamplesResult> result = Task.Factory.StartNew(
                    () => capturedThis.AccumulateSamplesForNode(capturedTreeNode, capturedRecursionCount, capturedRecursionGuard.ResetOnNewThread),
                    TaskCreationOptions.LongRunning);

                return result.GetAwaiter().GetResult();
            }

            bool isFocusNode = treeNode.Name.Equals(Name);
            if (isFocusNode)
            {
                recursionCount++;
            }

            // We hope we are uniform (will fix if this is not true)
            bool isUniformRet = true;

            // Compute the weighting.   This is either 0 if we have not yet seen the focus node, or
            // 1/recusionCount if we have (splitting all samples equally among each of the samples)
            double weightedSummaryScaleRet = 0;
            CallTreeNodeBase weightedSummaryRet = null;          // If the weight is zero, we don't care about the value
            if (recursionCount > 0)
            {
                weightedSummaryScaleRet = 1.0F / recursionCount;

                // We opportunistically hope that all nodes in this subtree have the same weighting and thus
                // we can simply return the treeNode itself as the summary node for this subtree.  
                // This will get corrected to the proper value if our hopes prove unfounded.  
                weightedSummaryRet = treeNode;
            }

            // Get all the samples for the children and set the calleeSum information  We also set the
            // information in the CallerCalleNode's Callees list.  
            if (treeNode.Callees != null)
            {
                for (int i = 0; i < treeNode.m_callees.Count; i++)
                {
                    CallTreeNode treeNodeCallee = treeNode.m_callees[i];

                    // Get the correct weighted summary for the children.  
                    var nestedResult = AccumulateSamplesForNode(treeNodeCallee, recursionCount, recursionGuard.Recurse);
                    CallTreeNodeBase calleeWeightedSummary = nestedResult.WeightedSummary;
                    double calleeWeightedSummaryScale = nestedResult.WeightedSummaryScale;
                    bool isUniform = nestedResult.IsUniform;

                    // Did we have any samples at all that contained the focus node this treeNode's callee?
                    if (weightedSummaryScaleRet != 0 && calleeWeightedSummaryScale != 0)
                    {
                        // Yes, then add the summary for the treeNode's callee to corresponding callee node in 
                        // the caller-callee aggregation. 
                        if (isFocusNode)
                        {
                            var callee = Find(ref m_calleesByName, treeNodeCallee.Name);
                            callee.CombineByIdSamples(calleeWeightedSummary, true, calleeWeightedSummaryScale);
                        }

                        // And also add it to the weightedSummaryRet node we need to return.   
                        // This is the trickiest part of this code.  The way this works is that
                        // return value ALWAYS starts with the aggregation AS IF the weighting
                        // was uniform.   However if that proves to be an incorrect assumption
                        // we subtract out the uniform values and add back in the correctly weighted 
                        // values.   
                        if (!isUniform || calleeWeightedSummaryScale != weightedSummaryScaleRet)
                        {
                            isUniformRet = false;       // We ourselves are not uniform.  

                            // We can no longer use the optimization of using the treenode itself as our weighted
                            // summary node because we need to write to it.   Thus replace the node with a copy.  
                            if (weightedSummaryRet == treeNode)
                            {
                                weightedSummaryRet = new CallTreeNodeBase(weightedSummaryRet);
                            }

                            // Subtract out the unweighted value and add in the weighted one
                            double scale = calleeWeightedSummaryScale / weightedSummaryScaleRet;
                            weightedSummaryRet.m_inclusiveMetric += (float)(calleeWeightedSummary.m_inclusiveMetric * scale - treeNodeCallee.m_inclusiveMetric);
                            weightedSummaryRet.m_inclusiveCount += (float)(calleeWeightedSummary.m_inclusiveCount * scale - treeNodeCallee.m_inclusiveCount);
                            if (weightedSummaryRet.m_inclusiveMetricByTime != null)
                            {
                                weightedSummaryRet.m_inclusiveMetricByTime.AddScaled(calleeWeightedSummary.m_inclusiveMetricByTime, scale);
                                weightedSummaryRet.m_inclusiveMetricByTime.AddScaled(treeNodeCallee.m_inclusiveMetricByTime, -1);
                            }
                            if (weightedSummaryRet.m_inclusiveMetricByScenario != null)
                            {
                                weightedSummaryRet.m_inclusiveMetricByScenario.AddScaled(calleeWeightedSummary.m_inclusiveMetricByScenario, scale);
                                weightedSummaryRet.m_inclusiveMetricByScenario.AddScaled(treeNodeCallee.m_inclusiveMetricByScenario, -1);
                            }
                        }
                    }
                }
            }

            // OK we are past the tricky part of creating a weighted summary node.   If this is a focus node, we can simply
            // Add this aggregation to the CallerCallee node itself as well as the proper Caller node.  
            if (isFocusNode)
            {
                CombineByIdSamples(weightedSummaryRet, true, weightedSummaryScaleRet);

                // Set the Caller information now 
                CallTreeNode callerTreeNode = treeNode.Caller;
                if (callerTreeNode != null)
                {
                    Find(ref m_callersByName, callerTreeNode.Name).CombineByIdSamples(weightedSummaryRet, true, weightedSummaryScaleRet);
                }
            }

            return new AccumulateSamplesResult { WeightedSummary = weightedSummaryRet, WeightedSummaryScale = weightedSummaryScaleRet, IsUniform = isUniformRet };
        }

        private struct AccumulateSamplesResult
        {
            public CallTreeNodeBase WeightedSummary;
            public double WeightedSummaryScale;
            public bool IsUniform;
        }

        /// <summary>
        /// Find the Caller-Callee treeNode in 'elems' with name 'frameName'.  Always succeeds because it
        /// creates one if necessary. 
        /// </summary>
        private CallTreeNodeBase Find(ref Dictionary<string, CallTreeNodeBase> elems, string frameName)
        {
            CallTreeNodeBase elem;
            if (!elems.TryGetValue(frameName, out elem))
            {
                elem = new CallTreeNodeBase(frameName, StackSourceFrameIndex.Invalid, m_callTree);
                elems.Add(frameName, elem);
            }
            return elem;
        }

        // state;
        private List<CallTreeNodeBase> m_callers;
        private List<CallTreeNodeBase> m_callees;

        // During construction we want fast lookup  by name.  Later we throw these away and use m_callers and m_callees;
        private Dictionary<string, CallTreeNodeBase> m_callersByName;
        private Dictionary<string, CallTreeNodeBase> m_calleesByName;

        #endregion
    }

    /// <summary>
    /// AggregateCallTreeNode supports a multi-level caller-callee view.   
    /// 
    /// It does this by allow you to take any 'focus' node (typically a byname node)
    /// and compute a tree of its callers and a tree of its callees.   You do this
    /// by passing the node of interested to either the 'CallerTree' or 'CalleeTrees'.
    /// 
    /// The AggregateCallTreeNode remembers if if is a caller or callee node and its
    /// 'Callees' method returns the children (which may in fact be Callers). 
    /// 
    /// What is nice about 'AggregateCallTreeNode is that it is lazy, and you only 
    /// form the part of the tree you actually explore.     A classic 'caller-callee' 
    /// view is simply the caller and callee trees only explored to depth 1.
    /// </summary>
    public sealed class AggregateCallTreeNode : CallTreeNode
    {
        /// <summary>
        /// Given any node (typically a byName node, but it works on any node), Create a 
        /// tree rooted at 'node' that represents the callers of that node.  
        /// </summary>
        public static CallTreeNode CallerTree(CallTreeNodeBase node)
        {
            var ret = new AggregateCallTreeNode(node, null, 0);

            node.GetTrees(delegate (CallTreeNode tree)
            {
                ret.m_trees.Add(tree);
            });
            ret.CombineByIdSamples(node, true);
            return ret;
        }
        /// <summary>
        /// Given any node (typically a byName node, but it works on any node), Create a 
        /// tree rooted at 'node' that represents the callees of that node.  
        /// </summary>
        public static CallTreeNode CalleeTree(CallTreeNodeBase node)
        {
            var ret = new AggregateCallTreeNode(node, null, -1);

            node.GetTrees(delegate (CallTreeNode tree)
            {
                ret.m_trees.Add(tree);
            });
            ret.CombineByIdSamples(node, true);
            return ret;
        }
        /// <summary>
        /// Calls 'callback' for each distinct call tree in this node.  Note that the same
        /// trees can overlap (in the case of recursive functions), so you need a mechanism
        /// for visiting a tree only once.  
        /// </summary>
        public override void GetTrees(Action<CallTreeNode> callback)
        {
            foreach (var tree in m_trees)
            {
                callback(tree);
            }
        }

        /// <summary>
        /// Returns an XML representation of the AggregateCallTreeNode (for debugging);
        /// </summary>
        public override string ToString()
        {
            StringWriter sw = new StringWriter();
            sw.Write("<AggregateCallTreeNode");
            base.ToXmlAttribs(sw);
            sw.WriteLine(" CallerOffset=\"{0}\" TreeNodeCount=\"{1}\"/>", m_callerOffset, m_trees.Count);
            return sw.ToString();
        }
        #region private
        /// <summary>
        /// Implementation of CallTreeNodeBase interface
        /// </summary>
        protected override void FreeMemory(Stack<CallTreeNodeBase> nodesToFree)
        {
            m_trees.Clear();
            base.FreeMemory(nodesToFree);
        }
        /// <summary>
        /// Implementation of CallTreeNode interface
        /// </summary>
        protected override List<CallTreeNode> GetCallees()
        {
            var ret = new List<CallTreeNode>();
            if (IsCalleeTree)
            {
                foreach (var tree in m_trees)
                {
                    MergeCallee(tree, ret);
                }

                // By calling MergeCallee on tree, we have walked the entire forest in 'ret'
                // and have set the m_recursion bit if the node contains m_idToExclude.   
                // To avoid having to walk the tree again, we set this m_idToExclude to Invalid
                // for trees that are known not to contain m_idToExclude, which allows us to
                // skip most trees 
                if (m_idToExclude != StackSourceFrameIndex.Invalid)
                {
                    foreach (AggregateCallTreeNode callee in ret)
                    {
                        if (!callee.m_recursion)
                        {
                            callee.m_idToExclude = StackSourceFrameIndex.Invalid;
                        }
                    }
                }
            }
            else
            {
                foreach (var tree in m_trees)
                {
                    MergeCaller(tree, ret, m_callerOffset);
                }
            }

            ret.Sort((x, y) => Math.Abs(y.InclusiveMetric).CompareTo(Math.Abs(x.InclusiveMetric)));
#if DEBUG
            // Check that the exc time + children inc time = inc time 
            var incCountChildren = 0.0F;
            var incMetricChildren = 0.0F;

            foreach (var callee in ret)
            {
                incCountChildren += callee.InclusiveCount;
                incMetricChildren += callee.InclusiveMetric;
            }
            if (IsCalleeTree)
            {
                Debug.Assert(Math.Abs(InclusiveCount - (ExclusiveCount + incCountChildren)) <= Math.Abs(InclusiveCount / 1000.0F));
                Debug.Assert(Math.Abs(InclusiveMetric - (ExclusiveMetric + incMetricChildren)) <= Math.Abs(InclusiveMetric / 1000.0F));
            }
            else
            {
                if (ret.Count != 0)
                {
                    // For caller nodes, the root node has no children, but does have inclusive count
                    Debug.Assert(Math.Abs(InclusiveCount - incCountChildren) < Math.Abs(InclusiveCount / 1000.0F));
                    Debug.Assert(Math.Abs(InclusiveMetric - incMetricChildren) < Math.Abs(InclusiveMetric / 1000.0F));
                }
            }
#endif
            return ret;
        }

        /// <summary>
        /// See m_callerOffset and MergeCallee for more.
        /// 
        /// The 'this' node is a AggregateCallTree representing the 'callers' nodes.  Like 
        /// MergeCallee the aggregate node represents a list of CallTreeNodes.   However unlike
        /// MergeCallee, the list of CallTreeNodes each represent a sample (a complete call stack)
        /// and 'callerOffset' indicates how far 'up' that stack is the node of interest.  
        /// </summary>
        private void MergeCaller(CallTreeNode treeNode, List<CallTreeNode> callerList, int callerOffset)
        {
            // treeNode represents the sample (the complete call stack), but we want the node 
            // 'callerOffset' up the stack toward the root.  Calculate that here.  
            CallTreeNode treeForNode = treeNode;
            for (int i = 0; i < callerOffset; i++)
            {
                treeForNode = treeForNode.Caller;
            }

            CallTreeNode treeForCaller = treeForNode.Caller;
            if (treeForCaller == null)
            {
                return;
            }

            // Next find or make a node for 'treeForCaller' in the 'callerList' of child nodes
            // we are creating.   
            AggregateCallTreeNode childWithID = FindNodeInList(treeForCaller.ID, callerList);
            if (childWithID == null)
            {
                childWithID = new AggregateCallTreeNode(treeNode, this, callerOffset + 1);
                // TODO breaking abstraction.
                childWithID.m_id = treeForCaller.ID;
                childWithID.m_name = treeForCaller.Name;
                callerList.Add(childWithID);
            }

            // Add this tree to the node we found.
            childWithID.m_trees.Add(treeNode);

            // And compute our statistics.  
            // We pass addExclusive=false to CombindByIdSamples because callers never have exclusive samples
            // associated with them (because all samples occurred lower in the stack
            childWithID.CombineByIdSamples(treeNode, true, 1, false);

            // To get the correct inclusive time you also have to subtract out the any double counting. 
            if (m_idToExclude != StackSourceFrameIndex.Invalid)
            {
                if (treeNode.Callees != null)
                {
                    foreach (var callee in treeNode.Callees)
                    {
                        SubtractOutTrees(callee, m_idToExclude, childWithID);
                    }
                }
            }
        }

        /// <summary>
        /// An aggregateCallTreeNode is exactly that, the sum of several callTrees
        /// (each of which represent a number of individual samples).    Thus we had to 
        /// take each sample (which is 'treenode' and merge it into the aggregate.
        /// We do this one at a time.   Thus we call MergeCallee for each calltree 
        /// in our list and we find the 'callees' of each of those nodes, and create 
        /// aggregates for the children (which is in calleeList).   
        /// 
        /// This routine is not recursive and does not touch most of the tree but
        /// it does call SubtractOutTrees which is recursive and may look at a lot
        /// of the tree (although we try to minimize this)
        /// </summary>
        private void MergeCallee(CallTreeNode treeNode, List<CallTreeNode> calleeList)
        {
            if (treeNode.Callees != null)
            {
                // As an optimization we don't need to check for the prior existence of
                // nodes in the output list if the output list started as empty.  THis
                // is pretty important when there is only on tree node (e.g. root) and 
                // it has a lot of children (which would otherwise cause N*N behavior 
                bool checkForExistanceInOutputList = (calleeList.Count != 0);

                foreach (var treeCallee in treeNode.Callees)
                {
                    // Skip any children we were told to skip.  
                    if (treeCallee.ID == m_idToExclude)
                    {
                        continue;
                    }

                    AggregateCallTreeNode childWithID = null;
                    if (checkForExistanceInOutputList)
                    {
                        childWithID = FindNodeInList(treeCallee.ID, calleeList);
                    }
                    else
                    {
                        Debug.Assert(FindNodeInList(treeCallee.ID, calleeList) == null);
                    }

                    if (childWithID == null)
                    {
                        childWithID = new AggregateCallTreeNode(treeCallee, this, -1);
                        calleeList.Add(childWithID);
                    }

                    childWithID.m_trees.Add(treeCallee);

                    // Start to the normal inclusive counts
                    childWithID.CombineByIdSamples(treeCallee, true);

                    // Optimization if we know there are not samples to exclude, we don't need to do any adjustment   
                    if (m_idToExclude != StackSourceFrameIndex.Invalid)
                    {
                        SubtractOutTrees(treeCallee, m_idToExclude, childWithID);
                    }
                }
            }
        }

        /// <summary>
        /// Traverse 'treeCallee' and subtract out the inclusive time for any tree that matches 'idToExclude' from the node 'statsRet'.
        /// This is needed in AggregateCallTrees because the same trees from the focus node are in the list to aggregate, but are also
        /// in the subtree's in various places (and thus are counted twice).   We solve this by walking this subtree (in this routine)
        /// and subtracting out any nodes that match 'idToExclude'.   
        /// 
        /// As an optimization this routine also sets the m_recursion bit 'statsRet' if anywhere in 'treeCallee' we do find an id to 
        /// exclude.  That way in a common case (where there is no instances of 'idToExclude') we don't have to actualy walk the
        /// tree the second time (we simply know that there is no adjustment necessary.   
        /// </summary>
        private static void SubtractOutTrees(CallTreeNode treeCallee, StackSourceFrameIndex idToExclude, AggregateCallTreeNode statsRet)
        {
            if (treeCallee.ID == idToExclude)
            {
                statsRet.m_recursion = true;
                statsRet.CombineByIdSamples(treeCallee, true, -1, false);
                return;
            }
            // Subtract out any times we should have excluded
            if (treeCallee.Callees != null)
            {
                foreach (var callee in treeCallee.Callees)
                {
                    SubtractOutTrees(callee, idToExclude, statsRet);
                }
            }
        }

        private static AggregateCallTreeNode FindNodeInList(StackSourceFrameIndex id, List<CallTreeNode> calleeList)
        {
            foreach (var aggCallee in calleeList)
            {
                if (id == aggCallee.ID)
                {
                    return (AggregateCallTreeNode)aggCallee;
                }
            }
            return null;
        }

        internal AggregateCallTreeNode(CallTreeNodeBase node, AggregateCallTreeNode caller, int callerOffset)
            : base(node.Name, node.ID, caller, node.CallTree)
        {
            // Remember what the samples were by setting m_trees, which contain the actual samples 
            m_trees = new List<CallTreeNode>();
            m_callerOffset = callerOffset;

            if (caller != null)
            {
                m_idToExclude = caller.m_idToExclude;
            }
            else
            {
                m_idToExclude = node.ID;
                // Optimization. we know there is  no recursion for the root node without checking.    
                if (m_idToExclude == CallTree.Root.ID)
                {
                    m_idToExclude = StackSourceFrameIndex.Invalid;
                }
            }
        }

        internal /*protected*/ override StackSourceFrameIndex GetExcludeChildID()
        {
            return m_idToExclude;
        }

        internal /*protected*/ override bool IsCalleeTree { get { return m_callerOffset < 0; } set { Debug.Assert(value); m_callerOffset = -1; } }

        /// <summary>
        /// An AggregateCallTree remembers all its samples by maintaining a list of call trees 
        /// that actually contain the samples that the Aggregate represents.  m_trees hold this.   
        /// </summary>
        private List<CallTreeNode> m_trees;

        /// <summary>
        /// AggregateCallTreeNode can represent either a 'callers' tree or a 'callees' tree.   For 
        /// the 'callers' tree case the node represented by the aggregate does NOT have same ID as
        /// the tree in the m_trees list.   Instead the aggregate is some node 'up the chain' toward 
        /// the caller.  m_callerOffset keeps track of this (it is the same number for all elements 
        /// in m_trees).   
        /// 
        /// For callee nodes, this number is not needed.   Thus we use a illegal value (-1) to 
        /// represent that fact that the node is a callee node rather than a caller node.  
        /// </summary>
        internal int m_callerOffset;
        private StackSourceFrameIndex m_idToExclude;  // We should exclude any children with this ID as they are already counted.  
        private bool m_recursion;                     // Set to true if m_idToExclude does exists in 'm_trees' somewhere 
        #endregion
    }

}