using Diagnostics.Tracing.StackSources;
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Stacks;
using Microsoft.Diagnostics.Utilities;
using PerfView;
using PerfViewModel;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using Utilities;

#if !PERFVIEW_COLLECT
using Graphs;
using EventSources;
using PerfView.Dialogs;
using PerfView.GuiUtilities;
#endif

namespace PerfViewExtensibility
{
    /// <summary>
    /// CommandEnvironment defines the 'primitive starting points' that a PerfView Extension uses
    /// to start doing interesting things.   
    /// </summary>
    public class CommandEnvironment
    {
        // logging 
        /// <summary>
        /// The log file is a place to write verbose diagnostics.  It is either the console or the GUI log, or
        /// a file that use redirected logging to with the /logFile=XXX qualifier.
        /// </summary>
        public static TextWriter LogFile { get { return App.CommandProcessor.LogFile; } }

        // File I/O
        /// <summary>
        /// Open an ETL file (same as new TraceLog(etlFileName))
        /// </summary>
        /// <param name="etlFileName"></param>
        /// <returns></returns>
        public static ETLDataFile OpenETLFile(string etlFileName) { return new ETLDataFile(etlFileName); }
        /// <summary>
        /// Opens a *.perfView.xml.zip or *.perfview.xml 
        /// </summary>
        public static Stacks OpenPerfViewXmlFile(string perfViewXmlFileName)
        {
            var guiState = new StackWindowGuiState();
            var source = new XmlStackSource(perfViewXmlFileName, delegate (XmlReader reader)
            {
                if (reader.Name == "StackWindowGuiState")
                {
                    guiState.ReadFromXml(reader);
                }
                else
                {
                    reader.Skip();
                }
            });
            var ret = new Stacks(source, perfViewXmlFileName);
            ret.GuiState = guiState;
            ret.Name = perfViewXmlFileName;
            return ret;
        }
#if !PERFVIEW_COLLECT
        /// <summary>
        /// Opens a GCHeap dump (created with HeapSnapshot)
        /// </summary>
        public static Stacks OpenGCDumpFile(string gcDumpFileName)
        {
            var log = App.CommandProcessor.LogFile;
            var gcDump = new GCHeapDump(gcDumpFileName);

            Graph graph = gcDump.MemoryGraph;

            log.WriteLine(
                   "Opened Graph {0} Bytes: {1:f3}M NumObjects: {2:f3}K  NumRefs: {3:f3}K Types: {4:f3}K RepresentationSize: {5:f1}M",
                   gcDumpFileName, graph.TotalSize / 1000000.0, (int)graph.NodeIndexLimit / 1000.0,
                   graph.TotalNumberOfReferences / 1000.0, (int)graph.NodeTypeIndexLimit / 1000.0,
                   graph.SizeOfGraphDescription() / 1000000.0);

#if false // TODO FIX NOW remove
            using (StreamWriter writer = File.CreateText(Path.ChangeExtension(this.FilePath, ".heapDump.xml")))
            {
                ((MemoryGraph)graph).DumpNormalized(writer);
            }
#endif
            var retSource = new MemoryGraphStackSource(graph, log, gcDump.CountMultipliersByType);

            // Set the sampling ratio so that the number of objects does not get too far out of control.  
            if (2000000 <= (int)graph.NodeIndexLimit)
            {
                retSource.SamplingRate = ((int)graph.NodeIndexLimit / 1000000);
                log.WriteLine("Setting the sampling rate to {0} to keep processing under control.", retSource.SamplingRate);
            }

            // Figure out the multiplier 
            string extraTopStats = "";
            if (gcDump.CountMultipliersByType != null)
            {
                extraTopStats += string.Format(" Heap Sampled: Mean Count Multiplier {0:f2} Mean Size Multiplier {1:f2}",
                        gcDump.AverageCountMultiplier, gcDump.AverageSizeMultiplier);
            }

            log.WriteLine("Type Histogram > 1% of heap size");
            log.Write(graph.HistogramByTypeXml(graph.TotalSize / 100));

            // TODO FIX NOW better name. 
            var retStacks = new Stacks(retSource, "GC Heap Dump of " + Path.GetFileName(gcDumpFileName));
            retStacks.m_fileName = gcDumpFileName;
            retStacks.ExtraTopStats = extraTopStats;
            return retStacks;
        }
#endif
        // Data Collection
        /// <summary>
        /// Runs the command 'commandLine' with ETW enabled.   Creates  data file 'outputFileName'.  
        /// By default this is a Zipped ETL file.  
        /// </summary>  
        /// <param name="commandLine">The command line to run.</param>
        /// <param name="dataFile">The data file to place the profile data.  If omitted it is the parsedParams.DataFile</param>
        /// <param name="parsedCommandLine">Any other arguments for the run command.  
        /// If omitted it is inherited from the CommandEnvironment.CommandLineArgs </param>
        public static void Run(string commandLine, string dataFile = null, CommandLineArgs parsedCommandLine = null)
        {
            if (parsedCommandLine == null)
            {
                parsedCommandLine = App.CommandLineArgs;
            }

            parsedCommandLine.CommandLine = commandLine;
            if (dataFile != null)
            {
                parsedCommandLine.DataFile = dataFile;
            }

            App.CommandProcessor.Run(parsedCommandLine);
        }
        /// <summary>
        /// Run the 'Collect command creating the data file outputFileName.  By default this is a Zipped ETL file.  
        /// </summary>
        /// <param name="dataFile">The data file to place the profile data.  If omitted it is the parsedParams.DataFile</param>
        /// <param name="parsedCommandLine">Any other arguments for the run command.  
        /// If ommited it is inherited from the CommandEnvironment.CommandLineArgs </param>
        public static void Collect(string dataFile = null, CommandLineArgs parsedCommandLine = null)
        {
            if (parsedCommandLine == null)
            {
                parsedCommandLine = App.CommandLineArgs;
            }

            if (dataFile != null)
            {
                parsedCommandLine.DataFile = dataFile;
            }

            App.CommandProcessor.Collect(parsedCommandLine);
        }
        /// <summary>
        /// Collect a heap snapshot from 'process' placing the data in 'outputFileName' (a gcdump file)
        /// </summary>
        /// <param name="process">The name of the process or the process ID.  
        /// If there is more than one process with the same name, the one that was started LAST is chosen. </param>
        /// <param name="outputFileName">The data file (.gcdump) to generate.</param>
        public static void HeapSnapshot(string process, string outputFileName = null)
        {
            CommandLineArgs.Process = process;
            if (outputFileName != null)
            {
                CommandLineArgs.DataFile = outputFileName;
            }

            App.CommandProcessor.HeapSnapshot(CommandLineArgs);
        }
        /// <summary>
        /// Collect a heap snapshot from a process dump file 'dumpFile'  placing the data in 'outputFileName' (a gcdump file)
        /// </summary>
        /// <param name="inputDumpFile">The dump file to extract the heap from.</param>
        /// <param name="outputFileName">The data file (.gcdump) to generate. </param>
        public static void HeapSnapshotFromProcessDump(string inputDumpFile, string outputFileName = null)
        {
            CommandLineArgs.ProcessDumpFile = inputDumpFile;
            if (outputFileName != null)
            {
                CommandLineArgs.DataFile = outputFileName;
            }

            App.CommandProcessor.HeapSnapshot(CommandLineArgs);
        }

#if !PERFVIEW_COLLECT
        // gui operations 
        /// <summary>
        /// Open a new stack viewer GUI window in the dat in 'stacks'
        /// </summary>
        /// <param name="stacks"></param>
        /// <param name="OnOpened"></param>
        public static void OpenStackViewer(Stacks stacks, Action<StackWindow> OnOpened = null)
        {
            GuiApp.MainWindow.Dispatcher.BeginInvoke((Action)delegate ()
            {
                // TODO FIX NOW Major hacks. 
                PerfViewStackSource perfViewStackSource;
                string filePath;
                if (stacks.m_EtlFile != null)
                {
                    filePath = stacks.m_EtlFile.FilePath;
                    ETLPerfViewData file = (ETLPerfViewData)PerfViewFile.Get(filePath);
                    file.OpenWithoutWorker(GuiApp.MainWindow, GuiApp.MainWindow.StatusBar);
                    var stackSourceName = stacks.Name.Substring(0, stacks.Name.IndexOf(" file"));
                    perfViewStackSource = file.GetStackSource(stackSourceName);
                    if (perfViewStackSource == null)
                        perfViewStackSource = new PerfViewStackSource(file, "");
                }
                else
                {
                    if (stacks.m_fileName != null)
                        filePath = stacks.m_fileName;
                    else
                        filePath = stacks.Name;
                    if (string.IsNullOrWhiteSpace(filePath))
                        filePath = "X.PERFVIEW.XML";            // MAJOR HACK.

                    var perfViewFile = PerfViewFile.Get(filePath);
                    var gcDumpFile = perfViewFile as HeapDumpPerfViewFile;
                    if (gcDumpFile != null)
                    {
                        gcDumpFile.OpenWithoutWorker(GuiApp.MainWindow, GuiApp.MainWindow.StatusBar);
                        var gcDump = gcDumpFile.GCDump;
                        if (gcDump.CreationTool != null && gcDump.CreationTool == "ILSize")
                        {
                            // Right now we set nothing.  
                            stacks.GuiState = new StackWindowGuiState();
                            stacks.GuiState.Columns = new List<string>(9) { "NameColumn",
                                "ExcPercentColumn", "ExcColumn", "ExcCountColumn",
                                "IncPercentColumn", "IncColumn", "IncCountColumn",
                                "FoldColumn", "FoldCountColumn" };
                        }
                        perfViewStackSource = gcDumpFile.GetStackSource();

                    }
                    else
                    {
                        var xmlFile = perfViewFile as XmlPerfViewFile;
                        if (xmlFile == null)
                            throw new ApplicationException("Currently only support ETL files and XML files");
                        perfViewStackSource = new PerfViewStackSource(xmlFile, "");
                    }
                }
                var stackWindow = new StackWindow(GuiApp.MainWindow, perfViewStackSource);
                if (stacks.HasGuiState)
                    stackWindow.RestoreWindow(stacks.GuiState, filePath);
                stackWindow.Filter = stacks.Filter;
                stackWindow.SetStackSource(stacks.StackSource, delegate
                {
                    if (stacks.HasGuiState)
                        stackWindow.GuiState = stacks.GuiState;
                    else
                        perfViewStackSource.ConfigureStackWindow(stackWindow);

                    LogFile.WriteLine("[Opened stack viewer {0}]", filePath);
                    OnOpened?.Invoke(stackWindow);
                });
                stackWindow.Show();
            });
        }
        /// <summary>
        /// Open a new EventViewer with the given set of events
        /// </summary>
        /// <param name="events"></param>
        /// <param name="OnOpened">If non-null an action to perform after the window is opened (on the GUI thread)</param>
        /// <returns></returns>
        public static void OpenEventViewer(Events events, Action<EventWindow> OnOpened = null)
        {
            GuiApp.MainWindow.Dispatcher.BeginInvoke((Action)delegate ()
            {
                // TODO FIX NOW this is probably a hack?
                var file = PerfViewFile.Get(events.m_EtlFile.FilePath);
                var eventSource = new PerfViewEventSource(file);
                eventSource.m_eventSource = events;
                eventSource.Viewer = new EventWindow(GuiApp.MainWindow, eventSource);
                eventSource.Viewer.Show();
                if (OnOpened != null)
                    eventSource.Viewer.Loaded += delegate { OnOpened(eventSource.Viewer); };
            });
        }
        /// <summary>
        /// Displays an HTML file htmlFilePath, (typically created using CommandEnvironment.CreateUniqueCacheFileName()) 
        /// in a new window.  
        /// </summary>
        /// <param name="htmlFilePath">The path to the file containing the HTML.</param>
        /// <param name="title">The title for the new window.</param>
        /// <param name="DoCommand">Docommand(string command, TextWriter log), is a action that is called any time a URL of the
        /// form <a href="command:XXXX"></a> is clicked on.   The XXXX is passed as the command and a TextWriter that can display
        /// messages to the windows log is given, and a handle to the web browser window itself.   Messages surrounded by [] in 
        /// the log are also displayed on the windows one line status bar.   Thus important messages should be surrounded by [].   
        /// This callback is NOT on the GUI thread, so you have to use the window.Dispatcher.BeginInvoke() to cause actions on 
        /// the GUI thread.</param>
        /// <param name="OnOpened">If non-null an action to perform after the window is opened (on the GUI thread)</param>
        public static void OpenHtmlReport(string htmlFilePath, string title,
            Action<string, TextWriter, WebBrowserWindow> DoCommand = null, Action<WebBrowserWindow> OnOpened = null)
        {
            GuiApp.MainWindow.Dispatcher.BeginInvoke((Action)delegate ()
            {
                var viewer = new WebBrowserWindow(GuiApp.MainWindow);
                viewer.Browser.Navigating += delegate (object sender, System.Windows.Navigation.NavigatingCancelEventArgs e)
                {
                    if (e.Uri != null)
                    {
                        if (e.Uri.Scheme == "command")
                        {
                            e.Cancel = true;
                            if (viewer.StatusBar.Visibility != System.Windows.Visibility.Visible)
                                viewer.StatusBar.Visibility = System.Windows.Visibility.Visible;
                            viewer.StatusBar.StartWork("Following Hyperlink", delegate ()
                            {
                                if (DoCommand != null)
                                    DoCommand(e.Uri.LocalPath, viewer.StatusBar.LogWriter, viewer);
                                else
                                    viewer.StatusBar.Log("This view does not support command URLs.");
                                viewer.StatusBar.EndWork(null);
                            });
                        }
                    }
                };
                viewer.Width = 1000;
                viewer.Height = 600;
                viewer.Title = title;
                WebBrowserWindow.Navigate(viewer.Browser, Path.GetFullPath(htmlFilePath));
                viewer.Show();
                if (OnOpened != null)
                    viewer.Loaded += delegate { OnOpened(viewer); };

            });
        }
#endif

