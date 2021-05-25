// Copyright (c) Microsoft Corporation.  All rights reserved
// This file is best viewed using outline mode (Ctrl-M Ctrl-O)
//
// This program uses code hyperlinks available as part of the HyperAddin Visual Studio plug-in.
// It is available from http://www.codeplex.com/hyperAddin 
// using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Analysis.GC;
using Microsoft.Diagnostics.Tracing.Analysis.JIT;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Parsers.ClrPrivate;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Parsers.Symbol;
using Microsoft.Diagnostics.Tracing.Stacks;
using Microsoft.Diagnostics.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Address = System.UInt64;

namespace Microsoft.Diagnostics.Tracing.Analysis
{
    /// <summary>
    /// Extension methods to enable TraceManagedProcess
    /// </summary>
    public static class TraceLoadedDotNetRuntimeExtensions
    {
        public static void NeedLoadedDotNetRuntimes(this TraceEventDispatcher source)
        {
            // ensure there are base processes
            source.NeedProcesses();

            if(m_weakCurrentSource.Target != source)
            {
                TraceLoadedDotNetRuntime.SetupCallbacks(source);
            }

            source.UserData["Computers/LoadedDotNetRuntimes"] = new Dictionary<ProcessIndex, DotNetRuntime>();

            m_weakCurrentSource.Target = source;
        }

        public static TraceLoadedDotNetRuntime LoadedDotNetRuntime(this TraceProcess process)
        {
            Debug.Assert(process.Source != null);
            Debug.Assert(m_weakCurrentSource.Target == process.Source);
            Dictionary<ProcessIndex, DotNetRuntime> map = process.Source.UserData["Computers/LoadedDotNetRuntimes"] as Dictionary<ProcessIndex, DotNetRuntime>;
            if (map.ContainsKey(process.ProcessIndex))
            {
                return map[process.ProcessIndex].Runtime;
            }
            else
            {
                return null;
            }
        }

        public static void AddCallbackOnDotNetRuntimeLoad(this TraceProcess process, Action<TraceLoadedDotNetRuntime> OnDotNetRuntimeLoaded)
        {
            Debug.Assert(process.Source != null);
            Debug.Assert(m_weakCurrentSource.Target == process.Source);
            Dictionary<ProcessIndex, DotNetRuntime> map = (Dictionary<ProcessIndex, DotNetRuntime>)process.Source.UserData["Computers/LoadedDotNetRuntimes"];
            if (!map.ContainsKey(process.ProcessIndex))
            {
                map.Add(process.ProcessIndex, new DotNetRuntime());
            }

            map[process.ProcessIndex].OnLoaded += OnDotNetRuntimeLoaded;
        }

        public static void SetMutableTraceEventStackSource(this TraceProcess process, MutableTraceEventStackSource stackSource)
        {
            Debug.Assert(process.Source != null);
            Debug.Assert(m_weakCurrentSource.Target == process.Source);
            Dictionary<ProcessIndex, DotNetRuntime> map = (Dictionary<ProcessIndex, DotNetRuntime>)process.Source.UserData["Computers/LoadedDotNetRuntimes"];
            if (!map.ContainsKey(process.ProcessIndex))
            {
                map.Add(process.ProcessIndex, new DotNetRuntime());
            }

            map[process.ProcessIndex].StackSource = stackSource;
        }

        public static MutableTraceEventStackSource MutableTraceEventStackSource(this TraceProcess process)
        {
            Debug.Assert(process.Source != null);
            Debug.Assert(m_weakCurrentSource.Target == process.Source);
            Dictionary<ProcessIndex, DotNetRuntime> map = (Dictionary<ProcessIndex, DotNetRuntime>)process.Source.UserData["Computers/LoadedDotNetRuntimes"];
            if (map.ContainsKey(process.ProcessIndex))
            {
                return map[process.ProcessIndex].StackSource;
            }
            else
            {
                return null;
            }
        }

        public static bool HasMutableTraceEventStackSource(this TraceEventDispatcher source)
        {
            Dictionary<ProcessIndex, DotNetRuntime> map = (Dictionary<ProcessIndex, DotNetRuntime>)source.UserData["Computers/LoadedDotNetRuntimes"];
            return map.Any(kv => kv.Value.StackSource != null);
        }

#region private
        public class DotNetRuntime
        {
            public Action<TraceLoadedDotNetRuntime> OnLoaded;
            public TraceLoadedDotNetRuntime Runtime;
            public MutableTraceEventStackSource StackSource;
        }

        internal static void OnDotNetRuntimeLoaded(this TraceProcess process, TraceLoadedDotNetRuntime runtime)
        {
            Debug.Assert(process.Source != null);
            Dictionary<ProcessIndex, DotNetRuntime> map = (Dictionary<ProcessIndex, DotNetRuntime>)process.Source.UserData["Computers/LoadedDotNetRuntimes"];
            if (!map.ContainsKey(process.ProcessIndex))
            {
                map.Add(process.ProcessIndex, new DotNetRuntime());
            }

            map[process.ProcessIndex].Runtime = runtime;
            if (map[process.ProcessIndex].OnLoaded != null)
            {
                map[process.ProcessIndex].OnLoaded(runtime);
            }
        }

        internal static void OnDotNetRuntimeUnloaded(this TraceProcess process)
        {
            Debug.Assert(process.Source != null);
            Dictionary<ProcessIndex, DotNetRuntime> map = (Dictionary<ProcessIndex, DotNetRuntime>)process.Source.UserData["Computers/LoadedDotNetRuntimes"];
            if (map.ContainsKey(process.ProcessIndex))
            {
                // remove this runtime, since the process has terminated
                map.Remove(process.ProcessIndex);
            }
        }

        private static WeakReference m_weakCurrentSource = new WeakReference(null); // used to ensure non-concurrent usage
#endregion
    }

    /// <summary>
    /// Extension properties for TraceProcess that include necessary .NET values
    /// 
    /// TODO This implementation is poor at idenitfying the ParentPID, 64bitness, and Start/End times
    /// </summary>
    public class TraceLoadedDotNetRuntime
    {
        /// <summary>
        /// Returns the textual version of the .NET Framework
        /// </summary>
        public string RuntimeVersion { get { return "V " + runtimeVersion.Major + "." + runtimeVersion.Minor + "." + runtimeVersion.Build + "." + runtimeVersion.Revision; } }
        /// <summary>
        /// Returns the .NET startup flags
        /// </summary>
        public StartupFlags StartupFlags { get; internal set; }
        /// <summary>
        /// Date and time of when the runtime was built
        /// This is useful when a more detailed version is not present
        /// </summary>
        public DateTime RuntimeBuiltTime { get; internal set; }

        /// <summary>
        /// Garbage Collector (GC) specific details about this process
        /// </summary>
        public TraceGarbageCollector GC { get; } = new TraceGarbageCollector();
        /// <summary>
        /// Fired on the start of a GC
        /// </summary>
        public event Action<TraceProcess, TraceGC> GCStart = null;
        /// <summary>
        /// Fired at the end of the GC.  Given the nature of the GC, it is possible that multiple GCs will be inflight at the same time.
        /// </summary>
        public event Action<TraceProcess, TraceGC> GCEnd = null;

        /// <summary>
        /// Just-in-time compilation (JIT) specific details about this process
        /// </summary>
        public TraceJitCompiler JIT { get; } = new TraceJitCompiler();

        /// <summary>
        /// Fired when a managed method is starting to compile (jit)
        /// </summary>
        public event Action<TraceProcess, TraceJittedMethod> JITMethodStart = null;
        /// <summary>
        /// Fired when a managed method is done compiling (jitting).  Given the nature of the JIT, it is possible that multiple methods will be compiled at the same time.
        /// </summary>
        public event Action<TraceProcess, TraceJittedMethod> JITMethodEnd = null;

        /// <summary>
        /// Indicates whether any of the jitted method code versions have a known optimization tier
        /// </summary>
        public bool HasAnyKnownOptimizationTier;

        /// <summary>
        /// Indicates whether tiered compilation is enabled
        /// </summary>
        public bool IsTieredCompilationEnabled;

        /// <summary>
        /// An XML representation of the TraceEventProcess (for debugging)
        /// </summary>
        public override string ToString()
        {
            string xml = base.ToString();
            StringBuilder sb = new StringBuilder();

            sb.Append("ClrRuntimeVersion=").Append(XmlUtilities.XmlQuote(RuntimeVersion)).Append(" ");
            sb.Append("ClrStartupFlags=").Append(XmlUtilities.XmlQuote(StartupFlags)).Append(" ");
            sb.Append("/>");

            return xml.Replace("/>", sb.ToString());
        }

#region private

        internal TraceLoadedDotNetRuntime(TraceProcess proc)
        {
            runtimeVersion = new Version(0, 0, 0, 0);
            StartupFlags = StartupFlags.None;
            RuntimeBuiltTime = default(DateTime);
        }

