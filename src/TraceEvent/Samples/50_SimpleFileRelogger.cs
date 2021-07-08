
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace TraceEventSamples
{
    /// <summary>
    /// In this trivial example, we show how to use the ETWReloggerTraceEventSource filter out some
    /// events in an ETL file.    It is basically the KernelAndClrFile sample with 'FilterData' 
    /// transformation in between, which only allows a handful of events through based on its own
    /// criteria.  
    /// </summary>
    internal class SimpleFileRelogger
    {
        /// <summary>
        /// Where all the output goes.  
        /// </summary>
        private static TextWriter Out = AllSamples.Out; // Console.Out

        public static void Run()
        {
            var inputFileName = "ReloggerFileInput.etl";
            var outFileName = "ReloggerFileOutput.etl";

            // Create some data by listening for 10 seconds
            DataCollection(inputFileName);

            FilterData(inputFileName, outFileName);

            DataProcessing(outFileName);
        }

        /// <summary>
        /// This routine shows how to use ETWReloggerTraceEventSource take a ETL file (inputFileName)
        /// and filter out events to form another ETL file (outputFileName).  
        /// 
        /// For this example we filter out all events that are not GCAllocationTicks 
        /// </summary>
        private static void FilterData(string inputFileName, string outFileName)
        {
            // Open the input file and output file.   You will then get a callback on every input event,
            // and if you call 'WriteEvent' you can copy it to output file.   
            // In our example we only copy large object 
            using (var relogger = new ETWReloggerTraceEventSource(inputFileName, outFileName))
            {
                // Here we register callbacks for data we are interested in and further filter by.  

                // In this case we keep the image load events for DLL with 'clr' in their name.
                relogger.Kernel.ImageGroup += delegate (ImageLoadTraceData data)
                {
                    if (0 <= data.FileName.IndexOf("clr", StringComparison.OrdinalIgnoreCase))
                    {
                        relogger.WriteEvent(data);
                    }
                };

                // Keep all the process start events 
                relogger.Kernel.ProcessStart += delegate (ProcessTraceData data)
                {
                    relogger.WriteEvent(data);
                };

                // Keep GC Start and stop events.  This can be done more efficiently if you 
                // use multiple callbacks, but this technique may be easier if the events are 
                // not known at compile time. 
                relogger.Clr.All += delegate (TraceEvent data)
                {
                    if (data.EventName == "GC/Start" || data.EventName == "GC/Stop")
                    {
                        relogger.WriteEvent(data);
                    }
                };

#if false       // Turn on to get debugging on unhandled events.  
                relogger.UnhandledEvents += delegate(TraceEvent data)
                {
                    Console.WriteLine("Unknown Event " + data);
                };
#endif 
                relogger.Process();
            }
        }

        /// <summary>
        /// Collect data to form an ETL file.  
        /// </summary>
        /// <param name="dataFileName"></param>
        private static void DataCollection(string dataFileName)
        {
            Out.WriteLine("Collecting 10 seconds of kernel and CLR events to a file, and then printing.");
            Out.WriteLine();
            Out.WriteLine("Start a .NET program while monitoring to see some events!");
            Out.WriteLine();
            if (TraceEventSession.IsElevated() != true)
            {
                Out.WriteLine("Must be elevated (Admin) to run this program.");
                Debugger.Break();
                return;
            }

            // Create one user mode session and one kernel mode session
            Out.WriteLine("Creating a file mode session");
            using (var session = new TraceEventSession("MonitorKernelAndClrEventsSession", dataFileName))
            {
                // Set up Ctrl-C to stop both user mode and kernel mode sessions
                Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs cancelArgs)
                {
                    Out.WriteLine("Insuring all ETW sessions are stopped.");
                    session.Stop(true);         // true means don't throw on error
                    // Since we don't cancel the Ctrl-C we will terminate the process as normal for Ctrl-C
                    Out.WriteLine("OnCtrl C handler ending.");
                };

                // Enable the events we care about for the kernel in the kernel session
                // For this instant the session will buffer any incoming events.  
                // This has to be first, and it will fail if you are not on Win8.  
                session.EnableKernelProvider(
                    KernelTraceEventParser.Keywords.ImageLoad |
                    KernelTraceEventParser.Keywords.Process |
                    KernelTraceEventParser.Keywords.Thread);

                // Enable the events we care about for the CLR (in the user session).
                // unlike the kernel session, you can call EnableProvider on other things too.  
                // For this instant the session will buffer any incoming events.  
                session.EnableProvider(
                    ClrTraceEventParser.ProviderGuid,
                    TraceEventLevel.Verbose,
                    (ulong)(ClrTraceEventParser.Keywords.Default));

                Out.WriteLine("Collecting data for 10 seconds (run a .Net program to generate events).");
                Thread.Sleep(10000);

                Out.WriteLine("Stopping sessions");
            }    // Using clauses will insure that session are disposed (and thus stopped) before Main returns.  

        }

        /// <summary>
        /// Processing the data in a particular file.  
        /// </summary>
        private static void DataProcessing(string dataFileName)
        {
            Out.WriteLine("Opening the output file and printing the results.");
            Out.WriteLine("The list is filtered quite a bit...");
            using (var source = new ETWTraceEventSource(dataFileName))
            {
                if (source.EventsLost != 0)
                {
                    Out.WriteLine("WARNING: there were {0} lost events", source.EventsLost);
                }

                // Set up callbacks to 
                source.Clr.All += Print;
                source.Kernel.All += Print;

                // When you merge a file, some 'symbol' events are injected into the trace.  
                // To avoid these showing up as 'unknown' add the parser for these.  This
                // also shows how you hook up a TraceEventParser that is not support by
                // properties on the source itself (like CLR, and kernel)
                var symbolParser = new SymbolTraceEventParser(source);
                symbolParser.All += Print;

#if DEBUG
                // The callback above will only be called for events the parser recognizes (in the case of Kernel and CLR parsers)
                // It is sometimes useful to see the other events that are not otherwise being handled.  The source knows about these and you 
                // can ask the source to send them to you like this.  
                source.UnhandledEvents += delegate(TraceEvent data)
                {
                    // To avoid 'rundown' events that happen in the beginning and end of the trace filter out things during those times
                    if (data.TimeStampRelativeMSec < 1000 || 9000 < data.TimeStampRelativeMSec)
                    {
                        return;
                    }

                    Out.WriteLine("GOT UNHANDLED EVENT: " + data.Dump());
                };
#endif

                // go into a loop processing events can calling the callbacks.  This will return when the all the events
                // In the file are processed, or the StopProcessing() call is made.  
                source.Process();
                Out.WriteLine("Done Processing.");
            }
        }

        /// <summary>
        /// Print data.  Note that this method is called FROM DIFFERNET THREADS which means you need to properly
        /// lock any read-write data you access.   It turns out Out.Writeline is already thread safe so
        /// there is nothing I have to do in this case. 
        /// </summary>
        private static void Print(TraceEvent data)
        {
            // There are a lot of data collection start on entry that I don't want to see (but often they are quite handy
            if (data.Opcode == TraceEventOpcode.DataCollectionStart || data.Opcode == TraceEventOpcode.DataCollectionStop)
            {
                return;
            }

            // Merging inject some 'symbol' events that are not that interesting so we ignore those too.  
            if (data.ProviderGuid == SymbolTraceEventParser.ProviderGuid)
            {
                return;
            }

            // To avoid 'rundown' events that happen in the beginning and end of the trace filter out things during those times
            if (data.TimeStampRelativeMSec < 1000 || 9000 < data.TimeStampRelativeMSec)
            {
                return;
            }

            Out.WriteLine(data.ToString());
        }
    }
}