        /// <summary>
        /// Open Excel on csvFilePath.   
        /// </summary>
        public static void OpenExcel(string csvFilePath)
        {
            LogFile.WriteLine("[Opening CSV on {0}]", csvFilePath);
            Command.Run(Command.Quote(csvFilePath), new CommandOptions().AddStart().AddTimeout(CommandOptions.Infinite));
        }
#if !PERFVIEW_COLLECT
        /// <summary>
        /// Opens the log window if this is running under the GUI, otherwise does nothing.  
        /// </summary>
        public static void OpenLog()
        {
            if (App.CommandLineArgs.NoGui)
                return;
            GuiApp.MainWindow.Dispatcher.BeginInvoke((Action)delegate ()
            {
                GuiApp.MainWindow.StatusBar.OpenLog();
            });
        }
#endif

        // environment support 
        /// <summary>
        /// The command lines passed to perfView itself.  These are also populated by default values.
        /// Setting these values in CommandLineArgs will cause the commands below to use the updated values. 
        /// </summary>
        public static CommandLineArgs CommandLineArgs { get { return App.CommandLineArgs; } }
        /// <summary>
        /// ConfigData is a set of key-value dictionary that is persisted (as AppData\Roaming\PerfView\UserConfig.xml)
        /// so it is remembered across invocations of the program.  
        /// </summary>
        public static ConfigData ConfigData { get { return App.ConfigData; } }
        /// <summary>
        /// This is a directory where you can place temporary files.   These files will be cleaned up
        /// eventually if the number grows too large.   (this is %TEMP%\PerfView)
        /// </summary>
        public static string CacheFileDirectory { get { return CacheFiles.CacheDir; } }
        /// <summary>
        /// Creates a file name that is in the CacheFileDirectory that will not collide with any other file.  
        /// You give it the base name (file name without extension), as well as the extension, and it returns
        /// a full file path that is guaranteed to be unique.  
        /// </summary>
        /// <param name="baseFilePath">The file name without extension (a suffix will be added to this)</param>
        /// <param name="extension">optionally an extension to add to the file name (must have a . in front)</param>
        /// <returns>The full path of a unique file in the CacheFileDirectory</returns>
        public static string CreateUniqueCacheFileName(string baseFilePath, string extension = "")
        {
            return CacheFiles.FindFile(baseFilePath, extension);
        }
        /// <summary>
        /// This is the directory that all extensions live in (e.g. PerfViewExtensibity next to PerfView.exe)
        /// </summary>
        public static string ExtensionsDirectory { get { return Extensions.ExtensionsDirectory; } }
        /// <summary>
        /// This is the directory where support files can go (.e.g AppData\Roaming\PerfView\VER.*)
        /// </summary>
        public static string SupportFilesDirectory { get { return SupportFiles.SupportFileDir; } }
    }

    public class DataFile : IDisposable
    {        /// <summary>
             /// The path of the data file
             /// </summary>
        public string FilePath { get { return m_FilePath; } }
        public void Close() { Dispose(); }
        public virtual void Dispose() { }

        #region private
        protected string m_FilePath;
        #endregion
    }

    /// <summary>
    /// ETL file is a simplified wrapper over the TraceLog class, which represents an ETL data file.
    /// If you need 'advanced' features access the TraceLog property.  
    /// </summary>
    public class ETLDataFile : DataFile
    {
        // We have the concept of a process to focus on.  All STACK sampling will be filtered by this.  
        // If null, then no filtering is done.   Do try to limit to one process if possible as it makes
        // analysis and symbol lookup faster.  
        public void SetFilterProcess(TraceProcess process) { m_FilterProcess = process; }
        public void SetFilterProcess(string name) { m_FilterProcess = Processes.LastProcessWithName(name); }
        public void SetFilterProcess(int processId) { m_FilterProcess = Processes.LastProcessWithID(processId); }
        public TraceProcess FilterProcess { get { return m_FilterProcess; } }

        /// <summary>
        /// ETL files collect machine wide, but it is good practice to focus on a particular process
        /// as quickly as possible.   TraceProcesses is a shortcut to TraceLog.Process that let you
        /// find a process of interest quickly.  
        /// </summary>
        public TraceProcesses Processes { get { return TraceLog.Processes; } }
        /// <summary>
        /// If the ETL file has stack samples, fetch them.  
        /// </summary>
        public Stacks CPUStacks(bool loadAllCachedSymbols = false)
        {
            return new Stacks(TraceLog.CPUStacks(m_FilterProcess), "CPU", this, loadAllCachedSymbols);
        }
        /// <summary>
        /// If the ETL file has stack samples, fetch them.  
        /// </summary>
        public Stacks ThreadTimeStacks(bool loadAllCachedSymbols = false)
        {
            return new Stacks(TraceLog.ThreadTimeStacks(m_FilterProcess), "Thread Time", this, loadAllCachedSymbols);
        }
        /// <summary>
        /// If the ETL file has stack samples, fetch them as an activity aware stack source.
        /// </summary>
        public Stacks ThreadTimeWithTasksStacks(bool loadAllCachedSymbols = false)
        {
            return new Stacks(TraceLog.ThreadTimeWithTasksStacks(m_FilterProcess), "Thread Time (with Tasks)", this, loadAllCachedSymbols);
        }

#if !PERFVIEW_COLLECT
        /// <summary>
        /// Get the list of raw events.  
        /// </summary>
        public Events Events { get { return new Events(this); } }
#endif

