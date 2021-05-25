#define DEMOS
using Graphs;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Stacks;
using PerfView;
using PerfView.GuiUtilities;
using PerfViewExtensibility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Address = System.UInt64;

// HOW PERFVIEW SUPPORTS USER EXTENSIONS
// 
// When you execute
// 
//     PerfView UserCommand Global.Demonstration arg1 arg2 arg3
//     
// PerfView will look for a DLL called 'PerfViewExtensions\Global.dll next to PerfView.exe
// It will then look for a type call 'Commands' and create an instance of it.    Then it looks 
// for a method within that type called 'Demonstration' It then passes the rest of the parameteters 
// of the command to that method.  
// 
// If the target method target is varags (its last argument is 'params string[]') then that is 
// also supported.   In this case Demonstration is varags, so I can pass any number of arguments to it. 
// 
// The extension named 'Global' is special in that if the user command has no '.' in his user
// command, the extension is assumed to be 'Global' extension.   Thus the command above could 
// be shorted to
// 
//     PerfView UserCommand Demonstration arg1 arg2 arg3 
/**********************************************************************************************/
// CREATING A NEW PERFVIEW EXTENSION
// 
// PerView has a speical command called CreateExtentionTemplate that make creating a new extension 
// very easy (assuming you have Visual Studio 2010 or later installed).   To make a new extension
// Simply invoke the command
//
//     PerfView CreateExtensionProject EXTENSION_NAME
// 
// This will create a directory 'PerfViewExtensions' next to the PerfView.exe file.  It will also 
//
//     Create PerfViewExtensions\EXTENSION_NAMESrc with a .csFile and template to get you started
//     Create/Update PerfViewExtentions\Extensions.SLN to include all extensions projects in the
//          PerfViewExtensions directory.  
//
// If EXTENSION_NAME is not given the extension name 'Global' is assumed 
// 
// Thus to get going you simply need to run 
//
//     PerfView CreateExtensionProject
// 
// And then open the PerfViewExtensions\Extensions.sln file that was created/updated.  
//
// This file comes with a 'Commands' class with a 'Demonstration' command in it.  Set a breakpoint
// and hit F5 to start debugging your first PerfView Extension.  
/**********************************************************************************************/
// EXPLORING THE PERFVIEW OBJECT MODEL
//
//     1) INTELLISENSE IS YOUR FRIEND!  Only the PerfViewExtensibility is open by default and this
//        is where the most important classes in PerfView's object model reside.  This means that
//        there is a good chance if you type some characters, you will find what you are looking for.
//     2) CommmandEnvironment is a good place to start.   This is the class that defines 'global'
//        methods.  If you select on the CommmandEnvironment below and hit F12, you can browse the
//        other global methods.  These methods will return other important types in the object model
//        (e.g. EtlFile, Events, Stacks).  
//     3) Understand classes in PerfViewExtensibility first.  You can use the object browser (Ctrl-W J)
//        and look under the PerfView.PerfViewExtensibility namespace.
//     4) Take a look at the example commands.  These use many of the important features (logging,
//        symbol lookup, html report) in context, which is quite helpeful.  
/**********************************************************************************************/
// TIPS AND TRICKS
//
//     1) LogFile.WriteLine messages surrounded by [] will also be displayed in the Status Line 
//        (of the main window)
/**********************************************************************************************/

/// <summary>
/// The methods in this Commands class is the class that is called from PerfView 'UserCommand DLLNAME.Method'
/// 
/// Commands can have varags (params argument qualifier) and can also have default arguments.   
/// 
/// To use them: PerfView UserCommand METHODNAME arg1 arg2 ...
/// </summary>
public class Commands : CommandEnvironment
{
    // TODO please remove these Demo commands and replace them with your own.  They are here as templates.  
#if DEMOS
    /// <summary>
    /// A quick example of a command that has default arguments.  You can also use the 'params' style
    /// varargs.  
    /// </summary>
    /// <param name="requiredArg1">A demo of a required argument</param>
    /// <param name="optionalArg2">A demo of a optional argument</param>
    /// <param name="optionalArg3">A demo of a optional argument</param>
    public void DemoCommandWithDefaults(string requiredArg1, string optionalArg2 = "arg2", string optionalArg3 = "arg3")
    {
        LogFile.WriteLine("Ran CommandWithDefaults arg1 = {0} arg2 = {1} arg3 = {2}", requiredArg1, optionalArg2, optionalArg3);
    }

