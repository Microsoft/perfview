//     Copyright (c) Microsoft Corporation.  All rights reserved.
// This file is best viewed using outline mode (Ctrl-M Ctrl-O)
//
// This program uses code hyperlinks available as part of the HyperAddin Visual Studio plug-in.
// It is available from http://www.codeplex.com/hyperAddin 
// 
using Microsoft.Diagnostics.Tracing.Compatibility;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Utilities;
using Address = System.UInt64;

// code:System.Diagnostics.ETWTraceEventSource definition.
namespace Microsoft.Diagnostics.Tracing
{
    /// <summary>
    /// A ETWTraceEventSource represents the stream of events that was collected from a
    /// TraceEventSession (eg the ETL moduleFile, or the live session event stream). Like all
    /// TraceEventSource, it logically represents a stream of TraceEvent s. Like all
    /// TraceEventDispathers it supports a callback model where Parsers attach themselves to this
    /// sources, and user callbacks defined on the parsers are called when the 'Process' method is called.
    /// 
    /// * See also TraceEventDispatcher
    /// * See also TraceEvent
    /// * See also #ETWTraceEventSourceInternals
    /// * See also #ETWTraceEventSourceFields
    /// </summary>    
    public sealed unsafe class ETWTraceEventSource : TraceEventDispatcher, IDisposable
    {
        /// <summary>
        /// Open a ETW event trace moduleFile (ETL moduleFile) for processing.  
        /// </summary>
        /// <param name="fileName">The ETL data moduleFile to open</param>` 
        public ETWTraceEventSource(string fileName)
            : this(fileName, TraceEventSourceType.MergeAll)
        {
        }

        /// <summary>
        /// Open a ETW event source for processing.  This can either be a moduleFile or a real time ETW session
        /// </summary>
        /// <param name="fileOrSessionName">
        /// If type == ModuleFile this is the name of the moduleFile to open.
        /// If type == Session this is the name of real time session to open.</param>
        /// <param name="type"></param>
        // [SecuritySafeCritical]
        public ETWTraceEventSource(string fileOrSessionName, TraceEventSourceType type)
        {
            Initialize(fileOrSessionName, type);
        }

        /// <summary>
        /// Open multiple etl files as one trace for processing.
        /// </summary>
        /// <param name="fileNames"></param>
        /// <param name="type">If type == MergeAll, call Initialize.</param>
        // [SecuritySafeCritical]
        public ETWTraceEventSource(IEnumerable<string> fileNames, TraceEventSourceType type)
        {
            if (type == TraceEventSourceType.MergeAll)
            {
                Initialize(fileNames);
            }
            else
            {
                this.fileNames = fileNames;
            }
        }

        /// <summary>
        /// Process all the files in 'fileNames' in order (that is all the events in the first
        /// file are processed, then the second ...).   Intended for parsing the 'Multi-File' collection mode. 
        /// </summary>
        /// <param name="fileNames">The list of files path names to process (in that order)</param>
        public ETWTraceEventSource(IEnumerable<string> fileNames)
        {
            this.fileNames = fileNames;
        }

        // Process is called after all desired subscriptions have been registered.  
        /// <summary>
        /// Processes all the events in the data source, issuing callbacks that were subscribed to.  See
        /// #Introduction for more
        /// </summary>
        /// <returns>false If StopProcesing was called</returns>
        // [SecuritySafeCritical]
        public override bool Process()
        {
            stopProcessing = false;
            if (processTraceCalled)
            {
                Reset();
            }

            processTraceCalled = true;

            if (fileNames != null)
            {
                foreach (var fileName in fileNames)
                {
                    if (handles != null)
                    {
                        Debug.Assert(handles.Length == 1);
                        if (handles[0] != TraceEventNativeMethods.INVALID_HANDLE_VALUE)
                        {
                            TraceEventNativeMethods.CloseTrace(handles[0]);
                        }
                    }

                    Initialize(fileName, TraceEventSourceType.FileOnly);
                    if (!ProcessOneFile())
                    {
                        OnCompleted();
                        Debug.Assert(sessionEndTimeQPC != long.MaxValue);       // Not a real time session
                        return false;
                    }
                }
                OnCompleted();
                Debug.Assert(sessionEndTimeQPC != long.MaxValue);       // Not a real time session
                return true;
            }
            else
            {
                var ret = ProcessOneFile();

                // If the session is real time, set he sessionEndTime (since the session is stopping).  
                if (sessionEndTimeQPC == long.MaxValue)
                {
                    sessionEndTimeQPC = QPCTime.GetUTCTimeAsQPC(DateTime.UtcNow);
                }

                OnCompleted();
                return ret;
            }
        }