        /// <summary>
        /// Open an existing ETL file by name
        /// </summary>
        /// <param name="fileName">The name of the ETL file to open</param>
        /// <param name="onLostEvents">If non-null, this method is called when there are lost events (with the count of lost events)</param>
        public ETLDataFile(string fileName, Action<bool, int, int> onLostEvents = null)
        {
            var log = App.CommandProcessor.LogFile;
            m_FilePath = fileName;

            var etlOrEtlXFileName = FilePath;
            UnZipIfNecessary(ref etlOrEtlXFileName, log);

            for (; ; )  // RETRY Loop 
            {
                var usedAnExistingEtlxFile = false;
                var etlxFileName = etlOrEtlXFileName;
                if (etlOrEtlXFileName.EndsWith(".etl", StringComparison.OrdinalIgnoreCase))
                {
                    etlxFileName = CacheFiles.FindFile(etlOrEtlXFileName, ".etlx");
                    if (File.Exists(etlxFileName) && File.GetLastWriteTimeUtc(etlOrEtlXFileName) <= File.GetLastWriteTimeUtc(etlxFileName))
                    {
                        usedAnExistingEtlxFile = true;
                        log.WriteLine("Found a existing ETLX file in cache: {0}", etlxFileName);
                    }
                    else
                    {
                        var options = new TraceLogOptions();
                        options.ConversionLog = log;
                        if (App.CommandLineArgs.KeepAllEvents)
                        {
                            options.KeepAllEvents = true;
                        }

                        options.MaxEventCount = App.CommandLineArgs.MaxEventCount;
                        options.ContinueOnError = App.CommandLineArgs.ContinueOnError;
                        options.SkipMSec = App.CommandLineArgs.SkipMSec;
                        options.OnLostEvents = onLostEvents;
                        options.LocalSymbolsOnly = false;
                        options.ShouldResolveSymbols = delegate (string moduleFilePath) { return false; };

                        log.WriteLine("Creating ETLX file {0} from {1}", etlxFileName, etlOrEtlXFileName);
                        TraceLog.CreateFromEventTraceLogFile(etlOrEtlXFileName, etlxFileName, options);

                        var dataFileSize = "Unknown";
                        if (File.Exists(etlOrEtlXFileName))
                        {
                            dataFileSize = ((new System.IO.FileInfo(etlOrEtlXFileName)).Length / 1000000.0).ToString("n3") + " MB";
                        }

                        log.WriteLine("ETL Size {0} ETLX Size {1:n3} MB",
                            dataFileSize, (new System.IO.FileInfo(etlxFileName)).Length / 1000000.0);
                    }
                }
                // At this point we have an etlxFileName set, so we can open the TraceLog file. 
                try
                {
                    m_TraceLog = new TraceLog(etlxFileName);
                }
                catch (Exception)
                {
                    // Failure! If we used an existing ETLX file we should try to regenerate the file 
                    if (usedAnExistingEtlxFile)
                    {
                        log.WriteLine("Could not open the ETLX file, regenerating...");
                        FileUtilities.ForceDelete(etlxFileName);
                        if (!File.Exists(etlxFileName))
                        {
                            continue;       // Retry 
                        }
                    }
                    throw;
                }
                break;
            }

            // Yeah we have opened the log file!
            if (App.CommandLineArgs.UnsafePDBMatch)
            {
                m_TraceLog.CodeAddresses.UnsafePDBMatching = true;
            }
        }
        /// <summary>
        /// Lookup all symbols for any module with 'simpleFileName'.   If processID==0 then all processes are searched. 
        /// </summary>
        /// <param name="simpleModuleName">The name of the module without directory or extension</param>
        /// <param name="symbolFlags">Options for symbol reading</param>
        public void LookupSymbolsForModule(string simpleModuleName, SymbolReaderOptions symbolFlags = SymbolReaderOptions.None)
        {
            var symbolReader = GetSymbolReader(symbolFlags);
            var log = App.CommandProcessor.LogFile;

            // Remove any extensions.  
            simpleModuleName = Path.GetFileNameWithoutExtension(simpleModuleName);

            // If we have a process, look the DLL up just there
            var moduleFiles = new Dictionary<int, TraceModuleFile>();
            if (m_FilterProcess != null)
            {
                foreach (var loadedModule in m_FilterProcess.LoadedModules)
                {
                    var baseName = Path.GetFileNameWithoutExtension(loadedModule.Name);
                    if (string.Compare(baseName, simpleModuleName, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        moduleFiles[(int)loadedModule.ModuleFile.ModuleFileIndex] = loadedModule.ModuleFile;
                    }
                }
            }

            // We did not find it, try system-wide
            if (moduleFiles.Count == 0)
            {
                foreach (var moduleFile in TraceLog.ModuleFiles)
                {
                    var baseName = Path.GetFileNameWithoutExtension(moduleFile.Name);
                    if (string.Compare(baseName, simpleModuleName, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        moduleFiles[(int)moduleFile.ModuleFileIndex] = moduleFile;
                    }
                }
            }

            if (moduleFiles.Count == 0)
            {
                throw new ApplicationException("Could not find module " + simpleModuleName + " in trace.");
            }

            if (moduleFiles.Count > 1)
            {
                log.WriteLine("Found {0} modules with name {1}", moduleFiles.Count, simpleModuleName);
            }

            foreach (var moduleFile in moduleFiles.Values)
            {
                TraceLog.CodeAddresses.LookupSymbolsForModule(symbolReader, moduleFile);
            }
        }

        /// <summary>
        /// Access the underlying TraceLog class that actually represents the ETL data.   You use
        /// when the simplified wrappers are not sufficient.  
        /// </summary>
        public TraceLog TraceLog { get { return m_TraceLog; } }
        /// <summary>
        /// Returns a SymbolReader which knows to look in local places associated with this file as well
        /// as the user defined places. 
        /// </summary>
        public SymbolReader GetSymbolReader(SymbolReaderOptions symbolFlags = SymbolReaderOptions.None)
        {
            return App.GetSymbolReader(m_FilePath, symbolFlags);
        }

        /// <summary>
        /// Closes the file (so for example you could update it after this)
        /// </summary>
        public override void Dispose()
        {
            m_TraceLog.Dispose();
            m_TraceLog = null;
        }
        #region private

        private static void UnZipIfNecessary(ref string inputFileName, TextWriter log, bool unpackInCache = true)
        {
            if (string.Compare(Path.GetExtension(inputFileName), ".zip", StringComparison.OrdinalIgnoreCase) == 0)
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
                else
                {
                    if (!inputFileName.EndsWith(".etl.zip", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new ApplicationException("File does not end with the .etl.zip file extension");
                    }

                    unzipedEtlFile = inputFileName.Substring(0, inputFileName.Length - 4);
                }

                Stopwatch sw = Stopwatch.StartNew();
                log.WriteLine("[Decompressing {0}]", inputFileName);
                log.WriteLine("Generating output file {0}", unzipedEtlFile);
                var zipArchive = ZipFile.OpenRead(inputFileName);

                ZipArchiveEntry zippedEtlFile = null;
                string dirForPdbs = null;
                foreach (var entry in zipArchive.Entries)
                {
                    if (entry.Length == 0)  // Skip directories. 
                    {
                        continue;
                    }

                    var fullName = entry.FullName;
                    if (fullName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
                    {
                        fullName = fullName.Replace('/', '\\');     // normalize separator convention 
                        string pdbRelativePath = null;
                        if (fullName.StartsWith(@"symbols\", StringComparison.OrdinalIgnoreCase))
                        {
                            pdbRelativePath = fullName.Substring(8);
                        }
                        else if (fullName.StartsWith(@"ngenpdbs\", StringComparison.OrdinalIgnoreCase))
                        {
                            pdbRelativePath = fullName.Substring(9);
                        }
                        else
                        {
                            var m = Regex.Match(fullName, @"^[^\\]+\.ngenpdbs?\\(.*)", RegexOptions.IgnoreCase);
                            if (m.Success)
                            {
                                pdbRelativePath = m.Groups[1].Value;
                            }
                            else
                            {
                                log.WriteLine("WARNING: found PDB file that was not in a symbol server style directory, skipping extraction");
                                log.WriteLine("         Unzip this ETL and PDB by hand to use this PDB.");
                                continue;
                            }
                        }

                        if (dirForPdbs == null)
                        {
                            var inputDir = Path.GetDirectoryName(inputFileName);
                            if (inputDir.Length == 0)
                            {
                                inputDir = ".";
                            }

                            var symbolsDir = Path.Combine(inputDir, "symbols");
                            if (Directory.Exists(symbolsDir))
                            {
                                dirForPdbs = symbolsDir;
                            }
                            else
                            {
                                dirForPdbs = new SymbolPath(App.SymbolPath).DefaultSymbolCache();
                            }

                            log.WriteLine("Putting symbols in {0}", dirForPdbs);
                        }

                        var pdbTargetPath = Path.Combine(dirForPdbs, pdbRelativePath);
                        var pdbTargetName = Path.GetFileName(pdbTargetPath);
                        if (!File.Exists(pdbTargetPath) || (new System.IO.FileInfo(pdbTargetPath).Length != entry.Length))
                        {
                            var firstNameInRelativePath = pdbRelativePath;
                            var sepIdx = firstNameInRelativePath.IndexOf('\\');
                            if (sepIdx >= 0)
                            {
                                firstNameInRelativePath = firstNameInRelativePath.Substring(0, sepIdx);
                            }

                            var firstNamePath = Path.Combine(dirForPdbs, firstNameInRelativePath);
                            if (File.Exists(firstNamePath))
                            {
                                log.WriteLine("Deleting pdb file that is in the way {0}", firstNamePath);
                                FileUtilities.ForceDelete(firstNamePath);
                            }
                            log.WriteLine("Extracting PDB {0}", pdbRelativePath);
                            AtomicExtract(entry, pdbTargetPath);
                        }
                        else
                        {
                            log.WriteLine("PDB {0} exists, skipping", pdbRelativePath);
                        }
                    }
                    else if (fullName.EndsWith(".etl", StringComparison.OrdinalIgnoreCase))
                    {
                        if (zippedEtlFile != null)
                        {
                            throw new ApplicationException("The ZIP file does not have exactly 1 ETL file in it, can't auto-extract.");
                        }

                        zippedEtlFile = entry;
                    }
                }
                if (zippedEtlFile == null)
                {
                    throw new ApplicationException("The ZIP file does not have any ETL files in it!");
                }

                AtomicExtract(zippedEtlFile, unzipedEtlFile);
                log.WriteLine("Zipped size = {0:f3} MB Unzipped = {1:f3} MB",
                    zippedEtlFile.CompressedLength / 1000000.0, zippedEtlFile.Length / 1000000.0);

                File.SetLastWriteTime(unzipedEtlFile, DateTime.Now);       // Touch the file
                inputFileName = unzipedEtlFile;
                log.WriteLine("Finished decompression, took {0:f0} sec", sw.Elapsed.TotalSeconds);
            }
        }
        // Extract to a temp file and move so we get atomic update.  May leave trash behind
        private static void AtomicExtract(ZipArchiveEntry zipEntry, string targetPath)
        {
            // Insure directory exists. 
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
            var extractPath = targetPath + ".new";
            try
            {
                zipEntry.ExtractToFile(extractPath, true);
                FileUtilities.ForceMove(extractPath, targetPath);
            }
            finally
            {
                FileUtilities.ForceDelete(extractPath);
            }
        }

        private TraceLog m_TraceLog;
        private TraceProcess m_FilterProcess;       // Only care about this process. 
        #endregion
    }

#if !PERFVIEW_COLLECT
    public class Events : ETWEventSource
    {
        void SaveAsCSV(string csvFileName)
        {
            string listSeparator = Thread.CurrentThread.CurrentCulture.TextInfo.ListSeparator;
            using (var csvFile = File.CreateText(csvFileName))
            {
                // Write out column header
                csvFile.Write("Event Name{0}Time MSec{0}Process Name", listSeparator);
                // TODO get rid of ugly 4 column restriction
                var maxField = 0;
                var hasRest = true;
                if (ColumnsToDisplay != null)
                {
                    hasRest = false;
                    foreach (var columnName in ColumnsToDisplay)
                    {
                        Debug.Assert(!columnName.Contains(listSeparator));
                        if (maxField >= 4)
                        {
                            hasRest = true;
                            break;
                        }
                        maxField++;
                        csvFile.Write("{0}{1}", listSeparator, columnName);
                    }
                }
                if (hasRest)
                    csvFile.Write("{0}Rest", listSeparator);
                csvFile.WriteLine();

                // Write out events 
                this.ForEach(delegate (EventRecord _event)
                {
                    // Have we exceeded MaxRet?
                    if (_event.EventName == null)
                        return false;

                    csvFile.Write("{0}{1}{2:f3}{1}{3}", _event.EventName, listSeparator, _event.TimeStampRelatveMSec, EscapeForCsv(_event.ProcessName, listSeparator));
                    var fields = _event.DisplayFields;
                    for (int i = 0; i < maxField; i++)
                        csvFile.Write("{0}{1}", listSeparator, EscapeForCsv(fields[i], listSeparator));
                    if (hasRest)
                        csvFile.Write("{0}{1}", listSeparator, EscapeForCsv(_event.Rest, listSeparator));
                    csvFile.WriteLine();
                    return true;
                });
            }
        }
        void OpenInExcel()
        {
            var log = App.CommandProcessor.LogFile;

            var csvFile = CacheFiles.FindFile(m_EtlFile.FilePath, ".excel.csv");
            if (File.Exists(csvFile))
            {
                FileUtilities.TryDelete(csvFile);

                var baseFile = csvFile.Substring(0, csvFile.Length - 9);
                for (int i = 1; ; i++)
                {
                    csvFile = baseFile + i.ToString() + ".excel.csv";
                    if (!File.Exists(csvFile))
                        break;
                }
            }

            log.WriteLine("Saving to CSV file {0}", csvFile);
            SaveAsCSV(csvFile);
            log.WriteLine("Opening CSV .");
            Command.Run(Command.Quote(csvFile), new CommandOptions().AddStart().AddTimeout(CommandOptions.Infinite));
            log.WriteLine("CSV file opened.");
            throw new NotImplementedException();
        }

        public Events(ETLDataFile etlFile)
            : base(etlFile.TraceLog)
        {
            m_EtlFile = etlFile;
        }

    #region private
        /// <summary>
        /// Returns a string that is will be exactly one field of a CSV file.  Thus it escapes , and ""
        /// </summary>
        internal static string EscapeForCsv(string str, string listSeparator)
        {
            // TODO FIX NOW is this a hack?
            if (str == null)
                return "";
            // If you don't have a comma, you are OK (we are losing leading and trailing whitespace but I don't care about that. 
            if (str.IndexOf(listSeparator) < 0)
                return str;

            // Escape all " by repeating them
            str = str.Replace("\"", "\"\"");
            return "\"" + str + "\"";       // then quote the whole thing
        }

        internal ETLDataFile m_EtlFile;
    #endregion
    }
#endif

    /// <summary>
    /// A Stacks class represents what the PerfView 'stacks' view shows you.   It has 
    /// a 'FilterParams' property which represents all the filter strings in that view.  
    /// It also has properties that represent the various tabs in that view (calltee, byname ...)
    /// </summary>
    public class Stacks
    {
        public FilterParams Filter { get { return m_Filter; } set { m_Filter = value; m_StackSource = null; } }
        public void Update() { m_StackSource = null; }

        public CallTree CallTree
        {
            get
            {
                if (m_CallTree == null || m_StackSource == null)
                {
                    m_CallTree = new CallTree(ScalingPolicyKind.ScaleToData);
                    m_CallTree.StackSource = StackSource;
                }
                return m_CallTree;
            }
        }

        private IEnumerable<CallTreeNodeBase> ByName
        {
            get
            {
                if (m_byName == null || m_CallTree == null || m_StackSource == null)
                {
                    m_byName = CallTree.ByIDSortedExclusiveMetric();
                }

                return m_byName;
            }
        }
        public CallTreeNodeBase FindNodeByName(string nodeNamePat)
        {
            var regEx = new Regex(nodeNamePat, RegexOptions.IgnoreCase);
            foreach (var node in ByName)
            {
                if (regEx.IsMatch(node.Name))
                {
                    return node;
                }
            }
            return CallTree.Root;
        }
        public CallTreeNode GetCallers(string focusNodeName)
        {
            var focusNode = FindNodeByName(focusNodeName);
            return AggregateCallTreeNode.CallerTree(focusNode);
        }
        public CallTreeNode GetCallees(string focusNodeName)
        {
            var focusNode = FindNodeByName(focusNodeName);
            return AggregateCallTreeNode.CalleeTree(focusNode);
        }

        /// <summary>
        /// Resolve the symbols of all modules that have at least 'minCount' INCLUSIVE samples.  
        /// symbolFlags indicate how aggressive you wish to be.   By default it is aggressive as possible (do 
        /// whatever you need to get the PDB you need).  
        /// Setting 'minCount' to 0 will try to look up all symbols possible (which is relatively expensive). 
        /// 
        /// By default all Warm symbols with a count > 50 AND in the machine local symbol cache are looked up.  
        /// If the cache is empty, or if you want even low count modules included, call this explicitly
        /// </summary>
        public void LookupWarmSymbols(int minCount, SymbolReaderOptions symbolFlags = SymbolReaderOptions.None)
        {
            TraceEventStackSource asTraceEventStackSource = GetTraceEventStackSource(m_rawStackSource);
            if (asTraceEventStackSource == null)
            {
                App.CommandProcessor.LogFile.WriteLine("LookupWarmSymbols: Stack source does not support symbols.");
                return;
            }
            string etlFilepath = null;
            if (m_EtlFile != null)
            {
                etlFilepath = m_EtlFile.FilePath;
            }

            var reader = App.GetSymbolReader(etlFilepath, symbolFlags);
            asTraceEventStackSource.LookupWarmSymbols(minCount, reader, StackSource);
            m_StackSource = null;
        }
        /// <summary>
        /// Lookup the symbols for a particular module (DLL). 
        /// </summary>
        /// <param name="simpleModuleName">The simple name (dll name without path or file extension)</param>
        /// <param name="symbolFlags">Optional flags that control symbol lookup</param>
        public void LookupSymbolsForModule(string simpleModuleName, SymbolReaderOptions symbolFlags = SymbolReaderOptions.None)
        {
            if (m_EtlFile != null)
            {
                m_EtlFile.LookupSymbolsForModule(simpleModuleName, symbolFlags);
                m_StackSource = null;
            }
            else
            {
                App.CommandProcessor.LogFile.WriteLine("LookupSymbolsForModule: Stack source does not support symbols.");
                return;
            }
        }

        /// <summary>
        /// Saves the stacks as a XML file (or a ZIPed XML file).  Only samples that pass the filter are saved.
        /// Also all interesting symbolic names should be resolved first because it is impossible to resolve them 
        /// later.   The saved samples CAN be regrouped later, however.  
        /// </summary>
        public void SaveAsXml(string outputFileName, bool zip = true, bool includeGuiState = true)
        {
            // TODO remember the status log even when we don't have a gui. 
#if !PERFVIEW_COLLECT
            if (GuiApp.MainWindow != null)
                GuiState.Log = File.ReadAllText(App.LogFileName);
#endif

            Action<XmlWriter> additionalData = null;
            if (includeGuiState)
            {
                additionalData = new Action<XmlWriter>((XmlWriter writer) => { GuiState.WriteToXml("StackWindowGuiState", writer); });
            }

            // Intern to compact it, only take samples in the view but leave the names unmorphed. 
            InternStackSource source = new InternStackSource(StackSource, m_rawStackSource);
            if (zip)
            {
                XmlStackSourceWriter.WriteStackViewAsZippedXml(source, outputFileName, additionalData);
            }
            else
            {
                XmlStackSourceWriter.WriteStackViewAsXml(source, outputFileName, additionalData);
            }

            GuiState.Log = null;        // Save some space. 
        }
        /// <summary>
        /// This is an optional and is used for human interactions. 
        /// </summary>
        public string Name { get; internal set; }
        /// <summary>
        /// ExtraTopStats is a string that is pretty important that humans see when viewing these stacks.
        /// Often they are aggregate statistics, or maybe warnings or error summaries.   It should be 
        /// well under 100 chars long.  
        /// </summary>
        public string ExtraTopStats { get; set; }

        /// <summary>
        /// Stacks is just a convenience wrapper around the StackSource class.  For advanced use
        /// you may need to get at the stack source.   
        /// </summary>
        public StackSource StackSource
        {
            get
            {
                if (m_StackSource == null)
                {
                    m_StackSource = new FilterStackSource(m_Filter, m_rawStackSource, ScalingPolicyKind.ScaleToData);
                }

                return m_StackSource;
            }
        }
        /// <summary>
        /// If you have a stackSource and want a Stacks, 
        /// </summary>
        public Stacks(StackSource source, string name = "")
        {
            m_Filter = new FilterParams();
            m_rawStackSource = source;
            Name = name;
        }

        // GUI State
        /// <summary>
        /// There is a bunch of data that really is useful to persist to make 
        /// the GUI work well, (like the history of Filter selections etc) 
        /// but is not central to the model of the data.  This all goes into
        /// 'GuiState'.   This data gets persisted when SaveAsXml is called and
        /// is passed to the GUI when OpenStackViewer is called, but otherwise
        /// we don't do much with it.  (unless you wish influence the GUI
        /// by explicitly changing it).  
        /// </summary>
        public StackWindowGuiState GuiState
        {
            get
            {
                if (m_GuiState == null)
                {
                    m_GuiState = DefaultCallStackWindowState("CPU");
                }

                return m_GuiState;
            }
            set { m_GuiState = value; }
        }

        public bool HasGuiState { get { return m_GuiState != null; } }

        public static StackWindowGuiState DefaultCallStackWindowState(string name)
        {
            // TODO logic for getting out of ConfigSettings.  

            var ret = new StackWindowGuiState();
            ret.Columns = new List<string>(12) {
                    "NameColumn",
                    "ExcPercentColumn", "ExcColumn", "ExcCountColumn",
                    "IncPercentColumn", "IncColumn", "IncCountColumn",
                    "FoldColumn", "FoldCountColumn",
                    "WhenColumn", "FirstColumn", "LastColumn" };

            if (name == "Memory")
            {
                ret.FilterGuiState.FoldRegEx.Value = @"[];mscorlib!System.String";
            }
            else
            {
                ret.FilterGuiState.GroupRegEx.Value =
                    @"[group CLR/OS entries] \Temporary ASP.NET Files\->;v4.0.30319\%!=>CLR;v2.0.50727\%!=>CLR;mscoree=>CLR;\mscorlib.*!=>LIB;\System.Xaml.*!=>WPF;\System.*!=>LIB;" +
                    @"Presentation%=>WPF;WindowsBase%=>WPF;system32\*!=>OS;syswow64\*!=>OS;{%}!=> module $1";
                ret.FilterGuiState.GroupRegEx.History = new List<string>(6) { ret.FilterGuiState.GroupRegEx.Value,
                     "[group modules]           {%}!->module $1",
                     "[group module entries]  {%}!=>module $1",
                     "[group full path module entries]  {*}!=>module $1",
                     "[group class entries]     {%!*}.%(=>class $1;{%!*}::=>class $1",
                     "[group classes]            {%!*}.%(->class $1;{%!*}::->class $1" };

                ret.FilterGuiState.ExcludeRegEx.Value = "^Process% Idle";
                ret.FilterGuiState.ExcludeRegEx.History = new List<string>(1) { ret.FilterGuiState.ExcludeRegEx.Value };

                ret.FilterGuiState.FoldPercent.Value = "1";

                // Can allow users to tweek this with config data. 
                switch (name)
                {
                    case "CPU":
                        ret.Columns.Remove("IncCountColumn");
                        ret.Columns.Remove("ExcCountColumn");
                        ret.Columns.Remove("FoldCountColumn");
                        ret.ScalingPolicy = ScalingPolicyKind.TimeMetric;
                        break;
                }
            }
            return ret;
        }

        public override string ToString()
        {
            var sw = new System.IO.StringWriter();
            sw.Write("<Stacks");
            if (Name != null)
            {
                sw.Write(" Name=\"{0}\"", Name);
            }

            sw.WriteLine(">");
            sw.Write(" <RootStats ");
            CallTree.Root.ToXmlAttribs(sw);
            sw.Write("/>");
            sw.WriteLine("</Stacks>");
            return sw.ToString();
        }

        #region private
        /// <summary>
        /// TODO should not have to specify the ETL file. 
        /// </summary>
        public Stacks(StackSource source, string name, ETLDataFile etlFile, bool loadAllCachedSymbols = false)
        {
            m_Filter = new FilterParams();
            m_rawStackSource = source;
            m_EtlFile = etlFile;
            Name = string.Format("{0} file {1} in {2}", name, Path.GetFileName(etlFile.FilePath), Path.GetDirectoryName(etlFile.FilePath));

            // By default, look up all symbols in cache that have at least 50 samples.  
            LookupWarmSymbols((loadAllCachedSymbols ? 0 : 50), SymbolReaderOptions.CacheOnly);
        }
        protected Stacks() { }

        /// <summary>
        /// Unwind the wrapped sources to get to a TraceEventStackSource if possible. 
        /// </summary>
        internal static TraceEventStackSource GetTraceEventStackSource(StackSource source)
        {
            StackSourceStacks rawSource = source;
            TraceEventStackSource asTraceEventStackSource = null;
            for (; ; )
            {
                asTraceEventStackSource = rawSource as TraceEventStackSource;
                if (asTraceEventStackSource != null)
                {
                    return asTraceEventStackSource;
                }

                var asCopyStackSource = rawSource as CopyStackSource;
                if (asCopyStackSource != null)
                {
                    rawSource = asCopyStackSource.SourceStacks;
                    continue;
                }
                var asStackSource = rawSource as StackSource;
                if (asStackSource != null && asStackSource != asStackSource.BaseStackSource)
                {
                    rawSource = asStackSource.BaseStackSource;
                    continue;
                }
                return null;
            }

        }

        protected StackSource m_rawStackSource;           // Before the filter is applied 
        private StackSource m_StackSource;              // After the filter is applied, note this changes every time the filter does. 
        private CallTree m_CallTree;
        private List<CallTreeNodeBase> m_byName;
        private FilterParams m_Filter;
        internal ETLDataFile m_EtlFile;                 // If this stack came from and ETL File this is that file.  
        internal string m_fileName;                     // TODO is this a hack.  This is the file name if present.  
        private StackWindowGuiState m_GuiState;
        #endregion
    }

    #region internal classes
    internal static class Extensions
    {
        public static string ExtensionsDirectory
        {
            get
            {
                if (s_ExtensionsDirectory == null)
                {
                    var exeDir = Path.GetDirectoryName(SupportFiles.MainAssemblyPath);
                    // This is for development ease development of perfView itself.  
                    if (exeDir.EndsWith(@"\perfView\bin\Release", StringComparison.OrdinalIgnoreCase))
                    {
                        exeDir = exeDir.Substring(0, exeDir.Length - 20);
                    }
                    else if (exeDir.EndsWith(@"\perfView\bin\Debug", StringComparison.OrdinalIgnoreCase))
                    {
                        exeDir = exeDir.Substring(0, exeDir.Length - 18);
                    }

                    s_ExtensionsDirectory = Path.Combine(exeDir, "PerfViewExtensions");
                }
                return s_ExtensionsDirectory;
            }
        }

#if !PERFVIEW_COLLECT
        public static void RunUserStartupCommands(StatusBar worker)
        {
            try
            {
                var startupFilePath = Path.Combine(ExtensionsDirectory, "PerfViewStartup");
                if (File.Exists(startupFilePath))
                {
                    string errorMessage = null;
                    string line = "";
                    int lineNum = 0;
                    using (var startupFile = File.OpenText(startupFilePath))
                    {
                        for (;;)
                        {
                            lineNum++;
                            line = startupFile.ReadLine();
                            if (line == null)
                                return;
                            line = line.Trim();
                            if (line.Length == 0 || line.StartsWith("#"))
                                continue;
                            List<string> commandAndArgs = MainWindow.ParseWordsOrQuotedStrings(line);
                            var command = "";
                            if (0 < commandAndArgs.Count)
                                command = commandAndArgs[0];
                            if (command == "OnStartup")
                            {
                                if (commandAndArgs.Count != 2)
                                {
                                    errorMessage = "OnStartup command requires 1 argument, the startup user command.";
                                    goto Failed;
                                }
                                ExecuteUserCommand(commandAndArgs[1], null);
                            }
                            else if (command == "OnFileOpen")
                            {
                                if (commandAndArgs.Count != 3)
                                {
                                    errorMessage = "OnFileOpen command requires 2 arguments, the file extension and the user command.";
                                    goto Failed;
                                }
                                string extension = commandAndArgs[1];
                                PerfViewFile.GetTemplateForExtension(extension).OnOpenFile(commandAndArgs[2]);
                            }
                            else if (command == "DeclareFileView")
                            {
                                if (commandAndArgs.Count != 4)
                                {
                                    errorMessage = "DeclareFileView command requires 3 arguments, the file extension, view name and the user command.";
                                    goto Failed;
                                }
                                string extension = commandAndArgs[1];
                                PerfViewFile.GetTemplateForExtension(extension).DeclareFileView(commandAndArgs[2], commandAndArgs[3]);
                            }
                            else
                            {
                                if (string.IsNullOrWhiteSpace(line))
                                    continue;
                                errorMessage = "Unrecognized command.";
                                goto Failed;
                            }
                        }
                        Failed:
                        throw new ApplicationException("Error: " + errorMessage + "  '" + line + @"' line " + lineNum + @" in PerfViewExtensions\PerfViewStartup file");
                    }
                }
            }
            catch (Exception e)
            {
                worker.LogError(@"Error executing PerfViewExtensions\PerfViewStartup file.\r\n" + e.Message);
            }
        }
#endif

        public static IEnumerable<string> GetExtensionDlls()
        {
            if (Directory.Exists(ExtensionsDirectory))
            {
                foreach (var dll in Directory.GetFiles(ExtensionsDirectory, "*.dll", SearchOption.TopDirectoryOnly))
                {
                    using (var peFile = new PEFile.PEFile(dll))
                    {
                        if (!peFile.Header.IsManaged || peFile.Header.IsPE64)
                        {
                            continue;
                        }
                    }
                    yield return dll;
                }
            }
        }

        public static void GenerateHelp(TextWriter log)
        {
            var commandsHelp = GetUserCommandHelp();
            foreach (var method in Extensions.GetAllUserCommands())
            {
                var commandSummary = Extensions.GetCommandString(method);
                log.WriteLine();
                log.WriteLine("-------------------------------------------------------------------------");
                log.WriteLine("{0}", commandSummary);

                // Get command name
                var idx = commandSummary.IndexOf(' ');
                if (idx < 0)
                {
                    idx = commandSummary.Length;
                }

                var commandName = commandSummary.Substring(0, idx);

                // Print extra help
                CommandHelp commandHelp;
                if (commandsHelp.TryGetValue(commandName, out commandHelp))
                {
                    log.WriteLine();
                    log.WriteLine("  Summary: ");
                    WriteWrapped("      ", commandHelp.Summary, "      ", 80, log);
                    if (commandHelp.Params != null)
                    {
                        log.WriteLine("  Parameters: ");
                        foreach (var param in commandHelp.Params)
                        {
                            WriteWrapped("      " + param.Name + ": ", param.Help, "          ", 80, log);
                        }
                    }
                }
            }
        }

        private static void WriteWrapped(string prefix, string body, string wrap, int maxColumn, TextWriter log)
        {
            body = Regex.Replace(body, @"\s+", " ");
            log.Write(prefix);
            int curColumn = prefix.Length;
            int idx = 0;
            do
            {
                var nextBreak = GetLineNextBreak(body, idx, curColumn, maxColumn);

                log.WriteLine(body.Substring(idx, nextBreak - idx));
                if (body.Length <= nextBreak)
                {
                    break;
                }

                idx = nextBreak + 1;
                log.Write(wrap);
                curColumn = wrap.Length;
            }
            while (idx < body.Length);
        }

        /// <summary>
        /// Find the end index of the chunk of 'str' if we are at 'curColumn' and we don't want the text
        /// to go beyond maxColumn.    
        /// </summary>
        private static int GetLineNextBreak(string str, int startIdx, int startColumn, int maxColumn)
        {
            var curPos = startIdx + (maxColumn - startColumn);
            if (curPos >= str.Length)
            {
                return str.Length;
            }

            var spaceIdx = str.LastIndexOf(' ', curPos, curPos - startIdx);
            if (0 <= spaceIdx)
            {
                return spaceIdx;
            }

            spaceIdx = str.IndexOf(' ', curPos);
            if (0 <= spaceIdx)
            {
                return spaceIdx;
            }

            return str.Length;
        }

        public static List<MethodInfo> GetAllUserCommands()
        {
            var ret = new List<MethodInfo>();

            // Get the ones built into perfView itself (in UserCommands.cs PerfViewExtensibility\Commands)
            var methods = typeof(PerfViewExtensibility.Commands).GetMethods(
                BindingFlags.Public | BindingFlags.InvokeMethod | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var method in methods)
            {
                ret.Add(method);
            }

            // Find all the ones that are in user extension dlls.  
            foreach (var extensionDllPath in GetExtensionDlls())
            {
                try
                {
                    var assembly = Assembly.LoadFrom(extensionDllPath);
                    var commandType = assembly.GetType("Commands");
                    if (commandType != null)
                    {
                        methods = commandType.GetMethods(
                            BindingFlags.Public | BindingFlags.InvokeMethod | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                        foreach (var method in methods)
                        {
                            ret.Add(method);
                        }
                    }
                }
                catch (Exception)
                {
                    App.CommandProcessor.LogFile.WriteLine("Dll " + extensionDllPath + "could not be loaded, assuming it has no user commands");
                }
            }

            return ret;
        }

        /// <summary>
        /// Returns strings of the from COMMANDNAME arg1Name [arg2Name] .... 
        /// where [] indicate optional arguments.  
        /// </summary>
        /// <returns></returns>
        public static string GetCommandString(MethodInfo method)
        {
            var sb = new StringBuilder();

            var assembly = method.DeclaringType.Assembly;
            var assemblyName = Path.GetFileNameWithoutExtension(assembly.ManifestModule.FullyQualifiedName);
            if (string.Compare(assemblyName, "Global", StringComparison.OrdinalIgnoreCase) != 0 && assembly != Assembly.GetExecutingAssembly())
            {
                sb.Append(assemblyName).Append('.');
            }

            sb.Append(method.Name);
            foreach (var param in method.GetParameters())
            {
                sb.Append(' ');
                var defaultValue = param.RawDefaultValue;
                if (defaultValue != System.DBNull.Value)
                {
                    sb.Append('[').Append(param.Name).Append(']');
                }
                else
                {
                    var attribs = param.GetCustomAttributes(typeof(ParamArrayAttribute), false);
                    if (attribs.Length != 0)
                    {
                        sb.Append('[').Append(param.Name).Append("...]");
                    }
                    else
                    {
                        sb.Append(param.Name);
                    }
                }
            }
            var ret = sb.ToString();
            return ret;
        }

        /// <summary>
        /// Return all the user command help, indexed by user command name.  
        /// </summary>
        public static Dictionary<string, CommandHelp> GetUserCommandHelp()
        {
            var ret = new Dictionary<string, CommandHelp>();
            foreach (var extDll in GetExtensionDlls())
            {
                var extName = Path.GetFileNameWithoutExtension(extDll);
                var xmlHelp = Path.ChangeExtension(extDll, ".xml");
                AddUserCommandHelp(ret, extName, xmlHelp);
            }
            // Add the help for the ones in Extensions.cs in perfView itself.  
            AddUserCommandHelp(ret, "", Path.Combine(SupportFiles.SupportFileDir, "PerfView.xml"));
            return ret;
        }

        /// <summary>
        /// Given the name of the extension DLL (extName) and the xml help file for the extension xmlHelp
        /// Find the XML comments for all commands and add them to the 'userComandHelp' dictionary.  
        /// </summary>
        private static void AddUserCommandHelp(Dictionary<string, CommandHelp> userComandHelp, string extName, string xmlHelp)
        {
            // TODO we actually end up with too large of dictionary because we also store the private methods too.  
            try
            {
                if (File.Exists(xmlHelp))
                {
                    XmlReaderSettings settings = new XmlReaderSettings() { IgnoreWhitespace = true, IgnoreComments = true };
                    XmlReader reader = XmlReader.Create(xmlHelp, settings);
                    reader.ReadToDescendant("members");
                    var startDepth = reader.Depth;
                    reader.Read();      // Advance to children 
                    while (startDepth < reader.Depth)
                    {
                        if (reader.NodeType == XmlNodeType.Element)
                        {
                            if (reader.Name == "member")
                            {
                                var xmlMemberName = reader.GetAttribute("name");
                                if (xmlMemberName.StartsWith("M:Commands."))
                                {
                                    var endIdx = xmlMemberName.IndexOf('(');
                                    if (endIdx < 0)
                                    {
                                        endIdx = xmlMemberName.Length;
                                    }

                                    var name = xmlMemberName.Substring(11, endIdx - 11);
                                    if (extName != "Global")
                                    {
                                        name = extName + "." + name;
                                    }

                                    userComandHelp.Add(name, new CommandHelp(name, reader));
                                }
                                else if (extName.Length == 0 && xmlMemberName.StartsWith("M:PerfViewExtensibility.Commands."))
                                {
                                    // Handle the case for user commands defined in PerfView.exe itself.  
                                    var endIdx = xmlMemberName.IndexOf('(');
                                    if (endIdx < 0)
                                    {
                                        endIdx = xmlMemberName.Length;
                                    }

                                    var name = xmlMemberName.Substring(33, endIdx - 33);
                                    userComandHelp[name] = new CommandHelp(name, reader);
                                }
                                else
                                {
                                    reader.Skip();
                                }
                            }
                            else
                            {
                                reader.Skip();
                            }
                        }
                        else if (!reader.Read())
                        {
                            break;
                        }
                    }

                }
            }
            catch (Exception e)
            {
                // TODO add logging that gets to the user.  
                Debug.WriteLine("error reading XML file " + e.Message);
            }
        }

        /// <summary>
        /// Represents the help for a method. 
        /// </summary>
        public class CommandHelp
        {
            public string Name;
            public string Summary;
            public List<CommandHelpParam> Params;
            private string name;

            public CommandHelp(string name, XmlReader reader)
            {
                this.name = name;

                var startDepth = reader.Depth;
                reader.Read();      // Advance to children 
                while (startDepth < reader.Depth)
                {
                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        if (reader.Name == "summary")
                        {
                            Summary = reader.ReadElementContentAsString().Trim();
                        }
                        else if (reader.Name == "param")
                        {
                            if (Params == null)
                            {
                                Params = new List<CommandHelpParam>();
                            }

                            var newParam = new CommandHelpParam();
                            newParam.Name = reader.GetAttribute("name");
                            newParam.Help = reader.ReadElementContentAsString().Trim();
                            Params.Add(newParam);
                        }
                        else
                        {
                            reader.Skip();
                        }
                    }
                    else if (!reader.Read())
                    {
                        break;
                    }
                }
            }
        }
        /// <summary>
        /// Represents the help for a single parameter
        /// </summary>
        public class CommandHelpParam
        {
            public string Name;
            public string Help;
        }


        #region private
        private static string s_ExtensionsDirectory;

        private static Dictionary<string, object> LoadedObjects;

        internal static void ExecuteUserCommand(string command, string[] args)
        {
            object instance = null;     // The object instance to invoke
            Type instanceType = null;

            // Parse command into fileSpec and methodSpec
            var fileSpec = "Global";
            var methodSpec = command;
            var dotIndex = command.IndexOf('.');
            if (0 < dotIndex)
            {
                fileSpec = command.Substring(0, dotIndex);
                methodSpec = command.Substring(dotIndex + 1);
            }
            else
            {
                // It is a global command, first try look in PerfView itself 
                instanceType = typeof(Commands);
                if (instanceType.GetMethod(methodSpec) != null)
                {
                    instance = new Commands();
                }
            }

            // Could not find it in perfView, look in extensions.  
            if (instance == null)
            {
                // Find the instance of 'Commands' that we may have created previously, otherwise make a new one
                if (LoadedObjects == null)
                {
                    LoadedObjects = new Dictionary<string, object>();
                }

                if (!LoadedObjects.TryGetValue(fileSpec, out instance))
                {
                    var fullFilePath = Path.Combine(ExtensionsDirectory, fileSpec + ".dll");
                    if (!File.Exists(fullFilePath))
                    {
                        if (fileSpec == "Global")
                        {
                            throw new FileNotFoundException("Could not find " + methodSpec + " in PerfView's built in user commands.");
                        }

                        throw new FileNotFoundException("Could not find file " + fullFilePath + " for for user extensions.", fullFilePath);
                    }
                    var assembly = Assembly.LoadFrom(fullFilePath);

                    instanceType = assembly.GetType("Commands");
                    if (instanceType == null)
                    {
                        throw new ApplicationException("Could not find type 'Commands' in " + fullFilePath);
                    }

                    instance = Activator.CreateInstance(instanceType);
                    LoadedObjects[fileSpec] = instance;
                }
                else
                {
                    instanceType = instance.GetType();
                }
            }

            // Actually invoke the method.  
            try
            {
                instanceType.InvokeMember(methodSpec,
                   BindingFlags.InvokeMethod | BindingFlags.Instance | BindingFlags.Public | BindingFlags.OptionalParamBinding,
                   null, instance, args);
            }
            catch (TargetInvocationException ex)
            {
                // TODO we don't get the stack for the inner exception. 
                // When we move to V4.5.1 we can use ExceptionDispatchInfo to fix.  
                throw ex.InnerException;
            }
            catch (AggregateException ex)
            {
                throw ex.InnerExceptions[0];
            }
            catch (MissingMethodException)
            {
                throw new ApplicationException(string.Format(
                    "Could not find user command {0} that takes {1} arguments.  Use /userCommandHelp for help.", methodSpec, args == null ? 0 : args.Length));
            }
        }
        #endregion
    }

#if false
// TODO FIX NOW use or remove 
//
// What is the right model?
// 
// StackSource - represents the raw data.   No dependencies, Can do filtering. - Clean for Model
// CallTree - Depends on StackSource, model for treeview,  - Clean for Model
// AggregateCallTree - callers view and callees view - Clean for Model
// EventSource - eventView - Clean for Model.  
// 
// MutableTraceEventStackSource - Sources for ETL file - Clean for Model 
// 
// PerfViewItem
//     Filename, ICON, help, Expanded, Children, Open 
// PerfViewFile
// PerfViewStackSource - know their view
//     At the very least they are a model for the MainViewer's GUI.  
//     They open 
// PerfViewEventSource - know their view
// PerfViewHtmlReport
// 
// These things know about StatusBars, they know their view.   
//
// Does the automation drive the GUI or does it drive the MODEL?  
static class GuiModel
{
    public static void Wait(this StatusBar worker)
    {
        while (worker.IsWorking)
            Thread.Sleep(100);
    }

    public static PerfViewFile Open(string fileName)
    {
        var ret = PerfViewFile.Get(fileName);

        var mainWindow = GuiApp.MainWindow;
        ret.Open(mainWindow, mainWindow.StatusBar);
        mainWindow.StatusBar.Wait();
        return ret;
    }

    public static void ResolveSymbols(this PerfViewStackSource source)
    {
        var viewer = source.Viewer;
        viewer.DoLookupWarmSymbols(null, null);
        viewer.StatusBar.Wait();
    }

    public static void SetFilter(this PerfViewStackSource source, FilterParams filter)
    {
        var viewer = source.Viewer;
        viewer.Filter = filter;
        viewer.Update();
        viewer.StatusBar.Wait();
    }

    public static CallTree CallTree(this PerfViewStackSource source)
    {
        return source.Viewer.CallTree;
    }

    public static void Save(this PerfViewStackSource source, string fileName)
    {
        var viewer = source.Viewer;
        viewer.FileName = fileName;
        viewer.DoSave(null, null);
        viewer.StatusBar.Wait();
    }
}
#endif
    #endregion
}

// PerfViewModel contains things that are not very important for the user to see 
// but are never the less part of the model.  
namespace PerfViewModel
{
    /// <summary>
    /// This class effectively serializes GUI state in the StackWindow.  It is 
    /// simply a strongly typed version of the XML data.  
    /// </summary>
    public class StackWindowGuiState
    {
        public StackWindowGuiState() { FilterGuiState = new FilterGuiState(); }
        public StackWindowGuiState ReadFromXml(XmlReader reader)
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                throw new InvalidOperationException("Must advance to XML element (e.g. call ReadToDescendant)");
            }

            var inputDepth = reader.Depth;
            // This is here for backward compatibility.  Can remove after 2013.  
            TabSelected = reader.GetAttribute("TabSelected");

            reader.Read();      // Advance to children 
            while (inputDepth < reader.Depth)
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    string valueStr;
                    switch (reader.Name)
                    {
                        case "FilterGuiState":
                            FilterGuiState.ReadFromXml(reader);
                            break;
                        case "Notes":
                            Notes = reader.ReadElementContentAsString().Trim();
                            break;
                        case "Log":
                            Log = reader.ReadElementContentAsString().Trim();
                            break;
                        case "Columns":
                            Columns = TextBoxGuiState.ReadStringList(reader);
                            break;
                        case "NotesPaneHidden":
                            valueStr = reader.ReadElementContentAsString().Trim();
                            NotesPaneHidden = string.Compare(valueStr, "True", StringComparison.OrdinalIgnoreCase) == 0;
                            break;
                        case "ScalingPolicy":
                            valueStr = reader.ReadElementContentAsString().Trim();
                            if (string.Compare(valueStr, "TimeMetric", StringComparison.OrdinalIgnoreCase) == 0)
                            {
                                ScalingPolicy = ScalingPolicyKind.TimeMetric;
                            }
                            else
                            {
                                Debug.Assert(string.Compare(valueStr, "ScaleToData", StringComparison.OrdinalIgnoreCase) == 0);
                            }

                            break;
                        case "TabSelected":
                            TabSelected = reader.ReadElementContentAsString().Trim();
                            break;
                        case "FocusName":
                            FocusName = reader.ReadElementContentAsString().Trim();
                            break;
                        //case "ByNameSelection":
                        //    ByNameSelection = reader.ReadElementContentAsString().Trim();
                        //    break;
                        //case "CallTreeSelection":
                        //    CallTreeSelection = reader.ReadElementContentAsString().Trim();
                        //    break;
                        //case "CalleesSelection":
                        //    CalleesSelection = reader.ReadElementContentAsString().Trim();
                        //    break;
                        //case "CallersSelection":
                        //    CallersSelection = reader.ReadElementContentAsString().Trim();
                        //    break;
                        // This is here for backward compatibility.  Can remove after 2013.  
                        case "CallerCallee":
                            FocusName = reader.GetAttribute("Focus");
                            reader.Skip();
                            break;
                        default:
                            Debug.WriteLine("Skipping unknown element {0}", reader.Name);
                            reader.Skip();
                            break;
                    }
                }
                else if (!reader.Read())
                {
                    break;
                }
            }
            return this;
        }
        public void WriteToXml(string name, XmlWriter writer)
        {
            writer.WriteStartElement(name);
            FilterGuiState.WriteToXml("FilterGuiState", writer);

            writer.WriteElementString("Notes", Notes);
            writer.WriteElementString("Log", XmlUtilities.XmlEscape(Log));
            if (Columns != null)
            {
                writer.WriteStartElement("Columns");
                foreach (var columnName in Columns)
                {
                    writer.WriteElementString("string", columnName);
                }

                writer.WriteEndElement();
            }
            writer.WriteElementString("NotesPaneHidden", NotesPaneHidden.ToString());
            writer.WriteElementString("ScalingPolicy", ScalingPolicy.ToString());
            writer.WriteElementString("TabSelected", TabSelected);
            writer.WriteElementString("FocusName", FocusName);

            //writer.WriteElementString("ByNameSelection", ByNameSelection);
            //writer.WriteElementString("CallTreeSelection", CallTreeSelection);
            //writer.WriteElementString("CalleesSelection", CalleesSelection);
            //writer.WriteElementString("CallersSelection", CallersSelection);

            writer.WriteEndElement();
        }

        // Filter 
        public FilterGuiState FilterGuiState;
        public string Notes;
        public string Log;

        // Global attributes.  
        public List<string> Columns;
        public bool NotesPaneHidden;
        public ScalingPolicyKind ScalingPolicy;

        // What tab is selected
        public string TabSelected;
        public string FocusName;

        // Where the cursor is in each tab 
        //public string ByNameSelection;
        //public string CallTreeSelection;
        //public string CallersSelection;
        //public string CalleesSelection;
    }

    /// <summary>
    /// This class effectively serializes the GUI state of the filter parameters.  It is 
    /// simply a strongly typed version of the XML data.  
    /// </summary>
    public class FilterGuiState
    {
        public FilterGuiState()
        {
            Start = new TextBoxGuiState();
            End = new TextBoxGuiState();
            Scenarios = new TextBoxGuiState();
            GroupRegEx = new TextBoxGuiState();
            FoldPercent = new TextBoxGuiState();
            FoldRegEx = new TextBoxGuiState();
            IncludeRegEx = new TextBoxGuiState();
            ExcludeRegEx = new TextBoxGuiState();
            TypePriority = new TextBoxGuiState();
        }
        public FilterGuiState ReadFromXml(XmlReader reader)
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                throw new InvalidOperationException("Must advance to XML element (e.g. call ReadToDescendant)");
            }

            var inputDepth = reader.Depth;
            reader.Read();      // Advance to children 
            while (inputDepth < reader.Depth)
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    switch (reader.Name)
                    {
                        case "Start":
                            Start.ReadFromXml(reader, true);
                            break;
                        case "End":
                            End.ReadFromXml(reader, true);
                            break;
                        case "GroupRegEx":
                            GroupRegEx.ReadFromXml(reader);
                            break;
                        case "FoldPercent":
                            FoldPercent.ReadFromXml(reader, true);
                            break;
                        case "FoldRegEx":
                            FoldRegEx.ReadFromXml(reader);
                            break;
                        case "IncludeRegEx":
                            IncludeRegEx.ReadFromXml(reader);
                            break;
                        case "ExcludeRegEx":
                            ExcludeRegEx.ReadFromXml(reader);
                            break;
                        case "TypePriority":
                            TypePriority.ReadFromXml(reader);
                            break;
                        case "Scenarios":
                            Scenarios.ReadFromXml(reader);
                            break;
                        default:
                            Debug.WriteLine("Skipping unknown element {0}", reader.Name);
                            reader.Skip();
                            break;
                    }
                }
                else if (!reader.Read())
                {
                    break;
                }
            }
            return this;
        }
        public void WriteToXml(string name, XmlWriter writer)
        {
            writer.WriteStartElement(name);
            Start.WriteToXml("Start", writer);
            End.WriteToXml("End", writer);
            Scenarios.WriteToXml("Scenarios", writer);
            GroupRegEx.WriteToXml("GroupRegEx", writer);
            FoldPercent.WriteToXml("FoldPercent", writer);
            FoldRegEx.WriteToXml("FoldRegEx", writer);
            IncludeRegEx.WriteToXml("IncludeRegEx", writer);
            ExcludeRegEx.WriteToXml("ExcludeRegEx", writer);
            ExcludeRegEx.WriteToXml("TypePriority", writer);
            writer.WriteEndElement();
        }

