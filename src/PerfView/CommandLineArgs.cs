using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using System;
using System.Collections.Generic;
using Utilities;

namespace PerfView
{
    /// <summary>
    /// The code:PerfViewCommandLine class holds the parsed form of all the commandLine line arguments.  It is
    /// initialized by handing it the 'args' array for main, and it has a public field for each named argument
    /// (eg -debug). See code:#CommandLineDefinitions for the code that defines the arguments (and the help
    /// strings associated with them). 
    /// 
    /// See code:CommandLineParser for more on parser itself.   
    /// </summary>
    public class CommandLineArgs
    {
        public CommandLineArgs()
        {
        }
        /// <summary>
        /// Sets CommandLineFailure field if there is a parse failure 
        /// </summary>
        public void ParseArgs(string[] args)
        {
            try
            {
                var parser = new CommandLineParser(args);
                SetupCommandLine(parser);
                if (parser.HelpRequested != null)
                {
                    HelpRequested = true;
                }

                if (CommandAndArgs != null)
                {
                    CommandLine = CommandLineUtilities.FormCommandLineFromArguments(CommandAndArgs, 0);
                }

                parser.CompleteValidation();
            }
            catch (CommandLineParserException e)
            {
                CommandLineFailure = e;
            }
        }
        public static string GetHelpString(int maxLineWidth)
        {
            string ret;
            var commandLineArgs = new CommandLineArgs(maxLineWidth, out ret);
            return ret;
        }
        public bool HelpRequested;
        public bool AcceptEULA;
        public bool TrustPdbs;
        public bool SafeMode;
        public string RestartingToElevelate;    // internal: are we restarting perfView so we can elevate.  The value is the old command

        // The command to execute (determined by the parameter set)
        public Action<CommandLineArgs> DoCommand;

        // options common to multiple commands
        public string DataFile;             // This is the name of the ETL file (not the ZIP file)
        public string LogFile;

        // Memory options
        public string ProcessDumpFile;      // if taking a snapshot from a dump, this is the dump file (dataFile is the output file)
        public bool SaveETL;                // Save the ETL file when dumping the JS heap
        public bool DumpData;               // Dump the heap data as well as the connectivity info
        public bool Freeze;                 // Freeze the process while the dump is taken
        public int MaxDumpCountK = 250;     // Maximum size of the File to generate.   We sample aggressively enough to try to hit this count of objects in the file
        public int MaxNodeCountK;           // Maximum size to even look at in the heap 

#if CROSS_GENERATION_LIVENESS
        // Cross generation liveness options
        public int CGL_PID;
        public int CGL_Generation;
        public ulong CGL_PromotedBytesThreshold;
        public string CGL_PathToOutputFile;
#endif

        // view options
        public string Process;              // A process name to focus on.

        // run Options 
        public string[] CommandAndArgs;     // This is broken up into words
        public string CommandLine;          // This is a one long string.  

        // collect options 
        public int MaxCollectSec;           // collect no more than this number of seconds. 
        public string[] StopOnPerfCounter;  // stop collection on this performance counter trigger. 
        public string[] StartOnPerfCounter; // start collection on this performance counter trigger. 
        public bool StopOnGen2GC;           // Stop on a gen 2 GC 
        public string[] StopOnEtwEvent;
        public string StopOnException;
        public int StopOnGCOverMsec;
        public int StopOnBGCFinalPauseOverMsec; // Stop on a BGC whose final pause is over this many ms
        public float DecayToZeroHours;          //causes 'StopOn*OverMSec' timeouts to decay to zero over this time period
        public int MinSecForTrigger = 3;        // affects StopOnPerfCounter and StartOnPerfCounter
        public string StopOnEventLogMessage;    // stop collection on event logs
        public string StopCommand;              // is executed when a stop is triggered.   
        public int StopOnAppFabricOverMsec;
        public int DelayAfterTriggerSec = 5;    // Number of seconds to wait after a trigger  
        public string[] MonitorPerfCounter;     // logs perf counters to the ETL file.  
        public bool EnableEventsInContainers;        // Enable user-mode events inside of containers.
        public bool EnableSourceContainerTracking;   // Enable inclusion of container id in each event.