    /// <summary>
    /// This command creates a simple HTML report that has links that in turn cause actions.  If you make custom reports you 
    /// are likely to want to do this.  
    /// </summary>
    /// <param name="args">a list of strings that will appear in the report</param>
    public void DemoHtmlReport(params string[] args)
    {
        LogFile.WriteLine("[Loading Extension DLL {0}]", System.Reflection.Assembly.GetExecutingAssembly().ManifestModule.FullyQualifiedName);

        var htmlFileName = CreateUniqueCacheFileName("DemoHtmlReport", ".html");
        using (var htmlWriter = File.CreateText(htmlFileName))
        {
            htmlWriter.WriteLine("<h1>Demonstration Title</h1>");
            htmlWriter.WriteLine("<ol>");
            for (int i = 0; i < args.Length; i++)
            {
                htmlWriter.WriteLine("<li>Param <a href=\"command:{0}\">{0}</a></li>", args[i]);
            }

            htmlWriter.WriteLine("<li>Param <a href=\"#myAnchor\">Goto another Header</a></li>");
            htmlWriter.WriteLine("<li>Param <a href=\"command:ClearPage\">Will Clear the current page (demonstrates updating page in place)</a></li>");

            htmlWriter.WriteLine("</ol>");
            for (int i = 0; i < 20; i++)
            {
                htmlWriter.WriteLine("<p>&nbsp;</p>");  // Create some whitespace 
            }

            htmlWriter.WriteLine("<h1><a id=\"myAnchor\">Another Header</a></h1>");
            htmlWriter.WriteLine("<p>This is just a demonstration of linking to other parts of the same page.</p>");  // Create some whitespace 
        }

        LogFile.WriteLine("[Opening {0}]", htmlFileName);
        OpenHtmlReport(htmlFileName, "Demonstration", delegate (string command, TextWriter log, WebBrowserWindow window)
        {
            // This gets called when hyperlinks with a url that begin with 'command:' are clicked.   

            // You can call OpenHtmlReport to open new pages.  

            // Or you can update your current page
            if (command == "ClearPage")
            {
                // Here I update in place, but I could have simply nativated somewhere else.  
                File.WriteAllText(htmlFileName, "Cleared all Text.");

                // We are not on the GUI thread, so any time we interact with the GUI thread we must dispatch to it.  
                window.Dispatcher.BeginInvoke((Action)delegate
                {
                    window.Navigate(htmlFileName);
                });
                // You can also get the window.Browser DOM object and fiddle with elements directly.   
                return;
            }

            // Typically you open up new Html windows.  
            log.WriteLine("[clicked on command {0}, simulating a command that takes 3 sec]", command);
            // You can do long operations here, We simulate this with a 3 second delay
            System.Threading.Thread.Sleep(3000);
        });
    }

    /// <summary>
    /// This demo scans a particular ETL file for a strongly typed events (in this case Image Load events)
    /// and prints them out.  
    /// </summary>
    public void DemoLoadLibrary(string etlFileName)
    {
        using (var etlFile = OpenETLFile(etlFileName))
        {
            // If the user asked to focus on one process, do so.  
            TraceEvents events = GetTraceEventsWithProcessFilter(etlFile);

            // Using this callback model is more efficient than the 'foreach' model because each event goes to
            // its callback already strongly typed to exactly the right type for the event.   Search for subclasses
            // of TraceEventParser to see what strongly typed events exist.  The Kernel and CLR events have these
            // and you can even make TraceEventParser's for your own events.  
            var traceEventSource = events.GetSource();

            // Set it up so that this code gets called back every time an ImageLoad event is encountered. 
            traceEventSource.Kernel.ImageLoad += delegate (ImageLoadTraceData data)
            {
                LogFile.WriteLine("At {0:f3} Msec loaded at 0x{1:x} {2}", data.TimeStampRelativeMSec, data.ImageBase, data.FileName);
            };

            // Process all the events in the 'events' list (do the callbacks that have been set up)
            traceEventSource.Process();
            LogFile.WriteLine("[See log file for report]");     // Tell the user where the info is.  
        }
    }

    /// <summary>
    /// This demo scans a particular ETL file for events of a particular type (in this case PerfView events)
    /// and prints them to the log.  
    /// </summary>
    /// <param name="etlFileName">The ETL file to open</param>    
    public void DemoEventSearch(string etlFileName)
    {
        using (var etlFile = OpenETLFile(etlFileName))
        {
            var events = GetTraceEventsWithProcessFilter(etlFile);
            // To filter by a specific time 
            // events = events.FilterByTime(0.0, double.PositiveInfinity);

            // Here is how you look for events from a particular provider.  Note that this is relatively inefficient.
            // But still acceptable for lots of scnearios, and has the advantage of being very straightforward. 
            // If have a high volume scenario see http://toolbox/TraceparserGen for creating strongly typed event records.  
            foreach (TraceEvent _event in events)
            {
                if (_event.ProviderName == "PerfView")        // Is it from PerfView?
                {
                    LogFile.WriteLine("Got PerfView event {0} ", _event.ToString());
                    if (_event.EventName == "CommandLineParameters")            // Is it CommandLineparameters Event?
                    {
                        var commandLine = _event.PayloadByName("commandLine");              // Get Individual fields
                        var currentDirectory = _event.PayloadByName("currentDirectory");    // Get Individual fields
                        LogFile.WriteLine("Parsed PerfView CommandLineParams {0} {1} ", commandLine, currentDirectory);
                    }
                }
            }
        }
    }

    /// <summary>
    /// A trivial example of opening a PerfView XML file and then displaying it in the viewer.  
    /// </summary>
    /// <param name="fileName"></param>
    public void DemoOpenPerfViewXml(string fileName)
    {
        var myStacks = OpenPerfViewXmlFile(fileName);

        // Here you can set up the stacks anyway you like it and then display 
        // myStacks.Filter.GroupRegExs = "MyDll!->MyGroup";

        LogFile.WriteLine("[Opening viewer on {0}]", fileName);
        OpenStackViewer(myStacks);
    }

