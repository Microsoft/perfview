using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Session;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using TraceEventSamples.Producer;

/* README FIRST */
// Many operations can be done by streaming over the raw events in time order.   However more sophisticated analysis
// requires information from multiple events to be correlated and collated to be useful.   Chief among these are 
// stack traces (because for at least kernel stack traces, stacks come as two events (one for the kernel part and
// one for the user mode part), that are separated in time (and even separated from the event that triggered the stack
// trace.  In addition stacks requires that information about module loading and JIT compilation (for .NET or JScript) 
// also be incorporated.
//
// A useful way of accomplishing this is to convert the raw ETL file to a augmented ETLX file format.   This translation
// can keep all the original information from the ETL file but it can rearrange it so that it is 'digested' so that information
// is stored for easy retrieval instead of easy generation (which is what the ETL file format was optimized for).
//
// The programmatic interface to this ETLX file format is the 'TraceLog' class.   You convert an ETL file to an ETLX file
// using the 'TraceLog.CreateFromETL() API, and once you have an ETLX file,  you can open it with the TraceLog(string) 
// constructor.
//
// Like an ETL file an ETLX file does allow you scan all the events in the file in time order using callbacks, but it
// can do so much more including
//
//  * Detailed summary information is available without need to scan all the events.  Summary information include
//         * TraceProcess - for each process in the trace for any amount of time, its start time, stop time, command line, ...)
//         * TraceThread - For each process, the threads that ever existed in the process
//         * TraceLoadedModule - For each process all the DLLs it ever loaded, including how much CPU time is spent in each
//         * TraceCounts - For each event, how many times the event occurs in the trace, and whether it has stacks associated with it
// 
//  * Much better navigation capabilities
//         * IEnumerables on the events (rather than the callback model)
//         * Easy and efficient filtering by time range (random access), process or by event type. 
//         * Ability to iterate backwards 
//
//  * All items have dense (array) indexes
//         * All the items in the ETLX file have indexes associated with them.  This means that you can create 'side arrays'
//           of this size and thus 'attach' your own information to these things.   All of the items have ways of looking
//           up the item by this index, which means you can also 'remember' particular items (typically Events, but also
//           stacks, code addresses, processes or modules) in your own data structures.    This solves many otherwise ugly
//           problems (for example a OS process ID is NOT unique if the process dies, thus in a trace a process ID is NOT
//           a unique identifier, but a TraceLog ProcessIndex IS a unique identifier for any process in the trace.  
//
//  * Stack support.   ETW supports generating call stacks along with particular events.   This information in its raw form
//       is a list of return address locations.   TraceLog has logic to translate these addresses into TraceCodeAddresses
//       which represent a particular locations (methods and line numbers) in particular DLLs.  It handles all the 
//       complexity of decoding these addresses and looking up both managed (JIT compiled) and native (PDB lookup).   
//         * TraceCallStack - for every stack trace, the list of code addresses in the stack.  
//         * TraceCodeAddress - for every instruction pointer in the trace, the module or JIT compiled method associated with it.
// 
// In this demo, like the SimpleEVetnSourceFile we generate some EventSource events to a file.   However we also turn on
// stack logging for these events.   We also cause some C# exceptions during data collection.   Then we use TraceLog to 
// print the stack traces for these events and the stack associated with them.
namespace TraceEventSamples
{
    internal class SimpleTraceLog
    {
        /// <summary>
        /// Where all the output goes.  
        /// </summary>
        private static TextWriter Out = AllSamples.Out;

        public static void Run()
        {
            Out.WriteLine("******************** SimpleTraceLog DEMO ********************");
            Out.WriteLine("This program generates processes and displays EventSource events with stacks.");
            Out.WriteLine();
            Out.WriteLine("Note that this program will attempt to resolve symbols (PDBs) for Microsoft");
            Out.WriteLine("DLL using the web.  Thus if you have a poor network connection it will work");
            Out.WriteLine("poorly, still the program will not crash it just wont great symbolic info.");
            Out.WriteLine("The information will be cached locally, so it will be fast for later runs.");
            Out.WriteLine();
            Out.WriteLine("Current Directory for files: {0}", Environment.CurrentDirectory);

            // Get some data and put it in a file
            CollectData("Microsoft-Demos-SimpleMonitor", "SimpleTraceLogData.etl");

            // Process the data in the file.  
            ProcessData("SimpleTraceLogData.etl");
        }

