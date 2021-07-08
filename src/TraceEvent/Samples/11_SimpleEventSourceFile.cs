using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using TraceEventSamples.Producer;

/* README FIRST */
// This program is a simple demo that demonstrates how to make EVentSources log to a file as
// as well as how to read that file and parse the resulting events.   
// 
// The basic flow of the program is
//   Generating of the ETL file:
//     * Create a 'file' TraceEventSession.  This lets you control what events you wish to collect, and 
//          tells the session where to put the file.  
//     * Enable the ETW providers of interest (in this case Microsoft-Demos-SimpleMonitor) 
//     * Kick off EventGenerator to generate some Microsoft-Demos-SimpleMonitor events (normally in another process).  
//     * Wait for the generate to generate some events (12 seconds)
//     * End the session 
//
//   Parsing of the ETL file 
//     * Create an ETWTraceEventSource which gets its data from the data file.   A TraceEventSource 
//           represents the stream of events in the file.  
//     * Connect a TraceEventParser to the source.    Parsers know how to interpret events from particular 
//          ETW providers.  In this case we care about EventSource events and the DynamicTraceEventParser
//          is the parser that cares about it. 
//     * Attach callbacks to the Parser for the events you are interested in.  We use the 'All' C# event 
//          for this.  
//     * Call ETWTraceEventSource.Process() which will loop through all events in the file.  It can
//          be stopped early by calling ETWTraceEventSource.StopProcessing().  
//     * Your processing happens in the callbacks
//     * ETWTraceEventSource.Process() returns.  
//     * Dispose the ETWTraceEventSource, which closes the files and cleans up resources.
//
namespace TraceEventSamples
{
    /// <summary>
    /// The main program is the 'listener' that listens and processes the events that come from EventGenerator
    /// </summary>
    internal class SimpleEventSourceFile
    {
        /// <summary>
        /// Where all the output goes.  
        /// </summary>
        private static TextWriter Out = AllSamples.Out;

        /// <summary>
        /// This is a demo of using TraceEvent to activate a 'real time' provider that is listening to 
        /// the MyEventSource above.   Normally this event source would be in a different process,  but 
        /// it also works if this process generate the events and I do that here for simplicity.  
        /// </summary>
        public static int Run()
        {
            Out.WriteLine("******************** SimpleEventSourceFile DEMO ********************");
            Out.WriteLine("This program generates processes and displays EventSource events");
            Out.WriteLine("using the ETW File based pipeline.  (e.g. for later analysis)");
            Out.WriteLine();

            // Get some data and put it in a file
            CollectData("Microsoft-Demos-SimpleMonitor", "EventSourceData.etl");

            // Process the data in the file.  
            ProcessData("EventSourceData.etl");
            return 0;
        }

        /// <summary>
        /// CollectData turn on logging of data from 'eventSourceName' to the file 'dataFileName'.
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
            var sessionName = "SimpleMontitorSession";
            Out.WriteLine("Creating a '{0}' session writing to {1}", sessionName, dataFileName);
            Out.WriteLine("Use 'logman query -ets' to see active sessions.");
            Out.WriteLine("Use 'logman stop {0} -ets' to manually stop orphans.", sessionName);
            using (var session = new TraceEventSession(sessionName, dataFileName))      // Since we give it a file name, the data goes there.   
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
                Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e) { session.Dispose(); };

                // Enable my provider, you can call many of these on the same session to get other events.   
                var restarted = session.EnableProvider(MyEventSource.Log.Name);
                if (restarted)      // Generally you don't bother with this warning, but for the demo we do.  
                {
                    Out.WriteLine("The session {0} was already active, it has been restarted.", sessionName);
                }

                // Start another thread that Causes MyEventSource to create some events
                // Normally this code as well as the EventSource itself would be in a different process.  
                EventGenerator.CreateEvents();

