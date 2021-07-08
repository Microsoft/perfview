using Microsoft.Diagnostics.Tracing.Stacks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Address = System.UInt64;

namespace ClrProfiler
{
    public class ClrProfilerAllocStackSource : StackSource
    {
        public ClrProfilerAllocStackSource(string clrProfilerFileName)
        {
            m_fileName = clrProfilerFileName;
            m_clrProfiler = new ClrProfilerParser();
            m_gcHeap = new GCSimulation(m_clrProfiler);

            m_clrProfiler.ReadFile(m_fileName);
        }

        public override void ForEach(Action<StackSourceSample> callback)
        {
            var sample = new StackSourceSample(this);
            var allocs = m_gcHeap.Allocs;
            for (int i = 0; i < allocs.Count; i++)
            {
                sample.SampleIndex = (StackSourceSampleIndex)i;
                Debug.Assert(allocs[i].AllocId != ProfilerAllocID.Null);
                sample.StackIndex = (StackSourceCallStackIndex)(m_clrProfiler.StackIdLimit + (int)allocs[i].AllocId);
                sample.Metric = allocs[i].Size;
                sample.TimeRelativeMSec = allocs[i].MsecFromStart;

                // TODO this is for debugging can remove
                // var stackId = m_clrProfilerParser.GetAllocStack(Allocs[i].AllocId);
                // var clrProfilerStr = new ProfilerStack(m_clrProfilerParser, stackId).ToString();
                // var stackSourceStr = this.ToString(sample.StackIndex);

                // We expect to add the 'alloc' frame and no more.  
                Debug.Assert(StackDepth(sample.StackIndex) ==
                             new ProfilerStack(m_clrProfiler, m_clrProfiler.GetAllocStack(allocs[i].AllocId)).Depth + 1);
                callback(sample);
            }
        }
        public override StackSourceCallStackIndex GetCallerIndex(StackSourceCallStackIndex callStackIndex)
        {
            // a call stack index might be a CLRProfiler call stack index, or it might be CLRProfiler allocation index
            if ((uint)callStackIndex < (uint)m_clrProfiler.StackIdLimit)
            {
                ProfilerStackTraceID index = m_clrProfiler.NextFrame((ProfilerStackTraceID)callStackIndex);
                if (index == ProfilerStackTraceID.Null)
                {
                    return StackSourceCallStackIndex.Invalid;
                }

                var ret = (StackSourceCallStackIndex)index;
                Debug.Assert(ret != callStackIndex);
                return ret;
            }
            else
            {
                // It is a allocation index, so the frame typeName is the nodeId name (offset by the number of methods)
                ProfilerAllocID allocID = (ProfilerAllocID)((int)callStackIndex - (int)m_clrProfiler.StackIdLimit);
                Debug.Assert(allocID != ProfilerAllocID.Null && allocID < m_clrProfiler.AllocIdLimit);
                ProfilerStackTraceID stackId = m_clrProfiler.GetAllocStack(allocID);
                if (stackId == ProfilerStackTraceID.Null)
                {
                    return StackSourceCallStackIndex.Invalid;
                }

                return (StackSourceCallStackIndex)stackId;
            }
        }
        public override StackSourceFrameIndex GetFrameIndex(StackSourceCallStackIndex callStackIndex)
        {
            // a call stack index might be a CLRProfiler call stack index, or it might be CLRProfiler allocation index
            if ((uint)callStackIndex < (uint)m_clrProfiler.StackIdLimit)
            {
                var method = m_clrProfiler.Method((ProfilerStackTraceID)callStackIndex);
                return (StackSourceFrameIndex)((int)method.MethodId + StackSourceFrameIndex.Start);
            }
            else
            {
                // It is a allocation index, so the frame typeName is the nodeId name (offset by the number of methods)
                ProfilerAllocID allocID = (ProfilerAllocID)((int)callStackIndex - (int)m_clrProfiler.StackIdLimit);
                Debug.Assert(allocID != ProfilerAllocID.Null && allocID < m_clrProfiler.AllocIdLimit);
                return (StackSourceFrameIndex)((int)m_clrProfiler.MethodIdLimit + (int)m_clrProfiler.GetAllocTypeId(allocID) +
                    StackSourceFrameIndex.Start);
            }
        }
        public override string GetFrameName(StackSourceFrameIndex frameIndex, bool verboseName)
        {
            if (frameIndex < StackSourceFrameIndex.Start)
            {
                return System.Enum.GetName(typeof(StackSourceFrameIndex), frameIndex);      // TODO can be more efficient
            }

            frameIndex = (StackSourceFrameIndex)(frameIndex - StackSourceFrameIndex.Start);

            // a frame index might be a CLRProfiler method index, or it might be CLRProfiler nodeId index
            if ((uint)frameIndex < (uint)m_clrProfiler.MethodIdLimit)
            {
                return "!" + m_clrProfiler.GetMethodById((ProfilerMethodID)((int)frameIndex)).FullName;
            }
            else
            {
                var typeId = (ProfilerTypeID)((int)frameIndex - (int)m_clrProfiler.MethodIdLimit);
                return "ALLOC " + m_clrProfiler.GetTypeById(typeId).name;
            }
        }
        public override int CallStackIndexLimit
        {
            // We have pseudo-frames for the nodeId being allocated after MaxStackID 
            get { return (int)m_clrProfiler.StackIdLimit + (int)m_clrProfiler.AllocIdLimit; }
        }
        public override int CallFrameIndexLimit
        {
            // We have pseudo-frames for the nodeId being allocated after MaxMethodID
            get { return (int)m_clrProfiler.MethodIdLimit + (int)m_clrProfiler.TypeIdLimit + (int)StackSourceFrameIndex.Start; }
        }
        public override double SampleTimeRelativeMSecLimit
        {
            get { return m_gcHeap.CurrentTimeMSec + 1; }
        }
        public override int SampleIndexLimit
        {
            get
            {
                return m_gcHeap.Allocs.Count;
            }
        }
        public override StackSourceSample GetSampleByIndex(StackSourceSampleIndex sampleIndex)
        {
            if (m_sample == null)
            {
                m_sample = new StackSourceSample(this);
            }

            var allocs = m_gcHeap.Allocs;
            m_sample.SampleIndex = sampleIndex; ;
            m_sample.StackIndex = (StackSourceCallStackIndex)(m_clrProfiler.StackIdLimit + (int)allocs[(int)sampleIndex].AllocId);
            m_sample.Metric = allocs[(int)sampleIndex].Size;
            m_sample.TimeRelativeMSec = allocs[(int)sampleIndex].MsecFromStart;
            return m_sample;
        }

