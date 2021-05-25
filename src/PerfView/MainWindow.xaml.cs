using Controls;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Diagnostics.Utilities;
using PerfView.Dialogs;
using PerfView.GuiUtilities;
using PerfViewExtensibility;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using Utilities;

/* Master TODO list */
// ****** LARGER ISSUES  ******** 
// Better JSON support.  
// Better control of static variable in C#
// Add PreStub Event (useful for working set regression)
// Add Class loaded Event (useful for working set regressions)

/* .NET Runtime (/
// Make Allocation Tick variable.  
// JitStarted Info level (not verbose)
// Consolidate the GCRange events into one event
// Insure that we don't have 2 GC Start events (Or others...)
// Allow ClrStress to pay attention to level and keyword.
// GC allocation stacks should work without process restart.  
// Keyword if you only care about large objects.
// Guarantee that every large object gets an event. 
// Review and insure that we don't have any CLRStacks on any events that don't need them.  
// PinObjectAtGCTime and FinalizeObject seem to have a non-trivial impact in some scenarios.    Both should NOT have stacks!

// The efficiency of stopwatch can be improved (at least review).
// DateTime.Now should be more efficient (cache timeZone, should not allocate)
//

// ****** BLOGGING IDEAS ******** 
// Tell people about finding TDH manifest errors. 
// Blog on 32 / 64 bit perf tradeoffs
// How to use EventSources with logman.  
// Publish a 'Listen' application
// Blog about activities correlation, etc. 
// Make video on 'goto source'  

// ****** OTHER ******** 
// Put WeakHandle benchmarks in MeasureIt 

/******* EVENTSOURCE ********/
// Make it an error if you use implicit event numbering after using explicit numbering

// Add the per-listener logic for filtering (a filter delegate you can register). 
// The EventSource to EventListener path is now pretty poor (goes through encode - decode logic).  
// * When exceptions happen they get swallowed!
// * Remove the limit on the number of strings
// * allow blob support.  
// If you don't make Tasks public the error is very tricky
// Strings with embedded nulls fail.  
// Get Powershell on the EventSource plan
// In particular in the short term. 
// Don't like the keyword 0 semantics
// Make doing start and stop easy
// Can we get Tofino on board (what about VS, as a plugin?)
// Get Powershell consumption.  
// Solve the lost manifest problem (circular buffer with process that dies).  (Rundown at process shutdown?)
// Diagnostic mode in EventSource.  
// Think about versioning of eventSources (return nulls if you are off the end of an event)
// Make EventSource parsing work with multiple versions (basically don't go off the end of the buffer).

/**************** PERFVIEW ****************/
/* EXTERNAL REPORTS */
// Localization bug Windows 102699  
//          Hi, In PerfView you can select cells in the data view and it will sum up the values in the bottom of the window. Problem is, it converts the numbers for displaying using American culture (eg. 1,234.56), while the sum functionality interprets the formatted numbers using the local culture (in my case danish: 1.234,56). I took a screenshot: imgur.com/GbYEL The values selected are 95 and 2.9 and they get summed up to 979. Similarly, when I select 4717 and 142 they get summed up to 146.717. I also found the same problem in the event window. If you select a time cell in the data view and right click -> Open Any Stacks, you will get a CPU stack window where the start and end time has been set to the wrong number. Example: I select the time 319.879 and when the cpu stack window opens, it's empty and the start and end time has been set to 319879. This is again because the data view doesn't use the local culture, while everything else does.

// *** VIDEOS needed
// Catching Intermittent issues Using the /StopOnPerfCounter* and /StopOnRequestMSec* options. (this is probably 2 or 3 videos).  
// Using Diffing to investigate regressions.  
// Using GC Alloc view to track down excessive allocation or large object heap allocation. 
// Tracking down GC issues with the GCStats view. 
// Using the Server Request View
// Investigating Startup issues because of JIT with the JITStats view. 
// Taking heap snapshots from a process dump.  
// Using GC Alloc view to track down excessive allocation or large object heap allocation. 
// Using Excel to manipulate Event Data (File I/O)
// Unmanaged Memory Investigation.
// File or Network I/O investigations.   
// Using the Net Allocation Stacks for .NET programs. 
// Virtual Allocation Investigation 
// Ready Thread View 
// Discovering what ETW providers exist
// Changing the CPU sampling rate (for short scenarios).  
// Using the EventStats view to determine what events are in the trace (and how to reduce them) 
// Automating Data collection (how to call it from batch scripts etc).  
// Writing user commands for perfView
// Writing new views for perfView.  

// Using the CPU CHIP Performance Counters 

/* FOR BEN */
// Add the ability to do pattern matching or comparisons on fields in /StopOnETWEvent.  
// Change exclusive column so that excludes folded items.  
// PerfView Listen (sends to a file or to a text editor).  
// FIx the quoting in the Event Viewer  
// DONE (userCommand DumpEventsAsXml) Add PerfView monitor /providers=*EventSource OUTPUTFILE which allows you to get a simple text output.   
// DONE Allow users to set default groupings.   
// Add ZIP file size to TraceLog report. 
// DONE Log Everything in the log to the ZIP file.  
// Add a provider explorer to PerfView.   

// Add a LISTEN capability (real time listening of eventSources). (finish GenericEventSource).  
// Finish Total memory breakdown view.  (Need to move into HeapDump.exe so it works on any bitness process)
// Add a Refset view.  
// Build some tests! (Figure out test framework)

// A view that shows the fusion logging events in a nice way. 
// Document / tutorials on various views that I have not talked about yet. 
// Allow users to save and restore groupings
// Object Viewer?   
// View for threadpool?  
// The command line cut and paste for the collection views
// Compute EventStats for a range of time.  
// 

/* BLOG ENTRIES */
// New ASP.NET EventSoruce in V4.5.1
// Blog about Directing EventSource to TraceSource.  
// Versioning of ClrTraceEventParser.Keywords.JittedMethodILToNativeMapEventSources. 

/* UNTRIAGED */
// Fix 'leak' in KernelTraceEventParser for the following in the TraceLog in monitoring mode.  
//        internal HistoryDictionary<string> fileIDToName;
//        internal HistoryDictionary<int> threadIDtoProcessID;
//        internal HistoryDictionary<int> threadIDtoProcessIDRundown;
//        TraceLog.threadIDtoThread 

// Interop report in Heap dump seems to be broken disabled for now.  
// Add ability to use wildcards in the names (at least the instance names) of the StopOnPerfCounter option.   
// We have the problem with French making bad numbers in the HeapDUmp output because the Command run does not recognise the data from the process as being UTF8.  This seems to need a framework update.
// Make 'DumpEvent' in the 'events' view work on a range of values.  Allow it to be dumped. as EXCEL?   
// Provide a method of getting at things like EventIndex, DataTime, ... from the 'events' view.  
// Win10 kernel event support.  
// Problem when perf counter names have : in them (escaping mechnanism).  
// From Andre:  A more user-friendly message when you start PerfView and a kernel logger is already running.   
// Plumb the HyperTHreading flag to the TraceLog report. 
// Allow PerfView to extract the heap dump from a live image (or a dump file). 
// For TraceLogging Events don't rely on the event ID for identity.   (sigh).  At the very least don't assume this across processes (ideally sessions).  
// Localization does not seem to be correct still.   See comments by Peter Palotas 20 Jun 2015 http://blogs.msdn.com/b/vancem/archive/2014/11/03/perfview-version-v1-7-released-to-the-web.aspx// 
//    Some local testing suggests that it works properly...
// Registry name stuff needs review.  It probably is unbounded and not clear it looks up names properly and is efficient.  
// Update the symbol server lookup to track the new format with /XX/XXYYY.pdb directories
// Fix it so that the Regsitry events are decode the full paths.  https://social.msdn.microsoft.com/Forums/en-US/ff07fc25-31e3-4b6f-810e-7a1ee458084b/etw-registry-monitoring?forum=etw
// When you do a 'Open Any Stacks' from the Events view with nothing selected or a non-timestamp), it shows everything which is confusing.  Simply cause an error.  
// Use the PssCaptureSnapshot APIs for memory capturing.  See https://msdn.microsoft.com/en-us/library/dn469412(v=vs.85).aspx
// *** Fix the Wiki link (currently broken).
// Make sure that the Win 8.1 mechanisms for turning on events work properly.  (Andre 4/2015)
// Make sure that creating manifest for registered providers works properly (Andre 4/2015)
// Make attaching the PerfVIewLogFile.txt work even if multiple PerfViews are collecting data  
// **** FIX the Daylight savings time issue with sessionStartTime   (Need ot use UTC instead) ** Reply to mail from Jean-Richard on 4/2/2015 
// Fix it so that DynamicTraceEventParser can also look up the TraceLogging data.   
// Make a Rename operation that is like Flatten without removing stack structure.   
// Fix it so that two traceLogging providers don't collide on event IDs.  
// Allow users to pick the segments they care about?  
// Sort GC segments so that you dump the small ones and the large object heap one first.  
// Save and warn people when their heap is being truncated because it is too big.   
// Save the position of stack and event windows. 
// Save a default command line options to be passed on startup. 
// Warn users when the heaps are too big and truncation occurs.  
// Make sampling work on any size heap for 64 bit processes (Probably a dictionary per segment).  
// Obey the Hex attribute when parsing things in the RegisteredEventProvider.  
// Make it so that the text boxes for the Events View are remembered from run to run (like the Stacks view). 
// Fix The lookup of directories in the main view to avoid having to enumerate the directories of the current directory to populate the tree view.   
//    This can probably be done by making it an observable collection and modifying it afterward.   This also avoids the pauses. 
// Deserializing traceLog takes too long because of the FileID->Name tables.   Make the lazy.  
// Support two level store: http://voneinem-windbg.blogspot.de/2008/12/symbol-server-performance-improvement.html
// The ability to trigger on specific field values of an event.   In particular make something that can stop when a process starts.   
// Make sure that (with Tasks) stacks work for project K (coreCLR).  
// Make it so that the 5 min symbol wait gets reset 
// Implement a sampled version of the call count stuff.  
// FIX NOW: Fix assert about multiple stacks for the same event.  The issue is that some kernel stacks transition to user mode
// and our algorithm of attaching user mode stack to each kernel stack on the same thread gets messed up.  
// We don't detect the last of MSCORLIB symbols for activity tracing very well.
// FIX Assert Debug.Assert(eventsToStacks[i].EventIndex <= eventsToStacks[i + 1].EventIndex); in TraceLog.cs to be < not <=
// fix bug where threads have the wrong CPU count in NetFxDev1-SyncModule.etl.zip, depending on the GroupBy pattern.   It seems to cause an assert Debug.Assert(treeNode == m_root); that seems relevant. 
// Make it so the event viewer's 'rest' fields are in the order specified by the field text box.  
// Make sure if you use onlyProvides and ask for stacks you get a good error message.  
// Use the public API to set the CPU sampling rate. 
// Make sure that the hyperlink help for the collection dialog tell you the command line way of doing that particular thing.  
// Counts are wrong for ASP.NET if there are nested events (see writable\users\vancem\NestedAspNet.etl.zip).  
// Allow 0x hex numbers to be put in start and end text boxes (for image Size)
// Make it so that we probe both SVR* and normal file on _NT_SYMBOL_PATH entries.  
// Trigger on a process start or stop 
// Allow the help to query the DataFile to figure out where to get the help.   Make better help for the Image Files stuff  
// Add the parent Process and Thread ID that created the thread to the threadStart event
// When you open from the command line, sometimes it does not oonpe it.s  theyou n
// Add Tool Tip that shows size for the files and timesode (X
// Capture the Path and timestamp and file size of the ETL file and reject it if the timestamps don't match.  
// Make dumping very large heaps (over 125M objects) work by either : sampling segments randomly, or, collecting multiple graphs and combining the sampled graphs. 
// FIx ASP.NET Stats to work per-process.  
// Look into races associated with having the log window open.   We seem to get indexout of range error in the 'StringBuilder.ToString' in Flush's anonymous delegate
// Make triggering work for process starts (maybe just allow pattern matching on etw fields) 
// Document the /StartOnPerfCouner for taking heap snapshots (also allow periodic).  
// Add a registeredTraceEventParser example to the trace eventSamples.  
// Fix it so that KernelTraceEventParser's memory usage does not grow without bound in the monitoring case. (HistoryDictionaries).  
// Change the sampling to keep more objects that have only a small number (may not be that interesting)
// Add logic to keep all structures bounded in long real time TraceLog sessions. 
// Make the TraceLog Stacks scenario (see 41_TraceLogMonitor.cs), nice for looking up stacks.  Make LookupSymbolsForModule efficient.  
// Decide what to do about Project N Guid (thinking maybe registering a callback?  Or just special knowledge of CLRTraceEventParser?)
// Investigate why some events have multiple stacks associated with them.  (See tutorial.etl event ID 43387)
// Fix so that you can have multiple PerfViews running
// Don't use a separate kernel and user etL file (maybe use in memory for everything).  
// Make the InMemoryBuffer the default
// For the help in the collection dialog, indicate how to get it from the command line.   
// TraceParserGen should allow multiple providers 
// TraceParserGen fails if the events have no payloads (see mail on 7/2/2014 from Ritesh)
// Fix it so that local PDBs are searched first without compromising security.  
// Hide ETLX in the PerfView view (at least externally) Or make ETLX more stable 
// Why is it that the top node (process) does not fold or group?  Fix this.
// Confirm that ClrStress works in perfVIew 

