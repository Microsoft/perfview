using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Analysis.GC;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Parsers.ClrPrivate;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Media;

namespace PerfView
{
    /// <summary>
    /// All managed heap related events
    /// </summary>
    public enum HeapEvents
    {
        Unknown,

        CPUSample,

        ContentionStart,
        ContentionStop,

        GCAllocationTick,

        GCHeapStats,

        BGCAllocWaitStart,
        BGCAllocWaitStop,

        GCOptimized,

        GCCreateSegment,
        GCFreeSegment,

        GCCreateConcurrentThread,
        GCTerminateConcurrentThread,

        GCDecision,
        GCSettings,
        GCPerHeapHistory,
        GCGlobalHeapHistory,
        GCFullNotify,

        /// 

        GCFinalizersBegin,
        GCFinalizersEnd,

        GCSuspendEEBegin,
        GCSuspendEEEnd,

        GCStart,
        GCEnd,

        GCRestartEEBegin,
        GCRestartEEEnd,

        // GarbageCollectionPrivate

        GCJoin,
        GCMarkStackRoots,
        GCMarkFinalizeQueueRoots,
        GCMarkHandles,
        GCMarkCards,

        BGCBegin,
        BGC1stNonCondEnd,
        BGC1stConEnd,
        BGC2ndNonConBegin,
        BGC2ndNonConEnd,
        BGC2ndConBegin,
        BGC2ndConEnd,
        BGCPlanEnd,
        BGCSweepEnd,
        BGCDrainMark,
        BGCRevisit,
        BGCOverflow,

        /// //////////////////////////

        FinalizeObject,

        CCWRefCountChange,
        SetGCHandle,
        DestroyGCHandle,

        GCEventMax,

        GCMarkerFirst = GCFinalizersBegin,
        GCMarkerLast = GCMarkCards,

        BGCMarkerFirst = BGCBegin,
        BGCMarkerLast = BGCOverflow,
    }

    /// <summary>
    /// Per event data
    /// </summary>
    public struct HeapEventData
    {
        internal HeapEvents m_event;
        internal double m_time;
        internal object m_data;
    }

    /// <summary>
    /// Sorting thread by name(type) and then CPU sample
    /// </summary>
    public class ThreadMemoryInfoComparer : IComparer<ThreadMemoryInfo>
    {
        public int Compare(ThreadMemoryInfo x, ThreadMemoryInfo y)
        {
            string n1 = x.Name;
            string n2 = y.Name;

            if (n1 == null)
            {
                n1 = String.Empty;
            }

            if (n2 == null)
            {
                n2 = String.Empty;
            }

            int r = n1.CompareTo(n2);

            if (r != 0)
            {
                return -r;
            }
            else
            {
                return -x.CpuSample.CompareTo(y.CpuSample);
            }
        }
    }

    /// <summary>
    /// Per-thread data
    /// </summary>
    public class ThreadMemoryInfo
    {
        private ProcessMemoryInfo m_process;
        private int m_threadID;
        private string m_name;

        internal List<HeapEventData> m_events;
        internal int[] m_histogram;

        internal int m_heapNum = -1;   // User by server GC thread
        internal bool m_backgroundGc;   // User by background GC 

        private int GetCount(HeapEvents e)
        {
            return m_histogram[(int)e];
        }

        public int CLRContentionCount
        {
            get
            {
                return Math.Min(GetCount(HeapEvents.ContentionStart), GetCount(HeapEvents.ContentionStop));
            }
        }


        public int ThreadID
        {
            get
            {
                return m_threadID;
            }
        }

        public string Name
        {
            get
            {
                return m_name;
            }
        }

        public double CpuSample
        {
            get
            {
                return GetCount(HeapEvents.CPUSample) * SampleInterval;
            }
        }

        public double CpuSamplePercent
        {
            get
            {
                return CpuSample * 100 / m_process.TotalCpuSample;
            }
        }

        public double FirstEvent
        {
            get
            {
                return m_events[0].m_time;
            }
        }

        public double LastEvent
        {
            get
            {
                return m_events[m_events.Count - 1].m_time;
            }
        }

        public double SampleInterval;

        internal ThreadMemoryInfo(ProcessMemoryInfo proc, int threadID)
        {
            m_process = proc;
            m_threadID = threadID;
        }

