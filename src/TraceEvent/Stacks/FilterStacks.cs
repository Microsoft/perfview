// Copyright (c) Microsoft Corporation.  All rights reserved
using Microsoft.Diagnostics.Tracing.Stacks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;

namespace Diagnostics.Tracing.StackSources
{
    /// <summary>
    /// This is just a class that holds data.  It does nothing except support an 'update' events 
    /// </summary>
    public class FilterParams
    {
        /// <summary>
        /// Constructs a Filter parameter class with all empty properties. 
        /// </summary>
        public FilterParams()
        {
            Name = "";
            StartTimeRelativeMSec = "";
            EndTimeRelativeMSec = "";
            MinInclusiveTimePercent = "";
            FoldRegExs = "";
            IncludeRegExs = "";
            ExcludeRegExs = "";
            GroupRegExs = "";
            TypePriority = "";
            ScenarioList = null;
        }
        /// <summary>
        /// Create a Filter Parameters Structure form another one
        /// </summary>
        /// <param name="other"></param>
        public FilterParams(FilterParams other)
        {
            Set(other);
        }
        /// <summary>
        /// Set a Filter Parameters Structure form another one
        /// </summary>
        public void Set(FilterParams filterParams)
        {
            if (this == filterParams)
            {
                return;
            }

            Name = filterParams.Name;
            StartTimeRelativeMSec = filterParams.StartTimeRelativeMSec;
            EndTimeRelativeMSec = filterParams.EndTimeRelativeMSec;
            MinInclusiveTimePercent = filterParams.MinInclusiveTimePercent;
            FoldRegExs = filterParams.FoldRegExs;
            IncludeRegExs = filterParams.IncludeRegExs;
            ExcludeRegExs = filterParams.ExcludeRegExs;
            GroupRegExs = filterParams.GroupRegExs;
            TypePriority = filterParams.TypePriority;
            ScenarioList = filterParams.ScenarioList;
        }

        /// <summary>
        /// Fetch Name 
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Fetch StartTimeRelativeMSec 
        /// </summary>
        public string StartTimeRelativeMSec { get; set; }
        /// <summary>
        /// Fetch EndTimeRelativeMSec 
        /// </summary>
        public string EndTimeRelativeMSec { get; set; }
        /// <summary>
        /// Fetch MinInclusiveTimePercent 
        /// </summary>
        public string MinInclusiveTimePercent { get; set; }
        /// <summary>
        /// Fetch FoldRegExs 
        /// </summary>
        public string FoldRegExs { get; set; }
        /// <summary>
        /// Fetch IncludeRegExs 
        /// </summary>
        public string IncludeRegExs { get; set; }
        /// <summary>
        /// Fetch ExcludeRegExs 
        /// </summary>
        public string ExcludeRegExs { get; set; }
        /// <summary>
        /// Fetch GroupRegExs 
        /// </summary>
        public string GroupRegExs { get; set; }
        /// <summary>
        /// Fetch TypePriority 
        /// </summary>
        public string TypePriority { get; set; }
        /// <summary>
        /// Fetch ScenarioList 
        /// </summary>
        public int[] ScenarioList { get; set; }