// Make it fail if Process() is called from another thread than the constructor for a ETWTraceEventSource.  
// FInalizeObject seems to be broken (only gives Meta data token and give a Bulk Time every time!).  
// Make sure FinalizeObject is useful (you can look up the type ID to get the type name)
// FIX ForceGC for project K, N
// Fix overflow issues in TraceEventSession filters (see mail from Fernando on 5/30/2014
// Add support for winBinary payloads to dynamicTraceEventParser.  
// Fix it so that PerfCoutner stuff works properly for 64 bit .NET processes (you have to run perfCoutners in a 64 bit process) Uggh.  
// Fix MonitorPerfCounter to also work for 64 bit.  
// Fix session names so that you can have multiple versions of PerfView running without interference.  
// Make the event window sort numerically when appropriate. (Done partially)
// Implement a Finalizable object  view.  (maybe a large object view as well).
// If you run your Process() on another thread than the session, things don't work.   Throw an error for this
// Remove TraceEventOptions.ShouldResolveSymbols
// Doc what the Count is for Thread Time Views.  
// Add names to Profile Sources in the Any Stacks view.  
// Add documentation for Server Request View 'understanding Perf Data' link. 
// Bug see trace devdiv-integration-auto7-3083648-PerfMetrics-Iteration-1*etl.zip  load at 10201.735.  Has frame count of 98 but splices RtlCompareEncodedBlobs. 
// REview the ReadAllSamples bug from David Klimek (4/9/2014).   Basically the assumption that metric is time is broken when scaling...
// Add support for SQL events (BidInterface) (see mail from Kurt Schenk on 2/14/2014)  See AdoNetAndPerfView directory.  
//    I tried the following but not working yet. Followed instructions 
//    at http://blogs.msdn.com/b/spike/archive/2010/10/22/simplified-steps-for-creating-bid-etw-traces-for-ado-net-and-sqlncli.aspx
//    And http://msdn.microsoft.com/en-us/library/cc765421.aspx#_A:_MOF_Files
// Add documentation for doing TCP/IP network analysis.   
// In eventRegsiter there is a new version of CommandLineParser that understandings response files.   Update all other copies.  
// Fix so that FilterState is on the EtlStacks model.   Make sure the XML stuff is there.  We have a default. 
// Make a tool for re-basing the tests for PerfView.
// Have the option of making NGEN pdbs in the MERGE operation in TraceEventSession
// Make CLR Rundown nice.  (It might work well already, if not hide it to make it look nice).  
// Support inline function line numbers (see Maoni's mail 4/14/2014. Look for Dia2Dump vctools\pdb\src\dia2dump).  
// Fix it so that /NoGui does not prevent the PerfViewLog messages from getting into the ETL file.  
// Compensate for DPCs and ISRs in the CPU view.  
// Negative Number in Heap dump (in 2014) (WAworkerHost.dmp?)
// InMemmoryCircularBuffer does not work uniformly.  Its rundown is a bit wonky (you get DCStart and ends right next to each other).    
// Capture PerfView log file output before the user mode provider is on, and in the circular buffer case.  
// Could reorder user and kernel mode session creation to capture PMC stuff in the log better.    
// Add the PMC ProfileSource names to the events in the AnyStacks view.  
// Add an annotation whether a type is a value type or reference type to the allocation stack view (can be done for the ObjectAllocation event).  
// Esc key should cancel dialog boxes.  
// Mukul wanted EVENT_TRACE_INDEPENDENT_SESSION_MODE (3/6/2014)
// Improve usability with circular buffers.   In particular on Win8 use only one session so that you don't have as much weirdness.  
// Turn on unmanaged StreamWriter 
// Add support to EnableProvider to support the Timeout feature (maybe just give a default timeout of a few seconds).  
// Issue in UI where sometimes the children of a ETL file are not displayed (you have to reopen the children).  Happens after recollecting to same file among other times.

// Add PerfView prettyPrint INPUTFILE which allow you to get the same from and ETL file.  
// Sort out PMC support in the TraceEventSession and the need for KernelTraceControl etc.   
// Make it easy to remit CAPTURE STATE information in multi-file scenarios (probably need a EnabledProviders API)
// Put in checks so that if people don't enable kernel events first it gives an error message (rather than simply failing) 
// Bad perf on TraceLog.GetEvent() Feng (2/19/2014)
// Make Printf style output of ETW stuff SUPER easy.  (like PerfMonitor). 
//* Fix it so that you can have multiple instances of PerfVIew running on Win8 boxes at least.    Warn on downlevel
// Obey the hex specifier in PayloadString in DynamicTraceEventParser.cs 
// TFS*gcdump failure because of stack overflow.  
// Make it so that PerfView can be launched as 64 bit if necessary.  
// Add point that JavaScript heap dumping only works on Win8 and above to the docs
// If you have a lot of threads, cut the default fold so you don't get everything folded away. 
// Add view for reference set 
// Fix the VirtualAlloc Region to not be O(n) in the number of regions.   (Very slow when you have lots of allocs and frees).   (2/4/2014)] mukul's traces.  
// Make it so that you can specify performance counter instances using process ID or a pattern match of the command line 
// Expose the number of samples that need to be above threshold to be triggered in /stopOnPerfCounter.  
// Determine if Microsoft-Windows-Process can be used to get process names for real time sessions.  
// Make ETLX hold the timestamp of the file it was generated from, and use that as an ID to check for validity.  
// DOC Put that document how costly each of checkboxes are in the collection dialog 
// DOC Indicate how to use perfmon to get performance counter names 
// DOC Tell people about logman etc to get provider names keywords, events.   
// EventStats counts (David Berg 1/29/2014) don't add up.  
// Make it so that at least on windows 8 you can run two perFView sessions simultaneously
// Make PerfVIew understand Microsoft-Kernel-Process events (so you don't need a kernel session?)
// Make 'View Any Stack' fast by using the range information to isolate it it a range before displaying.  

// Mark if stack is broken because the 192 frame limit is reached.   
// Make the relogger sample do a filter by process scenario.
// Make it so that if you have a symbols directory you also cache your symbols there (works locally, make it work on network server).  
// Keyword parsing for Perfview (users wanted it in dumpEvent, but I think it is more useful in collection dialog).  
// Remove the Request URL node from the ASP.NET View?
// Complete the fix for stitching together kernel-user stacks (see CDiscount\PerfVIewData2.etl.zip file)   
// Fix pause time in GCStats view to compensate for GC movement overhead.  
// Finish DeclareFileView.  
// Add 64bit to the MemoryGraph (rather than just GCHeapDump) and set it for ETW case.  
// Make dependent handles act like references from key to value.   
// Make it so that people can find TraceEvent object reuse easier.   
// Add ability to understand XPERF style embedded meta-data to TraceEvent. 
// More work on ObjectViewer 
// More docs on reference set.  
// Tell people about the WS events and how to turn them on.  
// Expose the fusion log events in a nice way 
// Add perftrace functionality to PerfView. 
// Fix probing of crash dump DataTarget.LoadCrashDump => data target instance Architecture.  
// Can enumerate PDBS for modules  
// Add logging and error messages so that when heap dumps are taking on project N and the DAC or PDBs are not present you get a good experience. 
// Make sure that there are easy links to find how to turn on ASP.NET events in the help from various places.  
// Robustness for PDB reader (delete cached file if there is an error?)  Or maybe just don't copy into the cache until complete.      
// Figure out how to do activity IDs in TraceEvent / PerfVIew in a generic way.  
// Feature that keeps track of how long you block trying to get a lock.  
// Document the MemInfoWSTraceData events (how to use Microsoft-Windows-Kernel-Memory keyword 0x40 KERNEL_MEM_KEYWORD_MEMINFO_EX)
// BUG If you open a file in the file view it closes directories.   
// Fix TraceEvent so that GenerateNGenSymbolsForModule is on its own, Fix the sample so that it works off machine.   
// The ability to log perfCounters into the ETW stream at an interval.  
// Be robust with respect to corrupted PDBS,  Add a command to delete the symbol server cache. 
// Fix it so that when you open ETL files in subdirectories in the view does not close the upper directories.  
// FIx DynamicTraceParser to not fail for the whole provider if there is a problem with the manifest.  
// FIX there is an infinite loop when you try to elevate but don't succeed.  
// When looking up symbols, probe for the /dll and /exe subdirectories too.  
// Allow the manipulation of object instances in the stack viewer.  
// Indicate in the BROKEN stacks why it was broken (in particular when the stack depth did it). 
// Make the Event Viewer's DURATION_MSEC smart in that it tracks causality when it uses ActivityIDs to correlate.  
// Add ability to remove unused files in the symbol cache
// Currently data files are kept 5 days.   Should we also take size into account?
// Look up data in the Exchange data.9-25/*9am*WithAllocs to figure out why pinAtGCTime does not work 
// TraceParserGen fails for strings that are ASCII counted (e.g. WinINet).  
// Fail fast if you try to use PerfView heap snapshots on ARM. r
// Make it so that the name of the ETL file shows up in the status bar when it is selected (so that you can cut and paste it).  
// Remove the event limit but have some way of detecting you went over the limit and retry.  
// Wire in the ETW allocation events.  
// Do real time events from different processors come out of order or not?
// Add ability to get absolute time in PerfVIew views.  
// See mail from CHirs Novak 8/13/2013.   THe 'sum=' bar at the bottom gets the wrong answer in cultures using . for ,.  
// Add array bucketting to the allocation stack memory views. GCHeapSimulator.OnObjectAllocated should have logic like GCHeapDumper.GetTypeIndex 
// ARe the problem with real time providers if APIs are called from different threads. If so we should enforce the restriction.  
// We hit the network A LOT for source lookup.  See what we can do.  
// Add a EventSource monitor function.  It should go in real time to the EventViewer.  
// Make it so that PerfView will not use user or temp locations to install on machine if desired (ANdre). 
// Make the event viewer columns sort by numeric value rather than string value when it can.  
// Make the 'All' callback in the TraceEventParsers support the event id 0 (for EventWriteString).  
// Add tool tip for file size and modification time in the file list window.  
// Add compressed and uncompressed size to the TraceInfo window.  
// Change the convention of BinarySearch to match that of List<T>
// Add runtime version to the GCDump format.  
// Allow stack traces to be linked to the memory nodes (Brian's work gets us a good chunk of the way there)
// Document unreachable memory more.
// Use the contention event to give a better call stack when locks are taken to detect deadlock better.  
// DO a heap dump on w3wp-heapsnapshot.gcdump, remove all grouping, folding, exclusions.   Will take a long time and then fail.  (It is a stack overflow in CallTree.AccumulateSumByID)
// A view that shows the fusion logging events in a nice way.  
// Add a processor pseudo field to GC allocation events (maybe other events too).  
// Goto SOurce for javaScript
// symbols for javaScript when you attach. 
// Add Generation to the GCDump view somehow.  
// Add % wall clock time to GC stats view - I think Brian Robbins did this.    
// Collecting a heap snapshot from an unelevated exe seems to be broken. 
// Add support for XPERF embedded meta-data events (can always read WPR generated events).  

// Add an 'images' view, which shows loaded images per process as well as file version info (like XPERF)
// Add feature to a CCW stack view that pairs up adds and releases.  