    /// <summary>
    /// Trivial demo that opens an ETL file named 'eltFileName' and opens the view on the CPU data. 
    /// </summary>
    /// <param name="etlFileName"></param>
    public void DemoOpenCpuStacks(string etlFileName)
    {
        var etlFile = OpenETLFile(etlFileName);
        Stacks stacks = etlFile.CPUStacks();
        OpenStackViewer(stacks);
    }

    /// <summary>
    /// If you wish to programatically get at events in the ETL file you typically will want 
    /// to use the etlFile.TraceLog.Events directly (they are more efficient).   This is what
    /// is done in the DemoEventSearch, DemoLoadLibrary, and DemoStartupReport cases. 
    /// 
    /// However if you simply want to control the PerfView event viewer and open it, then using
    /// the etlFile.Events class is what you want since you can open the event viewer on it 
    /// easily.  
    /// 
    /// In this demo, we open a viewer for just the process and image load events.  
    /// </summary>
    /// <param name="etlFileName">The ETL file to open</param>
    public void DemoOpenEventView(string etlFileName)
    {
        using (var etlFile = OpenETLFile(etlFileName))
        {
            var events = etlFile.Events;

            // Pick out the desired events. 
            var desiredEvents = new List<string>();
            foreach (var eventName in events.EventNames)
            {
                if (eventName.Contains("Process") || eventName.Contains("Image"))
                {
                    desiredEvents.Add(eventName);
                }
            }
            events.SetEventFilter(desiredEvents);
            LogFile.WriteLine("[Opening event viewer on {0}]", etlFileName);
            OpenEventViewer(events);
        }
    }

