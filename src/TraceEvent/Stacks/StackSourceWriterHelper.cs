using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Microsoft.Diagnostics.Tracing.Stacks
{
    internal static class StackSourceWriterHelper
    {
        /// <summary>
        /// we want to identify the thread for every sample to prevent from 
        /// overlaping of samples for the concurrent code so we group the samples by Threads
        /// this method also sorts the samples by relative time (ascending)
        /// </summary>
        internal static IReadOnlyDictionary<ThreadInfo, List<Sample>> GetSortedSamplesPerThread(StackSource stackSource)
        {
            var samplesPerThread = new Dictionary<ThreadInfo, List<Sample>>();

            stackSource.ForEach(sample =>
            {
                var stackIndex = sample.StackIndex;

                while (stackIndex != StackSourceCallStackIndex.Invalid)
                {
                    var frameName = stackSource.GetFrameName(stackSource.GetFrameIndex(stackIndex), false);

                    // we walk the stack up until we find the Thread name
                    if (!frameName.StartsWith("Thread ("))
                    {
                        stackIndex = stackSource.GetCallerIndex(stackIndex);
                        continue;
                    }

                    // we assume that the next caller is always process
                    var processStackIndex = stackSource.GetCallerIndex(stackIndex);
                    var processFrameName = processStackIndex == StackSourceCallStackIndex.Invalid
                        ? "Unknown"
                        : stackSource.GetFrameName(stackSource.GetFrameIndex(processStackIndex), false);

                    var threadInfo = new ThreadInfo(frameName, processFrameName);

                    if (!samplesPerThread.TryGetValue(threadInfo, out var samples))
                        samplesPerThread[threadInfo] = samples = new List<Sample>();

                    samples.Add(new Sample(sample.StackIndex, sample.TimeRelativeMSec, sample.Metric, -1, -1));

                    return;
                }

                // Sample with no Thread assigned - it's most probably a "Process" sample, we just ignore it
            });

            foreach (var samples in samplesPerThread.Values)
            {
                // all samples in the StackSource should be sorted, but we want to ensure it
                samples.Sort(CompareSamplesByTime);
            }

            return samplesPerThread;
        }

        /// <summary>
        /// all the samples that we have are leafs (last sample in the call stack)
        /// this method walks the stack up to the beginning and merges the samples and outputs them in proper order
        /// </summary>
        internal static IReadOnlyList<ProfileEvent> GetProfileEvents(StackSource stackSource, IReadOnlyList<Sample> leafs,
            Dictionary<string, int> exportedFrameNameToExportedFrameId, Dictionary<int, FrameInfo> exportedFrameIdToExportedNameAndCallerId)
        {
            var results = new List<ProfileEvent>(leafs.Count * 20);

            var previousSamples = new List<Sample>(30);
            var currentSamples = new List<Sample>(30);

            // we use stack here because we want a certain order: from the root to the leaf
            var stackIndexesToHandle = new Stack<StackSourceCallStackIndex>();

            foreach (var leafSample in leafs)
            {
                // walk the stack first
                var stackIndex = leafSample.StackIndex;
                while (stackIndex != StackSourceCallStackIndex.Invalid)
                {
                    stackIndexesToHandle.Push(stackIndex);

                    stackIndex = stackSource.GetCallerIndex(stackIndex);
                }

                // add sample for every method on the stack
                int depth = -1;
                int callerFrameId = -1;
                while (stackIndexesToHandle.Count > 0)
                {
                    stackIndex = stackIndexesToHandle.Pop();
                    depth++;

                    var frameIndex = stackSource.GetFrameIndex(stackIndex);
                    if (frameIndex == StackSourceFrameIndex.Broken || frameIndex == StackSourceFrameIndex.Invalid)
                        continue;

                    var frameName = stackSource.GetFrameName(frameIndex, false);
                    if (string.IsNullOrEmpty(frameName))
                        continue;

                    if (!exportedFrameNameToExportedFrameId.TryGetValue(frameName, out int exportedFrameId))
                        exportedFrameNameToExportedFrameId.Add(frameName, exportedFrameId = exportedFrameNameToExportedFrameId.Count);

                    // the time and metric are the same as for the leaf sample
                    currentSamples.Add(new Sample(stackIndex, leafSample.RelativeTime, leafSample.Metric, depth, exportedFrameId));

                    if (!exportedFrameIdToExportedNameAndCallerId.ContainsKey(exportedFrameId))
                    {
                        // in the future we could identify the categories in a more advance way
                        // and split JIT, GC, Runtime, Libraries and ASP.NET Code into separate categories
                        int index = frameName.IndexOf('!');
                        string category = index > 0 ? frameName.Substring(0, index) : string.Empty;
                        string shortName = index > 0 ? frameName.Substring(index + 1) : frameName;
                        exportedFrameIdToExportedNameAndCallerId.Add(exportedFrameId, new FrameInfo(callerFrameId, shortName, category));
                    }

                    callerFrameId = exportedFrameId;
                }

                HandleSamples(previousSamples, currentSamples, results);
            }

            // close the remaining samples
            double lastKnownTimestamp = results.Count > 0 ? results[results.Count - 1].RelativeTime : 0.0;
            for (int i = previousSamples.Count - 1; i >= 0; i--)
            {
                var sample = previousSamples[i];
                lastKnownTimestamp = Math.Max(lastKnownTimestamp, sample.RelativeTime + sample.Metric);
                results.Add(new ProfileEvent(ProfileEventType.Close, sample.FrameId, lastKnownTimestamp, sample.Depth));
            }

            return results;
        }

        internal static void HandleSamples(List<Sample> previousSamples, List<Sample> currentSamples, List<ProfileEvent> results)
        {
            // used to ensure that next reported profile event is not starting before the previous one
            // we very often have a situation where there is super small difference
            // between previous sample relative time + metric and next sample relative time
            // sth like:
            // 3.13245014 (last profile event = previous sample relative time + metric)
            // 3.13245 (next sample relative time)
            double lastKnownTimestamp = results.Count > 0 ? results[results.Count - 1].RelativeTime : 0.0;

            int i = 0;
            int max = Math.Min(previousSamples.Count, currentSamples.Count);
            // increase the duration of currently opened samples until they are not continuous
            while (i < max && !AreNotContinuous(previousSamples[i], currentSamples[i]))
            {
                previousSamples[i] = previousSamples[i].IncreaseDuration(currentSamples[i].Metric);
                i++;
            }

            // close the tail samples (from the last to first diff) that don't match
            for (int j = previousSamples.Count - 1; j >= i; j--)
            {
                var sample = previousSamples[j];
                lastKnownTimestamp = Math.Max(sample.RelativeTime + sample.Metric, lastKnownTimestamp);
                results.Add(new ProfileEvent(ProfileEventType.Close, sample.FrameId, lastKnownTimestamp, sample.Depth));
                previousSamples.RemoveAt(j);
            }

            // open the new samples (ascending by depth)
            for (int k = i; k < currentSamples.Count; k++)
            {
                var sample = currentSamples[k];
                lastKnownTimestamp = Math.Max(lastKnownTimestamp, sample.RelativeTime);
                results.Add(new ProfileEvent(ProfileEventType.Open, sample.FrameId, lastKnownTimestamp, sample.Depth));
                previousSamples.Add(sample);
            }

            currentSamples.Clear();
        }

        internal static bool Validate(IReadOnlyList<ProfileEvent> orderedProfileEvents)
        {
            var stack = new Stack<ProfileEvent>();

            var previous = orderedProfileEvents.First();

            foreach (var current in orderedProfileEvents)
            {
                if (previous.RelativeTime > current.RelativeTime)
                {
                    return false;
                }

                if (double.Parse(previous.RelativeTime.ToString("R", CultureInfo.InvariantCulture)) > double.Parse(current.RelativeTime.ToString("R", CultureInfo.InvariantCulture)))
                {
                    return false;
                }

                if (current.Type == ProfileEventType.Open)
                {
                    stack.Push(current);
                }
                else if (stack.Count == 0)
                {
                    // we have a closing event, but there is no corresponding open event
                    return false;
                }
                else
                {
                    previous = stack.Pop();

                    // the closing event must be closing an Open event of the same Frame and Depth
                    if (previous.Type != ProfileEventType.Open || previous.Depth != current.Depth || previous.FrameId != current.FrameId || previous.RelativeTime > current.RelativeTime)
                    {
                        return false;
                    }
                }

                previous = current;
            }

            return stack.Count == 0;
        }

        internal static string GetExporterInfo()
        {
            var traceEvent = typeof(StackSourceWriterHelper).GetTypeInfo().Assembly.GetName();

            return $"{traceEvent.Name}@{traceEvent.Version}"; // sth like "Microsoft.Diagnostics.Tracing.TraceEvent@2.0.56.0"
        }

        private static int CompareSamplesByTime(Sample x, Sample y)
        {
            int timeComparison = x.RelativeTime.CompareTo(y.RelativeTime);
            if (timeComparison != 0)
                return timeComparison;

            // in case both samples start at the same time, the one with smaller metric should be the first one
            return x.Metric.CompareTo(y.Metric);
        }

        /// <summary>
        /// this method checks if both samples do NOT belong to the same profile event
        /// </summary>
        private static bool AreNotContinuous(Sample left, Sample right)
        {
            if (left.Depth != right.Depth)
                return true;
            if (left.FrameId != right.FrameId)
                return true;

            return left.RelativeTime + (left.Metric * 1.001) < right.RelativeTime;
        }

        internal readonly struct ThreadInfo : IEquatable<ThreadInfo>
        {
            private static readonly Regex IdExpression = new Regex(@"\((\d+)\)", RegexOptions.Compiled);

            internal ThreadInfo(string threadFrameName, string processFrameName)
            {
                var threadIdMatch = IdExpression.Match(threadFrameName);
                var processIdMatch = IdExpression.Match(processFrameName);

                Name = threadFrameName;
                Id = threadIdMatch.Success ? int.Parse(threadIdMatch.Groups[1].Value) : 0;
                ProcessId = processIdMatch.Success ? int.Parse(processIdMatch.Groups[1].Value) : 0;
            }

            internal ThreadInfo(string name, int id, int processId)
            {
                Name = name;
                Id = id;
                ProcessId = processId;
            }

            public override string ToString() => Name;

            public bool Equals(ThreadInfo other) => Name == other.Name && Id == other.Id && ProcessId == other.ProcessId;

            public override bool Equals(object obj) => obj is ThreadInfo other && Equals(other);

            public override int GetHashCode() => Name.GetHashCode() ^ Id ^ ProcessId;

            #region private
            internal string Name { get; }
            internal int Id { get; }
            internal int ProcessId { get; }
            #endregion private
        }

        internal readonly struct Sample
        {
            internal Sample(StackSourceCallStackIndex stackIndex, double relativeTime, double metric, int depth, int frameId)
            {
                StackIndex = stackIndex;
                RelativeTime = relativeTime;
                Metric = metric;
                Depth = depth;
                FrameId = frameId;
            }

            public override string ToString() => RelativeTime.ToString(CultureInfo.InvariantCulture);

            internal Sample IncreaseDuration(double metric) => new Sample(StackIndex, RelativeTime, Metric + metric, Depth, FrameId);

            #region private
            internal StackSourceCallStackIndex StackIndex { get; }
            internal double RelativeTime { get; }
            internal double Metric { get; }
            internal int Depth { get; }
            internal int FrameId { get; }
            #endregion private
        }

        internal enum ProfileEventType : byte
        {
            Open = 0, Close = 1
        }

        internal readonly struct ProfileEvent
        {
            public ProfileEvent(ProfileEventType type, int frameId, double relativeTime, int depth)
            {
                Type = type;
                FrameId = frameId;
                RelativeTime = relativeTime;
                Depth = depth;
            }

            public override string ToString() => $"{RelativeTime.ToString(CultureInfo.InvariantCulture)} {Type} {FrameId}";

            #region private
            internal ProfileEventType Type { get; }
            internal int FrameId { get; }
            internal double RelativeTime { get; }
            internal int Depth { get; }
            #endregion private
        }

        internal readonly struct FrameInfo
        {
            public FrameInfo(int parentId, string frameName, string category)
            {
                ParentId = parentId;
                Name = frameName;
                Category = category;
            }

            #region private
            internal int ParentId { get; }
            internal string Name { get; }
            internal string Category { get; }
            #endregion private
        }
    }
}
