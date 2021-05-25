// #define PUBLIC_BUILD
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Diagnostics.Utilities;
using PerfView.Properties;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Utilities;

namespace PerfView
{
    /// <summary>
    /// The App Class is the main program
    ///
    /// We don't do the normal WPF style main (where WPF is responsible for the main program because 
    /// on ARM devices we don't have WPF, however we still want to enable some things (like data collection).   
    /// Thus we need to defer touching WPF until we know we need it.  To do this we take explicit control over
    /// the Entry point and do command line processing first, before doing any GUI stuff.  
    /// </summary>
    public class App
    {
        /// <summary>
        /// At the top most level, Main simply calls 'DoMain' insuring that all error messages get to the user.  
        /// Main is also responsible for doing the 'install On First launch' logic that unpacks the EXE if needed.  
        /// </summary>  
        [System.STAThreadAttribute()]
        // [System.Diagnostics.DebuggerNonUserCodeAttribute()]
        public static int Main(string[] args)
        {
            CommandProcessor = new CommandProcessor();

            StreamWriter writerToCleanup = null;   // If we create a log file, we need to clean it up.  
            int retCode = -1;
            bool newConsoleCreated = false;        // If we create a new console, we need to wait before existing            
            try
            {
#if !PERFVIEW_COLLECT
                // Can't display on ARM because the SplashScreen is WPF
                var noGui = SupportFiles.ProcessArch == ProcessorArchitecture.Arm ||
                    (args.Length > 0 &&
                    (string.Compare(args[0], "/noGui", StringComparison.OrdinalIgnoreCase) == 0 ||
                     string.Compare(args[0], 0, "/logFile", 0, 8, StringComparison.OrdinalIgnoreCase) == 0));

                // If we need to install, display the splash screen early, otherwise wait
                if (!Directory.Exists(SupportFiles.SupportFileDir) && !noGui)
                {
                    DisplaySplashScreen();
                }
#endif
                App.Unpack();                   // Install the program if it is not done already 
                App.RelaunchIfNeeded(args);     // If we are running from a a network share, relaunch locally. 

                // This does the real work
                retCode = DoMain(args, ref newConsoleCreated, ref writerToCleanup);
            }
            catch (ThreadInterruptedException)
            {
                if (App.CommandProcessor.LogFile != null)
                {
                    App.CommandProcessor.LogFile.WriteLine("Thread Aborted by user.");
                }
            }
            catch (Exception e)
            {
                if (App.CommandProcessor.LogFile == null)
                {
                    // This really can only happen when program is buggy, but we still want to display an error message.   
                    newConsoleCreated = CreateConsole();
                    App.CommandProcessor.LogFile = Console.Out;
                }
                bool userLevel;
                string message = ExceptionMessage.GetUserMessage(e, out userLevel);
                App.CommandProcessor.LogFile.WriteLine(message);
            }
            finally
            {
                if (App.CommandProcessor.LogFile != null)
                {
                    App.CommandProcessor.LogFile.Flush();
                }

                if (writerToCleanup != null)
                {
                    writerToCleanup.Dispose();
                }
            }

            // If we created a new console (collect command), prompt before closing it so the user has a chance to look at it.
            if (newConsoleCreated)
            {
                Console.WriteLine("Press enter to close window.");
                Console.ReadLine();
            }

            return retCode;
        }