        // Start options.
        public bool StackCompression = true;    // Use compresses stacks when collecting traces. 
        public int BufferSizeMB = 256;
        public int CircularMB;
        public bool InMemoryCircularBuffer;         // Uses EVENT_TRACE_BUFFERING_MODE for an in-memory circular buffer
        public KernelTraceEventParser.Keywords KernelEvents = KernelTraceEventParser.Keywords.Default;
        public string[] CpuCounters;        // Specifies any profile sources (CPU counters) to turn on (Win 8 only)
        public ClrTraceEventParser.Keywords ClrEvents = ClrTraceEventParser.Keywords.Default;
        public TraceEventLevel ClrEventLevel = Microsoft.Diagnostics.Tracing.TraceEventLevel.Verbose;    // The verbosity of CLR events
        public TplEtwProviderTraceEventParser.Keywords TplEvents = TplEtwProviderTraceEventParser.Keywords.None;
        public bool ThreadTime;             // Shortcut for /KernelEvents=ThreadTime
        public bool GCOnly;                 // collect only enough for GC analysis
        public bool GCCollectOnly;          // Turn off even the allocation Tick
        public bool DotNetAlloc;            // Turn on .NET Allocation profiling 
        public bool DotNetAllocSampled;     // Turn on .NET Allocation profiling, but only sampled (in a smart way)
        public bool DotNetCalls;            // Turn on logging of every .NET call
        public bool DotNetCallsSampled;     // Sampling of .NET calls.  
        public bool DisableInlining;        // Force inlining to be disabled. (useful for DotNetCalls).  
        public bool JITInlining;            // Turn on logging of successful and failed JIT inlining
        public int OSHeapProcess;           // Turn on OS Heap tracing for the process with the given process ID.
        public string OSHeapExe;            // Turn on OS heap tracing for any process with the given EXE

        public bool NetworkCapture;         // Capture the full packets of every incoming and outgoing  packet
        public bool NetMonCapture;          // Capture a NetMon-only trace as well as a standard ETW trace (implies NetworkCapture)  
        public bool CCWRefCount;            // Capture CCW references count increasing and decreasing
        public bool RuntimeLoading;         // Capture information about runtime loading such as R2R and type load events

        public bool Wpr;                    // Collect like WPR (no zip, puts NGEN pdbs in a .ngenpdbs directory).  

        public string[] Providers;          // Additional providers to turn on.   

        // Stop options.  
        public string FocusProcess;       // The target process for CLR Rundown.  
        public bool NoRundown;
        public bool NoNGenPdbs;
        public bool NoNGenRundown;
        public bool NoClrRundown;
        public bool NoV2Rundown;
        public bool LowPriority;

        public bool? Merge;
        public bool? Zip;
        public bool ShouldMerge
        {
            get
            {
                // We must merge if we Zip. 
                if (ShouldZip)
                {
                    return true;
                }
                // User asks for it explicitly. 
                if (Merge.HasValue)
                {
                    return Merge.Value;
                }
                // Otherwise the default is to merge.  
                return true;
            }
        }
        public bool ShouldZip
        {
            get
            {
                // User asks for it explicitly. 
                if (Zip.HasValue)
                {
                    return Zip.Value;
                }
                // If user asks for no-merge explicitly, and Zip was was not asked for, then /Zip:false is assumed 
                if (Merge.HasValue && Merge.Value == false)
                {
                    return false;
                }

                // by default, we don't zip if we were asked to mimic WPR.  
                if (Wpr)
                {
                    return false;
                }

                // But the final default is true. 
                return true;
            }
        }

        public int RundownTimeout = 120;
        public int MinRundownTime;
        public bool NoView;
        public float CpuSampleMSec = 1.0F;
        public bool KeepAllEvents;
        public int MaxEventCount;
        public bool ContinueOnError;
        public double SkipMSec;
        public DateTime StartTime;
        public DateTime EndTime;
        public bool ForceNgenRundown;
        public bool DumpHeap;

        // Collect options
        public bool NoGui;
        public int CollectMultiple;     // Collect several instances (incrementing the file name)

        // Mark options
        public string Message;

        // Merge options.
        public bool ImageIDsOnly;

        // Viewer options
        public bool UnsafePDBMatch;
        public bool ShowUnknownAddresses;
        public bool ShowOptimizationTiers;

        // Parameter to CreateExtensionTemplate
        public string ExtensionName = "Global";

        // If parsing fails, this field is set. 
        public CommandLineParserException CommandLineFailure;
        #region private
        private CommandLineArgs(int maxLineWidth, out string helpString)
            : this()
        {
            var parser = new CommandLineParser("perfView /?");
            SetupCommandLine(parser);
            helpString = parser.GetHelp(maxLineWidth, null);
        }

        /// <summary>
        /// Shared logic to configure CommandLineArgs for GCCollectOnly mode
        /// </summary>
        /// <param name="commandLineArgs"></param>
        internal static void ConfigureForGCCollectOnly(CommandLineArgs commandLineArgs)
        {
            // The process events are so we get process names. The ImageLoad events are so that we get version information about the DLLs.
            commandLineArgs.KernelEvents = KernelTraceEventParser.Keywords.Process | KernelTraceEventParser.Keywords.ImageLoad;
            commandLineArgs.ClrEvents = ClrTraceEventParser.Keywords.GC;
            commandLineArgs.ClrEventLevel = TraceEventLevel.Informational;
            commandLineArgs.TplEvents = TplEtwProviderTraceEventParser.Keywords.None;
            commandLineArgs.NoRundown = true;
        }