        public void AddEvent(HeapEvents evt, double time, object obj = null)
        {
            switch (evt)
            {
                case HeapEvents.GCAllocationTick:
                    if (m_name == null)
                    {
                        m_name = ".Net";
                    }
                    break;

                case HeapEvents.GCFinalizersBegin:
                    m_name = ".Net Finalizer";
                    break;

                case HeapEvents.BGCBegin:
                    m_name = ".Net BGC";
                    break;

                case HeapEvents.GCJoin:
                    if (m_name == null)
                    {
                        m_name = ".Net GC";
                    }
                    break;

                default:
                    break;
            }

            if (m_events == null)
            {
                m_events = new List<HeapEventData>();
                m_histogram = new int[(int)HeapEvents.GCEventMax];
            }

            HeapEventData data = new HeapEventData();

            data.m_event = evt;
            data.m_time = time;
            data.m_data = obj;

            m_histogram[(int)evt]++;
            m_events.Add(data);
        }
    }

    internal class GcEventExtra
    {
        internal EventIndex GCStartIndex;
        internal TraceThread GCStartThread;
    }

    /// <summary>
    /// Per-process data, extension of GCProcess
    /// </summary>
    internal partial class ProcessMemoryInfo : HeapDiagramGenerator
    {
        private const int OneMB = 1024 * 1024;
        private const double OneMBD = 1024 * 1024;

        protected TraceLog m_traceLog;
        protected PerfViewFile m_dataFile;

        protected Dictionary<int, ThreadMemoryInfo> m_threadInfo = new Dictionary<int, ThreadMemoryInfo>();
        private Dictionary<int, GcEventExtra> m_gcEventExtra = new Dictionary<int, GcEventExtra>();

        internal Dictionary<int, ThreadMemoryInfo> Threads
        {
            get
            {
                return m_threadInfo;
            }
        }

        internal int ProcessID
        {
            get
            {
                return m_procID;
            }
        }

        private StackDecoder m_stackDecoder;
        private StatusBar m_statusBar;

        public ProcessMemoryInfo(TraceLog traceLog, PerfViewFile dataFile, StatusBar statusBar)
        {
            m_traceLog = traceLog;
            m_dataFile = dataFile;
            m_statusBar = statusBar;

            m_stackDecoder = new StackDecoder(m_traceLog);
        }

        private ThreadMemoryInfo GetThread(int threadID)
        {
            ThreadMemoryInfo ret;

            if (!m_threadInfo.TryGetValue(threadID, out ret))
            {
                ret = new ThreadMemoryInfo(this, threadID);
                ret.SampleInterval = m_SampleInterval;

                m_threadInfo[threadID] = ret;
            }

            return ret;
        }

        private GcEventExtra GetGcEventExtra(int gc, bool create = true)
        {
            GcEventExtra data;

            if (!m_gcEventExtra.TryGetValue(gc, out data) && create)
            {
                data = new GcEventExtra();
                m_gcEventExtra[gc] = data;
            }

            return data;
        }

        internal double m_g0Budget;
        internal double m_g3Budget;
        internal double m_g0Alloc;
        internal double m_g3Alloc;

        internal bool seenBadAlloc;