        /// <summary>
        /// DoMain's job is to parse the args, and determine if we should be using the GUI or the command line, then execute
        /// the command in the appropriate environment.  
        /// </summary>
        /// <param name="args">The arguments to the program</param>
        /// <param name="newConsoleCreated">If we created a new console window (and thus we should wait before closing it on exit, set this)</param>
        /// <param name="textWriterToCleanUp">If you create a StreamWriter, set this so we clean it up on exit.</param>
        /// <returns></returns>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static int DoMain(string[] args, ref bool newConsoleCreated, ref StreamWriter textWriterToCleanUp)
        {
            Triggers.ETWEventTrigger.SessionNamePrefix = CommandProcessor.s_UserModeSessionName + "ETWTrigger";
            CommandLineArgs = new CommandLineArgs();
            if (args.Length > 0)
            {
                CommandLineArgs.ParseArgs(args);   // This routine catches command line parsing exceptions.  (sets CommandLineFailure)
            }

            // Figure out where output goes and set CommandProcessor.LogFile
#if !PERFVIEW_COLLECT
            // On ARM we don't have a GUI
            if (SupportFiles.ProcessArch == ProcessorArchitecture.Arm)
            {
                CommandLineArgs.NoGui = true;
            }
#else
            // We never support GUI in PerfViewCollect
            CommandLineArgs.NoGui = true;
#endif
            // If the operation is to collect, we also need to create a new console
            // even if we already have one (because that console has already 'moved on' and is reading the next command)
            // To get data from that console would be very confusing.  
            var needNewConsole = (CommandLineArgs.DoCommand == CommandProcessor.Collect && CommandLineArgs.MaxCollectSec == 0);

            if (CommandLineArgs.LogFile != null)
            {
                textWriterToCleanUp = new StreamWriter(CommandLineArgs.LogFile, true);
                textWriterToCleanUp.AutoFlush = true;
                CommandProcessor.LogFile = textWriterToCleanUp;
                CommandProcessor.LogFile.WriteLine("PerfView logging started at " + DateTime.Now);
                CommandProcessor.LogFile.Flush();
                CommandLineArgs.NoGui = true;
            }
            else
            {
                if (!CommandLineArgs.NoGui)
                {
                    // Collect uses the GUI as well as View.  
                    if (!(CommandLineArgs.DoCommand == null ||
                        CommandLineArgs.DoCommand == CommandProcessor.Collect ||
                        CommandLineArgs.DoCommand == CommandProcessor.Run ||
                        CommandLineArgs.DoCommand == CommandProcessor.View ||
                        CommandLineArgs.DoCommand == CommandProcessor.UserCommand ||
                        CommandLineArgs.DoCommand == CommandProcessor.UserCommandHelp ||
                        CommandLineArgs.DoCommand == CommandProcessor.GuiRun ||
                        CommandLineArgs.DoCommand == CommandProcessor.GuiCollect ||
                        CommandLineArgs.DoCommand == CommandProcessor.GuiHeapSnapshot))
                    {
                        CommandLineArgs.NoGui = true;
                    }
                }

                if (CommandLineArgs.NoGui)
                {
                    newConsoleCreated = CreateConsole();
                    CommandProcessor.LogFile = Console.Out;
                }
            }

            // Check for a common mistake (misspelling a command name) 
            if (CommandLineArgs.DoCommand == App.CommandProcessor.View && CommandLineArgs.DataFile != null &&
                CommandLineArgs.DataFile.IndexOf('.') < 0 && CommandLineArgs.DataFile.IndexOf('\\') < 0)
            {
                throw new ApplicationException("Error " + CommandLineArgs.DataFile + " not a perfView command.");
            }

#if !PERFVIEW_COLLECT
            // Check for error where you have a TraceEvent dll in the wrong place.
            var traceEventDllPath = typeof(TraceEvent).Assembly.ManifestModule.FullyQualifiedName;
            if (!traceEventDllPath.StartsWith(SupportFiles.SupportFileDir, StringComparison.OrdinalIgnoreCase))
            {
                var correctTraceEventDll = Path.Combine(SupportFiles.SupportFileDir, "Microsoft.Diagnostics.Tracing.TraceEvent.dll");
                using (var fileInUse = new PEFile.PEFile(traceEventDllPath))
                using (var correctFile = new PEFile.PEFile(correctTraceEventDll))
                {
                    if (fileInUse.Header.TimeDateStampSec != correctFile.Header.TimeDateStampSec)
                    {
                        throw new ApplicationException("Error using the wrong Microsoft.Diagnostics.Tracing.TraceEvent.dll.\r\n" +
                            "   You cannot place a version of Microsoft.Diagnostics.Tracing.TraceEvent.dll next to PerfView.exe.");
                    }
                }
            }
#endif

            // The 64 bit version of some of the native DLLs we use are built against the new Win10 libraries and will 
            // fail to bind if they are loaded on an older OS (it would probably work for Win8 but do we care?) 
            // It also can work if we only do viewing operations (msdia* uses old libraries), but again do we care?  
            // If we do we can move this to before the DLLs are loaded.   
            // Give the user a clean error.   
            if (Environment.Is64BitProcess && Environment.OSVersion.Version.Major * 10 + Environment.OSVersion.Version.Minor < 62)
            {
                throw new ApplicationException("The PerfView64 does not work properly Windows version < 10\r\n" +
                    "    Please use the 32 bit version (PerfView.exe).");
            }

            // For reasons I have not dug into SetFileName does not work if you attach to a session.  Warn the user.  
            if (CommandLineArgs.InMemoryCircularBuffer && CommandLineArgs.DoCommand == CommandProcessor.Start)
            {
                throw new ApplicationException("Error: InMemoryCircularBuffer currently can't be used with separate Start and Stop processes.");
            }

            if (CommandLineArgs.NoGui)
            {
                if (SupportFiles.ProcessArch != ProcessorArchitecture.Arm)
                {
                    CloseSplashScreen();
                }

                if (needNewConsole && !newConsoleCreated)
                {
                    newConsoleCreated = CreateConsole();
                }

                if (CommandLineArgs.CommandLineFailure != null)
                {
                    CommandProcessor.LogFile.WriteLine("Error: " +
                        CommandLineArgs.CommandLineFailure.Message + "\r\n" + "Use -? for help.");
                    return -2;
                }
                if (CommandLineArgs.HelpRequested)
                {
                    CommandProcessor.LogFile.Write(CommandLineArgs.GetHelpString(80));
                    return 0;
                }

                if (CommandLineArgs.DoCommand == null || CommandLineArgs.DoCommand == CommandProcessor.View)
                {
#if !PERFVIEW_COLLECT // THese messages are confusing for PerfViewCollect given that it does not support View.  
                    if (CommandLineArgs.DataFile != null)
                    {
                        CommandProcessor.LogFile.WriteLine("Trying to view {0}", CommandLineArgs.DataFile);
                    }
                    else
                    {
                        CommandProcessor.LogFile.WriteLine("No command given, Trying to open viewer.");
                    }
#endif
                    CommandProcessor.LogFile.WriteLine("Use 'PerfView collect' or 'PerfView HeapSnapshot' to collect data.");
                    return -4;
                }

                if (NeedsEulaConfirmation(CommandLineArgs))
                {
                    // Send it both the the log file and the console window.   
                    Console.WriteLine("The PerfView EULA has not been accepted.  Use /AcceptEULA to accept.");
                    CommandProcessor.LogFile.WriteLine("The PerfView EULA has not been accepted.  Use /AcceptEULA to accept.");
                    return -3;
                }

                // For these commands, redirect most of the log to a file.  
                if (CommandLineArgs.LogFile == null && (
                    CommandLineArgs.DoCommand == CommandProcessor.Start ||
                    CommandLineArgs.DoCommand == CommandProcessor.Stop ||
                    CommandLineArgs.DoCommand == CommandProcessor.Run ||
                    CommandLineArgs.DoCommand == CommandProcessor.Merge ||
                    CommandLineArgs.DoCommand == CommandProcessor.UserCommand ||
                    CommandLineArgs.DoCommand == CommandProcessor.Collect))
                {
                    string verboseLogName;
                    if (CommandLineArgs.DataFile == null)
                    {
                        verboseLogName = "PerfViewData.log.txt";
                    }
                    else
                    {
                        verboseLogName = Path.ChangeExtension(CommandLineArgs.DataFile, "log.txt");
                    }

                    App.LogFileName = verboseLogName;
                    CommandProcessor.LogFile.WriteLine("VERBOSE LOG IN: {0}", verboseLogName);
                    if (CommandLineArgs.DoCommand != CommandProcessor.Collect)
                    {
                        CommandProcessor.LogFile.WriteLine("Use /LogFile:FILE  to redirect output entirely.");
                    }

                    TextWriter verboseLog;
                    if (CommandLineArgs.DoCommand == CommandProcessor.Stop)
                    {
                        verboseLog = File.AppendText(verboseLogName);
                    }
                    else
                    {
                        verboseLog = File.CreateText(verboseLogName);
                    }

                    CommandProcessor.LogFile = new VerboseLogWriter(verboseLog, CommandProcessor.LogFile);
                }
                else
                {
                    App.LogFileName = CommandLineArgs.LogFile;
                }

                var allArgs = string.Join(" ", args);
                CommandProcessor.LogFile.WriteLine("[EXECUTING: PerfView {0}]", allArgs);
                // Since we are not doing the GUI, just do the command directly
                var retCode = CommandProcessor.ExecuteCommand(CommandLineArgs);
                CommandProcessor.LogFile.WriteLine("[DONE {0} {1}: PerfView {2}]", DateTime.Now.ToString("HH:mm:ss"),
                    retCode == 0 ? "SUCCESS" : "FAIL", allArgs);
                return retCode;
            }
            else
            {
                // Ask the gui to do the command.   This is in its own method so that on ARM we never try to load WPF.  
                DoMainForGui();
                return 0;       // Does not actually return but 
            }
        }

