using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

/* README FIRST */
// This shows you how to listen to both Kernel and non-Kernel (in this case the CLR) events on Windows 8.
// This is significantly easier than on Win7 (which is shown in 34_KernelAndClrFileWin7.cs) because
// a single session can have both kernel and non-kernel providers. 
// 
namespace TraceEventSamples
{
    public class KernelAndClrFile
    {
        /// <summary>
        /// Where all the output goes.  
        /// </summary>
        private static TextWriter Out = AllSamples.Out;

        public static void Run()
        {
            if (Environment.OSVersion.Version.Major * 10 + Environment.OSVersion.Version.Minor < 62)
            {
                Out.WriteLine("This demo only works on Win8 / Win 2012 an above)");
                return;
            }

            Out.WriteLine("******************** KernelAndClrFile DEMO ********************");
            string dataFileName = "output.etl";
            DataCollection(dataFileName);
            DataProcessing(dataFileName);
        }

        /// <summary>
        /// Turning on providers and creating the file
        /// </summary>
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
                // THis has to be first, and it will fail if you are not on Win8.  
                session.EnableKernelProvider(
                    KernelTraceEventParser.Keywords.ImageLoad |
                    KernelTraceEventParser.Keywords.Process |
                    KernelTraceEventParser.Keywords.Thread);

                // Enable the events we care about for the CLR (in the user session).
                // unlike the kernel session, you can call EnableProvider on other things too.  
                // For this instant the session will buffer any incoming events.  
                session.EnableProvider(
                    ClrTraceEventParser.ProviderGuid,
                    TraceEventLevel.Informational,
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
                    if ((int)data.ID == 0xFFFE)         // The EventSource manifest events show up as unhandled, filter them out.
                    {
                        return;
                    }

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