// Make it work that ListProviderKeywords works on EventSources.  
// Expose the provider exploration APIs through user commands. 
// Take a heap dump at the end of a ETW collection.  
// Make it so that it is VERY hard to created unmerged file (make it on by default but sticky, and pop a dialog box while you do the work);  
// Insure that /merge:false implies /zip:false
// Make default groupings a user preference (it is remembered) 
// Goo error messages if you overflow the 4GB  ETLX limit
// FIx it so that the max event count is not used but we stop before we hit the 4GB limit.  
// Add a error/warning in GCStats view if you see GC Dump events to indicate that times are incorrect. 
// Simplified view for neophytes.   In particular memory and memory leaks seem to be the most common issues (John Robbins from WIntellect) 
//      Large object allocations (strings)
//      DataSets
//      Also event handlers (Proper tyChange (forgot the common one).
// CHange Graph.Node.ToString so that it does not mutate node (so that watch windows don't cause mutation)
// Support unregistering the dynamic event parsers.  
// LImit the size of the Kernel and registry maps for long running applications (Age them out?).  
// Fail more severely if you see a V2.0 runtime when forcing a GC
// Timeouts in ForceGC when you have a service running.  
// I have seen traces where %GC time total is large than any %GC time for individual GCs.  This should not be possible!) 
// Weird DURATION_MMSEC on Mukul's traces (roughly 2/2013)
// Clean up sessionStartTimeQPC.  
// Prefer manifests in the file rather than in ETWManifest.  

// The TreeView's columns are too small (When, First Last).  Why?  Can we fix it? 
// Put counts in the event-viewer (alongside each event).  
// Create profiles for data collection
// Another refcount to change the hyperlinks in the column headers to use the ? hyperlink instead.  
// Remember startup directory in the persisted state so that perfView starts up where it last was.  (at least for double click) 
// Give good warnings about using an inappropriate GC (basically non-server GC on a high-scale application).  
// Pick a differnet color for each trace that is opened, to help users keep them straight.   
// SymbolsForDlls does not work in PerfView.  
// Add ability to open log from collection dialog
// Upgrade to lastest ClrMemDiag.  
// Support Private Trace loggers in TraceEventSession
// PerfView in the Process dialog does not sort propertly in Swedish (see Mail 1/22/2013 Niklas Engfelt).  
// Multi-file TraceEventSource.   Not done.  
// Turning off all events in gui does not turn off all events.  
// Allow users to have more than one active session simultaneously 
// Warn people with a pop-up if the ETLX file is truncated.  
// Add a disabled non-circular buffer capability for collection (just don't make it easy to turn on). 
// Solve the problem of EventSources not haveing a manifest in a circular buffer case if the process dies.   
//     At least warn people about the effect.  (maybe allow people to cache manifests).  
// Remember the default parameters per-user
// Change it so that it zips by default (but remembers per user).  
// Support enumerations as symbolic value for the RegsiterEventParser (TdhGetEventMapInformation)
// In the main view, allow right click to set the selection item.  
// Zip by default until you turn it off.  
// Fix so that you can turn off providers completely.  
// Dave's bugs
//    Slowness
//    OOM
//    WHich 
//    Sampling needed 
//    
// When selecting ranges from the Which field can we keep the data in the status bar. 
// The file name completion needs to be faster (read in the files)
// Exception dialog box does not come to the front.  Very confusing!!
// The SaveCPUScenarioStacks feature does not work properly with explicit time range and process when there are mulitple processes

// JavaScript allocation stacks. 
// Grouping with mscorlib!->XXX;*->OTHER (which matches everyting), does surprising things (most things folded away)
// Folding away top level nodes. 
// When you have a lot of expicit fields, in the viewer they look like 'Rest' but in Excel they don't and the filtering does not work. 
// Disable goto source in memory views (or make it work!).  
// Better filtering for allocation stacks.  per type, after 128 items throttle to a rate of no more than 128 /sec.  
// Launching PerfView with a command line arg does not change the view directory.  
// Fix it so that the timestamps used in the ETW trigger are closely related to the timestamps used in the log 
// (use absolute time?)  
// A When field for Cores.  
// Try harder to resynchronize when heap dumps are walking the heap.  
// When Dumps partially fail, makes sure users know that that happened.  
// Add right click option when a time is selected to get the actual date-time printed to the status bar / log.  

// Give some guidance on how expensive Async is (how big the work item has to be to make perf issues small)

// SERIOUS BUG: ASP.NET seems to leak memory (just keep hitting update)
// Create an 'Image Version Report view'
// When heap dump fails (at least with an unhandled exception), we get poor user feedback (it just says it completes)
// Trace Info report should show the region of good data (where kernel and user traces overlap).
// Add log output to ETL file you are generating.  
// Fix versioning of the CCW stuff.  
// Put a warning when MaxEventCount caused truncation.   Also get closer to the etlx limit before bailing.  
// Make sure that for real time ETW sessions, that disposing the source while it is waiting does not cause a crash.  
// Add collection of reference set information. 
// Support XML manifest files for AppFabric
// Document the JavaScript memory stuff. 
// Make it so that RegisteredTraceEventParsers will show the 'Message' if present. 
// Process Tree view (parent child, add up)
// Put Metric in the stack view.  
// Fix is so that bad symbol paths do not cause massive slowdown.  
// Use new symbol lookup code. 
// Validate that Kernel keywords work when using the CPU counters.  
// Insure that symbol server issues don't cause grief during collection (merge (NGen Pdbs)).
// Make it so that Symbol lookup is smart about machine failures (cache failure results).  
// If you order the columns by something make search move in the way users expect.  
// Look at traces from ARM and see if we can do better with traces with identical timestamps for two different events. 
// Change PerfView to have the option of not using the normal kernel provider on Win8 (so you can profile a profiler).  

// BUGS FROM USERS
// Doug Stewart: Unhandled exception when you put illegal characters in a path (in his case quotes).  Happened to be for reading a memory dump
// Onur Feedback on 4/12/12.   (Cut and past in Russian fails).  This is a problem with '.' vs ',' 
// Make sure that the GUI is fast and small for large GC heap inspection.  
// We emit method names with {} which confuse the stack viewer code.  
// Feedback on           8/22/12 Thomas Krueger
// Make the column 'true exclusive' rather than folded 
// Way of getting the full path name of a module easily.  
// Add 'LOG button to collection dialog 
// We seem to wait after clicking stop explicitly 
// FIgure out how to get GC stacks for server case.   
// dHiniker's  suggestion of doing GCs until they are unproductive
// Make it so that perfView dialogs boxes don't go back to main monitor in a multi-monitor scenario. 
// Counts negated in Diff? 
// Take heap dumps when GCs start and end.  
// Put something in the title to show it is Drill Into.  

// Confirm that Profiler DLL loads properly on machines that don't have any VS installed (no C runtime)
// Log if the trace is complete as well where it 'starts' if it is incomplete.  
// Make sure that PerfView's / EventSource for value works properly.   ALso check for CAPTURE_STATE.  
// Change End to Stop for opcode
// Print string payloads in PerfView (EventWriteMessage)
// Change Fold column to be 'NoFold'  It is more useful.  
// Dump type information associated with allocs at end (for circular buffer case).  
// Color scheme (Dark Blue selection) 
// Insure that if CPU sample rate can be set above 100Msec or there is a good error message.    
// Add a ? to the BROKEN node that takes you to the explaination. 
// Allow Goto source to work in diffs and bring up both sources (or a diff of the sources.  
// Color scheme (more muted) 
// Display the .NET heap generation info in a reasonable way.  
// Add the ability to log stacks of allocations only certain types 
// Add the ability to view allocation stack of a particular object.
// Search all source code associated with a module.

// Currently we can use CPU counters by doing
// PerfView listCpuCounters to get a list of CPU counters and then /CpuCounters to use them.
// PerfView /CpuCounters=BranchInstuctions:10000,InstructionsRetired:10000

// HIGHER PRIORITY
// ** Find located things in the wrong order if someone has sorted by another column (in general there are issues when things are sorted by cols)
// Don't remember sort order after refreshing (symbols). 
// ** Make Filtering part of the FilterParams!
// ** BETTER SUPPORT FOR CIRCULAR BUFFERS (warn about when events start and end)
// ** MAKE WORK ON LOCKED-DOWN ARM
// ** Fix so that we recover from having an NGEN pdb without line number info.  
// Allow you to see object lifetime / promotion.  
// ** FIX NGEN CreatePDB so that it works on appcontainer scenarios. 
// Remove node based GetFirstChild in favor of indexes from graph
// Rip out ClrProfilerHeapDump support (which means CDB.exe and dbgeng.exe)
// Put PDBs of things found next to DLL / from EXE path into the ZIP file (since it might be updated constantly). 
// Find out why ZIP is not compute bound when it unzips files.   
// Make SetTimeRange work if there is something in the paste buffer that looks relevant.  
// Add ability to show generation number and secondary links.  
// Visualize thread pool heusitics.  
// Make symbol resolution robust to file permission issues. 
// Preserve sort order when update happen. 
// Prohibit merging while data collection is happening.  
// Check if something else is using the kernel logger on collection if logging fails.  
// Add file to ZIP that logs the NGEN PDB information. 

// From Dan Taylor (7/6/2012)
//      Some way of switching the “Exc Ct” column to show the delta.
//      do a “Just my code” group pat to the gc dump view.
//      The title of the Referred-From tab should be “Referred-From” ;) 
//      Have a way of attributing costs to native references.
//      Some end-to-end tool for doing the analysis.
//      Collects before/after snapshots including triggering a GC.
//      Sets the right defaults (including emptying the Fold% and FoldPats box)

// Surface a way for users to set priorities when taking heap samples.  

// BIG BUGS 
// Make Task-based attributization work 
// Insure that Heap dumps work  
// Security check on PDBs that came from the ZIP file.  

// EASY GUI IMPROVEMENTS
// Make process view order by start time (but still only processes that start and end in the trace).  Make the columns sortable (or make it a gui)
// Remember the user command history from run to run.  
// Persist the collection parameters (David Berg) 
// Warn if you are going to overwrite a file.  (David Berg)
// Ashok: use ? instead of column name for help in col
// Make it so that if you hit enter in the filter textbox on the main window that it selects the top most item. 
// From BrianGru: ability to see clock time for events.     

// GUI IMPROVEMENTS 
// Arrow keys work with space bar, 
// remember expansion/sorting when loading symbols
// Allow the user to specify the default grouping/filtering parameters and save them from run-to-run.

// REALTIVELY EASY NEW FUNCTIONALITY
// PerfView monitor capability for eventSources (real time).  
// %% Finish Total memory breakdown view.  

// FOR JUSTIN
// ** Data Mining as the overaching goal. 
//    * consuming multiple ETL files an aggregating them. 
// ** adding other graphical views.  
// ** Finishing the memory support
// ** Good fidelity memory collection
// ** Better memory graph visulization
// ** Adding / testing Task support
// ** adding more videos (on e.g. memory diffing) 
// ** Footprint Analysis - Using JIT events to show where code is touched a lot (and rarely used)
// **    Can do the same thing with CLRProfiler data (even more accurately)
// ** the rename should remember the suffix. 


// USABILITY 
// Warn people if there is an ASP.NET process but no ASP.net events. 
// Figure out and warn users about using Win8 ETLs on Win7.  
// Make Disk i/o events have always have a Disk Read nodes associated with them (even if you don't know the file name)
// David Berg 4/3/12 summary of jitting across all machines that include the process as a column. 

// OTHER 
// Allow begin-end event pairs to define regions of time that show up stack views.  
// David Bergs complaint that event columns get confused (it probably is the case).  
// David Bergs 4/2/2012 mail about Activity timelines. 

// FOLLOWUP
// Make shure we can find PDBS with full Unicode characters in them (Managed module load events lie). 
// Figure out how to cleanly get the TraceEventStackSource for symbol resolution from a generic stack source. 
// Writing a Filtered Stack source to XML does not work (not critical since we convert to a interned stack source before writing)  
// Allow overloaded methods in user-defined commands.  
// Add command to list user defined commands. 
// Make extensions work on a file share (when PerfView.exe is lauched from its cache)
// Allow extensions to be user-local 
// Move rundown waiting logic into TraceEvent.  
// For merged files, the session end time is no longer the max, compute this max while translating to fix.  
// Annotate whether the thing being pinned is in GEN2 or not in the pinning view.  
// Make dependent handles work like a GC reference traversal
// Add the incomming reference count to the items in the memory view.  
// Allow you to see source even when checksum fails.  
// Merging from the command line does not work.  
// Add thread id to the CSV JIT stats.  
// Allow just the filtered events to be viewed in the stack viewer.  