        /// <summary>
        /// Logic in DoMainForGui was segregated into its own method so that we don't load WPF until we need to (for ARM)
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void DoMainForGui()
        {
#if !PERFVIEW_COLLECT
            DisplaySplashScreen();          // If we have not already displayed the splash screen do it now.  
            s_splashScreen = null;          // this serves no purpose any more.  
            var app = new PerfView.GuiApp();
            app.Run();
#endif
        }

        // ConfigData
        public static string ConfigDataFileName
        {
            get
            {
                if (s_ConfigDataName == null)
                {
                    s_ConfigDataName = Path.Combine(SupportFiles.SupportFileDirBase, "UserConfig.xml");
                }

                return s_ConfigDataName;
            }
        }
        public static ConfigData ConfigData
        {
            get
            {
                if (s_ConfigData == null)
                {
                    s_ConfigData = new ConfigData(ConfigDataFileName, autoWrite: true);
                }

                return s_ConfigData;
            }
        }
        public static void WriteConfig() { ConfigData.Write(ConfigDataFileName); }

        // Logfile
        /// <summary>
        /// Returns the name of the log file that is saved for any perfView instance.   It is the /LogFile parameter if given,
        /// otherwise it is a generate name in the Temp\PerfView directory.   
        /// </summary>
        public static string LogFileName
        {
            get
            {
                if (s_LogFileName == null)
                {
                    if (CommandLineArgs.LogFile != null)
                    {
                        s_LogFileName = CommandLineArgs.LogFile;
                    }
                    else
                    {
                        var uniq = "";
                        for (int i = 1; ; i++)
                        {
                            s_LogFileName = Path.Combine(CacheFiles.CacheDir, "PerfViewLogFile" + uniq + ".txt");
                            if (FileUtilities.TryDelete(s_LogFileName))
                            {
                                break;
                            }

                            uniq = "." + i.ToString();
                        }
                    }
                }
                return s_LogFileName;
            }
            set
            {
                Debug.Assert(s_LogFileName == null);
                s_LogFileName = value;
            }
        }
        private static string s_LogFileName;

        // CommandLine processing
        public static CommandLineArgs CommandLineArgs;
        public static CommandProcessor CommandProcessor;

        /// <summary>
        /// Unpacks all the support files associated with this program.   
        /// </summary>
        public static bool Unpack()
        {
            var unpacked = SupportFiles.UnpackResourcesIfNeeded();
            if (unpacked)
            {
                // We store the tutorial.cs source as a .cs.txt file so that the browser will load it properly.
                // But we also need is original non .txt suffix so that source code fetching will find it.  
                var tutorial = Path.Combine(SupportFiles.SupportFileDir, "tutorial.cs");
                var tutorialTxt = Path.Combine(SupportFiles.SupportFileDir, "tutorial.cs.txt");
                if (File.Exists(tutorialTxt))
                {
                    File.Copy(tutorialTxt, tutorial);
                }

                if (Environment.OSVersion.Platform != PlatformID.Unix)
                {
                    // You don't need amd64 on ARM (TODO remove it on X86 machines too).  
                    if (SupportFiles.ProcessArch == ProcessorArchitecture.Arm)
                    {
                        DirectoryUtilities.Clean(Path.Combine(SupportFiles.SupportFileDir, "amd64"));
                    }

                    // We have two versions of HeapDump.exe, and they each need their own copy of  Microsoft.Diagnostics.Runtime.dll 
                    // so copy this dll to the other architectures.  
                    var fromDir = Path.Combine(SupportFiles.SupportFileDir, "x86");
                    foreach (var arch in new string[] { "amd64", "arm" })
                    {
                        var toDir = Path.Combine(SupportFiles.SupportFileDir, arch);
                        var fromFile = Path.Combine(fromDir, "Microsoft.Diagnostics.Runtime.dll");
                        if (Directory.Exists(toDir) && File.Exists(fromFile))
                        {
                            File.Copy(fromFile, Path.Combine(toDir, "Microsoft.Diagnostics.Runtime.dll"));

                            // ARM can use the X86 version of the heap dumper.  
                            if (arch == "arm")
                            {
                                File.Copy(Path.Combine(fromDir, "HeapDump.exe"), Path.Combine(toDir, "HeapDump.exe"));
                            }
                        }
                    }

                    // To support intellisense for extensions, we need the PerfView.exe to be next to the .XML file that describes it
                    var targetExe = Path.Combine(SupportFiles.SupportFileDir, Path.GetFileName(SupportFiles.MainAssemblyPath));
                    if (!File.Exists(targetExe))
                    {
                        File.Copy(SupportFiles.MainAssemblyPath, targetExe);
                        // This file indicates that we need to copy the extensions if we use this EXE to run from
                        File.WriteAllText(Path.Combine(SupportFiles.SupportFileDir, "ExtensionsNotCopied"), "");
                    }

                    // The KernelTraceControl that works for Win10 and above does not work properly form older OSes
                    // The symptom is that when collecting data, it does not properly merge files and you don't get
                    // the KernelTraceControl events for PDBs and thus symbol lookup does not work.  
                    var version = Environment.OSVersion.Version.Major * 10 + Environment.OSVersion.Version.Minor;
                    if (version < 62)
                    {
                        var kernelTraceControlDir = Path.Combine(SupportFiles.SupportFileDir, "x86");
                        var src = Path.Combine(kernelTraceControlDir, "KernelTraceControl.Win61.dll");
                        var dest = Path.Combine(kernelTraceControlDir, "KernelTraceControl.dll");
                        FileUtilities.ForceCopy(src, dest);
                    }

                    SetPermissionsForWin8Apps();
                }
            }
            return unpacked;
        }