        /// <summary>
        /// CollectData doe will turn on logging of data from 'eventSourceName' to the file 'dataFileName'.
        /// It will then call EventGenerator.CreateEvents and wait 12 seconds for it to generate some data. 
        /// </summary>
        private static void CollectData(string eventSourceName, string dataFileName)
        {

            // Today you have to be Admin to turn on ETW events (anyone can write ETW events).   
            if (!(TraceEventSession.IsElevated() ?? false))
            {
                Out.WriteLine("To turn on ETW events you need to be Administrator, please run from an Admin process.");
                Debugger.Break();
                return;
            }

            // As mentioned below, sessions can outlive the process that created them.  Thus you need a way of 
            // naming the session so that you can 'reconnect' to it from another process.   This is what the name
            // is for.  It can be anything, but it should be descriptive and unique.   If you expect multiple versions
            // of your program to run simultaneously, you need to generate unique names (e.g. add a process ID suffix) 
            // however this is dangerous because you can leave data collection on if the program ends unexpectedly.  
            // 
            // In this case we tell the session to place the data in MonitorToFileData.etl.  
            var sessionName = "SimpleTraceLogSession";
            Out.WriteLine("Creating a '{0}' session writing to {1}", sessionName, dataFileName);
            Out.WriteLine("Use 'logman query -ets' to see active sessions.");
            Out.WriteLine("Use 'logman stop {0} -ets' to manually stop orphans.", sessionName);
            using (var session = new TraceEventSession(sessionName, dataFileName))      // Since we give it a file name, the data goes there. 
            using (var kernelSession = new TraceEventSession(KernelTraceEventParser.KernelSessionName, Path.ChangeExtension(dataFileName, ".kernel.etl")))
            {
                /* BY DEFAULT ETW SESSIONS SURVIVE THE DEATH OF THE PROESS THAT CREATES THEM! */
                // Unlike most other resources on the system, ETW session live beyond the lifetime of the 
                // process that created them.   This is very useful in some scenarios, but also creates the 
                // very real possibility of leaving 'orphan' sessions running.  
                //
                // To help avoid this by default TraceEventSession sets 'StopOnDispose' so that it will stop
                // the ETW session if the TraceEventSession dies.   Thus executions that 'clean up' the TraceEventSession
                // will clean up the ETW session.   This covers many cases (including throwing exceptions)
                //  
                // However if the process is killed manually (including control C) this cleanup will not happen.  
                // Thus best practices include
                //
                //     * Add a Control C handler that calls session.Dispose() so it gets cleaned up in this common case
                //     * use the same session name run-to-run so you don't create many orphans. 
                //
                // By default TraceEventSessions are in 'create' mode where it assumes you want to create a new session.
                // In this mode if a session already exists, it is stopped and the new one is created.   
                // 
                // Here we install the Control C handler.   It is OK if Dispose is called more than once.  
                Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e) { session.Dispose(); kernelSession.Dispose(); };

                // Enable kernel events.  
                kernelSession.EnableKernelProvider(KernelTraceEventParser.Keywords.ImageLoad | KernelTraceEventParser.Keywords.Process | KernelTraceEventParser.Keywords.Thread);

                // Enable my provider, you can call many of these on the same session to get events from other providers  

                // Turn on the eventSource given its name.   
                // Note we turn on Verbose level all keywords (ulong.MaxValue == 0xFFF....) and turn on stacks for 
                // this provider (for all events, until Windows 8.1 you can only turn on stacks for every event 
                // for a particular provider or no stacks)
                var options = new TraceEventProviderOptions() { StacksEnabled = true };
                var restarted = session.EnableProvider(eventSourceName, TraceEventLevel.Verbose, ulong.MaxValue, options);
                if (restarted)      // Generally you don't bother with this warning, but for the demo we do.  
                {
                    Out.WriteLine("The session {0} was already active, it has been restarted.", sessionName);
                }

                // We also turn on CLR events because we need them to decode Stacks and we also get exception events (and their stacks)
                session.EnableProvider(ClrTraceEventParser.ProviderGuid, TraceEventLevel.Verbose, (ulong)ClrTraceEventParser.Keywords.Default);

                // Start another thread that Causes MyEventSource to create some events
                // Normally this code as well as the EventSource itself would be in a different process.  
                EventGenerator.CreateEvents();

                // Also generate some exceptions so we have interesting stacks to look at
                Thread.Sleep(100);
                EventGenerator.GenerateExceptions();

                Out.WriteLine("Waiting 12 seconds for events to come in.");
                Thread.Sleep(12000);

                // Because the process in question (this process) lives both before and after the time the events were 
                // collected, we don't have complete information about JIT compiled methods in that method.   There are 
                // some methods that were JIT compiled before the session started (e.g. SimpleTraceLog.Main) for which
                // we do not have information.   We collect this by forcing a CLR 'rundown' which will dump method information
                // for JIT compiled methods that were not present.  If you know that the process of interest ends before
                // data collection ended or that data collection started before the process started, then this is not needed.  
                Out.WriteLine("Forcing rundown of JIT methods.");
                var rundownFileName = Path.ChangeExtension(dataFileName, ".clrRundown.etl");
                using (var rundownSession = new TraceEventSession(sessionName + "Rundown", rundownFileName))
                {
                    rundownSession.EnableProvider(ClrRundownTraceEventParser.ProviderGuid, TraceEventLevel.Verbose, (ulong)ClrRundownTraceEventParser.Keywords.Default);
                    // Poll until 2 second goes by without growth.  
                    for (var prevLength = new FileInfo(rundownFileName).Length; ;)
                    {
                        Thread.Sleep(2000);
                        var newLength = new FileInfo(rundownFileName).Length;
                        if (newLength == prevLength)
                        {
                            break;
                        }

                        prevLength = newLength;
                    }
                }
                Out.WriteLine("Done with rundown.");
            }