// Mohamed's worry about fold % \\fawzy-reddev2\scratch\folding_bug.perfview.xml.zip 
// Look at Jeffs's failure \\clrmain\public\writable\users\vancem\JeffSchw
// Remove Auto-open to the Cpu view 
// Disk I/O view that is based on IO size not time. 
// Warn people that the heap dump may not be complete.  
// Dustin's GC heap stats look wrong (jump in 'before' start) Dustin.etl process 3388
// Place memory dump command into the dump.  Also log how long it took etc.  
// Add docs on the non-obvious nature of the column sorting, and that treeview does not sort.  Fix 'Can sort by it' links in the docs.  
// Persist the sort after a symbol lookup is done.   
// Fix imageload view so that it takes into account double loads. 
// Investigation why BadWT shows   KERNEL32!DisablePredefinedHandleTableForIndex callling ntdll!RtlExitUserProcess
// Add the incomming reference count to the nodes in the memory view.  
// Predfined filters to remove ETW overhead from CLR traces.  
// Fix symbol lookup and source code access in diff.  
// Make timeline view all events and update incrementally. 
// Good error message if you try to take a heap dump from a processes dump that is not a full dump.  
// Have the ability to see heap imbalence for a server GC heap. 
// Have a MaxTimeMSec as well as MaxEventCount 
// Add more thread tags (they did not show up in Ben Grover's scenarios, why  not?)
// Generating of XML.ZIP files from the command line.  
// Make 'Broken' inherit from the common stack of the frames on either side of it (assuming they are not broken).  
// Make ZIPing avoid generating the ETLX file.  
// Jeff: Add the command line build steps for Tutuorial1.cs – for example /debug:pdbonly – which is needed for the source view later.
// Jeff: Add a tutorial for ‘Tutorial for GC Heap Memory Analysis’ – it would help get the concept of the heap as a graph.
// Add Heap Region to the GCDump information (so you can see how imbalenced the heap is).  
// Add hyperthreading flag to TraceInfo report 
// Make it foolproof to collect data using the command line and read it on another machine. 
// EventView can have multiple copies of an event name
// Running on Network traces does not produced valid XML (bad strings etc).  
// Do somethign with the stack from the loader heap.  
// Transfer the Process filter when jumping from the event view.  
// Fix is so that you can select a region in the event viewer to filter to.   (including process).  
// Allow users to set the default groupings (this is probably not hard).  
// Fix it so that we get the right drive letter for DLLs (for symbol resolution)
// When there are errors in the EventSource manifest, the error messages are poor
// Add Activity IDs to events view.   
// Drill into should remove the inc and exc patterns.  
// Display the source file event if it mismatches (but with a warning) 
// Add ability to collect unmanaged memory profiles
// Add ability to collect Win8 hardware counters
// Add ability to look up names in the source code
// Better memory sampling for large heaps 
// Ability to see all memory in the process (VMMap like)
// Write blog entry on EventSource.  
// Ability to see event Providers in a process


//
// *** Exception when you have sorting (by includive in the ByName view) and then set the time range (by selecting a range in the 'When' field
// 
// *** Make sure there are no look-alikes for any dlls I use in the directory where perfView runs (or relaunch)
// *** Make GUI ability to turn off .NET Tasks. 
// Warn if you don't see the rundown end. 
// Review SymbolPath logic  
// Make Run dialog box stay up during a 'run' command. 
// there is a and ugly problem with persistence of suboptimial PDB files
// Allow TraceEventSession to disable completely a provider.  
// Audit all FreeHGlobal, CloseHandle() use for proper lifetime management.  (and safeHandle useage). 
// Deal with not having PDBs during NGEN CreatePDB operations which currently cause us to lose line numbers. 
// use the fact the file has stopped growing, not the CPU to determine if the rundown is complete. 
// Make goto SOurce work for blocked time, drill into. 
// Make run dialog have a status bar.   
// Make Memory dialog work in the face of multiple ones open (may be OK).  
// Make sure that filters work when you give bad regular expressions 
// stacks that end with ntoskrnl should also probably not be considered broken (at least for startSystemThread)
// Remove '/' vs '\' distinction in Event Reader.  

// Find on the TreeView is unintuitive.  It does not start over if you click on something.  
// Fix Goto source on NGEN images.
// Warn that rundown did not complete

// MEMORY HEAP
// Change GCGraph to GCDump
// Compression of heap info.  

// Help should launch external web pages in a instance of IE, not in the window.  
// Annoying clicking when help is resized.  
// Some pause times are ignored for background GC.   Insure that we don't double count.  (Review all GC Stats for background GC consideration) 
//   Probably can be fixed by noticing overlap when computing total pause, and keeping both the total pause and max pause for every GC so you
//   can deal with several pauses for a single GC.   

// EASY: Remove % columns when doing a diff (they are confusing)
// EASY: Fix just my app for the NGEN case.  
// EASY: You need to be admin for NGEN pdb to work, Warn people about this more explicitly.  
// EASY: Put managed Version in the process view.  
// EASY: Fix run dialog to use SizeToContent window option.  
// EASY: Make the illegal combinations of memory dumping options impossible to set in the dialog. 
// Add caputure logs to file format for both memory and ETL files.  
// Warn the user that you will lose windows before elevating.  
// Forcing a GC, or waiting for a GC before taking a heap dump.  
// Improve the experience if you copy an unmerged ETL file.

// On heap display, show value types, Finalizable, if you are finalized, CCWs (maybe Object Header)  Array Sizes, Component Size, Generation
// Make default file name better for the RUN command (name of exe .etl)
// Make it so that it is hard not to ZIP when transferring files to other machines (rename .etl to .unmerged.etl)
// Make sure that the names I use for Kernel events are 'offical' (what TDH has)
// *** Put your home directory in the history of the main window's COMBO box.  
// *** Fix When display when you drill in or use XML.ZIP files.
// *** THere are times when the end time gets set to -.001 and causes a blank display
// *** Run some scenares with a DEBUG build to see if there are any asserts (turn on all events)
// Allow users to specify an editor for Goto Source. 
// EASY cache file names to make completion fast.  
// Make ESC close dialog boxes (memory) 
// Memory dialog cancel it not quite what I want (only active during working)
// The help does not remember links in the back button properly.  
// Indicate if a complete heap snapshot has been taken or not, indicate dead objects in that case.  

// From Scott Mosier
// Column neaders should use ? to allow for the sort buttons to be accessible
// Horizontal scrolling in the treeview.
// Sort by name in byname view.  

// EASY
// ASP.NET rollup stats is using the session start and end times which are not correct for cicular buffers.  
// TraceParserGen fails on Templates that exist but have no fields. 

// THere were out of memory failures in symbol resolution (seems to be when you try to look up RVAs in managed PDBS)
// Symbolic names in the Event viewer for code addresses (sample profile, page fault)
// Document X64 stack trace issue. 
// Better Ready-Thread visualization

// Maoni's asks. 
// Add the GC Condemn reason to display
// Add CSV file generation for the %time in GC
// Attribute GC time to an ASP.NET request (this is wierd)
// Hide advanced GC info
// Specify what events pretty precisely (probably already done).  

// Add a references set view. 
// Add introductory videos.  
// When you ZIP a ETL file that is already merged, notice that.
// Avoid the perf problem of generating an ETLX file to create a ZIP file.  
// Fix tests to not rely on waits (time dependent).
// Fix so that you can say you don't want auto-symbol lookup on load.  

// Crash in WPF
// Fix issue with cutting and pasting numbers into start and end regions in different cultures (which use space for sep) 
// Add CSV output for GCStats and JitStats.  
// Views that are open when a File is closed can have null ref exceptions later on.  
// Make PerfView run on ARM
// Some of the advanced settings in the 'Run' dialog are not being remembered.  

// Option to cut the current start and end values (so you can cut and paste current range)
// Arm Support. 
// BUG: Looking up symbols from a second trace sometimes thinks it does not need to look things up.
// Make it obvious that you should merge before copying off the machine.  

// Good Source code browsing
// Ability to generate unmanaged memory traces
// Ability to generate allocation profiles.  (ETW profiler provider) 
// Stitching together threads in ASP.NET scenarios
// Stitching together threads in TPL scenarios. 
// FIX it so that if you change /MaxEventCount and /SkipMSec that you don't have to manually clear the temp file
// Add a /MaxDurationMSec? 
// Add PDBThreashold to collection dialog.   Explain it in the help.  

// Fix the ASP.NET THread pool average to take the last value if there has been no adjustment.  
// add a File->Open menu item (just for discoverability)
// Put ThreadPoolCount on the ASP.NET stats.  
// Bug: Truncated process name selection (truncates to 10) in Event Viewer.  
// Add a 'View stack in allStackView' option in event viewer.  
// From Karl Burtram
// * Allow navigation to the stack for an event in the AllStacks view.  
// * Page fault view. 
// Symbol lookup before writing an XML file (since you loose the ability to do it later).  
// Save all views.  
// Allow command line spec to have more details on what to open (CPU view etc).  
// exposing Ready Thread in an interesting way.    
// Avoid re-run if contents has not changed.  
// Inc Exc patterns don't remember history if updated via context menu. 
// Good patterns for diff view. 

// Find wierdness (searching for .ni in a modules view did not find mscorlib.ni)
// Investigation codeAddresses (several code addresses for the same physical address.  Why is that?). 
// Duplicates in ModuleFiles (Tutorial)
// And Image view that shows for each process what DLLs are loaded (and their versions)
// A view of appdomains
// Warning if rundown is not complete
// A % time in GC in the GC view.  

// Keep run dialog up 
// TODO FIX NOW. tutorial example, multiple module files for EXE
// Port Win7 stack enable syntax to PerfMonitor.
// Fix Memory Heap collection from a dump on X64.  

// Remember user options. 
// Add Disk Time to ASP.NET stats.  
// Caputure the log when sending error reports.  

// Fix status bar so that it shows more relevant status messages
// Finish making local sym cache
// Calvin's data
// Allow for the collection of Unmanaged heap data.  
// Allow the creation of ad-hoc groups with GUI gestures. 
// Rename TraceEvent.dll so we can't accidentally load the wrong one.  
// Allow setting of Kernel, give a command line in GUI ...
// Make exploring providers easy (see providers available, keywords available ...)

// Place a limit on ETL size (e.g. 10Gig) so that it never gets out of hand. 

// Add the ability to unelevate when running a command
// Allow nice names when specifying providers.  

// This is good for the file share case.   
// If symbols directory exists, use it for the symbol store.  Make sure all sym files are put there if found
// Make an option to create it if it does not exist
// Make option to make zip with PDBs.  

// REmember preferences
//  * default filters
//  * symbol resolution 
//  * help display / notes pane.  

//   * Make symbol diagnostic messages REALLY good.  

// Update tutorial with a DIFF able scenario.  
// Improve messages when symbol lookup fails to the main screen. 
// FIX lame scalability in the Log viewer (write it to a text file).  

// Fix it so that the stack's thread ID is transfered to events with Thread ID of -1 (like VirtualAlloc).  
// Clean up things in %TEMP%\symbols that are old. 

// Change the run command to use the file.EXE and args as the basis for the .ETL name 
// ** AllocateTick knows what generation it goes into, Take advantage of that.

// We seem to be leaking call trees when we reload the tree (after a symbol resolution etc).   Check that. 
// Column Summation does not seem to respect the filter (adds filtered entries)

// For very large traces, allow you to filter the range BEFORE creating the tree.  
// Insure that event error CPU samples are attributed to the root node.  I don't want to lose anything.  

// Create an option for copying the PDBs necessary next to the ETL file (to make it SymServer agnostic)
//  * This option probably needs one where OS dlls don't count. 

// Create the cold pages metric tool for IBC tuning.    
// Double counting of GC pause times with background GC.  
// Multi-select on processes 
// Ability to sort Name column

// Confirm that folding works properly
// Add a * qualifer in the event viewer to say 'all other fields' 

// Make the Stack Views useful if you don't have stack traces (show exclusive)
// Freezing during conversion.  Seems to be writting too much to the logs. 
// Fix memory leak associated with FileIDToName table 
// Make stack traces better when there is no stack for CPU events
// Make a 'all stacks' view
// Make a way of opening stacks from the event viewer. 
// Cancellation of symbol lookup. 

// Display something to indicate column sorting capability
// Default preferences (grouping folding)
// Remember more gui state when creating sub-windows (sort direction, column widths)
// Determine if find has weird issues 
// Should we have a 'filter' box in the 'byName' view
// Confirm that Process VIEW's CPU count matches Stack View's CPU count.  
// Place events into the log for the logger itself that indicate how the trace was taken etc. 
// On EventView, Ctrl-N needs to clone the textbox values.  
// Make sure that if you cancel a stack view, that you don't require that you have to restart.  
// Update the title when the filter changes (and thus the total in the title is wrong).   

// Display file sizes in tree view.  

// Make sure Excel exports stuff works in european cultures (where . and , swap)
// failure on \\vancem4\public\bing\* when looking at resource fields.
// Resource provider event names not showing up. 
// Remember directories where you found ETL files 
// Should ASP.NET be informational not verbose by default? (probably)
// Callee views not sorted by absolute value.  
// CPU Samples that have no stack should at least show the sample location. 
// The allocation amount in the GC Tick is negative in some traces.  What si going on?

