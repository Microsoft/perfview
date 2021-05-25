using Graphs;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Session;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

/// <summary>
/// This class dumps the .NET GC Heap using ETW events.   
/// </summary>
public class DotNetHeapDumper
{
    /// <summary>
    /// Dump the  dot net Heap for process 'processID' to the etl file name 'etlFileName'.  Send diagnostics to 'log'.
    /// If 'memoryGraph is non-null also update it to contain the heap Dump.  If null you get just the ETL file. 
    /// returns true if successful. 
    /// </summary>
    public static bool DumpAsEtlFile(int processID, string etlFileName, TextWriter log, MemoryGraph memoryGraph = null, DotNetHeapInfo dotNetInfo = null)
    {
        bool success = false;

        log.WriteLine("Starting ETW logging on File {0}", etlFileName);
        using (var session = new TraceEventSession("PerfViewGCHeapETLSession", etlFileName))
        {
            session.BufferSizeMB = 256;
            session.EnableKernelProvider(KernelTraceEventParser.Keywords.Process | KernelTraceEventParser.Keywords.Thread | KernelTraceEventParser.Keywords.ImageLoad);

            // Isolate this to a single process.  
            var options = new TraceEventProviderOptions() { ProcessIDFilter = new List<int>(1) { processID } };

            // There is a bug in the runtime 4.6.2 and earlier where we only clear the table of types we have already emitted when you ENABLE 
            // the Clr Provider WITHOUT the ClrTraceEventParser.Keywords.Type keyword. We achieve this by turning on just the GC events,
            // (which clears the Type table) and then turn all the events we need on.   
            // Note we do this here, as well as in Dump() because it only works if the CLR Type keyword is off (and we turn it on below)
            session.EnableProvider(ClrTraceEventParser.ProviderGuid, TraceEventLevel.Informational, (ulong)ClrTraceEventParser.Keywords.GC, options);
            System.Threading.Thread.Sleep(50);      // Wait for it to complete (it is async)

            // For non-project N we need module rundown to figure out the correct module name
            session.EnableProvider(ClrRundownTraceEventParser.ProviderGuid, TraceEventLevel.Verbose,
                    (ulong)(ClrRundownTraceEventParser.Keywords.Loader | ClrRundownTraceEventParser.Keywords.ForceEndRundown), options);

            session.EnableProvider(ClrTraceEventParser.ProviderGuid, TraceEventLevel.Informational,
                (ulong)(ClrTraceEventParser.Keywords.GCHeapDump | ClrTraceEventParser.Keywords.GC | ClrTraceEventParser.Keywords.Type | ClrTraceEventParser.Keywords.GCHeapAndTypeNames), options);
            // Project  N support. 
            session.EnableProvider(ClrTraceEventParser.NativeProviderGuid, TraceEventLevel.Informational,
                (ulong)(ClrTraceEventParser.Keywords.GCHeapDump | ClrTraceEventParser.Keywords.GC | ClrTraceEventParser.Keywords.Type | ClrTraceEventParser.Keywords.GCHeapAndTypeNames), options);

            success = Dump(processID, memoryGraph, log, dotNetInfo);
            log.WriteLine("Stopping ETW logging on {0}", etlFileName);
        }

        log.WriteLine("DumpAsETLFile returns.  Success={0}", success);
        return success;
    }