    /// <summary>
    /// Here is an example of a 'real' report.  It computes startup statistics for a given process.  
    /// </summary>
    /// <param name="etlFileName">The ETL file to open</param>
    /// <param name="processName">The name of the process to focus on.  If not present, the first process to start is used.</param>
    public void DemoStartupReport(string etlFileName, string processName = null)
    {
        using (var etlFile = OpenETLFile(etlFileName))
        {
            if (processName != null)
            {
                CommandLineArgs.Process = processName;
            }

            TraceProcess process = null;
            if (CommandLineArgs.Process != null)
            {
                process = etlFile.Processes.LastProcessWithName(CommandLineArgs.Process);
                if (process == null)
                {
                    throw new ApplicationException("Could not find process named " + CommandLineArgs.Process);
                }

                LogFile.WriteLine("Focusing on process: {0} ({1}) starting at {2:n3} Msec",
                    process.Name, process.ProcessID, process.StartTimeRelativeMsec);
            }
            else
            {
                // Find the first process that actually started in the trace.     
                foreach (var processInFile in etlFile.Processes)
                {
                    if (0 < processInFile.StartTimeRelativeMsec)
                    {
                        process = processInFile;
                    }
                }
                if (process == null)
                {
                    throw new ApplicationException("No process started in the trace.");
                }

                LogFile.WriteLine("Focusing on first process: {0} ({1}) starting at {2:n3} Msec",
                    process.Name, process.ProcessID, process.StartTimeRelativeMsec);
            }

            // We define idle as the first time there this many msecs goes by without the process consuming 
            // CPU or disk.  Because the Prefetcher can keep the process 'idle' as it does bulk I/O for longer
            // than this we require that at least 3 DLL be loaded (since the Prefetcher happens VERY early).  
            double idleDurationMSec = 200;      // todo Make this a parameter.  

            // Using this callback model is more efficient than the 'foreach' model because each event goes to
            // its callback already strongly typed to exactly the right type for the event.   Search for subclasses
            // of TraceEventParser to see what strongly typed events exist.  The Kernel and CLR events have these
            // and you can even make TraceEventParser's for your own events.  
            TraceEvents events = process.EventsInProcess;
            var traceEventSource = events.GetSource();

            double startTimeRelativeMSec = process.StartTimeRelativeMsec;

            /* Walk the events, gathering stats on I/O and CPU */
            double lastNonIdleRelativeMSec = startTimeRelativeMSec;           // used to compute idle 
            int imageLoadCount = 0;                                 // used to compute idle

            var CpuTimeByDllMSec = new Dictionary<string, double>();
            double CpuTimeMSecTotal = 0;

            var IOTimeByFileMSec = new Dictionary<string, double>();
            double IOTimeTotalMSec = 0;
            var lastIObyDisk = new double[4];                       // Needed to compute service time.  expanded as needed. 

            traceEventSource.Kernel.ImageLoad += delegate (ImageLoadTraceData data)
            {
                imageLoadCount++;
            };

            traceEventSource.Kernel.DiskIORead += delegate (DiskIOTraceData data)
            {
                // Stop if we have hit idle 
                if (imageLoadCount > 2 && data.TimeStampRelativeMSec - lastNonIdleRelativeMSec > idleDurationMSec)
                {
                    LogFile.WriteLine("Processing stopped at {0:n3} MSec", data.TimeStampRelativeMSec);
                    traceEventSource.StopProcessing();
                }
                lastNonIdleRelativeMSec = data.TimeStampRelativeMSec;

                if (data.DiskNumber >= lastIObyDisk.Length)
                {
                    var newLastIObyDisk = new double[data.DiskNumber + 1];
                    Array.Copy(lastIObyDisk, newLastIObyDisk, lastIObyDisk.Length);
                    lastIObyDisk = newLastIObyDisk;
                }

                // Service time is defined as the time the disk is actually servicing your requests (not waiting 
                // for the requests in front of it to complete).   This can be computed by taking the Min of 
                // the Elapsed time and the time since the last I/O time from that disk (which is when it got to the
                // front of the queue).  
                double serviceTimeMSec = Math.Min(data.ElapsedTimeMSec, data.TimeStampRelativeMSec - lastIObyDisk[data.DiskNumber]);

                // Accumulate I/O time 
                var fileName = data.FileName;
                double fileIOTime = 0;
                IOTimeByFileMSec.TryGetValue(fileName, out fileIOTime);
                IOTimeByFileMSec[fileName] = fileIOTime + serviceTimeMSec;

                IOTimeTotalMSec += serviceTimeMSec;
                lastIObyDisk[data.DiskNumber] = data.TimeStampRelativeMSec;
            };
            traceEventSource.Kernel.PerfInfoSample += delegate (SampledProfileTraceData data)
            {
                // Stop if we have hit idle 
                if (imageLoadCount > 2 && data.TimeStampRelativeMSec - lastNonIdleRelativeMSec > idleDurationMSec)
                {
                    LogFile.WriteLine("Processing stoped at {0:n3} Msec", data.TimeStampRelativeMSec);
                    traceEventSource.StopProcessing();
                }
                lastNonIdleRelativeMSec = data.TimeStampRelativeMSec;

                // Accumulate CPU time by dll 
                double sampleCpuMSec = etlFile.TraceLog.SampleProfileInterval.TotalMilliseconds;
                CpuTimeMSecTotal += sampleCpuMSec;

                var moduleFilePath = "UNKNOWN";
                var codeAddrIdx = data.IntructionPointerCodeAddressIndex();
                if (codeAddrIdx != CodeAddressIndex.Invalid)
                {
                    var moduleFile = data.Log().CodeAddresses.ModuleFile(codeAddrIdx);
                    if (moduleFile != null)
                    {
                        moduleFilePath = moduleFile.FilePath;
                    }
                }
                if (!string.IsNullOrEmpty(moduleFilePath))
                {
                    double curCpuTimeByDll = 0;
                    CpuTimeByDllMSec.TryGetValue(moduleFilePath, out curCpuTimeByDll);
                    CpuTimeByDllMSec[moduleFilePath] = curCpuTimeByDll + sampleCpuMSec;
                }
            };

            // Process all the events in the 'events' list (do the callbacks that have been set up)
            traceEventSource.Process();

            /* OK, my statistics variables have been udpated.   Generate the report. */
            var htmlFileName = CreateUniqueCacheFileName("StartupReport", ".html");
            using (var htmlWriter = File.CreateText(htmlFileName))
            {
                htmlWriter.WriteLine("<h1>Startup report for {0}</h1>", process.Name);
                htmlWriter.WriteLine("<ol>");
                htmlWriter.WriteLine("<li>Total Startup time: {0:n3} MSec</li>", lastNonIdleRelativeMSec - startTimeRelativeMSec);
                htmlWriter.WriteLine("<li>Total CPU time:     {0:n3} MSec</li>", CpuTimeMSecTotal);
                htmlWriter.WriteLine("<li>Total I/O time:     {0:n3} MSec</li>", IOTimeTotalMSec);
                htmlWriter.WriteLine("</ol>");

                htmlWriter.WriteLine("<h2>CPU Breakdown by DLL</h2>");
                htmlWriter.WriteLine("<table border=\"1\">");
                htmlWriter.WriteLine("<TR><TH>Dll Name</TH><TH>Cpu MSec</TH></TR>");

                var dllNames = new List<string>(CpuTimeByDllMSec.Keys);
                // Sort decending by CPU time.  
                dllNames.Sort((x, y) => CpuTimeByDllMSec[y].CompareTo(CpuTimeByDllMSec[x]));
                foreach (var dllName in dllNames)
                {
                    htmlWriter.WriteLine("<TR><TD>{0}</TD><TD>{1:n3}</TD></TR>", Path.GetFileName(dllName), CpuTimeByDllMSec[dllName]);
                }

                htmlWriter.WriteLine("</table>");

                htmlWriter.WriteLine("<h2>I/O Breakdown by DLL</h2>");
                htmlWriter.WriteLine("<table border=\"1\">");
                htmlWriter.WriteLine("<TR><TH>File Name</TH><TH>I/O MSec</TH></TR>");

                var fileNames = new List<string>(IOTimeByFileMSec.Keys);
                // Sort decending by I/O time.  
                fileNames.Sort((x, y) => IOTimeByFileMSec[y].CompareTo(IOTimeByFileMSec[x]));
                foreach (var fileName in fileNames)
                {
                    htmlWriter.WriteLine("<TR><TD>{0}</TD><TD>{1:n3}</TD></TR>", Path.GetFileName(fileName), IOTimeByFileMSec[fileName]);
                }

                htmlWriter.WriteLine("</table>");
            }

            LogFile.WriteLine("[Opening {0}]", htmlFileName);
            OpenHtmlReport(htmlFileName, "Startup Report", delegate (string command, TextWriter log, WebBrowserWindow window)
            {
                // This gets called when hyperlinks with a url that begin with 'command:' are clicked.   
                // Typically you open up new Html windows or create CSV files and use OpenExcel
                log.WriteLine("[clicked on command {0}]", command);
            });
        }
    }