        /// <summary>
        /// Reprocess a pre-constructed event which this processor has presumably created. Helpful to re-examine
        /// "unknown" events, perhaps after a manifest has been received from the ETW stream.
        /// Note when queuing events to reprocess you must <see cref="TraceEvent.Clone">Clone</see> them first
        /// or certain internal data may no longer be available and you may receive memory access violations.
        /// </summary>
        /// <param name="ev">Event to re-process.</param>
        [Obsolete("Not obsolete but experimental.   We may change this in the future.")]
        public void ReprocessEvent(TraceEvent ev)
        {
            RawDispatch(ev.eventRecord);
        }

        /// <summary> 
        /// The log moduleFile that is being processed (if present)
        /// TODO: what does this do for Real time sessions?
        /// </summary>
        public string LogFileName { get { return logFiles[0].LogFileName; } }
        /// <summary>
        /// The name of the session that generated the data. 
        /// </summary>
        public string SessionName { get { return logFiles[0].LoggerName; } }
        /// <summary>
        /// The size of the log, will return 0 if it does not know. 
        /// </summary>
        public override long Size
        {
            get
            {
                long ret = 0;
                for (int i = 0; i < logFiles.Length; i++)
                {
                    var fileName = logFiles[i].LogFileName;
                    if (File.Exists(fileName))
                    {
                        ret += new FileInfo(fileName).Length;
                    }
                }
                return ret;
            }
        }
        /// <summary>
        /// returns the number of events that have been lost in this session.    Note that this value is NOT updated
        /// for real time sessions (it is a snapshot).  Instead you need to use the TraceEventSession.EventsLost property. 
        /// </summary>
        public override int EventsLost
        {
            get
            {
                int ret = 0;
                for (int i = 0; i < logFiles.Length; i++)
                {
                    ret += (int)logFiles[i].LogfileHeader.EventsLost;
                }

                return ret;
            }
        }
        /// <summary>
        /// Returns true if the Process can be called multiple times (if the Data source is from a
        /// moduleFile, not a real time stream.
        /// </summary>
        public bool CanReset { get { return (logFiles[0].LogFileMode & TraceEventNativeMethods.EVENT_TRACE_REAL_TIME_MODE) == 0; } }

        /// <summary>
        /// This routine is only useful/valid for real-time sessions.  
        /// 
        /// TraceEvent.TimeStamp internally is stored using a high resolution clock called the Query Performance Counter (QPC).
        /// This clock is INDEPENDENT of the system clock used by DateTime.   These two clocks are synchronized to within 2 msec at 
        /// session startup but they can drift from there (typically 2msec / min == 3 seconds / day).   Thus if you have long
        /// running real time session it becomes problematic to compare the timestamps with those in another session or something
        /// timestamped with the system clock.   SynchronizeClock will synchronize the TraceEvent.Timestamp clock with the system
        /// clock again.   If you do this right before you start another session, then the two sessions will be within 2 msec of
        /// each other, and their timestamps will correlate.     Doing it periodically (e.g. hourly), will keep things reasonably close.  
        /// 
        /// TODO: we can achieve perfect synchronization by exposing the QPC tick sync point so we could read the sync point 
        /// from one session and set that exact sync point for another session.  
        /// </summary>
        public void SynchronizeClock()
        {
            if (!IsRealTime)
            {
                throw new InvalidOperationException("SynchronizeClock is only for Real-Time Sessions");
            }

            DateTime utcNow = DateTime.UtcNow;
            _syncTimeQPC = QPCTime.GetUTCTimeAsQPC(utcNow);
            _syncTimeUTC = utcNow;
        }

        /// <summary>
        /// Options that can be passed to GetModulesNeedingSymbols
        /// </summary>
        [Flags]
        public enum ModuleSymbolOptions
        {
            /// <summary>
            /// This is the default, where only NGEN images are included (since these are the only images whose PDBS typically
            /// need to be resolved aggressively AT COLLECTION TIME)
            /// </summary>
            OnlyNGENImages = 0,
            /// <summary>
            /// If set, this option indicates that non-NGEN images should also be included in the list of returned modules
            /// </summary>
            IncludeUnmanagedModules = 1,
            /// <summary>
            /// Normally only modules what have a CPU or stack sample are included in the list of assemblies (thus you don't 
            /// unnecessarily have to generate NGEN PDBS for modules that will never be looked up).  However if there are 
            /// events that have addresses that need resolving that this routine does not recognise, this option can be
            /// set to insure that any module that was event LOADED is included.   This is inefficient, but guaranteed to
            /// be complete
            /// </summary>
            IncludeModulesWithOutSamples = 2
        }

