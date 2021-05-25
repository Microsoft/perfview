using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Session;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reactive.Linq;

namespace TraceEventSamples
{
    internal class ObserveGCEvents
    {
        /// <summary>
        /// Where all the output goes.  
        /// </summary>
        private static TextWriter Out = AllSamples.Out;

        public static void Run()
        {
            Out.WriteLine("******************** ObserveGCEvents DEMO ********************");
            Out.WriteLine("This program Demos using the reactive framework (IObservable) to monitor");
            Out.WriteLine(".NET Garbage collector (GC) events.");
            Out.WriteLine();
            Out.WriteLine("The program will print a line every time 100K of memory was allocated");
            Out.WriteLine("on the GC heap along with the type of the object that 'tripped' the 100K");
            Out.WriteLine("sample.   It will also print a line every time a GC happened and show ");
            Out.WriteLine("the sizes of each generation after the GC.");
            Out.WriteLine();
            Out.WriteLine("Run a .NET Program while the monitoring is active to see GC events.");
            Out.WriteLine();

            if (TraceEventSession.IsElevated() != true)
            {
                Out.WriteLine("Must be elevated (Admin) to run this method.");
                Debugger.Break();
                return;
            }
            var monitoringTimeSec = 10;
            Out.WriteLine("The monitor will run for a maximum of {0} seconds", monitoringTimeSec);
            Out.WriteLine("Press Ctrl-C to stop monitoring of GC Allocs");

            // create a real time user mode session
            using (var userSession = new TraceEventSession("ObserveGCAllocs"))
            {
                // Set up Ctrl-C to stop the session
                SetupCtrlCHandler(() => { userSession.Stop(); });

                // enable the CLR provider with default keywords (minus the rundown CLR events)
                userSession.EnableProvider(ClrTraceEventParser.ProviderGuid, TraceEventLevel.Verbose,
                    (ulong)(ClrTraceEventParser.Keywords.GC));

                // Create a stream of GC Allocation events (happens every time 100K of allocations happen)
                IObservable<GCAllocationTickTraceData> gcAllocStream = userSession.Source.Clr.Observe<GCAllocationTickTraceData>();

                // Print the outgoing stream to the console
                gcAllocStream.Subscribe(allocData =>
                    Out.WriteLine("GC Alloc  :  Proc: {0,10} Amount: {1,6:f1}K  TypeSample: {2}", GetProcessName(allocData.ProcessID), allocData.AllocationAmount / 1000.0, allocData.TypeName));

                // Create a stream of GC Collection events
                IObservable<GCHeapStatsTraceData> gcCollectStream = userSession.Source.Clr.Observe<GCHeapStatsTraceData>();

                // Print the outgoing stream to the console
                gcCollectStream.Subscribe(collectData =>
                    Out.WriteLine("GC Collect:  Proc: {0,10} Gen0: {1,6:f1}M Gen1: {2,6:f1}M Gen2: {3,6:f1}M LargeObj: {4,6:f1}M PinObj {4,6:f1}M",
                         GetProcessName(collectData.ProcessID),
                         collectData.GenerationSize0 / 1000000.0,
                         collectData.GenerationSize1 / 1000000.0,
                         collectData.GenerationSize2 / 1000000.0,
                         collectData.GenerationSize3 / 1000000.0,
                         0));
                // TODO, enable POH display once TraceEvent package is updated
                // collectData.GenerationSize4 / 1000000.0));

                IObservable<long> timer = Observable.Timer(new TimeSpan(0, 0, monitoringTimeSec));
                timer.Subscribe(delegate
                {
                    Out.WriteLine("Stopped after {0} sec", monitoringTimeSec);
                    userSession.Dispose();
                });

                // OK we are all set up, time to listen for events and pass them to the observers.  
                userSession.Source.Process();
            }
            Out.WriteLine("Done with program.");
        }

        /// <summary>
        /// Returns the process name for a given process ID
        /// </summary>
        private static string GetProcessName(int processID)
        {
            // Only keep the cache for 10 seconds to avoid issues with process ID reuse.  
            var now = DateTime.UtcNow;
            if ((now - s_processNameCacheLastUpdate).TotalSeconds > 10)
            {
                s_processNameCache.Clear();
            }

            s_processNameCacheLastUpdate = now;

            string ret = null;
            if (!s_processNameCache.TryGetValue(processID, out ret))
            {
                Process proc = null;
                try { proc = Process.GetProcessById(processID); }
                catch (Exception) { }
                if (proc != null)
                {
                    ret = proc.ProcessName;
                }

                if (string.IsNullOrWhiteSpace(ret))
                {
                    ret = processID.ToString();
                }

                s_processNameCache.Add(processID, ret);
            }
            return ret;
        }
        private static Dictionary<int, string> s_processNameCache = new Dictionary<int, string>();
        private static DateTime s_processNameCacheLastUpdate;

        #region Console CtrlC handling
        private static bool s_bCtrlCExecuted;
        private static ConsoleCancelEventHandler s_CtrlCHandler;
        /// <summary>
        /// This implementation allows one to call this function multiple times during the
        /// execution of a console application. The CtrlC handling is disabled when Ctrl-C 
        /// is typed, one will need to call this method again to re-enable it.
        /// </summary>
        /// <param name="action"></param>
        private static void SetupCtrlCHandler(Action action)
        {
            s_bCtrlCExecuted = false;
            // uninstall previous handler
            if (s_CtrlCHandler != null)
            {
                Console.CancelKeyPress -= s_CtrlCHandler;
            }

            s_CtrlCHandler =
                (object sender, ConsoleCancelEventArgs cancelArgs) =>
                {
                    if (!s_bCtrlCExecuted)
                    {
                        s_bCtrlCExecuted = true;    // ensure non-reentrant

                        Out.WriteLine("Stopping monitor");

                        action();                   // execute custom action

                        // terminate normally (i.e. when the monitoring tasks complete b/c we've stopped the sessions)
                        cancelArgs.Cancel = true;
                    }
                };
            Console.CancelKeyPress += s_CtrlCHandler;
        }
        #endregion
    }
}