// Long delays when accessing files on a network share.  
// Validate the CSWITCH fiew.  Seems to add up to over 100% which is wrong.  
// Main view tree control could seek if you type a letter
// If you manipuate the ProcessFilter text box it looses some of the processes.  (I think I fixes this)

// Source code! 
// Add command line in the select process view.  
// Change default file name from PerfViewData.etl to <processName.etl>
// Chnage message when we wrap on a find to say that (not just 'not found') 
// Expose the circular buffer capability in the GUI

// Just My App is set inappropriately (on Diffs for example)
// Finish dumps from process dumps.
// Figure out the right default for silverlight (turn off just my app for internet explorer, maybe others).  
// Indicate the time range as you select more or fewer characters in the When View 
// Heap browser view. 
// Use the 'Name field in the stack viewer or rip it out.  

// Cut and paste in the log seems to cause null ref exceptions
// Not clear it is possible.  Ideally When in a 'diff' view should not count 'canceled out' samples.  
// Add command line syntax for dumping heap, and opening specific views of a file.
// Clean up PerfViewData open files problem. 

// SymbolPath should put caches first (Before other local paths). 
// Looking up symbols causes 'drill down' sample filtering to be lost (goes back to full view).  
// Document the heap features
// Document negative metrics (alloc and free)
// Work if sampling rate is not 1MSEC even if rundown events are not there. 

// Bugs found in memory stuff (sleep example)
//      V4.0 Roots issue!
//   Microsoft.Win32.Win32Native.InputRecord does not have full name!
//   Unreachable nodes in clrprofiler log 

// Exception if start time is too big in Event Viewer.
// FIX NOW: remember the caller-callee focus point when you look up symbols
// FIX NOW GC report needs rework
// Support getting info on task library dispatch (stitching together causality).
// Allow selection of 'When' field from the text box itelf. 
// Put vital stats into view notes.  
// FIX NOW, annoyance with filenames on saving 
// FIX NOW Have to hit refresh when loading saved views. 
// Allow sorting in TreeView
// FIX NOW in use issues with collection and views.  
// Make ETLX file sharable after creation (so that you can open the same ETL twice on the same machine)
// In process view, sort by start time rather than CPU. 
// Display DLL DateTimeTimeStamp as a date.  

// Have a directory of views from the main view.  
// Arrow keys open up nodes in treeview 
// Add notes to view
// A view of all the open windows.  
// Rename Find to 'Goto' in the right click menu
// SetRoot in calltree view.  (some way of trimming the top of the stack). 
// Add view state to perfView.XML files
// Source code fetch 
// Cut and paste of the caller-callee view (as a whole)

// Indicate the number of total samples in the view in the title.  
// Insure that open files are closed when file operations are performed.  
// Caller-Callee view does not agree with ByName view!
// Do ZIP by right clicking
// Finish Merge by right clicking.  Decide if we ever open a dialog box (indicate that the file is merged somehow)
// Selecting non-continguous cells is weird on cut and paste
// Indicate Size for cached files somehow
// Turn on counts for CSVZ file stacks (like DLL loading!)
// Make sure that things work when you only have CPU samples but no stacks.
// Paste of long mantissas look bad when pasted as texto
// Allow saving of session and restoring of session

// If you set kernel flags on the command line show that in the textboxes on Collect. 
// If you abort a StackView kill the window. 
// Use new command line parser.  
// We get nothing in stack view when there are no stacks.   Should get at least exclusive samples.  
// Clear cache command
// Put MaxCollectSec on run display dialog.  
// Review catch (Exception) /* probably a bad idea */
// Abort while merging
// Help links in GC columns
// Review GC stats.  (keep track of amount of condemed Size)
// Null ref exception when you collect with the logfile turned on
// Close after collection. 
// Close window after 
// File sizes when reading files (gives some idea of time)
// Summary data for machine etc
// Reenable asserts!
// Callers and Callees views.
// Adding Folded Nodes to the PerfGridView.   
// Finish XML reader / Writer
// Add CPU sampling to TraceLog
// Make programatic selection of the TreeView work properly
// Allow cancellation if we are resolving symbols (do symbol resolution on its own thread).  
// Fix Closing of files (so you can recollect).  
// Find backward
// Add check that rundown completes and warn if not.  
// Fix it so that default for .NET Rundown correct for collection command
// Enable sorting by name in the ByName View
// When you click on the sorting buttons, it should sort High to low first
// Works properly with ldr64 setwow
// Need a dialog for setting the symbol path.  
// Expand Run command dialog box when expander expands.   
// Review file locking behavior 
// Save as CSV, launch Excel with pivots.  
// Review cut and paste in general, Grid/GUI navigation (I worry that I hacked it).
// Put only extra information into ETLX.   
// VS symbol server paths.    
// Better symbol path dialog box
// EventSource Support (turning on event sources)
// Can pick up wrong PDB if not finding by symbol server.  
// Show source code.  
// Review TraceCodeAddresses data strucuture.  I believe it can be wrong
// Share more between CSV and ETL event source
// Make certain cancel works during symbol lookup.
// Had a weird issue where same PDB file was being looked up again and again. 
// Allow creation of XPERF-like marks during data collection. 
// Fail on unsafe syms?
// Allow control over threashold when heap filtering happens. 
// Consider a dialog box if events are lost
// Put the Size of the data file in the treeView.  
// Option to clean the file cache. 
// Drill Into goes back to JustMyApp 
// Should I capture Minidumps when we send feedback about exceptions?

// Keyboard shourcuts (expand nodes by clicking on space for example, in the call tree mode specially )
// A column for Call count,  this is very useful when opening a wt trace,  granted it is useless for sample profiles.
// Ability to copy calltree from Caller /callee view 

// Will pick up a TraceEvent.DLL in directory with PerfView.exe instead of one in its deployment dir
// Memory Leak!
// Canceling when converting a trace keeps the file locked.  We ARE closing the handles
// Make Drill-In work on caller-callee view
// Backport read-only mode to ETLStackBrowse
// Gui option to show full path names
// Gui for status on the whole  trace
// Insure that things work when mulitple traces are scaning the same file.  
// Better arithmetic with the clipboard? 

// Make /? go to command line help with a link to users guide 
// RegEx Compile option is an ANTI_perf feature 
// Back button.
// Form Group GUI command.  
// Should I disable filter comboBoxes when working?   (Probably)
// Make 'When' work on Caller-callee view. 
// Shortcuts don't work on selected cells. 
// Insure the deserialization errors give good diagnostics. 
// The group pattern *=>X does not seem to work properly 
// New should preserve the current window's state and place the focus where it was.  
// Implement rigth arrow key in call tree window.  
// Allow selection in the 'When' box directly. 
// Warn users when there are too few samples. 
// Make it so that PerfView can run a program many times during a run.   
//
// OTHER
// Ability to sort by name in ByName view. 
// Keeping track of checkboxes properly in CallTreeView when cells change
// Anomalies in Find in CallTreeView
// SamplesComplete main page. 
// When Kernel mapping fails symbol lookup is painful
// Check for version compatility of ETLX format being read
// Go to caller in caller-callee view when node disappears.  
// Find options (backwards, no reg ex, case sensitive).  


namespace PerfView
{
    /// <summary>
    /// The main window of the performance viewer. 
    /// </summary>
    public partial class MainWindow : Window
    {
        // TODO FIX NOW do a better job keeping track of open windows
        public int NumWindowsNeedingSaving;

        public MainWindow()
        {
            InitializeComponent();
            Directory.HistoryLength = 25;
            DataContext = this;

            // Initialize the directory history if available. 
            var directoryHistory = App.ConfigData["DirectoryHistory"];
            if (directoryHistory != null)
            {
                Directory.SetHistory(directoryHistory.Split(';'));
            }

            // And also add the docs directory   
            var docsDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrEmpty(docsDir))
            {
                Directory.AddToHistory(docsDir);
            }

            // Make sure the location is sane so it can be displayed. 
            var top = App.ConfigData.GetDouble("MainWindowTop", Top);
            Top = Math.Min(Math.Max(top, 0), System.Windows.SystemParameters.PrimaryScreenHeight - 200);

            var left = App.ConfigData.GetDouble("MainWindowLeft", Left);
            Left = Math.Min(Math.Max(left, 0), System.Windows.SystemParameters.PrimaryScreenWidth - 200);

            Height = App.ConfigData.GetDouble("MainWindowHeight", Height);
            Width = App.ConfigData.GetDouble("MainWindowWidth", Width);

            Loaded += delegate (object sender1, RoutedEventArgs e2)
            {
                FileFilterTextBox.Focus();
            };

            Closing += delegate (object sender, CancelEventArgs e)
            {
                if (NumWindowsNeedingSaving != 0)
                {
                    var result = MessageBox.Show(this, "You have unsaved notes in some Stack Views.\r\nDo you wish to exit anyway?", "Unsaved Data", MessageBoxButton.OKCancel);
                    if (result == MessageBoxResult.Cancel)
                    {
                        e.Cancel = true;
                        return;
                    }
                }
                if (StatusBar.IsWorking)
                {
                    StatusBar.AbortWork();
                }

                if (WindowState != System.Windows.WindowState.Maximized)
                {
                    App.ConfigData["MainWindowWidth"] = RenderSize.Width.ToString("f0", CultureInfo.InvariantCulture);
                    App.ConfigData["MainWindowHeight"] = RenderSize.Height.ToString("f0", CultureInfo.InvariantCulture);
                    App.ConfigData["MainWindowTop"] = Top.ToString("f0", CultureInfo.InvariantCulture);
                    App.ConfigData["MainWindowLeft"] = Left.ToString("f0", CultureInfo.InvariantCulture);
                }

                AppLog.LogUsage("Exiting");
            };

            InitializeFeedback();
        }
        public PerfViewDirectory CurrentDirectory { get { return m_CurrentDirectory; } }
        /// <summary>
        /// Set the left pane to the specified directory.  If it is a file name, then that file name is opened
        /// </summary>
        public void OpenPath(string path, bool force = false)
        {
            // If someone holds down shift, right clicks on a file and selects "Copy as path" it will
            // contain a starting and ending quote (e.g. "e:\trace.etl" as apposed to e:\trace.etl). If
            // they then paste this into the MainWindow's directory HistoryComboBox without removing 
            // the quotes and press enter, an exception will be thrown here. Remove these quotes so 
            // it doesn't need to be manually done.
            if (path.StartsWith("\"") && path.EndsWith("\"") && path.Length >= 2)
            {
                path = path.Substring(1, path.Length - 2);
            }

            if (System.IO.Directory.Exists(path))
            {
                var fullPath = App.MakeUniversalIfPossible(Path.GetFullPath(path));
                if (force || m_CurrentDirectory == null || fullPath != m_CurrentDirectory.FilePath)
                {
                    Directory.Text = fullPath;
                    if (Directory.AddToHistory(fullPath))
                    {
                        StringBuilder sb = new StringBuilder();
                        foreach (string item in Directory.Items)
                        {
                            if (sb.Length != 0)
                            {
                                sb.Append(';');
                            }

                            sb.Append(item);
                        }
                        App.ConfigData["DirectoryHistory"] = sb.ToString();
                    }

                    FileFilterTextBox.Text = "";
                    m_CurrentDirectory = new PerfViewDirectory(fullPath);
                    UpdateFileFilter();

                    string appName = Environment.Is64BitProcess ? "PerfView64" : "PerfView";
                    string elevatedSuffix = (TraceEventSession.IsElevated() ?? false) ? " (Administrator)" : "";
                    Title = appName + " " + CurrentDirectory.FilePath + elevatedSuffix;
                }
            }
            else if (System.IO.File.Exists(path))
            {
                Open(path);
            }
            else
            {
                Directory.RemoveFromHistory(Directory.Text);
                if (m_CurrentDirectory != null)
                {
                    Directory.Text = m_CurrentDirectory.FilePath;
                }

                StatusBar.LogError("Directory " + path + " does not exist");
            }
        }
        /// <summary>
        /// Given a file name and format open the file (if format is null we try to infer the format from the file extension)
        /// </summary>
        public void Open(string dataFileName, PerfViewFile format = null, Action doAfter = null)
        {
            // Allow people open a directory name.  
            if (System.IO.Directory.Exists(dataFileName))
            {
                StatusBar.Log("Opened directory " + dataFileName);
                OpenPath(dataFileName);
                return;
            }
            var dir = Path.GetDirectoryName(dataFileName);
            if (string.IsNullOrEmpty(dir))
            {
                dir = ".";
            }

            OpenPath(dir);
            PerfViewFile.Get(dataFileName).Open(this, StatusBar, doAfter);
        }
        /// <summary>
        /// Opens a stack source of a given name (null is the default) for a given file.
        /// </summary>
        public void OpenStacks(string dataFileName, PerfViewFile format = null, string stackSourceName = null)
        {
            Open(dataFileName, null, delegate ()
            {
                var data = PerfViewFile.Get(dataFileName);
                if (data.Children != null)
                {
                    if (stackSourceName == null)
                    {
                        stackSourceName = data.DefaultStackSourceName;
                    }

                    var source = data.GetStackSource(stackSourceName);
                    if (source != null)
                    {
                        source.Open(this, StatusBar);
                    }
                }
            });
        }