        private void SetupCommandLine(CommandLineParser parser)
        {
            // #CommandLineDefinitions
            parser.ParameterSetsWhereQualifiersMustBeFirst = new string[] { "run", "UserCommand" };
            parser.NoDashOnParameterSets = true;

            parser.DefineOptionalQualifier("LogFile", ref LogFile, "Send messages to this file instead launching the GUI.  Intended for batch scripts and other automation.");

            // These apply to start, collect and run
            parser.DefineOptionalQualifier("BufferSize", ref BufferSizeMB, "The size the buffers (in MB) the OS should use to store events waiting to be written to disk."); // TODO remove eventually. 
            parser.DefineOptionalQualifier("Circular", ref CircularMB, "Do Circular logging with a file size in MB.  Zero means non-circular.");  // TODO remove eventually. 
            parser.DefineOptionalQualifier("BufferSizeMB", ref BufferSizeMB, "The size the buffers (in MB) the OS should use to store events waiting to be written to disk.");
            parser.DefineOptionalQualifier("CircularMB", ref CircularMB, "Do Circular logging with a file size in MB.  Zero means non-circular.");
            parser.DefineOptionalQualifier("InMemoryCircularBuffer", ref InMemoryCircularBuffer, "Keeps the circular buffer in memory until the session is stopped.");
            parser.DefineOptionalQualifier("StackCompression", ref StackCompression, "Use stack compression (only on Win 8+) to make collected file smaller.");
            parser.DefineOptionalQualifier("MaxCollectSec", ref MaxCollectSec,
                "Turn off collection (and kill the program if perfView started it) after this many seconds. Zero means no timeout.");
            parser.DefineOptionalQualifier("StopOnPerfCounter", ref StopOnPerfCounter,
                "This is of the form CATEGORY:COUNTERNAME:INSTANCE OP NUM  where CATEGORY:COUNTERNAME:INSTANCE, identify " +
                "a performance counter (same as PerfMon), OP is either < or >, and NUM is a number.  " +
                "When that condition is true then collection will stop.  You can specify this qualifier more than once (logical OR).  See 'Stop Trigger' in the users guide for more.");
            parser.DefineOptionalQualifier("StopOnEventLogMessage", ref StopOnEventLogMessage,
                "Stop when an event log message that matches the given (ignore case) regular expression is written to the Windows 'Application' event log.  " +
                "You can specify a particular event log with the syntax eventLogName@RegExp.   Can be specified more than once (logical OR).");

            parser.DefineOptionalQualifier("StopOnEtwEvent", ref StopOnEtwEvent,
                "This is of the form PROVIDER/EVENTNAME;key1=value1;key2=value2... " +
                "This option is quite powerful, See the users guide for more details.");

            int StopOnRequestOverMsec = 0;
            int StopOnGCSuspendOverMSec = 0;

            // These are basically special cases of the /StopOnEtwEvent
            parser.DefineOptionalQualifier("StopOnRequestOverMsec", ref StopOnRequestOverMsec,
                "Trigger a stop of a collect command if there is any IIS request that is longer than the given number of MSec.");
            parser.DefineOptionalQualifier("StopOnGCOverMsec", ref StopOnGCOverMsec,
                "Trigger a stop of a collect command if there is a .NET Garbage Collection (GC) is longer than the given number of MSec.");
            parser.DefineOptionalQualifier("StopOnGCSuspendOverMSec", ref StopOnGCSuspendOverMSec,
                "Trigger a stop of a collect command if there is a .NET Garbage Collection (GC) where suspending for the GC took over the given number of MSec.");
            parser.DefineOptionalQualifier("StopOnBGCFinalPauseOverMsec", ref StopOnBGCFinalPauseOverMsec,
               "Trigger a stop of a collect command if there is a background .NET Garbage Collection (GC) whose final pause is longer than the given number of MSec. To work correctly, " +
               "this requires that heap survival and movement tracking is not enabled.");
            parser.DefineOptionalQualifier("StopOnAppFabricOverMsec", ref StopOnAppFabricOverMsec,
                "Trigger a stop of a collect command if there is a AppFabric request is longer than the given number of MSec.");

            parser.DefineOptionalQualifier("StopOnException", ref StopOnException,
                "Where the text is a regular expression that will be used to match the full name and message of the .NET Exception thrown." +
                "The empty string represents any exception.");
            parser.DefineOptionalQualifier("StopOnGen2GC", ref StopOnGen2GC,
                "This will stop on any non-background Gen2 GC from the given process (can be a process ID or a process Name (exe file name without path or extension) or * (any process)");

            parser.DefineOptionalQualifier("Process", ref Process, "A process name (exe file name without directory or extension) or the Decimal Process ID.  " +
                "If used with the /StopOn* qualifiers using ETW events, will restrict events to only that process.");
            parser.DefineOptionalQualifier("DecayToZeroHours", ref DecayToZeroHours,
                "The trigger value used in StopOnPerfCounter or StopOn*OverMSec will decay to zero in this interval of time.");
            parser.DefineOptionalQualifier("MinSecForTrigger", ref MinSecForTrigger,
                "The number of seconds a perf Counter has to be above threshold before it is considered triggered.");
            parser.DefineOptionalQualifier("DelayAfterTriggerSec", ref DelayAfterTriggerSec,
                "Wait this number of seconds after a trigger before actually stopping the trace.");
            parser.DefineOptionalQualifier("CollectMultiple", ref CollectMultiple, "Collect Multiple instance (used in conjunction with StopTrigger).");
            parser.DefineOptionalQualifier("StartOnPerfCounter", ref StartOnPerfCounter,
                "This is of the form CATEGORY:COUNTERNAME:INSTANCE OP NUM  where CATEGORY:COUNTERNAME:INSTANCE, identify " +
                "a performance counter (same as PerfMon), OP is either < or >, and NUM is a number.  " +
                "When that condition is true then collection will start.  You can specify this qualifier more than once.  Search for 'MonitorPerfCounter' in the users guide for more.");
            parser.DefineOptionalQualifier("StopCommand", ref StopCommand,
                "If present this command is executed when a PerfView stops.  It is useful to stopping other tracing logic external to PerfView.");

            List<string> etwStopEvents = new List<string>();
            if (StopOnRequestOverMsec != 0)
            {
                etwStopEvents.Add("Microsoft-Windows-IIS/EventID(1);Level=Critical;TriggerMSec=" + StopOnRequestOverMsec);
            }

            if (StopOnGCSuspendOverMSec != 0)
            {
                etwStopEvents.Add("E13C0D23-CCBC-4E12-931B-D9CC2EEE27E4/GC/SuspendEEStart;StopEvent=GC/SuspendEEStop;StartStopID=ThreadID;Keywords=0x1;TriggerMSec=" + StopOnGCSuspendOverMSec);
            }

            if (0 < etwStopEvents.Count)
            {
                if (StopOnEtwEvent != null)
                {
                    etwStopEvents.AddRange(StopOnEtwEvent);
                }

                StopOnEtwEvent = etwStopEvents.ToArray();
            }

            // Respect the /Process and /DecayToZeroHours options by tacking them on the end if they are not already present.  
            if (StopOnEtwEvent != null && (Process != null || DecayToZeroHours != 0))
            {
                etwStopEvents.Clear();
                foreach (var stopEtwEvent in StopOnEtwEvent)
                {
                    var newStopEtwEvent = stopEtwEvent;
                    if (Process != null && !stopEtwEvent.Contains(";Process="))
                    {
                        newStopEtwEvent += ";Process=" + Process;
                    }

                    if (DecayToZeroHours != 0 && !stopEtwEvent.Contains(";DecayToZeroHours="))
                    {
                        newStopEtwEvent += ";DecayToZeroHours=" + DecayToZeroHours;
                    }

                    etwStopEvents.Add(newStopEtwEvent);
                }
                StopOnEtwEvent = etwStopEvents.ToArray();
            }

            parser.DefineOptionalQualifier("MonitorPerfCounter", ref MonitorPerfCounter,
                "This is of the form CATEGORY:COUNTERNAME:INSTANCE@NUM  where CATEGORY:COUNTERNAME:INSTANCE, identify " +
                "a performance counter (same as PerfMon), and NUM is a number representing seconds.  The @NUM part is " +
                 "optional and defaults to 2.   The value of the performance counter is logged to the ETL file as an " +
                 "event ever NUM seconds");
            parser.DefineOptionalQualifier("CpuSampleMSec", ref CpuSampleMSec,
                "The interval (MSec) between CPU samples (.125Msec min).");

            // These apply to Stop Collect and Run 
            parser.DefineOptionalQualifier("Merge", ref Merge, "Do a merge after stopping collection.");
            parser.DefineOptionalQualifier("Zip", ref Zip, "Zip the ETL file (implies /Merge).");
            parser.DefineOptionalQualifier("Wpr", ref Wpr, "Make output mimic WPR (Windows Performance Recorder). Don't ZIP, make a .ngenpdbs directory.  " +
                "This also enables threadTime as well as user mode providers WPR would normally collect by default.   This option can also be used " +
                "On the unzip command.   See 'Working with WPA' in the help for more.");
            parser.DefineOptionalQualifier("LowPriority", ref LowPriority, "Do merging and ZIPing at low priority to minimize impact to system.");
            parser.DefineOptionalQualifier("NoRundown", ref NoRundown, "Don't collect rundown events.  Use only if you know the process of interest has exited.");
            parser.DefineOptionalQualifier("FocusProcess", ref FocusProcess, "Either a decimal process ID or a process name (exe name without path but WITH extension) to focus ETW commands." +
                "All NON-KERNEL providers are only send to this process (and rundown is only done on this process) which can cut overhead significantly in some cases.");

            parser.DefineOptionalQualifier("NoNGenPdbs", ref NoNGenPdbs, "Don't generate NGEN Pdbs");
            parser.DefineOptionalQualifier("NoNGenRundown", ref NoNGenRundown,
                "Don't do rundown of symbolic information in NGEN images (only needed pre V4.5).");
            parser.DefineOptionalQualifier("NoClrRundown", ref NoClrRundown,
                "Don't do rundown .NET (CLR) rundown information )(for symbolic name lookup).");
            parser.DefineOptionalQualifier("RundownTimeout", ref RundownTimeout,
                "Maximum number of seconds to wait for CLR rundown to complete.");
            parser.DefineOptionalQualifier("MinRundownTime", ref MinRundownTime,
                "Minimum number of seconds to wait for CLR rundown to complete.");
            parser.DefineOptionalQualifier("KeepAllEvents", ref KeepAllEvents,
                "A debug option to keep all events, even symbolic rundown events.");
            parser.DefineOptionalQualifier("MaxEventCount", ref MaxEventCount, "Limits the total number of events.  " +
                "Useful for trimming large ETL files. 1M typically yields 300-400 Meg of data considered.");
            parser.DefineOptionalQualifier("SkipMSec", ref SkipMSec, "Skips the first N MSec of the trace.  " +
                "Useful for trimming large ETL files in conjunction with the /MaxEventCount qualifier.");
            parser.DefineOptionalQualifier("StartTime", ref StartTime, "The start date and time used to filter events of the input trace for formats that support this.");
            parser.DefineOptionalQualifier("EndTime", ref EndTime, "The end date and time used to filter events of the input trace for formats that support this.");
            parser.DefineOptionalQualifier("ContinueOnError", ref ContinueOnError, "Processes bad traces as best it can.");

            parser.DefineOptionalQualifier("CpuCounters", ref CpuCounters,
                "A comma separated list of hardware CPU counters specifications NAME:COUNT to turn on.  " +
                "See Users guide for details.  See ListCpuCounters for available sources (Win8 only)");

            parser.DefineOptionalQualifier("Providers", ref Providers,
                "Additional providers.  This is comma separated list of ProviderGuid:Keywords:Level:Stack specs.  " +
                "This qualifier has the same syntax as the Additional Providers TextBox in the collection window.  " +
                " See help on that for more.");

            string[] onlyProviders = null;
            parser.DefineOptionalQualifier("OnlyProviders", ref onlyProviders,
                "Like the Providers qualifier, but also turns off the default Kernel and CLR providers.");
            if (onlyProviders != null)
            {
                // Allow stack traces to work if 'stacks' was specified.  
                bool hasStacks = false;
                bool hasTpl = false;
                foreach (var provider in onlyProviders)
                {
                    if (0 <= provider.IndexOf("@StacksEnabled=true", StringComparison.OrdinalIgnoreCase))
                    {
                        hasStacks = true;
                    }

                    if (0 <= provider.IndexOf("@EventIDStacksToEnable", StringComparison.OrdinalIgnoreCase))
                    {
                        hasStacks = true;
                    }

                    if (provider.StartsWith(".NETTasks", StringComparison.OrdinalIgnoreCase))
                    {
                        hasTpl = true;
                    }
                }

                if (hasStacks)
                {
                    KernelEvents = KernelTraceEventParser.Keywords.Process | KernelTraceEventParser.Keywords.Thread | KernelTraceEventParser.Keywords.ImageLoad;
                    ClrEvents = ClrTraceEventParser.Keywords.Jit | ClrTraceEventParser.Keywords.Loader;
                }
                else
                {
                    KernelEvents = KernelTraceEventParser.Keywords.None;
                    ClrEvents = ClrTraceEventParser.Keywords.None;
                    NoNGenRundown = true;   // We still do normal rundown because EventSource rundown is done there.   
                    NoClrRundown = true;
                }

                if (!hasTpl)
                {
                    // Turn on causality tracking.
                    TplEvents = TplEtwProviderTraceEventParser.Keywords.TasksFlowActivityIds;
                }

                Providers = onlyProviders;
            }
            parser.DefineOptionalQualifier("ThreadTime", ref ThreadTime, "Shortcut for turning on context switch and readyThread events");
            if (ThreadTime)
            {
                KernelEvents = KernelTraceEventParser.Keywords.ThreadTime;
            }

            parser.DefineOptionalQualifier("GCOnly", ref GCOnly, "Turns on JUST GC collections an allocation sampling.");
            if (GCOnly)
            {
                // TODO this logic is cloned.  We need it in only one place.  If you update it do the other location as well
                // For stack parsing.  
                KernelEvents = KernelTraceEventParser.Keywords.Process | KernelTraceEventParser.Keywords.Thread | KernelTraceEventParser.Keywords.ImageLoad | KernelTraceEventParser.Keywords.VirtualAlloc;
                ClrEvents = ClrTraceEventParser.Keywords.GC | ClrTraceEventParser.Keywords.GCHeapSurvivalAndMovement | ClrTraceEventParser.Keywords.Stack |
                            ClrTraceEventParser.Keywords.Jit | ClrTraceEventParser.Keywords.StopEnumeration | ClrTraceEventParser.Keywords.SupressNGen |
                            ClrTraceEventParser.Keywords.Loader | ClrTraceEventParser.Keywords.Exception | ClrTraceEventParser.Keywords.Type | ClrTraceEventParser.Keywords.GCHeapAndTypeNames;
                TplEvents = TplEtwProviderTraceEventParser.Keywords.None;

                // This is not quite correct if you have providers of your own, but this covers the most important case.  
                if (Providers == null)
                {
                    Providers = new string[] { "Microsoft-Windows-Kernel-Memory:0x60" };
                }

                CommandProcessor.s_UserModeSessionName = "PerfViewGCSession";
                DataFile = "PerfViewGCOnly.etl";
            }
            parser.DefineOptionalQualifier("GCCollectOnly", ref GCCollectOnly, "Turns on GC collections (no allocation sampling).");
            if (GCCollectOnly)
            {
                ConfigureForGCCollectOnly(this);
                CommandProcessor.s_UserModeSessionName = "PerfViewGCSession";
                DataFile = "PerfViewGCCollectOnly.etl";
            }

            // WPR option implies a bunch of kernel events.  
            if (Wpr)
            {
                KernelEvents = KernelTraceEventParser.Keywords.ThreadTime |
                    KernelTraceEventParser.Keywords.DeferedProcedureCalls |
                    KernelTraceEventParser.Keywords.Driver |
                    KernelTraceEventParser.Keywords.Interrupt;
            }

            parser.DefineOptionalQualifier("DumpHeap", ref DumpHeap, "Capture a heap snapshot on profile stop");
            parser.DefineOptionalQualifier("ClrEventLevel", ref ClrEventLevel, "The verbosity for CLR events");
            parser.DefineOptionalQualifier("ClrEvents", ref ClrEvents,
                "A comma separated list of .NET CLR events to turn on.  See Users guide for details.");
            parser.DefineOptionalQualifier("KernelEvents", ref KernelEvents,
                "A comma separated list of windows OS kernel events to turn on.  See Users guide for details.");
            parser.DefineOptionalQualifier("TplEvents", ref TplEvents,
                "A comma separated list of Task Parallel Library (TPL) events to turn on.  See Users guide for details.");

            parser.DefineOptionalQualifier("DotNetAlloc", ref DotNetAlloc, "Turns on per-allocation .NET profiling.");
            parser.DefineOptionalQualifier("DotNetAllocSampled", ref DotNetAllocSampled, "Turns on per-allocation .NET profiling, sampling types in a smart way to keep overhead low.");
            parser.DefineOptionalQualifier("DotNetCalls", ref DotNetCalls, "Turns on per-call .NET profiling.");
            parser.DefineOptionalQualifier("DotNetCallsSampled", ref DotNetCallsSampled, "Turns on per-call .NET profiling, sampling types in a smart way to keep overhead low.");
            parser.DefineOptionalQualifier("DisableInlining", ref DisableInlining, "Turns off inlining (but only affects processes that start after trace start.");
            parser.DefineOptionalQualifier("JITInlining", ref JITInlining, "Turns on logging of successful and failed JIT inlining attempts.");
            parser.DefineOptionalQualifier("CCWRefCount", ref CCWRefCount, "Turns on logging of information about .NET Native CCW reference counting.");
            parser.DefineOptionalQualifier("RuntimeLoading", ref RuntimeLoading, "Turn on logging of runtime loading operations.");
            parser.DefineOptionalQualifier("OSHeapProcess", ref OSHeapProcess, "Turn on per-allocation profiling of allocation from the OS heap for the process with the given process ID.");
            parser.DefineOptionalQualifier("OSHeapExe", ref OSHeapExe, "Turn on per-allocation profiling of allocation from the OS heap for the process with the given EXE (only filename WITH extension).");

            parser.DefineOptionalQualifier("NetworkCapture", ref NetworkCapture, "Captures the full data of every network packet entering or leaving the OS.");
            parser.DefineOptionalQualifier("NetMonCapture", ref NetMonCapture, "Create _netmon.etl file that NetMon.exe can read, along with the standard ETL file.   Implies /NetworkCapture.");

            parser.DefineOptionalQualifier("ForceNgenRundown", ref ForceNgenRundown,
                "By default on a V4.0 runtime NGEN rundown is suppressed, because NGEN PDB are a less expensive way of getting symbolic " +
                "information for NGEN images.  This option forces NGEN rundown, so NGEN PDBs are not needed.  This can be useful " +
                "in some scenarios where NGEN PDB are not working properly.");
            parser.DefineOptionalQualifier("NoV2Rundown", ref NoV2Rundown,
                "Don't do rundown for .NET (CLR) V2 processes.");
            parser.DefineOptionalQualifier("TrustPdbs", ref TrustPdbs, "Normally PerfView does not trust PDBs outside the _NT_SYMBOL_PATH and pops a dialog box.  Suppress this.");
            parser.DefineOptionalQualifier("AcceptEULA", ref AcceptEULA, "Accepts the EULA associated with PerfView.");
            parser.DefineOptionalQualifier("DataFile", ref DataFile,
                "FileName of the profile data to generate.");
            parser.DefineOptionalQualifier("NoView", ref NoView,
                "Normally after collecting data the data is viewed.  This suppresses that.");
            parser.DefineOptionalQualifier("UnsafePDBMatch", ref UnsafePDBMatch,
                "Allow the use of PDBs even when the trace does not contain PDB signatures.");
            parser.DefineOptionalQualifier("ShowUnknownAddresses", ref ShowUnknownAddresses,
                "Displays the hexadecimal address rather than ? when the address is unknown.");
            parser.DefineOptionalQualifier("ShowOptimizationTiers", ref ShowOptimizationTiers,
                "Displays the optimization tier of each code version executed for the method.");
            parser.DefineOptionalQualifier("NoGui", ref NoGui,
                "Use the Command line version of the command (like on ARM).  Brings up a console window.  For batch scripts/automation use /LogFile instead (see users guide under 'Scripting' for more).");
            parser.DefineOptionalQualifier("SafeMode", ref SafeMode, "Turn off parallelism and other risky features.");
            parser.DefineOptionalQualifier("RestartingToElevelate", ref RestartingToElevelate, "Internal: indicates that perfView is restarting to get Admin privileges.");

            string sessionName = null;
            parser.DefineOptionalQualifier("SessionName", ref sessionName, "Define the name for the user mode session (kernel session will also be named analogously) Useful for collecting traces when another ETW profiler (including PerfView) is being used.");
            if (sessionName != null)
            {
                if (Environment.OSVersion.Version.Major * 10 + Environment.OSVersion.Version.Minor < 62)
                    throw new ApplicationException("SessionName qualifier only works on Windows 8 and above.");
                CommandProcessor.s_UserModeSessionName = sessionName;
                CommandProcessor.s_KernelessionName = sessionName + "Kernel";
            }

            parser.DefineOptionalQualifier("MaxNodeCountK", ref MaxNodeCountK,
                "The maximum number of objects (in K or thousands) that will even be examined when dumping the heap.  Avoids memory use at collection time.  " +
                 "This is useful if heap dumping causes out of memory exceptions.");

            parser.DefineOptionalQualifier("EnableEventsInContainers", ref EnableEventsInContainers,
                "Enable user mode events inside of containers to flow back to the host for collection.");
            parser.DefineOptionalQualifier("EnableSourceContainerTracking", ref EnableSourceContainerTracking,
                "Emit the container ID as part of the payload of each usermode event emitted inside of a container.");

            /* end of qualifier that apply to more than one parameter set (command) */
            /****************************************************************************************/
            /* Parameter set (command) definitions */
            parser.DefineParameterSet("run", ref DoCommand, App.CommandProcessor.Run,
                "Starts data collection, runs a command and stops.");
            parser.DefineParameter("CommandAndArgs", ref CommandAndArgs,
                "Command to run and arguments (PerfView options must come before run command).");

            parser.DefineParameterSet("collect", ref DoCommand, App.CommandProcessor.Collect,
                "Starts data collection, wait for user input, then stops.");
            parser.DefineOptionalParameter("DataFile", ref DataFile,
                "ETL file containing profile data.");

            parser.DefineParameterSet("start", ref DoCommand, App.CommandProcessor.Start,
                "Starts machine wide profile data collection.");
            parser.DefineOptionalParameter("DataFile", ref DataFile, "ETL file containing profile data.");

            parser.DefineParameterSet("stop", ref DoCommand, App.CommandProcessor.Stop,
                "Stop collecting profile data (machine wide).  If you specified EventSources with the /Providers qualifier on start you should repeat them here to insure manifest rundown.");

            parser.DefineParameterSet("mark", ref DoCommand, App.CommandProcessor.Mark,
                "Add a PerfView 'Mark' event to the event stream with a optional string message");
            parser.DefineOptionalParameter("Message", ref Message, "The string message to attach to the PerfView Mark event.");

            parser.DefineParameterSet("abort", ref DoCommand, App.CommandProcessor.Abort,
                "Insures that any active PerfView sessions are stopped.");

            parser.DefineParameterSet("merge", ref DoCommand, App.CommandProcessor.Merge,
                "Combine separate ETL files into a single ETL file (that can be decoded on another machine).");
            parser.DefineOptionalParameter("DataFile", ref DataFile, "ETL file containing profile data.");
            parser.DefineOptionalQualifier("ImageIDsOnly", ref ImageIDsOnly,
                "Only perform image ID injection during the merge operation.");

            parser.DefineParameterSet("unzip", ref DoCommand, App.CommandProcessor.Unzip,
                "Unpack a ZIP file into its ETL file (and possibly its NGEN PDBS) /WPR option can be specified.");
            parser.DefineOptionalParameter("DataFile", ref DataFile, "ETL file containing profile data.");

            parser.DefineParameterSet("listSessions", ref DoCommand, App.CommandProcessor.ListSessions,
                "Lists active ETW sessions.");

            parser.DefineParameterSet("ListCpuCounters", ref DoCommand, App.CommandProcessor.ListCpuCounters,
                "Lists the ListCpuCounters CPU counters available on the system (win8+ only).");

            parser.DefineParameterSet("EnableKernelStacks", ref DoCommand, App.CommandProcessor.EnableKernelStacks,
                "On X64 machines if you have problems with broken stacks when the code is executing in the kernel," +
                " setting this option and rebooting may improve things");

            parser.DefineParameterSet("DisableKernelStacks", ref DoCommand, App.CommandProcessor.DisableKernelStacks,
                "Resets the registry keys set by EnableKernelStack.");

            string ProcessParam = null;
            parser.DefineParameterSet("HeapSnapshot", ref DoCommand, App.CommandProcessor.HeapSnapshot,
                "Take a snapshot of the CLR GC heap of a process.");
            parser.DefineParameter("Process", ref ProcessParam, "The process ID or Process Name (Exe without extension) of the process  take a heap snapshot.");
            parser.DefineOptionalParameter("DataFile", ref DataFile, "The name of the file to place the heap snapshot.");
            parser.DefineOptionalQualifier("SaveETL", ref SaveETL, "Save an ETL file along with the GCDump file when dumping the JS Heap.");
            parser.DefineOptionalQualifier("MaxDumpCountK", ref MaxDumpCountK,
                "The maximum number of objects (in K or thousands) to place int the .gcDump file.   Sample sufficiently to hit this metric.");
            parser.DefineOptionalQualifier("Freeze", ref Freeze, "Freeze the dump while data is taken.");

            parser.DefineParameterSet("ForceGC", ref DoCommand, App.CommandProcessor.ForceGC,
                    "Forces a GC on the specified process");
            parser.DefineParameter("Process", ref ProcessParam, "The process ID or Process Name (Exe without extension) of the process to force a GC.");

            // We have both a qualifier and a parameter named Process. It is OK that they use the same variable, but the parameter should not
            // overwrite the qualifier if it is null.  
            if (ProcessParam != null)
            {
                Process = ProcessParam;
            }

            parser.DefineParameterSet("HeapSnapshotFromProcessDump", ref DoCommand, App.CommandProcessor.HeapSnapshotFromProcessDump,
                "Extract the CLR GC heap from a process dump file specified.");
            parser.DefineParameter("ProcessDumpFile", ref ProcessDumpFile, "The name of the input process dump file.");
            parser.DefineOptionalParameter("DataFile", ref DataFile, "The name of the file to place the heap snapshot.");
            // TODO FIX NOW parser.DefineOptionalQualifier("DumpData", ref DumpData, "Dump the data as well as the connectivity information.");

            parser.DefineParameterSet("GuiRun", ref DoCommand, App.CommandProcessor.GuiRun, "Opens the 'Run' dialog box.");
            parser.DefineParameterSet("GuiCollect", ref DoCommand, App.CommandProcessor.GuiCollect, "Opens the 'Collect' dialog box.");
            parser.DefineParameterSet("GuiHeapSnapshot", ref DoCommand, App.CommandProcessor.GuiHeapSnapshot,
                "Opens the 'TakeHeapSnapshot' dialog box.");

            parser.DefineParameterSet("UserCommand", ref DoCommand, App.CommandProcessor.UserCommand,
                "Runs a user defined command.  Type 'PerfView UserCommandHelp' to see the help for all the user commands. " +
                "See PerfView Extensions in the users guide for more on creating user commands.");
            parser.DefineParameter("CommandAndArgs", ref CommandAndArgs, "User command to run and any arguments.");

            parser.DefineParameterSet("UserCommandHelp", ref DoCommand, App.CommandProcessor.UserCommandHelp,
                "Displays help for user commands.  Also see Help->User Command Help in the GUI.");

            parser.DefineParameterSet("CreateExtensionProject", ref DoCommand, App.CommandProcessor.CreateExtensionProject,
                "Creates a VS project for creates a perfView extension.");
            parser.DefineOptionalParameter("ExtensionName", ref ExtensionName, "The name of the extension (no .DLL)");

#if CROSS_GENERATION_LIVENESS
            parser.DefineParameterSet("CollectCrossGenerationLiveness", ref DoCommand, App.CommandProcessor.CollectCrossGenerationLiveness,
                "Collect a heap snapshot that can be used to do cross-generation liveness analysis.");
            parser.DefineQualifier("PID", ref CGL_PID, "The process ID of the process to snapshot.");
            parser.DefineQualifier("Generation", ref CGL_Generation, "The generation of the GC to collect.");
            parser.DefineQualifier("PromotedBytesThreshold", ref CGL_PromotedBytesThreshold, "The threshold of promoted bytes after which a snapshot of the heap should be collected.");
            parser.DefineQualifier("OutputFile", ref CGL_PathToOutputFile, "The full path including filename where the resulting gcdump file should be stored.");
#endif

            parser.DefineDefaultParameterSet(ref DoCommand, App.CommandProcessor.View, "View profile data.");
            parser.DefineOptionalParameter("DataFile", ref DataFile, "ETL or ETLX file containing profile data.");
        }
        #endregion
    };
}