        /// <summary>
        /// This code is separated into its own method because it uses Command, which is in TraceEvent.dll, which was
        /// unpacked in the  previous step.   
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void SetPermissionsForWin8Apps()
        {
            // Are we on Win8 or above
            var version = Environment.OSVersion.Version.Major * 10 + Environment.OSVersion.Version.Minor;
            if (version >= 62)
            {
                // Make sure that Win8 packages can get at the EtwClrProfiler dll.   
                // *S-1-15-2-1 == "ALL APPLICATION PACKAGES" we don't use the text because it does not work in other locales 
                var cmdLine = "icacls.exe \"" + SupportFiles.SupportFileDir + "\" /grant *S-1-15-2-1:(OI)(CI)(RX) /T";
                var cmd = Command.Run(cmdLine, new CommandOptions().AddNoThrow());
                Debug.Assert(cmd.ExitCode == 0);

                // Also grant *S-1-1-0 = everyone read access (so that ASP.NET users can get at the ETW profiler DLL. 
                cmdLine = "icacls.exe \"" + SupportFiles.SupportFileDir + "\" /grant *S-1-1-0:(OI)(CI)(RX) /T";
                cmd = Command.Run(cmdLine, new CommandOptions().AddNoThrow());
                Debug.Assert(cmd.ExitCode == 0);
            }
        }