    /// <summary>
    /// This demonstrates how to use the CallTree view problematically.  
    /// This demo takes a set of DLL names, and then assigns all CPU time to whichever
    /// member of the set is hit first crawling from the IP to the top of the stack.
    /// 
    /// The idea is that you want to 'blame' one of small set of DLLs, and you want
    /// to ignore all other dlls that are not in your set (e.g. OS dlls), and assign
    /// any time they had one in your set.  
    /// </summary>
    /// <param name="etlFileName">The ETL file to open</param>
    /// <param name="processName">The name of the process to focus on (null means first pr</param>
    /// <param name="dllNames">The list of DLLs names (file names without extensions) to blame.</param>
    public void DemoStacksReportByDll(string etlFileName, string processName = null, params string[] dllNames)
    {
        using (var etlFile = OpenETLFile(etlFileName))
        {
            if (processName != null)
            {
                CommandLineArgs.Process = processName;
            }

            TraceProcess process = null;
            if (CommandLineArgs.Process != null)
            {
                process = etlFile.Processes.LastProcessWithName(CommandLineArgs.Process);
                if (process == null)
                {
                    throw new ApplicationException("Could not find process named " + CommandLineArgs.Process);
                }

                LogFile.WriteLine("Focusing on process: {0} ({1}) starting at {2:n3} Msec",
                    process.Name, process.ProcessID, process.StartTimeRelativeMsec);
                etlFile.SetFilterProcess(process);
            }
            else
            {
                // Find the first process that actually started in the trace.     
                foreach (var processInFile in etlFile.Processes)
                {
                    if (0 < processInFile.StartTimeRelativeMsec)
                    {
                        process = processInFile;
                    }
                }
                if (process == null)
                {
                    throw new ApplicationException("No process started in the trace.");
                }

                LogFile.WriteLine("Focusing on first process: {0} ({1}) starting at {2:n3} Msec",
                    process.Name, process.ProcessID, process.StartTimeRelativeMsec);
                etlFile.SetFilterProcess(process);
            }
            var myStacks = etlFile.CPUStacks();

            if (dllNames.Length == 0)
            {
                throw new ApplicationException("Must provide a list of DLLs to group by");
            }

            // Set up the filter the way we want it. 
            var groups = new StringBuilder();
            foreach (var dllName in dllNames)
            {
                groups.Append(dllName).Append("!-> module ").Append(dllName).Append(';');
            }

            groups.Append("!->OTHER");
            myStacks.Filter.GroupRegExs = groups.ToString();
            myStacks.Filter.FoldRegExs = "^Thread;^OTHER";
            myStacks.Filter.MinInclusiveTimePercent = "";
            myStacks.Update();
            LogFile.WriteLine("Filter:\r\n{0}", myStacks.Filter.ToXml());

            // We could just open the Stack viewer at this point
            // LogFile.WriteLine("[Opening viewer on {0}]", etlFileName);
            // OpenStackViewer(myStacks);

            // But we will demonstrate instead how to create an HTML report from calltree.  
            var htmlFileName = CreateUniqueCacheFileName("CpuByDllReport", ".html");
            using (var htmlWriter = File.CreateText(htmlFileName))
            {
                htmlWriter.WriteLine("<h1>CPU by DLL Report</h1>");

                htmlWriter.WriteLine("<ol>");
                htmlWriter.WriteLine("<li>Total CPU time:     {0:n3} MSec</li>", myStacks.CallTree.Root.InclusiveMetric);
                htmlWriter.WriteLine("<li>Focus Dlls: {0}</li>", string.Join(" ", dllNames));
                htmlWriter.WriteLine("</ol>");

                htmlWriter.WriteLine("<table border=\"1\">");
                htmlWriter.WriteLine("<TR><TH>Dll Name</TH><TH>Cpu MSec</TH></TR>");

                List<CallTreeNodeBase> byNames = myStacks.CallTree.ByIDSortedExclusiveMetric();
                foreach (CallTreeNodeBase byName in byNames)
                {
                    if (byName.ExclusiveMetric == 0)
                    {
                        continue;
                    }

                    htmlWriter.WriteLine("<TR><TD>{0}</TD><TD>{1:n3}</TD></TR>", byName.Name, byName.ExclusiveMetric);
                }
            }

            LogFile.WriteLine("[Opening {0}]", htmlFileName);
            OpenHtmlReport(htmlFileName, "Cpu By DLL Report", delegate (string command, TextWriter log, WebBrowserWindow window)
            {
                // This gets called when hyperlinks with a url that begin with 'command:' are clicked.   
                // Typically you open up new Html windows or create CSV files and use OpenExcel
                log.WriteLine("[clicked on command {0}]", command);
            });

        }
        return;
    }