        /// <summary>
        /// Given an ETL file, returns a list of the full paths to DLLs that were loaded in the trace that need symbolic 
        /// information (PDBs) so that the stack traces and CPU samples can be properly resolved.   By default this only
        /// returns NGEN images since these are the ones that need to be resolved and generated at collection time.   
        /// </summary>
        public static IEnumerable<string> GetModulesNeedingSymbols(string etlFile, ModuleSymbolOptions options = ModuleSymbolOptions.OnlyNGENImages)
        {
            var images = new List<ImageData>(300);
            var addressCounts = new Dictionary<Address, int>();

            // Get the name of all DLLS (in the file, and the set of all address-process pairs in the file.   
            using (var source = new ETWTraceEventSource(etlFile))
            {
                var handledImageGroupFiles = new HashSet<string>();
                source.Kernel.ImageGroup += delegate (ImageLoadTraceData data)
                {
                    var fileName = data.FileName;
                    if (!handledImageGroupFiles.Add(fileName))
                        return;

                    if (fileName.IndexOf(".ni.", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        // READY_TO_RUN support generate PDBs for ready-to-run images.    
                        // TODO can rip this out when we don't package ready-to-run images
                        var windowsIdx = fileName.IndexOf(@"\windows\", StringComparison.OrdinalIgnoreCase);
                        if (0 <= windowsIdx && windowsIdx <= 2)
                        {
                            return;
                        }

                        if (!File.Exists(fileName))
                        {
                            return;
                        }

                        try
                        {
                            using (var peFile = new PEFile.PEFile(fileName))
                            {
                                if (!peFile.IsManagedReadyToRun)
                                {
                                    return;
                                }
                            }
                        }
                        catch { return; }
                    }

                    var processId = data.ProcessID;
                    images.Add(new ImageData(processId, fileName, data.ImageBase, data.ImageSize));
                };

                source.Kernel.StackWalkStack += delegate (StackWalkStackTraceData data)
                {
                    if (data.ProcessID == 0)
                    {
                        return;
                    }

                    var processId = data.ProcessID;
                    for (int i = 0; i < data.FrameCount; i++)
                    {
                        var address = (data.InstructionPointer(i) & 0xFFFFFFFFFFFF0000L) + ((Address)(processId & 0xFFFF));
                        addressCounts[address] = 1;
                    }
                };

                source.Clr.ClrStackWalk += delegate (ClrStackWalkTraceData data)
                {
                    var processId = data.ProcessID;
                    for (int i = 0; i < data.FrameCount; i++)
                    {
                        var address = (data.InstructionPointer(i) & 0xFFFFFFFFFFFF0000L) + ((Address)(processId & 0xFFFF));
                        addressCounts[address] = 1;
                    }
                };

                source.Kernel.PerfInfoSample += delegate (SampledProfileTraceData data)
                {
                    if (data.ProcessID == 0)
                    {
                        return;
                    }

                    var processId = data.ProcessID;
                    var address = (data.InstructionPointer & 0xFFFFFFFFFFFF0000L) + ((Address)(processId & 0xFFFF));
                    addressCounts[address] = 1;
                };

                source.Process();
            }

            // imageNames is a set of names that we want symbols for.  
            var imageNames = new Dictionary<string, string>(100);
            foreach (var image in images)
            {
                if (!imageNames.ContainsKey(image.DllName))
                {
                    for (uint offset = 0; offset < (uint)image.Size; offset += 0x10000)
                    {
                        var key = image.BaseAddress + offset + (uint)(image.ProcessID & 0xFFFF);
                        if (addressCounts.ContainsKey(key))
                        {
                            imageNames[image.DllName] = image.DllName;
                            break;
                        }
                    }
                }
            }

            // Find the PDBS for the given images. 
            return new List<string>(imageNames.Keys);
        }

        #region Private
        /// <summary>
        /// Image data is a trivial record for image data, where it is keyed by the base address, processID and name.  
        /// </summary>
        private class ImageData : IComparable<ImageData>
        {
            public int CompareTo(ImageData other)
            {
                var ret = BaseAddress.CompareTo(other.BaseAddress);
                if (ret != 0)
                {
                    return ret;
                }

                ret = ProcessID - other.ProcessID;
                if (ret != 0)
                {
                    return ret;
                }

                return DllName.CompareTo(other.DllName);
            }

            public ImageData(int ProcessID, string DllName, Address BaseAddress, int Size)
            {
                this.ProcessID = ProcessID;
                this.DllName = DllName;
                this.BaseAddress = BaseAddress;
                this.Size = Size;
            }
            public int ProcessID;
            public string DllName;
            public Address BaseAddress;
            public int Size;
        }

        private void Initialize(IEnumerable<string> fileNames)
        {
            List<string> allLogFiles = new List<string>(fileNames);
            logFiles = new TraceEventNativeMethods.EVENT_TRACE_LOGFILEW[allLogFiles.Count];
            for (int i = 0; i < allLogFiles.Count; i++)
            {
                logFiles[i].LogFileName = allLogFiles[i];
            }

            InitializeFiles();
        }

        private void Initialize(string fileOrSessionName, TraceEventSourceType type)
        {

            // Allocate the LOGFILE and structures and arrays that hold them  
            // Figure out how many log files we have
            if (type == TraceEventSourceType.MergeAll)
            {
                List<string> allLogFiles = GetMergeAllLogFiles(fileOrSessionName);

                logFiles = new TraceEventNativeMethods.EVENT_TRACE_LOGFILEW[allLogFiles.Count];
                for (int i = 0; i < allLogFiles.Count; i++)
                {
                    logFiles[i].LogFileName = allLogFiles[i];
                }
            }
            else
            {
                logFiles = new TraceEventNativeMethods.EVENT_TRACE_LOGFILEW[1];
                if (type == TraceEventSourceType.FileOnly)
                {
                    logFiles[0].LogFileName = fileOrSessionName;
                }
                else
                {
                    Debug.Assert(type == TraceEventSourceType.Session);
                    logFiles[0].LoggerName = fileOrSessionName;
                    logFiles[0].LogFileMode |= TraceEventNativeMethods.EVENT_TRACE_REAL_TIME_MODE;
                    IsRealTime = true;
                }
            }

            InitializeFiles();
        }

        private void InitializeFiles()
        {
            handles = new ulong[logFiles.Length];

            // Fill  out the first log file information (we will clone it later if we have multiple files). 
            logFiles[0].BufferCallback = TraceEventBufferCallback;
            handles[0] = TraceEventNativeMethods.INVALID_HANDLE_VALUE;
            useClassicETW = !OperatingSystemVersion.AtLeast(OperatingSystemVersion.Vista);
            if (useClassicETW)
            {
                var mem = (TraceEventNativeMethods.EVENT_RECORD*)Marshal.AllocHGlobal(sizeof(TraceEventNativeMethods.EVENT_RECORD));
                *mem = default(TraceEventNativeMethods.EVENT_RECORD);
                convertedHeader = mem;
                logFiles[0].EventCallback = RawDispatchClassic;
            }
            else
            {
                logFiles[0].LogFileMode |= TraceEventNativeMethods.PROCESS_TRACE_MODE_EVENT_RECORD;
                logFiles[0].EventCallback = RawDispatch;
            }
            // We want the raw timestamp because it is needed to match up stacks with the event they go with.  
            logFiles[0].LogFileMode |= TraceEventNativeMethods.PROCESS_TRACE_MODE_RAW_TIMESTAMP;

            // Copy the information to any additional log files 
            for (int i = 1; i < logFiles.Length; i++)
            {
                logFiles[i].BufferCallback = logFiles[0].BufferCallback;
                logFiles[i].EventCallback = logFiles[0].EventCallback;
                logFiles[i].LogFileMode = logFiles[0].LogFileMode;
                handles[i] = handles[0];
            }

            DateTime minSessionStartTimeUTC = DateTime.MaxValue;
            DateTime maxSessionEndTimeUTC = DateTime.MinValue + new TimeSpan(1 * 365, 0, 0, 0); // TO avoid roundoff error when converting to QPC add a year.  

            // Open all the traces
            for (int i = 0; i < handles.Length; i++)
            {
                handles[i] = TraceEventNativeMethods.OpenTrace(ref logFiles[i]);
                if (handles[i] == TraceEventNativeMethods.INVALID_HANDLE_VALUE)
                {
                    Marshal.ThrowExceptionForHR(TraceEventNativeMethods.GetHRForLastWin32Error());
                }

                // Start time is minimum of all start times
                DateTime logFileStartTimeUTC = SafeFromFileTimeUtc(logFiles[i].LogfileHeader.StartTime);
                DateTime logFileEndTimeUTC = SafeFromFileTimeUtc(logFiles[i].LogfileHeader.EndTime);

                if (logFileStartTimeUTC < minSessionStartTimeUTC)
                {
                    minSessionStartTimeUTC = logFileStartTimeUTC;
                }
                // End time is maximum of all start times
                if (logFileEndTimeUTC > maxSessionEndTimeUTC)
                {
                    maxSessionEndTimeUTC = logFileEndTimeUTC;
                }

                // TODO do we even need log pointer size anymore?   
                // We take the max pointer size.  
                if ((int)logFiles[i].LogfileHeader.PointerSize > pointerSize)
                {
                    pointerSize = (int)logFiles[i].LogfileHeader.PointerSize;
                }
            }

            _QPCFreq = logFiles[0].LogfileHeader.PerfFreq;
            if (_QPCFreq == 0)
            {
                _QPCFreq = Stopwatch.Frequency;
            }

            // Real time providers don't set this to something useful
            if ((logFiles[0].LogFileMode & TraceEventNativeMethods.EVENT_TRACE_REAL_TIME_MODE) != 0)
            {
                DateTime nowUTC = DateTime.UtcNow;
                long nowQPC = QPCTime.GetUTCTimeAsQPC(nowUTC);

                _syncTimeQPC = nowQPC;
                _syncTimeUTC = nowUTC;

                sessionStartTimeQPC = nowQPC - _QPCFreq / 10;           // Subtract 1/10 sec to keep now and nowQPC in sync.  
                sessionEndTimeQPC = long.MaxValue;                      // Represents infinity.      

                Debug.Assert(SessionStartTime < SessionEndTime);
            }
            else
            {
                _syncTimeUTC = minSessionStartTimeUTC;

                // UTCDateTimeToQPC is actually going to give the wrong value for these because we have
                // not set _syncTimeQPC, but will be adjusted when we see the event Header and know _syncTypeQPC.  
                sessionStartTimeQPC = UTCDateTimeToQPC(minSessionStartTimeUTC);
                sessionEndTimeQPC = UTCDateTimeToQPC(maxSessionEndTimeUTC);
            }
            Debug.Assert(_QPCFreq != 0);
            if (pointerSize == 0 || IsRealTime)  // We get on x64 OS 4 as pointer size which is wrong for realtime sessions. Fix it up. 
            {
                pointerSize = GetOSPointerSize();

                Debug.Assert((logFiles[0].LogFileMode & TraceEventNativeMethods.EVENT_TRACE_REAL_TIME_MODE) != 0);
            }
            Debug.Assert(pointerSize == 4 || pointerSize == 8);

            cpuSpeedMHz = (int)logFiles[0].LogfileHeader.CpuSpeedInMHz;
            numberOfProcessors = (int)logFiles[0].LogfileHeader.NumberOfProcessors;

            // We ask for raw timestamps, but the log file may have used system time as its raw timestamp.
            // SystemTime is like a QPC time that happens 10M times a second (100ns).  
            // ReservedFlags is actually the ClockType 0 = Raw, 1 = QPC, 2 = SystemTimne 3 = CpuTick (we don't support)
            if (logFiles[0].LogfileHeader.ReservedFlags == 2)   // If ClockType == EVENT_TRACE_CLOCK_SYSTEMTIME
            {
                _QPCFreq = 10000000;
            }

            Debug.Assert(_QPCFreq != 0);
            int ver = (int)logFiles[0].LogfileHeader.Version;
            osVersion = new Version((byte)ver, (byte)(ver >> 8));

            // Logic for looking up process names
            processNameForID = new Dictionary<int, string>();

            var kernelParser = new KernelTraceEventParser(this, KernelTraceEventParser.ParserTrackingOptions.None);
            kernelParser.ProcessStartGroup += delegate (ProcessTraceData data)
            {
                // Get just the file name without the extension.  Can't use the 'Path' class because
                // it tests to make certain it does not have illegal chars etc.  Since KernelImageFileName
                // is not a true user mode path, we can get failures. 
                string path = data.KernelImageFileName;
                int startIdx = path.LastIndexOf('\\');
                if (0 <= startIdx)
                {
                    startIdx++;
                }
                else
                {
                    startIdx = 0;
                }

                int endIdx = path.LastIndexOf('.');
                if (endIdx <= startIdx)
                {
                    endIdx = path.Length;
                }

                processNameForID[data.ProcessID] = path.Substring(startIdx, endIdx - startIdx);
            };
            kernelParser.ProcessEndGroup += delegate (ProcessTraceData data)
            {
                processNameForID.Remove(data.ProcessID);
            };
            kernelParser.EventTraceHeader += delegate (EventTraceHeaderTraceData data)
            {
                if (_syncTimeQPC == 0)
                {   // In merged files there can be more of these, we only set the QPC time on the first one 
                    // We were using a 'start location' of 0, but we want it to be the timestamp of this events, so we add this to our 
                    // existing QPC values.
                    _syncTimeQPC = data.TimeStampQPC;
                    sessionStartTimeQPC += data.TimeStampQPC;
                    sessionEndTimeQPC += data.TimeStampQPC;
                }
            };
        }

        /// <summary>
        /// Returns the size of pointer (8 or 4) for the operating system (not necessarily the process) 
        /// </summary>
        internal static int GetOSPointerSize()
        {
            if (IntPtr.Size == 8)
            {
                return 8;
            }
#if !NETSTANDARD1_6
            bool is64bitOS = Environment.Is64BitOperatingSystem;
#else
            // Sadly this API does not work properly on V4.7.1 of the Desktop framework.   See https://github.com/Microsoft/perfview/issues/478 for more.  
            // However with this partial fix, (works on everything not NetSTandard, and only in 32 bit processes), that we can wait for the fix.
            bool is64bitOS = (RuntimeInformation.OSArchitecture == Architecture.X64 || RuntimeInformation.OSArchitecture == Architecture.Arm64);
#endif
            return is64bitOS ? 8 : 4;
        }

        internal static DateTime SafeFromFileTimeUtc(long fileTime)
        {
            ulong maxTime = (ulong)DateTime.MaxValue.ToFileTimeUtc();
            if (maxTime < (ulong)fileTime)
            {
                return DateTime.MaxValue;
            }

            return DateTime.FromFileTimeUtc(fileTime);
        }

        /// <summary>
        /// This is a little helper class that maps QueryPerformanceCounter (QPC) ticks to DateTime.  There is an error of
        /// a few msec, but as long as every one uses the same one, we probably don't care.  
        /// </summary>
        private class QPCTime
        {
            public static long GetUTCTimeAsQPC(DateTime utcTime)
            {
                QPCTime qpcTime = new QPCTime();
                return qpcTime._GetUTCTimeAsQPC(utcTime);
            }

            #region private
            private long _GetUTCTimeAsQPC(DateTime utcTime)
            {
                // Convert to seconds from the baseline
                double deltaSec = (utcTime.Ticks - m_timeAsDateTimeUTC.Ticks) / 10000000.0;
                // scale to QPC units and then add back in the base.  
                return (long)(deltaSec * Stopwatch.Frequency) + m_timeAsQPC;
            }

            private QPCTime()
            {
                // We call Now and GetTimeStame at one point (it will be off by the latency of
                // one call to these functions).   However since UtcNow only changes once every 16
                // msec, we loop until we see it change which lets us get with 1-2 msec of the
                // correct synchronization.  
                DateTime start = DateTime.UtcNow;
                long lastQPC = Stopwatch.GetTimestamp();
                for (; ; )
                {
                    var next = DateTime.UtcNow;
                    m_timeAsQPC = Stopwatch.GetTimestamp();
                    if (next != start)
                    {
                        m_timeAsDateTimeUTC = next;
                        m_timeAsQPC = lastQPC;       // We would rather be before than after.   
                        break;
                    }
                    lastQPC = m_timeAsQPC;
                }
            }

            // A QPC object just needs to hold a point in time in both units (DateTime and QPC). 
            private DateTime m_timeAsDateTimeUTC;
            private long m_timeAsQPC;

            #endregion
        }

        internal static List<string> GetMergeAllLogFiles(string fileName)
        {
            string fileBaseName = Path.GetFileNameWithoutExtension(fileName);
            string dir = Path.GetDirectoryName(fileName);
            if (dir.Length == 0)
            {
                dir = ".";
            }

            List<string> allLogFiles = new List<string>();
            allLogFiles.AddRange(Directory.GetFiles(dir, fileBaseName + ".etl"));
            allLogFiles.AddRange(Directory.GetFiles(dir, fileBaseName + ".kernel*.etl"));
            allLogFiles.AddRange(Directory.GetFiles(dir, fileBaseName + ".clr*.etl"));
            allLogFiles.AddRange(Directory.GetFiles(dir, fileBaseName + ".user*.etl"));

            if (allLogFiles.Count == 0)
            {
                throw new FileNotFoundException("Could not find file     " + fileName);
            }

            return allLogFiles;
        }

        private bool ProcessOneFile()
        {
            int dwErr = TraceEventNativeMethods.ProcessTrace(handles, (uint)handles.Length, (IntPtr)0, (IntPtr)0);
            if (dwErr == 6)
            {
                throw new ApplicationException("Error opening ETL file.  Most likely caused by opening a Win8 Trace on a Pre Win8 OS.");
            }

            // ETW returns 1223 when you stop processing explicitly 
            if (!(dwErr == 1223 && stopProcessing))
            {
                Marshal.ThrowExceptionForHR(TraceEventNativeMethods.GetHRFromWin32(dwErr));
            }

            return !stopProcessing;
        }

#if DEBUG
        internal bool DisallowEventIndexAccess { get; set; }
#endif

        // #ETWTraceEventSourceInternals
        // 
        // ETWTraceEventSource is a wrapper around the Windows API TraceEventNativeMethods.OpenTrace
        // method (see http://msdn2.microsoft.com/en-us/library/aa364089.aspx) We set it up so that we call
        // back to ETWTraceEventSource.Dispatch which is the heart of the event callback logic.
        // [SecuritySafeCritical]
        private void RawDispatchClassic(TraceEventNativeMethods.EVENT_RECORD* eventData)
        {
            // TODO not really a EVENT_RECORD on input, but it is a pain to be type-correct.  
            TraceEventNativeMethods.EVENT_TRACE* oldStyleHeader = (TraceEventNativeMethods.EVENT_TRACE*)eventData;
            eventData = convertedHeader;

            eventData->EventHeader.Size = (ushort)sizeof(TraceEventNativeMethods.EVENT_TRACE_HEADER);
            // HeaderType
            eventData->EventHeader.Flags = TraceEventNativeMethods.EVENT_HEADER_FLAG_CLASSIC_HEADER;

            // TODO Figure out if there is a marker that is used in the WOW for the classic providers 
            // right now I assume they are all the same as the machine.  
            if (pointerSize == 8)
            {
                eventData->EventHeader.Flags |= TraceEventNativeMethods.EVENT_HEADER_FLAG_64_BIT_HEADER;
            }
            else
            {
                eventData->EventHeader.Flags |= TraceEventNativeMethods.EVENT_HEADER_FLAG_32_BIT_HEADER;
            }

            // EventProperty
            eventData->EventHeader.ThreadId = oldStyleHeader->Header.ThreadId;
            eventData->EventHeader.ProcessId = oldStyleHeader->Header.ProcessId;
            eventData->EventHeader.TimeStamp = oldStyleHeader->Header.TimeStamp;
            eventData->EventHeader.ProviderId = oldStyleHeader->Header.Guid;            // ProviderId = TaskId
            // ID left 0
            eventData->EventHeader.Version = (byte)oldStyleHeader->Header.Version;
            // Channel
            eventData->EventHeader.Level = oldStyleHeader->Header.Level;
            eventData->EventHeader.Opcode = oldStyleHeader->Header.Type;
            // Task
            // Keyword
            eventData->EventHeader.KernelTime = oldStyleHeader->Header.KernelTime;
            eventData->EventHeader.UserTime = oldStyleHeader->Header.UserTime;
            // ActivityID

            eventData->BufferContext = oldStyleHeader->BufferContext;
            // ExtendedDataCount
            eventData->UserDataLength = (ushort)oldStyleHeader->MofLength;
            // ExtendedData
            eventData->UserData = oldStyleHeader->MofData;
            // UserContext 

            RawDispatch(eventData);
        }

        // [SecuritySafeCritical]
        private void RawDispatch(TraceEventNativeMethods.EVENT_RECORD* rawData)
        {
            if (stopProcessing)
            {
                return;
            }

            if (lockObj != null)
            {
                Monitor.Enter(lockObj);
            }

            Debug.Assert(rawData->EventHeader.HeaderType == 0);     // if non-zero probably old-style ETW header

            // Give it an event ID if it does not have one.  
            traceLoggingEventId.TestForTraceLoggingEventAndFixupIfNeeded(rawData);

            TraceEvent anEvent = Lookup(rawData);
#if DEBUG
            anEvent.DisallowEventIndexAccess = DisallowEventIndexAccess;
#endif
            // Keep in mind that for UnhandledTraceEvent 'PrepForCallback' has NOT been called, which means the
            // opcode, guid and eventIds are not correct at this point.  The ToString() routine WILL call
            // this so if that is in your debug window, it will have this side effect (which is good and bad)
            // Looking at rawData will give you the truth however. 
            anEvent.DebugValidate();

            if (anEvent.NeedsFixup)
            {
                anEvent.FixupData();
            }

            Dispatch(anEvent);

            if (lockObj != null)
            {
                Monitor.Exit(lockObj);
            }
        }

        /// <summary>
        /// see Dispose pattern
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            // We only want one thread doing this at a time.  
            lock (this)
            {
                stopProcessing = true;
                if (handles != null)
                {
                    foreach (ulong handle in handles)
                    {
                        if (handle != TraceEventNativeMethods.INVALID_HANDLE_VALUE)
                        {
                            TraceEventNativeMethods.CloseTrace(handle);
                        }
                    }

                    handles = null;
                }

                if (convertedHeader != null)
                {
                    Marshal.FreeHGlobal((IntPtr)convertedHeader);
                    convertedHeader = null;
                }

                traceLoggingEventId.Dispose();

                // logFiles = null; Keep the callback delegate alive as long as possible.  
                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// see Dispose pattern
        /// </summary>
        ~ETWTraceEventSource()
        {
            Dispose(false);
        }

        private void Reset()
        {
            if (!CanReset)
            {
                throw new InvalidOperationException("Event stream is not resetable (e.g. real time).");
            }

            if (handles != null)
            {
                for (int i = 0; i < handles.Length; i++)
                {
                    if (handles[i] != TraceEventNativeMethods.INVALID_HANDLE_VALUE)
                    {
                        TraceEventNativeMethods.CloseTrace(handles[i]);
                        handles[i] = TraceEventNativeMethods.INVALID_HANDLE_VALUE;
                    }
                    // Annoying.  The OS resets the LogFileMode field, so I have to set it up again.   
                    if (!useClassicETW)
                    {
                        logFiles[i].LogFileMode = TraceEventNativeMethods.PROCESS_TRACE_MODE_EVENT_RECORD;
                        logFiles[i].LogFileMode |= TraceEventNativeMethods.PROCESS_TRACE_MODE_RAW_TIMESTAMP;
                    }

                    handles[i] = TraceEventNativeMethods.OpenTrace(ref logFiles[i]);

                    if (handles[i] == TraceEventNativeMethods.INVALID_HANDLE_VALUE)
                    {
                        Marshal.ThrowExceptionForHR(TraceEventNativeMethods.GetHRForLastWin32Error());
                    }
                }
            }
        }

        // Private data / methods 
        // [SecuritySafeCritical]
        private bool TraceEventBufferCallback(IntPtr rawLogFile)
        {
            return !stopProcessing;
        }

        // #ETWTraceEventSourceFields
        private bool processTraceCalled;
        private TraceEventNativeMethods.EVENT_RECORD* convertedHeader;

        // Returned from OpenTrace
        private TraceEventNativeMethods.EVENT_TRACE_LOGFILEW[] logFiles;
        private UInt64[] handles;

        private IEnumerable<string> fileNames;        // Used if more than one file being processed.  (Null otherwise)

        // TODO this can be removed, and use AddDispatchHook instead.  
        /// <summary>
        /// Used by real time TraceLog on Windows7.   
        /// If we have several real time sources we have them coming in on several threads, but we want the illusion that they
        /// are one source (thus being processed one at a time).  Thus we want a lock that is taken on every dispatch.   
        /// </summary>
        internal object lockObj;

        // We do minimal processing to keep track of process names (since they are REALLY handy). 
        private Dictionary<int, string> processNameForID;

        // Used to give TraceLogging events Event IDs. 
        private TraceLoggingEventId traceLoggingEventId;

        internal override string ProcessName(int processID, long time100ns)
        {
            string ret;
            if (!processNameForID.TryGetValue(processID, out ret))
            {
                ret = "";
            }

            return ret;
        }
        #endregion
    }

    /// <summary>
    /// The kinds of data sources that can be opened (see ETWTraceEventSource)
    /// </summary>
    public enum TraceEventSourceType
    {
        /// <summary>
        /// Look for any files like *.etl or *.*.etl (the later holds things like *.kernel.etl or *.clrRundown.etl ...)
        /// </summary>
        MergeAll,
        /// <summary>
        /// Look for a ETL moduleFile *.etl as the event data source 
        /// </summary>
        FileOnly,
        /// <summary>
        /// Use a real time session as the event data source.
        /// </summary>
        Session,
    };
}