        /// <summary>
        /// Gathers relevant details about the processes in the event source
        /// </summary>
        /// <param name="source"></param>
        internal static void SetupCallbacks(TraceEventDispatcher source)
        {
            bool processGCEvents = true;
            bool processJITEvents = true;

            //
            // Set additional TraceManageProcess properties
            //

            // These parsers create state and we want to collect that so we put it on our 'parsers' list that we serialize.  
            var clrRundownParser = new ClrRundownTraceEventParser(source);
            // See if the source knows about the CLR Private provider, if it does, then 
            var clrPrivate = new ClrPrivateTraceEventParser(source);

            Dictionary<TraceProcess, TraceLoadedDotNetRuntime> processRuntimes = new Dictionary<TraceProcess, TraceLoadedDotNetRuntime>();

            // if any clr event is fired, this process is managed
            Action<TraceEvent> createManagedProc = delegate (TraceEvent data)
            {
                var proc = data.Process(); // this will create an instance, if one does not exist
                TraceLoadedDotNetRuntime mang;
                if (!processRuntimes.TryGetValue(proc, out mang))
                {
                    // duplicate the TraceProcess and create an instance of TraceManagedProcess
                    mang = new TraceLoadedDotNetRuntime(proc);

                    // The TraceProcess infrastructure relies on Kernel Events to properly set key process information (like process name)
                    // Set process name directly if not set
                    // This is needed for linux traces or traces on Windows which do not have backProcessing enabled (very rare)
                    if (string.IsNullOrWhiteSpace(proc.Name))
                    {
                        proc.name = data.ProcessName;
                    }

                    // fire callback and associate this DotNetRuntime with this process
                    proc.OnDotNetRuntimeLoaded(mang);
                    processRuntimes.Add(proc, mang);
                }
            };

            // if applying lifetime, trim loaded managed runtimes when processes are terminated and are out of lifetime
            source.AddCallbackOnProcessStop((p) =>
            {
                // check if we are applying a lifetime model
                if (source.DataLifetimeEnabled())
                {
                    // iterate through all processes and unload the managed runtime from processes
                    //  that have exited and are out of lifetime
                    // immediately removing the runtime for stopped processes is possible, but that
                    //  breaks the contract of how long data is kept with the lifetime
                    foreach (var process in source.Processes())
                    {
                        // continue if the process has not exited yet
                        if (!process.ExitStatus.HasValue)
                        {
                            continue;
                        }

                        if (process.EndTimeRelativeMsec < (p.EndTimeRelativeMsec - source.DataLifetimeMsec))
                        {
                            // remove this managed runtime instance
                            process.OnDotNetRuntimeUnloaded();
                            // remove from the local cache
                            if (processRuntimes.ContainsKey(process))
                            {
                                processRuntimes.Remove(process);
                            }
                        }
                    }
                }
            });

            source.Clr.All += createManagedProc;
            clrPrivate.All += createManagedProc;

            Func<TraceEvent, TraceLoadedDotNetRuntime> currentManagedProcess = delegate (TraceEvent data)
            {
                TraceLoadedDotNetRuntime mang;
                if (!processRuntimes.TryGetValue(data.Process(), out mang))
                {
                    createManagedProc(data);
                    mang = processRuntimes[data.Process()];

                    Debug.Assert(mang != null);
                }

                return mang;
            };

            Action<RuntimeInformationTraceData> doAtRuntimeStart = delegate (RuntimeInformationTraceData data)
           {
               TraceProcess process = data.Process();
               TraceLoadedDotNetRuntime mang = currentManagedProcess(data);

               // replace the current runtimeversion if it is currently not set, or this version has information including revision (eg. qfe number)
               if (mang.runtimeVersion.Major == 0 || data.VMQfeNumber > 0)
               {
                   mang.runtimeVersion = new Version(data.VMMajorVersion, data.VMMinorVersion, data.VMBuildNumber, data.VMQfeNumber);
               }

               mang.StartupFlags = data.StartupFlags;
               // proxy for bitness, given we don't have a traceevent to pass through
               process.Is64Bit = (data.RuntimeDllPath.ToLower().Contains("framework64"));

               if (process.CommandLine.Length == 0)
               {
                   process.CommandLine = data.CommandLine;
               }
           };
            clrRundownParser.RuntimeStart += doAtRuntimeStart;
            source.Clr.RuntimeStart += doAtRuntimeStart;

            var symbolParser = new SymbolTraceEventParser(source);
            symbolParser.ImageIDFileVersion += delegate (FileVersionTraceData data)
            {
                TraceProcess process = data.Process();

                if (string.Equals(data.OrigFileName, "clr.dll", StringComparison.OrdinalIgnoreCase) || string.Equals(data.OrigFileName, "mscorwks.dll", StringComparison.OrdinalIgnoreCase) || string.Equals(data.OrigFileName, "coreclr.dll", StringComparison.OrdinalIgnoreCase))
                {
                    // this will create a mang instance for this process
                    TraceLoadedDotNetRuntime mang = currentManagedProcess(data);
                    Version version;
                    // replace the current runtimeVersion if there is not good revision information
                    if ((mang.runtimeVersion.Major == 0 || mang.runtimeVersion.Revision == 0) && Version.TryParse(data.ProductVersion, out version))
                    {
                        mang.runtimeVersion = new Version(version.Major, version.Minor, version.Build, version.Revision);
                    }

                    if (mang.RuntimeBuiltTime == default(DateTime))
                    {
                        mang.RuntimeBuiltTime = data.BuildTime;
                    }
                }
            };
            symbolParser.ImageID += delegate (ImageIDTraceData data)
            {
                TraceProcess process = data.Process();

                if (string.Equals(data.OriginalFileName, "clr.dll", StringComparison.OrdinalIgnoreCase) || string.Equals(data.OriginalFileName, "mscorwks.dll", StringComparison.OrdinalIgnoreCase) || string.Equals(data.OriginalFileName, "coreclr.dll", StringComparison.OrdinalIgnoreCase))
                {
                    // this will create a mang instance for this process
                    TraceLoadedDotNetRuntime mang = currentManagedProcess(data);
                    // capture the CLR build stamp to provide deeper version information (when version information is not present)
                    if (mang.RuntimeBuiltTime == default(DateTime))
                    {
                        mang.RuntimeBuiltTime = data.BuildTime;
                    }
                }
            };

            //
            // GC
            //
            // Blocking GCs are marked as complete (IsComplete set to true) during RestartEEStop, except for the gen1 GCs that
            // happen right before the NGC2 (full blocking GC) in provisional mode. For the exceptional case we set the gen1 as
            // complete during the NGC2's GCStart at which point we know that's an NGC2 triggered due to provisional mode.
            // Background GCs are marked as complete during GCHeapStats as it does not call RestartEE at the end of a GC.
            //
            if (processGCEvents)
            {
                // log at both startup and rundown
                var clrRundown = new ClrRundownTraceEventParser(source);
                clrRundown.RuntimeStart += doAtRuntimeStart;
                source.Clr.RuntimeStart += doAtRuntimeStart;

                CircularBuffer<ThreadWorkSpan> RecentThreadSwitches = new CircularBuffer<ThreadWorkSpan>(1000);
                source.Kernel.ThreadCSwitch += delegate (CSwitchTraceData data)
                {
                    RecentThreadSwitches.Add(new ThreadWorkSpan(data));
                    TraceProcess tmpProc = data.Process();
                    TraceLoadedDotNetRuntime mang;
                    if (processRuntimes.TryGetValue(tmpProc, out mang))
                    {
                        mang.GC.m_stats.ThreadId2Priority[data.NewThreadID] = data.NewThreadPriority;
                        int heapIndex = mang.GC.m_stats.IsServerGCThread(data.ThreadID);
                        if ((heapIndex > -1) && !(mang.GC.m_stats.ServerGcHeap2ThreadId.ContainsKey(heapIndex)))
                        {
                            mang.GC.m_stats.ServerGcHeap2ThreadId[heapIndex] = data.ThreadID;
                        }
                    }

                    foreach (var pair in processRuntimes)
                    {
                        var proc = pair.Key;
                        mang = pair.Value;

                        TraceGC _gc = TraceGarbageCollector.GetCurrentGC(mang);
                        // If we are in the middle of a GC.
                        if (_gc != null)
                        {
                            if ((_gc.Type != GCType.BackgroundGC) && (mang.GC.m_stats.IsServerGCUsed == 1))
                            {
                                _gc.AddServerGcThreadSwitch(new ThreadWorkSpan(data));
                            }
                        }
                    }
                };

                CircularBuffer<ThreadWorkSpan> RecentCpuSamples = new CircularBuffer<ThreadWorkSpan>(1000);
                source.Kernel.PerfInfoSample += delegate (SampledProfileTraceData data)
                {
                    RecentCpuSamples.Add(new ThreadWorkSpan(data));
                    if (source.HasMutableTraceEventStackSource())
                    {
                        TraceLoadedDotNetRuntime loadedRuntime = null;
                        TraceProcess gcProcess = null;
                        foreach (var pair in processRuntimes)
                        {
                            var proc = pair.Key;
                            var tmpMang = pair.Value;

                            TraceGC e = TraceGarbageCollector.GetCurrentGC(tmpMang);
                            // If we are in the middle of a GC.
                            if (e != null)
                            {
                                if ((e.Type != GCType.BackgroundGC) && (tmpMang.GC.m_stats.IsServerGCUsed == 1))
                                {
                                    e.AddServerGcSample(new ThreadWorkSpan(data));
                                    loadedRuntime = tmpMang;
                                    gcProcess = proc;
                                }
                            }
                        }

                        if (loadedRuntime != null && gcProcess != null && gcProcess.MutableTraceEventStackSource() != null)
                        {
                            var stackSource = gcProcess.MutableTraceEventStackSource();
                            TraceGC e = TraceGarbageCollector.GetCurrentGC(loadedRuntime);
                            StackSourceSample sample = new StackSourceSample(stackSource);
                            sample.Metric = 1;
                            sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                            var nodeName = string.Format("Server GCs #{0} in {1} (PID:{2})", e.Number, gcProcess.Name, gcProcess.ProcessID);
                            var nodeIndex = stackSource.Interner.FrameIntern(nodeName);
                            sample.StackIndex = stackSource.Interner.CallStackIntern(nodeIndex, stackSource.GetCallStack(data.CallStackIndex(), data));
                            stackSource.AddSample(sample);
                        }
                    }

                    TraceProcess tmpProc = data.Process();
                    TraceLoadedDotNetRuntime mang;
                    if (processRuntimes.TryGetValue(tmpProc, out mang))
                    {
                        int heapIndex = mang.GC.m_stats.IsServerGCThread(data.ThreadID);

                        if ((heapIndex > -1) && !(mang.GC.m_stats.ServerGcHeap2ThreadId.ContainsKey(heapIndex)))
                        {
                            mang.GC.m_stats.ServerGcHeap2ThreadId[heapIndex] = data.ThreadID;
                        }

                        var cpuIncrement = tmpProc.SampleIntervalMSec();

                        TraceGC _gc = TraceGarbageCollector.GetCurrentGC(mang);
                        // If we are in the middle of a GC.
                        if (_gc != null)
                        {
                            bool isThreadDoingGC = false;
                            if ((_gc.Type != GCType.BackgroundGC) && (mang.GC.m_stats.IsServerGCUsed == 1))
                            {
                                if (heapIndex != -1)
                                {
                                    _gc.AddServerGCThreadTime(heapIndex, cpuIncrement);
                                    isThreadDoingGC = true;
                                }
                            }
                            else if (data.ThreadID == mang.GC.m_stats.suspendThreadIDGC)
                            {
                                _gc.GCCpuMSec += cpuIncrement;
                                isThreadDoingGC = true;
                            }
                            else if (mang.GC.m_stats.IsBGCThread(data.ThreadID))
                            {
                                Debug.Assert(mang.GC.m_stats.currentBGC != null);
                                if (mang.GC.m_stats.currentBGC != null)
                                {
                                    mang.GC.m_stats.currentBGC.GCCpuMSec += cpuIncrement;
                                }

                                isThreadDoingGC = true;
                            }

                            if (isThreadDoingGC)
                            {
                                mang.GC.m_stats.TotalCpuMSec += cpuIncrement;
                            }
                        }
                    }
                };

                source.Clr.GCSuspendEEStart += delegate (GCSuspendEETraceData data)
                {
                    var process = data.Process();
                    var mang = currentManagedProcess(data);
                    switch (data.Reason)
                    {
                        case GCSuspendEEReason.SuspendForGC:
                            mang.GC.m_stats.suspendThreadIDGC = data.ThreadID;
                            break;
                        case GCSuspendEEReason.SuspendForGCPrep:
                            mang.GC.m_stats.suspendThreadIDBGC = data.ThreadID;
                            break;
                        default:
                            mang.GC.m_stats.suspendThreadIDOther = data.ThreadID;
                            // There are several other reasons for a suspend but we
                            // don't care about them
                            return;
                    }
                    mang.GC.m_stats.lastSuspendReason = data.Reason;

                    mang.GC.m_stats.suspendTimeRelativeMSec = data.TimeStampRelativeMSec;

                    if ((process.Log != null) && !mang.GC.m_stats.gotThreadInfo)
                    {
                        mang.GC.m_stats.gotThreadInfo = true;
                        Microsoft.Diagnostics.Tracing.Etlx.TraceProcess traceProc = process.Log.Processes.GetProcess(process.ProcessID, data.TimeStampRelativeMSec);
                        if (traceProc != null)
                        {
                            foreach (var procThread in traceProc.Threads)
                            {
                                if ((procThread.ThreadInfo != null) && (procThread.ThreadInfo.Contains(".NET Server GC Thread")))
                                {
                                    mang.GC.m_stats.IsServerGCUsed = 1;
                                    break;
                                }
                            }

                            if (mang.GC.m_stats.IsServerGCUsed == 1)
                            {
                                mang.GC.m_stats.HeapCount = 0;
                                mang.GC.m_stats.serverGCThreads = new Dictionary<int, int>(2);

                                foreach (var procThread in traceProc.Threads)
                                {
                                    if ((procThread.ThreadInfo != null) && (procThread.ThreadInfo.StartsWith(".NET Server GC Thread")))
                                    {
                                        mang.GC.m_stats.HeapCount++;

                                        int startIndex = procThread.ThreadInfo.IndexOf('(');
                                        int endIndex = procThread.ThreadInfo.IndexOf(')');
                                        string heapNumString = procThread.ThreadInfo.Substring(startIndex + 1, (endIndex - startIndex - 1));
                                        int heapNum = int.Parse(heapNumString);
                                        mang.GC.m_stats.serverGCThreads[procThread.ThreadID] = heapNum;
                                        mang.GC.m_stats.ServerGcHeap2ThreadId[heapNum] = procThread.ThreadID;
                                    }
                                }
                            }
                        }
                    }
                };

                // In 2.0 we didn't have this event.
                source.Clr.GCSuspendEEStop += delegate (GCNoUserDataTraceData data)
                {
                    var mang = currentManagedProcess(data);

                    if(!(data.ThreadID == mang.GC.m_stats.suspendThreadIDBGC || data.ThreadID == mang.GC.m_stats.suspendThreadIDGC))
                    {
                        // We only care about SuspendStop events that correspond to GC or PrepForGC reasons
                        // If we had initiated one of those then we set the corresponding threadid field in
                        // SuspendStart and we are guaranteed that the matching stop will occur on the same
                        // thread. Any other SuspendStop must be part of a suspension we aren't tracking.
                        return;
                    }

                    if ((mang.GC.m_stats.suspendThreadIDBGC > 0) && (mang.GC.m_stats.currentBGC != null))
                    {
                        mang.GC.m_stats.currentBGC.SuspendDurationMSec += data.TimeStampRelativeMSec - mang.GC.m_stats.suspendTimeRelativeMSec;
                    }

                    mang.GC.m_stats.suspendEndTimeRelativeMSec = data.TimeStampRelativeMSec;
                };

                source.Clr.GCRestartEEStop += delegate (GCNoUserDataTraceData data)
                {
                    var process = data.Process();
                    var stats = currentManagedProcess(data);

                    if(data.ThreadID == stats.GC.m_stats.suspendThreadIDOther)
                    {
                        stats.GC.m_stats.suspendThreadIDOther = -1;
                    }

                    if (!(data.ThreadID == stats.GC.m_stats.suspendThreadIDBGC || data.ThreadID == stats.GC.m_stats.suspendThreadIDGC))
                    {
                        // We only care about RestartEE events that correspond to GC or PrepForGC suspensions
                        // If we had initiated one of those then we set the corresponding threadid field in
                        // SuspendStart and we are guaranteed that the matching RestartEE will occur on the 
                        // same thread. Any other RestartEE must be part of a suspension we aren't tracking.
                        return;
                    }

                    TraceGC _gc = TraceGarbageCollector.GetCurrentGC(stats);
                    if (_gc != null)
                    {
                        if (_gc.Type == GCType.BackgroundGC)
                        {
                            stats.GC.m_stats.AddConcurrentPauseTime(_gc, data.TimeStampRelativeMSec);
                        }
                        else
                        {
                            if (!_gc.IsConcurrentGC)
                            {
                                Debug.Assert(_gc.PauseDurationMSec == 0);
                            }
                            Debug.Assert(_gc.PauseStartRelativeMSec != 0);
                            // In 2.0 Concurrent GC, since we don't know the GC's type we can't tell if it's concurrent 
                            // or not. But we know we don't have nested GCs there so simply check if we have received the
                            // GCStop event; if we have it means it's a blocking GC; otherwise it's a concurrent GC so 
                            // simply add the pause time to the GC without making the GC complete.
                            if (_gc.DurationMSec == 0)
                            {
                                Debug.Assert(_gc.is20Event);
                                _gc.IsConcurrentGC = true;
                                stats.GC.m_stats.AddConcurrentPauseTime(_gc, data.TimeStampRelativeMSec);
                            }
                            else
                            {
                                _gc.PauseDurationMSec = data.TimeStampRelativeMSec - _gc.PauseStartRelativeMSec;
                                if (_gc.HeapStats != null)
                                {
                                    _gc.OnEnd(stats.GC); // set IsComplete = true;
                                    stats.GC.m_stats.lastCompletedGC = _gc;

                                    // fire event
                                    if (stats.GCEnd != null)
                                    {
                                        stats.GCEnd(process, _gc);
                                    }
                                }
                            }
                        }
                    }

                    // We don't change between a GC end and the pause resume.   
                    //Debug.Assert(stats.allocTickAtLastGC == stats.allocTickCurrentMB);
                    // Mark that we are not in suspension anymore.  
                    stats.GC.m_stats.suspendTimeRelativeMSec = -1;
                    stats.GC.m_stats.suspendThreadIDBGC = -1;
                    stats.GC.m_stats.suspendThreadIDGC = -1;
                };

                source.Clr.GCAllocationTick += delegate (GCAllocationTickTraceData data)
                {
                    var stats = currentManagedProcess(data);
                    stats.GC.m_stats.HasAllocTickEvents = true;

                    double valueMB = data.GetAllocAmount(ref stats.GC.m_stats.SeenBadAllocTick) / 1000000.0;

                    if (data.AllocationKind == GCAllocationKind.Small)
                    {
                        // Would this do the right thing or is it always 0 for SOH since AllocationAmount 
                        // is an int??? 
                        stats.GC.m_stats.allocTickCurrentMB[0] += valueMB;
                    }
                    else
                    {
                        stats.GC.m_stats.allocTickCurrentMB[1] += valueMB;
                    }
                };

                source.Clr.GCStart += delegate (GCStartTraceData data)
                {
                    var process = data.Process();
                    var stats = currentManagedProcess(data);

                    // We need to filter the scenario where we get 2 GCStart events for each GC.
                    if ((stats.GC.m_stats.suspendThreadIDGC > 0 || stats.GC.m_stats.suspendThreadIDOther > 0) &&
                            !((stats.GC.GCs.Count > 0) && stats.GC.GCs[stats.GC.GCs.Count - 1].Number == data.Count))
                    {
                        TraceGC _gc = new TraceGC(stats.GC.m_stats.HeapCount);
                        Debug.Assert(0 <= data.Depth && data.Depth <= 2);
                        _gc.Generation = data.Depth;
                        _gc.Reason = data.Reason;
                        _gc.Number = data.Count;
                        _gc.Type = data.Type;
                        _gc.Index = stats.GC.GCs.Count;
                        _gc.is20Event = data.IsClassicProvider;
                        bool isEphemeralGCAtBGCStart = false;
                        // Detecting the ephemeral GC that happens at the beginning of a BGC.
                        if (stats.GC.GCs.Count > 0)
                        {
                            TraceGC lastGCEvent = stats.GC.GCs[stats.GC.GCs.Count - 1];
                            if ((lastGCEvent.Type == GCType.BackgroundGC) &&
                                (!lastGCEvent.IsComplete) &&
                                (data.Type == GCType.NonConcurrentGC))
                            {
                                isEphemeralGCAtBGCStart = true;
                            }
                        }

                        Debug.Assert(stats.GC.m_stats.suspendTimeRelativeMSec != -1);
                        if (isEphemeralGCAtBGCStart || _gc.Reason == GCReason.PMFullGC)
                        {
                            _gc.PauseStartRelativeMSec = data.TimeStampRelativeMSec;

                            if (_gc.Reason == GCReason.PMFullGC)
                            {
                                TraceGC lastGC = TraceGarbageCollector.GetCurrentGC(stats);
                                if (lastGC != null)
                                {
                                    lastGC.OnEnd(stats.GC);
                                }
                            }
                        }
                        else
                        {
                            _gc.PauseStartRelativeMSec = stats.GC.m_stats.suspendTimeRelativeMSec;
                            if (stats.GC.m_stats.suspendEndTimeRelativeMSec == -1)
                            {
                                stats.GC.m_stats.suspendEndTimeRelativeMSec = data.TimeStampRelativeMSec;
                            }

                            _gc.SuspendDurationMSec = stats.GC.m_stats.suspendEndTimeRelativeMSec - stats.GC.m_stats.suspendTimeRelativeMSec;
                        }

                        _gc.StartRelativeMSec = data.TimeStampRelativeMSec;
                        stats.GC.GCs.Add(_gc);
                        if (_gc.Type == GCType.BackgroundGC)
                        {
                            stats.GC.m_stats.currentBGC = _gc;
                            // For BGC, we need to add the suspension time so far to its pause so we don't miss including it.
                            // If there's an ephemeral GC happening before the BGC starts, AddConcurrentPauseTime will not
                            // add this suspension time to GC pause as that GC would be seen the ephemeral GC, not the BGC.
                            _gc.PauseDurationMSec = _gc.SuspendDurationMSec;
                            _gc.ProcessCpuAtLastGC = stats.GC.m_stats.ProcessCpuAtLastGC;
                        }

                        if ((_gc.Type != GCType.BackgroundGC) && (stats.GC.m_stats.IsServerGCUsed == 1))
                        {
                            stats.GC.m_stats.SetUpServerGcHistory(process.ProcessID, _gc);
                            foreach (var s in RecentCpuSamples)
                            {
                                _gc.AddServerGcSample(s);
                            }

                            foreach (var s in RecentThreadSwitches)
                            {
                                _gc.AddServerGcThreadSwitch(s);
                            }
                        }

                        // fire event
                        if (stats.GCStart != null)
                        {
                            stats.GCStart(process, _gc);
                        }

                        // check if we should apply a lifetime limit to the GC cache
                        if (source.DataLifetimeEnabled() && data.TimeStampRelativeMSec >= stats.GC.NextRelativeTimeStampMsec)
                        {
                            // note the next time that lifetime should be applied, to avoid cleaningup too frequently
                            stats.GC.NextRelativeTimeStampMsec = data.TimeStampRelativeMSec + (source.DataLifetimeMsec / 2.0);
                            // trim the GCs to only include those either incomplete or completed after lifetime
                            stats.GC.m_gcs = stats.GC.m_gcs.Where(gc => !gc.IsComplete || gc.StartRelativeMSec >= (data.TimeStampRelativeMSec - source.DataLifetimeMsec)).ToList();
                            // rewrite the index for fast lookup
                            for (int i = 0; i < stats.GC.m_gcs.Count; i++)
                            {
                                stats.GC.m_gcs[i].Index = i;
                            }
                        }
                    }
                };

                source.Clr.GCPinObjectAtGCTime += delegate (PinObjectAtGCTimeTraceData data)
                {
                    var stats = currentManagedProcess(data);

                    TraceGC _gc = TraceGarbageCollector.GetCurrentGC(stats);
                    if (_gc != null)
                    {
                        if (_gc.PinnedObjects == null)
                        {
                            _gc.PinnedObjects = new Dictionary<Address, long>();
                        }

                        if (!_gc.PinnedObjects.ContainsKey(data.ObjectID))
                        {
                            _gc.PinnedObjects.Add(data.ObjectID, data.ObjectSize);
                        }
                        else
                        {
                            _gc.duplicatedPinningReports++;
                        }
                    }
                };

                // Some builds have this as a public event, and some have it as a private event.
                // All will move to the private event, so we'll remove this code afterwards.
                source.Clr.GCPinPlugAtGCTime += delegate (PinPlugAtGCTimeTraceData data)
                {
                    var stats = currentManagedProcess(data);

                    TraceGC _event = TraceGarbageCollector.GetCurrentGC(stats);
                    if (_event != null)
                    {
                        // ObjectID is supposed to be an IntPtr. But "Address" is defined as UInt64 in 
                        // TraceEvent.
                        if (_event.PinnedPlugs == null)
                        {
                            _event.PinnedPlugs = new List<TraceGC.PinnedPlug>();
                        }

                        _event.PinnedPlugs.Add(new TraceGC.PinnedPlug(data.PlugStart, data.PlugEnd));
                    }
                };

                source.Clr.GCMarkWithType += delegate (GCMarkWithTypeTraceData data)
                {
                    var stats = currentManagedProcess(data);

                    stats.GC.m_stats.AddServerGCThreadFromMark(data.ThreadID, data.HeapNum);

                    TraceGC _gc = TraceGarbageCollector.GetCurrentGC(stats);
                    if (_gc != null)
                    {
                        if (_gc.PerHeapMarkTimes == null)
                        {
                            _gc.PerHeapMarkTimes = new Dictionary<int, MarkInfo>();
                        }

                        if (!_gc.PerHeapMarkTimes.ContainsKey(data.HeapNum))
                        {
                            _gc.PerHeapMarkTimes.Add(data.HeapNum, new MarkInfo());
                        }

                        _gc.PerHeapMarkTimes[data.HeapNum].MarkTimes[(int)data.Type] = data.TimeStampRelativeMSec;
                        _gc.PerHeapMarkTimes[data.HeapNum].MarkPromoted[(int)data.Type] = data.Promoted;
                    }
                };

                source.Clr.GCGlobalHeapHistory += delegate (GCGlobalHeapHistoryTraceData data)
                {
                    var stats = currentManagedProcess(data);
                    GCStats.ProcessGlobalHistory(stats, data);
                };

                source.Clr.GCPerHeapHistory += delegate (GCPerHeapHistoryTraceData data)
                {
                    var stats = currentManagedProcess(data);
                    GCStats.ProcessPerHeapHistory(stats, data);
                };

                source.Clr.GCJoin += delegate (GCJoinTraceData data)
                {
                    var stats = currentManagedProcess(data);
                    TraceGC _gc = TraceGarbageCollector.GetCurrentGC(stats);
                    if (_gc != null)
                    {
                        _gc.AddGcJoin(data);
                    }
                };

                clrPrivate.GCPinPlugAtGCTime += delegate (PinPlugAtGCTimeTraceData data)
                {
                    var stats = currentManagedProcess(data);
                    TraceGC _gc = TraceGarbageCollector.GetCurrentGC(stats);
                    if (_gc != null)
                    {
                        // ObjectID is supposed to be an IntPtr. But "Address" is defined as UInt64 in 
                        // TraceEvent.
                        if (_gc.PinnedPlugs == null)
                        {
                            _gc.PinnedPlugs = new List<TraceGC.PinnedPlug>();
                        }

                        _gc.PinnedPlugs.Add(new TraceGC.PinnedPlug(data.PlugStart, data.PlugEnd));
                    }
                };

                // Sometimes at the end of a trace I see only some mark events are included in the trace and they
                // are not in order, so need to anticipate that scenario.
                clrPrivate.GCMarkStackRoots += delegate (GCMarkTraceData data)
                {
                    var stats = currentManagedProcess(data);
                    stats.GC.m_stats.AddServerGCThreadFromMark(data.ThreadID, data.HeapNum);

                    TraceGC _gc = TraceGarbageCollector.GetCurrentGC(stats);
                    if (_gc != null)
                    {
                        if (_gc.PerHeapMarkTimes == null)
                        {
                            _gc.PerHeapMarkTimes = new Dictionary<int, MarkInfo>();
                        }

                        if (!_gc.PerHeapMarkTimes.ContainsKey(data.HeapNum))
                        {
                            _gc.PerHeapMarkTimes.Add(data.HeapNum, new MarkInfo(false));
                        }

                        _gc.PerHeapMarkTimes[data.HeapNum].MarkTimes[(int)MarkRootType.MarkStack] = data.TimeStampRelativeMSec;
                    }
                };

                clrPrivate.GCMarkFinalizeQueueRoots += delegate (GCMarkTraceData data)
                {
                    var stats = currentManagedProcess(data);
                    TraceGC _gc = TraceGarbageCollector.GetCurrentGC(stats);
                    if (_gc != null)
                    {
                        if ((_gc.PerHeapMarkTimes != null) && _gc.PerHeapMarkTimes.ContainsKey(data.HeapNum))
                        {
                            _gc.PerHeapMarkTimes[data.HeapNum].MarkTimes[(int)MarkRootType.MarkFQ] =
                                data.TimeStampRelativeMSec;
                        }
                    }
                };

                clrPrivate.GCMarkHandles += delegate (GCMarkTraceData data)
                {
                    var stats = currentManagedProcess(data);
                    TraceGC _gc = TraceGarbageCollector.GetCurrentGC(stats);
                    if (_gc != null)
                    {
                        if ((_gc.PerHeapMarkTimes != null) && _gc.PerHeapMarkTimes.ContainsKey(data.HeapNum))
                        {
                            _gc.PerHeapMarkTimes[data.HeapNum].MarkTimes[(int)MarkRootType.MarkHandles] =
                               data.TimeStampRelativeMSec;
                        }
                    }
                };

                clrPrivate.GCMarkCards += delegate (GCMarkTraceData data)
                {
                    var stats = currentManagedProcess(data);
                    TraceGC _gc = TraceGarbageCollector.GetCurrentGC(stats);
                    if (_gc != null)
                    {
                        if ((_gc.PerHeapMarkTimes != null) && _gc.PerHeapMarkTimes.ContainsKey(data.HeapNum))
                        {
                            _gc.PerHeapMarkTimes[data.HeapNum].MarkTimes[(int)MarkRootType.MarkOlder] =
                                data.TimeStampRelativeMSec;
                        }
                    }
                };

                clrPrivate.GCGlobalHeapHistory += delegate (GCGlobalHeapHistoryTraceData data)
                {
                    var stats = currentManagedProcess(data);
                    GCStats.ProcessGlobalHistory(stats, data);
                };

                clrPrivate.GCPerHeapHistory += delegate (GCPerHeapHistoryTraceData data)
                {
                    var stats = currentManagedProcess(data);
                    GCStats.ProcessPerHeapHistory(stats, data);
                };

                clrPrivate.GCBGCStart += delegate (GCNoUserDataTraceData data)
                {
                    var stats = currentManagedProcess(data);
                    TraceGC _gc = stats.GC.m_stats.currentBGC;
                    if (_gc != null)
                    {
                        if (stats.GC.m_stats.backgroundGCThreads == null)
                        {
                            stats.GC.m_stats.backgroundGCThreads = new Dictionary<int, object>(16);
                        }
                        stats.GC.m_stats.backgroundGCThreads[data.ThreadID] = null;
                        _gc.BGCCurrentPhase = BGCPhase.BGC1stNonConcurrent;
                    }
                };

                clrPrivate.GCBGC1stNonCondStop += delegate (GCNoUserDataTraceData data)
                {
                    var stats = currentManagedProcess(data);
                    TraceGC _gc = stats.GC.m_stats.currentBGC;
                    if (_gc != null)
                    {
                        _gc.BGCCurrentPhase = BGCPhase.BGC1stConcurrent;
                    }
                };

                clrPrivate.GCBGC2ndNonConStart += delegate (GCNoUserDataTraceData data)
                {
                    var stats = currentManagedProcess(data);
                    TraceGC _gc = stats.GC.m_stats.currentBGC;
                    if (_gc != null)
                    {
                        _gc.BGCCurrentPhase = BGCPhase.BGC2ndNonConcurrent;
                    }
                };

                clrPrivate.GCBGC2ndConStart += delegate (GCNoUserDataTraceData data)
                {
                    var stats = currentManagedProcess(data);
                    TraceGC _gc = stats.GC.m_stats.currentBGC;
                    if (_gc != null)
                    {
                        _gc.BGCCurrentPhase = BGCPhase.BGC2ndConcurrent;
                    }
                };

                clrPrivate.GCBGCRevisit += delegate (BGCRevisitTraceData data)
                {
                    var stats = currentManagedProcess(data);
                    TraceGC _gc = TraceGarbageCollector.GetCurrentGC(stats);
                    if (_gc != null)
                    {
                        Debug.Assert(_gc.Type == GCType.BackgroundGC);
                        int iStateIndex = ((_gc.BGCCurrentPhase == BGCPhase.BGC1stConcurrent) ?
                                           (int)TraceGC.BGCRevisitState.Concurrent :
                                           (int)TraceGC.BGCRevisitState.NonConcurrent);
                        int iHeapTypeIndex = ((data.IsLarge == 1) ? (int)TraceGC.HeapType.LOH : (int)TraceGC.HeapType.SOH);
                        _gc.EnsureBGCRevisitInfoAlloc();
                        (_gc.BGCRevisitInfoArr[iStateIndex][iHeapTypeIndex]).PagesRevisited += data.Pages;
                        (_gc.BGCRevisitInfoArr[iStateIndex][iHeapTypeIndex]).ObjectsRevisited += data.Objects;
                    }
                };

                source.Clr.GCStop += delegate (GCEndTraceData data)
                {
                    var stats = currentManagedProcess(data);
                    TraceGC _gc = TraceGarbageCollector.GetCurrentGC(stats);
                    if (_gc != null)
                    {
                        _gc.DurationMSec = data.TimeStampRelativeMSec - _gc.StartRelativeMSec;
                        Debug.Assert(_gc.Number == data.Count);
                    }
                };

                source.Clr.GCHeapStats += delegate (GCHeapStatsTraceData data)
                {
                    var process = data.Process();
                    var stats = currentManagedProcess(data);
                    TraceGC _gc = TraceGarbageCollector.GetCurrentGC(stats);

                    var sizeAfterMB = (data.GenerationSize1 + data.GenerationSize2 + data.GenerationSize3 + data.GenerationSize4) / 1000000.0;
                    if (_gc != null)
                    {
                        _gc.HeapStats = new GCHeapStats()
                        {
                            Depth = data.Depth
                            ,
                            FinalizationPromotedCount = data.FinalizationPromotedCount
                            ,
                            FinalizationPromotedSize = data.FinalizationPromotedSize
                            ,
                            GCHandleCount = data.GCHandleCount
                            ,
                            GenerationSize0 = data.GenerationSize0
                            ,
                            GenerationSize1 = data.GenerationSize1
                            ,
                            GenerationSize2 = data.GenerationSize2
                            ,
                            GenerationSize3 = data.GenerationSize3
                            ,
                            GenerationSize4 = data.GenerationSize4
                            ,
                            PinnedObjectCount = data.PinnedObjectCount
                            ,
                            SinkBlockCount = data.SinkBlockCount
                            ,
                            TotalHeapSize = data.TotalHeapSize
                            ,
                            TotalPromoted = data.TotalPromoted
                            ,
                            TotalPromotedSize0 = data.TotalPromotedSize0
                            ,
                            TotalPromotedSize1 = data.TotalPromotedSize1
                            ,
                            TotalPromotedSize2 = data.TotalPromotedSize2
                            ,
                            TotalPromotedSize3 = data.TotalPromotedSize3
                            ,
                            TotalPromotedSize4 = data.TotalPromotedSize4
                        };

                        if (_gc.Type == GCType.BackgroundGC)
                        {
                            _gc.ProcessCpuMSec = process.CPUMSec - _gc.ProcessCpuAtLastGC;
                            _gc.DurationSinceLastRestartMSec = data.TimeStampRelativeMSec - stats.GC.m_stats.lastRestartEndTimeRelativeMSec;
                        }
                        else
                        {
                            _gc.ProcessCpuMSec = process.CPUMSec - stats.GC.m_stats.ProcessCpuAtLastGC;
                            _gc.DurationSinceLastRestartMSec = _gc.PauseStartRelativeMSec - stats.GC.m_stats.lastRestartEndTimeRelativeMSec;
                        }

                        if (stats.GC.m_stats.HasAllocTickEvents)
                        {
                            _gc.HasAllocTickEvents = true;
                            _gc.AllocedSinceLastGCBasedOnAllocTickMB[0] = stats.GC.m_stats.allocTickCurrentMB[0] - stats.GC.m_stats.allocTickAtLastGC[0];
                            _gc.AllocedSinceLastGCBasedOnAllocTickMB[1] = stats.GC.m_stats.allocTickCurrentMB[1] - stats.GC.m_stats.allocTickAtLastGC[1];
                        }

                        // This is where a background GC ends.
                        if ((_gc.Type == GCType.BackgroundGC) && (stats.GC.m_stats.currentBGC != null))
                        {
                            stats.GC.m_stats.currentBGC.OnEnd(stats.GC); // set IsComplete = true;
                            stats.GC.m_stats.lastCompletedGC = stats.GC.m_stats.currentBGC;
                            stats.GC.m_stats.currentBGC = null;

                            // fire event
                            if (stats.GCEnd != null)
                            {
                                stats.GCEnd(process, stats.GC.m_stats.lastCompletedGC);
                            }
                        }

                        if (_gc.IsConcurrentGC)
                        {
                            Debug.Assert(_gc.is20Event);
                            _gc.OnEnd(stats.GC); // set IsComplete = true
                            stats.GC.m_stats.lastCompletedGC = _gc;

                            // fire event
                            if (stats.GCEnd != null)
                            {
                                stats.GCEnd(process, _gc);
                            }
                        }
                    }

                    stats.GC.m_stats.ProcessCpuAtLastGC = process.CPUMSec;
                    stats.GC.m_stats.allocTickAtLastGC[0] = stats.GC.m_stats.allocTickCurrentMB[0];
                    stats.GC.m_stats.allocTickAtLastGC[1] = stats.GC.m_stats.allocTickCurrentMB[1];
                    stats.GC.m_stats.lastRestartEndTimeRelativeMSec = data.TimeStampRelativeMSec;
                };

                source.Clr.GCTerminateConcurrentThread += delegate (GCTerminateConcurrentThreadTraceData data)
                {
                    var stats = currentManagedProcess(data);
                    if (stats.GC.m_stats.backgroundGCThreads != null)
                    {
                        stats.GC.m_stats.backgroundGCThreads = null;
                    }
                };

                clrPrivate.GCBGCAllocWaitStart += delegate (BGCAllocWaitTraceData data)
                {
                    var stats = currentManagedProcess(data);
                    Debug.Assert(stats.GC.m_stats.currentBGC != null);

                    if (stats.GC.m_stats.currentBGC != null)
                    {
                        stats.GC.m_stats.currentBGC.AddLOHWaitThreadInfo(data.ThreadID, data.TimeStampRelativeMSec, data.Reason, true);
                    }
                };

                clrPrivate.GCBGCAllocWaitStop += delegate (BGCAllocWaitTraceData data)
                {
                    var stats = currentManagedProcess(data);

                    TraceGC _gc = GCStats.GetLastBGC(stats);

                    if (_gc != null)
                    {
                        _gc.AddLOHWaitThreadInfo(data.ThreadID, data.TimeStampRelativeMSec, data.Reason, false);
                    }
                };

                clrPrivate.GCJoin += delegate (GCJoinTraceData data)
                {
                    var stats = currentManagedProcess(data);
                    TraceGC _gc = TraceGarbageCollector.GetCurrentGC(stats);
                    if (_gc != null)
                    {
                        _gc.AddGcJoin(data);
                    }
                };

                source.Clr.GCFinalizeObject += data =>
                {
                    var stats = currentManagedProcess(data);
                    long finalizationCount;
                    stats.GC.m_stats.FinalizedObjects[data.TypeName] =
                        stats.GC.m_stats.FinalizedObjects.TryGetValue(data.TypeName, out finalizationCount) ?
                            finalizationCount + 1 :
                            1;
                };

            }

            //
            // Jit
            //
            bool backgroundJITEventsOn = false;
            if (processJITEvents)
            {
                source.Clr.MethodJittingStarted += delegate (MethodJittingStartedTraceData data)
                {
                    var process = data.Process();
                    var stats = currentManagedProcess(data);
                    var _method = stats.JIT.m_stats.LogJitStart(stats, data, JITStats.GetMethodName(data), data.MethodILSize, data.ModuleID, data.MethodID);

                    // fire event
                    if (stats.JITMethodStart != null)
                    {
                        stats.JITMethodStart(process, _method);
                    }

                    // check if we should apply a lifetime limit to the method cache
                    if (source.DataLifetimeEnabled() && data.TimeStampRelativeMSec >= stats.JIT.NextRelativeTimeStampMsec)
                    {
                        // note the next time that lifetime should be applied, to avoid cleaningup too frequently
                        stats.JIT.NextRelativeTimeStampMsec = data.TimeStampRelativeMSec + (source.DataLifetimeMsec / 2.0);
                        // trim the methods to only include those that were JITT'd after the lifetime timestamp
                        stats.JIT.m_methods = stats.JIT.m_methods.Where(meth => meth.StartTimeMSec >= (data.TimeStampRelativeMSec - source.DataLifetimeMsec)).ToList();
                    }
                };
                ClrRundownTraceEventParser parser = new ClrRundownTraceEventParser(source);
                Action<ModuleLoadUnloadTraceData> moduleLoadAction = delegate (ModuleLoadUnloadTraceData data)
                {
                    var stats = currentManagedProcess(data);
                    stats.JIT.m_stats.moduleNamesFromID[data.ModuleID] = data.ModuleILPath;

                    // fix-up methods that have been previously marked with incomplete module information
                    foreach (var _method in stats.JIT.Methods.Where(m => m.ModuleID == data.ModuleID))
                    {
                        if (string.IsNullOrWhiteSpace(_method.ModuleILPath))
                        {
                            _method.ModuleILPath = data.ModuleILPath;
                        }
                    }
                };
                source.Clr.LoaderModuleLoad += moduleLoadAction;
                source.Clr.LoaderModuleUnload += moduleLoadAction;
                parser.LoaderModuleDCStop += moduleLoadAction;

                source.Clr.MethodLoadVerbose += delegate (MethodLoadUnloadVerboseTraceData data)
                {
                    if (data.IsJitted)
                    {
                        var process = data.Process();
                        var stats = currentManagedProcess(data);

                        bool createdNewMethod;
                        var _method = JITStats.MethodComplete(stats, data, JITStats.GetMethodName(data), (int)data.ReJITID, out createdNewMethod);

                        // fire event - but only once
                        if (createdNewMethod && stats.JITMethodStart != null)
                        {
                            stats.JITMethodStart(process, _method);
                        }

                        if (stats.JITMethodEnd != null && _method.Completed == 1)
                        {
                            stats.JITMethodEnd(process, _method);
                        }
                    }
                };

                source.Clr.MethodLoad += delegate (MethodLoadUnloadTraceData data)
                {
                    if (data.IsJitted)
                    {
                        var process = data.Process();
                        var stats = currentManagedProcess(data);

                        bool createdNewMethod;
                        var _method = JITStats.MethodComplete(stats, data, "", 0, out createdNewMethod);

                        // fire event - but only once
                        if (createdNewMethod && stats.JITMethodStart != null)
                        {
                            stats.JITMethodStart(process, _method);
                        }

                        if (stats.JITMethodEnd != null && _method.Completed == 1)
                        {
                            stats.JITMethodEnd(process, _method);
                        }
                    }
                };
                source.Clr.RuntimeStart += delegate (RuntimeInformationTraceData data)
                {
                    var process = data.Process();
                    var stats = currentManagedProcess(data);
                    stats.JIT.m_stats.IsClr4 = true;
                    if (process.CommandLine == null)
                    {
                        process.CommandLine = data.CommandLine;
                    }
                };

                source.Clr.MethodMemoryAllocatedForJitCode += delegate(MethodJitMemoryAllocatedForCodeTraceData data)
                {
                    var process = data.Process();
                    var stats = currentManagedProcess(data);
                    JITStats.LogJitMethodAllocation(stats, data);
                };

                clrPrivate.ClrMulticoreJitCommon += delegate (MulticoreJitPrivateTraceData data)
                {
                    var process = data.Process();
                    var stats = currentManagedProcess(data);
                    if (!backgroundJITEventsOn)
                    {
                        stats.JIT.m_stats.LastBlockedReason = null;
                    }
                    backgroundJITEventsOn = true;

                    if (process.Name == null)
                    {
                        process.name = data.ProcessName;
                    }

                    if (stats.JIT.m_stats.BackgroundJitThread == 0 && (data.String1 == "GROUPWAIT" || data.String1 == "JITTHREAD"))
                    {
                        stats.JIT.m_stats.BackgroundJitThread = data.ThreadID;
                    }

                    if (data.String1 == "ADDMODULEDEPENDENCY")
                    {
                        // Add the blocked module to the list of recorded modules.
                        if (!stats.JIT.m_stats.RecordedModules.Contains(data.String2))
                        {
                            stats.JIT.m_stats.RecordedModules.Add(data.String2);
                        }
                    }

                    if (data.String1 == "BLOCKINGMODULE")
                    {
                        // Set the blocking module.
                        stats.JIT.m_stats.LastBlockedReason = data.String2;

                        // Add the blocked module to the list of recorded modules.
                        if (!stats.JIT.m_stats.RecordedModules.Contains(data.String2))
                        {
                            stats.JIT.m_stats.RecordedModules.Add(data.String2);
                        }
                    }

                    if (data.String1 == "GROUPWAIT" && data.String2 == "Leave")
                    {
                        if (data.Int2 == 0)
                        {
                            // Clear the last blocked reason, since we're no longer blocked on modules.
                            stats.JIT.m_stats.LastBlockedReason = null;
                        }
                        else
                        {
                            // If GroupWait returns and Int2 != 0, this means that not all of the module loads were satisifed
                            // and we have aborted playback.
                            stats.JIT.m_stats.LastBlockedReason = "Playback Aborted";
                            stats.JIT.m_stats.playbackAborted = true;
                        }
                    }

                    if (data.String1 == "ABORTPROFILE")
                    {
                        stats.JIT.m_stats.BackgroundJitAbortedAtMSec = data.TimeStampRelativeMSec;
                    }
                };
                clrPrivate.ClrMulticoreJitMethodCodeReturned += delegate (MulticoreJitMethodCodeReturnedPrivateTraceData data)
                {
                    backgroundJITEventsOn = true;

                    var stats = currentManagedProcess(data);

                    // Get the associated JIT information
                    TraceJittedMethod backgroundJitInfo = null;

                    JITStats.MethodKey methodKey = new JITStats.MethodKey(data.ModuleID, data.MethodID);
                    if (stats.JIT.m_stats.backgroundJitEvents.TryGetValue(methodKey, out backgroundJitInfo))
                    {
                        if (backgroundJitInfo.ThreadID == stats.JIT.m_stats.BackgroundJitThread)
                        {
                            backgroundJitInfo.ForegroundMethodRequestTimeMSec = data.TimeStampRelativeMSec;
                            stats.JIT.m_stats.backgroundJitEvents.Remove(methodKey);
                        }
                    }
                };

                clrPrivate.BindingLoaderPhaseStart += delegate (BindingTraceData data)
                {
                    // Keep track if the last assembly loaded before Background JIT aborts.  
                    var stats = currentManagedProcess(data);
                    if (stats.JIT.m_stats.BackgroundJitAbortedAtMSec == 0)
                    {
                        stats.JIT.m_stats.LastAssemblyLoadNameBeforeAbort = data.AssemblyName;
                        stats.JIT.m_stats.LastAssemblyLoadBeforeAbortMSec = data.TimeStampRelativeMSec;
                    }
                };

                clrPrivate.BindingLoaderDeliverEventsPhaseStop += delegate (BindingTraceData data)
                {
                    // If we hit this events, we assume assembly load is successful. 

                    var stats = currentManagedProcess(data);
                    if (stats.JIT.m_stats.BackgroundJitAbortedAtMSec != 0)
                    {
                        if (stats.JIT.m_stats.LastAssemblyLoadNameBeforeAbort == data.AssemblyName)
                        {
                            stats.JIT.m_stats.LastAssemblyLoadBeforeAbortSuccessful = true;
                        }
                    }
                };

                clrPrivate.StartupPrestubWorkerStart += delegate (StartupTraceData data)
                {
                    // TODO, we want to know if we have background JIT events.   Today we don't have an event
                    // that says 'events are enabled, its just no one used the events'  We want this.  
                    // Today we turn on all CLRPrivate events to turn on listening to Backgroung JITTing and
                    // we use the fact that the PrestubWorker evnets are on as a proxy.  
                    backgroundJITEventsOn = true;
                };
                source.Clr.AppDomainResourceManagementThreadTerminated += delegate (ThreadTerminatedOrTransitionTraceData data)
                {

                    var stats = currentManagedProcess(data);
                    if (!stats.JIT.m_stats.playbackAborted)
                    {
                        stats.JIT.m_stats.LastBlockedReason = "Playback Completed";
                    }
                };

                source.Clr.MethodInliningSucceeded += delegate (MethodJitInliningSucceededTraceData data)
                {
                    var stats = currentManagedProcess(data);
                    stats.JIT.m_stats.InliningSuccesses.Add(new InliningSuccessResult
                    {
                        MethodBeingCompiled = data.MethodBeingCompiledNamespace + "." + data.MethodBeingCompiledName,
                        Inliner = data.InlinerNamespace + "." + data.InlinerName,
                        Inlinee = data.InlineeNamespace + "." + data.InlineeName
                    });
                };
                source.Clr.MethodInliningFailedAnsi += delegate (MethodJitInliningFailedAnsiTraceData data)
                {
                    var stats = currentManagedProcess(data);
                    stats.JIT.m_stats.InliningFailures.Add(new InliningFailureResult
                    {
                        MethodBeingCompiled = data.MethodBeingCompiledNamespace + "." + data.MethodBeingCompiledName,
                        Inliner = data.InlinerNamespace + "." + data.InlinerName,
                        Inlinee = data.InlineeNamespace + "." + data.InlineeName,
                        Reason = data.FailReason
                    });
                };
                source.Clr.MethodInliningFailed += delegate (MethodJitInliningFailedTraceData data)
                {
                    var stats = currentManagedProcess(data);
                    stats.JIT.m_stats.InliningFailures.Add(new InliningFailureResult
                    {
                        MethodBeingCompiled = data.MethodBeingCompiledNamespace + "." + data.MethodBeingCompiledName,
                        Inliner = data.InlinerNamespace + "." + data.InlinerName,
                        Inlinee = data.InlineeNamespace + "." + data.InlineeName,
                        Reason = data.FailReason
                    });
                };
            }

            Action<TieredCompilationSettingsTraceData> onTieredCompilationSettings = data =>
            {
                var stats = currentManagedProcess(data);
                stats.IsTieredCompilationEnabled = true;
            };
            source.Clr.TieredCompilationSettings += onTieredCompilationSettings;
            clrRundownParser.TieredCompilationRundownSettingsDCStart += onTieredCompilationSettings;
        }

