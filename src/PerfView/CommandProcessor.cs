using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.AspNet;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Diagnostics.Utilities;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Triggers;
using Utilities;
using Trigger = Triggers.Trigger;

namespace PerfView
{
    /// <summary>
    /// CommandProcessor knows how to take a CommandLineArgs and do basic operations 
    /// that are NOT gui dependent.  
    /// </summary>
    public class CommandProcessor
    {
        public CommandProcessor() { }
        public int ExecuteCommand(CommandLineArgs parsedArgs)
        {
            try
            {
                parsedArgs.DoCommand(parsedArgs);
                return 0;
            }
            catch (ThreadInterruptedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                bool userLevel;
                var message = ExceptionMessage.GetUserMessage(ex, out userLevel);
                LogFile.WriteLine("[{0}]", message);
                return 1;
            }
        }

        public bool CommandLineCommand;
        public bool CollectingData;
        public TextWriter LogFile
        {
            get { return m_logFile; }
            set { m_logFile = value; }
        }

        /// <summary>
        /// Set to true if the command would like the Log viewed if possible
        /// </summary>
        public bool ShowLog;

        /// <summary>
        /// If this is set, we don't exit the current process when we elevate
        /// </summary>
        public bool NoExitOnElevate;

        // Command line commands.  
        public void Run(CommandLineArgs parsedArgs)
        {
            LaunchPerfViewElevatedIfNeeded("Run", parsedArgs);

            // TODO can we simpify?
            // Find the command from the command line and see if we need to wrap it in cmd /c 
            var exeName = GetExeName(parsedArgs.CommandLine);

            // Add the support directory to the path so you get the tutorial examples.  (
            if (!s_addedSupportDirToPath)
            {
                Environment.SetEnvironmentVariable("PATH", Environment.GetEnvironmentVariable("PATH") + ";" + SupportFiles.SupportFileDir);
                s_addedSupportDirToPath = true;
            }
            var exeFullPath = Command.FindOnPath(exeName);

            if (string.Compare(Path.GetExtension(exeFullPath), ".exe", StringComparison.OrdinalIgnoreCase) == 0 && parsedArgs.Process == null)
            {
                parsedArgs.Process = Path.GetFileNameWithoutExtension(exeFullPath);
            }

            if (exeFullPath == null && string.Compare(exeName, "start", StringComparison.OrdinalIgnoreCase) != 0)
            {
                throw new FileNotFoundException("Could not find command " + exeName + " on path.");
            }

            var fullCmdLine = parsedArgs.CommandLine;
            if (string.Compare(Path.GetExtension(exeFullPath), ".exe", StringComparison.OrdinalIgnoreCase) != 0 ||
                fullCmdLine.IndexOfAny(new char[] { '<', '>', '&' }) >= 0)   // File redirection ...
            {
                fullCmdLine = "cmd /c call " + parsedArgs.CommandLine;
            }

            // OK actually do the work.
            parsedArgs.NoNGenRundown = true;        // You don't need rundown for the run command because the process of interest will have died.  
            bool success = false;
            Command cmd = null;
            try
            {
                Start(parsedArgs);
                Thread.Sleep(100);          // Allow time for the start rundown events OS events to happen.  
                DateTime startTime = DateTime.Now;
                LogFile.WriteLine("Starting at {0}", startTime);
                // TODO allow users to specify the launch directory
                LogFile.WriteLine("Current Directory {0}", Environment.CurrentDirectory);
                LogFile.WriteLine("Executing: {0} {{", fullCmdLine);

                // Options:  add support dir to path so that tutorial examples work.  
                var options = new CommandOptions().AddNoThrow().AddTimeout(CommandOptions.Infinite).AddOutputStream(LogFile);
                cmd = new Command(fullCmdLine, options);

                // We break this up so that on thread interrupted exceptions can happen
                while (!cmd.HasExited)
                {
                    if (parsedArgs.MaxCollectSec != 0 && (DateTime.Now - startTime).TotalSeconds > parsedArgs.MaxCollectSec)
                    {
                        LogFile.WriteLine("Exceeded the maximum collection time of {0} sec.", parsedArgs.MaxCollectSec);
                        parsedArgs.NoNGenRundown = false;
                        break;
                    }
                    Thread.Sleep(200);
                }
                DateTime stopTime = DateTime.Now;
                LogFile.WriteLine("}} Stopping at {0} = {1:f3} sec", stopTime, (stopTime - startTime).TotalSeconds);

                Stop(parsedArgs);

                if (cmd.HasExited)
                {
                    if (cmd.ExitCode != 0)
                    {
                        LogFile.WriteLine("Warning: Command exited with non-success error code 0x{0:x}", cmd.ExitCode);
                    }
                }
                else
                {
                    LogFile.WriteLine("Warning: Command did not exit, killing.");
                    cmd.Kill();
                }
                success = true;
            }
            finally
            {
                if (!success)
                {
                    if (cmd != null)
                    {
                        cmd.Kill();
                    }

                    Abort(parsedArgs);
                }
            }
        }
        public void Collect(CommandLineArgs parsedArgs)
        {
            LaunchPerfViewElevatedIfNeeded("Collect", parsedArgs);

            // When you collect we ALWAYS use circular buffer mode (too dangerous otherwise).
            // Users can use a very large number if they want 'infinity'.  
            if (parsedArgs.CircularMB == 0)
            {
                LogFile.WriteLine("Circular buffer size = 0, setting to 500.");
                parsedArgs.CircularMB = 500;
            }

            for (int collectionNum = 1; ;)
            {
                if (parsedArgs.CollectMultiple > 1)
                {
                    LogFile.WriteLine("[************** CollectMultple={0} collecting {1} ****************]", parsedArgs.CollectMultiple, collectionNum);
                }

                bool success = false;
                ManualResetEvent collectionCompleted = new ManualResetEvent(false);
                try
                {
                    m_aborted = false;
                    if (parsedArgs.NoGui)
                    {
                        SetupWaitNoGui(collectionCompleted, parsedArgs);
                    }
                    else
                    {
                        SetupWaitGui(collectionCompleted, parsedArgs);
                    }

                    WaitForStart(parsedArgs, collectionCompleted);

                    if (collectionCompleted.WaitOne(0))
                    {
                        LogFile.WriteLine("Collection aborted before we even started.");
                    }
                    else
                    {
                        Start(parsedArgs);
                        WaitUntilCollectionDone(collectionCompleted, parsedArgs, DateTime.Now);
                        if (m_aborted)
                        {
                            throw new ThreadInterruptedException();
                        }

                        Stop(parsedArgs);
                        success = true;
                    }
                }
                finally
                {
                    collectionCompleted.Set();  // This insures that the GUI window closes.  
                    if (!success)
                    {
                        Abort(parsedArgs);
                    }
                }

                collectionNum++;
                if (collectionNum > parsedArgs.CollectMultiple)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// If there are any command line arguments in 'parsedArgs' that indicate that we should wait until starting do so.
        /// 'collectionCompleted' is an event that is fired when among other things the user manually dismisses the collection
        /// (so you should stop).  
        /// </summary>
        private void WaitForStart(CommandLineArgs parsedArgs, ManualResetEvent collectionCompleted)
        {
#if !DOTNET_CORE // perf counters dont exist on .NET Core
            if (parsedArgs.StartOnPerfCounter != null)
            {
                if (!App.IsElevated)
                {
                    throw new ApplicationException("Must be elevated to collect ETW information.");
                }

                DateTime waitStartTime = DateTime.Now;
                DateTime lastProgressReportTime = waitStartTime;
                LogFile.WriteLine("[StartOnPerfCounter active waiting for trigger: {0}]", string.Join(",", parsedArgs.StartOnPerfCounter));
                var startTriggers = new List<Trigger>();
                try
                {
                    // Set up the triggers
                    bool startTriggered = false;
                    foreach (var startTriggerSpec in parsedArgs.StartOnPerfCounter)
                    {
                        var perfCtrTrigger = new PerformanceCounterTrigger(startTriggerSpec, 0, LogFile, delegate (PerformanceCounterTrigger startTrigger)
                        {
                            LogFile.WriteLine("StartOnPerfCounter " + startTriggerSpec + " Triggered.  Value: " + startTrigger.CurrentValue.ToString("n1"));
                            startTriggered = true;
                        });
                        perfCtrTrigger.MinSecForTrigger = parsedArgs.MinSecForTrigger;
                        startTriggers.Add(perfCtrTrigger);
                    }

                    // Wait for the triggers to happen.
                    while (!collectionCompleted.WaitOne(200))
                    {
                        if (startTriggered)
                        {
                            break;
                        }

                        var now = DateTime.Now;
                        if ((now - lastProgressReportTime).TotalSeconds > 10)
                        {
                            LogFile.WriteLine("Waiting for start trigger {0} sec.", (int)(now - waitStartTime).TotalSeconds);
                            foreach (var startTrigger in startTriggers)
                            {
                                var triggerStatus = startTrigger.Status;
                                if (triggerStatus.Length != 0)
                                {
                                    LogFile.WriteLine(triggerStatus);
                                }
                            }
                            lastProgressReportTime = now;
                        }
                    }
                }
                finally
                {
                    foreach (var startTrigger in startTriggers)
                    {
                        startTrigger.Dispose();
                    }
                }
            }
#endif
        }

        public void Start(CommandLineArgs parsedArgs)
        {
            LaunchPerfViewElevatedIfNeeded("Start", parsedArgs);

            // Are we on an X86 machine?
            if (Environment.Is64BitOperatingSystem)
            {
                if (!IsKernelStacks64Enabled())
                {
                    var ver = Environment.OSVersion.Version.Major * 10 + Environment.OSVersion.Version.Minor;
                    if (ver <= 61)
                    {
                        LogFile.WriteLine("Warning: This trace is being collected on a X64 machine on a Pre Win8 OS");
                        LogFile.WriteLine("         And paging is allowed in the kernel.  This can cause stack breakage");
                        LogFile.WriteLine("         when samples are taken in the kernel and there is memory pressure.");
                        LogFile.WriteLine("         It is recommended that you disable paging in the kernel to decrease");
                        LogFile.WriteLine("         the number of broken stacks.   To do this run the command:");
                        LogFile.WriteLine("");
                        LogFile.WriteLine("         PerfView EnableKernelStacks ");
                        LogFile.WriteLine("");
                        LogFile.WriteLine("         A reboot will be required for the change to have an effect.");
                        LogFile.WriteLine("");
                    }
                }
            }

            ETWClrProfilerTraceEventParser.Keywords profilerKeywords = 0;
            if (parsedArgs.DotNetCalls)
            {
                profilerKeywords |= ETWClrProfilerTraceEventParser.Keywords.Call;
            }

            if (parsedArgs.DotNetCallsSampled)
            {
                profilerKeywords |= ETWClrProfilerTraceEventParser.Keywords.CallSampled;
            }

            if (parsedArgs.DotNetAlloc)
            {
                profilerKeywords |= ETWClrProfilerTraceEventParser.Keywords.GCAlloc;
            }

            if (parsedArgs.DotNetAllocSampled)
            {
                profilerKeywords |= ETWClrProfilerTraceEventParser.Keywords.GCAllocSampled;
            }

            if (parsedArgs.DisableInlining)
            {
                profilerKeywords |= ETWClrProfilerTraceEventParser.Keywords.DisableInlining;
            }

            if (parsedArgs.RuntimeLoading)
            {
                parsedArgs.ClrEvents |= ClrTraceEventParser.Keywords.CompilationDiagnostic;
                parsedArgs.ClrEvents |= ClrTraceEventParser.Keywords.MethodDiagnostic;
                parsedArgs.ClrEvents |= ClrTraceEventParser.Keywords.TypeDiagnostic;
            }

            if (profilerKeywords != 0)
            {
                InstallETWClrProfiler(LogFile, (int)profilerKeywords);
                LogFile.WriteLine("WARNING: Only processes that start after this point will log object allocation events.");
            }

            if (parsedArgs.DataFile == null)
            {
                parsedArgs.DataFile = "PerfViewData.etl";
            }

            // The DataFile does not have the .zip associated with it (it is implied)
            if (parsedArgs.DataFile.EndsWith(".etl.zip", StringComparison.OrdinalIgnoreCase))
            {
                parsedArgs.DataFile = parsedArgs.DataFile.Substring(0, parsedArgs.DataFile.Length - 4);
            }
            else if(!parsedArgs.DataFile.EndsWith(".etl"))
            {
                parsedArgs.DataFile = parsedArgs.DataFile + ".etl";
            }

            // Don't clobber the results file if we were told not to.  
            if (parsedArgs.CollectMultiple > 1)
            {
                var finalResultFile = parsedArgs.DataFile;
                if (parsedArgs.ShouldZip)
                {
                    finalResultFile = finalResultFile + ".zip";
                }

                finalResultFile = GetNewFile(finalResultFile);
                if (parsedArgs.ShouldZip)
                {
                    finalResultFile = finalResultFile.Substring(0, finalResultFile.Length - 4);
                }

                parsedArgs.DataFile = finalResultFile;
            }

            string zipFileName = Path.ChangeExtension(parsedArgs.DataFile, ".etl.zip");
            string userFileName = Path.ChangeExtension(parsedArgs.DataFile, ".etl");
            string kernelFileName = Path.ChangeExtension(parsedArgs.DataFile, ".kernel.etl");
            string heapFileName = Path.ChangeExtension(parsedArgs.DataFile, ".userheap.etl");
            string rundownFileName = Path.ChangeExtension(parsedArgs.DataFile, ".clrRundown.etl");
            string kernelRundownFileName = Path.ChangeExtension(parsedArgs.DataFile, ".kernelRundown.etl");
            // Insure that old data is gone
            var fileNames = new string[] { zipFileName, userFileName, kernelFileName, heapFileName, rundownFileName, kernelRundownFileName };
            try
            {
                foreach (var fileName in fileNames)
                {
                    FileUtilities.ForceDelete(fileName);
                }
            }
            catch (IOException)
            {
                LogFile.WriteLine("Files in use, aborting and trying again.");
                Abort(parsedArgs);
                foreach (var fileName in fileNames)
                {
                    FileUtilities.ForceDelete(fileName);
                }
            }
            if (parsedArgs.Wpr)
            {
                // Just creating this directory is enough for the rest to 'just work' 
                var ngenPdbs = parsedArgs.DataFile + ".ngenpdb";
                LogFile.WriteLine("Putting NGEN pdbs into {0}", ngenPdbs);
                Directory.CreateDirectory(ngenPdbs);
            }

            CollectingData = true;
            // Create the sessions

            if (parsedArgs.InMemoryCircularBuffer)
            {
                kernelFileName = null;              // In memory buffers dont have a file name  
            }
            else
            {
                LogFile.WriteLine("[Kernel Log: {0}]", Path.GetFullPath(kernelFileName));
            }

            using (TraceEventSession kernelModeSession = new TraceEventSession(s_KernelessionName, kernelFileName))
            {
                if (parsedArgs.CpuCounters != null)
                {
                    SetCpuCounters(parsedArgs.CpuCounters);
                    parsedArgs.KernelEvents |= KernelTraceEventParser.Keywords.PMCProfile;
                }
                else
                {
                    if ((parsedArgs.KernelEvents & KernelTraceEventParser.Keywords.PMCProfile) != 0)
                    {
                        throw new ApplicationException("The PMCProfile should not be set explicitly.  Simply set the CpuCounters.");
                    }
                }

                LogFile.WriteLine("Kernel keywords enabled: {0}", parsedArgs.KernelEvents);
                if (parsedArgs.KernelEvents != KernelTraceEventParser.Keywords.None)
                {
                    if ((parsedArgs.KernelEvents & (KernelTraceEventParser.Keywords.Process | KernelTraceEventParser.Keywords.ImageLoad)) == 0 &&
                        (parsedArgs.KernelEvents & (KernelTraceEventParser.Keywords.Profile | KernelTraceEventParser.Keywords.ContextSwitch)) != 0)
                    {
                        LogFile.WriteLine("Kernel process and image thread events not present, adding them");
                        parsedArgs.KernelEvents |= (
                            KernelTraceEventParser.Keywords.Process |
                            KernelTraceEventParser.Keywords.ImageLoad |
                            KernelTraceEventParser.Keywords.Thread);
                    }

                    // If these are on, turn on Virtual Allocs as well.  
                    if (parsedArgs.OSHeapProcess != 0 || parsedArgs.OSHeapExe != null || parsedArgs.DotNetAlloc || parsedArgs.DotNetAllocSampled)
                    {
                        parsedArgs.KernelEvents |= KernelTraceEventParser.Keywords.VirtualAlloc;
                    }

                    kernelModeSession.BufferSizeMB = parsedArgs.BufferSizeMB;
                    kernelModeSession.StackCompression = parsedArgs.StackCompression;
                    kernelModeSession.CpuSampleIntervalMSec = parsedArgs.CpuSampleMSec;
                    if (parsedArgs.CircularMB != 0)
                    {
                        kernelModeSession.CircularBufferMB = parsedArgs.CircularMB;
                    }

                    kernelModeSession.EnableKernelProvider(parsedArgs.KernelEvents, parsedArgs.KernelEvents);
                }

                // Turn on the OS Heap stuff if anyone asked for it.  
                TraceEventSession heapSession = null;
                if (parsedArgs.OSHeapProcess != 0 || parsedArgs.OSHeapExe != null)
                {
                    if (parsedArgs.OSHeapProcess != 0 && parsedArgs.OSHeapExe != null)
                    {
                        throw new ApplicationException("OSHeapProcess and OSHeapExe cannot both be specified simultaneously.");
                    }

                    heapSession = new TraceEventSession(s_HeapSessionName, heapFileName);
                    // Default is 256Meg and twice whatever the others are
                    heapSession.BufferSizeMB = Math.Max(256, parsedArgs.BufferSizeMB * 2);

                    if (parsedArgs.CircularMB != 0)
                    {
                        LogFile.WriteLine("[Warning: OS Heap provider does not use Circular buffering.]");
                    }

                    if (parsedArgs.OSHeapProcess != 0)
                    {
                        heapSession.EnableWindowsHeapProvider(parsedArgs.OSHeapProcess);
                        LogFile.WriteLine("[Enabling heap logging for process {0} to : {1}]", parsedArgs.OSHeapProcess, Path.GetFullPath(heapFileName));
                    }
                    else
                    {
                        parsedArgs.OSHeapExe = Path.ChangeExtension(parsedArgs.OSHeapExe, ".exe");
                        heapSession.EnableWindowsHeapProvider(parsedArgs.OSHeapExe);
                        LogFile.WriteLine("[Enabling heap logging for process with EXE {0} to : {1}]", parsedArgs.OSHeapExe, Path.GetFullPath(heapFileName));
                    }
                }

                if (parsedArgs.InMemoryCircularBuffer)
                {
                    userFileName = null;                    // In memory buffers don't have a file name 
                }
                else
                {
                    LogFile.WriteLine("[User mode Log: {0}]", Path.GetFullPath(userFileName));
                }

                using (TraceEventSession userModeSession = new TraceEventSession(s_UserModeSessionName, userFileName))
                {
                    TraceEventProviderOptions options = new TraceEventProviderOptions();
                    if (parsedArgs.FocusProcess != null)
                    {
                        int processId;
                        if (Int32.TryParse(parsedArgs.FocusProcess, out processId))
                        {
                            options.ProcessIDFilter = new List<int>(1) { processId };
                            LogFile.WriteLine("**** /FocusProcess specified LIMITING user mode events to process with ID {0}", processId);
                        }
                        else
                        {
                            if (!parsedArgs.FocusProcess.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                LogFile.WriteLine("**** WARNING: process name does not end in .exe, likely you will exclude processes of interest");

                            LogFile.WriteLine("**** /FocusProcess specified LIMITING user mode events to process with name {0}", parsedArgs.FocusProcess);
                            options.ProcessNameFilter = new List<string>(1) { parsedArgs.FocusProcess };
                        }
                    }

                    if (parsedArgs.EnableEventsInContainers)
                    {
                        options.EnableInContainers = true;
                    }
                    if (parsedArgs.EnableSourceContainerTracking)
                    {
                        options.EnableSourceContainerTracking = true;
                    }

                    var stacksEnabled = options.Clone();
                    stacksEnabled.StacksEnabled = true;

                    userModeSession.BufferSizeMB = parsedArgs.BufferSizeMB;
                    // DotNetAlloc needs a large buffer size too.  
                    if (parsedArgs.DotNetAlloc || parsedArgs.DotNetCalls)
                    {
                        userModeSession.BufferSizeMB = Math.Max(512, parsedArgs.BufferSizeMB * 2);
                    }

                    // Note that you don't need the rundown 300Meg if you are V4.0.
                    if (parsedArgs.CircularMB != 0)
                    {
                        // Typically you only need less than 1/5 the space + rundown. However, some scenarios primarily
                        // use the user mode session so we keep it the full size.
                        userModeSession.CircularBufferMB = parsedArgs.CircularMB + 300;
                    }

                    // Turn on PerfViewLogger
                    EnableUserProvider(userModeSession, "PerfViewLogger", PerfViewLogger.Log.Guid,
                        TraceEventLevel.Verbose, ulong.MaxValue, options);

                    Thread.Sleep(100);  // Give it at least some time to start, it is not synchronous. 

                    PerfViewLogger.Log.StartTracing();
                    PerfViewLogger.StartTime = DateTime.UtcNow;

                    PerfViewLogger.Log.SessionParameters(s_KernelessionName, kernelFileName ?? "",
                        kernelModeSession.BufferSizeMB, kernelModeSession.CircularBufferMB);
                    PerfViewLogger.Log.KernelEnableParameters(parsedArgs.KernelEvents, parsedArgs.KernelEvents);
                    PerfViewLogger.Log.SessionParameters(s_UserModeSessionName, userFileName ?? "",
                        userModeSession.BufferSizeMB, userModeSession.CircularBufferMB);

                    // If you turn on allocation sampling, then you also need the types and names and deaths.  
                    if ((parsedArgs.ClrEvents & (ClrTraceEventParser.Keywords.GCSampledObjectAllocationHigh | ClrTraceEventParser.Keywords.GCSampledObjectAllocationLow)) != 0)
                    {
                        parsedArgs.ClrEvents |= ClrTraceEventParser.Keywords.Type | ClrTraceEventParser.Keywords.GCHeapSurvivalAndMovement;
                    }

                    if (parsedArgs.Wpr)
                    {
                        SetWPRProviders(userModeSession, options);
                    }
                    else if (parsedArgs.ClrEvents != ClrTraceEventParser.Keywords.None)
                    {

                        // If we don't change the core set then we should assume the user wants more stuff.  
                        var coreClrEvents = ClrTraceEventParser.Keywords.Default &
                            ~ClrTraceEventParser.Keywords.NGen & ~ClrTraceEventParser.Keywords.SupressNGen;

                        if ((parsedArgs.ClrEvents & coreClrEvents) == coreClrEvents)
                        {
                            LogFile.WriteLine("Turning on more CLR GC, JScript and ASP.NET Events.");

                            // Turn on DotNet Telemetry
                            EnableUserProvider(userModeSession, "DotNet",
                                new Guid("319dc449-ada5-50f7-428e-957db6791668"), TraceEventLevel.Verbose, ulong.MaxValue, stacksEnabled);

                            // Turn on ETW logging about etw logging (so we get lost event info) ... (Really need a separate session to get the lost event Info properly). 
                            EnableUserProvider(userModeSession, "Microsoft-Windows-Kernel-EventTracing",
                                new Guid("B675EC37-BDB6-4648-BC92-F3FDC74D3CA2"), TraceEventLevel.Verbose, 0x70, stacksEnabled);

                            // Turn on File Create (open) logging as it is useful for investigations and lightweight. 
                            // Don't bother if the Kernel FileIOInit events are on because they are strictly better
                            // and you end up with annoying redundancy.  
                            if ((parsedArgs.KernelEvents & KernelTraceEventParser.Keywords.FileIOInit) == 0)
                            {
                                // 0x80 = CREATE_FILE (which is any open, including GetFileAttributes etc.   
                                EnableUserProvider(userModeSession, "Microsoft-Windows-Kernel-File",
                                    new Guid("EDD08927-9CC4-4E65-B970-C2560FB5C289"), TraceEventLevel.Verbose, 0x80, stacksEnabled);
                            }

                            // Turn on the user-mode Process start events.  This allows you to get the stack of create-process calls
                            // 0x10 =  Process  
                            EnableUserProvider(userModeSession, "Microsoft-Windows-Kernel-Process",
                                new Guid("22FB2CD6-0E7B-422B-A0C7-2FAD1FD0E716"), TraceEventLevel.Informational, 0x10, stacksEnabled);

                            // Default CLR events also means ASP.NET and private events. 
                            // Turn on ASP.NET at informational by default.
                            EnableUserProvider(userModeSession, "ASP.NET", AspNetTraceEventParser.ProviderGuid,
                                parsedArgs.ClrEventLevel, ulong.MaxValue - 0x2, options); // the - 0x2 will turn off Module level logging, which is very verbose
                            CheckAndWarnAboutAspNet(AspNetTraceEventParser.ProviderGuid);

                            // Turn on the new V4.5.1 ASP.Net  EventSource (TODO Not clear we should do this, and how much to turn on).  
                            // TODO turned on stacks for debugging probably should turn off in the long run.  
                            EnableUserProvider(userModeSession, "*Microsoft-Windows-ASPNET",
                                 new Guid("ee799f41-cfa5-550b-bf2c-344747c1c668"), TraceEventLevel.Informational, ulong.MaxValue, stacksEnabled);

                            // Turn on just minimum (start and stop) for IIS)
                            EnableUserProvider(userModeSession, "Microsoft-Windows-IIS",
                                new Guid("DE4649C9-15E8-4FEA-9D85-1CDDA520C334"), TraceEventLevel.Critical, 0, options);

                            // These let you see IE in and have few events. 
                            EnableUserProvider(userModeSession, "Microsoft-PerfTrack-IEFRAME",
                                new Guid("B2A40F1F-A05A-4DFD-886A-4C4F18C4334C"), TraceEventLevel.Verbose, ulong.MaxValue, options);

                            EnableUserProvider(userModeSession, "Microsoft-PerfTrack-MSHTML",
                                new Guid("FFDB9886-80F3-4540-AA8B-B85192217DDF"), TraceEventLevel.Verbose, ulong.MaxValue, options);

                            // Set you see the URLs that IE is processing. 
                            EnableUserProvider(userModeSession, "Microsoft-Windows-WinINet",
                                new Guid("43D1A55C-76D6-4F7E-995C-64C711E5CAFE"), TraceEventLevel.Verbose, 2, options);

                            // Turn on WCF.  This can be very verbose.  We need to figure out a balance  
                            EnableUserProvider(userModeSession, "Microsoft-Windows-Application Server-Applications",
                                ApplicationServerTraceEventParser.ProviderGuid, TraceEventLevel.Informational, ulong.MaxValue, options);

                            EnableUserProvider(userModeSession, "Microsoft-IE",
                                new Guid("9E3B3947-CA5D-4614-91A2-7B624E0E7244"), TraceEventLevel.Informational, 0x1300, options);

                            EnableUserProvider(userModeSession, "Microsoft-Windows-DNS-Client",
                                new Guid("1C95126E-7EEA-49A9-A3FE-A378B03DDB4D"), TraceEventLevel.Informational, ulong.MaxValue, options);

                            EnableUserProvider(userModeSession, "Microsoft-Windows-DirectComposition",
                                new Guid("C44219D0-F344-11DF-A5E2-B307DFD72085"), TraceEventLevel.Verbose, 0x4, options);

                            EnableUserProvider(userModeSession, "Microsoft-Windows-Immersive-Shell",
                                new Guid("315A8872-923E-4EA2-9889-33CD4754BF64"), TraceEventLevel.Informational, ulong.MaxValue, options);

                            EnableUserProvider(userModeSession, "Microsoft-Windows-XAML",
                                new Guid("531A35AB-63CE-4BCF-AA98-F88C7A89E455"), TraceEventLevel.Informational, ulong.MaxValue, options);

                            // Turn on JScript events too
                            EnableUserProvider(userModeSession, "Microsoft-JScript", JScriptTraceEventParser.ProviderGuid,
                                TraceEventLevel.Verbose, ulong.MaxValue, options);

                            EnableUserProvider(userModeSession, "CLRPrivate", ClrPrivateTraceEventParser.ProviderGuid,
                                TraceEventLevel.Informational,
                                (ulong)(
                                    ClrPrivateTraceEventParser.Keywords.GC |
                                    ClrPrivateTraceEventParser.Keywords.Binding |
                                    ClrPrivateTraceEventParser.Keywords.Fusion |
                                    ClrPrivateTraceEventParser.Keywords.MulticoreJit |   /* only works on verbose */
                                    // ClrPrivateTraceEventParser.Keywords.LoaderHeap |     /* only verbose */
                                    //  ClrPrivateTraceEventParser.Keywords.Startup 
                                    ClrPrivateTraceEventParser.Keywords.Stack
                                ), options);

                            if (parsedArgs.TplEvents != TplEtwProviderTraceEventParser.Keywords.None)
                            {
                                // Used to determine what is going on with tasks.
                                var netTaskStacks = stacksEnabled;
                                if (TraceEventProviderOptions.FilteringSupported)
                                {
                                    // This turns on stacks only for TaskScheduled (7) TaskWaitSend (10) and AwaitTaskContinuationScheduled (12)
                                    netTaskStacks = options.Clone();
                                    netTaskStacks.EventIDStacksToEnable = new List<int>(3) { 7, 10, 12 };
                                }
                                EnableUserProvider(userModeSession, ".NETTasks",
                                    TplEtwProviderTraceEventParser.ProviderGuid, parsedArgs.ClrEventLevel,
                                    (ulong)parsedArgs.TplEvents,
                                    netTaskStacks);
                            }

                            EnableUserProvider(userModeSession, ".NETFramework",
                                FrameworkEventSourceTraceEventParser.ProviderGuid,
                                 parsedArgs.ClrEventLevel,
                                (ulong)(
                                    FrameworkEventSourceTraceEventParser.Keywords.ThreadPool |
                                    FrameworkEventSourceTraceEventParser.Keywords.ThreadTransfer |
                                    FrameworkEventSourceTraceEventParser.Keywords.NetClient),
                                stacksEnabled);

                            // Turn on the Nuget package provider that tracks activity IDs. 
                            EnableUserProvider(userModeSession, "Microsoft.Tasks.Nuget", TraceEventProviders.GetEventSourceGuidFromName("Microsoft.Tasks.Nuget"), TraceEventLevel.Informational, 0x80, options);

                            // Turn on new SQL client logging 
                            EnableUserProvider(userModeSession, "Microsoft-AdoNet-SystemData",
                                TraceEventProviders.GetEventSourceGuidFromName("Microsoft-AdoNet-SystemData"),
                                TraceEventLevel.Informational,
                                1, // This enables just the client events.  
                                stacksEnabled);

                            EnableUserProvider(userModeSession, "ETWCLrProfiler Diagnostics",
                                new Guid(unchecked((int)0x6652970f), unchecked((short)0x1756), unchecked((short)0x5d8d), 0x08, 0x05, 0xe9, 0xaa, 0xd1, 0x52, 0xaa, 0x79),
                                TraceEventLevel.Verbose, ulong.MaxValue, options);

                            // TODO should we have stacks on for everything?
                            var diagSourceOptions = stacksEnabled.Clone();
                            // The removal of IgnoreShortCutKeywords turns on HTTP incoming and SQL events
                            // The spec below turns on outgoing Http requests.  
                            string filterSpec =
                                "HttpHandlerDiagnosticListener/System.Net.Http.Request@Activity2Start:" +
                                "Request.RequestUri" +
                                "\n" +
                                "HttpHandlerDiagnosticListener/System.Net.Http.Response@Activity2Stop:" +
                                "Response.StatusCode";
                            diagSourceOptions.AddArgument("FilterAndPayloadSpecs", filterSpec);
                            const ulong IgnoreShortCutKeywords = 0x0800;    // Turing this OFF enables all the shortcut keywords (ASP.NET and Entity Framework).  
                            EnableUserProvider(userModeSession, "Microsoft-Diagnostics-DiagnosticSource",
                                new Guid("adb401e1-5296-51f8-c125-5fda75826144"),
                                TraceEventLevel.Informational, ulong.MaxValue - IgnoreShortCutKeywords, diagSourceOptions);

                            // This is likely redundant with the diagnosticSource above, but is simpler to parse on the reader side.

                            EnableUserProvider(userModeSession, "Microsoft-AspNetCore-Hosting",
                                new Guid("9e620d2a-55d4-5ade-deb7-c26046d245a8"), TraceEventLevel.Verbose, ulong.MaxValue, options);

                            EnableUserProvider(userModeSession, "Microsoft-ApplicationInsights-Core",
                                new Guid("74af9f20-af6a-5582-9382-f21f674fb271"),
                                TraceEventLevel.Verbose, ulong.MaxValue, stacksEnabled);

                            // Turn on Power stuff
                            EnableUserProvider(userModeSession, "Microsoft-Windows-Kernel-Power",
                                new Guid("331C3B3A-2005-44C2-AC5E-77220C37D6B4"), TraceEventLevel.Informational, 0xFFB, options);
                            EnableUserProvider(userModeSession, "Microsoft-Windows-Kernel-Processor-Power",
                                new Guid("0F67E49F-FE51-4E9F-B490-6F2948CC6027"), TraceEventLevel.Informational, 0xE5D, options);
                            EnableUserProvider(userModeSession, "Microsoft-Windows-PowerCpl",
                                new Guid("B1F90B27-4551-49D6-B2BD-DFC6453762A6"), TraceEventLevel.Informational, ulong.MaxValue, options);
                            EnableUserProvider(userModeSession, "Microsoft-Windows-PowerCfg",
                                 new Guid("9F0C4EA8-EC01-4200-A00D-B9701CBEA5D8"), TraceEventLevel.Informational, ulong.MaxValue, options);

                            // If we have turned on CSwitch and ReadyThread events, go ahead and turn on networking stuff and antimalware too.
                            // It does not increase the volume in a significant way and they can be pretty useful.
                            if ((parsedArgs.KernelEvents & (KernelTraceEventParser.Keywords.Dispatcher | KernelTraceEventParser.Keywords.ContextSwitch))
                                == (KernelTraceEventParser.Keywords.Dispatcher | KernelTraceEventParser.Keywords.ContextSwitch))
                            {
                                EnableUserProvider(userModeSession, MicrosoftAntimalwareEngineTraceEventParser.ProviderName,
                                    MicrosoftAntimalwareEngineTraceEventParser.ProviderGuid,
                                    TraceEventLevel.Verbose, ulong.MaxValue, stacksEnabled);

                                EnableUserProvider(userModeSession, MicrosoftAntimalwareAMFilterTraceEventParser.ProviderName,
                                    MicrosoftAntimalwareAMFilterTraceEventParser.ProviderGuid,
                                    TraceEventLevel.Verbose, ulong.MaxValue, stacksEnabled);

                                EnableUserProvider(userModeSession, "Microsoft-Antimalware-Service",
                                    new Guid("751ef305-6c6e-4fed-b847-02ef79d26aef"),
                                    TraceEventLevel.Verbose, ulong.MaxValue, options);

                                EnableUserProvider(userModeSession, "Microsoft-Antimalware-RTP",
                                    new Guid("8e92deef-5e17-413b-b927-59b2f06a3cfc"),
                                    TraceEventLevel.Verbose, ulong.MaxValue, options);

                                EnableUserProvider(userModeSession, "Microsoft-Antimalware-Protection",
                                    new Guid("e4b70372-261f-4c54-8fa6-a5a7914d73da"),
                                    TraceEventLevel.Verbose, ulong.MaxValue, options);

                                EnableUserProvider(userModeSession, "Microsoft-Windows-HttpService",
                                    new Guid("DD5EF90A-6398-47A4-AD34-4DCECDEF795F"),
                                    parsedArgs.ClrEventLevel, ulong.MaxValue, stacksEnabled);

                                // TODO this can be expensive.   turned it down (not clear what we lose).  
                                EnableUserProvider(userModeSession, "Microsoft-Windows-TCPIP",
                                    new Guid("2F07E2EE-15DB-40F1-90EF-9D7BA282188A"), TraceEventLevel.Informational, ulong.MaxValue, stacksEnabled);

                                // This actually will not cause any events to fire unless you first also enable
                                // the kernel in a special way.  Basically doing
                                // netsh trace start scenario=InternetClient capture=yes correlation=no report=disabled maxSize=250 traceFile=NetMonTrace.net.etl
                                EnableUserProvider(userModeSession, "Microsoft-Windows-NDIS-PacketCapture",
                                    new Guid("2ED6006E-4729-4609-B423-3EE7BCD678EF"),
                                    TraceEventLevel.Informational, ulong.MaxValue, options);

                                EnableUserProvider(userModeSession, "Microsoft-Windows-WebIO",
                                    new Guid("50B3E73C-9370-461D-BB9F-26F32D68887D"), TraceEventLevel.Informational, ulong.MaxValue, options);

                                // This provider is verbose in high volume networking scnearios and its value is dubious.  
                                //EnableUserProvider(userModeSession, "Microsoft-Windows-Winsock-AFD",
                                //    new Guid("E53C6823-7BB8-44BB-90DC-3F86090D48A6"),
                                //    parsedArgs.ClrEventLevel, ulong.MaxValue);

                                // This is probably too verbose, but we will see 
                                EnableUserProvider(userModeSession, "Microsoft-Windows-WinINet",
                                    new Guid("43D1A55C-76D6-4F7E-995C-64C711E5CAFE"), TraceEventLevel.Verbose, ulong.MaxValue, options);

                                // This is probably too verbose, but we will see 
                                EnableUserProvider(userModeSession, "Microsoft-Windows-WinHttp",
                                    new Guid("7D44233D-3055-4B9C-BA64-0D47CA40A232"), TraceEventLevel.Verbose, ulong.MaxValue, options);

                                // This has proven to be too expensive.  Wait until we need it.  
                                // EnableUserProvider(userModeSession, "Microsoft-Windows-Networking-Correlation",
                                //     new Guid("83ED54F0-4D48-4E45-B16E-726FFD1FA4AF"), (TraceEventLevel)255, 0);

                                EnableUserProvider(userModeSession, "Microsoft-Windows-RPC",
                                    new Guid("6AD52B32-D609-4BE9-AE07-CE8DAE937E39"), TraceEventLevel.Informational, 0, options);

                                // This is what WPA turns on in its 'GENERAL' setting  
                                //Microsoft-Windows-Immersive-Shell: 0x0000000000100000: 0x04
                                //Microsoft-Windows-Kernel-Power: 0x0000000000000004: 0xff
                                //Microsoft-Windows-Win32k: 0x0000000000402000: 0xff
                                //Microsoft-Windows-WLAN-AutoConfig: 0x0000000000000200: 0xff
                                //.NET Common Language Runtime: 0x0000000000000098: 0x05
                                //Microsoft-JScript: 0x0000000000000001: 0xff e7ef96be-969f-414f-97d7-3ddb7b558ccc: 0x0000000000002000: 0xff
                                //MUI Resource Trace: : 0xff
                                //Microsoft-Windows-COMRuntime: 0x0000000000000003: 0xff
                                //Microsoft-Windows-Networking-Correlation: : 0xff
                                //Microsoft-Windows-RPCSS: : 0x04
                                //Microsoft-Windows-RPC: : 0x04 a669021c-c450-4609-a035-5af59af4df18: : 0x00
                                //Microsoft-Windows-Kernel-Processor-Power: : 0xff
                                //Microsoft-Windows-Kernel-StoreMgr: : 0xff e7ef96be-969f-414f-97d7-3ddb7b558ccc: : 0xff
                                //Microsoft-Windows-UserModePowerService: : 0xff
                                //Microsoft-Windows-Win32k: : 0xff
                                //Microsoft-Windows-ReadyBoostDriver: : 0xff

#if false            // TODO FIX NOW remove 
                    var networkProviders = new List<string>();
                    networkProviders.Add("Microsoft-Windows-WebIO:*:5:stack");
                    networkProviders.Add("Microsoft-Windows-WinINet:*:5:stack");
                    networkProviders.Add("Microsoft-Windows-TCPIP:*:5:stack");
                    networkProviders.Add("Microsoft-Windows-NCSI:*:5:stack");
                    networkProviders.Add("Microsoft-Windows-WFP:*:5:stack");
                    networkProviders.Add("Microsoft-Windows-Iphlpsvc-Trace:*:5:stack");
                    networkProviders.Add("Microsoft-Windows-WinHttp:*:5:stack");
                    networkProviders.Add("Microsoft-Windows-NDIS-PacketCapture");
                    networkProviders.Add("Microsoft-Windows-NWiFi:*:5:stack");
                    networkProviders.Add("Microsoft-Windows-NlaSvc:*:5:stack");
                    networkProviders.Add("Microsoft-Windows-NDIS:*:5:stack");

                    EnableAdditionalProviders(userModeSession, networkProviders.ToArray());
#endif
                            }
                        }
                        else if ((parsedArgs.ClrEvents & ClrTraceEventParser.Keywords.GC) != 0)
                        {
                            LogFile.WriteLine("Turned on additional CLR GC events");
                            EnableUserProvider(userModeSession, "CLRPrivate", ClrPrivateTraceEventParser.ProviderGuid,
                                TraceEventLevel.Informational, (ulong)ClrPrivateTraceEventParser.Keywords.GC, options);
                        }

                        if ((parsedArgs.KernelEvents & KernelTraceEventParser.Keywords.ReferenceSet) != 0)
                        {
                            // ALso get heap ranges if ReferenceSet is on.  
                            EnableUserProvider(userModeSession, "Win32HeapRanges", HeapTraceProviderTraceEventParser.HeapRangeProviderGuid,
                                TraceEventLevel.Verbose, 0, options);
                        }

                        if (profilerKeywords != 0)
                        {
                            // Turn on allocation profiling if the user asked for it.   
                            EnableUserProvider(userModeSession, "ETWClrProfiler",
                                ETWClrProfilerTraceEventParser.ProviderGuid, TraceEventLevel.Verbose,
                                (ulong)profilerKeywords,
                                stacksEnabled);
                        }

                        LogFile.WriteLine("Turning on VS CodeMarkers and MeasurementBlock Providers.");
                        EnableUserProvider(userModeSession, "MeasurementBlock",
                            new Guid("143A31DB-0372-40B6-B8F1-B4B16ADB5F54"), TraceEventLevel.Verbose, ulong.MaxValue, options);
                        EnableUserProvider(userModeSession, "CodeMarkers",
                            new Guid("641D7F6C-481C-42E8-AB7E-D18DC5E5CB9E"), TraceEventLevel.Verbose, ulong.MaxValue, options);

                        // Turn off NGEN if they asked for it.  
                        if (parsedArgs.NoNGenRundown)
                        {
                            parsedArgs.ClrEvents &= ~ClrTraceEventParser.Keywords.NGen;
                        }

                        // Force NGEN rundown if they asked for it. 
                        if (parsedArgs.ForceNgenRundown)
                        {
                            parsedArgs.ClrEvents &= ~ClrTraceEventParser.Keywords.SupressNGen;
                        }

                        LogFile.WriteLine("Enabling CLR Events: {0}", parsedArgs.ClrEvents);
                        EnableUserProvider(userModeSession, "CLR", ClrTraceEventParser.ProviderGuid,
                            parsedArgs.ClrEventLevel, (ulong)parsedArgs.ClrEvents, options);
                    }

                    // Start network monitoring capture if needed
                    if (parsedArgs.NetMonCapture)
                    {
                        parsedArgs.NetworkCapture = true;
                    }

                    if (parsedArgs.NetworkCapture)
                    {
                        string maxSize = "maxSize=1";
                        string correlation = "correlation=no";
                        string report = "report=disabled";
                        string scenario = "InternetClient";
                        string perfMerge = "perfMerge=no";
                        string traceFile = CacheFiles.FindFile(parsedArgs.DataFile, ".netmon.etl");

                        var osVer = Environment.OSVersion.Version.Major * 10 + Environment.OSVersion.Version.Minor;
                        if (parsedArgs.NetMonCapture || osVer < 62)
                        {
                            traceFile = Path.GetFileNameWithoutExtension(parsedArgs.DataFile) + "_netmon.etl";  // We use the _ to avoid conventions about merging.  
                            maxSize = "";
                            correlation = "";
                            perfMerge = "";
                            report = "";
                        }
                        FileUtilities.ForceDelete(traceFile);

                        EnableUserProvider(userModeSession, "Microsoft-Windows-NDIS-PacketCapture",
                            new Guid("2ED6006E-4729-4609-B423-3EE7BCD678EF"), TraceEventLevel.Informational, ulong.MaxValue, options);
                        EnableUserProvider(userModeSession, "Microsoft-Windows-TCPIP",
                            new Guid("2F07E2EE-15DB-40F1-90EF-9D7BA282188A"), TraceEventLevel.Informational, ulong.MaxValue, stacksEnabled);

                        string commandLine = string.Format("netsh trace start scenario={0} capture=yes {1} {2} {3} {4} \"traceFile={5}\"",
                            scenario, correlation, report, maxSize, perfMerge, traceFile);

                        LogFile.WriteLine("Turning on network packet monitoring");
                        LogFile.WriteLine("Can turn off running 'netsh trace stop' or rebooting.");

                        LogFile.WriteLine("Executing the command: {0}", commandLine);

                        // Make sure that if we are on a 64 bit machine we run the 64 bit version of netsh.  
                        var cmdExe = Path.Combine(Environment.GetEnvironmentVariable("SystemRoot"), "SysNative", "cmd.exe");
                        if (!File.Exists(cmdExe))
                        {
                            cmdExe = cmdExe.Replace("SysNative", "System32");
                        }

                        commandLine = cmdExe + " /c " + commandLine;
                        var command = Command.Run(commandLine, new CommandOptions().AddNoThrow().AddOutputStream(LogFile));

                        string netMonFile = Path.Combine(CacheFiles.CacheDir, "NetMonActive.txt");
                        File.WriteAllText(netMonFile, "");      // mark that Network monitoring is potentially active 

                        if (command.ExitCode != 0)
                        {
                            throw new ApplicationException("Could not turn on network packet monitoring with the 'netsh trace' command.");
                        }

                        LogFile.WriteLine("netsh trace command succeeded.");
                    }
                    if (parsedArgs.CCWRefCount)
                    {
                        EnableUserProvider(userModeSession, "InteropEventProvider", new Guid("c4ac552a-e1eb-4fa2-a651-b200efd7aa91"), TraceEventLevel.Verbose, ulong.MaxValue, stacksEnabled);
                    }

                    LogFile.WriteLine("Enabling Providers specified by the user.");
                    if (parsedArgs.Providers != null)
                    {
                        EnableAdditionalProviders(userModeSession, parsedArgs.Providers, parsedArgs.CommandLine, options);
                    }

                    // OK at this point, we want to leave both sessions for an indefinite period of time (even past process exit)
                    kernelModeSession.StopOnDispose = false;
                    userModeSession.StopOnDispose = false;
                    if (heapSession != null)
                    {
                        heapSession.StopOnDispose = false;
                    }

                    PerfViewLogger.Log.CommandLineParameters(ParsedArgsAsString(null, parsedArgs), Environment.CurrentDirectory, AppLog.VersionNumber);
                }
            }
        }

        /// <summary>
        /// Mimics the WPR user mode providers 
        /// </summary>
        private void SetWPRProviders(TraceEventSession userModeSession, TraceEventProviderOptions options)
        {
            EnableProvider(userModeSession, options, "Microsoft-Windows-Kernel-Power", 0x1000000000004L, (TraceEventLevel)0xff);
            EnableProvider(userModeSession, options, "Microsoft-Windows-PowerCpl", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Kernel-Power", 0x1000000000004L, (TraceEventLevel)0xff);

            LogFile.WriteLine("Adding the user mode providers that WPR would.");
            EnableProvider(userModeSession, options, "Microsoft-Windows-Kernel-Memory", 0x60);   // WPR uses kernel for this but this makes up for it. 
            EnableProvider(userModeSession, options, "Microsoft-Windows-WLAN-AutoConfig", 0x1000000000200L, (TraceEventLevel)0xff);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Tethering-Station", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-SleepStudy", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-WinINet", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-SettingSync", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-UIAutomationCore", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-ntshrui", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Kernel-PnP", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-NlaSvc", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Antimalware-Engine", 0xffffffffffffffffL, TraceEventLevel.Verbose);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Diagnosis-MSDE", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-MobilityCenter", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Diagnosis-WDC", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-AppHost", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-PushNotifications-Platform", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-ErrorReportingConsole", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-IME-KRTIP", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-FileHistory-UI", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-RPCSS", 0xffffffffffffffff);
            EnableProvider(userModeSession, options, "Microsoft-Windows-COMRuntime", 0x3L, (TraceEventLevel)0xff);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Network-and-Sharing-Center", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-WPDClassInstaller", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Search-Core", 0x1000000000000L, (TraceEventLevel)0xff);
            EnableProvider(userModeSession, options, "e7ef96be-969f-414f-97d7-3ddb7b558ccc", 0x2000L, (TraceEventLevel)0xff);
            EnableProvider(userModeSession, options, "Microsoft-PerfTrack-MSHTML", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-DiagCpl", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-stobject", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-DeviceSetupManager", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Kernel-BootDiagnostics", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Diagnostics-Networking", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-WWAN-CFE", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Immersive-Shell", 0x1000000100000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-AppReadiness", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-PerfTrack-IEFRAME", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-WindowsUpdateClient", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-PortableWorkspaces-Creator-Tool", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-VAN", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Wcmsvc", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Tethering-Manager", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-NetworkGCW", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Netshell", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-ThemeUI", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-DxgKrnl", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Diagnosis-AdvancedTaskManager", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-User-ControlPanel", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Documents", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-PDC", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Shell-AuthUI", 0x1000000000000L);
            EnableProvider(userModeSession, options, "36b6f488-aad7-48c2-afe3-d4ec2c8b46fa", 0x10000L, (TraceEventLevel)0xff);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Dwm-Core", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-ProcessStateManager", 0x1000000000000L, (TraceEventLevel)0xff);
            EnableProvider(userModeSession, options, "Microsoft-Windows-DXP", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-WlanConn", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-UserPnp", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-AppXDeployment-Server", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-MediaEngine", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-HealthCenter", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Ncasvc", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-HomeGroup-ProviderService", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-JScript", 0x1, (TraceEventLevel)0xff);
            EnableProvider(userModeSession, options, "Microsoft-Windows-VolumeControl", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-NWiFi", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-PrimaryNetworkIcon", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-NetworkProfile", 0x1000000000000L);
            EnableProvider(userModeSession, options, ".NET Common Language Runtime", 0x98, TraceEventLevel.Verbose);
            EnableProvider(userModeSession, options, "Microsoft-Windows-IME-TIP", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-IME-TCTIP", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-MediaFoundation-MFCaptureEngine", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-DisplaySwitch", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Shell-LockScreenContent", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-LUA", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-DateTimeControlPanel", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-TabletPC-InputPanel", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-TaskScheduler", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Help", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Audio", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-MediaFoundation-Performance", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-WlanPref", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-UserAccountControl", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Antimalware-Service", 0xffffffffffffffffL, (TraceEventLevel)0xff);
            EnableProvider(userModeSession, options, "Microsoft-Windows-IME-JPTIP", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-WMP", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Antimalware-AMFilter", 0xffffffffffffffffL, (TraceEventLevel)0xff);
            EnableProvider(userModeSession, options, "Microsoft-Windows-WCNWiz", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Graphics-Printing", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-WlanDlg", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Dwm-Udwm", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-ComDlg32", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-DesktopActivityModerator", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-HotspotAuth", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-FileManagerApp", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Dhcp-Client", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Sensors", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Display", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-UxTheme", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-NetworkProvisioning", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-WWAN-SVC-EVENTS", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-WiFiDisplay", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Proximity-Common", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-DxpTaskSyncProvider", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-NCSI", 0x1000000000000L, (TraceEventLevel)0xff);
            EnableProvider(userModeSession, options, "Microsoft-Antimalware-RTP", 0xffffffffffffffff, (TraceEventLevel)0xff);
            EnableProvider(userModeSession, options, "Microsoft-Windows-SrumTelemetry", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-DeviceUx", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Antimalware-Protection", 0xffffffffffffffffL, (TraceEventLevel)0xff);
            EnableProvider(userModeSession, options, "Microsoft-Windows-HealthCenterCPL", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Speech-UserExperience", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-User Profiles Service", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Networking-Correlation", 0xffffffffffffffffL, (TraceEventLevel)0xff);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Store-Client-UI", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-XAML", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Immersive-Shell-API", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-WindowsUIImmersive", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Winlogon", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-UI-Search", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-PrintDialogs", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-BootUX", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-PowerShell", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-SkyDrive-SyncEngine", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-WMPNSS-Service", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Services", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-AltTab", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-ThemeCPL", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Diagnostics-PerfTrack", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-RPC", 0xffffffffffffffffL);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Win32k", 0x1000000402000L, (TraceEventLevel)0xff);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Shell-Core", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-BrokerInfrastructure", 0x1000000000001L, (TraceEventLevel)0xff);
            EnableProvider(userModeSession, options, "Microsoft-Windows-Superfetch", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-DriverFrameworks-UserMode", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-SystemSettings", 0x1000000000000L);
            EnableProvider(userModeSession, options, "Microsoft-Windows-DHCPv6-Client", 0x1000000000000L);
        }

        private void EnableProvider(TraceEventSession userModeSession, TraceEventProviderOptions options, string providerNameOrGuid, ulong keywords, TraceEventLevel level = TraceEventLevel.Informational)
        {
            Guid providerGuid = TraceEventProviders.GetProviderGuidByName(providerNameOrGuid);
            Debug.Assert(providerGuid != Guid.Empty);
            EnableUserProvider(userModeSession, providerNameOrGuid, providerGuid, level, keywords, options);
        }

        public void Stop(CommandLineArgs parsedArgs)
        {
            if (parsedArgs.DataFile == null)
            {
                parsedArgs.DataFile = "PerfViewData.etl";
            }

            // The DataFile does not have the .zip associated with it (it is implied)
            if (parsedArgs.DataFile.EndsWith(".etl.zip", StringComparison.OrdinalIgnoreCase))
            {
                parsedArgs.DataFile = parsedArgs.DataFile.Substring(0, parsedArgs.DataFile.Length - 4);
            }

            LaunchPerfViewElevatedIfNeeded("Stop", parsedArgs);

            if (parsedArgs.DumpHeap)
            {
                // Take a heap snapshot.
                GuiHeapSnapshot(parsedArgs, true);

                // Ensure that we clean up the heap snapshot state.
                parsedArgs.DumpHeap = false;

            }

            LogFile.WriteLine("Stopping tracing for sessions '" + s_KernelessionName +
                "' and '" + s_UserModeSessionName + "'.");

            PerfViewLogger.Log.CommandLineParameters(ParsedArgsAsString(null, parsedArgs), Environment.CurrentDirectory, AppLog.VersionNumber);
            PerfViewLogger.Log.StopTracing();
            PerfViewLogger.StopTime = DateTime.UtcNow;
            PerfViewLogger.Log.StartAndStopTimes();

            // Also log the CPU Counters mapping.
            var osVersion = Environment.OSVersion.Version.Major + Environment.OSVersion.Version.Minor / 10.0;
            if (6.2 <= osVersion)        // CPU Counters only supported on Windows 8 and above
            {
                var cpuCounters = TraceEventProfileSources.GetInfo();
                foreach (var cpuCounter in cpuCounters.Values)
                {
                    if (string.CompareOrdinal(cpuCounter.Name, "Timer") == 0)
                    {
                        continue;
                    }

                    PerfViewLogger.Log.CpuCounterIntervalSetting(cpuCounter.Name, cpuCounter.Interval, cpuCounter.ID);
                    // LogFile.WriteLine("Cpu Counter Config {0} ID {1} Interval {2}", cpuCounter.Name, cpuCounter.Interval, cpuCounter.ID);
                }
            }

            // Try to stop the kernel session

            Task stopKernel = Task.Factory.StartNew(delegate ()
            {
                try
                {
                    if (parsedArgs.KernelEvents != KernelTraceEventParser.Keywords.None)
                    {
                        using (var kernelSession = new TraceEventSession(s_KernelessionName, TraceEventSessionOptions.Attach))
                        {
                            if (parsedArgs.InMemoryCircularBuffer)
                            {
                                LogFile.WriteLine("InMemoryCircularBuffer Set, Dumping kernel log");
                                kernelSession.SetFileName(Path.ChangeExtension(parsedArgs.DataFile, ".kernel.etl")); // Flush the file 


                                LogFile.WriteLine("InMemoryCircularBuffer Set, Doing Kernel Rundown");
                                // We need to manually do a kernel rundown to get the list of running processes and images loaded into memory
                                // Ideally this is done by the SetFileName API so we can avoid merging.  
                                var rundownFile = Path.ChangeExtension(parsedArgs.DataFile, ".kernelRundown.etl");

                                // Note that enabling providers is async, and thus there is a concern that we would lose events if we don't wait 
                                // until the events are logged before shutting down the session.   However we only need the DCEnd events and
                                // those are PART of kernel session stop, which is synchronous (the session will not die until it is complete)
                                // so we don't have to wait after enabling the kernel session.    It is somewhat unfortunate that we have both
                                // the DCStart and the DCStop events, but there does not seem to be a way of asking for just one set.  
                                using (var kernelRundownSession = new TraceEventSession(s_UserModeSessionName + "KernelRundown", rundownFile))
                                {
                                    kernelRundownSession.BufferSizeMB = 256;    // Try to avoid lost events.  
                                    kernelRundownSession.EnableKernelProvider(KernelTraceEventParser.Keywords.Process | KernelTraceEventParser.Keywords.ImageLoad);
                                }
                                LogFile.WriteLine("InMemoryCircularBuffer Set, Finished Kernel Rundown");
                            }
                            kernelSession.Stop();
                        }
                    }
                }
                catch (FileNotFoundException) { LogFile.WriteLine("No Kernel events were active for this trace."); }
                catch (Exception e) { if (!(e is ThreadInterruptedException)) { LogFile.WriteLine("Error stopping Kernel session: " + e.Message); } throw; }
            });

            string dataFile = null;
            Task stopUser = Task.Factory.StartNew(delegate ()
            {
                try
                {
                    using (TraceEventSession clrSession = new TraceEventSession(s_UserModeSessionName, TraceEventSessionOptions.Attach))
                    {
                        if (parsedArgs.InMemoryCircularBuffer)
                        {
                            LogFile.WriteLine("InMemoryCircularBuffer Set, Dumping kernel log");
                            dataFile = parsedArgs.DataFile;
                            clrSession.SetFileName(dataFile);   // Flush the file 
                        }
                        else
                        {
                            dataFile = clrSession.FileName;
                        }

                        clrSession.Stop();

                        // Try to force the rundown of CLR method and loader events.  This routine does not fail.  
                        DoClrRundownForSession(dataFile, clrSession.SessionName, parsedArgs);
                    }
                }
                catch (Exception e) { if (!(e is ThreadInterruptedException)) { LogFile.WriteLine("Error stopping User session: " + e.Message); } throw; }
            });

            Task stopHeap = Task.Factory.StartNew(delegate ()
            {
                try
                {
                    using (var heapSession = new TraceEventSession(s_HeapSessionName, TraceEventSessionOptions.Attach))
                    {
                        heapSession.Stop();
                    }
                }
                catch (FileNotFoundException) { LogFile.WriteLine("No Heap events were active for this trace."); }
                catch (Exception e) { if (!(e is ThreadInterruptedException)) { LogFile.WriteLine("Error stopping Heap session: " + e.Message); } throw; }
            });

            // We stop the two sessions concurrently because we have notice that sometime the kernel session 
            // Takes a while to shutdown, and we want the user mode session to shutdown at basically the same time
            // Doing them concurrently minimizes any skew.  
            Task.WaitAll(stopKernel, stopUser, stopHeap);
            LogFile.WriteLine("Done stopping sessions.");

            UninstallETWClrProfiler(LogFile);

            if (dataFile == null || !File.Exists(dataFile))
            {
                LogFile.WriteLine("Warning: no data generated. (Separate Start and Stop does not work with /InMemoryCircularBuffer)\n");
            }
            else
            {
                parsedArgs.DataFile = dataFile;
                if (parsedArgs.ShouldMerge)
                {
                    Merge(parsedArgs);
                }
            }
            CollectingData = false;

            if (App.CommandLineArgs.StopCommand != null)        // it is a bit of a hack to use the global variable. 
            {
                var commandToRun = App.CommandLineArgs.StopCommand;
                commandToRun = commandToRun.Replace("%OUTPUTDIR%", Path.GetDirectoryName(App.CommandLineArgs.DataFile));
                commandToRun = commandToRun.Replace("%OUTPUTBASENAME%", Path.GetFileNameWithoutExtension(App.CommandLineArgs.DataFile));

                LogFile.WriteLine("Executing /StopCommand: {0}", commandToRun);

                // We are in the wow, so run this in 64 bit if we need 
                var cmdExe = Path.Combine(Environment.GetEnvironmentVariable("SystemRoot"), "SysNative", "Cmd.exe");
                if (!File.Exists(cmdExe))
                {
                    cmdExe = cmdExe.Replace("SysNative", "System32");
                }

                commandToRun = cmdExe + " /c " + commandToRun;
                var cmd = Command.Run(commandToRun, new CommandOptions().AddOutputStream(LogFile).AddNoThrow().AddTimeout(60000));
                if (cmd.ExitCode != 0)
                {
                    LogFile.WriteLine("Error: On Stop command return error code {0}", cmd.ExitCode);
                }

                LogFile.WriteLine("/StopCommand complete {0}", commandToRun);
            }

            // We put this last because it can take a while.  
            DisableNetMonTrace();

            DateTime stopComplete = DateTime.Now;
            LogFile.WriteLine("Stop Completed at {0}", stopComplete);
        }
        public void Mark(CommandLineArgs parsedArgs)
        {
            if (parsedArgs.Message == null)
            {
                parsedArgs.Message = "";
            }

            PerfViewLogger.Log.Mark(parsedArgs.Message);
        }
        public void Abort(CommandLineArgs parsedArgs)
        {
            LaunchPerfViewElevatedIfNeeded("Abort", parsedArgs);
            lock (s_UserModeSessionName)    // Insure only one thread can be aborting at a time.
            {
                if (s_abortInProgress)
                {
                    return;
                }

                s_abortInProgress = true;
                m_logFile.WriteLine("Aborting tracing for sessions '" +
                    s_KernelessionName + "' and '" + s_UserModeSessionName + "'.");
                try
                {
                    using (var kernelSession = new TraceEventSession(s_KernelessionName, TraceEventSessionOptions.Attach))
                    {
                        kernelSession.Stop(true);
                    }
                }
                catch (Exception) { }

                try
                {
                    using (var heapSession = new TraceEventSession(s_HeapSessionName, TraceEventSessionOptions.Attach))
                    {
                        heapSession.Stop(true);
                    }
                }
                catch (Exception) { }

                try
                {
                    using (var userSession = new TraceEventSession(s_UserModeSessionName, TraceEventSessionOptions.Attach))
                    {
                        userSession.Stop(true);
                    }
                }
                catch (Exception) { }

                try
                {
                    using (var gcHeapSession = new TraceEventSession("PerfViewGCHeapSession", TraceEventSessionOptions.Attach))
                    {
                        gcHeapSession.Stop(true);
                    }
                }
                catch (Exception) { }

                try
                {
                    using (var gcHeapSession = new TraceEventSession("PerfViewJSHeapSession", TraceEventSessionOptions.Attach))
                    {
                        gcHeapSession.Stop(true);
                    }
                }
                catch (Exception) { }

                // Insure all the ETWEventTrigger sessions are dead.  
                foreach (var sessionName in TraceEventSession.GetActiveSessionNames())
                {
                    if (sessionName.StartsWith(ETWEventTrigger.SessionNamePrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            using (var triggerSession = new TraceEventSession(sessionName, TraceEventSessionOptions.Attach))
                            {
                                triggerSession.Stop(true);
                            }
                        }
                        catch (Exception) { }
                    }
                }

                // Insure that the rundown session is also stopped. 
                try
                {
                    using (var rundownSession = new TraceEventSession(s_UserModeSessionName + "Rundown", TraceEventSessionOptions.Attach))
                    {
                        rundownSession.Stop(true);
                    }
                }
                catch (Exception) { }
                CollectingData = false;

                try { UninstallETWClrProfiler(LogFile); }
                catch (Exception) { }

                // Insure that network monitoring is off
                try
                {
                    DisableNetMonTrace();
                }
                catch (Exception) { }
            }
        }

        // returns true if this collection is not from the command line.  
        public static bool IsGuiCollection(CommandLineArgs parsedArgs)
        {
            if (parsedArgs.RestartingToElevelate == null)
            {
                return parsedArgs.DoCommand != App.CommandProcessor.Run && parsedArgs.DoCommand != App.CommandProcessor.Collect;
            }
            else
            {
                return parsedArgs.RestartingToElevelate == "";
            }
        }

        public void Merge(CommandLineArgs parsedArgs)
        {
            // If users have not set up a symbol server, don't notify the user.  
            if (parsedArgs.DataFile == null)
            {
                parsedArgs.DataFile = "PerfViewData.etl";
            }

            // Save the original path so that if necessary, we can write the resulting file to it.
            string originalArchivePath = parsedArgs.DataFile;

            if (parsedArgs.ImageIDsOnly)
            {
                // Make a directory to put the symbols in.
                // We may need to unzip the file if this is the second merge (e.g. for container scenarios where we must merge on the host and inside the container).
                UnZipIfNecessary(ref parsedArgs.DataFile, LogFile, unpackInCache: false, wprConventions: false, unpackInSeparateDirectory: true);
                LogFile.WriteLine("Pre-merge file extracted to " + parsedArgs.DataFile);
            }

            LogFile.WriteLine("[Merging data files to " + Path.GetFileName(parsedArgs.DataFile) + ".  Can take 10s of seconds... (can skip if data analyzed on same machine with PerfView)]");
            Stopwatch sw = Stopwatch.StartNew();

            if (!parsedArgs.NoGui && !App.ConfigData.ContainsKey("InformedAboutSkippingMerge"))
            {
                if (IsGuiCollection(parsedArgs))
                {
                    InformedAboutSkippingMerge();
                    App.ConfigData["InformedAboutSkippingMerge"] = "true";
                }
            }

            // Set up the writer parameters.  
            ZippedETLWriter etlWriter = new ZippedETLWriter(parsedArgs.DataFile, LogFile);
            if (parsedArgs.LowPriority)
            {
                etlWriter.LowPriority = true;
            }

            if (parsedArgs.NoRundown || parsedArgs.NoNGenPdbs)
            {
                etlWriter.NGenSymbolFiles = false;
            }

            etlWriter.SymbolReader = App.GetSymbolReader(parsedArgs.DataFile);
            if (!parsedArgs.ShouldZip)
            {
                etlWriter.Zip = false;
            }

            if (parsedArgs.StackCompression)
            {
                etlWriter.CompressETL = true;
            }

            if(parsedArgs.ImageIDsOnly)
            {
                etlWriter.MergeImageIDsOnly = true;

                // If we're only performing image ID injection, make sure to keep any PDBs that were generated in previous merge steps.
                etlWriter.IncludeExistingPDBs = true;
            }

            etlWriter.DeleteInputFile = false;
            if (File.Exists(App.LogFileName))
            {
                etlWriter.AddFile(App.LogFileName, "LogFile.txt");
            }

            // remember the .etlx file that would coorespond to the etl file 
            // that we are about to merge.   It is importnat to do this here
            // before we modify the timestamp for DataFile.  We will use this
            string etlxInCache = CacheFiles.FindFile(parsedArgs.DataFile, ".etlx");
            DateTime etlTimeStamp = File.GetLastWriteTimeUtc(parsedArgs.DataFile);

            // Actually create the archive.  
            var success = etlWriter.WriteArchive();

            // ImageID only merge operations are done in a separate temp directory, so the resulting file
            // must be copied back to the original location so the user can find it.
            if(parsedArgs.ImageIDsOnly)
            {
                // Generate the full path to the destination file.
                string destDir = Path.GetDirectoryName(originalArchivePath);
                string srcFileName = Path.GetFileName(etlWriter.ZipArchivePath);
                string destPath = Path.Combine(destDir, srcFileName);

                // Copy the file from the temp location back to the original file location.
                File.Copy(etlWriter.ZipArchivePath, destPath, overwrite: true);
                LogFile.WriteLine("Final archive copied to " + destPath);
            }

            if (parsedArgs.ShouldZip && !parsedArgs.ImageIDsOnly)
            {
                // The rest of this is an optimization.   If we have ETL or ETLX
                // files for the file we just ZIPPed then set it up so that if
                // we try to open the file it will go down the fast path.  
                if (success)
                {
                    // Move the original ETL file to the cache (so we can reuse it)                
                    var etlInCache = CacheFiles.FindFile(etlWriter.ZipArchivePath, ".etl");
                    FileUtilities.ForceMove(parsedArgs.DataFile, etlInCache);
                    File.SetLastWriteTime(etlInCache, DateTime.Now);   // Touch the file

                    // Move the ETLX file (if any) from the original ETL file 
                    if (File.Exists(etlxInCache) && etlTimeStamp < File.GetLastWriteTimeUtc(etlxInCache))
                    {
                        var newEtlxInCache = CacheFiles.FindFile(etlInCache, ".etlx");
                        FileUtilities.ForceMove(etlxInCache, newEtlxInCache);
                        File.SetLastWriteTime(newEtlxInCache, DateTime.Now + new TimeSpan(1));  // Touch the file ensure it is bigger. 
                    }
                }
                parsedArgs.DataFile = etlWriter.ZipArchivePath;
            }
        }

        public void Unzip(CommandLineArgs parsedArgs)
        {
            LogFile.WriteLine("[Unpacking the file {0}", parsedArgs.DataFile);
            UnZipIfNecessary(ref parsedArgs.DataFile, LogFile, false, parsedArgs.Wpr);
            LogFile.WriteLine("[Unpacked ETL file {0}", parsedArgs.DataFile);
        }

        internal static void UnZipIfNecessary(ref string inputFileName, TextWriter log, bool unpackInCache = true, bool wprConventions = false, bool unpackInSeparateDirectory = false)
        {
            if (inputFileName.EndsWith(".trace.zip", StringComparison.OrdinalIgnoreCase))
            {
                log.WriteLine($"'{inputFileName}' is a linux trace.");
                return;
            }

            var extension = Path.GetExtension(inputFileName);
            if (string.Compare(extension, ".zip", StringComparison.OrdinalIgnoreCase) == 0 ||
                string.Compare(extension, ".vspx", StringComparison.OrdinalIgnoreCase) == 0)
            {
                string unzipedEtlFile;
                if (unpackInCache)
                {
                    unzipedEtlFile = CacheFiles.FindFile(inputFileName, ".etl");
                    if (File.Exists(unzipedEtlFile) && File.GetLastWriteTimeUtc(inputFileName) <= File.GetLastWriteTimeUtc(unzipedEtlFile))
                    {
                        log.WriteLine("Found a existing unzipped file {0}", unzipedEtlFile);
                        inputFileName = unzipedEtlFile;
                        return;
                    }
                }
                else if (unpackInSeparateDirectory)
                {
                    // Compute the separate directory name.
                    unzipedEtlFile = CacheFiles.FindFile(inputFileName);

                    // Delete the directory if it exists.
                    if (Directory.Exists(unzipedEtlFile))
                    {
                        Directory.Delete(unzipedEtlFile, true);
                    }

                    // Create the new directory.
                    Directory.CreateDirectory(unzipedEtlFile);

                    // Add the ETL file name to the destination path.
                    if (inputFileName.EndsWith(".etl.zip", StringComparison.OrdinalIgnoreCase))
                    {
                        string fileName = Path.GetFileName(inputFileName.Substring(0, inputFileName.Length - 4));
                        unzipedEtlFile = Path.Combine(unzipedEtlFile, fileName);
                    }
                }
                else
                {
                    if (inputFileName.EndsWith(".etl.zip", StringComparison.OrdinalIgnoreCase))
                    {
                        unzipedEtlFile = inputFileName.Substring(0, inputFileName.Length - 4);
                    }
                    else if (inputFileName.EndsWith(".vspx", StringComparison.OrdinalIgnoreCase))
                    {
                        unzipedEtlFile = Path.ChangeExtension(inputFileName, ".etl");
                    }
                    else
                    {
                        throw new ApplicationException("File does not end with the .etl.zip file extension");
                    }
                }

                ZippedETLReader etlReader = new ZippedETLReader(inputFileName, log);
                etlReader.EtlFileName = unzipedEtlFile;

                // Figure out where to put the symbols.  
                if (wprConventions)
                {
                    etlReader.SymbolDirectory = Path.ChangeExtension(inputFileName, ".ngenpdb");
                }
                else if(unpackInSeparateDirectory)
                {
                    // Get the target directory name for the ETL file.
                    string destDirectory = Path.GetDirectoryName(unzipedEtlFile);

                    // Append a new symbols directory.
                    destDirectory = Path.Combine(destDirectory, "symbols");

                    // Create the directory.
                    Directory.CreateDirectory(destDirectory);

                    // Set the destination symbols directory.
                    etlReader.SymbolDirectory = destDirectory;
                }
                else
                {
                    var inputDir = Path.GetDirectoryName(inputFileName);
                    if (inputDir.Length == 0)
                    {
                        inputDir = ".";
                    }

                    var symbolsDir = Path.Combine(inputDir, "symbols");
                    if (Directory.Exists(symbolsDir))
                    {
                        etlReader.SymbolDirectory = symbolsDir;
                    }
                    else
                    {
                        etlReader.SymbolDirectory = new SymbolPath(App.SymbolPath).DefaultSymbolCache();
                    }
                }
                log.WriteLine("Putting symbols in {0}", etlReader.SymbolDirectory);

                etlReader.UnpackArchive();
                inputFileName = unzipedEtlFile;
            }
        }

        private void InformedAboutSkippingMerge()
        {
#if !PERFVIEW_COLLECT
            GuiApp.MainWindow.Dispatcher.BeginInvoke((Action)delegate ()
            {
                MessageBox.Show(GuiApp.MainWindow,
                    "If you are analyzing the data on the same machine on which you collected it, in the future " +
                    "you can avoid  the time it takes to merge and zip the file by unchecking the 'merge' checkbox " +
                    "on the collection dialog box.\r\n\r\n" +
                    "Be careful however, PerfView will remember this option from run to run and you will have to " +
                    "either check the zip checkbox or use the PerfView's zip command if you wish to analyze on another machine.\r\n\r\n" +
                    "The WPA analyzer requires merging unconditionally, so you must merge if you wish to use that tool.\r\n\n" +
                    "See the 'Merging' section in the users guide for complete details.",
                    "Skip Merging/Zipping for faster local processing.");
            });
#endif 
        }
        public void GuiRun(CommandLineArgs parsedArgs)
        {
#if !PERFVIEW_COLLECT
            if (GuiApp.MainWindow != null)
            {
                GuiApp.MainWindow.Dispatcher.BeginInvoke((Action)delegate ()
                {
                    GuiApp.MainWindow.DoRun(null, null);
                });
            }
#endif
        }
        public void GuiCollect(CommandLineArgs parsedArgs)
        {
#if !PERFVIEW_COLLECT
            if (GuiApp.MainWindow != null)
            {
                GuiApp.MainWindow.Dispatcher.BeginInvoke((Action)delegate ()
                {
                    GuiApp.MainWindow.DoCollect(null, null);
                });
            }
#endif
        }
        public void View(CommandLineArgs parsedArgs)
        {
            // View does nothing, it is post-command that opens the DataFile
        }

        public void ForceGC(CommandLineArgs parsedArgs)
        {
            var processID = HeapDumper.GetProcessID(parsedArgs.Process);
            if (processID < 0)
            {
                throw new ApplicationException("Could not find a process with a name or ID of '" + parsedArgs.Process + "'");
            }

#if !DOTNET_CORE // perf counters dont exist on .NET Core
            // Support StartOnPerfCounter 
            if (parsedArgs.StartOnPerfCounter != null)
            {
                LogFile.WriteLine("Waiting on a performance counter trigger {0}", parsedArgs.StartOnPerfCounter[0]);
                bool done = false;
                var pcTrigger = new PerformanceCounterTrigger(parsedArgs.StartOnPerfCounter[0], parsedArgs.DecayToZeroHours, LogFile, delegate (PerformanceCounterTrigger trigger)
                {
                    done = true;
                });
                while (!done)
                {
                    Thread.Sleep(10);
                }
            }
#endif
            HeapDumper.ForceGC(processID, LogFile);
        }
        public void HeapSnapshot(CommandLineArgs parsedArgs)
        {
            if (parsedArgs.DataFile == null)
            {
                parsedArgs.DataFile = "PerfViewGCHeap.gcDump";
            }

            parsedArgs.DataFile = Path.ChangeExtension(parsedArgs.DataFile, ".gcdump");

            // we don't clobber files.  
            parsedArgs.DataFile = GetNewFile(parsedArgs.DataFile);

            // Support the /StartOnPerfCounter option.   
            if (parsedArgs.StartOnPerfCounter != null)
            {
                ManualResetEvent collectionCompleted = new ManualResetEvent(false);
                WaitForStart(parsedArgs, collectionCompleted);
            }

            int processID = -1;
            string qualifiers = "";
            if (parsedArgs.SaveETL)
            {
                qualifiers += " /SaveETL /UseEtw";
            }

            if (parsedArgs.DumpData)
            {
                qualifiers += " /DumpData";
            }

            if (parsedArgs.MaxDumpCountK > 0)
            {
                qualifiers += " /MaxDumpCountK=" + parsedArgs.MaxDumpCountK;
            }

            if (parsedArgs.MaxNodeCountK > 0)
            {
                qualifiers += " /MaxNodeCountK=" + parsedArgs.MaxNodeCountK;
            }

            if (parsedArgs.Process != null)
            {
                LogFile.WriteLine("Collecting a GC Heap SnapShot for process {0}", parsedArgs.Process);
                processID = HeapDumper.GetProcessID(parsedArgs.Process);
                if (processID < 0)
                {
                    throw new ApplicationException("Could not find a process with a name or ID of '" + parsedArgs.Process + "'");
                }

                LogFile.WriteLine("[Taking heap snapshot of process '{0}' ID {1} to {2}.  This can take 10s of seconds to minutes.]", parsedArgs.Process, processID, parsedArgs.DataFile);
                LogFile.WriteLine("During the dump the process will be frozen.   If the dump is aborted, the process being dumped will need to be killed.");

                if (parsedArgs.Freeze)
                {
                    qualifiers += " /Freeze";
                }
            }
            else
            {
                LogFile.WriteLine("[Extracting gcHeap from process dump {0} to {1}]", parsedArgs.ProcessDumpFile, parsedArgs.DataFile);
            }

            LogFile.WriteLine("Starting dump at " + DateTime.Now);
            if (processID >= 0)
            {
                HeapDumper.DumpGCHeap(processID, parsedArgs.DataFile, LogFile, qualifiers);
            }
            else
            {
                HeapDumper.DumpGCHeap(parsedArgs.ProcessDumpFile, parsedArgs.DataFile, LogFile, qualifiers);
            }

            LogFile.WriteLine("Finished dump at " + DateTime.Now);

            LogFile.WriteLine("[Done taking snapshot to {0}]", Path.GetFullPath(parsedArgs.DataFile));
        }
        public void HeapSnapshotFromProcessDump(CommandLineArgs parsedArgs)
        {
            HeapSnapshot(parsedArgs);
        }

        public void GuiHeapSnapshot(CommandLineArgs parsedArgs)
        {
            GuiHeapSnapshot(parsedArgs, false);
        }

        public void GuiHeapSnapshot(CommandLineArgs parsedArgs, bool waitForCompletion)
        {
#if !PERFVIEW_COLLECT
            // We'll wait on this handle until the heap snapshot completes.
            ManualResetEvent waitHandle = new ManualResetEvent(false);

            if (GuiApp.MainWindow != null)
            {
                GuiApp.MainWindow.Dispatcher.BeginInvoke((Action)delegate ()
                {
                    GuiApp.MainWindow.TakeHeapShapshot((Action)delegate ()
                    {
                        // Set the wait handle once the memory heap snapshot has been completed.
                        // This action will run in the memory dialog continuation.
                        waitHandle.Set();
                    });
                });
            }

            if (waitForCompletion)
            {
                waitHandle.WaitOne();
            }
#endif
        }

        public void ListCpuCounters(CommandLineArgs parsedArgs)
        {
            var cpuCounters = TraceEventProfileSources.GetInfo();

            LogFile.WriteLine("Cpu Counters available on machine.");
            int ctr = 0;

            LogFile.WriteLine("Source Name                      ID   Current    MinVal    MaxValue");
            LogFile.WriteLine("--------------------------------------------------------------------");
            foreach (var cpuCounter in cpuCounters.Values)
            {
                LogFile.WriteLine("{0,-30} {1,4} {2,9} {3,9} {4,11}", cpuCounter.Name, cpuCounter.ID,
                    cpuCounter.Interval, cpuCounter.MinInterval, cpuCounter.MaxInterval);
                ctr++;
            }
            LogFile.WriteLine();
            LogFile.WriteLine("[{0} Total Profile sources: (See Log)]", ctr);
        }

        public void ListSessions(CommandLineArgs parsedArgs)
        {
            // ListSessions needs to be elevated.  
            LaunchPerfViewElevatedIfNeeded("ListSessions", parsedArgs);

            LogFile.WriteLine("Active Session Names");
            int ctr = 0;
            foreach (string activeSessionName in TraceEventSession.GetActiveSessionNames())
            {
                LogFile.WriteLine("    " + activeSessionName);
                ctr++;
            }
            LogFile.WriteLine("[{0} Total Active sessions: (See Log)]", ctr);
            ShowLog = true;
        }

        public void EnableKernelStacks(CommandLineArgs parsedArgs)
        {
            SetKernelStacks64(true, LogFile);
            ShowLog = true;
        }
        public void DisableKernelStacks(CommandLineArgs parsedArgs)
        {
            SetKernelStacks64(false, LogFile);
            ShowLog = true;
        }
        public void CreateExtensionProject(CommandLineArgs parsedArgs)
        {
#if !PERFVIEW_COLLECT 
            // We do this to avoid a common mistake where people will create extensions on shared copies of perfView.  
            if (PerfViewExtensibility.Extensions.ExtensionsDirectory.StartsWith(@"\\") ||
                PerfViewExtensibility.Extensions.ExtensionsDirectory.StartsWith(SupportFiles.SupportFileDir, StringComparison.OrdinalIgnoreCase))
            {
                throw new ApplicationException("Currently PerView.exe must be a machine-local copy of the EXE.  Copy it locally first.");
            }

            var extensionSrcDir = Path.Combine(PerfViewExtensibility.Extensions.ExtensionsDirectory, parsedArgs.ExtensionName + "Src");
            if (Directory.Exists(extensionSrcDir))
            {
                throw new ApplicationException("The extension directory " + extensionSrcDir + " already exists.");
            }

            Directory.CreateDirectory(extensionSrcDir);

            File.Copy(Path.Combine(SupportFiles.SupportFileDir, @"ExtensionTemplate\Commands.cs"),
                      Path.Combine(extensionSrcDir, "Commands.cs"));

            // Morph Global to be the correct name. 
            var extensionProjName = Path.Combine(extensionSrcDir, parsedArgs.ExtensionName + ".csproj");
            var extensionProjData = File.ReadAllText(Path.Combine(SupportFiles.SupportFileDir, @"ExtensionTemplate\Global.csproj"));
            extensionProjData = Regex.Replace(extensionProjData, "Global", parsedArgs.ExtensionName);
            extensionProjData = Regex.Replace(extensionProjData, "{91DFAE19-098F-4E19-B81D-6CB36A9020D6}",
                Guid.NewGuid().ToString("B").ToUpper());
            extensionProjData = Regex.Replace(extensionProjData, @"\s*<Scc.*", "");
            File.WriteAllText(extensionProjName, extensionProjData);

            var extensionDebugName = extensionProjName + ".user";
            var extensionDebugData = File.ReadAllText(Path.Combine(SupportFiles.SupportFileDir, @"ExtensionTemplate\Global.csproj.user"));
            extensionDebugData = Regex.Replace(extensionDebugData, "<StartProgram>.*</StartProgram>",
                "<StartProgram>" + SupportFiles.MainAssemblyPath + "</StartProgram>");
            extensionDebugData = Regex.Replace(extensionDebugData, "Global", parsedArgs.ExtensionName);
            File.WriteAllText(extensionDebugName, extensionDebugData);
            LogFile.WriteLine("Created new project {0}", extensionProjName);

            // Write out a solution file that combines all existing extensions.  
            var projectFiles = new List<string>();
            foreach (var dirName in Directory.EnumerateDirectories(PerfViewExtensibility.Extensions.ExtensionsDirectory, "*Src", SearchOption.TopDirectoryOnly))
            {
                var shortDirName = Path.GetFileName(dirName);
                var extensionName = shortDirName.Substring(0, shortDirName.Length - 3);   // Remove .src
                var projFile = Path.Combine(dirName, extensionName + ".csproj");
                if (File.Exists(projFile))
                {
                    projectFiles.Add(projFile);
                }
            }

            var extensionsSolnName = Path.Combine(PerfViewExtensibility.Extensions.ExtensionsDirectory, "Extensions.sln");
            LogFile.WriteLine("Updating solution file {0}.", extensionsSolnName);
            CreateSolution(extensionsSolnName, projectFiles);

            LogFile.WriteLine("Launching Visual Studio on {0}.", extensionsSolnName);
            Command.Run(Command.Quote(extensionsSolnName), new CommandOptions().AddStart().AddTimeout(CommandOptions.Infinite));
#endif
        }
        public void UserCommand(CommandLineArgs parsedArgs)
        {
            if (parsedArgs.CommandAndArgs.Length < 1)
            {
                throw new CommandLineParserException("User command missing.");
            }

            LogFile.WriteLine("[Running User Command: {0}]", string.Join(" ", parsedArgs.CommandAndArgs));

            string userCommand = parsedArgs.CommandAndArgs[0];
            var userArgs = new string[parsedArgs.CommandAndArgs.Length - 1];
            Array.Copy(parsedArgs.CommandAndArgs, 1, userArgs, 0, userArgs.Length);
            PerfViewExtensibility.Extensions.ExecuteUserCommand(userCommand, userArgs);
        }
        public void UserCommandHelp(CommandLineArgs parsedArgs)
        {
#if !PERFVIEW_COLLECT
            if (GuiApp.MainWindow != null)
            {
                GuiApp.MainWindow.Dispatcher.BeginInvoke((Action)delegate ()
                {
                    GuiApp.MainWindow.DoUserCommandHelp(null, null);
                });
            }
            else
#endif
            {
                var log = App.CommandProcessor.LogFile;
                log.WriteLine("All User Commands");
                PerfViewExtensibility.Extensions.GenerateHelp(log);
            }

        }

        // Given a path, keeps adding .N. before the extension until you find a new file 
        public static string GetNewFile(string path)
        {
            if (!File.Exists(path))
            {
                return path;
            }

            var extension = Path.GetExtension(path);
            var fileNameBase = path.Substring(0, path.Length - extension.Length);
            if (string.Compare(extension, ".zip", StringComparison.OrdinalIgnoreCase) == 0)
            {
                var nextExt = Path.GetExtension(fileNameBase);
                if (nextExt.Length != 0)
                {
                    extension = nextExt + extension;
                    fileNameBase = path.Substring(0, path.Length - extension.Length);
                }
            }
            // Strip any .N suffix
            var idx = fileNameBase.Length - 1;
            if (0 < idx)
            {
                while (0 < idx && Char.IsDigit(fileNameBase[idx]))
                {
                    --idx;
                }

                if (fileNameBase[idx] == '.')
                {
                    fileNameBase = fileNameBase.Substring(0, idx);
                }
            }

            // Find a unique file by adding .N. before the extension. 
            for (int ctr = 1; ; ctr++)
            {
                var filePath = fileNameBase + "." + ctr.ToString() + extension;
                if (!File.Exists(filePath))
                {
                    return filePath;
                }
            }
        }

#if CROSS_GENERATION_LIVENESS
        public void CollectCrossGenerationLiveness(CommandLineArgs parsedArgs)
        {
            // Validate the input file name.
            string fileName = Path.GetFileName(parsedArgs.CGL_PathToOutputFile);
            if (string.IsNullOrEmpty(fileName) || !parsedArgs.CGL_PathToOutputFile.EndsWith(".gcdump", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Invalid GCDump file path.  The specified path must contain a file name that ends in .gcdump.");
            }

            HeapDumper.DumpGCHeapForCrossGenerationLiveness(
                parsedArgs.CGL_PID,
                parsedArgs.CGL_Generation,
                parsedArgs.CGL_PromotedBytesThreshold,
                parsedArgs.CGL_PathToOutputFile,
                LogFile);

        }
#endif

        #region private
        private void DisableNetMonTrace()
        {
            string netMonFile = Path.Combine(CacheFiles.CacheDir, "NetMonActive.txt");
            if (File.Exists(netMonFile))
            {
                LogFile.WriteLine("Running netsh trace stop command to stop network monitoring.");
                LogFile.WriteLine("If /NetMonCapture is active this can take a while...");

                string commandToRun = "netsh trace stop";
                // We are in the wow, so run this in 64 bit if we need 
                var cmdExe = Path.Combine(Environment.GetEnvironmentVariable("SystemRoot"), "SysNative", "Cmd.exe");
                if (!File.Exists(cmdExe))
                {
                    cmdExe = cmdExe.Replace("SysNative", "System32");
                }

                commandToRun = cmdExe + " /c " + commandToRun;

                Command.Run(commandToRun, new CommandOptions().AddNoThrow().AddOutputStream(LogFile));
                FileUtilities.ForceDelete(netMonFile);
            }
        }

        /// <summary>
        /// Parses cpuCounterSpecs and calls TraceEventSession.SetProfileSources 
        /// Each cpuCounterSpec is NAME:NUM tuple (e.g), for the allowable NAME use 
        /// the ListCpuCounters command.  
        /// </summary>
        private void SetCpuCounters(string[] cpuCounterSpecs)
        {
            var sourceInfos = TraceEventProfileSources.GetInfo();
            var sourceIDs = new int[cpuCounterSpecs.Length];
            var sourceIntervals = new int[cpuCounterSpecs.Length];
            for (int i = 0; i < cpuCounterSpecs.Length; i++)
            {
                var cpuCounterSpec = cpuCounterSpecs[i];

                var m = Regex.Match(cpuCounterSpec, @"(.*?):(\d+)");
                if (!m.Success)
                {
                    throw new ApplicationException("Cpu Counter specifications must be of the form NAME:COUNT.");
                }

                var name = m.Groups[1].Value;
                var count = int.Parse(m.Groups[2].Value);

                if (!sourceInfos.ContainsKey(name))
                {
                    throw new ApplicationException("Cpu Counter " + name + " does not exist.  Use ListCpuCounters for valid values.");
                }

                var sourceInfo = sourceInfos[name];
                if (count < sourceInfo.MinInterval)
                {
                    throw new ApplicationException("Cpu Counter " + name + " has a count that is below the minimum of " + sourceInfo.MinInterval);
                }
                else if (sourceInfo.MaxInterval < count)
                {
                    throw new ApplicationException("Cpu Counter " + name + " has a count that is above the maximum of " + sourceInfo.MaxInterval);
                }

                LogFile.WriteLine("Configuring Cpu Counter (ProfileSource) {0} ID: {1} to Interval {2}", name, sourceInfo.ID, count);
                // Can't log to PerfViewLogger because it is not on yet (kernel session turns on first).  
                sourceIDs[i] = sourceInfo.ID;
                sourceIntervals[i] = count;
            }
            TraceEventProfileSources.Set(sourceIDs, sourceIntervals);
        }

        private void WaitUntilCollectionDone(ManualResetEvent collectionCompleted, CommandLineArgs parsedArgs, DateTime startTime)
        {
            var triggers = new List<Trigger>();
#if !DOTNET_CORE // perf counters dont exist on .NET Core
            var monitors = new List<PerformanceCounterMonitor>();
#endif
            try
            {
#if !DOTNET_CORE  // perf counters dont exist on .NET Core
                if (parsedArgs.StopOnPerfCounter != null)
                {
                    foreach (var perfCounterTrigger in parsedArgs.StopOnPerfCounter)
                    {
                        LogFile.WriteLine("[Enabling StopOnPerfCounter {0}.]", perfCounterTrigger);
                        var perfCtrTrigger = new PerformanceCounterTrigger(perfCounterTrigger, parsedArgs.DecayToZeroHours, LogFile, delegate (PerformanceCounterTrigger trigger)
                        {
                            TriggerStop(collectionCompleted, "StopOnPerfCounter " + perfCounterTrigger + " Triggered.  Value: " + trigger.CurrentValue.ToString("n1"),
                            parsedArgs.DelayAfterTriggerSec);
                        });
                        perfCtrTrigger.MinSecForTrigger = parsedArgs.MinSecForTrigger;
                        triggers.Add(perfCtrTrigger);
                    }
                }
                if (parsedArgs.MonitorPerfCounter != null)
                {
                    foreach (var perfCounterSpec in parsedArgs.MonitorPerfCounter)
                    {
                        monitors.Add(new PerformanceCounterMonitor(perfCounterSpec, LogFile));
                    }
                }
#endif

                if (parsedArgs.StopOnGCOverMsec > 0)
                {
                    LogFile.WriteLine("[Enabling StopOnGCOverMsec {0}.]", parsedArgs.StopOnGCOverMsec);
                    triggers.Add(ETWEventTrigger.GCTooLong(parsedArgs.StopOnGCOverMsec, parsedArgs.DecayToZeroHours, parsedArgs.Process, LogFile, delegate (ETWEventTrigger trigger)
                    {
                        TriggerStop(collectionCompleted, trigger.TriggeredMessage, parsedArgs.DelayAfterTriggerSec);
                    }));
                }

                if (parsedArgs.StopOnBGCFinalPauseOverMsec > 0)
                {
                    LogFile.WriteLine("[Enabling StopOnBGCFinalPauseOverMsec {0}.]", parsedArgs.StopOnBGCFinalPauseOverMsec);
                    triggers.Add(ETWEventTrigger.BgcFinalPauseTooLong(parsedArgs.StopOnBGCFinalPauseOverMsec, parsedArgs.DecayToZeroHours, parsedArgs.Process, LogFile, delegate (ETWEventTrigger trigger)
                    {
                        TriggerStop(collectionCompleted, trigger.TriggeredMessage, parsedArgs.DelayAfterTriggerSec);
                    }));
                }

                if (parsedArgs.StopOnException != null)
                {
                    LogFile.WriteLine("[Enabling StopOnException {0}.]", parsedArgs.StopOnException);
                    triggers.Add(ETWEventTrigger.StopOnException(parsedArgs.StopOnException, parsedArgs.Process, LogFile, delegate (ETWEventTrigger trigger)
                        {
                            TriggerStop(collectionCompleted, trigger.TriggeredMessage, parsedArgs.DelayAfterTriggerSec);
                        }));
                }
                if (parsedArgs.StopOnEtwEvent != null)
                {
                    foreach (string etwTriggerSpec in parsedArgs.StopOnEtwEvent)
                    {
                        LogFile.WriteLine("[Enabling StopOnEtwEvent {0}.]", etwTriggerSpec);
                        triggers.Add(new ETWEventTrigger(etwTriggerSpec, LogFile, delegate (ETWEventTrigger trigger)
                        {
                            TriggerStop(collectionCompleted, trigger.TriggeredMessage, parsedArgs.DelayAfterTriggerSec);
                        }));
                    }
                }
                if (parsedArgs.StopOnGen2GC)
                {
                    LogFile.WriteLine("[Enabling StopOnGen2GC.]");
                    triggers.Add(ETWEventTrigger.StopOnGen2GC(parsedArgs.Process, LogFile, delegate (ETWEventTrigger trigger)
                    {
                        TriggerStop(collectionCompleted, trigger.TriggeredMessage, parsedArgs.DelayAfterTriggerSec);
                    }));
                }

                if (parsedArgs.StopOnAppFabricOverMsec > 0)
                {
                    LogFile.WriteLine("[Enabling StopOnAppFabricOverMSec {0}.]", parsedArgs.StopOnAppFabricOverMsec);
                    triggers.Add(ETWEventTrigger.AppFabricTooLong(parsedArgs.StopOnAppFabricOverMsec, parsedArgs.DecayToZeroHours, parsedArgs.Process, LogFile, delegate (ETWEventTrigger trigger)
                    {
                        TriggerStop(collectionCompleted, trigger.TriggeredMessage, parsedArgs.DelayAfterTriggerSec);
                    }));
                }
#if !DOTNET_CORE // EventLog doesn't exist on .NET Core
                if (parsedArgs.StopOnEventLogMessage != null)
                {
                    LogFile.WriteLine("[Enabling StopOnEventLogMessage with Regex pattern: '{0}'.]", parsedArgs.StopOnEventLogMessage);
                    triggers.Add(new EventLogTrigger(parsedArgs.StopOnEventLogMessage, LogFile, delegate (EventLogTrigger trigger)
                    {
                        TriggerStop(collectionCompleted, "StopOnEventLogMessage triggered.  Message: " + parsedArgs.StopOnEventLogMessage,
                            parsedArgs.DelayAfterTriggerSec);
                    }));
                }
#endif
                var lastStatusTime = startTime;
                LogFile.WriteLine("[Starting collection at {0}]", startTime);
                string startedDropping = "";
                while (!collectionCompleted.WaitOne(200))
                {
                    var now = DateTime.Now;
                    if ((now - lastStatusTime).TotalSeconds > 10)
                    {
                        var status = GetStatusLine(parsedArgs, startTime, ref startedDropping);
                        foreach (var trigger in triggers)
                        {
                            var triggerStatus = trigger.Status;
                            if (triggerStatus.Length != 0)
                            {
                                status = status + " " + triggerStatus;
                            }
                        }
                        LogFile.WriteLine("[" + status + "]");
                        PerfViewLogger.Log.Tick(status);
                        lastStatusTime = now;
                    }

                    if (parsedArgs.MaxCollectSec != 0 && (now - startTime).TotalSeconds > parsedArgs.MaxCollectSec)
                    {
                        TriggerStop(collectionCompleted, "Exceeded MaxCollectSec " + parsedArgs.MaxCollectSec, 0);
                    }
                }

            }
            finally
            {
                if (triggers.Count > 0)
                {
                    LogFile.WriteLine("Turning off monitoring for stop triggers");
                    foreach (Trigger trigger in triggers)
                    {
                        trigger.Dispose();
                    }
                }
#if !DOTNET_CORE // perf counters dont exist on .NET Core
                if (monitors.Count > 0)
                {
                    LogFile.WriteLine("Turning off perf monitoring.");
                    foreach (var monitor in monitors)
                    {
                        monitor.Dispose();
                    }
                }
#endif
            }
        }

        private void TriggerStop(ManualResetEvent collectionCompleted, string message, int waitAfterTriggerSec)
        {
            PerfViewLogger.Log.StopReason(message);
            LogFile.WriteLine("[{0}]", message);
            LogFile.Flush();
            if (waitAfterTriggerSec > 0)
            {
                LogFile.WriteLine("[Trigger fired.  Waiting " + waitAfterTriggerSec + " more seconds (in case something interesting happens) ...]");
                Thread.Sleep(waitAfterTriggerSec * 1000);
                LogFile.WriteLine("[Stopping logging.]");
            }

            collectionCompleted.Set();
        }

        private void SetupWaitGui(ManualResetEvent collectionCompleted, CommandLineArgs parsedArgs)
        {
#if !PERFVIEW_COLLECT
            RunCommandDialog collectWindow = null;

            // Hook up the logic to cause the 'Stop' button to set 'collectionCompleted'.
            GuiApp.MainWindow.Dispatcher.BeginInvoke((Action)delegate ()
            {
                collectWindow = GuiApp.MainWindow.CollectWindow;
                if (collectWindow == null)      // This happens on the command line case and collectMultiple case.  Is this a hack?
                {
                    collectWindow = new RunCommandDialog(parsedArgs, GuiApp.MainWindow, true);
                    collectWindow.StartCollection();
                }

                // This callback gets called when we END collection (same button is used for start and end of collection.  
                collectWindow.OKButton.Click += delegate (object sender, System.Windows.RoutedEventArgs e)
                {
                    // Because ZIP Merge, and NoRundown affect post collection we allow the user to update them 
                    // even after collection has started by only updating the values when the collection has stopped.   
                    // If the user touched the checkboxes, then remember those values for subsequent launches of PerfView.  

                    parsedArgs.Merge = collectWindow.MergeCheckBox.IsChecked;
                    if (collectWindow.m_mergeOrZipCheckboxTouched && parsedArgs.Merge.HasValue)
                    {
                        App.ConfigData["Merge"] = parsedArgs.Merge.Value.ToString();
                    }

                    parsedArgs.Zip = collectWindow.ZipCheckBox.IsChecked;
                    if (collectWindow.m_mergeOrZipCheckboxTouched && parsedArgs.Zip.HasValue)
                    {
                        App.ConfigData["Zip"] = parsedArgs.Zip.Value.ToString();
                    }

                    parsedArgs.NoRundown = !(collectWindow.RundownCheckBox.IsChecked ?? false);
                    int.TryParse(collectWindow.RundownTimeoutTextBox.Text, out parsedArgs.RundownTimeout);
                    TriggerStop(collectionCompleted, "Manually Stopped (Gui)", 0);
                };
                collectWindow.Show();
            });

            // Set up a task that fires when the collection is completed (for any reason) and closes the GUI window.  
            Task.Factory.StartNew(delegate
            {
                collectionCompleted.WaitOne();
                GuiApp.MainWindow.Dispatcher.BeginInvoke((Action)delegate ()
                {
                    if (collectWindow != null)
                    {
                        collectWindow.Close();
                        collectWindow = null;
                    }
                });
            });
#endif
        }

        private void SetupWaitNoGui(ManualResetEvent collectionCompleted, CommandLineArgs parsedArgs)
        {
            Console.WriteLine("");
            if (parsedArgs.NoNGenRundown)
            {
                Console.WriteLine("Pre V4.0 .NET Rundown disabled, Type 'E' to enable symbols for V3.5 processes.");
            }
            else
            {
                Console.WriteLine("Pre V4.0 .NET Rundown enabled, Type 'D' to disable and speed up .NET Rundown.");
            }

#if !PERFVIEW_COLLECT
            Console.WriteLine("Do NOT close this console window.   It will leave collection on!");
#endif
            var consider = "";
            if (parsedArgs.MaxCollectSec == 0)
            {
                consider = "(Also consider /MaxCollectSec:N)";
            }

            Console.WriteLine("Type S to stop collection, 'A' will abort.  {0}", consider);

            Task.Factory.StartNew(delegate
            {
                while (!collectionCompleted.WaitOne(200))
                {
                    if (App.ConsoleCreated && Console.KeyAvailable)
                    {
                        var keyInfo = Console.ReadKey(true);
                        var keyChar = char.ToLower(keyInfo.KeyChar);
                        if (keyInfo.KeyChar == 's')
                        {
                            TriggerStop(collectionCompleted, "Manually Stopped (NoGui)", 0);
                        }
                        else if (keyInfo.KeyChar == 'e')
                        {
                            parsedArgs.NoNGenRundown = false;
                            Console.WriteLine("Pre V4.0 .NET Rundown enabled.");
                        }
                        else if (keyInfo.KeyChar == 'd')
                        {
                            parsedArgs.NoNGenRundown = true;
                            Console.WriteLine("Pre V4.0 .NET Rundown disabled.");
                        }
                        else if (keyInfo.KeyChar == 'a')
                        {
                            Console.WriteLine("Aborting collection...");
                            m_aborted = true;
                            collectionCompleted.Set();
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Returns a status line for the collection that indicates how much data we have collected.  
        /// TODO review, I don't really like this.  
        /// </summary>
        internal static string GetStatusLine(CommandLineArgs parsedArgs, DateTime startTime, ref string startedDropping)
        {
            var durationSec = (DateTime.Now - startTime).TotalSeconds;
            var fileSizeMB = 0.0;
            if (File.Exists(parsedArgs.DataFile))
            {
                bool droppingData = startedDropping.Length != 0;

                fileSizeMB = new FileInfo(parsedArgs.DataFile).Length / 1048576.0;      // MB here are defined as 2^20 
                if (!droppingData && parsedArgs.CircularMB != 0 && parsedArgs.CircularMB <= fileSizeMB)
                {
                    droppingData = true;
                }

                var kernelName = Path.ChangeExtension(parsedArgs.DataFile, ".kernel.etl");
                if (File.Exists(kernelName))
                {
                    var kernelFileSizeMB = new FileInfo(kernelName).Length / 1048576.0;
                    if (!droppingData && parsedArgs.CircularMB != 0 && parsedArgs.CircularMB <= kernelFileSizeMB)
                    {
                        droppingData = true;
                    }

                    fileSizeMB += kernelFileSizeMB;
                }

                if (droppingData && startedDropping.Length == 0)
                {
                    startedDropping = "  Recycling started at " + TimeStr(durationSec) + ".";
                }
            }

            return string.Format("Collecting {0,8}: Size={1,5:n1} MB.{2}", TimeStr(durationSec), fileSizeMB, startedDropping);
        }

        internal static string TimeStr(double durationSec)
        {
            string ret;
            if (durationSec < 60)
            {
                ret = durationSec.ToString("f0") + " sec";
            }
            else if (durationSec < 3600)
            {
                ret = (durationSec / 60).ToString("f1") + " min";
            }
            else if (durationSec < 86400)
            {
                ret = (durationSec / 3600).ToString("f1") + " hr";
            }
            else
            {
                ret = (durationSec / 86400).ToString("f1") + " days";
            }

            return ret;
        }

        private static void CreateSolution(string slnFile, List<string> projectFiles)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("\uFEFF");     // We want a byte order mark;
            sb.AppendLine("Microsoft Visual Studio Solution File, Format Version 11.00");
            sb.AppendLine("# Visual Studio 2010");

            var projectGuids = new List<string>();
            foreach (var projectFile in projectFiles)
            {
                string projectName = Path.GetFileNameWithoutExtension(projectFile);
                string relativeProjectPath = PathUtil.PathRelativeTo(projectFile, Path.GetDirectoryName(slnFile));
                string projectData = File.ReadAllText(projectFile);
                var match = Regex.Match(projectData, "<ProjectGuid>({.*})</ProjectGuid>");
                if (!match.Success)
                {
                    App.CommandProcessor.LogFile.WriteLine("Could not find project guid for {0}", projectFile);
                    continue;
                }
                var projectGuid = match.Groups[1].Value;
                projectGuids.Add(projectGuid);

                sb.AppendLine("Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"" +
                    projectName + "\", \"" + relativeProjectPath + "\", \"" + projectGuid + "\"");
                sb.AppendLine("EndProject");
            }
            sb.AppendLine("Global");
            sb.AppendLine("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution");
            foreach (var projectGuid in projectGuids)
            {
                sb.AppendLine("\t\t" + projectGuid + ".Debug|Any CPU.ActiveCfg = Debug|Any CPU");
                sb.AppendLine("\t\t" + projectGuid + ".Debug|Any CPU.Build.0 = Debug|Any CPU");
                sb.AppendLine("\t\t" + projectGuid + ".Release|Any CPU.ActiveCfg = Release|Any CPU");
                sb.AppendLine("\t\t" + projectGuid + ".Release|Any CPU.Build.0 = Release|Any CPU");
            }
            sb.AppendLine("\tEndGlobalSection");
            sb.AppendLine("EndGlobal");

            File.WriteAllText(slnFile, sb.ToString());
        }

        private static string s_dotNetKey = @"Software\Microsoft\.NETFramework";
        private static string s_dotNetKey32 = @"Software\Wow6432Node\Microsoft\.NETFramework";

        // Insures that our EtwClrProfiler is set up (for both X64 and X86).  Does not actually turn on the provider.  
        private static void InstallETWClrProfiler(TextWriter log, int profilerKeywords)
        {
            log.WriteLine("Insuring that the .NET CLR Profiler is installed.");
            var profilerDll = Path.Combine(SupportFiles.SupportFileDir, SupportFiles.ProcessArchitectureDirectory, "EtwClrProfiler.dll");
            if (File.Exists(profilerDll))
            {
                log.WriteLine("Profiler DLL to load is {0}", profilerDll);
                log.WriteLine(@"Adding HKLM\Software\Microsoft\.NETFramework\COR* registry keys");
                using (RegistryKey key = Registry.LocalMachine.CreateSubKey(s_dotNetKey))
                {
                    InsertEtwClrProfilerKeys(key, "COR", profilerDll, profilerKeywords, SupportFiles.ProcessArch.ToString(), log);
                    InsertEtwClrProfilerKeys(key, "CORECLR", profilerDll, profilerKeywords, SupportFiles.ProcessArch.ToString(), log);
                }
            }
            else
            {
                log.WriteLine("ERROR do not have a ETWClrProfiler.dll for architecture {0}", SupportFiles.ProcessArch);
            }

            // If we are on a 64 bit system (in the wow), also enable the 64 bit version.     
            var nativeArch = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432");
            if (nativeArch != null)
            {
                var profilerNativeDll = Path.Combine(SupportFiles.SupportFileDir, nativeArch + "\\EtwClrProfiler.dll");
                if (File.Exists(profilerNativeDll))
                {
                    log.WriteLine(@"Detected 64 bit system, Adding 64 bit HKLM\Software\Microsoft\.NETFramework\COR* registry keys");
                    using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                    using (RegistryKey key = hklm.CreateSubKey(s_dotNetKey))
                    {
                        InsertEtwClrProfilerKeys(key, "COR", profilerNativeDll, profilerKeywords, nativeArch, log);
                        InsertEtwClrProfilerKeys(key, "CORECLR", profilerNativeDll, profilerKeywords, nativeArch, log);
                    }
                }
                else
                {
                    log.WriteLine("ERROR do not have a ETWClrProfiler.dll for architecture {0}", nativeArch);
                }
            }
            // If we are amd64 process, also install in the 32 bit subsystem.  
            else if (SupportFiles.ProcessArch == ProcessorArchitecture.Amd64)
            {
                var arch = "x86";
                var profilerNativeDll = Path.Combine(SupportFiles.SupportFileDir, arch + "\\EtwClrProfiler.dll");
                if (File.Exists(profilerNativeDll))
                {
                    log.WriteLine(@"Detected 64 bit system, installing in the 32 bit subsystem.");
                    using (RegistryKey key = Registry.LocalMachine.CreateSubKey(s_dotNetKey32))
                    {
                        InsertEtwClrProfilerKeys(key, "COR", profilerNativeDll, profilerKeywords, arch, log);
                        InsertEtwClrProfilerKeys(key, "CORECLR", profilerNativeDll, profilerKeywords, arch, log);
                    }
                }
                else
                {
                    log.WriteLine("ERROR do not have a ETWClrProfiler.dll for architecture {0}", arch);
                }
            }
        }

        /// <summary>
        /// Given a pointer to the .Net registry key area, 'dotnetHive' and a prefix (either COR or CORECLR)
        /// as well as the profilerDll, profilerKeywords, and native architecture, install the profiler DLL
        /// as the EtwClr profiler.  Log status messages to 'log'.  
        /// </summary>
        private static void InsertEtwClrProfilerKeys(RegistryKey dotnetHive, string prefix, string profilerDll, int profilerKeywords, string nativeArch, TextWriter log)
        {
            var existingValue = dotnetHive.GetValue("_PROFILER") as string;
            if (existingValue == null || "{6652970f-1756-5d8d-0805-e9aad152aa84}" == existingValue)
            {
                dotnetHive.SetValue(prefix + "_PROFILER", "{6652970f-1756-5d8d-0805-e9aad152aa84}");
                dotnetHive.SetValue(prefix + "_PROFILER_PATH", profilerDll);
                dotnetHive.SetValue(prefix + "_ENABLE_PROFILING", 1);
                dotnetHive.SetValue("PerfView_Keywords", profilerKeywords);
            }
            else
            {
                log.WriteLine("ERROR there is an existing CLR Profiler arch {0} {1}.", nativeArch, existingValue);
            }
        }

        private static void UninstallETWClrProfiler(TextWriter log)
        {
            log.WriteLine("Insuring .NET Allocation profiler not installed.");

            using (RegistryKey key = Registry.LocalMachine.CreateSubKey(s_dotNetKey))
            {
                DeleteEtwClrProfilerKeys(key, "COR", log);
                DeleteEtwClrProfilerKeys(key, "CORECLR", log);
            }

            var nativeArch = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432");
            if (nativeArch != null)
            {
                log.WriteLine(@"Detected 64 bit system, removing 64 bit keys");
                using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey key = hklm.CreateSubKey(s_dotNetKey))
                {
                    DeleteEtwClrProfilerKeys(key, "COR", log);
                    DeleteEtwClrProfilerKeys(key, "CORECLR", log);
                }
            }
            // If we are amd64 process, also uninstall in the WOW.  
            else if (SupportFiles.ProcessArch == ProcessorArchitecture.Amd64)
            {
                log.WriteLine(@"Detected 64 bit system, removing 32 bit keys.");
                using (RegistryKey key = Registry.LocalMachine.CreateSubKey(s_dotNetKey32))
                {
                    DeleteEtwClrProfilerKeys(key, "COR", log);
                    DeleteEtwClrProfilerKeys(key, "CORECLR", log);
                }
            }
        }

        /// <summary>
        /// Given a pointer to the .Net registry key area, 'dotnetHive' and a prefix (either COR or CORECLR)
        /// delete all the .NET PROfiler keys for the EtwClrProfiler (if present).   Log messages to 'log'.  
        /// </summary>
        private static void DeleteEtwClrProfilerKeys(RegistryKey dotnetHive, string prefix, TextWriter log)
        {
            string existingValue = dotnetHive.GetValue(prefix + "_PROFILER") as string;
            if (existingValue != null)
            {
                if (existingValue == "{6652970f-1756-5d8d-0805-e9aad152aa84}")
                {
                    dotnetHive.DeleteValue(prefix + "_PROFILER", false);
                    dotnetHive.DeleteValue(prefix + "_PROFILER_PATH", false);
                    dotnetHive.DeleteValue(prefix + "_ENABLE_PROFILING", false);
                    dotnetHive.DeleteValue("PerfView_Keywords", false);
                }
                else
                {
                    log.WriteLine("ERROR trying to remove EtwClrProfiler, found an existing Profiler {0} doing nothing.", existingValue);
                }
            }
        }

        private static RegistryKey GetMemManagementKey(bool writable)
        {
            // Open this computer's 64 bit registry (even if this is a 32 bit process. 
            RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default);
            if (hklm == null)
            {
                Debug.Assert(false, "Could not get HKLM key");
                return null;
            }
            RegistryKey memManagment = hklm.OpenSubKey(@"System\CurrentControlSet\Control\Session Manager\Memory Management", writable);
            hklm.Dispose();
            return memManagment;
        }
        private static void SetKernelStacks64(bool crawlable, TextWriter writer)
        {
            // Are we on a 64 bit system? 
            if (!Environment.Is64BitOperatingSystem)
            {
                writer.WriteLine("Disabling kernel paging is only necessary on X64 machines");
                return;
            }

            if (IsKernelStacks64Enabled() == crawlable)
            {
                writer.WriteLine(@"HLKM\" + @"System\CurrentControlSet\Control\Session Manager\Memory Management" + "DisablePagingExecutive" + " already {0}",
                    crawlable ? "set" : "unset");
                return;
            }

            // This is not needed on Windows 8 (mostly)
            var ver = Environment.OSVersion.Version.Major * 10 + Environment.OSVersion.Version.Minor;
            if (ver > 61)
            {
                writer.WriteLine("Disabling kernel paging is not necessary on Win8 machines.");
                return;
            }

            try
            {
                RegistryKey memKey = GetMemManagementKey(true);
                if (memKey != null)
                {
                    memKey.SetValue("DisablePagingExecutive", crawlable ? 1 : 0, RegistryValueKind.DWord);
                    memKey.Dispose();
                    writer.WriteLine();
                    writer.WriteLine("The memory management configuration has been {0} for stack crawling.", crawlable ? "enabled" : "disabled");
                    writer.WriteLine("However a reboot is needed for it to take effect.  You can reboot by executing");
                    writer.WriteLine("     shutdown /r /t 1 /f");
                    writer.WriteLine();
                }
                else
                {
                    writer.WriteLine("Error: Could not access Kernel memory management registry keys.");
                }
            }
            catch (Exception e)
            {
                writer.WriteLine("Error: Failure setting registry keys: {0}", e.Message);
            }
        }
        private static bool IsKernelStacks64Enabled()
        {
            bool ret = false;
            RegistryKey memKey = GetMemManagementKey(false);
            if (memKey != null)
            {
                object valueObj = memKey.GetValue("DisablePagingExecutive", null);
                if (valueObj != null && valueObj is int)
                {
                    ret = ((int)valueObj) != 0;
                }
                memKey.Dispose();
            }
            return ret;
        }

        public void LaunchPerfViewElevatedIfNeeded(string command, CommandLineArgs parsedArgs)
        {
            // Nothing to do if already elevated. 
            if (App.IsElevated)
            {
                return;
            }
            LogFile.WriteLine("Need Elevation, but this process is not Elevated, Relaunching process as Elevated");

#if PERFVIEW_COLLECT
            throw new ApplicationException("PerfViewCollect needs to run with elevated privileges.");
#else
            LaunchPerfViewElevated(command, parsedArgs);
#endif
        }

        public static string ParsedArgsAsString(string command, CommandLineArgs parsedArgs)
        {
            var cmdLineArgs = "";
            if (parsedArgs.DataFile != null)
            {
                cmdLineArgs += " " + Command.Quote("/DataFile:" + parsedArgs.DataFile);
            }

            if (parsedArgs.MinRundownTime != 0)
            {
                cmdLineArgs += " /MinRundownTime:" + parsedArgs.MinRundownTime;
            }

            if (parsedArgs.BufferSizeMB != 64)
            {
                cmdLineArgs += " /BufferSizeMB:" + parsedArgs.BufferSizeMB;
            }

            if (parsedArgs.StackCompression)
            {
                cmdLineArgs += " /StackCompression";
            }

            if (parsedArgs.CircularMB != 0)
            {
                cmdLineArgs += " /CircularMB:" + parsedArgs.CircularMB;
            }

            if (parsedArgs.InMemoryCircularBuffer)
            {
                cmdLineArgs += " /InMemoryCircularBuffer";
            }

            if (parsedArgs.CpuSampleMSec != 1)
            {
                cmdLineArgs += " /CpuSampleMSec:" + parsedArgs.CpuSampleMSec;
            }

            if (parsedArgs.MaxCollectSec != 0)
            {
                cmdLineArgs += " /MaxCollectSec:" + parsedArgs.MaxCollectSec;
            }

            if (parsedArgs.RundownTimeout != 120)
            {
                cmdLineArgs += " /RundownTimeout:" + parsedArgs.RundownTimeout;
            }

            if (parsedArgs.CpuCounters != null)
            {
                cmdLineArgs += " /CpuCounters:" + Command.Quote(string.Join(",", parsedArgs.CpuCounters));
            }

            if (parsedArgs.SafeMode)
            {
                cmdLineArgs += " /SafeMode";
            }

            if (parsedArgs.CollectMultiple != 0)
            {
                cmdLineArgs += " /CollectMultiple:" + parsedArgs.CollectMultiple;
            }

            if (parsedArgs.StopOnPerfCounter != null)
            {
                cmdLineArgs += " /StopOnPerfCounter:" + Command.Quote(string.Join(",", parsedArgs.StopOnPerfCounter));
            }

            if (parsedArgs.MonitorPerfCounter != null)
            {
                cmdLineArgs += " /MonitorPerfCounter:" + Command.Quote(string.Join(",", parsedArgs.MonitorPerfCounter));
            }

            if (parsedArgs.StopOnGen2GC)
            {
                cmdLineArgs += " /StopOnGen2GC";
            }

            if (parsedArgs.StopOnEventLogMessage != null)
            {
                cmdLineArgs += " /StopOnEventLogMessage:" + Command.Quote(parsedArgs.StopOnEventLogMessage);
            }

            if (parsedArgs.StopOnAppFabricOverMsec != 0)
            {
                cmdLineArgs += " /StopOnAppFabricOverMsec:" + parsedArgs.StopOnAppFabricOverMsec;
            }

            if (parsedArgs.StopOnGCOverMsec != 0)
            {
                cmdLineArgs += " /StopOnGcOverMsec:" + parsedArgs.StopOnGCOverMsec;
            }

            if (parsedArgs.StopOnBGCFinalPauseOverMsec != 0)
            {
                cmdLineArgs += " /StopOnBGCFinalPauseOverMsec:" + parsedArgs.StopOnBGCFinalPauseOverMsec;
            }

            if (parsedArgs.StopOnEtwEvent != null)
            {
                cmdLineArgs += " /StopOnEtwEvent:" + Command.Quote(string.Join(",", parsedArgs.StopOnEtwEvent));
            }

            if (parsedArgs.StopOnException != null)
            {
                cmdLineArgs += " /StopOnException:" + Command.Quote(parsedArgs.StopOnException);
            }

            if (parsedArgs.DecayToZeroHours != 0)
            {
                cmdLineArgs += " /DecayToZeroHours:" + parsedArgs.DecayToZeroHours.ToString("f3");
            }

            if (parsedArgs.MinSecForTrigger != 3)
            {
                cmdLineArgs += " /MinSecForTrigger:" + parsedArgs.MinSecForTrigger.ToString();
            }

            if (parsedArgs.DelayAfterTriggerSec != 5)
            {
                cmdLineArgs += " /DelayAfterTriggerSec:" + parsedArgs.DelayAfterTriggerSec;
            }

            if (parsedArgs.KernelEvents != KernelTraceEventParser.Keywords.Default)
            {
                cmdLineArgs += " /KernelEvents:" + parsedArgs.KernelEvents.ToString().Replace(" ", "");
            }

            if (parsedArgs.StartOnPerfCounter != null)
            {
                cmdLineArgs += " /StartOnPerfCounter:" + Command.Quote(string.Join(",", parsedArgs.StartOnPerfCounter));
            }

            if (parsedArgs.Process != null)
            {
                cmdLineArgs += " /Process:" + Command.Quote(parsedArgs.Process);
            }

            if (parsedArgs.StopCommand != null)
            {
                cmdLineArgs += " /StopCommand:" + Command.Quote(parsedArgs.StopCommand);
            }

            if(parsedArgs.EnableEventsInContainers)
            {
                cmdLineArgs += " /EnableEventsInContainers";
            }

            if(parsedArgs.EnableSourceContainerTracking)
            {
                cmdLineArgs += " /EnableSourceContainerTracking";
            }

            if (parsedArgs.ClrEventLevel != Microsoft.Diagnostics.Tracing.TraceEventLevel.Verbose)
            {
                cmdLineArgs += " /ClrEventLevel:" + parsedArgs.ClrEventLevel.ToString();
            }

            if (parsedArgs.ClrEvents != ClrTraceEventParser.Keywords.Default)
            {
                cmdLineArgs += " /ClrEvents:" + parsedArgs.ClrEvents.ToString().Replace(" ", "");
            }

            if(parsedArgs.TplEvents != TplEtwProviderTraceEventParser.Keywords.None)
            {
                cmdLineArgs += " /TplEvents:" + parsedArgs.TplEvents.ToString().Replace(" ", "");
            }

            if (parsedArgs.Providers != null)
            {
                cmdLineArgs += " /Providers:" + Command.Quote(string.Join(",", parsedArgs.Providers));
            }

            if (parsedArgs.KeepAllEvents)
            {
                cmdLineArgs += " /KeepAllEvents";
            }

            if (parsedArgs.UnsafePDBMatch)
            {
                cmdLineArgs += " /UnsafePdbMatch";
            }

            if (parsedArgs.ShowUnknownAddresses)
            {
                cmdLineArgs += " /ShowUnknownAddresses";
            }

            if (parsedArgs.ShowOptimizationTiers)
            {
                cmdLineArgs += " /ShowOptimizationTiers";
            }

            if (parsedArgs.ContinueOnError)
            {
                cmdLineArgs += " /ContinueOnError";
            }

            if (parsedArgs.MaxEventCount != 0)
            {
                cmdLineArgs += " /MaxEventCount:" + parsedArgs.MaxEventCount.ToString();
            }

            if (parsedArgs.SkipMSec != 0)
            {
                cmdLineArgs += " /SkipMSec:" + parsedArgs.SkipMSec.ToString("f3");
            }

            if (parsedArgs.NoGui)
            {
                cmdLineArgs += " /NoGui";
            }

            if (s_UserModeSessionName != "PerfViewSession")
            {
                cmdLineArgs += " /SessionName:" + s_UserModeSessionName;
            }

            if (parsedArgs.LogFile != null)
            {
                cmdLineArgs += " /LogFile:" + Command.Quote(parsedArgs.LogFile);
            }

            if (parsedArgs.NoRundown)
            {
                cmdLineArgs += " /NoRundown";
            }

            if (parsedArgs.FocusProcess != null)
            {
                cmdLineArgs += " /FocusProcess:" + Command.Quote(parsedArgs.FocusProcess);
            }

            if (parsedArgs.NoNGenPdbs)
            {
                cmdLineArgs += " /NoNGenPdbs";
            }

            if (parsedArgs.NoNGenRundown)
            {
                cmdLineArgs += " /NoNGenRundown";
            }

            if (parsedArgs.ForceNgenRundown)
            {
                cmdLineArgs += " /ForceNgenRundown";
            }

            if (parsedArgs.NoClrRundown)
            {
                cmdLineArgs += " /NoClrRundown";
            }

            if (parsedArgs.Merge.HasValue)
            {
                cmdLineArgs += " /Merge:" + parsedArgs.Merge.Value;
            }

            if (parsedArgs.Wpr)
            {
                cmdLineArgs += " /Wpr";
            }

            if (parsedArgs.Zip.HasValue)
            {
                cmdLineArgs += " /Zip:" + parsedArgs.Zip.Value;
            }

            if (parsedArgs.NoView)
            {
                cmdLineArgs += " /NoView";
            }

            if (parsedArgs.GCCollectOnly)
            {
                cmdLineArgs += " /GCCollectOnly";
            }

            if (parsedArgs.DotNetAlloc)
            {
                cmdLineArgs += " /DotNetAlloc";
            }

            if (parsedArgs.DotNetAllocSampled)
            {
                cmdLineArgs += " /DotNetAllocSampled";
            }

            if (parsedArgs.DotNetCalls)
            {
                cmdLineArgs += " /DotNetCalls";
            }

            if (parsedArgs.DotNetCallsSampled)
            {
                cmdLineArgs += " /DotNetCallsSampled";
            }

            if (parsedArgs.DisableInlining)
            {
                cmdLineArgs += " /DisableInlining";
            }

            if (parsedArgs.JITInlining)
            {
                cmdLineArgs += " /JITInlining";
            }

            if (parsedArgs.OSHeapExe != null)
            {
                cmdLineArgs += " /OSHeapExe:" + Command.Quote(parsedArgs.OSHeapExe);
            }

            if (parsedArgs.OSHeapProcess != 0)
            {
                cmdLineArgs += " /OSHeapProcess:" + parsedArgs.OSHeapProcess.ToString();
            }

            if (parsedArgs.NetworkCapture)
            {
                cmdLineArgs += " /NetworkCapture";
            }

            if (parsedArgs.NetMonCapture)
            {
                cmdLineArgs += " /NetMonCapture";
            }

            if (parsedArgs.DumpHeap)
            {
                cmdLineArgs += " /DumpHeap";
            }

            if (parsedArgs.GCOnly)
            {
                cmdLineArgs += " /GCOnly";
            }

            if (parsedArgs.Freeze)
            {
                cmdLineArgs += " /Freeze";
            }

            if (parsedArgs.SaveETL)
            {
                cmdLineArgs += " /SaveETL";
            }

            if (parsedArgs.DumpData)
            {
                cmdLineArgs += " /DumpData";
            }

            if (parsedArgs.MaxDumpCountK != 250)
            {
                cmdLineArgs += " /MaxDumpCountK=" + parsedArgs.MaxDumpCountK;
            }

            if (parsedArgs.MaxNodeCountK != 0)
            {
                cmdLineArgs += " /MaxNodeCountK=" + parsedArgs.MaxNodeCountK;
            }

            if (parsedArgs.CCWRefCount)
            {
                cmdLineArgs += " /CCWRefCount";
            }

            if (parsedArgs.RuntimeLoading)
            {
                cmdLineArgs += " /RuntimeLoading";
            }

            if(parsedArgs.ImageIDsOnly)
            {
                cmdLineArgs += " /ImageIDsOnly";
            }

            // TODO FIX NOW this is sort ugly fix is so that commands are an enum 
            if (command == null)
            {
                command = "";
                if (!string.IsNullOrEmpty(parsedArgs.CommandLine))
                {
                    command = "run";
                }
            }

            cmdLineArgs += " " + command;
            if (string.Compare(command, "run", StringComparison.OrdinalIgnoreCase) == 0)
            {
                cmdLineArgs += " " + parsedArgs.CommandLine;
            }

            if (string.Compare(command, "HeapSnapshot", StringComparison.OrdinalIgnoreCase) == 0)
            {
                cmdLineArgs += " " + Command.Quote(parsedArgs.Process);
            }

            if (string.Compare(command, "HeapSnapshotFromProcessDump", StringComparison.OrdinalIgnoreCase) == 0)
            {
                cmdLineArgs += " " + Command.Quote(parsedArgs.ProcessDumpFile);
            }

            return cmdLineArgs;
        }

        public void LaunchPerfViewElevated(string command, CommandLineArgs parsedArgs)
        {
            Debug.Assert(!App.IsElevated);
            var perfView = SupportFiles.ExePath;

            if (parsedArgs.RestartingToElevelate != null)
            {
                throw new ApplicationException("PerfView has attempted to restart to gain Administrative Permissions but failed to do so.");
            }

            string arg = "";
            if (parsedArgs.DoCommand == App.CommandProcessor.Collect)
            {
                arg = "collect";
            }
            else if (parsedArgs.DoCommand == App.CommandProcessor.Run)
            {
                arg = "run";
            }

            var cmdLine = Command.Quote(perfView) + " /RestartingToElevelate:" + arg + " " + ParsedArgsAsString(command, parsedArgs);
            Command.Run(cmdLine, new CommandOptions().AddStart().AddTimeout(CommandOptions.Infinite).AddElevate());

            // Kill the current version if we have not opened a log file yet.   
            if (!NoExitOnElevate)
            {
                Environment.Exit(0);
            }

            throw new UnauthorizedAccessException("Launching PerfView as an elevated app. Consider closing this instance.");
        }

        /// <summary>
        /// Enable any additional providers specified by 'providerSpecs'.  
        /// </summary>
        private void EnableAdditionalProviders(TraceEventSession userModeSession, string[] providerSpecs, string commandLine, TraceEventProviderOptions options)
        {
            string wildCardFileName = null;
            if (commandLine != null)
            {
                wildCardFileName = Command.FindOnPath(GetExeName(commandLine));
            }

            var parsedProviders = ProviderParser.ParseProviderSpecs(providerSpecs, wildCardFileName, LogFile);
            foreach (var parsedProvider in parsedProviders)
            {
                if (parsedProvider.Level == TraceEventLevel.Always && parsedProvider.MatchAnyKeywords == 0)
                {
                    LogFile.WriteLine("Disabling Provider {0} Guid {1}", parsedProvider.Name, parsedProvider.Guid);
                    userModeSession.DisableProvider(parsedProvider.Guid);
                }
                else
                {
                    CheckAndWarnAboutAspNet(parsedProvider.Guid);
                    EnableUserProvider(userModeSession, parsedProvider.Name, parsedProvider.Guid, parsedProvider.Level,
                        (ulong)parsedProvider.MatchAnyKeywords, parsedProvider.Options ?? options);
                }
            }
        }

        private void CheckAndWarnAboutAspNet(Guid guid)
        {
            if (guid != AspNetTraceEventParser.ProviderGuid)
            {
                return;
            }

            // We turned on the ASP.NET provider, make sure ASP.NET is enabled 
            var iisCorePath = Path.Combine(Environment.GetEnvironmentVariable("WINDIR"), @"System32\inetsrv\iiscore.dll");

            if (!File.Exists(iisCorePath))  // IIS is not installed.  
            {
                LogFile.WriteLine("File {0} does not, ASP.NET is not enabled on the machine", iisCorePath);
                return;
            }
            var iisetwPath = Path.Combine(Environment.GetEnvironmentVariable("WINDIR"), @"System32\inetsrv\iisetw.dll");
            if (File.Exists(iisetwPath))   // Tracing is installed d
            {
                LogFile.WriteLine("File {0} exists, ASP.NET ETW is enabled", iisetwPath);
                return;
            }

            var message = "ASP.NET provider activated but ASP.NET Tracing not installed.\r\n" +
                          "    No ASP.NET events will be created.\r\n" +
                          "    To fix: DISM /online /Enable-Feature /FeatureName:IIS-HttpTracing\r\n" +
                          "    See 'ASP.NET events' in help for more details.";
            LogFile.WriteLine(message);

            if (App.CommandLineArgs.NoGui || SupportFiles.ProcessArch == ProcessorArchitecture.Arm)
            {
                LogFile.WriteLine("[ASP.NET events will not fire, see log for details.]");
                return;
            }

            var warnedAboutAspNetTracing = App.ConfigData["WarnedAboutAspNetTracing"];
            if (warnedAboutAspNetTracing == "true")
            {
                return;
            }

            ShowAspNetWarningBox(message);
            App.ConfigData["WarnedAboutAspNetTracing"] = "true";
        }

        // In its own routine so that we don't run WPF on ARM.  
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void ShowAspNetWarningBox(string message)
        {
#if !PERFVIEW_COLLECT
            // Are we activating with the GUI, then pop a dialog box
            if (App.CommandLineArgs.LogFile == null && GuiApp.MainWindow != null)
            {
                GuiApp.MainWindow.Dispatcher.BeginInvoke((Action)delegate ()
                {
                    MessageBox.Show(GuiApp.MainWindow, message, "Warning ASP.NET Tracing not installed");
                });
            }
#endif
        }

        private void EnableUserProvider(TraceEventSession userModeSession, string providerName, Guid providerGuid,
            TraceEventLevel providerLevel, ulong matchAnyKeywords, TraceEventProviderOptions options)
        {
            var valuesStr = "";
            int stacksEnabled = 0;

            if (options != null)
            {
                if (options.StacksEnabled)
                {
                    stacksEnabled = 1;
                }

                if (options.Arguments != null)
                {

                    foreach (var keyValue in options.Arguments)
                    {
                        if (valuesStr.Length != 0)
                        {
                            valuesStr += ",";
                        }

                        valuesStr += keyValue.Key + "=" + keyValue.Value;
                    }
                }
            }

            if (providerGuid == ClrTraceEventParser.ProviderGuid)
            {
                // ALso turn on the Project N provider
                TraceEventProviderOptions optionsProjectN;
                if (options == null)
                {
                    optionsProjectN = new TraceEventProviderOptions();
                }
                else
                {
                    optionsProjectN = options.Clone();
                }

                // We turn on stacks for the project N provider to get allocation tick events (TODO use event options to limit)
                if (((ClrTraceEventParser.Keywords)matchAnyKeywords & ClrTraceEventParser.Keywords.GC) != 0 && providerLevel >= TraceEventLevel.Verbose)
                {
                    optionsProjectN.StacksEnabled = true;
                }

                EnableUserProvider(userModeSession, "ClrNative", ClrTraceEventParser.NativeProviderGuid, providerLevel, matchAnyKeywords, optionsProjectN);
                PerfViewLogger.Log.ClrEnableParameters(matchAnyKeywords, providerLevel);
            }
            else
            {
                if (providerGuid == ClrPrivateTraceEventParser.ProviderGuid)
                {
                    EnableUserProvider(userModeSession, "ClrPrivateNative", ClrPrivateTraceEventParser.NativeProviderGuid, providerLevel, matchAnyKeywords, options);
                }

                PerfViewLogger.Log.ProviderEnableParameters(providerName, providerGuid, providerLevel, matchAnyKeywords, stacksEnabled, valuesStr);
            }

            // If we turn on verbose for the Microsoft-Windows-IIS provider, go ahead an turn on a bunch of others ones as well.  
            if (providerLevel == TraceEventLevel.Verbose && providerGuid.ToString() == "de4649c9-15e8-4fea-9d85-1cdda520c334")
            {
                EnableUserProvider(userModeSession, "ASP.NET", new Guid("AFF081FE-0247-4275-9C4E-021F3DC1DA35"), TraceEventLevel.Verbose, 0xFFFFFFFF, options);
                EnableUserProvider(userModeSession, "IIS: Active Server Pages (ASP)", new Guid("06B94D9A-B15E-456E-A4EF-37C984A2CB4B"), TraceEventLevel.Verbose, 0xFFFFFFFF, options);
                EnableUserProvider(userModeSession, "IIS: WWW Global", new Guid("D55D3BC9-CBA9-44DF-827E-132D3A4596C2"), TraceEventLevel.Verbose, 0xFFFFFFFF, options);
                EnableUserProvider(userModeSession, "IIS: WWW Isapi Extension", new Guid("A1C2040E-8840-4C31-BA11-9871031A19EA"), TraceEventLevel.Verbose, 0xFFFFFFFF, options);
                EnableUserProvider(userModeSession, "IIS: WWW Server", new Guid("3A2A4E84-4C21-4981-AE10-3FDA0D9B0F83"), TraceEventLevel.Verbose, 0xFFFFFFFE, options);
                EnableUserProvider(userModeSession, "Microsoft-Windows-IIS-WMSVC", new Guid("23108B68-1B7E-43FA-94FB-EC3066805744"), TraceEventLevel.Verbose, ulong.MaxValue, options);
                EnableUserProvider(userModeSession, "Microsoft-Windows-HttpEvent", new Guid("7B6BC78C-898B-4170-BBF8-1A469EA43FC5"), TraceEventLevel.Verbose, ulong.MaxValue, options);
                EnableUserProvider(userModeSession, "Microsoft-Windows-HttpService", new Guid("DD5EF90A-6398-47A4-AD34-4DCECDEF795F"), TraceEventLevel.Verbose, ulong.MaxValue, options);
                EnableUserProvider(userModeSession, "Microsoft-Windows-IIS-APPHOSTSVC", new Guid("CAC10856-9223-48FE-96BA-2A772274FB53"), TraceEventLevel.Verbose, ulong.MaxValue, options);
                EnableUserProvider(userModeSession, "Microsoft-Windows-IIS-FTP", new Guid("AB29F35C-8531-42FF-810D-B8552D23BC92"), TraceEventLevel.Verbose, ulong.MaxValue, options);
                EnableUserProvider(userModeSession, "Microsoft-Windows-IIS-IisMetabaseAudit", new Guid("BBB924B8-F415-4F57-AA45-1007F704C9B1"), TraceEventLevel.Verbose, ulong.MaxValue, options);
                EnableUserProvider(userModeSession, "Microsoft-Windows-IIS-IISReset", new Guid("DA9A85BB-563D-40FB-A164-8E982EA6844B"), TraceEventLevel.Verbose, ulong.MaxValue, options);
                EnableUserProvider(userModeSession, "Microsoft-Windows-IIS-W3SVC", new Guid("05448E22-93DE-4A7A-BBA5-92E27486A8BE"), TraceEventLevel.Verbose, ulong.MaxValue, options);
                EnableUserProvider(userModeSession, "Microsoft-Windows-IIS-W3SVC-PerfCounters", new Guid("90303B54-419D-4081-A683-6DBCB532F261"), TraceEventLevel.Verbose, ulong.MaxValue, options);
                EnableUserProvider(userModeSession, "Microsoft-Windows-IIS-WMSVC", new Guid("23108B68-1B7E-43FA-94FB-EC3066805744"), TraceEventLevel.Verbose, ulong.MaxValue, options);
                EnableUserProvider(userModeSession, "Microsoft-Windows-IIS-W3SVC-WP", new Guid("670080D9-742A-4187-8D16-41143D1290BD"), TraceEventLevel.Verbose, ulong.MaxValue, options);
            }

            LogFile.WriteLine("Enabling Provider:{0} Level:{1} Keywords:0x{2:x} Stacks:{3} Values:{4} Guid:{5}",
                providerName, providerLevel, matchAnyKeywords, stacksEnabled, valuesStr, providerGuid);

            userModeSession.EnableProvider(providerGuid, providerLevel, matchAnyKeywords, options);
        }

        private static string GetExeName(string commandLine)
        {
            Match m = Regex.Match(commandLine, "^\\s*\"(.*?)\"");    // Is it quoted?
            if (!m.Success)
            {
                m = Regex.Match(commandLine, @"\s*(\S*)");           // Nope, then whatever is before the first space.
            }

            return m.Groups[1].Value;
        }

        /// <summary>
        /// Activates the CLR rundown for the user session 'sessionName' with logFile 'fileName'  
        /// </summary>
        private void DoClrRundownForSession(string fileName, string sessionName, CommandLineArgs parsedArgs)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return;
            }

            LogFile.WriteLine("[Sending rundown command to CLR providers...]");
            if (!parsedArgs.NoNGenRundown)
            {
                LogFile.WriteLine("[Use /NoNGenRundown if you don't care about pre V4.0 runtimes]");
            }

            Stopwatch sw = Stopwatch.StartNew();
            TraceEventSession clrRundownSession = null;
            try
            {
                try
                {
                    var rundownFile = Path.ChangeExtension(fileName, ".clrRundown.etl");
                    clrRundownSession = new TraceEventSession(sessionName + "Rundown", rundownFile);

                    clrRundownSession.BufferSizeMB = Math.Max(parsedArgs.BufferSizeMB, 256);

                    TraceEventProviderOptions options = null;
                    if (parsedArgs.FocusProcess != null && TraceEventProviderOptions.FilteringSupported)
                    {
                        options = new TraceEventProviderOptions();
                        int processId;
                        if (Int32.TryParse(parsedArgs.FocusProcess, out processId))
                        {
                            options.ProcessIDFilter = new List<int>(1) { processId };
                            LogFile.WriteLine("**** /FocusProcess specified LIMITING RUNDOWN to process with ID {0}", processId);
                        }
                        else
                        {
                            if (!parsedArgs.FocusProcess.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                LogFile.WriteLine("**** WARNING: process name does not end in .exe, likely you will exclude processes of interest");

                            LogFile.WriteLine("**** /FocusProcess specified LIMITING RUNDOWN to process with name {0}", parsedArgs.FocusProcess);
                            options.ProcessNameFilter = new List<string>(1) { parsedArgs.FocusProcess };
                        }
                    }

                    if(parsedArgs.EnableEventsInContainers || parsedArgs.EnableSourceContainerTracking)
                    {
                        if(options == null)
                        {
                            options = new TraceEventProviderOptions();
                        }

                        if(parsedArgs.EnableEventsInContainers)
                        {
                            options.EnableInContainers = true;
                        }
                        if(parsedArgs.EnableSourceContainerTracking)
                        {
                            options.EnableSourceContainerTracking = true;
                        }
                    }

                    EnableUserProvider(clrRundownSession, "PerfViewLogger", PerfViewLogger.Log.Guid,
                        TraceEventLevel.Verbose, ulong.MaxValue, options);
                    Thread.Sleep(20);       // Give it time to startup 
                    PerfViewLogger.Log.StartRundown();

                    // If full rundown is configured, enable the specified providers.
                    if (!parsedArgs.NoRundown)
                    {
                        if (parsedArgs.Providers != null)
                        {
                            var parsedProviders = ProviderParser.ParseProviderSpecs(parsedArgs.Providers, null, LogFile);
                            foreach (var parsedProvider in parsedProviders)
                            {
                                // turn it on in the Rundown Session, this will dump the manifest into the rundown information. 

                                if (TraceEventProviders.MaybeAnEventSource(parsedProvider.Guid))
                                {
                                    // We don't use 0 for the keywords because that means 'provider default' which is typically
                                    // everything.  Thus we use an obscure keyword we hope is not to volumous (we are really
                                    // relying on the critical event level to filter things).  
                                    EnableUserProvider(clrRundownSession, parsedProvider.Name, parsedProvider.Guid,
                                        TraceEventLevel.Critical, 0x800_0000_0000, options);
                                }
                            }
                        }
                    }

                    if (parsedArgs.ClrEvents != ClrTraceEventParser.Keywords.None)
                    {
                        // Always enable minimal rundown, which ensures that we get the runtime start event.
                        // We use the keyword 0x800000000000 which does not match any valid keyword in the rundown provider.
                        // Choosing 0 results in enabling all keywords based on the logic that checks for keyword status in the runtime.
                        // NOTE: This used to be 0x40000000 which matched the ClrStack keyword in the main provider.  This resulted in
                        // lots of ClrStack/Walk events that couldn't be matched with Clr events, because for V2 rundown, we enable
                        // the Clr provider.
                        var rundownKeywords = (ClrRundownTraceEventParser.Keywords)0x800000000000;

                        // Only consider forcing suppression of these keywords if full rundown is enabled.
                        if (!parsedArgs.NoRundown && !parsedArgs.NoClrRundown)
                        {
                            rundownKeywords = ClrRundownTraceEventParser.Keywords.Default;

                            // If user explicitly suppressed ILToNativeMap then do so on rundown as well. 
                            if ((parsedArgs.ClrEvents & ClrTraceEventParser.Keywords.JittedMethodILToNativeMap) == 0)
                            {
                                rundownKeywords &= ~ClrRundownTraceEventParser.Keywords.JittedMethodILToNativeMap;
                            }

                            if (parsedArgs.ForceNgenRundown)
                            {
                                rundownKeywords &= ~ClrRundownTraceEventParser.Keywords.SupressNGen;
                            }

                            if (parsedArgs.NoNGenRundown)
                            {
                                rundownKeywords &= ~ClrRundownTraceEventParser.Keywords.NGen;
                            }
                        }

                        // The runtime does method rundown first then the module rundown.  This means if you have a large
                        // number of methods and method rundown does not complete you don't get ANYTHING.   To avoid this
                        // we first trigger all module (loader) rundown and then trigger the method rundown
                        if ((rundownKeywords & ClrRundownTraceEventParser.Keywords.Loader) != 0)
                        {
                            EnableUserProvider(clrRundownSession, "CLRRundown", ClrRundownTraceEventParser.ProviderGuid, TraceEventLevel.Verbose,
                                (ulong)(ClrRundownTraceEventParser.Keywords.Loader | ClrRundownTraceEventParser.Keywords.ForceEndRundown), options);
                        }

                        Thread.Sleep(500);                  // Give it some time to complete, so we don't have so many events firing simultaneously.  
                                                            // when we do the method rundown below.  

                        // Enable rundown provider. (we don't do the loader events since we have done them above
                        EnableUserProvider(clrRundownSession, "CLRRundown", ClrRundownTraceEventParser.ProviderGuid, TraceEventLevel.Verbose,
                            (ulong)(rundownKeywords & ~ClrRundownTraceEventParser.Keywords.Loader), options);

                        // For V2.0 runtimes you activate the main provider so we do that too.  
                        if (!parsedArgs.NoV2Rundown)
                        {
                            EnableUserProvider(clrRundownSession, "Clr", ClrTraceEventParser.ProviderGuid,
                                TraceEventLevel.Verbose, (ulong)rundownKeywords, options);
                        }
                    }

                    // Wait for the perfview logger to complete rundown.
                    PerfViewLogger.Log.WaitForIdle();

                    // Wait for rundown to complete.
                    WaitForRundownIdle(parsedArgs.MinRundownTime, parsedArgs.RundownTimeout, rundownFile);

                    // Complete perfview rundown.
                    PerfViewLogger.Log.CommandLineParameters(ParsedArgsAsString(null, parsedArgs), Environment.CurrentDirectory, AppLog.VersionNumber);
                    PerfViewLogger.Log.StartAndStopTimes();
                    PerfViewLogger.Log.StopRundown();

                    // Disable the rundown provider.
                    clrRundownSession.Stop();
                    clrRundownSession = null;
                    sw.Stop();
                    LogFile.WriteLine("CLR Rundown took {0:f3} sec.", sw.Elapsed.TotalSeconds);
                }
                finally
                {
                    if (clrRundownSession != null)
                    {
                        clrRundownSession.Stop();
                    }
                }
            }
            catch (Exception e)
            {
                if (!(e is ThreadInterruptedException))
                {
                    LogFile.WriteLine("Warning: failure during CLR Rundown " + e.Message);
                }

                throw;
            }
        }
        /// <summary>
        /// Currently there is no good way to know when rundown is finished.  We basically wait as long as
        /// the rundown file is growing.  
        /// </summary>
        private void WaitForRundownIdle(int minSeconds, int maxSeconds, string rundownFilePath)
        {
            LogFile.WriteLine("Waiting up to {0} sec for rundown events.  Use /RundownTimeout to change.", maxSeconds);
            LogFile.WriteLine("If you know your process has exited, use /noRundown qualifer to skip this step.");

            long rundownFileLen = 0;
            for (int i = 0; ; i++)
            {
                if (maxSeconds <= i)
                {
                    LogFile.WriteLine("Exceeded maximum rundown wait time of {0} seconds.", maxSeconds);
                    break;
                }
                Thread.Sleep(1000);
                var newRundownFileLen = new FileInfo(rundownFilePath).Length;
                var delta = newRundownFileLen - rundownFileLen;
                LogFile.WriteLine("Rundown File Length: {0:n1}MB delta: {1:n1}MB", newRundownFileLen / 1000000.0, delta / 1000000.0);
                rundownFileLen = newRundownFileLen;

                if (i >= minSeconds)
                {
                    if (delta == 0 && newRundownFileLen != 0)
                    {
                        LogFile.WriteLine("Rundown file has stopped growing, assuming rundown complete.");
                        break;
                    }
                }
            }
        }

        internal static string s_UserModeSessionName = "PerfViewSession";
        internal static string s_KernelessionName = KernelTraceEventParser.KernelSessionName;
        private static string s_HeapSessionName { get { return s_UserModeSessionName + "Heap"; } }

        private static bool s_addedSupportDirToPath;
        private static bool s_abortInProgress;      // We are currently in Abort()

        private TextWriter m_logFile;
        private bool m_aborted;
        #endregion
    }

    /// <summary>
    /// ProviderParser knows how to take a string provider specification and parse it.  
    /// </summary>
    internal static class ProviderParser
    {
        public class ParsedProvider
        {
            public string Name;
            public Guid Guid;
            public TraceEventLevel Level;
            public TraceEventKeyword MatchAnyKeywords;
            public TraceEventProviderOptions Options;
        }

        /// <summary>
        /// TODO FIX NOW document
        /// </summary>
        public static List<ParsedProvider> ParseProviderSpecs(string[] providerSpecs, string wildCardFileName, TextWriter log = null)
        {
            var ret = new List<ParsedProvider>();

            foreach (var providerSpec in providerSpecs)
            {
                if (log != null)
                {
                    log.WriteLine("Parsing ETW Provider Spec: {0}", providerSpec);
                }

                TraceEventProviderOptions options = new TraceEventProviderOptions();
                TraceEventLevel level = TraceEventLevel.Verbose;
                ulong matchAnyKeywords = unchecked((ulong)-1);

                var rest = providerSpec;
                Match m = Regex.Match(rest, @"^([^:]*)(:(.*))?$");
                Debug.Assert(m.Success);
                rest = m.Groups[3].Value;

                // Validate the provider spec
                var providerStr = m.Groups[1].Value;
                if (providerStr == "@" || providerStr.Length == 0 && wildCardFileName != null)
                {
                    if (log != null)
                    {
                        log.WriteLine("No file name provided using {0}", wildCardFileName);
                    }

                    providerStr = "@" + wildCardFileName;
                }

                RETRY:
                // Handle : style keyword, level and stacks description. 
                m = Regex.Match(rest, @"^([^:=]*)(:(.*))?$");
                if (m.Success)
                {
                    var matchAnyKeywordsStr = m.Groups[1].Value;
                    rest = m.Groups[3].Value;
                    if (matchAnyKeywordsStr.Length > 0)
                    {
                        // Hack.  There are some legacy ETW providers that have : in their name.   account for them
                        // This is mostly there for the Provider Browser.  Otherwise I would say they should just 
                        // use the GUID.  
                        if ((providerStr.StartsWith("Active Directory") || providerStr == "IIS" || providerStr == "Security"))
                        {
                            providerStr = providerStr + ":" + matchAnyKeywordsStr;
                            goto RETRY;
                        }
                        if (matchAnyKeywordsStr == "*")
                        {
                            matchAnyKeywords = ulong.MaxValue;
                        }
                        else
                        {
                            matchAnyKeywords = ParseKeywords(matchAnyKeywordsStr, providerStr);
                        }
                    }

                    // handle level 
                    m = Regex.Match(rest, @"^([^:=]*)(:(.*))?$");
                    if (m.Success)
                    {
                        var levelStr = m.Groups[1].Value;
                        rest = m.Groups[3].Value;
                        if (levelStr.Length > 0)
                        {
                            int intLevel;
                            if (levelStr == "*")
                            {
                                level = TraceEventLevel.Verbose;
                            }
                            else if (int.TryParse(levelStr, out intLevel) && 0 <= intLevel && intLevel < 256)
                            {
                                level = (TraceEventLevel)intLevel;
                            }
                            else
                            {
                                try { level = (TraceEventLevel)Enum.Parse(typeof(TraceEventLevel), levelStr); }
                                catch { throw new CommandLineParserException("Could not parse level specification " + levelStr); }
                            }
                        }

                        m = Regex.Match(rest, @"^([^:=]*)(:(.*))?$");
                        if (m.Success)
                        {
                            var stackStr = m.Groups[1].Value;
                            rest = m.Groups[3].Value;
                            if (stackStr == "stack" || stackStr == "stacks")
                            {
                                options.StacksEnabled = true;
                            }
                        }
                    }
                }

                // Handle key-value pairs 
                if (rest.Length > 0)
                {
                    // TODO FIX so that it works with things with commas and colons and equals
                    for (var pos = 0; pos < rest.Length;)
                    {
                        var regex = new Regex(@"\s*(@?\w+)=([^;]*)");
                        var match = regex.Match(rest, pos);
                        if (!match.Success || match.Groups[1].Index != pos)
                        {
                            throw new ApplicationException("Could not parse values '" + rest + "'");
                        }

                        var key = match.Groups[1].Value;
                        var value = match.Groups[2].Value;
                        value = value.Replace(@" \n", " \n");   // Allow escaped newlines in values.   

                        if (key.StartsWith("@"))
                        {
                            if (key == "@StacksEnabled")
                            {
                                options.StacksEnabled = string.Compare(value, "false", StringComparison.OrdinalIgnoreCase) != 0;
                            }
                            else if (key == "@ProcessIDFilter")
                            {
                                options.ProcessIDFilter = ParseIntList(value);
                            }
                            else if (key == "@ProcessNameFilter")
                            {
                                options.ProcessNameFilter = ParseStringList(value);
                            }
                            else if (key == "@EventIDsToEnable")
                            {
                                options.EventIDsToEnable = ParseIntList(value);
                            }
                            else if (key == "@EventIDsToDisable")
                            {
                                options.EventIDsToDisable = ParseIntList(value);
                            }
                            else if (key == "@EventIDStacksToEnable")
                            {
                                options.EventIDStacksToEnable = ParseIntList(value);
                            }
                            else if (key == "@EventIDStacksToDisable")
                            {
                                options.EventIDStacksToDisable = ParseIntList(value);
                            }
                            else
                            {
                                throw new ApplicationException("Unrecognized '@' value '" + key + "'");
                            }
                        }
                        else
                        {
                            options.AddArgument(key, value);
                        }
                        pos += match.Length;
                        if (pos < rest.Length && rest[pos] == ';')
                        {
                            pos++;
                        }
                    }
                }
                ParseProviderSpec(providerStr, level, (TraceEventKeyword)matchAnyKeywords, options, ret, log);
            }
            return ret;
        }

        private static IList<string> ParseStringList(string spaceSeparatedList)
        {
            var ret = new List<string>();
            var ids = spaceSeparatedList.Split(' ');
            foreach (var id in ids)
            {
                if (id.Length == 0)
                {
                    continue;
                }

                ret.Add(id);
            }
            return ret;
        }

        #region private

        private static IList<int> ParseIntList(string spaceSeparatedList)
        {
            var ret = new List<int>();
            var ids = spaceSeparatedList.Split(' ');
            foreach (var id in ids)
            {
                if (id.Length == 0)
                {
                    continue;
                }

                ret.Add(int.Parse(id));
            }
            return ret;
        }
        // Given a provider specification (guid or name or @filename#eventSource return a list of providerGuids for it.  
        private static void ParseProviderSpec(string providerSpec, TraceEventLevel level, TraceEventKeyword matchAnyKeywords,
            TraceEventProviderOptions options, List<ParsedProvider> retList, TextWriter log)
        {
            // Is it a normal GUID 
            Guid providerGuid;
            if (Regex.IsMatch(providerSpec, "........-....-....-....-............"))
            {
                if (!Guid.TryParse(providerSpec, out providerGuid))
                {
                    throw new ApplicationException("Could not parse Guid '" + providerSpec + "'");
                }
            }
            else if (providerSpec.StartsWith("*"))
            {
                // We allow you to specify EventSources without knowing where they came from with the * syntax.  
                providerGuid = TraceEventProviders.GetEventSourceGuidFromName(providerSpec.Substring(1));
            }
            // Is it specially known.  TODO should we remove some of these?
            else if (string.Compare(providerSpec, "Clr", StringComparison.OrdinalIgnoreCase) == 0)
            {
                providerGuid = ClrTraceEventParser.ProviderGuid;
            }
            else if (string.Compare(providerSpec, "ClrRundown", StringComparison.OrdinalIgnoreCase) == 0)
            {
                providerGuid = ClrRundownTraceEventParser.ProviderGuid;
            }
            else if (string.Compare(providerSpec, "ClrStress", StringComparison.OrdinalIgnoreCase) == 0)
            {
                providerGuid = ClrStressTraceEventParser.ProviderGuid;
            }
            else if (string.Compare(providerSpec, "ClrPrivate", StringComparison.OrdinalIgnoreCase) == 0)
            {
                providerGuid = ClrPrivateTraceEventParser.ProviderGuid;
            }
            else if (string.Compare(providerSpec, "ClrNative", StringComparison.OrdinalIgnoreCase) == 0)           // ProjectN
            {
                providerGuid = ClrTraceEventParser.NativeProviderGuid;
            }
            else if (string.Compare(providerSpec, "ClrNativePrivate", StringComparison.OrdinalIgnoreCase) == 0)    // ProjectN Private
            {
                providerGuid = ClrPrivateTraceEventParser.NativeProviderGuid;
            }
            else if (string.Compare(providerSpec, "ASP.Net", StringComparison.OrdinalIgnoreCase) == 0)
            {
                providerGuid = new Guid("AFF081FE-0247-4275-9C4E-021F3DC1DA35");
            }
            else if (string.Compare(providerSpec, "Win32HeapRanges", StringComparison.OrdinalIgnoreCase) == 0)
            {
                providerGuid = new Guid("d781ca11-61c0-4387-b83d-af52d3d2dd6a");
            }
            else if (string.Compare(providerSpec, ".NetTasks", StringComparison.OrdinalIgnoreCase) == 0 ||
                string.Compare(providerSpec, "System.Threading.Tasks.TplEventSource", StringComparison.OrdinalIgnoreCase) == 0)
            {
                providerGuid = new Guid(0x2e5dba47, 0xa3d2, 0x4d16, 0x8e, 0xe0, 0x66, 0x71, 0xff, 220, 0xd7, 0xb5);
            }
            else if (string.Compare(providerSpec, ".NetFramework", StringComparison.OrdinalIgnoreCase) == 0)
            {
                providerGuid = new Guid(0x8e9f5090, 0x2d75, 0x4d03, 0x8a, 0x81, 0xe5, 0xaf, 0xbf, 0x85, 0xda, 0xf1);
            }
            else if (string.Compare(providerSpec, ".NetPLinq", StringComparison.OrdinalIgnoreCase) == 0)
            {
                providerGuid = new Guid(0x159eeeec, 0x4a14, 0x4418, 0xa8, 0xfe, 250, 0xab, 0xcd, 0x98, 120, 0x87);
            }
            else if (string.Compare(providerSpec, ".NetConcurrentCollections", StringComparison.OrdinalIgnoreCase) == 0)
            {
                providerGuid = new Guid(0x35167f8e, 0x49b2, 0x4b96, 0xab, 0x86, 0x43, 0x5b, 0x59, 0x33, 0x6b, 0x5e);
            }
            else if (string.Compare(providerSpec, ".NetSync", StringComparison.OrdinalIgnoreCase) == 0)
            {
                providerGuid = new Guid(0xec631d38, 0x466b, 0x4290, 0x93, 6, 0x83, 0x49, 0x71, 0xba, 2, 0x17);
            }
            else if (string.Compare(providerSpec, "MeasurementBlock", StringComparison.OrdinalIgnoreCase) == 0)
            {
                providerGuid = new Guid("143A31DB-0372-40B6-B8F1-B4B16ADB5F54");
            }
            else if (string.Compare(providerSpec, "CodeMarkers", StringComparison.OrdinalIgnoreCase) == 0)
            {
                providerGuid = new Guid("641D7F6C-481C-42E8-AB7E-D18DC5E5CB9E");
            }
            else if (string.Compare(providerSpec, "Heap Trace Provider", StringComparison.OrdinalIgnoreCase) == 0)
            {
                providerGuid = HeapTraceProviderTraceEventParser.ProviderGuid;
            }
            else
            {
                providerGuid = TraceEventProviders.GetProviderGuidByName(providerSpec);
                // Look it up as an EventSource 
                if (providerGuid == Guid.Empty)
                {
                    providerGuid = TraceEventProviders.GetEventSourceGuidFromName(providerSpec);
                    log.WriteLine("Provider named '{0}' was unknown to the operating system, assuming it is an EventSource with GUID '{1}'",
                        providerSpec, providerGuid);
                    log.WriteLine("WARNING: If {0} is misspelled we are simply using the wrong GUID and the provider will be ignored.", providerSpec);
                }
            }

            retList.Add(new ParsedProvider()
            {
                Name = providerSpec,
                Guid = providerGuid,
                Level = level,
                MatchAnyKeywords = matchAnyKeywords,
                Options = options
            });
        }

        private static ulong ParseKeywords(string matchKeywordString, string providerName)
        {
            Debug.Assert(providerName != null);

            //Creates a dictionary with all the key words for the given provider
            Dictionary<string, ProviderDataItem> keys = new Dictionary<string, ProviderDataItem>();
            var providerGuid = TraceEventProviders.GetProviderGuidByName(providerName);
            if (providerGuid != Guid.Empty)
            {
                foreach (var keyword in TraceEventProviders.GetProviderKeywords(providerGuid))
                {
                    keys.Add(keyword.Name.ToString(), keyword);
                }
            }

            //breaks keywordString into tokens separated by '|' and parse each token
            ulong returnValue = 0;
            string[] keyStrings = matchKeywordString.Split('|');
            foreach (string keyString in keyStrings)
            {
                ulong numberForToken;
                if (keys.ContainsKey(keyString))
                {
                    numberForToken = keys[keyString].Value;
                }
                else
                {
                    string numberKeystring = keyString;
                    if (keyString.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        numberKeystring = keyString.Substring(2);
                    }

                    if (!ulong.TryParse(numberKeystring, System.Globalization.NumberStyles.HexNumber, null, out numberForToken))
                    {
                        throw new CommandLineParserException("Could not parse as a hexadecimal keyword specification " + numberKeystring);
                    }
                }
                returnValue = returnValue | numberForToken;
            }
            return returnValue;
        }
        #endregion
    }

    /// <summary>
    /// EventSourceFinder is a class that can find all the EventSources in a file
    /// </summary>
    internal static class EventSourceFinder
    {
        // TODO remove and depend on framework for these instead.  
        public static Guid GetGuid(Type eventSource)
        {
            foreach (var attrib in CustomAttributeData.GetCustomAttributes(eventSource))
            {
                foreach (var arg in attrib.NamedArguments)
                {
                    if (arg.MemberInfo.Name == "Guid")
                    {
                        var value = (string)arg.TypedValue.Value;
                        return new Guid(value);
                    }
                }
            }

            return TraceEventProviders.GetEventSourceGuidFromName(GetName(eventSource));
        }
        public static string GetName(Type eventSource)
        {
            foreach (var attrib in CustomAttributeData.GetCustomAttributes(eventSource))
            {
                foreach (var arg in attrib.NamedArguments)
                {
                    if (arg.MemberInfo.Name == "Name")
                    {
                        var value = (string)arg.TypedValue.Value;
                        return value;
                    }
                }
            }
            return eventSource.Name;
        }
        public static string GetManifest(Type eventSource)
        {
            // Invoke GenerateManifest
            string manifest = (string)eventSource.BaseType.InvokeMember("GenerateManifest",
                BindingFlags.InvokeMethod | BindingFlags.Static | BindingFlags.Public,
                null, null, new object[] { eventSource, "" });

            return manifest;
        }

        #region private

        private static void GetStaticReferencedAssemblies(Assembly assembly, Dictionary<Assembly, Assembly> soFar)
        {
            soFar[assembly] = assembly;
            string assemblyDirectory = Path.GetDirectoryName(assembly.ManifestModule.FullyQualifiedName);
            foreach (AssemblyName childAssemblyName in assembly.GetReferencedAssemblies())
            {
                try
                {
                    // TODO is this is at best heuristic.  
                    string childPath = Path.Combine(assemblyDirectory, childAssemblyName.Name + ".dll");
                    Assembly childAssembly = null;
                    if (File.Exists(childPath))
                    {
                        childAssembly = Assembly.ReflectionOnlyLoadFrom(childPath);
                    }

                    //TODO do we care about things in the GAC?   it expands the search quite a bit. 
                    //else
                    //    childAssembly = Assembly.Load(childAssemblyName);

                    if (childAssembly != null && !soFar.ContainsKey(childAssembly))
                    {
                        GetStaticReferencedAssemblies(childAssembly, soFar);
                    }
                }
                catch (Exception)
                {
                    Console.WriteLine("Could not load assembly " + childAssemblyName + " skipping.");
                }
            }
        }
        #endregion
    }
}