            Out.WriteLine("Zipping the raw files into a single '{0}' file.", dataFileName);

            // At this point you have multiple ETL files that don't have all the information 
            // inside them necessary for analysis off the current machine.    To do analysis
            // of the machine you need to merge the ETL files (which can be done with
            //        TraceEventSession.MergeInPlace(dataFileName, Out);
            // However this does not get the symbolic information (NGEN PDBS) needed to
            // decode the stacks in the .NET managed framewrok on another machine.   
            // To do the merging AND generate these NGEN images it is best to us ethe 
            // ZipppedETLWriter that does all this (and compresses all the files in a ZIP archive).

            ZippedETLWriter writer = new ZippedETLWriter(dataFileName, Out);
            writer.WriteArchive();

            Out.WriteLine("Zip complete, output file = {0}", writer.ZipArchivePath);
        }

        /// <summary>
        /// Process the data in 'dataFileName' printing the events and doing delta computation between 'MyFirstEvent'
        /// and 'MySecondEvent'.  
        /// </summary>
        private static void ProcessData(string dataFileName)
        {
            var zipDataFileName = dataFileName + ".zip";
            Out.WriteLine("**************  Unpacking the ZIP file {0}", zipDataFileName);
            // This will unpack the ETL file as well as unpacks any symbols into  the default
            // symbol cache (the first symbol cache on your _NT_SYMBOL_PATH or %TEMP%\symbols
            // if there is no such symbol cache.    You can override where this goes by 
            // setting the SymbolDirectory variable.  
            ZippedETLReader zipReader = new ZippedETLReader(zipDataFileName, Out);
            zipReader.UnpackArchive();
            Out.WriteLine("Unpacked ETL to {0} Unpacked Symbols to {1}", zipReader.EtlFileName, zipReader.SymbolDirectory);

            Out.WriteLine("**************  Creating a ETLX file for {0}", dataFileName);
            // Note the OpenOrConvert will take an ETL file and generate an ETLX (right next to it) if it is out of date.  
            // We TraceLogOptions gives you control over this conversion.  Here we spew the log file to the console
            var traceLog = TraceLog.OpenOrConvert(dataFileName, new TraceLogOptions() { ConversionLog = Out });
            Out.WriteLine("**************  Done converting {0}", Path.GetFileName(traceLog.FilePath));

            // The OS process ID of this process
            var myProcessID = Process.GetCurrentProcess().Id;

            // Find myself in th trace.  
            var simpleTraceLogProcess = traceLog.Processes.LastProcessWithID(myProcessID);
            Debug.Assert(simpleTraceLogProcess != null);

            // Resolve symbols for clr and ntdll using the standard Microsoft symbol server path.  
            var symbolReader = new SymbolReader(Out, SymbolPath.MicrosoftSymbolServerPath);

            // By default the symbol reader will NOT read PDBs from 'unsafe' locations (like next to the EXE)  
            // because hackers might make malicious PDBs.   If you wish ignore this threat, you can override this
            // check to always return 'true' for checking that a PDB is 'safe'.  
            symbolReader.SecurityCheck = (path => true);

            foreach (var module in simpleTraceLogProcess.LoadedModules)
            {
                if (module.Name == "clr" || module.Name == "ntdll" || module.Name == "mscorlib.ni")
                {
                    traceLog.CodeAddresses.LookupSymbolsForModule(symbolReader, module.ModuleFile);
                }
            }

            // Source line lookup is verbose, so we don't send it to the console but to srcLookupLog (which we currently ignore)
            var srcLookupLog = new StringWriter();
            var silentSymbolReader = new SymbolReader(srcLookupLog, SymbolPath.MicrosoftSymbolServerPath);
            silentSymbolReader.Options = SymbolReaderOptions.CacheOnly;     // don't try to look things up on the network for source 
            silentSymbolReader.SecurityCheck = (pdbPath) => true;           // for this demo we trust any pdb location.   This lets us find the PDB of the demo itself

            // By default the symbol reader will NOT read PDBs from 'unsafe' locations (like next to the EXE)  
            // because hackers might make malicious PDBs.   If you wish ignore this threat, you can override this
            // check to always return 'true' for checking that a PDB is 'safe'.  
            silentSymbolReader.SecurityCheck = (path => true);


            Out.WriteLine("******Looking for EXCEPTION EVENTS");
            // Get all the exception events in 
            foreach (var exceptionData in (simpleTraceLogProcess.EventsInProcess.ByEventType<ExceptionTraceData>()))
            {
                Out.WriteLine("Found an EXCEPTION event in SimpleTraceLog: Type: {0} Message: {1}", exceptionData.ExceptionType, exceptionData.ExceptionMessage);
                PrintStack(exceptionData.CallStack(), silentSymbolReader);
            }

            Out.WriteLine();
            Out.WriteLine("******Looking for Microsoft-Demos-SimpleMonitor.Stop EVENTS");
            foreach (var data in simpleTraceLogProcess.EventsInProcess)
            {
                if (data.ProviderName == "Microsoft-Demos-SimpleMonitor" && data.EventName == "Stop")
                {
                    Out.WriteLine("Found an EVENTSOURCE event {0} at {1:f3} MSec into trace", data.EventName, data.TimeStampRelativeMSec);
                    PrintStack(data.CallStack(), silentSymbolReader);
                }
            }
        }

        private static void PrintStack(TraceCallStack callStack, SymbolReader symbolReader)
        {
            Out.WriteLine("STACKTRACE:");
            while (callStack != null)
            {
                var method = callStack.CodeAddress.Method;
                var module = callStack.CodeAddress.ModuleFile;
                if (method != null)
                {
                    // see if we can get line number information
                    var lineInfo = "";
                    var sourceLocation = callStack.CodeAddress.GetSourceLine(symbolReader);
                    if (sourceLocation != null)
                    {
                        lineInfo = string.Format("  AT: {0}({1})", Path.GetFileName(sourceLocation.SourceFile.BuildTimeFilePath), sourceLocation.LineNumber);
                    }

                    Out.WriteLine("    Method: {0}!{1}{2}", module.Name, method.FullMethodName, lineInfo);
                }
                else if (module != null)
                {
                    Out.WriteLine("    Module: {0}!0x{1:x}", module.Name, callStack.CodeAddress.Address);
                }
                else
                {
                    Out.WriteLine("    ?!0x{0:x}", callStack.CodeAddress.Address);
                }

                callStack = callStack.Caller;
            }
        }
    }
}