        private Version runtimeVersion;

#endregion
    }

    /// <summary>
    /// Garbage Collector (GC) specific details about this process
    /// </summary>
    public class TraceGarbageCollector
    {
        /// <summary>
        /// Process view of GC statistics
        /// </summary>
        public GCStats Stats() { Calculate(); return m_stats; }
        /// <summary>
        /// Process view of GC generational statistics
        /// </summary>
        public GCStats[] Generations() { Calculate(); return m_generations; }

        /// <summary>
        /// Process view of all GCs
        /// </summary>
        public List<TraceGC> GCs { get { return m_gcs; } }

#region private
        internal static TraceGC GetCurrentGC(TraceLoadedDotNetRuntime proc)
        {
            if (proc.GC.GCs.Count > 0)
            {
                if (!proc.GC.GCs[proc.GC.GCs.Count - 1].IsComplete)
                {
                    return proc.GC.GCs[proc.GC.GCs.Count - 1];
                }
                else if (proc.GC.m_stats.currentBGC != null)
                {
                    return proc.GC.m_stats.currentBGC;
                }
            }

            return null;
        }

        internal List<TraceGC> m_gcs = new List<TraceGC>();
        private GCStats[] m_generations = new GCStats[3];
        internal GCStats m_stats = new GCStats();
        private int m_prvcount = 0;
        private int m_prvCompleted = 0;
        internal double NextRelativeTimeStampMsec;