        #region private
        private string m_fileName;
        private ClrProfilerParser m_clrProfiler;
        private GCSimulation m_gcHeap;
        private StackSourceSample m_sample;
        #endregion
    }

    public class ClrProfilerMethodSizeStackSource : StackSource
    {
        public ClrProfilerMethodSizeStackSource(string clrProfilerFileName)
        {
            m_fileName = clrProfilerFileName;
            m_clrProfiler = new ClrProfilerParser();
            m_calls = new GrowableArray<int>(1000000);
            m_clrProfiler.Call += delegate (ProfilerStackTraceID stackId, uint threadId)
            {
                m_calls.Add((int)stackId);
                var method = m_clrProfiler.Method(stackId);
                var stats = (MethodStats)method.UserData;
                if (stats == null)
                {
                    m_totalMethodSize += (int)method.size;
                    m_totalMethodCount++;
                    method.UserData = stats = new MethodStats();
                    // Debug.WriteLine(string.Format("METHOD Size {1,6}: {0}", method.name, method.size));
                }
                stats.count++;
            };
            m_clrProfiler.ReadFile(m_fileName);
            // Debug.WriteLine(string.Format("MethodSize {0} MethodCount {1} callCount {2}", m_totalMethodSize, m_totalMethodCount, m_calls.Count));
        }
        public override void ForEach(Action<StackSourceSample> callback)
        {
            var sample = new StackSourceSample(this);
            Debug.Assert(StackSourceCallStackIndex.Start == 0);         // We assume this in our encoding. 
            Debug.Assert((int)StackSourceCallStackIndex.Invalid == -1);
            for (int i = 0; i < m_calls.Count; i++)
            {
                var stackId = (ProfilerStackTraceID)m_calls[i];
                sample.SampleIndex = (StackSourceSampleIndex)i;
                // subtract 1 so 0 (clrprofiler Scentinal) becomes CallStackIndex Sentinal
                sample.StackIndex = (StackSourceCallStackIndex)(stackId - 1);
                var method = m_clrProfiler.Method(stackId);
                var stats = (MethodStats)method.UserData;
                sample.Metric = (double)method.size / stats.count;
                callback(sample);
            }
        }
        public override StackSourceCallStackIndex GetCallerIndex(StackSourceCallStackIndex callStackIndex)
        {
            Debug.Assert(callStackIndex >= 0);
            // subtract 1 so 0 (clrprofiler Scentinal) becomes CallStackIndex Sentinal
            return (StackSourceCallStackIndex)(m_clrProfiler.NextFrame((ProfilerStackTraceID)(callStackIndex + 1)) - 1);
        }
        public override StackSourceFrameIndex GetFrameIndex(StackSourceCallStackIndex callStackIndex)
        {
            // add 1 to convert from callStackIndex to ProfilerStackTraceID
            var method = m_clrProfiler.Method((ProfilerStackTraceID)(callStackIndex + 1));
            return (StackSourceFrameIndex)((int)method.MethodId + (int)StackSourceFrameIndex.Start);
        }
        public override string GetFrameName(StackSourceFrameIndex frameIndex, bool verboseName)
        {
            return "!" + m_clrProfiler.GetMethodById((ProfilerMethodID)(frameIndex - StackSourceFrameIndex.Start)).name;
        }
        public override int CallStackIndexLimit
        {
            get { return (int)m_clrProfiler.StackIdLimit; }
        }
        public override int CallFrameIndexLimit
        {
            get { return (int)m_clrProfiler.MethodIdLimit + (int)StackSourceFrameIndex.Start; }
        }