        /// <summary>
        /// Fetch Scenarios 
        /// </summary>
        public string Scenarios
        {
            get
            {
                if (ScenarioList == null)
                {
                    return "";
                }
                else
                {
                    return string.Join(",", ScenarioList);
                }
            }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    ScenarioList = null;
                }
                else
                {
                    var scenarios = value.Split(',', ' ');
                    ScenarioList = new int[scenarios.Length];

                    for (int i = 0; i < scenarios.Length; i++)
                    {
                        ScenarioList[i] = int.Parse(scenarios[i]);
                    }
                }
            }
        }

        /// <summary>
        ///  override
        /// </summary>
        public override bool Equals(object obj)
        {
            var asFilterParams = obj as FilterParams;
            if (asFilterParams == null)
            {
                return false;
            }

            return StartTimeRelativeMSec == asFilterParams.StartTimeRelativeMSec &&
                EndTimeRelativeMSec == asFilterParams.EndTimeRelativeMSec &&
                MinInclusiveTimePercent == asFilterParams.MinInclusiveTimePercent &&
                FoldRegExs == asFilterParams.FoldRegExs &&
                IncludeRegExs == asFilterParams.IncludeRegExs &&
                ExcludeRegExs == asFilterParams.ExcludeRegExs &&
                GroupRegExs == asFilterParams.GroupRegExs &&
                Scenarios == asFilterParams.Scenarios;
        }
        /// <summary>
        ///  override
        /// </summary>
        public override int GetHashCode()
        {
            return StartTimeRelativeMSec.GetHashCode();
        }

        /// <summary>
        /// TODO Document
        /// </summary>
        public static string EscapeRegEx(string str)
        {
            // Right now I don't allow matching names with * in them, which is our wildcard
            return str;
        }

        /// <summary>
        /// Write out the FilterParameters to XML 'writer'
        /// </summary>
        public void WriteToXml(XmlWriter writer)
        {
            writer.WriteStartElement("FilterXml");

            writer.WriteStartElement("Start");
            writer.WriteString(StartTimeRelativeMSec);
            writer.WriteEndElement();

            writer.WriteStartElement("End");
            writer.WriteString(EndTimeRelativeMSec);
            writer.WriteEndElement();

            writer.WriteStartElement("GroupRegEx");
            writer.WriteString(GroupRegExs);
            writer.WriteEndElement();

            writer.WriteStartElement("FoldPercent");
            writer.WriteString(MinInclusiveTimePercent);
            writer.WriteEndElement();

            writer.WriteStartElement("FoldRegEx");
            writer.WriteString(FoldRegExs);
            writer.WriteEndElement();

            writer.WriteStartElement("IncludeRegEx");
            writer.WriteString(IncludeRegExs);
            writer.WriteEndElement();

            writer.WriteStartElement("ExcludeRegEx");
            writer.WriteString(ExcludeRegExs);
            writer.WriteEndElement();

            writer.WriteStartElement("Scenarios");
            writer.WriteString(Scenarios);
            writer.WriteEndElement();

            writer.WriteEndElement();   // FilterXml
        }
        /// <summary>
        /// Create an XML representation of FilterParams as a string
        /// </summary>
        /// <returns></returns>
        public string ToXml()
        {
            var sb = new StringBuilder();
            using (var writer = XmlWriter.Create(sb, new XmlWriterSettings() { Indent = true }))
            {
                WriteToXml(writer);
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// A FilterStackSouce morphs one stack filters or groups the stacks of one stack source to form a new
    /// stack source.   It is very powerful mechanism.  
    /// </summary>
    public class FilterStackSource : StackSource
    {
        /// <summary>
        /// Create a new FilterStackSource.   
        /// </summary>
        /// <param name="filterParams">Specifies how to filter or group the stacks</param>
        /// <param name="stackSource">The input source to morph</param>
        /// <param name="scalingPolicy">How to scale the data (as time or simply by size of data)</param>
        public FilterStackSource(FilterParams filterParams, StackSource stackSource, ScalingPolicyKind scalingPolicy)
        {
            m_baseStackSource = stackSource;
            m_scalingPolicy = scalingPolicy;

            if (!double.TryParse(filterParams.StartTimeRelativeMSec, out m_minTimeRelativeMSec))
            {
                m_minTimeRelativeMSec = double.NegativeInfinity;
            }

            if (!double.TryParse(filterParams.EndTimeRelativeMSec, out m_maxTimeRelativeMSec))
            {
                m_maxTimeRelativeMSec = double.PositiveInfinity;
            }

            m_minTimeRelativeMSec -= .0006;      // Be inclusive as far as rounding goes.  
            m_maxTimeRelativeMSec += .0006;

            m_includePats = ParseRegExList(filterParams.IncludeRegExs);
            m_excludePats = ParseRegExList(filterParams.ExcludeRegExs);
            m_foldPats = ParseRegExList(filterParams.FoldRegExs);
            m_groups = ParseGroups(filterParams.GroupRegExs);

            if (stackSource.ScenarioCount > 0)
            {
                m_scenarioIncluded = new bool[stackSource.ScenarioCount];
                var scenarios = filterParams.ScenarioList;
                // Default to including all scenarios.
                if (scenarios == null)
                {

                }
                else
                {
                    foreach (int scenario in scenarios)
                    {
                        Debug.Assert(!m_scenarioIncluded[scenario]);
                        m_scenarioIncluded[scenario] = true;
                    }
                }
            }
            else
            {
                m_scenarioIncluded = null;
            }


            m_frameIdToFrameInfo = new FrameInfo[m_baseStackSource.CallFrameIndexLimit];
            m_GroupNameToFrameInfo = new Dictionary<string, StackSourceFrameIndex>();

            // Intialize the StackInfo cache (and the IncPathsMatchedSoFarStorage variable)
            m_stackInfoCache = new StackInfo[StackInfoCacheSize];
            for (int i = 0; i < m_stackInfoCache.Length; i++)
            {
                m_stackInfoCache[i] = new StackInfo(m_includePats.Length);
            }
        }

        /// <summary>
        /// Override
        /// </summary>
        public override void ForEach(Action<StackSourceSample> callback)
        {
            Action<StackSourceSample> filter = delegate (StackSourceSample sample)
            {
                // We always have at least the thread and process, unless we got a bad process 
                // Debug.Assert(sample.StackIndex != StackSourceCallStackIndex.Invalid);

                // Not in the upper bound of the range.  Discard
                if (!(sample.TimeRelativeMSec <= m_maxTimeRelativeMSec))
                {
                    return;
                }

                if (m_scalingPolicy == ScalingPolicyKind.TimeMetric)
                {
                    // If we have a time metric, we can only discard the sample if it does not overlap at all 
                    // The Math.Abs is a bit of a hack.  The problem is that that sample does not
                    // represent time for a DIFF (because we negated it) but I rely on the fact 
                    // that we only negate it so I can undo it 
                    if (!(m_minTimeRelativeMSec <= sample.TimeRelativeMSec + Math.Abs(sample.Metric)))
                    {
                        return;
                    }
                }
                else
                {
                    // before the lower bound (and we don't need to prorate)
                    if (!(m_minTimeRelativeMSec <= sample.TimeRelativeMSec))
                    {
                        return;                    // Even if we have to prorate the sample we on 
                    }
                }

                if (sample.StackIndex != StackSourceCallStackIndex.Invalid)
                {
                    StackInfo stackInfo = GetStackInfo(sample.StackIndex);

                    // Have we been told to discard (exclude patterns)
                    if (stackInfo.FrameIndex == StackSourceFrameIndex.Discard)
                    {
                        return;     // discard the sample 
                    }

                    // Do we have include patterns that were not matched?   If so discard.  
                    if (!stackInfo.AreIncPathsBitsAllSet)
                    {
                        return;     // discard the sample 
                    }
                }
                else if (m_includePats.Length > 0)
                {
                    return;         // discard the sample if there are any filters.  
                }

                // Not one of our included scenarios.
                if (m_scenarioIncluded != null && !m_scenarioIncluded[sample.Scenario])
                {
                    return;
                }

                // Check if we have to prorate the sample.  
                if (m_scalingPolicy == ScalingPolicyKind.TimeMetric)
                {
                    // The Math.Abs is a bit of a hack.  The problem is that that sample does not
                    // represent time for a DIFF (because we negated it) but I rely on the fact 
                    // that we only negate it so I can undo it 
                    double timeSpan = Math.Abs(sample.Metric);
                    var metricSign = sample.Metric > 0 ? 1 : -1;

                    Debug.Assert(m_minTimeRelativeMSec <= sample.TimeRelativeMSec + timeSpan);
                    Debug.Assert(sample.TimeRelativeMSec <= m_maxTimeRelativeMSec);

                    // We overlap with the minimum time 
                    if (sample.TimeRelativeMSec < m_minTimeRelativeMSec)
                    {
                        timeSpan = (sample.TimeRelativeMSec + timeSpan) - m_minTimeRelativeMSec;
                        Debug.Assert(0 <= timeSpan);        // because we checked earlier that minTime < sampleEndTime  
                        // Create a writable sample that has the prorated value
                        sample = new StackSourceSample(sample);
                        sample.Metric = timeSpan * metricSign;
                        sample.TimeRelativeMSec = m_minTimeRelativeMSec;

                    }
                    // We overlap with the maximum time. 
                    if (m_maxTimeRelativeMSec < sample.TimeRelativeMSec + timeSpan)
                    {
                        timeSpan = m_maxTimeRelativeMSec - sample.TimeRelativeMSec;
                        // Create a writable sample that has the prorated value
                        sample = new StackSourceSample(sample);
                        sample.Metric = timeSpan * metricSign;
                    }
                }

                callback(sample);
            };

            var aggregate = m_baseStackSource as AggregateStackSource;
            if (aggregate != null)
            {
                aggregate.ForEach(filter, m_scenarioIncluded);
            }
            else
            {
                m_baseStackSource.ForEach(filter);
            }
        }
        /// <summary>
        /// Override
        /// </summary>
        public override StackSourceCallStackIndex GetCallerIndex(StackSourceCallStackIndex callStackIndex)
        {
            StackInfo stackInfo = GetStackInfo(callStackIndex);
            return stackInfo.CallerIndex;
        }
        /// <summary>
        /// override
        /// </summary>
        public override int GetNumberOfFoldedFrames(StackSourceCallStackIndex callStackIndex)
        {
            StackInfo stackInfo = GetStackInfo(callStackIndex);
            return stackInfo.NumberOfFoldedFrames;
        }
        /// <summary>
        /// Override
        /// </summary>
        public override StackSourceFrameIndex GetFrameIndex(StackSourceCallStackIndex callStackIndex)
        {
            StackInfo stackInfo = GetStackInfo(callStackIndex);
            return stackInfo.FrameIndex;
        }
        /// <summary>
        /// Override
        /// </summary>
        public override string GetFrameName(StackSourceFrameIndex frameIndex, bool fullName)
        {
            var frameInfo = GetFrameInfo(frameIndex);
            if (frameInfo.GroupName != null)
            {
                if (frameInfo.IsEntryGroup)
                {
                    return frameInfo.GroupName + " <<" + m_baseStackSource.GetFrameName(frameIndex, false) + ">>";
                }
                else
                {
                    return frameInfo.GroupName;
                }
            }
            return m_baseStackSource.GetFrameName(frameIndex, fullName);
        }
        /// <summary>
        /// Override
        /// </summary>
        public override double SampleTimeRelativeMSecLimit
        {
            get
            {
                // TODO is this good enough? 
                return Math.Min(m_maxTimeRelativeMSec, m_baseStackSource.SampleTimeRelativeMSecLimit);
            }
        }
        /// <summary>
        /// Override
        /// </summary>
        public override int ScenarioCount { get { return m_baseStackSource.ScenarioCount; } }

        // lesser used routines
        /// <summary>
        /// Override
        /// </summary>
        public override int CallStackIndexLimit { get { return m_baseStackSource.CallStackIndexLimit; } }
        /// <summary>
        /// Override
        /// </summary>
        public override int CallFrameIndexLimit { get { return m_baseStackSource.CallFrameIndexLimit; } }
        /// <summary>
        /// Override
        /// </summary>
        public override StackSourceSample GetSampleByIndex(StackSourceSampleIndex sampleIndex) { return m_baseStackSource.GetSampleByIndex(sampleIndex); }
        /// <summary>
        /// Override
        /// </summary>
        public override StackSource BaseStackSource { get { return m_baseStackSource; } }
        /// <summary>
        /// Override
        /// </summary>
        public override bool IsGraphSource { get { return m_baseStackSource.IsGraphSource; } }
        /// <summary>
        /// Override
        /// </summary>
        public override void GetReferences(StackSourceSampleIndex nodeIndex, RefDirection dir, Action<StackSourceSampleIndex> callback)
        {
            m_baseStackSource.GetReferences(nodeIndex, dir, callback);
        }
        /// <summary>
        /// Override
        /// </summary>
        public override int SampleIndexLimit { get { return m_baseStackSource.SampleIndexLimit; } }

        #region private
        /// <summary>
        /// Associated with every frame is a FrameInfo which is the computed answers associated with that frame name.  
        /// We cache these and so most of the time looking up frame information is just an array lookup.  
        /// 
        /// FrameInfo contains information that is ONLY dependent on the frame name (not the stack it came from), so
        /// entry point groups and include patterns can not be completely processed at this point.   Never returns null. 
        /// </summary>
        private StackInfo GetStackInfo(StackSourceCallStackIndex stackIndex, RecursionGuard recursionGuard = default(RecursionGuard))
        {
            if (recursionGuard.RequiresNewThread)
            {
                // Avoid capturing method parameters for use in the lambda to reduce fast-path allocation costs
                var capturedThis = this;
                var capturedStackIndex = stackIndex;
                var capturedRecursionGuard = recursionGuard;
                Task<StackInfo> operation = Task.Factory.StartNew(
                    () => capturedThis.GetStackInfo(capturedStackIndex, capturedRecursionGuard.ResetOnNewThread),
                    TaskCreationOptions.LongRunning);

                return operation.GetAwaiter().GetResult();
            }

            Debug.Assert(0 <= stackIndex);                              // No illegal stacks, or other special stacks.  
            Debug.Assert((int)stackIndex < CallStackIndexLimit);         // And in range.  

            // Check the the cache, otherwise create it.  
            int hash = (((int)stackIndex) & (StackInfoCacheSize - 1));
            var stackInfo = m_stackInfoCache[hash];
            if (stackInfo.StackIndex != stackIndex)
            {
                // Try to reuse the slot.  Give up an allocate if necessary (TODO we can recycle if it happens frequently)
                if (stackInfo.InUse)
                {
                    stackInfo = new StackInfo(m_includePats.Length);
                }

                stackInfo.InUse = true;
                GenerateStackInfo(stackIndex, stackInfo, recursionGuard);
                stackInfo.InUse = false;
            }
            return stackInfo;
        }
        /// <summary>
        /// Generate the stack information for 'stack' and place it in stackInfoRet.  Only called by GetStackInfo.    
        /// </summary>
        private void GenerateStackInfo(StackSourceCallStackIndex stackIndex, StackInfo stackInfoRet, RecursionGuard recursionGuard)
        {
            // Clear out old information.  
            stackInfoRet.StackIndex = stackIndex;
            stackInfoRet.CallerIndex = m_baseStackSource.GetCallerIndex(stackIndex);
#if DEBUG
            stackInfoRet.DebugOrigFrameIndex = m_baseStackSource.GetFrameIndex(stackIndex);
            stackInfoRet.DebugOrigFrameName = m_baseStackSource.GetFrameName(stackInfoRet.DebugOrigFrameIndex, false);
#endif
            stackInfoRet.IncPathsMatchedSoFar = null;
            stackInfoRet.NumberOfFoldedFrames = 0;

            // If our caller was told to discard, then we do too.  
            StackInfo parentStackInfo = null;
            if (stackInfoRet.CallerIndex != StackSourceCallStackIndex.Invalid)
            {
                parentStackInfo = GetStackInfo(stackInfoRet.CallerIndex, recursionGuard.Recurse);
                if (parentStackInfo.FrameIndex == StackSourceFrameIndex.Discard)
                {
                    stackInfoRet.FrameIndex = StackSourceFrameIndex.Discard;
                    return;
                }
            }

            // Get the frame information 
            var ungroupedFrameIndex = m_baseStackSource.GetFrameIndex(stackIndex);
            stackInfoRet.FrameInfo = GetFrameInfo(ungroupedFrameIndex);
            if (stackInfoRet.FrameInfo.Discard)
            {
                stackInfoRet.FrameIndex = StackSourceFrameIndex.Discard;
                return;
            }

            // By default we use the ungrouped frame index as our frame index (and grouping will modify this). 
            stackInfoRet.FrameIndex = ungroupedFrameIndex;

            // Am I a member of a group?
            if (stackInfoRet.FrameInfo.GroupName != null)
            {
                // If I am an entry group, and my group matches my caller, then I become a member of my caller's group.
                // Otherwise I am the entry point for this group and I use myself (which is already set up)
                if (stackInfoRet.FrameInfo.IsEntryGroup)
                {
                    if (parentStackInfo != null && parentStackInfo.FrameInfo != null &&
                        stackInfoRet.FrameInfo.GroupID == parentStackInfo.FrameInfo.GroupID)
                    {
                        stackInfoRet.FrameIndex = parentStackInfo.FrameIndex;
                    }
                }
                else
                {
                    stackInfoRet.FrameIndex = stackInfoRet.FrameInfo.GroupID;
                }
            }

            // We fold if we have been told or there is direct recursion  
            // 
            // If we have have been told to fold, then this node disappears, so we update the 
            // slot for this stack to have exactly the information the parent had, effectively
            // this Frame ID and the parent are synonymous (which is what folding wants to do)
            // TODO confirm this works right when parentStackInfo == null 
            if (parentStackInfo != null)
            {
                var shouldFold = false;

                if (stackInfoRet.FrameInfo.Fold || stackInfoRet.FrameIndex == parentStackInfo.FrameIndex)
                {
                    shouldFold = true;
                }
                else if (stackInfoRet.FrameInfo.IsMoveNext && parentStackInfo.FrameInfo.IsMoveNext)
                {
                    // Because MoveNext is used in tasks and we want good recursion removal for that case, check then name
                    // instead of just the indexes (which won't match if the locations in the method are different).   
                    // This is reasonably expensive, so we only do it for MoveNext operations.  TODO make this efficient all the time. 
                    var parentName = m_baseStackSource.GetFrameName(parentStackInfo.FrameIndex, true);
                    var curName = m_baseStackSource.GetFrameName(stackInfoRet.FrameIndex, true);
                    shouldFold = (parentName == curName);
                }
                if (shouldFold)
                {
                    var patFold = stackInfoRet.FrameInfo.Fold;
                    stackInfoRet.CloneValue(parentStackInfo);
                    if (patFold)
                    {
                        stackInfoRet.NumberOfFoldedFrames++;
                    }

                    return;
                }
            }

            stackInfoRet.CopyIncPathsBits(parentStackInfo);
            // If this frame matches any inc patterns, then add mark the fact that we have added them. 
            stackInfoRet.SetIncPathsBits(stackInfoRet.FrameInfo.IncPatternsMatched);
        }
        /// <summary>
        /// Returns the frame information for frameIndex.   Never returns null.  
        /// </summary>
        private FrameInfo GetFrameInfo(StackSourceFrameIndex frameIndex)
        {
            Debug.Assert(frameIndex >= 0);       // No illegal or special frames allowed.

            // See if we have cached the answer. 
            FrameInfo frameInfo = m_frameIdToFrameInfo[(int)frameIndex];
            if (frameInfo != null)
            {
                return frameInfo;
            }

            // No cached answer.  compute the answer.   Start with the name (with the full module path)
            string fullFrameName = m_baseStackSource.GetFrameName(frameIndex, true);

            // TODO we are doing a lot of string manipulation.   Can we avoid this?
            // It turns out that we don't have a lot of unique frame names and we can avoid the cost
            // of the regular expressions if we remember.   We probably should not keep them all... 
            string origFullFrameName = fullFrameName;
            if (m_ByFrameName == null)
            {
                m_ByFrameName = new Dictionary<string, FrameInfo>();
            }

            if (m_ByFrameName.TryGetValue(fullFrameName, out frameInfo))
            {
                m_frameIdToFrameInfo[(int)frameIndex] = frameInfo;
                return frameInfo;
            }

            // Do the regular expression matching to find the transformed name
            bool isEntryGroup;
            var groupName = FindGroupNameFromFrameName(fullFrameName, out isEntryGroup);

            // Make a group for unknown symbols for modules.  Otherwise you end up with lots of !? that don't fold
            if (groupName == null && fullFrameName.EndsWith("!?"))
            {
                groupName = m_baseStackSource.GetFrameName(frameIndex, false);
                isEntryGroup = false;
            }

            if (groupName != null)
            {
                // The first FrameIndex that we find that matches the group becomes our 'canonical' ID for 
                // this group.   Get this index (or make it if necessary.                 
                StackSourceFrameIndex groupID = StackSourceFrameIndex.Invalid;
                if (!m_GroupNameToFrameInfo.TryGetValue(groupName, out groupID))
                {
                    m_GroupNameToFrameInfo.Add(groupName, frameIndex);
                    groupID = frameIndex;
                }

                // normal (non-entry point) groups can all share the same FrameInfo, so see if we have one
                fullFrameName = groupName;
                if (isEntryGroup)
                {
                    fullFrameName = groupName + " <<" + m_baseStackSource.GetFrameName(frameIndex, false) + ">>";
                }

                // This is a bit of a tricky optimization.   FrameInfo's job is to cache the grouping, folding, exclude and include pattern
                // matching for a particular frame name.   We would like to create as few as them as we can.  For non-Entry point groups
                // we clearly can (since after grouping, the name is the group name and folding,excluding, including are all the same for
                // a given name (which is the group name).   However this is an entry point group, we can only share them if the answers
                // for the one we want to reuse happens to match the answers we get for the full name.   Now for folding and exclusion
                // below we reuse one canonical frameInfo (the Fold and Discard frameInfo), as long as we don't reuse these we are OK.   However
                // we do need to check the include patterns are the same (often they are, since they include a process ID or only care about
                // the group name.   
                int[] incPatternsMatched = null;
                frameInfo = m_frameIdToFrameInfo[(int)groupID];         // Get the frameInfo for the group as a whole 
                if (frameInfo != null && isEntryGroup)                  // We may not be able to reuse this for entry point groups.  
                {
                    if (frameInfo == Discard || frameInfo == Fold)
                    {
                        frameInfo = null;
                    }
                    else
                    {
                        // If the incPats are the same, then we can reuse the group, otherwise we set frameInfo to null and we will regenerate it.  
                        incPatternsMatched = MatchSet(m_includePats, fullFrameName);
                        if (!SameSet(incPatternsMatched, frameInfo.IncPatternsMatched))
                        {
                            frameInfo = null;
                        }
                    }
                }

                if (frameInfo == null)
                {
                    // Don't have an canonical frame that represents the group.  The current frame becomes our canonical ID for the group    
                    frameInfo = new FrameInfo();
                    frameInfo.IsEntryGroup = isEntryGroup;
                    frameInfo.GroupID = groupID;
                    frameInfo.GroupName = groupName;
                    if (incPatternsMatched == null)
                    {
                        incPatternsMatched = MatchSet(m_includePats, fullFrameName);
                    }

                    frameInfo.IncPatternsMatched = incPatternsMatched;
                }
            }
            else
            {
                var isMoveNext = fullFrameName.Contains(".MoveNext()");

                // We did not find a group.  
                // Does this frame match any incPatterns?
                var incPatternsMatched = MatchSet(m_includePats, fullFrameName);
                if (incPatternsMatched != null || isMoveNext)
                {
                    // can't share, create a new FrameInfo and set the incPatternsMatched bits. 
                    frameInfo = new FrameInfo();
                    frameInfo.IncPatternsMatched = incPatternsMatched;
                    frameInfo.IsMoveNext = isMoveNext;
                }
                else    // No inc patterns, so we can reuse the static 'MatchesNothing' frame which says no include patterns match.  
                {
                    frameInfo = MatchesNothing;
                }
            }
            // At this point fullFrameName, and we have a candidate frameInfo

            // See if we should filter or fold it.  Note we clobber frameInfo, however if these are set, nothing else matters.  
            if (IsMatch(m_excludePats, fullFrameName) >= 0)
            {
                frameInfo = Discard;
            }
            else if (IsMatch(m_foldPats, fullFrameName) >= 0)
            {
                frameInfo = Fold;
            }

            // Keep the cache size under control by picking names that are likely to be used again.   Limit the cache size.  
            if (m_ByFrameName.Count < 4096)
            {
                // Currently as an efficiency measure, we only do this for unknown modules, since there will be many code addresses with the same name
                if (origFullFrameName.Contains("!?"))
                {
                    m_ByFrameName[origFullFrameName] = frameInfo;
                }
            }

            m_frameIdToFrameInfo[(int)frameIndex] = frameInfo;
            return frameInfo;
        }

        /// <summary>
        /// This is just the parsed form of a grouping specification Pat->GroupNameTemplate  (it has a pattern regular 
        /// expression and a group name that can have replacements)  It is a trivial class
        /// </summary>
        private class GroupPattern
        {
            // PatternInfo is basically a union, and these static functions create each of the kinds of union.  
            public GroupPattern(Regex pattern, string groupNameTemplate, string op)
            {
                Pattern = pattern;
                GroupNameTemplate = groupNameTemplate;
                IsEntryGroup = (op == "=>");
            }

            /// <summary>
            /// Experimentally we are going to special case the module entry pattern.  
            /// </summary>
            public bool IsModuleEntry; // TODO IsModuleEntry is an experimental thing.  Remove after gathering data.  

            public Regex Pattern;
            public string GroupNameTemplate;
            public bool IsEntryGroup;
            public bool HasReplacements { get { return GroupNameTemplate.IndexOf('$') >= 0; } }
            public override string ToString()
            {
                return string.Format("{0}->{1}", Pattern, GroupNameTemplate);
            }
            #region private
            private GroupPattern() { }
            #endregion
        }
        /// <summary>
        /// Parses a string into the GroupPattern structure that allows it to executed (matched).  
        /// </summary>
        private static GroupPattern[] ParseGroups(string groupPatternStr)
        {
            // First trim off any comments (in []) or whitespace. 
            var groupsStr = Regex.Replace(groupPatternStr, @"^\s*(\[.*?\])?\s*(.*?)\s*$", "$2");
            if (groupsStr.Length == 0)
            {
                return new GroupPattern[0];
            }

            var stringGroups = groupsStr.Split(';');
            var groups = new GroupPattern[stringGroups.Length];
            for (int i = 0; i < groups.Length; i++)
            {
                var stringGroup = stringGroups[i].Trim();
                if (stringGroup.Length == 0)
                {
                    continue;
                }

                var op = "=>";              // This means that you distinguish the entry points into the group
                int arrowIdx = stringGroup.IndexOf("=>");
                if (arrowIdx < 0)
                {
                    op = "->";              // This means you just group them losing information about what function was used to enter the group. 
                    arrowIdx = stringGroup.IndexOf("->");
                }
                var replaceStr = "$&";         // By default whatever we match is what we used as the replacement. 
                string patStr = null;
                if (arrowIdx >= 0)
                {
                    patStr = stringGroup.Substring(0, arrowIdx);
                    arrowIdx += 2;
                    replaceStr = stringGroup.Substring(arrowIdx, stringGroup.Length - arrowIdx).Trim();
                }
                else
                {
                    patStr = stringGroup;
                }

                var pat = new Regex(ToDotNetRegEx(patStr), RegexOptions.IgnoreCase);    // TODO perf bad if you compile!
                groups[i] = new GroupPattern(pat, replaceStr, op);

                // TODO IsModuleEntry is an experiemental thing.  Remove after gathering data.  
                if (stringGroup == "{%}!=>module $1")
                {
                    groups[i].IsModuleEntry = true;
                }
            }
            return groups;
        }
        /// <summary>
        /// Given the name of a frame, look it up in the group patterns and morph it to its group name. 
        /// If the group that matches is a entryGroup then set 'isEntryGroup'.  Will return null if
        /// no group matches 'frameName'
        /// </summary>
        private string FindGroupNameFromFrameName(string frameName, out bool isEntryGroup)
        {
            isEntryGroup = false;
            string groupName = null;

            // Look in every declared group looking for a match.  
            for (int i = 0; i < m_groups.Length; i++)
            {
                var candidateGroup = m_groups[i];
                if (candidateGroup == null)
                {
                    continue;
                }

                // TODO IsModuleEntry is an experiemental thing.  Remove after gathering data.  
                if (candidateGroup.IsModuleEntry)
                {
                    int bangIdx = frameName.IndexOf('!');
                    if (0 < bangIdx)
                    {
                        int moduleNameStartIdx = bangIdx - 1;
                        while (0 <= moduleNameStartIdx)
                        {
                            var c = frameName[moduleNameStartIdx];
                            if (!Char.IsLetterOrDigit(c) && c != '.' && c != '_')
                            {
                                break;
                            }

                            --moduleNameStartIdx;
                        }
                        moduleNameStartIdx++;
                        groupName = "module " + frameName.Substring(moduleNameStartIdx, bangIdx - moduleNameStartIdx);
                        isEntryGroup = true;
                        return groupName;
                    }
                    continue;
                }

                var match = candidateGroup.Pattern.Match(frameName);
                if (match.Success)
                {
                    groupName = candidateGroup.GroupNameTemplate;
                    if (candidateGroup.HasReplacements)
                    {
                        if (groupName == "$&")
                        {
                            groupName = match.Groups[0].Value;
                        }
                        else
                        {
                            // Replace the $1, $2, ... with the strings that were matched in the original regexp.  
                            groupName = Regex.Replace(candidateGroup.GroupNameTemplate, @"\$(\d)",
                                replaceMatch => match.Groups[replaceMatch.Groups[1].Value[0] - '0'].Value);
                            // Replace $& with the match replacement.  
                            groupName = Regex.Replace(groupName, @"\$&", match.Groups[0].Value);
                        }
                    }
                    isEntryGroup = candidateGroup.IsEntryGroup;
                    if (groupName.Length == 0)
                    {
                        return null;
                    }
                    // Entry groups terminate matching...
                    if (isEntryGroup)
                    {
                        return groupName;
                    }

                    // Otherwise we keep going.  
                    frameName = groupName;
                }
            }
            return groupName;
        }

        /// <summary>
        /// Holds parsed information about patterns for groups includes, excludes or folds.  
        /// </summary>
        private static Regex[] ParseRegExList(string patterns)
        {
            patterns = patterns.Trim();
            if (patterns.Length == 0)
            {
                return new Regex[0];
            }

            var stringGroupPats = patterns.Split(';');
            var ret = new Regex[stringGroupPats.Length];
            for (int i = 0; i < ret.Length; i++)
            {
                var patStr = stringGroupPats[i].Trim();
                if (patStr.Length > 0)         // Skip empty entries.  
                {
                    ret[i] = new Regex(ToDotNetRegEx(patStr), RegexOptions.IgnoreCase);    // TODO perf bad if you compile!
                }
            }
            return ret;
        }
        /// <summary>
        /// Returns the index in the 'pats' array of the first pattern that matches 'str'.   Returns -1 if no match. 
        /// </summary>
        private static int IsMatch(Regex[] pats, string str)
        {
            for (int i = 0; i < pats.Length; i++)
            {
                var pat = pats[i];
                if (pat != null && pat.IsMatch(str))
                {
                    return i;
                }
            }
            return -1;
        }
        // Returns the set of indexes for each pattern that matches.  
        private static int[] MatchSet(Regex[] pats, string str)
        {
            int[] ret = null;
            int retCount = 0;
            for (int i = 0; i < pats.Length; i++)
            {
                var pat = pats[i];
                if (pat != null && pat.IsMatch(str))
                {
                    // Note we can allocate a lot of arrays this way, but it likelihood of matching
                    // more than once is very low, so this is OK. 
                    var newRet = new int[retCount + 1];
                    if (retCount > 0)
                    {
                        Array.Copy(ret, newRet, retCount);
                    }

                    newRet[retCount] = i;
                    ret = newRet;
                    retCount++;
                }
            }
            return ret;
        }

        /// <summary>
        /// returns true if set1 and set1 (as returned from MatchSet) are identical 
        /// </summary>
        private bool SameSet(int[] set1, int[] set2)
        {
            if (set1 == null)
            {
                return set2 == null;
            }

            if (set2 == null)
            {
                return false;
            }

            if (set1.Length != set2.Length)
            {
                return false;
            }
            // We can assume values are sorted 
            for (int i = 0; i < set1.Length; i++)
            {
                if (set1[i] != set2[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Convert a string from my regular expression format (where you only have * and {  } as grouping operators
        /// and convert them to .NET regular expressions string
        /// </summary>
        public static string ToDotNetRegEx(string str)
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

        /// <summary>
        /// FrameInfo is all the information we need to associate with an Frame ID (to figure out what group/pattern it belongs to) 
        /// This includes what group it belongs to, the include patterns it matches whether to discard or fold it.   It is
        /// all the processing we can do with JUST the frame ID.  
        /// 
        /// Note that FrameInfo is reused by multiple stacks, which means that you should NOT update fields in it after initial creation.  
        /// </summary>
        private class FrameInfo
        {
            public FrameInfo()
            {
                GroupID = StackSourceFrameIndex.Invalid;
            }
            public bool Discard;                                // This sample should be discarded 
            public bool Fold;                                   // This frame should be folded into its parent
            public bool IsMoveNext;                             // As a special case MoveNext we insure 'perfect' recursion removal (TODO: make this work for any method)

            public bool IsEntryGroup;                           // This frame is part of a entry group. 
            /// <summary>
            /// This is what we return to the Stack crawler, it encodes either that we should filter the sample,
            /// fold the frame, form a group, or the frameID that we have chosen to represent the group as a whole.  
            /// </summary>
            public StackSourceFrameIndex GroupID;               // We assign one frame index to represent the group as a whole this is it.
            public string GroupName;                            // The name of the group (result after apply replacement pattern to frame name)
            public int[] IncPatternsMatched;                    // Each element in array is an index into m_includePats, which this frame matches.  
        }

        /// <summary>
        /// Represents all accumulated information about grouping for a particular stack.  Effectively this is the
        /// 'result' of applying the grouping and filtering to a particular stack.   We cache the last 100 or so
        /// of these because stacks tend to reuse the parts of the stack close the root.     
        /// </summary>
        private class StackInfo
        {
            public StackInfo(int numIncPats)
            {
                StackIndex = StackSourceCallStackIndex.Invalid;
                if (numIncPats > 0)
                {
                    IncPathsMatchedSoFarStorage = new bool[numIncPats];
                }
            }

            public StackSourceCallStackIndex StackIndex;        // This information was generated from this index. 
            public StackSourceCallStackIndex CallerIndex;       // This is just a cache of the 'GetCallerIndex call 
            public StackSourceFrameIndex FrameIndex;            // The frame index associated frame farthest from the root, it may have been morphed by grouping 

            internal FrameInfo FrameInfo;                       // Information about 'FrameIndex'
            internal int NumberOfFoldedFrames;                  // How many additional frames where folded into this one (by folding patterns)
            internal bool InUse;                                // can't be reused.  Someone is pointing at it.  
            /// <summary>
            /// The include patterns that have been matched by some frame in this stack.  (ultimately we need all bits set).
            /// Can be null, which means the empty set.  
            /// </summary>
            internal bool[] IncPathsMatchedSoFar;               // All the include patterns that have been matched for the entire stack.  
            // We reuse the StackInfo entries, and we want to avoid allocating when we do so.  So if we had a 
            // bool[] in IncPathsMatchedSoFar but now need IncPathsMatchedSoFar to be null, we store the array
            // here so that we can reuse it when we need it again.  
            internal bool[] IncPathsMatchedSoFarStorage;
#if DEBUG
            internal StackSourceFrameIndex DebugOrigFrameIndex; // Index before it was morphed by grouping
            internal string DebugOrigFrameName;
#endif
            //routines for manipulating a bitset of include patterns.  
            internal void CloneValue(StackInfo other)
            {
                // Note that I intentionally don't clone the StackIndex or IncPathsMatchedSoFarStorage as these are not the 'value' of this node. 
                FrameIndex = other.FrameIndex;
                NumberOfFoldedFrames = other.NumberOfFoldedFrames;
                CallerIndex = other.CallerIndex;
                FrameInfo = other.FrameInfo;
                CopyIncPathsBits(other);
            }
            #region Bit Set Helpers
            internal void CopyIncPathsBits(StackInfo other)
            {
                if (other == null || other.IncPathsMatchedSoFar == null)
                {
                    IncPathsMatchedSoFar = null;
                    return;
                }
                IncPathsMatchedSoFar = IncPathsMatchedSoFarStorage;
                for (int i = 0; i < IncPathsMatchedSoFar.Length; i++)
                {
                    IncPathsMatchedSoFar[i] = other.IncPathsMatchedSoFar[i];
                }
            }
            internal void SetIncPathsBits(int[] indexesToSet)
            {
                if (indexesToSet == null)
                {
                    return;
                }

                if (IncPathsMatchedSoFar == null)
                {
                    IncPathsMatchedSoFar = IncPathsMatchedSoFarStorage;
                    for (int i = 0; i < IncPathsMatchedSoFar.Length; i++)
                    {
                        IncPathsMatchedSoFar[i] = false;
                    }
                }
                for (int i = 0; i < indexesToSet.Length; i++)
                {
                    IncPathsMatchedSoFar[indexesToSet[i]] = true;
                }
            }
            internal bool AreIncPathsBitsAllSet
            {
                get
                {
                    if (IncPathsMatchedSoFar == null)
                    {
                        return IncPathsMatchedSoFarStorage == null;
                    }

                    for (int i = 0; i < IncPathsMatchedSoFar.Length; i++)
                    {
                        if (!IncPathsMatchedSoFar[i])
                        {
                            return false;
                        }
                    }

                    return true;
                }
            }
            #endregion
        }

        // These represent particular Frame information that can be shared among many frames 
        /// <summary>
        /// Represents a frame that does not match any pattern.  Thus the default of simply returning the frame ID is appropriate
        /// </summary>
        private static FrameInfo MatchesNothing = new FrameInfo();

        /// <summary>
        /// Represents a frame that should be discarded.  
        /// </summary>
        private static FrameInfo Discard = new FrameInfo() { Discard = true };

        /// <summary>
        /// Represents a frame that should be folded into its caller.  
        /// </summary>
        private static FrameInfo Fold = new FrameInfo() { Fold = true };

        // These are the 'raw' patterns that are just parsed form of what the user specified in the TextBox
        private double m_minTimeRelativeMSec;
        private double m_maxTimeRelativeMSec;
        private GroupPattern[] m_groups;
        private Regex[] m_includePats;               // Can have null entries
        private Regex[] m_excludePats;               // Can have null entries
        private Regex[] m_foldPats;                  // Can have null entries
        private bool[] m_scenarioIncluded;
        private StackSource m_baseStackSource;
        private ScalingPolicyKind m_scalingPolicy;

        // To avoid a lot of regular expression matching, we remember for a given frame ID the pattern it matched 
        // This allows us to avoid string matching on all but the first lookup of a given frame.  
        private FrameInfo[] m_frameIdToFrameInfo;

        // Once we have applied the regular expression to a group, we have a string, we need to find the
        // 'canonical' FrameIndex associated with that name, this mapping does that.  
        private Dictionary<string, StackSourceFrameIndex> m_GroupNameToFrameInfo;
        private Dictionary<string, FrameInfo> m_ByFrameName;

        /// <summary>
        /// We cache information about stacks we have previously seen so we can short-circuit work. 
        /// TODO make dynamic.   
        /// 
        /// Note when this value is 4096 some memory profiles are VERY sluggish.  Don't make it too
        /// small unless it is adaptive.  
        /// </summary>
        private const int StackInfoCacheSize = 4096 * 8;             // Must be a power of 2; 
        private StackInfo[] m_stackInfoCache;
        #endregion
    }
}
