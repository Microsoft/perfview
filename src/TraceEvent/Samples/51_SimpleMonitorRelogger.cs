using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;


namespace TraceEventSamples
{
    /// <summary>
    /// In this trivial example, we show how to use the ETWReloggerTraceEventSource to monitor ETW in real 
    /// time and write out selected events to an ETL file.    The monitor listens for event in real time an
    /// writes out to the output ETL file selected events (in this case CLR GC allocation Tick events for
    /// 
    /// </summary>
    internal class SimpleMonitorRelogger
    {
        /// <summary>
        /// Where all the output goes.  
        /// </summary>
        private static TextWriter Out = AllSamples.Out;

        public static void Run()
        {
            int monitoringTimeSec = 10;

            if (Environment.OSVersion.Version.Major * 10 + Environment.OSVersion.Version.Minor < 62)
            {
                Out.WriteLine("This demo only works on Win8 / Win 2012 an above)");
                return;
            }

            // Today you have to be Admin to turn on ETW events (anyone can write ETW events).   
            if (!(TraceEventSession.IsElevated() ?? false))
            {
                Out.WriteLine("To turn on ETW events you need to be Administrator, please run from an Admin process.");
                Debugger.Break();
                return;
            }

            string outputFileName = "ReloggerMonitorOutput.etl";
            if (File.Exists(outputFileName))
            {
                File.Delete(outputFileName);
            }

            Out.WriteLine("******************** Simple Relogger DEMO ********************");
            Out.WriteLine("This program shows how you can monitor an ETW stream in real time.");
            Out.WriteLine("And conditionally pass the events on to a ETL file");
            Out.WriteLine("Ctrl-C will end earlier");
            Out.WriteLine();
            Out.WriteLine("Please run some managed code while collection is happening...");
            Out.WriteLine();

            // To listen to ETW events you need a session, which allows you to control which events will be produced
            // Note that it is the session and not the source that buffers events, and by default sessions will buffer
            // 64MB of events before dropping events.  Thus even if you don't immediately connect up the source and
            // read the events you should not lose them. 
            //
            // As mentioned below, sessions can outlive the process that created them.  Thus you may need a way of 
            // naming the session so that you can 'reconnect' to it from another process.   This is what the name
            // is for.  It can be anything, but it should be descriptive and unique.   If you expect multiple versions
            // of your program to run simultaneously, you need to generate unique names (e.g. add a process ID suffix) 
            // however this is dangerous because you can leave data collection on if the program ends unexpectedly.  
            var sessionName = "SimpleMontitorSession";
            Out.WriteLine("Creating a '{0}' session", sessionName);
            using (var session = new TraceEventSession(sessionName))
            {
                // Enable the events we care about for the kernel in the kernel session
                // For this instant the session will buffer any incoming events.  
                // This has to be first, and it will fail if you are not on Win8.  
                session.EnableKernelProvider(
                    KernelTraceEventParser.Keywords.ImageLoad |
                    KernelTraceEventParser.Keywords.Process |
                    KernelTraceEventParser.Keywords.Thread);

                // A relogger is a TraceEventSource and acts much like an ETWTraceEventSource, with extra Write APIS. 
                // Thus you get a callback on any event you want.   
                // Only things that you call 'WriteEvent' on will end up in the output file.  
                var relogger = new ETWReloggerTraceEventSource(sessionName, TraceEventSourceType.Session, outputFileName);

                // Here we set up the callbacks we want in the output file.   In this case all GC allocation Tick
                // events for 'String' as well as any ExceptionStart events.  
                relogger.Clr.GCAllocationTick += delegate (GCAllocationTickTraceData data)
                {
                    if (data.TypeName == "System.String")
                    {
                        relogger.WriteEvent(data);
                    }
                };
                relogger.Clr.ExceptionStart += delegate (ExceptionTraceData data)
                {
                    relogger.WriteEvent(data);
                };

                // We also keep the image load events for DLL with 'clr' in their name.
                relogger.Kernel.ImageGroup += delegate (ImageLoadTraceData data)
                {
                    if (0 <= data.FileName.IndexOf("clr", StringComparison.OrdinalIgnoreCase))
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
                // Allow the test to be terminated with Ctrl-C cleanly. 
                Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e) { session.Dispose(); };

                // Set up a timer to stop processing after monitoringTimeSec
                var timer = new Timer(delegate (object state)
                {
                    Out.WriteLine("Stopped after {0} sec", monitoringTimeSec);
                    session.Dispose();
                }, null, monitoringTimeSec * 1000, Timeout.Infinite);

                // Turn on the events to the provider.  In this case most CLR events 

                Out.WriteLine("**** Turn on CLR Etw Providers.  Run managed code to see events.");
                session.EnableProvider(ClrTraceEventParser.ProviderGuid, TraceEventLevel.Verbose, (ulong)ClrTraceEventParser.Keywords.Default);

                // go into a loop processing events can calling the callbacks.  Because this is live data (not from a file)
                // processing never completes by itself, but only because someone called 'source.Dispose()'.  
                Out.WriteLine("**** Start listening for events from the Microsoft-Demos-SimpleMonitor provider.");
                Out.WriteLine("The monitor will run for a maximum of {0} seconds.  Run managed code for more output.", monitoringTimeSec);
                relogger.Process();
                Out.WriteLine();
                Out.WriteLine("Stopping the collection of events.");
                timer.Dispose();    // Turn off the timer.  
            }

            Out.WriteLine("Monitoring complete, only certain CLR events put in the output file.");
            Out.WriteLine("The output ETL file: {0}", Path.GetFullPath(outputFileName));
            Out.WriteLine();

            if (!File.Exists(outputFileName))
            {
                Out.WriteLine("Error: No output file was generated (did you run anything during the collection?");
                return;
            }

            // Show what was actually produced in the filtered file.  
            DataProcessing(outputFileName);
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