        internal void OnClrEvent(TraceEvent data)
        {
            TraceEventID eventID = data.ID;
            HeapEvents heapEvent = HeapEvents.Unknown;

            // TODO don't use IDs but use individual callbacks.   
            const TraceEventID GCStartEventID = (TraceEventID)1;
            const TraceEventID GCStopEventID = (TraceEventID)2;
            const TraceEventID GCRestartEEStopEventID = (TraceEventID)3;
            const TraceEventID GCHeapStatsEventID = (TraceEventID)4;
            const TraceEventID GCCreateSegmentEventID = (TraceEventID)5;
            const TraceEventID GCFreeSegmentEventID = (TraceEventID)6;
            const TraceEventID GCRestartEEStartEventID = (TraceEventID)7;
            const TraceEventID GCSuspendEEStopEventID = (TraceEventID)8;
            const TraceEventID GCSuspendEEStartEventID = (TraceEventID)9;
            const TraceEventID GCAllocationTickEventID = (TraceEventID)10;
            const TraceEventID GCCreateConcurrentThreadEventID = (TraceEventID)11;
            const TraceEventID GCTerminateConcurrentThreadEventID = (TraceEventID)12;
            const TraceEventID GCFinalizersStopEventID = (TraceEventID)13;
            const TraceEventID GCFinalizersStartEventID = (TraceEventID)14;
            const TraceEventID ContentionStartEventID = (TraceEventID)81;
            const TraceEventID ContentionStopEventID = (TraceEventID)91;

            switch (eventID)
            {
                case GCStartEventID:
                    {
                        var mdata = data as Microsoft.Diagnostics.Tracing.Parsers.Clr.GCStartTraceData;
                        if (mdata != null)
                        {
                            GcEventExtra extra = GetGcEventExtra(mdata.Count);

                            extra.GCStartIndex = data.EventIndex;
                            extra.GCStartThread = data.Thread();
                        }
                    }
                    heapEvent = HeapEvents.GCStart;
                    break;

                case GCStopEventID:
                    heapEvent = HeapEvents.GCEnd;
                    break;

                case GCRestartEEStartEventID:
                    heapEvent = HeapEvents.GCRestartEEBegin;
                    break;

                case GCRestartEEStopEventID:
                    heapEvent = HeapEvents.GCRestartEEEnd;
                    break;

                case GCHeapStatsEventID:
                    heapEvent = HeapEvents.GCHeapStats;
                    break;

                case GCCreateSegmentEventID:
                    heapEvent = HeapEvents.GCCreateSegment;
                    break;

                case GCFreeSegmentEventID:
                    heapEvent = HeapEvents.GCFreeSegment;
                    break;

                case GCSuspendEEStartEventID:
                    heapEvent = HeapEvents.GCSuspendEEBegin;
                    break;

                case GCSuspendEEStopEventID:
                    heapEvent = HeapEvents.GCSuspendEEEnd;
                    break;

                case GCAllocationTickEventID:
                    heapEvent = HeapEvents.GCAllocationTick;
                    {
                        GCAllocationTickTraceData mdata = data as GCAllocationTickTraceData;

                        AllocationTick(mdata, mdata.AllocationKind == GCAllocationKind.Large, mdata.GetAllocAmount(ref seenBadAlloc) / OneMBD);
                    }
                    break;

                case GCCreateConcurrentThreadEventID:
                    heapEvent = HeapEvents.GCCreateConcurrentThread;
                    break;

                case GCTerminateConcurrentThreadEventID:
                    heapEvent = HeapEvents.GCTerminateConcurrentThread;
                    break;

                case GCFinalizersStartEventID:
                    heapEvent = HeapEvents.GCFinalizersBegin;
                    break;

                case GCFinalizersStopEventID:
                    heapEvent = HeapEvents.GCFinalizersEnd;
                    break;

                case ContentionStartEventID:
                    heapEvent = HeapEvents.ContentionStart;
                    break;

                case ContentionStopEventID:
                    heapEvent = HeapEvents.ContentionStop;
                    break;

                default:
                    break;
            }

            if (heapEvent != HeapEvents.Unknown)
            {
                ThreadMemoryInfo thread = GetThread(data.ThreadID);

                thread.AddEvent(heapEvent, data.TimeStampRelativeMSec);
            }
        }