    /// <summary>
    /// Commands with many options need more complex argument parsing, which is what 'CommandLineParser does.  
    /// Here is an example of adding two optional qualifiers as well as parameters.  
    /// </summary>
    public void DemoCommandWithQualifiers(params string[] args)
    {
        // If you need lots of possible parameters that are optionally present you should use the CommandLineParser
        // to parse the arguments (like what is done for the PerfView command line itself). 
        var commandLineParser = new Utilities.CommandLineParser(args);
        string Process = null;
        bool LookupAllSymbols = false;
        string DataFile = "PerfViewDataFile.etl";
        commandLineParser.DefineOptionalQualifier("Process", ref Process, "The process to focus on.  If absent all processes selected.");
        commandLineParser.DefineOptionalQualifier("LookupAllSymbols", ref LookupAllSymbols,
            "If true, it looks all symbols (not just warm ones in the cache).   Can be slow.");
        commandLineParser.DefineOptionalParameter("DataFile", ref DataFile, "The data file to open.");
        commandLineParser.CompleteValidation();
        if (commandLineParser.HelpRequested != null)
        {
            LogFile.Write(commandLineParser.GetHelp(80));
            return;
        }
        // Now Parser, ResolveAllSymbols, DataFile are set (and users can get help on your command). 
        LogFile.WriteLine("Got CommandWithArgs dataFile = {0} process = {1} LookupAllSymbols = {2}", DataFile, Process, LookupAllSymbols);
    }

    /// <summary>
    /// This routine demos the use of the 'ConfigData' to save state across invokations of the perfView program. 
    /// </summary>
    /// <param name="value">A value to remember between invokations</param>
    public void DemoConfigSettings(string value = "a value")
    {
        LogFile.WriteLine("[Remembering {0} even after program exits.  Previous value = {1}]",
            value, ConfigData["MyDataKey"]);

        ConfigData["MyDataKey"] = value;


        LogFile.WriteLine("Existing persisted values");
        foreach (var keyValue in ConfigData)
        {
            LogFile.WriteLine("   ConfigValue[{0}] = {1}", keyValue.Key, keyValue.Value);
        }
    }

    /// <summary>
    /// This demo shows you how to create a a graph of memory and turn it into a stackSource with MemoryGraphStackSource. 
    /// </summary>
    public void DemoMemoryGraph()
    {
        // Make a custom stack source that was created out of nothing.   InternStackSouce is your friend here. 
        MemoryGraph myGraph = new MemoryGraph(1000);

        // Create a memory graph out of 'nothing'.  In the example below we create a graph where the root which points at
        // 'base' which has a bunch of children, each of which have a child that points back to the base node (thus it has lots of cycles)

        var baseIdx = myGraph.GetNodeIndex(0);        // Define the NAME (index) for the of the graph (but we have not defined what is in it)

        GrowableArray<NodeIndex> children = new GrowableArray<NodeIndex>(1);            // This array is reused again and again for each child.  
        GrowableArray<NodeIndex> rootChildren = new GrowableArray<NodeIndex>(100);      // This is used to create the children of the root;
        //Here I make up a graph of memory addresses at made up locations
        for (Address objAddr = 0x100000; objAddr < 0x200000; objAddr += 0x10000)
        {
            NodeIndex nodeIdx = myGraph.GetNodeIndex(objAddr);                          // Create the name (node index) for the child
            // Make a type for the child.  In this case we make a new type for each node, normally you keep these in a interning table so that
            // every distinct type name has exactly one type index.   Interning is not STRICTLLY needed, but the representation assumes that
            // there are many more nodes than there are types.  
            NodeTypeIndex nodeTypeIdx = myGraph.CreateType("MyType_" + objAddr.ToString(), "MyModule");
            // Create a list of children for this node in this case, each node has exactly one child, which is the root node. 
            children.Clear();
            children.Add(baseIdx);
            // Actually define the node with the given name (nodeIdx), type, size (100 in our case) and children; 
            myGraph.SetNode(nodeIdx, nodeTypeIdx, 100, children);

            rootChildren.Add(nodeIdx);      // Remember this node name as belonging to the things that the root points at. 
        }

        // At this point we have everything we need to define the base node, do it here.  
        myGraph.SetNode(baseIdx, myGraph.CreateType("[Base]"), 0, rootChildren);

        // Create a root node that points at the base node.  
        myGraph.RootIndex = myGraph.GetNodeIndex(1);
        children.Clear();
        children.Add(baseIdx);
        myGraph.SetNode(myGraph.RootIndex, myGraph.CreateType("[ROOT]"), 0, children);

        // Note that the raw graph APIs force you to know all the children of a particular node before you can define
        // the node.  There is a class call MemoryNodeBuilder which makes this a bit nicer in that you can incrementally
        // add children to nodes and then 'finalize' them all at once at the end.   Ideally, however you don't have many
        // nodes that need MemoryNodeBuilder as they are more expensive to build.  

        // So far, the graph is in 'write mode' where only creation APIs are allowed.   Change to 'Read Mode' which no writes 
        // are allowed by read APIs are allowed.  (We may lift this restriction at some point).  
        myGraph.AllowReading();

        // I can  dump it as XML to look at it.
        using (var writer = File.CreateText("MyGraph.dump.xml"))
        {
            myGraph.WriteXml(writer);
        }

        // I can write the graph out as a file and read it back in later.  
        // myGraph.WriteAsBinaryFile("myGraph.gcGraph");
        // var roundTrip = MemoryGraph.ReadFromBinaryFile("myGraph.gcGraph");

        // OK we have a graph, turn it into a stack source so that I can view it.  
        MemoryGraphStackSource myStackSource = new MemoryGraphStackSource(myGraph, LogFile);

        // Create a Stacks class (which remembers all the Filters, Scaling policy and other things the viewer wants.  
        Stacks stacks = new Stacks(myStackSource);

        // If you wanted to, you can change the filtering ...
        stacks.Filter.GroupRegExs = "";
        stacks.Filter.MinInclusiveTimePercent = "";

        // And view it.  
        OpenStackViewer(stacks);
    }