    /// <summary>
    /// Add the nodes of the .NET heap for the process 'processID' to 'memoryGraph' sending diagnostic
    /// messages to 'log'.  If 'memoryGraph' is null, the ETW providers are triggered by we obviously
    /// don't update the memoryGraph.  Thus it is only done for the side effect of triggering a .NET heap 
    /// dump. returns true if successful. 
    /// </summary>
    public static bool Dump(int processID, MemoryGraph memoryGraph, TextWriter log, DotNetHeapInfo dotNetInfo = null)
    {
        var sw = Stopwatch.StartNew();
        var dumper = new DotNetHeapDumpGraphReader(log);
        dumper.DotNetHeapInfo = dotNetInfo;
        bool dumpComplete = false;
        bool listening = false;
        TraceEventSession session = null;
        Task readerTask = null;
        try
        {
            bool etwDataPresent = false;
            TimeSpan lastEtwUpdate = sw.Elapsed;
            // Set up a separate thread that will listen for ETW events coming back telling us we succeeded. 
            readerTask = Task.Factory.StartNew(delegate
            {
                string sessionName = "PerfViewGCHeapSession";
                session = new TraceEventSession(sessionName, null);
                int gcNum = -1;
                session.BufferSizeMB = 1024;         // Events come pretty fast, so make the buffer bigger. 

                // Start the providers and trigger the GCs.  
                log.WriteLine("{0,5:n1}s: Requesting a .NET Heap Dump", sw.Elapsed.TotalSeconds);
                // Have to turn on Kernel provider first (before touching Source) so we do it here.     
                session.EnableKernelProvider(KernelTraceEventParser.Keywords.Process | KernelTraceEventParser.Keywords.ImageLoad);

                session.Source.Clr.GCStart += delegate (GCStartTraceData data)
                {
                    if (data.ProcessID != processID)
                    {
                        return;
                    }

                    etwDataPresent = true;

                    if (gcNum < 0 && data.Depth == 2 && data.Type != GCType.BackgroundGC)
                    {
                        gcNum = data.Count;
                        log.WriteLine("{0,5:n1}s: Dot Net Dump Started...", sw.Elapsed.TotalSeconds);
                    }
                };

                session.Source.Clr.GCStop += delegate (GCEndTraceData data)
                {
                    if (data.ProcessID != processID)
                    {
                        return;
                    }

                    if (data.Count == gcNum)
                    {
                        log.WriteLine("{0,5:n1}s: DotNet GC Complete.", sw.Elapsed.TotalSeconds);
                        dumpComplete = true;
                    }
                };

                session.Source.Clr.GCBulkNode += delegate (GCBulkNodeTraceData data)
                {
                    if (data.ProcessID != processID)
                    {
                        return;
                    }

                    etwDataPresent = true;

                    if ((sw.Elapsed - lastEtwUpdate).TotalMilliseconds > 500)
                    {
                        log.WriteLine("{0,5:n1}s: Making  GC Heap Progress...", sw.Elapsed.TotalSeconds);
                    }

                    lastEtwUpdate = sw.Elapsed;
                };

                if (memoryGraph != null)
                {
                    dumper.SetupCallbacks(memoryGraph, session.Source, processID.ToString());
                }

                listening = true;
                session.Source.Process();
                log.WriteLine("{0,5:n1}s: ETW Listener dieing", sw.Elapsed.TotalSeconds);
            });

            // Wait for thread above to start listening (should be very fast)
            while (!listening)
            {
                readerTask.Wait(1);
            }

            Debug.Assert(session != null);

            // Request the heap dump.   We try to isolate this to a single process.  
            var options = new TraceEventProviderOptions() { ProcessIDFilter = new List<int>(1) { processID } };

            // There is a bug in the runtime 4.6.2 and earlier where we only clear the table of types we have already emitted when you ENABLE 
            // the Clr Provider WITHOUT the ClrTraceEventParser.Keywords.Type keyword.  we achieve this by turning on just the GC events, 
            // (which clears the Type table) and then turn all the events we need on.   
            session.EnableProvider(ClrTraceEventParser.ProviderGuid, TraceEventLevel.Informational, (ulong)ClrTraceEventParser.Keywords.GC, options);
            System.Threading.Thread.Sleep(50);      // Wait for it to complete (it is async)

            // For non-project N we need module rundown to figure out the correct module name
            session.EnableProvider(ClrRundownTraceEventParser.ProviderGuid, TraceEventLevel.Verbose,
                (ulong)(ClrRundownTraceEventParser.Keywords.Loader | ClrRundownTraceEventParser.Keywords.ForceEndRundown), options);

            session.EnableProvider(ClrTraceEventParser.ProviderGuid, TraceEventLevel.Informational, (ulong)ClrTraceEventParser.Keywords.GCHeapSnapshot, options);
            // Project N support. 
            session.EnableProvider(ClrTraceEventParser.NativeProviderGuid, TraceEventLevel.Informational, (ulong)ClrTraceEventParser.Keywords.GCHeapSnapshot, options);

            for (; ; )
            {
                if (readerTask.Wait(100))
                {
                    break;
                }

                if (!etwDataPresent && sw.Elapsed.TotalSeconds > 5)      // Assume it started within 5 seconds.  
                {
                    log.WriteLine("{0,5:n1}s: Assume no Dot Heap", sw.Elapsed.TotalSeconds);
                    break;
                }
                if (sw.Elapsed.TotalSeconds > 100)       // Time out after 100 seconds. 
                {
                    log.WriteLine("{0,5:n1}s: Timed out after 100 seconds", sw.Elapsed.TotalSeconds);
                    break;
                }
                // TODO FIX NOW, time out faster if we seek to be stuck
                if (dumpComplete)
                {
                    break;
                }
            }
            if (etwDataPresent)
            {
                dumper.ConvertHeapDataToGraph();        // Finish the conversion.  
            }
        }
        finally
        {
            // Stop the ETW providers
            log.WriteLine("{0,5:n1}s: Shutting down ETW session", sw.Elapsed.TotalSeconds);
            if (session != null)
            {
                session.Dispose();
            }
        }
        if (readerTask != null)
        {
            log.WriteLine("{0,5:n1}s: Waiting for shutdown to complete.", sw.Elapsed.TotalSeconds);
            if (!readerTask.Wait(2000))
            {
                log.WriteLine("{0,5:n1}s: Shutdown wait timed out after 2 seconds.", sw.Elapsed.TotalSeconds);
            }
        }
        log.WriteLine("[{0,5:n1}s: Done Dumping .NET heap success={1}]", sw.Elapsed.TotalSeconds, dumpComplete);

        return dumpComplete;
    }
}