        public TextBoxGuiState Start;
        public TextBoxGuiState End;
        public TextBoxGuiState GroupRegEx;
        public TextBoxGuiState FoldPercent;
        public TextBoxGuiState FoldRegEx;
        public TextBoxGuiState IncludeRegEx;
        public TextBoxGuiState ExcludeRegEx;
        public TextBoxGuiState TypePriority;
        public TextBoxGuiState Scenarios;
    }

    /// <summary>
    /// This class effectively serializes the GUI state a single historyTextBox.  It is 
    /// simply a strongly typed version of the XML data.  
    /// 
    /// 
    /// </summary>
    public class TextBoxGuiState
    {
        public TextBoxGuiState() { }
        /// <summary>
        /// Assumes we are ON the 'MyName' node below.  Readers in the values.  
        /// Leaves the reader on the EndElement.   
        ///    <MyName>
        ///      <Value>myValue</Value>
        ///      <History>
        ///        <string>old</string>
        ///      </History>
        ///    </MyName>
        /// </summary>
        public TextBoxGuiState ReadFromXml(XmlReader reader, bool validateIsDouble = false)
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                throw new InvalidOperationException("Must advance to XML element (e.g. call ReadToDescendant)");
            }

            var inputDepth = reader.Depth;
            reader.Read();      // Advance to children 
            while (inputDepth < reader.Depth)
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (reader.Name == "Value")
                    {
                        Value = reader.ReadElementContentAsString().Trim();
                    }
                    else if (reader.Name == "History")
                    {
                        History = ReadStringList(reader);
                    }
                    else
                    {
                        reader.Skip();
                    }
                }
                // This is here for compatibilty
                else if (reader.NodeType == XmlNodeType.Text || Value == null)
                {
                    Value = reader.ReadString().Trim();
                }
                else if (!reader.Read())
                {
                    break;
                }
            }

            // If the value needs to be a syntatically correct double check and set to emp
            double dummy;
            if (validateIsDouble && Value != null && !double.TryParse(Value, out dummy))
            {
                Value = null;
            }

            return this;
        }
        public void WriteToXml(string name, XmlWriter writer)
        {
            writer.WriteStartElement(name);
            if (Value != null)
            {
                writer.WriteElementString("Value", Value);
            }

            if (History != null)
            {
                writer.WriteStartElement("History");
                foreach (var str in History)
                {
                    writer.WriteElementString("string", str);
                }

                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
        public string Value;
        public List<string> History;

        // TODO does not really belong here, it is generic code. 
        /// <summary>
        /// Reads a string list in XMLSerialization format.   Assumes we are on MyList element
        /// to start, and ends having read the end element of MyList.  
        ///      <MyList>
        ///        <string>elem1</string>
        ///        <string>elem2</string>
        ///        <string>elem3</string>
        ///      </MyList>
        /// </summary>
        internal static List<string> ReadStringList(XmlReader reader)
        {
            var ret = new List<string>();
            if (reader.NodeType != XmlNodeType.Element)
            {
                throw new InvalidOperationException("Must advance to XML element (e.g. call ReadToDescendant)");
            }

            var inputDepth = reader.Depth;
            reader.Read();      // Advance to children 
            while (inputDepth < reader.Depth)
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    // HistoryItem and Column is there for compatibility.  Can be removed after 2013
                    if (reader.Name == "string" || reader.Name == "HistoryItem")
                    {
                        ret.Add(reader.ReadElementContentAsString().Trim());
                    }
                    else if (reader.Name == "Column")
                    {
                        ret.Add(reader.GetAttribute("Name").Trim());
                        reader.Skip();
                    }
                    else
                    {
                        reader.Skip();
                    }
                }
                else if (!reader.Read())
                {
                    break;
                }
            }
            return ret;
        }
    }
}