        private void Calculate()
        {
            bool recalc = false;

            // determine if the stats need to be recalculated
            if (m_gcs.Count != m_prvcount)
            {
                recalc = true;
            }
            else
            {
                int complete = m_gcs.Sum(gc => (gc.IsComplete) ? 1 : 0);
                if (m_prvCompleted < complete)
                {
                    recalc = true;
                }

                int gencount = m_generations.Sum(gen => (gen != null) ? gen.Count : 0);
                if (gencount < m_prvCompleted)
                {
                    recalc = true;
                }

                m_prvCompleted = complete;
            }

            // calculate and cache the results - only when there are new GCs to process
            if (recalc)
            {
                // clear
                m_stats.Count = 0;
                m_stats.NumInduced = 0;
                m_stats.PinnedObjectSizes = 0;
                m_stats.NumWithPinEvents = 0;
                m_stats.PinnedObjectPercentage = 0;
                m_stats.NumWithPinPlugEvents = 0;
                m_stats.TotalSizeAfterMB = 0;
                m_stats.TotalPromotedMB = 0;
                m_stats.TotalSizePeakMB = 0;
                m_stats.TotalPauseTimeMSec = 0;
                m_stats.TotalAllocatedMB = 0;
                m_stats.MaxPauseDurationMSec = 0;
                m_stats.MaxSizePeakMB = 0;
                m_stats.MaxAllocRateMBSec = 0;
                m_stats.MaxSuspendDurationMSec = 0;

                // clear out the generation information
                for (int gen = 0; gen <= (int)Gens.Gen2; gen++)
                {
                    m_generations[gen] = new GCStats();
                }

                // calculate the stats
                for (int i = 0; i < m_gcs.Count; i++)
                {
                    TraceGC _gc = m_gcs[i];
                    if (!_gc.IsComplete)
                    {
                        continue;
                    }

                    // GC event details trickle in, and as they do the stats need to be regenerated
                    _gc.OnEnd(this);

                    _gc.Index = i;
                    if (_gc.PerHeapHistories != null && _gc.PerHeapHistories.Count > 0)  //per heap histories is not null
                    {
                        m_stats.HasDetailedGCInfo = true;
                    }

                    // Update the per-generation information 
                    m_generations[_gc.Generation].Count++;
                    bool isInduced = ((_gc.Reason == GCReason.Induced) || (_gc.Reason == GCReason.InducedNotForced));
                    if (isInduced)
                    {
                        (m_generations[_gc.Generation].NumInduced)++;
                    }

                    long PinnedObjectSizes = _gc.GetPinnedObjectSizes();
                    if (PinnedObjectSizes != 0)
                    {
                        m_generations[_gc.Generation].PinnedObjectSizes += PinnedObjectSizes;
                        m_generations[_gc.Generation].NumWithPinEvents++;
                    }

                    int PinnedObjectPercentage = _gc.GetPinnedObjectPercentage();
                    if (PinnedObjectPercentage != -1)
                    {
                        m_generations[_gc.Generation].PinnedObjectPercentage += _gc.GetPinnedObjectPercentage();
                        m_generations[_gc.Generation].NumWithPinPlugEvents++;
                    }

                    m_generations[_gc.Generation].TotalCpuMSec += _gc.GetTotalGCTime();
                    m_generations[_gc.Generation].TotalSizeAfterMB += _gc.HeapSizeAfterMB;

                    m_generations[_gc.Generation].TotalSizePeakMB += _gc.HeapSizePeakMB;
                    m_generations[_gc.Generation].TotalPromotedMB += _gc.PromotedMB;
                    m_generations[_gc.Generation].TotalPauseTimeMSec += _gc.PauseDurationMSec;
                    m_generations[_gc.Generation].TotalAllocatedMB += _gc.AllocedSinceLastGCMB;
                    m_generations[_gc.Generation].MaxPauseDurationMSec = Math.Max(m_generations[_gc.Generation].MaxPauseDurationMSec, _gc.PauseDurationMSec);
                    m_generations[_gc.Generation].MaxSizePeakMB = Math.Max(m_generations[_gc.Generation].MaxSizePeakMB, _gc.HeapSizePeakMB);
                    m_generations[_gc.Generation].MaxAllocRateMBSec = Math.Max(m_generations[_gc.Generation].MaxAllocRateMBSec, _gc.AllocRateMBSec);
                    m_generations[_gc.Generation].MaxPauseDurationMSec = Math.Max(m_generations[_gc.Generation].MaxPauseDurationMSec, _gc.PauseDurationMSec);
                    m_generations[_gc.Generation].MaxSuspendDurationMSec = Math.Max(m_generations[_gc.Generation].MaxSuspendDurationMSec, _gc.SuspendDurationMSec);

                    // And the totals 
                    m_stats.Count++;
                    if (isInduced)
                    {
                        m_stats.NumInduced++;
                    }

                    if (PinnedObjectSizes != 0)
                    {
                        m_stats.PinnedObjectSizes += PinnedObjectSizes;
                        m_stats.NumWithPinEvents++;
                    }
                    if (PinnedObjectPercentage != -1)
                    {
                        m_stats.PinnedObjectPercentage += _gc.GetPinnedObjectPercentage();
                        m_stats.NumWithPinPlugEvents++;
                    }
                    m_stats.TotalSizeAfterMB += _gc.HeapSizeAfterMB;
                    m_stats.TotalPromotedMB += _gc.PromotedMB;
                    m_stats.TotalSizePeakMB += _gc.HeapSizePeakMB;
                    m_stats.TotalPauseTimeMSec += _gc.PauseDurationMSec;
                    m_stats.TotalAllocatedMB += _gc.AllocedSinceLastGCMB;
                    m_stats.MaxPauseDurationMSec = Math.Max(m_stats.MaxPauseDurationMSec, _gc.PauseDurationMSec);
                    m_stats.MaxSizePeakMB = Math.Max(m_stats.MaxSizePeakMB, _gc.HeapSizePeakMB);
                    m_stats.MaxAllocRateMBSec = Math.Max(m_stats.MaxAllocRateMBSec, _gc.AllocRateMBSec);
                    m_stats.MaxSuspendDurationMSec = Math.Max(m_stats.MaxSuspendDurationMSec, _gc.SuspendDurationMSec);
                }

                m_prvcount = m_gcs.Count;
            }
        }
#endregion
    }