        /// <summary>
        /// Open the Memory Dump dialog.  If processDumpFile == null then it will prompt for a live process.
        /// It will prime the process dump file from the given string.  
        /// </summary>
        public void TakeHeapShapshot(Action continuation)
        {
            // Set the default value for continuation
            if (continuation == null)
            {
                continuation = TryOpenDataFile;
            }

            // You need to be admin if you are not taking the snapshot from a dump.  
            if (App.CommandLineArgs.ProcessDumpFile == null)
            {
                App.CommandProcessor.LaunchPerfViewElevatedIfNeeded("GuiHeapSnapshot", App.CommandLineArgs);
            }

            ChangeCurrentDirectoryIfNeeded();
            var memoryDialog = new Dialogs.MemoryDataDialog(App.CommandLineArgs, this, continuation);
            memoryDialog.Show();        // Can't be a true dialog because you can't bring up the log otherwise.  
            // TODO FIX NOW.   no longer a dialog, insure that it is unique?
        }

        /// <summary>
        /// Hides the window (if it can still be reached).  
        /// </summary>
        public void HideWindow()
        {
            // TODO need count of all active children
            if (StackWindow.StackWindows.Count > 0)
            {
                Visibility = System.Windows.Visibility.Hidden;
            }
        }

        public RunCommandDialog CollectWindow { get; set; }
        /// <summary>
        /// This is a helper that performs a command line style action, logging output to the log.  
        /// statusMessage is what is displayed while the command is executing.  
        /// 
        /// If continuation is non-null it is executed after the command completes successfully 
        /// If _finally is non-null, it is executed after the command (and continuation), even if the command is unsuccessful.  
        /// </summary>
        public void ExecuteCommand(string statusMessage, Action<CommandLineArgs> command, StatusBar worker = null, Action continuation = null, Action finally_ = null)
        {
            App.CommandProcessor.ShowLog = false;

            if (worker == null)
            {
                worker = StatusBar;
            }

            App.CommandProcessor.LogFile = worker.LogWriter;
            worker.StartWork(statusMessage, delegate ()
            {
                command(App.CommandLineArgs);
                worker.EndWork(delegate ()
                {
                    // Refresh directory view 
                    RefreshCurrentDirectory();

                    // TODO FIX NOW use continuation instead
                    if (App.CommandProcessor.ShowLog)
                    {
                        worker.OpenLog();
                    }

                    var openNext = GuiApp.MainWindow.m_openNextFileName;
                    GuiApp.MainWindow.m_openNextFileName = null;
                    if (openNext != null)
                    {
                        Open(openNext);
                    }

                    continuation?.Invoke();
                });
            }, finally_);
        }

        /// GUI command callbacks
        // The file menu callbacks
        internal void DoSetSymbolPath(object sender, RoutedEventArgs e)
        {
            var symPathDialog = new SymbolPathDialog(this, App.SymbolPath, "Symbol", delegate (string newPath)
            {
                App.SymbolPath = newPath;
            });
            symPathDialog.Show();
        }

        internal void DoRun(object sender, RoutedEventArgs e)
        {
            ChangeCurrentDirectoryIfNeeded();
            CollectWindow = new RunCommandDialog(App.CommandLineArgs, this, false, TryOpenDataFile);
            CollectWindow.Show();
        }
        internal void DoCollect(object sender, RoutedEventArgs e)
        {
            ChangeCurrentDirectoryIfNeeded();
            CollectWindow = new RunCommandDialog(App.CommandLineArgs, this, true, TryOpenDataFile);
            CollectWindow.Show();
        }

        internal void TryOpenDataFile()
        {
            if (App.CommandLineArgs.DataFile != null)
            {
                var file = PerfViewFile.TryGet(App.CommandLineArgs.DataFile);
                if (file != null)
                {
                    if (file.IsOpened)
                    {
                        file.Close();
                    }

                    OpenPath(App.CommandLineArgs.DataFile);
                }
            }
        }

        /// <summary>
        /// Refreshes the current shown directory
        /// </summary>
        private void RefreshCurrentDirectory()
        {
            OpenPath(Directory.Text, force: true);
        }

        private void DoAbort(object sender, RoutedEventArgs e)
        {
            ExecuteCommand("Aborting any active Data collection", App.CommandProcessor.Abort);
        }
        private void DoMerge(object sender, RoutedEventArgs e)
        {
            // TODO FIX NOW, decide how I want this done.   Do I select or do I use GetDataFielName
            var selectedFile = TreeView.SelectedItem as PerfViewFile;
            if (selectedFile == null)
            {
                throw new ApplicationException("No file selected.");
            }

            // TODO this has a side effect... 
            App.CommandLineArgs.DataFile = selectedFile.FilePath;
            App.CommandLineArgs.Zip = false;
            if (!App.CommandLineArgs.DataFile.EndsWith(".etl"))
            {
                throw new ApplicationException("File " + App.CommandLineArgs.DataFile + " not a .ETL file");
            }

            ExecuteCommand("Merging " + Path.GetFullPath(App.CommandLineArgs.DataFile), App.CommandProcessor.Merge);
        }
        private void DoZip(object sender, RoutedEventArgs e)
        {
            var selectedFile = TreeView.SelectedItem as PerfViewFile;
            if (selectedFile == null)
            {
                throw new ApplicationException("No file selected.");
            }

            // TODO this has a side effect... 
            App.CommandLineArgs.DataFile = selectedFile.FilePath;
            App.CommandLineArgs.Zip = true;
            if (!App.CommandLineArgs.DataFile.EndsWith(".etl"))
            {
                throw new ApplicationException("File " + App.CommandLineArgs.DataFile + " not a .ETL file");
            }
            // TODO we may be doing an unnecessary merge.  
            ExecuteCommand("Merging and Zipping " + Path.GetFullPath(App.CommandLineArgs.DataFile), App.CommandProcessor.Merge);
        }

        private void DoMergeAndZipAll(object sender, RoutedEventArgs e)
        {
            List<Action> actions = new List<Action>();
            foreach (var file in TreeView.Items.OfType<PerfViewFile>().Reverse())
            {
                var filePath = file.FilePath;
                if (!filePath.EndsWith(".etl"))
                {
                    continue;
                }

                var continuation = actions.LastOrDefault();
                actions.Add(() =>
                {
                    // TODO this has a side effect... 
                    App.CommandLineArgs.DataFile = filePath;
                    App.CommandLineArgs.Zip = true;

                    ExecuteCommand("Merging and Zipping " + Path.GetFullPath(App.CommandLineArgs.DataFile), App.CommandProcessor.Merge, continuation: continuation);
                });
            }

            actions.LastOrDefault()?.Invoke();
        }

        private void DoUnZip(object sender, RoutedEventArgs e)
        {
            var selectedFile = TreeView.SelectedItem as PerfViewFile;
            if (selectedFile == null)
            {
                throw new ApplicationException("No file selected.");
            }

            // TODO this has a side effect... 
            var inputName = selectedFile.FilePath;
            if (!inputName.EndsWith(".etl.zip"))
            {
                throw new ApplicationException("File " + inputName + " not a zipped .ETL file");
            }

            // TODO make a command
            StatusBar.StartWork("Unzipping " + inputName, delegate ()
            {
                CommandProcessor.UnZipIfNecessary(ref inputName, StatusBar.LogWriter, false);
                StatusBar.EndWork(delegate ()
                {
                    // Refresh the directory view
                    RefreshCurrentDirectory();
                });
            });
        }

        private void DoHide(object sender, RoutedEventArgs e)
        {
            HideWindow();
        }
        private void DoUserCommand(object sender, RoutedEventArgs e)
        {
            if (m_UserDefineCommandDialog == null)
            {
                m_UserDefineCommandDialog = new UserCommandDialog(this, delegate (string commandAndArgs)
                {
                    App.CommandLineArgs.CommandAndArgs = ParseWordsOrQuotedStrings(commandAndArgs).ToArray();
                    bool commandSuccessful = false;

                    ExecuteCommand("User Command " + string.Join(" ", commandAndArgs), App.CommandProcessor.UserCommand, null,
                        delegate { commandSuccessful = true; },
                        delegate { if (!commandSuccessful) { m_UserDefineCommandDialog.RemoveHistory(commandAndArgs); } });
                });
            }
            m_UserDefineCommandDialog.Show();
            m_UserDefineCommandDialog.Focus();
        }

        private void DoExit(object sender, RoutedEventArgs e)
        {
            Close();
        }
        private void DoClearTempFiles(object sender, RoutedEventArgs e)
        {
            StatusBar.Log("Cleaning up " + CacheFiles.CacheDir + ".");
            DirectoryUtilities.Clean(CacheFiles.CacheDir);
            System.IO.Directory.CreateDirectory(CacheFiles.CacheDir);
            foreach (var file in System.IO.Directory.EnumerateFiles(CacheFiles.CacheDir))
            {
                StatusBar.Log("Could not delete " + file);
            }
        }
        private void DoClearUserConfig(object sender, RoutedEventArgs e)
        {
            StatusBar.Log("Deleting user config file " + App.ConfigDataFileName + ".");
            FileUtilities.ForceDelete(App.ConfigDataFileName);
            App.ConfigData.Clear();
        }