        internal void OnClrPrivateEvent(TraceEvent data)
        {
            TraceEventID eventID = data.ID;

            HeapEvents heapEvent = HeapEvents.Unknown;

            // TODO don't use IDs but use individual callbacks. 
            const TraceEventID GCDecisionEventID = (TraceEventID)1;
            const TraceEventID GCSettingsEventID = (TraceEventID)2;
            const TraceEventID GCOptimizedEventID = (TraceEventID)3;
            const TraceEventID GCPerHeapHistoryEventID = (TraceEventID)4;
            const TraceEventID GCGlobalHeapHistoryEventID = (TraceEventID)5;
            const TraceEventID GCJoinEventID = (TraceEventID)6;
            const TraceEventID GCMarkStackRootsEventID = (TraceEventID)7;
            const TraceEventID GCMarkFinalizeQueueRootsEventID = (TraceEventID)8;
            const TraceEventID GCMarkHandlesEventID = (TraceEventID)9;
            const TraceEventID GCMarkCardsEventID = (TraceEventID)10;
            const TraceEventID GCBGCStartEventID = (TraceEventID)11;
            const TraceEventID GCBGC1stNonCondStopEventID = (TraceEventID)12;
            const TraceEventID GCBGC1stConStopEventID = (TraceEventID)13;
            const TraceEventID GCBGC2ndNonConStartEventID = (TraceEventID)14;
            const TraceEventID GCBGC2ndNonConStopEventID = (TraceEventID)15;
            const TraceEventID GCBGC2ndConStartEventID = (TraceEventID)16;
            const TraceEventID GCBGC2ndConStopEventID = (TraceEventID)17;
            const TraceEventID GCBGCPlanStopEventID = (TraceEventID)18;
            const TraceEventID GCBGCSweepStopEventID = (TraceEventID)19;
            const TraceEventID GCBGCDrainMarkEventID = (TraceEventID)20;
            const TraceEventID GCBGCRevisitEventID = (TraceEventID)21;
            const TraceEventID GCBGCOverflowEventID = (TraceEventID)22;
            const TraceEventID GCBGCAllocWaitStartEventID = (TraceEventID)23;
            const TraceEventID GCBGCAllocWaitStopEventID = (TraceEventID)24;
            const TraceEventID GCFullNotifyEventID = (TraceEventID)25;

            switch (eventID)
            {
                case GCDecisionEventID:
                    heapEvent = HeapEvents.GCDecision;
                    break;

                case GCSettingsEventID:
                    heapEvent = HeapEvents.GCSettings;
                    break;

                case GCOptimizedEventID:
                    heapEvent = HeapEvents.GCOptimized;
                    {
                        GCOptimizedTraceData mdata = data as GCOptimizedTraceData;

                        if (mdata.GenerationNumber == 0)
                        {
                            m_g0Budget = mdata.DesiredAllocation;
                            m_g0Alloc = mdata.NewAllocation;
                        }
                        else
                        {
                            m_g3Budget = mdata.DesiredAllocation;
                            m_g3Alloc = mdata.NewAllocation;
                        }
                    }
                    break;

                case GCPerHeapHistoryEventID:
                    heapEvent = HeapEvents.GCPerHeapHistory;
                    break;

                case GCGlobalHeapHistoryEventID:
                    heapEvent = HeapEvents.GCGlobalHeapHistory;
                    {
                        GCGlobalHeapHistoryTraceData mdata = data as GCGlobalHeapHistoryTraceData;

                        m_heapCount = mdata.NumHeaps;
                    }
                    break;

                case GCJoinEventID:
                    heapEvent = HeapEvents.GCJoin;
                    break;

                case GCMarkStackRootsEventID:
                    heapEvent = HeapEvents.GCMarkStackRoots;
                    break;

                case GCMarkFinalizeQueueRootsEventID:
                    heapEvent = HeapEvents.GCMarkFinalizeQueueRoots;
                    break;

                case GCMarkHandlesEventID:
                    heapEvent = HeapEvents.GCMarkHandles;
                    break;

                case GCMarkCardsEventID:
                    heapEvent = HeapEvents.GCMarkCards;
                    break;

                case GCBGCStartEventID:
                    heapEvent = HeapEvents.BGCBegin;
                    break;

                case GCBGC1stNonCondStopEventID:
                    heapEvent = HeapEvents.BGC1stNonCondEnd;
                    break;

                case GCBGC1stConStopEventID:
                    heapEvent = HeapEvents.BGC1stConEnd;
                    break;

                case GCBGC2ndNonConStartEventID:
                    heapEvent = HeapEvents.BGC2ndNonConBegin;
                    break;

                case GCBGC2ndNonConStopEventID:
                    heapEvent = HeapEvents.BGC2ndNonConEnd;
                    break;

                case GCBGC2ndConStartEventID:
                    heapEvent = HeapEvents.BGC2ndConBegin;
                    break;

                case GCBGC2ndConStopEventID:
                    heapEvent = HeapEvents.BGC2ndConEnd;
                    break;

                case GCBGCPlanStopEventID:
                    heapEvent = HeapEvents.BGCPlanEnd;
                    break;

                case GCBGCSweepStopEventID:
                    heapEvent = HeapEvents.BGCSweepEnd;
                    break;

                case GCBGCDrainMarkEventID:
                    heapEvent = HeapEvents.BGCDrainMark;
                    break;

                case GCBGCRevisitEventID:
                    heapEvent = HeapEvents.BGCRevisit;
                    break;

                case GCBGCOverflowEventID:
                    heapEvent = HeapEvents.BGCOverflow;
                    break;

                case GCBGCAllocWaitStartEventID:
                    heapEvent = HeapEvents.BGCAllocWaitStart;
                    break;

                case GCBGCAllocWaitStopEventID:
                    heapEvent = HeapEvents.BGCAllocWaitStop;
                    break;

                case GCFullNotifyEventID:
                    heapEvent = HeapEvents.GCFullNotify;
                    break;

                //    FinalizeObject,
                //    CCWRefCountChange,
                //    SetGCHandle,
                //    DestroyGCHandle,
                default:
                    break;
            }

            if (heapEvent != HeapEvents.Unknown)
            {
                ThreadMemoryInfo thread = GetThread(data.ThreadID);

                thread.AddEvent(heapEvent, data.TimeStampRelativeMSec);
            }
        }