    /// <summary>
    /// Just-in-time compilation (JIT) specific details about this process
    /// </summary>
    public class TraceJitCompiler
    {
        /// <summary>
        /// Process view of JIT statistics
        /// </summary>
        public JITStats Stats() { return m_stats; }
        /// <summary>
        /// Process view of all methods jitted
        /// </summary>
        public List<TraceJittedMethod> Methods { get { return m_methods; } }

#region private
        internal JITStats m_stats = new JITStats();
        internal List<TraceJittedMethod> m_methods = new List<TraceJittedMethod>();
        internal double NextRelativeTimeStampMsec;
#endregion
    }

#region internal classes 
    internal class CircularBuffer<T> : IEnumerable<T>
        where T : class
    {
        private int StartIndex, AfterEndIndex, Size;
        private T[] Items;
        public CircularBuffer(int size)
        {
            if (size < 1)
            {
                throw new ArgumentException("size");
            }

            StartIndex = 0;
            AfterEndIndex = 0;
            Size = size + 1;
            Items = new T[Size];
        }

        public void Add(T item)
        {
            if (Next(AfterEndIndex) == StartIndex)
            {
                Items[StartIndex] = null;
                StartIndex = Next(StartIndex);
            }
            Items[AfterEndIndex] = item;
            AfterEndIndex = Next(AfterEndIndex);
        }

        private int Next(int i)
        {
            return (i == Size - 1) ? 0 : i + 1;
        }

        public IEnumerator<T> GetEnumerator()
        {
            for (int i = StartIndex; i != AfterEndIndex; i = Next(i))
            {
                yield return Items[i];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
#endregion // internal classes
}

namespace Microsoft.Diagnostics.Tracing.Analysis.GC
{
    /// <summary>
    /// 
    /// </summary>
    public class TraceGC
    {
        public enum BGCRevisitState
        {
            Concurrent = 0,
            NonConcurrent = 1,
            MaxState = 2,
        }

        public enum HeapType
        {
            SOH = 0,
            LOH = 1,
            MaxType = 2,
        }

        public struct BGCRevisitInfo
        {
            public long PagesRevisited;
            public long ObjectsRevisited;
        }

        public TraceGC(int heapCount)
        {
            HeapCount = heapCount;

            if (HeapCount > 1)
            {
                GCCpuServerGCThreads = new float[HeapCount];
            }

            pinnedObjectSizes = -1;
            TotalPinnedPlugSize = -1;
            TotalUserPinnedPlugSize = -1;
            duplicatedPinningReports = 0;
        }

        /// <summary>
        /// Primary GC information
        /// </summary>
        public int Number;                      // Set in GCStart (starts at 1, unique for process)
        /// <summary>
        /// Type of the GC, eg. NonConcurrent, Background or Foreground
        /// </summary>
        public GCType Type;                     // Set in GCStart
        /// <summary>
        /// Reason for the GC, eg. exhausted small heap, etc.
        /// </summary>
        public GCReason Reason;                 // Set in GCStart
        /// <summary>
        /// Generation of the heap collected.  If you compare Generation at the start and stop GC events they may differ.
        /// </summary>
        public int Generation;                  // Set in GCStop(Generation 0, 1 or 2)
        /// <summary>
        /// Time relative to the start of the trace.  Useful for ordering
        /// </summary>
        public double StartRelativeMSec;           //  Set in Start, does not include suspension.  
        /// <summary>
        /// Duration of the GC, excluding the suspension time
        /// </summary>
        public double DurationMSec;             // Set in Stop This is JUST the GC time (not including suspension) That is Stop-Start.  
        /// <summary>
        /// Duration the EE suspended the process
        /// </summary>
        public double PauseDurationMSec;       // Total time EE is suspended (can be less than GC time for background)
        /// <summary>
        /// Time the EE took to suspend all the threads
        /// </summary>
        public double SuspendDurationMSec;      // Time it takes to do the suspension
        /// <summary>
        /// Percentage time the GC took compared to the process lifetime
        /// </summary>
        public double PercentTimeInGC { get { return (float)(GetTotalGCTime() * 100 / ProcessCpuMSec); } }          // Of all the CPU, how much as a percentage is spent in the GC since end of last GC.
        /// <summary>
        /// The number of CPU samples gathered for the lifetime of this process
        /// </summary>
        public double ProcessCpuMSec;               // The amount of CPU time the process consumed since the last GC.
        /// <summary>
        /// The number of CPU samples gathered during a GC
        /// </summary>
        public double GCCpuMSec;                 // The amount of CPU time this GC consumed.
        /// <summary>
        /// Mark time information per heap.  Key is the heap number
        /// </summary>
        public Dictionary<int /*heap number*/, MarkInfo> PerHeapMarkTimes;      // The dictionary of heap number and info on time it takes to mark various roots.
        internal bool fMarkTimesConverted;
        /// <summary>
        /// Time since the last EE restart
        /// </summary>
        public double DurationSinceLastRestartMSec;  //  Set in GCStart
        /// <summary>
        ///Realtive time to the trace of when the GC pause began
        /// </summary>
        public double PauseStartRelativeMSec;        //  Set in GCStart
        /// <summary>
        /// Marks if the GC is in a completed state
        /// </summary>
        public bool IsComplete;
        // 
        // The 2 fields below would only make sense if the type is BackgroundGC.
        // 
        public BGCPhase BGCCurrentPhase;
        public BGCRevisitInfo[][] BGCRevisitInfoArr;
        public double BGCFinalPauseMSec;

        public void EnsureBGCRevisitInfoAlloc()
        {
            if (BGCRevisitInfoArr == null)
            {
                BGCRevisitInfoArr = new TraceGC.BGCRevisitInfo[(int)TraceGC.BGCRevisitState.MaxState][];
                for (int i = 0; i < (int)TraceGC.BGCRevisitState.MaxState; i++)
                {
                    BGCRevisitInfoArr[i] = new TraceGC.BGCRevisitInfo[(int)TraceGC.HeapType.MaxType];
                }
            }
        }

        /// <summary>
        /// Server GC histories
        /// </summary>
        //list of workload histories per server GC heap
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public List<ServerGcHistory> ServerGcHeapHistories = new List<ServerGcHistory>();
        /// <summary>
        /// Amount of memory allocated since last GC.  Requires GCAllocationTicks enabled.  The 
        /// data is split into small and large heaps
        /// </summary>
        public double[] AllocedSinceLastGCBasedOnAllocTickMB = { 0.0, 0.0 };// Set in HeapStats
        /// <summary>
        /// Number of heaps.  -1 is the default
        /// </summary>
        public int HeapCount { get; private set; } = -1;
        /// <summary>
        /// Calculate the size of all pinned objects
        /// </summary>
        /// <returns></returns>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public long GetPinnedObjectSizes()
        {
            if (pinnedObjectSizes == -1)
            {
                pinnedObjectSizes = 0;
                if (PinnedObjects != null)
                {
                    foreach (KeyValuePair<ulong, long> item in PinnedObjects)
                    {
                        pinnedObjectSizes += item.Value;
                    }
                }
            }
            return pinnedObjectSizes;
        }
        /// <summary>
        /// Percentage of the pinned objects created by the user
        /// </summary>
        /// <returns></returns>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public int GetPinnedObjectPercentage()
        {
            if (TotalPinnedPlugSize == -1)
            {
                TotalPinnedPlugSize = 0;
                TotalUserPinnedPlugSize = 0;

                if (PinnedObjects != null && PinnedPlugs != null)
                {
                    foreach (KeyValuePair<ulong, long> item in PinnedObjects)
                    {
                        ulong Address = item.Key;

                        for (int i = 0; i < PinnedPlugs.Count; i++)
                        {
                            if ((Address >= PinnedPlugs[i].Start) && (Address < PinnedPlugs[i].End))
                            {
                                PinnedPlugs[i].PinnedByUser = true;
                                break;
                            }
                        }
                    }
                }

                if (PinnedPlugs != null)
                {
                    for (int i = 0; i < PinnedPlugs.Count; i++)
                    {
                        long Size = (long)(PinnedPlugs[i].End - PinnedPlugs[i].Start);
                        TotalPinnedPlugSize += Size;
                        if (PinnedPlugs[i].PinnedByUser)
                        {
                            TotalUserPinnedPlugSize += Size;
                        }
                    }
                }
            }

            return ((TotalPinnedPlugSize == 0) ? -1 : (int)((double)pinnedObjectSizes * 100 / (double)TotalPinnedPlugSize));
        }
        /// <summary>
        /// Total time taken by the GC
        /// </summary>
        /// <returns></returns>
        public double GetTotalGCTime()
        {
            if (_TotalGCTimeMSec < 0)
            {
                _TotalGCTimeMSec = 0;
                if (GCCpuServerGCThreads != null)
                {
                    for (int i = 0; i < GCCpuServerGCThreads.Length; i++)
                    {
                        _TotalGCTimeMSec += GCCpuServerGCThreads[i];
                    }
                }
                _TotalGCTimeMSec += GCCpuMSec;
            }

            Debug.Assert(_TotalGCTimeMSec >= 0);
            return _TotalGCTimeMSec;
        }
        /// <summary>
        /// Friendly GC name including type, reason and generation
        /// </summary>
        public object GCGenerationName
        {
            get
            {
                string typeSuffix = "";
                if (Type == GCType.NonConcurrentGC)
                {
                    typeSuffix = "N";
                }
                else if (Type == GCType.BackgroundGC)
                {
                    typeSuffix = "B";
                }
                else if (Type == GCType.ForegroundGC)
                {
                    typeSuffix = "F";
                }

                string inducedSuffix = "";
                if (Reason == GCReason.Induced)
                {
                    inducedSuffix = "I";
                }

                if (Reason == GCReason.InducedNotForced)
                {
                    inducedSuffix = "i";
                }

                return Generation.ToString() + typeSuffix + inducedSuffix;
            }
        }
        /// <summary>
        /// Heap size after GC (mb)
        /// </summary>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public double HeapSizeAfterMB
        {
            get
            {
                if (null != HeapStats)
                {
                    return (HeapStats.GenerationSize0 + HeapStats.GenerationSize1 + HeapStats.GenerationSize2 + HeapStats.GenerationSize3 + HeapStats.GenerationSize4) / 1000000.0;
                }
                else
                {
                    return -1.0;
                }
            }
        }
        /// <summary>
        /// Amount of memory promoted with GC (mb)
        /// </summary>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public double PromotedMB
        {
            get
            {
                if (null != HeapStats)
                {
                    return (HeapStats.TotalPromotedSize0 + HeapStats.TotalPromotedSize1 +
                       HeapStats.TotalPromotedSize2 + HeapStats.TotalPromotedSize3 + HeapStats.TotalPromotedSize4) / 1000000.0;
                }
                else
                {
                    return -1.0;
                }
            }
        }
        /// <summary>
        /// Memory survival percentage by generation
        /// </summary>
        /// <param name="gen"></param>
        /// <returns></returns>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public double SurvivalPercent(Gens gen)
        {
            double retSurvRate = double.NaN;

            long SurvRate = 0;

            if (gen == Gens.GenLargeObj)
            {
                if (Generation < 2)
                {
                    return retSurvRate;
                }
            }
            else if ((int)gen > Generation)
            {
                return retSurvRate;
            }

            if (PerHeapHistories != null && PerHeapHistories.Count > 0)
            {
                for (int i = 0; i < PerHeapHistories.Count; i++)
                {
                    SurvRate += PerHeapHistories[i].GenData[(int)gen].SurvRate;
                }

                SurvRate /= PerHeapHistories.Count;
            }

            retSurvRate = SurvRate;

            return retSurvRate;
        }
        /// <summary>
        /// Heap size by generation after GC (mb)
        /// </summary>
        /// <param name="gen"></param>
        /// <returns></returns>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public double GenSizeAfterMB(Gens gen)
        {
            if (gen == Gens.GenPinObj)
            {
                return HeapStats.GenerationSize4 / 1000000.0;
            }

            if (gen == Gens.GenLargeObj)
            {
                return HeapStats.GenerationSize3 / 1000000.0;
            }

            if (gen == Gens.Gen2)
            {
                return HeapStats.GenerationSize2 / 1000000.0;
            }

            if (gen == Gens.Gen1)
            {
                return HeapStats.GenerationSize1 / 1000000.0;
            }

            if (gen == Gens.Gen0)
            {
                return HeapStats.GenerationSize0 / 1000000.0;
            }

            Debug.Assert(false);
            return double.NaN;
        }
        /// <summary>
        /// Heap fragmentation by generation (mb)
        /// </summary>
        /// <param name="gen"></param>
        /// <returns></returns>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public double GenFragmentationMB(Gens gen)
        {
            if (PerHeapHistories == null)
            {
                return double.NaN;
            }

            double ret = 0.0;
            for (int HeapIndex = 0; HeapIndex < PerHeapHistories.Count; HeapIndex++)
            {
                ret += PerHeapHistories[HeapIndex].GenData[(int)gen].Fragmentation / 1000000.0;
            }

            return ret;
        }
        /// <summary>
        /// Percentage of heap fragmented by generation 
        /// </summary>
        /// <param name="gen"></param>
        /// <returns></returns>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public double GenFragmentationPercent(Gens gen)
        {
            return (GenFragmentationMB(gen) * 100.0 / GenSizeAfterMB(gen));
        }
        /// <summary>
        /// Amount of memory at the start of the GC by generation (mb)
        /// </summary>
        /// <param name="gen"></param>
        /// <returns></returns>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public double GenInMB(Gens gen)
        {
            if (PerHeapHistories == null)
            {
                return double.NaN;
            }

            double ret = 0.0;
            for (int HeapIndex = 0; HeapIndex < PerHeapHistories.Count; HeapIndex++)
            {
                ret += PerHeapHistories[HeapIndex].GenData[(int)gen].In / 1000000.0;
            }

            return ret;
        }
        /// <summary>
        /// Amount of memory after the gc by generation (mb)
        /// </summary>
        /// <param name="gen"></param>
        /// <returns></returns>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public double GenOutMB(Gens gen)
        {
            if (PerHeapHistories == null)
            {
                return double.NaN;
            }

            double ret = 0.0;
            for (int HeapIndex = 0; HeapIndex < PerHeapHistories.Count; HeapIndex++)
            {
                ret += PerHeapHistories[HeapIndex].GenData[(int)gen].Out / 1000000.0;
            }

            return ret;
        }
        /// <summary>
        /// Memory promoted by generation (mb)
        /// Note that in 4.0 TotalPromotedSize is not entirely accurate (since it doesn't
        /// count the pins that got demoted. We could consider using the PerHeap event data
        /// to compute the accurate promoted size. 
        /// In 4.5 this is accurate.
        /// </summary>
        /// <param name="gen"></param>
        /// <returns></returns>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public double GenPromotedMB(Gens gen)
        {
            if (gen == Gens.GenPinObj)
            {
                return HeapStats.TotalPromotedSize4 / 1000000.0;
            }

            if (gen == Gens.GenLargeObj)
            {
                return HeapStats.TotalPromotedSize3 / 1000000.0;
            }

            if (gen == Gens.Gen2)
            {
                return HeapStats.TotalPromotedSize2 / 1000000.0;
            }

            if (gen == Gens.Gen1)
            {
                return HeapStats.TotalPromotedSize1 / 1000000.0;
            }

            if (gen == Gens.Gen0)
            {
                return HeapStats.TotalPromotedSize0 / 1000000.0;
            }

            Debug.Assert(false);
            return double.NaN;
        }
        /// <summary>
        /// Heap budget by generation (mb)
        /// </summary>
        /// <param name="gen"></param>
        /// <returns></returns>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public double GenBudgetMB(Gens gen)
        {
            if (PerHeapHistories == null)
            {
                return double.NaN;
            }

            double budget = 0.0;
            for (int HeapIndex = 0; HeapIndex < PerHeapHistories.Count; HeapIndex++)
            {
                budget += PerHeapHistories[HeapIndex].GenData[(int)gen].Budget / 1000000.0;
            }

            return budget;
        }
        /// <summary>
        /// Object size by generation after GC (mb)
        /// </summary>
        /// <param name="gen"></param>
        /// <returns></returns>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public double GenObjSizeAfterMB(Gens gen)
        {
            if (PerHeapHistories == null)
            {
                return double.NaN;
            }

            double objSizeAfter = 0.0;
            for (int HeapIndex = 0; HeapIndex < PerHeapHistories.Count; HeapIndex++)
            {
                objSizeAfter += PerHeapHistories[HeapIndex].GenData[(int)gen].ObjSizeAfter / 1000000.0;
            }

            return objSizeAfter;
        }
        /// <summary>
        /// Global condemned reasons by GC
        /// </summary>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public GCCondemnedReasons GlobalCondemnedReasons
        {
            get
            {
                if ((GlobalHeapHistory != null) && (GlobalHeapHistory.HasCondemnReasons0) && (_GlobalCondemnedReasons == null))
                {
                    _GlobalCondemnedReasons = new GCCondemnedReasons();
                    _GlobalCondemnedReasons.EncodedReasons.Reasons = GlobalHeapHistory.CondemnReasons0;
                    _GlobalCondemnedReasons.EncodedReasons.ReasonsEx = GlobalHeapHistory.CondemnReasons1;
                    _GlobalCondemnedReasons.CondemnedReasonGroups = new byte[(int)CondemnedReasonGroup.Max];
                    _GlobalCondemnedReasons.Decode(/* Version = */ 3);
                }
                return _GlobalCondemnedReasons;
            }
        }
        /// <summary>
        /// Heap condemned reasons by GC
        /// </summary>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public GCCondemnedReasons[] PerHeapCondemnedReasons
        {
            get
            {
                if ((PerHeapHistories != null) && (_PerHeapCondemnedReasons == null))
                {
                    int NumHeaps = PerHeapHistories.Count;
                    _PerHeapCondemnedReasons = new GCCondemnedReasons[NumHeaps];

                    for (int HeapIndex = 0; HeapIndex < NumHeaps; HeapIndex++)
                    {
                        _PerHeapCondemnedReasons[HeapIndex] = new GCCondemnedReasons();
                        _PerHeapCondemnedReasons[HeapIndex].EncodedReasons.Reasons = PerHeapHistories[HeapIndex].CondemnReasons0;
                        if (PerHeapHistories[HeapIndex].HasCondemnReasons1)
                        {
                            _PerHeapCondemnedReasons[HeapIndex].EncodedReasons.ReasonsEx = PerHeapHistories[HeapIndex].CondemnReasons1;
                        }
                        _PerHeapCondemnedReasons[HeapIndex].CondemnedReasonGroups = new byte[(int)CondemnedReasonGroup.Max];
                        _PerHeapCondemnedReasons[HeapIndex].Decode(PerHeapHistories[HeapIndex].Version);
                    }
                }

                return _PerHeapCondemnedReasons;
            }
        }
        /// <summary>
        /// Identify the first and greatest condemned heap
        /// </summary>
        /// <returns></returns>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public int FindFirstHighestCondemnedHeap()
        {
            int GenNumberHighest = (int)Generation;
            for (int HeapIndex = 0; HeapIndex < PerHeapCondemnedReasons.Length; HeapIndex++)
            {
                int gen = PerHeapCondemnedReasons[HeapIndex].CondemnedReasonGroups[(int)CondemnedReasonGroup.Final_Generation];
                if (gen == GenNumberHighest)
                {
                    return HeapIndex;
                }
            }

            return 0;
        }
        /// <summary>
        /// Indicates that the GC has low ephemeral space
        /// </summary>
        /// <returns></returns>
        public bool IsLowEphemeral()
        {
            return CondemnedReasonGroupSet(CondemnedReasonGroup.Low_Ephemeral);
        }
        /// <summary>
        /// Indicates that the GC was not compacting
        /// </summary>
        /// <returns></returns>
        public bool IsNotCompacting()
        {
            if (GlobalHeapHistory == null)
            {
                return true;
            }

            return ((GlobalHeapHistory.GlobalMechanisms & (GCGlobalMechanisms.Compaction)) != 0);
        }
        /// <summary>
        /// Returns the condemned reason for this heap
        /// </summary>
        /// <param name="ReasonsInfo"></param>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public void GetCondemnedReasons(Dictionary<CondemnedReasonGroup, int> ReasonsInfo)
        {
            // Older versions of the runtime does not have this event. So even for a complete GC, we may not have this
            // info.
            if (PerHeapCondemnedReasons == null || PerHeapCondemnedReasons.Length == 0)
            {
                return;
            }

            int HeapIndexHighestGen = 0;
            if (PerHeapCondemnedReasons.Length > 1)
            {
                HeapIndexHighestGen = FindFirstHighestCondemnedHeap();
            }

            byte[] ReasonGroups = PerHeapCondemnedReasons[HeapIndexHighestGen].CondemnedReasonGroups;

            // These 2 reasons indicate a gen number. If the number is the same as the condemned gen, we 
            // include this reason.
            for (int i = (int)CondemnedReasonGroup.Alloc_Exceeded; i <= (int)CondemnedReasonGroup.Time_Tuning; i++)
            {
                if (ReasonGroups[i] == Generation)
                {
                    AddCondemnedReason(ReasonsInfo, (CondemnedReasonGroup)i);
                }
            }

            if (ReasonGroups[(int)CondemnedReasonGroup.Induced] != 0)
            {
                if (ReasonGroups[(int)CondemnedReasonGroup.Initial_Generation] == Generation)
                {
                    AddCondemnedReason(ReasonsInfo, CondemnedReasonGroup.Induced);
                }
            }

            // The rest of the reasons are conditions so include the ones that are set.
            for (int i = (int)CondemnedReasonGroup.Low_Ephemeral; i < (int)CondemnedReasonGroup.Max; i++)
            {
                if (ReasonGroups[i] != 0)
                {
                    AddCondemnedReason(ReasonsInfo, (CondemnedReasonGroup)i);
                }
            }
        }
        /// <summary>
        /// Per heap statistics
        /// </summary>
        public List<GCPerHeapHistory> PerHeapHistories = new List<GCPerHeapHistory>();
        /// <summary>
        /// Sum of the pinned plug sizes
        /// </summary>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public long TotalPinnedPlugSize;
        /// <summary>
        /// Sum of the user created pinned plug sizes
        /// </summary>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public long TotalUserPinnedPlugSize;
        /// <summary>
        /// Per heap statstics
        /// </summary>
        public GCHeapStats HeapStats;
        /// <summary>
        /// Large object heap wait threads
        /// </summary>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public Dictionary<int, BGCAllocWaitInfo> LOHWaitThreads;
        /// <summary>
        /// Process heap statistics
        /// </summary>
        public GCGlobalHeapHistory GlobalHeapHistory;
        /// <summary>
        /// Free list efficiency statistics
        /// </summary>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public FreeListEfficiency FreeList;
        /// <summary>
        /// Memory allocated since last GC (mb)
        /// </summary>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public double AllocedSinceLastGCMB;
        /// <summary>
        /// Ratio of heap size before and after
        /// </summary>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public double RatioPeakAfter;
        /// <summary>
        /// Ratio of allocations since last GC over time executed
        /// </summary>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public double AllocRateMBSec;
        /// <summary>
        /// Peak heap size before GCs (mb)
        /// </summary>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public double HeapSizePeakMB;
        /// <summary>
        /// Per generation view of user allocated data
        /// </summary>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public double[] UserAllocated = new double[(int)Gens.MaxGenCount];
        /// <summary>
        /// Heap size before gc (mb)
        /// </summary>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public double HeapSizeBeforeMB;
        /// <summary>
        /// Per generation view of heap sizes before GC (mb)
        /// </summary>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public double[] GenSizeBeforeMB = new double[(int)Gens.MaxGenCount];
        /// <summary>
        /// This represents the percentage time spent paused for this GC since the last GC completed. 
        /// </summary>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public double PauseTimePercentageSinceLastGC;

#region private
        internal void OnEnd(TraceGarbageCollector details)
        {
            IsComplete = true;

            // calculate core gc values
            pinnedObjectSizes = -1;
            TotalPinnedPlugSize = -1;
            GetPinnedObjectSizes();
            GetPinnedObjectPercentage();
            FreeList = GetFreeListEfficiency(details.GCs, this);
            AllocedSinceLastGCMB = GetAllocedSinceLastGCMB(details.GCs, this);
            RatioPeakAfter = GetRatioPeakAfter(details.GCs, this);
            AllocRateMBSec = GetAllocRateMBSec(details.GCs, this);
            HeapSizePeakMB = GetHeapSizePeakMB(details.GCs, this);
            UserAllocated[(int)Gens.Gen0] = GetUserAllocated(details.GCs, this, Gens.Gen0);
            UserAllocated[(int)Gens.GenLargeObj] = GetUserAllocated(details.GCs, this, Gens.GenLargeObj);
            HeapSizeBeforeMB = GetHeapSizeBeforeMB(details.GCs, this);
            for (int gen = (int)Gens.Gen0; gen <= (int)Gens.GenLargeObj; gen++)
            {
                GenSizeBeforeMB[gen] = GetGenSizeBeforeMB(details.GCs, this, (Gens)gen);
            }

            PauseTimePercentageSinceLastGC = GetPauseTimePercentageSinceLastGC(details.GCs, this);

            // calculate core gc process values
            details.m_stats.ProcessDuration = GetProcessDuration(details.m_stats, details.GCs, this);

            BlockingGCEnd();

            // clear out large internal data, after the data was used to calculate satistics
            if (PinnedObjects != null) { PinnedObjects.Clear(); PinnedObjects = null; }
            if (PinnedPlugs != null) { PinnedPlugs.Clear(); PinnedPlugs = null; }
        }

        internal static double GetProcessDuration(GCStats stats, List<TraceGC> GCs, TraceGC gc)
        {
            double startRelativeMSec = 0.0;

            for (int i = 0; i < GCs.Count; i++)
            {
                if (GCs[i].IsComplete)
                {
                    startRelativeMSec = GCs[i].PauseStartRelativeMSec;
                    break;
                }
            }

            if (startRelativeMSec == 0.0)
            {
                return 0;
            }

            // Get the end time of the last GC.
            double endRelativeMSec = stats.lastRestartEndTimeRelativeMSec;
            return (endRelativeMSec - startRelativeMSec);
        }

        internal static double GetPauseTimePercentageSinceLastGC(List<TraceGC> GCs, TraceGC gc)
        {
            double pauseTimePercentage;

            if (gc.Type == GCType.BackgroundGC)
            {
                // Find all GCs that occurred during the current background GC.
                double startTimeRelativeMSec = gc.StartRelativeMSec;
                double endTimeRelativeMSec = gc.StartRelativeMSec + gc.DurationMSec;

                // Calculate the pause time for this BGC.
                // Pause time is defined as pause time for the BGC + pause time for all FGCs that ran during the BGC.
                double totalPauseTime = gc.PauseDurationMSec;

                if (gc.Index + 1 < GCs.Count)
                {
                    TraceGC gcEvent;
                    for (int i = gc.Index + 1; i < GCs.Count; ++i)
                    {
                        gcEvent = GCs[i];
                        if ((gcEvent.StartRelativeMSec >= startTimeRelativeMSec) && (gcEvent.StartRelativeMSec < endTimeRelativeMSec))
                        {
                            totalPauseTime += gcEvent.PauseDurationMSec;
                        }
                        else
                        {
                            // We've finished processing all FGCs that occurred during this BGC.
                            break;
                        }
                    }
                }

                // Get the elapsed time since the previous GC finished.
                int previousGCIndex = gc.Index - 1;
                double previousGCStopTimeRelativeMSec;
                if (previousGCIndex >= 0)
                {
                    TraceGC previousGCEvent = GCs[previousGCIndex];
                    previousGCStopTimeRelativeMSec = previousGCEvent.StartRelativeMSec + previousGCEvent.DurationMSec;
                }
                else
                {
                    // Backstop in case this is the first GC.
                    previousGCStopTimeRelativeMSec = GCs[0].StartRelativeMSec;
                }

                double totalTime = (gc.StartRelativeMSec + gc.DurationMSec) - previousGCStopTimeRelativeMSec;
                pauseTimePercentage = (totalPauseTime * 100) / (totalTime);
            }
            else
            {
                double totalTime = gc.PauseDurationMSec + gc.DurationSinceLastRestartMSec;
                pauseTimePercentage = (gc.PauseDurationMSec * 100) / (totalTime);
            }

            Debug.Assert(pauseTimePercentage <= 100);
            return pauseTimePercentage;
        }

        internal static FreeListEfficiency GetFreeListEfficiency(List<TraceGC> GCs, TraceGC gc)
        {
            Gens gen = Gens.Gen2;
            FreeListEfficiency freeList = new FreeListEfficiency();

            // I am not worried about gen0 or LOH's free list efficiency right now - it's 
            // calculated differently.
            if ((gc.PerHeapHistories == null) ||
                (gc.PerHeapHistories.Count == 0) ||
                (gen == Gens.Gen0) ||
                (gen == Gens.GenLargeObj) ||
                (gc.Index <= 0) ||
                !(gc.PerHeapHistories[0].VersionRecognized))
            {
                return freeList;
            }

            int YoungerGen = (int)gen - 1;

            if (gc.Generation != YoungerGen)
            {
                return freeList;
            }

            if (gc.PerHeapHistories[0].HasFreeListAllocated && gc.PerHeapHistories[0].HasFreeListRejected)
            {
                freeList.Allocated = 0;
                freeList.FreeListConsumed = 0;
                for (int HeapIndex = 0; HeapIndex < gc.PerHeapHistories.Count; HeapIndex++)
                {
                    GCPerHeapHistory hist = (GCPerHeapHistory)gc.PerHeapHistories[HeapIndex];
                    freeList.Allocated += hist.FreeListAllocated;
                    freeList.FreeListConsumed += hist.FreeListAllocated + hist.FreeListRejected;
                }
                freeList.Valid = true;
                return freeList;
            }

            // I am not using MB here because what's promoted from gen1 can easily be less than a MB.
            double YoungerGenOut = 0;
            double FreeListBefore = 0;
            double FreeListAfter = 0;
            // Includes fragmentation. This lets us know if we had to expand the size.
            double GenSizeBefore = 0;
            double GenSizeAfter = 0;
            for (int HeapIndex = 0; HeapIndex < gc.PerHeapHistories.Count; HeapIndex++)
            {
                YoungerGenOut += gc.PerHeapHistories[HeapIndex].GenData[YoungerGen].Out;
                GenSizeBefore += gc.PerHeapHistories[HeapIndex].GenData[(int)gen].SizeBefore;
                GenSizeAfter += gc.PerHeapHistories[HeapIndex].GenData[(int)gen].SizeAfter;
                // Occasionally I've seen a GC in the middle that simply missed some events,
                // some of which are PerHeap hist events so we don't have data.
                if (GCs[gc.Index - 1].PerHeapHistories == null || GCs[gc.Index - 1].PerHeapHistories.Count == 0)
                {
                    return freeList;
                }

                if (gc.PerHeapHistories[HeapIndex].GenData[(int)gen].HasFreeListSpaceAfter && gc.PerHeapHistories[HeapIndex].GenData[(int)gen].HasFreeListSpaceBefore)
                {
                    FreeListBefore += gc.PerHeapHistories[HeapIndex].GenData[(int)gen].FreeListSpaceBefore;
                    FreeListAfter += gc.PerHeapHistories[HeapIndex].GenData[(int)gen].FreeListSpaceAfter;
                }
                else
                {
                    FreeListBefore += GCs[gc.Index - 1].PerHeapHistories[HeapIndex].GenData[(int)gen].Fragmentation;
                    FreeListAfter += gc.PerHeapHistories[HeapIndex].GenData[(int)gen].Fragmentation;
                }
            }

            double GenSizeGrown = GenSizeAfter - GenSizeBefore;

            // This is the most accurate situation we can calculuate (if it's not accurate it means
            // we are over estimating which is ok.
            if ((GenSizeGrown == 0) && ((FreeListBefore > 0) && (FreeListAfter >= 0)))
            {
                freeList.Allocated = YoungerGenOut;
                freeList.FreeListConsumed = FreeListBefore - FreeListAfter;
                // We don't know how much of the survived is pinned so we are overestimating here.
                if (freeList.Allocated < freeList.FreeListConsumed)
                {
                    freeList.Valid = true;
                    return freeList;
                }
            }

            return freeList;
        }

        internal static double GetAllocedSinceLastGCMB(List<TraceGC> GCs, TraceGC gc)
        {
            return GetUserAllocated(GCs, gc, Gens.Gen0) + GetUserAllocated(GCs, gc, Gens.GenLargeObj);
        }

        internal static double GetRatioPeakAfter(List<TraceGC> GCs, TraceGC gc) { if (gc.HeapSizeAfterMB == 0) { return 0; } return GetHeapSizePeakMB(GCs, gc) / gc.HeapSizeAfterMB; }
        internal static double GetAllocRateMBSec(List<TraceGC> GCs, TraceGC gc) { return GetAllocedSinceLastGCMB(GCs, gc) * 1000.0 / gc.DurationSinceLastRestartMSec; }

        internal static double GetHeapSizePeakMB(List<TraceGC> GCs, TraceGC gc)
        {
            var ret = GetHeapSizeBeforeMB(GCs, gc);
            if (gc.Type == GCType.BackgroundGC)
            {
                var BgGcEndedRelativeMSec = gc.PauseStartRelativeMSec + gc.DurationMSec;
                for (int i = gc.Index + 1; i < GCs.Count; i++)
                {
                    var _event = GCs[i];
                    if (BgGcEndedRelativeMSec < _event.PauseStartRelativeMSec)
                    {
                        break;
                    }

                    ret = Math.Max(ret, GetHeapSizeBeforeMB(GCs, _event));
                }
            }
            return ret;
        }

        /// <summary>
        /// Get what's allocated into gen0 or gen3. For server GC this gets the total for 
        /// all heaps.
        /// </summary>
        internal static double GetUserAllocated(List<TraceGC> GCs, TraceGC gc, Gens gen)
        {
            Debug.Assert((gen == Gens.Gen0) || (gen == Gens.GenLargeObj));

            if ((gc.Type == GCType.BackgroundGC) && (gen == Gens.Gen0))
            {
                return gc.AllocedSinceLastGCBasedOnAllocTickMB[(int)gen];
            }

            if (gc.PerHeapHistories != null && gc.Index > 0 && GCs[gc.Index - 1].PerHeapHistories != null)
            {
                double TotalAllocated = 0;
                if (gc.Index > 0)
                {
                    for (int i = 0; i < gc.PerHeapHistories.Count; i++)
                    {
                        double Allocated = GetUserAllocatedPerHeap(GCs, gc, i, gen);

                        TotalAllocated += Allocated / 1000000.0;
                    }

                    return TotalAllocated;
                }
                else
                {
                    return GetGenSizeBeforeMB(GCs, gc, gen);
                }
            }

            return gc.AllocedSinceLastGCBasedOnAllocTickMB[(gen == Gens.Gen0) ? 0 : 1];
        }

        internal static double GetHeapSizeBeforeMB(List<TraceGC> GCs, TraceGC gc)
        {
            double ret = 0;
            for (Gens gen = Gens.Gen0; gen <= Gens.GenLargeObj; gen++)
            {
                ret += GetGenSizeBeforeMB(GCs, gc, gen);
            }

            return ret;
        }

        // Per generation stats.  
        internal static double GetGenSizeBeforeMB(List<TraceGC> GCs, TraceGC gc, Gens gen)
        {
            if (gc.PerHeapHistories != null && gc.PerHeapHistories.Count > 0)
            {
                double ret = 0.0;
                for (int HeapIndex = 0; HeapIndex < gc.PerHeapHistories.Count; HeapIndex++)
                {
                    ret += gc.PerHeapHistories[HeapIndex].GenData[(int)gen].SizeBefore / 1000000.0;
                }

                return ret;
            }

            // When we don't have perheap history we can only estimate for gen0 and gen3.
            double Gen0SizeBeforeMB = 0;
            if (gen == Gens.Gen0)
            {
                Gen0SizeBeforeMB = gc.AllocedSinceLastGCBasedOnAllocTickMB[0];
            }

            if (gc.Index == 0)
            {
                return ((gen == Gens.Gen0) ? Gen0SizeBeforeMB : 0);
            }

            // Find a previous HeapStats.  
            GCHeapStats heapStats = null;
            for (int j = gc.Index - 1; ; --j)
            {
                if (j == 0)
                {
                    return 0;
                }

                heapStats = GCs[j].HeapStats;
                if (heapStats != null)
                {
                    break;
                }
            }
            if (gen == Gens.Gen0)
            {
                return Math.Max((heapStats.GenerationSize0 / 1000000.0), Gen0SizeBeforeMB);
            }

            if (gen == Gens.Gen1)
            {
                return heapStats.GenerationSize1 / 1000000.0;
            }

            if (gen == Gens.Gen2)
            {
                return heapStats.GenerationSize2 / 1000000.0;
            }

            if (gen == Gens.GenLargeObj)
            {
                if (gc.HeapStats != null)
                {
                    return Math.Max(heapStats.GenerationSize3, gc.HeapStats.GenerationSize3) / 1000000.0;
                }
                else
                {
                    return heapStats.GenerationSize3 / 1000000.0;
                }
            }

            Debug.Assert(gen == Gens.GenPinObj);

            if (gc.HeapStats != null)
            {
                return Math.Max(heapStats.GenerationSize4, gc.HeapStats.GenerationSize4) / 1000000.0;
            }
            else
            {
                return heapStats.GenerationSize4 / 1000000.0;
            }

        }

        /// <summary>
        /// For a given heap, get what's allocated into gen0 or gen3.
        /// We calculate this differently on 4.0, 4.5 Beta and 4.5 RC+.
        /// The caveat with 4.0 and 4.5 Beta is that when survival rate is 0,
        /// We don't know how to calculate the allocated - so we just use the
        /// last GC's budget (We should indicate this in the tool)
        /// </summary>
        private static double GetUserAllocatedPerHeap(List<TraceGC> GCs, TraceGC gc, int HeapIndex, Gens gen)
        {
            long prevObjSize = 0;
            if (gc.Index > 0)
            {
                // If the prevous GC has that heap get its size.  
                var perHeapGenData = GCs[gc.Index - 1].PerHeapHistories;
                if (HeapIndex < perHeapGenData.Count)
                {
                    prevObjSize = perHeapGenData[HeapIndex].GenData[(int)gen].ObjSizeAfter;
                    // Note that for gen3 we need to do something extra as its after data may not be updated if the last
                    // GC was a gen0 GC (A GC will update its size after data up to (Generation + 1) because that's all 
                    // it would change).
                    if ((gen == Gens.GenLargeObj) && (prevObjSize == 0) && (GCs[gc.Index - 1].Generation < (int)Gens.Gen1))
                    {
                        prevObjSize = perHeapGenData[HeapIndex].GenData[(int)gen].ObjSpaceBefore;
                    }
                }
            }
            GCPerHeapHistoryGenData currentGenData = gc.PerHeapHistories[HeapIndex].GenData[(int)gen];
            double Allocated;

            if (currentGenData.HasObjSpaceBefore)
            {
                Allocated = currentGenData.ObjSpaceBefore - prevObjSize;
            }
            else
            {
                long survRate = currentGenData.SurvRate;

                if (survRate == 0)
                {
                    Allocated = EstimateAllocSurv0(GCs, gc, HeapIndex, gen);
                }
                else
                {
                    long currentObjSize = currentGenData.ObjSizeAfter;
                    Allocated = (currentGenData.Out + currentObjSize) * 100 / survRate - prevObjSize;
                }
            }

            return Allocated;
        }

        // When survival rate is 0, for certain releases (see comments for GetUserAllocatedPerHeap)
        // we need to estimate.
        private static double EstimateAllocSurv0(List<TraceGC> GCs, TraceGC gc, int HeapIndex, Gens gen)
        {
            if (gc.HasAllocTickEvents)
            {
                return gc.AllocedSinceLastGCBasedOnAllocTickMB[(gen == Gens.Gen0) ? 0 : 1];
            }
            else
            {
                if (gc.Index > 0)
                {
                    // If the prevous GC has that heap get its size.  
                    var perHeapGenData = GCs[gc.Index - 1].PerHeapHistories;
                    if (HeapIndex < perHeapGenData.Count)
                    {
                        return perHeapGenData[HeapIndex].GenData[(int)gen].Budget;
                    }
                }
                return 0;
            }
        }

        internal bool IsConcurrentGC;
        internal Dictionary<ulong /*objectid*/, long> PinnedObjects;   // list of Pinned objects
        internal int Index;                       // Index into the list of GC events

        /// <summary>
        /// Legacy properties that need to be refactored and removed
        /// </summary>

        internal bool is20Event;


        // For background GC we need to remember when the GC before it ended because
        // when we get the GCStop event some foreground GCs may have happened.
        internal double ProcessCpuAtLastGC;

        internal bool HasAllocTickEvents = false;

        internal void SetHeapCount(int count)
        {
            if (HeapCount == -1)
            {
                HeapCount = count;
            }
        }


        internal void AddServerGCThreadTime(int heapIndex, float cpuMSec)
        {
            if (GCCpuServerGCThreads != null)
            {
                if (heapIndex >= GCCpuServerGCThreads.Length)
                {
                    var old = GCCpuServerGCThreads;
                    GCCpuServerGCThreads = new float[heapIndex + 1];
                    Array.Copy(old, GCCpuServerGCThreads, old.Length);
                }
                GCCpuServerGCThreads[heapIndex] += cpuMSec;
            }
        }

        internal void AddServerGcThreadSwitch(ThreadWorkSpan cswitch)
        {
            if (cswitch.ProcessorNumber >= 0 && cswitch.ProcessorNumber < ServerGcHeapHistories.Count)
            {
                ServerGcHeapHistories[cswitch.ProcessorNumber].AddSwitchEvent(cswitch, PauseStartRelativeMSec);
            }
        }

        internal void AddServerGcSample(ThreadWorkSpan sample)
        {
            if (sample.ProcessorNumber >= 0 && sample.ProcessorNumber < ServerGcHeapHistories.Count)
            {
                ServerGcHeapHistories[sample.ProcessorNumber].AddSampleEvent(sample, PauseStartRelativeMSec);
            }
        }

        internal void AddGcJoin(GCJoinTraceData data)
        {
            if (data.Heap >= 0 && data.Heap < ServerGcHeapHistories.Count)
            {
                ServerGcHeapHistories[data.Heap].AddJoin(data, PauseStartRelativeMSec);
            }
            else
            {
                foreach (var heap in ServerGcHeapHistories)
                {
                    heap.AddJoin(data, PauseStartRelativeMSec);
                }
            }
        }

        internal void BlockingGCEnd()
        {
            ConvertMarkTimes();
            foreach (var serverHeap in ServerGcHeapHistories)
            {
                serverHeap.GCEnd(PauseDurationMSec);
            }
        }

        // We recorded these as the timestamps when we saw the mark events, now convert them 
        // to the actual time that it took for each mark.
        private void ConvertMarkTimes()
        {
            if (fMarkTimesConverted)
            {
                return;
            }

            if (PerHeapMarkTimes != null)
            {
                foreach (KeyValuePair<int, MarkInfo> item in PerHeapMarkTimes)
                {
                    if (item.Value.MarkTimes[(int)MarkRootType.MarkSizedRef] == 0.0)
                    {
                        item.Value.MarkTimes[(int)MarkRootType.MarkSizedRef] = StartRelativeMSec;
                    }

                    if (item.Value.MarkTimes[(int)MarkRootType.MarkOverflow] > StartRelativeMSec)
                    {
                        if (item.Value.MarkTimes[(int)MarkRootType.MarkOlder] == 0.0)
                        {
                            item.Value.MarkTimes[(int)MarkRootType.MarkOverflow] -= item.Value.MarkTimes[(int)MarkRootType.MarkOlder];
                        }
                        else
                        {
                            item.Value.MarkTimes[(int)MarkRootType.MarkOverflow] -= item.Value.MarkTimes[(int)MarkRootType.MarkHandles];
                        }
                    }

                    if (Generation == 2)
                    {
                        item.Value.MarkTimes[(int)MarkRootType.MarkOlder] = 0;
                    }
                    else
                    {
                        item.Value.MarkTimes[(int)MarkRootType.MarkOlder] -= item.Value.MarkTimes[(int)MarkRootType.MarkHandles];
                    }

                    item.Value.MarkTimes[(int)MarkRootType.MarkHandles] -= item.Value.MarkTimes[(int)MarkRootType.MarkFQ];
                    item.Value.MarkTimes[(int)MarkRootType.MarkFQ] -= item.Value.MarkTimes[(int)MarkRootType.MarkStack];
                    item.Value.MarkTimes[(int)MarkRootType.MarkStack] -= item.Value.MarkTimes[(int)MarkRootType.MarkSizedRef];
                    item.Value.MarkTimes[(int)MarkRootType.MarkSizedRef] -= StartRelativeMSec;
                }
            }
            fMarkTimesConverted = true;
        }

        // For true/false groups, return whether that group is set.
        private bool CondemnedReasonGroupSet(CondemnedReasonGroup Group)
        {
            if (PerHeapCondemnedReasons == null)
            {
                return false;
            }

            int HeapIndexHighestGen = 0;
            if (PerHeapCondemnedReasons.Length != 1)
            {
                HeapIndexHighestGen = FindFirstHighestCondemnedHeap();
            }

            return (PerHeapCondemnedReasons[HeapIndexHighestGen].CondemnedReasonGroups[(int)Group] != 0);
        }

        private void AddCondemnedReason(Dictionary<CondemnedReasonGroup, int> ReasonsInfo, CondemnedReasonGroup Reason)
        {
            if (!ReasonsInfo.ContainsKey(Reason))
            {
                ReasonsInfo.Add(Reason, 1);
            }
            else
            {
                (ReasonsInfo[Reason])++;
            }
        }


        internal void AddLOHWaitThreadInfo(int TID, double time, int reason, bool IsStart)
        {
            BGCAllocWaitReason ReasonLOHAlloc = (BGCAllocWaitReason)reason;

            if ((ReasonLOHAlloc == BGCAllocWaitReason.GetLOHSeg) ||
                (ReasonLOHAlloc == BGCAllocWaitReason.AllocDuringSweep) ||
                (ReasonLOHAlloc == BGCAllocWaitReason.AllocDuringBGC))
            {
                if (LOHWaitThreads == null)
                {
                    LOHWaitThreads = new Dictionary<int, BGCAllocWaitInfo>();
                }

                BGCAllocWaitInfo info;

                if (LOHWaitThreads.TryGetValue(TID, out info))
                {
                    if (IsStart)
                    {
                        // If we are finding the value it means we are hitting the small
                        // window where BGC sweep finished and BGC itself finished, discard
                        // this.
                    }
                    else
                    {
                        Debug.Assert(info.Reason == ReasonLOHAlloc);
                        info.WaitStopRelativeMSec = time;
                    }
                }
                else
                {
                    info = new BGCAllocWaitInfo();
                    if (IsStart)
                    {
                        info.Reason = ReasonLOHAlloc;
                        info.WaitStartRelativeMSec = time;
                    }
                    else
                    {
                        // We are currently not displaying this because it's incomplete but I am still adding 
                        // it so we could display if we want to.
                        info.WaitStopRelativeMSec = time;
                    }

                    LOHWaitThreads.Add(TID, info);
                }
            }
        }

        internal class PinnedPlug
        {
            public ulong Start;
            public ulong End;
            public bool PinnedByUser;

            public PinnedPlug(ulong s, ulong e)
            {
                Start = s;
                End = e;
                PinnedByUser = false;
            }
        };

        internal List<PinnedPlug> PinnedPlugs;
        private long pinnedObjectSizes;
        internal long duplicatedPinningReports;

        // The dictionary of heap number and info on time it takes to mark various roots.
        private GCCondemnedReasons[] _PerHeapCondemnedReasons;

        private GCCondemnedReasons _GlobalCondemnedReasons;

        private double _TotalGCTimeMSec = -1;
        // When we are using Server GC we store the CPU spent on each thread
        // so we can see if there's an imbalance. We concurrently don't do this
        // for server background GC as the imbalance there is much less important.

        private float[] GCCpuServerGCThreads = null;

#endregion
    }

    /// <summary>
    /// Condemned reasons are organized into the following groups.
    /// Each group corresponds to one or more reasons. 
    /// Groups are organized in the way that they mean something to users. 
    /// </summary>
    [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
    public enum CondemnedReasonGroup
    {
        // The first 4 will have values of a number which is the generation.
        // Note that right now these 4 have the exact same value as what's in
        // Condemned_Reason_Generation.
        Initial_Generation = 0,
        Final_Generation = 1,
        Alloc_Exceeded = 2,
        Time_Tuning = 3,

        // The following are either true(1) or false(0). They are not 
        // a 1:1 mapping from 
        Induced = 4,
        Low_Ephemeral = 5,
        Expand_Heap = 6,
        Fragmented_Ephemeral = 7,
        Fragmented_Gen1_To_Gen2 = 8,
        Fragmented_Gen2 = 9,
        Fragmented_Gen2_High_Mem = 10,
        GC_Before_OOM = 11,
        Too_Small_For_BGC = 12,
        Ephemeral_Before_BGC = 13,
        Internal_Tuning = 14,
        Almost_Max_Alloc = 15,
        Avoid_Unproductive = 16,
        Pm_Induced_Fullgc_p = 17,
        Pm_Alloc_LOH = 18,
        Gen1_In_Pm = 19,
        Limit_Before_OOM = 20,
        Limit_LOH_Frag = 21,
        Limit_LOH_Reclaim = 22,
        Servo_Initial = 23,
        Servo_NGC = 24,
        Servo_BGC = 25,
        Servo_Postpone = 26,
        Stress_Mix = 27,
        Stress = 28,
        Max = 29
    }

    /// <summary>
    /// Background GC allocation information
    /// </summary>
    [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
    public class BGCAllocWaitInfo
    {
        public double WaitStartRelativeMSec;
        public double WaitStopRelativeMSec;
        public BGCAllocWaitReason Reason;

        public bool GetWaitTime(ref double pauseMSec)
        {
            if ((WaitStartRelativeMSec != 0) &&
                (WaitStopRelativeMSec != 0))
            {
                pauseMSec = WaitStopRelativeMSec - WaitStartRelativeMSec;
                return true;
            }
            return false;
        }

        public bool IsLOHWaitLong(double pauseMSecMin)
        {
            double pauseMSec = 0;
            if (GetWaitTime(ref pauseMSec))
            {
                return (pauseMSec > pauseMSecMin);
            }
            return false;
        }

        public override string ToString()
        {
            if ((Reason == BGCAllocWaitReason.GetLOHSeg) ||
                (Reason == BGCAllocWaitReason.AllocDuringSweep))
            {
                return "Waiting for BGC to thread free lists";
            }
            else
            {
                Debug.Assert(Reason == BGCAllocWaitReason.AllocDuringBGC);
                return "Allocated too much during BGC, waiting for BGC to finish";
            }
        }
    }

    /// <summary>
    /// Span of thread work recorded by CSwitch or CPU Sample Profile events
    /// </summary>
    [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
    public class ThreadWorkSpan
    {
        public int ThreadId;
        public int ProcessId;
        public string ProcessName;
        public int ProcessorNumber;
        public double AbsoluteTimestampMsc;
        public double DurationMsc;
        public int Priority = -1;
        public int WaitReason = -1;

        public ThreadWorkSpan(CSwitchTraceData switchData)
        {
            ProcessName = switchData.NewProcessName;
            ThreadId = switchData.NewThreadID;
            ProcessId = switchData.NewProcessID;
            ProcessorNumber = switchData.ProcessorNumber;
            AbsoluteTimestampMsc = switchData.TimeStampRelativeMSec;
            Priority = switchData.NewThreadPriority;
            WaitReason = (int)switchData.OldThreadWaitReason;
        }

        public ThreadWorkSpan(ThreadWorkSpan span)
        {
            ProcessName = span.ProcessName;
            ThreadId = span.ThreadId;
            ProcessId = span.ProcessId;
            ProcessorNumber = span.ProcessorNumber;
            AbsoluteTimestampMsc = span.AbsoluteTimestampMsc;
            DurationMsc = span.DurationMsc;
            Priority = span.Priority;
            WaitReason = span.WaitReason;
        }

        public ThreadWorkSpan(SampledProfileTraceData sample)
        {
            ProcessName = sample.ProcessName;
            ProcessId = sample.ProcessID;
            ThreadId = sample.ThreadID;
            ProcessorNumber = sample.ProcessorNumber;
            AbsoluteTimestampMsc = sample.TimeStampRelativeMSec;
            DurationMsc = 1;
            Priority = 0;
        }
    }
    /// <summary>
    /// Reason for an induced GC
    /// </summary>
    public enum InducedType
    {
        Blocking = 1,
        NotForced = 2,
    }
    /// <summary>
    /// CondemnedReason
    /// </summary>
    [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
    public struct EncodedCondemnedReasons
    {
        public int Reasons;
        public int ReasonsEx;
    }

    /// <summary>
    /// Heap condemned reason
    /// </summary>
    [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
    public class GCCondemnedReasons
    {
        public EncodedCondemnedReasons EncodedReasons;
        /// <summary>
        /// This records which reasons are used and the value. Since the biggest value
        /// we need to record is the generation number a byte is sufficient.
        /// </summary>
        public byte[] CondemnedReasonGroups;
        public void Decode(int Version)
        {
            // First decode the reasons that return us a generation number. 
            // It's the same in 4.0 and 4.5.
            for (Condemned_Reason_Generation i = 0; i < Condemned_Reason_Generation.Max; i++)
            {
                CondemnedReasonGroups[(int)i] = (byte)GetReasonWithGenNumber(i);
            }

            // Then decode the reasons that just indicate true or false.
            for (Condemned_Reason_Condition i = 0; i < Condemned_Reason_Condition.Max; i++)
            {
                if (GetReasonWithCondition(i, Version))
                {
                    switch (i)
                    {
                        case Condemned_Reason_Condition.Induced_fullgc_p:
                            CondemnedReasonGroups[(int)CondemnedReasonGroup.Induced] = (byte)InducedType.Blocking;
                            break;
                        case Condemned_Reason_Condition.Induced_noforce_p:
                            CondemnedReasonGroups[(int)CondemnedReasonGroup.Induced] = (byte)InducedType.NotForced;
                            break;
                        case Condemned_Reason_Condition.Low_ephemeral_p:
                            CondemnedReasonGroups[(int)CondemnedReasonGroup.Low_Ephemeral] = 1;
                            break;
                        case Condemned_Reason_Condition.Low_card_p:
                            CondemnedReasonGroups[(int)CondemnedReasonGroup.Internal_Tuning] = 1;
                            break;
                        case Condemned_Reason_Condition.Eph_high_frag_p:
                            CondemnedReasonGroups[(int)CondemnedReasonGroup.Fragmented_Ephemeral] = 1;
                            break;
                        case Condemned_Reason_Condition.Max_high_frag_p:
                            CondemnedReasonGroups[(int)CondemnedReasonGroup.Fragmented_Gen2] = 1;
                            break;
                        case Condemned_Reason_Condition.Max_high_frag_e_p:
                            CondemnedReasonGroups[(int)CondemnedReasonGroup.Fragmented_Gen1_To_Gen2] = 1;
                            break;
                        case Condemned_Reason_Condition.Max_high_frag_m_p:
                        case Condemned_Reason_Condition.Max_high_frag_vm_p:
                            CondemnedReasonGroups[(int)CondemnedReasonGroup.Fragmented_Gen2_High_Mem] = 1;
                            break;
                        case Condemned_Reason_Condition.Max_gen1:
                            CondemnedReasonGroups[(int)CondemnedReasonGroup.Alloc_Exceeded] = 2;
                            break;
                        case Condemned_Reason_Condition.Expand_fullgc_p:
                            CondemnedReasonGroups[(int)CondemnedReasonGroup.Expand_Heap] = 1;
                            break;
                        case Condemned_Reason_Condition.Before_oom:
                            CondemnedReasonGroups[(int)CondemnedReasonGroup.GC_Before_OOM] = 1;
                            break;
                        case Condemned_Reason_Condition.Gen2_too_small:
                            CondemnedReasonGroups[(int)CondemnedReasonGroup.Too_Small_For_BGC] = 1;
                            break;
                        case Condemned_Reason_Condition.Before_bgc:
                            CondemnedReasonGroups[(int)CondemnedReasonGroup.Ephemeral_Before_BGC] = 1;
                            break;
                        case Condemned_Reason_Condition.Almost_max_alloc:
                            CondemnedReasonGroups[(int)CondemnedReasonGroup.Almost_Max_Alloc] = 1;
                            break;
                        case Condemned_Reason_Condition.Avoid_unproductive:
                            CondemnedReasonGroups[(int)CondemnedReasonGroup.Avoid_Unproductive] = 1;
                            break;
                        case Condemned_Reason_Condition.Pm_induced_fullgc_p:
                            CondemnedReasonGroups[(int)CondemnedReasonGroup.Pm_Induced_Fullgc_p] = 1;
                            break;
                        case Condemned_Reason_Condition.Pm_alloc_loh:
                            CondemnedReasonGroups[(int)CondemnedReasonGroup.Pm_Alloc_LOH] = 1;
                            break;
                        case Condemned_Reason_Condition.Gen1_in_pm:
                            CondemnedReasonGroups[(int)CondemnedReasonGroup.Gen1_In_Pm] = 1;
                            break;
                        case Condemned_Reason_Condition.Limit_before_oom:
                            CondemnedReasonGroups[(int)CondemnedReasonGroup.Limit_Before_OOM] = 1;
                            break;
                        case Condemned_Reason_Condition.Limit_loh_frag:
                            CondemnedReasonGroups[(int)CondemnedReasonGroup.Limit_LOH_Frag] = 1;
                            break;
                        case Condemned_Reason_Condition.Limit_loh_reclaim:
                            CondemnedReasonGroups[(int)CondemnedReasonGroup.Limit_LOH_Reclaim] = 1;
                            break;
                        case Condemned_Reason_Condition.Servo_initial:
                            CondemnedReasonGroups[(int)CondemnedReasonGroup.Servo_Initial] = 1;
                            break;
                        case Condemned_Reason_Condition.Servo_ngc:
                            CondemnedReasonGroups[(int)CondemnedReasonGroup.Servo_NGC] = 1;
                            break;
                        case Condemned_Reason_Condition.Servo_bgc:
                            CondemnedReasonGroups[(int)CondemnedReasonGroup.Servo_BGC] = 1;
                            break;
                        case Condemned_Reason_Condition.Servo_postpone:
                            CondemnedReasonGroups[(int)CondemnedReasonGroup.Servo_Postpone] = 1;
                            break;
                        case Condemned_Reason_Condition.Stress_mix:
                            CondemnedReasonGroups[(int)CondemnedReasonGroup.Stress_Mix] = 1;
                            break;
                        case Condemned_Reason_Condition.Stress:
                            CondemnedReasonGroups[(int)CondemnedReasonGroup.Stress] = 1;
                            break;
                        default:
                            Debug.Assert(false, "Unexpected reason");
                            break;
                    }
                }
            }
        }

#region private
        // These values right now are the same as the first 4 in CondemnedReasonGroup.
        private enum Condemned_Reason_Generation
        {
            Initial = 0,
            Final_per_heap = 1,
            Alloc_budget = 2,
            Time_tuning = 3,
            Max = 4,
        };

        private enum Condemned_Reason_Condition
        {
            Induced_fullgc_p = 0,
            Expand_fullgc_p = 1,
            High_mem_p = 2,
            Very_high_mem_p = 3,
            Low_ephemeral_p = 4,
            Low_card_p = 5,
            Eph_high_frag_p = 6,
            Max_high_frag_p = 7,
            Max_high_frag_e_p = 8,
            Max_high_frag_m_p = 9,
            Max_high_frag_vm_p = 10,
            Max_gen1 = 11,
            Before_oom = 12,
            Gen2_too_small = 13,
            Induced_noforce_p = 14,
            Before_bgc = 15,
            Almost_max_alloc = 16,
            Avoid_unproductive = 17,
            Pm_induced_fullgc_p = 18,
            Pm_alloc_loh = 19,
            Gen1_in_pm = 20,
            Limit_before_oom = 21,
            Limit_loh_frag = 22,
            Limit_loh_reclaim = 23,
            Servo_initial = 24,
            Servo_ngc = 25,
            Servo_bgc = 26,
            Servo_postpone = 27,
            Stress_mix = 28,
            Stress = 29,
            Max = 30
        };

        private int GetReasonWithGenNumber(Condemned_Reason_Generation Reason_GenNumber)
        {
            int GenNumber = ((EncodedReasons.Reasons >> ((int)Reason_GenNumber * 2)) & 0x3);
            return GenNumber;
        }

        private bool GetReasonWithCondition(Condemned_Reason_Condition Reason_Condition, int Version)
        {
            bool ConditionIsSet = false;
            if (Version == 0)
            {
                Debug.Assert((int)Reason_Condition < 16);
                ConditionIsSet = ((EncodedReasons.Reasons & (1 << (int)(Reason_Condition + 16))) != 0);
            }
            else if (Version >= 2)
            {
                ConditionIsSet = ((EncodedReasons.ReasonsEx & (1 << (int)Reason_Condition)) != 0);
            }
            else
            {
                Debug.Assert(false, "GetReasonWithCondition invalid version : " + Version);
            }

            return ConditionIsSet;
        }

#endregion
    }

    /// <summary>
    /// Container for mark times
    /// </summary>
    public class MarkInfo
    {
        // Note that in 4.5 and prior (ie, from GCMark events, not GCMarkWithType), the first stage of the time 
        // includes scanning sizedref handles(which can be very significant). We could distinguish that by interpreting 
        // the Join events which I haven't done yet.
        public double[] MarkTimes;
        public long[] MarkPromoted;

        public MarkInfo(bool initPromoted = true)
        {
            MarkTimes = new double[(int)MarkRootType.MarkMax];
            if (initPromoted)
            {
                MarkPromoted = new long[(int)MarkRootType.MarkMax];
            }
        }
    };

    /// <summary>
    /// Per heap statistics
    /// </summary>
    public class GCPerHeapHistory
    {
        public int MemoryPressure;
        public bool HasMemoryPressure;
        public bool VersionRecognized;
        public long FreeListAllocated;
        public bool HasFreeListAllocated;
        public long FreeListRejected;
        public bool HasFreeListRejected;
        public int CondemnReasons0;
        public int CondemnReasons1;
        public bool HasCondemnReasons1;
        public int CompactMechanisms;
        public int ExpandMechanisms;
        public long ExtraGen0Commit;
        public bool HasExtraGen0Commit;
        public int Version;
        public GCPerHeapHistoryGenData[] GenData;
    }

    /// <summary>
    /// Process heap statistics
    /// </summary>
    public class GCGlobalHeapHistory
    {
        public long FinalYoungestDesired;
        public int NumHeaps;
        public int CondemnedGeneration;
        public int Gen0ReductionCount;
        public GCReason Reason;
        public GCGlobalMechanisms GlobalMechanisms;
        public GCPauseMode PauseMode;
        public int MemoryPressure;
        public bool HasMemoryPressure;
        public int CondemnReasons0;
        public bool HasCondemnReasons0;
        public int CondemnReasons1;
        public bool HasCondemnReasons1;
    }

    /// <summary>
    /// Per heap stastics
    /// </summary>
    public class GCHeapStats
    {
        public long TotalHeapSize;
        public long TotalPromoted;
        public int Depth;
        public long GenerationSize0;
        public long TotalPromotedSize0;
        public long GenerationSize1;
        public long TotalPromotedSize1;
        public long GenerationSize2;
        public long TotalPromotedSize2;
        public long GenerationSize3;
        public long TotalPromotedSize3;
        public long GenerationSize4;
        public long TotalPromotedSize4;
        public long FinalizationPromotedSize;
        public long FinalizationPromotedCount;
        public int PinnedObjectCount;
        public int SinkBlockCount;
        public int GCHandleCount;
    }

    /// <summary>
    /// Approximations we do in this function for V4_5 and prior:
    /// On 4.0 we didn't separate free list from free obj, so we just use fragmentation (which is the sum)
    /// as an approximation. This makes the efficiency value a bit larger than it actually is.
    /// We don't actually update in for the older gen - this means we only know the out for the younger 
    /// gen which isn't necessarily all allocated into the older gen. So we could see cases where the 
    /// out is > 0, yet the older gen's free list doesn't change. Using the younger gen's out as an 
    /// approximation makes the efficiency value larger than it actually is.
    ///
    /// For V4_6 this requires no approximation.
    ///
    /// 
    /// </summary>
    [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
    public class FreeListEfficiency
    {
        public bool Valid = false;
        public double Allocated;
        public double FreeListConsumed;
    }

    [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
    public class GcJoin
    {
        public int Heap;
        public double RelativeTimestampMsc;
        public double AbsoluteTimestampMsc;
        public GcJoinType Type;
        public GcJoinTime Time;
        public int JoinID;
    }

    // Server history per heap. This is for CSwitch/CPU sample/Join events.
    // Each server GC thread goes through this flow during each GC
    // 1) runs server GC code
    // 2) joins with other GC threads
    // 3) restarts
    // 4) goes back to 1).
    // We call 1 through 3 an activity. There are as many activities as there are joins.
    [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
    public class ServerGcHistory
    {
        public int HeapId;
        public int ProcessId;
        public int GcWorkingThreadId;
        public int GcWorkingThreadPriority;
        public List<GcWorkSpan> SwitchSpans = new List<GcWorkSpan>();
        public List<GcWorkSpan> SampleSpans = new List<GcWorkSpan>();
        public List<GcJoin> GcJoins = new List<GcJoin>();

#region private 
        //list of times in msc starting from GC start when GCJoin events were fired for this heap

        internal void AddSampleEvent(ThreadWorkSpan sample, double pauseStartRelativeMSec)
        {
            GcWorkSpan lastSpan = SampleSpans.Count > 0 ? SampleSpans[SampleSpans.Count - 1] : null;
            if (lastSpan != null && lastSpan.ThreadId == sample.ThreadId && lastSpan.ProcessId == sample.ProcessId &&
                ((ulong)sample.AbsoluteTimestampMsc == (ulong)(lastSpan.AbsoluteTimestampMsc + lastSpan.DurationMsc)))
            {
                lastSpan.DurationMsc++;
            }
            else
            {
                SampleSpans.Add(new GcWorkSpan(sample)
                {
                    Type = GetSpanType(sample),
                    RelativeTimestampMsc = sample.AbsoluteTimestampMsc - pauseStartRelativeMSec,
                    DurationMsc = 1
                });
            }
        }

        internal void AddSwitchEvent(ThreadWorkSpan switchData, double pauseStartRelativeMSec)
        {
            GcWorkSpan lastSpan = SwitchSpans.Count > 0 ? SwitchSpans[SwitchSpans.Count - 1] : null;
            if (switchData.ThreadId == GcWorkingThreadId && switchData.ProcessId == ProcessId)
            {
                //update gc thread priority since we have new data
                GcWorkingThreadPriority = switchData.Priority;
            }

            if (lastSpan != null)
            {
                //updating duration of the last one, based on a timestamp from the new one
                lastSpan.DurationMsc = switchData.AbsoluteTimestampMsc - lastSpan.AbsoluteTimestampMsc;

                //updating wait readon of the last one
                lastSpan.WaitReason = switchData.WaitReason;
            }

            SwitchSpans.Add(new GcWorkSpan(switchData)
            {
                Type = GetSpanType(switchData),
                RelativeTimestampMsc = switchData.AbsoluteTimestampMsc - pauseStartRelativeMSec,
                Priority = switchData.Priority
            });
        }

        internal void GCEnd(double pauseDurationMSec)
        {
            GcWorkSpan lastSpan = SwitchSpans.Count > 0 ? SwitchSpans[SwitchSpans.Count - 1] : null;
            if (lastSpan != null)
            {
                lastSpan.DurationMsc = pauseDurationMSec - lastSpan.RelativeTimestampMsc;
            }
        }

        private WorkSpanType GetSpanType(ThreadWorkSpan span)
        {
            if (span.ThreadId == GcWorkingThreadId && span.ProcessId == ProcessId)
            {
                return WorkSpanType.GcThread;
            }

            if (span.ProcessId == 0)
            {
                return WorkSpanType.Idle;
            }

            if (span.Priority >= GcWorkingThreadPriority || span.Priority == -1)
            {
                return WorkSpanType.RivalThread;
            }
            else
            {
                return WorkSpanType.LowPriThread;
            }
        }

        // A note about the join events - the restart events have no heap number associated so 
        // we add them to every heap with the ProcessorNumber so we know which heap/processor it was 
        // fired on.
        // Also for these restart events, the id field is always -1.
        internal void AddJoin(GCJoinTraceData data, double pauseStartRelativeMSec)
        {
            GcJoins.Add(new GcJoin
            {
                Heap = data.ProcessorNumber,
                AbsoluteTimestampMsc = data.TimeStampRelativeMSec,
                RelativeTimestampMsc = data.TimeStampRelativeMSec - pauseStartRelativeMSec,
                Type = data.JoinType,
                Time = data.JoinTime,
                JoinID = data.GCID,
            });
        }
#endregion
    }

    [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
    public enum WorkSpanType
    {
        GcThread,
        RivalThread,
        LowPriThread,
        Idle
    }

    [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
    public class GcWorkSpan : ThreadWorkSpan
    {
        public WorkSpanType Type;
        public double RelativeTimestampMsc;

        public GcWorkSpan(ThreadWorkSpan span)
            : base(span)
        {
        }
    }
}

// Aggregate analysis.  

namespace Microsoft.Diagnostics.Tracing.Analysis.JIT
{

    /// <summary>
    /// Process statistics about JIT'd code
    /// </summary>
    public class JITStats
    {
        /// <summary>
        /// Number of JITT'd methods
        /// </summary>
        public long Count;
        /// <summary>
        /// Total cpu samples for this process
        /// </summary>
        public double TotalCpuTimeMSec;
        /// <summary>
        /// Number of methods JITT'd by foreground threads just prior to execution
        /// </summary>
        public long CountForeground;
        /// <summary>
        /// Total time spent compiling methods on foreground threads
        /// </summary>
        public double TotalForegroundCpuTimeMSec;
        /// <summary>
        /// Number of methods JITT'd by the multicore JIT background threads
        /// </summary>
        public long CountBackgroundMultiCoreJit;
        /// <summary>
        /// Total time spent compiling methods on background threads for multicore JIT
        /// </summary>
        public double TotalBackgroundMultiCoreJitCpuTimeMSec;
        /// <summary>
        /// Number of methods JITT'd by the tiered compilation background threads
        /// </summary>
        public long CountBackgroundTieredCompilation;
        /// <summary>
        /// Total time spent compiling methods on background threads for tiered compilation
        /// </summary>
        public double TotalBackgroundTieredCompilationCpuTimeMSec;
        /// <summary>
        /// Total IL size for all JITT'd methods
        /// </summary>
        public long TotalILSize;
        /// <summary>
        /// Total native code size for all JITT'd methods
        /// </summary>
        public long TotalNativeSize;
        /// <summary>
        /// Total hot code size allocated for all JITT'd methods
        /// </summary>
        public long TotalHotCodeAllocSize;
        /// <summary>
        /// Total read-only data size allocated for all JITT'd methods
        /// </summary>
        public long TotalRODataAllocSize;
        /// <summary>
        /// Total size allocated for all JITT'd methods
        /// </summary>
        public long TotalAllocSizeForJitCode;
        /// <summary>
        /// If data from alloc size for JIT event present
        /// </summary>
        public bool IsJitAllocSizePresent = false;
        /// <summary>
        /// Indication if this is running on .NET 4.x+
        /// </summary>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public bool IsClr4;
        /// <summary>
        /// Indicates if this process has sufficient JIT activity to be interesting
        /// </summary>
        public bool Interesting { get { return Count > 0 || IsClr4; } }

        /// <summary>
        /// Background JIT: Time Jit was aborted (ms)
        /// </summary>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public double BackgroundJitAbortedAtMSec;
        /// <summary>
        /// Background JIT: Assembly name of last assembly loaded before JIT aborted
        /// </summary>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public string LastAssemblyLoadNameBeforeAbort;
        /// <summary>
        /// Background JIT: Relative start time of last assembly loaded before JIT aborted
        /// </summary>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public double LastAssemblyLoadBeforeAbortMSec;
        /// <summary>
        /// Background JIT: Indication if the last assembly load was successful before JIT aborted
        /// </summary>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public bool LastAssemblyLoadBeforeAbortSuccessful;
        /// <summary>
        /// Background JIT: Thread id of the background JIT
        /// </summary>
        public int BackgroundJitThread;
        /// <summary>
        /// Background JIT: Indication that background JIT events are enabled
        /// </summary>
        public bool BackgroundJITEventsOn;

        /// <summary>
        /// Indicates whether any of the jitted method code versions in this process have a known optimization tier
        /// </summary>
        public bool HasAtLeastOneKnownOptimizationTier;

        /// <summary>
        /// List of successfully inlinded methods
        /// </summary>
        public List<InliningSuccessResult> InliningSuccesses = new List<InliningSuccessResult>();

        /// <summary>
        /// List of failed inlined methods
        /// </summary>
        public List<InliningFailureResult> InliningFailures = new List<InliningFailureResult>();

        /// <summary>
        /// Modules encountered while processing managed samples
        /// </summary>
        public HashSet<string> RecordedModules = new HashSet<string>();
        /// <summary>
        /// List of modules whose symbols were not successfully loaded
        /// </summary>
        public HashSet<string> SymbolsMissing = new HashSet<string>();

        /// <summary>
        /// Aggregate a method to be included in the statistics
        /// </summary>
        /// <param name="method"></param>
        public void AddMethodToStatistics(TraceJittedMethod method)
        {
            Count++;
            TotalCpuTimeMSec += method.CompileCpuTimeMSec;
            TotalILSize += method.ILSize;
            TotalNativeSize += method.NativeSize;
            TotalHotCodeAllocSize += method.JitHotCodeRequestSize;
            TotalRODataAllocSize += method.JitRODataRequestSize;
            IsJitAllocSizePresent |= method.IsJitAllocSizePresent;
            TotalAllocSizeForJitCode += method.AllocatedSizeForJitCode;
            if (method.CompilationThreadKind == CompilationThreadKind.MulticoreJitBackground)
            {
                CountBackgroundMultiCoreJit++;
                TotalBackgroundMultiCoreJitCpuTimeMSec += method.CompileCpuTimeMSec;
            }
            else if (method.CompilationThreadKind == CompilationThreadKind.TieredCompilationBackground)
            {
                CountBackgroundTieredCompilation++;
                TotalBackgroundTieredCompilationCpuTimeMSec += method.CompileCpuTimeMSec;
            }
            else if (method.CompilationThreadKind == CompilationThreadKind.Foreground)
            {
                CountForeground++;
                TotalForegroundCpuTimeMSec += method.CompileCpuTimeMSec;
            }
        }

#region private
        /// <summary>
        /// Legacy
        /// </summary>
        internal static TraceJittedMethod MethodComplete(TraceLoadedDotNetRuntime stats, MethodLoadUnloadTraceDataBase data, string methodName, int rejitID, out bool createdNewMethod)
        {
            TraceJittedMethod _method = stats.JIT.m_stats.FindIncompleteJitEventOnThread(stats, data.ThreadID);
            createdNewMethod = false;
            if (_method == null)
            {
                createdNewMethod = true;

                // We don't have JIT start, do the best we can.  
                _method = stats.JIT.m_stats.LogJitStart(stats, data, methodName, 0, data.ModuleID, data.MethodID);
                if (stats.JIT.m_stats.IsClr4)
                {
                    // Debug.WriteLine("Warning: MethodComplete at {0:n3} process {1} thread {2} without JIT Start, assuming 0 JIT time",
                    //    data.TimeStampRelativeMSec, data.ProcessName, data.ThreadID);
                }
                else if (!stats.JIT.m_stats.warnedUser)
                {
                    // Console.WriteLine("Warning: Process {0} ({1}) is running a V2.0 CLR, no JIT Start events available, so JIT times will all be 0.", stats.ProcessName, stats.ProcessID);
                    stats.JIT.m_stats.warnedUser = true;
                }
            }
            _method.NativeSize = data.MethodSize;
            _method.CompileCpuTimeMSec = data.TimeStampRelativeMSec - _method.StartTimeMSec;
            _method.SetOptimizationTier(data.OptimizationTier, stats);
            _method.VersionID = rejitID;

            if (stats.JIT.Stats().BackgroundJitThread != 0 && _method.ThreadID == stats.JIT.Stats().BackgroundJitThread)
            {
                _method.CompilationThreadKind = CompilationThreadKind.MulticoreJitBackground;
            }
            else
            {
                // This isn't always true, but we don't yet have enough data to distinguish tiered compilation from other causes of versioned compilation (ie profiler ReJIT)
                _method.CompilationThreadKind = _method.IsDefaultVersion ? CompilationThreadKind.Foreground : CompilationThreadKind.TieredCompilationBackground;

            }

            _method.Completed++;
            stats.JIT.m_stats.AddMethodToStatistics(_method);

            return _method;
        }

        /// <summary>
        /// Handles AllocRequest event for JIT
        /// </summary>
        internal static void LogJitMethodAllocation(TraceLoadedDotNetRuntime stats, MethodJitMemoryAllocatedForCodeTraceData data)
        {
            TraceJittedMethod _method = stats.JIT.m_stats.FindIncompleteJitEventOnThread(stats, data.ThreadID);
            if (_method != null)
            {
                // If we already counted alloc size for a method, don't count it again.
                if (_method.JitHotCodeRequestSize == 0)
                {
                    _method.IsJitAllocSizePresent = true;
                    _method.JitHotCodeRequestSize = data.JitHotCodeRequestSize;
                    _method.JitRODataRequestSize = data.JitRODataRequestSize;
                    _method.AllocatedSizeForJitCode = data.AllocatedSizeForJitCode;
                    _method.JitAllocFlag = data.JitAllocFlag;
                }
            }
        }

        /// <summary>
        /// Uniquely represents a method within a process.
        /// Used as a lookup key for data structures.
        /// </summary>
        internal struct MethodKey
        {
            private long _ModuleId;
            private long _MethodId;

            public MethodKey(
                long moduleId,
                long methodId)
            {
                _ModuleId = moduleId;
                _MethodId = methodId;
            }

            public override bool Equals(object obj)
            {
                if (obj is MethodKey)
                {
                    MethodKey otherKey = (MethodKey)obj;
                    return ((_ModuleId == otherKey._ModuleId) && (_MethodId == otherKey._MethodId));
                }

                return false;
            }

            public override int GetHashCode()
            {
                return (int)(_ModuleId ^ _MethodId);
            }
        }

        internal TraceJittedMethod LogJitStart(TraceLoadedDotNetRuntime proc, TraceEvent data, string methodName, int ILSize, long moduleID, long methodID)
        {
            TraceJittedMethod _method = new TraceJittedMethod();
            _method.StartTimeMSec = data.TimeStampRelativeMSec;
            _method.ILSize = ILSize;
            _method.MethodName = methodName;
            _method.ThreadID = data.ThreadID;
            string modname = "";
            _method.ModuleID = moduleID;
            moduleNamesFromID.TryGetValue(moduleID, out modname);
            if (!string.IsNullOrWhiteSpace(modname))
            {
                _method.ModuleILPath = modname;
            }
            else
            {
                _method.ModuleILPath = "";
            }

            proc.JIT.Methods.Add(_method);

            if (BackgroundJitThread == _method.ThreadID)
            {
                MethodKey key = new MethodKey(moduleID, methodID);
                backgroundJitEvents[key] = _method;
            }
            else if (BackgroundJitThread != 0)
            {
                // Get the module name.
                if (moduleNamesFromID.ContainsKey(moduleID))
                {
                    string moduleName = moduleNamesFromID[moduleID];
                    if (!string.IsNullOrEmpty(moduleName))
                    {
                        moduleName = System.IO.Path.GetFileNameWithoutExtension(moduleName);
                    }

                    // Check to see if this module is in the profile.
                    if (!RecordedModules.Contains(moduleName))
                    {
                        // Mark the blocking reason that the module is not in the profile, so we'd never background JIT it.
                        _method.BlockedReason = "Module not recorded";
                    }
                    else
                    {
                        _method.BlockedReason = LastBlockedReason;
                    }
                }
                else
                {
                    _method.BlockedReason = LastBlockedReason;
                }
            }
            else
            {
                _method.BlockedReason = LastBlockedReason;
            }

            return _method;
        }

        internal static string GetMethodName(MethodJittingStartedTraceData data)
        {
            int parenIdx = data.MethodSignature.IndexOf('(');
            if (parenIdx < 0)
            {
                parenIdx = data.MethodSignature.Length;
            }

            return data.MethodNamespace + "." + data.MethodName + data.MethodSignature.Substring(parenIdx);
        }

        internal static string GetMethodName(R2RGetEntryPointTraceData data)
        {
            int parenIdx = data.MethodSignature.IndexOf('(');
            if (parenIdx < 0)
            {
                parenIdx = data.MethodSignature.Length;
            }

            return data.MethodNamespace + "." + data.MethodName + data.MethodSignature.Substring(parenIdx);
        }

        internal static string GetMethodName(MethodLoadUnloadVerboseTraceData data)
        {
            int parenIdx = data.MethodSignature.IndexOf('(');
            if (parenIdx < 0)
            {
                parenIdx = data.MethodSignature.Length;
            }

            return data.MethodNamespace + "." + data.MethodName + data.MethodSignature.Substring(parenIdx);
        }

        private TraceJittedMethod FindIncompleteJitEventOnThread(TraceLoadedDotNetRuntime proc, int threadID)
        {
            for (int i = proc.JIT.Methods.Count - 1; 0 <= i; --i)
            {
                TraceJittedMethod ret = proc.JIT.Methods[i];
                if (ret.ThreadID == threadID)
                {
                    // This is a completed JIT event, not what we are looking for. 
                    if (ret.NativeSize > 0 || ret.CompileCpuTimeMSec > 0)
                    {
                        continue;
                    }

                    return ret;
                }
            }
            return null;
        }

        internal string LastBlockedReason = "BG JIT not enabled";

        internal bool warnedUser;

        internal bool playbackAborted = false;
        internal Dictionary<MethodKey, TraceJittedMethod> backgroundJitEvents = new Dictionary<MethodKey, TraceJittedMethod>();
        internal Dictionary<long, string> moduleNamesFromID = new Dictionary<long, string>();

#endregion
    }

    /// <summary>
    /// JIT inlining successes
    /// </summary>
    public struct InliningSuccessResult
    {
        public string MethodBeingCompiled;
        public string Inliner;
        public string Inlinee;
    }

    /// <summary>
    /// JIT inlining failures
    /// </summary>
    public struct InliningFailureResult
    {
        public string MethodBeingCompiled;
        public string Inliner;
        public string Inlinee;
        public string Reason;
    }

    public enum CompilationThreadKind
    {
        Foreground,
        MulticoreJitBackground,
        TieredCompilationBackground
    }

    /// <summary>
    /// Per method information
    /// </summary>
    public class TraceJittedMethod
    {
        /// <summary>
        /// Time taken to compile the method
        /// </summary>
        public double CompileCpuTimeMSec;
        /// <summary>
        /// IL size of method
        /// </summary>
        public int ILSize;
        /// <summary>
        /// Native code size of method
        /// </summary>
        public int NativeSize;
        /// <summary>
        /// Hot code size allocated for JIT code of method
        /// </summary>
        public long JitHotCodeRequestSize;
        /// <summary>
        /// Read-only data size allocated for JIT code of method
        /// </summary>
        public long JitRODataRequestSize;
        /// <summary>
        /// Total size allocated for JIT code of method
        /// </summary>
        public long AllocatedSizeForJitCode;
        /// <summary>
        /// Jit allocation flag
        /// </summary>
        public int JitAllocFlag;
        /// <summary>
        /// Relative start time of JIT'd method
        /// </summary>
        public double StartTimeMSec;
        /// <summary>
        /// Method name
        /// </summary>
        public string MethodName;
        /// <summary>
        /// Module name
        /// </summary>
        public string ModuleILPath;
        /// <summary>
        /// Thread id where JIT'd
        /// </summary>
        public int ThreadID;
        /// <summary>
        /// Indication of if it was JIT'd in the background
        /// </summary>
        [Obsolete("Use CompilationThreadKind")]
        public bool IsBackground
        {
            get
            {
                return CompilationThreadKind != CompilationThreadKind.Foreground;
            }
        }
        /// <summary>
        /// Indication of if it was JIT'd in the background and why
        /// </summary>
        public CompilationThreadKind CompilationThreadKind;
        /// <summary>
        /// Amount of time the method was forcasted to JIT
        /// </summary>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public double DistanceAhead
        {
            get
            {
                double distanceAhead = 0;
                if (default(double) != ForegroundMethodRequestTimeMSec)
                {
                    distanceAhead = ForegroundMethodRequestTimeMSec - StartTimeMSec;
                }

                return distanceAhead;
            }
        }
        /// <summary>
        /// Indication of if the background JIT request was blocked and why
        /// </summary>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public string BlockedReason
        {
            get
            {
                if (null == _blockedReason)
                {
                    _blockedReason = "None";
                }

                return _blockedReason;
            }
            set
            {
                _blockedReason = value;
            }
        }
        /// <summary>
        /// Number of cpu samples for this method
        /// </summary>
        public double RunCpuTimeMSec;

        /// <summary>
        /// The optimization tier at which the method was jitted
        /// </summary>
        public OptimizationTier OptimizationTier { get; private set; }

        /// <summary>
        /// The version id that is created by the runtime code versioning feature. This is an incrementing counter that starts at 0 for each method.
        /// The ETW events historically name this as the ReJITID event parameter in the payload, but we have now co-opted its usage.
        /// </summary>
        public int VersionID;

        public bool IsDefaultVersion { get { return VersionID == 0; } }

        public bool IsJitAllocSizePresent
        {
            get;
            set;
        }

#region private
        internal void SetOptimizationTier(OptimizationTier optimizationTier, TraceLoadedDotNetRuntime stats)
        {
            if (optimizationTier != OptimizationTier.Unknown)
            {
                OptimizationTier = optimizationTier;
                stats.HasAnyKnownOptimizationTier = true;
            }
        }

        /// <summary>
        /// Legacy
        /// </summary>
        internal double ForegroundMethodRequestTimeMSec;
        internal string _blockedReason;
        internal int Completed = 0;
        internal long ModuleID = 0;
#endregion
    }
}

namespace Microsoft.Diagnostics.Tracing.Analysis.GC
{
    /// <summary>
    /// Statistical garbage collector (GC) information about a managed process
    /// </summary>
    public class GCStats
    {
        /// <summary>
        /// Number of GC's for this process
        /// </summary>
        public int Count;
        /// <summary>
        /// Number of GC's which were induced, eg. GC.Collect, etc.
        /// </summary>
        public int NumInduced;
        /// <summary>
        /// Total size of the pinned objects seen at collection time
        /// </summary>
        public long PinnedObjectSizes;
        /// <summary>
        /// Of all the memory that is current pinned, how much of it is from pinned objects
        /// </summary>
        public int PinnedObjectPercentage;
        /// <summary>
        /// Number of GC's that contained pinned objects
        /// </summary>
        public long NumWithPinEvents;
        /// <summary>
        /// Number of GC's that contained pin plugs
        /// </summary>
        public long NumWithPinPlugEvents;
        /// <summary>
        /// The longest pause duration (ms)
        /// </summary>
        public double MaxPauseDurationMSec;
        /// <summary>
        /// Avarege pause duration (ms)
        /// </summary>
        public double MeanPauseDurationMSec { get { return TotalPauseTimeMSec / Count; } }
        /// <summary>
        /// Average heap size after a GC (mb)
        /// </summary>
        public double MeanSizeAfterMB { get { return TotalSizeAfterMB / Count; } }
        /// <summary>
        /// Average peak heap size (mb)
        /// </summary>
        public double MeanSizePeakMB { get { return TotalSizePeakMB / Count; } }
        /// <summary>
        /// Average exclusive cpu samples (ms) during GC's
        /// </summary>
        public double MeanCpuMSec { get { return TotalCpuMSec / Count; } }
        /// <summary>
        /// Total GC pause time (ms)
        /// </summary>
        public double TotalPauseTimeMSec;
        /// <summary>
        /// Max suspend duration (ms), should be very small
        /// </summary>
        public double MaxSuspendDurationMSec;
        /// <summary>
        /// Max peak heap size (mb)
        /// </summary>
        public double MaxSizePeakMB;
        /// <summary>
        /// Max allocation per second (mb/sec)
        /// </summary>
        public double MaxAllocRateMBSec;
        /// <summary>
        /// Total allocations in the process lifetime (mb)
        /// </summary>
        public double TotalAllocatedMB;
        /// <summary>
        /// Total exclusive cpu samples (ms)
        /// </summary>
        public double TotalCpuMSec;
        /// <summary>
        /// Total memory promoted between generations (mb)
        /// </summary>
        public double TotalPromotedMB;
        /// <summary>
        /// (obsolete) Total size of heaps after GC'ss (mb)
        /// </summary>
        public double TotalSizeAfterMB;
        /// <summary>
        /// (obsolete) Total peak heap sizes (mb)
        /// </summary>
        public double TotalSizePeakMB;
        /// <summary>
        /// Indication if this process is interesting from a GC pov
        /// </summary>
        public bool Interesting { get { return Count > 0; } }
        /// <summary>
        /// List of finalizer objects
        /// </summary>
        public Dictionary<string, long> FinalizedObjects = new Dictionary<string, long>();
        /// <summary>
        /// Percentage of time spent paused as compared to the process lifetime
        /// </summary>
        /// <returns></returns>
        public double GetGCPauseTimePercentage()
        {
            return ((ProcessDuration == 0) ? 0.0 : ((TotalPauseTimeMSec * 100) / ProcessDuration));
        }
        /// <summary>
        /// Running time of the process.  Measured as time spent between first and last GC event observed
        /// </summary>
        public double ProcessDuration;
        /// <summary>
        /// Means it detected that the ETW information is in a format it does not understand.
        /// </summary>
        [Obsolete("This is experimental, you should not use it yet for non-experimental purposes.")]
        public bool GCVersionInfoMismatch { get; private set; }
        /// <summary>
        /// Indicator of if ServerGC is enabled (1).  -1 indicates that not enough events have been processed to know for sure.
        /// We don't necessarily have the GCSettings event (only fired at the beginning if we attach)
        /// So we have to detect whether we are running server GC or not.
        /// Till we get our first GlobalHeapHistory event which indicates whether we use server GC 
        /// or not this remains -1.
        /// </summary>
        public int IsServerGCUsed = -1;
        /// <summary>
        /// Number of heaps.  -1 indicates that not enough events have been processed to know for sure.
        /// </summary>
        public int HeapCount = -1;
        /// <summary>
        /// Indicator if PerHeapHistories is present
        /// </summary>
        public bool HasDetailedGCInfo;

#region private

        // This is the last GC in progress. We need this for server Background GC.
        // See comments for lastCompletedGC.
        private static TraceGC GetLastGC(TraceLoadedDotNetRuntime proc)
        {
            TraceGC _event = TraceGarbageCollector.GetCurrentGC(proc);
            if ((proc.GC.m_stats.IsServerGCUsed == 1) &&
                (_event == null))
            {
                if (proc.GC.m_stats.lastCompletedGC != null)
                {
                    Debug.Assert(proc.GC.m_stats.lastCompletedGC.Type == GCType.BackgroundGC);
                    _event = proc.GC.m_stats.lastCompletedGC;
                }
            }

            return _event;
        }

        //
        // candidate to be made private/ex
        //
        // The amount of memory allocated by the user threads. So they are divided up into gen0 and LOH allocations.
        internal double[] allocTickCurrentMB = { 0.0, 0.0 };
        internal double[] allocTickAtLastGC = { 0.0, 0.0 };
        internal bool HasAllocTickEvents = false;
        internal bool SeenBadAllocTick = false;

        internal double lastRestartEndTimeRelativeMSec;

        // This is the BGC that's in progress as we are parsing. We need to remember this 
        // so we can correctly attribute the suspension time.
        internal TraceGC currentBGC = null;

        internal static TraceGC GetLastBGC(TraceLoadedDotNetRuntime proc)
        {
            if (proc.GC.m_stats.currentBGC != null)
            {
                return proc.GC.m_stats.currentBGC;
            }

            if ((proc.GC.m_stats.lastCompletedGC != null) && (proc.GC.m_stats.lastCompletedGC.Type == GCType.BackgroundGC))
            {
                return proc.GC.m_stats.lastCompletedGC;
            }

            // Otherwise we search till we find the last BGC if we have seen one.
            for (int i = (proc.GC.GCs.Count - 1); i >= 0; i--)
            {
                if (proc.GC.GCs[i].Type == GCType.BackgroundGC)
                {
                    return proc.GC.GCs[i];
                }
            }

            return null;
        }

        internal void AddConcurrentPauseTime(TraceGC _event, double RestartEEMSec)
        {
            if (suspendThreadIDBGC > 0)
            {
                double pause = RestartEEMSec - suspendTimeRelativeMSec;
                _event.PauseDurationMSec += pause;
                if (lastSuspendReason == GCSuspendEEReason.SuspendForGCPrep)
                {
                    _event.BGCFinalPauseMSec = pause;
                }
            }
            else
            {
                Debug.Assert(_event.PauseDurationMSec == 0);
                _event.PauseDurationMSec = RestartEEMSec - _event.PauseStartRelativeMSec;
            }
        }

        internal void AddServerGCThreadFromMark(int ThreadID, int HeapNum)
        {
            if (IsServerGCUsed == 1)
            {
                Debug.Assert(HeapCount > 1);

                if (serverGCThreads.Count < HeapCount)
                {
                    // I am seeing that sometimes we are not getting these events from all heaps
                    // for a complete GC so I have to check for that.
                    if (!serverGCThreads.ContainsKey(ThreadID))
                    {
                        serverGCThreads.Add(ThreadID, HeapNum);
                    }
                }
            }
        }

        internal static void ProcessGlobalHistory(TraceLoadedDotNetRuntime proc, GCGlobalHeapHistoryTraceData data)
        {
            if (proc.GC.m_stats.IsServerGCUsed == -1)
            {
                // We detected whether we are using Server GC now.
                proc.GC.m_stats.IsServerGCUsed = ((data.NumHeaps > 1) ? 1 : 0);
                if (proc.GC.m_stats.HeapCount == -1)
                {
                    proc.GC.m_stats.HeapCount = data.NumHeaps;
                }

                if (proc.GC.m_stats.IsServerGCUsed == 1)
                {
                    proc.GC.m_stats.serverGCThreads = new Dictionary<int, int>(data.NumHeaps);
                }
            }

            TraceGC _event = GetLastGC(proc);
            if (_event != null)
            {
                _event.GlobalHeapHistory = new GCGlobalHeapHistory()
                {
                    FinalYoungestDesired = data.FinalYoungestDesired,
                    CondemnedGeneration = data.CondemnedGeneration,
                    Gen0ReductionCount = data.Gen0ReductionCount,
                    GlobalMechanisms = data.GlobalMechanisms,
                    PauseMode = data.PauseMode,
                    HasMemoryPressure = data.HasMemoryPressure,
                    MemoryPressure = (data.HasMemoryPressure) ? data.MemoryPressure : -1,
                    NumHeaps = data.NumHeaps,
                    Reason = data.Reason,
                    CondemnReasons0 = (data.HasCondemnReasons0) ? data.CondemnReasons0 : -1,
                    CondemnReasons1 = (data.HasCondemnReasons1) ? data.CondemnReasons1 : -1,
                    HasCondemnReasons0 = data.HasCondemnReasons0,
                    HasCondemnReasons1 = data.HasCondemnReasons1,
                };
                _event.SetHeapCount(proc.GC.m_stats.HeapCount);
            }
        }

        internal static void ProcessPerHeapHistory(TraceLoadedDotNetRuntime proc, GCPerHeapHistoryTraceData data)
        {
            if (!data.VersionRecognized)
            {
                proc.GC.m_stats.GCVersionInfoMismatch = true;
                return;
            }

            TraceGC _event = GetLastGC(proc);
            if (_event != null)
            {
                int numGenerations = data.NumGenerations;
                var hist = new GCPerHeapHistory()
                {
                    FreeListAllocated = (data.HasFreeListAllocated) ? data.FreeListAllocated : -1,
                    HasFreeListAllocated = data.HasFreeListAllocated,
                    FreeListRejected = (data.HasFreeListRejected) ? data.FreeListRejected : -1,
                    HasFreeListRejected = data.HasFreeListRejected,
                    MemoryPressure = (data.HasMemoryPressure) ? data.MemoryPressure : -1,
                    HasMemoryPressure = data.HasMemoryPressure,
                    VersionRecognized = data.VersionRecognized,
                    GenData = new GCPerHeapHistoryGenData[numGenerations],
                    CondemnReasons0 = data.CondemnReasons0,
                    CondemnReasons1 = (data.HasCondemnReasons1) ? data.CondemnReasons1 : -1,
                    HasCondemnReasons1 = data.HasCondemnReasons1,
                    CompactMechanisms = (int)data.CompactMechanisms,
                    ExpandMechanisms = (int)data.ExpandMechanisms,
                    ExtraGen0Commit = data.ExtraGen0Commit,
                    HasExtraGen0Commit = data.HasExtraGen0Commit,
                    Version = data.Version
                };

                for (Gens GenIndex = Gens.Gen0; GenIndex < (Gens)numGenerations; GenIndex++)
                {
                    hist.GenData[(int)GenIndex] = data.GenData(GenIndex);
                }

                _event.PerHeapHistories.Add(hist);
            }
        }


        internal Dictionary<int, int> ThreadId2Priority = new Dictionary<int, int>();
        internal Dictionary<int, int> ServerGcHeap2ThreadId = new Dictionary<int, int>();


        // EE can be suspended via different reasons. The only ones we care about are
        // SuspendForGC(1) - suspending for GC start
        // SuspendForGCPrep(6) - BGC uses it in the middle of a BGC.
        // We need to filter out the rest of the suspend/resume events.
        // Keep track of the last time we started suspending the EE.  Will use in 'Start' to set PauseStartRelativeMSec
        internal int suspendThreadIDOther = -1;
        internal int suspendThreadIDBGC = -1;
        // This is either the user thread (in workstation case) or a server GC thread that called SuspendEE to do a GC
        internal int suspendThreadIDGC = -1;
        internal double suspendTimeRelativeMSec = -1;
        internal double suspendEndTimeRelativeMSec = -1;
        internal GCSuspendEEReason lastSuspendReason;

        // This records the amount of CPU time spent at the end of last GC.
        internal double ProcessCpuAtLastGC = 0;


        internal Dictionary<int, object> backgroundGCThreads = new Dictionary<int, object>();
        internal bool IsBGCThread(int threadID)
        {
            if (backgroundGCThreads != null)
            {
                return backgroundGCThreads.ContainsKey(threadID);
            }

            return false;
        }

        // I keep this for the purpose of server Background GC. Unfortunately for server background 
        // GC we are firing the GCEnd/GCHeaps events and Global/Perheap events in the reversed order.
        // This is so that the Global/Perheap events can still be attributed to the right BGC.
        internal TraceGC lastCompletedGC = null;


        internal bool gotThreadInfo = false;
        // This is the server GC threads. It's built up in the 2nd server GC we see. 
        internal Dictionary<int, int> serverGCThreads = new Dictionary<int, int>();


        internal int IsServerGCThread(int threadID)
        {
            int heapIndex;
            if (serverGCThreads != null)
            {
                if (serverGCThreads.TryGetValue(threadID, out heapIndex))
                {
                    return heapIndex;
                }
            }
            return -1;
        }

        internal void SetUpServerGcHistory(int id, TraceGC gc)
        {
            for (int i = 0; i < HeapCount; i++)
            {
                int gcThreadId = 0;
                int gcThreadPriority = 0;
                ServerGcHeap2ThreadId.TryGetValue(i, out gcThreadId);
                ThreadId2Priority.TryGetValue(gcThreadId, out gcThreadPriority);
                gc.ServerGcHeapHistories.Add(new ServerGcHistory
                {
                    ProcessId = id,
                    HeapId = i,
                    GcWorkingThreadId = gcThreadId,
                    GcWorkingThreadPriority = gcThreadPriority
                });
            }
        }

#endregion
    }
}