        public override StackSourceSample GetSampleByIndex(StackSourceSampleIndex sampleIndex)
        {
            if (m_sample == null)
            {
                m_sample = new StackSourceSample(this);
            }

            var stackId = (ProfilerStackTraceID)m_calls[(int)sampleIndex];
            m_sample.SampleIndex = sampleIndex;
            // subtract 1 so 0 (clrprofiler Scentinal) becomes CallStackIndex Sentinal
            m_sample.StackIndex = (StackSourceCallStackIndex)(stackId - 1);
            var method = m_clrProfiler.Method(stackId);
            var stats = (MethodStats)method.UserData;
            m_sample.Metric = (double)method.size / stats.count;
            return m_sample;
        }
        public override int SampleIndexLimit { get { return m_calls.Count; } }

        public int TotalMethodSize { get { return m_totalMethodSize; } }
        public int TotalMethodCount { get { return m_totalMethodCount; } }
        public int TotalCalls { get { return m_calls.Count; } }
        #region private
        private class MethodStats
        {
            public int count;
        };

        private string m_fileName;
        private ClrProfilerParser m_clrProfiler;
        private GrowableArray<int> m_calls;
        private int m_totalMethodCount;
        private int m_totalMethodSize;
        private StackSourceSample m_sample;
        #endregion
    }