        private void DoCancel(object sender, ExecutedRoutedEventArgs e)
        {
            StatusBar.AbortWork();
        }
        private void InitializeFeedback()
        {
            System.Threading.Thread.Sleep(100);     // Wait for startup to end, this is lower priority work.    

            // FeedbackButton.Visibility = System.Windows.Visibility.Collapsed;
            WikiButton.Visibility = System.Windows.Visibility.Collapsed;

            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                if (AppLog.s_IsUnderTest)
                {
                    return;
                }

                bool canSendFeedback = AppLog.CanSendFeedback;
                AppLog.LogUsage("Start", DateTime.Now.ToString(), AppLog.BuildDate);

                // If we have the PdbScope.exe file then we can enable the ImageFile capability
                string pdbScopeFile = Path.Combine(PerfViewExtensibility.Extensions.ExtensionsDirectory, "PdbScope.exe");
                bool pdbScopeExists = File.Exists(pdbScopeFile);

                string ilSizeFile = Path.Combine(PerfViewExtensibility.Extensions.ExtensionsDirectory, "ILSize.dll");
                bool ilSizeExists = File.Exists(ilSizeFile);

                Dispatcher.BeginInvoke((Action)delegate ()
                {
                    if (canSendFeedback)
                    {
                        // FeedbackButton.Visibility = System.Windows.Visibility.Visible;

                        // TODO Currently even the internal Wiki is now a broken link, so simply give up for now.
                        // When we go open source we can use GitHub for this.   
                        // WikiButton.Visibility = System.Windows.Visibility.Visible;
                    }
                    if (pdbScopeExists)
                    {
                        ImageSizeMenuItem.Visibility = System.Windows.Visibility.Visible;
                    }
                    else
                    {
                        StatusBar.Log("Warning: PdbScope not found at " + pdbScopeFile);
                        StatusBar.Log("Disabling the Image Size Menu Item.");
                    }
                    if (ilSizeExists)
                    {
                        ILSizeMenuItem.Visibility = System.Windows.Visibility.Visible;
                    }
                    else
                    {
                        StatusBar.Log("Warning: ILSize not found at " + ilSizeFile);
                        StatusBar.Log("Disabling the IL Size Menu Item.");
                    }
                });
            });
        }
        private void DoWikiClick(object sender, RoutedEventArgs e)
        {
            var wikiUrl = "http://devdiv/sites/wikis/perf/Wiki%20Pages/PerfView%20Wiki.aspx";
            StatusBar.Log("Opening " + wikiUrl);
            Command.Run(wikiUrl, new CommandOptions().AddStart().AddTimeout(CommandOptions.Infinite));
        }
        private void DoVideoClick(object sender, RoutedEventArgs e)
        {
            var videoUrl = Path.Combine(Path.GetDirectoryName(SupportFiles.MainAssemblyPath), @"PerfViewVideos\PerfViewVideos.htm");
            if (!File.Exists(videoUrl))
            {
                if (!AllowNativateToWeb)
                {
                    StatusBar.LogError("Navigating to web disallowed, canceling.");
                    return;
                }
                videoUrl = Path.Combine(SupportFiles.SupportFileDir, "perfViewWebVideos.htm");
            }
            StatusBar.Log("Opening " + videoUrl);
            Command.Run(Command.Quote(videoUrl), new CommandOptions().AddStart().AddTimeout(CommandOptions.Infinite));
        }
        private void DoFeedbackClick(object sender, RoutedEventArgs e)
        {
            if (AppLog.CanSendFeedback)
            {
                var feedbackDialog = new PerfView.Dialogs.FeedbackDialog(this, delegate (string message)
                {
                    if (string.IsNullOrWhiteSpace(message))
                    {
                        StatusBar.Log("No feedback provided, cancelling.");
                        return;
                    }
                    if (AppLog.SendFeedback(message, false))
                    {
                        StatusBar.Log("Successfully appended to " + AppLog.FeedbackFilePath);
                        StatusBar.Log("Feedback sent.");
                    }
                    else
                    {
                        StatusBar.LogError("Sorry, there was a problem sending feedback.");
                    }
                });
                feedbackDialog.Show();
            }
            else
            {
                DisplayUsersGuide("Feedback");
            }
        }
        private void DoTakeHeapSnapshot(object sender, RoutedEventArgs e)
        {
            App.CommandLineArgs.ProcessDumpFile = null;
            TakeHeapShapshot(null);
        }
        private void DoDirectorySize(object sender, RoutedEventArgs e)
        {
            App.CommandLineArgs.CommandAndArgs = new string[] { "DirectorySize" };
            App.CommandLineArgs.DoCommand = App.CommandProcessor.UserCommand;
            ExecuteCommand("Computing directory size", App.CommandLineArgs.DoCommand);
        }
        private void DoImageSize(object sender, RoutedEventArgs e)
        {
            App.CommandLineArgs.CommandAndArgs = new string[] { "ImageSize" };
            App.CommandLineArgs.DoCommand = App.CommandProcessor.UserCommand;
            ExecuteCommand("Computing image size", App.CommandLineArgs.DoCommand);
        }
        private void DoILSize(object sender, RoutedEventArgs e)
        {
            App.CommandLineArgs.CommandAndArgs = new string[] { "ILSize.ILSize" };
            App.CommandLineArgs.DoCommand = App.CommandProcessor.UserCommand;
            ExecuteCommand("Computing image size", App.CommandLineArgs.DoCommand);
        }

        private void DoTakeHeapShapshotFromProcessDump(object sender, RoutedEventArgs e)
        {
            App.CommandLineArgs.ProcessDumpFile = "";
            TakeHeapShapshot(null);
        }

        // The Help menu callbacks
        internal void DoCommandLineHelp(object sender, RoutedEventArgs e)
        {
            var editor = new TextEditorWindow(this);
            editor.Width = 1000;
            editor.Height = 600;
            editor.Title = "PerfView Command Line Help";
            editor.TextEditor.AppendText(CommandLineArgs.GetHelpString(120));
            editor.TextEditor.IsReadOnly = true;
            editor.Show();
        }
        internal void DoUserCommandHelp(object sender, RoutedEventArgs e)
        {
            var sw = new StringWriter();
            sw.WriteLine("All User Commands");
            Extensions.GenerateHelp(sw);

            var editor = new TextEditorWindow(this);
            editor.Width = 850;
            editor.Height = 600;
            editor.Title = "PerfView Command Line Help";
            editor.TextEditor.AppendText(sw.ToString());
            editor.TextEditor.IsReadOnly = true;
            editor.Show();
        }

        private void DoAbout(object sender, RoutedEventArgs e)
        {
            string versionString = "PerfView Version " + AppLog.VersionNumber + " \r\nBuildDate: " + AppLog.BuildDate;
            MessageBox.Show(versionString, versionString);
        }

        // Gui actions in the TreeView pane
        private void DoMouseDoubleClickInTreeView(object sender, MouseButtonEventArgs e)
        {
            DoOpen(sender, null);
        }
        private void KeyDownInTreeView(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                DoOpen(sender, null);
            }
        }
        private void SelectedItemChangedInTreeView(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var asFile = TreeView.SelectedItem as PerfViewFile;
            if (asFile != null)
            {
                StatusBar.Status = "File : " + Path.GetFullPath(asFile.FilePath);
            }
        }
        private void DoTextEnteredInDirectoryTextBox(object sender, RoutedEventArgs e)
        {
            OpenPath(Directory.Text);
        }
        private void DoDrop(object sender, DragEventArgs e)
        {
            var fileNames = e.Data.GetData(System.Windows.DataFormats.FileDrop) as string[];
            // Don't allow multiple drops as it is expensive.  
            if (fileNames != null && fileNames.Length > 0)
            {
                Open(fileNames[0]);
            }
        }

        // Context menu in the Treeview pane
        private void CanDoItemHelp(object sender, CanExecuteRoutedEventArgs e)
        {
            var selectedItem = TreeView.SelectedItem as PerfViewTreeItem;
            if (selectedItem != null && selectedItem.HelpAnchor != null)
            {
                e.CanExecute = true;
            }
        }

        private void DoItemHelp(object sender, ExecutedRoutedEventArgs e)
        {
            var selectedItem = TreeView.SelectedItem as PerfViewTreeItem;
            if (selectedItem == null)
            {
                throw new ApplicationException("No item selected.");
            }

            var anchor = selectedItem.HelpAnchor;
            if (anchor == null)
            {
                throw new ApplicationException("Item does not have help.");
            }

            StatusBar.Log("Looking up topic " + anchor + " in Users Guide.");
            DisplayUsersGuide(anchor);
        }

        private void OpenInBrowser(object sender, ExecutedRoutedEventArgs e)
        {
            var selectedReport = TreeView.SelectedItem as PerfViewHtmlReport;
            if (selectedReport == null)
            {
                throw new ApplicationException("No report selected.");
            }

            selectedReport.OpenInExternalBrowser(StatusBar);
        }

        private void CanOpenInBrowser(object sender, CanExecuteRoutedEventArgs e)
        {
            if (TreeView.SelectedItem is PerfViewHtmlReport)
            {
                e.CanExecute = true;
            }
        }

        private void DoRefreshDir(object sender, ExecutedRoutedEventArgs e)
        {
            RefreshCurrentDirectory();
        }
        private void DoOpen(object sender, ExecutedRoutedEventArgs e)
        {
            var selectedItem = TreeView.SelectedItem as PerfViewTreeItem;
            if (selectedItem == null)
            {
                throw new ApplicationException("No item selected.");
            }

            selectedItem.Open(this, StatusBar, delegate ()
            {
#if false // TODO FIX NOW this causes undesirable side effects of closing any opened tree nodes.    Remove permanently.
                // The item was expanded after it was opened, refresh the current directory
                if (selectedItem.IsExpanded)
                {
                    // refresh the directory. 
                    RefreshCurrentDirectory();
                }
#endif
            });
        }
        private void DoClose(object sender, ExecutedRoutedEventArgs e)
        {
            var selectedFile = TreeView.SelectedItem as PerfViewFile;
            if (selectedFile == null)
            {
                throw new ApplicationException("No file selected.");
            }

            // TODO FIX NOW Actually keep track of open windows, also does not track open event windows.  
            if (StackWindow.StackWindows.Count != 0)
            {
                throw new ApplicationException("Currently can only close files if all stack windows are closed.");
            }

            if (selectedFile.IsOpened)
            {
                selectedFile.Close();
            }
        }
        private void DoDelete(object sender, ExecutedRoutedEventArgs e)
        {
            var selectedFile = TreeView.SelectedItem as PerfViewFile;
            if (selectedFile == null)
            {
                throw new ApplicationException("No file selected.");
            }

            var response = MessageBox.Show(this,
                "Delete " + Path.GetFileName(selectedFile.FilePath) + "?", "Delete Confirmation", MessageBoxButton.OKCancel);

            // TODO does not work with the unmerged files
            if (response == MessageBoxResult.OK)
            {
                string selectedFilePath = selectedFile.FilePath;
                // Delete the file.  
                FileUtilities.ForceDelete(selectedFilePath);

                // If it is an ETL file, remove all the other components of an unmerged ETL file.  
                if (selectedFilePath.EndsWith(".etl", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (string relatedFile in System.IO.Directory.GetFiles(Path.GetDirectoryName(selectedFile.FilePath), Path.GetFileNameWithoutExtension(selectedFilePath) + ".*"))
                    {
                        Match m = Regex.Match(relatedFile, @"\.((clr.*)|(user.*)|(kernel.*)\.etl)$", RegexOptions.IgnoreCase);
                        if (m.Success)
                        {
                            FileUtilities.ForceDelete(relatedFile);
                        }
                    }
                }
            }

            // refresh the directory. 
            RefreshCurrentDirectory();
        }
        private void DoRename(object sender, ExecutedRoutedEventArgs e)
        {
            var selectedFile = TreeView.SelectedItem as PerfViewFile;
            if (selectedFile == null)
            {
                throw new ApplicationException("No file selected.");
            }

            string selectedFilePath = selectedFile.FilePath;

            var targetPath = GetDataFileName("Rename File", false, "", null);
            if (targetPath == null)
            {
                StatusBar.Log("Operation Canceled");
                return;
            }

            // Add a ETL suffix if the source has one.  
            bool selectedFileIsEtl = selectedFilePath.EndsWith(".etl", StringComparison.OrdinalIgnoreCase);
            if (selectedFileIsEtl && !Path.HasExtension(targetPath))
            {
                targetPath = Path.ChangeExtension(targetPath, ".etl");
            }

            // Do the move.  
            FileUtilities.ForceMove(selectedFilePath, targetPath);

            // rename all the other variations of the unmerged file
            if (selectedFileIsEtl && targetPath.EndsWith(".etl", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string relatedFile in System.IO.Directory.GetFiles(Path.GetDirectoryName(selectedFilePath), Path.GetFileNameWithoutExtension(selectedFilePath) + ".*"))
                {
                    Match m = Regex.Match(relatedFile, @"\.((clr.*)|(user.*)|(kernel.*)\.etl)$", RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        FileUtilities.ForceMove(relatedFile, Path.ChangeExtension(targetPath, m.Groups[1].Value));
                    }
                }
            }

            // refresh the directory. 
            RefreshCurrentDirectory();
        }
        private void DoMakeLocalSymbolDir(object sender, ExecutedRoutedEventArgs e)
        {
            var selectedFile = TreeView.SelectedItem as PerfViewFile;
            if (selectedFile == null)
            {
                throw new ApplicationException("No file selected.");
            }

            var dir = Path.GetDirectoryName(selectedFile.FilePath);
            if (dir.Length == 0)
            {
                dir = ".";
            }

            var symbolDir = Path.Combine(dir, "symbols");
            if (System.IO.Directory.Exists(symbolDir))
            {
                StatusBar.Log("Local symbol directory " + symbolDir + " already exists.");
            }
            else
            {
                System.IO.Directory.CreateDirectory(symbolDir);
                StatusBar.Log("Created local symbol directory " + symbolDir + ".");
            }
        }

        // Misc Gui actions 
        private void DoHyperlinkHelp(object sender, ExecutedRoutedEventArgs e)
        {
            var param = e.Parameter as string;
            if (param == null)
            {
                param = "MainViewerQuickStart";       // This is the F1 help
            }

            StatusBar.Log("Looking up topic " + param + " in Users Guide.");
            DisplayUsersGuide(param);
        }
        private void DoReleaseNotes(object sender, RoutedEventArgs e)
        {
            StatusBar.Log("Displaying the release notes.");
            DisplayUsersGuide("ReleaseNotes");
        }
        private void DoReferenceGuide(object sender, RoutedEventArgs e)
        {
            StatusBar.Log("Displaying the reference guide.");
            DisplayUsersGuide("ReferenceGuide");
        }
        private void DoFocusDirectory(object sender, RoutedEventArgs e)
        {
            Directory.Focus();
        }

        private void UpdateFileFilter()
        {
            var filterText = FileFilterTextBox.Text;
            Regex filterRegex = null;
            if (filterText.Length != 0)
            {
                var morphed = Regex.Escape(filterText);
                morphed = "^" + morphed.Replace(@"\*", ".*");
                filterRegex = new Regex(morphed, RegexOptions.IgnoreCase);
            }
            m_CurrentDirectory.Filter = filterRegex;

            var children = m_CurrentDirectory.Children;
            TreeView.ItemsSource = children;
            if (children.Count > 0)
            {
                children[0].IsSelected = true;
            }

            if (children.Count <= 1 && m_CurrentDirectory.Filter != null)
            {
                StatusBar.LogError("WARNING: filter " + FileFilterTextBox.Text + " has excluded all items.");
            }
        }

        private void FilterTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateFileFilter();
        }

        private void FilterKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (m_CurrentDirectory.Children.Count > 0)
                {
                    var selected = TreeView.SelectedItem as PerfViewTreeItem;
                    if (selected == null)
                    {
                        selected = m_CurrentDirectory.Children[0];
                    }

                    selected.Open(this, StatusBar);
                    TreeView.Focus();
                }
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (AppLog.s_IsUnderTest)
            {
                return;
            }

            if (App.CommandProcessor.CollectingData)
            {
                DoAbort(null, null);
            }

            Environment.Exit(0);        // TODO can we do this another way?
        }

        // GUI Command objects.  
        public static RoutedUICommand CollectCommand = new RoutedUICommand("Collect", "Collect", typeof(MainWindow),
            new InputGestureCollection() { new KeyGesture(Key.C, ModifierKeys.Alt) });
        public static RoutedUICommand RunCommand = new RoutedUICommand("Run", "Run", typeof(MainWindow),
            new InputGestureCollection() { new KeyGesture(Key.R, ModifierKeys.Alt) });
        public static RoutedUICommand AbortCommand = new RoutedUICommand("Abort", "Abort", typeof(MainWindow),
            new InputGestureCollection() { new KeyGesture(Key.A, ModifierKeys.Alt) });
        public static RoutedUICommand MergeCommand = new RoutedUICommand("Merge", "Merge", typeof(MainWindow));
        public static RoutedUICommand ZipCommand = new RoutedUICommand("Zip", "Zip", typeof(MainWindow));
        public static RoutedUICommand UnZipCommand = new RoutedUICommand("UnZip", "UnZip", typeof(MainWindow));
        public static RoutedUICommand ItemHelpCommand = new RoutedUICommand("Help on Item", "ItemHelp", typeof(MainWindow));
        public static RoutedUICommand OpenInBrowserCommand = new RoutedUICommand("Open in Browser", "OpenInBrowser", typeof(MainWindow));
        public static RoutedUICommand HideCommand = new RoutedUICommand("Hide", "Hide", typeof(MainWindow),
            new InputGestureCollection() { new KeyGesture(Key.H, ModifierKeys.Alt) });
        public static RoutedUICommand UserCommand = new RoutedUICommand("User Command", "UserCommand", typeof(MainWindow),
    new InputGestureCollection() { new KeyGesture(Key.U, ModifierKeys.Alt) });
        public static RoutedUICommand RefreshDirCommand = new RoutedUICommand("Refresh Dir", "RefreshDir",
            typeof(MainWindow), new InputGestureCollection() { new KeyGesture(Key.F5) });
        public static RoutedUICommand OpenCommand = new RoutedUICommand("Open", "Open", typeof(MainWindow));
        public static RoutedUICommand DeleteCommand = new RoutedUICommand("Delete", "Delete", typeof(MainWindow),
            new InputGestureCollection() { new KeyGesture(Key.Delete) });   // TODO is this shortcut a good idea?
        public static RoutedUICommand RenameCommand = new RoutedUICommand("Rename", "Rename", typeof(MainWindow));
        public static RoutedUICommand MakeLocalSymbolDirCommand = new RoutedUICommand(
            "Make Local Symbol Dir", "MakeLocalSymbolDir", typeof(MainWindow));
        public static RoutedUICommand CloseCommand = new RoutedUICommand("Close", "Close", typeof(MainWindow));
        public static RoutedUICommand CancelCommand = new RoutedUICommand("Cancel", "Cancel", typeof(EventWindow),
            new InputGestureCollection() { new KeyGesture(Key.Escape) });
        public static RoutedUICommand UsersGuideCommand = new RoutedUICommand("UsersGuide", "UsersGuide", typeof(MainWindow));
        public static RoutedUICommand HeapSnapshotCommand = new RoutedUICommand("Take Heap Snapshot", "HeapSnapshot", typeof(MainWindow),
            new InputGestureCollection() { new KeyGesture(Key.S, ModifierKeys.Alt) });
        public static RoutedUICommand DirectorySizeCommand = new RoutedUICommand("Directory Size", "DirectorySize", typeof(MainWindow),
            new InputGestureCollection() { new KeyGesture(Key.D, ModifierKeys.Alt) });
        public static RoutedUICommand ImageSizeCommand = new RoutedUICommand("Image Size", "ImageSize", typeof(MainWindow),
            new InputGestureCollection() { new KeyGesture(Key.I, ModifierKeys.Alt) });
        public static RoutedUICommand ILSizeCommand = new RoutedUICommand("IL Size", "ILSize", typeof(MainWindow),
            new InputGestureCollection() { new KeyGesture(Key.L, ModifierKeys.Alt) });

        public static RoutedUICommand HeapSnapshotFromDumpCommand = new RoutedUICommand("Take Heap Snapshot from Process Dump", "HeapSnapshotFromDump",
            typeof(MainWindow));
        public static RoutedUICommand FocusDirectoryCommand = new RoutedUICommand("Focus Directory", "FocusDirectory", typeof(MainWindow),
            new InputGestureCollection() { new KeyGesture(Key.L, ModifierKeys.Control) });
        #region private
        internal static List<string> ParseWordsOrQuotedStrings(string commandAndArgs)
        {
            var words = new List<string>();

            // Match the next work or quoted string 
            // TODO quoting quotes 
            var regex = new Regex("\\s*(([^\"]\\S*)|(\"[^\"]*\"))");
            int cur = 0;
            while (cur < commandAndArgs.Length)
            {
                var m = regex.Match(commandAndArgs, cur);
                if (!m.Success)
                {
                    break;
                }

                var start = m.Groups[1].Index;
                var len = m.Groups[1].Length;
                cur = start + len + 1;

                // Remove the quotes if necessary. 
                if (commandAndArgs[start + len - 1] == '"')
                {
                    --len;
                }

                if (commandAndArgs[start] == '"')
                {
                    start++;
                    --len;
                }
                words.Add(commandAndArgs.Substring(start, len));
            }
            return words;
        }

        /// <summary>
        /// If we can't write to the directory as a normal user, change the directory to your home directory.  
        /// This is useful if PerfVIew is launch from embeded E-mail to avoid writing in \Program Files
        /// </summary>
        private void ChangeCurrentDirectoryIfNeeded()
        {
            // See if the current directory is writable

            bool changDir = false;
            var curDir = Environment.CurrentDirectory;
            if (string.Compare(curDir, 1, @":\windows\System32", 0, 18, StringComparison.OrdinalIgnoreCase) == 0)
            {
                // ETW will refuse to write files int system32 and if people put PerfView there it will end up trying to do so. 
                changDir = true;
            }
            else
            {
                try
                {
                    var testFile = Path.Combine(curDir, "PerfViewData.testfile");
                    File.Open(testFile, FileMode.Create, FileAccess.Write).Close();
                    File.Delete(testFile);
                    return;
                }
                catch (UnauthorizedAccessException)
                {
                    changDir = true;
                }
                catch (Exception) { }
            }
            if (changDir)
            {
                // No then change directory to my documents directory 
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                StatusBar.Log("Current Directory " + Environment.CurrentDirectory + " is not writable, changing to " + docs);
                Environment.CurrentDirectory = docs;
            }
        }
        private PerfViewFile GetSelectedPerfViewData()
        {
            // TODO get item under cursor, not selected item
            var selectedItem = TreeView.SelectedItem as PerfViewFile;
            if (selectedItem == null)
            {
                throw new ApplicationException("No data file Selected.");
            }

            return selectedItem;
        }
        internal string GetDataFileName(string title, bool shouldExist, string fileName, string filter)
        {
            // TODO should use SaveFileDialog sometimes.  
            StatusBar.Status = "";
            var openDialog = new Microsoft.Win32.OpenFileDialog();
            openDialog.FileName = fileName;
            openDialog.InitialDirectory = CurrentDirectory.FilePath;
            openDialog.Title = title;
            openDialog.DefaultExt = Path.GetExtension(fileName);
            openDialog.Filter = filter;     // Filter files by extension
            openDialog.AddExtension = true;
            openDialog.ReadOnlyChecked = shouldExist;
            openDialog.CheckFileExists = shouldExist;

            // Show open file dialog box
            Nullable<bool> result = openDialog.ShowDialog();
            if (result == true)
            {
                return (openDialog.FileName);
            }
            else
            {
                StatusBar.LogError("Operation canceled.");
            }

            return null;
        }
        internal static bool DisplayUsersGuide(string anchor = null)
        {
            // This is hack because of issues when we pass non-ascii characters to the WPF browser control.  Spawn a true browser
            // and use its current directory to avoid needing to pass the non-ascii characters.  
            if (!IsPrintableAscii(SupportFiles.SupportFileDir))
            {
                Command.Run("UsersGuide.htm", new CommandOptions().AddCurrentDirectory(SupportFiles.SupportFileDir).AddStart());
                return true;
            }

            if (s_Browser == null)
            {
                s_Browser = new WebBrowserWindow(GuiApp.MainWindow);
                s_Browser.Title = "PerfView Help";

                // When you simply navigate, you don't remember your position.  In the case
                // Where the browser was closed you can at least fix it easily by starting over.
                // Thus we abandon browsers on close.  
                s_Browser.Closing += delegate
                {
                    s_Browser = null;
                };

                s_Browser.Browser.Navigating += delegate (object sender, NavigatingCancelEventArgs e)
                {
                    if (e.Uri != null && e.Uri.Host.Length > 0)
                    {
                        if (!GuiApp.MainWindow.AllowNativateToWeb)
                        {
                            GuiApp.MainWindow.StatusBar.LogError("Navigating to web disallowed, canceling.");
                            e.Cancel = true;
                        }
                    }
                };
            }

            string usersGuideFilePath = Path.Combine(SupportFiles.SupportFileDir, "UsersGuide.htm");
            string url = "file://" + usersGuideFilePath.Replace('\\', '/').Replace(" ", "%20");

            if (!string.IsNullOrEmpty(anchor))
            {
                url = url + "#" + anchor;
            }

            WebBrowserWindow.Navigate(s_Browser.Browser, url);
            if (s_Browser.WindowState == WindowState.Minimized)
            {
                s_Browser.WindowState = WindowState.Normal;
            }

            s_Browser.Show();
            s_Browser._Browser.Focus();
            return true;
        }

        private static bool IsPrintableAscii(string url)
        {
            for (int i = 0; i < url.Length; i++)
            {
                char c = url[i];
                if (!(' ' <= c && c <= 'z'))
                {
                    return false;
                }
            }
            return true;
        }

        private bool AllowNativateToWeb
        {
            get
            {
                if (!m_AllowNativateToWeb)
                {
                    var naviateToWeb = App.ConfigData["AllowNavigateToWeb"];
                    m_AllowNativateToWeb = naviateToWeb == "true";
                    if (!m_AllowNativateToWeb)
                    {
                        var result = MessageBox.Show(
                            "PerfView is about to fetch content from the web.\r\nIs this OK?",
                            "Navigate to Web", MessageBoxButton.YesNo);
                        if (result == MessageBoxResult.Yes)
                        {
                            m_AllowNativateToWeb = true;
                            App.ConfigData["AllowNavigateToWeb"] = "true";
                        }
                    }
                }
                return m_AllowNativateToWeb;
            }
        }

        private bool m_AllowNativateToWeb;

        private PerfViewDirectory m_CurrentDirectory;
        private static WebBrowserWindow s_Browser;
        private UserCommandDialog m_UserDefineCommandDialog;
        #endregion

        /// <summary>
        /// Indicates that 'outputFileName' should be opened after the command is completed.  
        /// </summary>
        public void OpenNext(string fileName)
        {
            m_openNextFileName = fileName;
        }

        private string m_openNextFileName;

        /// <summary>
        /// When you right click an item in the TreeView it doesn't automatically change to the TreeViewItem you clicked on.
        /// This helper method changes focus so that the right-click menu items commands are bound to the right TreeViewItem
        /// </summary>
        private void TreeView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            TreeViewItem treeViewItem = FindTreeViewItemInVisualHeirarchy(e.OriginalSource as DependencyObject);

            if (treeViewItem != null)
            {
                treeViewItem.Focus();
                e.Handled = true;
            }
        }

        /// <summary>
        /// Given an item in visual a tree, navigate the parents upwards until we find the TreeViewItem it represents.
        /// </summary>
        private static TreeViewItem FindTreeViewItemInVisualHeirarchy(DependencyObject source)
        {
            while (source != null && !(source is TreeViewItem))
            {
                source = VisualTreeHelper.GetParent(source);
            }

            return source as TreeViewItem;
        }
    }
}
