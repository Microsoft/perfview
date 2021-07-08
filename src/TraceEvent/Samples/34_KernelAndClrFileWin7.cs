using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

/* README FIRST */
// Until Windows 8, kernel events can only be turned on in a session called 'NT Kernel Logger' and no
// other providers except the kernel provider can be in such a session.   Thus if you wish a trace 
// that has both kernel and non-kernel events you must have two session.   In the code below we show
// how to do this in the file case.    Basically you simply have two session, one for the
// kernel events and one for everything else.   This means that you generate to files, one for 
// kernel events and one for everything else (in our case the CLR events).  You then can 'merge'
// the files as a post-processing step.    
//
// As mentioned on Windows 8 you can use a single session and thus avoid these complexities.  In addition
// event before Windows 8 there are a number of kernel events that are available as non-kernel 
// providers.   Do a 
//             logman query providers
// To see all providers that the OS provides, and look for ones with the Microsoft-Windows-Kernel prefix.
// While these providers have 'Kernel' in their name they are not subject to the restrictions mentioned
// above, and thus can be used with other user-mode providers.   In particular the Microsoft-Windows-Kernel-Process
// provider can give you the process, thread, and image load events which are some of the most likely
// kernel events you may want.  
// 
namespace TraceEventSamples
{
    public class KernelAndClrFileWin7
    {
        /// <summary>
        /// Where all the output goes.  
        /// </summary>
        private static TextWriter Out = AllSamples.Out;

        public static void Run()
        {
            Out.WriteLine("******************** KernelAndClrFile DEMO ********************");
            string dataFileName = "output.etl";
            DataCollection(dataFileName);
            DataProcessing(dataFileName);
        }

        /// <summary>
        /// Turning on providers and creating files
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

            string kernelDataFileName = Path.ChangeExtension(dataFileName, ".kernel.etl");

            // Create one user mode session and one kernel mode session
            Out.WriteLine("Creating two raw files, one with kernel events and one with clr events.");
            using (var userSession = new TraceEventSession("MonitorKernelAndClrEventsSession", dataFileName))
            using (var kernelSession = new TraceEventSession(KernelTraceEventParser.KernelSessionName, kernelDataFileName))
            {
                // Set up Ctrl-C to stop both user mode and kernel mode sessions
                Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs cancelArgs)
                {
                    Out.WriteLine("Insuring all ETW sessions are stopped.");
                    kernelSession.Stop(true);         // true means don't throw on error
                    userSession.Stop(true);           // true means don't throw on error
                    // Since we don't cancel the Ctrl-C we will terminate the process as normal for Ctrl-C
                    Out.WriteLine("OnCtrl C handler ending.");
                };

                // Enable the events we care about for the kernel in the kernel session
                // For this instant the session will buffer any incoming events.  
                kernelSession.EnableKernelProvider(
                    KernelTraceEventParser.Keywords.ImageLoad |
                    KernelTraceEventParser.Keywords.Process |
                    KernelTraceEventParser.Keywords.Thread);

                // Enable the events we care about for the CLR (in the user session).
                // unlike the kernel session, you can call EnableProvider on other things too.  
                // For this instant the session will buffer any incoming events.  
                userSession.EnableProvider(
                    ClrTraceEventParser.ProviderGuid,
                    TraceEventLevel.Informational,
                    (ulong)(ClrTraceEventParser.Keywords.Default));

                Out.WriteLine("Collecting data for 10 seconds (run a .Net program to generate events).");
                Thread.Sleep(10000);

                Out.WriteLine("Stopping sessions");
            }    // Using clauses will insure that session are disposed (and thus stopped) before Main returns.  

            Out.WriteLine("Merging the raw files into a single '{0}' file.", dataFileName);
            TraceEventSession.MergeInPlace(dataFileName, Out);
            Out.WriteLine("Merge complete.");
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