        /// <summary>
        /// This code is only needed because we let people run PerfView.exe from a network file 
        /// share and we want to update the file share easily.  Currently windows makes this
        /// problematic (you can't update without forcibly closing the file).  Our solution is 
        /// to copy the EXE locally and run it there, so there is only a very short time where
        /// perfView is actually being run from the network.  
        /// 
        /// TODO this can be removed when we don't update PerfView commonly and are willing 
        /// to 'kick people off' when we need to do an update.  
        /// </summary>
        public static void RelaunchIfNeeded(string[] args)
        {
            // TODO FIX NOW.  should I use 'args' rather than CommandLine
            var cmdLine = Environment.CommandLine;
            // Give up if we are running as a script, as it is likely waiting on the result
            if (cmdLine.IndexOf("/logFile=", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return;
            }

            // Is the EXE on a network share 
            var exe = Assembly.GetEntryAssembly().ManifestModule.FullyQualifiedName;
            if (!exe.StartsWith(@"\\"))
            {
                return;
            }

            // This is redundant, but insures that we don't get into infinite loops during testing. 
            if (exe.StartsWith(SupportFiles.SupportFileDir, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // We have unpacked.  
            Debug.Assert(Directory.Exists(SupportFiles.SupportFileDir));
            try
            {
                bool updatedExe = false;
                var targetExe = Path.Combine(SupportFiles.SupportFileDir, Path.GetFileName(exe));
                if (!File.Exists(targetExe) || File.GetLastWriteTimeUtc(targetExe) != File.GetLastWriteTimeUtc(exe))
                {
                    Directory.CreateDirectory(SupportFiles.SupportFileDir);
                    File.Copy(exe, targetExe);
                    updatedExe = true;
                }

                // Unpacking can create the EXE file but it does not do extensions (because they are not 
                // needed if you don't launch PerfView from a file share).  It will however create a
                // ExtentionNotCopied file to mark the fact that it is just the EXE so we can do this quick check.  
                var extensionsCopiedFile = Path.Combine(SupportFiles.SupportFileDir, "ExtensionsNotCopied");
                if (updatedExe || File.Exists(extensionsCopiedFile))
                {
                    var pdbFile = Path.ChangeExtension(exe, ".pdb");
                    if (File.Exists(pdbFile))
                    {
                        File.Copy(pdbFile, Path.ChangeExtension(targetExe, ".pdb"));
                    }

                    // Copy TraceEvent.pdb if you can.  
                    var srcTraceEventPdb = Path.Combine(Path.GetDirectoryName(exe), "TraceEvent.pdb");
                    if (File.Exists(srcTraceEventPdb))
                    {
                        var dstTraceEventPdb = Path.Combine(SupportFiles.SupportFileDir, SupportFiles.ProcessArchitectureDirectory, "TraceEvent.pdb");
                        File.Copy(srcTraceEventPdb, dstTraceEventPdb);
                    }

                    //Copy any PerfViewExtensions stuff
                    var extensionsDir = Path.Combine(Path.GetDirectoryName(exe), "perfViewExtensions");
                    if (Directory.Exists(extensionsDir))
                    {
                        var dstExtensionsDir = Path.Combine(SupportFiles.SupportFileDir, "perfViewExtensions");
                        DirectoryUtilities.Clean(dstExtensionsDir);
                        DirectoryUtilities.Copy(extensionsDir, dstExtensionsDir, SearchOption.TopDirectoryOnly);
                    }

                    // Indicate that we have the extensions directory copied.  
                    FileUtilities.ForceDelete(extensionsCopiedFile);
                }

                var m = System.Text.RegularExpressions.Regex.Match(cmdLine, "^\\s*\"(.*?)\"\\s*(.*)");
                if (!m.Success)
                {
                    m = System.Text.RegularExpressions.Regex.Match(cmdLine, @"\s*(\S*)\s*(.*)");
                }

                // Can't use Command class because that lives in TraceEvent and have not set up our assembly resolve event. 
                var perfView = new Process();
                perfView.StartInfo.FileName = targetExe;
                perfView.StartInfo.Arguments = m.Groups[2].Value;
                perfView.Start();
                Environment.Exit(0);
            }
            catch (Exception) { }
        }

        // Legal stuff (EULA)
        public static bool NeedsEulaConfirmation(CommandLineArgs commandLineArgs)
        {
            var acceptedVersion = App.ConfigData["EULA_Accepted"];
            if (acceptedVersion != null && acceptedVersion == EulaVersion)
            {
                return false;
            }

            // Did the accept the EULA by command line argument?
            if (App.CommandLineArgs.AcceptEULA)
            {
                App.ConfigData["EULA_Accepted"] = EulaVersion;
                return false;
            }

            // Internal users implicitly accept the EULA
            if (AppLog.InternalUser)
            {
                return false;
            }

            return true;
        }
        public static void AcceptEula()
        {
            App.ConfigData["EULA_Accepted"] = EulaVersion;
            // It will auto-update the user state.  
        }

        /// <summary>
        /// True if the process is elevated. 
        /// </summary>
        public static bool IsElevated
        {
            get
            {
                if (!s_IsElevatedInited)
                {
                    s_IsElevated = TraceEventSession.IsElevated() ?? false;
                    s_IsElevatedInited = true;
                }
                return s_IsElevated;
            }
        }

        // Global symbol and source paths
        public static string SymbolPath
        {
            get
            {
                if (m_SymbolPath == null)
                {
                    // Start with _NT_SYMBOL_PATH
                    var symPath = new SymbolPath(Microsoft.Diagnostics.Symbols.SymbolPath.SymbolPathFromEnvironment);

                    // Add any path that we had on previous runs.  
                    var savedPath = App.ConfigData["_NT_SYMBOL_PATH"];
                    if (savedPath != null)
                    {
                        symPath.Add(savedPath);
                    }

                    bool persistSymPath = true;

                    // If we still don't have anything, add a default one
                    // Since the default goes off machine, if we are outside of Microsoft, we have to ask
                    // the user for permission. 
                    if (AppLog.InternalUser)
                    {
                        symPath.Add("SRV*http://symweb.corp.microsoft.com");
                        symPath.Add(Microsoft.Diagnostics.Symbols.SymbolPath.MicrosoftSymbolServerPath);
                    }
                    else if (symPath.Elements.Count == 0)
                    {
                        if (SupportFiles.ProcessArch == ProcessorArchitecture.Arm || App.CommandLineArgs.NoGui)
                        {
                            App.CommandProcessor.LogFile.WriteLine("WARNING NO _NT_SYMBOL_PATH set ...");
                            persistSymPath = false;     // If we could not interact with the user, don't persist the answer.  
                        }
                        else
                        {
                            if (UserOKWithSymbolServerGui())
                            {
                                symPath.Add(Microsoft.Diagnostics.Symbols.SymbolPath.MicrosoftSymbolServerPath);
                            }
                        }
                    }

                    // TODO FIX NOW we will end up with both internal and external symbol servers on the symbol path.  
                    // Should we clean that up?

                    // Remember it.  
                    if (persistSymPath)
                    {
                        SymbolPath = symPath.InsureHasCache(symPath.DefaultSymbolCache()).CacheFirst().ToString();
                    }
                }
                return m_SymbolPath;
            }
            set
            {
                m_SymbolPath = value;
                if (App.ConfigData["_NT_SYMBOL_PATH"] != m_SymbolPath)
                {
                    App.ConfigData["_NT_SYMBOL_PATH"] = m_SymbolPath;
                }
            }
        }
        public static string SourcePath
        {
            get
            {
                if (m_SourcePath == null)
                {
                    var symPath = new SymbolPath(Environment.GetEnvironmentVariable("_NT_SOURCE_PATH"));
                    var savedPath = App.ConfigData["_NT_SOURCE_PATH"];
                    if (savedPath != null)
                    {
                        symPath.Add(savedPath);
                    }

                    // Remember it.  
                    SourcePath = symPath.ToString();
                }
                return m_SourcePath;
            }
            set
            {
                m_SourcePath = value;
                App.ConfigData["_NT_SOURCE_PATH"] = value;
            }
        }

        /// <summary>
        /// A SymbolReader contains all the context (symbol path, symbol lookup preferences ...) needed
        /// to look up PDB files needed to give events in the TraceLog symbolic names.  Note that by 
        /// default this symbol path includes directories relative to the TraceLog (the directory and 
        /// a 'Symbols' directory next to the file).  
        /// </summary>
        public static SymbolReader GetSymbolReader(string etlFilePath = null, SymbolReaderOptions symbolFlags = SymbolReaderOptions.None)
        {
            var log = App.CommandProcessor.LogFile;
            SymbolPath symPath = new SymbolPath(App.SymbolPath);
            if ((symbolFlags & SymbolReaderOptions.CacheOnly) != 0)
            {
                symPath = new SymbolPath("SRV*" + symPath.DefaultSymbolCache());
            }

            var sourcePath = App.SourcePath;
            string localSymDir = symPath.DefaultSymbolCache();
            if (etlFilePath != null)
            {
                // Add the directory where the file resides and a 'symbols' subdirectory 
                var filePathDir = Path.GetDirectoryName(etlFilePath);
                if (filePathDir.Length != 0)
                {
                    // Then the directory where the .ETL file lives. 
                    symPath.Insert(filePathDir);

                    // If there is a 'symbols' directory next to the data file, look for symbols there
                    // as well.   Note that we also put copies of any symbols here as well (see below)
                    string potentiallocalSymDir = Path.Combine(filePathDir, "symbols");
                    if (Directory.Exists(potentiallocalSymDir))
                    {
                        symPath.Insert(potentiallocalSymDir);
                        symPath.Insert("SRV*" + potentiallocalSymDir);
                        localSymDir = potentiallocalSymDir;
                    }

                    // WPR conventions add any .etl.ngenPDB directory to the path too.   has higher priority still. 
                    var wprSymDir = etlFilePath + ".NGENPDB";
                    if (Directory.Exists(wprSymDir))
                    {
                        symPath.Insert("SRV*" + wprSymDir);
                    }
                    else
                    {
                        // I have now seen both conventions .etl.ngenpdb and .ngenpdb, so look for both.  
                        wprSymDir = Path.ChangeExtension(etlFilePath, ".NGENPDB");
                        if (Directory.Exists(wprSymDir))
                        {
                            symPath.Insert("SRV*" + wprSymDir);
                        }
                    }
                    // VS uses .NGENPDBS as a convention.  
                    wprSymDir = etlFilePath + ".NGENPDBS";
                    if (Directory.Exists(wprSymDir))
                    {
                        symPath.Insert("SRV*" + wprSymDir);
                    }

                    if (!string.IsNullOrWhiteSpace(sourcePath))
                    {
                        sourcePath += ";";
                    }

                    sourcePath += filePathDir;
                    var srcDir = Path.Combine(filePathDir, "src");
                    if (Directory.Exists(srcDir))
                    {
                        sourcePath += ";" + srcDir;
                    }
                }
            }
            // Add the Support Files directory so that you get the tutorial example
            if (!string.IsNullOrWhiteSpace(sourcePath))
            {
                sourcePath += ";";
            }

            sourcePath += SupportFiles.SupportFileDir;

            // Can we use the cached symbol reader?
            if (s_symbolReader != null)
            {
                s_symbolReader.SourcePath = sourcePath;
                if (symbolFlags == SymbolReaderOptions.None && s_symbolReader.SymbolPath == symPath.ToString())
                {
                    return s_symbolReader;
                }

                s_symbolReader.Dispose();
                s_symbolReader = null;
            }

            log.WriteLine("Symbol reader _NT_SYMBOL_PATH= {");
            foreach (var element in symPath.Elements)
            {
                log.WriteLine("    {0};", element.ToString());
            }

            log.WriteLine("    }");
            log.WriteLine("This can be set using the File -> Set Symbol Path dialog on the Stack Viewer.");
            SymbolReader ret = new SymbolReader(log, symPath.ToString());
            ret.SourcePath = sourcePath;
            ret.Options = symbolFlags;

#if !PERFVIEW_COLLECT
            if (!AppLog.InternalUser && !App.CommandLineArgs.TrustPdbs)
            {
                ret.SecurityCheck = delegate (string pdbFile)
                {
                    var result = System.Windows.MessageBox.Show("Found " + pdbFile + " in a location that may not be trustworthy, do you trust this file?",
                        "Security Check", System.Windows.MessageBoxButton.YesNo);
                    return result == System.Windows.MessageBoxResult.Yes;
                };
            }
            else
#endif
            {
                ret.SecurityCheck = (pdbFile => true);
            }
            ret.SourceCacheDirectory = Path.Combine(CacheFiles.CacheDir, "src");
            if (localSymDir != null)
            {
                ret.OnSymbolFileFound += (pdbPath, pdbGuid, pdbAge) => CacheInLocalSymDir(localSymDir, pdbPath, pdbGuid, pdbAge, log);
            }

            if (symbolFlags == SymbolReaderOptions.None)
            {
                s_symbolReader = ret;
            }

            return ret;
        }

        #region private
        /// <summary>
        /// This routine gets called every time we find a PDB.  We copy any PDBs to 'localPdbDir' if it is not
        /// already there.  That way every PDB that is needed is locally available, which is a nice feature.  
        /// We log any action we take to 'log'.  
        /// </summary>
        private static void CacheInLocalSymDir(string localPdbDir, string pdbPath, Guid pdbGuid, int pdbAge, TextWriter log)
        {
            // We do this all in a fire-and-forget task so that it does not block the User.   It is 
            // optional after all.  
            Task.Factory.StartNew(delegate ()
            {
                try
                {
                    var fileName = Path.GetFileName(pdbPath);
                    if (pdbGuid != Guid.Empty)
                    {
                        var pdbPathPrefix = Path.Combine(localPdbDir, fileName);
                        // There is a non-trivial possibility that someone puts a FILE that is named what we want the dir to be.  
                        if (File.Exists(pdbPathPrefix))
                        {
                            // If the pdb path happens to be the SymbolCacheDir (a definite possibility) then we would
                            // clobber the source file in our attempt to set up the target.  In this case just give up
                            // and leave the file as it was.  
                            if (string.Compare(pdbPath, pdbPathPrefix, StringComparison.OrdinalIgnoreCase) == 0)
                            {
                                return;
                            }

                            log.WriteLine("Removing file {0} from symbol cache to make way for symsrv files.", pdbPathPrefix);
                            File.Delete(pdbPathPrefix);
                        }
                        localPdbDir = Path.Combine(pdbPathPrefix, pdbGuid.ToString("N") + pdbAge.ToString());
                    }

                    if (!Directory.Exists(localPdbDir))
                    {
                        Directory.CreateDirectory(localPdbDir);
                    }

                    var localPdbPath = Path.Combine(localPdbDir, fileName);
                    var fileExists = File.Exists(localPdbPath);
                    if (!fileExists || File.GetLastWriteTimeUtc(localPdbPath) != File.GetLastWriteTimeUtc(pdbPath))
                    {
                        if (fileExists)
                        {
                            log.WriteLine("WARNING: overwriting existing file {0}.", localPdbPath);
                        }

                        log.WriteLine("Copying {0} to local cache {1}", pdbPath, localPdbPath);
                        // Do it as a copy and a move so that the update is atomic.  
                        var newLocalPdbPath = localPdbPath + ".new";
                        FileUtilities.ForceCopy(pdbPath, newLocalPdbPath);
                        FileUtilities.ForceMove(newLocalPdbPath, localPdbPath);
                    }
                }
                catch (Exception e)
                {
                    log.WriteLine("Error trying to update local PDB cache {0}", e.Message);
                }
            });
        }

        // Display the splash screen (if it is not already displayed).  
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void DisplaySplashScreen()
        {
#if !PERFVIEW_COLLECT
            try
            {
                if (s_splashScreen == null)
                {
                    var splashScreen = new System.Windows.SplashScreen("splashscreen.png");
                    s_splashScreen = new WeakReference(splashScreen);
                    splashScreen.Show(true);
                }
            }
            catch (Exception) { }
#endif
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void CloseSplashScreen()
        {
#if !PERFVIEW_COLLECT
            if (s_splashScreen != null)
            {
                var splashScreen = (System.Windows.SplashScreen)s_splashScreen.Target;
                s_splashScreen = null;
                if (splashScreen != null)
                {
                    splashScreen.Close(new TimeSpan(0));
                }
            }
#endif
        }
#if !PERFVIEW_COLLECT
        private static WeakReference s_splashScreen;
#endif
        private const string EulaVersion = "1";
        private static ConfigData s_ConfigData;
        private static string s_ConfigDataName;

        private static bool s_IsElevated;
        private static bool s_IsElevatedInited;

        private static SymbolReader s_symbolReader;

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool UserOKWithSymbolServerGui()
        {
#if !PERFVIEW_COLLECT
            // Ask the user if it is OK to use the Microsoft symbol server.  
            var done = false;
            var ret = false;
            if (GuiApp.MainWindow == null || GuiApp.MainWindow.Dispatcher == null)
            {
                return ret;
            }

            // We are on the GUI thread, we can just open the dialog 
            if (GuiApp.MainWindow.Dispatcher.CheckAccess())
            {
                var emptyPathDialog = new PerfView.Dialogs.EmptySymbolPathDialog(GuiApp.MainWindow);
                emptyPathDialog.Owner = GuiApp.MainWindow;
                emptyPathDialog.ShowDialog();
                ret = emptyPathDialog.UseMSSymbols;
            }
            else
            {
                // We are not on the GUI thread, we have to do BeginInvoke to get there.  

                // TODO this is a bit of a hack.  I really want the 'current' StackWindow 
                GuiApp.MainWindow.Dispatcher.BeginInvoke((Action)delegate ()
                {
                    try
                    {
                        var emptyPathDialog = new PerfView.Dialogs.EmptySymbolPathDialog(GuiApp.MainWindow);
                        emptyPathDialog.Owner = GuiApp.MainWindow;
                        emptyPathDialog.ShowDialog();
                        ret = emptyPathDialog.UseMSSymbols;
                    }
                    finally
                    {
                        done = true;
                    }
                });

                // Yuk, spin until the dialog box is closed.  
                while (!done)
                {
                    System.Threading.Thread.Sleep(100);
                }
            }
            return ret;
#else
            return true;
#endif
        }
        private static string m_SymbolPath;
        private static string m_SourcePath;

        #region CreateConsole
        [System.Runtime.InteropServices.DllImport("kernel32", SetLastError = true)]
        private extern static int AllocConsole();
        [System.Runtime.InteropServices.DllImport("kernel32", SetLastError = true)]
        private extern static IntPtr GetStdHandle(int nStdHandle);

        private const int UNIVERSAL_NAME_INFO_LEVEL = 1;
        [System.Runtime.InteropServices.DllImport("mpr")]
        private static extern unsafe int WNetGetUniversalNameW(char* localPath, int infoLevel, void* buffer, ref int bufferSize);

        /// <summary>
        /// Convert a network drive (e.g. Z:\testing) to its universal name (e.g. \\clrmain\public\testing).   
        /// </summary>
        public static unsafe string MakeUniversalIfPossible(string path)
        {
            byte[] buffer = new byte[256 * 2 + 4];
            fixed (char* pathPtr = path)
            fixed (byte* ptr = buffer)
            {
                int retSize = buffer.Length;
                var hr = WNetGetUniversalNameW(pathPtr, UNIVERSAL_NAME_INFO_LEVEL, ptr, ref retSize);

                if (hr == 0)
                {
                    char** outString = (char**)ptr;
                    path = new string(*outString);
                }
            }
            return path;
        }

        /// <summary>
        /// Tries to fetch the console that created this process or creates a new one if the parent process has no 
        /// console.   Returns true if a NEW console has been created.  
        /// </summary>
        internal static bool ConsoleCreated;

        private static bool CreateConsole()
        {
            ConsoleCreated = true;
#if DOTNET_CORE
            return false;  // If you return true, it indicates at NEW console was created (this is not true for PerfViewCollect)
#else
            // TODO AttachConsole is not reliable (GetStdHandle returns an invalid handle about half the time)
            // So I have given up on it, I always create a new console
            AllocConsole();

            IntPtr stdHandle = GetStdHandle(-11);       // Get STDOUT
            var safeFileHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdHandle, true);
            Thread.Sleep(100);
            FileStream fileStream;
            try
            {
                fileStream = new FileStream(safeFileHandle, FileAccess.Write);
            }
            catch (System.IO.IOException)
            {
                return false;       // This will simply fail.  
            }

            var encoding = System.Text.Encoding.GetEncoding(437);   // MSDOS Code page.  
            StreamWriter standardOutput = new StreamWriter(fileStream, encoding);
            standardOutput.AutoFlush = true;
            Console.SetOut(standardOutput);
            s_threadToInterrupt = Thread.CurrentThread;

            // Set up a Ctrl-C (Control-C) hander.  
            Console.CancelKeyPress += new ConsoleCancelEventHandler(delegate (object sender, ConsoleCancelEventArgs e)
            {
                if (Interlocked.CompareExchange(ref s_controlCPressed, 1, 0) == 0)
                {
                    Console.WriteLine("Ctrl-C Pressed.  Aborting.");
                    if (s_threadToInterrupt != null)
                    {
                        s_threadToInterrupt.Interrupt();
                        Thread.Sleep(30000);
                        Console.WriteLine("Thread did not die after 30 seconds.  Killing process.");
                    }
                    Environment.Exit(-20);
                }
                e.Cancel = true;
            });

            return true;
#endif
        }

#if !DOTNET_CORE
        private static Thread s_threadToInterrupt;
        private static int s_controlCPressed = 0;
#endif

        #endregion
        #endregion
    }

    /// <summary>
    /// APIs for logging usage data and feedback.
    /// </summary>
    public static class AppLog
    {
        /// <summary>
        /// Returns true if you have access to the file share where we log feedback
        /// </summary>
        public static bool CanSendFeedback
        {
            get
            {
#if PUBLIC_BUILD
                return false;
#else
                if (!s_CanSendFeedback.HasValue)
                {
                    if (s_IsUnderTest)
                    {
                        s_CanSendFeedback = false;          // Don't send feedback about test runs.  
                    }
                    else
                    {
                        // Have we tried to probe for the existence of \\clrmain?
                        if (s_ProbedForFeedbackAt.Ticks == 0)
                        {
                            s_ProbedForFeedbackAt = DateTime.Now;
                            // Only collect data in the REDMOND domain EUROPE has rules about telemetry.  
                            var userDomain = Environment.GetEnvironmentVariable("USERDOMAIN");
                            if (userDomain != "REDMOND")
                            {
                                s_CanSendFeedback = false;
                            }
                            else
                            {
                                s_CanSendFeedback = SymbolPath.ComputerNameExists(FeedbackServer) && WriteFeedbackToLog(FeedbackFilePath, "");
                            }
                        }
                        else
                        {
                            // Yes, see what has become of it.   
                            int msecSinceProbe = (int)(DateTime.Now - s_ProbedForFeedbackAt).TotalMilliseconds;
                            if (msecSinceProbe < 800)
                            {
                                Thread.Sleep(msecSinceProbe);       // We probed not to long ago, give it some time
                            }

                            if (!s_CanSendFeedback.HasValue)
                            {
                                s_CanSendFeedback = false;          //Give up if we don't have an answer by now. 
                            }
                        }
                    }
                }
                return s_CanSendFeedback.Value;
#endif
            }
        }
        /// <summary>
        /// Are we internal to Microsoft (and thus can have experimental features. 
        /// </summary>
        public static bool InternalUser
        {
            get
            {
                if (!s_InternalUser.HasValue)
                {
                    s_InternalUser = s_IsUnderTest || SymbolPath.ComputerNameExists(FeedbackServer, 400);
                }

                return s_InternalUser.Value;
            }
        }
        /// <summary>
        /// Log that the event 'eventName' with an optional string arg happened.  Will
        /// get stamped with the time, user, and session ID.  
        /// </summary>
        public static void LogUsage(string eventName, string arg1 = "", string arg2 = "")
        {
#if !PUBLIC_BUILD
            if (!CanSendFeedback)
            {
                return;
            }

            try
            {
                var usagePath = UsageFilePath;
                var userName = Environment.GetEnvironmentVariable("USERNAME");

                using (var writer = File.AppendText(usagePath))
                {
                    var now = DateTime.Now;
                    if (s_startTime.Ticks == 0)
                    {
                        s_startTime = now;
                    }

                    var secFromStart = (now - s_startTime).TotalSeconds;

                    var sessionID = (uint)(s_startTime.Ticks / 100000);
                    // SessionID, user, secondFromStart messageKind, arg 
                    writer.WriteLine("{0},{1},{2:f1},{3},\"{4}\",\"{5}\"", sessionID, userName, secFromStart, eventName, arg1, arg2);
                }

                // Keep the file to 10 meg;
                // Note that the move might fail, but that is OK.  
                if (new FileInfo(usagePath).Length > 10000000)
                {
                    File.Move(usagePath, Path.ChangeExtension(usagePath, ".prev.csv"));
                }
            }
            catch (Exception) { }
#endif
        }
        /// <summary>
        /// Called if you wish to send feedback to the developer.  Returns true if successful
        /// We segregate feedback into crashes and suggestions.  
        /// </summary>
        public static bool SendFeedback(string message, bool crash)
        {
#if PUBLIC_BUILD
            return false;
#else
            if (!CanSendFeedback)
            {
                return false;
            }

            StringWriter sw = new StringWriter();
            var userName = Environment.GetEnvironmentVariable("USERNAME");
            var userDomain = Environment.GetEnvironmentVariable("USERDOMAIN");

            var issueID = userName.Replace(" ", "") + "-" + DateTime.Now.ToString("yyyy'-'MM'-'dd'.'HH'.'mm'.'ss");
            string feedbackFile = FeedbackFilePath;
            var logPath = Path.Combine(FeedbackDirectory, "UserLog." + issueID + ".txt");

            sw.WriteLine("**********************************************************************");
            sw.WriteLine("OpenIssueID: {0}", issueID);
            sw.WriteLine("Date: {0}", DateTime.Now);
            sw.WriteLine("UserName: {0}", userName);
            sw.WriteLine("UserDomain: {0}", userDomain);
            sw.WriteLine("PerfView Version Number: {0}", VersionNumber);
            sw.WriteLine("PerfView Build Date: {0}", BuildDate);

            try
            {
                // Capture the user log, to see how we got here.  if it is less than 20 Meg.  
                if (File.Exists(App.LogFileName) && (new FileInfo(App.LogFileName)).Length < 20000000)
                {
                    File.Copy(App.LogFileName, logPath, true);
                }

                sw.WriteLine("UserLog: {0}", logPath);
            }
            catch { };

            sw.WriteLine("Message:");
            sw.Write("    ");
            sw.WriteLine(message.Replace("\n", "\n    "));
            return WriteFeedbackToLog(feedbackFile, sw.ToString());
#endif
        }
        public static string VersionNumber
        {
            get
            {
                // Update the AssemblyFileVersion attribute in AssemblyInfo.cs to update the version number 
                var fileVersion = (AssemblyFileVersionAttribute)(Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyFileVersionAttribute), false)[0]);
                return fileVersion.Version;
            }
        }
        public static string BuildDate
        {
            get
            {
                var buildDateAttribute = typeof(AppLog).Assembly.GetCustomAttributes<BuildDateAttribute>().FirstOrDefault();
                return buildDateAttribute?.BuildDate ?? "Unknown";
            }
        }

        #region private


        private static string FeedbackServer { get { return "clrMain"; } }
        private static string UsageFilePath { get { return Path.Combine(FeedbackDirectory, "PerfViewUsage.csv"); } }
        internal static string FeedbackFilePath { get { return Path.Combine(FeedbackDirectory, "PerfViewFeedback.txt"); } }
        private static string CrashLogFilePath { get { return Path.Combine(FeedbackDirectory, "PerfViewCrashes.txt"); } }
        private static string FeedbackDirectory
        {
            get
            {
                return @"\\" + FeedbackServer + @"\public\writable\perfView";
            }
        }

        private static DateTime s_startTime;    // used as a unique ID for the launch of the program (for SQM style logging)    
        internal static bool s_IsUnderTest; // set from tests: indicates we're in a test
        private static bool? s_InternalUser;
#if !PUBLIC_BUILD
        private static DateTime s_ProbedForFeedbackAt;
        private static bool? s_CanSendFeedback;
#endif
        private static bool WriteFeedbackToLog(string filePath, string message)
        {
            // Try 5 times (50 msec) to write the file.
            DateTime start = DateTime.UtcNow;
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    System.IO.Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                    using (var writer = new StreamWriter(filePath, true))   // open for appending. 
                    {
                        writer.Write(message);
                    }

                    return true;
                }
                catch (Exception) { }

                if ((DateTime.UtcNow - start).TotalMilliseconds > 50)
                {
                    break;
                }

                System.Threading.Thread.Sleep(10);
            }
            return false;
        }
        #endregion
    }

    /// <summary>
    /// VerboseLogWriter is a textWriter that forwards everything to 'verboseLog' but
    /// also sends any lines in [] to the 'terseLog'. 
    /// </summary>
    internal class VerboseLogWriter : TextWriter
    {
        public VerboseLogWriter(TextWriter verboseLog, TextWriter terseLog)
        {
            m_terseLog = terseLog;
            m_verboseLog = verboseLog;
        }
        public override void Write(char value)
        {
            Write(new String(value, 1));
        }
        public override void Write(char[] buffer, int index, int count)
        {
            Write(new String(buffer, index, count));
        }
        public override void Flush()
        {
            m_terseLog.Flush();
            m_verboseLog.Flush();
        }
        public override void Write(string value)
        {
            Match m = Regex.Match(value, @"^\s*\[(.*)\]\s*$");
            if (m.Success)
            {
                m_terseLog.WriteLine(m.Groups[1].Value);
                m_terseLog.Flush();
            }
            m_verboseLog.Write(value);
        }
        public override Encoding Encoding
        {
            get { return Encoding.UTF8; }
        }
        protected override void Dispose(bool disposing)
        {
            m_terseLog.Dispose();
            m_verboseLog.Dispose();
        }
        #region private
        private TextWriter m_verboseLog;
        private TextWriter m_terseLog;
        #endregion
    }
}