    /// <summary>
    /// This demo shows you how to create a StackSource (or a class Stacks from 'nothing').  
    /// 
    /// Note that a useful alternative to this is to simply generate a file XML file which
    /// matches the format of a PerfVIew.XML file.   This XML file is just what you would
    /// expect (a list of frame definitions, which define stacks which define samples).  
    /// Thus you may wish to simply generate this XML directy and simple read that file
    /// with OpenPerfViewXmlFile
    /// </summary>
    public void DemoInternStackSource()
    {
        // Make a custom stack source that was created out of nothing.   InternStackSouce is your friend here. 
        StackSource myStackSource = new MyStackSource();

        // Create a Stacks class (which remembers all the Filters, Scaling policy and other things the viewer wants.  
        Stacks stacks = new Stacks(myStackSource);

        // If you wanted to, you can change the filtering ...

        // And view it.  
        OpenStackViewer(stacks);
    }


    /// <summary>
    /// InternStackSource is used to create a StackSource out of nothing.  However at least currently
    /// you need to subclass it to get at the creation APIs.   
    /// </summary>
    private class MyStackSource : InternStackSource
    {
        public MyStackSource()
        {
            StackSourceModuleIndex emptyModuleIdx = Interner.ModuleIntern("");

            // Make up a stack source with 10 samples in it, all with the same stack.  
            var mySample = new StackSourceSample(this);
            for (int i = 0; i < 10; i++)
            {
                mySample.TimeRelativeMSec = i;
                mySample.Metric = 10 + i;       // Just to make things interesting.  
                mySample.StackIndex = StackSourceCallStackIndex.Invalid;

                // Add a frame 'Frame 1'
                mySample.StackIndex = Interner.CallStackIntern(Interner.FrameIntern("Frame 1", emptyModuleIdx), mySample.StackIndex);

                // Add a frame 'Frame 2'
                mySample.StackIndex = Interner.CallStackIntern(Interner.FrameIntern("Frame 2", emptyModuleIdx), mySample.StackIndex);

                // This copies mySample, so you can keep reusing mySample for the next sample
                AddSample(mySample);
            }
        }
    }

    /// <summary>
    /// Gets the TraceEvents list of events from etlFile, applying a process filter if the /process argument is given. 
    /// </summary>
    private TraceEvents GetTraceEventsWithProcessFilter(ETLDataFile etlFile)
    {
        // If the user asked to focus on one process, do so.  
        TraceEvents events;
        if (CommandLineArgs.Process != null)
        {
            var process = etlFile.Processes.LastProcessWithName(CommandLineArgs.Process);
            if (process == null)
            {
                throw new ApplicationException("Could not find process named " + CommandLineArgs.Process);
            }

            events = process.EventsInProcess;
        }
        else
        {
            events = etlFile.TraceLog.Events;           // All events in the process.
        }

        return events;
    }

    /******************************************************************************/
    /*                        Demo of PerfViewStartup hooks                       */
    /******************************************************************************/
    /* GETTING STARTED 
     * The following user commands are actually hooks into the GUI   They are intended
     * to be used in conjunction with at 'PerfViewStartup' file that should be 
     * placed in the PerfViewExtensions directory where user extension DLLs live.  
     * To get start, cut and paste the following lines into your PerfViewStartup
     * file and your user commands will be called back at at various points by
     * the GUI.   As the startup file indicate there are at present 3 hooks 
     * that let you control what happens at startup, when a file is opened and
     * when a view in a file is opened.   
     * 
        ################################
        # PerfView Startup.  This script gets executed when PerfView starts.  Most logic is in extension DLLs.  However this script
        # allows a certain number of hooks at various places.  The intent is that most extensions can defer loading and simply 
        # declare to be called back at particular places (for particular file extensions or for particular view are clicked. 
        # this keeps extensions pay-for play
        #
        ###################
        # OnStartup Command 
        # OnStartup will execute a user command at startup.  This allows extensions to force loading and do initialization at startup.
        # you should NOT do this unless you have to, as it is not pay for play.  The method is called with no arguments.  
        # Try to do as little as possible (typically registering yourself with additional hooks to get called back later). 
        #
        OnStartup Global.DemoOnStartup
        #
        ###################
        # OnFileOpen FileExtension Command
        # OnFileOpen command indicates a callback to be called with the file node  the given extension opened in the viewer.  
        # If you only want to add a new kind of view, consider using DeclareFileView hook instead since your DLL will not be
        # event loaded until someone clicks on your view (rather then when someone clicked on the file)  
        #
        # The callback must take one string argument which is the name of the data file that is being opened.  Typically this 
        # callback adds additional views (children) to the file in the viewer.  
        #
        OnFileOpen .etl Global.DemoOnFileOpen
        OnFileOpen .txt Global.DemoOnFileOpen
        #
        ###################
        # DeclareFileView Extension ChildName Command Icon HelpLink
        # DeclareFileView command indicates the desire for another view in the viewer for a file of the given extension.   You tell 
        # us the name of this view in the viewer and the name of the callback to invoke if that view is opened.  Optionally you can
        # provide a Icon resource for this child as well as a hyperlink in the help file
        #
        # The callback must take two string arguments the first is the name of the file being opened, and the second is the name of 
        # the view being opened.   Typically you look in the file. and open some new viewport (stackviewer or eventViewer).  
        #
        DeclareFileView .etl "DemoDeclareFile" Global.DemoDeclareFileView
    ******************************************************************************/