                Out.WriteLine("Waiting 12 seconds for events to come in.");
                Thread.Sleep(12000);
            }
        }

        /// <summary>
        /// Process the data in 'dataFileName' printing the events and doing delta computation between 'MyFirstEvent'
        /// and 'MySecondEvent'.  
        /// </summary>
        private static void ProcessData(string dataFileName)
        {
            // prepare to read from the session, connect the ETWTraceEventSource to the file
            // Notice there is no session at this point (reading and controlling are not necessarily related). 
            // YOu can read the file on another machine if you like.  
            Out.WriteLine("Opening {0} to see what data is in the file.", dataFileName);
            using (var source = new ETWTraceEventSource(dataFileName))
            {
                if (source.EventsLost != 0)
                {
                    Out.WriteLine("WARNING: there were {0} lost events", source.EventsLost);
                }

                // To demonstrate non-trivial event manipulation, we calculate the time delta between 'MyFirstEvent and 'MySecondEvent'
                // firstEventTimeMSec remembers all the 'MyFirstEvent' arrival times (indexed by their ID)  
                var firstEventTimeMSec = new Dictionary<int, double>();

                /*****************************************************************************************************/
                // Hook up events.   To so this first we need a 'Parser. which knows how to part the events of a particular Event Provider.
                // In this case we get a DynamicTraceEventSource, which knows how to parse any EventSource provider.    This parser
                // is so common, that TraceEventSource as a shortcut property called 'Dynamic' that fetches this parsers.  

                // For debugging, and demo purposes, hook up a callback for every event that 'Dynamic' knows about (this is not EVERY
                // event only those know about by DynamiceTraceEventParser).   However the 'UnhandledEvents' handler below will catch
                // the other ones.
                source.Dynamic.All += delegate (TraceEvent data)
                {
                    Out.WriteLine(data.PayloadByName("MyName"));
                    Out.WriteLine("GOT EVENT: " + data.ToString());
                };

                // Add logic on what to do when we get "MyFirstEvent"
                source.Dynamic.AddCallbackForProviderEvent("Microsoft-Demos-SimpleMonitor", "MyFirstEvent", delegate (TraceEvent data)
                {
                    // On First Events, simply remember the ID and time of the event
                    firstEventTimeMSec[(int)data.PayloadByName("MyId")] = data.TimeStampRelativeMSec;
                });

                // Add logic on what to do when we get "MySecondEvent"
                source.Dynamic.AddCallbackForProviderEvent("Microsoft-Demos-SimpleMonitor", "MySecondEvent", delegate (TraceEvent data)
                {
                    // On Second Events, if the ID matches, compute the delta and display it. 
                    var myID = (int)data.PayloadByName("MyId");
                    double firstEventTime;
                    if (firstEventTimeMSec.TryGetValue(myID, out firstEventTime))
                    {
                        firstEventTimeMSec.Remove(myID);            // We are done with the ID after matching it, so remove it from the table. 
                        Out.WriteLine("   >>> Time Delta from first Event = {0:f3} MSec", data.TimeStampRelativeMSec - firstEventTime);
                    }
                    else
                    {
                        Out.WriteLine("   >>> WARNING, Found a 'SecondEvent' without a corresponding 'FirstEvent'");
                    }
                });

                // Add logic on what to do when we get "Stop"
                source.Dynamic.AddCallbackForProviderEvent("Microsoft-Demos-SimpleMonitor", "MyStopEvent", delegate (TraceEvent data)
                {
                    Out.WriteLine("    >>> Got a stop message");
                    // Stop processing after we we see the 'Stop' event
                    source.StopProcessing();
                });

#if DEBUG
                // There may be events that are not handled by ANY of the callbacks above.  This is often a bug in your program 
                // (you forgot a callback).  In debug we would like to see these to know that something is probably wrong.  
                // The 'UnhandledEvents' callback will be called whenever no other callback has handled the event.   There is
                // also an 'All callback on the source (not a particular parser) that will call back on EVERY event from the source
                source.UnhandledEvents += delegate(TraceEvent data)
                {
                    if ((int)data.ID != 0xFFFE)         // The EventSource manifest events show up as unhandled, filter them out.
                    {
                        Out.WriteLine("GOT UNHANDLED EVENT: " + data.Dump());
                    }
                };
#endif

                // go into a loop processing events can calling the callbacks.  This will return when the all the events
                // In the file are processed, or the StopProcessing() call is made.  
                source.Process();
                Out.WriteLine("Done Processing.");
            }
        }
    }
}