        internal void DumpThreadInfo(HtmlWriter writer)
        {
            writer.WriteLine("<pre>");

            foreach (ThreadMemoryInfo v in m_threadInfo.Values.OrderByDescending(e => e.CpuSample))
            {
                writer.Write("Thread {0}, {1} samples, {2}", v.ThreadID, v.CpuSample, v.Name);

                int count = v.m_events.Count;

                if (count != 0)
                {
                    writer.Write(", {0:N3} .. {1:N3} ms", v.m_events[0].m_time, v.m_events[count - 1].m_time);
                }

                writer.WriteLine();

                for (int i = 0; i < v.m_histogram.Length; i++)
                {
                    int val = v.m_histogram[i];

                    if (val != 0)
                    {
                        writer.WriteLine("  {0},{1}", val, (HeapEvents)i);
                    }
                }
            }

            writer.WriteLine("</pre>");
        }

        private Microsoft.Diagnostics.Tracing.Analysis.TraceProcess m_process;
        private Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntime m_runtime;
        private int m_heapCount;
        private double m_SampleInterval;
        private TraceEventSource m_source;

        internal double TotalCpuSample
        {
            get
            {
                return m_process.CPUMSec;
            }
        }

        internal List<TraceGC> GcEvents
        {
            get
            {
                if (m_runtime != null)
                {
                    return m_runtime.GC.GCs;
                }
                else
                {
                    return null;
                }
            }
        }

        private int m_procID;
        private Guid kernelGuid;
        private Dictionary<int /*pid*/, Microsoft.Diagnostics.Tracing.Analysis.TraceProcess> m_processLookup;

        /// <summary>
        /// Event filtering by process ID. Called in ForwardEventEnumerator::MoveNext
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        private bool FilterEvent(TraceEvent data)
        {
            if (data.ProcessID == m_procID)
            {
                if (m_process == null && m_processLookup.ContainsKey(data.ProcessID))
                {
                    m_process = m_processLookup[data.ProcessID];
                    m_runtime = Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.LoadedDotNetRuntime(m_process);
                }

                if (m_source == null)
                {
                    m_source = data.Source;
                    FirstEventTime = data.TimeStampRelativeMSec;
                }

                LastEventTime = data.TimeStampRelativeMSec;

                if (data.ProviderGuid == kernelGuid)
                {
                }

                return true;
            }
            return false;
        }

        internal bool HasVmAlloc
        {
            get
            {
                return m_MaxVMSize > 0;
            }
        }

        // Single entry for 1 mb of committed memory, 256 + byte
        // 1 gb = 256 kb memory
        // 64 gb = 16 mb memory
        private Dictionary<ulong, ModuleClass[]> m_MemMap;
        private ulong m_VMSize;
        private ulong[] m_ModuleVMSize;
        private ulong m_MaxVMSize;
        private const int PageSize = 4096;
        private List<double> m_VMCurve;