    /// <summary>
    /// This allows you to keep track of allocations that have not been freed.  
    /// </summary>
    internal class GCSimulation
    {
        public GCSimulation(ClrProfilerParser profiler)
        {
            m_clrProfiler = profiler;
            m_relocs = new GrowableArray<Relocation>(256);
            Allocs = new GrowableArray<AllocInfo>(100000);

            m_clrProfiler.GCStart += new ClrProfilerParser.GCEventHandler(GCStart);
            m_clrProfiler.GCEnd += new ClrProfilerParser.GCEventHandler(GCEnd);
            m_clrProfiler.ObjectRangeLive += new ClrProfilerParser.LiveObjectRangeHandler(ObjectRangeLive);
            m_clrProfiler.ObjectRangeRelocation += new ClrProfilerParser.RelocationEventHandler(ObjectRangeRelocation);

            // TODO expose the thread information
            AllocInfo info = new AllocInfo();
            m_clrProfiler.Allocation += delegate (ProfilerAllocID allocId, Address objectAddress, uint threadId)
            {
                Debug.Assert(allocId != ProfilerAllocID.Null);
                info.Size = (int)m_clrProfiler.GetAllocSize(allocId);
                info.AllocId = allocId;

                var stackId = m_clrProfiler.GetAllocStack(allocId);
                info.MsecFromStart = CurrentTimeMSec;

                info.ObjectAddress = objectAddress;
                Allocs.Add(info);
                m_numAllocs++;
            };

            m_clrProfiler.Tick += delegate (int milliSecondsSinceStart)
            {
                CurrentTimeMSec = milliSecondsSinceStart;
            };

        }
        public GrowableArray<AllocInfo> Allocs;
        public int CurrentTimeMSec;

        public ProfilerAllocID GetAllocIdForObjectAddress(Address objectAddress)
        {
            if (!m_areAllocsSorted)
            {
                Allocs.Sort(delegate (AllocInfo x, AllocInfo y) { return ((ulong)x.ObjectAddress).CompareTo((ulong)y.ObjectAddress); });
                m_areAllocsSorted = true;
            }

            int index;
            bool success = Allocs.BinarySearch(objectAddress, out index,
                delegate (Address key, AllocInfo elem) { return ((ulong)key).CompareTo((ulong)elem.ObjectAddress); });
            if (!success)
            {
                return ProfilerAllocID.Null;
            }

            Debug.Assert(Allocs[index].ObjectAddress == objectAddress);
            return Allocs[index].AllocId;
        }

        #region private
        private void ObjectRangeLive(Address startAddress, uint size)
        {
            ObjectRangeRelocation(startAddress, startAddress, size);
        }
        private void ObjectRangeRelocation(Address oldBase, Address newBase, uint size)
        {
            Relocation reloc = new Relocation();
            reloc.SourceStart = oldBase;
            reloc.DiffToTarget = newBase - oldBase;
            reloc.SourceEnd = oldBase + (ulong)size;

            m_relocs.Add(reloc);
        }
        private void GCStart(int gcNumber, bool induced, int condemnedGeneration, List<ProfilerGCSegment> gcSegments)
        {
            m_curGC++;
            m_relocs.Count = 0;
            m_lastRelocIdx = 0;
            m_lastRelocRegionEnd = 0;
            m_areRelocsSorted = false;
            m_gcSegments = gcSegments;

            m_condemedGeneration = condemnedGeneration;
            m_promotedToGeneration = condemnedGeneration + 1;
            if (m_promotedToGeneration > 2)
            {
                m_promotedToGeneration = 2;
            }
        }
        private void GCEnd(int gcNumber, bool induced, int condemnedGeneration, List<ProfilerGCSegment> gcMemoryRanges)
        {
            if (!m_areRelocsSorted)
            {
                m_relocs.Sort(delegate (Relocation x, Relocation y) { return ((ulong)x.SourceStart).CompareTo((ulong)y.SourceStart); });
                m_areRelocsSorted = true;
            }

#if DEBUG
            int mem = 0;
            for (int i = 0; i < Allocs.Count; i++)
            {
                mem += Allocs[i].Size;
            }

            Debug.WriteLine(string.Format("Starting GC {0} condemend generation {1} initial number of objects {2} size {3}",
                gcNumber, condemnedGeneration, Allocs.Count, mem));
#endif

            Debug.Assert(condemnedGeneration <= 2);
            int writeIdx = m_genBoundary[condemnedGeneration];
            AllocInfo[] array = Allocs.UnderlyingArray;
            int survivedMemSize = 0;
            for (int curIdx = writeIdx; curIdx < Allocs.Count; curIdx++)
            {
                if (ApplyRelocsToAlloc(ref array[curIdx]))
                {
                    if (curIdx > writeIdx)
                    {
                        array[writeIdx] = array[curIdx];
                    }

                    survivedMemSize += array[writeIdx].Size;
                    writeIdx++;
                }
            }
            Allocs.Count = writeIdx;
            Debug.WriteLine(string.Format("After GC {0} condemend generation {1} objects that survive {2} total size {3}",
                gcNumber, condemnedGeneration, Allocs.Count, survivedMemSize));
        }

