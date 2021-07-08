using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using TraceEventSamples.Producer;

/* README FIRST */
// This program is a simple demo that demonstrates both the EventSource to generate ETL events as 
// well as using the ETWTraceEventSource class to read the resulting ETW events in real time.   
//
// The basic flow of the program is
//     * Create a 'real time' TraceEventSession.  This lets you control what events you wish to collect
//     * Connect a ETWTraceEventSource for the session using the 'Source' property.   A TraceEventSource 
//           represents the stream of events coming from the session.  
//     * Connect a TraceEventParser to the source.    Parsers know how to interpret events from particular 
//          ETW providers.  In this case we care about EVentSource events and the DynamicTraceEventParser
//          is the parser that cares about it.  
//     * Attach callbacks to the Parser for the events you are interested in.  We use the 'All' C# event 
//          for this.  
//     * Enable the ETW providers of interest (in this case Microsoft-Demos-SimpleMonitor) 
//     * Kick off EventGenerator to generate some Microsoft-Demos-SimpleMonitor events (normally in another process).  
//     * Call the ETWTraceEventSource.Process() method, which waits for events and sends them to the callbacks. 
//     * At this point callbacks get called when the events come in.  
//     
// After 'Process()' is called, it will return if
//     * ETWTraceEventSource.StopProcessing() is called.
//     * The TraceEventSession.Dispose() is called.  
namespace TraceEventSamples
{
    /// <summary>
    /// The main program is the 'listener' that listens and processes the events that come from ANY 
    /// process that is generating Microsoft-Demos-SimpleMonitor events.  
    /// </summary>
    internal class SimpleEventSourceMonitor
    {
        private static TextWriter Out = AllSamples.Out;

        /// <summary>
        /// This is a demo of using TraceEvent to activate a 'real time' provider that is listening to 
        /// the MyEventSource above.   Normally this event source would be in a different process,  but 
        /// it also works if this process generate the events and I do that here for simplicity.  
        /// </summary>
        public static int Run()
        {
            Out.WriteLine("******************** SimpleEventSourceMonitor DEMO ********************");
            Out.WriteLine("This program generates processes and displays EventSource events");
            Out.WriteLine("using the ETW REAL TIME pipeline. (thus no files are created)");
            Out.WriteLine();

            // Today you have to be Admin to turn on ETW events (anyone can write ETW events).   
            if (!(TraceEventSession.IsElevated() ?? false))
            {
                Out.WriteLine("To turn on ETW events you need to be Administrator, please run from an Admin process.");
                Debugger.Break();
                return -1;
            }

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
            Out.WriteLine("Use 'logman query -ets' to see active sessions.");
            Out.WriteLine("Use 'logman stop {0} -ets' to manually stop orphans.", sessionName);
            using (var session = new TraceEventSession(sessionName))
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
                //     * use the same session name (say your program name) run-to-run so you don't create many orphans. 
                //
                // By default TraceEventSessions are in 'create' mode where it assumes you want to create a new session.
                // In this mode if a session already exists, it is stopped and the new one is created.   
                // 
                // Here we install the Control C handler.   It is OK if Dispose is called more than once.  
                Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e) { session.Dispose(); };

                // To demonstrate non-trivial event manipulation, we calculate the time delta between 'MyFirstEvent and 'MySecondEvent'
                // firstEventTimeMSec remembers all the 'MyFirstEvent' arrival times (indexed by their ID)  
                var firstEventTimeMSec = new Dictionary<int, double>();

                /*****************************************************************************************************/
                // Hook up events.   To do this we first  need a 'Parser. which knows how to part the events of a particular Event Provider.
                // In this case we get a DynamicTraceEventSource, which knows how to parse any EventSource provider.    This parser
                // is so common, that TraceEventSource has a shortcut property called 'Dynamic' that fetches this parser.  

                // For debugging, and demo purposes, hook up a callback for every event that 'Dynamic' knows about (this is not EVERY
                // event only those known about by DynamiceTraceEventParser).   However the 'UnhandledEvents' handler below will catch
                // the other ones.
                session.Source.Dynamic.All += delegate (TraceEvent data)
                {
                    // ETW buffers events and only delivers them after buffering up for some amount of time.  Thus 
                    // there is a small delay of about 2-4 seconds between the timestamp on the event (which is very 
                    // accurate), and the time we actually get the event.  We measure that delay here.     
                    var delay = (DateTime.Now - data.TimeStamp).TotalSeconds;
                    Out.WriteLine("GOT Event Delay={0:f1}sec: {1} ", delay, data.ToString());
                };

                // Add logic on what to do when we get "MyFirstEvent"
                session.Source.Dynamic.AddCallbackForProviderEvent("Microsoft-Demos-SimpleMonitor", "MyFirstEvent", delegate (TraceEvent data)
                {
                    // On First Events, simply remember the ID and time of the event
                    firstEventTimeMSec[(int)data.PayloadByName("MyId")] = data.TimeStampRelativeMSec;
                });

                // Add logic on what to do when we get "MySecondEvent"
                session.Source.Dynamic.AddCallbackForProviderEvent("Microsoft-Demos-SimpleMonitor", "MySecondEvent", delegate (TraceEvent data)
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
                session.Source.Dynamic.AddCallbackForProviderEvent("Microsoft-Demos-SimpleMonitor", "MyStopEvent", delegate (TraceEvent data)
                {
                    Out.WriteLine("    >>> Got a stop message");
                    // Stop processing after we we see the 'Stop' event, this will 'Process() to return.   It is OK to call Dispose twice 
                    session.Source.Dispose();
                });

#if DEBUG
                // The callback above will only be called for events the parser recognizes (in the case of DynamicTraceEventParser, EventSources)
                // It is sometimes useful to see the other events that are not otherwise being handled.  The source knows about these and you 
                // can ask the source to send them to you like this.  
                session.Source.UnhandledEvents += delegate(TraceEvent data)
                {
                    if ((int)data.ID != 0xFFFE)         // The EventSource manifest events show up as unhanded, filter them out.
                    {
                        Out.WriteLine("GOT UNHANDLED EVENT: " + data.Dump());
                    }
                };
#endif
                // At this point we have created a TraceEventSession, hooked it up to a TraceEventSource, and hooked the
                // TraceEventSource to a TraceEventParser (you can do several of these), and then hooked up callbacks
                // up to the TraceEventParser (again you can have several).  However we have NOT actually told any
                // provider (EventSources) to actually send any events to our TraceEventSession.  
                // We do that now.  

                // Enable my provider, you can call many of these on the same session to get events from other providers.  
                // Because this EventSource did not define any keywords, I can only turn on all events or none.  
                var restarted = session.EnableProvider("Microsoft-Demos-SimpleMonitor");
                if (restarted)      // Generally you don't bother with this warning, but for the demo we do. 
                {
                    Out.WriteLine("The session {0} was already active, it has been restarted.", sessionName);
                }

                // Start another thread that Causes MyEventSource to create some events
                // Normally this code as well as the EventSource itself would be in a different process.  
                EventGenerator.CreateEvents();

                Out.WriteLine("**** Start listening for events from the Microsoft-Demos-SimpleMonitor provider.");
                // go into a loop processing events can calling the callbacks.  Because this is live data (not from a file)
                // processing never completes by itself, but only because someone called 'source.Dispose()'.  
                session.Source.Process();
                Out.WriteLine();
                Out.WriteLine("Stopping the collection of events.");
            }
            return 0;
        }
    }
}