        internal void OnVirtualMem(VirtualAllocTraceData data)
        {
            VirtualAllocTraceData.VirtualAllocFlags flags = data.Flags;

            ModuleClass alloc = ModuleClass.Free;

            if ((flags & VirtualAllocTraceData.VirtualAllocFlags.MEM_COMMIT) != 0)
            {
                alloc = ModuleClass.Unknown;

                CallStackIndex s = m_stackDecoder.FirstFrame(data.EventIndex);

                while (s != CallStackIndex.Invalid)
                {
                    ModuleClass old = alloc;

                    alloc = m_stackDecoder.GetModuleClass(s);

                    if ((old == ModuleClass.OSUser) && (alloc != old))
                    {
                        break;
                    }

                    s = m_stackDecoder.GetCaller(s);
                }
            }
            else if ((flags & (VirtualAllocTraceData.VirtualAllocFlags.MEM_DECOMMIT | VirtualAllocTraceData.VirtualAllocFlags.MEM_RELEASE)) == 0)
            {
                return;
            }


            if (m_MemMap == null)
            {
                m_MemMap = new Dictionary<ulong, ModuleClass[]>();
                m_ModuleVMSize = new ulong[(int)(ModuleClass.Max)];
            }

            Debug.Assert((data.BaseAddr % PageSize) == 0);
            Debug.Assert((data.Length % PageSize) == 0);

            ulong addr = data.BaseAddr;

            // TODO because of the algorithm below, we can't handle very large blocks of memory
            // because it is linear in size of the alloc or free.  It turns out that sometimes
            // we free very large chunks.  Thus cap it.  This is not accurate but at least we
            // complete.   
            long len = data.Length / PageSize;
            if (len > 0x400000)     // Cap it at 4M pages = 16GB chunks.  
            {
                len = 0x400000;
            }

            while (len > 0)
            {
                ModuleClass[] bit;

                ulong region = addr / OneMB;

                if (!m_MemMap.TryGetValue(region, out bit))
                {
                    bit = new ModuleClass[OneMB / PageSize];

                    m_MemMap[region] = bit;
                }

                int offset = (int)(addr % OneMB) / PageSize;

                if (alloc != bit[offset])
                {
                    ModuleClass old = bit[offset];

                    bit[offset] = alloc;

                    if (alloc != ModuleClass.Free)
                    {
                        if (old == ModuleClass.Free)
                        {
                            m_ModuleVMSize[(int)alloc] += PageSize;

                            m_VMSize += PageSize;

                            if (m_VMSize > m_MaxVMSize)
                            {
                                m_MaxVMSize = m_VMSize;
                            }
                        }
                    }
                    else
                    {
                        m_ModuleVMSize[(int)old] -= PageSize;

                        m_VMSize -= PageSize;
                    }
                }

                addr += PageSize;
                len--;
            }

            if (m_VMCurve == null)
            {
                m_VMCurve = new List<double>();
            }

            double clrSize = m_ModuleVMSize[(int)ModuleClass.Clr] / OneMBD;
            double graphSize = m_ModuleVMSize[(int)ModuleClass.OSGraphics] / OneMBD;

            double win8StoreSize;

            if (m_stackDecoder.WwaHost)
            {
                win8StoreSize = m_ModuleVMSize[(int)ModuleClass.JScript] / OneMBD;
            }
            else
            {
                win8StoreSize = m_ModuleVMSize[(int)ModuleClass.Win8Store] / OneMBD;
            }

            m_VMCurve.Add(data.TimeStampRelativeMSec);
            m_VMCurve.Add(clrSize);
            m_VMCurve.Add(clrSize + graphSize);
            m_VMCurve.Add(clrSize + graphSize + win8StoreSize);
            m_VMCurve.Add(m_VMSize / OneMBD);
        }

        public bool LoadEvents(int procID, int sampleInterval100ns)
        {
            m_procID = procID;
            m_SampleInterval = sampleInterval100ns / 10000.0;

            // Filter to process
            TraceEvents processEvents = m_traceLog.Events.Filter(FilterEvent);

            // Get Dispatcher
            TraceEventDispatcher source = processEvents.GetSource();

            kernelGuid = KernelTraceEventParser.ProviderGuid;

            // Hookup events
            source.Clr.All += OnClrEvent;

            ClrPrivateTraceEventParser clrPrivate = new ClrPrivateTraceEventParser(source);
            clrPrivate.All += OnClrPrivateEvent;

            KernelTraceEventParser kernel = source.Kernel;

            kernel.PerfInfoSample += delegate (SampledProfileTraceData data)
            {
                ThreadMemoryInfo thread = GetThread(data.ThreadID);

                thread.AddEvent(HeapEvents.CPUSample, data.TimeStampRelativeMSec);
            };

            kernel.VirtualMemAlloc += OnVirtualMem;
            kernel.VirtualMemFree += OnVirtualMem;

            m_processLookup = new Dictionary<int, Microsoft.Diagnostics.Tracing.Analysis.TraceProcess>();

            // Process all events into GCProcess lookup dictionary
            Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.NeedLoadedDotNetRuntimes(source);
            Microsoft.Diagnostics.Tracing.Analysis.TraceProcessesExtensions.AddCallbackOnProcessStart(source, proc =>
            {
                Microsoft.Diagnostics.Tracing.Analysis.TraceProcessesExtensions.SetSampleIntervalMSec(proc, sampleInterval100ns);
                proc.Log = m_traceLog;
            });
            source.Process();
            foreach (var proc in Microsoft.Diagnostics.Tracing.Analysis.TraceProcessesExtensions.Processes(source))
            {
                if (Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.LoadedDotNetRuntime(proc) != null)
                {
                    m_processLookup.Add(proc.ProcessID, proc);
                }
            }

            // Get the process we want
            if (!m_processLookup.ContainsKey(procID))
            {
                return false;
            }

            m_process = m_processLookup[procID];
            m_runtime = Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.LoadedDotNetRuntime(m_process);
            return true;
        }