    /// <summary>
    /// If you place a file called PerfViewExtensions\PerfViewStartup next to the 
    /// PerfView.exe it will run execute commands in that file.  If you put
    /// 
    /// OnStartup DemoOnStartup
    /// 
    /// It will execute this user command when PerfView starts up.  
    /// </summary>
    public void DemoOnStartup()
    {
        LogFile.WriteLine("*********** In DemoOnStartup");
    }

    /// <summary>
    /// If you place a file called PerfViewExtensions\PerfViewStartup next to the PerfView.exe it will 
    /// run execute commands in that file.  If you put
    /// 
    /// OnFileOpen .etl DemoOnFileOpen
    /// 
    /// It will execute this user command whenever a file is opened.  This allows you to manipulate 
    /// the views for this file type.   It is passed the name of the file that was opened.  
    /// </summary>
    public void DemoOnFileOpen(string fileName)
    {
        LogFile.WriteLine("************ In DemoOnFileOpen file = {0}", fileName);

        // This demo adds a new node to a file when it opens.  
        //var file = PerfViewFile.Get(fileName);
        //var newChild = new PerfViewReport(file, "MyNewFileNode", delegate(string filename, string viewName) {
        //    LogFile.WriteLine("************ In DemoOnFileOpen file = {0} view = {1}", fileName, viewName);
        //});
        //file.Children.Add(newChild);
    }

    /// <summary>
    /// If you place a file called PerfViewExtensions\PerfViewStartup next to the PerfView.exe it will 
    /// run execute commands in that file.  If you put
    /// 
    /// DeclareFileView .etl "Demo View In Etl File" DemoDeclareFileView
    /// 
    /// It will create a child node for all .etl files called 'Demo View In Etl File'  If you click 
    /// on this node it will execute this user command.  It is passed the name of the file that was 
    /// opened and the name of the view that was opened (in this case 'Demo View In Etl File').  
    /// </summary>
    public void DemoDeclareFileView(string fileName, string viewName)
    {
        // This demo creates a view that shows you all the START events in a stack view.   
        LogFile.WriteLine("************ In DemoDeclareFileView file = {0} view = {1}", fileName, viewName);

        // This is an example of opening an ETL file.  
        ETLDataFile etlFile = OpenETLFile(fileName);

        // An ETLData file is a high level construct that knows about high level 'views' of the data (CPU stacks, thread time Stacks ...)

        // However if you want to create a new view, you probably want a TraceLog which is the underlying ETW data.
        TraceLog traceLog = etlFile.TraceLog;

        // A tracelog represent the whole ETL file (which has process, images, threads etc), we want events, and we want callbacks
        // for each event which is what GetSource() does.   THus we get a source (which we can add callbacks to)
        var eventSource = traceLog.Events.GetSource();

        // At this point create the 'output' of our routine.  Our goal is to produce stacks that we will view in the 
        // stack viewer.   Thus we create an 'empty' one fo these. 
        var stackSource = new MutableTraceEventStackSource(traceLog);
        // A stack source is  list of samples.  We create a sample structure, mutate it and then call AddSample() repeatedly to add samples. 
        var sample = new StackSourceSample(stackSource);

        // Setup the callbacks, In this case we are going to watch for stacks where GCs happen
        eventSource.Clr.GCStart += delegate (GCStartTraceData data)
        {
            // An TraceLog should have a callstack associated with this event;
            CallStackIndex callStackIdx = data.CallStackIndex();
            if (callStackIdx != CallStackIndex.Invalid)
            {
                // Convert the TraceLog call stack to a MutableTraceEventStackSource call stack
                StackSourceCallStackIndex stackCallStackIndex = stackSource.GetCallStack(callStackIdx, data);

                // Add a pseudo frame on the bottom of the stack
                StackSourceFrameIndex frameIdxForName = stackSource.Interner.FrameIntern("GC Gen " + data.Depth + "Reason " + data.Reason);
                stackCallStackIndex = stackSource.Interner.CallStackIntern(frameIdxForName, stackCallStackIndex);

                // create a sample with that stack and add it to the stack source (list of samples)
                sample.Metric = 1;
                sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                sample.StackIndex = stackCallStackIndex;
                stackSource.AddSample(sample);
            }
        };
        // You can set up other callback for other events.  

        // This causes the TraceLog source to actually spin through all the events calling our callbacks as requested.  
        eventSource.Process();

        // We are done adding sample to our stack Source, so we tell the MutableTraceEventStackSource that.  
        // after that is becomes viewable (and read-only).   
        stackSource.DoneAddingSamples();

        // Take the stack source (raw data) and make it into a 'Stacks' allows you to add filtering to and send to 'OpendStackViewer'
        Stacks stacksForViewer = new Stacks(stackSource, viewName, etlFile);
        // Set any filtering options here.  

        // Now we can display the viewer.  
        OpenStackViewer(stacksForViewer);
    }
#endif  // Demos
}