        // Returns true if 'alloc' survivies.  
        private bool ApplyRelocsToAlloc(ref AllocInfo alloc)
        {
            // You survive if your generation is large than the condemed generation.  
            if (alloc.Generation > m_condemedGeneration)
            {
                return true;
            }

            Debug.Assert(m_areRelocsSorted);

            // See if you survived.
            int idx = m_lastRelocIdx;                               // See if you can start where you left off.  
            if (alloc.ObjectAddress < m_lastRelocRegionEnd)
            {
                idx = 0;                                            // Nope, start at the begining. 
            }

            while (idx < m_relocs.Count)
            {
                if (alloc.ObjectAddress < m_relocs[idx].SourceStart)
                {
                    return false;
                }

                if (alloc.ObjectAddress < m_relocs[idx].SourceEnd)
                {
                    alloc.ObjectAddress = alloc.ObjectAddress + m_relocs[idx].DiffToTarget;
                    alloc.Generation = m_promotedToGeneration;
                    return true;
                }
                m_lastRelocRegionEnd = m_relocs[idx].SourceEnd;
                idx++;
                m_lastRelocIdx = idx;
            }
            // TODO FIX NOW, not right when we have segments everywhere.. 

            // OK it did not survive the condemed generation, see if it is in a segment that was not condemed
            if (m_condemedGeneration < 2)
            {
                foreach (var gcSegment in m_gcSegments)
                {
                    if (gcSegment.rangeStart <= alloc.ObjectAddress &&
                        alloc.ObjectAddress < gcSegment.rangeStart + (ulong)gcSegment.rangeLength)
                    {
                        // Note rangeGeneration can == 3 for Large object heap, but this still works because we only even look 
                        // for generations less than 2.  
                        return gcSegment.rangeGeneration > m_condemedGeneration;
                    }
                }
                Debug.Assert(false, "Object in no heap segment!");
            }
            return false;
        }

        private struct Relocation
        {
            public Address SourceStart;
            public Address SourceEnd;       // Points just beyond the region. 
            public ulong DiffToTarget;
        }

        // Private fields
        private GrowableArray<Relocation> m_relocs;
        private int m_condemedGeneration;
        private List<ProfilerGCSegment> m_gcSegments;

        private int m_promotedToGeneration;
        private int m_lastRelocIdx;
        private Address m_lastRelocRegionEnd;
        private bool m_areRelocsSorted;
        private bool m_areAllocsSorted;
        private int m_curGC;
        private ClrProfilerParser m_clrProfiler;
        private int[] m_genBoundary = new int[3];     // index to the first allocation that can be in Generation N (alwasy 0 for gen 2). 
        private long m_numAllocs;
        #endregion
    }

    internal struct AllocInfo
    {
        public int Size;
        public int Generation;
        public ProfilerAllocID AllocId;
        public int MsecFromStart;
        public Address ObjectAddress;
    }
}