        public List<Metric> GetMetrics()
        {
            List<Metric> metrics = new List<Metric>();

            string version = m_runtime.RuntimeVersion;

            if (!String.IsNullOrEmpty(version))
            {
                if (version.StartsWith("V ", StringComparison.OrdinalIgnoreCase))
                {
                    version = version.Substring(2);
                }

                metrics.Add(new Metric("Version", version));
            }

            metrics.Add(new Metric("Cpu", TotalCpuSample, Toolbox.TimeFormatN0));
            metrics.Add(new Metric("GC Cpu", m_runtime.GC.Stats().TotalCpuMSec, Toolbox.TimeFormatN0));
            metrics.Add(new Metric("GC %", m_runtime.GC.Stats().TotalCpuMSec * 100.0 / m_process.CPUMSec, Toolbox.PercentageFormat));
            metrics.Add(new Metric("GC Pause", m_runtime.GC.Stats().TotalPauseTimeMSec, Toolbox.TimeFormatN0));
            metrics.Add(new Metric("Thread #", m_threadInfo.Count));

            if (m_process.PeakWorkingSet != 0)
            {
                metrics.Add(new Metric("Peak VM", m_process.PeakVirtual / 1000000.0, Toolbox.MemoryFormatN0));
            }

            if (m_process.PeakWorkingSet != 0)
            {
                metrics.Add(new Metric("Peak WS", m_process.PeakWorkingSet / 1000000.0, Toolbox.MemoryFormatN0));
            }

            metrics.Add(new Metric("Alloc", m_runtime.GC.Stats().TotalAllocatedMB, Toolbox.MemoryFormatN0));
            metrics.Add(new Metric("Max heap", m_runtime.GC.Stats().MaxSizePeakMB, Toolbox.MemoryFormatN0));

            metrics.Add(new Metric("Heap #", m_heapCount));
            metrics.Add(new Metric("GC", m_runtime.GC.Stats().Count));
            metrics.Add(new Metric("Gen0 GC", (m_runtime.GC.Generations()[0] != null) ? m_runtime.GC.Generations()[0].Count : 0));
            metrics.Add(new Metric("Gen1 GC", (m_runtime.GC.Generations()[1] != null) ? m_runtime.GC.Generations()[1].Count : 0));
            metrics.Add(new Metric("Gen2 GC", (m_runtime.GC.Generations()[2] != null) ? m_runtime.GC.Generations()[2].Count : 0));

            metrics.Add(new Metric("FirstEvent", FirstEventTime, Toolbox.TimeFormatN0));
            metrics.Add(new Metric("LastEvent", LastEventTime, Toolbox.TimeFormatN0));

            if (m_MemMap != null)
            {
                metrics.Add(new Metric("\u2228 VMCmt", m_MaxVMSize / OneMBD, Toolbox.MemoryFormatN0));
            }

            return metrics;
        }

        public DiagramData RenderLegend(int width, int height, int threads)
        {
            DiagramData data = new DiagramData();

            data.dataFile = m_traceLog;
            data.events = m_runtime.GC.GCs;
            data.procID = m_process.ProcessID;
            data.threads = m_threadInfo;
            data.allocsites = m_allocSites;
            data.drawLegend = true;
            data.drawThreadCount = threads;

            RenderDiagram(width, height, data);

            return data;
        }

