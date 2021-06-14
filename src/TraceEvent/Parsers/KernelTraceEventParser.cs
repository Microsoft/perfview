//     Copyright (c) Microsoft Corporation.  All rights reserved.
//
using FastSerialization;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Utilities;
using Address = System.UInt64;

/* This file was generated with the command */
// traceParserGen /needsState /merge /renameFile KernelTraceEventParser.renames /mof KernelTraceEventParser.mof KernelTraceEventParser.cs
/* And then modified by hand to add functionality (handle to name lookup, fixup of events ...) */
// The version before any hand modifications is kept as KernelTraceEventParser.base.cs, and a 3
// way diff is done when traceParserGen is rerun.  This allows the 'by-hand' modifications to be
// applied again if the mof or the traceParserGen transformation changes. 
// 
// See traceParserGen /usersGuide for more on the /merge option 

// TODO I have low confidence in the TCP headers, especially for Versions < 2 (how much do we care?)
namespace Microsoft.Diagnostics.Tracing.Parsers
{
    /// <summary>
    /// The KernelTraceEventParser is a class that knows how to decode the 'standard' kernel events.
    /// It exposes an event for each event of interest that users can subscribe to.
    /// 
    /// see TraceEventParser for more 
    /// </summary>
    // [SecuritySafeCritical]
    [System.CodeDom.Compiler.GeneratedCode("traceparsergen", "1.0")]
    public sealed class KernelTraceEventParser : TraceEventParser
    {
        /// <summary>
        /// The special name for the Kernel session
        /// </summary>
        public static string KernelSessionName { get { return "NT Kernel Logger"; } }

        public static readonly string ProviderName = "Windows Kernel";
        public static readonly Guid ProviderGuid = new Guid(unchecked((int)0x9e814aad), unchecked((short)0x3204), unchecked((short)0x11d2), 0x9a, 0x82, 0x00, 0x60, 0x08, 0xa8, 0x69, 0x39);
        /// <summary>
        /// This is passed to TraceEventSession.EnableKernelProvider to enable particular sets of
        /// events.  See http://msdn.microsoft.com/en-us/library/aa363784(VS.85).aspx for more information on them 
        /// </summary>
        [Flags]
        public enum Keywords
        {
            /// <summary>
            /// Logs nothing
            /// </summary>
            None = 0x00000000, // no tracing
            // Part of the 'default set of keywords' (good value in most scenarios).  
            /// <summary>
            /// Logs the mapping of file IDs to actual (kernel) file names. 
            /// </summary>
            DiskFileIO = 0x00000200,
            /// <summary>
            /// Loads the completion of Physical disk activity. 
            /// </summary>
            DiskIO = 0x00000100, // physical disk IO
            /// <summary>
            /// Logs native modules loads (LoadLibrary), and unloads
            /// </summary>
            ImageLoad = 0x00000004, // image load
            /// <summary>
            /// Logs all page faults that must fetch the data from the disk (hard faults)
            /// </summary>
            MemoryHardFaults = 0x00002000,
            /// <summary>
            /// Logs TCP/IP network send and receive events. 
            /// </summary>
            NetworkTCPIP = 0x00010000,
            /// <summary>
            /// Logs process starts and stops.
            /// </summary>
            Process = 0x00000001,
            /// <summary>
            /// Logs process performance counters (TODO When?) (Vista+ only)
            /// see KernelTraceEventParser.ProcessPerfCtr, ProcessPerfCtrTraceData
            /// </summary>
            ProcessCounters = 0x00000008,
            /// <summary>
            /// Sampled based profiling (every msec) (Vista+ only) (expect 1K events per proc per second)
            /// </summary>
            Profile = 0x01000000,
            /// <summary>
            /// Logs threads starts and stops
            /// </summary>
            Thread = 0x00000002,

            // These are useful in some situations, however are more volumous so are not part of the default set. 
            /// <summary>
            /// log thread context switches (Vista only) (can be > 10K events per second)
            /// </summary>
            ContextSwitch = 0x00000010,
            /// <summary>
            /// log Disk operations (Vista+ only)
            /// Generally not TOO volumous (typically less than 1K per second) (Stacks associated with this)
            /// </summary>
            DiskIOInit = 0x00000400,
            /// <summary>
            /// Thread Dispatcher (ReadyThread) (Vista+ only) (can be > 10K events per second)
            /// </summary>
            Dispatcher = 0x00000800,
            /// <summary>
            /// log file FileOperationEnd (has status code) when they complete (even ones that do not actually
            /// cause Disk I/O).  (Vista+ only)
            /// Generally not TOO volumous (typically less than 1K per second) (No stacks associated with these)
            /// </summary>
            FileIO = 0x02000000,
            /// <summary>
            /// log the start of the File I/O operation as well as the end. (Vista+ only)
            /// Generally not TOO volumous (typically less than 1K per second)
            /// </summary>
            FileIOInit = 0x04000000,
            /// <summary>
            /// Logs all page faults (hard or soft)
            /// Can be pretty volumous (> 1K per second)
            /// </summary>
            Memory = 0x00001000,
            /// <summary>
            /// Logs activity to the windows registry. 
            /// Can be pretty volumous (> 1K per second)
            /// </summary>
            Registry = 0x00020000, // registry calls
            /// <summary>
            /// log calls to the OS (Vista+ only)
            /// This is VERY volumous (can be > 100K events per second)
            /// </summary>
            SystemCall = 0x00000080,
            /// <summary>
            /// Log Virtual Alloc calls and VirtualFree.   (Vista+ Only)
            /// Generally not TOO volumous (typically less than 1K per second)
            /// </summary> 
            VirtualAlloc = 0x004000,
            /// <summary>
            /// Log mapping of files into memory (Win8 and above Only)
            /// Generally low volume.  
            /// </summary>
            VAMap = 0x8000,

            // advanced logging (when you care about the internals of the OS)
            /// <summary>
            /// Logs Advanced Local Procedure call events. 
            /// </summary>
            AdvancedLocalProcedureCalls = 0x00100000,
            /// <summary>
            /// log defered procedure calls (an Kernel mechanism for having work done asynchronously) (Vista+ only)
            /// </summary> 
            DeferedProcedureCalls = 0x00000020,
            /// <summary>
            /// Device Driver logging (Vista+ only)
            /// </summary>
            Driver = 0x00800000,
            /// <summary>
            /// log hardware interrupts. (Vista+ only)
            /// </summary>
            Interrupt = 0x00000040,
            /// <summary>
            /// Disk I/O that was split (eg because of mirroring requirements) (Vista+ only)
            /// </summary> 
            SplitIO = 0x00200000,
            /// <summary>
            /// Good default kernel flags.  (TODO more detail)
            /// </summary>  
            Default = DiskIO | DiskFileIO | DiskIOInit | ImageLoad | MemoryHardFaults | NetworkTCPIP | Process | ProcessCounters | Profile | Thread,
            /// <summary>
            /// These events are too verbose for normal use, but this give you a quick way of turing on 'interesting' events
            /// This does not include SystemCall because it is 'too verbose'
            /// </summary>
            Verbose = Default | ContextSwitch | Dispatcher | FileIO | FileIOInit | Memory | Registry | VirtualAlloc | VAMap,  // use as needed
            /// <summary>
            /// Use this if you care about blocked time.  
            /// </summary>
            ThreadTime = Default | ContextSwitch | Dispatcher,
            /// <summary>
            /// You mostly don't care about these unless you are dealing with OS internals.  
            /// </summary>
            OS = AdvancedLocalProcedureCalls | DeferedProcedureCalls | Driver | Interrupt | SplitIO,
            /// <summary>
            /// All legal kernel events
            /// </summary>
            All = Verbose | ContextSwitch | Dispatcher | FileIO | FileIOInit | Memory | Registry | VirtualAlloc | VAMap  // use as needed
                | SystemCall        // Interesting but very expensive. 
                | OS,

            /// <summary>
            /// These are the kernel events that are not allowed in containers.  Can be subtracted out.  
            /// </summary>
            NonContainer = ~(Process | Thread | ImageLoad | Profile | ContextSwitch | ProcessCounters),

            // These are ones that I have made up  
            // All = 0x07B3FFFF, so 4'0000, 8'0000, 40'0000, and F000'00000 are free.  
            /// <summary>
            /// Turn on PMC (Precise Machine Counter) events.   Only Win 8
            /// </summary>
            PMCProfile = unchecked((int)0x80000000),
            /// <summary>
            /// Kernel reference set events (like XPERF ReferenceSet).   Fully works only on Win 8.  
            /// </summary>
            ReferenceSet = 0x40000000,
            /// <summary>
            /// Events when thread priorities change.  
            /// </summary>
            ThreadPriority = 0x20000000,
            /// <summary>
            /// Events when queuing and dequeuing from the I/O completion ports.    
            /// </summary>
            IOQueue = 0x10000000,
            /// <summary>
            /// Handle creation and closing (for handle leaks) 
            /// </summary>
            Handle = 0x400000,
        };

        /// <summary>
        /// These keywords can't be passed to the OS, they are defined by KernelTraceEventParser
        /// </summary>
        internal static Keywords NonOSKeywords
        {
            get
            {
                var ret = (Keywords)unchecked((int)0xf84c8000); // PMCProfile ReferenceSet ThreadPriority IOQueue Handle VAMap 
                if (OperatingSystemVersion.AtLeast(OperatingSystemVersion.Win8))
                    ret &= ~Keywords.VAMap;
                return ret;
            }
        }

        /// <summary>
        /// What his parser should track by default.  
        /// </summary>
        [Flags]
        public enum ParserTrackingOptions
        {
            None = 0,
            ThreadToProcess = 1,
            FileNameToObject = 2,
            RegistryNameToObject = 4,   // Not on by default for real time sessions 
            DiskIOServiceTime = 8,      // Not on by default for real time sessions
            VolumeMapping = 16,
            ObjectNameToObject = 32,
            Default = ThreadToProcess + FileNameToObject + DiskIOServiceTime + VolumeMapping + RegistryNameToObject + ObjectNameToObject,
        }

        public KernelTraceEventParser(TraceEventSource source) : this(source, DefaultOptionsForSource(source)) { }

        public KernelTraceEventParser(TraceEventSource source, ParserTrackingOptions tracking)
            : base(source)
        {
            // TODO FIX NOW, need to make it so that this is bounded.  

            // Note that all kernel parsers share the same state.   
            KernelTraceEventParserState state = State;
            if ((tracking & ParserTrackingOptions.RegistryNameToObject) != 0 && (state.callBacksSet & ParserTrackingOptions.RegistryNameToObject) == 0)
            {
                state.callBacksSet |= ParserTrackingOptions.RegistryNameToObject;
                // logic to initialize state
                AddCallbackForEvents(delegate (RegistryTraceData data)
                {
                    var isRundown = (data.Opcode == (TraceEventOpcode)22);        // RegistryRundown
                    if (RegistryTraceData.NameIsKeyName(data.Opcode))
                        state.fileIDToName.Add(data.KeyHandle, data.TimeStampQPC, data.KeyName, isRundown);
                });
            }
            if ((tracking & ParserTrackingOptions.FileNameToObject) != 0 && (state.callBacksSet & ParserTrackingOptions.FileNameToObject) == 0)
            {
                state.callBacksSet |= ParserTrackingOptions.FileNameToObject;

                AddCallbackForEvents<FileIONameTraceData>(delegate (FileIONameTraceData data)
                {
                    // TODO this does now work for DCStarts.  Do DCStarts event exist?  
                    var isRundown = (data.Opcode == (TraceEventOpcode)36) || (data.Opcode == (TraceEventOpcode)35);        // 36=FileIOFileRundown 35=FileIODelete
                    Debug.Assert(data.FileName.Length != 0);
                    state.fileIDToName.Add(data.FileKey, data.TimeStampQPC, data.FileName, isRundown);
                });

#if !DOTNET_V35
                // Because we may not have proper startup rundown, we also remember not only the FileKey but 
                // also the fileObject (which is per-open file not per fileName).   
                FileIOCreate += delegate (FileIOCreateTraceData data)
                {
                    state.fileIDToName.Add(data.FileObject, data.TimeStampQPC, data.FileName);
                };

                if (source.IsRealTime)
                {
                    // Keep the table under control
                    Action<FileIONameTraceData> onNameDeath = delegate (FileIONameTraceData data)
                    {
                        state.fileIDToName.Remove(data.FileKey);
                    };
                    FileIOFileDelete += onNameDeath;
                    FileIOFileRundown += onNameDeath;

                    FileIOCleanup += delegate (FileIOSimpleOpTraceData data)
                    {
                        // Keep the table under control remove unneeded entries.  
                        state.fileIDToName.Remove(data.FileObject);
                    };
                }
#endif
            }
            if ((tracking & ParserTrackingOptions.ObjectNameToObject) != 0 && (state.callBacksSet & ParserTrackingOptions.ObjectNameToObject) == 0)
            {
                state.callBacksSet |= ParserTrackingOptions.ObjectNameToObject;
                // logic to initialize state
                AddCallbackForEvents(delegate (ObjectNameTraceData data)
                {
                    state.fileIDToName.Add(data.Object, data.TimeStampQPC, data.ObjectName, true);
                });

                AddCallbackForEvents(delegate (ObjectTypeNameTraceData data)
                {
                    if (state._objectTypeToName == null)
                        state._objectTypeToName = new Dictionary<int, string>(50);
                    state._objectTypeToName[data.ObjectType] = data.ObjectTypeName;
                });
            }
            if ((tracking & ParserTrackingOptions.ThreadToProcess) != 0 && (state.callBacksSet & ParserTrackingOptions.ThreadToProcess) == 0)
            {
                state.callBacksSet |= ParserTrackingOptions.ThreadToProcess;
                ThreadStartGroup += delegate (ThreadTraceData data)
                {
                    Debug.Assert(data.ThreadID >= 0);
                    Debug.Assert(data.ProcessID >= 0);
                    state.threadIDtoProcessID.Add(data.ThreadID, 0, data.ProcessID);
                };
                ThreadEndGroup += delegate (ThreadTraceData data)
                {
                    int processID;
                    if (source.IsRealTime)
                    {
                        state.threadIDtoProcessID.Remove(data.ThreadID);
                    }
                    else
                    {
                        // Do we have thread start information for this thread?
                        if (!state.threadIDtoProcessID.TryGetValue(data.ThreadID, data.TimeStampQPC, out processID))
                        {
                            // No, this is likely a circular buffer, remember the thread end information 
                            if (state.threadIDtoProcessIDRundown == null)
                                state.threadIDtoProcessIDRundown = new HistoryDictionary<int, int>(100);

                            // Notice I NEGATE the timestamp, this way HistoryDictionary does the comparison the way I want it.  
                            state.threadIDtoProcessIDRundown.Add(data.ThreadID, -data.TimeStampQPC, data.ProcessID);
                        }
                    }
                };
            }

            if ((tracking & ParserTrackingOptions.DiskIOServiceTime) != 0 && (state.callBacksSet & ParserTrackingOptions.DiskIOServiceTime) == 0)
            {
                state.callBacksSet |= ParserTrackingOptions.DiskIOServiceTime;
                AddCallbackForEvents(delegate (DiskIOTraceData data)
                {
                    state.diskEventTimeStamp.Add(new KernelTraceEventParserState.DiskIOTime(data.DiskNumber, data.TimeStampRelativeMSec));
                });

                DiskIOFlushBuffers += delegate (DiskIOFlushBuffersTraceData data)
                {
                    state.diskEventTimeStamp.Add(new KernelTraceEventParserState.DiskIOTime(data.DiskNumber, data.TimeStampRelativeMSec));
                };
            }

            if ((tracking & ParserTrackingOptions.DiskIOServiceTime) != 0 && (state.callBacksSet & ParserTrackingOptions.VolumeMapping) == 0)
            {
                state.callBacksSet |= ParserTrackingOptions.VolumeMapping;
                SysConfigVolumeMapping += delegate (VolumeMappingTraceData data)
                {
                    state.driveMapping.AddMapping(data.NtPath, data.DosPath);
                };
                SysConfigSystemPaths += delegate (SystemPathsTraceData data)
                {
                    var windows = data.SystemWindowsDirectory;
                    state.driveMapping.AddSystemDrive(windows);
                };
            }
        }

        /// <summary>
        /// Defines how kernel paths are converted to user paths. Setting it overrides the default path conversion mechanism.
        /// </summary>
        public Func<string, string> KernelPathToUserPathMapper { set { State.driveMapping.MapKernelToUser = value; } }

        public string FileIDToFileName(Address fileKey)
        {
            return State.KernelToUser(State.FileIDToName(fileKey, long.MaxValue));
        }

        public event Action<EventTraceHeaderTraceData> EventTraceHeader
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new EventTraceHeaderTraceData(value, 0xFFFF, 0, "EventTrace", EventTraceTaskGuid, 0, "Header", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 0, EventTraceTaskGuid);
            }
        }
        public event Action<HeaderExtensionTraceData> EventTraceExtension
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new HeaderExtensionTraceData(value, 0xFFFF, 0, "EventTrace", EventTraceTaskGuid, 5, "Extension", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 5, EventTraceTaskGuid);
            }
        }
        public event Action<HeaderExtensionTraceData> EventTraceEndExtension
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new HeaderExtensionTraceData(value, 0xFFFF, 0, "EventTrace", EventTraceTaskGuid, 32, "EndExtension", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 32, EventTraceTaskGuid);
            }
        }
        public event Action<EmptyTraceData> EventTraceRundownComplete
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new EmptyTraceData(value, 0xFFFF, 0, "EventTrace", EventTraceTaskGuid, 8, "RundownComplete", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 8, EventTraceTaskGuid);
            }
        }
        public event Action<ProcessTraceData> ProcessStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ProcessTraceData(value, 0xFFFF, 1, "Process", ProcessTaskGuid, 1, "Start", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 1, ProcessTaskGuid);
            }
        }
        /// <summary>
        /// Registers both ProcessStart and ProcessDCStart
        /// </summary>
        public event Action<ProcessTraceData> ProcessStartGroup
        {
            add
            {
                ProcessStart += value;
                ProcessDCStart += value;
            }
            remove
            {
                ProcessStart -= value;
                ProcessDCStart -= value;
            }
        }

        public event Action<ProcessTraceData> ProcessStop
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ProcessTraceData(value, 0xFFFF, 1, "Process", ProcessTaskGuid, 2, "Stop", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 2, ProcessTaskGuid);
            }
        }
        /// <summary>
        /// Registers both ProcessEnd and ProcessDCStop
        /// </summary>
        public event Action<ProcessTraceData> ProcessEndGroup
        {
            add
            {
                ProcessStop += value;
                ProcessDCStop += value;
            }
            remove
            {
                ProcessStop -= value;
                ProcessDCStop -= value;
            }
        }
        public event Action<ProcessTraceData> ProcessGroup
        {
            add
            {
                ProcessEndGroup += value;
                ProcessStartGroup += value;
            }
            remove
            {
                ProcessEndGroup -= value;
                ProcessStartGroup -= value;
            }
        }

        public event Action<ProcessTraceData> ProcessDCStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ProcessTraceData(value, 0xFFFF, 1, "Process", ProcessTaskGuid, 3, "DCStart", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 3, ProcessTaskGuid);
            }
        }
        public event Action<ProcessTraceData> ProcessDCStop
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ProcessTraceData(value, 0xFFFF, 1, "Process", ProcessTaskGuid, 4, "DCStop", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 4, ProcessTaskGuid);
            }
        }
        public event Action<ProcessTraceData> ProcessDefunct
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ProcessTraceData(value, 0xFFFF, 1, "Process", ProcessTaskGuid, 39, "Defunct", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 39, ProcessTaskGuid);
            }
        }
        public event Action<ProcessCtrTraceData> ProcessPerfCtr
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ProcessCtrTraceData(value, 0xFFFF, 1, "Process", ProcessTaskGuid, 32, "PerfCtr", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 32, ProcessTaskGuid);
            }
        }
        public event Action<ProcessCtrTraceData> ProcessPerfCtrRundown
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ProcessCtrTraceData(value, 0xFFFF, 1, "Process", ProcessTaskGuid, 33, "PerfCtrRundown", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 33, ProcessTaskGuid);
            }
        }
        public event Action<ThreadTraceData> ThreadStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ThreadTraceData(value, 0xFFFF, 2, "Thread", ThreadTaskGuid, 1, "Start", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 1, ThreadTaskGuid);
            }
        }
        /// <summary>
        /// Registers both ThreadStart and ThreadDCStart
        /// </summary>
        public event Action<ThreadTraceData> ThreadStartGroup
        {
            add
            {
                ThreadStart += value;
                ThreadDCStart += value;
            }
            remove
            {
                ThreadStart -= value;
                ThreadDCStart -= value;
            }
        }
        public event Action<ThreadTraceData> ThreadStop
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ThreadTraceData(value, 0xFFFF, 2, "Thread", ThreadTaskGuid, 2, "Stop", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 2, ThreadTaskGuid);
            }
        }
        /// <summary>
        /// Registers both ThreadEnd and ThreadDCStop
        /// </summary>
        public event Action<ThreadTraceData> ThreadEndGroup
        {
            add
            {
                ThreadStop += value;
                ThreadDCStop += value;
            }
            remove
            {
                ThreadStop -= value;
                ThreadDCStop -= value;
            }
        }
        public event Action<ThreadTraceData> ThreadDCStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ThreadTraceData(value, 0xFFFF, 2, "Thread", ThreadTaskGuid, 3, "DCStart", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 3, ThreadTaskGuid);
            }
        }
        public event Action<ThreadTraceData> ThreadDCStop
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ThreadTraceData(value, 0xFFFF, 2, "Thread", ThreadTaskGuid, 4, "DCStop", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 4, ThreadTaskGuid);
            }
        }
        public event Action<ThreadSetNameTraceData> ThreadSetName
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ThreadSetNameTraceData(value, 0xFFFF, 2, "Thread", ThreadTaskGuid, 72, "SetName", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 72, ThreadTaskGuid);
            }
        }
        public event Action<CSwitchTraceData> ThreadCSwitch
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new CSwitchTraceData(value, 0xFFFF, 2, "Thread", ThreadTaskGuid, 36, "CSwitch", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 36, ThreadTaskGuid);
            }
        }
        public event Action<EmptyTraceData> ThreadCompCS
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new EmptyTraceData(value, 0xFFFF, 2, "Thread", ThreadTaskGuid, 37, "CompCS", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 37, ThreadTaskGuid);
            }
        }
        public event Action<EnqueueTraceData> ThreadEnqueue
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new EnqueueTraceData(value, 0xFFFF, 2, "Thread", ThreadTaskGuid, 62, "Enqueue", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 62, ThreadTaskGuid);
            }
        }
        public event Action<DequeueTraceData> ThreadDequeue
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new DequeueTraceData(value, 0xFFFF, 2, "Thread", ThreadTaskGuid, 63, "Dequeue", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 63, ThreadTaskGuid);
            }
        }
#if false 
        public event Action<WorkerThreadTraceData> ThreadWorkerThread
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new WorkerThreadTraceData(value, 0xFFFF, 2, "Thread", ThreadTaskGuid, 57, "WorkerThread", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 57, ThreadTaskGuid);
            }
        }
        public event Action<ReserveCreateTraceData> ThreadReserveCreate
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ReserveCreateTraceData(value, 0xFFFF, 2, "Thread", ThreadTaskGuid, 48, "ReserveCreate", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 48, ThreadTaskGuid);
            }
        }
        public event Action<ReserveDeleteTraceData> ThreadReserveDelete
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ReserveDeleteTraceData(value, 0xFFFF, 2, "Thread", ThreadTaskGuid, 49, "ReserveDelete", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 49, ThreadTaskGuid);
            }
        }
        public event Action<ReserveJoinThreadTraceData> ThreadReserveJoinThread
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ReserveJoinThreadTraceData(value, 0xFFFF, 2, "Thread", ThreadTaskGuid, 52, "ReserveJoinThread", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 52, ThreadTaskGuid);
            }
        }
        public event Action<ReserveDisjoinThreadTraceData> ThreadReserveDisjoinThread
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ReserveDisjoinThreadTraceData(value, 0xFFFF, 2, "Thread", ThreadTaskGuid, 53, "ReserveDisjoinThread", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 53, ThreadTaskGuid);
            }
        }
        public event Action<ReserveStateTraceData> ThreadReserveState
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ReserveStateTraceData(value, 0xFFFF, 2, "Thread", ThreadTaskGuid, 54, "ReserveState", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 54, ThreadTaskGuid);
            }
        }
        public event Action<ReserveBandwidthTraceData> ThreadReserveBandwidth
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ReserveBandwidthTraceData(value, 0xFFFF, 2, "Thread", ThreadTaskGuid, 55, "ReserveBandwidth", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 55, ThreadTaskGuid);
            }
        }
        public event Action<ReserveLateCountTraceData> ThreadReserveLateCount
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ReserveLateCountTraceData(value, 0xFFFF, 2, "Thread", ThreadTaskGuid, 56, "ReserveLateCount", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 56, ThreadTaskGuid);
            }
        }
#endif
        public event Action<DiskIOTraceData> DiskIORead
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new DiskIOTraceData(value, 0xFFFF, 3, "DiskIO", DiskIOTaskGuid, 10, "Read", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 10, DiskIOTaskGuid);
            }
        }
        public event Action<DiskIOTraceData> DiskIOWrite
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new DiskIOTraceData(value, 0xFFFF, 3, "DiskIO", DiskIOTaskGuid, 11, "Write", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 11, DiskIOTaskGuid);
            }
        }
        public event Action<DiskIOInitTraceData> DiskIOReadInit
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new DiskIOInitTraceData(value, 0xFFFF, 3, "DiskIO", DiskIOTaskGuid, 12, "ReadInit", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 12, DiskIOTaskGuid);
            }
        }
        public event Action<DiskIOInitTraceData> DiskIOWriteInit
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new DiskIOInitTraceData(value, 0xFFFF, 3, "DiskIO", DiskIOTaskGuid, 13, "WriteInit", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 13, DiskIOTaskGuid);
            }
        }
        public event Action<DiskIOInitTraceData> DiskIOFlushInit
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new DiskIOInitTraceData(value, 0xFFFF, 3, "DiskIO", DiskIOTaskGuid, 15, "FlushInit", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 15, DiskIOTaskGuid);
            }
        }
        public event Action<DiskIOFlushBuffersTraceData> DiskIOFlushBuffers
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new DiskIOFlushBuffersTraceData(value, 0xFFFF, 3, "DiskIO", DiskIOTaskGuid, 14, "FlushBuffers", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 14, DiskIOTaskGuid);
            }
        }
        public event Action<DriverMajorFunctionCallTraceData> DiskIODriverMajorFunctionCall
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new DriverMajorFunctionCallTraceData(value, 0xFFFF, 3, "DiskIO", DiskIOTaskGuid, 34, "DriverMajorFunctionCall", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 34, DiskIOTaskGuid);
            }
        }
        public event Action<DriverMajorFunctionReturnTraceData> DiskIODriverMajorFunctionReturn
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new DriverMajorFunctionReturnTraceData(value, 0xFFFF, 3, "DiskIO", DiskIOTaskGuid, 35, "DriverMajorFunctionReturn", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 35, DiskIOTaskGuid);
            }
        }
        public event Action<DriverCompletionRoutineTraceData> DiskIODriverCompletionRoutine
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new DriverCompletionRoutineTraceData(value, 0xFFFF, 3, "DiskIO", DiskIOTaskGuid, 37, "DriverCompletionRoutine", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 37, DiskIOTaskGuid);
            }
        }
        public event Action<DriverCompleteRequestTraceData> DiskIODriverCompleteRequest
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new DriverCompleteRequestTraceData(value, 0xFFFF, 3, "DiskIO", DiskIOTaskGuid, 52, "DriverCompleteRequest", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 52, DiskIOTaskGuid);
            }
        }
        public event Action<DriverCompleteRequestReturnTraceData> DiskIODriverCompleteRequestReturn
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new DriverCompleteRequestReturnTraceData(value, 0xFFFF, 3, "DiskIO", DiskIOTaskGuid, 53, "DriverCompleteRequestReturn", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 53, DiskIOTaskGuid);
            }
        }
        public event Action<RegistryTraceData> RegistryCreate
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new RegistryTraceData(value, 0xFFFF, 4, "Registry", RegistryTaskGuid, 10, "Create", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 10, RegistryTaskGuid);
            }
        }
        public event Action<RegistryTraceData> RegistryOpen
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new RegistryTraceData(value, 0xFFFF, 4, "Registry", RegistryTaskGuid, 11, "Open", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 11, RegistryTaskGuid);
            }
        }
        public event Action<RegistryTraceData> RegistryDelete
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new RegistryTraceData(value, 0xFFFF, 4, "Registry", RegistryTaskGuid, 12, "Delete", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 12, RegistryTaskGuid);
            }
        }
        public event Action<RegistryTraceData> RegistryQuery
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new RegistryTraceData(value, 0xFFFF, 4, "Registry", RegistryTaskGuid, 13, "Query", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 13, RegistryTaskGuid);
            }
        }
        public event Action<RegistryTraceData> RegistrySetValue
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new RegistryTraceData(value, 0xFFFF, 4, "Registry", RegistryTaskGuid, 14, "SetValue", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 14, RegistryTaskGuid);
            }
        }
        public event Action<RegistryTraceData> RegistryDeleteValue
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new RegistryTraceData(value, 0xFFFF, 4, "Registry", RegistryTaskGuid, 15, "DeleteValue", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 15, RegistryTaskGuid);
            }
        }
        public event Action<RegistryTraceData> RegistryQueryValue
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new RegistryTraceData(value, 0xFFFF, 4, "Registry", RegistryTaskGuid, 16, "QueryValue", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 16, RegistryTaskGuid);
            }
        }
        public event Action<RegistryTraceData> RegistryEnumerateKey
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new RegistryTraceData(value, 0xFFFF, 4, "Registry", RegistryTaskGuid, 17, "EnumerateKey", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 17, RegistryTaskGuid);
            }
        }
        public event Action<RegistryTraceData> RegistryEnumerateValueKey
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new RegistryTraceData(value, 0xFFFF, 4, "Registry", RegistryTaskGuid, 18, "EnumerateValueKey", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 18, RegistryTaskGuid);
            }
        }
        public event Action<RegistryTraceData> RegistryQueryMultipleValue
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new RegistryTraceData(value, 0xFFFF, 4, "Registry", RegistryTaskGuid, 19, "QueryMultipleValue", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 19, RegistryTaskGuid);
            }
        }
        public event Action<RegistryTraceData> RegistrySetInformation
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new RegistryTraceData(value, 0xFFFF, 4, "Registry", RegistryTaskGuid, 20, "SetInformation", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 20, RegistryTaskGuid);
            }
        }
        public event Action<RegistryTraceData> RegistryFlush
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new RegistryTraceData(value, 0xFFFF, 4, "Registry", RegistryTaskGuid, 21, "Flush", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 21, RegistryTaskGuid);
            }
        }
        public event Action<RegistryTraceData> RegistryKCBCreate
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new RegistryTraceData(value, 0xFFFF, 4, "Registry", RegistryTaskGuid, 22, "KCBCreate", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 22, RegistryTaskGuid);
            }
        }
        public event Action<RegistryTraceData> RegistryKCBDelete
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new RegistryTraceData(value, 0xFFFF, 4, "Registry", RegistryTaskGuid, 23, "KCBDelete", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 23, RegistryTaskGuid);
            }
        }
        public event Action<RegistryTraceData> RegistryKCBRundownBegin
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new RegistryTraceData(value, 0xFFFF, 4, "Registry", RegistryTaskGuid, 24, "KCBRundownBegin", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 24, RegistryTaskGuid);
            }
        }
        public event Action<RegistryTraceData> RegistryKCBRundownEnd
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new RegistryTraceData(value, 0xFFFF, 4, "Registry", RegistryTaskGuid, 25, "KCBRundownEnd", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 25, RegistryTaskGuid);
            }
        }
        public event Action<RegistryTraceData> RegistryVirtualize
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new RegistryTraceData(value, 0xFFFF, 4, "Registry", RegistryTaskGuid, 26, "Virtualize", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 26, RegistryTaskGuid);
            }
        }
        public event Action<RegistryTraceData> RegistryClose
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new RegistryTraceData(value, 0xFFFF, 4, "Registry", RegistryTaskGuid, 27, "Close", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 27, RegistryTaskGuid);
            }
        }
        public event Action<SplitIoInfoTraceData> SplitIoVolMgr
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new SplitIoInfoTraceData(value, 0xFFFF, 5, "SplitIo", SplitIoTaskGuid, 32, "VolMgr", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 32, SplitIoTaskGuid);
            }
        }
        public event Action<MapFileTraceData> FileIOMapFile
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new MapFileTraceData(value, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 37, "MapFile", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 39, FileIOTaskGuid);
            }
        }
        public event Action<MapFileTraceData> FileIOUnmapFile
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new MapFileTraceData(value, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 38, "UnmapFile", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 39, FileIOTaskGuid);
            }
        }
        public event Action<MapFileTraceData> FileIOMapFileDCStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new MapFileTraceData(value, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 39, "MapFileDCStart", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 39, FileIOTaskGuid);
            }
        }
        public event Action<MapFileTraceData> FileIOMapFileDCStop
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new MapFileTraceData(value, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 40, "MapFileDCStop", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 39, FileIOTaskGuid);
            }
        }
        public event Action<FileIONameTraceData> FileIOName
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new FileIONameTraceData(value, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 0, "Name", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 0, FileIOTaskGuid);
            }
        }
        public event Action<FileIONameTraceData> FileIOFileCreate
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new FileIONameTraceData(value, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 32, "FileCreate", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 32, FileIOTaskGuid);
            }
        }
        public event Action<FileIONameTraceData> FileIOFileDelete
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new FileIONameTraceData(value, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 35, "FileDelete", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 35, FileIOTaskGuid);
            }
        }
        public event Action<FileIONameTraceData> FileIOFileRundown
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new FileIONameTraceData(value, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 36, "FileRundown", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 36, FileIOTaskGuid);
            }
        }
        public event Action<FileIOCreateTraceData> FileIOCreate
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new FileIOCreateTraceData(value, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 64, "Create", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 64, FileIOTaskGuid);
            }
        }
        public event Action<FileIOSimpleOpTraceData> FileIOCleanup
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new FileIOSimpleOpTraceData(value, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 65, "Cleanup", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 65, FileIOTaskGuid);
            }
        }
        public event Action<FileIOSimpleOpTraceData> FileIOClose
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new FileIOSimpleOpTraceData(value, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 66, "Close", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 66, FileIOTaskGuid);
            }
        }
        public event Action<FileIOSimpleOpTraceData> FileIOFlush
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new FileIOSimpleOpTraceData(value, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 73, "Flush", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 73, FileIOTaskGuid);
            }
        }
        public event Action<FileIOReadWriteTraceData> FileIORead
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new FileIOReadWriteTraceData(value, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 67, "Read", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 67, FileIOTaskGuid);
            }
        }
        public event Action<FileIOReadWriteTraceData> FileIOWrite
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new FileIOReadWriteTraceData(value, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 68, "Write", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 68, FileIOTaskGuid);
            }
        }
        public event Action<FileIOInfoTraceData> FileIOSetInfo
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new FileIOInfoTraceData(value, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 69, "SetInfo", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 69, FileIOTaskGuid);
            }
        }
        public event Action<FileIOInfoTraceData> FileIODelete
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new FileIOInfoTraceData(value, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 70, "Delete", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 70, FileIOTaskGuid);
            }
        }
        public event Action<FileIOInfoTraceData> FileIORename
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new FileIOInfoTraceData(value, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 71, "Rename", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 71, FileIOTaskGuid);
            }
        }
        public event Action<FileIOInfoTraceData> FileIOQueryInfo
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new FileIOInfoTraceData(value, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 74, "QueryInfo", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 74, FileIOTaskGuid);
            }
        }
        public event Action<FileIOInfoTraceData> FileIOFSControl
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new FileIOInfoTraceData(value, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 75, "FSControl", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 75, FileIOTaskGuid);
            }
        }
        public event Action<FileIODirEnumTraceData> FileIODirEnum
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new FileIODirEnumTraceData(value, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 72, "DirEnum", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 72, FileIOTaskGuid);
            }
        }
        public event Action<FileIODirEnumTraceData> FileIODirNotify
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new FileIODirEnumTraceData(value, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 77, "DirNotify", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 77, FileIOTaskGuid);
            }
        }
        public event Action<FileIOOpEndTraceData> FileIOOperationEnd
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new FileIOOpEndTraceData(value, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 76, "OperationEnd", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 76, FileIOTaskGuid);
            }
        }
        public event Action<TcpIpSendTraceData> TcpIpSend
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new TcpIpSendTraceData(value, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 10, "Send", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 10, TcpIpTaskGuid);
            }
        }
        public event Action<TcpIpTraceData> TcpIpRecv
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new TcpIpTraceData(value, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 11, "Recv", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 11, TcpIpTaskGuid);
            }
        }
        public event Action<TcpIpConnectTraceData> TcpIpConnect
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new TcpIpConnectTraceData(value, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 12, "Connect", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 12, TcpIpTaskGuid);
            }
        }
        public event Action<TcpIpTraceData> TcpIpDisconnect
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new TcpIpTraceData(value, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 13, "Disconnect", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 13, TcpIpTaskGuid);
            }
        }
        public event Action<TcpIpTraceData> TcpIpRetransmit
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new TcpIpTraceData(value, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 14, "Retransmit", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 14, TcpIpTaskGuid);
            }
        }
        public event Action<TcpIpConnectTraceData> TcpIpAccept
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new TcpIpConnectTraceData(value, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 15, "Accept", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 15, TcpIpTaskGuid);
            }
        }
        public event Action<TcpIpTraceData> TcpIpReconnect
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new TcpIpTraceData(value, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 16, "Reconnect", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 16, TcpIpTaskGuid);
            }
        }
        public event Action<TcpIpFailTraceData> TcpIpFail
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new TcpIpFailTraceData(value, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 17, "Fail", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 17, TcpIpTaskGuid);
            }
        }
        public event Action<TcpIpTraceData> TcpIpTCPCopy
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new TcpIpTraceData(value, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 18, "TCPCopy", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 18, TcpIpTaskGuid);
            }
        }
        public event Action<TcpIpTraceData> TcpIpARPCopy
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new TcpIpTraceData(value, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 19, "ARPCopy", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 19, TcpIpTaskGuid);
            }
        }
        public event Action<TcpIpTraceData> TcpIpFullACK
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new TcpIpTraceData(value, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 20, "FullACK", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 20, TcpIpTaskGuid);
            }
        }
        public event Action<TcpIpTraceData> TcpIpPartACK
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new TcpIpTraceData(value, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 21, "PartACK", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 21, TcpIpTaskGuid);
            }
        }
        public event Action<TcpIpTraceData> TcpIpDupACK
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new TcpIpTraceData(value, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 22, "DupACK", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 22, TcpIpTaskGuid);
            }
        }
        public event Action<TcpIpV6SendTraceData> TcpIpSendIPV6
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new TcpIpV6SendTraceData(value, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 26, "SendIPV6", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 26, TcpIpTaskGuid);
            }
        }
        public event Action<TcpIpV6TraceData> TcpIpRecvIPV6
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new TcpIpV6TraceData(value, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 27, "RecvIPV6", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 27, TcpIpTaskGuid);
            }
        }
        public event Action<TcpIpV6TraceData> TcpIpDisconnectIPV6
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new TcpIpV6TraceData(value, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 29, "DisconnectIPV6", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 29, TcpIpTaskGuid);
            }
        }
        public event Action<TcpIpV6TraceData> TcpIpRetransmitIPV6
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new TcpIpV6TraceData(value, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 30, "RetransmitIPV6", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 30, TcpIpTaskGuid);
            }
        }
        public event Action<TcpIpV6TraceData> TcpIpReconnectIPV6
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new TcpIpV6TraceData(value, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 32, "ReconnectIPV6", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 32, TcpIpTaskGuid);
            }
        }
        public event Action<TcpIpV6TraceData> TcpIpTCPCopyIPV6
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new TcpIpV6TraceData(value, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 34, "TCPCopyIPV6", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 34, TcpIpTaskGuid);
            }
        }
        public event Action<TcpIpV6ConnectTraceData> TcpIpConnectIPV6
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new TcpIpV6ConnectTraceData(value, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 28, "ConnectIPV6", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 28, TcpIpTaskGuid);
            }
        }
        public event Action<TcpIpV6ConnectTraceData> TcpIpAcceptIPV6
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new TcpIpV6ConnectTraceData(value, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 31, "AcceptIPV6", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 31, TcpIpTaskGuid);
            }
        }
        public event Action<UdpIpTraceData> UdpIpSend
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new UdpIpTraceData(value, 0xFFFF, 8, "UdpIp", UdpIpTaskGuid, 10, "Send", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 10, UdpIpTaskGuid);
            }
        }
        public event Action<UdpIpTraceData> UdpIpRecv
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new UdpIpTraceData(value, 0xFFFF, 8, "UdpIp", UdpIpTaskGuid, 11, "Recv", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 11, UdpIpTaskGuid);
            }
        }
        public event Action<UdpIpFailTraceData> UdpIpFail
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new UdpIpFailTraceData(value, 0xFFFF, 8, "UdpIp", UdpIpTaskGuid, 17, "Fail", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 17, UdpIpTaskGuid);
            }
        }
        public event Action<UpdIpV6TraceData> UdpIpSendIPV6
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new UpdIpV6TraceData(value, 0xFFFF, 8, "UdpIp", UdpIpTaskGuid, 26, "SendIPV6", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 26, UdpIpTaskGuid);
            }
        }
        public event Action<UpdIpV6TraceData> UdpIpRecvIPV6
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new UpdIpV6TraceData(value, 0xFFFF, 8, "UdpIp", UdpIpTaskGuid, 27, "RecvIPV6", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 27, UdpIpTaskGuid);
            }
        }
        public event Action<ImageLoadTraceData> ImageGroup
        {
            add
            {
                ImageLoadGroup += value;
                ImageUnloadGroup += value;
            }
            remove
            {
                ImageLoadGroup -= value;
                ImageUnloadGroup -= value;
            }
        }
        public event Action<ImageLoadTraceData> ImageLoad
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ImageLoadTraceData(value, 0xFFFF, 9, "Image", ImageTaskGuid, 10, "Load", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 10, ImageTaskGuid);
            }
        }
        /// <summary>
        /// Registers both ImageLoad and ImageDCStart
        /// </summary>
        public event Action<ImageLoadTraceData> ImageLoadGroup
        {
            add
            {
                ImageLoad += value;
                ImageDCStart += value;
            }
            remove
            {
                ImageLoad -= value;
                ImageDCStart -= value;
            }
        }
        public event Action<ImageLoadTraceData> ImageUnload
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ImageLoadTraceData(value, 0xFFFF, 9, "Image", ImageTaskGuid, 2, "Unload", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 2, ImageTaskGuid);
            }
        }
        /// <summary>
        /// Registers both ImageUnload and ImageDCStop
        /// </summary>
        public event Action<ImageLoadTraceData> ImageUnloadGroup
        {
            add
            {
                ImageUnload += value;
                ImageDCStop += value;
            }
            remove
            {
                ImageUnload -= value;
                ImageDCStop -= value;
            }
        }
        public event Action<ImageLoadTraceData> ImageDCStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ImageLoadTraceData(value, 0xFFFF, 9, "Image", ImageTaskGuid, 3, "DCStart", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 3, ImageTaskGuid);
            }
        }
        public event Action<ImageLoadTraceData> ImageDCStop
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ImageLoadTraceData(value, 0xFFFF, 9, "Image", ImageTaskGuid, 4, "DCStop", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 4, ImageTaskGuid);
            }
        }
        public event Action<MemoryPageFaultTraceData> MemoryTransitionFault
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new MemoryPageFaultTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 10, "TransitionFault", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 10, MemoryTaskGuid);
            }
        }
        public event Action<MemoryPageFaultTraceData> MemoryDemandZeroFault
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new MemoryPageFaultTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 11, "DemandZeroFault", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 11, MemoryTaskGuid);
            }
        }
        public event Action<MemoryPageFaultTraceData> MemoryCopyOnWrite
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new MemoryPageFaultTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 12, "CopyOnWrite", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 12, MemoryTaskGuid);
            }
        }
        public event Action<MemoryPageFaultTraceData> MemoryGuardMemory
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new MemoryPageFaultTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 13, "GuardMemory", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 13, MemoryTaskGuid);
            }
        }
        public event Action<MemoryPageFaultTraceData> MemoryHardMemory
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new MemoryPageFaultTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 14, "HardMemory", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 14, MemoryTaskGuid);
            }
        }
        public event Action<MemoryPageFaultTraceData> MemoryAccessViolation
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new MemoryPageFaultTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 15, "AccessViolation", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 15, MemoryTaskGuid);
            }
        }
        public event Action<MemoryHardFaultTraceData> MemoryHardFault
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new MemoryHardFaultTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 32, "HardFault", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 32, MemoryTaskGuid);
            }
        }
        public event Action<MemoryHeapRangeRundownTraceData> MemoryHeapRangeRundown
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new MemoryHeapRangeRundownTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 100, "HeapRangeRundown", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 100, MemoryTaskGuid);
            }
        }
        public event Action<MemoryHeapRangeCreateTraceData> MemoryHeapRangeCreate
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new MemoryHeapRangeCreateTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 101, "HeapRangeCreate", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 101, MemoryTaskGuid);
            }
        }
        public event Action<MemoryHeapRangeTraceData> MemoryHeapRangeReserve
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new MemoryHeapRangeTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 102, "HeapRangeReserve", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 102, MemoryTaskGuid);
            }
        }
        public event Action<MemoryHeapRangeTraceData> MemoryHeapRangeRelease
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new MemoryHeapRangeTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 103, "HeapRangeRelease", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 103, MemoryTaskGuid);
            }
        }
        public event Action<MemoryHeapRangeDestroyTraceData> MemoryHeapRangeDestroy
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new MemoryHeapRangeDestroyTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 104, "HeapRangeDestroy", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 104, MemoryTaskGuid);
            }
        }
        public event Action<MemoryImageLoadBackedTraceData> MemoryImageLoadBacked
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new MemoryImageLoadBackedTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 105, "ImageLoadBacked", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 105, MemoryTaskGuid);
            }
        }

        // TODO FIX NOW, this easily may not be correct. 
        public event Action<MemoryPageAccessTraceData> MemoryInMemory
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new MemoryPageAccessTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 35, "InMemory", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 35, MemoryTaskGuid);
            }
        }

        public event Action<MemorySystemMemInfoTraceData> MemorySystemMemInfo
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new MemorySystemMemInfoTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 112, "SystemMemInfo", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 112, MemoryTaskGuid);
            }
        }
        // TODO FIX NOW, this easily may not be correct. 
        public event Action<MemoryPageAccessTraceData> MemoryInMemoryActive
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new MemoryPageAccessTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 117, "InMemoryActive", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 117, MemoryTaskGuid);
            }
        }

        public event Action<MemoryPageAccessTraceData> MemoryPageAccess
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new MemoryPageAccessTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 118, "PageAccess", ProviderGuid, ProviderName, State));

                // This is the EX version of the event.  
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new MemoryPageAccessTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 130, "PageAccess", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 118, MemoryTaskGuid);  // PageAccess
                source.UnregisterEventTemplate(value, 130, MemoryTaskGuid);  // Page AccessEx
            }
        }
        public event Action<MemoryProcessMemInfoTraceData> MemoryProcessMemInfo
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new MemoryProcessMemInfoTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 125, "ProcessMemInfo", ProviderGuid, ProviderName, State));
                // This event is in the kernel provider as well as in the Microsoft-Windows-Kernel-Memory provider
                source.RegisterEventTemplate(new MemoryProcessMemInfoTraceData(value, 2, 2, "MemoryProcessMemInfo", Guid.Empty, 0, "", MemoryProviderGuid, "Microsoft-Windows-Kernel-Memory", State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 125, MemoryTaskGuid);
                source.UnregisterEventTemplate(value, 2, MemoryProviderGuid);
            }
        }

        /// <summary>
        /// Rasied every 0.5s with memory metrics of the current machine.
        /// </summary>
        public event Action<MemInfoTraceData> MemoryMemInfo
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new MemInfoTraceData(value, 1, 1, "MemoryMemInfo", Guid.Empty, 0, "", MemoryProviderGuid, "Microsoft-Windows-Kernel-Memory", State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 1, MemoryProviderGuid);
            }
        }

        // TODO Added by hand without proper body decode.  
        public event Action<EmptyTraceData> MemoryPFMappedSectionCreate
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new EmptyTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 73, "PFMappedSectionCreate", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 73, MemoryTaskGuid);
            }
        }
        public event Action<EmptyTraceData> MemorySessionDCStop
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new EmptyTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 76, "SessionDCStop", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 76, MemoryTaskGuid);
            }
        }
        public event Action<EmptyTraceData> MemoryPFMappedSectionDelete
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new EmptyTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 79, "PFMappedSectionDelete", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 79, MemoryTaskGuid);
            }
        }
        public event Action<EmptyTraceData> MemoryInMemoryStoreFault
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new EmptyTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 115, "InMemoryStoreFault", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 115, MemoryTaskGuid);
            }
        }
        public event Action<EmptyTraceData> MemoryPageRelease
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new EmptyTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 119, "PageRelease", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 119, MemoryTaskGuid);
            }
        }
        public event Action<EmptyTraceData> MemoryRangeAccess
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new EmptyTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 120, "RangeAccess", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 120, MemoryTaskGuid);
            }
        }
        public event Action<EmptyTraceData> MemoryRangeRelease
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new EmptyTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 121, "RangeRelease", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 121, MemoryTaskGuid);
            }
        }

        public event Action<EmptyTraceData> MemoryCombine
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new EmptyTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 122, "Combine", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 122, MemoryTaskGuid);
            }
        }
        public event Action<EmptyTraceData> MemoryKernelMemUsage
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new EmptyTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 123, "KernelMemUsage", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 123, MemoryTaskGuid);
            }
        }
        public event Action<EmptyTraceData> MemoryMMStats
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new EmptyTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 124, "MMStats", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 124, MemoryTaskGuid);
            }
        }
        public event Action<EmptyTraceData> MemoryMemInfoSessionWS
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new EmptyTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 126, "MemInfoSessionWS", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 126, MemoryTaskGuid);
            }
        }
        public event Action<EmptyTraceData> MemoryVirtualRotate
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new EmptyTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 127, "VirtualRotate", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 127, MemoryTaskGuid);
            }
        }
        public event Action<EmptyTraceData> MemoryVirtualAllocDCStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new EmptyTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 128, "VirtualAllocDCStart", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 128, MemoryTaskGuid);
            }
        }
        public event Action<EmptyTraceData> MemoryVirtualAllocDCStop
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new EmptyTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 129, "VirtualAllocDCStop", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 129, MemoryTaskGuid);
            }
        }
        public event Action<EmptyTraceData> MemoryRemoveFromWS
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new EmptyTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 131, "RemoveFromWS", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 131, MemoryTaskGuid);
            }
        }
        public event Action<EmptyTraceData> MemoryWSSharableRundown
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new EmptyTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 132, "WSSharableRundown", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 132, MemoryTaskGuid);
            }
        }
        public event Action<EmptyTraceData> MemoryInMemoryActiveRundown
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new EmptyTraceData(value, 0xFFFF, 10, "Memory", MemoryTaskGuid, 133, "InMemoryActiveRundown", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 133, MemoryTaskGuid);
            }
        }

        public event Action<SampledProfileTraceData> PerfInfoSample
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new SampledProfileTraceData(value, 0xFFFF, 11, "PerfInfo", PerfInfoTaskGuid, 46, "Sample", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 46, PerfInfoTaskGuid);
            }
        }
        public event Action<PMCCounterProfTraceData> PerfInfoPMCSample
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new PMCCounterProfTraceData(value, 0xFFFF, 11, "PerfInfo", PerfInfoTaskGuid, 47, "PMCSample", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 47, PerfInfoTaskGuid);
            }
        }
#if false       // TODO FIX NOW remove (it is not used and is not following conventions on array fields.   
        public event Action<BatchedSampledProfileTraceData> PerfInfoBatchedSample
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new BatchedSampledProfileTraceData(value, 0xFFFF, 11, "PerfInfo", PerfInfoTaskGuid, 55, "BatchedSample", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 55, PerfInfoTaskGuid);
            }
        }
#endif
        public event Action<SampledProfileIntervalTraceData> PerfInfoSetInterval
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new SampledProfileIntervalTraceData(value, 0xFFFF, 11, "PerfInfo", PerfInfoTaskGuid, 72, "SetInterval", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 72, PerfInfoTaskGuid);
            }
        }
        public event Action<SampledProfileIntervalTraceData> PerfInfoCollectionStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new SampledProfileIntervalTraceData(value, 0xFFFF, 11, "PerfInfo", PerfInfoTaskGuid, 73, "CollectionStart", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 73, PerfInfoTaskGuid);
            }
        }
        public event Action<SampledProfileIntervalTraceData> PerfInfoCollectionEnd
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new SampledProfileIntervalTraceData(value, 0xFFFF, 11, "PerfInfo", PerfInfoTaskGuid, 74, "CollectionEnd", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 74, PerfInfoTaskGuid);
            }
        }
        public event Action<SysCallEnterTraceData> PerfInfoSysClEnter
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new SysCallEnterTraceData(value, 0xFFFF, 11, "PerfInfo", PerfInfoTaskGuid, 51, "SysClEnter", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 51, PerfInfoTaskGuid);
            }
        }
        public event Action<SysCallExitTraceData> PerfInfoSysClExit
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new SysCallExitTraceData(value, 0xFFFF, 11, "PerfInfo", PerfInfoTaskGuid, 52, "SysClExit", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 52, PerfInfoTaskGuid);
            }
        }
        public event Action<ISRTraceData> PerfInfoISR
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ISRTraceData(value, 0xFFFF, 11, "PerfInfo", PerfInfoTaskGuid, 67, "ISR", ProviderGuid, ProviderName, State));
                source.RegisterEventTemplate(new ISRTraceData(value, 0xFFFF, 11, "PerfInfo", PerfInfoTaskGuid, 50, "ISR", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 67, PerfInfoTaskGuid);
            }
        }
        public event Action<DPCTraceData> PerfInfoThreadedDPC
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new DPCTraceData(value, 0xFFFF, 11, "PerfInfo", PerfInfoTaskGuid, 66, "ThreadedDPC", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 66, PerfInfoTaskGuid);
            }
        }
        public event Action<DPCTraceData> PerfInfoDPC
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new DPCTraceData(value, 0xFFFF, 11, "PerfInfo", PerfInfoTaskGuid, 68, "DPC", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 68, PerfInfoTaskGuid);
            }
        }
        public event Action<DPCTraceData> PerfInfoTimerDPC
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new DPCTraceData(value, 0xFFFF, 11, "PerfInfo", PerfInfoTaskGuid, 69, "TimerDPC", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 69, PerfInfoTaskGuid);
            }
        }
        public event Action<EmptyTraceData> PerfInfoDebuggerEnabled
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new EmptyTraceData(value, 0xFFFF, 11, "PerfInfo", PerfInfoTaskGuid, 58, "DebuggerEnabled", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 58, PerfInfoTaskGuid);
            }
        }
        public event Action<StackWalkStackTraceData> StackWalkStack
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new StackWalkStackTraceData(value, 0xFFFF, 12, "StackWalk", StackWalkTaskGuid, 32, "Stack", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 32, StackWalkTaskGuid);
            }
        }

        public event Action<StackWalkRefTraceData> StackWalkStackKeyKernel
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new StackWalkRefTraceData(value, 0xFFFF, 12, "StackWalk", StackWalkTaskGuid, 37, "StackKeyKernel", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 37, StackWalkTaskGuid);
            }
        }
        public event Action<StackWalkRefTraceData> StackWalkStackKeyUser
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new StackWalkRefTraceData(value, 0xFFFF, 12, "StackWalk", StackWalkTaskGuid, 38, "StackKeyUser", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 38, StackWalkTaskGuid);
            }
        }
        public event Action<StackWalkDefTraceData> StackWalkKeyDelete
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new StackWalkDefTraceData(value, 0xFFFF, 12, "StackWalk", StackWalkTaskGuid, 35, "KeyDelete", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 35, StackWalkTaskGuid);
            }
        }
        public event Action<StackWalkDefTraceData> StackWalkKeyRundown
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new StackWalkDefTraceData(value, 0xFFFF, 12, "StackWalk", StackWalkTaskGuid, 36, "KeyRundown", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 36, StackWalkTaskGuid);
            }
        }

        public event Action<ALPCSendMessageTraceData> ALPCSendMessage
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ALPCSendMessageTraceData(value, 0xFFFF, 13, "ALPC", ALPCTaskGuid, 33, "SendMessage", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 33, ALPCTaskGuid);
            }
        }
        public event Action<ALPCReceiveMessageTraceData> ALPCReceiveMessage
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ALPCReceiveMessageTraceData(value, 0xFFFF, 13, "ALPC", ALPCTaskGuid, 34, "ReceiveMessage", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 34, ALPCTaskGuid);
            }
        }
        public event Action<ALPCWaitForReplyTraceData> ALPCWaitForReply
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ALPCWaitForReplyTraceData(value, 0xFFFF, 13, "ALPC", ALPCTaskGuid, 35, "WaitForReply", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 35, ALPCTaskGuid);
            }
        }
        public event Action<ALPCWaitForNewMessageTraceData> ALPCWaitForNewMessage
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ALPCWaitForNewMessageTraceData(value, 0xFFFF, 13, "ALPC", ALPCTaskGuid, 36, "WaitForNewMessage", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 36, ALPCTaskGuid);
            }
        }
        public event Action<ALPCUnwaitTraceData> ALPCUnwait
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ALPCUnwaitTraceData(value, 0xFFFF, 13, "ALPC", ALPCTaskGuid, 37, "Unwait", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 37, ALPCTaskGuid);
            }
        }
        public event Action<EmptyTraceData> LostEvent
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName

                source.RegisterEventTemplate(new EmptyTraceData(value, 0xFFFF, 14, "LostEvent", LostEventTaskGuid, 32, "", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 32, LostEventTaskGuid);
            }
        }
        public event Action<SystemConfigCPUTraceData> SystemConfigCPU
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new SystemConfigCPUTraceData(value, 0xFFFF, 15, "SystemConfig", SystemConfigTaskGuid, 10, "CPU", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 10, SystemConfigTaskGuid);
            }
        }
        public event Action<SystemConfigPhyDiskTraceData> SystemConfigPhyDisk
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new SystemConfigPhyDiskTraceData(value, 0xFFFF, 15, "SystemConfig", SystemConfigTaskGuid, 11, "PhyDisk", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 11, SystemConfigTaskGuid);
            }
        }
        public event Action<SystemConfigLogDiskTraceData> SystemConfigLogDisk
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new SystemConfigLogDiskTraceData(value, 0xFFFF, 15, "SystemConfig", SystemConfigTaskGuid, 12, "LogDisk", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 12, SystemConfigTaskGuid);
            }
        }
        public event Action<SystemConfigNICTraceData> SystemConfigNIC
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new SystemConfigNICTraceData(value, 0xFFFF, 15, "SystemConfig", SystemConfigTaskGuid, 13, "NIC", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 13, SystemConfigTaskGuid);
            }
        }
        public event Action<SystemConfigVideoTraceData> SystemConfigVideo
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new SystemConfigVideoTraceData(value, 0xFFFF, 15, "SystemConfig", SystemConfigTaskGuid, 14, "Video", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 14, SystemConfigTaskGuid);
            }
        }
        public event Action<SystemConfigServicesTraceData> SystemConfigServices
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new SystemConfigServicesTraceData(value, 0xFFFF, 15, "SystemConfig", SystemConfigTaskGuid, 15, "Services", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 15, SystemConfigTaskGuid);
            }
        }
        public event Action<SystemConfigPowerTraceData> SystemConfigPower
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new SystemConfigPowerTraceData(value, 0xFFFF, 15, "SystemConfig", SystemConfigTaskGuid, 16, "Power", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 16, SystemConfigTaskGuid);
            }
        }
        public event Action<SystemConfigIRQTraceData> SystemConfigIRQ
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new SystemConfigIRQTraceData(value, 0xFFFF, 15, "SystemConfig", SystemConfigTaskGuid, 21, "IRQ", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 21, SystemConfigTaskGuid);
            }
        }
        public event Action<SystemConfigPnPTraceData> SystemConfigPnP
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new SystemConfigPnPTraceData(value, 0xFFFF, 15, "SystemConfig", SystemConfigTaskGuid, 22, "PnP", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 22, SystemConfigTaskGuid);
            }
        }
        public event Action<SystemConfigNetworkTraceData> SystemConfigNetwork
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new SystemConfigNetworkTraceData(value, 0xFFFF, 15, "SystemConfig", SystemConfigTaskGuid, 17, "Network", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 17, SystemConfigTaskGuid);
            }
        }
        public event Action<SystemConfigIDEChannelTraceData> SystemConfigIDEChannel
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new SystemConfigIDEChannelTraceData(value, 0xFFFF, 15, "SystemConfig", SystemConfigTaskGuid, 23, "IDEChannel", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 23, SystemConfigTaskGuid);
            }
        }
        // Added by hand. 
        public event Action<VirtualAllocTraceData> VirtualMemAlloc
        {
            add
            {
                source.RegisterEventTemplate(new VirtualAllocTraceData(value, 0xFFFF, 0, "VirtualMem", VirtualAllocTaskGuid, 98, "Alloc", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 98, VirtualAllocTaskGuid);
            }
        }
        public event Action<VirtualAllocTraceData> VirtualMemFree
        {
            add
            {
                source.RegisterEventTemplate(new VirtualAllocTraceData(value, 0xFFFF, 0, "VirtualMem", VirtualAllocTaskGuid, 99, "Free", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 99, VirtualAllocTaskGuid);
            }
        }
        public event Action<DispatcherReadyThreadTraceData> DispatcherReadyThread
        {
            add
            {
                source.RegisterEventTemplate(new DispatcherReadyThreadTraceData(value, 0xFFFF, 0, "Dispatcher", ReadyThreadTaskGuid, 50, "ReadyThread", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 50, ReadyThreadTaskGuid);
            }
        }

        public event Action<ObjectHandleTraceData> ObjectCreateHandle
        {
            add
            {
                source.RegisterEventTemplate(new ObjectHandleTraceData(null, 0xFFFF, 0, "Object", ObjectTaskGuid, 32, "CreateHandle", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 32, ObjectTaskGuid);
            }
        }
        public event Action<ObjectHandleTraceData> ObjectCloseHandle
        {
            add
            {
                source.RegisterEventTemplate(new ObjectHandleTraceData(null, 0xFFFF, 0, "Object", ObjectTaskGuid, 33, "CloseHandle", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 33, ObjectTaskGuid);
            }
        }
        public event Action<ObjectDuplicateHandleTraceData> ObjectDuplicateHandle
        {
            add
            {
                source.RegisterEventTemplate(new ObjectHandleTraceData(null, 0xFFFF, 0, "Object", ObjectTaskGuid, 34, "DuplicateHandle", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 34, ObjectTaskGuid);
            }
        }
        public event Action<ObjectNameTraceData> ObjectHandleDCEnd
        {
            add
            {
                source.RegisterEventTemplate(new ObjectNameTraceData(null, 0xFFFF, 0, "Object", ObjectTaskGuid, 39, "HandleDCEnd", ProviderGuid, ProviderName, null));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 39, ObjectTaskGuid);
            }
        }
        public event Action<ObjectTypeNameTraceData> ObjectTypeDCEnd
        {
            add
            {
                source.RegisterEventTemplate(new ObjectTypeNameTraceData(null, 0xFFFF, 0, "Object", ObjectTaskGuid, 37, "TypeDCEnd", ProviderGuid, ProviderName, null));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 37, ObjectTaskGuid);
            }
        }

        public event Action<StringTraceData> PerfInfoMark
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new StringTraceData(value, 0xFFFF, 0, "PerfInfo", PerfInfoTaskGuid, 34, "Mark", ProviderGuid, ProviderName, false));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 34, PerfInfoTaskGuid);
            }
        }

        public event Action<BuildInfoTraceData> SysConfigBuildInfo
        {
            add
            {
                source.RegisterEventTemplate(new BuildInfoTraceData(value, 0xFFFF, 0, "SysConfig", SysConfigTaskGuid, 32, "BuildInfo", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 32, SysConfigTaskGuid);
            }
        }

        /// <summary>
        /// File names in ETW are the Kernel names, which need to be mapped to the drive specification users see. 
        /// This event indicates this mapping. 
        /// </summary>
        public event Action<VolumeMappingTraceData> SysConfigVolumeMapping
        {
            add
            {
                source.RegisterEventTemplate(new VolumeMappingTraceData(value, 0xFFFF, 0, "SysConfig", SysConfigTaskGuid, 35, "VolumeMapping", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 35, SysConfigTaskGuid);
            }

        }
        public event Action<StringTraceData> SysConfigUnknownVolume
        {
            add
            {
                source.RegisterEventTemplate(new StringTraceData(value, 0xFFFF, 0, "SysConfig", SysConfigTaskGuid, 34, "UnknownVolume", ProviderGuid, ProviderName, true));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 34, SysConfigTaskGuid);
            }

        }
        public event Action<SystemPathsTraceData> SysConfigSystemPaths
        {
            add
            {
                source.RegisterEventTemplate(new SystemPathsTraceData(value, 0xFFFF, 0, "SysConfig", SysConfigTaskGuid, 33, "SystemPaths", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 33, SysConfigTaskGuid);
            }

        }

        #region private
        protected override string GetProviderName() { return ProviderName; }
        static private volatile TraceEvent[] s_templates;
        protected internal override void EnumerateTemplates(Func<string, string, EventFilterResponse> eventsToObserve, Action<TraceEvent> callback)
        {
            if (s_templates == null)
            {
                var templates = new TraceEvent[194];
                templates[0] = new EventTraceHeaderTraceData(null, 0xFFFF, 0, "EventTrace", EventTraceTaskGuid, 0, "Header", ProviderGuid, ProviderName, null);
                templates[1] = new HeaderExtensionTraceData(null, 0xFFFF, 0, "EventTrace", EventTraceTaskGuid, 5, "Extension", ProviderGuid, ProviderName, null);
                templates[2] = new HeaderExtensionTraceData(null, 0xFFFF, 0, "EventTrace", EventTraceTaskGuid, 32, "EndExtension", ProviderGuid, ProviderName, null);
                templates[3] = new EmptyTraceData(null, 0xFFFF, 0, "EventTrace", EventTraceTaskGuid, 8, "RundownComplete", ProviderGuid, ProviderName);
                templates[4] = new ProcessTraceData(null, 0xFFFF, 1, "Process", ProcessTaskGuid, 1, "Start", ProviderGuid, ProviderName, null);
                templates[5] = new ProcessTraceData(null, 0xFFFF, 1, "Process", ProcessTaskGuid, 2, "Stop", ProviderGuid, ProviderName, null);
                templates[6] = new ProcessTraceData(null, 0xFFFF, 1, "Process", ProcessTaskGuid, 3, "DCStart", ProviderGuid, ProviderName, null);
                templates[7] = new ProcessTraceData(null, 0xFFFF, 1, "Process", ProcessTaskGuid, 4, "DCStop", ProviderGuid, ProviderName, null);
                templates[8] = new ProcessTraceData(null, 0xFFFF, 1, "Process", ProcessTaskGuid, 39, "Defunct", ProviderGuid, ProviderName, null);
                templates[9] = new ProcessCtrTraceData(null, 0xFFFF, 1, "Process", ProcessTaskGuid, 32, "PerfCtr", ProviderGuid, ProviderName, null);
                templates[10] = new ProcessCtrTraceData(null, 0xFFFF, 1, "Process", ProcessTaskGuid, 33, "PerfCtrRundown", ProviderGuid, ProviderName, null);
                templates[11] = new ThreadTraceData(null, 0xFFFF, 2, "Thread", ThreadTaskGuid, 1, "Start", ProviderGuid, ProviderName, null);
                templates[12] = new ThreadTraceData(null, 0xFFFF, 2, "Thread", ThreadTaskGuid, 2, "Stop", ProviderGuid, ProviderName, null);
                templates[13] = new ThreadTraceData(null, 0xFFFF, 2, "Thread", ThreadTaskGuid, 3, "DCStart", ProviderGuid, ProviderName, null);
                templates[14] = new ThreadTraceData(null, 0xFFFF, 2, "Thread", ThreadTaskGuid, 4, "DCStop", ProviderGuid, ProviderName, null);
                templates[15] = new CSwitchTraceData(null, 0xFFFF, 2, "Thread", ThreadTaskGuid, 36, "CSwitch", ProviderGuid, ProviderName, null);
                templates[16] = new EmptyTraceData(null, 0xFFFF, 2, "Thread", ThreadTaskGuid, 37, "CompCS", ProviderGuid, ProviderName);
                templates[17] = new EnqueueTraceData(null, 0xFFFF, 2, "Thread", ThreadTaskGuid, 62, "Enqueue", ProviderGuid, ProviderName, null);
                templates[18] = new DequeueTraceData(null, 0xFFFF, 2, "Thread", ThreadTaskGuid, 63, "Dequeue", ProviderGuid, ProviderName, null);
#if false 
                templates[17] = new WorkerThreadTraceData(null, 0xFFFF, 2, "Thread", ThreadTaskGuid, 57, "WorkerThread", ProviderGuid, ProviderName, null);
                templates[18] = new ReserveCreateTraceData(null, 0xFFFF, 2, "Thread", ThreadTaskGuid, 48, "ReserveCreate", ProviderGuid, ProviderName, null);
                templates[19] = new ReserveDeleteTraceData(null, 0xFFFF, 2, "Thread", ThreadTaskGuid, 49, "ReserveDelete", ProviderGuid, ProviderName, null);
                templates[20] = new ReserveJoinThreadTraceData(null, 0xFFFF, 2, "Thread", ThreadTaskGuid, 52, "ReserveJoinThread", ProviderGuid, ProviderName, null);
                templates[21] = new ReserveDisjoinThreadTraceData(null, 0xFFFF, 2, "Thread", ThreadTaskGuid, 53, "ReserveDisjoinThread", ProviderGuid, ProviderName, null);
                templates[22] = new ReserveStateTraceData(null, 0xFFFF, 2, "Thread", ThreadTaskGuid, 54, "ReserveState", ProviderGuid, ProviderName, null);
                templates[23] = new ReserveBandwidthTraceData(null, 0xFFFF, 2, "Thread", ThreadTaskGuid, 55, "ReserveBandwidth", ProviderGuid, ProviderName, null);
                templates[24] = new ReserveLateCountTraceData(null, 0xFFFF, 2, "Thread", ThreadTaskGuid, 56, "ReserveLateCount", ProviderGuid, ProviderName, null);
#endif
                templates[25] = new DiskIOTraceData(null, 0xFFFF, 3, "DiskIO", DiskIOTaskGuid, 10, "Read", ProviderGuid, ProviderName, null);
                templates[26] = new DiskIOTraceData(null, 0xFFFF, 3, "DiskIO", DiskIOTaskGuid, 11, "Write", ProviderGuid, ProviderName, null);
                templates[27] = new DiskIOInitTraceData(null, 0xFFFF, 3, "DiskIO", DiskIOTaskGuid, 12, "ReadInit", ProviderGuid, ProviderName, null);
                templates[28] = new DiskIOInitTraceData(null, 0xFFFF, 3, "DiskIO", DiskIOTaskGuid, 13, "WriteInit", ProviderGuid, ProviderName, null);
                templates[29] = new DiskIOInitTraceData(null, 0xFFFF, 3, "DiskIO", DiskIOTaskGuid, 15, "FlushInit", ProviderGuid, ProviderName, null);
                templates[30] = new DiskIOFlushBuffersTraceData(null, 0xFFFF, 3, "DiskIO", DiskIOTaskGuid, 14, "FlushBuffers", ProviderGuid, ProviderName, null);
                templates[31] = new DriverMajorFunctionCallTraceData(null, 0xFFFF, 3, "DiskIO", DiskIOTaskGuid, 34, "DriverMajorFunctionCall", ProviderGuid, ProviderName, null);
                templates[32] = new DriverMajorFunctionReturnTraceData(null, 0xFFFF, 3, "DiskIO", DiskIOTaskGuid, 35, "DriverMajorFunctionReturn", ProviderGuid, ProviderName, null);
                templates[33] = new DriverCompletionRoutineTraceData(null, 0xFFFF, 3, "DiskIO", DiskIOTaskGuid, 37, "DriverCompletionRoutine", ProviderGuid, ProviderName, null);
                templates[34] = new DriverCompleteRequestTraceData(null, 0xFFFF, 3, "DiskIO", DiskIOTaskGuid, 52, "DriverCompleteRequest", ProviderGuid, ProviderName, null);
                templates[35] = new DriverCompleteRequestReturnTraceData(null, 0xFFFF, 3, "DiskIO", DiskIOTaskGuid, 53, "DriverCompleteRequestReturn", ProviderGuid, ProviderName, null);
                templates[36] = new RegistryTraceData(null, 0xFFFF, 4, "Registry", RegistryTaskGuid, 10, "Create", ProviderGuid, ProviderName, null);
                templates[37] = new RegistryTraceData(null, 0xFFFF, 4, "Registry", RegistryTaskGuid, 11, "Open", ProviderGuid, ProviderName, null);
                templates[38] = new RegistryTraceData(null, 0xFFFF, 4, "Registry", RegistryTaskGuid, 12, "Delete", ProviderGuid, ProviderName, null);
                templates[39] = new RegistryTraceData(null, 0xFFFF, 4, "Registry", RegistryTaskGuid, 13, "Query", ProviderGuid, ProviderName, null);
                templates[40] = new RegistryTraceData(null, 0xFFFF, 4, "Registry", RegistryTaskGuid, 14, "SetValue", ProviderGuid, ProviderName, null);
                templates[41] = new RegistryTraceData(null, 0xFFFF, 4, "Registry", RegistryTaskGuid, 15, "DeleteValue", ProviderGuid, ProviderName, null);
                templates[42] = new RegistryTraceData(null, 0xFFFF, 4, "Registry", RegistryTaskGuid, 16, "QueryValue", ProviderGuid, ProviderName, null);
                templates[43] = new RegistryTraceData(null, 0xFFFF, 4, "Registry", RegistryTaskGuid, 17, "EnumerateKey", ProviderGuid, ProviderName, null);
                templates[44] = new RegistryTraceData(null, 0xFFFF, 4, "Registry", RegistryTaskGuid, 18, "EnumerateValueKey", ProviderGuid, ProviderName, null);
                templates[45] = new RegistryTraceData(null, 0xFFFF, 4, "Registry", RegistryTaskGuid, 19, "QueryMultipleValue", ProviderGuid, ProviderName, null);
                templates[46] = new RegistryTraceData(null, 0xFFFF, 4, "Registry", RegistryTaskGuid, 20, "SetInformation", ProviderGuid, ProviderName, null);
                templates[47] = new RegistryTraceData(null, 0xFFFF, 4, "Registry", RegistryTaskGuid, 21, "Flush", ProviderGuid, ProviderName, null);
                templates[48] = new RegistryTraceData(null, 0xFFFF, 4, "Registry", RegistryTaskGuid, 22, "KCBCreate", ProviderGuid, ProviderName, null);
                templates[49] = new RegistryTraceData(null, 0xFFFF, 4, "Registry", RegistryTaskGuid, 23, "KCBDelete", ProviderGuid, ProviderName, null);
                templates[50] = new RegistryTraceData(null, 0xFFFF, 4, "Registry", RegistryTaskGuid, 24, "KCBRundownBegin", ProviderGuid, ProviderName, null);
                templates[51] = new RegistryTraceData(null, 0xFFFF, 4, "Registry", RegistryTaskGuid, 25, "KCBRundownEnd", ProviderGuid, ProviderName, null);
                templates[52] = new RegistryTraceData(null, 0xFFFF, 4, "Registry", RegistryTaskGuid, 26, "Virtualize", ProviderGuid, ProviderName, null);
                templates[53] = new RegistryTraceData(null, 0xFFFF, 4, "Registry", RegistryTaskGuid, 27, "Close", ProviderGuid, ProviderName, null);
                templates[54] = new SplitIoInfoTraceData(null, 0xFFFF, 5, "SplitIo", SplitIoTaskGuid, 32, "VolMgr", ProviderGuid, ProviderName, null);
                templates[55] = new MapFileTraceData(null, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 37, "MapFile", ProviderGuid, ProviderName, null);
                templates[56] = new MapFileTraceData(null, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 38, "UnmapFile", ProviderGuid, ProviderName, null);
                templates[57] = new MapFileTraceData(null, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 39, "MapFileDCStart", ProviderGuid, ProviderName, null);
                templates[58] = new MapFileTraceData(null, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 40, "MapFileDCStop", ProviderGuid, ProviderName, null);
                templates[59] = new FileIONameTraceData(null, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 0, "Name", ProviderGuid, ProviderName, null);
                templates[60] = new FileIONameTraceData(null, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 32, "FileCreate", ProviderGuid, ProviderName, null);
                templates[61] = new FileIONameTraceData(null, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 35, "FileDelete", ProviderGuid, ProviderName, null);
                templates[62] = new FileIONameTraceData(null, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 36, "FileRundown", ProviderGuid, ProviderName, null);
                templates[63] = new FileIOCreateTraceData(null, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 64, "Create", ProviderGuid, ProviderName, null);
                templates[64] = new FileIOSimpleOpTraceData(null, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 65, "Cleanup", ProviderGuid, ProviderName, null);
                templates[65] = new FileIOSimpleOpTraceData(null, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 66, "Close", ProviderGuid, ProviderName, null);
                templates[66] = new FileIOSimpleOpTraceData(null, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 73, "Flush", ProviderGuid, ProviderName, null);
                templates[67] = new FileIOReadWriteTraceData(null, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 67, "Read", ProviderGuid, ProviderName, null);
                templates[68] = new FileIOReadWriteTraceData(null, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 68, "Write", ProviderGuid, ProviderName, null);
                templates[69] = new FileIOInfoTraceData(null, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 69, "SetInfo", ProviderGuid, ProviderName, null);
                templates[70] = new FileIOInfoTraceData(null, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 70, "Delete", ProviderGuid, ProviderName, null);
                templates[71] = new FileIOInfoTraceData(null, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 71, "Rename", ProviderGuid, ProviderName, null);
                templates[72] = new FileIOInfoTraceData(null, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 74, "QueryInfo", ProviderGuid, ProviderName, null);
                templates[73] = new FileIOInfoTraceData(null, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 75, "FSControl", ProviderGuid, ProviderName, null);
                templates[74] = new FileIODirEnumTraceData(null, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 72, "DirEnum", ProviderGuid, ProviderName, null);
                templates[75] = new FileIODirEnumTraceData(null, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 77, "DirNotify", ProviderGuid, ProviderName, null);
                templates[76] = new FileIOOpEndTraceData(null, 0xFFFF, 6, "FileIO", FileIOTaskGuid, 76, "OperationEnd", ProviderGuid, ProviderName, null);
                templates[77] = new TcpIpSendTraceData(null, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 10, "Send", ProviderGuid, ProviderName, null);
                templates[78] = new TcpIpTraceData(null, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 11, "Recv", ProviderGuid, ProviderName, null);
                templates[79] = new TcpIpConnectTraceData(null, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 12, "Connect", ProviderGuid, ProviderName, null);
                templates[80] = new TcpIpTraceData(null, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 13, "Disconnect", ProviderGuid, ProviderName, null);
                templates[81] = new TcpIpTraceData(null, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 14, "Retransmit", ProviderGuid, ProviderName, null);
                templates[82] = new TcpIpConnectTraceData(null, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 15, "Accept", ProviderGuid, ProviderName, null);
                templates[83] = new TcpIpTraceData(null, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 16, "Reconnect", ProviderGuid, ProviderName, null);
                templates[84] = new TcpIpFailTraceData(null, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 17, "Fail", ProviderGuid, ProviderName, null);
                templates[85] = new TcpIpTraceData(null, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 18, "TCPCopy", ProviderGuid, ProviderName, null);
                templates[86] = new TcpIpTraceData(null, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 19, "ARPCopy", ProviderGuid, ProviderName, null);
                templates[87] = new TcpIpTraceData(null, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 20, "FullACK", ProviderGuid, ProviderName, null);
                templates[88] = new TcpIpTraceData(null, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 21, "PartACK", ProviderGuid, ProviderName, null);
                templates[89] = new TcpIpTraceData(null, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 22, "DupACK", ProviderGuid, ProviderName, null);
                templates[90] = new TcpIpV6SendTraceData(null, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 26, "SendIPV6", ProviderGuid, ProviderName, null);
                templates[91] = new TcpIpV6TraceData(null, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 27, "RecvIPV6", ProviderGuid, ProviderName, null);
                templates[92] = new TcpIpV6TraceData(null, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 29, "DisconnectIPV6", ProviderGuid, ProviderName, null);
                templates[93] = new TcpIpV6TraceData(null, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 30, "RetransmitIPV6", ProviderGuid, ProviderName, null);
                templates[94] = new TcpIpV6TraceData(null, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 32, "ReconnectIPV6", ProviderGuid, ProviderName, null);
                templates[95] = new TcpIpV6TraceData(null, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 34, "TCPCopyIPV6", ProviderGuid, ProviderName, null);
                templates[96] = new TcpIpV6ConnectTraceData(null, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 28, "ConnectIPV6", ProviderGuid, ProviderName, null);
                templates[97] = new TcpIpV6ConnectTraceData(null, 0xFFFF, 7, "TcpIp", TcpIpTaskGuid, 31, "AcceptIPV6", ProviderGuid, ProviderName, null);
                templates[98] = new UdpIpTraceData(null, 0xFFFF, 8, "UdpIp", UdpIpTaskGuid, 10, "Send", ProviderGuid, ProviderName, null);
                templates[99] = new UdpIpTraceData(null, 0xFFFF, 8, "UdpIp", UdpIpTaskGuid, 11, "Recv", ProviderGuid, ProviderName, null);
                templates[100] = new UdpIpFailTraceData(null, 0xFFFF, 8, "UdpIp", UdpIpTaskGuid, 17, "Fail", ProviderGuid, ProviderName, null);
                templates[101] = new UpdIpV6TraceData(null, 0xFFFF, 8, "UdpIp", UdpIpTaskGuid, 26, "SendIPV6", ProviderGuid, ProviderName, null);
                templates[102] = new UpdIpV6TraceData(null, 0xFFFF, 8, "UdpIp", UdpIpTaskGuid, 27, "RecvIPV6", ProviderGuid, ProviderName, null);
                templates[103] = new ImageLoadTraceData(null, 0xFFFF, 9, "Image", ImageTaskGuid, 10, "Load", ProviderGuid, ProviderName, null);
                templates[104] = new ImageLoadTraceData(null, 0xFFFF, 9, "Image", ImageTaskGuid, 2, "Unload", ProviderGuid, ProviderName, null);
                templates[105] = new ImageLoadTraceData(null, 0xFFFF, 9, "Image", ImageTaskGuid, 3, "DCStart", ProviderGuid, ProviderName, null);
                templates[106] = new ImageLoadTraceData(null, 0xFFFF, 9, "Image", ImageTaskGuid, 4, "DCStop", ProviderGuid, ProviderName, null);
                templates[107] = new MemoryPageFaultTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 10, "TransitionFault", ProviderGuid, ProviderName, null);
                templates[108] = new MemoryPageFaultTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 11, "DemandZeroFault", ProviderGuid, ProviderName, null);
                templates[109] = new MemoryPageFaultTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 12, "CopyOnWrite", ProviderGuid, ProviderName, null);
                templates[110] = new MemoryPageFaultTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 13, "GuardMemory", ProviderGuid, ProviderName, null);
                templates[111] = new MemoryPageFaultTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 14, "HardMemory", ProviderGuid, ProviderName, null);
                templates[112] = new MemoryPageFaultTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 15, "AccessViolation", ProviderGuid, ProviderName, null);
                templates[113] = new MemoryHardFaultTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 32, "HardFault", ProviderGuid, ProviderName, null);
                templates[114] = new MemoryHeapRangeRundownTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 100, "HeapRangeRundown", ProviderGuid, ProviderName, null);
                templates[115] = new MemoryHeapRangeCreateTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 101, "HeapRangeCreate", ProviderGuid, ProviderName, null);
                templates[116] = new MemoryHeapRangeTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 102, "HeapRangeReserve", ProviderGuid, ProviderName, null);
                templates[117] = new MemoryHeapRangeTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 103, "HeapRangeRelease", ProviderGuid, ProviderName, null);
                templates[118] = new MemoryHeapRangeDestroyTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 104, "HeapRangeDestroy", ProviderGuid, ProviderName, null);
                templates[119] = new MemoryImageLoadBackedTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 105, "ImageLoadBacked", ProviderGuid, ProviderName, null);
                templates[120] = new MemoryPageAccessTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 35, "InMemory", ProviderGuid, ProviderName, null);
                templates[121] = new MemorySystemMemInfoTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 112, "SystemMemInfo", ProviderGuid, ProviderName, null);
                templates[122] = new MemoryPageAccessTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 117, "InMemoryActive", ProviderGuid, ProviderName, null);
                templates[123] = new MemoryPageAccessTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 118, "PageAccess", ProviderGuid, ProviderName, null);
                templates[124] = new MemoryPageAccessTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 130, "PageAccess", ProviderGuid, ProviderName, null);
                templates[125] = new MemoryProcessMemInfoTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 125, "ProcessMemInfo", ProviderGuid, ProviderName, null);
                templates[126] = new MemoryProcessMemInfoTraceData(null, 2, 2, "MemoryProcessMemInfo", Guid.Empty, 0, "", MemoryProviderGuid, "Microsoft-Windows-Kernel-Memory", null);
                templates[127] = new MemInfoTraceData(null, 1, 1, "MemoryMemInfo", Guid.Empty, 0, "", MemoryProviderGuid, "Microsoft-Windows-Kernel-Memory", State);
                templates[128] = new EmptyTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 73, "PFMappedSectionCreate", ProviderGuid, ProviderName);
                templates[129] = new EmptyTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 76, "SessionDCStop", ProviderGuid, ProviderName);
                templates[130] = new EmptyTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 79, "PFMappedSectionDelete", ProviderGuid, ProviderName);
                templates[131] = new EmptyTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 115, "InMemoryStoreFault", ProviderGuid, ProviderName);
                templates[132] = new EmptyTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 119, "PageRelease", ProviderGuid, ProviderName);
                templates[133] = new EmptyTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 120, "RangeAccess", ProviderGuid, ProviderName);
                templates[134] = new EmptyTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 121, "RangeRelease", ProviderGuid, ProviderName);
                templates[135] = new EmptyTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 122, "Combine", ProviderGuid, ProviderName);
                templates[136] = new EmptyTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 123, "KernelMemUsage", ProviderGuid, ProviderName);
                templates[137] = new EmptyTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 124, "MMStats", ProviderGuid, ProviderName);
                templates[138] = new EmptyTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 126, "MemInfoSessionWS", ProviderGuid, ProviderName);
                templates[139] = new EmptyTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 127, "VirtualRotate", ProviderGuid, ProviderName);
                templates[140] = new EmptyTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 128, "VirtualAllocDCStart", ProviderGuid, ProviderName);
                templates[141] = new EmptyTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 129, "VirtualAllocDCStop", ProviderGuid, ProviderName);
                templates[142] = new EmptyTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 131, "RemoveFromWS", ProviderGuid, ProviderName);
                templates[143] = new EmptyTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 132, "WSSharableRundown", ProviderGuid, ProviderName);
                templates[144] = new EmptyTraceData(null, 0xFFFF, 10, "Memory", MemoryTaskGuid, 133, "InMemoryActiveRundown", ProviderGuid, ProviderName);
                templates[145] = new SampledProfileTraceData(null, 0xFFFF, 11, "PerfInfo", PerfInfoTaskGuid, 46, "Sample", ProviderGuid, ProviderName, null);
                templates[146] = new PMCCounterProfTraceData(null, 0xFFFF, 11, "PerfInfo", PerfInfoTaskGuid, 47, "PMCSample", ProviderGuid, ProviderName, null);
                templates[147] = new SystemPathsTraceData(null, 0xFFFF, 0, "SysConfig", SysConfigTaskGuid, 33, "SystemPaths", ProviderGuid, ProviderName);
                templates[148] = new SampledProfileIntervalTraceData(null, 0xFFFF, 11, "PerfInfo", PerfInfoTaskGuid, 72, "SetInterval", ProviderGuid, ProviderName, null);
                templates[149] = new SampledProfileIntervalTraceData(null, 0xFFFF, 11, "PerfInfo", PerfInfoTaskGuid, 73, "CollectionStart", ProviderGuid, ProviderName, null);
                templates[150] = new SampledProfileIntervalTraceData(null, 0xFFFF, 11, "PerfInfo", PerfInfoTaskGuid, 74, "CollectionEnd", ProviderGuid, ProviderName, null);
                templates[151] = new SysCallEnterTraceData(null, 0xFFFF, 11, "PerfInfo", PerfInfoTaskGuid, 51, "SysClEnter", ProviderGuid, ProviderName, null);
                templates[152] = new SysCallExitTraceData(null, 0xFFFF, 11, "PerfInfo", PerfInfoTaskGuid, 52, "SysClExit", ProviderGuid, ProviderName, null);
                templates[153] = new ISRTraceData(null, 0xFFFF, 11, "PerfInfo", PerfInfoTaskGuid, 67, "ISR", ProviderGuid, ProviderName, null);
                templates[154] = new DPCTraceData(null, 0xFFFF, 11, "PerfInfo", PerfInfoTaskGuid, 66, "ThreadedDPC", ProviderGuid, ProviderName, null);
                templates[155] = new DPCTraceData(null, 0xFFFF, 11, "PerfInfo", PerfInfoTaskGuid, 68, "DPC", ProviderGuid, ProviderName, null);
                templates[156] = new DPCTraceData(null, 0xFFFF, 11, "PerfInfo", PerfInfoTaskGuid, 69, "TimerDPC", ProviderGuid, ProviderName, null);
                templates[157] = new EmptyTraceData(null, 0xFFFF, 11, "PerfInfo", PerfInfoTaskGuid, 58, "DebuggerEnabled", ProviderGuid, ProviderName);
                templates[158] = new StackWalkStackTraceData(null, 0xFFFF, 12, "StackWalk", StackWalkTaskGuid, 32, "Stack", ProviderGuid, ProviderName, null);
                templates[159] = new StackWalkRefTraceData(null, 0xFFFF, 12, "StackWalk", StackWalkTaskGuid, 37, "StackKeyKernel", ProviderGuid, ProviderName, null);
                templates[160] = new StackWalkRefTraceData(null, 0xFFFF, 12, "StackWalk", StackWalkTaskGuid, 38, "StackKeyUser", ProviderGuid, ProviderName, null);
                templates[161] = new StackWalkDefTraceData(null, 0xFFFF, 12, "StackWalk", StackWalkTaskGuid, 35, "KeyDelete", ProviderGuid, ProviderName, null);
                templates[162] = new StackWalkDefTraceData(null, 0xFFFF, 12, "StackWalk", StackWalkTaskGuid, 36, "KeyRundown", ProviderGuid, ProviderName, null);
                templates[163] = new ALPCSendMessageTraceData(null, 0xFFFF, 13, "ALPC", ALPCTaskGuid, 33, "SendMessage", ProviderGuid, ProviderName, null);
                templates[164] = new ALPCReceiveMessageTraceData(null, 0xFFFF, 13, "ALPC", ALPCTaskGuid, 34, "ReceiveMessage", ProviderGuid, ProviderName, null);
                templates[165] = new ALPCWaitForReplyTraceData(null, 0xFFFF, 13, "ALPC", ALPCTaskGuid, 35, "WaitForReply", ProviderGuid, ProviderName, null);
                templates[166] = new ALPCWaitForNewMessageTraceData(null, 0xFFFF, 13, "ALPC", ALPCTaskGuid, 36, "WaitForNewMessage", ProviderGuid, ProviderName, null);
                templates[167] = new ALPCUnwaitTraceData(null, 0xFFFF, 13, "ALPC", ALPCTaskGuid, 37, "Unwait", ProviderGuid, ProviderName, null);
                templates[168] = new EmptyTraceData(null, 0xFFFF, 14, "LostEvent", LostEventTaskGuid, 32, "", ProviderGuid, ProviderName);
                templates[169] = new SystemConfigCPUTraceData(null, 0xFFFF, 15, "SystemConfig", SystemConfigTaskGuid, 10, "CPU", ProviderGuid, ProviderName, null);
                templates[170] = new SystemConfigPhyDiskTraceData(null, 0xFFFF, 15, "SystemConfig", SystemConfigTaskGuid, 11, "PhyDisk", ProviderGuid, ProviderName, null);
                templates[171] = new SystemConfigLogDiskTraceData(null, 0xFFFF, 15, "SystemConfig", SystemConfigTaskGuid, 12, "LogDisk", ProviderGuid, ProviderName, null);
                templates[172] = new SystemConfigNICTraceData(null, 0xFFFF, 15, "SystemConfig", SystemConfigTaskGuid, 13, "NIC", ProviderGuid, ProviderName, null);
                templates[173] = new SystemConfigVideoTraceData(null, 0xFFFF, 15, "SystemConfig", SystemConfigTaskGuid, 14, "Video", ProviderGuid, ProviderName, null);
                templates[174] = new SystemConfigServicesTraceData(null, 0xFFFF, 15, "SystemConfig", SystemConfigTaskGuid, 15, "Services", ProviderGuid, ProviderName, null);
                templates[175] = new SystemConfigPowerTraceData(null, 0xFFFF, 15, "SystemConfig", SystemConfigTaskGuid, 16, "Power", ProviderGuid, ProviderName, null);
                templates[176] = new SystemConfigIRQTraceData(null, 0xFFFF, 15, "SystemConfig", SystemConfigTaskGuid, 21, "IRQ", ProviderGuid, ProviderName, null);
                templates[177] = new SystemConfigPnPTraceData(null, 0xFFFF, 15, "SystemConfig", SystemConfigTaskGuid, 22, "PnP", ProviderGuid, ProviderName, null);
                templates[178] = new SystemConfigNetworkTraceData(null, 0xFFFF, 15, "SystemConfig", SystemConfigTaskGuid, 17, "Network", ProviderGuid, ProviderName, null);
                templates[179] = new SystemConfigIDEChannelTraceData(null, 0xFFFF, 15, "SystemConfig", SystemConfigTaskGuid, 23, "IDEChannel", ProviderGuid, ProviderName, null);
                templates[180] = new VirtualAllocTraceData(null, 0xFFFF, 0, "VirtualMem", VirtualAllocTaskGuid, 98, "Alloc", ProviderGuid, ProviderName, null);
                templates[181] = new VirtualAllocTraceData(null, 0xFFFF, 0, "VirtualMem", VirtualAllocTaskGuid, 99, "Free", ProviderGuid, ProviderName, null);
                templates[182] = new DispatcherReadyThreadTraceData(null, 0xFFFF, 0, "Dispatcher", ReadyThreadTaskGuid, 50, "ReadyThread", ProviderGuid, ProviderName, null);
                templates[183] = new StringTraceData(null, 0xFFFF, 0, "PerfInfo", PerfInfoTaskGuid, 34, "Mark", ProviderGuid, ProviderName, false);
                templates[184] = new BuildInfoTraceData(null, 0xFFFF, 0, "SysConfig", SysConfigTaskGuid, 32, "BuildInfo", ProviderGuid, ProviderName);
                templates[185] = new VolumeMappingTraceData(null, 0xFFFF, 0, "SysConfig", SysConfigTaskGuid, 35, "VolumeMapping", ProviderGuid, ProviderName);
                templates[186] = new StringTraceData(null, 0xFFFF, 0, "SysConfig", SysConfigTaskGuid, 34, "UnknownVolume", ProviderGuid, ProviderName, true);
                // templates[146] = new BatchedSampledProfileTraceData(null, 0xFFFF, 11, "PerfInfo", PerfInfoTaskGuid, 55, "BatchedSample", ProviderGuid, ProviderName, null);
                templates[187] = new ObjectHandleTraceData(null, 0xFFFF, 0, "Object", ObjectTaskGuid, 32, "CreateHandle", ProviderGuid, ProviderName, null);
                templates[188] = new ObjectHandleTraceData(null, 0xFFFF, 0, "Object", ObjectTaskGuid, 33, "CloseHandle", ProviderGuid, ProviderName, null);
                templates[189] = new ObjectDuplicateHandleTraceData(null, 0xFFFF, 0, "Object", ObjectTaskGuid, 34, "DuplicateHandle", ProviderGuid, ProviderName, null);
                templates[190] = new ObjectTypeNameTraceData(null, 0xFFFF, 0, "Object", ObjectTaskGuid, 37, "TypeDCEnd", ProviderGuid, ProviderName, null);
                templates[191] = new ObjectNameTraceData(null, 0xFFFF, 0, "Object", ObjectTaskGuid, 39, "HandleDCEnd", ProviderGuid, ProviderName, null);
                templates[192] = new ISRTraceData(null, 0xFFFF, 11, "PerfInfo", PerfInfoTaskGuid, 50, "ISR", ProviderGuid, ProviderName, null);
                templates[193] = new ThreadSetNameTraceData(null, 0xFFFF, 2, "Thread", ThreadTaskGuid, 72, "SetName", ProviderGuid, ProviderName);
                s_templates = templates;
            }
            foreach (var template in s_templates)
            {
                if (template == null)       // We removed some
                    continue;
                if (eventsToObserve == null || eventsToObserve(template.ProviderName, template.EventName) == EventFilterResponse.AcceptEvent)
                    callback(template);
            }
        }

        private static ParserTrackingOptions DefaultOptionsForSource(TraceEventSource source)
        {
            var ret = ParserTrackingOptions.Default;
            // Currently DiskIO and Registry too expensive (unbounded) for real time).  
            if (source.IsRealTime)
                ret &= ~(ParserTrackingOptions.DiskIOServiceTime | ParserTrackingOptions.RegistryNameToObject);
            return ret;
        }

        internal KernelTraceEventParserState State
        {
            get
            {
                KernelTraceEventParserState ret = (KernelTraceEventParserState)StateObject;
                if (ret == null)
                {
                    ret = new KernelTraceEventParserState();
                    StateObject = ret;
                }
                return ret;
            }
        }
        internal static readonly Guid EventTraceTaskGuid = new Guid(unchecked((int)0x68fdd900), unchecked((short)0x4a3e), unchecked((short)0x11d1), 0x84, 0xf4, 0x00, 0x00, 0xf8, 0x04, 0x64, 0xe3);
        internal static readonly Guid ProcessTaskGuid = new Guid(unchecked((int)0x3d6fa8d0), unchecked((short)0xfe05), unchecked((short)0x11d0), 0x9d, 0xda, 0x00, 0xc0, 0x4f, 0xd7, 0xba, 0x7c);
        internal static readonly Guid ThreadTaskGuid = new Guid(unchecked((int)0x3d6fa8d1), unchecked((short)0xfe05), unchecked((short)0x11d0), 0x9d, 0xda, 0x00, 0xc0, 0x4f, 0xd7, 0xba, 0x7c);
        internal static readonly Guid DiskIOTaskGuid = new Guid(unchecked((int)0x3d6fa8d4), unchecked((short)0xfe05), unchecked((short)0x11d0), 0x9d, 0xda, 0x00, 0xc0, 0x4f, 0xd7, 0xba, 0x7c);
        internal static readonly Guid RegistryTaskGuid = new Guid(unchecked((int)0xae53722e), unchecked((short)0xc863), unchecked((short)0x11d2), 0x86, 0x59, 0x00, 0xc0, 0x4f, 0xa3, 0x21, 0xa1);
        internal static readonly Guid SplitIoTaskGuid = new Guid(unchecked((int)0xd837ca92), unchecked((short)0x12b9), unchecked((short)0x44a5), 0xad, 0x6a, 0x3a, 0x65, 0xb3, 0x57, 0x8a, 0xa8);
        internal static readonly Guid FileIOTaskGuid = new Guid(unchecked((int)0x90cbdc39), unchecked((short)0x4a3e), unchecked((short)0x11d1), 0x84, 0xf4, 0x00, 0x00, 0xf8, 0x04, 0x64, 0xe3);
        internal static readonly Guid TcpIpTaskGuid = new Guid(unchecked((int)0x9a280ac0), unchecked((short)0xc8e0), unchecked((short)0x11d1), 0x84, 0xe2, 0x00, 0xc0, 0x4f, 0xb9, 0x98, 0xa2);
        internal static readonly Guid UdpIpTaskGuid = new Guid(unchecked((int)0xbf3a50c5), unchecked((short)0xa9c9), unchecked((short)0x4988), 0xa0, 0x05, 0x2d, 0xf0, 0xb7, 0xc8, 0x0f, 0x80);
        internal static readonly Guid ImageTaskGuid = new Guid(unchecked((int)0x2cb15d1d), unchecked((short)0x5fc1), unchecked((short)0x11d2), 0xab, 0xe1, 0x00, 0xa0, 0xc9, 0x11, 0xf5, 0x18);
        internal static readonly Guid MemoryTaskGuid = new Guid(unchecked((int)0x3d6fa8d3), unchecked((short)0xfe05), unchecked((short)0x11d0), 0x9d, 0xda, 0x00, 0xc0, 0x4f, 0xd7, 0xba, 0x7c);
        internal static readonly Guid MemoryProviderGuid = new Guid(unchecked((int)0x3d1d93ef7), unchecked((short)0xe1f2), unchecked((short)0x4f45), 0x99, 0x43, 0x03, 0xd2, 0x45, 0xfe, 0x6c, 0x00);
        internal static readonly Guid PerfInfoTaskGuid = new Guid(unchecked((int)0xce1dbfb4), unchecked((short)0x137e), unchecked((short)0x4da6), 0x87, 0xb0, 0x3f, 0x59, 0xaa, 0x10, 0x2c, 0xbc);
        internal static readonly Guid StackWalkTaskGuid = new Guid(unchecked((int)0xdef2fe46), unchecked((short)0x7bd6), unchecked((short)0x4b80), 0xbd, 0x94, 0xf5, 0x7f, 0xe2, 0x0d, 0x0c, 0xe3);
        // Used for new style user mode stacks.  
        internal static readonly Guid EventTracingProviderGuid = new Guid(unchecked((int)0xb675ec37), unchecked((short)0xbdb6), unchecked((short)0x4648), 0xbc, 0x92, 0xf3, 0xfd, 0xc7, 0x4d, 0x3c, 0xa2);
        internal static readonly Guid ALPCTaskGuid = new Guid(unchecked((int)0x45d8cccd), unchecked((short)0x539f), unchecked((short)0x4b72), 0xa8, 0xb7, 0x5c, 0x68, 0x31, 0x42, 0x60, 0x9a);
        internal static readonly Guid LostEventTaskGuid = new Guid(unchecked((int)0x6a399ae0), unchecked((short)0x4bc6), unchecked((short)0x4de9), 0x87, 0x0b, 0x36, 0x57, 0xf8, 0x94, 0x7e, 0x7e);
        internal static readonly Guid SystemConfigTaskGuid = new Guid(unchecked((int)0x01853a65), unchecked((short)0x418f), unchecked((short)0x4f36), 0xae, 0xfc, 0xdc, 0x0f, 0x1d, 0x2f, 0xd2, 0x35);
        internal static readonly Guid VirtualAllocTaskGuid = new Guid(unchecked((int)0x3d6fa8d3), unchecked((short)0xfe05), unchecked((short)0x11d0), 0x9d, 0xda, 0x00, 0xc0, 0x4f, 0xd7, 0xba, 0x7c);
        internal static readonly Guid ReadyThreadTaskGuid = new Guid(unchecked((int)0x3d6fa8d1), unchecked((short)0xfe05), unchecked((short)0x11d0), 0x9d, 0xda, 0x00, 0xc0, 0x4f, 0xd7, 0xba, 0x7c);
        internal static readonly Guid SysConfigTaskGuid = new Guid(unchecked((int)0x9b79ee91), unchecked((short)0xb5fd), 0x41c0, 0xa2, 0x43, 0x42, 0x48, 0xe2, 0x66, 0xe9, 0xD0);
        internal static readonly Guid ObjectTaskGuid = new Guid(unchecked((int)0x89497f50), unchecked((short)0xeffe), 0x4440, 0x8c, 0xf2, 0xce, 0x6b, 0x1c, 0xdc, 0xac, 0xa7);
        #endregion
    }
    #region private types
    /// <summary>
    /// KernelTraceEventParserState holds all information that is shared among all events that is
    /// needed to decode kernel events.   This class is registered with the source so that it will be
    /// persisted.  Things in here include
    /// 
    ///     * FileID to FileName mapping, 
    ///     * ThreadID to ProcessID mapping
    ///     * Kernel file name to user file name mapping 
    /// </summary>
    // [SecuritySafeCritical]
    internal class KernelTraceEventParserState : IFastSerializable
    {
        public KernelTraceEventParserState()
        {
            driveMapping = new KernelToUserDriveMapping();
        }

        internal string FileIDToName(Address fileKey, long timeQPC)
        {
            lazyFileIDToName.FinishRead();      // We don't read fileIDToName from the disk unless we need to, check
            string ret;
            if (!fileIDToName.TryGetValue(fileKey, timeQPC, out ret))
            {
                return "";
            }

            return ret;
        }
        /// <summary>
        /// If you have a file object (per-open-file) in addition to a fileKey, try using both 
        /// to look up the file name.  
        /// </summary>
        internal string FileIDToName(Address fileKey, Address fileObject, long timeQPC)
        {
            lazyFileIDToName.FinishRead();      // We don't read fileIDToName from the disk unless we need to, check

            string ret;
            if (!fileIDToName.TryGetValue(fileKey, timeQPC, out ret) && !fileIDToName.TryGetValue(fileObject, timeQPC, out ret))
            {
                return "";
            }

            return ret;
        }

        internal string ObjectToName(Address objectAddress, long timeQPC)
        {
            string ret;
            if (!fileIDToName.TryGetValue(objectAddress, timeQPC, out ret))
            {
                return "";
            }

            return ret;
        }

        internal string ObjectTypeToName(int objectType)
        {
            string ret;
            if (_objectTypeToName == null || !_objectTypeToName.TryGetValue(objectType, out ret))
            {
                return "";
            }

            return ret;
        }

        internal int ThreadIDToProcessID(int threadID, long timeQPC)
        {
            int ret;
            if (!threadIDtoProcessID.TryGetValue(threadID, timeQPC, out ret))
            {
                // See if we have end-Thread information, and use that if it is there.  
                if (threadIDtoProcessIDRundown != null && threadIDtoProcessIDRundown.TryGetValue(threadID, -timeQPC, out ret))
                {
                    return ret;
                }

                ret = -1;
            }
            return ret;
        }
        internal string KernelToUser(string kernelName)
        {
            return driveMapping[kernelName];
        }
        #region private
        void IFastSerializable.ToStream(Serializer serializer)
        {
            serializer.Write(driveMapping);

            serializer.Write(threadIDtoProcessID.Count);
            serializer.Log("<WriteCollection name=\"ProcessIDForThread\" count=\"" + threadIDtoProcessID.Count + "\">\r\n");
            foreach (HistoryDictionary<int, int>.HistoryValue entry in threadIDtoProcessID.Entries)
            {
                serializer.Write(entry.Key);
                serializer.Write(entry.StartTime);
                serializer.Write(entry.Value);
            }

            if (threadIDtoProcessIDRundown == null)
            {
                serializer.Write(0);
            }
            else
            {
                serializer.Write(threadIDtoProcessIDRundown.Count);
                serializer.Log("<WriteCollection name=\"ProcessIDForThreadRundown\" count=\"" + threadIDtoProcessIDRundown.Count + "\">\r\n");
                foreach (HistoryDictionary<int, int>.HistoryValue entry in threadIDtoProcessIDRundown.Entries)
                {
                    serializer.Write(entry.Key);
                    serializer.Write(entry.StartTime);
                    serializer.Write(entry.Value);
                }
                serializer.Log("</WriteCollection>\r\n");
            }

            lazyFileIDToName.Write(serializer, delegate
            {
                serializer.Log("<WriteCollection name=\"fileIDToName\" count=\"" + fileIDToName.Count + "\">\r\n");
                serializer.Write(fileIDToName.Count);
                foreach (HistoryDictionary<Address, string>.HistoryValue entry in fileIDToName.Entries)
                {
                    serializer.WriteAddress(entry.Key);
                    serializer.Write(entry.StartTime);
                    serializer.Write(entry.Value);
                }
                serializer.Log("</WriteCollection>\r\n");
            });

            lazyDiskEventTimeStamp.Write(serializer, delegate
            {
                serializer.Log("<WriteCollection name=\"diskEventTimeStamp\" count=\"" + diskEventTimeStamp.Count + "\">\r\n");
                serializer.Write(diskEventTimeStamp.Count);
                for (int i = 0; i < diskEventTimeStamp.Count; i++)
                {
                    Debug.Assert(i == 0 || diskEventTimeStamp[i - 1].TimeStampRelativeMSec <= diskEventTimeStamp[i].TimeStampRelativeMSec);
                    serializer.Write(diskEventTimeStamp[i].DiskNum);
                    serializer.Write(diskEventTimeStamp[i].TimeStampRelativeMSec);
                }
                serializer.Log("</WriteCollection>");
            });

            if (_objectTypeToName != null)
            {
                serializer.Log("<WriteCollection name=\"objectTypeToName\" count=\"" + _objectTypeToName.Count + "\">\r\n");
                serializer.Write(_objectTypeToName.Count);
                foreach (KeyValuePair<int, string> keyValue in _objectTypeToName)
                {
                    serializer.Write(keyValue.Key);
                    serializer.Write(keyValue.Value);
                }
                serializer.Log("</WriteCollection>\r\n");
            }
            else
            {
                serializer.Write(0);
            }
        }
        void IFastSerializable.FromStream(Deserializer deserializer)
        {
            deserializer.ReadObject(out driveMapping);

            int count; deserializer.ReadInt32(out count);
            Debug.Assert(count >= 0);
            deserializer.Log("<Marker name=\"ProcessIDForThread\"/ count=\"" + count + "\">");
            for (int i = 0; i < count; i++)
            {
                int key; deserializer.ReadInt32(out key);
                long startTimeQPC; deserializer.ReadInt64(out startTimeQPC);
                int value; deserializer.ReadInt32(out value);
                threadIDtoProcessID.Add(key, startTimeQPC, value);
            }

            deserializer.ReadInt32(out count);
            Debug.Assert(count >= 0);
            deserializer.Log("<Marker name=\"ProcessIDForThreadRundown\"/ count=\"" + count + "\">");
            if (count > 0)
            {
                threadIDtoProcessIDRundown = new HistoryDictionary<int, int>(count);
                for (int i = 0; i < count; i++)
                {
                    int key; deserializer.ReadInt32(out key);
                    long startTimeQPC; deserializer.ReadInt64(out startTimeQPC);
                    int value; deserializer.ReadInt32(out value);
                    threadIDtoProcessIDRundown.Add(key, startTimeQPC, value);
                }
            }

            lazyFileIDToName.Read(deserializer, delegate
            {
                deserializer.ReadInt32(out count);
                Debug.Assert(count >= 0);
                deserializer.Log("<Marker name=\"fileIDToName\"/ count=\"" + count + "\">");
                for (int i = 0; i < count; i++)
                {
                    Address key; deserializer.ReadAddress(out key);
                    long startTimeQPC; deserializer.ReadInt64(out startTimeQPC);
                    string value; deserializer.ReadString(out value);
                    fileIDToName.Add(key, startTimeQPC, value);
                }
            });

            lazyDiskEventTimeStamp.Read(deserializer, delegate
            {
                deserializer.ReadInt32(out count);
                Debug.Assert(count >= 0);
                deserializer.Log("<Marker name=\"diskEventTimeStamp\"/ count=\"" + count + "\">");
                for (int i = 0; i < count; i++)
                {
                    diskEventTimeStamp.Add(new DiskIOTime(deserializer.ReadInt32(), deserializer.ReadDouble()));
                }
            });

            deserializer.ReadInt32(out count);
            Debug.Assert(count >= 0);
            if (count > 0)
            {
                deserializer.Log("<Marker name=\"objectTypeToName\"/ count=\"" + count + "\">");
                _objectTypeToName = new Dictionary<int, string>(count);
                for (int i = 0; i < count; i++)
                {
                    _objectTypeToName.Add(deserializer.ReadInt32(), deserializer.ReadString());
                }
            }
        }

        internal HistoryDictionary<Address, string> fileIDToName
        {
            get
            {
                if (_fileIDToName == null)
                {
                    _fileIDToName = new HistoryDictionary<Address, string>(500);
                }

                return _fileIDToName;
            }
        }
        internal HistoryDictionary<int, int> threadIDtoProcessID
        {
            get
            {
                if (_threadIDtoProcessID == null)
                {
                    _threadIDtoProcessID = new HistoryDictionary<int, int>(50);
                }

                return _threadIDtoProcessID;
            }
        }
        internal GrowableArray<DiskIOTime> diskEventTimeStamp
        {
            get
            {
                if (_diskEventTimeStamp.EmptyCapacity)
                {
                    _diskEventTimeStamp = new GrowableArray<DiskIOTime>(500);
                }

                return _diskEventTimeStamp;
            }
        }


        private DeferedRegion lazyFileIDToName;
        internal DeferedRegion lazyDiskEventTimeStamp;

        internal struct DiskIOTime
        {
            public DiskIOTime(int DiskNum, double TimeStampQPC) { this.DiskNum = DiskNum; TimeStampRelativeMSec = TimeStampQPC; }
            public int DiskNum;
            public double TimeStampRelativeMSec;
        };

        // Fields 
        internal KernelToUserDriveMapping driveMapping;
        private HistoryDictionary<Address, string> _fileIDToName;
        private HistoryDictionary<int, int> _threadIDtoProcessID;
        internal Dictionary<int, string> _objectTypeToName;
        private GrowableArray<DiskIOTime> _diskEventTimeStamp;
        internal int lastDiskEventIdx;

        /// <summary>
        /// This is for the circular buffer case.  In that case we may not have thread starts (and thus we don't
        /// have entries in threadIDtoProcessID).   Because HistoryTable finds the FIRST entry GREATER than the
        /// given threadID we NEGATE all times before we place it in this table.
        /// 
        /// Also, because circular buffering is not the common case, we only add entries to this table if needed
        /// (if we could not find the thread ID using threadIDtoProcessID).  
        /// </summary>
        internal HistoryDictionary<int, int> threadIDtoProcessIDRundown;
        internal KernelTraceEventParser.ParserTrackingOptions callBacksSet;
        #endregion

    }

    /// <summary>
    /// Keeps track of the mapping from kernel names to file system names (drives)  
    /// </summary>
    public sealed class KernelToUserDriveMapping : IFastSerializable
    {
        /// <summary>
        /// Create a new KernelToUserDriveMapping that can look up kernel names for drives and map them to windows drive letters. 
        /// </summary>
        public KernelToUserDriveMapping()
        {
            kernelToDriveMap = new List<KeyValuePair<string, string>>();
            MapKernelToUser = MapKernelToUserDefault;
        }

        /// <summary>
        /// Returns the string representing the windows drive letter for the kernel drive name 'kernelName'
        /// </summary>
        /// <param name="kernelName"></param>
        /// <returns></returns>
        public string this[string kernelName]
        {
            get { return MapKernelToUser(kernelName); }
        }

        #region private
        internal string MapKernelToUserDefault(string kernelName)
        {
            // TODO confirm that you are on the local machine before initializing in this way.  
            if (kernelToDriveMap.Count == 0)
            {
                PopulateFromLocalMachine();
            }

#if !CONTAINER_WORKAROUND_NOT_NEEDED
            // Currently ETW shows paths from the HOST not the CLIENT for some files.   We recognize them 
            // because they have the form of a GUID path \OS or \File and then the client path.   It is enough
            // to fix this for files in the \windows directory so we use \OS\Windows\ or \Files\Windows as the key 
            // to tell if we have a HOST file path and we morph the name to fix it.
            // We can pull this out when the OS fixes ETW to show client names.  
            var filesIdx = kernelName.IndexOf(@"\OS\Windows\", StringComparison.OrdinalIgnoreCase);
            if (0 <= filesIdx && filesIdx + 3 < kernelName.Length)
            {
                return systemDrive + kernelName.Substring(filesIdx + 3);
            }

            filesIdx = kernelName.IndexOf(@"\Files\Windows\", StringComparison.OrdinalIgnoreCase);
            if (0 <= filesIdx && filesIdx + 6 < kernelName.Length)
            {
                return systemDrive + kernelName.Substring(filesIdx + 6);
            }
#endif
            for (int i = 0; i < kernelToDriveMap.Count; i++)
            {
                Debug.Assert(kernelToDriveMap[i].Key.EndsWith(@"\"));
                Debug.Assert(kernelToDriveMap[i].Value.Length == 0 || kernelToDriveMap[i].Value.EndsWith(@"\"));

                // For every string in the map, does the kernel name match a prefix in the table?
                // If so we have found a match. 
                string kernelPrefix = kernelToDriveMap[i].Key;
                if (string.Compare(kernelName, 0, kernelPrefix, 0, kernelPrefix.Length, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    var ret = kernelToDriveMap[i].Value + kernelName.Substring(kernelPrefix.Length);
                    return ret;
                }
            }

            // Heuristic.  If we have not found it yet, tack on the system drive letter if it is not 
            // This is similar to what XPERF does too, but it is clear it is not perfect. 
            if (kernelName.Length > 2 && kernelName[0] == '\\' && Char.IsLetterOrDigit(kernelName[1]))
            {
                return systemDrive + kernelName;
            }

            // TODO this is still not complete, compare to XPERF and align.  
            return kernelName;
        }

        internal void PopulateFromLocalMachine()
        {
            kernelToDriveMap.Add(new KeyValuePair<string, string>(@"\??\", ""));
            kernelToDriveMap.Add(new KeyValuePair<string, string>(@"\SystemRoot\", Environment.GetEnvironmentVariable("SystemRoot") + @"\"));

            StringBuilder kernelNameBuff = new StringBuilder(2048);
            int logicalDriveBitVector = GetLogicalDrives();
            char curChar = 'A';
            while (logicalDriveBitVector != 0)
            {
                if ((logicalDriveBitVector & 1) != 0)
                {
                    string driveName = new string(curChar, 1) + @":";
                    kernelNameBuff.Length = 0;
                    if (QueryDosDeviceW(driveName, kernelNameBuff, 2048) != 0)
                    {
                        kernelToDriveMap.Add(new KeyValuePair<string, string>(kernelNameBuff.ToString() + @"\", driveName + @"\"));
                    }
                }
                logicalDriveBitVector >>= 1;
                curChar++;
            }

            kernelToDriveMap.Add(new KeyValuePair<string, string>(@"\Device\LanmanRedirector\", @"\\"));
            kernelToDriveMap.Add(new KeyValuePair<string, string>(@"\Device\Mup\", @"\\"));
            var windir = Environment.GetEnvironmentVariable("windir") + @"\";
            systemDrive = windir.Substring(0, 2);
            kernelToDriveMap.Add(new KeyValuePair<string, string>(@"\Windows\", windir));
            kernelToDriveMap.Add(new KeyValuePair<string, string>(@"\\", @"\\"));
        }

        internal void AddMapping(string kernelName, string driveName)
        {
            kernelToDriveMap.Add(new KeyValuePair<string, string>(kernelName, driveName));
        }
        internal void AddSystemDrive(string windows)
        {
            AddMapping(@"\Windows\", windows);
            systemDrive = windows.Substring(0, 2);        // grab just the drive letter.  
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint QueryDosDeviceW(string lpDeviceName, StringBuilder lpTargetPath, int ucchMax);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int GetLogicalDrives();

        internal List<KeyValuePair<string, string>> kernelToDriveMap;
        internal string systemDrive;
        internal Func<string, string> MapKernelToUser;

        void IFastSerializable.ToStream(Serializer serializer)
        {
            serializer.Write(kernelToDriveMap.Count);
            serializer.Log("<WriteCollection name=\"driveNames\" count=\"" + kernelToDriveMap.Count + "\">\r\n");
            foreach (var keyValue in kernelToDriveMap)
            {
                serializer.Write(keyValue.Key);
                serializer.Write(keyValue.Value);
            }
            serializer.Log("</WriteCollection>\r\n");
            serializer.Write(systemDrive);
        }

        void IFastSerializable.FromStream(Deserializer deserializer)
        {
            int numDrives; deserializer.ReadInt32(out numDrives);
            for (int i = 0; i < numDrives; i++)
            {
                string key, value;
                deserializer.ReadString(out key);
                deserializer.ReadString(out value);
                kernelToDriveMap.Add(new KeyValuePair<string, string>(key, value));
            }
            deserializer.ReadString(out systemDrive);
        }
        #endregion
    }

    #endregion
}

namespace Microsoft.Diagnostics.Tracing.Parsers.Kernel
{
    // [SecuritySafeCritical]
    public sealed class EventTraceHeaderTraceData : TraceEvent
    {
        public int BufferSize { get { return GetInt32At(0); } }
        public new int Version { get { return GetInt32At(4); } }
        public int ProviderVersion { get { return GetInt32At(8); } }
        public int NumberOfProcessors { get { return GetInt32At(12); } }
        internal long EndTime100ns { get { return GetInt64At(16); } }
        public DateTime EndTime { get { return ETWTraceEventSource.SafeFromFileTimeUtc(EndTime100ns).ToLocalTime(); } }
        public int TimerResolution { get { return GetInt32At(24); } }
        public int MaxFileSize { get { return GetInt32At(28); } }
        public int LogFileMode { get { return GetInt32At(32); } }
        public int BuffersWritten { get { return GetInt32At(36); } }
        public int StartBuffers { get { return GetInt32At(40); } }
        public new int PointerSize { get { return GetInt32At(44); } }
        public int EventsLost { get { return GetInt32At(48); } }
        public int CPUSpeed { get { return GetInt32At(52); } }
        // Skipping LoggerName (pointer size)
        // Skipping LogFileName (pointer size) 
        // TimeZoneInformation HostOffset(60, 2), size 176?  see https://msdn.microsoft.com/en-us/library/windows/desktop/ms725481(v=vs.85).aspx
        /// <summary>
        /// This is the number of minutes between the local time where the data was collected and UTC time. 
        /// It does NOT take Daylight savings time into account.   
        /// It is positive if your time zone is WEST of Greenwich.  
        /// </summary>
        public int UTCOffsetMinutes { get { return GetInt32At(HostOffset(64, 2)); } }
#if false

  uint8  TimeZoneInformation[];
  uint64 BootTime;
  uint64 PerfFreq;
  uint64 StartTime;
  uint32 ReservedFlags;
  uint32 BuffersLost;
#endif
        internal long BootTime100ns { get { return GetInt64At(HostOffset(240, 2)); } }
        public DateTime BootTime { get { return DateTime.FromFileTime(BootTime100ns); } }
        public long PerfFreq { get { return GetInt64At(HostOffset(248, 2)); } }
        internal long StartTime100ns { get { return GetInt64At(HostOffset(256, 2)); } }
        public DateTime StartTime { get { return DateTime.FromFileTime(StartTime100ns); } }
        public int ReservedFlags { get { return GetInt32At(HostOffset(264, 2)); } }
        public int BuffersLost { get { return GetInt32At(HostOffset(268, 2)); } }
        public string SessionName { get { return GetUnicodeStringAt(HostOffset(272, 2)); } }
        public string LogFileName { get { return GetUnicodeStringAt(SkipUnicodeString(HostOffset(272, 2))); } }

        #region Private
        internal EventTraceHeaderTraceData(Action<EventTraceHeaderTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<EventTraceHeaderTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUnicodeString(SkipUnicodeString(HostOffset(272, 2)))));
            Debug.Assert(!(Version > 0 && EventDataLength < SkipUnicodeString(SkipUnicodeString(HostOffset(272, 2)))));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "BufferSize", BufferSize);
            XmlAttribHex(sb, "Version", Version);
            XmlAttrib(sb, "ProviderVersion", ProviderVersion);
            XmlAttrib(sb, "NumberOfProcessors", NumberOfProcessors);
            XmlAttrib(sb, "EndTime", EndTime);
            XmlAttrib(sb, "TimerResolution", TimerResolution);
            XmlAttrib(sb, "MaxFileSize", MaxFileSize);
            XmlAttribHex(sb, "LogFileMode", LogFileMode);
            XmlAttrib(sb, "BuffersWritten", BuffersWritten);
            XmlAttrib(sb, "StartBuffers", StartBuffers);
            XmlAttrib(sb, "PointerSize", PointerSize);
            XmlAttrib(sb, "EventsLost", EventsLost);
            XmlAttrib(sb, "CPUSpeed", CPUSpeed);
            XmlAttrib(sb, "BootTime", BootTime);
            XmlAttrib(sb, "PerfFreq", PerfFreq);
            XmlAttrib(sb, "StartTime", StartTime);
            XmlAttribHex(sb, "ReservedFlags", ReservedFlags);
            XmlAttrib(sb, "BuffersLost", BuffersLost);
            XmlAttrib(sb, "SessionName", SessionName);
            XmlAttrib(sb, "LogFileName", LogFileName);
            XmlAttrib(sb, "UTCOffsetMinutes", UTCOffsetMinutes);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "BufferSize", "Version", "ProviderVersion", "NumberOfProcessors", "EndTime", "TimerResolution", "MaxFileSize", "LogFileMode", "BuffersWritten", "StartBuffers", "PointerSize", "EventsLost", "CPUSpeed", "BootTime", "PerfFreq", "StartTime", "ReservedFlags", "BuffersLost", "SessionName", "LogFileName", "UtcOffsetMinutes" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return BufferSize;
                case 1:
                    return Version;
                case 2:
                    return ProviderVersion;
                case 3:
                    return NumberOfProcessors;
                case 4:
                    return EndTime;
                case 5:
                    return TimerResolution;
                case 6:
                    return MaxFileSize;
                case 7:
                    return LogFileMode;
                case 8:
                    return BuffersWritten;
                case 9:
                    return StartBuffers;
                case 10:
                    return PointerSize;
                case 11:
                    return EventsLost;
                case 12:
                    return CPUSpeed;
                case 13:
                    return BootTime;
                case 14:
                    return PerfFreq;
                case 15:
                    return StartTime;
                case 16:
                    return ReservedFlags;
                case 17:
                    return BuffersLost;
                case 18:
                    return SessionName;
                case 19:
                    return LogFileName;
                case 20:
                    return UTCOffsetMinutes;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<EventTraceHeaderTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class HeaderExtensionTraceData : TraceEvent
    {
        public int GroupMask1 { get { return GetInt32At(0); } }
        public int GroupMask2 { get { return GetInt32At(4); } }
        public int GroupMask3 { get { return GetInt32At(8); } }
        public int GroupMask4 { get { return GetInt32At(12); } }
        public int GroupMask5 { get { return GetInt32At(16); } }
        public int GroupMask6 { get { return GetInt32At(20); } }
        public int GroupMask7 { get { return GetInt32At(24); } }
        public int GroupMask8 { get { return GetInt32At(28); } }
        public int KernelEventVersion { get { if (Version >= 2) { return GetInt32At(32); } return 0; } }

        #region Private
        internal HeaderExtensionTraceData(Action<HeaderExtensionTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<HeaderExtensionTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            // TODO fix
            Debug.Assert(!(Version == 0 && EventDataLength != 32));
            Debug.Assert(!(Version == 1 && EventDataLength != 32));
            Debug.Assert(!(Version == 2 && EventDataLength != 36));
            Debug.Assert(!(Version > 2 && EventDataLength < 36));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "GroupMask1", GroupMask1);
            XmlAttribHex(sb, "GroupMask2", GroupMask2);
            XmlAttribHex(sb, "GroupMask3", GroupMask3);
            XmlAttribHex(sb, "GroupMask4", GroupMask4);
            XmlAttribHex(sb, "GroupMask5", GroupMask5);
            XmlAttribHex(sb, "GroupMask6", GroupMask6);
            XmlAttribHex(sb, "GroupMask7", GroupMask7);
            XmlAttribHex(sb, "GroupMask8", GroupMask8);
            XmlAttribHex(sb, "KernelEventVersion", KernelEventVersion);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "GroupMask1", "GroupMask2", "GroupMask3", "GroupMask4", "GroupMask5", "GroupMask6", "GroupMask7", "GroupMask8", "KernelEventVersion" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return GroupMask1;
                case 1:
                    return GroupMask2;
                case 2:
                    return GroupMask3;
                case 3:
                    return GroupMask4;
                case 4:
                    return GroupMask5;
                case 5:
                    return GroupMask6;
                case 6:
                    return GroupMask7;
                case 7:
                    return GroupMask8;
                case 8:
                    return KernelEventVersion;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<HeaderExtensionTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }

    [Flags]
    public enum ProcessFlags
    {
        None = 0,
        PackageFullName = 1,
        Wow64 = 2,
        Protected = 4,
    }

    public sealed class ProcessTraceData : TraceEvent
    {
        // public int ProcessID { get { if (Version >= 1) return GetInt32At(HostOffset(4, 1)); return (int) GetHostPointer(0); } }
        public int ParentID { get { if (Version >= 1) { return GetInt32At(HostOffset(8, 1)); } return (int)GetAddressAt(HostOffset(4, 1)); } }
        // Skipping UserSID
        public string KernelImageFileName { get { if (Version >= 1) { return GetUTF8StringAt(GetKernelImageNameOffset()); } return ""; } }
        public string ImageFileName
        {
            get
            {
                try
                {
                    return state.KernelToUser(KernelImageFileName);
                }
                catch { }
                return "";
            }
        }

        public Address DirectoryTableBase { get { if (Version >= 3) { return GetAddressAt(HostOffset(20, 1)); } return 0; } }
        public ProcessFlags Flags { get { if (Version >= 4) { return (ProcessFlags)GetInt32At(HostOffset(24, 2)); } return 0; } }

        public int SessionID { get { if (Version >= 1) { return GetInt32At(HostOffset(12, 1)); } return 0; } }
        public int ExitStatus { get { if (Version >= 1) { return GetInt32At(HostOffset(16, 1)); } return 0; } }
        public Address UniqueProcessKey { get { if (Version >= 2) { return GetAddressAt(0); } return 0; } }
        public string CommandLine
        {
            get
            {
                try
                {
                    if (Version >= 2)
                    {
                        return GetUnicodeStringAt(SkipUTF8String(GetKernelImageNameOffset()));
                    }
                }
                catch { }
                return "";
            }
        }
        public string PackageFullName
        {
            get
            {
                if (Version >= 4)
                {
                    return GetUnicodeStringAt(SkipUnicodeString(SkipUTF8String(GetKernelImageNameOffset())));
                }

                return "";
            }
        }
        public string ApplicationID
        {
            get
            {
                if (Version >= 4)
                {
                    return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUTF8String(GetKernelImageNameOffset()))));
                }

                return "";
            }
        }
        #region Private
        private int GetKernelImageNameOffset()
        {
            return SkipSID(Version >= 4 ? HostOffset(28, 2) : (Version >= 3) ? HostOffset(24, 2) : HostOffset(20, 1));
        }
        internal ProcessTraceData(Action<ProcessTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ProcessTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength < SkipUTF8String(SkipSID(HostOffset(8, 2)))));  // TODO fixed by hand
            Debug.Assert(!(Version == 1 && EventDataLength < SkipUTF8String(SkipSID((Version >= 3) ? HostOffset(24, 2) : HostOffset(20, 1))))); // TODO fixed by hand
            Debug.Assert(!(Version == 2 && EventDataLength != SkipUnicodeString(SkipUTF8String(GetKernelImageNameOffset()))));
            Debug.Assert(!(Version == 3 && EventDataLength != SkipUnicodeString(SkipUTF8String(GetKernelImageNameOffset()))));
            Debug.Assert(!(Version == 4 && EventDataLength !=
                SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUTF8String(GetKernelImageNameOffset()))))));
            // TODO version 5 seesm to have put 8 bytes after it (on 32 bit, maybe more on 64 bit. 
            Debug.Assert(!(Version == 5 && EventDataLength <
                SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUTF8String(GetKernelImageNameOffset())))) + 8));

            Debug.Assert(!(Version > 5 && EventDataLength <
                SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUTF8String(GetKernelImageNameOffset())))) + 8));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ProcessID", ProcessID);
            XmlAttrib(sb, "ParentID", ParentID);
            XmlAttrib(sb, "ImageFileName", ImageFileName);
            XmlAttribHex(sb, "DirectoryTableBase", DirectoryTableBase);
            XmlAttrib(sb, "Flags", Flags);
            XmlAttrib(sb, "SessionID", SessionID);
            XmlAttribHex(sb, "ExitStatus", ExitStatus);
            XmlAttribHex(sb, "UniqueProcessKey", UniqueProcessKey);
            XmlAttrib(sb, "CommandLine", CommandLine);
            if (PackageFullName.Length != 0)
            {
                XmlAttrib(sb, "PackageFullName", PackageFullName);
            }

            if (ApplicationID.Length != 0)
            {
                XmlAttrib(sb, "ApplicationID", ApplicationID);
            }

            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "ProcessID", "ParentID", "ImageFileName", "PageDirectoryBase",
                        "Flags", "SessionID", "ExitStatus", "UniqueProcessKey", "CommandLine",
                        "PackageFullName", "ApplicationID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ProcessID;
                case 1:
                    return ParentID;
                case 2:
                    return ImageFileName;
                case 3:
                    return DirectoryTableBase;
                case 4:
                    return Flags;
                case 5:
                    return SessionID;
                case 6:
                    return ExitStatus;
                case 7:
                    return UniqueProcessKey;
                case 8:
                    return CommandLine;
                case 9:
                    return PackageFullName;
                case 10:
                    return ApplicationID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ProcessTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;

        internal override unsafe void FixupData()
        {
            // We wish to create the illusion that the events are reported by the process being started.   
            eventRecord->EventHeader.ProcessId = GetInt32At(HostOffset(4, 1));
            ParentThread = eventRecord->EventHeader.ThreadId;
            eventRecord->EventHeader.ThreadId = -1;
        }
        #endregion
    }
    public sealed class ProcessCtrTraceData : TraceEvent
    {
        // public int ProcessID { get { return GetInt32At(0); } }
        public int MemoryCount { get { return GetInt32At(4); } }
        public int HandleCount { get { return GetInt32At(8); } }
        // Skipping Reserved
        public long PeakVirtualSize { get { return (long)GetAddressAt(16); } }
        public long PeakWorkingSetSize { get { return (long)GetAddressAt(HostOffset(20, 1)); } }
        public long PeakPagefileUsage { get { return (long)GetAddressAt(HostOffset(24, 2)); } }
        public long QuotaPeakPagedPoolUsage { get { return (long)GetAddressAt(HostOffset(28, 3)); } }
        public long QuotaPeakNonPagedPoolUsage { get { return (long)GetAddressAt(HostOffset(32, 4)); } }
        public long VirtualSize { get { return (long)GetAddressAt(HostOffset(36, 5)); } }
        public long WorkingSetSize { get { return (long)GetAddressAt(HostOffset(40, 6)); } }
        public long PagefileUsage { get { return (long)GetAddressAt(HostOffset(44, 7)); } }
        public long QuotaPagedPoolUsage { get { return (long)GetAddressAt(HostOffset(48, 8)); } }
        public long QuotaNonPagedPoolUsage { get { return (long)GetAddressAt(HostOffset(52, 9)); } }
        public long PrivatePageCount { get { return (long)GetAddressAt(HostOffset(56, 10)); } }

        #region Private
        internal ProcessCtrTraceData(Action<ProcessCtrTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ProcessCtrTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 2 && EventDataLength != HostOffset(60, 11)));
            Debug.Assert(!(Version > 2 && EventDataLength < HostOffset(60, 11)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ProcessID", ProcessID);
            XmlAttrib(sb, "MemoryCount", MemoryCount);
            XmlAttrib(sb, "HandleCount", HandleCount);
            XmlAttribHex(sb, "PeakVirtualSize", PeakVirtualSize);
            XmlAttribHex(sb, "PeakWorkingSetSize", PeakWorkingSetSize);
            XmlAttribHex(sb, "PeakPagefileUsage", PeakPagefileUsage);
            XmlAttribHex(sb, "QuotaPeakPagedPoolUsage", QuotaPeakPagedPoolUsage);
            XmlAttribHex(sb, "QuotaPeakNonPagedPoolUsage", QuotaPeakNonPagedPoolUsage);
            XmlAttribHex(sb, "VirtualSize", VirtualSize);
            XmlAttribHex(sb, "WorkingSetSize", WorkingSetSize);
            XmlAttribHex(sb, "PagefileUsage", PagefileUsage);
            XmlAttribHex(sb, "QuotaPagedPoolUsage", QuotaPagedPoolUsage);
            XmlAttribHex(sb, "QuotaNonPagedPoolUsage", QuotaNonPagedPoolUsage);
            XmlAttribHex(sb, "PrivatePageCount", PrivatePageCount);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "ProcessID", "MemoryCount", "HandleCount", "PeakVirtualSize", "PeakWorkingSetSize", "PeakPagefileUsage", "QuotaPeakPagedPoolUsage", "QuotaPeakNonPagedPoolUsage", "VirtualSize", "WorkingSetSize", "PagefileUsage", "QuotaPagedPoolUsage", "QuotaNonPagedPoolUsage", "PrivatePageCount" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ProcessID;
                case 1:
                    return MemoryCount;
                case 2:
                    return HandleCount;
                case 3:
                    return PeakVirtualSize;
                case 4:
                    return PeakWorkingSetSize;
                case 5:
                    return PeakPagefileUsage;
                case 6:
                    return QuotaPeakPagedPoolUsage;
                case 7:
                    return QuotaPeakNonPagedPoolUsage;
                case 8:
                    return VirtualSize;
                case 9:
                    return WorkingSetSize;
                case 10:
                    return PagefileUsage;
                case 11:
                    return QuotaPagedPoolUsage;
                case 12:
                    return QuotaNonPagedPoolUsage;
                case 13:
                    return PrivatePageCount;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ProcessCtrTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;

        internal override unsafe void FixupData()
        {
            Debug.Assert(eventRecord->EventHeader.ProcessId == -1);
            eventRecord->EventHeader.ProcessId = GetInt32At(0);
        }

        #endregion
    }
    public sealed class ThreadTraceData : TraceEvent
    {
        // public int ThreadID { get { if (Version >= 1) return GetInt32At(4); return GetInt32At(0); } }
        // public int ProcessID { get { if (Version >= 1) return GetInt32At(0); return GetInt32At(4); } }
        public Address StackBase { get { if (Version >= 2) { return GetAddressAt(8); } return 0; } }
        public Address StackLimit { get { if (Version >= 2) { return GetAddressAt(HostOffset(12, 1)); } return 0; } }
        public Address UserStackBase { get { if (Version >= 2) { return GetAddressAt(HostOffset(16, 2)); } return 0; } }
        public Address UserStackLimit { get { if (Version >= 2) { return GetAddressAt(HostOffset(20, 3)); } return 0; } }
        public Address StartAddr { get { if (Version >= 2) { return GetAddressAt(HostOffset(24, 4)); } return 0; } }
        public Address Win32StartAddr { get { if (Version >= 2) { return GetAddressAt(HostOffset(28, 5)); } return 0; } }
        // Not present in V2 public int WaitMode { get { if (Version >= 1) return GetByteAt(HostOffset(32, 6)); return 0; } }
        public Address TebBase { get { if (Version >= 2) { return GetAddressAt(HostOffset(32, 6)); } return 0; } }
        public int SubProcessTag { get { if (Version >= 2) { return GetInt32At(HostOffset(36, 7)); } return 0; } }
        public int BasePriority { get { if (Version >= 3 && EventDataLength >= HostOffset(41, 7)) { return GetByteAt(HostOffset(40, 7)); } return 0; } }
        public int PagePriority { get { if (Version >= 3 && EventDataLength >= HostOffset(42, 7)) { return GetByteAt(HostOffset(41, 7)); } return 0; } }
        public int IoPriority { get { if (Version >= 3 && EventDataLength >= HostOffset(43, 7)) { return GetByteAt(HostOffset(42, 7)); } return 0; } }
        public int ThreadFlags { get { if (Version >= 3 && EventDataLength >= HostOffset(44, 7)) { return GetByteAt(HostOffset(43, 7)); } return 0; } }
        public string ThreadName { get { if (Version >= 3 && EventDataLength >= HostOffset(46, 7)) { return GetUnicodeStringAt(HostOffset(44, 7)); } return ""; } }

        // The thread that started this thread (only in start events 
        public int ParentThreadID
        {
            get
            {
                if (Version < 2 || Source is ETWReloggerTraceEventSource)
                {
                    return -1;
                }

                return GetInt32At(4);   // This is not the standard location see FixupData, we swap the ThreadIDs   See FixupData 
                ;
            }
        }
        public int ParentProcessID
        {
            get
            {
                if (Version < 2 || Source is ETWReloggerTraceEventSource)
                {
                    return -1;
                }

                return GetInt32At(0);   // This is not the standard location see FixupData, we swap the Process ID   See FixupData 
                ;
            }
        }
        #region Private
        internal ThreadTraceData(Action<ThreadTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ThreadTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 8));
            Debug.Assert(!(Version == 1 && EventDataLength < 8));        // TODO fixed by hand (can be better)
            Debug.Assert(!(Version == 2 && EventDataLength != HostOffset(40, 7)));
            Debug.Assert(!(Version > 2 && EventDataLength < HostOffset(40, 7)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ThreadName", ThreadName);
            XmlAttribHex(sb, "StackBase", StackBase);
            XmlAttribHex(sb, "StackLimit", StackLimit);
            XmlAttribHex(sb, "UserStackBase", UserStackBase);
            XmlAttribHex(sb, "UserStackLimit", UserStackLimit);
            XmlAttribHex(sb, "StartAddr", StartAddr);
            XmlAttribHex(sb, "Win32StartAddr", Win32StartAddr);
            XmlAttribHex(sb, "TebBase", TebBase);
            XmlAttribHex(sb, "SubProcessTag", SubProcessTag);
            XmlAttribHex(sb, "ParentThreadID", ParentThreadID);
            XmlAttribHex(sb, "ParentProcessID", ParentProcessID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "StackBase", "StackLimit", "UserStackBase", "UserStackLimit",
                        "StartAddr", "Win32StartAddr", "TebBase", "SubProcessTag",
                        "BasePriority", "PagePriority", "IoPriority", "ThreadFlags", "ThreadName", "ParentThreadID", "ParentProcessID"
                    };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return StackBase;
                case 1:
                    return StackLimit;
                case 2:
                    return UserStackBase;
                case 3:
                    return UserStackLimit;
                case 4:
                    return StartAddr;
                case 5:
                    return Win32StartAddr;
                case 6:
                    return TebBase;
                case 7:
                    return SubProcessTag;
                case 8:
                    return BasePriority;
                case 9:
                    return PagePriority;
                case 10:
                    return IoPriority;
                case 11:
                    return ThreadFlags;
                case 12:
                    return ThreadName;
                case 13:
                    return ParentThreadID;
                case 14:
                    return ParentProcessID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ThreadTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;

        internal override unsafe void FixupData()
        {
            if (Version < 2)
            {
                return;
            }

            // We wish to create the illusion that the events are reported by the thread being started.   
            var parentProcess = -1;
            ParentThread = -1;
            if (Opcode != TraceEventOpcode.Stop)                            // Stop events do have the correct ThreadID, so keep it
            {
                if (Opcode == TraceEventOpcode.Start)
                {
                    parentProcess = eventRecord->EventHeader.ProcessId;
                    ParentThread = eventRecord->EventHeader.ThreadId;      // This field is transient (does not survive ETLX conversion) (we may be able to remove)
                }
                eventRecord->EventHeader.ThreadId = GetInt32At(4);          // Thread being started.  
                eventRecord->EventHeader.ProcessId = GetInt32At(0);
            }

            // We are doing something questionable here.   We are repurposing fields (the ThreadId and ProcessId fields)
            // to be new things (the ParentProcessID and ParentThreadId.   This works fine except for the case of
            // the relogger, because in that case we don't want to change the fields (since they will be written via
            // the relogger).   Thus we give up providing the ParentProcessID and ParentThreadID fields in the
            // case of ETWReloggerTraceEventSource (they always return -1). 
            if (!(Source is ETWReloggerTraceEventSource))
            {
                ((int*)DataStart)[0] = parentProcess;                           // Use offset 0 to now hold the ParentProcessID.  
                ((int*)DataStart)[1] = ParentThread;                            // Use offset 4 to now hold the ParentThreadID.  
            }
        }

        /// <summary>
        /// Indicate that StartAddr and Win32StartAddr are a code addresses that needs symbolic information
        /// </summary>
        internal override bool LogCodeAddresses(Func<TraceEvent, Address, bool> callBack)
        {
            // TODO is this one worth resolving?
            // callBack(this, Win32StartAddr);
            return true;
        }
        #endregion
    }

    public sealed class ThreadSetNameTraceData : TraceEvent
    {
        // public int ProcessID { get { return GetInt32At(0); } }
        // public int ThreadID { get { return GetInt32At(4); } }

        public string ThreadName { get { return GetUnicodeStringAt(8); } }

        #region Private
        internal ThreadSetNameTraceData(Action<ThreadSetNameTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ThreadSetNameTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(Version >= 2 && EventDataLength >= SkipUnicodeString(8));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ThreadName", ThreadName);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "ThreadName" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ThreadName;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ThreadSetNameTraceData> Action;

        internal override unsafe void FixupData()
        {
            Debug.Assert(eventRecord->EventHeader.ProcessId == -1);
            eventRecord->EventHeader.ProcessId = GetInt32At(0);
            eventRecord->EventHeader.ThreadId = GetInt32At(4);
        }

        #endregion
    }
    public sealed class CSwitchTraceData : TraceEvent
    {
        public enum ThreadWaitMode
        {
            NonSwap = 0,
            Swappable = 1,
        };

        /// <summary>
        /// We report a context switch from from the new thread.  Thus NewThreadID == ThreadID.  
        /// </summary>
        public int NewThreadID { get { return ThreadID; } }
        public int NewProcessID { get { return ProcessID; } }
        public string NewProcessName { get { return ProcessName; } }
        public int OldThreadID { get { return GetInt32At(4); } }
        public int NewThreadPriority { get { return GetByteAt(8); } }
        public int OldThreadPriority { get { return GetByteAt(9); } }
        public int OldProcessID { get { return state.ThreadIDToProcessID(OldThreadID, TimeStampQPC); } }
        public string OldProcessName { get { return traceEventSource.ProcessName(OldProcessID, TimeStampQPC); } }
        // TODO figure out which one of these are right
        public int NewThreadQuantum { get { return GetByteAt(10); } }
        public int OldThreadQuantum { get { return GetByteAt(11); } }

        // public int PreviousCState { get { return GetByteAt(10); } }
        // public int SpareByte { get { return GetByteAt(11); } }

        public ThreadWaitReason OldThreadWaitReason { get { return (ThreadWaitReason)GetByteAt(0xc); } }
        public ThreadWaitMode OldThreadWaitMode { get { return (ThreadWaitMode)GetByteAt(0xd); } }
        public ThreadState OldThreadState { get { return (ThreadState)GetByteAt(0xe); } }
        public int OldThreadWaitIdealProcessor { get { return GetByteAt(15); } }
        public int NewThreadWaitTime { get { return GetInt32At(16); } }
        // Skipping Reserved

        #region Private
        internal CSwitchTraceData(Action<CSwitchTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<CSwitchTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 2 && EventDataLength != 24));
            Debug.Assert(!(Version > 2 && EventDataLength < 24));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "OldThreadID", OldThreadID);
            XmlAttrib(sb, "OldProcessID", OldProcessID);
            XmlAttrib(sb, "OldProcessName", OldProcessName);
            XmlAttrib(sb, "NewThreadPriority", NewThreadPriority);
            XmlAttrib(sb, "OldThreadPriority", OldThreadPriority);
            XmlAttrib(sb, "NewThreadQuantum", NewThreadQuantum);
            XmlAttrib(sb, "OldThreadQuantum", OldThreadQuantum);
            XmlAttrib(sb, "OldThreadWaitReason", OldThreadWaitReason);
            XmlAttrib(sb, "OldThreadWaitMode", OldThreadWaitMode);
            XmlAttrib(sb, "OldThreadState", OldThreadState);
            XmlAttrib(sb, "OldThreadWaitIdealProcessor", OldThreadWaitIdealProcessor);
            XmlAttribHex(sb, "NewThreadWaitTime", NewThreadWaitTime);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "OldThreadID", "OldProcessID", "OldProcessName",
                        "NewThreadID", "NewProcessID", "NewProcessName", "ProcessorNumber",
                        "NewThreadPriority", "OldThreadPriority", "NewThreadQuantum", "OldThreadQuantum",
                        "OldThreadWaitReason", "OldThreadWaitMode", "OldThreadState", "OldThreadWaitIdealProcessor",
                        "NewThreadWaitTime" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return OldThreadID;
                case 1:
                    return OldProcessID;
                case 2:
                    return OldProcessName;
                case 3:
                    return NewThreadID;
                case 4:
                    return NewProcessID;
                case 5:
                    return NewProcessName;
                case 6:
                    return ProcessorNumber;
                case 7:
                    return NewThreadPriority;
                case 8:
                    return OldThreadPriority;
                case 9:
                    return NewThreadQuantum;
                case 10:
                    return OldThreadQuantum;
                case 11:
                    return OldThreadWaitReason;
                case 12:
                    return OldThreadWaitMode;
                case 13:
                    return OldThreadState;
                case 14:
                    return OldThreadWaitIdealProcessor;
                case 15:
                    return NewThreadWaitTime;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<CSwitchTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;

        internal override unsafe void FixupData()
        {
            if (eventRecord->EventHeader.ThreadId == -1)
            {
                eventRecord->EventHeader.ThreadId = GetInt32At(0);
            }

            if (eventRecord->EventHeader.ProcessId == -1)
            {
                eventRecord->EventHeader.ProcessId = state.ThreadIDToProcessID(ThreadID, TimeStampQPC);
            }
        }

        public override unsafe int ProcessID
        {
            get
            {
                // We try to fix up the process ID 'on the fly' however in the case of a circular buffer,
                // We simply don't know the process ID until the end of the trace.  Thus you to check and
                // possibly try again.  
                var ret = eventRecord->EventHeader.ProcessId;
                if (ret == -1)
                {
                    ret = state.ThreadIDToProcessID(ThreadID, TimeStampQPC);
                }

                return ret;
            }
        }

        private string ToString(ThreadState state)
        {
            switch (state)
            {
                case ThreadState.Initialized: return "Initialized";
                case ThreadState.Ready: return "Ready";
                case ThreadState.Running: return "Running";
                case ThreadState.Standby: return "Standby";
                case ThreadState.Terminated: return "Terminated";
                case ThreadState.Wait: return "Wait";
                case ThreadState.Transition: return "Transition";
                case ThreadState.Unknown: return "Unknown";
                default: return ((int)state).ToString();
            }
        }
        private object ToString(ThreadWaitMode mode)
        {
            switch (mode)
            {
                case ThreadWaitMode.NonSwap: return "NonSwap";
                case ThreadWaitMode.Swappable: return "Swappable";
                default: return ((int)mode).ToString();
            }
        }
        private object ToString(ThreadWaitReason reason)
        {
            switch (reason)
            {
                case ThreadWaitReason.Executive: return "Executive";
                case ThreadWaitReason.FreePage: return "FreePage";
                case ThreadWaitReason.PageIn: return "PageIn";
                case ThreadWaitReason.SystemAllocation: return "SystemAllocation";
                case ThreadWaitReason.ExecutionDelay: return "ExecutionDelay";
                case ThreadWaitReason.Suspended: return "Suspended";
                case ThreadWaitReason.UserRequest: return "UserRequest";
                case ThreadWaitReason.EventPairHigh: return "EventPairHigh";
                case ThreadWaitReason.EventPairLow: return "EventPairLow";
                case ThreadWaitReason.LpcReceive: return "LpcReceive";
                case ThreadWaitReason.LpcReply: return "LpcReply";
                case ThreadWaitReason.VirtualMemory: return "VirtualMemory";
                case ThreadWaitReason.PageOut: return "PageOut";
                case ThreadWaitReason.Unknown: return "Unknown";
                default: return ((int)reason).ToString();
            }
        }
        #endregion
    }

    public sealed class EnqueueTraceData : TraceEvent
    {
        public Address Entry { get { return (Address)GetInt64At(0); } }
        #region Private
        internal EnqueueTraceData(Action<EnqueueTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
            this.state = state;
        }

        internal override unsafe void FixupData()
        {
            if (eventRecord->EventHeader.ThreadId == -1)
            {
                eventRecord->EventHeader.ThreadId = GetInt32At(8);
            }

            if (eventRecord->EventHeader.ProcessId == -1)
            {
                eventRecord->EventHeader.ProcessId = state.ThreadIDToProcessID(ThreadID, TimeStampQPC);
            }
        }

        public override unsafe int ProcessID
        {
            get
            {
                // We try to fix up the process ID 'on the fly' however in the case of a circular buffer,
                // We simply don't know the process ID until the end of the trace.  Thus you to check and
                // possibly try again.  
                var ret = eventRecord->EventHeader.ProcessId;
                if (ret == -1)
                {
                    ret = state.ThreadIDToProcessID(ThreadID, TimeStampQPC);
                }

                return ret;
            }
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<EnqueueTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "Entry", Entry);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Entry" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Entry;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<EnqueueTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }

    public sealed class DequeueTraceData : TraceEvent
    {
        public int Count { get { return GetInt32At(4); } }
        public Address Entry(int idx) { return (Address)GetInt64At(8 + 8 * idx); }

        #region Private
        internal DequeueTraceData(Action<DequeueTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
            this.state = state;
        }

        internal override unsafe void FixupData()
        {
            if (eventRecord->EventHeader.ThreadId == -1)
            {
                eventRecord->EventHeader.ThreadId = GetInt32At(0);
            }

            if (eventRecord->EventHeader.ProcessId == -1)
            {
                eventRecord->EventHeader.ProcessId = state.ThreadIDToProcessID(ThreadID, TimeStampQPC);
            }
        }
        public override unsafe int ProcessID
        {
            get
            {
                // We try to fix up the process ID 'on the fly' however in the case of a circular buffer,
                // We simply don't know the process ID until the end of the trace.  Thus you to check and
                // possibly try again.  
                var ret = eventRecord->EventHeader.ProcessId;
                if (ret == -1)
                {
                    ret = state.ThreadIDToProcessID(ThreadID, TimeStampQPC);
                }

                return ret;
            }
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<DequeueTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "Count", Count);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Count", "FirstEntry" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Count;
                case 1:
                    return Entry(0);
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<DequeueTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }

#if false
    public sealed class WorkerThreadTraceData : TraceEvent
    {
        public int TThreadID { get { return GetInt32At(0); } }
        public DateTime StartTime { get { return DateTime.FromFileTime(GetInt64At(4)); } }
        public Address ThreadRoutine { get { return GetAddressAt(12); } }

    #region Private
        internal WorkerThreadTraceData(Action<WorkerThreadTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            this.Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<WorkerThreadTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 2 && EventDataLength != HostOffset(16, 1)));
            Debug.Assert(!(Version > 2 && EventDataLength < HostOffset(16, 1)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "TThreadID", TThreadID);
            XmlAttrib(sb, "StartTime", StartTime);
            XmlAttribHex(sb, "ThreadRoutine", ThreadRoutine);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[] { "TThreadID", "StartTime", "ThreadRoutine" };
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return TThreadID;
                case 1:
                    return StartTime;
                case 2:
                    return ThreadRoutine;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<WorkerThreadTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
    #endregion
    }
    public sealed class ReserveCreateTraceData : TraceEvent
    {
        public Address Reserve { get { return GetAddressAt(0); } }
        public int Period { get { return GetInt32At(HostOffset(4, 1)); } }
        public int Budget { get { return GetInt32At(HostOffset(8, 1)); } }
        public int ObjectFlags { get { return GetInt32At(HostOffset(12, 1)); } }
        public int Processor { get { return GetByteAt(HostOffset(16, 1)); } }

    #region Private
        internal ReserveCreateTraceData(Action<ReserveCreateTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            this.Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ReserveCreateTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 2 && EventDataLength != HostOffset(17, 1)));
            // TODO FIX NOW Version 3 is smaller.  Debug.Assert(!(Version > 2 && EventDataLength < HostOffset(17, 1)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "Reserve", Reserve);
            XmlAttrib(sb, "Period", Period);
            XmlAttrib(sb, "Budget", Budget);
            XmlAttrib(sb, "ObjectFlags", ObjectFlags);
            XmlAttrib(sb, "Processor", Processor);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[] { "Reserve", "Period", "Budget", "ObjectFlags", "Processor" };
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Reserve;
                case 1:
                    return Period;
                case 2:
                    return Budget;
                case 3:
                    return ObjectFlags;
                case 4:
                    return Processor;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ReserveCreateTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
    #endregion
    }
    public sealed class ReserveDeleteTraceData : TraceEvent
    {
        public Address Reserve { get { return GetAddressAt(0); } }

    #region Private
        internal ReserveDeleteTraceData(Action<ReserveDeleteTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            this.Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ReserveDeleteTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 2 && EventDataLength != HostOffset(4, 1)));
            Debug.Assert(!(Version > 2 && EventDataLength < HostOffset(4, 1)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "Reserve", Reserve);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[] { "Reserve" };
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Reserve;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ReserveDeleteTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
    #endregion
    }
    public sealed class ReserveJoinThreadTraceData : TraceEvent
    {
        public Address Reserve { get { return GetAddressAt(0); } }
        // public int TThreadID { get { return GetInt32At(HostOffset(4, 1)); } }   // This does not exist on Version 3 and above.  

    #region Private
        internal ReserveJoinThreadTraceData(Action<ReserveJoinThreadTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            this.Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ReserveJoinThreadTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 2 && EventDataLength != HostOffset(8, 1)));
            Debug.Assert(!(Version > 2 && EventDataLength < HostOffset(4, 1)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "Reserve", Reserve);
            // XmlAttrib(sb, "TThreadID", TThreadID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[] { "Reserve" };
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Reserve;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ReserveJoinThreadTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
    #endregion
    }
    public sealed class ReserveDisjoinThreadTraceData : TraceEvent
    {
        public Address Reserve { get { return GetAddressAt(0); } }
        public int TThreadID { get { return GetInt32At(HostOffset(4, 1)); } }

    #region Private
        internal ReserveDisjoinThreadTraceData(Action<ReserveDisjoinThreadTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            this.Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ReserveDisjoinThreadTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 2 && EventDataLength != HostOffset(8, 1)));
            Debug.Assert(!(Version > 2 && EventDataLength < HostOffset(8, 1)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "Reserve", Reserve);
            XmlAttrib(sb, "TThreadID", TThreadID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[] { "Reserve", "TThreadID" };
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Reserve;
                case 1:
                    return TThreadID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ReserveDisjoinThreadTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
    #endregion
    }
    public sealed class ReserveStateTraceData : TraceEvent
    {
        public Address Reserve { get { return GetAddressAt(0); } }
        public int DispatchState { get { return GetByteAt(HostOffset(4, 1)); } }
        public bool Replenished { get { return GetByteAt(HostOffset(5, 1)) != 0; } }

    #region Private
        internal ReserveStateTraceData(Action<ReserveStateTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            this.Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ReserveStateTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 2 && EventDataLength != HostOffset(6, 1)));
            Debug.Assert(!(Version > 2 && EventDataLength < HostOffset(6, 1)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "Reserve", Reserve);
            XmlAttrib(sb, "DispatchState", DispatchState);
            XmlAttrib(sb, "Replenished", Replenished);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[] { "Reserve", "DispatchState", "Replenished" };
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Reserve;
                case 1:
                    return DispatchState;
                case 2:
                    return Replenished;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ReserveStateTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
    #endregion
    }
    public sealed class ReserveBandwidthTraceData : TraceEvent
    {
        public Address Reserve { get { return GetAddressAt(0); } }
        public int Period { get { return GetInt32At(HostOffset(4, 1)); } }
        public int Budget { get { return GetInt32At(HostOffset(8, 1)); } }

    #region Private
        internal ReserveBandwidthTraceData(Action<ReserveBandwidthTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            this.Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ReserveBandwidthTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 2 && EventDataLength != HostOffset(12, 1)));
            Debug.Assert(!(Version > 2 && EventDataLength < HostOffset(12, 1)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "Reserve", Reserve);
            XmlAttrib(sb, "Period", Period);
            XmlAttrib(sb, "Budget", Budget);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[] { "Reserve", "Period", "Budget" };
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Reserve;
                case 1:
                    return Period;
                case 2:
                    return Budget;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ReserveBandwidthTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
    #endregion
    }
    public sealed class ReserveLateCountTraceData : TraceEvent
    {
        public Address Reserve { get { return GetAddressAt(0); } }
        public int LateCountIncrement { get { return GetInt32At(HostOffset(4, 1)); } }

    #region Private
        internal ReserveLateCountTraceData(Action<ReserveLateCountTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            this.Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ReserveLateCountTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 2 && EventDataLength != HostOffset(8, 1)));
            Debug.Assert(!(Version > 2 && EventDataLength < HostOffset(8, 1)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "Reserve", Reserve);
            XmlAttrib(sb, "LateCountIncrement", LateCountIncrement);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[] { "Reserve", "LateCountIncrement" };
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Reserve;
                case 1:
                    return LateCountIncrement;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ReserveLateCountTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
    #endregion
    }
#endif
    public sealed class DiskIOTraceData : TraceEvent
    {
        public int DiskNumber { get { return GetInt32At(0); } }
        public IrpFlags IrpFlags { get { return ((IrpFlags)GetInt32At(4)) & ~IrpFlags.PriorityMask; } }
        public IOPriority Priority { get { return (IOPriority)((GetInt32At(4) >> 17) & 7); } }
        public int TransferSize { get { return GetInt32At(8); } }
        // Skipping Reserved
        public int Reserved { get { return GetInt32At(12); } }
        public long ByteOffset { get { return GetInt64At(16); } }
        public Address FileKey { get { return GetAddressAt(24); } }
        public string FileName { get { return state.FileIDToName(FileKey, TimeStampQPC); } }
        /// <summary>
        /// The I/O Response Packet address.  This represents the 'identity' of this particular I/O
        /// </summary>
        public Address Irp { get { return GetAddressAt(HostOffset(28, 1)); } }
        /// <summary>
        /// This is the time since the I/O was initiated, in source.PerfFreq (QPC) ticks.  
        /// </summary>
        private long HighResResponseTime { get { return GetInt64At(HostOffset(32, 2)); } }
        /// <summary>
        /// This is the actual time the disk spent servicing this IO.   Same as elapsed time for real time providers.  
        /// </summary>
        public double DiskServiceTimeMSec
        {
            get
            {
                double timeStampRelativeMSec = TimeStampRelativeMSec;
                int diskNum = DiskNumber;

                state.lazyDiskEventTimeStamp.FinishRead(); // We don't read diskEventTimeStamp from the disk unless we need to, check

                // Search the table for the last disk event for this disk
                double lastDiskIOTimeForDiskRelativeMSec = 0;        // If there is nothing there (or this is the first I/O) assume I/O happened log ago
                var diskEvents = state.diskEventTimeStamp;
                if (diskEvents.Count > 0)
                {
                    // See if we can start were we last left off.  
                    var idx = state.lastDiskEventIdx;
                    if (timeStampRelativeMSec <= diskEvents[idx].TimeStampRelativeMSec)
                    {
                        idx = 0;
                    }

                    while (idx < diskEvents.Count)
                    {
                        // Have we gone past this disk I/O
                        if (timeStampRelativeMSec <= diskEvents[idx].TimeStampRelativeMSec)
                        {
                            state.lastDiskEventIdx = idx;
                            break;
                        }
                        if (diskEvents[idx].DiskNum == diskNum)
                        {
                            lastDiskIOTimeForDiskRelativeMSec = diskEvents[idx].TimeStampRelativeMSec;
                        }

                        idx++;
                    }
                }
                return Math.Min(ElapsedTimeMSec, timeStampRelativeMSec - lastDiskIOTimeForDiskRelativeMSec);
            }
        }

        /// <summary>
        /// The time since the I/O was initiated.
        /// </summary>
        public double ElapsedTimeMSec
        {
            get
            {
                return HighResResponseTime * 1000.0 / traceEventSource.QPCFreq;
            }
        }
        // TODO you can get service time (what XPERF gives) by taking the minimum of 
        // the Elapsed time and the time of the completion of the last Disk event.  
        #region Private
        internal DiskIOTraceData(Action<DiskIOTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
            NeedsFixup = true;
        }
        internal override unsafe void FixupData()
        {
            if (eventRecord->EventHeader.ThreadId == -1 && HostOffset(44, 2) <= EventDataLength)
            {
                eventRecord->EventHeader.ThreadId = GetInt32At(HostOffset(40, 2));
            }

            if (eventRecord->EventHeader.ProcessId == -1)
            {
                eventRecord->EventHeader.ProcessId = state.ThreadIDToProcessID(ThreadID, TimeStampQPC);
            }
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<DiskIOTraceData>)value; }
        }
        protected internal override void Dispatch()
        {

            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(40, 2)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(40, 2)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "DiskNumber", DiskNumber);
            XmlAttrib(sb, "IrpFlags", IrpFlags);
            XmlAttrib(sb, "Priority", Priority);
            XmlAttribHex(sb, "TransferSize", TransferSize);
            XmlAttribHex(sb, "ByteOffset", ByteOffset);
            XmlAttribHex(sb, "FileKey", FileKey);
            XmlAttribHex(sb, "Irp", Irp);
            XmlAttrib(sb, "ElapsedTimeMSec", ElapsedTimeMSec.ToString("f4"));
            XmlAttrib(sb, "DiskServiceTimeMSec", DiskServiceTimeMSec.ToString("f4"));
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "DiskNumber", "IrpFlags", "Priority", "TransferSize", "ByteOffset", "Irp", "ElapsedTimeMSec", "DiskServiceTimeMSec", "FileKey", "FileName" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return DiskNumber;
                case 1:
                    return IrpFlags;
                case 2:
                    return Priority;
                case 3:
                    return TransferSize;
                case 4:
                    return ByteOffset;
                case 5:
                    return Irp;
                case 6:
                    return ElapsedTimeMSec;
                case 7:
                    return DiskServiceTimeMSec;
                case 8:
                    return FileKey;
                case 9:
                    return FileName;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<DiskIOTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class DiskIOInitTraceData : TraceEvent
    {
        public Address Irp { get { return GetAddressAt(0); } }

        #region Private
        internal DiskIOInitTraceData(Action<DiskIOInitTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
            this.state = state;
        }

        internal override unsafe void FixupData()
        {
            if (Version >= 3 && eventRecord->EventHeader.ThreadId == -1)
            {
                eventRecord->EventHeader.ThreadId = GetInt32At(HostOffset(4, 1));
            }

            if (eventRecord->EventHeader.ProcessId == -1)
            {
                eventRecord->EventHeader.ProcessId = state.ThreadIDToProcessID(ThreadID, TimeStampQPC);
            }
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<DiskIOInitTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(4, 1)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(4, 1)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "Irp", Irp);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Irp" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Irp;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<DiskIOInitTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class DiskIOFlushBuffersTraceData : TraceEvent
    {
        public int DiskNumber { get { return GetInt32At(0); } }
        public IrpFlags IrpFlags { get { return ((IrpFlags)GetInt32At(4)) & ~IrpFlags.PriorityMask; } }

        /// <summary>
        /// This is the time since the I/O was initiated, in source.PerfFreq (QPC) ticks.  
        /// </summary>
        private long HighResResponseTime { get { return GetInt64At(8); } }
        /// <summary>
        /// The time since the I/O was initiated.  
        /// </summary>
        public double ElapsedTimeMSec
        {
            get
            {
                return HighResResponseTime * 1000.0 / traceEventSource.QPCFreq;
            }
        }
        public Address Irp { get { return GetAddressAt(16); } }

        #region Private
        internal DiskIOFlushBuffersTraceData(Action<DiskIOFlushBuffersTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
            this.state = state;
        }

        internal override unsafe void FixupData()
        {
            if (Version >= 3 && eventRecord->EventHeader.ThreadId == -1)
            {
                eventRecord->EventHeader.ThreadId = GetInt32At(HostOffset(4, 1));
            }

            if (eventRecord->EventHeader.ProcessId == -1)
            {
                eventRecord->EventHeader.ProcessId = state.ThreadIDToProcessID(ThreadID, TimeStampQPC);
            }
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<DiskIOFlushBuffersTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(20, 1)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(20, 1)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "DiskNumber", DiskNumber);
            XmlAttrib(sb, "IrpFlags", IrpFlags);
            XmlAttribHex(sb, "Irp", Irp);
            XmlAttribHex(sb, "ElapsedTimeMSec", Irp);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "DiskNumber", "IrpFlags", "Irp", "ElapsedTimeMSec" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return DiskNumber;
                case 1:
                    return IrpFlags;
                case 2:
                    return Irp;
                case 3:
                    return ElapsedTimeMSec;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<DiskIOFlushBuffersTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }

    public enum IOPriority
    {
        Notset = 0,
        Verylow = 1,
        Low = 2,
        Normal = 3,
        High = 4,
        Critical = 5,
        Reserved0 = 6,
        Reserved1 = 7,
        Max = 8
    }

    [Flags]
    public enum IrpFlags
    {
        None = 0x0,
        Nocache = 0x00000001,
        PagingIo = 0x00000002,
        MountCompletion = 0x00000002,
        SynchronousApi = 0x00000004,
        AssociatedIrp = 0x00000008,
        BufferedIO = 0x00000010,
        DeallocateBuffer = 0x00000020,
        InputOperation = 0x00000040,
        SynchronousPagingIO = 0x00000040,
        Create = 0x00000080,
        Read = 0x00000100,
        Write = 0x00000200,
        Close = 0x00000400,
        DeferIOCompletion = 0x00000800,
        ObQueryName = 0x00001000,
        HoldDeviceQueue = 0x00002000,

        PriorityMask = 0xe0000,        // 3 bits represent I/O priority 
    };

    public sealed class DriverMajorFunctionCallTraceData : TraceEvent
    {
        public int MajorFunction { get { return GetInt32At(0); } }
        public int MinorFunction { get { return GetInt32At(4); } }
        public Address RoutineAddr { get { return GetAddressAt(8); } }
        public Address FileKey { get { return GetAddressAt(HostOffset(12, 1)); } }
        public string FileName { get { return state.FileIDToName(FileKey, TimeStampQPC); } }
        public Address Irp { get { return GetAddressAt(HostOffset(16, 2)); } }
        public int UniqMatchID { get { return GetInt32At(HostOffset(20, 3)); } }

        #region Private
        internal DriverMajorFunctionCallTraceData(Action<DriverMajorFunctionCallTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<DriverMajorFunctionCallTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(24, 3)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(24, 3)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "MajorFunction", MajorFunction);
            XmlAttrib(sb, "MinorFunction", MinorFunction);
            XmlAttribHex(sb, "RoutineAddr", RoutineAddr);
            XmlAttribHex(sb, "FileKey", FileKey);
            XmlAttribHex(sb, "Irp", Irp);
            XmlAttrib(sb, "UniqMatchID", UniqMatchID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "MajorFunction", "MinorFunction", "RoutineAddr", "FileKey", "Irp", "UniqMatchID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return MajorFunction;
                case 1:
                    return MinorFunction;
                case 2:
                    return RoutineAddr;
                case 3:
                    return FileKey;
                case 4:
                    return Irp;
                case 5:
                    return UniqMatchID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<DriverMajorFunctionCallTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class DriverMajorFunctionReturnTraceData : TraceEvent
    {
        public Address Irp { get { return GetAddressAt(0); } }
        public int UniqMatchID { get { return GetInt32At(HostOffset(4, 1)); } }

        #region Private
        internal DriverMajorFunctionReturnTraceData(Action<DriverMajorFunctionReturnTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<DriverMajorFunctionReturnTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(8, 1)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(8, 1)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "Irp", Irp);
            XmlAttrib(sb, "UniqMatchID", UniqMatchID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Irp", "UniqMatchID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Irp;
                case 1:
                    return UniqMatchID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<DriverMajorFunctionReturnTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class DriverCompletionRoutineTraceData : TraceEvent
    {
        public Address Routine { get { return GetAddressAt(0); } }
        public Address IrpPtr { get { return GetAddressAt(HostOffset(4, 1)); } }
        public int UniqMatchID { get { return GetInt32At(HostOffset(8, 2)); } }

        #region Private
        internal DriverCompletionRoutineTraceData(Action<DriverCompletionRoutineTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<DriverCompletionRoutineTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(12, 2)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(12, 2)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "Routine", Routine);
            XmlAttribHex(sb, "IrpPtr", IrpPtr);
            XmlAttrib(sb, "UniqMatchID", UniqMatchID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Routine", "IrpPtr", "UniqMatchID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Routine;
                case 1:
                    return IrpPtr;
                case 2:
                    return UniqMatchID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<DriverCompletionRoutineTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class DriverCompleteRequestTraceData : TraceEvent
    {
        public Address RoutineAddr { get { return GetAddressAt(0); } }
        public Address Irp { get { return GetAddressAt(HostOffset(4, 1)); } }
        public int UniqMatchID { get { return GetInt32At(HostOffset(8, 2)); } }

        #region Private
        internal DriverCompleteRequestTraceData(Action<DriverCompleteRequestTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<DriverCompleteRequestTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(12, 2)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(12, 2)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "RoutineAddr", RoutineAddr);
            XmlAttribHex(sb, "Irp", Irp);
            XmlAttrib(sb, "UniqMatchID", UniqMatchID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "RoutineAddr", "Irp", "UniqMatchID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return RoutineAddr;
                case 1:
                    return Irp;
                case 2:
                    return UniqMatchID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<DriverCompleteRequestTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class DriverCompleteRequestReturnTraceData : TraceEvent
    {
        public Address Irp { get { return GetAddressAt(0); } }
        public int UniqMatchID { get { return GetInt32At(HostOffset(4, 1)); } }

        #region Private
        internal DriverCompleteRequestReturnTraceData(Action<DriverCompleteRequestReturnTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<DriverCompleteRequestReturnTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(8, 1)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(8, 1)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "Irp", Irp);
            XmlAttrib(sb, "UniqMatchID", UniqMatchID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Irp", "UniqMatchID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Irp;
                case 1:
                    return UniqMatchID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<DriverCompleteRequestReturnTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class RegistryTraceData : TraceEvent
    {
        private long InitialTimeQPC { get { if (Version >= 2) { return GetInt64At(0); } return 0; } }

        public double ElapsedTimeMSec { get { return TimeStampRelativeMSec - traceEventSource.QPCTimeToRelMSec(InitialTimeQPC); } }

        public int Status { get { if (Version >= 2) { GetInt32At(8); } return 0; } }

        public int Index { get { if (Version >= 2) { GetInt32At(12); } return 0; } }

        public Address KeyHandle { get { if (Version >= 2) { return GetAddressAt(16); } return 0; } }

        public string KeyName
        {
            get
            {
                if (Version < 2)
                {
                    return "";
                }

                // TODO All of this logic is suspect.   it could use a careful review.  
                if (NameIsKeyName(Opcode))
                {
                    string ret = GetUnicodeStringAt(HostOffset(20, 1));
                    if (ret.Length != 0)
                    {
                        return ret;
                    }
                }
                return state.FileIDToName(KeyHandle, TimeStampQPC);
            }
        }
        public string ValueName
        {
            get
            {
                if (NameIsKeyName(Opcode))
                {
                    return "";
                }
                else
                {
                    return GetUnicodeStringAt((Version < 2 ? HostOffset(0x14, 2) : HostOffset(0x14, 1)));
                }
            }
        }

        #region Private
        internal RegistryTraceData(Action<RegistryTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<RegistryTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUnicodeString(HostOffset(16, 2))));
            Debug.Assert(!(Version == 1 && EventDataLength != SkipUnicodeString(HostOffset(20, 2))));
            Debug.Assert(!(Version == 2 && EventDataLength != SkipUnicodeString(HostOffset(20, 1))));
            Debug.Assert(!(Version > 2 && EventDataLength < SkipUnicodeString(HostOffset(20, 1))));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "Status", Status);
            XmlAttribHex(sb, "KeyHandle", KeyHandle);
            XmlAttrib(sb, "ElapsedTimeMSec", ElapsedTimeMSec);
            XmlAttrib(sb, "KeyName", KeyName);
            XmlAttrib(sb, "Index", Index);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Status", "KeyHandle", "ElapsedTimeMSec", "KeyName", "ValueName", "Index" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Status;
                case 1:
                    return KeyHandle;
                case 2:
                    return ElapsedTimeMSec;
                case 3:
                    return KeyName;
                case 4:
                    return ValueName;
                case 5:
                    return Index;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<RegistryTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;

        internal static bool NameIsKeyName(TraceEventOpcode code)
        {
            // TODO confirm this is true
            switch ((int)code)
            {
                case 10: // Create
                case 11: // Open
                case 12: // Delete 
                    return true;
                case 13:    // Query
                case 14:    // SetValue
                case 15:    // DeleteValue
                case 16:    // QueryValue
                    return false;
                case 17:    // EnumerateKey
                    return true;
                case 18:    // EnumerateValueKey
                case 19:    // QueryMultipleValue
                    return false;
                case 20:    // SetInformation
                case 21:    // Flush
                case 22:    // KCBCreate
                case 23:    // KCBDelete
                case 24:    // KCBRundownBegin
                case 25:    // KCBRundownEnd
                case 26:    // Virtualize
                case 27:    // Close
                    return true;
                default:
                    Debug.Assert(false, "Unexpected Opcode");
                    return true;    // Seems the lesser of evils
            }
        }
        #endregion
    }
    public sealed class SplitIoInfoTraceData : TraceEvent
    {
        public Address ParentIrp { get { return GetAddressAt(0); } }
        public Address ChildIrp { get { return GetAddressAt(HostOffset(4, 1)); } }

        #region Private
        internal SplitIoInfoTraceData(Action<SplitIoInfoTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<SplitIoInfoTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(8, 2)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(8, 2)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "ParentIrp", ParentIrp);
            XmlAttribHex(sb, "ChildIrp", ChildIrp);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "ParentIrp", "ChildIrp" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ParentIrp;
                case 1:
                    return ChildIrp;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<SplitIoInfoTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class FileIONameTraceData : TraceEvent
    {
        /// <summary>
        /// This is a handle that represents a file NAME (not an open file).   
        /// In the MSDN does this field is called FileObject.  However in other events FileObject is something
        /// returned from Create file and is different.  Events have have both (and some do) use FileKey.  Thus
        /// I use FileKey uniformly to avoid confusion.   
        /// </summary>
        public Address FileKey { get { return GetAddressAt(0); } }
        public string FileName { get { return state.KernelToUser(GetUnicodeStringAt(HostOffset(4, 1))); } }

        #region Private
        internal FileIONameTraceData(Action<FileIONameTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<FileIONameTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUnicodeString(HostOffset(4, 1))));
            Debug.Assert(!(Version == 1 && EventDataLength != SkipUnicodeString(HostOffset(4, 1))));
            Debug.Assert(!(Version == 2 && EventDataLength != SkipUnicodeString(HostOffset(4, 1))));
            Debug.Assert(!(Version > 2 && EventDataLength < SkipUnicodeString(HostOffset(4, 1))));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "FileKey", FileKey);
            XmlAttrib(sb, "FileName", FileName);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "FileKey", "FileName" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return FileKey;
                case 1:
                    return FileName;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<FileIONameTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }

    public sealed class MapFileTraceData : TraceEvent
    {
        public Address ViewBase { get { return GetAddressAt(0); } }
        public Address FileKey { get { return GetAddressAt(HostOffset(4, 1)); } }
        public long MiscInfo { get { return GetInt64At(HostOffset(8, 2)); } }
        public Address ViewSize { get { return GetAddressAt(HostOffset(16, 2)); } }
        public string FileName { get { return state.FileIDToName(FileKey, TimeStampQPC); } }

        // In Version 3 we have byte offset field 
        public long ByteOffset
        {
            get
            {
                if (Version < 3)
                {
                    return 0;
                }
                else
                {
                    return GetInt64At(HostOffset(20, 3));
                }
            }
        }

        // TODO I am not actually that certain of this parsing.   Which Version ByteOffset got put in, and what the layout is on 32 bit.
        // but this does work on Win 10 (which uses Version 3) and for 64 bit which is the most important.    
        // Process ID = Version < 3 ? GetInt32At(HostOffset(20, 3)) : GetInt32At(HostOffset(28, 3))

        #region Private
        internal MapFileTraceData(Action<MapFileTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<MapFileTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version >= 0 && EventDataLength < HostOffset(20, 3) + 4));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "ViewBase", ViewBase);
            XmlAttribHex(sb, "FileKey", FileKey);
            XmlAttribHex(sb, "MiscInfo", MiscInfo);
            XmlAttribHex(sb, "ViewSize", ViewSize);
            XmlAttribHex(sb, "ByteOffset", ByteOffset);
            XmlAttrib(sb, "FileName", FileName);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "ViewBase", "FileKey", "MiscInfo", "ViewSize", "ByteOffset", "FileName" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ViewBase;
                case 1:
                    return FileKey;
                case 2:
                    return MiscInfo;
                case 3:
                    return ViewSize;
                case 4:
                    return ByteOffset;
                case 5:
                    return FileName;

                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        internal override unsafe void FixupData()
        {
            int processIDFromEvent = Version < 3 ? GetInt32At(HostOffset(20, 3)) : GetInt32At(HostOffset(28, 3));
            Debug.Assert(eventRecord->EventHeader.ProcessId == -1 || eventRecord->EventHeader.ProcessId == processIDFromEvent);
            eventRecord->EventHeader.ProcessId = processIDFromEvent;
        }

        private event Action<MapFileTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;

        #endregion
    }

    public sealed class FileIOCreateTraceData : TraceEvent
    {
        // Pointer to fltmgr!_FLT_CALLBACK_DATA
        public Address IrpPtr { get { return GetAddressAt(0); } }
        // Pointer to nt!_FILE_OBJECT
        public Address FileObject { get { return GetAddressAt(LayoutVersion <= 2 ? HostOffset(8, 2) : HostOffset(4, 1)); } }
        // public Address TTID { get { return GetInt32At(Version <= 2 ? HostOffset(4, 1) : HostOffset(8, 2)); } }

        /// <summary>
        /// See the Windows CreateFile API CreateOptions for this 
        /// </summary>
        // _FLT_IO_PARAMETER_BLOCK.Create.Options
        // 24 lower bits are the Create Options
        public CreateOptions CreateOptions { get { return (CreateOptions)((GetInt32At(LayoutVersion <= 2 ? HostOffset(12, 3) : HostOffset(12, 2))) & 0xFFFFFF); } }

        /// <summary>
        /// See Windows CreateFile API CreateDisposition for this.  
        /// </summary>
        // _FLT_IO_PARAMETER_BLOCK.Create.Options
        // 8 higher bits are the Disposition as passed to IoCreateFileSpecifyDeviceObjectHint
        public CreateDisposition CreateDisposition { get { return (CreateDisposition)(GetByteAt(LayoutVersion <= 2 ? HostOffset(15, 3) : HostOffset(15, 2))); } }
        /// <summary>
        /// See Windows CreateFile API ShareMode parameter
        /// </summary>
        // _FLT_IO_PARAMETER_BLOCK.Create.FileAttributes
        public FileAttributes FileAttributes { get { return (FileAttributes)(GetInt32At(LayoutVersion <= 2 ? HostOffset(16, 3) : HostOffset(16, 2))); } }

        /// <summary>
        /// See windows CreateFile API ShareMode parameter
        /// </summary>
        // _FLT_IO_PARAMETER_BLOCK.Create.ShareAccess
        public FileShare ShareAccess { get { return (FileShare)(GetInt32At(LayoutVersion <= 2 ? HostOffset(20, 3) : HostOffset(20, 2))); } }
        public string FileName { get { return state.KernelToUser(GetUnicodeStringAt(LayoutVersion <= 2 ? HostOffset(24, 3) : HostOffset(24, 2))); } }
        public override unsafe int ProcessID
        {
            get
            {
                // We try to fix up the process ID 'on the fly' however in the case of a circular buffer,
                // We simply don't know the process ID until the end of the trace.  Thus you to check and
                // possibly try again.  
                var ret = eventRecord->EventHeader.ProcessId;
                if (ret == -1)
                {
                    ret = state.ThreadIDToProcessID(ThreadID, TimeStampQPC);
                }

                return ret;
            }
        }
        #region Private
        // The LayoutVersion is used to determine the field layout.  It is the version as seen by the Kernel
        // provider, but the Microsoft-Windows-Kernel-File provider has a different numbering scheme.
        private int LayoutVersion
        {
            get
            {
                // If it is classic, it is the kernel provider, otherwise it is the Microsoft-Windows-Kernel-File provider.  
                int ret = Version;
                if (!IsClassicProvider)
                {
                    ret += 2;
                }

                return ret;
            }
        }

        internal FileIOCreateTraceData(Action<FileIOCreateTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<FileIOCreateTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(LayoutVersion == 2 && EventDataLength != SkipUnicodeString(HostOffset(24, 3))));
            Debug.Assert(!(LayoutVersion == 3 && EventDataLength != SkipUnicodeString(HostOffset(24, 2))));
            Debug.Assert(!(LayoutVersion > 3 && EventDataLength < SkipUnicodeString(HostOffset(24, 2))));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "IrpPtr", IrpPtr);
            XmlAttribHex(sb, "FileObject", FileObject);
            XmlAttrib(sb, "CreateOptions", CreateOptions);
            XmlAttrib(sb, "CreateDisposition", CreateDisposition);
            XmlAttrib(sb, "FileAttributes", FileAttributes);
            XmlAttrib(sb, "ShareAccess", ShareAccess);
            XmlAttrib(sb, "FileName", FileName);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "IrpPtr", "FileObject", "CreateOptions", "CreateDisposition", "FileAttributes", "ShareAccess", "FileName" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return IrpPtr;
                case 1:
                    return FileObject;
                case 2:
                    return CreateOptions;
                case 3:
                    return CreateDisposition;
                case 4:
                    return FileAttributes;
                case 5:
                    return ShareAccess;
                case 6:
                    return FileName;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        internal override unsafe void FixupData()
        {
            if (eventRecord->EventHeader.ThreadId == -1)
            {
                eventRecord->EventHeader.ThreadId = GetInt32At(LayoutVersion <= 2 ? HostOffset(4, 1) : HostOffset(8, 2));
            }

            if (eventRecord->EventHeader.ProcessId == -1)
            {
                eventRecord->EventHeader.ProcessId = state.ThreadIDToProcessID(ThreadID, TimeStampQPC);
            }
        }
        private event Action<FileIOCreateTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }

    /// <summary>
    /// See Windows CreateFile function CreateDispostion parameter.
    /// The enum written to the ETW trace is the Disposition parameter passed to IoCreateFileSpecifyDeviceObjectHint.
    /// </summary>
    public enum CreateDisposition
    {
        SUPERSEDE = 0,          // FILE_SUPERSEDE - if the file exists, replace a file with another file.
        CREATE_NEW = 2,         // FILE_OPEN - Must NOT exist previously, otherwise fails
        CREATE_ALWAYS = 5,      // FILE_OVERWRITE_IF - Creates if necessary, trucates
        OPEN_EXISTING = 1,       // FILE_OPEN - Must exist previously otherwise fails.
        OPEN_ALWAYS = 3,        // FILE_OPEN_IF - Create if necessary, leaves data.
        TRUNCATE_EXISTING = 4,  // FILE_OVERWRITE - Must Exist previously, otherwise fails, truncates.  MOST WRITE OPENS USE THIS!
    }

    /// <summary>
    /// See Windows CreateFile function FlagsAndAttributes parameter. 
    /// TODO FIX NOW: these have not been validated yet.  
    /// </summary>
    [Flags]
    public enum CreateOptions
    {
        NONE = 0,
        FILE_ATTRIBUTE_ARCHIVE = (0x20),
        FILE_ATTRIBUTE_COMPRESSED = (0x800),
        FILE_ATTRIBUTE_DEVICE = (0x40),
        FILE_ATTRIBUTE_DIRECTORY = (0x10),
        FILE_ATTRIBUTE_ENCRYPTED = (0x4000),
        FILE_ATTRIBUTE_HIDDEN = (0x2),
        FILE_ATTRIBUTE_INTEGRITY_STREAM = (0x8000),
        FILE_ATTRIBUTE_NORMAL = (0x80),
        FILE_ATTRIBUTE_NOT_CONTENT_INDEXED = (0x2000),
        FILE_ATTRIBUTE_NO_SCRUB_DATA = (0x20000),
        FILE_ATTRIBUTE_OFFLINE = (0x1000),
        FILE_ATTRIBUTE_READONLY = (0x1),
        FILE_ATTRIBUTE_REPARSE_POINT = (0x400),
        FILE_ATTRIBUTE_SPARSE_FILE = (0x200),
        FILE_ATTRIBUTE_SYSTEM = (0x4),
        FILE_ATTRIBUTE_TEMPORARY = (0x100),
        FILE_ATTRIBUTE_VIRTUAL = (0x10000),
    };

    public sealed class FileIOSimpleOpTraceData : TraceEvent
    {
        public Address IrpPtr { get { return GetAddressAt(0); } }
        public Address FileObject { get { return GetAddressAt(Version <= 2 ? HostOffset(8, 2) : HostOffset(4, 1)); } }
        public string FileName { get { return state.FileIDToName(FileKey, FileObject, TimeStampQPC); } }
        public Address FileKey { get { return GetAddressAt(Version <= 2 ? HostOffset(12, 3) : HostOffset(8, 2)); } }
        // public Address TTID { get { return GetInt32At(Version <= 2 ? HostOffset(4, 1) : HostOffset(12, 3)); } }
        public override unsafe int ProcessID
        {
            get
            {
                // We try to fix up the process ID 'on the fly' however in the case of a circular buffer,
                // We simply don't know the process ID until the end of the trace.  Thus you to check and
                // possibly try again.  
                var ret = eventRecord->EventHeader.ProcessId;
                if (ret == -1)
                {
                    ret = state.ThreadIDToProcessID(ThreadID, TimeStampQPC);
                }

                return ret;
            }
        }
        #region Private
        internal FileIOSimpleOpTraceData(Action<FileIOSimpleOpTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<FileIOSimpleOpTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 2 && EventDataLength != HostOffset(16, 4)));
            Debug.Assert(!(Version == 3 && EventDataLength != HostOffset(16, 3)));
            Debug.Assert(!(Version > 3 && EventDataLength < HostOffset(16, 3)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "IrpPtr", IrpPtr);
            XmlAttribHex(sb, "FileObject", FileObject);
            XmlAttribHex(sb, "FileKey", FileKey);
            XmlAttrib(sb, "FileName", FileName);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "IrpPtr", "FileObject", "FileKey", "FileName" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return IrpPtr;
                case 1:
                    return FileObject;
                case 2:
                    return FileKey;
                case 3:
                    return FileName;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        internal override unsafe void FixupData()
        {
            if (eventRecord->EventHeader.ThreadId == -1)
            {
                eventRecord->EventHeader.ThreadId = GetInt32At(Version <= 2 ? HostOffset(4, 1) : HostOffset(12, 3));
            }

            if (eventRecord->EventHeader.ProcessId == -1)
            {
                eventRecord->EventHeader.ProcessId = state.ThreadIDToProcessID(ThreadID, TimeStampQPC);
            }
        }

        private event Action<FileIOSimpleOpTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class FileIOReadWriteTraceData : TraceEvent
    {
        public long Offset { get { return GetInt64At(0); } }
        public Address IrpPtr { get { return GetAddressAt(8); } }
        public Address FileObject { get { return GetAddressAt(Version <= 2 ? HostOffset(16, 2) : HostOffset(12, 1)); } }
        public Address FileKey { get { return GetAddressAt(Version <= 2 ? HostOffset(20, 3) : HostOffset(16, 2)); } }
        public string FileName { get { return state.FileIDToName(FileKey, FileObject, TimeStampQPC); } }
        public override unsafe int ProcessID
        {
            get
            {
                // We try to fix up the process ID 'on the fly' however in the case of a circular buffer,
                // We simply don't know the process ID until the end of the trace.  Thus you to check and
                // possibly try again.  
                var ret = eventRecord->EventHeader.ProcessId;
                if (ret == -1)
                {
                    ret = state.ThreadIDToProcessID(ThreadID, TimeStampQPC);
                }

                return ret;
            }
        }
        // public Address TTID { get { return GetInt32At(Version <= 2 ? HostOffset(12, 1) : HostOffset(20, 3)); } }

        public int IoSize { get { return GetInt32At(Version <= 2 ? HostOffset(24, 4) : HostOffset(24, 3)); } }
        public int IoFlags { get { return GetInt32At(Version <= 2 ? HostOffset(28, 4) : HostOffset(28, 3)); } }

        #region Private
        internal FileIOReadWriteTraceData(Action<FileIOReadWriteTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<FileIOReadWriteTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 2 && EventDataLength != HostOffset(32, 4)));
            Debug.Assert(!(Version == 3 && EventDataLength < HostOffset(32, 3)));       // TODO changed to <.  observed 48 byte length on 64 bit (1 dword more) 
            Debug.Assert(!(Version > 3 && EventDataLength < HostOffset(32, 3)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "FileName", FileName);
            XmlAttrib(sb, "Offset", Offset);
            XmlAttribHex(sb, "IrpPtr", IrpPtr);
            XmlAttribHex(sb, "FileObject", FileObject);
            XmlAttribHex(sb, "FileKey", FileKey);
            XmlAttrib(sb, "IoSize", IoSize);
            XmlAttrib(sb, "IoFlags", IoFlags);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Offset", "IrpPtr", "FileObject", "FileKey", "IoSize", "IoFlags", "FileName" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Offset;
                case 1:
                    return IrpPtr;
                case 2:
                    return FileObject;
                case 3:
                    return FileKey;
                case 4:
                    return IoSize;
                case 5:
                    return IoFlags;
                case 6:
                    return FileName;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        internal override unsafe void FixupData()
        {
            if (eventRecord->EventHeader.ThreadId == -1)
            {
                eventRecord->EventHeader.ThreadId = GetInt32At(Version <= 2 ? HostOffset(12, 1) : HostOffset(20, 3));
            }

            if (eventRecord->EventHeader.ProcessId == -1)
            {
                eventRecord->EventHeader.ProcessId = state.ThreadIDToProcessID(ThreadID, TimeStampQPC);
            }
        }
        private event Action<FileIOReadWriteTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class FileIOInfoTraceData : TraceEvent
    {
        public Address IrpPtr { get { return GetAddressAt(0); } }
        public Address FileObject { get { return GetAddressAt(Version <= 2 ? HostOffset(8, 2) : HostOffset(4, 1)); } }
        public string FileName { get { return state.FileIDToName(FileKey, FileObject, TimeStampQPC); } }
        public Address FileKey { get { return GetAddressAt(Version <= 2 ? HostOffset(12, 3) : HostOffset(8, 2)); } }
        public Address ExtraInfo { get { return GetAddressAt(Version <= 2 ? HostOffset(16, 4) : HostOffset(12, 3)); } }
        // public Address TTID { get { return GetInt32At(Version <= 2 ? HostOffset(4, 1) : HostOffset(16, 4)); } }
        public int InfoClass { get { return GetInt32At(Version <= 2 ? HostOffset(20, 5) : HostOffset(20, 4)); } }
        public override unsafe int ProcessID
        {
            get
            {
                // We try to fix up the process ID 'on the fly' however in the case of a circular buffer,
                // We simply don't know the process ID until the end of the trace.  Thus you to check and
                // possibly try again.  
                var ret = eventRecord->EventHeader.ProcessId;
                if (ret == -1)
                {
                    ret = state.ThreadIDToProcessID(ThreadID, TimeStampQPC);
                }

                return ret;
            }
        }
        #region Private
        internal FileIOInfoTraceData(Action<FileIOInfoTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<FileIOInfoTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 2 && EventDataLength != HostOffset(24, 5)));
            Debug.Assert(!(Version == 3 && EventDataLength != HostOffset(24, 4)));
            Debug.Assert(!(Version > 3 && EventDataLength < HostOffset(24, 4)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "FileName", FileName);
            XmlAttribHex(sb, "IrpPtr", IrpPtr);
            XmlAttribHex(sb, "FileObject", FileObject);
            XmlAttribHex(sb, "FileKey", FileKey);
            XmlAttribHex(sb, "ExtraInfo", ExtraInfo);
            XmlAttrib(sb, "InfoClass", InfoClass);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "IrpPtr", "FileObject", "FileKey", "ExtraInfo", "InfoClass", "FileName" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return IrpPtr;
                case 1:
                    return FileObject;
                case 2:
                    return FileKey;
                case 3:
                    return ExtraInfo;
                case 4:
                    return InfoClass;
                case 5:
                    return FileName;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }
        internal override unsafe void FixupData()
        {
            if (eventRecord->EventHeader.ThreadId == -1)
            {
                eventRecord->EventHeader.ThreadId = GetInt32At(Version <= 2 ? HostOffset(4, 1) : HostOffset(16, 4));
            }

            if (eventRecord->EventHeader.ProcessId == -1)
            {
                eventRecord->EventHeader.ProcessId = state.ThreadIDToProcessID(ThreadID, TimeStampQPC);
            }
        }
        private event Action<FileIOInfoTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class FileIODirEnumTraceData : TraceEvent
    {
        public Address IrpPtr { get { return GetAddressAt(0); } }
        /// <summary>
        /// The FileObject is the object for the Directory (used by CreateFile to open and passed to Close to close)
        /// </summary>
        public Address FileObject { get { return GetAddressAt(Version <= 2 ? HostOffset(8, 2) : HostOffset(4, 1)); } }
        /// <summary>
        /// The FileKey is the object that represents the name of the directory.  
        /// </summary>
        public Address FileKey { get { return GetAddressAt(Version <= 2 ? HostOffset(12, 3) : HostOffset(8, 2)); } }
        public string DirectoryName { get { return state.FileIDToName(FileKey, FileObject, TimeStampQPC); } }
        // public Address TTID { get { return GetInt32At(Version <= 2 ? HostOffset(4, 1) : HostOffset(12, 3)); } }
        public int Length { get { return GetInt32At(Version <= 2 ? HostOffset(16, 4) : HostOffset(16, 3)); } }
        public int InfoClass { get { return GetInt32At(Version <= 2 ? HostOffset(20, 4) : HostOffset(20, 3)); } }
        public int FileIndex { get { return GetInt32At(Version <= 2 ? HostOffset(24, 4) : HostOffset(24, 3)); } }
        public string FileName { get { return state.KernelToUser(GetUnicodeStringAt(Version <= 2 ? HostOffset(28, 4) : HostOffset(28, 3))); } }
        public override unsafe int ProcessID
        {
            get
            {
                // We try to fix up the process ID 'on the fly' however in the case of a circular buffer,
                // We simply don't know the process ID until the end of the trace.  Thus you to check and
                // possibly try again.  
                var ret = eventRecord->EventHeader.ProcessId;
                if (ret == -1)
                {
                    ret = state.ThreadIDToProcessID(ThreadID, TimeStampQPC);
                }

                return ret;
            }
        }
        #region Private
        internal FileIODirEnumTraceData(Action<FileIODirEnumTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<FileIODirEnumTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 2 && EventDataLength != SkipUnicodeString(HostOffset(28, 4))));
            Debug.Assert(!(Version == 3 && EventDataLength != SkipUnicodeString(HostOffset(28, 3))));
            Debug.Assert(!(Version > 3 && EventDataLength < SkipUnicodeString(HostOffset(28, 3))));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "FileName", FileName);
            XmlAttribHex(sb, "IrpPtr", IrpPtr);
            XmlAttribHex(sb, "FileObject", FileObject);
            XmlAttribHex(sb, "FileKey", FileKey);
            XmlAttrib(sb, "DirectoryName", DirectoryName);
            XmlAttrib(sb, "Length", Length);
            XmlAttrib(sb, "InfoClass", InfoClass);
            XmlAttrib(sb, "FileIndex", FileIndex);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "IrpPtr", "FileObject", "FileKey", "DirectoryName", "Length", "InfoClass", "FileIndex", "FileName" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return IrpPtr;
                case 1:
                    return FileObject;
                case 2:
                    return FileKey;
                case 3:
                    return DirectoryName;
                case 4:
                    return Length;
                case 5:
                    return InfoClass;
                case 6:
                    return FileIndex;
                case 7:
                    return FileName;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        internal override unsafe void FixupData()
        {
            if (eventRecord->EventHeader.ThreadId == -1)
            {
                eventRecord->EventHeader.ThreadId = GetInt32At(Version <= 2 ? HostOffset(4, 1) : HostOffset(12, 3));
            }

            if (eventRecord->EventHeader.ProcessId == -1)
            {
                eventRecord->EventHeader.ProcessId = state.ThreadIDToProcessID(ThreadID, TimeStampQPC);
            }
        }

        private event Action<FileIODirEnumTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class FileIOOpEndTraceData : TraceEvent
    {
        public Address IrpPtr { get { return GetAddressAt(0); } }
        public Address ExtraInfo { get { return GetAddressAt(HostOffset(4, 1)); } }
        public int NtStatus { get { return GetInt32At(HostOffset(8, 2)); } }

        #region Private
        internal FileIOOpEndTraceData(Action<FileIOOpEndTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<FileIOOpEndTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 2 && EventDataLength != HostOffset(12, 2)));
            Debug.Assert(!(Version > 2 && EventDataLength < HostOffset(12, 2)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "IrpPtr", IrpPtr);
            XmlAttribHex(sb, "ExtraInfo", ExtraInfo);
            XmlAttrib(sb, "NtStatus", NtStatus);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "IrpPtr", "ExtraInfo", "NtStatus" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return IrpPtr;
                case 1:
                    return ExtraInfo;
                case 2:
                    return NtStatus;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<FileIOOpEndTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class TcpIpTraceData : TraceEvent
    {

        // PID
        public int size { get { if (Version >= 1) { return GetInt32At(4); } return GetInt32At(12); } }
        public System.Net.IPAddress daddr
        {
            get
            {
                var addr = (uint)((Version >= 1) ? GetInt32At(8) : GetInt32At(0));
                return new System.Net.IPAddress(addr);
            }
        }
        public System.Net.IPAddress saddr
        {
            get
            {
                var addr = (uint)((Version >= 1) ? GetInt32At(12) : GetInt32At(4));
                return new System.Net.IPAddress(addr);
            }
        }
        public int dport { get { if (Version >= 1) { return ByteSwap16(GetInt16At(16)); } return ByteSwap16(GetInt16At(8)); } }
        public int sport { get { if (Version >= 1) { return ByteSwap16(GetInt16At(18)); } return ByteSwap16(GetInt16At(10)); } }
        public Address connid { get { if (Version >= 1) { return GetAddressAt(HostOffset(20, 1)); } return 0; } }
        public int seqnum { get { if (Version >= 1) { return GetInt32At(HostOffset(24, 1)); } return 0; } }

        internal static int ByteSwap16(int val) { return ((val << 8) & 0xFF00) + ((val >> 8) & 0xFF); }
        #region Private
        internal TcpIpTraceData(Action<TcpIpTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<TcpIpTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            //Debug.Assert(!(Version == 0 && EventDataLength != 20));
            //Debug.Assert(!(Version == 1 && EventDataLength < HostOffset(28, 1)));   // TODO fixed by hand
            //Debug.Assert(!(Version > 1 && EventDataLength < HostOffset(28, 1)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "daddr", daddr);
            XmlAttrib(sb, "saddr", saddr);
            XmlAttrib(sb, "dport", dport);
            XmlAttrib(sb, "sport", sport);
            XmlAttrib(sb, "size", size);
            XmlAttrib(sb, "connid", connid);
            XmlAttrib(sb, "seqnum", seqnum);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "daddr", "saddr", "dport", "sport", "size", "connid", "seqnum" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return daddr;
                case 1:
                    return saddr;
                case 2:
                    return dport;
                case 3:
                    return sport;
                case 4:
                    return size;
                case 5:
                    return connid;
                case 6:
                    return seqnum;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<TcpIpTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;

        internal override unsafe void FixupData()
        {
            Debug.Assert(eventRecord->EventHeader.ProcessId == -1);
            if (Version >= 1)
            {
                eventRecord->EventHeader.ProcessId = GetInt32At(0);
            }
            else
            {
                eventRecord->EventHeader.ProcessId = GetInt32At(16);
            }
        }
        #endregion
    }
    public sealed class TcpIpFailTraceData : TraceEvent
    {
        public int Proto { get { if (Version >= 2) { return GetInt16At(0); } return GetInt32At(0); } }
        public int FailureCode { get { if (Version >= 2) { return GetInt16At(2); } return 0; } }

        #region Private
        internal TcpIpFailTraceData(Action<TcpIpFailTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<TcpIpFailTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 1 && EventDataLength != 4));
            Debug.Assert(!(Version == 2 && EventDataLength != 4));
            Debug.Assert(!(Version > 2 && EventDataLength < 4));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "Proto", Proto);
            XmlAttrib(sb, "FailureCode", FailureCode);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Proto", "FailureCode" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Proto;
                case 1:
                    return FailureCode;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<TcpIpFailTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }

    public sealed class TcpIpSendTraceData : TraceEvent
    {
        // TODO not quite right for V0 TcpIP (does anyone care?)

        // PID
        public int size { get { return GetInt32At(4); } }

        public System.Net.IPAddress daddr { get { return new System.Net.IPAddress((uint)GetInt32At(8)); } }
        public System.Net.IPAddress saddr { get { return new System.Net.IPAddress((uint)GetInt32At(12)); } }
        public int dport { get { return TcpIpTraceData.ByteSwap16(GetInt16At(16)); } }
        public int sport { get { return TcpIpTraceData.ByteSwap16(GetInt16At(18)); } }
        public int startime { get { return GetInt32At(20); } }
        public int endtime { get { return GetInt32At(24); } }
        public int seqnum { get { return GetInt32At(28); } }
        public Address connid { get { return GetAddressAt(32); } }

        #region Private
        internal TcpIpSendTraceData(Action<TcpIpSendTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<TcpIpSendTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            //Debug.Assert(!(Version == 2 && EventDataLength != HostOffset(36, 1)));
            //Debug.Assert(!(Version > 2 && EventDataLength < HostOffset(36, 1)));
            Action(this);
        }

        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "size", size);
            XmlAttrib(sb, "daddr", daddr);
            XmlAttrib(sb, "saddr", saddr);
            XmlAttrib(sb, "dport", dport);
            XmlAttrib(sb, "sport", sport);
            XmlAttrib(sb, "startime", startime);
            XmlAttrib(sb, "endtime", endtime);
            XmlAttrib(sb, "seqnum", seqnum);
            XmlAttrib(sb, "connid", connid);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "size", "daddr", "saddr", "dport", "sport", "startime", "endtime", "seqnum", "connid" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return size;
                case 1:
                    return daddr;
                case 2:
                    return saddr;
                case 3:
                    return dport;
                case 4:
                    return sport;
                case 5:
                    return startime;
                case 6:
                    return endtime;
                case 7:
                    return seqnum;
                case 8:
                    return connid;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<TcpIpSendTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;

        internal override unsafe void FixupData()
        {
            Debug.Assert(eventRecord->EventHeader.ProcessId == -1);
            eventRecord->EventHeader.ProcessId = GetInt32At(0);
        }
        #endregion
    }
    public sealed class TcpIpConnectTraceData : TraceEvent
    {
        // TODO not quite right for V0 TcpIP (does anyone care?)

        // PID
        public int size { get { return GetInt32At(4); } }
        public System.Net.IPAddress daddr { get { return new System.Net.IPAddress((uint)GetInt32At(8)); } }
        public System.Net.IPAddress saddr { get { return new System.Net.IPAddress((uint)GetInt32At(12)); } }
        public int dport { get { return TcpIpTraceData.ByteSwap16(GetInt16At(16)); } }
        public int sport { get { return TcpIpTraceData.ByteSwap16(GetInt16At(18)); } }
        public int mss { get { return GetInt16At(20); } }
        public int sackopt { get { return GetInt16At(22); } }
        public int tsopt { get { return GetInt16At(24); } }
        public int wsopt { get { return GetInt16At(26); } }
        public int rcvwin { get { return GetInt32At(28); } }
        public int rcvwinscale { get { return GetInt16At(32); } }
        public int sndwinscale { get { return GetInt16At(34); } }
        public int seqnum { get { return GetInt32At(36); } }
        public Address connid { get { return GetAddressAt(40); } }

        #region Private
        internal TcpIpConnectTraceData(Action<TcpIpConnectTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<TcpIpConnectTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            //Debug.Assert(!(Version == 2 && EventDataLength != HostOffset(44, 1)));
            //Debug.Assert(!(Version > 2 && EventDataLength < HostOffset(44, 1)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "size", size);
            XmlAttrib(sb, "daddr", daddr);
            XmlAttrib(sb, "saddr", saddr);
            XmlAttrib(sb, "dport", dport);
            XmlAttrib(sb, "sport", sport);
            XmlAttrib(sb, "mss", mss);
            XmlAttrib(sb, "sackopt", sackopt);
            XmlAttrib(sb, "tsopt", tsopt);
            XmlAttrib(sb, "wsopt", wsopt);
            XmlAttrib(sb, "rcvwin", rcvwin);
            XmlAttrib(sb, "rcvwinscale", rcvwinscale);
            XmlAttrib(sb, "sndwinscale", sndwinscale);
            XmlAttrib(sb, "seqnum", seqnum);
            XmlAttrib(sb, "connid", connid);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "size", "daddr", "saddr", "dport", "sport", "mss", "sackopt", "tsopt", "wsopt", "rcvwin", "rcvwinscale", "sndwinscale", "seqnum", "connid" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return size;
                case 1:
                    return daddr;
                case 2:
                    return saddr;
                case 3:
                    return dport;
                case 4:
                    return sport;
                case 5:
                    return mss;
                case 6:
                    return sackopt;
                case 7:
                    return tsopt;
                case 8:
                    return wsopt;
                case 9:
                    return rcvwin;
                case 10:
                    return rcvwinscale;
                case 11:
                    return sndwinscale;
                case 12:
                    return seqnum;
                case 13:
                    return connid;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<TcpIpConnectTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;

        internal override unsafe void FixupData()
        {
            Debug.Assert(eventRecord->EventHeader.ProcessId == -1);
            eventRecord->EventHeader.ProcessId = GetInt32At(0);
        }
        #endregion
    }
    public sealed class TcpIpV6TraceData : TraceEvent
    {
        // PID
        public int size { get { return GetInt32At(4); } }
        public System.Net.IPAddress daddr { get { return GetIPAddrV6At(8); } }
        public System.Net.IPAddress saddr { get { return GetIPAddrV6At(24); } }
        public int dport { get { return TcpIpTraceData.ByteSwap16(GetInt16At(40)); } }
        public int sport { get { return TcpIpTraceData.ByteSwap16(GetInt16At(42)); } }
        public Address connid { get { return GetAddressAt(44); } }
        public int seqnum { get { return GetInt32At(HostOffset(48, 1)); } }

        #region Private
        internal TcpIpV6TraceData(Action<TcpIpV6TraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<TcpIpV6TraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version >= 1 && EventDataLength < HostOffset(52, 1)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "size", size);
            XmlAttrib(sb, "daddr", daddr);
            XmlAttrib(sb, "saddr", saddr);
            XmlAttribHex(sb, "dport", dport);
            XmlAttribHex(sb, "sport", sport);
            XmlAttrib(sb, "connid", connid);
            XmlAttrib(sb, "seqnum", seqnum);
            sb.Append("/>");
            return sb;
        }
        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "size", "daddr", "saddr", "dport", "sport", "connid", "seqnum" };
                }

                return payloadNames;
            }
        }
        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return size;
                case 1:
                    return daddr;
                case 2:
                    return saddr;
                case 3:
                    return dport;
                case 4:
                    return sport;
                case 5:
                    return connid;
                case 6:
                    return seqnum;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }
        private event Action<TcpIpV6TraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;

        internal override unsafe void FixupData()
        {
            Debug.Assert(eventRecord->EventHeader.ProcessId == -1);
            eventRecord->EventHeader.ProcessId = GetInt32At(0);
        }
        #endregion
    }
    public sealed class TcpIpV6SendTraceData : TraceEvent
    {
        // PID
        public int size { get { return GetInt32At(4); } }
        public System.Net.IPAddress daddr { get { return GetIPAddrV6At(8); } }
        public System.Net.IPAddress saddr { get { return GetIPAddrV6At(24); } }
        public int dport { get { return TcpIpTraceData.ByteSwap16(GetInt16At(40)); } }
        public int sport { get { return TcpIpTraceData.ByteSwap16(GetInt16At(42)); } }
        public int startime { get { return GetInt32At(44); } }
        public int endtime { get { return GetInt32At(48); } }
        public int seqnum { get { return GetInt32At(52); } }
        public Address connid { get { return GetAddressAt(56); } }

        #region Private
        internal TcpIpV6SendTraceData(Action<TcpIpV6SendTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<TcpIpV6SendTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 2 && EventDataLength != HostOffset(60, 1)));
            Debug.Assert(!(Version > 2 && EventDataLength < HostOffset(60, 1)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "size", size);
            XmlAttrib(sb, "daddr", daddr);
            XmlAttrib(sb, "saddr", saddr);
            XmlAttribHex(sb, "dport", dport);
            XmlAttribHex(sb, "sport", sport);
            XmlAttribHex(sb, "startime", startime);
            XmlAttribHex(sb, "endtime", endtime);
            XmlAttrib(sb, "seqnum", seqnum);
            XmlAttrib(sb, "connid", connid);
            sb.Append("/>");
            return sb;
        }
        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "size", "daddr", "saddr", "dport", "sport", "startime", "endtime", "seqnum", "connid", };
                }

                return payloadNames;
            }
        }
        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return size;
                case 1:
                    return daddr;
                case 2:
                    return saddr;
                case 3:
                    return dport;
                case 4:
                    return sport;
                case 5:
                    return startime;
                case 6:
                    return endtime;
                case 7:
                    return seqnum;
                case 8:
                    return connid;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }
        private event Action<TcpIpV6SendTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;

        internal override unsafe void FixupData()
        {
            Debug.Assert(eventRecord->EventHeader.ProcessId == -1);
            eventRecord->EventHeader.ProcessId = GetInt32At(0);
        }
        #endregion
    }
    public sealed class TcpIpV6ConnectTraceData : TraceEvent
    {
        // PID
        public int size { get { return GetInt32At(4); } }
        public System.Net.IPAddress daddr { get { return GetIPAddrV6At(8); } }
        public System.Net.IPAddress saddr { get { return GetIPAddrV6At(24); } }
        public int dport { get { return TcpIpTraceData.ByteSwap16(GetInt16At(40)); } }
        public int sport { get { return TcpIpTraceData.ByteSwap16(GetInt16At(42)); } }
        public int mss { get { return GetInt16At(44); } }
        public int sackopt { get { return GetInt16At(46); } }
        public int tsopt { get { return GetInt16At(48); } }
        public int wsopt { get { return GetInt16At(50); } }
        public int rcvwin { get { return GetInt32At(52); } }
        public int rcvwinscale { get { return GetInt16At(56); } }
        public int sndwinscale { get { return GetInt16At(58); } }
        public int seqnum { get { return GetInt32At(60); } }
        public Address connid { get { return GetAddressAt(64); } }

        #region Private
        internal TcpIpV6ConnectTraceData(Action<TcpIpV6ConnectTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<TcpIpV6ConnectTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 2 && EventDataLength != HostOffset(68, 1)));
            Debug.Assert(!(Version > 2 && EventDataLength < HostOffset(68, 1)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "size", size);
            XmlAttrib(sb, "dport", dport);
            XmlAttrib(sb, "sport", sport);
            XmlAttrib(sb, "mss", mss);
            XmlAttrib(sb, "sackopt", sackopt);
            XmlAttrib(sb, "tsopt", tsopt);
            XmlAttrib(sb, "wsopt", wsopt);
            XmlAttrib(sb, "rcvwin", rcvwin);
            XmlAttrib(sb, "rcvwinscale", rcvwinscale);
            XmlAttrib(sb, "sndwinscale", sndwinscale);
            XmlAttrib(sb, "seqnum", seqnum);
            XmlAttrib(sb, "connid", connid);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "size", "dport", "sport", "mss", "sackopt", "tsopt", "wsopt", "rcvwin", "rcvwinscale", "sndwinscale", "seqnum", "connid" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return size;
                case 1:
                    return dport;
                case 2:
                    return sport;
                case 3:
                    return mss;
                case 4:
                    return sackopt;
                case 5:
                    return tsopt;
                case 6:
                    return wsopt;
                case 7:
                    return rcvwin;
                case 8:
                    return rcvwinscale;
                case 9:
                    return sndwinscale;
                case 10:
                    return seqnum;
                case 11:
                    return connid;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<TcpIpV6ConnectTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        internal override unsafe void FixupData()
        {
            Debug.Assert(eventRecord->EventHeader.ProcessId == -1);
            eventRecord->EventHeader.ProcessId = GetInt32At(0);
        }
        #endregion
    }
    public sealed class UdpIpTraceData : TraceEvent
    {
        public Address context { get { return GetAddressAt(0); } }
        public System.Net.IPAddress saddr
        {
            get
            {
                var addr = (uint)((Version >= 1) ? GetInt32At(12) : GetInt32At(HostOffset(4, 1)));
                return new System.Net.IPAddress(addr);
            }
        }
        public int sport { get { if (Version >= 1) { return TcpIpTraceData.ByteSwap16(GetInt16At(18)); } return TcpIpTraceData.ByteSwap16(GetInt16At(HostOffset(8, 1))); } }
        public int size { get { if (Version >= 1) { return GetInt32At(4); } return GetInt16At(HostOffset(10, 1)); } }
        public System.Net.IPAddress daddr
        {
            get
            {
                var addr = (uint)((Version >= 1) ? GetInt32At(8) : GetInt32At(HostOffset(12, 1)));
                return new System.Net.IPAddress(addr);
            }
        }
        public int dport { get { if (Version >= 1) { return TcpIpTraceData.ByteSwap16(GetInt16At(16)); } return TcpIpTraceData.ByteSwap16(GetInt16At(HostOffset(16, 1))); } }
        public int dsize { get { return GetInt16At(HostOffset(18, 1)); } }
        // PID  
        #region Private
        internal UdpIpTraceData(Action<UdpIpTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<UdpIpTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength < HostOffset(20, 1)));   // TODO fixed by hand
            Debug.Assert(!(Version == 1 && EventDataLength < 20));                  // TODO fixed by hand
            Debug.Assert(!(Version > 1 && EventDataLength < 20));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "context", context);
            XmlAttrib(sb, "saddr", saddr);
            XmlAttrib(sb, "sport", sport);
            XmlAttrib(sb, "size", size);
            XmlAttrib(sb, "daddr", daddr);
            XmlAttrib(sb, "dport", dport);
            XmlAttrib(sb, "dsize", dsize);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "context", "saddr", "sport", "size", "daddr", "dport", "dsize" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return context;
                case 1:
                    return saddr;
                case 2:
                    return sport;
                case 3:
                    return size;
                case 4:
                    return daddr;
                case 5:
                    return dport;
                case 6:
                    return dsize;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<UdpIpTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        internal override unsafe void FixupData()
        {
            Debug.Assert(eventRecord->EventHeader.ProcessId == -1);
            if (Version >= 1)
            {
                eventRecord->EventHeader.ProcessId = GetInt32At(0);
            }
        }
        #endregion
    }
    public sealed class UdpIpFailTraceData : TraceEvent
    {
        public int Proto { get { return GetInt16At(0); } }
        public int FailureCode { get { return GetInt16At(2); } }

        #region Private
        internal UdpIpFailTraceData(Action<UdpIpFailTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<UdpIpFailTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 2 && EventDataLength != 4));
            Debug.Assert(!(Version > 2 && EventDataLength < 4));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "Proto", Proto);
            XmlAttrib(sb, "FailureCode", FailureCode);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Proto", "FailureCode" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Proto;
                case 1:
                    return FailureCode;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<UdpIpFailTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class UpdIpV6TraceData : TraceEvent
    {
        // PID
        public int size { get { return GetInt32At(4); } }
        public System.Net.IPAddress daddr { get { return GetIPAddrV6At(8); } }
        public System.Net.IPAddress saddr { get { return GetIPAddrV6At(24); } }
        public int dport { get { return TcpIpTraceData.ByteSwap16(GetInt16At(40)); } }
        public int sport { get { return TcpIpTraceData.ByteSwap16(GetInt16At(42)); } }
        public int seqnum { get { return GetInt32At(44); } }
        public Address connid { get { return GetAddressAt(48); } }

        #region Private
        internal UpdIpV6TraceData(Action<UpdIpV6TraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<UpdIpV6TraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 2 && EventDataLength != HostOffset(52, 1)));
            Debug.Assert(!(Version > 2 && EventDataLength < HostOffset(52, 1)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "size", size);
            XmlAttrib(sb, "dport", dport);
            XmlAttrib(sb, "sport", sport);
            XmlAttrib(sb, "seqnum", seqnum);
            XmlAttrib(sb, "connid", connid);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "size", "dport", "sport", "seqnum", "connid" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return size;
                case 1:
                    return dport;
                case 2:
                    return sport;
                case 3:
                    return seqnum;
                case 4:
                    return connid;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<UpdIpV6TraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        internal override unsafe void FixupData()
        {
            Debug.Assert(eventRecord->EventHeader.ProcessId == -1);
            eventRecord->EventHeader.ProcessId = GetInt32At(0);
        }
        #endregion
    }
    public sealed class ImageLoadTraceData : TraceEvent
    {
        public Address ImageBase { get { return GetAddressAt(0); } }
        public int ImageSize { get { return (int)GetAddressAt(HostOffset(4, 1)); } }
        // public int ProcessID { get { if (Version >= 1) return GetInt32At(HostOffset(8, 2)); return 0; } }
        public int ImageChecksum { get { if (Version >= 2) { return GetInt32At(HostOffset(12, 2)); } return 0; } }
        public int TimeDateStamp { get { if (Version >= 2) { return GetInt32At(HostOffset(16, 2)); } return 0; } }
        /// <summary>
        /// This is the TimeDateStamp converted to a DateTime
        /// TODO: daylight savings time seems to mess this up.  
        /// </summary>
        public DateTime BuildTime
        {
            get
            {
                return PEFile.PEHeader.TimeDateStampToDate(TimeDateStamp);
            }
        }

        // Skipping Reserved0
        public Address DefaultBase { get { if (Version >= 2) { return GetAddressAt(HostOffset(24, 2)); } return 0; } }
        // Skipping Reserved1
        // Skipping Reserved2
        // Skipping Reserved3
        // Skipping Reserved4
        public string FileName { get { return state.KernelToUser(KernelFileName); } }
        private string KernelFileName { get { if (Version >= 2) { return GetUnicodeStringAt(HostOffset(44, 3)); } if (Version >= 1) { return GetUnicodeStringAt(HostOffset(12, 2)); } return ""; } }

        #region Private
        internal ImageLoadTraceData(Action<ImageLoadTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ImageLoadTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength < SkipUnicodeString(HostOffset(8, 1))));
            Debug.Assert(!(Version == 1 && EventDataLength < SkipUnicodeString(HostOffset(12, 2))));
            Debug.Assert(!(Version == 2 && EventDataLength != SkipUnicodeString(HostOffset(44, 3))));
            Debug.Assert(!(Version > 2 && EventDataLength < SkipUnicodeString(HostOffset(44, 3))));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "ImageBase", ImageBase);
            XmlAttribHex(sb, "ImageSize", ImageSize);
            XmlAttrib(sb, "ImageChecksum", ImageChecksum);
            XmlAttrib(sb, "TimeDateStamp", TimeDateStamp);
            XmlAttribHex(sb, "DefaultBase", DefaultBase);
            XmlAttrib(sb, "FileName", FileName);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "ImageBase", "ImageSize", "ImageChecksum", "TimeDateStamp", "DefaultBase", "BuildTime", "FileName" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ImageBase;
                case 1:
                    return ImageSize;
                case 2:
                    return ImageChecksum;
                case 3:
                    return TimeDateStamp;
                case 4:
                    return DefaultBase;
                case 5:
                    return BuildTime;
                case 6:
                    return FileName;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ImageLoadTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;

        internal override unsafe void FixupData()
        {
            // We wish to create the illusion that the events are reported by the process where it is loaded. 
            // This it not actually true for DCStart and DCStop, and Stop events, so we fix it up.  
            if (Opcode == TraceEventOpcode.DataCollectionStart ||
                Opcode == TraceEventOpcode.DataCollectionStop || Opcode == TraceEventOpcode.Stop)
            {
                eventRecord->EventHeader.ThreadId = -1;     // DCStarts and DCStops have no useful thread.
                if (eventRecord->EventHeader.Version >= 1)
                {
                    eventRecord->EventHeader.ProcessId = GetInt32At(HostOffset(8, 2));
                }
            }
            // Debug.Assert(eventRecord->EventHeader.Version == 0 || eventRecord->EventHeader.ProcessId == GetInt32At(HostOffset(8, 2)));
        }
        #endregion
    }
    public sealed class MemoryPageFaultTraceData : TraceEvent
    {
        public Address VirtualAddress { get { return GetAddressAt(0); } }
        public Address ProgramCounter { get { return GetAddressAt(HostOffset(4, 1)); } }

        #region Private
        internal MemoryPageFaultTraceData(Action<MemoryPageFaultTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<MemoryPageFaultTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(8, 2)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(8, 2)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "VirtualAddress", VirtualAddress);
            XmlAttribHex(sb, "ProgramCounter", ProgramCounter);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "VirtualAddress", "ProgramCounter" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return VirtualAddress;
                case 1:
                    return ProgramCounter;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<MemoryPageFaultTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;

        /// <summary>
        /// Indicate that ProgramCounter is a code address that needs symbolic information
        /// </summary>
        internal override bool LogCodeAddresses(Func<TraceEvent, Address, bool> callBack)
        {
            return callBack(this, ProgramCounter);
        }
        #endregion
    }
    public sealed class MemoryHardFaultTraceData : TraceEvent
    {
        // This seems to be in PerfFreq units, but that does not help us that  much because
        // we need to know the absolute time.   
        /// <summary>
        /// The time spent during the page fault.  
        /// </summary>
        public double ElapsedTimeMSec
        {
            get
            {
                return (TimeStampQPC - InitialTime) * 1000.0 / traceEventSource.QPCFreq;
            }
        }
        private long InitialTime { get { return GetInt64At(0); } }
        public long ReadOffset { get { return GetInt64At(8); } }
        public Address VirtualAddress { get { return GetAddressAt(16); } }
        public Address FileKey { get { return GetAddressAt(HostOffset(20, 1)); } }
        public string FileName { get { return state.FileIDToName(FileKey, TimeStampQPC); } }

        // public int TThreadID { get { return GetInt32At(HostOffset(24, 2)); } }
        public int ByteCount { get { return GetInt32At(HostOffset(28, 2)); } }

        #region Private
        internal MemoryHardFaultTraceData(Action<MemoryHardFaultTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<MemoryHardFaultTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(GetInt32At(HostOffset(24, 2)) == ThreadID);    // TThreadID == ThreadID
            Debug.Assert(!(Version >= 0 && EventDataLength < HostOffset(32, 2)));
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(32, 2)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(32, 2)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ElapsedTimeMSec", ElapsedTimeMSec);
            XmlAttribHex(sb, "ReadOffset", ReadOffset);
            XmlAttribHex(sb, "VirtualAddress", VirtualAddress);
            XmlAttribHex(sb, "FileKey", FileKey);
            XmlAttrib(sb, "ByteCount", ByteCount);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "ElapsedTimeMSec", "ReadOffset", "VirtualAddress", "FileKey", "ByteCount", "FileName" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ElapsedTimeMSec;
                case 1:
                    return ReadOffset;
                case 2:
                    return VirtualAddress;
                case 3:
                    return FileKey;
                case 4:
                    return ByteCount;
                case 5:
                    return FileName;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<MemoryHardFaultTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;

        internal override unsafe void FixupData()
        {
            if (eventRecord->EventHeader.ThreadId == -1)
            {
                eventRecord->EventHeader.ThreadId = GetInt32At(HostOffset(0x18, 2));
            }

            if (eventRecord->EventHeader.ProcessId == -1)
            {
                eventRecord->EventHeader.ProcessId = state.ThreadIDToProcessID(ThreadID, TimeStampQPC);
            }
        }

        public override unsafe int ProcessID
        {
            get
            {
                // We try to fix up the process ID 'on the fly' however in the case of a circular buffer,
                // We simply don't know the process ID until the end of the trace.  Thus you to check and
                // possibly try again.  
                var ret = eventRecord->EventHeader.ProcessId;
                if (ret == -1)
                {
                    ret = state.ThreadIDToProcessID(ThreadID, TimeStampQPC);
                }

                return ret;
            }
        }

        #endregion
    }

    public enum PageKind
    {
        ProcessPrivate,
        File,
        PageFileMapped,
        PageTable,
        PagedPool,
        NonPagedPool,
        SystemPTE,
        SessionPrivate,
        MetaFile,
        AwePage,
        DriverLockPage,
        KernelStack,
        WSMetaData,
        LargePage
    };

    public enum PageList
    {
        Zero,
        Free,
        Standby,
        Modified,
        ModifiedNoWrite,
        Bad,
        Active,
        Transition
    };

    public sealed class MemoryPageAccessTraceData : TraceEvent
    {
        public PageKind PageKind { get { return (PageKind)(GetByteAt(0) & 0xF); } }
        public PageList PageList { get { return (PageList)((GetByteAt(0) >> 4) & 7); } }

        public Address PageFrameIndex { get { return GetAddressAt(8); } }
        public Address VirtualAddress
        {
            get
            {
                if (PageKind == Kernel.PageKind.File || PageKind == Kernel.PageKind.MetaFile)
                {
                    return GetAddressAt(HostOffset(16, 2));
                }

                return GetAddressAt(HostOffset(12, 1)) & ~3UL;
            }
        }

        // TODO FIX NOW.  Probably not right since it is only valid for PageKind==File
        public Address FileKey
        {
            get
            {
                if (PageKind == Kernel.PageKind.File || PageKind == Kernel.PageKind.MetaFile)
                {
                    return GetAddressAt(HostOffset(12, 1)) & ~3UL;
                }

                return 0;
            }
        }
        public string FileName { get { return state.FileIDToName(FileKey, TimeStampQPC); } }

        #region Private
        internal MemoryPageAccessTraceData(Action<MemoryPageAccessTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<MemoryPageAccessTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            // We use this for the PageAccess and PageAccessEx.  
            // TODO FIX NOW.   reenable this assert (we get 24 on a 32 bit process)
            // Debug.Assert(!(Version == 2 && EventDataLength != HostOffset(16, 2)) || EventDataLength == HostOffset(20, 3));
            Debug.Assert(!(Version > 2 && EventDataLength < HostOffset(16, 2)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "PageKind", PageKind);
            XmlAttrib(sb, "PageList", PageList);
            XmlAttrib(sb, "PageFrameIndex", PageFrameIndex);
            XmlAttribHex(sb, "VirtualAddress", VirtualAddress);
            XmlAttrib(sb, "FileName", FileName);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "PageKind", "PageList", "PageFrameIndex", "VirtualAddress", "FileName" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return PageKind;
                case 1:
                    return PageList;
                case 2:
                    return PageFrameIndex;
                case 3:
                    return VirtualAddress;
                case 4:
                    return FileName;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<MemoryPageAccessTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;

        /// <summary>
        /// Indicate that the Address is a code address that needs symbolic information
        /// </summary>
        internal override bool LogCodeAddresses(Func<TraceEvent, Address, bool> callBack)
        {
            return callBack(this, VirtualAddress);
        }
        #endregion
    }

    /// <summary>
    /// This event is emitted by the Microsoft-Windows-Kernel-Memory with Keyword 0x40  KERNEL_MEM_KEYWORD_MEMINFO_EX every .5 seconds
    /// </summary>
    public sealed class MemoryProcessMemInfoTraceData : TraceEvent
    {
        public int Count { get { return GetInt32At(0); } }

        /// <summary>
        /// Returns the edge at the given zero-based index (index less than Count).   The returned MemoryProcessMemInfoValues 
        /// points the the data in MemoryProcessMemInfoTraceData so it cannot live beyond that lifetime.  
        /// </summary>
        public MemoryProcessMemInfoValues Values(int index) { return new MemoryProcessMemInfoValues(this, 4 + index * ElementSize); }

        #region Private
        internal MemoryProcessMemInfoTraceData(Action<MemoryProcessMemInfoTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<MemoryProcessMemInfoTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override unsafe void Validate()
        {
            Debug.Assert(EventDataLength == 4 + ElementSize * Count);
        }
        public override unsafe StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "Count", Count);
            sb.AppendLine(">");
            for (int i = 0; i < Count; i++)
            {
                var proc = Values(i);
                sb.Append(" <Process ");
                XmlAttrib(sb, "ProcessID", proc.ProcessID);
                XmlAttrib(sb, "WorkingSetPageCount", proc.WorkingSetPageCount);
                XmlAttrib(sb, "CommitPageCount", proc.CommitPageCount);
                XmlAttrib(sb, "VirtualSizeInPages", proc.VirtualSizeInPages);
                XmlAttrib(sb, "PrivateWorkingSetPageCount", proc.PrivateWorkingSetPageCount);
                if (Version >= 2)
                {
                    XmlAttrib(sb, "StoreSizePageCount", proc.StoreSizePageCount);
                    XmlAttrib(sb, "StoredPageCount", proc.StoredPageCount);
                    XmlAttrib(sb, "CommitDebtInPages", proc.CommitDebtInPages);
                    XmlAttrib(sb, "SharedCommitInPages", proc.SharedCommitInPages);
                }
                sb.AppendLine("/>");
            }
            sb.AppendLine("</Event>");
            return sb;
        }
        // This event has an array of MemoryProcessMemInfoValues. This returns the size of each of these instance.
        internal int ElementSize
        {
            get
            {
                if (Version >= 2)
                {
                    // <data name="ProcessID" inType="win:UInt32" outType="xs:unsignedInt"></data>
                    // <data name="WorkingSetPageCount" inType="win:Pointer" outType="win:HexInt64"></data>
                    // <data name="CommitPageCount" inType="win:Pointer" outType="win:HexInt64"></data>
                    // <data name="VirtualSizeInPages" inType="win:Pointer" outType="win:HexInt64"></data>
                    // <data name="PrivateWorkingSetPageCount" inType="win:Pointer" outType="win:HexInt64"></data>
                    // <data name="StoreSizePageCount" inType="win:Pointer" outType="win:HexInt64"></data>
                    // <data name="StoredPageCount" inType="win:Pointer" outType="win:HexInt64"></data>
                    // <data name="CommitDebtInPages" inType="win:Pointer" outType="win:HexInt64"></data>
                    // <data name="SharedCommitInPages" inType="win:Pointer" outType="win:HexInt64"></data>
                    return HostOffset(9 * 4, 8);
                }
                else
                {
                    // <data name="ProcessID" inType="win:UInt32" outType="xs:unsignedInt"></data>
                    // <data name="WorkingSetPageCount" inType="win:Pointer" outType="win:HexInt64"></data>
                    // <data name="CommitPageCount" inType="win:Pointer" outType="win:HexInt64"></data>
                    // <data name="VirtualSizeInPages" inType="win:Pointer" outType="win:HexInt64"></data>
                    // <data name="PrivateWorkingSetPageCount" inType="win:Pointer" outType="win:HexInt64"></data>
                    return HostOffset(5 * 4, 4);
                }
            }
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Count", "ProcessID", "WorkingSetPageCount", "CommitPageCount", "VirtualSizeInPages", "PrivateWorkingSetPageCount", "StoreSizePageCount", "StoredPageCount", "CommitDebtInPages", "SharedCommitInPages" };
                }

                return payloadNames;
            }
        }
        /// <summary>
        /// The fields after 'Count' are the first value in the array of working sets.   
        /// </summary>
        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Count;
                case 1:
                    return Values(0).ProcessID;
                case 2:
                    return Values(0).WorkingSetPageCount;
                case 3:
                    return Values(0).CommitPageCount;
                case 4:
                    return Values(0).VirtualSizeInPages;
                case 5:
                    return Values(0).PrivateWorkingSetPageCount;
                case 6:
                    return Values(0).StoreSizePageCount;
                case 7:
                    return Values(0).StoredPageCount;
                case 8:
                    return Values(0).CommitDebtInPages;
                case 9:
                    return Values(0).SharedCommitInPages;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }
        private event Action<MemoryProcessMemInfoTraceData> Action;
        #endregion
    }

    /// <summary>
    /// This structure just POINTS at the data in the MemoryProcessMemInfoTraceData.  It can only be used as long as
    /// the MemoryProcessMemInfoTraceData is alive which (unless you cloned it) is only for the lifetime of the callback.  
    /// </summary>
    public unsafe struct MemoryProcessMemInfoValues
    {
        public int ProcessID { get { return m_data.GetInt32At(m_baseOffset); } }
        public long WorkingSetPageCount { get { return (long)m_data.GetAddressAt(m_baseOffset + 4); } }
        public long CommitPageCount { get { return (long)m_data.GetAddressAt(m_data.HostOffset(m_baseOffset + 8, 1)); } }
        public long VirtualSizeInPages { get { return (long)m_data.GetAddressAt(m_data.HostOffset(m_baseOffset + 12, 2)); } }
        public long PrivateWorkingSetPageCount { get { return (long)m_data.GetAddressAt(m_data.HostOffset(m_baseOffset + 16, 3)); } }
        public long StoreSizePageCount { get { return m_data.Version >= 2 ? (long)m_data.GetAddressAt(m_data.HostOffset(m_baseOffset + 20, 4)) : 0; } }
        public long StoredPageCount { get { return m_data.Version >= 2 ? (long)m_data.GetAddressAt(m_data.HostOffset(m_baseOffset + 24, 5)) : 0; } }
        public long CommitDebtInPages { get { return m_data.Version >= 2 ? (long)m_data.GetAddressAt(m_data.HostOffset(m_baseOffset + 28, 6)) : 0; } }
        public long SharedCommitInPages { get { return m_data.Version >= 2 ? (long)m_data.GetAddressAt(m_data.HostOffset(m_baseOffset + 32, 7)) : 0; } }

        #region private
        internal IntPtr RawData { get { return (IntPtr)(((byte*)m_data.userData) + m_baseOffset); } }

        internal MemoryProcessMemInfoValues(TraceEvent data, int baseOffset) { m_data = data; m_baseOffset = baseOffset; }

        private TraceEvent m_data;
        private int m_baseOffset;
        #endregion
    }

    public sealed class MemoryHeapRangeRundownTraceData : TraceEvent
    {
        public Address HeapHandle { get { return GetAddressAt(0); } }
        public int HeapRangeFlags { get { return GetInt32At(HostOffset(4, 1)); } }
        // HRPid 
        public int HRRangeCount { get { return GetInt32At(HostOffset(12, 1)); } }

        #region Private
        internal MemoryHeapRangeRundownTraceData(Action<MemoryHeapRangeRundownTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<MemoryHeapRangeRundownTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(16, 1)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(16, 1)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "HeapHandle", HeapHandle);
            XmlAttribHex(sb, "HeapRangeFlags", HeapRangeFlags);
            XmlAttrib(sb, "HeapRangeRangeCount", HRRangeCount);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "HeapHandle", "HeapRangeFlags", "HeapRangeRangeCount" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return HeapHandle;
                case 1:
                    return HeapRangeFlags;
                case 2:
                    return HRRangeCount;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<MemoryHeapRangeRundownTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;

        internal override unsafe void FixupData()
        {
            // We always make the process id the one where the fault occured
            // TODO is this a good idea?  
            // Debug.Assert(eventRecord->EventHeader.ProcessId == -1);
            eventRecord->EventHeader.ProcessId = GetInt32At(HostOffset(8, 1));
        }
        #endregion
    }
    public sealed class MemoryHeapRangeCreateTraceData : TraceEvent
    {
        public Address HeapHandle { get { return GetAddressAt(0); } }
        public Address FirstRangeSize { get { return GetAddressAt(HostOffset(4, 1)); } }
        public int HeapRangeCreateFlags { get { return GetInt32At(HostOffset(8, 2)); } }

        #region Private
        internal MemoryHeapRangeCreateTraceData(Action<MemoryHeapRangeCreateTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<MemoryHeapRangeCreateTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(12, 2)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(12, 2)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "HeapHandle", HeapHandle);
            XmlAttribHex(sb, "FirstRangeSize", FirstRangeSize);
            XmlAttribHex(sb, "HeapRangeCreateFlags", HeapRangeCreateFlags);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "HeapHandle", "FirstRangeSize", "HeapRangeCreateFlags" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return HeapHandle;
                case 1:
                    return FirstRangeSize;
                case 2:
                    return HeapRangeCreateFlags;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<MemoryHeapRangeCreateTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class MemoryHeapRangeTraceData : TraceEvent
    {
        public Address HeapHandle { get { return GetAddressAt(0); } }
        public Address HeapRangeAddress { get { return GetAddressAt(HostOffset(4, 1)); } }
        public Address HeapRangeSize { get { return GetAddressAt(HostOffset(8, 2)); } }

        #region Private
        internal MemoryHeapRangeTraceData(Action<MemoryHeapRangeTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<MemoryHeapRangeTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(12, 3)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(12, 3)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "HeapHandle", HeapHandle);
            XmlAttribHex(sb, "HeapRangeAddress", HeapRangeAddress);
            XmlAttribHex(sb, "HeapRangeSize", HeapRangeSize);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "HeapHandle", "HeapRangeAddress", "HeapRangeSize" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return HeapHandle;
                case 1:
                    return HeapRangeAddress;
                case 2:
                    return HeapRangeSize;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<MemoryHeapRangeTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class MemoryHeapRangeDestroyTraceData : TraceEvent
    {
        public Address HeapHandle { get { return GetAddressAt(0); } }

        #region Private
        internal MemoryHeapRangeDestroyTraceData(Action<MemoryHeapRangeDestroyTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<MemoryHeapRangeDestroyTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(4, 1)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(4, 1)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "HeapHandle", HeapHandle);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "HeapHandle" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return HeapHandle;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<MemoryHeapRangeDestroyTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class MemoryImageLoadBackedTraceData : TraceEvent
    {
        public Address FileKey { get { return GetAddressAt(0); } }
        public string FileName { get { return state.FileIDToName(FileKey, TimeStampQPC); } }
        public int DeviceChar { get { return GetInt32At(HostOffset(4, 1)); } }
        public int FileChar { get { return GetInt16At(HostOffset(8, 1)); } }
        public int LoadFlags { get { return GetInt16At(HostOffset(10, 1)); } }

        #region Private
        internal MemoryImageLoadBackedTraceData(Action<MemoryImageLoadBackedTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<MemoryImageLoadBackedTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(12, 1)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(12, 1)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "FileKey", FileKey);
            XmlAttribHex(sb, "DeviceChar", DeviceChar);
            XmlAttribHex(sb, "FileChar", FileChar);
            XmlAttribHex(sb, "LoadFlags", LoadFlags);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "FileKey", "DeviceChar", "FileChar", "LoadFlags", "FileName" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return FileKey;
                case 1:
                    return DeviceChar;
                case 2:
                    return FileChar;
                case 3:
                    return LoadFlags;
                case 4:
                    return FileName;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<MemoryImageLoadBackedTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }

    public sealed class MemorySystemMemInfoTraceData : TraceEvent
    {
        // TODO complete: MemInfo,   TimeDateStamp, FreePages, Standby7, Standby6, Standby5, Standby4, Standby3, Standby2, Standby1, Standby0, TotalStandby, ModifiedPages, InUsePages, RepurposedPages
        public long FreePages { get { return (long)GetAddressAt(0); } }

        #region Private
        internal MemorySystemMemInfoTraceData(Action<MemorySystemMemInfoTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<MemorySystemMemInfoTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            //Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(12, 1)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "FreePages", FreePages);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "FreePages" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return FreePages;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<MemorySystemMemInfoTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }

    public sealed class MemInfoTraceData : TraceEvent
    {
        public byte PriorityLevels { get { return (byte)GetByteAt(0); } }
        public long ZeroPageCount { get { return (long)GetAddressAt(1); } }
        public long FreePageCount { get { return (long)GetAddressAt(HostOffset(5, 1)); } }
        public long ModifiedPageCount { get { return (long)GetAddressAt(HostOffset(9, 2)); } }
        public long ModifiedNoWritePageCount { get { return (long)GetAddressAt(HostOffset(13, 3)); } }
        public long BadPageCount { get { return (long)GetAddressAt(HostOffset(17, 4)); } }

        #region Private
        internal MemInfoTraceData(Action<MemInfoTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<MemInfoTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override unsafe void Validate()
        {
            Debug.Assert(EventDataLength >= HostOffset(17, 1) + 4);
        }
        public override unsafe StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "PriorityLevels", PriorityLevels);
            XmlAttribHex(sb, "ZeroPageCount", ZeroPageCount);
            XmlAttribHex(sb, "FreePageCount", FreePageCount);
            XmlAttribHex(sb, "ModifiedPageCount", ModifiedPageCount);
            XmlAttribHex(sb, "ModifiedNoWritePageCount", ModifiedNoWritePageCount);
            XmlAttribHex(sb, "BadPageCount", BadPageCount);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "PriorityLevels", "ZeroPageCount", "FreePageCount", "ModifiedPageCount", "ModifiedNoWritePageCount", "BadPageCount" };
                }

                return payloadNames;
            }
        }
        /// <summary>
        /// The fields after 'Count' are the first value in the array of working sets.   
        /// </summary>
        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return PriorityLevels;
                case 1:
                    return ZeroPageCount;
                case 2:
                    return FreePageCount;
                case 3:
                    return ModifiedPageCount;
                case 4:
                    return ModifiedNoWritePageCount;
                case 5:
                    return BadPageCount;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }
        private event Action<MemInfoTraceData> Action;
        #endregion
    }

    public sealed class SampledProfileTraceData : TraceEvent
    {
        public Address InstructionPointer { get { return GetAddressAt(0); } }
        // public int ThreadID { get { return GetInt32At(HostOffset(4, 1)); } }
        public int Count { get { return GetInt16At(HostOffset(8, 1)); } }

        // ExecutingDPC, ExecutingISR and Priority only have non-zero values on Win8 and above. 
        /// <summary>
        /// Are we currently executing a Deferred Procedure Call (a mechanism the kernel uses to
        /// 'steal' a thread to run its own work).  If this is true, the CPU time is really 
        /// not logically related to the process (it is kernel time).  
        /// </summary>
        public bool ExecutingDPC { get { return (GetByteAt(HostOffset(10, 1)) & 1) != 0; } }
        /// <summary>
        /// Are we currently executing a Interrupt Service Routine?   Like ExecutingDPC if this
        /// is true the thread is really doing Kernel work, not work for the process.  
        /// </summary>
        public bool ExecutingISR { get { return (GetByteAt(HostOffset(10, 1)) & 2) != 0; } }
        /// <summary>
        /// NonProcess is true if ExecutingDPC or ExecutingISR is true.
        /// </summary>
        public bool NonProcess { get { return (GetByteAt(HostOffset(10, 1)) & 3) != 0; } }
        // next bit is reserved
        /// <summary>
        /// The thread's current priority (higher is more likely to run).   A normal thread with a normal base 
        /// priority is 8.   
        /// see http://msdn.microsoft.com/en-us/library/windows/desktop/ms685100(v=vs.85).aspx for more
        /// </summary>
        public int Priority { get { return (GetByteAt(HostOffset(10, 1)) >> 3) & 0x1F; } }
        /// <summary>
        /// Your scheduling If the thread is not part of a scheduling group, this is 0 (see callout.c) 
        /// </summary>
        public int Rank { get { return GetByteAt(HostOffset(11, 1)); } }
        // 16 bits of reserved space, seems to be a flags field
        #region Private
        internal SampledProfileTraceData(Action<SampledProfileTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<SampledProfileTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 2 && EventDataLength != HostOffset(12, 1)));
            Debug.Assert(!(Version > 2 && EventDataLength < HostOffset(12, 1)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "InstructionPointer", InstructionPointer);
            XmlAttrib(sb, "ThreadID", ThreadID);
            XmlAttrib(sb, "Count", Count);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "InstructionPointer", "ProcessorNumber", "Priority", "ExecutingDPC", "ExecutingISR", "Rank", "Count" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return InstructionPointer;
                case 1:
                    return ProcessorNumber;
                case 2:
                    return Priority;
                case 3:
                    return ExecutingDPC;
                case 4:
                    return ExecutingISR;
                case 5:
                    return Rank;
                case 6:
                    return Count;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<SampledProfileTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;

        /// <summary>
        /// Indicate that the Address is a code address that needs symbolic information
        /// </summary>
        internal override bool LogCodeAddresses(Func<TraceEvent, Address, bool> callBack)
        {
            return callBack(this, InstructionPointer);
        }

        internal override unsafe void FixupData()
        {
            if (eventRecord->EventHeader.ThreadId == -1)
            {
                eventRecord->EventHeader.ThreadId = GetInt32At(HostOffset(4, 1));
            }

            if (eventRecord->EventHeader.ProcessId == -1)
            {
                eventRecord->EventHeader.ProcessId = state.ThreadIDToProcessID(ThreadID, TimeStampQPC);
            }
        }

        public override unsafe int ProcessID
        {
            get
            {
                // We try to fix up the process ID 'on the fly' however in the case of a circular buffer,
                // We simply don't know the process ID until the end of the trace.  Thus you to check and
                // possibly try again.  
                var ret = eventRecord->EventHeader.ProcessId;
                if (ret == -1)
                {
                    ret = state.ThreadIDToProcessID(ThreadID, TimeStampQPC);
                }

                return ret;
            }
        }
        #endregion
    }

    /// <summary>
    /// PMC (Precise Machine Counter) events are fired when a CPU counter trips.  The the ProfileSource identifies
    /// which counter it is.   The PerfInfoCollectionStart events will tell you the count that was configured to trip
    /// the event.  
    /// </summary>
    public sealed class PMCCounterProfTraceData : TraceEvent
    {
        public Address InstructionPointer { get { return GetAddressAt(0); } }
        // public int ThreadID { get { return GetInt32At(HostOffset(4, 1)); } }
        public int ProfileSource { get { return GetInt16At(HostOffset(8, 1)); } }
        // short Reserved;
        #region Private
        internal PMCCounterProfTraceData(Action<PMCCounterProfTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<PMCCounterProfTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 2 && EventDataLength != HostOffset(12, 1)));
            Debug.Assert(!(Version > 2 && EventDataLength < HostOffset(12, 1)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "InstructionPointer", InstructionPointer);
            XmlAttrib(sb, "ThreadID", ThreadID);
            XmlAttrib(sb, "ProfileSource", ProfileSource);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "InstructionPointer", "ThreadID", "ProcessorNumber", "ProfileSource" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return InstructionPointer;
                case 1:
                    return ThreadID;
                case 2:
                    return ProcessorNumber;
                case 3:
                    return ProfileSource;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<PMCCounterProfTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;

        /// <summary>
        /// Indicate that Address is a code address that needs symbolic information
        /// </summary>
        internal override bool LogCodeAddresses(Func<TraceEvent, Address, bool> callBack)
        {
            return callBack(this, InstructionPointer);
        }

        internal override unsafe void FixupData()
        {
            if (eventRecord->EventHeader.ThreadId == -1)
            {
                eventRecord->EventHeader.ThreadId = GetInt32At(HostOffset(4, 1));
            }

            if (eventRecord->EventHeader.ProcessId == -1)
            {
                eventRecord->EventHeader.ProcessId = state.ThreadIDToProcessID(ThreadID, TimeStampQPC);
            }
        }

        public override unsafe int ProcessID
        {
            get
            {
                // We try to fix up the process ID 'on the fly' however in the case of a circular buffer,
                // We simply don't know the process ID until the end of the trace.  Thus you to check and
                // possibly try again.  
                var ret = eventRecord->EventHeader.ProcessId;
                if (ret == -1)
                {
                    ret = state.ThreadIDToProcessID(ThreadID, TimeStampQPC);
                }

                return ret;
            }
        }
        #endregion
    }

#if false   // Removed because I don't think it is used and it needs to conform to array convention.  
    public sealed class BatchedSampledProfileTraceData : TraceEvent
    {
        /// <summary>
        /// A BatchedSampleProfile contains many samples in a single payload.  The batchCount
        /// indicates the number of samples in this payload.  Each sample has a
        /// InstructionPointer, ThreadID and InstanceCount
        /// </summary>
        public int BatchCount { get { return GetInt32At(0); } }
        /// <summary>
        /// The instruction pointer associated with this sample 
        /// </summary>
        public Address InstructionPointer(int index)
        {
            Debug.Assert(0 <= index && index < BatchCount);
            int ptrSize = PointerSize;
            return GetAddressAt(4 + index * (ptrSize + 8));
        }
        /// <summary>
        /// The thread ID associated with the sample 
        /// </summary>
        public int InstanceThreadID(int index)
        {
            Debug.Assert(0 <= index && index < BatchCount);
            int ptrSize = PointerSize;
            return GetInt32At(4 + ptrSize + index * (ptrSize + 8));
        }

        /// <summary>
        /// Each sample may represent multiple instances of samples with the same Instruction
        /// Pointer and ThreadID.  
        /// </summary>
        /// <returns></returns>
        public int InstanceCount(int index)
        {
            Debug.Assert(0 <= index && index < BatchCount);
            int ptrSize = PointerSize;
            return GetInt32At(4 + 4 + ptrSize + index * (ptrSize + 8));
        }

    #region Private
        internal BatchedSampledProfileTraceData(Action<BatchedSampledProfileTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            this.Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
             get { return Action; }
             set { Action = (Action<BatchedSampledProfileTraceData>) value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(EventDataLength == BatchCount * HostOffset(12, 1) + 4);
            Action(this);
        }

        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "BatchCount", BatchCount);
            sb.Append(">").AppendLine();
            for (int i = 0; i < BatchCount; i++)
            {
                sb.Append("   <Sample");
                XmlAttribHex(sb, "InstructionPointer", InstructionPointer(i));
                XmlAttrib(sb, "InstanceThreadID", InstanceThreadID(i));
                XmlAttrib(sb, "InstanceCount", InstanceCount(i));
                sb.Append("/>").AppendLine();
            }
            sb.Append("</Event>");
            return sb;
        }
        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[0];
                return payloadNames;
            }
        }
        public override object PayloadValue(int index)
        {
            return null;
        }

        private event Action<BatchedSampledProfileTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;

        /// <summary>
        /// Indicate that Address is a code address that needs symbolic information
        /// </summary>
        internal override bool LogCodeAddresses(Func<TraceEvent, Address, bool> callBack)
        {
            bool ret = true;
            for (int i = 0; i < BatchCount; i++)
                if (!callBack(this, InstructionPointer(i)))
                    ret = false;
            return ret;
        }
    #endregion
    }
#endif

    public sealed class SampledProfileIntervalTraceData : TraceEvent
    {
        // This is 0 for the timeer, but is non-zero for CPU counter, and this number identifies the CPU counter. 
        public int SampleSource { get { return GetInt32At(0); } }
        public int NewInterval { get { return GetInt32At(4); } }
        public int OldInterval { get { return GetInt32At(8); } }

        #region Private
        internal SampledProfileIntervalTraceData(Action<SampledProfileIntervalTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<SampledProfileIntervalTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 2 && EventDataLength != 12));
            Debug.Assert(!(Version > 2 && EventDataLength < 12));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "SampleSource", SampleSource);
            XmlAttrib(sb, "NewInterval", NewInterval);
            XmlAttrib(sb, "OldInterval", OldInterval);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "SampleSource", "NewInterval", "OldInterval" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return SampleSource;
                case 1:
                    return NewInterval;
                case 2:
                    return OldInterval;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<SampledProfileIntervalTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class SysCallEnterTraceData : TraceEvent
    {
        public Address SysCallAddress { get { return GetAddressAt(0); } }

        #region Private
        internal SysCallEnterTraceData(Action<SysCallEnterTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<SysCallEnterTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 2 && EventDataLength != HostOffset(4, 1)));
            Debug.Assert(!(Version > 2 && EventDataLength < HostOffset(4, 1)));
            Action(this);
        }

        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "SysCallAddress", SysCallAddress);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "SysCallAddress" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return SysCallAddress;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<SysCallEnterTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;

        /// <summary>
        /// Indicate that the Address is a code address that needs symbolic information
        /// </summary>
        internal override bool LogCodeAddresses(Func<TraceEvent, Address, bool> callBack)
        {
            return callBack(this, SysCallAddress);
        }
        #endregion
    }
    public sealed class SysCallExitTraceData : TraceEvent
    {
        public int SysCallNtStatus { get { return GetInt32At(0); } }

        #region Private
        internal SysCallExitTraceData(Action<SysCallExitTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<SysCallExitTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 2 && EventDataLength != 4));
            Debug.Assert(!(Version > 2 && EventDataLength < 4));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "SysCallNtStatus", SysCallNtStatus);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "SysCallNtStatus" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return SysCallNtStatus;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<SysCallExitTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class ISRTraceData : TraceEvent
    {
        private long InitialTimeQPC { get { return GetInt64At(0); } }

        public double ElapsedTimeMSec { get { return TimeStampRelativeMSec - traceEventSource.QPCTimeToRelMSec(InitialTimeQPC); } }
        public Address Routine { get { return GetAddressAt(8); } }
        public int ReturnValue { get { return GetByteAt(HostOffset(12, 1)); } }
        public int Vector { get { return GetByteAt(HostOffset(13, 1)); } }
        // Skipping Reserved
        public int Message
        {
            get
            {
                if (24 <= HostOffset(20, 1))
                {
                    return GetInt32At(HostOffset(16, 1));
                }
                else
                {
                    return 0;
                }
            }
        }

        #region Private
        internal ISRTraceData(Action<ISRTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ISRTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 2 && EventDataLength != HostOffset(16, 1)));
            Debug.Assert(!(Version > 2 && EventDataLength < HostOffset(16, 1)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ElapsedTimeMSec", ElapsedTimeMSec);
            XmlAttribHex(sb, "Routine", Routine);
            XmlAttrib(sb, "ReturnValue", ReturnValue);
            XmlAttrib(sb, "Vector", Vector);
            XmlAttrib(sb, "Message", Message);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "ElapsedTimeMSec", "Routine", "ReturnValue", "Vector", "Message", "ProcessorNumber" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ElapsedTimeMSec;
                case 1:
                    return Routine;
                case 2:
                    return ReturnValue;
                case 3:
                    return Vector;
                case 4:
                    return Message;
                case 5:
                    return ProcessorNumber;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ISRTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class DPCTraceData : TraceEvent
    {
        private long InitialTimeQPC { get { return GetInt64At(0); } }

        public double ElapsedTimeMSec { get { return TimeStampRelativeMSec - traceEventSource.QPCTimeToRelMSec(InitialTimeQPC); } }

        public Address Routine { get { return GetAddressAt(8); } }

        #region Private
        internal DPCTraceData(Action<DPCTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<DPCTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 2 && EventDataLength != HostOffset(12, 1)));
            Debug.Assert(!(Version > 2 && EventDataLength < HostOffset(12, 1)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ElapsedTimeMSec", ElapsedTimeMSec);
            XmlAttribHex(sb, "Routine", Routine);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "ElapsedTimeMSec", "Routine", "ProcessorNumber" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ElapsedTimeMSec;
                case 1:
                    return Routine;
                case 2:
                    return ProcessorNumber;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<DPCTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }

    /// <summary>
    /// Collects the call callStacks for some other event.
    /// 
    /// (TODO: always for the event that preceded it on the same thread)?  
    /// </summary>
    public sealed class StackWalkStackTraceData : TraceEvent
    {
        /// <summary>
        /// The timestamp of the event which caused this stack walk using QueryPerformaceCounter
        /// cycles as the tick.
        /// </summary>
        public long EventTimeStampQPC { get { return GetInt64At(0); } }
        /// <summary>
        /// Converts this to a time relative to the start of the trace in msec. 
        /// </summary>
        public double EventTimeStampRelativeMSec { get { return traceEventSource.QPCTimeToRelMSec(EventTimeStampQPC); } }
        /// <summary>
        /// The total number of eventToStack frames collected.  The Windows OS currently has a maximum of 96 frames. 
        /// </summary>
        public int FrameCount { get { return (EventDataLength - 0x10) / PointerSize; } }
        /// <summary>
        /// Fetches the instruction pointer of a eventToStack frame 0 is the deepest frame, and the maximum should
        /// be a thread offset routine (if you get a complete stack).  
        /// </summary>
        /// <param name="index">The index of the frame to fetch.  0 is the CPU EIP, 1 is the Caller of that
        /// routine ...</param>
        /// <returns>The instruction pointer of the specified frame.</returns>
        public Address InstructionPointer(int index)
        {
            Debug.Assert(0 <= index && index < FrameCount);
            return GetAddressAt(16 + index * PointerSize);
        }
        /// <summary>
        /// Access to the instruction pointers as a unsafe memory blob
        /// </summary>
        internal unsafe void* InstructionPointers { get { return ((byte*)DataStart) + 16; } }
        #region Private
        internal StackWalkStackTraceData(Action<StackWalkStackTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<StackWalkStackTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(FrameCount >= 0);
            Action(this);
        }

        /// <summary>
        /// StackWalkTraceData does not set Thread and process ID fields properly.  if that.  
        /// </summary>
        internal override unsafe void FixupData()
        {
            if (eventRecord->EventHeader.ThreadId == -1)
            {
                eventRecord->EventHeader.ThreadId = GetInt32At(0xC);
            }

            if (eventRecord->EventHeader.ProcessId == -1)
            {
                eventRecord->EventHeader.ProcessId = GetInt32At(8);
            }
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "EventTimeStampQPC", EventTimeStampQPC);
            XmlAttrib(sb, "EventTimeStampRelativeMSec", EventTimeStampRelativeMSec.ToString("f4"));
            XmlAttrib(sb, "FrameCount", FrameCount);
            sb.AppendLine(">");
            for (int i = 0; i < FrameCount; i++)
            {
                sb.Append("  ");
                sb.Append("0x").Append(((ulong)InstructionPointer(i)).ToString("x"));
            }
            sb.AppendLine();
            sb.Append("</Event>");
            return sb;
        }
        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "EventTimeStampRelativeMSec", "FrameCount", "IP0", "IP1", "IP2", "IP3" };
                }

                return payloadNames;
            }
        }
        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return EventTimeStampRelativeMSec;
                case 1:
                    return FrameCount;
                case 2:
                case 3:
                case 4:
                case 5:
                    var idx = index - 2;
                    if (idx < FrameCount)
                    {
                        return InstructionPointer(idx);
                    }

                    return 0;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }
        public event Action<StackWalkStackTraceData> Action;

        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }

    /// <summary>
    /// To save space, stack walks in Win8 can be complressed.  The stack walk event only has a 
    /// reference to a stack Key which is then looked up by StackWalkDefTraceData. 
    /// </summary>
    public sealed class StackWalkRefTraceData : TraceEvent
    {
        /// <summary>
        /// The timestamp of the event which caused this stack walk using QueryPerformaceCounter
        /// cycles as the tick.
        /// </summary>
        public long EventTimeStampQPC { get { return GetInt64At(0); } }
        /// <summary>
        /// Converts this to a time relative to the start of the trace in msec. 
        /// </summary>
        public double EventTimeStampRelativeMSec { get { return traceEventSource.QPCTimeToRelMSec(EventTimeStampQPC); } }
        /// <summary>
        /// Returns a key that can be used to look up the stack in KeyDelete or KeyRundown events 
        /// </summary>
        public Address StackKey { get { return (Address)GetIntPtrAt(16); } }
        #region Private
        internal StackWalkRefTraceData(Action<StackWalkRefTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<StackWalkRefTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }

        /// <summary>
        /// StackWalkTraceData does not set Thread and process ID fields properly.  if that.  
        /// </summary>
        internal override unsafe void FixupData()
        {
            if (eventRecord->EventHeader.ThreadId == -1)
            {
                eventRecord->EventHeader.ThreadId = GetInt32At(0xC);
            }

            if (eventRecord->EventHeader.ProcessId == -1)
            {
                eventRecord->EventHeader.ProcessId = GetInt32At(8);
            }
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "EventTimeStampQPC", EventTimeStampQPC);
            XmlAttrib(sb, "EventTimeStampRelativeMSec", EventTimeStampRelativeMSec.ToString("f4"));
            XmlAttrib(sb, "StackKey", StackKey);
            sb.AppendLine("/>");
            return sb;
        }
        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "EventTimeStampRelativeMSec", "StackKey" };
                }

                return payloadNames;
            }
        }
        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return EventTimeStampRelativeMSec;
                case 1:
                    return StackKey;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }
        public event Action<StackWalkRefTraceData> Action;

        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }

    /// <summary>
    /// This event defines a stack and gives it a unique id (the StackKey), which StackWalkRefTraceData can point at.  
    /// </summary>
    public sealed class StackWalkDefTraceData : TraceEvent
    {
        /// <summary>
        /// Returns a key that can be used to look up the stack in KeyDelete or KeyRundown events 
        /// </summary>
        public Address StackKey { get { return (Address)GetIntPtrAt(0); } }
        /// <summary>
        /// The total number of eventToStack frames collected.  The Windows OS currently has a maximum of 96 frames. 
        /// </summary>
        public int FrameCount { get { return (EventDataLength / PointerSize) - 1; } }

        /// <summary>
        /// Fetches the instruction pointer of a eventToStack frame 0 is the deepest frame, and the maximum should
        /// be a thread offset routine (if you get a complete complete).  
        /// </summary>
        /// <param name="index">The index of the frame to fetch.  0 is the CPU EIP, 1 is the Caller of that
        /// routine ...</param>
        /// <returns>The instruction pointer of the specified frame.</returns>
        public Address InstructionPointer(int index)
        {
            Debug.Assert(0 <= index && index < FrameCount);
            return GetAddressAt((index + 1) * PointerSize);
        }

        /// <summary>
        /// Access to the instruction pointers as a unsafe memory blob
        /// </summary>
        internal unsafe void* InstructionPointers { get { return ((byte*)DataStart) + PointerSize; } }
        #region Private
        internal StackWalkDefTraceData(Action<StackWalkDefTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<StackWalkDefTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(FrameCount >= 0);
            Action(this);
        }

        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "StackKey", StackKey);
            XmlAttrib(sb, "FrameCount", FrameCount);
            sb.AppendLine(">");
            for (int i = 0; i < FrameCount; i++)
            {
                sb.Append("  ");
                sb.Append("0x").Append(((ulong)InstructionPointer(i)).ToString("x"));
            }
            sb.AppendLine();
            sb.Append("</Event>");
            return sb;
        }
        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "StackKey", "FrameCount", "IP0", "IP1", "IP2", "IP3" };
                }

                return payloadNames;
            }
        }
        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return StackKey;
                case 1:
                    return FrameCount;
                case 2:
                case 3:
                case 4:
                case 5:
                    var idx = index - 2;
                    if (idx < FrameCount)
                    {
                        return InstructionPointer(idx);
                    }

                    return 0;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }
        public event Action<StackWalkDefTraceData> Action;

        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }

    public sealed class ALPCSendMessageTraceData : TraceEvent
    {
        public int MessageID { get { return GetInt32At(0); } }

        #region Private
        internal ALPCSendMessageTraceData(Action<ALPCSendMessageTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ALPCSendMessageTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 4));
            Debug.Assert(!(Version > 0 && EventDataLength < 4));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "MessageID", MessageID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "MessageID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return MessageID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ALPCSendMessageTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class ALPCReceiveMessageTraceData : TraceEvent
    {
        public int MessageID { get { return GetInt32At(0); } }

        #region Private
        internal ALPCReceiveMessageTraceData(Action<ALPCReceiveMessageTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ALPCReceiveMessageTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 4));
            Debug.Assert(!(Version > 0 && EventDataLength < 4));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "MessageID", MessageID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "MessageID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return MessageID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ALPCReceiveMessageTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class ALPCWaitForReplyTraceData : TraceEvent
    {
        public int MessageID { get { return GetInt32At(0); } }

        #region Private
        internal ALPCWaitForReplyTraceData(Action<ALPCWaitForReplyTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ALPCWaitForReplyTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 4));
            Debug.Assert(!(Version > 0 && EventDataLength < 4));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "MessageID", MessageID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "MessageID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return MessageID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ALPCWaitForReplyTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class ALPCWaitForNewMessageTraceData : TraceEvent
    {
        public int IsServerPort { get { return GetInt32At(0); } }
        public string PortName { get { return GetUTF8StringAt(4); } }

        #region Private
        internal ALPCWaitForNewMessageTraceData(Action<ALPCWaitForNewMessageTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ALPCWaitForNewMessageTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUTF8String(4)));
            Debug.Assert(!(Version > 0 && EventDataLength < SkipUTF8String(4)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "IsServerPort", IsServerPort);
            XmlAttrib(sb, "PortName", PortName);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "IsServerPort", "PortName" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return IsServerPort;
                case 1:
                    return PortName;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ALPCWaitForNewMessageTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class ALPCUnwaitTraceData : TraceEvent
    {
        public int Status { get { return GetInt32At(0); } }

        #region Private
        internal ALPCUnwaitTraceData(Action<ALPCUnwaitTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ALPCUnwaitTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 4));
            Debug.Assert(!(Version > 0 && EventDataLength < 4));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "Status", Status);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Status" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Status;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ALPCUnwaitTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class SystemConfigCPUTraceData : TraceEvent
    {
        public int MHz { get { return GetInt32At(0); } }
        public int NumberOfProcessors { get { return GetInt32At(4); } }
        public int MemSize { get { return GetInt32At(8); } }
        public int PageSize { get { return GetInt32At(12); } }
        public int AllocationGranularity { get { return GetInt32At(16); } }
        public string ComputerName { get { return GetFixedUnicodeStringAt(256, (20)); } }
        public string DomainName { get { if (Version >= 2) { return GetFixedUnicodeStringAt(134, (532)); } return GetFixedUnicodeStringAt(132, (532)); } }
        public Address HyperThreadingFlag { get { if (Version >= 2) { return GetAddressAt(800); } return GetAddressAt(796); } }

        #region Private
        internal SystemConfigCPUTraceData(Action<SystemConfigCPUTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<SystemConfigCPUTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength < HostOffset(800, 1)));     // TODO hand changed
            Debug.Assert(!(Version == 1 && EventDataLength < HostOffset(800, 1)));     // TODO hand changed
            Debug.Assert(!(Version == 2 && EventDataLength != HostOffset(804, 1)));
            Debug.Assert(!(Version > 2 && EventDataLength < HostOffset(804, 1)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "MHz", MHz);
            XmlAttrib(sb, "NumberOfProcessors", NumberOfProcessors);
            XmlAttrib(sb, "MemSize", MemSize);
            XmlAttrib(sb, "PageSize", PageSize);
            XmlAttrib(sb, "AllocationGranularity", AllocationGranularity);
            XmlAttrib(sb, "ComputerName", ComputerName);
            XmlAttrib(sb, "DomainName", DomainName);
            XmlAttribHex(sb, "HyperThreadingFlag", HyperThreadingFlag);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "MHz", "NumberOfProcessors", "MemSize", "PageSize", "AllocationGranularity", "ComputerName", "DomainName", "HyperThreadingFlag" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return MHz;
                case 1:
                    return NumberOfProcessors;
                case 2:
                    return MemSize;
                case 3:
                    return PageSize;
                case 4:
                    return AllocationGranularity;
                case 5:
                    return ComputerName;
                case 6:
                    return DomainName;
                case 7:
                    return HyperThreadingFlag;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<SystemConfigCPUTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class SystemConfigPhyDiskTraceData : TraceEvent
    {
        public int DiskNumber { get { return GetInt32At(0); } }
        public int BytesPerSector { get { return GetInt32At(4); } }
        public int SectorsPerTrack { get { return GetInt32At(8); } }
        public int TracksPerCylinder { get { return GetInt32At(12); } }
        public long Cylinders { get { return GetInt64At(16); } }
        public int SCSIPort { get { return GetInt32At(24); } }
        public int SCSIPath { get { return GetInt32At(28); } }
        public int SCSITarget { get { return GetInt32At(32); } }
        public int SCSILun { get { return GetInt32At(36); } }
        public string Manufacturer { get { return GetFixedUnicodeStringAt(256, (40)); } }
        public int PartitionCount { get { return GetInt32At(552); } }
        public int WriteCacheEnabled { get { return GetByteAt(556); } }
        // Skipping Pad
        public string BootDriveLetter { get { return GetFixedUnicodeStringAt(3, (558)); } }
        public string Spare { get { return GetFixedUnicodeStringAt(2, (564)); } }

        #region Private
        internal SystemConfigPhyDiskTraceData(Action<SystemConfigPhyDiskTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<SystemConfigPhyDiskTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength < 568));    // TODO changed by hand
            Debug.Assert(!(Version == 1 && EventDataLength < 568));    // TODO changed by hand
            Debug.Assert(!(Version == 2 && EventDataLength != 568));
            Debug.Assert(!(Version > 2 && EventDataLength < 568));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "DiskNumber", DiskNumber);
            XmlAttrib(sb, "BytesPerSector", BytesPerSector);
            XmlAttrib(sb, "SectorsPerTrack", SectorsPerTrack);
            XmlAttrib(sb, "TracksPerCylinder", TracksPerCylinder);
            XmlAttrib(sb, "Cylinders", Cylinders);
            XmlAttrib(sb, "SCSIPort", SCSIPort);
            XmlAttrib(sb, "SCSIPath", SCSIPath);
            XmlAttrib(sb, "SCSITarget", SCSITarget);
            XmlAttrib(sb, "SCSILun", SCSILun);
            XmlAttrib(sb, "Manufacturer", Manufacturer);
            XmlAttrib(sb, "PartitionCount", PartitionCount);
            XmlAttrib(sb, "WriteCacheEnabled", WriteCacheEnabled);
            XmlAttrib(sb, "BootDriveLetter", BootDriveLetter);
            XmlAttrib(sb, "Spare", Spare);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "DiskNumber", "BytesPerSector", "SectorsPerTrack", "TracksPerCylinder", "Cylinders", "SCSIPort", "SCSIPath", "SCSITarget", "SCSILun", "Manufacturer", "PartitionCount", "WriteCacheEnabled", "BootDriveLetter", "Spare" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return DiskNumber;
                case 1:
                    return BytesPerSector;
                case 2:
                    return SectorsPerTrack;
                case 3:
                    return TracksPerCylinder;
                case 4:
                    return Cylinders;
                case 5:
                    return SCSIPort;
                case 6:
                    return SCSIPath;
                case 7:
                    return SCSITarget;
                case 8:
                    return SCSILun;
                case 9:
                    return Manufacturer;
                case 10:
                    return PartitionCount;
                case 11:
                    return WriteCacheEnabled;
                case 12:
                    return BootDriveLetter;
                case 13:
                    return Spare;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<SystemConfigPhyDiskTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class SystemConfigLogDiskTraceData : TraceEvent
    {
        public long StartOffset { get { return GetInt64At(0); } }
        public long PartitionSize { get { return GetInt64At(8); } }
        public int DiskNumber { get { return GetInt32At(16); } }
        public int Size { get { return GetInt32At(20); } }
        public int DriveType { get { return GetInt32At(24); } }
        public string DriveLetterString { get { return GetFixedUnicodeStringAt(4, (28)); } }
        // Skipping Pad1
        public int PartitionNumber { get { return GetInt32At(40); } }
        public int SectorsPerCluster { get { return GetInt32At(44); } }
        public int BytesPerSector { get { return GetInt32At(48); } }
        // Skipping Pad2
        public long NumberOfFreeClusters { get { return GetInt64At(56); } }
        public long TotalNumberOfClusters { get { return GetInt64At(64); } }
        public string FileSystem { get { return GetFixedUnicodeStringAt(16, (72)); } }
        public int VolumeExt { get { return GetInt32At(104); } }
        // Skipping Pad3

        #region Private
        internal SystemConfigLogDiskTraceData(Action<SystemConfigLogDiskTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<SystemConfigLogDiskTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength < 108));        // TODO fixed by hand
            Debug.Assert(!(Version == 1 && EventDataLength < 108));        // TODO fixed by hand
            Debug.Assert(!(Version == 2 && EventDataLength < 112));        // TODO fixed by hand
            Debug.Assert(!(Version > 2 && EventDataLength < 112));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "StartOffset", StartOffset);
            XmlAttrib(sb, "PartitionSize", PartitionSize);
            XmlAttrib(sb, "DiskNumber", DiskNumber);
            XmlAttrib(sb, "Size", Size);
            XmlAttrib(sb, "DriveType", DriveType);
            XmlAttrib(sb, "DriveLetterString", DriveLetterString);
            XmlAttrib(sb, "PartitionNumber", PartitionNumber);
            XmlAttrib(sb, "SectorsPerCluster", SectorsPerCluster);
            XmlAttrib(sb, "BytesPerSector", BytesPerSector);
            XmlAttrib(sb, "NumberOfFreeClusters", NumberOfFreeClusters);
            XmlAttrib(sb, "TotalNumberOfClusters", TotalNumberOfClusters);
            XmlAttrib(sb, "FileSystem", FileSystem);
            XmlAttrib(sb, "VolumeExt", VolumeExt);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "StartOffset", "PartitionSize", "DiskNumber", "Size", "DriveType", "DriveLetterString", "PartitionNumber", "SectorsPerCluster", "BytesPerSector", "NumberOfFreeClusters", "TotalNumberOfClusters", "FileSystem", "VolumeExt" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return StartOffset;
                case 1:
                    return PartitionSize;
                case 2:
                    return DiskNumber;
                case 3:
                    return Size;
                case 4:
                    return DriveType;
                case 5:
                    return DriveLetterString;
                case 6:
                    return PartitionNumber;
                case 7:
                    return SectorsPerCluster;
                case 8:
                    return BytesPerSector;
                case 9:
                    return NumberOfFreeClusters;
                case 10:
                    return TotalNumberOfClusters;
                case 11:
                    return FileSystem;
                case 12:
                    return VolumeExt;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<SystemConfigLogDiskTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class SystemConfigNICTraceData : TraceEvent
    {
        public int PhysicalAddrLen { get { if (Version >= 2) { return GetInt32At(8); } return GetInt32At(516); } }
        public long PhysicalAddr { get { if (Version >= 2) { return GetInt64At(0); } return 0; } }
        public int Ipv4Index { get { if (Version >= 2) { return GetInt32At(12); } return GetInt32At(512); ; } }
        public int Ipv6Index { get { if (Version >= 2) { return GetInt32At(16); } return 0; } }
        public string NICDescription { get { if (Version >= 2) { return GetUnicodeStringAt(20); } return GetFixedUnicodeStringAt(256, (0)); } }
        public string IpAddresses { get { if (Version >= 2) { return GetUnicodeStringAt(SkipUnicodeString(20)); } return ""; } }
        public string DnsServerAddresses { get { if (Version >= 2) { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(20))); } return ""; } }

        #region Private
        internal SystemConfigNICTraceData(Action<SystemConfigNICTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<SystemConfigNICTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength < 584));    // TODO changed by hand
            Debug.Assert(!(Version == 1 && EventDataLength < 584));    // TODO changed by  hand
            Debug.Assert(!(Version == 2 && EventDataLength != SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(20)))));
            Debug.Assert(!(Version > 2 && EventDataLength < SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(20)))));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "PhysicalAddrLen", PhysicalAddrLen);
            XmlAttrib(sb, "PhysicalAddr", PhysicalAddr);
            XmlAttrib(sb, "Ipv4Index", Ipv4Index);
            XmlAttrib(sb, "Ipv6Index", Ipv6Index);
            XmlAttrib(sb, "NICDescription", NICDescription);
            XmlAttrib(sb, "IpAddresses", IpAddresses);
            XmlAttrib(sb, "DnsServerAddresses", DnsServerAddresses);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "PhysicalAddrLen", "PhysicalAddr", "Ipv4Index", "Ipv6Index", "NICDescription", "IpAddresses", "DnsServerAddresses" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return PhysicalAddrLen;
                case 1:
                    return PhysicalAddr;
                case 2:
                    return Ipv4Index;
                case 3:
                    return Ipv6Index;
                case 4:
                    return NICDescription;
                case 5:
                    return IpAddresses;
                case 6:
                    return DnsServerAddresses;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<SystemConfigNICTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class SystemConfigVideoTraceData : TraceEvent
    {
        public int MemorySize { get { return GetInt32At(0); } }
        public int XResolution { get { return GetInt32At(4); } }
        public int YResolution { get { return GetInt32At(8); } }
        public int BitsPerPixel { get { return GetInt32At(12); } }
        public int VRefresh { get { return GetInt32At(16); } }
        public string ChipType { get { return GetFixedUnicodeStringAt(256, (20)); } }
        public string DACType { get { return GetFixedUnicodeStringAt(256, (532)); } }
        public string AdapterString { get { return GetFixedUnicodeStringAt(256, (1044)); } }
        public string BiosString { get { return GetFixedUnicodeStringAt(256, (1556)); } }
        public string DeviceID { get { return GetFixedUnicodeStringAt(256, (2068)); } }
        public int StateFlags { get { return GetInt32At(2580); } }

        #region Private
        internal SystemConfigVideoTraceData(Action<SystemConfigVideoTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<SystemConfigVideoTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 2584));
            Debug.Assert(!(Version == 1 && EventDataLength != 2584));
            Debug.Assert(!(Version == 2 && EventDataLength != 2584));
            Debug.Assert(!(Version > 2 && EventDataLength < 2584));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "MemorySize", MemorySize);
            XmlAttrib(sb, "XResolution", XResolution);
            XmlAttrib(sb, "YResolution", YResolution);
            XmlAttrib(sb, "BitsPerPixel", BitsPerPixel);
            XmlAttrib(sb, "VRefresh", VRefresh);
            XmlAttrib(sb, "ChipType", ChipType);
            XmlAttrib(sb, "DACType", DACType);
            XmlAttrib(sb, "AdapterString", AdapterString);
            XmlAttrib(sb, "BiosString", BiosString);
            XmlAttrib(sb, "DeviceID", DeviceID);
            XmlAttribHex(sb, "StateFlags", StateFlags);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "MemorySize", "XResolution", "YResolution", "BitsPerPixel", "VRefresh", "ChipType", "DACType", "AdapterString", "BiosString", "DeviceID", "StateFlags" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return MemorySize;
                case 1:
                    return XResolution;
                case 2:
                    return YResolution;
                case 3:
                    return BitsPerPixel;
                case 4:
                    return VRefresh;
                case 5:
                    return ChipType;
                case 6:
                    return DACType;
                case 7:
                    return AdapterString;
                case 8:
                    return BiosString;
                case 9:
                    return DeviceID;
                case 10:
                    return StateFlags;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<SystemConfigVideoTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class SystemConfigServicesTraceData : TraceEvent
    {
        public string ServiceName { get { if (Version >= 2) { return GetUnicodeStringAt(12); } return GetFixedUnicodeStringAt(34, (0)); } }
        public string DisplayName { get { if (Version >= 2) { return GetUnicodeStringAt(SkipUnicodeString(12)); } return GetFixedUnicodeStringAt(256, (68)); } }
        // public new string ProcessName { get { if (Version >= 2) return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(12))); return GetFixedUnicodeStringAt(34, (580)); } }
        // public int ProcessID { get { if (Version >= 2) return GetInt32At(0); return GetInt32At(648); } }
        // TODO does this need FixupData?
        public int ServiceState { get { if (Version >= 2) { return GetInt32At(4); } return 0; } }
        public int SubProcessTag { get { if (Version >= 2) { return GetInt32At(8); } return 0; } }

        #region Private
        internal SystemConfigServicesTraceData(Action<SystemConfigServicesTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<SystemConfigServicesTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength < 652));     // TODO fixed by hand
            Debug.Assert(!(Version == 1 && EventDataLength < 652));     // TODO fixed by hand
            Debug.Assert(!(Version == 2 && EventDataLength != SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(12)))));
            Debug.Assert(!(Version > 2 && EventDataLength < SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(12)))));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ServiceName", ServiceName);
            XmlAttrib(sb, "DisplayName", DisplayName);
            XmlAttrib(sb, "ProcessName", ProcessName);
            XmlAttrib(sb, "ProcessID", ProcessID);
            XmlAttribHex(sb, "ServiceState", ServiceState);
            XmlAttribHex(sb, "SubProcessTag", SubProcessTag);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "ServiceName", "DisplayName", "ProcessName", "ProcessID", "ServiceState", "SubProcessTag" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ServiceName;
                case 1:
                    return DisplayName;
                case 2:
                    return ProcessName;
                case 3:
                    return ProcessID;
                case 4:
                    return ServiceState;
                case 5:
                    return SubProcessTag;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<SystemConfigServicesTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;


        internal override unsafe void FixupData()
        {
            // Preserve the illusion that this event comes from the service it is for.
            // public int ProcessID { get { if (Version >= 2) return GetInt32At(0); return GetInt32At(648); } }
            // TODO does this need FixupData?
            if (Version >= 2)
            {
                eventRecord->EventHeader.ProcessId = GetInt32At(0);
            }
        }

        #endregion
    }
    public sealed class SystemConfigPowerTraceData : TraceEvent
    {
        public int S1 { get { return GetByteAt(0); } }
        public int S2 { get { return GetByteAt(1); } }
        public int S3 { get { return GetByteAt(2); } }
        public int S4 { get { return GetByteAt(3); } }
        public int S5 { get { return GetByteAt(4); } }
        // Skipping Pad1
        // Skipping Pad2
        // Skipping Pad3

        #region Private
        internal SystemConfigPowerTraceData(Action<SystemConfigPowerTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<SystemConfigPowerTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 8));
            Debug.Assert(!(Version == 1 && EventDataLength != 8));
            Debug.Assert(!(Version == 2 && EventDataLength != 8));
            Debug.Assert(!(Version > 2 && EventDataLength < 8));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "S1", S1);
            XmlAttrib(sb, "S2", S2);
            XmlAttrib(sb, "S3", S3);
            XmlAttrib(sb, "S4", S4);
            XmlAttrib(sb, "S5", S5);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "S1", "S2", "S3", "S4", "S5" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return S1;
                case 1:
                    return S2;
                case 2:
                    return S3;
                case 3:
                    return S4;
                case 4:
                    return S5;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<SystemConfigPowerTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class SystemConfigIRQTraceData : TraceEvent
    {
        public long IRQAffinity { get { return GetInt64At(0); } }
        public int IRQNum { get { return GetInt32At(8); } }
        // TODO hand modified.   Fix for real 
        public int DeviceDescriptionLen
        {
            get
            {
                if (Version >= 3)
                {
                    return GetInt32At(16);
                }
                else
                {
                    return GetInt32At(12);
                }
            }
        }
        public string DeviceDescription
        {
            get
            {
                if (Version >= 3)
                {
                    return GetUnicodeStringAt(20);
                }
                else
                {
                    return GetUnicodeStringAt(16);
                }
            }
        }

        #region Private
        internal SystemConfigIRQTraceData(Action<SystemConfigIRQTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<SystemConfigIRQTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUnicodeString(16)));
            Debug.Assert(!(Version == 1 && EventDataLength != SkipUnicodeString(16)));
            Debug.Assert(!(Version == 2 && EventDataLength != SkipUnicodeString(16)));
            Debug.Assert(!(Version > 2 && EventDataLength < SkipUnicodeString(16)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "IRQAffinity", IRQAffinity);
            XmlAttrib(sb, "IRQNum", IRQNum);
            XmlAttrib(sb, "DeviceDescriptionLen", DeviceDescriptionLen);
            XmlAttrib(sb, "DeviceDescription", DeviceDescription);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "IRQAffinity", "IRQNum", "DeviceDescriptionLen", "DeviceDescription" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return IRQAffinity;
                case 1:
                    return IRQNum;
                case 2:
                    return DeviceDescriptionLen;
                case 3:
                    return DeviceDescription;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<SystemConfigIRQTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class SystemConfigPnPTraceData : TraceEvent
    {
        // see  WMI_PNP_RECORD_V3  WMI_PNP_RECORD_V4  WMI_PNP_RECORD_V5 in ntwmi.h
        // DevStatus
        // DevProblem
        public string DeviceID { get { return GetUnicodeStringAt(DeviceIDStart); } }
        public string DeviceDescription { get { return GetUnicodeStringAt(SkipUnicodeString(DeviceIDStart)); } }
        public string FriendlyName { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(DeviceIDStart))); } }
        public string PdoName
        {
            get
            {
                if (Version < 4)
                {
                    return "";
                }

                return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(DeviceIDStart))));
            }
        }
        public string ServiceName
        {
            get
            {
                if (Version < 4)
                {
                    return "";
                }

                return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(DeviceIDStart)))));
            }
        }
        // UpperFilters
        // LowerFilters

        #region Private
        public int DeviceIDStart
        {
            get
            {
                if (Version <= 3)
                {
                    return 12;  // three lengths come first.  (but are redundant since the strings are null terminated)
                }

                if (Version == 4)
                {
                    return 24;  // ClassGuid, upperFilterCount lowerFilterCount
                }
                // Version 5 or more 
                return 32;  // ClassGuid, upperFilterCount lowerFilterCount DevStatus DevProblem
            }
        }

        internal SystemConfigPnPTraceData(Action<SystemConfigPnPTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<SystemConfigPnPTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(EventDataLength < SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(DeviceIDStart)))));
            Debug.Assert(!(4 <= Version && EventDataLength < SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(DeviceIDStart)))))));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "DeviceID", DeviceID);
            XmlAttrib(sb, "DeviceDescription", DeviceDescription);
            XmlAttrib(sb, "FriendlyName", FriendlyName);
            XmlAttrib(sb, "PdoName", PdoName);
            XmlAttrib(sb, "ServiceName", ServiceName);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "DeviceID", "DeviceDescription", "FriendlyName", "PdoName", "ServiceName" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return DeviceID;
                case 1:
                    return DeviceDescription;
                case 2:
                    return FriendlyName;
                case 3:
                    return PdoName;
                case 4:
                    return ServiceName;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }
        private event Action<SystemConfigPnPTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class SystemConfigNetworkTraceData : TraceEvent
    {
        public int TcbTablePartitions { get { return GetInt32At(0); } }
        public int MaxHashTableSize { get { return GetInt32At(4); } }
        public int TcpTimedWaitDelay { get { return GetInt32At(12); } }
        public int MaxUserPort { get { return GetInt32At(8); } }

        #region Private
        internal SystemConfigNetworkTraceData(Action<SystemConfigNetworkTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<SystemConfigNetworkTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 2 && EventDataLength != 16));
            Debug.Assert(!(Version > 2 && EventDataLength < 16));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "TcbTablePartitions", TcbTablePartitions);
            XmlAttrib(sb, "MaxHashTableSize", MaxHashTableSize);
            XmlAttrib(sb, "MaxUserPort", MaxUserPort);
            XmlAttrib(sb, "TcpTimedWaitDelay", TcpTimedWaitDelay);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "TcbTablePartitions", "MaxHashTableSize", "MaxUserPort", "TcpTimedWaitDelay" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return TcbTablePartitions;
                case 1:
                    return MaxHashTableSize;
                case 2:
                    return MaxUserPort;
                case 3:
                    return TcpTimedWaitDelay;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<SystemConfigNetworkTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }
    public sealed class SystemConfigIDEChannelTraceData : TraceEvent
    {
        public int TargetID { get { return GetInt32At(0); } }
        public int DeviceType { get { return GetInt32At(4); } }
        public int DeviceTimingMode { get { return GetInt32At(8); } }
        public int LocationInformationLen { get { return GetInt32At(12); } }
        public string LocationInformation { get { return GetUnicodeStringAt(16); } }

        #region Private
        internal SystemConfigIDEChannelTraceData(Action<SystemConfigIDEChannelTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<SystemConfigIDEChannelTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 2 && EventDataLength != SkipUnicodeString(16)));
            Debug.Assert(!(Version > 2 && EventDataLength < SkipUnicodeString(16)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "TargetID", TargetID);
            XmlAttribHex(sb, "DeviceType", DeviceType);
            XmlAttribHex(sb, "DeviceTimingMode", DeviceTimingMode);
            XmlAttrib(sb, "LocationInformationLen", LocationInformationLen);
            XmlAttrib(sb, "LocationInformation", LocationInformation);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "TargetID", "DeviceType", "DeviceTimingMode", "LocationInformationLen", "LocationInformation" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return TargetID;
                case 1:
                    return DeviceType;
                case 2:
                    return DeviceTimingMode;
                case 3:
                    return LocationInformationLen;
                case 4:
                    return LocationInformation;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<SystemConfigIDEChannelTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }

    public sealed class VirtualAllocTraceData : TraceEvent
    {
        [Flags]
        public enum VirtualAllocFlags
        {
            MEM_COMMIT = 0x1000,
            MEM_RESERVE = 0x2000,
            MEM_DECOMMIT = 0x4000,
            MEM_RELEASE = 0x8000,
            /*
            MEM_RESET = 0x80000,
            MEM_LARGE_PAGES = 0x20000000,
            MEM_PHYSICAL = 0x400000,
            MEM_TOP_DOWN = 0x100000,
            MEM_WRITE_WATCH = 0x200000,
            */
        };

        public Address BaseAddr { get { return GetAddressAt(0); } }
        public long Length { get { return (long)GetAddressAt(HostOffset(4, 1)); } }
        // Process ID is next (we fix it up). 
        public VirtualAllocFlags Flags { get { return (VirtualAllocFlags)GetInt32At(HostOffset(0xC, 2)); } }

        #region Private
        internal VirtualAllocTraceData(Action<VirtualAllocTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<VirtualAllocTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(EventDataLength == HostOffset(0x10, 2), "Unexpected data length");
            Action(this);
        }

        internal override unsafe void FixupData()
        {
            // We always choose the process ID to be the process where for the allocation happens 
            // TODO Is this really a good idea?  
            // Debug.Assert(eventRecord->EventHeader.ProcessId == -1 || eventRecord->EventHeader.ProcessId == GetInt32At(HostOffset(8, 2)));
            eventRecord->EventHeader.ProcessId = GetInt32At(HostOffset(8, 2));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "BaseAddr", BaseAddr);
            XmlAttribHex(sb, "Length", Length);
            XmlAttrib(sb, "Flags", Flags);
            sb.Append("/>");
            return sb;
        }
        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "BaseAddr", "Length", "Flags", "LengthHex", "EndAddr" };
                }

                return payloadNames;
            }
        }
        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return BaseAddr;
                case 1:
                    return Length;
                case 2:
                    return Flags;
                case 3:
                    return "0x" + Length.ToString("x");
                case 4:
                    return "0x" + ((Address)Length + BaseAddr).ToString("x");
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }
        public event Action<VirtualAllocTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }

    public sealed class ObjectHandleTraceData : TraceEvent
    {
        public Address Object { get { return GetAddressAt(0); } }
        public int Handle { get { return GetInt32At(HostOffset(4, 1)); } }
        public int ObjectType { get { return GetInt16At(HostOffset(8, 1)); } }
        public string ObjectName { get { return state.ObjectToName(Object, TimeStampQPC); } }
        public string ObjectTypeName { get { return state.ObjectTypeToName(ObjectType); } }

        // ObjectName is last, but is always empty (thus 2 bytes of 0)
        #region Private
        internal ObjectHandleTraceData(Action<ObjectHandleTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ObjectHandleTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(EventDataLength >= HostOffset(12, 1), "Unexpected data length");
            Action(this);
        }

        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "Object", Object);
            XmlAttribHex(sb, "Handle", Handle);
            XmlAttrib(sb, "ObjectType", ObjectType);
            XmlAttrib(sb, "ObjectName", ObjectName);
            XmlAttrib(sb, "ObjectTypeName", ObjectTypeName);
            sb.Append("/>");
            return sb;
        }
        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Object", "Handle", "ObjectType", "ObjectName", "ObjectTypeName" };
                }

                return payloadNames;
            }
        }
        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Object;
                case 1:
                    return Handle;
                case 2:
                    return ObjectType;
                case 3:
                    return ObjectName;
                case 4:
                    return ObjectTypeName;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }
        public event Action<ObjectHandleTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }

    public sealed class ObjectDuplicateHandleTraceData : TraceEvent
    {
        public Address Object { get { return GetAddressAt(0); } }
        public int SourceHandle { get { return GetInt32At(HostOffset(4, 1)); } }
        public int TargetHandle { get { return GetInt32At(HostOffset(8, 1)); } }
        // TODO Confirm this is the target processID
        public int TargetProcessID { get { return GetInt32At(HostOffset(12, 1)); } }
        public int ObjectType { get { return GetInt16At(HostOffset(16, 1)); } }
        // TODO Confirm this is the target processID
        public int SourceProcessID { get { return GetInt32At(HostOffset(18, 1)); } }
        public string ObjectName { get { return state.ObjectToName(Object, TimeStampQPC); } }
        public string ObjectTypeName { get { return state.ObjectTypeToName(ObjectType); } }

        #region Private
        internal ObjectDuplicateHandleTraceData(Action<ObjectDuplicateHandleTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ObjectDuplicateHandleTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(EventDataLength >= HostOffset(22, 1), "Unexpected data length");
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "Object", Object);
            XmlAttribHex(sb, "SourceHandle", SourceHandle);
            XmlAttrib(sb, "TargetHandle", TargetHandle);
            XmlAttrib(sb, "SourceProcessID", SourceProcessID);
            XmlAttrib(sb, "TargetHandleID", TargetProcessID);
            XmlAttrib(sb, "ObjectType", ObjectType);
            XmlAttrib(sb, "ObjectName", ObjectName);
            XmlAttrib(sb, "ObjectTypeName", ObjectTypeName);
            sb.Append("/>");
            return sb;
        }
        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Object", "SourceHandle", "TargetHandle", "SourceProcessID", "TargetProcessID", "ObjectType", "ObjectName", "ObjectTypeName" };
                }

                return payloadNames;
            }
        }
        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Object;
                case 1:
                    return SourceHandle;
                case 2:
                    return TargetHandle;
                case 3:
                    return SourceProcessID;
                case 4:
                    return TargetProcessID;
                case 5:
                    return ObjectType;
                case 6:
                    return ObjectName;
                case 7:
                    return ObjectTypeName;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }
        public event Action<ObjectDuplicateHandleTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }

    public sealed class ObjectNameTraceData : TraceEvent
    {
        public Address Object { get { return GetAddressAt(0); } }
        // Process ID (we fix it up)
        public int Handle { get { return GetInt32At(HostOffset(8, 1)); } }
        public int ObjectType { get { return GetInt16At(HostOffset(12, 1)); } }
        public string ObjectName { get { return GetUnicodeStringAt(HostOffset(14, 1)); } }

        #region Private
        internal ObjectNameTraceData(Action<ObjectNameTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ObjectNameTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(EventDataLength < SkipUnicodeString(HostOffset(14, 1)), "Unexpected data length");
            Action(this);
        }

        internal override unsafe void FixupData()
        {
            // We always choose the process ID to be the process where for the allocation happens 
            // TODO Is this really a good idea?  
            // Debug.Assert(eventRecord->EventHeader.ProcessId == -1 || eventRecord->EventHeader.ProcessId == GetInt32At(HostOffset(8, 2)));
            eventRecord->EventHeader.ProcessId = GetInt32At(HostOffset(8, 2));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "Object", Object);
            XmlAttribHex(sb, "Handle", Handle);
            XmlAttrib(sb, "ObjectType", ObjectType);
            XmlAttrib(sb, "ObjectName", ObjectName);
            sb.Append("/>");
            return sb;
        }
        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Object", "Handle", "ObjectType", "ObjectName" };
                }

                return payloadNames;
            }
        }
        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Object;
                case 1:
                    return Handle;
                case 2:
                    return ObjectType;
                case 3:
                    return ObjectName;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }
        public event Action<ObjectNameTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }

    public sealed class ObjectTypeNameTraceData : TraceEvent
    {
        public int ObjectType { get { return GetInt16At(0); } }
        // Reserved 2 bytes
        public string ObjectTypeName { get { return GetUnicodeStringAt(4); } }

        #region Private
        internal ObjectTypeNameTraceData(Action<ObjectTypeNameTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ObjectTypeNameTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(EventDataLength < SkipUnicodeString(HostOffset(14, 1)), "Unexpected data length");
            Action(this);
        }

        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ObjectType", ObjectType);
            XmlAttrib(sb, "ObjectTypeName", ObjectTypeName);
            sb.Append("/>");
            return sb;
        }
        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "ObjectType", "ObjectTypeName" };
                }

                return payloadNames;
            }
        }
        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ObjectType;
                case 1:
                    return ObjectTypeName;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }
        public event Action<ObjectTypeNameTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }

    public sealed class DispatcherReadyThreadTraceData : TraceEvent
    {
        public enum AdjustReasonEnum
        {
            None = 0,
            Unwait = 1,
            Boost = 2,
        };

        [Flags]
        public enum ReadyThreadFlags : byte
        {
            ReadiedFromDPC = 1,     // The thread has been readied from DPC (deferred procedure call).
            KernelSwappedOut = 2,   // The kernel stack is currently swapped out.
            ProcessSwappedOut = 4   // The process address space is swapped out.
        }

        public int AwakenedThreadID { get { return GetInt32At(0); } }
        public int AwakenedProcessID { get { return state.ThreadIDToProcessID(AwakenedThreadID, TimeStampQPC); } }
        public AdjustReasonEnum AdjustReason { get { return (AdjustReasonEnum)GetByteAt(4); } }
        public int AdjustIncrement { get { return GetByteAt(5); } }
        public ReadyThreadFlags Flags { get { return (ReadyThreadFlags)GetByteAt(6); } }
        // There is a reserved byte after Flags
        public override unsafe int ProcessID
        {
            get
            {
                // We try to fix up the process ID 'on the fly' however in the case of a circular buffer,
                // We simply don't know the process ID until the end of the trace.  Thus you to check and
                // possibly try again.  
                var ret = eventRecord->EventHeader.ProcessId;
                if (ret == -1)
                {
                    ret = state.ThreadIDToProcessID(ThreadID, TimeStampQPC);
                }

                return ret;
            }
        }
        #region Private
        internal DispatcherReadyThreadTraceData(Action<DispatcherReadyThreadTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, KernelTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            NeedsFixup = true;
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<DispatcherReadyThreadTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(EventDataLength == 8, "Unexpected data length");
            Action(this);
        }

        internal override unsafe void FixupData()
        {
            /* TODO FIX NOW: How do we get the thread ID of who did the awakening? 
            eventRecord->EventHeader.ThreadId = GetInt32At(0);
            eventRecord->EventHeader.ProcessId = state.ThreadIDToProcessID(ThreadID, TimeStampQPC);
             */
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "AwakenedThreadID", AwakenedThreadID);
            XmlAttrib(sb, "AwakenedProcessID", AwakenedProcessID);
            XmlAttrib(sb, "AdjustReason", AdjustReason);
            XmlAttrib(sb, "AdjustIncrement", AdjustIncrement);
            XmlAttrib(sb, "ReadyThreadFlags", Flags);
            sb.Append("/>");
            return sb;
        }
        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "AwakenedThreadID", "AwakenedProcessID", "AdjustReason", "AdjustIncrement", "Flags" };
                }

                return payloadNames;
            }
        }
        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return AwakenedThreadID;
                case 1:
                    return AwakenedProcessID;
                case 2:
                    return AdjustReason;
                case 3:
                    return AdjustIncrement;
                case 4:
                    return Flags;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }
        public event Action<DispatcherReadyThreadTraceData> Action;
        protected internal override void SetState(object newState) { state = (KernelTraceEventParserState)newState; }
        private KernelTraceEventParserState state;
        #endregion
    }

    // [SecuritySafeCritical]
    [System.CodeDom.Compiler.GeneratedCode("traceparsergen", "1.0")]
    public sealed class ThreadPoolTraceEventParser : TraceEventParser
    {
        public static readonly string ProviderName = "ThreadPool";
        public static readonly Guid ProviderGuid = new Guid(unchecked((int)0xc861d0e2), unchecked((short)0xa2c1), unchecked((short)0x4d36), 0x9f, 0x9c, 0x97, 0x0b, 0xab, 0x94, 0x3a, 0x12);
        public ThreadPoolTraceEventParser(TraceEventSource source) : base(source) { }

        public event Action<TPCBEnqueueTraceData> ThreadPoolTraceCBEnqueue
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new TPCBEnqueueTraceData(value, 0xFFFF, 0, "ThreadPoolTrace", ThreadPoolTraceTaskGuid, 32, "CBEnqueue", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 32, ThreadPoolTraceTaskGuid);
            }
        }
        public event Action<TPCBEnqueueTraceData> ThreadPoolTraceCBStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new TPCBEnqueueTraceData(value, 0xFFFF, 0, "ThreadPoolTrace", ThreadPoolTraceTaskGuid, 34, "CBStart", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 34, ThreadPoolTraceTaskGuid);
            }
        }
        public event Action<TPCBDequeueTraceData> ThreadPoolTraceCBDequeue
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new TPCBDequeueTraceData(value, 0xFFFF, 0, "ThreadPoolTrace", ThreadPoolTraceTaskGuid, 33, "CBDequeue", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 33, ThreadPoolTraceTaskGuid);
            }
        }
        public event Action<TPCBDequeueTraceData> ThreadPoolTraceCBStop
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new TPCBDequeueTraceData(value, 0xFFFF, 0, "ThreadPoolTrace", ThreadPoolTraceTaskGuid, 35, "CBStop", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 35, ThreadPoolTraceTaskGuid);
            }
        }
        public event Action<TPCBCancelTraceData> ThreadPoolTraceCBCancel
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new TPCBCancelTraceData(value, 0xFFFF, 0, "ThreadPoolTrace", ThreadPoolTraceTaskGuid, 36, "CBCancel", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 36, ThreadPoolTraceTaskGuid);
            }
        }
        public event Action<TPPoolCreateCloseTraceData> ThreadPoolTracePoolCreate
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new TPPoolCreateCloseTraceData(value, 0xFFFF, 0, "ThreadPoolTrace", ThreadPoolTraceTaskGuid, 37, "PoolCreate", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 37, ThreadPoolTraceTaskGuid);
            }
        }
        public event Action<TPPoolCreateCloseTraceData> ThreadPoolTracePoolClose
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new TPPoolCreateCloseTraceData(value, 0xFFFF, 0, "ThreadPoolTrace", ThreadPoolTraceTaskGuid, 38, "PoolClose", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 38, ThreadPoolTraceTaskGuid);
            }
        }
        public event Action<TPThreadSetTraceData> ThreadPoolTraceThreadMinSet
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new TPThreadSetTraceData(value, 0xFFFF, 0, "ThreadPoolTrace", ThreadPoolTraceTaskGuid, 39, "ThreadMinSet", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 39, ThreadPoolTraceTaskGuid);
            }
        }
        public event Action<TPThreadSetTraceData> ThreadPoolTraceThreadMaxSet
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new TPThreadSetTraceData(value, 0xFFFF, 0, "ThreadPoolTrace", ThreadPoolTraceTaskGuid, 40, "ThreadMaxSet", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 40, ThreadPoolTraceTaskGuid);
            }
        }

        #region private
        protected override string GetProviderName() { return ProviderName; }
        static private volatile TraceEvent[] s_templates;
        protected internal override void EnumerateTemplates(Func<string, string, EventFilterResponse> eventsToObserve, Action<TraceEvent> callback)
        {
            if (s_templates == null)
            {
                var templates = new TraceEvent[9];
                templates[0] = new TPCBEnqueueTraceData(null, 0xFFFF, 0, "ThreadPoolTrace", ThreadPoolTraceTaskGuid, 32, "CBEnqueue", ProviderGuid, ProviderName, null);
                templates[1] = new TPCBEnqueueTraceData(null, 0xFFFF, 0, "ThreadPoolTrace", ThreadPoolTraceTaskGuid, 34, "CBStart", ProviderGuid, ProviderName, null);
                templates[2] = new TPCBDequeueTraceData(null, 0xFFFF, 0, "ThreadPoolTrace", ThreadPoolTraceTaskGuid, 33, "CBDequeue", ProviderGuid, ProviderName, null);
                templates[3] = new TPCBDequeueTraceData(null, 0xFFFF, 0, "ThreadPoolTrace", ThreadPoolTraceTaskGuid, 35, "CBStop", ProviderGuid, ProviderName, null);
                templates[4] = new TPCBCancelTraceData(null, 0xFFFF, 0, "ThreadPoolTrace", ThreadPoolTraceTaskGuid, 36, "CBCancel", ProviderGuid, ProviderName, null);
                templates[5] = new TPPoolCreateCloseTraceData(null, 0xFFFF, 0, "ThreadPoolTrace", ThreadPoolTraceTaskGuid, 37, "PoolCreate", ProviderGuid, ProviderName, null);
                templates[6] = new TPPoolCreateCloseTraceData(null, 0xFFFF, 0, "ThreadPoolTrace", ThreadPoolTraceTaskGuid, 38, "PoolClose", ProviderGuid, ProviderName, null);
                templates[7] = new TPThreadSetTraceData(null, 0xFFFF, 0, "ThreadPoolTrace", ThreadPoolTraceTaskGuid, 39, "ThreadMinSet", ProviderGuid, ProviderName, null);
                templates[8] = new TPThreadSetTraceData(null, 0xFFFF, 0, "ThreadPoolTrace", ThreadPoolTraceTaskGuid, 40, "ThreadMaxSet", ProviderGuid, ProviderName, null);
                s_templates = templates;
            }
            foreach (var template in s_templates)
                if (eventsToObserve == null || eventsToObserve(template.ProviderName, template.EventName) == EventFilterResponse.AcceptEvent)
                    callback(template);
        }

        ThreadPoolTraceEventParserState State
        {
            get
            {
                ThreadPoolTraceEventParserState ret = (ThreadPoolTraceEventParserState)StateObject;
                if (ret == null)
                {
                    ret = new ThreadPoolTraceEventParserState();
                    StateObject = ret;
                }
                return ret;
            }
        }
        private static readonly Guid ThreadPoolTraceTaskGuid = new Guid(unchecked((int)0xc861d0e2), unchecked((short)0xa2c1), unchecked((short)0x4d36), 0x9f, 0x9c, 0x97, 0x0b, 0xab, 0x94, 0x3a, 0x12);
        #endregion
    }
    #region private types
    internal class ThreadPoolTraceEventParserState : IFastSerializable
    {
        //TODO: Fill in
        void IFastSerializable.ToStream(Serializer serializer)
        {
        }
        void IFastSerializable.FromStream(Deserializer deserializer)
        {
        }
    }
    #endregion

    public sealed class TPCBEnqueueTraceData : TraceEvent
    {
        public Address PoolID { get { return GetAddressAt(0); } }
        public Address TaskID { get { return GetAddressAt(HostOffset(4, 1)); } }
        public Address CallbackFunction { get { return GetAddressAt(HostOffset(8, 2)); } }
        public Address CallbackContext { get { return GetAddressAt(HostOffset(12, 3)); } }
        public Address SubProcessTag { get { return GetAddressAt(HostOffset(16, 4)); } }

        #region Private
        internal TPCBEnqueueTraceData(Action<TPCBEnqueueTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, ThreadPoolTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<TPCBEnqueueTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(20, 5)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(20, 5)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "PoolID", PoolID);
            XmlAttribHex(sb, "TaskID", TaskID);
            XmlAttribHex(sb, "CallbackFunction", CallbackFunction);
            XmlAttribHex(sb, "CallbackContext", CallbackContext);
            XmlAttribHex(sb, "SubProcessTag", SubProcessTag);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "PoolID", "TaskID", "CallbackFunction", "CallbackContext", "SubProcessTag" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return PoolID;
                case 1:
                    return TaskID;
                case 2:
                    return CallbackFunction;
                case 3:
                    return CallbackContext;
                case 4:
                    return SubProcessTag;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<TPCBEnqueueTraceData> Action;
        private ThreadPoolTraceEventParserState state;
        #endregion
    }
    public sealed class TPCBDequeueTraceData : TraceEvent
    {
        public Address TaskID { get { return GetAddressAt(0); } }

        #region Private
        internal TPCBDequeueTraceData(Action<TPCBDequeueTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, ThreadPoolTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<TPCBDequeueTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(4, 1)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(4, 1)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "TaskID", TaskID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "TaskID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return TaskID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<TPCBDequeueTraceData> Action;
        private ThreadPoolTraceEventParserState state;
        #endregion
    }
    public sealed class TPCBCancelTraceData : TraceEvent
    {
        public Address TaskID { get { return GetAddressAt(0); } }
        public int CancelCount { get { return GetInt32At(HostOffset(4, 1)); } }

        #region Private
        internal TPCBCancelTraceData(Action<TPCBCancelTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, ThreadPoolTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<TPCBCancelTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(8, 1)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(8, 1)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "TaskID", TaskID);
            XmlAttrib(sb, "CancelCount", CancelCount);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "TaskID", "CancelCount" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return TaskID;
                case 1:
                    return CancelCount;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<TPCBCancelTraceData> Action;
        private ThreadPoolTraceEventParserState state;
        #endregion
    }
    public sealed class TPPoolCreateCloseTraceData : TraceEvent
    {
        public Address PoolID { get { return GetAddressAt(0); } }

        #region Private
        internal TPPoolCreateCloseTraceData(Action<TPPoolCreateCloseTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, ThreadPoolTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<TPPoolCreateCloseTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(4, 1)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(4, 1)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "PoolID", PoolID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "PoolID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return PoolID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<TPPoolCreateCloseTraceData> Action;
        private ThreadPoolTraceEventParserState state;
        #endregion
    }
    public sealed class TPThreadSetTraceData : TraceEvent
    {
        public Address PoolID { get { return GetAddressAt(0); } }
        public int ThreadNum { get { return GetInt32At(HostOffset(4, 1)); } }

        #region Private
        internal TPThreadSetTraceData(Action<TPThreadSetTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, ThreadPoolTraceEventParserState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<TPThreadSetTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(8, 1)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(8, 1)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "PoolID", PoolID);
            XmlAttrib(sb, "ThreadNum", ThreadNum);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "PoolID", "ThreadNum" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return PoolID;
                case 1:
                    return ThreadNum;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<TPThreadSetTraceData> Action;
        private ThreadPoolTraceEventParserState state;
        #endregion
    }

    // [SecuritySafeCritical]
    [System.CodeDom.Compiler.GeneratedCode("traceparsergen", "1.0")]
    public sealed class HeapTraceProviderTraceEventParser : TraceEventParser
    {
        public static readonly string ProviderName = "HeapTraceProvider";
        public static readonly Guid ProviderGuid = new Guid(unchecked((int)0x222962ab), unchecked((short)0x6180), unchecked((short)0x4b88), 0xa8, 0x25, 0x34, 0x6b, 0x75, 0xf2, 0xa2, 0x4a);

        /* This only turns on the heap ranges (much lighter) d781ca11-61c0-4387-b83d-af52d3d2dd6a */
        public static readonly Guid HeapRangeProviderGuid = new Guid(unchecked((int)0xd781ca11), unchecked((short)0x61c0), unchecked((short)0x4387), 0xb8, 0x3d, 0xaf, 0x52, 0xd3, 0xd2, 0xdd, 0x6a);

        public HeapTraceProviderTraceEventParser(TraceEventSource source) : base(source) { }

        public event Action<HeapCreateTraceData> HeapTraceCreate
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new HeapCreateTraceData(value, 0xFFFF, 0, "HeapTrace", HeapTraceTaskGuid, 32, "Create", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 32, HeapTraceTaskGuid);
            }
        }
        public event Action<HeapAllocTraceData> HeapTraceAlloc
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new HeapAllocTraceData(value, 0xFFFF, 0, "HeapTrace", HeapTraceTaskGuid, 33, "Alloc", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 33, HeapTraceTaskGuid);
            }
        }
        public event Action<HeapReallocTraceData> HeapTraceReAlloc
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new HeapReallocTraceData(value, 0xFFFF, 0, "HeapTrace", HeapTraceTaskGuid, 34, "ReAlloc", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 34, HeapTraceTaskGuid);
            }
        }
        public event Action<HeapFreeTraceData> HeapTraceFree
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new HeapFreeTraceData(value, 0xFFFF, 0, "HeapTrace", HeapTraceTaskGuid, 36, "Free", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 36, HeapTraceTaskGuid);
            }
        }
        public event Action<HeapExpandTraceData> HeapTraceExpand
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new HeapExpandTraceData(value, 0xFFFF, 0, "HeapTrace", HeapTraceTaskGuid, 37, "Expand", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 37, HeapTraceTaskGuid);
            }
        }
        public event Action<HeapSnapShotTraceData> HeapTraceSnapShot
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new HeapSnapShotTraceData(value, 0xFFFF, 0, "HeapTrace", HeapTraceTaskGuid, 38, "SnapShot", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 38, HeapTraceTaskGuid);
            }
        }
        public event Action<HeapContractTraceData> HeapTraceContract
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new HeapContractTraceData(value, 0xFFFF, 0, "HeapTrace", HeapTraceTaskGuid, 42, "Contract", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 42, HeapTraceTaskGuid);
            }
        }
        public event Action<HeapTraceData> HeapTraceDestroy
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new HeapTraceData(value, 0xFFFF, 0, "HeapTrace", HeapTraceTaskGuid, 35, "Destroy", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 35, HeapTraceTaskGuid);
            }
        }
        public event Action<HeapTraceData> HeapTraceLock
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new HeapTraceData(value, 0xFFFF, 0, "HeapTrace", HeapTraceTaskGuid, 43, "Lock", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 43, HeapTraceTaskGuid);
            }
        }
        public event Action<HeapTraceData> HeapTraceUnlock
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new HeapTraceData(value, 0xFFFF, 0, "HeapTrace", HeapTraceTaskGuid, 44, "Unlock", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 44, HeapTraceTaskGuid);
            }
        }
        public event Action<HeapTraceData> HeapTraceValidate
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new HeapTraceData(value, 0xFFFF, 0, "HeapTrace", HeapTraceTaskGuid, 45, "Validate", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 45, HeapTraceTaskGuid);
            }
        }
        public event Action<HeapTraceData> HeapTraceWalk
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new HeapTraceData(value, 0xFFFF, 0, "HeapTrace", HeapTraceTaskGuid, 46, "Walk", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 46, HeapTraceTaskGuid);
            }
        }

        #region private
        protected override string GetProviderName() { return ProviderName; }
        static private volatile TraceEvent[] s_templates;
        protected internal override void EnumerateTemplates(Func<string, string, EventFilterResponse> eventsToObserve, Action<TraceEvent> callback)
        {
            if (s_templates == null)
            {
                var templates = new TraceEvent[12];
                templates[0] = new HeapCreateTraceData(null, 0xFFFF, 0, "HeapTrace", HeapTraceTaskGuid, 32, "Create", ProviderGuid, ProviderName, null);
                templates[1] = new HeapAllocTraceData(null, 0xFFFF, 0, "HeapTrace", HeapTraceTaskGuid, 33, "Alloc", ProviderGuid, ProviderName, null);
                templates[2] = new HeapReallocTraceData(null, 0xFFFF, 0, "HeapTrace", HeapTraceTaskGuid, 34, "ReAlloc", ProviderGuid, ProviderName, null);
                templates[3] = new HeapFreeTraceData(null, 0xFFFF, 0, "HeapTrace", HeapTraceTaskGuid, 36, "Free", ProviderGuid, ProviderName, null);
                templates[4] = new HeapExpandTraceData(null, 0xFFFF, 0, "HeapTrace", HeapTraceTaskGuid, 37, "Expand", ProviderGuid, ProviderName, null);
                templates[5] = new HeapSnapShotTraceData(null, 0xFFFF, 0, "HeapTrace", HeapTraceTaskGuid, 38, "SnapShot", ProviderGuid, ProviderName, null);
                templates[6] = new HeapContractTraceData(null, 0xFFFF, 0, "HeapTrace", HeapTraceTaskGuid, 42, "Contract", ProviderGuid, ProviderName, null);
                templates[7] = new HeapTraceData(null, 0xFFFF, 0, "HeapTrace", HeapTraceTaskGuid, 35, "Destroy", ProviderGuid, ProviderName, null);
                templates[8] = new HeapTraceData(null, 0xFFFF, 0, "HeapTrace", HeapTraceTaskGuid, 43, "Lock", ProviderGuid, ProviderName, null);
                templates[9] = new HeapTraceData(null, 0xFFFF, 0, "HeapTrace", HeapTraceTaskGuid, 44, "Unlock", ProviderGuid, ProviderName, null);
                templates[10] = new HeapTraceData(null, 0xFFFF, 0, "HeapTrace", HeapTraceTaskGuid, 45, "Validate", ProviderGuid, ProviderName, null);
                templates[11] = new HeapTraceData(null, 0xFFFF, 0, "HeapTrace", HeapTraceTaskGuid, 46, "Walk", ProviderGuid, ProviderName, null);
                s_templates = templates;
            }
            foreach (var template in s_templates)
                if (eventsToObserve == null || eventsToObserve(template.ProviderName, template.EventName) == EventFilterResponse.AcceptEvent)
                    callback(template);
        }

        HeapTraceProviderState State
        {
            get
            {
                HeapTraceProviderState ret = (HeapTraceProviderState)StateObject;
                if (ret == null)
                {
                    ret = new HeapTraceProviderState();
                    StateObject = ret;
                }
                return ret;
            }
        }
        private static readonly Guid HeapTraceTaskGuid = new Guid(unchecked((int)0x222962ab), unchecked((short)0x6180), unchecked((short)0x4b88), 0xa8, 0x25, 0x34, 0x6b, 0x75, 0xf2, 0xa2, 0x4a);
        #endregion
    }
    #region private types
    internal class HeapTraceProviderState : IFastSerializable
    {
        //TODO: Fill in
        void IFastSerializable.ToStream(Serializer serializer)
        {
        }
        void IFastSerializable.FromStream(Deserializer deserializer)
        {
        }
    }
    #endregion

    public sealed class HeapCreateTraceData : TraceEvent
    {
        public Address HeapHandle { get { return GetAddressAt(0); } }
        public int HeapFlags { get { return GetInt32At(HostOffset(4, 1)); } }

        #region Private
        internal HeapCreateTraceData(Action<HeapCreateTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, HeapTraceProviderState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<HeapCreateTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(8, 1)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(8, 1)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "HeapHandle", HeapHandle);
            XmlAttrib(sb, "HeapFlags", HeapFlags);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "HeapHandle", "HeapFlags" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return HeapHandle;
                case 1:
                    return HeapFlags;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<HeapCreateTraceData> Action;
        private HeapTraceProviderState state;
        #endregion
    }

    // TODO make an enum for SourceID these are the values. 
    // #define MEMORY_FROM_LOOKASIDE                   1       //Activity from LookAside  
    // #define MEMORY_FROM_LOWFRAG                     2       //Activity from Low Frag Heap  
    // #define MEMORY_FROM_MAINPATH                    3       //Activity from Main Code Path  
    // #define MEMORY_FROM_SLOWPATH                    4       //Activity from Slow C  
    // #define MEMORY_FROM_INVALID                     5  

    public sealed class HeapAllocTraceData : TraceEvent
    {
        public Address HeapHandle { get { return GetAddressAt(0); } }
        public long AllocSize { get { return (long)GetAddressAt(HostOffset(4, 1)); } }
        public Address AllocAddress { get { return GetAddressAt(HostOffset(8, 2)); } }
        public int SourceID { get { return GetInt32At(HostOffset(12, 3)); } }

        #region Private
        internal HeapAllocTraceData(Action<HeapAllocTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, HeapTraceProviderState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<HeapAllocTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(16, 3)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(16, 3)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "HeapHandle", HeapHandle);
            XmlAttribHex(sb, "AllocSize", AllocSize);
            XmlAttribHex(sb, "AllocAddress", AllocAddress);
            XmlAttrib(sb, "SourceID", SourceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "HeapHandle", "AllocSize", "AllocAddress", "SourceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return HeapHandle;
                case 1:
                    return AllocSize;
                case 2:
                    return AllocAddress;
                case 3:
                    return SourceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<HeapAllocTraceData> Action;
        private HeapTraceProviderState state;
        #endregion
    }
    public sealed class HeapReallocTraceData : TraceEvent
    {
        public Address HeapHandle { get { return GetAddressAt(0); } }
        public Address NewAllocAddress { get { return GetAddressAt(HostOffset(4, 1)); } }
        public Address OldAllocAddress { get { return GetAddressAt(HostOffset(8, 2)); } }
        public long NewAllocSize { get { return (long)GetAddressAt(HostOffset(12, 3)); } }
        public long OldAllocSize { get { return (long)GetAddressAt(HostOffset(16, 4)); } }
        public int SourceID { get { return GetInt32At(HostOffset(20, 5)); } }

        #region Private
        internal HeapReallocTraceData(Action<HeapReallocTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, HeapTraceProviderState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<HeapReallocTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(24, 5)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(24, 5)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "HeapHandle", HeapHandle);
            XmlAttribHex(sb, "NewAllocAddress", NewAllocAddress);
            XmlAttribHex(sb, "OldAllocAddress", OldAllocAddress);
            XmlAttribHex(sb, "NewAllocSize", NewAllocSize);
            XmlAttribHex(sb, "OldAllocSize", OldAllocSize);
            XmlAttrib(sb, "SourceID", SourceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "HeapHandle", "NewAllocAddress", "OldAllocAddress", "NewAllocSize", "OldAllocSize", "SourceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return HeapHandle;
                case 1:
                    return NewAllocAddress;
                case 2:
                    return OldAllocAddress;
                case 3:
                    return NewAllocSize;
                case 4:
                    return OldAllocSize;
                case 5:
                    return SourceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<HeapReallocTraceData> Action;
        private HeapTraceProviderState state;
        #endregion
    }
    public sealed class HeapFreeTraceData : TraceEvent
    {
        public Address HeapHandle { get { return GetAddressAt(0); } }
        public Address FreeAddress { get { return GetAddressAt(HostOffset(4, 1)); } }
        public int SourceID { get { return GetInt32At(HostOffset(8, 2)); } }

        #region Private
        internal HeapFreeTraceData(Action<HeapFreeTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, HeapTraceProviderState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<HeapFreeTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(12, 2)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(12, 2)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "HeapHandle", HeapHandle);
            XmlAttribHex(sb, "FreeAddress", FreeAddress);
            XmlAttrib(sb, "SourceID", SourceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "HeapHandle", "FreeAddress", "SourceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return HeapHandle;
                case 1:
                    return FreeAddress;
                case 2:
                    return SourceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<HeapFreeTraceData> Action;
        private HeapTraceProviderState state;
        #endregion
    }
    public sealed class HeapExpandTraceData : TraceEvent
    {
        public Address HeapHandle { get { return GetAddressAt(0); } }
        public Address CommittedSize { get { return GetAddressAt(HostOffset(4, 1)); } }
        public Address CommitAddress { get { return GetAddressAt(HostOffset(8, 2)); } }
        public Address FreeSpace { get { return GetAddressAt(HostOffset(12, 3)); } }
        public Address CommittedSpace { get { return GetAddressAt(HostOffset(16, 4)); } }
        public Address ReservedSpace { get { return GetAddressAt(HostOffset(20, 5)); } }
        public int NoOfUCRs { get { return GetInt32At(HostOffset(24, 6)); } }

        #region Private
        internal HeapExpandTraceData(Action<HeapExpandTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, HeapTraceProviderState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<HeapExpandTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(28, 6)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(28, 6)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "HeapHandle", HeapHandle);
            XmlAttribHex(sb, "CommittedSize", CommittedSize);
            XmlAttribHex(sb, "CommitAddress", CommitAddress);
            XmlAttribHex(sb, "FreeSpace", FreeSpace);
            XmlAttribHex(sb, "CommittedSpace", CommittedSpace);
            XmlAttribHex(sb, "ReservedSpace", ReservedSpace);
            XmlAttrib(sb, "NoOfUCRs", NoOfUCRs);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "HeapHandle", "CommittedSize", "CommitAddress", "FreeSpace", "CommittedSpace", "ReservedSpace", "NoOfUCRs" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return HeapHandle;
                case 1:
                    return CommittedSize;
                case 2:
                    return CommitAddress;
                case 3:
                    return FreeSpace;
                case 4:
                    return CommittedSpace;
                case 5:
                    return ReservedSpace;
                case 6:
                    return NoOfUCRs;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<HeapExpandTraceData> Action;
        private HeapTraceProviderState state;
        #endregion
    }
    public sealed class HeapSnapShotTraceData : TraceEvent
    {
        public Address HeapHandle { get { return GetAddressAt(0); } }
        public Address FreeSpace { get { return GetAddressAt(HostOffset(4, 1)); } }
        public Address CommittedSpace { get { return GetAddressAt(HostOffset(8, 2)); } }
        public Address ReservedSpace { get { return GetAddressAt(HostOffset(12, 3)); } }
        public int HeapFlags { get { return GetInt32At(HostOffset(16, 4)); } }

        #region Private
        internal HeapSnapShotTraceData(Action<HeapSnapShotTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, HeapTraceProviderState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<HeapSnapShotTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(20, 4)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(20, 4)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "HeapHandle", HeapHandle);
            XmlAttribHex(sb, "FreeSpace", FreeSpace);
            XmlAttribHex(sb, "CommittedSpace", CommittedSpace);
            XmlAttribHex(sb, "ReservedSpace", ReservedSpace);
            XmlAttrib(sb, "HeapFlags", HeapFlags);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "HeapHandle", "FreeSpace", "CommittedSpace", "ReservedSpace", "HeapFlags" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return HeapHandle;
                case 1:
                    return FreeSpace;
                case 2:
                    return CommittedSpace;
                case 3:
                    return ReservedSpace;
                case 4:
                    return HeapFlags;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<HeapSnapShotTraceData> Action;
        private HeapTraceProviderState state;
        #endregion
    }
    public sealed class HeapContractTraceData : TraceEvent
    {
        public Address HeapHandle { get { return GetAddressAt(0); } }
        public Address DeCommittedSize { get { return GetAddressAt(HostOffset(4, 1)); } }
        public Address DeCommitAddress { get { return GetAddressAt(HostOffset(8, 2)); } }
        public Address FreeSpace { get { return GetAddressAt(HostOffset(12, 3)); } }
        public Address CommittedSpace { get { return GetAddressAt(HostOffset(16, 4)); } }
        public Address ReservedSpace { get { return GetAddressAt(HostOffset(20, 5)); } }
        public int NoOfUCRs { get { return GetInt32At(HostOffset(24, 6)); } }

        #region Private
        internal HeapContractTraceData(Action<HeapContractTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, HeapTraceProviderState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<HeapContractTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(28, 6)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(28, 6)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "HeapHandle", HeapHandle);
            XmlAttribHex(sb, "DeCommittedSize", DeCommittedSize);
            XmlAttribHex(sb, "DeCommitAddress", DeCommitAddress);
            XmlAttribHex(sb, "FreeSpace", FreeSpace);
            XmlAttribHex(sb, "CommittedSpace", CommittedSpace);
            XmlAttribHex(sb, "ReservedSpace", ReservedSpace);
            XmlAttrib(sb, "NoOfUCRs", NoOfUCRs);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "HeapHandle", "DeCommittedSize", "DeCommitAddress", "FreeSpace", "CommittedSpace", "ReservedSpace", "NoOfUCRs" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return HeapHandle;
                case 1:
                    return DeCommittedSize;
                case 2:
                    return DeCommitAddress;
                case 3:
                    return FreeSpace;
                case 4:
                    return CommittedSpace;
                case 5:
                    return ReservedSpace;
                case 6:
                    return NoOfUCRs;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<HeapContractTraceData> Action;
        private HeapTraceProviderState state;
        #endregion
    }
    public sealed class HeapTraceData : TraceEvent
    {
        public Address HeapHandle { get { return GetAddressAt(0); } }

        #region Private
        internal HeapTraceData(Action<HeapTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, HeapTraceProviderState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<HeapTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(4, 1)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(4, 1)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "HeapHandle", HeapHandle);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "HeapHandle" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return HeapHandle;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<HeapTraceData> Action;
        private HeapTraceProviderState state;
        #endregion
    }

    // [SecuritySafeCritical]
    [System.CodeDom.Compiler.GeneratedCode("traceparsergen", "1.0")]
    public sealed class CritSecTraceProviderTraceEventParser : TraceEventParser
    {
        public static readonly string ProviderName = "CritSecTraceProvider";
        public static readonly Guid ProviderGuid = new Guid(unchecked((int)0x3ac66736), unchecked((short)0xcc59), unchecked((short)0x4cff), 0x81, 0x15, 0x8d, 0xf5, 0x0e, 0x39, 0x81, 0x6b);
        public CritSecTraceProviderTraceEventParser(TraceEventSource source) : base(source) { }

        public event Action<CritSecCollisionTraceData> CritSecTraceCollision
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new CritSecCollisionTraceData(value, 0xFFFF, 0, "CritSecTrace", CritSecTraceTaskGuid, 34, "Collision", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 34, CritSecTraceTaskGuid);
            }
        }
        public event Action<CritSecInitTraceData> CritSecTraceInitialize
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new CritSecInitTraceData(value, 0xFFFF, 0, "CritSecTrace", CritSecTraceTaskGuid, 35, "Initialize", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 35, CritSecTraceTaskGuid);
            }
        }

        #region private
        protected override string GetProviderName() { return ProviderName; }
        static private volatile TraceEvent[] s_templates;
        protected internal override void EnumerateTemplates(Func<string, string, EventFilterResponse> eventsToObserve, Action<TraceEvent> callback)
        {
            if (s_templates == null)
            {
                var templates = new TraceEvent[2];
                templates[0] = new CritSecCollisionTraceData(null, 0xFFFF, 0, "CritSecTrace", CritSecTraceTaskGuid, 34, "Collision", ProviderGuid, ProviderName, null);
                templates[1] = new CritSecInitTraceData(null, 0xFFFF, 0, "CritSecTrace", CritSecTraceTaskGuid, 35, "Initialize", ProviderGuid, ProviderName, null);
                s_templates = templates;
            }
            foreach (var template in s_templates)
                if (eventsToObserve == null || eventsToObserve(template.ProviderName, template.EventName) == EventFilterResponse.AcceptEvent)
                    callback(template);
        }

        CritSecTraceProviderState State
        {
            get
            {
                CritSecTraceProviderState ret = (CritSecTraceProviderState)StateObject;
                if (ret == null)
                {
                    ret = new CritSecTraceProviderState();
                    StateObject = ret;
                }
                return ret;
            }
        }
        private static readonly Guid CritSecTraceTaskGuid = new Guid(unchecked((int)0x3ac66736), unchecked((short)0xcc59), unchecked((short)0x4cff), 0x81, 0x15, 0x8d, 0xf5, 0x0e, 0x39, 0x81, 0x6b);
        #endregion
    }
    #region private types
    internal class CritSecTraceProviderState : IFastSerializable
    {
        //TODO: Fill in
        void IFastSerializable.ToStream(Serializer serializer)
        {
        }
        void IFastSerializable.FromStream(Deserializer deserializer)
        {
        }
    }
    #endregion

    public sealed class CritSecCollisionTraceData : TraceEvent
    {
        public int LockCount { get { return GetInt32At(0); } }
        public int SpinCount { get { return GetInt32At(4); } }
        public Address OwningThread { get { return GetAddressAt(8); } }
        public Address CritSecAddr { get { return GetAddressAt(HostOffset(12, 1)); } }

        #region Private
        internal CritSecCollisionTraceData(Action<CritSecCollisionTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, CritSecTraceProviderState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<CritSecCollisionTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(16, 2)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(16, 2)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "LockCount", LockCount);
            XmlAttrib(sb, "SpinCount", SpinCount);
            XmlAttribHex(sb, "OwningThread", OwningThread);
            XmlAttribHex(sb, "CritSecAddr", CritSecAddr);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "LockCount", "SpinCount", "OwningThread", "CritSecAddr" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return LockCount;
                case 1:
                    return SpinCount;
                case 2:
                    return OwningThread;
                case 3:
                    return CritSecAddr;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<CritSecCollisionTraceData> Action;
        private CritSecTraceProviderState state;
        #endregion
    }
    public sealed class CritSecInitTraceData : TraceEvent
    {
        public Address SpinCount { get { return GetAddressAt(0); } }
        public Address CritSecAddr { get { return GetAddressAt(HostOffset(4, 1)); } }

        #region Private
        internal CritSecInitTraceData(Action<CritSecInitTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, CritSecTraceProviderState state)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<CritSecInitTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(8, 2)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(8, 2)));
            Action(this);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "SpinCount", SpinCount);
            XmlAttribHex(sb, "CritSecAddr", CritSecAddr);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "SpinCount", "CritSecAddr" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return SpinCount;
                case 1:
                    return CritSecAddr;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<CritSecInitTraceData> Action;
        private CritSecTraceProviderState state;
        #endregion
    }

    public sealed class BuildInfoTraceData : TraceEvent
    {
        public DateTime InstallDate { get { return DateTime.FromFileTime(GetInt64At(0)); } }
        public string BuildLab { get { return GetUnicodeStringAt(8); } }
        public string ProductName { get { return GetUnicodeStringAt(SkipUnicodeString(8)); } }
        #region Private
        internal BuildInfoTraceData(Action<BuildInfoTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opCode, string opCodeName, Guid providerGuid, string providerName) :
            base(eventID, task, taskName, taskGuid, opCode, opCodeName, providerGuid, providerName)
        {
            Action = action;
        }

        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<BuildInfoTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override void Validate()
        {
            Debug.Assert(EventDataLength == SkipUnicodeString(8, 2));
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "InstallDate", "BuildLab", "ProductName" };
                }
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return InstallDate;
                case 1:
                    return BuildLab;
                case 2:
                    return ProductName;
                default:
                    Debug.Assert(false, "invalid index");
                    return null;
            }
        }

        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "InstallDate", InstallDate);
            XmlAttrib(sb, "BuildLab", BuildLab);
            XmlAttrib(sb, "ProductName", ProductName);
            sb.Append("/>");
            return sb;
        }
        private Action<BuildInfoTraceData> Action;
        #endregion
    }

    public sealed class SystemPathsTraceData : TraceEvent
    {
        /// <summary>
        /// e.g. c:\windows\system32
        /// </summary>
        public string SystemDirectory { get { return GetUnicodeStringAt(0); } }
        /// <summary>
        /// .e.g c:\windows
        /// </summary>
        public string SystemWindowsDirectory { get { return GetUnicodeStringAt(SkipUnicodeString(0)); } }
        #region Private
        internal SystemPathsTraceData(Action<SystemPathsTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opCode, string opCodeName, Guid providerGuid, string providerName) :
            base(eventID, task, taskName, taskGuid, opCode, opCodeName, providerGuid, providerName)
        {
            Action = action;
        }

        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<SystemPathsTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override void Validate()
        {
            Debug.Assert(EventDataLength == SkipUnicodeString(0, 2));
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "SystemDirectory", "SystemWindowsDirectory" };
                }
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return SystemDirectory;
                case 1:
                    return SystemWindowsDirectory;
                default:
                    Debug.Assert(false, "invalid index");
                    return null;
            }
        }

        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "SystemDirectory", SystemDirectory);
            XmlAttrib(sb, "SystemWindowsDirectory", SystemWindowsDirectory);
            sb.Append("/>");
            return sb;
        }
        private Action<SystemPathsTraceData> Action;
        #endregion
    }

    public sealed class VolumeMappingTraceData : TraceEvent
    {
        public string NtPath { get { return GetUnicodeStringAt(0); } }
        public string DosPath { get { return GetUnicodeStringAt(SkipUnicodeString(0)); } }
        #region Private
        internal VolumeMappingTraceData(Action<VolumeMappingTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opCode, string opCodeName, Guid providerGuid, string providerName) :
            base(eventID, task, taskName, taskGuid, opCode, opCodeName, providerGuid, providerName)
        {
            Action = action;
        }

        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<VolumeMappingTraceData>)value; }
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override void Validate()
        {
            Debug.Assert(EventDataLength == SkipUnicodeString(0, 2));
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "NtPath", "DosPath" };
                }
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return NtPath;
                case 1:
                    return DosPath;
                default:
                    Debug.Assert(false, "invalid index");
                    return null;
            }
        }

        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "NtPath", NtPath);
            XmlAttrib(sb, "DosPath", DosPath);
            sb.Append("/>");
            return sb;
        }
        private Action<VolumeMappingTraceData> Action;
        #endregion
    }
}