        public DiagramData RenderDiagram(
            int width, int height,
            double start, double end,
            bool gcEvents,
            int threadCount,
            bool drawMarker,
            bool alloctick)
        {
            DiagramData data = new DiagramData();

            data.dataFile = m_traceLog;
            data.events = m_runtime.GC.GCs;
            data.procID = m_process.ProcessID;
            data.threads = m_threadInfo;
            data.allocsites = m_allocSites;
            data.vmCurve = m_VMCurve;
            data.vmMaxVM = m_MaxVMSize / OneMBD;
            data.wwaHost = m_stackDecoder.WwaHost;

            data.startTime = start;
            data.endTime = end;
            data.drawGCEvents = gcEvents;
            data.drawThreadCount = threadCount;
            data.drawMarker = drawMarker;
            data.drawAllocTicks = alloctick;

            RenderDiagram(width, height, data);

            return data;
        }

        public void SaveDiagram(string fileName, bool xps)
        {
            fileName = Path.ChangeExtension(fileName, null).Replace(" ", "") + "_" + m_process.ProcessID;

            if (xps)
            {
                fileName = Toolbox.GetSaveFileName(fileName, ".xps", "XPS");
            }
            else
            {
                fileName = Toolbox.GetSaveFileName(fileName, ".png", "PNG");
            }

            if (fileName != null)
            {
                int width = 1280;
                int height = 720;

                DiagramData data = RenderDiagram(width, height, FirstEventTime, LastEventTime, true, 100, true, true);

                if (xps)
                {
                    Toolbox.SaveAsXps(data.visual, width, height, fileName);
                }
                else
                {
                    Toolbox.SaveAsPng(data.visual, width, height, fileName);
                }
            }
        }

        internal double FirstEventTime = -1;
        internal double LastEventTime = 0;

        #region Allocation Tick
        // AllocTickKey -> int
        private Dictionary<AllocTick, int> m_typeMap = new Dictionary<AllocTick, int>(new AllocTickComparer());

        // int -> AllocTickKey
        internal List<AllocTick> m_allocSites = new List<AllocTick>();
        private bool m_hasBadAllocTick;

        private void AddAlloc(AllocTick key, bool large, double val)
        {
            int id;

            if (!m_typeMap.TryGetValue(key, out id))
            {
                id = m_typeMap.Count;

                m_allocSites.Add(key);
                m_typeMap[key] = id;
            }

            if (val < 0)
            {
                m_hasBadAllocTick = true;
            }

            if (m_hasBadAllocTick)
            {
                // Clap this between 90K and 110K (for small objs) and 90K to 2Meg (for large obects).  
                val = Math.Max(val, .090);
                val = Math.Min(val, large ? 2 : .11);
            }

            m_allocSites[id].Add(large, val);
        }

        public void AllocationTick(GCAllocationTickTraceData data, bool large, double value)
        {
            AllocTick key = new AllocTick();

            // May not have type name prior to 4.5
            if (!String.IsNullOrEmpty(data.TypeName))
            {
                key.m_type = data.TypeName;
            }

            TraceCallStack stack = data.CallStack();

            // Walk the call stack to find module above clr
            while ((stack != null) && (stack.Caller != null) && stack.CodeAddress.ModuleName.IsClr())
            {
                stack = stack.Caller;
            }

            if (stack != null)
            {
                key.m_caller1 = stack.CodeAddress.CodeAddressIndex;

                stack = stack.Caller;

                // Walk call stack to find module above mscorlib
                while ((stack != null) && (stack.Caller != null) && stack.CodeAddress.ModuleName.IsMscorlib())
                {
                    stack = stack.Caller;
                }

                if (stack != null)
                {
                    key.m_caller2 = stack.CodeAddress.CodeAddressIndex;
                }
            }

            AddAlloc(key, large, value);
        }
        #endregion

    }

    /// <summary>
    /// Data passed to HeapDiagram
    /// </summary>
    internal class DiagramData
    {
        internal TraceLog dataFile;
        internal int procID;
        internal List<TraceGC> events;
        internal Dictionary<int, ThreadMemoryInfo> threads;
        internal List<AllocTick> allocsites;
        internal List<double> vmCurve;
        internal double vmMaxVM;
        internal bool wwaHost;

        internal double startTime;
        internal double endTime;

        internal bool drawGCEvents;
        internal int drawThreadCount;
        internal bool drawAllocTicks;
        internal bool drawLegend;
        internal bool drawMarker;

        internal double x0;
        internal double x1;
        internal double t0;
        internal double t1;
        internal Visual visual;
    }


}

