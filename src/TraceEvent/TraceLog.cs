//     Copyright (c) Microsoft Corporation.  All rights reserved.
// This file is best viewed using outline mode (Ctrl-M Ctrl-O)

// Uncomment this if you are having problems deserializing an ETLX file, and need to debug the problem.   It will create a useful log file.
// #define DEBUG_SERIALIZE

using FastSerialization;
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing.Compatibility;
using Microsoft.Diagnostics.Tracing.EventPipe;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.AspNet;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Parsers.ClrPrivate;
using Microsoft.Diagnostics.Tracing.Parsers.FrameworkEventSource;
using Microsoft.Diagnostics.Tracing.Parsers.JScript;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Parsers.Symbol;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Diagnostics.Tracing.Utilities;
using Microsoft.Diagnostics.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Address = System.UInt64;


namespace Microsoft.Diagnostics.Tracing.Etlx
{
    /// <summary>
    /// The data model for an Event trace log (ETL) file is simply a stream of events.     More sophisticated
    /// analysis typically needs a a richer data model then ETL files can provide, and this is the
    /// motivation for the ETLX (Event Trace Log eXtended) file format.   In particular any
    /// analysis that needs non-sequential access to the events or manipulates stack traces associated
    /// with events needs the additional support that the ETLX format provides.   See the TraceEventProgrammers guide
    /// for more on the capabilities of ETLX.
    /// <para>
    /// The TraceLog class is the programmatic representation of an ETLX file.   It represents the ETLX file as a whole.
    /// </para><para>
    /// ETLX files are typically created from ETL files using the TraceLog.OpenOrCreate method or more explicitly
    /// by the TraceLog.CreateFromEventTraceLogFile.
    /// </para>
    /// </summary>
    public sealed class TraceLog : TraceEventSource, IDisposable, IFastSerializable, IFastSerializableVersion
    {
        /// <summary>
        /// Given the path to an ETW trace log file (ETL) file, create an ETLX file for the data.
        /// <para>If etlxFilePath is null the output name is derived from etlFilePath by changing its file extension to .ETLX.</para>
        /// <returns>The name of the ETLX file that was generated.</returns>
        /// </summary>
        public static string CreateFromEventTraceLogFile(string filePath, string etlxFilePath = null, TraceLogOptions options = null, TraceEventDispatcherOptions traceEventDispatcherOptions = null)
        {
            if (etlxFilePath == null)
            {
                etlxFilePath = Path.ChangeExtension(filePath, ".etlx");
            }

            using (TraceEventDispatcher source = TraceEventDispatcher.GetDispatcherFromFileName(filePath, traceEventDispatcherOptions))
            {
                return CreateFromEventTraceLogFile(source, etlxFilePath, options);
            }
        }

        /// <summary>
        /// Given a <see cref="TraceEventDispatcher"/> that can be created from data source, create an ETLX file for the data.
        /// </summary>
        /// <para>If etlxFilePath is null the output name is derived from etlFilePath by changing its file extension to .ETLX.</para>
        /// <returns>The name of the ETLX file that was generated.</returns>
        public static string CreateFromEventTraceLogFile(TraceEventDispatcher source, string etlxFilePath, TraceLogOptions options = null)
        {
            if (etlxFilePath == null)
            {
                throw new ArgumentNullException(nameof(etlxFilePath));
            }

            if (source.EventsLost != 0 && options != null && options.OnLostEvents != null)
            {
                options.OnLostEvents(false, source.EventsLost, 0);
            }

            CreateFromTraceEventSource(source, etlxFilePath, options);

            return etlxFilePath;
        }

        /// <summary>
        /// Open an ETLX or ETL file as a ETLX file.
        /// <para>
        /// This routine assumes that you follow normal conventions of naming ETL files with the .ETL file extension
        /// and ETLX files with the .ETLX file extension.  It further assumes the ETLX file for a given ETL file
        /// should be in a file named the same as the ETL file with the file extension changed.
        /// </para><para>
        /// etlOrEtlxFilePath can be either the name of the ETL or ETLX file.   If the ETLX file does not
        /// exist or if it older than the corresponding ETL file then the ETLX file is regenerated with
        /// the given options.   However if an up-to-date ETLX file exists the conversion step is skipped.
        /// </para><para>
        /// Ultimately the ETLX file is opened and the resulting TraceLog instance is returned.
        /// </para>
        /// </summary>
        public static TraceLog OpenOrConvert(string etlOrEtlxFilePath, TraceLogOptions options = null)
        {
            // Accept either Etl or Etlx file name
            if (etlOrEtlxFilePath.EndsWith(".etl", StringComparison.OrdinalIgnoreCase))
            {
                etlOrEtlxFilePath = Path.ChangeExtension(etlOrEtlxFilePath, ".etlx");
            }

            // See if the etl file exists.
            string etlFilePath = Path.ChangeExtension(etlOrEtlxFilePath, ".etl");
            bool created = false;
            if (File.Exists(etlFilePath))
            {
                // Check that etlx is up to date.
                if (!File.Exists(etlOrEtlxFilePath) || File.GetLastWriteTimeUtc(etlOrEtlxFilePath) < File.GetLastWriteTimeUtc(etlFilePath))
                {
                    CreateFromEventTraceLogFile(etlFilePath, etlOrEtlxFilePath, options);
                    created = true;
                }
            }
            try
            {
                return new TraceLog(etlOrEtlxFilePath);
            }
            catch (Exception)
            {
                if (created)
                {
                    throw;
                }
            }
            // Try again to create from scratch.
            CreateFromEventTraceLogFile(etlFilePath, etlOrEtlxFilePath, options);
            return new TraceLog(etlOrEtlxFilePath);
        }

        /// <summary>
        /// From a TraceEventSession, create a real time TraceLog Event Source.   Like a ETWTraceEventSource a TraceLogEventSource
        /// will deliver events in real time.   However an TraceLogEventSource has an underlying Tracelog (which you can access with
        /// the .Log Property) which lets you get at aggregated information (Processes, threads, images loaded, and perhaps most
        /// importantly TraceEvent.CallStack() will work.  Thus you can get real time stacks from events).
        ///
        /// Note that in order for native stacks to resolve symbolically, you need to have some Kernel events turned on (Image, and Process)
        /// and only windows 8 has a session that allows both kernel and user mode events simultaneously.   Thus this is most useful
        /// on Win 8 systems.
        /// </summary>
        public static TraceLogEventSource CreateFromTraceEventSession(TraceEventSession session)
        {
            var traceLog = new TraceLog(session);
            return traceLog.realTimeSource;
        }

        /// <summary>
        /// Creates a ETLX file an Lttng Text file 'filePath'.
        /// </summary>
        public static string CreateFromLttngTextDataFile(string filePath, string etlxFilePath = null, TraceLogOptions options = null)
        {
            // Create the etlx file path.
            if (etlxFilePath == null)
            {
                etlxFilePath = filePath + ".etlx";
            }

            using (CtfTraceEventSource source = new CtfTraceEventSource(filePath))
            {
                if (source.EventsLost != 0 && options != null && options.OnLostEvents != null)
                {
                    options.OnLostEvents(false, source.EventsLost, 0);
                }

                CreateFromLinuxEventSources(source, etlxFilePath, null);
            }

            return etlxFilePath;
        }

        /// <summary>
        /// Creates a ETLX file an EventPipe 'filePath'.
        /// </summary>
        public static string CreateFromEventPipeDataFile(string filePath, string etlxFilePath = null, TraceLogOptions options = null)
        {
            // Create the etlx file path.
            if (etlxFilePath == null)
            {
                etlxFilePath = filePath + ".etlx";
            }

            using (var source = new EventPipeEventSource(filePath))
            {
                CreateFromEventPipeEventSources(source, etlxFilePath, options);

                if (source.EventsLost != 0 && options != null && options.OnLostEvents != null)
                {
                    options.OnLostEvents(false, source.EventsLost, 0);
                }
            }

            return etlxFilePath;
        }

        /// <summary>
        /// Opens an existing Extended Trace Event log file (ETLX) file.  See also TraceLog.OpenOrCreate.
        /// </summary>
        public TraceLog(string etlxFilePath)
            : this()
        {
            InitializeFromFile(etlxFilePath);
        }
        /// <summary>
        /// All the events in the ETLX file. The returned TraceEvents instance supports IEnumerable so it can be used
        /// in foreach statements, but it also supports other methods to further filter the evens before enumerating over them.
        ///
        /// Note that the TraceEvent returned from this IEnumerable may only be used for one iteration of the foreach.
        /// (it is reused for the next event).  If you need more lifetime than that you must call Clone() (see 'Lifetime
        /// Constraints' in the programmers guide for more).
        /// </summary>
        public TraceEvents Events
        {
            get
            {
                if (IsRealTime)
                {
                    throw new NotSupportedException("Enumeration is not supported on real time sessions.");
                }

                return events;
            }
        }
        /// <summary>
        /// All the Processes that logged an event in the ETLX file.  The returned TraceProcesses instance supports IEnumerable so it can be used
        /// in foreach statements, but it also supports other methods to select particular a particular process.
        /// </summary>
        public TraceProcesses Processes { get { return processes; } }
        /// <summary>
        /// All the Threads that logged an event in the ETLX file.  The returned TraceThreads instance supports IEnumerable so it can be used
        /// in foreach statements, but it also supports other methods to select particular thread.
        /// </summary>
        public TraceThreads Threads { get { return threads; } }
        /// <summary>
        /// All the module files (DLLs) that were loaded by some process in the ETLX file.  The returned TraceModuleFiles instance supports IEnumerable so it can be used
        /// in foreach statements, but it also supports other methods to select particular module file.
        /// </summary>
        public TraceModuleFiles ModuleFiles { get { return moduleFiles; } }
        /// <summary>
        /// All the call stacks in the ETLX file.  Normally you don't enumerate over these, but use you use other methods on TraceCallStacks
        /// information about code addresses using CallStackIndexes.
        /// </summary>
        public TraceCallStacks CallStacks { get { return callStacks; } }
        /// <summary>
        /// All the code addresses in the ETLX file.  Normally you don't enumerate over these, but use you use other methods on TraceCodeAddresses
        /// information about code addresses using CodeAddressIndexes.
        /// </summary>
        public TraceCodeAddresses CodeAddresses { get { return codeAddresses; } }
        /// <summary>
        /// Summary statistics on the events in the ETX file.
        /// </summary>
        public TraceEventStats Stats { get { return stats; } }

        // operations on events
        /// <summary>
        /// If the event has a call stack associated with it, retrieve it.   Returns null if there is not call stack associated with the event.
        /// <para>If you are retrieving many call stacks consider using GetCallStackIndexForEvent, as it is more efficient.</para>
        /// </summary>
        public TraceCallStack GetCallStackForEvent(TraceEvent anEvent)
        {
            return callStacks[GetCallStackIndexForEvent(anEvent)];
        }
        /// <summary>
        /// If the event has a call stack associated with it, retrieve CallStackIndex.   Returns CallStackIndex.Invalid if there is not call stack associated with the event.
        /// </summary>
        public CallStackIndex GetCallStackIndexForEvent(TraceEvent anEvent)
        {
            return GetCallStackIndexForEventIndex(anEvent.EventIndex);
        }

        /// <summary>
        /// Events are given an Index (ID) that are unique across the whole TraceLog.   They are not guaranteed
        /// to be sequential, but they are guaranteed to be between 0 and MaxEventIndex.  Ids can be used to
        /// allow clients to associate additional information with event (with a side lookup table).   See
        /// TraceEvent.EventIndex and EventIndex for more
        /// </summary>
        public EventIndex MaxEventIndex { get { return (EventIndex)eventCount; } }
        /// <summary>
        /// Given an eventIndex, get the event.  This is relatively expensive because we need to create a
        /// copy of the event that will not be reused by the TraceLog.   Ideally you would not use this API
        /// but rather use iterate over event using TraceEvents
        /// </summary>
        public TraceEvent GetEvent(EventIndex eventIndex)
        {
            // TODO this can probably be made more efficient.
            int pageIndex = (int)(((uint)eventIndex) / eventsPerPage);
            int eventOnPage = ((int)eventIndex) - (pageIndex * eventsPerPage);

            if (eventPages.Count <= pageIndex)
            {
                return null;
            }

            IEnumerable<TraceEvent> events = new TraceEvents(this, eventPages[pageIndex].TimeQPC, long.MaxValue, null, false);
            var iterator = events.GetEnumerator();
            while (iterator.MoveNext())
            {
                TraceEvent ret = iterator.Current;
                if (ret.EventIndex == eventIndex)
                {
                    return ret.Clone();
                }
            }
            return null;
        }
        /// <summary>
        /// The total number of events in the log.
        /// </summary>
        public int EventCount { get { return eventCount; } }

        /// <summary>
        /// The size of the log file in bytes.
        /// </summary>
        public override long Size
        {
            get
            {
                return new FileInfo(etlxFilePath).Length;
            }
        }
        /// <summary>
        /// override
        /// </summary>
        public override int EventsLost { get { return eventsLost; } }
        /// <summary>
        /// The file path for the ETLX file associated with this TraceLog instance.
        /// </summary>
        public string FilePath { get { return etlxFilePath; } }
        /// <summary>
        /// The machine on which the log was collected.  Returns empty string if unknown.
        /// </summary>
        public string MachineName { get { return machineName; } }
        /// <summary>
        /// The name of the Operating system.  Returns empty string if unknown.
        /// </summary>
        public string OSName { get { return osName; } }
        /// <summary>
        /// The build number information for the OS.  Returns empty string if unknown.
        /// </summary>
        public string OSBuild { get { return osBuild; } }
        /// <summary>
        /// The time the machine was booted.   Returns DateTime.MinValue if it is unknown.
        /// </summary>
        public DateTime BootTime { get { if (bootTime100ns == 0) { return DateTime.MaxValue; } return DateTime.FromFileTime(bootTime100ns); } }
        /// <summary>
        /// This is the number of minutes between the local time where the data was collected and UTC time.
        /// It is negative if your time zone is WEST of Greenwich.  This DOES take Daylights savings time into account
        /// but might be a daylight savings time transition happens inside the trace.
        /// May be unknown, in which case it returns null.
        /// </summary>
        public int? UTCOffsetMinutes { get { return utcOffsetMinutes; } }
        /// <summary>
        /// When an ETL file is 'merged', for every DLL in the trace information is added that allows the symbol
        /// information (PDBS) to be identified unambiguously on a symbol server.   This property returns true
        /// if the ETLX file was created from an ETL file with this added information.
        /// </summary>
        public bool HasPdbInfo { get { return hasPdbInfo; } }
        /// <summary>
        /// The size of the main memory (RAM) on the collection machine.  Will return 0 if memory size is unknown
        /// </summary>
        public int MemorySizeMeg { get { return memorySizeMeg; } }
        /// <summary>
        /// Are there any event in trace that has a call stack associated with it.
        /// </summary>
        public bool HasCallStacks { get { return CallStacks.Count > 0; } }
        /// <summary>
        /// If Kernel CPU sampling events are turned on, CPU samples are taken at regular intervals (by default every MSec).
        /// <para>This property returns the time interval between samples.
        /// </para><para>
        /// If the sampling interval was changed over the course of the trace, this property does not reflect that.  It
        /// returns the first value it had in the trace.
        /// </para>
        /// </summary>
        public TimeSpan SampleProfileInterval { get { return new TimeSpan(sampleProfileInterval100ns); } }
        /// <summary>
        /// Returns true if the  machine running this code is the same as the machine where the trace data was collected.
        /// <para>
        /// If this returns false, the path names references in the trace cannot be inspected (since they are on a different machine).
        /// </para>
        /// </summary>
        public bool CurrentMachineIsCollectionMachine()
        {
            if (IsRealTime)
            {
                return true;
            }

            // Trim off the domain, as there is ambiguity about whether to include that or not.
            var shortCurrentMachineName = Environment.MachineName;
            var dotIdx = shortCurrentMachineName.IndexOf('.');
            if (dotIdx > 0)
            {
                shortCurrentMachineName = shortCurrentMachineName.Substring(0, dotIdx);
            }

            var shortDataMachineName = MachineName;
            if (string.IsNullOrEmpty(shortDataMachineName))
            {
                return true;        // If the trace does not know what machine it was on, give up and guess that is is the correct machine.
            }

            dotIdx = shortDataMachineName.IndexOf('.');
            if (dotIdx > 0)
            {
                shortDataMachineName = shortDataMachineName.Substring(0, dotIdx);
            }

            return string.Compare(shortDataMachineName, shortCurrentMachineName, StringComparison.OrdinalIgnoreCase) == 0;
        }
        /// <summary>
        /// There is a size limit for ETLX files.  Thus  it is possible that the data from the original ETL file was truncated.
        /// This property returns true if this happened.
        /// </summary>
        public bool Truncated { get { return truncated; } }

        /// <summary>
        /// Returns the EvnetIndex (order in the file) of the first event that has a
        /// timestamp smaller than its predecessor.  Returns Invalid if there are no time inversions.
        /// </summary>
        public EventIndex FirstTimeInversion { get { return firstTimeInversion; } }

        /// <summary>
        /// Returns all the TraceEventParsers associated with this log.
        /// </summary>
        public IEnumerable<TraceEventParser> Parsers { get { return parsers.Values; } }

        /// <summary>
        /// An XML fragment that gives useful summary information about the trace as a whole.
        /// </summary>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(" <TraceLogHeader ");
            sb.AppendLine("   MachineName=" + XmlUtilities.XmlQuote(MachineName));
            sb.AppendLine("   EventCount=" + XmlUtilities.XmlQuote(EventCount));
            sb.AppendLine("   FilePath=" + XmlUtilities.XmlQuote(FilePath));
            sb.AppendLine("   EventsLost=" + XmlUtilities.XmlQuote(EventsLost));
            sb.AppendLine("   SessionStartTime=" + XmlUtilities.XmlQuote(SessionStartTime));
            sb.AppendLine("   SessionEndTime=" + XmlUtilities.XmlQuote(SessionEndTime));
            sb.AppendLine("   SessionDuration=" + XmlUtilities.XmlQuote((SessionDuration).ToString()));
            sb.AppendLine("   NumberOfProcessors=" + XmlUtilities.XmlQuote(NumberOfProcessors));
            sb.AppendLine("   CpuSpeedMHz=" + XmlUtilities.XmlQuote(CpuSpeedMHz));
            sb.AppendLine("   MemorySizeMeg=" + XmlUtilities.XmlQuote(MemorySizeMeg));
            sb.AppendLine("   PointerSize=" + XmlUtilities.XmlQuote(PointerSize));
            sb.AppendLine(" />");
            return sb.ToString();
        }

        #region ITraceParserServices Members

        internal override void RegisterEventTemplateImpl(TraceEvent template)
        {
            if (IsRealTime)
            {
                Debug.Assert(template.traceEventSource == this);
                realTimeSource.RegisterEventTemplateImpl(template);
                Debug.Assert(template.Source == this);
                return;
            }
            if (template.Target != null && !registeringStandardParsers)
            {
                throw new ApplicationException("You may not register callbacks in TraceEventParsers that you attach directly to a TraceLog.\r\n" +
                    "Instead you should use TraceEvents.GetSource() and attach TraceEventParsers to that and define callbacks on them");
            }
        }

        internal override void UnregisterEventTemplateImpl(Delegate action, Guid providerGuid, int eventId)
        {
            if (IsRealTime)
            {
                realTimeSource.UnregisterEventTemplateImpl(action, providerGuid, eventId);
            }
        }

        internal override void RegisterParserImpl(TraceEventParser parser)
        {
            Debug.Assert(parser.Source == this);
            var name = parser.GetType().FullName;
            if (!parsers.ContainsKey(name))
            {
                parsers[name] = parser;
            }
        }

        internal override void RegisterUnhandledEventImpl(Func<TraceEvent, bool> callback)
        {
            if (IsRealTime)
            {
                realTimeSource.RegisterUnhandledEventImpl(callback);
            }
        }

        internal override string TaskNameForGuidImpl(Guid guid)
        {
            var lookup = AllocLookup();
            var ret = ((ITraceParserServices)lookup).TaskNameForGuid(guid);
            FreeLookup(lookup);
            return ret;
        }
        internal override string ProviderNameForGuidImpl(Guid taskOrProviderGuid)
        {
            var lookup = AllocLookup();
            var ret = ((ITraceParserServices)lookup).ProviderNameForGuid(taskOrProviderGuid);
            FreeLookup(lookup);
            return ret;
        }

        #endregion
        #region Private
        private TraceLog()
        {
            // TODO: All the IFastSerializable parts of this are discarded, which is unfortunate.
            processes = new TraceProcesses(this);
            threads = new TraceThreads(this);
            events = new TraceEvents(this);
            moduleFiles = new TraceModuleFiles(this);
            codeAddresses = new TraceCodeAddresses(this, moduleFiles);
            callStacks = new TraceCallStacks(this, codeAddresses);
            parsers = new Dictionary<string, TraceEventParser>();
            stats = new TraceEventStats(this);
            machineName = "";
            osName = "";
            osBuild = "";
            sampleProfileInterval100ns = 10000;    // default is 1 msec
            fnAddAddressToCodeAddressMap = AddAddressToCodeAddressMap;
            firstTimeInversion = EventIndex.Invalid;
        }

        /// <summary>
        /// Create a new real time session called 'sessionName' and connect a TraceLog to it and return that TraceLog.
        /// Functionality of TraceLog that does not depend on either remembering past EVENTS or require future
        /// knowledge (e.g. stacks of kernel events), will 'just work'.
        /// </summary>
        private unsafe TraceLog(TraceEventSession session)
            : this()
        {
            IsRealTime = true;
            machineName = Environment.MachineName;

            realTimeSource = new TraceLogEventSource(events, ownsItsTraceLog: true);   // Dispose
            realTimeQueue = new Queue<QueueEntry>();
            realTimeFlushTimer = new Timer(FlushRealTimeEvents, null, 1000, 1000);
            pointerSize = ETWTraceEventSource.GetOSPointerSize();

            //double lastTime = 0;

            // Set up callbacks that handle stack processing
            Action<TraceEvent> onAllEvents = delegate (TraceEvent data)
            {
                // we need to guard our data structures from concurrent access.  TraceLog data
                // is modified by this code as well as code in FlushRealTimeEvents.
                lock (realTimeQueue)
                {
                    // we delay things so we have a chance to match up stacks.

                    // if (!removeFromStream && data.Opcode != TraceEventOpcode.DataCollectionStart && data.ProcessID != 0 && data.ProviderGuid != ClrRundownTraceEventParser.ProviderGuid)
                    //     Trace.WriteLine("REAL TIME QUEUE:  " + data.ToString());
                    TraceEventCounts countForEvent = Stats.GetEventCounts(data);
                    Debug.Assert((int)data.EventIndex == eventCount);
                    countForEvent.m_count++;
                    countForEvent.m_eventDataLenTotal += data.EventDataLength;

                    // Remember past events so we can hook up stacks to them.
                    data.eventIndex = (EventIndex)eventCount;
                    pastEventInfo.LogEvent(data, data.eventIndex, countForEvent);
                    eventCount++;

                    // currentID is used by the dispatcher to define the EventIndex.  Make sure at both sources have the
                    // same notion of what that is if we have two dispatcher.
                    if (rawKernelEventSource != null)
                    {
                        rawEventSourceToConvert.currentID = (EventIndex)eventCount;
                        rawKernelEventSource.currentID = (EventIndex)eventCount;
                    }

                    // Skip samples from the idle thread.
                    if (data.ProcessID == 0 && data is SampledProfileTraceData)
                    {
                        return;
                    }

                    var extendedDataCount = data.eventRecord->ExtendedDataCount;
                    if (extendedDataCount != 0)
                    {
                        bookKeepingEvent |= ProcessExtendedData(data, extendedDataCount, countForEvent);
                    }

                    realTimeQueue.Enqueue(new QueueEntry(data.Clone(), Environment.TickCount));
                }
            };

            // See if we are on Win7 and have a separate kernel session associated with 'session'
            if (session.m_kernelSession != null)
            {
                // Make sure both sources only dispatch one at a time by taking a lock during dispatch.
                session.m_kernelSession.Source.lockObj = realTimeQueue;
                session.m_associatedWithTraceLog = true;                         // Indicate that it is OK to have the m_kernelSession.
                session.Source.lockObj = realTimeQueue;

                // Set up the callbacks to the kernel session.
                rawKernelEventSource = session.m_kernelSession.Source;
                SetupCallbacks(rawKernelEventSource);
                rawKernelEventSource.unhandledEventTemplate.traceEventSource = this;       // Make everything point to the log as its source.
                rawKernelEventSource.AllEvents += onAllEvents;
            }

            // We use the session's source for our input.
            rawEventSourceToConvert = session.Source;
            SetupCallbacks(rawEventSourceToConvert);
            rawEventSourceToConvert.unhandledEventTemplate.traceEventSource = this;       // Make everything point to the log as its source.
            rawEventSourceToConvert.AllEvents += onAllEvents;
        }

        /// <summary>
        /// Removes all but the last 'keepCount' entries in 'growableArray' by sliding them down.
        /// </summary>
        private static void RemoveAllButLastEntries<T>(ref GrowableArray<T> growableArray, int keepCount)
        {
            Array.Copy(growableArray.UnderlyingArray, growableArray.Count - keepCount, growableArray.UnderlyingArray, 0, keepCount);
            growableArray.Count = keepCount;
        }

        /// <summary>
        /// Forwards an event that was saved (cloned) to the dispatcher associated with the real time source.
        /// </summary>
        private unsafe void DispatchClonedEvent(TraceEvent toSend)
        {
            TraceEvent eventInRealTimeSource = realTimeSource.Lookup(toSend.eventRecord);
            eventInRealTimeSource.userData = toSend.userData;
            eventInRealTimeSource.eventIndex = toSend.eventIndex;           // Lookup assigns the EventIndex, but we want to keep the original.
            realTimeSource.Dispatch(eventInRealTimeSource);

            // Optimization, remove 'toSend' from the finalization queue.
            Debug.Assert(toSend.myBuffer != IntPtr.Zero);
            GC.SuppressFinalize(toSend);    // Tell the finalizer you don't need it because I will do the cleanup
            // Do the cleanup, but also keep toSend alive during the dispatch and until finalization was suppressed.
            System.Runtime.InteropServices.Marshal.FreeHGlobal(toSend.myBuffer);
            toSend.instanceContainerID = null;
        }

        /// <summary>
        /// Flushes any event that has waited around long enough
        /// </summary>
        private void FlushRealTimeEvents(object notUsed)
        {
            lock (realTimeQueue)
            {
                var nowTicks = Environment.TickCount;
                // TODO review.
                for (; ; )
                {
                    var count = realTimeQueue.Count;
                    if (count == 0)
                    {
                        break;
                    }

                    QueueEntry entry = realTimeQueue.Peek();
                    // If it has been in the queue less than 1 second, we we wait until next time) & 3FFFFFF does wrap around subtraction.
                    if (((nowTicks - entry.enqueueTick) & 0x3FFFFFFF) < 1000)
                    {
                        break;
                    }

                    DispatchClonedEvent(entry.data);
                    realTimeQueue.Dequeue();
                }

                // Try to keep our memory under control by removing old data.
                // Lots of data structures in TraceLog can grow over time.
                // However currently we only trim three, all CAN grow on every event (so they grow most quickly of all data structures)
                // and we know they are not needed after dispatched the events they are for.

                // To keep overhead reasonable, we assume the worst case (every event has an entry) and we allow the tables to grow
                // to 3X what is needed, and then we slide down the 1X of entries we need.
                // We could be more accurate, but this at least keeps THESE arrays under control.
                int MaxEventCountBeforeReset = Math.Max(realTimeQueue.Count * 3, 1000);

                if (eventsToStacks.Count > MaxEventCountBeforeReset)
                {
                    RemoveAllButLastEntries(ref eventsToStacks, realTimeQueue.Count);
                }

                if (eventsToCodeAddresses.Count > MaxEventCountBeforeReset)
                {
                    RemoveAllButLastEntries(ref eventsToCodeAddresses, realTimeQueue.Count);
                }

                if (cswitchBlockingEventsToStacks.Count > MaxEventCountBeforeReset)
                {
                    RemoveAllButLastEntries(ref cswitchBlockingEventsToStacks, realTimeQueue.Count);
                }
            }
        }

        /// <summary>
        /// Given a process's virtual address 'address' and an event which acts as a
        /// context (determines which process and what time in that process), return
        /// a CodeAddressIndex (which represents a particular location in a particular
        /// method in a particular DLL). It is possible that different addresses will
        /// go to the same code address for the same address (in different contexts).
        /// This is because DLLS where loaded in different places in different processes.
        /// </summary>
        public CodeAddressIndex GetCodeAddressIndexAtEvent(Address address, TraceEvent context)
        {
            // TODO optimize for sequential access.
            EventIndex eventIndex = context.EventIndex;
            int index;
            if (!eventsToCodeAddresses.BinarySearch(eventIndex, out index, CodeAddressComparer))
            {
                return CodeAddressIndex.Invalid;
            }

            do
            {
                Debug.Assert(eventsToCodeAddresses[index].EventIndex == eventIndex);
                if (eventsToCodeAddresses[index].Address == address)
                {
                    return eventsToCodeAddresses[index].CodeAddressIndex;
                }

                index++;
            } while (index < eventsToCodeAddresses.Count && eventsToCodeAddresses[index].EventIndex == eventIndex);
            return CodeAddressIndex.Invalid;
        }

        /// <summary>
        /// If an event has a field of type 'Address' the address can be converted to a symbolic value (a
        /// TraceCodeAddress) by calling this function.   C
        /// </summary>
        internal TraceCodeAddress GetCodeAddressAtEvent(Address address, TraceEvent context)
        {
            CodeAddressIndex codeAddressIndex = GetCodeAddressIndexAtEvent(address, context);
            if (codeAddressIndex == CodeAddressIndex.Invalid)
            {
                return null;
            }

            return codeAddresses[codeAddressIndex];
        }

        /// <summary>
        /// Given an EventIndex for an event, retrieve the call stack associated with it
        /// (that can be given to TraceCallStacks). Many events may not have associated
        /// call stack in which case CallSTackIndex.Invalid is returned.
        /// </summary>
        internal CallStackIndex GetCallStackIndexForEventIndex(EventIndex eventIndex)
        {
            // TODO optimize for sequential access.
            lazyEventsToStacks.FinishRead();
            int index;
            if (eventsToStacks.BinarySearch(eventIndex, out index, stackComparer))
            {
                return eventsToStacks[index].CallStackIndex;
            }

            return CallStackIndex.Invalid;
        }

        internal static void CreateFromLinuxEventSources(CtfTraceEventSource source, string etlxFilePath, TraceLogOptions options)
        {
            if (options == null)
            {
                options = new TraceLogOptions();
            }

            TraceLog newLog = new TraceLog();
            newLog.rawEventSourceToConvert = source;
            newLog.options = options;

            // Parse the metadata.
            source.ParseMetadata();

            // Get all the users data from the original source.   Note that this happens by reference, which means
            // that even though we have not built up the state yet (since we have not scanned the data yet), it will
            // still work properly (by the time we look at this user data, it will be updated).
            foreach (string key in source.UserData.Keys)
            {
                newLog.UserData[key] = source.UserData[key];
            }

            // Avoid partially written files by writing to a temp and moving atomically to the final destination.
            string etlxTempPath = etlxFilePath + ".new";
            try
            {
                //****************************************************************************************************
                // ******** This calls TraceLog.ToStream operation on TraceLog which does the real work.   ***********
                using (Serializer serializer = new Serializer(etlxTempPath, newLog)) { }
                if (File.Exists(etlxFilePath))
                {
                    File.Delete(etlxFilePath);
                }

                File.Move(etlxTempPath, etlxFilePath);
            }
            finally
            {
                if (File.Exists(etlxTempPath))
                {
                    File.Delete(etlxTempPath);
                }
            }
        }

        internal static void CreateFromEventPipeEventSources(TraceEventDispatcher source, string etlxFilePath, TraceLogOptions options)
        {
            if (options == null)
            {
                options = new TraceLogOptions();
            }

            TraceLog newLog = new TraceLog();
            newLog.rawEventSourceToConvert = source;
            newLog.options = options;

            var dynamicParser = source.Dynamic;

            // Get all the users data from the original source.   Note that this happens by reference, which means
            // that even though we have not built up the state yet (since we have not scanned the data yet), it will
            // still work properly (by the time we look at this user data, it will be updated).
            foreach (string key in source.UserData.Keys)
            {
                newLog.UserData[key] = source.UserData[key];
            }

            // Avoid partially written files by writing to a temp and moving atomically to the final destination.
            string etlxTempPath = etlxFilePath + ".new";
            try
            {
                //****************************************************************************************************
                // ******** This calls TraceLog.ToStream operation on TraceLog which does the real work.   ***********
                using (Serializer serializer = new Serializer(etlxTempPath, newLog)) { }
                if (File.Exists(etlxFilePath))
                {
                    File.Delete(etlxFilePath);
                }

                File.Move(etlxTempPath, etlxFilePath);
            }
            finally
            {
                if (File.Exists(etlxTempPath))
                {
                    File.Delete(etlxTempPath);
                }
            }
        }

        /// <summary>
        /// Given a eventIndex for a CSWTICH event, return the call stack index for the thread
        /// that LOST the processor (the normal callStack is for the thread that GOT the CPU)
        /// </summary>
        internal CallStackIndex GetCallStackIndexForCSwitchBlockingEventIndex(EventIndex eventIndex)
        {
            // TODO optimize for sequential access.
            lazyCswitchBlockingEventsToStacks.FinishRead();
            int index;
            if (cswitchBlockingEventsToStacks.BinarySearch(eventIndex, out index, stackComparer))
            {
                return cswitchBlockingEventsToStacks[index].CallStackIndex;
            }

            return CallStackIndex.Invalid;
        }

        // TODO expose this publicly?
        /// <summary>
        /// Given a source of events 'source' generated a ETLX file representing these events from them. This
        /// file can then be opened with the TraceLog constructor. 'options' can be null.
        /// </summary>
        internal static void CreateFromTraceEventSource(TraceEventDispatcher source, string etlxFilePath, TraceLogOptions options)
        {
            if (options == null)
            {
                options = new TraceLogOptions();
            }

            // TODO copy the additional data from a ETLX file if the source is ETLX
            using (TraceLog newLog = new TraceLog())
            {
                newLog.rawEventSourceToConvert = source;

                newLog.options = options;

                if (options.ExplicitManifestDir != null && Directory.Exists(options.ExplicitManifestDir))
                {
                    var tmfDir = Path.Combine(options.ExplicitManifestDir, "TMF");
                    if (Directory.Exists(tmfDir))
                    {
                        options.ConversionLog.WriteLine("Looking for WPP metaData in {0}", tmfDir);
                        new WppTraceEventParser(newLog, tmfDir);
                    }
                    options.ConversionLog.WriteLine("Looking for explicit manifests in {0}", options.ExplicitManifestDir);
                    source.Dynamic.ReadAllManifests(options.ExplicitManifestDir);
                }

                // Any parser that has state we need to turn on during the conversion so that the the state will build up
                // (we copy it out below).   To date there are only three parsers that do this (registered, dynamic
                // (which includes registered), an kernel)
                // TODO add an option that allows users to add their own here.
                // Note that I am not using the variables below, I am fetching the value so that it has the side
                // effect of creating this parser (which will in turn indicate to the system that I care about the
                // state these parsers generate as part of their operation).
                var dynamicParser = source.Dynamic;
                var clrParser = source.Clr;
                var kernelParser = source.Kernel;

                // Get all the users data from the original source.   Note that this happens by reference, which means
                // that even though we have not built up the state yet (since we have not scanned the data yet), it will
                // still work properly (by the time we look at this user data, it will be updated).
                foreach (string key in source.UserData.Keys)
                {
                    newLog.UserData[key] = source.UserData[key];
                }

                // Avoid partially written files by writing to a temp and moving atomically to the
                // final destination.
                string etlxTempPath = etlxFilePath + ".new";
                try
                {
                    //****************************************************************************************************
                    // ******** This calls TraceLog.ToStream operation on TraceLog which does the real work.   ***********
                    using (Serializer serializer = new Serializer(etlxTempPath, newLog)) { }
                    if (File.Exists(etlxFilePath))
                    {
                        File.Delete(etlxFilePath);
                    }

                    File.Move(etlxTempPath, etlxFilePath);
                }
                finally
                {
                    if (File.Exists(etlxTempPath))
                    {
                        File.Delete(etlxTempPath);
                    }
                }
            }
        }

        internal void RegisterStandardParsers()
        {
            registeringStandardParsers = true;

            // We always create these parsers that the TraceLog knows about.   The current invariant is that
            // a ETLX file does not need anything outside itself to resolve any events.  All of that is done
            // at file creation time.
            var kernelParser = Kernel;
            var clrParser = Clr;
            new ClrRundownTraceEventParser(this);
            new ClrStressTraceEventParser(this);
            new ClrPrivateTraceEventParser(this);
            new JScriptTraceEventParser(this);
            new JSDumpHeapTraceEventParser(this);
            new AspNetTraceEventParser(this);
            new TplEtwProviderTraceEventParser(this);
            new SymbolTraceEventParser(this);
            new HeapTraceProviderTraceEventParser(this);
            new MicrosoftWindowsKernelFileTraceEventParser(this);
            new IisTraceEventParser(this);

            new SampleProfilerTraceEventParser(this);
            new WpfTraceEventParser(this);
#if false
            new AppHostTraceEventParser(newLog);
            new ImmersiveShellTraceEventParser(newLog);
            new XamlTraceEventParser(newLog);
#endif
            var dynamicParser = Dynamic;
            registeringStandardParsers = false;

        }

        internal override unsafe Guid GetRelatedActivityID(TraceEventNativeMethods.EVENT_RECORD* eventRecord)
        {
            // See TraceLog.ProcessExtendedData for more on our use of ExtendedData to hold a index.
            if ((ulong)eventRecord->ExtendedData > 0)
            {
                int idIndex;
                if(IntPtr.Size == 8)
                {
                    idIndex = (int)((ulong)eventRecord->ExtendedData >> 4);
                }
                else
                {
                    idIndex = (int)eventRecord->ExtendedData;
                }
                if ((uint)idIndex < (uint)relatedActivityIDs.Count)
                {
                    return relatedActivityIDs[idIndex];
                }
            }
            return Guid.Empty;
        }

        internal override unsafe string GetContainerID(TraceEventNativeMethods.EVENT_RECORD* eventRecord)
        {
            if(eventRecord->ExtendedDataCount > 0)
            {
                int index = eventRecord->ExtendedDataCount;
                if(index < containerIDs.Count)
                {
                    return containerIDs[index];
                }
            }
            return null;
        }

        internal override unsafe int LastChanceGetThreadID(TraceEvent data)
        {
            Debug.Assert(data.eventRecord->EventHeader.ThreadId == -1);          // we should only be calling this when we have no better answer.
            CallStackIndex callStack = data.CallStackIndex();
            if (callStack == CallStackIndex.Invalid)
            {
                return -1;
            }

            TraceThread thread = CallStacks.Thread(callStack);
            if (thread == null)
            {
                return -1;
            }

            return thread.ThreadID;
        }
        internal override unsafe int LastChanceGetProcessID(TraceEvent data)
        {
            Debug.Assert(data.eventRecord->EventHeader.ProcessId == -1);          // we should only be calling this when we have no better answer.
            CallStackIndex callStack = data.CallStackIndex();
            if (callStack == CallStackIndex.Invalid)
            {
                return -1;
            }

            TraceThread thread = CallStacks.Thread(callStack);
            if (thread == null)
            {
                return -1;
            }

            return thread.Process.ProcessID;
        }


        private void AddMarkThread(int threadID, long timeStamp, int heapNum)
        {
            var thread = Threads.GetThread(threadID, timeStamp);
            if (thread == null)
            {
                return;
            }

            if (thread.threadInfo != null)
            {
                return;
            }

            if (thread.process.shouldCheckIsServerGC)
            {
                thread.process.markThreadsInGC[threadID] = heapNum;
            }
        }
        /// <summary>
        /// SetupCallbacks installs all the needed callbacks for TraceLog Processing (stacks, process, thread, summaries etc)
        /// on the TraceEventSource rawEvents.
        /// </summary>
        private unsafe void SetupCallbacks(TraceEventDispatcher rawEvents)
        {
            processingDisabled = false;
            removeFromStream = false;
            bookKeepingEvent = false;                  // BookKeeping events are removed from the stream by default
            bookeepingEventThatMayHaveStack = false;   // Some bookkeeping events (ThreadDCEnd) might have stacks
            noStack = false;                           // This event should never have a stack associated with it, so skip them if we every try to attach a stack.
            numberOnPage = eventsPerPage;
            pastEventInfo = new PastEventInfo(this);
            eventCount = 0;

            // FIX NOW HACK, because Method and Module unload methods are missing.
            jittedMethods = new List<MethodLoadUnloadVerboseTraceData>();
            jsJittedMethods = new List<MethodLoadUnloadJSTraceData>();
            sourceFilesByID = new Dictionary<JavaScriptSourceKey, string>();

            // If this is a ETL file, we also need to compute all the normal TraceLog stuff the raw stream
            pointerSize = rawEvents.PointerSize;
            _syncTimeUTC = rawEvents._syncTimeUTC;
            _syncTimeQPC = rawEvents._syncTimeQPC;
            _QPCFreq = rawEvents._QPCFreq;
            sessionStartTimeQPC = rawEvents.sessionStartTimeQPC;
            sessionEndTimeQPC = rawEvents.sessionEndTimeQPC;
            cpuSpeedMHz = rawEvents.CpuSpeedMHz;
            numberOfProcessors = rawEvents.NumberOfProcessors;
            eventsLost = rawEvents.EventsLost;
            osVersion = rawEvents.OSVersion;

            // These parsers create state and we want to collect that so we put it on our 'parsers' list that we serialize.
            var kernelParser = rawEvents.Kernel;

            // If a event does not have a callback, then it will be treated as unknown.  Unfortunately this also means that the
            // virtual method 'LogCodeAddresses() will not fire.  Thus any event that has this overload needs to have a callback.
            // The events below don't otherwise need a callback, but we add one so that LogCodeAddress() works.
            Action<TraceEvent> doNothing = delegate (TraceEvent data) { };

            // TODO: I have given up for now.   IN addition to the events with LogCodeAddress, you also need any event with FixupData()
            // methods associated with them.  There are enough of these that I did not want to do them one by one (mostly because of fragility)
            // Also kernel events have the potential for being before the process start event, and we need to see these to fix this.  (mostly memory / virtual alloc events).
            kernelParser.All += doNothing;

            // We want high volume events to be looked up properly since GetEventCount() is slower thant we want.
            rawEvents.Clr.GCAllocationTick += doNothing;
            rawEvents.Clr.GCJoin += doNothing;
            rawEvents.Clr.GCFinalizeObject += doNothing;
            rawEvents.Clr.MethodJittingStarted += doNothing;

            //kernelParser.AddCallbackForEvents<PageFaultTraceData>(doNothing);        // Lots of page fault ones
            //kernelParser.AddCallbackForEvents<PageAccessTraceData>(doNothing);
            //kernelParser.PerfInfoSysClEnter += doNothing;
            //kernelParser.PMCCounterProf += doNothing;

            Debug.Assert(((eventsPerPage - 1) & eventsPerPage) == 0, "eventsPerPage must be a power of 2");

            kernelParser.EventTraceHeader += delegate (EventTraceHeaderTraceData data)
            {
                bootTime100ns = data.BootTime100ns;

                if (_syncTimeQPC == 0)
                {   // This is for the TraceLog, not just for the ETWTraceEventSource
                    _syncTimeQPC = data.TimeStampQPC;
                    sessionStartTimeQPC += data.TimeStampQPC;
                    sessionEndTimeQPC += data.TimeStampQPC;
                }

                if (!utcOffsetMinutes.HasValue)
                {
                    utcOffsetMinutes = -data.UTCOffsetMinutes;
                    if (SessionStartTime.IsDaylightSavingTime())
                    {
                        utcOffsetMinutes += 60;         // Compensate for Daylight savings time.
                    }
                }
            };

            kernelParser.SystemConfigCPU += delegate (SystemConfigCPUTraceData data)
            {
                memorySizeMeg = data.MemSize;
                if (data.DomainName.Length > 0)
                {
                    machineName = data.ComputerName + "." + data.DomainName;
                }
                else
                {
                    machineName = data.ComputerName;
                }
            };

            kernelParser.SysConfigBuildInfo += delegate (BuildInfoTraceData data)
            {
                osName = data.ProductName;
                osBuild = data.BuildLab;
            };

            // Process level events.
            kernelParser.ProcessStartGroup += delegate (ProcessTraceData data)
            {
                processes.GetOrCreateProcess(data.ProcessID, data.TimeStampQPC, data.Opcode == TraceEventOpcode.Start).ProcessStart(data);
                // Don't filter them out (not that many, useful for finding command line)
            };

            kernelParser.ProcessEndGroup += delegate (ProcessTraceData data)
            {
                processes.GetOrCreateProcess(data.ProcessID, data.TimeStampQPC).ProcessEnd(data);
                // Don't filter them out (not that many, useful for finding command line)
            };
            // Thread level events
            kernelParser.ThreadStartGroup += delegate (ThreadTraceData data)
            {
                TraceProcess process = processes.GetOrCreateProcess(data.ProcessID, data.TimeStampQPC);
                thread = Threads.GetOrCreateThread(data.ThreadID, data.TimeStampQPC, process, data.Opcode == TraceEventOpcode.Start || data.Opcode == TraceEventOpcode.DataCollectionStart);
                thread.startTimeQPC = data.TimeStampQPC;
                thread.userStackBase = data.UserStackBase;
                if (data.Opcode == TraceEventOpcode.DataCollectionStart)
                {
                    bookKeepingEvent = true;
                    thread.startTimeQPC = sessionStartTimeQPC;
                }
                else if (data.Opcode == TraceEventOpcode.Start)
                {
                    var threadProc = thread.Process;
                    if (!threadProc.anyThreads)
                    {
                        // We saw a real process start (not a DCStart or a non at all)
                        if (sessionStartTimeQPC < threadProc.startTimeQPC && threadProc.startTimeQPC < data.TimeStampQPC)
                        {
                            thread.threadInfo = "Startup Thread";
                        }

                        threadProc.anyThreads = true;
                    }
                }
            };

            kernelParser.ThreadSetName += delegate (ThreadSetNameTraceData data)
            {
                CategorizeThread(data, data.ThreadName);
            };

            kernelParser.ThreadEndGroup += delegate (ThreadTraceData data)
            {
                TraceProcess process = processes.GetOrCreateProcess(data.ProcessID, data.TimeStampQPC);
                thread = Threads.GetOrCreateThread(data.ThreadID, data.TimeStampQPC, process);
                if (thread.process == null)
                {
                    thread.process = process;
                }

                if (data.ThreadName.Length > 0)
                {
                    CategorizeThread(data, data.ThreadName);
                }

                Debug.Assert(thread.process == process, "Different events disagree on the process object!");
                DebugWarn(thread.endTimeQPC == long.MaxValue || thread.ThreadID == 0,
                    "Thread end on a terminated thread " + data.ThreadID + " that ended at " + QPCTimeToRelMSec(thread.endTimeQPC), data);
                DebugWarn(thread.Process.endTimeQPC == long.MaxValue, "Thread ending on ended process", data);
                thread.endTimeQPC = data.TimeStampQPC;
                thread.userStackBase = data.UserStackBase;
                if (data.Opcode == TraceEventOpcode.DataCollectionStop)
                {
                    thread.endTimeQPC = sessionEndTimeQPC;
                    bookKeepingEvent = true;
                    bookeepingEventThatMayHaveStack = true;
                }

                // Keep threadIDtoThread table under control by removing old entries.
                if (IsRealTime)
                {
                    Threads.threadIDtoThread.Remove(data.ThreadID);
                }
            };

            // ModuleFile level events
            DbgIDRSDSTraceData lastDbgData = null;
            ImageIDTraceData lastImageIDData = null;
            FileVersionTraceData lastFileVersionData = null;
            TraceModuleFile lastTraceModuleFile = null;
            long lastTraceModuleFileQPC = 0;

            kernelParser.ImageGroup += delegate (ImageLoadTraceData data)
            {
                var isLoad = ((data.Opcode == (TraceEventOpcode)10) || (data.Opcode == TraceEventOpcode.DataCollectionStart));

                // TODO is this a good idea?   It tries to undo the anonimization a bit.
                var fileName = data.FileName;
                if (fileName.EndsWith("########"))  // We threw away the DLL name
                {
                    // But at least we have the DLL file name (not the path).
                    if (lastImageIDData != null && data.TimeStampQPC == lastImageIDData.TimeStampQPC)
                    {
                        var anonomizedIdx = fileName.IndexOf("########");
                        fileName = fileName.Substring(0, anonomizedIdx + 8) + @"\" + lastImageIDData.OriginalFileName;
                    }
                }

                var moduleFile = processes.GetOrCreateProcess(data.ProcessID, data.TimeStampQPC).LoadedModules.ImageLoadOrUnload(data, isLoad, fileName);
                // TODO review:  is using the timestamp the best way to make the association
                if (lastDbgData != null && data.TimeStampQPC == lastDbgData.TimeStampQPC)
                {
                    moduleFile.pdbName = lastDbgData.PdbFileName;
                    moduleFile.pdbSignature = lastDbgData.GuidSig;
                    moduleFile.pdbAge = lastDbgData.Age;
                    // There is no guarantee that the names of the DLL and PDB match, but they do 99% of the time
                    // We tolerate the exceptions, because it is a useful check most of the time
                    Debug.Assert(RoughDllPdbMatch(moduleFile.fileName, moduleFile.pdbName));
                }
                moduleFile.timeDateStamp = data.TimeDateStamp;
                moduleFile.imageChecksum = data.ImageChecksum;
                if (moduleFile.timeDateStamp == 0 && lastImageIDData != null && data.TimeStampQPC == lastImageIDData.TimeStampQPC)
                {
                    moduleFile.timeDateStamp = lastImageIDData.TimeDateStamp;
                }

                if (lastFileVersionData != null && data.TimeStampQPC == lastFileVersionData.TimeStampQPC)
                {
                    moduleFile.fileVersion = lastFileVersionData.FileVersion;
                    moduleFile.productVersion = lastFileVersionData.ProductVersion;
                    moduleFile.productName = lastFileVersionData.ProductName;
                }

                // Remember this ModuleFile because there can be Image* events after this with
                // the same timestamp that have information that we need to put  into it
                // (the logic above handles the case when those other events are first).
                lastTraceModuleFile = moduleFile;
                lastTraceModuleFileQPC = data.TimeStampQPC;
            };
            var symbolParser = new SymbolTraceEventParser(rawEvents);

            // Symbol parser events never have a stack (but will have a QPC associated with the imageLoad) so we want them ignored
            symbolParser.All += delegate (TraceEvent data) { noStack = true; };
            symbolParser.ImageIDDbgID_RSDS += delegate (DbgIDRSDSTraceData data)
            {
                hasPdbInfo = true;

                // The ImageIDDbgID_RSDS may be after the ImageLoad
                if (lastTraceModuleFile != null && lastTraceModuleFileQPC == data.TimeStampQPC && string.IsNullOrEmpty(lastTraceModuleFile.pdbName))
                {
                    lastTraceModuleFile.pdbName = data.PdbFileName;
                    lastTraceModuleFile.pdbSignature = data.GuidSig;
                    lastTraceModuleFile.pdbAge = data.Age;
                    // There is no guarantee that the names of the DLL and PDB match, but they do 99% of the time
                    // We tolerate the exceptions, because it is a useful check most of the time
                    Debug.Assert(RoughDllPdbMatch(lastTraceModuleFile.fileName, lastTraceModuleFile.pdbName));
                    lastDbgData = null;
                }
                else  // Or before (it is handled in ImageGroup callback above)
                {
                    lastDbgData = (DbgIDRSDSTraceData)data.Clone();
                }
            };
            symbolParser.ImageID += delegate (ImageIDTraceData data)
            {
                // The ImageID may be after the ImageLoad
                if (lastTraceModuleFile != null && lastTraceModuleFileQPC == data.TimeStampQPC && lastTraceModuleFile.timeDateStamp == 0)
                {
                    lastTraceModuleFile.timeDateStamp = data.TimeDateStamp;
                    lastImageIDData = null;
                }
                else  // Or before (it is handled in ImageGroup callback above)
                {
                    lastImageIDData = (ImageIDTraceData)data.Clone();
                }
            };
            symbolParser.ImageIDFileVersion += delegate (FileVersionTraceData data)
            {
                // The ImageIDFileVersion may be after the ImageLoad
                if (lastTraceModuleFile != null && lastTraceModuleFileQPC == data.TimeStampQPC && lastTraceModuleFile.fileVersion == null)
                {
                    lastTraceModuleFile.fileVersion = data.FileVersion;
                    lastTraceModuleFile.productVersion = data.ProductVersion;
                    lastTraceModuleFile.productName = data.ProductName;
                    lastFileVersionData = null;
                }
                else  // Or before (it is handled in ImageGroup callback above)
                {
                    lastFileVersionData = (FileVersionTraceData)data.Clone();
                }
            };

            kernelParser.AddCallbackForEvents<FileIONameTraceData>(delegate (FileIONameTraceData data)
                {
                    bookKeepingEvent = true;
                });

            rawEvents.Clr.LoaderModuleLoad += delegate (ModuleLoadUnloadTraceData data)
            {
                processes.GetOrCreateProcess(data.ProcessID, data.TimeStampQPC).LoadedModules.ManagedModuleLoadOrUnload(data, true, false);
            };
            rawEvents.Clr.LoaderModuleUnload += delegate (ModuleLoadUnloadTraceData data)
            {
                processes.GetOrCreateProcess(data.ProcessID, data.TimeStampQPC).LoadedModules.ManagedModuleLoadOrUnload(data, false, false);
            };
            rawEvents.Clr.LoaderModuleDCStopV2 += delegate (ModuleLoadUnloadTraceData data)
            {
                processes.GetOrCreateProcess(data.ProcessID, data.TimeStampQPC).LoadedModules.ManagedModuleLoadOrUnload(data, false, true);
            };

            var ClrRundownParser = new ClrRundownTraceEventParser(rawEvents);
            Action<ModuleLoadUnloadTraceData> onLoaderRundown = delegate (ModuleLoadUnloadTraceData data)
            {
                processes.GetOrCreateProcess(data.ProcessID, data.TimeStampQPC).LoadedModules.ManagedModuleLoadOrUnload(data, false, true);
            };

            ClrRundownParser.LoaderModuleDCStop += onLoaderRundown;
            ClrRundownParser.LoaderModuleDCStart += onLoaderRundown;

            Action<MethodLoadUnloadVerboseTraceData> onMethodStart = delegate (MethodLoadUnloadVerboseTraceData data)
                {
                    // We only capture data on unload, because we collect the addresses first.
                    if (!data.IsDynamic && !data.IsJitted)
                    {
                        bookKeepingEvent = true;
                    }

                    if ((int)data.ID == 139)       // MethodDCStartVerboseV2
                    {
                        bookKeepingEvent = true;
                    }

                    if (data.IsJitted)
                    {
                        TraceProcess process = processes.GetOrCreateProcess(data.ProcessID, data.TimeStampQPC);
                        process.InsertJITTEDMethod(data.MethodStartAddress, data.MethodSize, delegate ()
                        {
                            TraceManagedModule module = process.LoadedModules.GetOrCreateManagedModule(data.ModuleID, data.TimeStampQPC);
                            MethodIndex methodIndex = CodeAddresses.Methods.NewMethod(TraceLog.GetFullName(data), module.ModuleFile.ModuleFileIndex, data.MethodToken);
                            return new TraceProcess.MethodLookupInfo(data.MethodStartAddress, data.MethodSize, methodIndex);
                        });

                        jittedMethods.Add((MethodLoadUnloadVerboseTraceData)data.Clone());
                    }
                };
            rawEvents.Clr.MethodLoadVerbose += onMethodStart;
            rawEvents.Clr.MethodDCStartVerboseV2 += onMethodStart;
            ClrRundownParser.MethodDCStartVerbose += onMethodStart;

            rawEvents.Clr.MethodUnloadVerbose += delegate (MethodLoadUnloadVerboseTraceData data)
            {
                codeAddresses.AddMethod(data);
                if (!data.IsJitted)
                {
                    bookKeepingEvent = true;
                }
            };
            rawEvents.Clr.MethodILToNativeMap += delegate (MethodILToNativeMapTraceData data)
            {
                codeAddresses.AddILMapping(data);
                bookKeepingEvent = true;
            };

            ClrRundownParser.MethodILToNativeMapDCStop += delegate (MethodILToNativeMapTraceData data)
            {
                codeAddresses.AddILMapping(data);
                bookKeepingEvent = true;
            };


            Action<MethodLoadUnloadVerboseTraceData> onMethodDCStop = delegate (MethodLoadUnloadVerboseTraceData data)
            {
#if false // TODO this is a hack for VS traces that only did DCStarts but no DCStops.
                if (data.IsJitted && data.TimeStampRelativeMSec < 4000)
                {
                    jittedMethods.Add((MethodLoadUnloadVerboseTraceData)data.Clone());
                }
#endif

                codeAddresses.AddMethod(data);
                bookKeepingEvent = true;
            };

            rawEvents.Clr.MethodDCStopVerboseV2 += onMethodDCStop;
            ClrRundownParser.MethodDCStopVerbose += onMethodDCStop;

            var jScriptParser = new JScriptTraceEventParser(rawEvents);

            jScriptParser.AddCallbackForEvents<Microsoft.Diagnostics.Tracing.Parsers.JScript.SourceLoadUnloadTraceData>(
                delegate (Microsoft.Diagnostics.Tracing.Parsers.JScript.SourceLoadUnloadTraceData data)
                {
                    sourceFilesByID[new JavaScriptSourceKey(data.SourceID, data.ScriptContextID)] = data.Url;
                });

            Action<MethodLoadUnloadJSTraceData> onJScriptMethodUnload = delegate (MethodLoadUnloadJSTraceData data)
            {
                codeAddresses.AddMethod(data, sourceFilesByID);
                bookKeepingEvent = true;
            };
            jScriptParser.MethodRuntimeMethodUnload += onJScriptMethodUnload;
            jScriptParser.MethodRundownMethodDCStop += onJScriptMethodUnload;


            Action<MethodLoadUnloadJSTraceData> onJScriptMethodLoad = delegate (MethodLoadUnloadJSTraceData data)
            {
                TraceProcess process = processes.GetOrCreateProcess(data.ProcessID, data.TimeStampQPC);
                process.InsertJITTEDMethod(data.MethodStartAddress, (int)data.MethodSize, delegate ()
                {
                    MethodIndex methodIndex = CodeAddresses.MakeJavaScriptMethod(data, sourceFilesByID);
                    return new TraceProcess.MethodLookupInfo(data.MethodStartAddress, (int)data.MethodSize, methodIndex);
                });
                jsJittedMethods.Add((MethodLoadUnloadJSTraceData)data.Clone());
            };
            jScriptParser.MethodRuntimeMethodLoad += onJScriptMethodLoad;
            jScriptParser.MethodRundownMethodDCStart += onJScriptMethodLoad;

            // We know that Disk I/O events should never have a stack associated with them (the init events do)
            // these sometimes have the same kernel timestamp as CSWITCHs, which cause ambiguity.
            kernelParser.AddCallbackForEvents(delegate (DiskIOTraceData data)
            {
                noStack = true;
            });

            Action<ClrStackWalkTraceData> clrStackWalk = delegate (ClrStackWalkTraceData data)
            {
                bookKeepingEvent = true;

                // Avoid creating data structures for events we will throw away
                if (processingDisabled)
                {
                    return;
                }

                int i = 0;
                // Look for the previous CLR event on this same thread.
                for (PastEventInfoIndex prevEventIndex = pastEventInfo.CurrentIndex; ;)
                {
                    i++;
                    Debug.Assert(i < 20000);

                    prevEventIndex = pastEventInfo.GetPreviousEventIndex(prevEventIndex, data.ThreadID, true);
                    if (prevEventIndex == PastEventInfoIndex.Invalid)
                    {
                        DebugWarn(false, "Could not find a previous event for a CLR stack trace.", data);
                        return;
                    }
                    if (pastEventInfo.IsClrEvent(prevEventIndex))
                    {
                        if (pastEventInfo.HasStack(prevEventIndex))
                        {
                            DebugWarn(false, "CLR Stack trying to be given to same event twice (can happen with lost events)", data);
                            return;
                        }
                        pastEventInfo.SetHasStack(prevEventIndex);

                        var process = Processes.GetOrCreateProcess(data.ProcessID, data.TimeStampQPC);
                        thread = Threads.GetOrCreateThread(data.ThreadID, data.TimeStampQPC, process);

                        CallStackIndex callStackIndex = callStacks.GetStackIndexForStackEvent(
                            data.InstructionPointers, data.FrameCount, data.PointerSize, thread);
                        Debug.Assert(callStacks.Depth(callStackIndex) == data.FrameCount);
                        DebugWarn(pastEventInfo.GetThreadID(prevEventIndex) == data.ThreadID, "Mismatched thread for CLR Stack Trace", data);

                        // Get the previous event on the same thread.
                        EventIndex eventIndex = pastEventInfo.GetEventIndex(prevEventIndex);
                        Debug.Assert(eventIndex != EventIndex.Invalid); // We don't delete CLR events and that is the only way eventIndexes can be invalid
                        AddStackToEvent(eventIndex, callStackIndex);
                        pastEventInfo.GetEventCounts(prevEventIndex).m_stackCount++;
                        return;
                    }
                }
            };
            rawEvents.Clr.ClrStackWalk += clrStackWalk;

            // Process stack trace from EventPipe trace
            Action<ClrThreadStackWalkTraceData> clrThreadStackWalk = delegate (ClrThreadStackWalkTraceData data)
            {
                bookKeepingEvent = true;

                // Avoid creating data structures for events we will throw away
                if (processingDisabled)
                {
                    return;
                }

                PastEventInfoIndex prevEventIndex = pastEventInfo.GetPreviousEventIndex(pastEventInfo.CurrentIndex, data.ThreadID, true);

                if (prevEventIndex == PastEventInfoIndex.Invalid)
                {
                    DebugWarn(false, "Could not find a previous event for a CLR thread stack trace.", data);
                    return;
                }

                var process = Processes.GetOrCreateProcess(data.ProcessID, data.TimeStampQPC);
                thread = Threads.GetOrCreateThread(data.ThreadID, data.TimeStampQPC, process);

                CallStackIndex callStackIndex = callStacks.GetStackIndexForStackEvent(
                    data.InstructionPointers, data.FrameCount, data.PointerSize, thread);
                Debug.Assert(callStacks.Depth(callStackIndex) == data.FrameCount);

                // Get the previous event and add stack
                EventIndex eventIndex = pastEventInfo.GetEventIndex(prevEventIndex);
                AddStackToEvent(eventIndex, callStackIndex);
                pastEventInfo.GetEventCounts(prevEventIndex).m_stackCount++;

                return;
            };
            var eventPipeParser = new SampleProfilerTraceEventParser(rawEvents);
            eventPipeParser.ThreadStackWalk += clrThreadStackWalk;

            var clrPrivate = new ClrPrivateTraceEventParser(rawEvents);
            clrPrivate.ClrStackWalk += clrStackWalk;
            kernelParser.StackWalkStack += delegate (StackWalkStackTraceData data)
            {
                bookKeepingEvent = true;
                if (processingDisabled)
                {
                    return;
                }
                // Trace.WriteLine("REAL TIME QUEUE: *** STACK EVENT *** " + data.TimeStampRelativeMSec.ToString("f3") + " for event at " + data.EventTimeStampRelativeMSec.ToString("f3"));

                var timeStampQPC = data.TimeStampQPC;
                IncompleteStack stackInfo = GetIncompleteStackForStackEvent(data, data.EventTimeStampQPC);
                TraceProcess process = processes.GetOrCreateProcess(data.ProcessID, timeStampQPC);
                thread = Threads.GetOrCreateThread(data.ThreadID, timeStampQPC, process);
                var isKernelModeStackFragment = IsKernelAddress(data.InstructionPointer(data.FrameCount - 1), data.PointerSize);
                if (isKernelModeStackFragment)
                {
                    // If we reach here the fragment we have is totally in the kernel, and thus might have a user mode part that we have
                    // not seen yet.  Thus we have the stackInfo remember this fragment so we can put it together later.
                    if (stackInfo != null)
                    {
                        if (!stackInfo.LogKernelStackFragment(data.InstructionPointers, data.FrameCount, data.PointerSize, timeStampQPC, this))
                        {
                            stackInfo.AddEntryToThread(ref thread.lastEntryIntoKernel);    // If not done remember to complete it
                        }
                    }
                }
                else
                {
                    // If we reach here, the fragment ends in user mode.
                    CallStackIndex stackIndex = callStacks.GetStackIndexForStackEvent(
                        data.InstructionPointers, data.FrameCount, data.PointerSize, thread, CallStackIndex.Invalid);

                    var lastEmitStackOnExitFromKernelQPC = thread.lastEmitStackOnExitFromKernelQPC;
                    var loggedUserStack = false;    // Have we logged this stack at all
                    // If this fragment starts in user mode, then we assume that it is on the 'boundary' of kernel and users mode
                    // and we use this as the 'top' of the stack for all kernel fragments on this thread.
                    if (!IsKernelAddress(data.InstructionPointer(0), data.PointerSize))
                    {
                        loggedUserStack = EmitStackOnExitFromKernel(ref thread.lastEntryIntoKernel, stackIndex, stackInfo);
                        thread.lastEmitStackOnExitFromKernelQPC = data.TimeStampQPC;
                    }

                    // If we have not logged the stack of the code above, then log it as a stand alone user stack.
                    // We don't do this for events that have already been processed by and EmitStackOnExitFromKernelQPC
                    if (!loggedUserStack && stackInfo != null)
                    {
                        if (data.EventTimeStampQPC < lastEmitStackOnExitFromKernelQPC)
                        {
                            DebugWarn(false, "Warning: Trying to attach a user stack to a stack already processed by EmitStackOnExitFromKernel.  Ignoring data", data);
                        }
                        else
                        {
                            stackInfo.LogUserStackFragment(stackIndex, this);
                        }
                    }
                }
            };

            kernelParser.StackWalkStackKeyKernel += delegate (StackWalkRefTraceData data)
            {
                bookKeepingEvent = true;
                if (processingDisabled)
                {
                    return;
                }

                IncompleteStack stackInfo = GetIncompleteStackForStackEvent(data, data.EventTimeStampQPC);
                if (stackInfo != null)
                {
                    var timeStampQPC = data.TimeStampQPC;
                    TraceProcess process = processes.GetOrCreateProcess(data.ProcessID, timeStampQPC);
                    thread = Threads.GetOrCreateThread(data.ThreadID, timeStampQPC, process);

                    if (!stackInfo.LogKernelStackFragment(data.StackKey, this))
                    {
                        stackInfo.AddEntryToThread(ref thread.lastEntryIntoKernel);    // If not done remember to complete it
                    }
                }
            };

            kernelParser.StackWalkStackKeyUser += delegate (StackWalkRefTraceData data)
            {
                bookKeepingEvent = true;
                if (processingDisabled)
                {
                    return;
                }

                IncompleteStack stackInfo = GetIncompleteStackForStackEvent(data, data.EventTimeStampQPC);
                if (stackInfo != null)
                {
                    var timeStampQPC = data.TimeStampQPC;
                    TraceProcess process = processes.GetOrCreateProcess(data.ProcessID, timeStampQPC);
                    thread = Threads.GetOrCreateThread(data.ThreadID, timeStampQPC, process);
                    if (!EmitStackOnExitFromKernel(ref thread.lastEntryIntoKernel, data.StackKey, stackInfo))
                    {
                        stackInfo.LogUserStackFragment(data.StackKey, this);
                    }
                }
            };

            // Matches Delete and Rundown events;
            kernelParser.AddCallbackForEvents<StackWalkDefTraceData>(delegate (StackWalkDefTraceData data)
            {
                bookKeepingEvent = true;
                LogStackDefinition(data);
            });

            // The following 3 callbacks for a small state machine to determine whether the process
            // is running server GC and what the server GC threads are.
            // We assume we are server GC if there are more than one thread doing the 'MarkHandles' event
            // during a GC, and the threads that do that are the server threads.  We use this to mark the
            // threads as Server GC Threads.
            rawEvents.Clr.GCStart += delegate (GCStartTraceData data)
            {
                var process = Processes.GetProcess(data.ProcessID, data.TimeStampQPC);
                if (process == null)
                {
                    return;
                }

                if ((process.markThreadsInGC.Count == 0) && (process.shouldCheckIsServerGC == false))
                {
                    process.shouldCheckIsServerGC = true;
                }
            };
            rawEvents.Clr.GCStop += delegate (GCEndTraceData data)
            {
                var process = Processes.GetProcess(data.ProcessID, data.TimeStampQPC);
                if (process == null)
                {
                    return;
                }

                if (process.markThreadsInGC.Count > 0)
                {
                    process.shouldCheckIsServerGC = false;
                }

                if (!process.isServerGC && (process.markThreadsInGC.Count > 1))
                {
                    process.isServerGC = true;
                    foreach (var curThread in process.Threads)
                    {
                        if (thread.threadInfo == null && process.markThreadsInGC.ContainsKey(curThread.ThreadID))
                        {
                            curThread.threadInfo = ".NET Server GC Thread(" + process.markThreadsInGC[curThread.ThreadID] + ")";
                        }
                    }
                }
            };
            rawEvents.Clr.GCMarkWithType += delegate (GCMarkWithTypeTraceData data)
            {
                if (data.Type == (int)MarkRootType.MarkHandles)
                {
                    AddMarkThread(data.ThreadID, data.TimeStampQPC, data.HeapNum);
                }
            };
            clrPrivate.GCMarkHandles += delegate (GCMarkTraceData data)
            {
                AddMarkThread(data.ThreadID, data.TimeStampQPC, data.HeapNum);
            };

            var aspNetParser = new AspNetTraceEventParser(rawEvents);
            aspNetParser.AspNetReqStart += delegate (AspNetStartTraceData data) { CategorizeThread(data, "Incoming Request Thread"); };
            rawEvents.Clr.GCFinalizersStart += delegate (GCNoUserDataTraceData data) { CategorizeThread(data, ".NET Finalizer Thread"); };
            rawEvents.Clr.GCFinalizersStop += delegate (GCFinalizersEndTraceData data) { CategorizeThread(data, ".NET Finalizer Thread"); };
            Action<TraceEvent> MarkAsBGCThread = delegate (TraceEvent data)
            {
                var process = Processes.GetOrCreateProcess(data.ProcessID, data.TimeStampQPC);
                thread = Threads.GetOrCreateThread(data.ThreadID, data.TimeStampQPC, process);
                bool isServerGC = (thread != null && thread.process.isServerGC);
                CategorizeThread(data, ".NET Background GC Thread");
            };

            // We use more than then GCBGStart to mark a GC thread because we need an event that happens more routinely
            // since this might be a circular buffer or other short trace.
            clrPrivate.GCBGCStart += delegate (GCNoUserDataTraceData data) { MarkAsBGCThread(data); };
            clrPrivate.GCBGC1stConStop += delegate (GCNoUserDataTraceData data) { MarkAsBGCThread(data); };
            clrPrivate.GCBGCDrainMark += delegate (BGCDrainMarkTraceData data) { MarkAsBGCThread(data); };
            clrPrivate.GCBGCRevisit += delegate (BGCRevisitTraceData data) { MarkAsBGCThread(data); };
            rawEvents.Clr.ThreadPoolWorkerThreadAdjustmentSample += delegate (ThreadPoolWorkerThreadAdjustmentSampleTraceData data)
            {
                CategorizeThread(data, ".NET ThreadPool");
            };
            rawEvents.Clr.ThreadPoolIODequeue += delegate (ThreadPoolIOWorkTraceData data) { CategorizeThread(data, ".NET IO ThreadPool Worker", true); };

            var fxParser = new FrameworkEventSourceTraceEventParser(rawEvents);
            fxParser.ThreadPoolDequeueWork += delegate (ThreadPoolDequeueWorkArgs data) { CategorizeThread(data, ".NET ThreadPool Worker"); };
            fxParser.ThreadTransferReceive += delegate (ThreadTransferReceiveArgs data) { CategorizeThread(data, ".NET ThreadPool Worker"); };

            // Attribute CPU samples to processes.
            kernelParser.PerfInfoSample += delegate (SampledProfileTraceData data)
            {
                if (data.ThreadID == 0 && !data.NonProcess && !(options != null && options.KeepAllEvents))    // Don't count process 0 (idle) unless they are executing DPCs or ISRs.
                {
                    removeFromStream = true;
                    return;
                }

                var process = Processes.GetOrCreateProcess(data.ProcessID, data.TimeStampQPC);
                thread = Threads.GetOrCreateThread(data.ThreadID, data.TimeStampQPC, process);
                thread.cpuSamples++;
            };

            // We assume that the sampling interval is uniform over the trace.   We pick the start if it
            // is there, otherwise the OLD value of the LAST set interval (since we RESET the interval at the end)
            // OR the OLD value at the end.
            bool setSeen = false;
            bool startSeen = false;

            kernelParser.PerfInfoCollectionStart += delegate (SampledProfileIntervalTraceData data)
            {
                if (data.SampleSource != 0)     // 0 is the CPU sampling interval
                {
                    return;
                }

                startSeen = true;
                sampleProfileInterval100ns = data.NewInterval;
            };

            kernelParser.PerfInfoSetInterval += delegate (SampledProfileIntervalTraceData data)
            {
                if (data.SampleSource != 0)     // 0 is the CPU sampling interval
                {
                    return;
                }

                setSeen = true;
                if (!startSeen)
                {
                    sampleProfileInterval100ns = data.OldInterval;
                }
            };

            kernelParser.PerfInfoSetInterval += delegate (SampledProfileIntervalTraceData data)
            {
                if (data.SampleSource != 0)     // 0 is the CPU sampling interval
                {
                    return;
                }

                if (!setSeen && !startSeen)
                {
                    sampleProfileInterval100ns = data.OldInterval;
                }
            };
        }

        /// <summary>
        ///  Copies the events from the 'rawEvents' dispatcher to the output stream 'IStreamWriter'.  It
        ///  also creates auxiliary data structures associated with the raw events (eg, processes, threads,
        ///  modules, address lookup maps...  Basically any information that needs to be determined by
        ///  scanning over the events during TraceLog creation should hook in here.
        /// </summary>
        private unsafe void CopyRawEvents(TraceEventDispatcher rawEvents, IStreamWriter writer)
        {
            SetupCallbacks(rawEvents);

            // Fix up MemInfoWS records so that we get one per process rather than one per machine
            rawEvents.Kernel.MemoryProcessMemInfo += delegate (MemoryProcessMemInfoTraceData data)
            {
                if (!processingDisabled)
                {
                    GenerateMemInfoRecordsPerProcess(data, writer);
                }
            };

            const int defaultMaxEventCount = 20000000;                   // 20M events produces about 3GB of data.  which is close to the limit of ETLX.
            int maxEventCount = defaultMaxEventCount;
            double startMSec = 0;
            if (options != null)
            {
                if (options.SkipMSec != 0)
                {
                    options.ConversionLog.WriteLine("Skipping the {0:n3} MSec of the trace.", options.SkipMSec);
                    processingDisabled = true;
                    startMSec = options.SkipMSec;
                }
                if (options.MaxEventCount >= 1000)      // Numbers smaller than this are almost certainly errors
                {
                    maxEventCount = options.MaxEventCount;
                }
                else if (options.MaxEventCount != 0)
                {
                    options.ConversionLog.WriteLine("MaxEventCount {0} < 1000, assumed in error, ignoring", options.MaxEventCount);
                }
            }
            options.ConversionLog.WriteLine("Collecting a maximum of {0:n0} events.", maxEventCount);

            uint rawEventCount = 0;
            double rawInputSizeMB = rawEvents.Size / 1000000.0;
            var startTime = DateTime.Now;
            long lastQPCEventTime = long.MinValue;     // We want the times to be ordered.
#if DEBUG
            long lastTimeStamp = 0;
#endif

            // While scanning over the stream, copy all data to the file.
            rawEvents.AllEvents += delegate (TraceEvent data)
            {
                Debug.Assert(_syncTimeQPC != 0);         // We should have set this in the Header event (or on session start if it is read time
#if DEBUG
                Debug.Assert(lastTimeStamp <= data.TimeStampQPC);     // Insure they are in order
                lastTimeStamp = data.TimeStampQPC;
#endif
                // Show status every 128K events
                if ((rawEventCount & 0x1FFFF) == 0)
                {
                    var curOutputSizeMB = ((double)(uint)writer.GetLabel()) / 1000000.0;
                    // Currently ETLX has a size restriction of 4Gig.  Thus if we are getting big, start truncating.
                    if (curOutputSizeMB > 3500)
                    {
                        processingDisabled = true;
                    }

                    if (options != null && options.ConversionLog != null)
                    {
                        if (rawEventCount == 0)
                        {
                            options.ConversionLog.WriteLine("[Opening a log file of size {0:n0} MB.]",
                                rawInputSizeMB);
                        }
                        else
                        {
                            var curDurationSec = (DateTime.Now - startTime).TotalSeconds;

                            var ratioOutputToInput = (double)eventCount / (double)rawEventCount;
                            var estimatedFinalSizeMB = Math.Max(rawInputSizeMB * ratioOutputToInput * 1.15, curOutputSizeMB * 1.02);
                            var ratioSizeComplete = curOutputSizeMB / estimatedFinalSizeMB;
                            var estTimeLeftSec = (int)(curDurationSec / ratioSizeComplete - curDurationSec);

                            var message = "";
                            if (0 < startMSec && data.TimeStampRelativeMSec < startMSec)
                            {
                                message = "  Before StartMSec truncating";
                            }
                            else if (eventCount >= maxEventCount)
                            {
                                message = "  Hit MaxEventCount, truncating.";
                            }
                            else if (curOutputSizeMB > 3500)
                            {
                                message = "  Hit File size limit (3.5Gig) truncating.";
                            }

                            options.ConversionLog.WriteLine(
                                "[Sec {0,4:f0} Read {1,10:n0} events. At {2,7:n0}ms.  Wrote {3,4:f0}MB ({4,3:f0}%).  EstDone {5,2:f0} min {6,2:f0} sec.{7}]",
                                curDurationSec,
                                rawEventCount,
                                data.TimeStampRelativeMSec,
                                curOutputSizeMB,
                                ratioSizeComplete * 100.0,
                                estTimeLeftSec / 60,
                                estTimeLeftSec % 60,
                                message);
                        }
                    }
                }
                rawEventCount++;
#if DEBUG
                if (data is UnhandledTraceEvent)
                {
                    Debug.Assert((byte)data.opcode != unchecked((byte)-1));        // Means PrepForCallback not done.
                    Debug.Assert(data.TaskName != "ERRORTASK");
                    Debug.Assert(data.OpcodeName != "ERROROPCODE");
                }
#endif
                if (processingDisabled)
                {
                    if (startMSec != 0 && startMSec <= data.TimeStampRelativeMSec)
                    {
                        startMSec = 0;                  // Marking it 0 indicates that we have triggered on it already.
                        processingDisabled = false;
                    }
                    return;
                }
                else
                {
                    if (maxEventCount <= eventCount)
                    {
                        processingDisabled = true;
                    }
                }
                // Sadly we have seen cases of merged ETL files where there are events past the end of the session.
                // This confuses later logic so insure that this does not happen.  Note that we also want the
                // any module-DCStops to happen at sessionEndTime so we have to do this after processing all events
                if (data.TimeStampQPC > sessionEndTimeQPC)
                {
                    sessionEndTimeQPC = data.TimeStampQPC;
                }

                if (data.TimeStampQPC < lastQPCEventTime)
                {
                    options.ConversionLog.WriteLine("WARNING, events out of order! This breaks event search.  Jumping from {0:n3} back to {1:n3} for {2} EventID {3} Thread {4}",
                        QPCTimeToRelMSec(lastQPCEventTime), data.TimeStampRelativeMSec, data.ProviderName, data.ID, data.ThreadID);
                    firstTimeInversion = (EventIndex) (uint) eventCount;
                }

                lastQPCEventTime = data.TimeStampQPC;

                // Update the counts
                var countForEvent = stats.GetEventCounts(data);
                countForEvent.m_count++;
                countForEvent.m_eventDataLenTotal += data.EventDataLength;

                var extendedDataCount = data.eventRecord->ExtendedDataCount;
                if (extendedDataCount != 0)
                {
                    bookKeepingEvent |= ProcessExtendedData(data, extendedDataCount, countForEvent);
                }

                if (bookKeepingEvent)
                {
                    bookKeepingEvent = false;
                    if (bookeepingEventThatMayHaveStack)
                    {
                        // We log the event so that we don't get spurious warnings about not finding the event for a stack,
                        // but we mark the EventIndex as invalid so that we know not to actually log this stack.
                        pastEventInfo.LogEvent(data, EventIndex.Invalid, countForEvent);
                        bookeepingEventThatMayHaveStack = false;
                    }
                    // But unless the user explicitly asked for them, we remove them from the trace.
                    if (!options.KeepAllEvents)
                    {
                        return;
                    }
                }
                else
                {
                    // Remember the event (to attach latter Stack Events) and also log event counts in TraceStats
                    if (!noStack)
                    {
                        pastEventInfo.LogEvent(data, removeFromStream ? EventIndex.Invalid : ((EventIndex)eventCount), countForEvent);
                    }
                    else
                    {
                        noStack = false;
                    }

                    if (removeFromStream)
                    {
                        removeFromStream = false;
                        if (!options.KeepAllEvents)
                        {
                            return;
                        }
                    }
                    else // Remember any code address in the event.
                    {
                        data.LogCodeAddresses(fnAddAddressToCodeAddressMap);
                    }
                }
                // We want all events to have a TraceProcess and TraceThread.
                // We force this to happen here.  We may have created a thread already, in which
                // case the 'thread' instance variable will hold it.  Use that if it is accurate.
                // Otherwise make a new one here.
                if (thread == null || thread.ThreadID != data.ThreadID && data.ProcessID != -1)
                {
                    TraceProcess process = processes.GetOrCreateProcess(data.ProcessID, data.TimeStampQPC);
                    if (data.ThreadID != -1)
                    {
                        // All Thread events should already be handled (since we are passing the wrong args for those here).
                        Debug.Assert(!(data is ThreadTraceData));
                        thread = Threads.GetOrCreateThread(data.ThreadID, data.TimeStampQPC, process);
                    }
                }

                if (numberOnPage >= eventsPerPage)
                {
                    // options.ConversionLog.WriteLine("Writing page " + this.eventPages.BatchCount, " Start " + writer.GetLabel());
                    eventPages.Add(new EventPageEntry(data.TimeStampQPC, writer.GetLabel()));
                    numberOnPage = 0;
                }
                unsafe
                {
                    Debug.Assert(data.eventRecord->EventHeader.TimeStamp < long.MaxValue);
                    WriteBlob((IntPtr)data.eventRecord, writer, headerSize);
                    WriteBlob(data.userData, writer, (data.EventDataLength + 3 & ~3));
                }
                numberOnPage++;
                eventCount++;
            };

#if DEBUG
            // This is a guard against code running in TraceLog.CopyRawEvents that attempts to use
            // the EventIndex for an event returned by ETWTraceEventSource. It is unsafe to do so
            // because the EventIndex returned represents the index in the ETW stream, but user
            // code needs the index in the newly created ETLX stream (which does not include
            // "bookkeeping" events. User code should use the TraceLog.EventCount instead (the
            // way TraceLog.ProcessExtendedData and Activities.HandleActivityCreation do)
            var rawEtwEvents = rawEvents as ETWTraceEventSource;
            if (rawEtwEvents != null)
            {
                rawEtwEvents.DisallowEventIndexAccess = true;
            }
#endif
            try
            {
                rawEvents.Process();                  // Run over the data.
            }
            catch (Exception e)
            {
                options.ConversionLog.WriteLine("[ERROR: processing events ****]");
                if (options.ContinueOnError)
                {
                    options.ConversionLog.WriteLine("***** The following Exception was thrown during processing *****");
                    options.ConversionLog.WriteLine(e.ToString());
                    options.ConversionLog.WriteLine("***** However ContinueOnError is set, so we continue processing  what we have *****");
                    options.ConversionLog.WriteLine("Continuing Processing...");
                }
                else
                {
                    options.ConversionLog.WriteLine("***** Consider using /ContinueOnError to ignore the bad part of the trace.  *****");
                    throw;
                }
            }
#if DEBUG
            if (rawEtwEvents != null)
            {
                rawEtwEvents.DisallowEventIndexAccess = false;
            }
#endif

            // EventPipe doesn't set EventsLost until after Process is called.
            if(rawEvents is EventPipeEventSource)
            {
                eventsLost = rawEvents.EventsLost;
            }

            if (eventCount >= maxEventCount)
            {
                if (options != null && options.ConversionLog != null)
                {
                    if (options.OnLostEvents != null)
                    {
                        options.OnLostEvents(true, EventsLost, eventCount);
                    }

                    options.ConversionLog.WriteLine("Truncated events to {0:n} events.  Use /MaxEventCount to change.", maxEventCount);
                    options.ConversionLog.WriteLine("However  is a hard limit of 4GB of of processed (ETLX) data, increasing it over 15M will probably hit that.");
                    options.ConversionLog.WriteLine("Instead you can use /SkipMSec:X to skip the beginning events and thus see the next window of /MaxEventCount the file.");
                }
            }

            freeEventStackInfos = null;
            pastEventInfo.Dispose();
            if (kernelStackKeyToInfo.Count != 0)
            {
                DebugWarn(false, "Warning: " + kernelStackKeyToInfo.Count + " undefined kernel stacks at the end of the trace.", null);
            }

            kernelStackKeyToInfo = null;
            if (userStackKeyToInfo.Count != 0)
            {
                DebugWarn(false, "Warning: " + userStackKeyToInfo.Count + " undefined user stacks at the end of the trace.", null);
            }

            userStackKeyToInfo = null;

            // TODO FIX NOW hack because unloadMethod not present
            foreach (var jittedMethod in jittedMethods)
            {
                codeAddresses.AddMethod(jittedMethod);
            }

            foreach (var jsJittedMethod in jsJittedMethods)
            {
                codeAddresses.AddMethod(jsJittedMethod, sourceFilesByID);
            }

            // Make sure that all threads have a process
            foreach (var curThread in Threads)
            {
                // Finish off the processing of the ETW compressed stacks.  This means doing all the deferred Kernel stack processing
                // and connecting all pseudo-callStack indexes into real ones.
                if (curThread.lastEntryIntoKernel != null)
                {
                    EmitStackOnExitFromKernel(ref curThread.lastEntryIntoKernel, TraceCallStacks.GetRootForThread(curThread.ThreadIndex), null);
                }

                if (curThread.process == null)
                {
                    DebugWarn(true, "Warning: could not determine the process for thread " + curThread.ThreadID, null);
                    var unknownProcess = Processes.GetOrCreateProcess(-1, 0);
                    unknownProcess.imageFileName = "UNKNOWN_PROCESS";
                    curThread.process = unknownProcess;
                }
                curThread.Process.cpuSamples += curThread.cpuSamples;         // Roll up CPU to the process.
            }

            // Make sure we are not missing any ImageEnds that we have ImageStarts for.
            foreach (var process in Processes)
            {
                foreach (var module in process.LoadedModules)
                {
                    // We did not unload the module
                    if (module.unloadTimeQPC == long.MaxValue && module.ImageBase != 0)
                    {
                        // simulate a module unload, and resolve all code addresses in the module's range.
                        CodeAddresses.ForAllUnresolvedCodeAddressesInRange(process, module.ImageBase, module.ModuleFile.ImageSize, false, delegate (ref TraceCodeAddresses.CodeAddressInfo info)
                        {
                            info.SetModuleFileIndex(module.ModuleFile);
                        });
                    }
                    if (module.unloadTimeQPC > sessionEndTimeQPC)
                    {
                        module.unloadTimeQPC = sessionEndTimeQPC;
                    }
                }

                if (process.endTimeQPC > sessionEndTimeQPC)
                {
                    process.endTimeQPC = sessionEndTimeQPC;
                }

                if (options != null && options.ConversionLog != null)
                {
                    if (process.unresolvedCodeAddresses.Count > 0)
                    {
                        options.ConversionLog.WriteLine("There were {0} address that did not resolve to a module or method in process {1} ({2})",
                            process.unresolvedCodeAddresses.Count, process.Name, process.ProcessID);
                        //options.ConversionLog.WriteLine(process.unresolvedCodeAddresses.Foreach(x => CodeAddresses.Address(x).ToString("x")));
                    }
                }

                // We are done with these data structures.
                process.codeAddressesInProcess = null;
                process.unresolvedCodeAddresses.Clear();
                // Link up all the 'Parent' fields of the process.
                process.SetParentForProcess();
            }

#if DEBUG
            // Confirm that there are no infinite chains (we guarantee this for sanity).
            foreach (var process in Processes)
            {
                Debug.Assert(process.ParentDepth() < Processes.Count);
            }
#endif

            // Sum up the module level statistics for code addresses.
            for (int codeAddrIdx = 0; codeAddrIdx < CodeAddresses.Count; codeAddrIdx++)
            {
                var inclusiveCount = CodeAddresses.codeAddresses[codeAddrIdx].InclusiveCount;

                var moduleIdx = CodeAddresses.ModuleFileIndex((CodeAddressIndex)codeAddrIdx);
                if (moduleIdx != ModuleFileIndex.Invalid)
                {
                    var module = CodeAddresses.ModuleFiles[moduleIdx];
                    module.codeAddressesInModule += inclusiveCount;
                }
                CodeAddresses.totalCodeAddresses += inclusiveCount;
            }

            // Insure the event to stack table is in sorted order.
            eventsToStacks.Sort(delegate (EventsToStackIndex x, EventsToStackIndex y)
            {
                return (int)x.EventIndex - (int)y.EventIndex;
            });
            cswitchBlockingEventsToStacks.Sort(delegate (EventsToStackIndex x, EventsToStackIndex y)
            {
                return (int)x.EventIndex - (int)y.EventIndex;
            });

#if DEBUG
            // Confirm that the CPU stats make sense.
            foreach (var process in Processes)
            {
                float cpuFromThreads = 0;
                foreach (var curThread in process.Threads)
                {
                    cpuFromThreads += curThread.CPUMSec;
                }

                Debug.Assert(Math.Abs(cpuFromThreads - process.CPUMSec) < .01);     // We add up
            }

            // The eventsToStacks array is sorted.
            // We sort this array above, so this should only fail if we have EQUAL EventIndex.
            // This means we tried to add two stacks to an event  (we should not do that).
            // See the asserts in AddStackToEvent for more.
            for (int i = 0; i < eventsToStacks.Count - 1; i++)
            {
                Debug.Assert(eventsToStacks[i].EventIndex < eventsToStacks[i + 1].EventIndex);
            }
#endif

            Debug.Assert(eventCount % eventsPerPage == numberOnPage || numberOnPage == eventsPerPage || eventCount == 0);
            options.ConversionLog.WriteLine("{0} distinct processes.", processes.Count);
            options.ConversionLog.WriteLine("Totals");
            options.ConversionLog.WriteLine("  {0,8:n0} events.", eventCount);
            options.ConversionLog.WriteLine("  {0,8:n0} events with stack traces.", eventsToStacks.Count);
            options.ConversionLog.WriteLine("  {0,8:n0} events with code addresses in them.", eventsToCodeAddresses.Count);
            options.ConversionLog.WriteLine("  {0,8:n0} total code address instances. (stacks or other)", codeAddresses.TotalCodeAddresses);
            options.ConversionLog.WriteLine("  {0,8:n0} unique code addresses. ", codeAddresses.Count);
            options.ConversionLog.WriteLine("  {0,8:n0} unique stacks.", callStacks.Count);
            options.ConversionLog.WriteLine("  {0,8:n0} unique managed methods parsed.", codeAddresses.Methods.Count);
            options.ConversionLog.WriteLine("  {0,8:n0} CLR method event records.", codeAddresses.ManagedMethodRecordCount);
            options.ConversionLog.WriteLine("[Conversion complete {0:n0} events.  Conversion took {1:n0} sec.]",
                eventCount, (DateTime.Now - startTime).TotalSeconds);
        }

        // Pdbs and DLLs often 'match'.   Use this to ensure we have hooked
        // up the PDB to the DLL correctly.    This is heurisitc and only used
        // in testing.
        private static bool RoughDllPdbMatch(string dllPath, string pdbPath)
        {
#if DEBUG
            string dllName = Path.GetFileNameWithoutExtension(dllPath);
            string pdbName = Path.GetFileNameWithoutExtension(pdbPath);

            // Give up on things outside the kernel or visual Studio.  There is just too much variability out there.
            if (!dllName.StartsWith(@"C:\Windows", StringComparison.OrdinalIgnoreCase) || 0 <= dllName.IndexOf("Visual Studio", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Exceptions to the rule below
            if (0 <= dllName.IndexOf("krnl", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // People often rename things but the keep the prefix in the PDB name.
            if (dllName.Length > 5)
            {
                dllName = dllName.Substring(0, 5);
            }

            if (0 <= pdbName.IndexOf(dllName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
#endif
            return false;
        }

        /// <summary>
        /// This is a helper routine that adds the address 'address' in the event 'data' to the map from events
        /// to this list of addresses.
        /// </summary>
        private bool AddAddressToCodeAddressMap(TraceEvent data, Address address)
        {
            TraceProcess process = Processes.GetOrCreateProcess(data.ProcessID, data.TimeStampQPC);
            CodeAddressIndex codeAddressIndex = codeAddresses.GetOrCreateCodeAddressIndex(process, address);

            // I require that the list be sorted by event ID.
            Debug.Assert(eventsToCodeAddresses.Count == 0 ||
                eventsToCodeAddresses[eventsToCodeAddresses.Count - 1].EventIndex <= (EventIndex)eventCount);

            eventsToCodeAddresses.Add(new EventsToCodeAddressIndex(MaxEventIndex, address, codeAddressIndex));
            return true;
        }

        /// <summary>
        /// Special logic to form MemInfoWSTraceData.   We take the single event (which has
        /// The working sets for every process in the system, an split them out into N events
        /// each of which has the processID for the event set properly, and only has the
        /// information for that process.    The first 3 processes in the list are -1, -2, and -3
        /// that have special meaning.
        /// </summary>
        private unsafe void GenerateMemInfoRecordsPerProcess(MemoryProcessMemInfoTraceData data, IStreamWriter writer)
        {
            // This is a bit of hack, we update the eventRecord in place, so save the original values
            int originalProcessId = data.eventRecord->EventHeader.ProcessId;
            ushort originalUserDataLength = data.eventRecord->UserDataLength;

            // change the size so that the array of information is exactly 1 count in size.
            data.eventRecord->UserDataLength = (ushort)(data.ElementSize + 4);
            int newCount = 1;
            // For every MemInfoWSData (that is for every process)
            for (int i = 0; i < data.Count; i++)
            {
                var proc = data.Values(i);

                // For now we skip the special process IDs since they are not real processes.
                if (proc.ProcessID < 0)
                {
                    continue;
                }

                data.eventRecord->EventHeader.ProcessId = proc.ProcessID;       // correct the process ID

                // This is part of writing an event, make sure the eventPages is kept up to date.
                if (numberOnPage >= eventsPerPage)
                {
                    eventPages.Add(new EventPageEntry(data.TimeStampQPC, writer.GetLabel()));
                    numberOnPage = 0;
                }

                // Write out the update event record
                WriteBlob((IntPtr)data.eventRecord, writer, headerSize);
                // And the count (which is always 1
                WriteBlob((IntPtr)(&newCount), writer, 4);
                // And the MemInfoWSData structure (might be 32 bit or 64 bit)
                WriteBlob(proc.RawData, writer, data.eventRecord->UserDataLength - 4);
                numberOnPage++;
                eventCount++;
            }
            // Restore the original record.
            data.eventRecord->UserDataLength = originalUserDataLength;
            data.eventRecord->EventHeader.ProcessId = originalProcessId;
        }

        private int m_orphanedStacks;
        /// <summary>
        /// Given just the stack event and the timestamp for the event the stack event is to attach to, find
        /// the IncompleteStack for the event.   If the event to attach to cannot be this will return null
        /// but otherwise it will make an IncompleteStack entry if one does not already exist or it.
        ///
        /// As part of allocating an Incomplete stack, it will increment the stack counts for target event.
        /// </summary>
        private IncompleteStack GetIncompleteStackForStackEvent(TraceEvent stackEvent, long eventTimeStampQPC)
        {
#if DEBUG
            double prevEventRelMSec = QPCTimeToRelMSec(eventTimeStampQPC);
#endif
            PastEventInfoIndex pastEventIndex = pastEventInfo.GetBestEventForQPC(eventTimeStampQPC, stackEvent.ThreadID, stackEvent.ProcessorNumber);
            if (pastEventIndex == PastEventInfoIndex.Invalid)
            {
                m_orphanedStacks++;
                if (m_orphanedStacks < 1000)
                {
                    // We don't warn if the time is too close to the start of the file
                    // We also don't report ThreadID because we do throw those out purposefully to safe space.
                    DebugWarn(stackEvent.TimeStampRelativeMSec < 100 || stackEvent.ThreadID == 0, "Stack refers to event with time " + QPCTimeToRelMSec(eventTimeStampQPC).ToString("f4") + " MSec that could not be found", stackEvent);
                    if (m_orphanedStacks == 999)
                    {
                        DebugWarn(true, "Last message about missing events.", stackEvent);
                    }
                }
                return null;
            }

            // Get information about this previous event
            IncompleteStack stackInfo = pastEventInfo.GetEventStackInfo(pastEventIndex);

            // For the target, if it does not have a stack entry we make one.
            if (stackInfo == null)
            {
                EventIndex eventIndex = pastEventInfo.GetEventIndex(pastEventIndex);
                if (eventIndex != EventIndex.Invalid)       // eventIndex == Invalid happens for events being removed from the stream.
                {
                    TraceProcess process = Processes.GetOrCreateProcess(stackEvent.ProcessID, stackEvent.TimeStampQPC);
                    TraceThread thread = Threads.GetOrCreateThread(stackEvent.ThreadID, stackEvent.TimeStampQPC, process);

                    stackInfo = AllocateIncompleteStack(eventIndex, thread, pastEventInfo.GetBlockingEventIndex(pastEventIndex));
                    pastEventInfo.SetEventStackInfo(pastEventIndex, stackInfo);     // Remember that we have info about this event.
                    pastEventInfo.GetEventCounts(pastEventIndex).m_stackCount++;
                }
            }
            return stackInfo;
        }
        /// <summary>
        /// Do the processing necessary to attach the user mode stack 'userModeStack' to any of the stacks in listOfIncompleteKernelStacks.
        /// It then clears this list.   While doing this processing it will check to see if the target stack 'target' is in that list and
        /// it will return true if it was.
        /// </summary>
        private bool EmitStackOnExitFromKernel(ref IncompleteStack listOfIncompleteKernelStacks, CallStackIndex userModeStack, IncompleteStack target)
        {
            bool foundTarget = false;
#if DEBUG
            int cnt = 0;
#endif
            for (IncompleteStack ptr = listOfIncompleteKernelStacks; ptr != null;)
            {
#if DEBUG
                Debug.Assert((++cnt % 8192) != 0, cnt.ToString() + " incomplete stacks");          // Not strictly true, but worthy of investigation if it is violated.
#endif
                var nextPtr = ptr.PrevKernelEventOnSameThread;
                ptr.PrevKernelEventOnSameThread = null;         // Remove it from the list.
                ptr.WaitingToLeaveKernel = false;               // Indicate that this entry was removed.

                ptr.LogUserStackFragment(userModeStack, this);
                if (ptr == target)
                {
                    foundTarget = true;
                }

                ptr = nextPtr;
            }
            listOfIncompleteKernelStacks = null;
            return foundTarget;
        }
        /// <summary>
        /// Do the processing necessary to attach the user mode stack 'userModeStack' to any of the stacks in listOfIncompleteKernelStacks.
        /// It then clears this list.   While doing this processing it will check to see if the target stack 'target' is in that list and
        /// it will return true if it was.
        /// </summary>
        private bool EmitStackOnExitFromKernel(ref IncompleteStack listOfIncompleteKernelStacks, Address userModeKey, IncompleteStack target)
        {
            bool foundTarget = false;
#if DEBUG
            int cnt = 0;
#endif
            for (IncompleteStack ptr = listOfIncompleteKernelStacks; ptr != null;)
            {
#if DEBUG
                Debug.Assert(cnt++ < 4096);          // Not strictly true, but worthy of investigation if it is violated.
#endif
                var nextPtr = ptr.PrevKernelEventOnSameThread;
                ptr.PrevKernelEventOnSameThread = null;         // Remove it from the list.
                ptr.WaitingToLeaveKernel = false;               // Indicate that this entry was removed.

                ptr.LogUserStackFragment(userModeKey, this);
                if (ptr == target)
                {
                    foundTarget = true;
                }

                ptr = nextPtr;
            }
            listOfIncompleteKernelStacks = null;
            return foundTarget;
        }
        /// <summary>
        /// Called when we get a definition event (for either a user mode or kernel mode stack fragment).
        /// </summary>
        private unsafe void LogStackDefinition(StackWalkDefTraceData data)
        {
            // Def or Rundown, I don't really care which.
            Debug.Assert(data.Opcode == (TraceEventOpcode)35 || data.Opcode == (TraceEventOpcode)36);

            if (data.FrameCount == 0)
            {
                DebugWarn(false, "Empty Stack definition", data);
                return;
            }

            // Get the linked list of events that use this stack key.
            // I have seen traces where here is a kernel and user mode stack with the SAME key live
            // at the same time.  Thus we need tow tables (one for user one for kernel), and when
            // we get a def, we have to know which one to look in (we do this based on the address
            // in the def (a bit kludgey in my opinion).
            IncompleteStack stackInfo;
            if (IsKernelAddress(data.InstructionPointer(data.FrameCount - 1), data.PointerSize))
            {
                // We have a kernel mode definition, look up in the kernel mode table.
                if (kernelStackKeyToInfo.TryGetValue(data.StackKey, out stackInfo))
                {
                    // It is a kernel def.
                    kernelStackKeyToInfo.Remove(data.StackKey);
                    while (stackInfo != null)
                    {
                        Debug.Assert(!stackInfo.IsDead);
                        Debug.Assert(stackInfo.KernelModeStackKey == data.StackKey);

                        // Remove from list.
                        stackInfo.KernelModeStackKey = 0;           // Now that we have the def, we can null it out This indicates it is not on a list.
                        var nextStackInfo = stackInfo.NextEventWithKernelKey;
                        stackInfo.NextEventWithKernelKey = null;

                        // We can't form the call stack yet, because we need the user mode part of the stack and we
                        // typically don't have it.   Moreover we have to convert the addresses to CodeAddressIndexes now
                        // because it depends on the loaded DLLs and that may change later.  Thus convert the
                        // fragment to a list of frames.
                        stackInfo.LogKernelStackFragment(data.InstructionPointers, data.FrameCount, pointerSize, data.TimeStampQPC, this);

                        stackInfo = nextStackInfo;
                    }
                }
                else
                {
                    DebugWarn(false, "Found a kernel stack definition without any uses", data);
                }
            }
            else
            {
                // We have a user mode definition, look up in the user mode table.
                if (userStackKeyToInfo.TryGetValue(data.StackKey, out stackInfo))
                {
                    Debug.Assert(stackInfo.PrevKernelEventOnSameThread == null);
                    Debug.Assert(!stackInfo.WaitingToLeaveKernel);
                    userStackKeyToInfo.Remove(data.StackKey);

                    while (stackInfo != null)
                    {
                        // User mode stacks we can convert immediately.
                        CallStackIndex callStack = callStacks.GetStackIndexForStackEvent(
                             data.InstructionPointers, data.FrameCount, data.PointerSize, stackInfo.Thread, stackInfo.UserModeStackIndex);

                        Debug.Assert(!stackInfo.IsDead);
                        Debug.Assert(stackInfo.UserModeStackKey == data.StackKey);
                        Debug.Assert(stackInfo.UserModeStackIndex == CallStackIndex.Invalid);

                        // Remove from list.
                        stackInfo.UserModeStackKey = 0;             // Indicates that it is no longer on any userStackKeyToInfo list.
                        var nextStackInfo = stackInfo.NextEventWithUserKey;
                        stackInfo.NextEventWithUserKey = null;

                        stackInfo.LogUserStackFragment(callStack, this);
                        stackInfo = nextStackInfo;
                    }
                }
                else
                {
                    DebugWarn(false, "Found a user stack definition without any uses", data);
                }
            }
        }

        private IncompleteStack AllocateIncompleteStack(EventIndex eventIndex, TraceThread thread, EventIndex blockingEventIndex)
        {
            Debug.Assert(eventIndex != EventIndex.Invalid);
            var ret = freeEventStackInfos;
            if (ret == null)
            {
                ret = new IncompleteStack();
            }
            else
            {
                freeEventStackInfos = ret.NextEventWithKernelKey;
                ret.Clear();
            }
            ret.Initialize(eventIndex, thread, blockingEventIndex);
            return ret;
        }
        private void FreeIncompleteStack(IncompleteStack toFree)
        {
            Debug.Assert(toFree.IsDead);
            toFree.NextEventWithKernelKey = freeEventStackInfos;
            freeEventStackInfos = toFree;
        }

        // Holds information needed to link up a stack with its event.
        private PastEventInfo pastEventInfo;

        // this is a linked list of unused EventStackInfos.
        private IncompleteStack freeEventStackInfos;

        // For any incomplete events we hold a linked list of key to a linked list of IncompleteStack that use that key.
        private Dictionary<Address, IncompleteStack> kernelStackKeyToInfo = new Dictionary<Address, IncompleteStack>();
        private Dictionary<Address, IncompleteStack> userStackKeyToInfo = new Dictionary<Address, IncompleteStack>();

        /// <summary>
        /// Holds information about stacks associated with an event.  This is a transient structure.  We only need it
        /// until all the information is collected for a particular event, at which point we can create a
        /// CallStackIndex for the stack and eventsToStacks table.
        /// </summary>
        internal class IncompleteStack
        {
            /// <summary>
            /// Clear clears entires that typically don't get set when we only have 1 frame fragment
            /// We can recycle the entries without setting these in that case.
            /// </summary>
            public void Clear()
            {
                Debug.Assert(IsDead);
                NextEventWithKernelKey = null;
                NextEventWithUserKey = null;
                KernelStackFrames.Count = 0;
                KernelModeStackKey = 0;
                UserModeStackKey = 0;
                PrevKernelEventOnSameThread = null;
                WaitingToLeaveKernel = false;
            }

            /// <summary>
            /// Clear all entries that can potentially change every time.
            /// </summary>
            public void Initialize(EventIndex eventIndex, TraceThread thread, EventIndex blockingEventIndex)
            {
                Debug.Assert(IsDead);
                UserModeStackIndex = CallStackIndex.Invalid;
                EventIndex = eventIndex;
                Thread = thread;
                BlockingEventIndex = blockingEventIndex;
                Debug.Assert(PrevKernelEventOnSameThread == null);
                Debug.Assert(!WaitingToLeaveKernel);
                Debug.Assert(NextEventWithKernelKey == null);
                Debug.Assert(NextEventWithUserKey == null);
                Debug.Assert(!IsDead);
            }

            /// <summary>
            /// Log the Kernel Stack fragment.  We simply remember all the frames (converted to CodeAddressIndexes).
            /// </summary>
            public unsafe bool LogKernelStackFragment(void* addresses, int addressCount, int pointerSize, long timeStampQPC, TraceLog eventLog)
            {
                Debug.Assert(!IsDead);
                KernelStackFrames.Clear();
                for (int i = 0; i < addressCount; i++)
                {
                    Address address;
                    if (pointerSize == 8)
                    {
                        address = ((ulong*)addresses)[i];
                    }
                    else
                    {
                        address = ((uint*)addresses)[i];
                    }

                    KernelStackFrames.Add(eventLog.CallStacks.CodeAddresses.GetOrCreateCodeAddressIndex(Thread.Process, address));
                }

                // Optimization: because process 0 and 4 never have user mode stacks, don't wait around waiting for
                // the user mode part, so mark it as complete and ready to go.
                // TODO can be optimized to never allocate the incomplete stack.
                var processID = Thread.Process.ProcessID;
                if (processID == 0 || processID == 4)                   // These processes never have user mode stacks, so complete them aggressively.
                {
                    UserModeStackIndex = TraceCallStacks.GetRootForThread(Thread.ThreadIndex);
                }

                return EmitStackForEventIfReady(eventLog);
            }

            /// <summary>
            /// Log the kernel stack fragment.  Returns true if all the pieces of the stack fragment are collected
            /// (we don't have to log something on the thread).
            /// </summary>
            public bool LogKernelStackFragment(Address kernelModeStackKey, TraceLog eventLog)
            {
                if (KernelModeStackKey != 0)
                {
                    // This happens when the same event has multiple stacks pointing at it.  We don't want to corrupt our linked lists by adding the same node twice.
                    eventLog.DebugWarn(false, "Error, an event has two kernel stack keys 0x" + kernelModeStackKey.ToString("x") + " and " + KernelModeStackKey.ToString("x"), null);
                    return true;
                }
                Debug.Assert(KernelStackFrames.Count == 0 && KernelModeStackKey == 0 && UserModeStackKey == 0 && UserModeStackIndex == CallStackIndex.Invalid);
                Debug.Assert(!IsDead);

                KernelModeStackKey = kernelModeStackKey;

                bool allFragmentsHaveBeenCollected = false;    // Indicates the not all the pieces of the stack are available.

                // Optimization: because process 0 and 4 never have user mode stacks, don't wait around waiting for
                // the user mode part, so mark it as complete and ready to go.
                // TODO can be optimized to never allocate the incomplete stack.
                var processID = Thread.Process.ProcessID;
                if (processID == 0 || processID == 4)
                {                // These processes never have user mode stacks, so complete them aggressively.
                    UserModeStackIndex = TraceCallStacks.GetRootForThread(Thread.ThreadIndex);
                    allFragmentsHaveBeenCollected = true;
                }

                // Put this on the list of stackInfos to be resolved with we find this kernel key.
                IncompleteStack prevWithKernelKey;
                eventLog.kernelStackKeyToInfo.TryGetValue(kernelModeStackKey, out prevWithKernelKey);
                Debug.Assert(prevWithKernelKey == null || prevWithKernelKey.KernelModeStackKey == kernelModeStackKey);
                Debug.Assert(NextEventWithKernelKey == null);
                NextEventWithKernelKey = prevWithKernelKey;
                eventLog.kernelStackKeyToInfo[kernelModeStackKey] = this;

                return allFragmentsHaveBeenCollected;
            }
            /// <summary>
            ///
            /// </summary>
            public void LogUserStackFragment(CallStackIndex userModeStackIndex, TraceLog eventLog)
            {
                Debug.Assert(!IsDead);
                //Debug.Assert(UserModeStackIndex == CallStackIndex.Invalid);
                UserModeStackIndex = userModeStackIndex;
                bool emitted = EmitStackForEventIfReady(eventLog);
                //Debug.Assert((emitted && IsDead) || KernelModeStackKey != 0);   // Only not having the kernel stack def can be left
            }
            public void LogUserStackFragment(Address userModeStackKey, TraceLog eventLog)
            {
                Debug.Assert(!IsDead);
                if (UserModeStackKey != 0)
                {
                    // This happens when the same event has multiple stacks pointing at it.  We don't want to corrupt our linked lists by adding the same node twice.
                    eventLog.DebugWarn(false, "Error, an event has two user stack keys 0x" + userModeStackKey.ToString("x") + " and " + UserModeStackKey.ToString("x"), null);
                    return;
                }
                Debug.Assert(UserModeStackKey == 0 && UserModeStackIndex == CallStackIndex.Invalid);
                UserModeStackKey = userModeStackKey;

                // Put this on the list of stackInfos to be resolved with we find this kernel key.
                IncompleteStack prevWithUserKey;
                eventLog.userStackKeyToInfo.TryGetValue(UserModeStackKey, out prevWithUserKey);
                Debug.Assert(prevWithUserKey == null || prevWithUserKey.UserModeStackKey == userModeStackKey);
                Debug.Assert(NextEventWithUserKey == null);
                NextEventWithUserKey = prevWithUserKey;
                eventLog.userStackKeyToInfo[UserModeStackKey] = this;
            }

            /// <summary>
            /// Determine if 'stackInfo' is complete and if so emit it to the 'eventsToStacks' array.  If 'force' is true
            /// then force what information there is out even if it is not complete (there is nothing else coming).
            ///
            /// Returns true if it was able to emit the stack
            /// </summary>
            private unsafe bool EmitStackForEventIfReady(TraceLog eventLog)
            {
                if (UserModeStackIndex != CallStackIndex.Invalid)
                {
                    bool hasKernelStack = false;
                    if (KernelStackFrames.Count != 0)  // If we had to defer some KernelStack processing, do it now.
                    {
                        hasKernelStack = true;
                        // Now that we have the user mode stack, we can append the kernel stack to it.
                        for (int i = KernelStackFrames.Count - 1; 0 <= i; --i)
                        {
                            UserModeStackIndex = eventLog.CallStacks.InternCallStackIndex(KernelStackFrames[i], UserModeStackIndex);
                        }

                        KernelStackFrames.Count = 0;
                    }

                    // We know that user stacks come after kernel stacks, and we have a user stack.  Thus
                    // if we have a kernel stack or there is no kernel stack, then we can emit it
                    if (hasKernelStack || KernelModeStackKey == 0)
                    {

                        // If the userModeStack is negative, that means it represents a thread (since we know it can't be
                        // Invalid (which is also negative).   This means the ENTIRE stack is just the thread, which means
                        // we had no actual stack frames.  This should not happen, so we assert it.
                        //Debug.Assert(UserModeStackIndex >= 0);

                        if (UserModeStackIndex >= 0)
                        {
                            // Failsafe if the assert fails, drop the stack since it is just the thread and process anyway.
                            eventLog.AddStackToEvent(EventIndex, UserModeStackIndex);
                            if (BlockingEventIndex != Tracing.EventIndex.Invalid)
                            {
                                eventLog.cswitchBlockingEventsToStacks.Add(new EventsToStackIndex(BlockingEventIndex, UserModeStackIndex));
                            }

                            // Trace.WriteLine("Writing Stack " + UserModeStackIndex + " for Event " + EventIndex);
                        }

                        // We have had collisions where we have had a two events with the exact same timestamp which both had stacks.
                        // Moreover, we have had one of these events have a StackWalk and the other a KernelStackKey.   This can
                        // result in us getting here (we are emitting a stack because we have the StackWalk event) but without
                        // a resolved kernel key.
                        //
                        // Only free the entry if it is not on any list (avoid evil reuse that leads to hell...)
                        if (KernelModeStackKey == 0 && UserModeStackKey == 0 && !WaitingToLeaveKernel)
                        {
                            Debug.Assert(PrevKernelEventOnSameThread == null);
                            Debug.Assert(KernelModeStackKey == 0);          // This says it is not on any kernelStackKeyToInfo list
                            Debug.Assert(NextEventWithKernelKey == null);
                            Debug.Assert(NextEventWithUserKey == null);
                            Debug.Assert(!WaitingToLeaveKernel);
                            Debug.Assert(KernelStackFrames.Count == 0);

                            Thread = null;      // Mark it as dead.
                            eventLog.FreeIncompleteStack(this);
                        }
                        else
                        {
                            if (KernelModeStackKey == 0)
                            {
                                eventLog.DebugWarn(false, "Warning, finished a stack when kernel key " + KernelModeStackKey.ToString("x") + " still unresolved.", null);
                            }

                            if (UserModeStackKey == 0)
                            {
                                eventLog.DebugWarn(false, "Warning, finished a stack when user key " + UserModeStackKey.ToString("x") + " still unresolved.", null);
                            }

                            if (WaitingToLeaveKernel)
                            {
                                eventLog.DebugWarn(false, "Warning, finished a stack is still waiting to return from kernel.", null);
                            }
                        }
                        return true;
                    }
                }
                return false;
            }

            /// <summary>
            /// returns true if the IncompleteStack is dead (just waiting to be reused).
            /// </summary>
            internal bool IsDead { get { return Thread == null; } }

            // Stuff about the event itself.
            internal EventIndex EventIndex { get; private set; } // We remember the event index for this stack mostly so we can know when this entry is

            /// <summary>
            /// We track the stacks for when CSwitches block, this is the CSWITCH event where that blocking happened.
            /// </summary>
            internal EventIndex BlockingEventIndex { get; set; }

            // invalid because it has been reused for another event.
            internal TraceThread Thread { get; private set; }    // Needed to compute very top of call stack.

            // The raw stack keys used in ETW compressed stacks to represent the stack fragments.
            internal Address KernelModeStackKey;
            internal Address UserModeStackKey;

            internal IncompleteStack NextEventWithKernelKey;     // We form a linked list of all events with a certain kernel mode key
            internal IncompleteStack NextEventWithUserKey;       // We form a linked list of all events with a certain user mode key

            // If we get a kernel stack, we are in the kernel.  when we leave the kernel we log the user
            // mode part of the stack.   We attach this to all kernel stacks on this list.  Thus we should
            // accumulate this list until we hit the user mode stack at which point we flush it.
            internal IncompleteStack PrevKernelEventOnSameThread;
            internal bool WaitingToLeaveKernel;                      // if true if some list on some thread contains this entry.

            // If the kernel data comes in before the user stack (a common case), we can't create a stack index
            // (because we don't know the thread-end of the stack), so we simply remember it.
            private GrowableArray<CodeAddressIndex> KernelStackFrames;

            // If data for the user stack comes in first, we can convert it immediately to a stack index.
            internal CallStackIndex UserModeStackIndex { get; private set; }

            internal void AddEntryToThread(ref IncompleteStack lastEntryIntoKernel)
            {
                // We run into this condition when two stacks point at the same event
                if (WaitingToLeaveKernel)       // Already on some list, give up.  At least we don't form infinite (circular) lists.
                {
                    return;
                }

                Debug.Assert(lastEntryIntoKernel != this && PrevKernelEventOnSameThread == null);

                Debug.Assert(!IsDead);
                // We only put stackInfos that have no user mode stacks on this list.
                Debug.Assert(UserModeStackIndex == CallStackIndex.Invalid && UserModeStackKey == 0);
#if DEBUG
                int len = numKernelEntries(this);
                Debug.Assert(len < 4096);                // Not really true, but close enough.

                for (var ptr = lastEntryIntoKernel; ptr != null; ptr = ptr.PrevKernelEventOnSameThread)
                {
                    Debug.Assert(ptr != this);
                }
#endif
                Debug.Assert(lastEntryIntoKernel == null || lastEntryIntoKernel.Thread == Thread);
                Debug.Assert(PrevKernelEventOnSameThread == null);
                PrevKernelEventOnSameThread = lastEntryIntoKernel;
                WaitingToLeaveKernel = true;
                lastEntryIntoKernel = this;
            }
#if DEBUG
            private static int numKernelEntries(IncompleteStack ptr)
            {
                var entries = new Dictionary<object, object>();
                int ret = 0;
                while (ptr != null)
                {
                    Debug.Assert(!entries.ContainsKey(ptr));
                    entries.Add(ptr, ptr);
                    Debug.Assert(ret < 10000);
                    ret++;
                    ptr = ptr.PrevKernelEventOnSameThread;
                }
                return ret;
            }
#endif
        }

        /// <summary>
        /// Put the thread that owns 'data' in to the category 'category.
        /// </summary>
        private void CategorizeThread(TraceEvent data, string category, bool overwrite=false)
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                return;
            }

            var thread = Threads.GetThread(data.ThreadID, data.TimeStampQPC);
            if (thread == null)
            {
                return;
            }

            if (thread.threadInfo == null || overwrite)
            {
                thread.threadInfo = category;
            }
        }

        internal static bool IsKernelAddress(Address ip, int pointerSize)
        {
            if (pointerSize == 4)
            {
                return ip >= 0x80000000;
            }

            return ip >= 0xFFFF000000000000;        // TODO I don't know what the true cutoff is.
        }

        /// <summary>
        /// Process any extended data (like Win7 style stack traces) associated with 'data'
        /// returns true if the event should be considered a bookkeeping event.
        /// </summary>
        internal unsafe bool ProcessExtendedData(TraceEvent data, ushort extendedDataCount, TraceEventCounts countForEvent)
        {
            var isBookkeepingEvent = false;
            var extendedData = data.eventRecord->ExtendedData;
            Debug.Assert(extendedData != null && extendedDataCount != 0);
            Guid* relatedActivityIDPtr = null;
            string containerID = null;
            for (int i = 0; i < extendedDataCount; i++)
            {
                if (extendedData[i].ExtType == TraceEventNativeMethods.EVENT_HEADER_EXT_TYPE_STACK_TRACE64 ||
                    extendedData[i].ExtType == TraceEventNativeMethods.EVENT_HEADER_EXT_TYPE_STACK_TRACE32)
                {
                    int pointerSize = (extendedData[i].ExtType == TraceEventNativeMethods.EVENT_HEADER_EXT_TYPE_STACK_TRACE64) ? 8 : 4;
                    var stackRecord = (TraceEventNativeMethods.EVENT_EXTENDED_ITEM_STACK_TRACE64*)extendedData[i].DataPtr;
                    // TODO Debug.Assert(stackRecord->MatchId == 0);
                    ulong* addresses = &stackRecord->Address[0];
                    int addressesCount = (extendedData[i].DataSize - sizeof(ulong)) / pointerSize;

                    TraceProcess process = processes.GetOrCreateProcess(data.ProcessID, data.TimeStampQPC);
                    TraceThread thread = Threads.GetOrCreateThread(data.ThreadIDforStacks(), data.TimeStampQPC, process);
                    EventIndex eventIndex = (EventIndex)eventCount;

                    ulong sampleAddress;
                    byte* lastAddressPtr = (((byte*)addresses) + (extendedData[i].DataSize - sizeof(ulong) - pointerSize));
                    if (pointerSize == 4)
                    {
                        sampleAddress = *((uint*)lastAddressPtr);
                    }
                    else
                    {
                        sampleAddress = *((ulong*)lastAddressPtr);
                    }

                    // Note that I use the pointer size for the log, not the event, since the kernel events
                    // might differ in pointer size from the user mode event.
                    if (PointerSize == 4)
                    {
                        sampleAddress &= 0xFFFFFFFF00000000;
                    }

                    if (IsKernelAddress(sampleAddress, PointerSize) && data.ProcessID != 0 && data.ProcessID != 4)
                    {
                        // If this is a kernel event, we have to defer making the stack (it is incomplete).
                        // Make a new IncompleteStack to track that (unlike other stack events we don't need to go looking for it.
                        IncompleteStack stackInfo = AllocateIncompleteStack(eventIndex, thread, EventIndex.Invalid);    // Blocking stack can be invalid because CSWitches don't use this path.
                        Debug.Assert(!(data is CSwitchTraceData));        // CSwtiches don't use this form of call stacks.  When they do set setackInfo.IsCSwitch.

                        // Remember the kernel frames
                        if (!stackInfo.LogKernelStackFragment(addresses, addressesCount, pointerSize, data.TimeStampQPC, this))
                        {
                            stackInfo.AddEntryToThread(ref thread.lastEntryIntoKernel);     // If not done remember to complete it
                        }

                        if (countForEvent != null)
                        {
                            countForEvent.m_stackCount++;   // Update stack counts
                        }
                    }
                    else
                    {
                        CallStackIndex callStackIndex = callStacks.GetStackIndexForStackEvent(
                            addresses, addressesCount, pointerSize, thread);
                        Debug.Assert(callStacks.Depth(callStackIndex) == addressesCount);

                        // Is this the special ETW_TASK_STACK_TRACE/ETW_OPCODE_USER_MODE_STACK_TRACE which is just
                        // there to attach to a kernel event if so attach it to all IncompleteStacks on this thread.
                        if (data.ID == (TraceEventID)18 && data.Opcode == (TraceEventOpcode)24 &&
                            data.ProviderGuid == KernelTraceEventParser.EventTracingProviderGuid)
                        {
                            isBookkeepingEvent = true;
                            EmitStackOnExitFromKernel(ref thread.lastEntryIntoKernel, callStackIndex, null);
                            thread.lastEmitStackOnExitFromKernelQPC = data.TimeStampQPC;
                        }
                        else
                        {
                            // If this is not the special user mode stack event that fires on exit from the kernel
                            // we don't need any IncompleteStack structures, we can just attach the stack to the
                            // current event and be done.

                            // Note that we don't interfere with the splicing of kernel and user mode stacks because we do
                            // see user mode stacks delayed and have a new style user mode stack spliced in.
                            AddStackToEvent(eventIndex, callStackIndex);
                            if (countForEvent != null)
                            {
                                countForEvent.m_stackCount++;   // Update stack counts
                            }
                        }
                    }
                }
                else if (extendedData[i].ExtType == TraceEventNativeMethods.EVENT_HEADER_EXT_TYPE_RELATED_ACTIVITYID)
                {
                    relatedActivityIDPtr = (Guid*)(extendedData[i].DataPtr);
                }
                else if(extendedData[i].ExtType == TraceEventNativeMethods.EVENT_HEADER_EXT_TYPE_CONTAINER_ID)
                {
                    containerID = Marshal.PtrToStringAnsi((IntPtr)extendedData[i].DataPtr, (int)extendedData[i].DataSize);
                }
            }

            if (relatedActivityIDPtr != null)
            {
                if(relatedActivityIDs.Count == 0)
                {
                    // Insert a synthetic value since 0 represents "no related activity ID".
                    relatedActivityIDs.Add(Guid.Empty);
                }

                // TODO This is a bit of a hack.   We wack this field in place.
                // We encode this as index into the relatedActivityID GrowableArray.
                if (IntPtr.Size == 8)
                {
                    data.eventRecord->ExtendedData = (TraceEventNativeMethods.EVENT_HEADER_EXTENDED_DATA_ITEM*)(relatedActivityIDs.Count << 4);
                }
                else
                {
                    data.eventRecord->ExtendedData = (TraceEventNativeMethods.EVENT_HEADER_EXTENDED_DATA_ITEM*)relatedActivityIDs.Count;
                }
                relatedActivityIDs.Add(*relatedActivityIDPtr);
            }
            else
            {
                data.eventRecord->ExtendedData = null;
            }

            if(containerID != null)
            {
                // TODO This is a bit of a hack.   We wack this field in place.
                // We encode this as index into the containerIDs GrowableArray.
                if (containerIDs.Count == 0)
                {
                    // Insert a synthetic value since 0 represents "no container ID".
                    containerIDs.Add(null);
                }

                // Look for the container ID.
                bool found = false;
                for(int i=0; i<containerIDs.Count; i++)
                {
                    if(containerIDs[i] == containerID)
                    {
                        data.eventRecord->ExtendedDataCount = (ushort)i;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    data.eventRecord->ExtendedDataCount = (ushort)containerIDs.Count;
                    containerIDs.Add(containerID);
                }
            }
            else
            {
                data.eventRecord->ExtendedDataCount = 0;
            }

            return isBookkeepingEvent;
        }

        internal override string ProcessName(int processID, long timeQPC)
        {
            TraceProcess process = Processes.GetProcess(processID, timeQPC);
            if (process != null)
                return process.Name;
            return base.ProcessName(processID, timeQPC);
        }

        /// <summary>
        /// Dispose pattern
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // If we have a timer dispose (stop) it.
                if (realTimeFlushTimer != null)
                {
                    realTimeFlushTimer.Dispose();
                }

                if (lazyRawEvents.Deserializer != null)
                {
                    lazyRawEvents.Deserializer.Dispose();
                }
            }

            base.Dispose(disposing);
        }
        private static unsafe void WriteBlob(IntPtr source, IStreamWriter writer, int byteCount)
        {
            // TODO: currently most uses the source aligned so
            // I don't bother trying to insure that the copy is aligned.
            Debug.Assert(byteCount % 4 == 0);
            int* sourcePtr = (int*)source;
            int intCount = byteCount >> 2;
            while (intCount > 0)
            {
                writer.Write(*sourcePtr++);
                --intCount;
            }
        }

        // [Conditional("DEBUG")]
        internal void DebugWarn(bool condition, string message, TraceEvent data)
        {
            if (!condition)
            {
                TextWriter writer = null;
                if (options != null)
                {
                    writer = options.ConversionLog;
                }

                bool debugBuild = false;
#if DEBUG
                debugBuild = true;
#endif
                if (writer == null && !debugBuild)
                {
                    return;
                }

                Trace.Write("WARNING: ");
                string prefix = "";
                if (data != null)
                {
                    prefix = "Time: " + data.TimeStampRelativeMSec.ToString("f4").PadLeft(12) + " PID: " + data.ProcessID.ToString().PadLeft(4) + ": ";
                    Debug.Write(prefix);
                }
                Trace.WriteLine(message);

                if (writer == null)
                {
                    return;
                }

                writer.Write("WARNING: ");
                if (prefix != null)
                {
                    writer.Write(prefix);
                }

                writer.WriteLine(message);

                ImageLoadTraceData asImageLoad = data as ImageLoadTraceData;
                if (asImageLoad != null)
                {
                    writer.WriteLine("    FILE: " + asImageLoad.FileName);
                    writer.WriteLine("    BASE: 0x" + asImageLoad.ImageBase.ToString("x"));
                    writer.WriteLine("    SIZE: 0x" + asImageLoad.ImageSize.ToString("x"));
                }
                ModuleLoadUnloadTraceData asModuleLoad = data as ModuleLoadUnloadTraceData;
                if (asModuleLoad != null)
                {
                    writer.WriteLine("    NGEN:     " + asModuleLoad.ModuleNativePath);
                    writer.WriteLine("    ILFILE:   " + asModuleLoad.ModuleILPath);
                    writer.WriteLine("    MODULEID: 0x" + ((ulong)asModuleLoad.ModuleID).ToString("x"));
                }
                MethodLoadUnloadVerboseTraceData asMethodLoad = data as MethodLoadUnloadVerboseTraceData;
                if (asMethodLoad != null)
                {
                    writer.WriteLine("    METHOD:   " + GetFullName(asMethodLoad));
                    writer.WriteLine("    MODULEID: " + ((ulong)asMethodLoad.ModuleID).ToString("x"));
                    writer.WriteLine("    START:    " + ((ulong)asMethodLoad.MethodStartAddress).ToString("x"));
                    writer.WriteLine("    LENGTH:   " + asMethodLoad.MethodSize.ToString("x"));
                }
            }
        }
        internal static string GetFullName(MethodLoadUnloadVerboseTraceData data)
        {
            string sig = data.MethodSignature;
            int parens = sig.IndexOf('(');
            string args;
            if (parens >= 0)
            {
                args = sig.Substring(parens);
            }
            else
            {
                args = "";
            }

            string fullName = data.MethodNamespace + "." + data.MethodName + args;
            return fullName;
        }

        internal int FindPageIndex(long timeQPC)
        {
            int pageIndex;
            // TODO error conditions.
            // TODO? extra copy of EventPageEntry during search.
            eventPages.BinarySearch(timeQPC, out pageIndex, delegate (long targetTimeQPC, EventPageEntry entry)
            {
                return targetTimeQPC.CompareTo(entry.TimeQPC);
            });
            // TODO completely empty logs.
            if (pageIndex < 0)
            {
                pageIndex = 0;
            }

            return pageIndex;
        }

        /// <summary>
        /// Advance 'reader' until it point at a event that occurs on or after 'timeQPC'.  on page
        /// 'pageIndex'.  If 'positions' is non-null, fill in that array.  Also return the index in
        /// 'positions' for the entry that was found.
        /// </summary>
        internal unsafe void SeekToTimeOnPage(PinnedStreamReader reader, long timeQPC, int pageIndex, out int indexOnPage, StreamLabel[] positions)
        {
            reader.Goto(eventPages[pageIndex].Position);
            int i = -1;
            while (i < TraceLog.eventsPerPage - 1)
            {
                i++;
                if (positions != null)
                {
                    positions[i] = reader.Current;
                }

                TraceEventNativeMethods.EVENT_RECORD* ptr = (TraceEventNativeMethods.EVENT_RECORD*)reader.GetPointer(headerSize);

                // Header sanity checks.
                Debug.Assert(ptr->EventHeader.Level <= 6);
                Debug.Assert(ptr->EventHeader.Version <= 10);

                long eventTimeQPC = ptr->EventHeader.TimeStamp;
                Debug.Assert(sessionStartTimeQPC <= eventTimeQPC && eventTimeQPC < DateTime.Now.Ticks || eventTimeQPC == long.MaxValue);

                if (eventTimeQPC >= timeQPC)
                {
                    break;
                }

                int eventDataLength = ptr->UserDataLength;
                Debug.Assert(eventDataLength < 0x20000);
                reader.Skip(headerSize + ((eventDataLength + 3) & ~3));
            }
            indexOnPage = i;
        }

        internal unsafe PinnedStreamReader AllocReader()
        {
            if (freeReader == null)
            {
                freeReader = ((PinnedStreamReader)lazyRawEvents.Deserializer.Reader).Clone();
            }

            PinnedStreamReader ret = freeReader;
            freeReader = null;
            return ret;
        }
        internal unsafe void FreeReader(PinnedStreamReader reader)
        {
            if (freeReader == null)
            {
                freeReader = reader;
            }
        }

        internal TraceLogEventSource AddAllTemplatesToDispatcher(TraceLogEventSource etlxSource)
        {
            List<TraceEventParser> dynamicParsers = new List<TraceEventParser>();
            foreach (var parser in Parsers)
            {
                if (parser.IsStatic)
                {
                    AddTemplatesForParser(parser, etlxSource);
                }
                else
                {
                    dynamicParsers.Add(parser);
                }
            }

            // The first registered template is used as the canonical template so we
            // register the static ones first so they get preference.
            foreach (var parser in dynamicParsers)
            {
                AddTemplatesForParser(parser, etlxSource);
            }

            // Debug.WriteLine("Got a TraceLog dispatcher");
            // etlxSource.DumpToDebugString();
            return etlxSource;
#if false
                // TODO FIX NOW ACTIVITIES: review
            if (this.HasActivitySubscriptions)
            {
                ret.activityScheduled = this.activityScheduled;
                ret.activityStarted = this.activityStarted;
                ret.activityCompleted = this.activityCompleted;
                Log.TraceActivities.SubscribeToActivityTracingEvents(ret, true);
            }
#endif
        }

        private void AddTemplatesForParser(TraceEventParser parser, TraceLogEventSource ret)
        {
            parser.EnumerateTemplates(null, delegate (TraceEvent template)
            {
                Debug.Assert(template.Target == null);
                Debug.Assert(template.Source == null);
                template = template.Clone();
                template.SetState(parser.StateObject);
                ((ITraceParserServices)ret).RegisterEventTemplate(template);
            });
        }

        /// <summary>
        /// We need a TraceEventDispatcher in the Enumerators for TraceLog that know how to LOOKUP an event
        /// We don't actually dispatch through it.  We do mutate the templates (to point a particular data
        /// record), but once we are done with it we can reuse this TraceEventDispatcher again an again
        /// (it is only concurrent access that is a problem).  Thus we have an Allocate and Free pattern
        /// to reuse them in the common case of sequential access.
        /// </summary>
        /// <returns></returns>
        internal unsafe TraceEventDispatcher AllocLookup()
        {
            if (freeLookup == null)
            {
                freeLookup = AddAllTemplatesToDispatcher(new TraceLogEventSource(events));
            }

            TraceEventDispatcher ret = freeLookup;
            freeLookup = null;
            return ret;
        }
        internal unsafe void FreeLookup(TraceEventDispatcher lookup)
        {
            if (freeLookup == null)
            {
                freeLookup = lookup;
            }
        }

        private unsafe void InitializeFromFile(string etlxFilePath)
        {
            // If this Assert files, fix the declaration of headerSize to match
            Debug.Assert(sizeof(TraceEventNativeMethods.EVENT_HEADER) == 0x50 && sizeof(TraceEventNativeMethods.ETW_BUFFER_CONTEXT) == 4);

            Deserializer deserializer = new Deserializer(new PinnedStreamReader(etlxFilePath, 0x10000), etlxFilePath);
            deserializer.TypeResolver = typeName => System.Type.GetType(typeName);  // resolve types in this assembly (and mscorlib)

            // when the deserializer needs a TraceLog we return the current instance.  We also assert that
            // we only do this once.
            deserializer.RegisterFactory(typeof(TraceLog), delegate
            {
                Debug.Assert(sessionStartTimeQPC == 0 && sessionEndTimeQPC == 0);
                return this;
            });
            deserializer.RegisterFactory(typeof(TraceProcess), delegate { return new TraceProcess(0, null, 0); });
            deserializer.RegisterFactory(typeof(TraceProcesses), delegate { return new TraceProcesses(null); });
            deserializer.RegisterFactory(typeof(TraceThreads), delegate { return new TraceThreads(null); });
            deserializer.RegisterFactory(typeof(TraceThread), delegate { return new TraceThread(0, null, (ThreadIndex)0); });
            deserializer.RegisterFactory(typeof(TraceActivity), delegate { return new TraceActivity(ActivityIndex.Invalid, null, EventIndex.Invalid, CallStackIndex.Invalid, 0, 0, false, false, TraceActivity.ActivityKind.Invalid); });
            deserializer.RegisterFactory(typeof(TraceModuleFiles), delegate { return new TraceModuleFiles(null); });
            deserializer.RegisterFactory(typeof(TraceModuleFile), delegate { return new TraceModuleFile(null, 0, 0); });
            deserializer.RegisterFactory(typeof(TraceMethods), delegate { return new TraceMethods(null); });
            deserializer.RegisterFactory(typeof(TraceCodeAddresses), delegate { return new TraceCodeAddresses(null, null); });
            deserializer.RegisterFactory(typeof(TraceCallStacks), delegate { return new TraceCallStacks(null, null); });
            deserializer.RegisterFactory(typeof(TraceEventStats), delegate { return new TraceEventStats(null); });
            deserializer.RegisterFactory(typeof(TraceEventCounts), delegate { return new TraceEventCounts(null, null); });

            deserializer.RegisterFactory(typeof(TraceLoadedModules), delegate { return new TraceLoadedModules(null); });
            deserializer.RegisterFactory(typeof(TraceLoadedModule), delegate { return new TraceLoadedModule(null, null, 0UL); });
            deserializer.RegisterFactory(typeof(TraceManagedModule), delegate { return new TraceManagedModule(null, null, 0L); });

            deserializer.RegisterFactory(typeof(ProviderManifest), delegate
            {
                return new ProviderManifest(null, ManifestEnvelope.ManifestFormats.SimpleXmlFormat, 0, 0, "");
            });
            deserializer.RegisterFactory(typeof(DynamicTraceEventData), delegate
            {
                return new DynamicTraceEventData(null, 0, 0, null, Guid.Empty, 0, null, Guid.Empty, null);
            });

            // when the serializer needs any TraceEventParser class, we assume that its constructor
            // takes an argument of type TraceEventSource and that you can pass null to make an
            // 'empty' parser to fill in with FromStream.
            deserializer.RegisterDefaultFactory(delegate (Type typeToMake)
            {
                if (typeToMake.GetTypeInfo().IsSubclassOf(typeof(TraceEventParser)))
                {
                    return (IFastSerializable)Activator.CreateInstance(typeToMake, new object[] { null });
                }

                return null;
            });

            IFastSerializable entry = deserializer.GetEntryObject();

            RegisterStandardParsers();

            // TODO this needs to be a runtime error, not an assert.
            Debug.Assert(entry == this);
            // Our deserializer is now attached to our deferred events.
            Debug.Assert(lazyRawEvents.Deserializer == deserializer);

            this.etlxFilePath = etlxFilePath;

            // Sanity checking.
            Debug.Assert(pointerSize == 4 || pointerSize == 8, "Bad pointer size");
            Debug.Assert(10 <= cpuSpeedMHz && cpuSpeedMHz <= 100000, "Bad cpu speed");
            Debug.Assert(0 < numberOfProcessors && numberOfProcessors < 1024, "Bad number of processors");
            Debug.Assert(0 < MaxEventIndex);
        }

        private static char[] s_directorySeparators = { '\\', '/' };

        // Path  GetFileNameWithoutExtension will throw on illegal chars, which is too strong, so avoid that here.
        internal static string GetFileNameWithoutExtensionNoIllegalChars(string filePath)
        {
            int lastDirectorySep = filePath.LastIndexOfAny(s_directorySeparators);
            if (lastDirectorySep < 0)
            {
                lastDirectorySep = 0;
            }
            else
            {
                lastDirectorySep++;
            }

            int dotIdx = filePath.LastIndexOf('.');
            if (dotIdx < lastDirectorySep)
            {
                dotIdx = filePath.Length;
            }

            return filePath.Substring(lastDirectorySep, dotIdx - lastDirectorySep);
        }

#if DEBUG
        /// <summary>
        /// Returns true if 'str' has only normal ASCII (printable) characters.
        /// </summary>
        internal static bool NormalChars(string str)
        {
            for (int i = 0; i < str.Length; i++)
            {
                Char c = str[i];
                if (c < ' ' && !Char.IsWhiteSpace(c) || '~' < c)
                {
                    return false;
                }
            }
            return true;
        }
#endif
        void IFastSerializable.ToStream(Serializer serializer)
        {
            // Write out the events themselves, Before we do this we write a reference past the end of the
            // events so we can skip them without actually reading them.
            // The real work is done in CopyRawEvents

            // Align to 8 bytes
            StreamLabel pos = serializer.Writer.GetLabel();
            int align = ((int)pos + 1) & 7;          // +1 take into account we always write the count
            if (align > 0)
            {
                align = 8 - align;
            }

            serializer.Write((byte)align);
            for (int i = 0; i < align; i++)
            {
                serializer.Write((byte)0);
            }

            Debug.Assert((int)serializer.Writer.GetLabel() % 8 == 0);

            serializer.Log("<Marker name=\"RawEvents\"/>");
            lazyRawEvents.Write(serializer, delegate
            {
                // Get the events from a given raw stream
                TraceEventDispatcher dispatcher = rawEventSourceToConvert;
                if (dispatcher == null)
                {
                    dispatcher = events.GetSource();
                }

                CopyRawEvents(dispatcher, serializer.Writer);
                // Write sentinel event with a long.MaxValue timestamp mark the end of the data.
                for (int i = 0; i < 11; i++)
                {
                    if (i == 2)
                    {
                        serializer.Write(long.MaxValue);
                    }
                    else
                    {
                        serializer.Write((long)0);          // The important field here is the EventDataSize field
                    }
                }

                if (HasCallStacks || options.AlwaysResolveSymbols)
                {
                    codeAddresses.LookupSymbols(options);
                }
            });

            serializer.Log("<Marker name=\"sessionStartTime\"/>");
            serializer.Write(_syncTimeUTC.ToFileTimeUtc());
            serializer.Write(pointerSize);
            serializer.Write(numberOfProcessors);
            serializer.Write(cpuSpeedMHz);
            serializer.Write((byte)osVersion.Major);
            serializer.Write((byte)osVersion.Minor);
            serializer.Write((byte)osVersion.MajorRevision);
            serializer.Write((byte)osVersion.MinorRevision);
            serializer.Write(QPCFreq);
            serializer.Write(sessionStartTimeQPC);
            serializer.Write(sessionEndTimeQPC);
            serializer.Write(eventsLost);
            serializer.Write(machineName);
            serializer.Write(memorySizeMeg);

            serializer.Write(processes);
            serializer.Write(threads);
            serializer.Write(codeAddresses);
            serializer.Write(stats);
            serializer.Write(callStacks);
            serializer.Write(moduleFiles);

            serializer.Log("<WriteCollection name=\"eventPages\" count=\"" + eventPages.Count + "\">\r\n");
            serializer.Write(eventPages.Count);
            for (int i = 0; i < eventPages.Count; i++)
            {
                serializer.Write(eventPages[i].TimeQPC);
                serializer.Write(eventPages[i].Position);
            }
            serializer.Write(eventPages.Count);                 // redundant as a checksum
            serializer.Log("</WriteCollection>\r\n");
            serializer.Write(eventCount);

            serializer.Log("<Marker Name=\"eventsToStacks\"/>");
            lazyEventsToStacks.Write(serializer, delegate
            {
                serializer.Log("<WriteCollection name=\"eventsToStacks\" count=\"" + eventsToStacks.Count + "\">\r\n");
                serializer.Write(eventsToStacks.Count);
                for (int i = 0; i < eventsToStacks.Count; i++)
                {
                    EventsToStackIndex eventToStack = eventsToStacks[i];
                    Debug.Assert(i == 0 || eventsToStacks[i - 1].EventIndex <= eventsToStacks[i].EventIndex, "event list not sorted");
                    serializer.Write((int)eventToStack.EventIndex);
                    serializer.Write((int)eventToStack.CallStackIndex);
                }
                serializer.Write(eventsToStacks.Count);             // Redundant as a checksum
                serializer.Log("</WriteCollection>\r\n");
            });

            serializer.Log("<Marker Name=\"cswitchBlockingEventsToStacks\"/>");
            lazyEventsToStacks.Write(serializer, delegate
            {
                serializer.Log("<WriteCollection name=\"cswitchBlockingEventsToStacks\" count=\"" + cswitchBlockingEventsToStacks.Count + "\">\r\n");
                serializer.Write(cswitchBlockingEventsToStacks.Count);
                for (int i = 0; i < cswitchBlockingEventsToStacks.Count; i++)
                {
                    EventsToStackIndex eventToStack = cswitchBlockingEventsToStacks[i];
                    Debug.Assert(i == 0 || cswitchBlockingEventsToStacks[i - 1].EventIndex <= cswitchBlockingEventsToStacks[i].EventIndex, "event list not sorted");
                    serializer.Write((int)eventToStack.EventIndex);
                    serializer.Write((int)eventToStack.CallStackIndex);
                }
                serializer.Write(cswitchBlockingEventsToStacks.Count);             // Redundant as a checksum
                serializer.Log("</WriteCollection>\r\n");
            });

            serializer.Log("<Marker Name=\"eventsToCodeAddresses\"/>");
            lazyEventsToCodeAddresses.Write(serializer, delegate
            {
                serializer.Log("<WriteCollection name=\"eventsToCodeAddresses\" count=\"" + eventsToCodeAddresses.Count + "\">\r\n");
                serializer.Write(eventsToCodeAddresses.Count);
                foreach (EventsToCodeAddressIndex eventsToCodeAddress in eventsToCodeAddresses)
                {
                    serializer.Write((int)eventsToCodeAddress.EventIndex);
                    serializer.Write((long)eventsToCodeAddress.Address);
                    serializer.Write((int)eventsToCodeAddress.CodeAddressIndex);
                }
                serializer.Write(eventsToCodeAddresses.Count);       // Redundant as a checksum
                serializer.Log("</WriteCollection>\r\n");
            });

            serializer.Log("<WriteCollection name=\"userData\" count=\"" + userData.Count + "\">\r\n");
            serializer.Write(userData.Count);
            foreach (KeyValuePair<string, object> pair in UserData)
            {
                serializer.Write(pair.Key);
                IFastSerializable asFastSerializable = (IFastSerializable)pair.Value;
                serializer.Write(asFastSerializable);
            }
            serializer.Write(userData.Count);                   // Redundant as a checksum
            serializer.Log("</WriteCollection>\r\n");

            serializer.Write(sampleProfileInterval100ns);
            serializer.Write(osName);
            serializer.Write(osBuild);
            serializer.Write(bootTime100ns);
            serializer.Write(utcOffsetMinutes ?? int.MinValue);
            serializer.Write(hasPdbInfo);

            serializer.Log("<WriteCollection name=\"m_relatedActivityIds\" count=\"" + relatedActivityIDs.Count + "\">\r\n");
            serializer.Write(relatedActivityIDs.Count);
            for (int i = 0; i < relatedActivityIDs.Count; i++)
            {
                serializer.Write(relatedActivityIDs[i]);
            }

            serializer.Log("<WriteCollection name=\"containerIDs\" count=\"" + containerIDs.Count + "\">\r\n");
            serializer.Write(containerIDs.Count);
            for(int i=0; i<containerIDs.Count; i++)
            {
                serializer.Write(containerIDs[i]);
            }

            serializer.Log("</WriteCollection>\r\n");

            serializer.Write(truncated);
            serializer.Write((int) firstTimeInversion);
        }
        void IFastSerializable.FromStream(Deserializer deserializer)
        {
            deserializer.Log("<Marker Name=\"RawEvents\"/>");
            byte align;
            deserializer.Read(out align);
            while (align > 0)
            {
                byte zero;
                deserializer.Read(out zero);
                --align;
            }

            // Skip all the raw events.
            lazyRawEvents.Read(deserializer, null);

            deserializer.Log("<Marker Name=\"sessionStartTime\"/>");
            _syncTimeUTC = DateTime.FromFileTimeUtc(deserializer.ReadInt64());
            deserializer.Read(out pointerSize);
            deserializer.Read(out numberOfProcessors);
            deserializer.Read(out cpuSpeedMHz);
            osVersion = new Version(deserializer.ReadByte(), deserializer.ReadByte(), deserializer.ReadByte(), deserializer.ReadByte());
            deserializer.Read(out _QPCFreq);
            deserializer.Read(out sessionStartTimeQPC);
            _syncTimeQPC = sessionStartTimeQPC;
            deserializer.Read(out sessionEndTimeQPC);
            deserializer.Read(out eventsLost);
            deserializer.Read(out machineName);
            deserializer.Read(out memorySizeMeg);

            deserializer.Read(out processes);
            deserializer.Read(out threads);
            deserializer.Read(out codeAddresses);
            deserializer.Read(out stats);
            deserializer.Read(out callStacks);
            deserializer.Read(out moduleFiles);

            deserializer.Log("<Marker Name=\"eventPages\"/>");
            int count = deserializer.ReadInt();
            eventPages = new GrowableArray<EventPageEntry>(count + 1);
            EventPageEntry entry = new EventPageEntry();
            for (int i = 0; i < count; i++)
            {
                deserializer.Read(out entry.TimeQPC);
                deserializer.Read(out entry.Position);
                eventPages.Add(entry);
            }
            int checkCount = deserializer.ReadInt();
            if (count != checkCount)
            {
                throw new SerializationException("Redundant count check fail.");
            }

            deserializer.Read(out eventCount);

            lazyEventsToStacks.Read(deserializer, delegate
            {
                int stackCount = deserializer.ReadInt();
                deserializer.Log("<Marker name=\"eventToStackIndex\" count=\"" + stackCount + "\"/>");
                eventsToStacks = new GrowableArray<EventsToStackIndex>(stackCount + 1);
                EventsToStackIndex eventToStackIndex = new EventsToStackIndex();
                for (int i = 0; i < stackCount; i++)
                {
                    eventToStackIndex.EventIndex = (EventIndex)deserializer.ReadInt();
                    Debug.Assert((int)eventToStackIndex.EventIndex < eventCount);
                    eventToStackIndex.CallStackIndex = (CallStackIndex)deserializer.ReadInt();
                    eventsToStacks.Add(eventToStackIndex);
                }
                int stackCheckCount = deserializer.ReadInt();
                if (stackCount != stackCheckCount)
                {
                    throw new SerializationException("Redundant count check fail.");
                }
            });
            lazyEventsToStacks.FinishRead();        // TODO REMOVE

            lazyCswitchBlockingEventsToStacks.Read(deserializer, delegate
            {
                int stackCount = deserializer.ReadInt();
                deserializer.Log("<Marker Name=\"lazyCswitchBlockingEventsToStacks\" count=\"" + stackCount + "\"/>");
                cswitchBlockingEventsToStacks = new GrowableArray<EventsToStackIndex>(stackCount + 1);
                EventsToStackIndex eventToStackIndex = new EventsToStackIndex();
                for (int i = 0; i < stackCount; i++)
                {
                    eventToStackIndex.EventIndex = (EventIndex)deserializer.ReadInt();
                    Debug.Assert((int)eventToStackIndex.EventIndex < eventCount);
                    eventToStackIndex.CallStackIndex = (CallStackIndex)deserializer.ReadInt();
                    cswitchBlockingEventsToStacks.Add(eventToStackIndex);
                }
                int stackCheckCount = deserializer.ReadInt();
                if (stackCount != stackCheckCount)
                {
                    throw new SerializationException("Redundant count check fail.");
                }
            });
            lazyCswitchBlockingEventsToStacks.FinishRead();        // TODO REMOVE

            lazyEventsToCodeAddresses.Read(deserializer, delegate
            {
                int codeAddressCount = deserializer.ReadInt();
                deserializer.Log("<Marker Name=\"eventToCodeAddressIndex\" count=\"" + codeAddressCount + "\"/>");
                eventsToCodeAddresses = new GrowableArray<EventsToCodeAddressIndex>(codeAddressCount + 1);
                EventsToCodeAddressIndex eventToCodeAddressIndex = new EventsToCodeAddressIndex();
                for (int i = 0; i < codeAddressCount; i++)
                {
                    eventToCodeAddressIndex.EventIndex = (EventIndex)deserializer.ReadInt();
                    deserializer.ReadAddress(out eventToCodeAddressIndex.Address);
                    eventToCodeAddressIndex.CodeAddressIndex = (CodeAddressIndex)deserializer.ReadInt();
                    eventsToCodeAddresses.Add(eventToCodeAddressIndex);
                }
                int codeAddressCheckCount = deserializer.ReadInt();
                if (codeAddressCount != codeAddressCheckCount)
                {
                    throw new SerializationException("Redundant count check fail.");
                }
            });
            lazyEventsToCodeAddresses.FinishRead();        // TODO REMOVE

            count = deserializer.ReadInt();
            deserializer.Log("<Marker Name=\"userData\" count=\"" + count + "\"/>");
            for (int i = 0; i < count; i++)
            {
                string key;
                deserializer.Read(out key);
                IFastSerializable value = deserializer.ReadObject();
                userData[key] = value;
            }
            checkCount = deserializer.ReadInt();
            if (count != checkCount)
            {
                throw new SerializationException("Redundant count check fail.");
            }

            deserializer.Read(out sampleProfileInterval100ns);
            deserializer.Read(out osName);
            deserializer.Read(out osBuild);
            deserializer.Read(out bootTime100ns);
            int encodedUtcOffsetMinutes;
            deserializer.Read(out encodedUtcOffsetMinutes);
            if (encodedUtcOffsetMinutes != int.MinValue)
            {
                utcOffsetMinutes = encodedUtcOffsetMinutes;
            }

            deserializer.Read(out hasPdbInfo);

            count = deserializer.ReadInt();
            Guid guid;
            relatedActivityIDs.Clear();
            for (int i = 0; i < count; i++)
            {
                deserializer.Read(out guid);
                relatedActivityIDs.Add(guid);
            }
            containerIDs.Clear();
            count = deserializer.ReadInt();
            string containerID;
            for(int i=0; i<count; i++)
            {
                deserializer.Read(out containerID);
                containerIDs.Add(containerID);
            }
            deserializer.Read(out truncated);
            firstTimeInversion = (EventIndex) (uint) deserializer.ReadInt();
        }
        int IFastSerializableVersion.Version
        {
            get { return 73; }
        }
        int IFastSerializableVersion.MinimumVersionCanRead
        {
            // We don't support backward compatibility for now.
            get { return ((IFastSerializableVersion)this).Version; }
        }
        int IFastSerializableVersion.MinimumReaderVersion
        {
            // We don't support old readers reading new formats.
            get { return ((IFastSerializableVersion)this).Version; }
        }

        // headerSize is the size we persist of TraceEventNativeMethods.EVENT_RECORD which is up to and
        // including the UserDataLength field (after this field the fields are architecture dependent in
        // size.
        // TODO: we add 16 just to keep compatibility with the size we used before.  This is a complete
        // waste at the moment.  When we decide to break compatibility we should reclaim this.
        internal const int headerSize = 0x50 /* EVENT_HEADER */ + 4 /* ETW_BUFFER_CONTEXT */ + 4 /* 2 shorts */ + 16;

        // #TraceLogVars
        // see #TraceEventVars
        private string etlxFilePath;
        private int memorySizeMeg;
        private int eventsLost;
        private string osName;
        private string osBuild;
        private long bootTime100ns;     // This is a windows FILETIME object
        private bool hasPdbInfo;
        private bool truncated;     // stopped because the file was too large.
        private EventIndex firstTimeInversion;
        private int sampleProfileInterval100ns;
        private string machineName;
        private TraceProcesses processes;
        private TraceThreads threads;
        private TraceCallStacks callStacks;
        private TraceCodeAddresses codeAddresses;
        private TraceEventStats stats;

        private DeferedRegion lazyRawEvents;
        private DeferedRegion lazyEventsToStacks;
        private DeferedRegion lazyEventsToCodeAddresses;
        private DeferedRegion lazyCswitchBlockingEventsToStacks;
        private TraceEvents events;
        private GrowableArray<EventPageEntry> eventPages;   // The offset offset of a page
        private int eventCount;                             // Total number of events
        private bool processingDisabled;                    // Have we turned off processing because of a MaxCount?
        private int numberOnPage;                           // Total number of events
        private bool removeFromStream;                      // Don't put these in the serialized stream.
        private bool bookKeepingEvent;                      // BookKeeping events are removed from the stream by default
        private bool bookeepingEventThatMayHaveStack;       // Some bookkeeping events (ThreadDCEnd) might have stacks
        private bool noStack;                               // This event should never have a stack associated with it, so skip them if we every try to attach a stack.
        private TraceThread thread;                         // cache of the TraceThread for the current event.

        // TODO FIX NOW remove the jittedMethods ones.
        private List<MethodLoadUnloadVerboseTraceData> jittedMethods;
        private List<MethodLoadUnloadJSTraceData> jsJittedMethods;
        private Dictionary<JavaScriptSourceKey, string> sourceFilesByID;

        private TraceModuleFiles moduleFiles;
        private GrowableArray<EventsToStackIndex> eventsToStacks;
        /// <summary>
        /// The context switch event gives the stack of the thread GETTING the CPU, but it is also very useful
        /// to have this stack at the point of blocking.   cswitchBlockingEventsToStacks gives this stack.
        /// </summary>
        private GrowableArray<EventsToStackIndex> cswitchBlockingEventsToStacks;
        private GrowableArray<EventsToCodeAddressIndex> eventsToCodeAddresses;

        private TraceEventDispatcher freeLookup;    // Try to reused old ones.
        private PinnedStreamReader freeReader;

        private Dictionary<string, TraceEventParser> parsers;   // this is a set.

        // In a TraceLog, we store all the GUIDS of RelatedActivityIDs here.  When then 'point'
        // at them with the index into this array.  (see TraceLog.GetRelatedActivityID).
        internal GrowableArray<Guid> relatedActivityIDs;

        // In a TraceLog, we store all of the container IDs here and then 'point'
        // at them with the index into this array.  This is just like relatedActivityIDs above.
        // See TraceLog.GetContainerID.
        internal GrowableArray<string> containerIDs;

        #region EventPages
        internal const int eventsPerPage = 1024;    // We keep track of  where events are in 'pages' of this size.
        private struct EventPageEntry
        {
            public EventPageEntry(long TimeQPC, StreamLabel Position)
            {
                this.TimeQPC = TimeQPC;
                this.Position = Position;
            }
            public long TimeQPC;                        // Time for the first items in this page.
            public StreamLabel Position;                // Offset to this page.
        }
        #endregion

        // These classes are only used during conversion from ETL files
        // They are not needed for ETLX consumption.
        #region PastEventInfo
        private enum PastEventInfoIndex { Invalid = -1 };

        /// <summary>
        /// We need to remember the the EventIndexes of the events that were 'just before' this event so we can
        /// associate eventToStack traces with the event that actually caused them.  PastEventInfo does this.
        /// </summary>
        private struct PastEventInfo
        {
            public PastEventInfo(TraceLog log)
            {
                this.log = log;
                pastEventInfo = new PastEventInfoEntry[historySize];
                curPastEventInfo = 0;
                Debug.Assert(((historySize - 1) & historySize) == 0);       // historySize is a power of 2
            }

            public void Dispose()
            {
                pastEventInfo = null;
            }

            public void LogEvent(TraceEvent data, EventIndex eventIndex, TraceEventCounts countForEvent)
            {
                int threadID = data.ThreadIDforStacks();

                // We should be logging in event ID order.
                Debug.Assert(pastEventInfo[curPastEventInfo].EventIndex == 0 || pastEventInfo[curPastEventInfo].EventIndex == EventIndex.Invalid ||
                    pastEventInfo[curPastEventInfo].EventIndex < eventIndex);
                pastEventInfo[curPastEventInfo].ThreadID = threadID;
                pastEventInfo[curPastEventInfo].ProcessorNumber = (ushort)data.ProcessorNumber;
                Debug.Assert(pastEventInfo[curPastEventInfo].ProcessorNumber == data.ProcessorNumber);
                pastEventInfo[curPastEventInfo].QPCTime = data.TimeStampQPC;
                pastEventInfo[curPastEventInfo].EventIndex = eventIndex;
                pastEventInfo[curPastEventInfo].CountForEvent = countForEvent;
                pastEventInfo[curPastEventInfo].isClrEvent = (data.ProviderGuid == ClrTraceEventParser.ProviderGuid || data.ProviderGuid == ClrPrivateTraceEventParser.ProviderGuid);
                pastEventInfo[curPastEventInfo].hasAStack = false;
                pastEventInfo[curPastEventInfo].BlockingEventIndex = EventIndex.Invalid;
                // Remember the eventIndex of where the current thread blocks.
                CSwitchTraceData asCSwitch = data as CSwitchTraceData;
                if (asCSwitch != null)
                {
                    TraceThread newThread = log.Threads.GetOrCreateThread(asCSwitch.ThreadID, asCSwitch.TimeStampQPC, null);
                    pastEventInfo[curPastEventInfo].BlockingEventIndex = newThread.lastBlockingCSwitchEventIndex;

                    TraceThread oldThread = log.Threads.GetOrCreateThread(asCSwitch.OldThreadID, asCSwitch.TimeStampQPC, null);
                    oldThread.lastBlockingCSwitchEventIndex = eventIndex;
                }
                curPastEventInfo = (curPastEventInfo + 1) & (historySize - 1);
            }

            /// <summary>
            /// Returns the previous Event on the 'threadID'.  Events with -1 thread IDs are also always returned.
            /// Returns PastEventInfoIndex.Invalid if there are not more events to consider.
            /// </summary>
            public PastEventInfoIndex GetPreviousEventIndex(PastEventInfoIndex start, int threadID, bool exactMatch = false, EventIndex minIdx = (EventIndex)0)
            {
                int idx = (int)start;
                for (; ; )
                {
                    // Event numbers should decrease.
                    Debug.Assert(idx == curPastEventInfo || idx == 0 || pastEventInfo[idx - 1].EventIndex == EventIndex.Invalid ||
                        pastEventInfo[idx - 1].EventIndex < pastEventInfo[idx].EventIndex);

                    --idx;
                    if (idx < 0)
                    {
                        idx = historySize - 1;
                    }

                    if (idx == curPastEventInfo)
                    {
                        break;
                    }

                    var eventThreadID = pastEventInfo[idx].ThreadID;
                    if (eventThreadID == threadID || (!exactMatch && eventThreadID == -1))
                    {
                        return (PastEventInfoIndex)idx;
                    }

                    EventIndex eventIdx = pastEventInfo[idx].EventIndex;
                    if ((uint)eventIdx < (uint)minIdx || eventIdx == 0)
                    {
                        break;
                    }
                }
                return PastEventInfoIndex.Invalid;
            }

            /// <summary>
            /// Find the event event on thread threadID to the given QPC timestamp.  If there is more than
            /// one event with the same QPC, we use thread and processor number to disambiguate.
            /// </summary>
            public PastEventInfoIndex GetBestEventForQPC(long QPCTime, int threadID, int processorNumber)
            {
                // There are times when we have the same timestamp for different events, thus we need to
                // choose the best one (thread IDs match), when we also have a 'poorer' match (when we don't
                // have a thread ID for the event)
                int idx = curPastEventInfo;
                var ret = PastEventInfoIndex.Invalid;
                bool threadAndProcNumMatch = false;
                bool updateThread = false;
                for (; ; )
                {
                    --idx;
                    if (idx < 0)
                    {
                        idx = historySize - 1;
                    }

                    // We match timestamps.  This is the main criteria
                    long entryQPCTime = pastEventInfo[idx].QPCTime;
                    if (QPCTime == entryQPCTime)
                    {
                        // Next we we see if the ThreadIDs  match
                        if (threadID == pastEventInfo[idx].ThreadID)
                        {
                            if (threadAndProcNumMatch)
                            {
                                // We hope this does not happen, ambiguity: two events with the same timestamp and thread ID and processor number
                                // This seems to happen for CSWITCH and SAMPLING on the phone (where timestamps are coarse);
                                log.DebugWarn(processorNumber != pastEventInfo[idx].ProcessorNumber, "Two events with the same Timestamp " + log.QPCTimeToRelMSec(QPCTime).ToString("f4"), null);
                                return ret;
                            }

                            // Remember if we have a perfect match
                            if (processorNumber == pastEventInfo[idx].ProcessorNumber)
                            {
                                threadAndProcNumMatch = true;
                            }

                            ret = (PastEventInfoIndex)idx;
                            updateThread = false;
                        } // Some events, (like VirtualAlloc, ReadyThread) don't have the thread ID set, we will rely on just QPC and processor number.
                        else if (pastEventInfo[idx].ThreadID == -1)
                        {
                            // If we have no result yet, then use this one.   If we have a result, at least the processor numbers need to match.
                            if (ret == PastEventInfoIndex.Invalid || (!threadAndProcNumMatch && processorNumber == pastEventInfo[idx].ProcessorNumber))
                            {
                                ret = (PastEventInfoIndex)idx;
                                updateThread = true;                // we match against ThreadID == -1, remember the true thread forever.
                            }
                        }
                    }
                    else if (entryQPCTime < QPCTime)            // We can stop after we past the QPC we are looking for.
                    {
                        break;
                    }

                    if (idx == (int)curPastEventInfo)
                    {
                        break;
                    }
                }
                // Remember the thread ID that we were 'attached to'.
                if (updateThread)
                {
                    Debug.Assert(pastEventInfo[(int)ret].ThreadID == -1);
                    pastEventInfo[(int)ret].ThreadID = threadID;
                }
                return ret;
            }
            public PastEventInfoIndex CurrentIndex { get { return (PastEventInfoIndex)curPastEventInfo; } }
            public bool IsClrEvent(PastEventInfoIndex index) { return pastEventInfo[(int)index].isClrEvent; }
            public bool HasStack(PastEventInfoIndex index) { return pastEventInfo[(int)index].hasAStack; }
            public void SetHasStack(PastEventInfoIndex index) { pastEventInfo[(int)index].hasAStack = true; }
            public int GetThreadID(PastEventInfoIndex index) { return pastEventInfo[(int)index].ThreadID; }
            public EventIndex GetEventIndex(PastEventInfoIndex index) { return pastEventInfo[(int)index].EventIndex; }
            public EventIndex GetBlockingEventIndex(PastEventInfoIndex index) { return pastEventInfo[(int)index].BlockingEventIndex; }
            public TraceEventCounts GetEventCounts(PastEventInfoIndex index) { return pastEventInfo[(int)index].CountForEvent; }
            public IncompleteStack GetEventStackInfo(PastEventInfoIndex index)
            {
                var stackInfo = pastEventInfo[(int)index].EventStackInfo;
                if (stackInfo == null)
                {
                    return null;
                }

                // We reuse EventStackInfos aggressively, make sure that this one is for us
                var eventIndex = GetEventIndex(index);
                if (stackInfo.EventIndex != eventIndex || stackInfo.Thread == null)
                {
                    return null;
                }

                return stackInfo;
            }
            public void SetEventStackInfo(PastEventInfoIndex index, IncompleteStack stackInfo) { pastEventInfo[(int)index].EventStackInfo = stackInfo; }
            #region private
            // Stuff we remember about past events, mostly it is the IncompleteStack (partial stacks to be put together)
            // and the counts of each event type.
            private struct PastEventInfoEntry
            {
#if DEBUG
                public double TimeStampRelativeMSec(PastEventInfo pastEventInfo)
                { return pastEventInfo.log.QPCTimeToRelMSec(QPCTime); }
#endif
                public bool hasAStack;
                public bool isClrEvent;
                public ushort ProcessorNumber;
                public long QPCTime;
                public int ThreadID;

                public IncompleteStack EventStackInfo;   // If this event actually had a stack, this holds info about it.
                public EventIndex EventIndex;            // This can be EventIndex.Invalid for events that are going to be removed from the stream.
                public EventIndex BlockingEventIndex;    // This is non-Invalid for CSwitches and repsrensets the other thread the blocked.
                public TraceEventCounts CountForEvent;
            }

            private const int historySize = 2048;               // Must be a power of 2
            private PastEventInfoEntry[] pastEventInfo;
            private int curPastEventInfo;                       // points at the first INVALD entry.
            private TraceLog log;
            #endregion
        }
        #endregion

        #region EventsToStackIndex
        internal struct EventsToStackIndex
        {
            internal EventsToStackIndex(EventIndex eventIndex, CallStackIndex stackIndex)
            {
                Debug.Assert(eventIndex != EventIndex.Invalid);
                // We should never be returning the IDs we use to encode the thread itself.
                Debug.Assert(stackIndex == CallStackIndex.Invalid || 0 <= stackIndex);
                EventIndex = eventIndex;
                CallStackIndex = stackIndex;
            }
            internal EventIndex EventIndex;
            internal CallStackIndex CallStackIndex;
        }

        /// <summary>
        /// Add a new entry that associates the stack 'stackIndex' with the event with index 'eventIndex'
        /// </summary>
        internal void AddStackToEvent(EventIndex eventIndex, CallStackIndex stackIndex)
        {
            int whereToInsertIndex = eventsToStacks.Count;
            if (IsRealTime)
            {
                // We need the array to be sorted, we do insertion sort, which works great because you are almost always
                // the last element (or very near the end).
                // for non-real-time we do the sorting in bulk at the end of the trace.
                while (0 < whereToInsertIndex)
                {
                    --whereToInsertIndex;
                    var prevIndex = eventsToStacks[whereToInsertIndex].EventIndex;
                    if (prevIndex <= eventIndex)
                    {
                        if (prevIndex == eventIndex)
                        {
                            DebugWarn(true, "Warning, two stacks given to the same event with ID " + eventIndex + " discarding the second one", null);
                            return;
                        }
                        whereToInsertIndex++;   // insert after this index is bigger than the element compared.
                        break;
                    }
                }
            }
            // For non-realtime session we simply insert it at the end because we will sort by eventIndex as a
            // post-processing step.  see eventsToStacks.Sort in CopyRawEvents().
#if DEBUG
            for (int i = 1; i < 8; i++)
            {
                int idx = eventsToStacks.Count - i;
                if (idx < 0)
                {
                    break;
                }
                // If this assert fires, it means that we added a stack to the same event twice.   This
                // means we screwed up which event a stack belongs to.   This can happen among other reasons
                // because we complete an incomplete stack before we should and when the other stack component
                // comes in we end up logging it as if it were a unrelated stack giving two stacks to the same event.
                // Note many of these issues are reasonably benign, (e.g. we lose the kernel part of a stack)
                // so don't sweat this too much.    Because the source that we do later is not stable, which
                // of the two equal entries gets chosen will be random.
                Debug.Assert(eventsToStacks[idx].EventIndex != eventIndex);
            }
#endif
            eventsToStacks.Insert(whereToInsertIndex, new EventsToStackIndex(eventIndex, stackIndex));
        }

        private static readonly Func<EventIndex, EventsToStackIndex, int> stackComparer = delegate (EventIndex eventID, EventsToStackIndex elem)
            { return TraceEvent.Compare(eventID, elem.EventIndex); };

        #endregion

        #region EventsToCodeAddressIndex

        private struct EventsToCodeAddressIndex
        {
            public EventsToCodeAddressIndex(EventIndex eventIndex, Address address, CodeAddressIndex codeAddressIndex)
            {
                EventIndex = eventIndex;
                Address = address;
                CodeAddressIndex = codeAddressIndex;
            }
            public EventIndex EventIndex;
            public Address Address;
            public CodeAddressIndex CodeAddressIndex;
        }
        private static readonly Func<EventIndex, EventsToCodeAddressIndex, int> CodeAddressComparer = delegate (EventIndex eventIndex, EventsToCodeAddressIndex elem)
            { return TraceEvent.Compare(eventIndex, elem.EventIndex); };

        #endregion

        // These are only used when converting from ETL
        internal TraceEventDispatcher rawEventSourceToConvert;      // used to convert from raw format only.  Null for ETLX files.
        internal TraceEventDispatcher rawKernelEventSource;         // Only used by real time TraceLog on Win7.   It is the
        internal TraceLogOptions options;
        internal bool registeringStandardParsers;                   // Are we registering

        // Used for Real Time
        private struct QueueEntry
        {
            public QueueEntry(TraceEvent data, int enqueueTick) { this.data = data; this.enqueueTick = enqueueTick; }
            public TraceEvent data;
            public int enqueueTick;
        }

        internal TraceLogEventSource realTimeSource;               // used to call back in real time case.
        private Queue<QueueEntry> realTimeQueue;                   // We have to wait a bit to hook up stacks, so we put real time entries in the queue

        // These can ONLY be accessed by the thread calling RealTimeEventSource.Process();
        private Timer realTimeFlushTimer;                          // Insures the queue gets flushed even if there are no incoming events.
        private Func<TraceEvent, ulong, bool> fnAddAddressToCodeAddressMap; // PERF: Cached delegate to avoid allocations in inner loop
        #endregion
    }

    /// <summary>
    /// Represents a source for a TraceLog file (or real time stream).  It is basically a TraceEventDispatcher
    /// (TraceEventSource) but you can also get at the TraceLog for it as well.
    /// </summary>
    public class TraceLogEventSource : TraceEventDispatcher
    {
        /// <summary>
        /// Returns the TraceLog associated with this TraceLogEventSource.
        /// </summary>
        public TraceLog TraceLog { get { return events.log; } }

        /// <summary>
        /// Returns the event Index of the 'current' event (we post increment it so it is always one less)
        /// </summary>
        public EventIndex CurrentEventIndex { get { return currentID - 1; } }

        /// <summary>
        /// override
        /// </summary>
        public override bool Process()
        {
            if (TraceLog.IsRealTime)
            {
                Debug.Assert(this == TraceLog.realTimeSource);

                Task kernelTask = null;
                if (TraceLog.rawKernelEventSource != null)
                {
                    kernelTask = Task.Factory.StartNew(delegate
                    {
                        TraceLog.rawKernelEventSource.Process();
                        TraceLog.rawEventSourceToConvert.StopProcessing();
                    });
                    kernelTask.Start();
                }
                TraceLog.rawEventSourceToConvert.Process();
                if (kernelTask != null)
                {
                    TraceLog.rawKernelEventSource.StopProcessing();
                    kernelTask.Wait();
                }
                return true;
            }
            Debug.Assert(unhandledEventTemplate.traceEventSource == TraceLog);

            // This basically a foreach loop, however we cheat and substitute our own dispatcher
            // to do the lookup.  TODO: is there a better way?
            IEnumerator<TraceEvent> enumerator = ((IEnumerable<TraceEvent>)events).GetEnumerator();
            TraceEvents.EventEnumeratorBase asBase = (TraceEvents.EventEnumeratorBase)enumerator;
            currentID = asBase.lookup.currentID;
            events.log.FreeLookup(asBase.lookup);
            asBase.lookup = this;

            // We add templates for all known events if we have registered callbacks that would benefit from them.
            if ((AllEventsHasCallback || unhandledEventTemplate.Target != null) && !registeredUnhandledEvents)
            {
                events.log.AddAllTemplatesToDispatcher(this);
                registeredUnhandledEvents = true;
            }

            try
            {
                while (enumerator.MoveNext())
                {
                    Dispatch(enumerator.Current);
                    if (stopProcessing)
                    {
                        OnCompleted();
                        return false;
                    }
                }
            }
            finally
            {
                events.log.FreeReader(asBase.reader);
            }
            OnCompleted();
            return true;
        }
        /// <summary>
        /// override
        /// </summary>
        public override int EventsLost { get { return TraceLog.EventsLost; } }

#if false // TODO FIX NOW use or remove 4/2014
        // TODO FIX NOW ACTIVITIES: review
        /// <summary>
        /// Fires when a new activity is scheduled. Client code should register to this
        /// event to get one unified notification for all ETW events marking the
        /// "scheduling" of some future work.
        ///   (o) The first argument can be used with this[newActivityIndex] to
        ///       inspect details related to the activity.
        ///   (o) The second argument represents the ETW event that marked the scheduling
        ///       of work. If client code needs this for future reference it should call
        ///       TraceEvent.Clone() to store a copy.
        /// </summary>
        public event Action<ActivityIndex, TraceEvent> ActivityScheduled
        {
            add
            { activityScheduled += value; Log.TraceActivities.SubscribeToActivityTracingEvents(this, true); }
            remove
            { activityScheduled -= value; if (!HasActivitySubscriptions) Log.TraceActivities.SubscribeToActivityTracingEvents(this, false); }
        }
    /// <summary>
        /// Fires when a new activity is starting. Client code should register to this
        /// event to get one unified notification for all ETW events marking the
        /// "beginning" of work for the scheduled activity.
        ///   (o) The first argument can be used with this[ActivityIndex] to
        ///       inspect details related to the activity starting execution.
        ///   (o) The second argument represents the ETW event that marked the beginning
        ///       of work. If client code needs this for future reference it should call
        ///       TraceEvent.Clone() to store a copy.
        /// </summary>
        public event Action<ActivityIndex, TraceEvent> ActivityStarted
        {
            add
            { activityStarted += value; Log.TraceActivities.SubscribeToActivityTracingEvents(this, true); }
            remove
            { activityStarted -= value; if (!HasActivitySubscriptions) Log.TraceActivities.SubscribeToActivityTracingEvents(this, false); }
        }
        /// <summary>
        /// Fires when an activity has completed. Client code should register to this
        /// event to get one unified notification for all ETW events marking the
        /// "completion" of work for the scheduled activity.
        ///   (o) The first argument can be used with this[ActivityIndex] to
        ///       inspect details related to the activity that just completed.
        ///   (o) The second argument represents the ETW event that marked the beginning
        ///       of work. If client code needs this for future reference it should call
        ///       TraceEvent.Clone() to store a copy.
        /// </summary>
        public event Action<ActivityIndex, TraceEvent> ActivityCompleted
        {
            add
            { activityCompleted += value; Log.TraceActivities.SubscribeToActivityTracingEvents(this, true); }
            remove
            { activityCompleted -= value; if (!HasActivitySubscriptions) Log.TraceActivities.SubscribeToActivityTracingEvents(this, false); }
        }

        internal void OnActivityScheduled(ActivityIndex uai, TraceEvent data)
        { if (activityScheduled != null) activityScheduled(uai, data); }
        internal void OnActivityStarted(ActivityIndex uai, TraceEvent data)
        { if (activityScheduled != null) activityStarted(uai, data); }
        internal void OnActivityCompleted(ActivityIndex uai, TraceEvent data)
        { if (activityCompleted != null) activityCompleted(uai, data); }
        internal bool HasActivitySubscriptions { get { return activityScheduled != null || activityStarted != null || activityCompleted != null; } }
#endif

        #region private
        /// <summary>
        /// override
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (ownsItsTraceLog)
                {
                    TraceLog.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        internal override string ProcessName(int processID, long timeQPC)
        {
            return TraceLog.ProcessName(processID, timeQPC);
        }
        internal override void RegisterEventTemplateImpl(TraceEvent template)
        {
            template.traceEventSource = TraceLog;
            base.RegisterEventTemplateImpl(template);
        }

        internal override unsafe Guid GetRelatedActivityID(TraceEventNativeMethods.EVENT_RECORD* eventRecord)
        {
            return TraceLog.GetRelatedActivityID(eventRecord);
        }

        internal override unsafe string GetContainerID(TraceEventNativeMethods.EVENT_RECORD* eventRecord)
        {
            return TraceLog.GetContainerID(eventRecord);
        }

        internal TraceLogEventSource(TraceEvents events, bool ownsItsTraceLog = false)
        {
            this.events = events;
            unhandledEventTemplate.traceEventSource = TraceLog;
            userData = TraceLog.UserData;
            this.ownsItsTraceLog = ownsItsTraceLog;
        }

        private TraceEvents events;
        private bool registeredUnhandledEvents;
        internal bool ownsItsTraceLog;          // Used for real time sessions, Dispose the TraceLog if this is disposed.

        #endregion
    }

    /// <summary>
    /// TraceEventStats represents the summary statistics (counts) of all the events in the log.
    /// </summary>
    public sealed class TraceEventStats : IEnumerable<TraceEventCounts>, IFastSerializable
    {
        /// <summary>
        /// The total number of distinct event types (there will be a TraceEventCounts for each distinct event Type)
        /// </summary>
        public int Count { get { return m_counts.Count; } }
        /// <summary>
        /// An XML representation of the TraceEventStats (for Debugging)
        /// </summary>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<TraceEventStats Count=").Append(XmlUtilities.XmlQuote(Count)).AppendLine(">");
            foreach (var counts in this)
            {
                sb.Append("  ").Append(counts.ToString()).AppendLine();
            }

            sb.AppendLine("</TraceEventStats>");
            return sb.ToString();
        }

        #region private
        /// <summary>
        /// Given an event 'data' look up the statistics for events that type.
        /// </summary>
        internal TraceEventCounts GetEventCounts(TraceEvent data)
        {
            var countsForEvent = data.EventTypeUserData as TraceEventCounts;
            if (countsForEvent == null)
            {
                TraceEventCountsKey key = new TraceEventCountsKey(data);
                if (!m_counts.TryGetValue(key, out countsForEvent))
                {
                    countsForEvent = new TraceEventCounts(this, data);
                    m_counts.Add(key, countsForEvent);
                }
                if (!(data is UnhandledTraceEvent))
                {
                    data.EventTypeUserData = countsForEvent;
                }
            }
#if DEBUG
            if (data.IsClassicProvider)
            {
                Debug.Assert(countsForEvent.IsClassic);
                Debug.Assert(countsForEvent.TaskGuid == data.taskGuid);
                if (!data.lookupAsWPP)
                {
                    Debug.Assert(countsForEvent.Opcode == data.Opcode || data.Opcode == TraceEventOpcode.Info);
                }
            }
            else
            {
                Debug.Assert(!countsForEvent.IsClassic);
                Debug.Assert(countsForEvent.ProviderGuid == data.ProviderGuid);
                Debug.Assert(countsForEvent.EventID == data.ID);
            }

#endif
            return countsForEvent;
        }

        void IFastSerializable.ToStream(Serializer serializer)
        {
            serializer.Write(m_log);
            serializer.Write(m_counts.Count);
            foreach (var counts in m_counts.Values)
            {
                serializer.Write(counts);
            }
        }
        void IFastSerializable.FromStream(Deserializer deserializer)
        {
            deserializer.Read(out m_log);
            m_counts.Clear();
            int count = deserializer.ReadInt();
            for (int i = 0; i < count; i++)
            {
                TraceEventCounts elem; deserializer.Read(out elem);
                m_counts.Add(elem.m_key, elem);
            }
        }

        IEnumerator<TraceEventCounts> IEnumerable<TraceEventCounts>.GetEnumerator()
        {
            return m_counts.Values.GetEnumerator();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { throw new NotImplementedException(); }
        internal TraceEventStats(TraceLog log)
        {
            m_counts = new Dictionary<TraceEventCountsKey, TraceEventCounts>();
            m_log = log;
        }

        private Dictionary<TraceEventCountsKey, TraceEventCounts> m_counts;
        internal TraceLog m_log;
        #endregion
    }

    [StructLayout(LayoutKind.Auto)]
    internal struct TraceEventCountsKey : IEquatable<TraceEventCountsKey>
    {
        public readonly bool m_classicProvider;     // This changes the meaning of m_providerGuid and m_eventId;
        public readonly Guid m_providerGuid;        // If classic this is task Guid
        public readonly TraceEventID m_eventId;              // If classic this is the opcode

        public unsafe TraceEventCountsKey(TraceEvent data)
        {
            m_classicProvider = data.IsClassicProvider;
            if (m_classicProvider)
            {
                m_providerGuid = data.taskGuid;

                // We use the sum of the opcode and eventID so that it works with WPP as well as classic.
                Debug.Assert(data.eventRecord->EventHeader.Id == 0 || data.eventRecord->EventHeader.Opcode == 0);
                m_eventId = (TraceEventID)(data.eventRecord->EventHeader.Id + data.eventRecord->EventHeader.Opcode);
            }
            else
            {
                m_providerGuid = data.ProviderGuid;
                m_eventId = data.ID;
            }
        }

        internal TraceEventCountsKey(Deserializer deserializer)
        {
            deserializer.Read(out m_providerGuid);
            m_eventId = (TraceEventID)deserializer.ReadInt();
            deserializer.Read(out m_classicProvider);
        }

        public bool Equals(TraceEventCountsKey other)
        {
            return m_eventId == other.m_eventId &&
                   m_classicProvider == other.m_classicProvider &&
                   m_providerGuid == other.m_providerGuid;
        }

        public override bool Equals(object obj)
        {
            return obj is TraceEventCountsKey && Equals((TraceEventCountsKey)obj);
        }

        public override int GetHashCode()
        {
            return unchecked(m_providerGuid.GetHashCode() + (int)m_eventId);
        }

        public void Serialize(Serializer serializer)
        {
            serializer.Write(m_providerGuid);
            serializer.Write((int)m_eventId);
            serializer.Write(m_classicProvider);
        }
    }

    /// <summary>
    /// TraceEventCount holds number of events (Counts) and the number of events with call stacks associated with them (StackCounts) for a particular event type.
    /// <para>It also has properties for looking up the event and provider names, but this information can only be complete if all the TraceEventParsers needed
    /// were associated with the TraceLog instance.
    /// </para>
    /// </summary>
    public sealed class TraceEventCounts : IFastSerializable
    {
        /// <summary>
        /// Returns a provider name for events in this TraceEventCounts.   It may return a string with a GUID or even
        /// UnknownProvider for classic ETW if the event is unknown to the TraceLog.
        /// </summary>
        public string ProviderName
        {
            get
            {
                var template = Template;
                if (template == null)
                {
                    var name = ((ITraceParserServices)m_stats.m_log).ProviderNameForGuid(m_key.m_providerGuid);
                    if (name != null)
                    {
                        return name;
                    }

                    if (m_key.m_classicProvider)
                    {
                        return "UnknownProvider";
                    }

                    return "Provider(" + m_key.m_providerGuid.ToString() + ")";
                }
                return template.ProviderName;
            }
        }
        /// <summary>
        /// Returns a name for events in this TraceEventCounts.   If the event is unknown to the Tracelog
        /// it will return EventID(XXX) (for manifest based events) or Task(XXX)/Opcode(XXX) (for classic events)
        /// </summary>
        public string EventName
        {
            get
            {
                var template = Template;
                if (template == null)
                {
                    if (m_key.m_classicProvider)
                    {
                        var taskName = ((ITraceParserServices)m_stats.m_log).TaskNameForGuid(m_key.m_providerGuid);
                        if (taskName == null)
                        {
                            taskName = "Task(" + m_key.m_providerGuid.ToString() + ")";
                        }

                        if (m_key.m_eventId == 0)
                        {
                            return taskName;
                        }

                        return taskName + "/Opcode(" + ((int)m_key.m_eventId).ToString() + ")";
                    }
                    if (m_key.m_eventId == 0)
                    {
                        return "EventWriteString";
                    }

                    return "EventID(" + ((int)m_key.m_eventId).ToString() + ")";
                }
                return template.EventName;
            }
        }

        /// <summary>
        /// Returns the payload names associated with this Event type.   Returns null if the payload names are unknown.
        /// </summary>
        public string[] PayloadNames
        {
            get
            {
                var template = Template;
                if (template == null)
                {
                    return null;
                }

                return template.PayloadNames;
            }
        }
        /// <summary>
        /// Returns true the provider associated with this TraceEventCouts is a classic (not manifest based) ETW provider.
        /// </summary>
        public bool IsClassic { get { return m_key.m_classicProvider; } }

        /// <summary>
        /// Returns the provider GUID of the events in this TraceEventCounts.  Returns Guid.Empty if IsClassic
        /// </summary>
        public Guid ProviderGuid
        {
            get
            {
                if (m_key.m_classicProvider)
                {
                    return Guid.Empty;
                }
                else
                {
                    return m_key.m_providerGuid;
                }
            }
        }
        /// <summary>
        /// Returns the event ID of the events in this TraceEventCounts.  Returns TraceEventID.Illegal if IsClassic
        /// </summary>
        public TraceEventID EventID
        {
            get
            {
                if (m_key.m_classicProvider)
                {
                    return TraceEventID.Illegal;
                }
                else
                {
                    return m_key.m_eventId;
                }
            }
        }
        /// <summary>
        /// Returns the Task GUID of the events in this TraceEventCounts.  Returns Guid.Empty if not IsClassic
        /// </summary>
        public Guid TaskGuid
        {
            get
            {
                if (m_key.m_classicProvider)
                {
                    return m_key.m_providerGuid;
                }
                else
                {
                    return Guid.Empty;
                }
            }
        }
        /// <summary>
        /// Returns the Opcode of the events in the TraceEventCounts.  Returns TraceEventOpcode.Info if not IsClassic
        /// </summary>
        public TraceEventOpcode Opcode
        {
            get
            {
                if (m_key.m_classicProvider)
                {
                    return (TraceEventOpcode)m_key.m_eventId;
                }
                else
                {
                    return TraceEventOpcode.Info;
                }
            }
        }

        /// <summary>
        /// Returns the average size of the event specific payload data (not the whole event) for all events in the TraceEventsCounts.
        /// </summary>
        public double AveragePayloadSize { get { return ((double)m_eventDataLenTotal) / m_count; } }
        /// <summary>
        /// Returns the number of events in the TraceEventCounts.
        /// </summary>
        public int Count { get { return m_count; } }
        /// <summary>
        /// Returns the number of events in the TraceEventCounts that have stack traces associated with them.
        /// </summary>
        public int StackCount { get { return m_stackCount; } }
        /// <summary>
        /// Returns the full name of the event (ProviderName/EventName)
        /// </summary>
        public string FullName
        {
            get
            {
                return ProviderName + "/" + EventName;
            }
        }
        /// <summary>
        /// An XML representation  of the top level statistics of the TraceEventCounts.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<TraceEventCounts");
            // TODO put in GUID, ID?
            sb.Append(" ProviderName=").Append(XmlUtilities.XmlQuote(ProviderName));
            sb.Append(" EventName=").Append(XmlUtilities.XmlQuote(EventName));
            sb.Append(" Count=").Append(XmlUtilities.XmlQuote(Count));
            sb.Append(" StackCount=").Append(XmlUtilities.XmlQuote(StackCount));
            sb.AppendLine("/>");
            return sb.ToString();
        }
        #region private
        private TraceEvent Template
        {
            get
            {
                if (!m_templateInited)
                {
                    var lookup = m_stats.m_log.AllocLookup();
                    m_template = lookup.LookupTemplate(m_key.m_providerGuid, m_key.m_eventId);
                    m_stats.m_log.FreeLookup(lookup);
                    m_templateInited = true;
                }
                return m_template;
            }
        }

        internal unsafe TraceEventCounts(TraceEventStats stats, TraceEvent data)
        {
            if (data == null)       // This happens in the deserialization case.
            {
                return;
            }

            m_stats = stats;
            m_key = new TraceEventCountsKey(data);
        }

        /// <summary>
        /// GetHashCode
        /// </summary>
        public override int GetHashCode()
        {
            return m_key.GetHashCode();
        }

        void IFastSerializable.ToStream(Serializer serializer)
        {
            serializer.Write(m_stats);
            m_key.Serialize(serializer);
            serializer.Write(m_count);
            serializer.Write(m_stackCount);
            serializer.Write(m_eventDataLenTotal);
        }
        void IFastSerializable.FromStream(Deserializer deserializer)
        {
            deserializer.Read(out m_stats);
            m_key = new TraceEventCountsKey(deserializer);
            deserializer.Read(out m_count);
            deserializer.Read(out m_stackCount);
            deserializer.Read(out m_eventDataLenTotal);
        }

        private TraceEventStats m_stats;             // provides the context to get the template (more info about event like its name)
        internal TraceEventCountsKey m_key;

        internal long m_eventDataLenTotal;
        internal int m_count;
        internal int m_stackCount;

        // Not serialized
        private bool m_templateInited;
        private TraceEvent m_template;
        #endregion
    }

    /// <summary>
    /// A TraceEvents represents a list of TraceEvent instances.  It is IEnumerable&lt;TraceEvent&gt; but
    /// also has additional useful ways of filtering the list.
    ///
    /// Note that the TraceEvent returned from this IEnumerable may only be used for one iteration of the foreach.
    /// (it is reused for the next event).  If you need more lifetime than that you must call Clone() (see 'Lifetime
    /// Constraints' in the programmers guide for more).
    /// </summary>
    public sealed class TraceEvents : IEnumerable<TraceEvent>
    {
        /// <summary>
        /// Returns a list of events in the TraceEvents that return a payload of type T.   Thus
        /// ByEventType &lt; TraceEvent &gt; returns all events.
        /// </summary>
        public IEnumerable<T> ByEventType<T>() where T : TraceEvent
        {
            foreach (TraceEvent anEvent in this)
            {
                T asTypedEvent = anEvent as T;
                if (asTypedEvent != null)
                {
                    yield return asTypedEvent;
                }
            }
        }
        /// <summary>
        /// Returns a TraceEventDispatcher (a push model object on which you can register
        /// callbacks for particular events) that will push all the vents in the TraceEvents.
        ///
        /// Note that the TraceEvent returned from this callback may only be used for the duration of the callback.
        /// If you need more lifetime than that you must call Clone() (see 'Lifetime Constraints' in the programmers guide for more).
        /// </summary>
        public TraceLogEventSource GetSource() { return new TraceLogEventSource(this); }
        /// <summary>
        /// Returns a new list which is the same as the TraceEvents but the events are
        /// delivered from last to first.  This allows you to search backwards in the
        /// event stream.
        /// </summary>
        public TraceEvents Backwards()
        {
            return new TraceEvents(log, startTimeQPC, endTimeQPC, predicate, true);
        }
        /// <summary>
        /// Filter the events by time.  Both starTime and endTime are inclusive.
        /// </summary>
        public TraceEvents FilterByTime(DateTime startTime, DateTime endTime)
        {
            // +1 because DateTimeToQPC will truncate and we want to avoid roundoff exclusion (round up)
            return Filter(log.UTCDateTimeToQPC(startTime.ToUniversalTime()), log.UTCDateTimeToQPC(endTime.ToUniversalTime()) + 1, null);
        }
        /// <summary>
        /// Filter the events by time.  StartTimeRelativeMSec and endTimeRelativeMSec are relative to the SessionStartTime and are inclusive.
        /// </summary>
        public TraceEvents FilterByTime(double startTimeRelativeMSec, double endTimeRelativeMSec)
        {
            // +1 because DateTimeToQPC will truncate and we want to avoid roundoff exclusion (round up)
            return Filter(log.RelativeMSecToQPC(startTimeRelativeMSec), log.RelativeMSecToQPC(endTimeRelativeMSec) + 1, null);
        }
        /// <summary>
        /// Create new list of Events that has all the events in the current TraceEvents
        /// that pass the given predicate.
        /// </summary>
        public TraceEvents Filter(Predicate<TraceEvent> predicate)
        {
            return Filter(0, long.MaxValue, predicate);
        }

        /// <summary>
        /// Returns the TraceLog associated with the events in the TraceEvents
        /// </summary>
        public TraceLog Log { get { return log; } }
        /// <summary>
        /// Returns a time that is guaranteed  to be before the first event in the TraceEvents list.
        /// It is returned as DateTime
        /// </summary>
        public DateTime StartTime { get { return log.QPCTimeToDateTimeUTC(startTimeQPC).ToLocalTime(); } }
        /// <summary>
        /// Returns a time that is guaranteed to be before the first event in the TraceEvents list.
        /// It is returned as floating point number of MSec since the start of the TraceLog
        /// </summary>
        public double StartTimeRelativeMSec { get { return log.QPCTimeToRelMSec(startTimeQPC); } }

        /// <summary>
        /// Returns a time that is guaranteed to be after the last event in the TraceEvents list.
        /// It is returned as DateTime
        /// </summary>
        public DateTime EndTime { get { return log.QPCTimeToDateTimeUTC(endTimeQPC).ToLocalTime(); } }
        /// <summary>
        /// Returns a time that is guaranteed to be after the last event in the TraceEvents list.
        /// It is returned as floating point number of MSec since the start of the TraceLog
        /// </summary>
        public double EndTimeRelativeMSec { get { return log.QPCTimeToRelMSec(endTimeQPC); } }

        #region private

        IEnumerator<TraceEvent> IEnumerable<TraceEvent>.GetEnumerator()
        {
            if (log.IsRealTime)
            {
                throw new NotSupportedException("Enumeration is not supported on real time sessions.");
            }

            if (backwards)
            {
                return new TraceEvents.BackwardEventEnumerator(this);
            }
            else
            {
                return new TraceEvents.ForwardEventEnumerator(this);
            }
        }

        internal TraceEvents(TraceLog log)
        {
            this.log = log;
            endTimeQPC = long.MaxValue - 10 * log.QPCFreq; // ten seconds from infinity
        }
        internal TraceEvents(TraceLog log, long startTimeQPC, long endTimeQPC, Predicate<TraceEvent> predicate, bool backwards)
        {
            this.log = log;
            this.startTimeQPC = startTimeQPC;
            this.endTimeQPC = endTimeQPC;
            this.predicate = predicate;
            this.backwards = backwards;
        }

        internal TraceEvents Filter(long startTimeQPC, long endTimeQPC, Predicate<TraceEvent> predicate)
        {
            // merge the two predicates
            if (predicate == null)
            {
                predicate = this.predicate;
            }
            else if (this.predicate != null)
            {
                Predicate<TraceEvent> predicate1 = this.predicate;
                Predicate<TraceEvent> predicate2 = predicate;
                predicate = delegate (TraceEvent anEvent)
                {
                    return predicate1(anEvent) && predicate2(anEvent);
                };
            }
            return new TraceEvents(log,
                Math.Max(startTimeQPC, this.startTimeQPC),
                Math.Min(endTimeQPC, this.endTimeQPC),
                predicate, backwards);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            throw new NotImplementedException(); // GetEnumerator
        }

        internal abstract class EventEnumeratorBase
        {
            protected EventEnumeratorBase(TraceEvents events)
            {
                this.events = events;
                reader = events.Log.AllocReader();
                lookup = events.Log.AllocLookup();
            }
            public TraceEvent Current { get { return current; } }
            public void Dispose()
            {
                events.Log.FreeReader(reader);
                events.Log.FreeLookup(lookup);
            }
            public void Reset()
            {
                throw new Exception("The method or operation is not implemented.");
            }
            protected unsafe TraceEvent GetNext()
            {
                TraceEventNativeMethods.EVENT_RECORD* ptr = (TraceEventNativeMethods.EVENT_RECORD*)reader.GetPointer(TraceLog.headerSize);
                TraceEvent ret = lookup.Lookup(ptr);

                // We use the first item in the linked list in 'ret'.   This should always be the 'best' way of decoding
                // (that it is a static template if that exists, otherwise a DynamicTraceEvent).   This is because we
                // only add DynamicTraceEvents lazily and thus only when traversing the list of events.  Static templates
                // should have been registered before any traversing happens and thus will be first.

                // This first check is just a perf optimization so in the common case we don't to
                // the extra logic
                if (ret.opcode == unchecked((TraceEventOpcode)(-1)))
                {
                    UnhandledTraceEvent unhandled = ret as UnhandledTraceEvent;
                    if (unhandled != null)
                    {
                        unhandled.PrepForCallback();
                    }
                }
                Debug.Assert(ret.traceEventSource == events.log);

                // Confirm we have a half-way sane event, to catch obvious loss of sync.
                Debug.Assert(ret.Level <= (TraceEventLevel)64);
                Debug.Assert(ret.Version <= 10 || ret.Version == 255);  // some events had a wacky version number

#if false // TODO FIX NOW remove or fix
                // TODO 50000000 arbitrary.   Fix underlying problem with merged ETL files.
                Debug.Assert(ret.TimeStampQPC == long.MaxValue ||
                    events.Log.sessionStartTimQPC <= ret.TimeStampQPC && ret.TimeStampQPC <= events.Log.sessionEndTimeQPC + 50000000);
#endif

                // We have to insure we have a pointer to the whole blob, not just the header.
                int totalLength = TraceLog.headerSize + (ret.EventDataLength + 3 & ~3);
                Debug.Assert(totalLength < 0x10000);
                ret.eventRecord = (TraceEventNativeMethods.EVENT_RECORD*)reader.GetPointer(totalLength);
                ret.userData = TraceEventRawReaders.Add((IntPtr)ret.eventRecord, TraceLog.headerSize);
                reader.Skip(totalLength);

                ret.DebugValidate();
                return ret;
            }

            protected TraceEvent current;
            protected TraceEvents events;
            protected internal PinnedStreamReader reader;
            protected internal TraceEventDispatcher lookup;
            protected StreamLabel[] positions;
            protected int indexOnPage;
            protected int pageIndex;
        }

        internal sealed class ForwardEventEnumerator : EventEnumeratorBase, IEnumerator<TraceEvent>
        {
            public ForwardEventEnumerator(TraceEvents events)
                : base(events)
            {
                pageIndex = events.Log.FindPageIndex(events.startTimeQPC);
                events.Log.SeekToTimeOnPage(reader, events.startTimeQPC, pageIndex, out indexOnPage, positions);
                lookup.currentID = (EventIndex)(pageIndex * TraceLog.eventsPerPage + indexOnPage);
            }
            public bool MoveNext()
            {
                for (; ; )
                {
                    current = GetNext();
                    if (current.TimeStampQPC == long.MaxValue || current.TimeStampQPC > events.endTimeQPC)
                    {
                        return false;
                    }

                    // TODO confirm this works with nested predicates
                    if (events.predicate == null || events.predicate(current))
                    {
                        return true;
                    }
                }
            }
            public new object Current { get { return current; } }
        }

        internal sealed class BackwardEventEnumerator : EventEnumeratorBase, IEnumerator<TraceEvent>
        {
            public BackwardEventEnumerator(TraceEvents events)
                : base(events)
            {
                long endTime = events.endTimeQPC;
                if (endTime != long.MaxValue)
                {
                    endTime++;
                }

                pageIndex = events.Log.FindPageIndex(endTime);
                positions = new StreamLabel[TraceLog.eventsPerPage];
                events.Log.SeekToTimeOnPage(reader, endTime, pageIndex, out indexOnPage, positions);
            }
            public bool MoveNext()
            {
                for (; ; )
                {
                    if (indexOnPage == 0)
                    {
                        if (pageIndex == 0)
                        {
                            return false;
                        }

                        --pageIndex;
                        events.Log.SeekToTimeOnPage(reader, long.MaxValue, pageIndex, out indexOnPage, positions);
                    }
                    else
                    {
                        --indexOnPage;
                    }

                    reader.Goto(positions[indexOnPage]);
                    lookup.currentID = (EventIndex)(pageIndex * TraceLog.eventsPerPage + indexOnPage);
                    current = GetNext();

                    if (current.TimeStampQPC < events.startTimeQPC)
                    {
                        return false;
                    }

                    // TODO confirm this works with nested predicates
                    if (events.predicate == null || events.predicate(current))
                    {
                        return true;
                    }
                }
            }
            public new object Current { get { return current; } }
        }

        // #TraceEventVars
        // see #TraceLogVars
        internal TraceLog log;
        internal long startTimeQPC;
        internal long endTimeQPC;
        internal Predicate<TraceEvent> predicate;
        internal bool backwards;
        #endregion
    }
    /// <summary>
    /// Each process is given a unique index from 0 to TraceProcesses.Count-1 and unlike
    /// the OS Process ID, is  unambiguous (The OS process ID can be reused after a
    /// process dies).  ProcessIndex represents this index.   By using an enum rather than an int
    /// it allows stronger typing and reduces the potential for errors.
    /// <para>
    /// It is expected that users of this library might keep arrays of size TraceProcesses.Count to store
    /// additional data associated with a process in the trace.
    /// </para>
    /// </summary>
    public enum ProcessIndex
    {
        /// <summary>
        /// Returned when no appropriate Process exists.
        /// </summary>
        Invalid = -1
    };

    /// <summary>
    /// A TraceProcesses instance represents the list of processes in the Event log.
    ///
    /// TraceProcesses are IEnumerable, and will return the processes in order of creation time.
    /// </summary>
    public sealed class TraceProcesses : IEnumerable<TraceProcess>, IFastSerializable
    {
        /// <summary>
        /// The log associated with this collection of processes.
        /// </summary>
        public TraceLog Log { get { return log; } }
        /// <summary>
        /// The count of the number of TraceProcess instances in the TraceProcesses list.
        /// </summary>
        public int Count { get { return processes.Count; } }
        /// <summary>
        /// Each process that occurs in the log is given a unique index (which unlike the PID is unique), that
        /// ranges from 0 to Count - 1.   Return the TraceProcess for the given index.
        /// </summary>
        public TraceProcess this[ProcessIndex processIndex]
        {
            get
            {
                if (processIndex == ProcessIndex.Invalid)
                {
                    return null;
                }

                return processes[(int)processIndex];
            }
        }

        /// <summary>
        /// Given an OS process ID and a time, return the last TraceProcess that has the same process ID,
        /// and whose process start time is less than 'timeRelativeMSec'.
        /// <para>
        /// If 'timeRelativeMSec' is during the processes's lifetime this is guaranteed to be the correct process.
        /// for the given process ID since process IDs are unique during the lifetime of the process.
        /// </para><para>
        /// If timeRelativeMSec == TraceLog.SessionDuration this method will return the last process with
        /// the given process ID, even if it had died during the trace.
        /// </para>
        /// </summary>
        public TraceProcess GetProcess(int processID, double timeRelativeMSec)
        {
            int index;
            var ret = FindProcessAndIndex(processID, log.RelativeMSecToQPC(timeRelativeMSec), out index);
            return ret;
        }
        /// <summary>
        /// Returns the last process in the log with the given process ID.  Useful when the logging session
        /// was stopped just after the processes completed (a common scenario).
        /// </summary>
        public TraceProcess LastProcessWithID(int processID)
        {
            return GetProcess(processID, Log.sessionEndTimeQPC);
        }
        /// <summary>
        /// Find the first process in the trace that has the process name 'processName' and whose process
        /// start time is after the given point in time.
        /// <para>A process's name is the file name of the EXE without the extension.</para>
        /// <para>Processes that began before the trace started have a start time of 0,  Thus
        /// specifying 0 for the time will include processes that began before the trace started.
        /// </para>
        /// </summary>
        public TraceProcess FirstProcessWithName(string processName, double afterTimeRelativeMSec = 0)
        {
            long afterTimeQPC = log.RelativeMSecToQPC(afterTimeRelativeMSec);
            for (int i = 0; i < Count; i++)
            {
                TraceProcess process = processes[i];
                if (afterTimeQPC <= process.startTimeQPC &&
                    string.Compare(process.Name, processName, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    return process;
                }
            }
            return null;
        }
        /// <summary>
        /// Find the last process in the trace that has the process name 'processName' and whose process
        /// start time is after the given point in time.
        /// <para>A process's name is the file name of the EXE without the extension.</para>
        /// <para>Processes that began before the trace started have a start time of 0,  Thus
        /// specifying 0 for the time will include processes that began before the trace started.
        /// </para>
        /// </summary>
        public TraceProcess LastProcessWithName(string processName, double afterTimeRelativeMSec = 0)
        {
            long afterTimeQPC = log.RelativeMSecToQPC(afterTimeRelativeMSec);
            TraceProcess ret = null;
            for (int i = 0; i < Count; i++)
            {
                TraceProcess process = processes[i];
                if (afterTimeQPC <= process.startTimeQPC &&
                    string.Compare(process.Name, processName, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    ret = process;
                }
            }
            return ret;
        }

        /// <summary>
        /// An XML representation of the TraceEventProcesses (for debugging)
        /// </summary>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<TraceProcesses Count=").Append(XmlUtilities.XmlQuote(Count)).AppendLine(">");
            foreach (TraceProcess process in this)
            {
                sb.Append("  ").Append(process.ToString()).AppendLine();
            }

            sb.AppendLine("</TraceProcesses>");
            return sb.ToString();
        }
        #region Private
        /// <summary>
        /// Enumerate all the processes that occurred in the trace log, ordered by creation time.
        /// </summary>
        IEnumerator<TraceProcess> IEnumerable<TraceProcess>.GetEnumerator()
        {
            for (int i = 0; i < processes.Count; i++)
            {
                yield return processes[i];
            }
        }
        /// <summary>
        /// Given an OS process ID and a time, return the last TraceProcess that has the same process ID,
        /// and whose offset start time is less than 'timeQPC'. If 'timeQPC' is during the thread's lifetime this
        /// is guaranteed to be the correct process. Using timeQPC = TraceLog.sessionEndTimeQPC will return the
        /// last process with the given PID, even if it had died.
        /// </summary>

        internal TraceProcess GetProcess(int processID, long timeQPC)
        {
            int index;
            var ret = FindProcessAndIndex(processID, timeQPC, out index);
            return ret;
        }
        /// <summary>
        /// TraceProcesses represents the entire ETL moduleFile log.   At the node level it is organized by threads.
        ///
        /// The TraceProcesses also is where we put various caches that are independent of the process involved.
        /// These include a cache for TraceModuleFile that represent native images that can be loaded into a
        /// process, as well as the process lookup tables and a cache that remembers the last calls to
        /// GetNameForAddress().
        /// </summary>
        internal TraceProcesses(TraceLog log)
        {
            this.log = log;
            processes = new GrowableArray<TraceProcess>(64);
            processesByPID = new GrowableArray<TraceProcess>(64);
        }
        internal TraceProcess GetOrCreateProcess(int processID, long timeQPC, bool isProcessStartEvent = false)
        {
            Debug.Assert(processes.Count == processesByPID.Count);
            int index;
            TraceProcess retProcess = FindProcessAndIndex(processID, timeQPC, out index);
            if (retProcess == null || isProcessStartEvent)
            {
                // We can have events before process start, (sigh) so fix that.
                if (retProcess != null && isProcessStartEvent)
                {
                    // If the process entry we found does not have a start or an end, then it is orphaned
                    if (retProcess.startTimeQPC == 0 && retProcess.endTimeQPC >= long.MaxValue)
                    {
                        // it should be within 10msec (or it is the Process DCStart and this firstEvent was the log header (which has time offset 0
                        log.DebugWarn(timeQPC - retProcess.firstEventSeenQPC < log.QPCFreq / 100 || retProcess.firstEventSeenQPC == 0,
                            "Events occurred > 10msec before process " + processID.ToString() +
                            " start at " + log.QPCTimeToRelMSec(retProcess.firstEventSeenQPC).ToString("n3") + " msec", null);
                        return retProcess;
                    }
                }
                retProcess = new TraceProcess(processID, log, (ProcessIndex)processes.Count);
                retProcess.firstEventSeenQPC = timeQPC;
                processes.Add(retProcess);
                processesByPID.Insert(index + 1, retProcess);
            }
            return retProcess;
        }
        internal TraceProcess FindProcessAndIndex(int processID, long timeQPC, out int index)
        {
            if (processesByPID.BinarySearch(processID, out index, compareByProcessID))
            {
                for (int candidateIndex = index; candidateIndex >= 0; --candidateIndex)
                {
                    TraceProcess candidate = processesByPID[candidateIndex];
                    if (candidate.ProcessID != processID)
                    {
                        break;
                    }

                    // Sadly we can have some kernel events a bit before the process start event.   Thus we need the minimum
                    if (candidate.startTimeQPC <= timeQPC || candidate.firstEventSeenQPC <= timeQPC)
                    {
                        index = candidateIndex;
                        return candidate;
                    }
                }
            }
            return null;
        }

        // State variables.
        private GrowableArray<TraceProcess> processes;          // The threads ordered in time.
        private GrowableArray<TraceProcess> processesByPID;     // The threads ordered by processID.
        private TraceLog log;

        private static readonly Func<int, TraceProcess, int> compareByProcessID = delegate (int processID, TraceProcess process)
        {
            return (processID - process.ProcessID);
        };
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            for (int i = 0; i < processes.Count; i++)
            {
                yield return processes[i];
            }
        }

        void IFastSerializable.ToStream(Serializer serializer)
        {
            serializer.Write(log);
            serializer.Log("<WriteCollection name=\"Processes\" count=\"" + processes.Count + "\">\r\n");
            serializer.Write(processes.Count);
            for (int i = 0; i < processes.Count; i++)
            {
                serializer.Write(processes[i]);
            }

            serializer.Log("</WriteCollection>\r\n");

            serializer.Log("<WriteCollection name=\"ProcessesByPID\" count=\"" + processesByPID.Count + "\">\r\n");
            serializer.Write(processesByPID.Count);
            for (int i = 0; i < processesByPID.Count; i++)
            {
                serializer.Write(processesByPID[i]);
            }

            serializer.Log("</WriteCollection>\r\n");
        }
        void IFastSerializable.FromStream(Deserializer deserializer)
        {
            deserializer.Read(out log);

            Debug.Assert(processes.Count == 0);
            int count = deserializer.ReadInt();
            processes = new GrowableArray<TraceProcess>(count + 1);
            for (int i = 0; i < count; i++)
            {
                TraceProcess elem; deserializer.Read(out elem);
                processes.Add(elem);
            }

            count = deserializer.ReadInt();
            processesByPID = new GrowableArray<TraceProcess>(count + 1);
            for (int i = 0; i < count; i++)
            {
                TraceProcess elem; deserializer.Read(out elem);
                processesByPID.Add(elem);
            }
        }

        #endregion
    }

    /// <summary>
    /// A TraceProcess represents a process in the trace.
    /// </summary>
    public sealed class TraceProcess : IFastSerializable
    {
        /// <summary>
        /// The OS process ID associated with the process. It is NOT unique across the whole log.  Use
        /// ProcessIndex for that.
        /// </summary>
        public int ProcessID { get { return processID; } }
        /// <summary>
        /// The index into the logical array of TraceProcesses for this process. Unlike ProcessID (which
        /// may be reused after the process dies, the process index is unique in the log.
        /// </summary>
        public ProcessIndex ProcessIndex { get { return processIndex; } }
        /// <summary>
        /// This is a short name for the process.  It is the image file name without the path or suffix.
        /// </summary>
        public string Name
        {
            get
            {
                if (name == null)
                {
                    name = TraceLog.GetFileNameWithoutExtensionNoIllegalChars(ImageFileName);
                    if (name.Length == 0 && ProcessID != -1 && processID != 0)  // These special cases are so I don't have to rebaseline the tests.
                        name = "Process(" + ProcessID + ")";
                }

                return name;
            }
        }
        /// <summary>
        /// The command line that started the process (may be empty string if unknown)
        /// </summary>
        public string CommandLine { get { return commandLine; } }
        /// <summary>
        /// The path name of the EXE that started the process (may be empty string if unknown)
        /// </summary>
        public string ImageFileName { get { return imageFileName; } }
        /// <summary>
        /// The time when the process started.  Returns the time the trace started if the process existed when the trace started.
        /// </summary>
        public DateTime StartTime { get { return log.QPCTimeToDateTimeUTC(startTimeQPC).ToLocalTime(); } }
        /// <summary>
        /// The time when the process started.  Returns the time the trace started if the process existed when the trace started.
        /// Returned as the number of MSec from the beginning of the trace.
        /// </summary>
        public double StartTimeRelativeMsec { get { return log.QPCTimeToRelMSec(startTimeQPC); } }
        /// <summary>
        /// The time when the process ended.  Returns the time the trace ended if the process existed when the trace ended.
        /// Returned as a DateTime
        /// </summary>
        public DateTime EndTime { get { return log.QPCTimeToDateTimeUTC(endTimeQPC).ToLocalTime(); } }
        /// <summary>
        /// The time when the process ended.  Returns the time the trace ended if the process existed when the trace ended.
        /// Returned as the number of MSec from the beginning of the trace.
        /// </summary>
        public double EndTimeRelativeMsec { get { return log.QPCTimeToRelMSec(endTimeQPC); } }
        /// <summary>
        /// The process ID of the parent process
        /// </summary>
        public int ParentID { get { return parentID; } }
        /// <summary>
        /// The process that started this process.  Returns null if unknown    Unlike ParentID
        /// the chain of Parent's will never form a loop.
        /// </summary>
        public TraceProcess Parent { get { return parent; } }
        /// <summary>
        /// If the process exited, the exit status of the process.  Otherwise null.
        /// </summary>
        public int? ExitStatus { get { return exitStatus; } }
        /// <summary>
        /// The amount of CPU time spent in this process based on the kernel CPU sampling events.
        /// </summary>
        public float CPUMSec { get { return (float)(cpuSamples * Log.SampleProfileInterval.TotalMilliseconds); } }
        /// <summary>
        /// Returns true if the process is a 64 bit process
        /// </summary>
        public bool Is64Bit
        {
            get
            {
                // We are 64 bit if any module was loaded high or
                // (if we are on a 64 bit and there were no modules loaded, we assume we are the OS system process)
                return loadedAModuleHigh || (!anyModuleLoaded && log.PointerSize == 8);
            }
        }
        /// <summary>
        /// The log file associated with the process.
        /// </summary>
        public TraceLog Log { get { return log; } }
        /// <summary>
        /// A list of all the threads that occurred in this process.
        /// </summary>
        public IEnumerable<TraceThread> Threads
        {
            get
            {
                for (int i = 0; i < log.Threads.Count; i++)
                {
                    TraceThread thread = log.Threads[(ThreadIndex)i];
                    if (thread.Process == this)
                    {
                        yield return thread;
                    }
                }
            }
        }
        /// <summary>
        /// Returns the list of modules that were loaded by the process.  The modules may be managed or
        /// native, and include native modules that were loaded event before the trace started.
        /// </summary>
        public TraceLoadedModules LoadedModules { get { return loadedModules; } }
        /// <summary>
        /// Filters events to only those for a particular process.
        /// </summary>
        public TraceEvents EventsInProcess
        {
            get
            {
                return log.Events.Filter(startTimeQPC, endTimeQPC, delegate (TraceEvent anEvent)
                {
                    // FIX Virtual allocs
                    if (anEvent.ProcessID == processID)
                    {
                        return true;
                    }
                    // FIX Virtual alloc's Process ID?
                    if (anEvent.ProcessID == -1)
                    {
                        return true;
                    }

                    return false;
                });
            }
        }
        /// <summary>
        /// Filters events to only that occurred during the time the process was alive.
        /// </summary>
        ///
        public TraceEvents EventsDuringProcess
        {
            get
            {
                return log.Events.Filter(startTimeQPC, endTimeQPC, null);
            }
        }

        /// <summary>
        /// An XML representation of the TraceEventProcess (for debugging)
        /// </summary>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<TraceProcess ");
            sb.Append("PID=").Append(XmlUtilities.XmlQuote(ProcessID)).Append(" ");
            sb.Append("ProcessIndex=").Append(XmlUtilities.XmlQuote(ProcessIndex)).Append(" ");
            sb.Append("ParentID=").Append(XmlUtilities.XmlQuote(ParentID)).Append(" ");
            sb.Append("Exe=").Append(XmlUtilities.XmlQuote(Name)).Append(" ");
            sb.Append("Start=").Append(XmlUtilities.XmlQuote(StartTimeRelativeMsec)).Append(" ");
            sb.Append("End=").Append(XmlUtilities.XmlQuote(EndTimeRelativeMsec)).Append(" ");
            if (ExitStatus.HasValue)
            {
                sb.Append("ExitStatus=").Append(XmlUtilities.XmlQuote(ExitStatus.Value)).Append(" ");
            }

            sb.Append("CPUMSec=").Append(XmlUtilities.XmlQuote(CPUMSec)).Append(" ");
            sb.Append("Is64Bit=").Append(XmlUtilities.XmlQuote(Is64Bit)).Append(" ");
            sb.Append("CommandLine=").Append(XmlUtilities.XmlQuote(CommandLine)).Append(" ");
            sb.Append("/>");
            return sb.ToString();
        }
        #region Private
        #region EventHandlersCalledFromTraceLog
        // #ProcessHandlersCalledFromTraceLog
        //
        // called from TraceLog.CopyRawEvents
        internal void ProcessStart(ProcessTraceData data)
        {
            Log.DebugWarn(parentID == 0, "Events for process happen before process start.  PrevEventTime: " + StartTimeRelativeMsec.ToString("f4"), data);

            if (data.Opcode == TraceEventOpcode.DataCollectionStart)
            {
                startTimeQPC = log.sessionStartTimeQPC;
            }
            else
            {
                Debug.Assert(data.Opcode == TraceEventOpcode.Start);
                Debug.Assert(endTimeQPC == long.MaxValue); // We would create a new Process record otherwise
                startTimeQPC = data.TimeStampQPC;
            }
            commandLine = data.CommandLine;
            imageFileName = data.ImageFileName;
            parentID = data.ParentID;
        }
        internal void ProcessEnd(ProcessTraceData data)
        {
            if (commandLine.Length == 0)
            {
                commandLine = data.CommandLine;
            }

            imageFileName = data.ImageFileName;        // Always overwrite as we might have guessed via the image loads
            if (parentID == 0 && data.ParentID != 0)
            {
                parentID = data.ParentID;
            }

            if (data.Opcode != TraceEventOpcode.DataCollectionStop)
            {
                Debug.Assert(data.Opcode == TraceEventOpcode.Stop);
                // Only set the exit code if it really is a process exit (not a DCStop).
                if (data.Opcode == TraceEventOpcode.Stop)
                {
                    exitStatus = data.ExitStatus;
                }

                endTimeQPC = data.TimeStampQPC;
            }
            Log.DebugWarn(startTimeQPC <= endTimeQPC, "Process Ends before it starts! StartTime: " + StartTimeRelativeMsec.ToString("f4"), data);
        }

        /// <summary>
        /// Sets the 'Parent' field for the process (based on the ParentID).
        ///
        /// sentinel is internal to the implementation, external callers should always pass null.
        /// TraceProcesses that have a parent==sentinel considered 'illegal' since it would form
        /// a loop in the parent chain, which we definitely don't want.
        /// </summary>
        internal void SetParentForProcess(TraceProcess sentinel = null)
        {
            if (parent != null)                     // already initialized, nothing to do.
            {
                return;
            }

            if (parentID == -1)
            {
                return;
            }

            if (parentID == 0)                      // Zero is the idle process and we prefer that it not have children.
            {
                parentID = -1;
                return;
            }

            // Look up the process ID, if we fail, we are done.
            int index;
            var potentialParent = Log.Processes.FindProcessAndIndex(parentID, startTimeQPC, out index);
            if (potentialParent == null)
            {
                return;
            }

            // If this is called from the outside, intialize the sentinel.  We will pass it
            // along in our recurisve calls.  It is just an illegal value that we can use
            // to indicate that a node is currnetly a valid parent (becase it would form a loop)
            if (sentinel == null)
            {
                sentinel = new TraceProcess(-1, Log, ProcessIndex.Invalid);
            }

            // During our recursive calls mark our parent with the sentinel this avoids loops.
            parent = sentinel;                      // Mark this node as off limits.

            // If the result is marked (would form a loop), give up setting the parent variable.
            if (potentialParent.parent == sentinel)
            {
                parent = null;
                parentID = -1;                              // This process ID is wrong, poison it to avoid using it again.
                return;
            }

            potentialParent.SetParentForProcess(sentinel);   // Finish the intialization of the parent Process, also giving up if it hits a sentinel
            parent = potentialParent;                        // OK parent is fully intialized, I can reset the sentenel
        }

#if DEBUG
        internal int ParentDepth()
        {
            int depth = 0;
            TraceProcess cur = this;
            while (depth < Log.Processes.Count)
            {
                if (cur.parent == null)
                {
                    break;
                }

                depth++;
                cur = cur.parent;
            }
            return depth;
        }
#endif
        #endregion

        /// <summary>
        /// Create a new TraceProcess.  It should only be done by log.CreateTraceProcess because
        /// only TraceLog is responsible for generating a new ProcessIndex which we need.   'processIndex'
        /// is a index that is unique for the whole log file (where as processID can be reused).
        /// </summary>
        internal TraceProcess(int processID, TraceLog log, ProcessIndex processIndex)
        {
            this.log = log;
            this.processID = processID;
            this.processIndex = processIndex;
            endTimeQPC = long.MaxValue;
            commandLine = "";
            imageFileName = "";
            loadedModules = new TraceLoadedModules(this);
            // TODO FIX NOW ACTIVITIES: if this is only used during translation, we should not allocate it in the ctor
            scheduledActivityIdToActivityIndex = new Dictionary<Address, ActivityIndex>();
        }

        void IFastSerializable.ToStream(Serializer serializer)
        {
            serializer.Write(processID);
            serializer.Write((int)processIndex);
            serializer.Write(log);
            serializer.Write(commandLine);
            serializer.Write(imageFileName);
            serializer.Write(firstEventSeenQPC);
            serializer.Write(startTimeQPC);
            serializer.Write(endTimeQPC);
            serializer.Write(exitStatus.HasValue);
            if (exitStatus.HasValue)
            {
                serializer.Write(exitStatus.Value);
            }

            serializer.Write(parentID);
            serializer.Write(parent);
            serializer.Write(loadedModules);
            serializer.Write(cpuSamples);
            serializer.Write(loadedAModuleHigh);
            serializer.Write(anyModuleLoaded);
        }

        void IFastSerializable.FromStream(Deserializer deserializer)
        {
            deserializer.Read(out processID);
            int processIndex; deserializer.Read(out processIndex); this.processIndex = (ProcessIndex)processIndex;
            deserializer.Read(out log);
            deserializer.Read(out commandLine);
            deserializer.Read(out imageFileName);
            deserializer.Read(out firstEventSeenQPC);
            deserializer.Read(out startTimeQPC);
            deserializer.Read(out endTimeQPC);
            if (deserializer.ReadBool())                // read an int? for exitStatus
            {
                exitStatus = deserializer.ReadInt();
            }
            else
            {
                exitStatus = null;
            }

            deserializer.Read(out parentID);
            deserializer.Read(out parent);
            deserializer.Read(out loadedModules);
            deserializer.Read(out cpuSamples);
            deserializer.Read(out loadedAModuleHigh);
            deserializer.Read(out anyModuleLoaded);
        }

        private int processID;
        internal ProcessIndex processIndex;
        private TraceLog log;

        private string commandLine;
        internal string imageFileName;
        private string name;
        internal long firstEventSeenQPC;      // Sadly there are events before process start.   This is minimum of those times.  Note that there may be events before this
        internal long startTimeQPC;
        internal long endTimeQPC;
        private int? exitStatus;
        private int parentID;
        private TraceProcess parent;

        internal int cpuSamples;
        internal bool loadedAModuleHigh;    // Was any module loaded above 0x100000000?  (which indicates it is a 64 bit process)
        internal bool anyModuleLoaded;
        internal bool anyThreads;

        internal bool isServerGC;
        // We only set this in the GCStart event because we want to make sure we are seeing a complete GC.
        // After we have seen a complete GC we set this to FALSE.
        internal bool shouldCheckIsServerGC = false;
        internal Dictionary<int, int> markThreadsInGC = new Dictionary<int, int>(); // Used during collection to determine if we are server GC or not.

        private TraceLoadedModules loadedModules;

        /* These are temporary and only used during conversion from ETL to resolve addresses to a CodeAddress.  */
        /// <summary>
        /// This table allows us to intern codeAddress so we only at most one distinct address per process.
        /// </summary>
        internal Dictionary<Address, CodeAddressIndex> codeAddressesInProcess;
        /// <summary>
        /// We also keep track of those code addresses that are NOT yet resolved to at least a File (for JIT compiled
        /// things this would be to a method
        /// </summary>
        internal GrowableArray<CodeAddressIndex> unresolvedCodeAddresses;
        internal bool unresolvedCodeAddressesIsSorted;      // True if we know that unresolvedCodeAddresses is sorted
        internal bool seenVersion2GCStartEvents;

        /// <summary>
        /// This is all the information needed to remember about at JIT compiled method (used in the jitMethods variable)
        /// </summary>
        internal class MethodLookupInfo
        {
            public MethodLookupInfo(Address startAddress, int length, MethodIndex method)
            {
                StartAddress = startAddress;
                Length = length;
                MethodIndex = method;
            }
            public Address StartAddress;
            public int Length;
            public MethodIndex MethodIndex;             // Logically represents the TraceMethod.
            public ModuleFileIndex ModuleIndex;
        }

        /// <summary>
        /// This table has a entry for each JIT compiled method that remembers its range.   It is actually only needed
        /// for the real time case, as the non-real time case you resolve code addresses on method unload/rundown and thus
        /// don't need to remember the information.   This table is NOT persisted in the ETLX file since is only needed
        /// to convert raw addresses into TraceMethods.
        ///
        /// It is a array of arrays to make insertion efficient.  Most of the time JIT methods will be added in
        /// contiguous memory (thus will be in order), however from time to time things will 'jump around' to a new
        /// segment.   By having a list of lists, (which are in order in both lists) you can efficiently (log(N)) search
        /// as well as insert.
        /// </summary>
        internal GrowableArray<GrowableArray<MethodLookupInfo>> jitMethods;

        internal MethodIndex FindJITTEDMethodFromAddress(Address codeAddress)
        {
            int index;
            jitMethods.BinarySearch(codeAddress, out index, (addr, elemList) => addr.CompareTo(elemList[0].StartAddress));
            if (index < 0)
            {
                return MethodIndex.Invalid;
            }

            GrowableArray<MethodLookupInfo> subList = jitMethods[index];
            subList.BinarySearch(codeAddress, out index, (addr, elem) => addr.CompareTo(elem.StartAddress));
            if (index < 0)
            {
                return MethodIndex.Invalid;
            }

            MethodLookupInfo methodLookupInfo = subList[index];
            Debug.Assert(methodLookupInfo.StartAddress <= codeAddress);
            if (methodLookupInfo.StartAddress + (uint)methodLookupInfo.Length <= codeAddress)
            {
                return MethodIndex.Invalid;
            }

            return methodLookupInfo.MethodIndex;
        }

        internal void InsertJITTEDMethod(Address startAddress, int length, Func<MethodLookupInfo> onInsert)
        {
            // Debug.WriteLine(string.Format("Process {0} Adding 0x{1:x} Len 0x{2:x}", ProcessID, startAddress, length));
            int index;
            if (jitMethods.BinarySearch(startAddress, out index, (addr, elemList) => addr.CompareTo(elemList[0].StartAddress)))
            {
                return;     // Start address already exists, do nothing.
            }

#if DEBUG
            var preCount = 0;
            if (_skipCount == 0)
            {
                preCount = JitTableCount();
            }
#endif
            if (index < 0)
            {
                // either empty or we are going BEFORE the first element in the list, add a new entry there and we are done.
                index = 0;
                var newSubList = new GrowableArray<MethodLookupInfo>();
                newSubList.Add(onInsert());
                jitMethods.Insert(0, newSubList);
            }
            else
            {

                GrowableArray<MethodLookupInfo> subList = jitMethods[index];
                Debug.Assert(0 < subList.Count);
                int subIndex = subList.Count - 1;        // Guess that it goes after the last element
                if (startAddress < subList[subIndex].StartAddress)
                {
                    // bad case, we are not adding in the end, move those elements to a new region so we can add at the end.
                    if (subList.BinarySearch(startAddress, out subIndex, (addr, elem) => addr.CompareTo(elem.StartAddress)))
                    {
                        return;     // Start address already exists, do nothing.
                    }

                    subIndex++;
                    var toMove = subList.Count - subIndex;
                    Debug.Assert(0 < toMove);
                    if (toMove <= 8)
                    {
                        jitMethods.UnderlyingArray[index].Insert(subIndex, onInsert());
                        goto RETURN;
                    }

                    // Move all the elements larger than subIndex to a new list right after this element.
                    var newSubList = new GrowableArray<MethodLookupInfo>();
                    for (int i = subIndex; i < subList.Count; i++)
                    {
                        newSubList.Add(subList[i]);
                    }

                    jitMethods.UnderlyingArray[index].Count = subIndex;

                    // Add the new list to the first-level list-of-lists.
                    jitMethods.Insert(index + 1, newSubList);
                }
                // Add the new entry
                jitMethods.UnderlyingArray[index].Add(onInsert());
            }

            RETURN:;
#if DEBUG
            // Confirm that we did not break anything.
            if (_skipCount == 0)
            {
                CheckJitTables();
                Debug.Assert(preCount + 1 == JitTableCount());
                _skipCount = 32;
            }
            --_skipCount;
#endif
        }

        [Conditional("DEBUG")]
        private void CheckJitTables()
        {
            ulong prev = 0;
            for (int i = 0; i < jitMethods.Count; i++)
            {
                var sub = jitMethods[i];
                Debug.Assert(0 < sub.Count);
                for (int j = 0; j < sub.Count; j++)
                {
                    Debug.Assert(prev <= sub[j].StartAddress);
                    prev = sub[j].StartAddress;
                }
            }
        }
#if DEBUG
        /// <summary>
        /// The JIT table checks are expensive. They are only enabled in debug builds, and even then do not run every
        /// time a method is added. This field counts down each time <see cref="InsertJITTEDMethod"/> is called; when it
        /// reaches zero the sanity checks are run and it is reset to an unspecified positive value.
        /// </summary>
        private static int _skipCount;

        private int JitTableCount()
        {
            int count = 0;
            for (int i = 0; i < jitMethods.Count; i++)
            {
                var sub = jitMethods[i];
                count += sub.Count;
            }
            return count;
        }

        private void DumpJITTables()
        {
            Debug.WriteLine("Jitted Methods Table for process " + ProcessID);
            ulong prev = 0;
            int maxChain = 0;
            int count = 0;
            for (int i = 0; i < jitMethods.Count; i++)
            {
                Debug.Write("  ");
                var sub = jitMethods[i];
                maxChain = Math.Max(maxChain, sub.Count);
                for (int j = 0; j < sub.Count; j++)
                {
                    Debug.Assert(prev <= sub[j].StartAddress);
                    prev = sub[j].StartAddress;
                    Debug.Write(sub[j].StartAddress.ToString("x"));
                    Debug.Write(" ");
                    count++;
                }
                Debug.WriteLine("");
            }
            Debug.WriteLine("Count = " + count + " MaxChain = " + maxChain + " firstLevel " + jitMethods.Count);
        }
#endif

        /// <summary>
        /// Maps a newly scheduled "user" activity ID to the ActivityIndex of the
        /// Activity. This keeps track of currently created/scheduled activities
        /// that have not started yet, and for multi-trigger events, created/scheduled
        /// activities that have not conclusively "died" (e.g. by having their "user"
        /// activity ID reused by another activity).
        /// </summary>
        internal Dictionary<Address, ActivityIndex> scheduledActivityIdToActivityIndex;
        #endregion
    }

    /// <summary>
    /// Each thread is given a unique index from 0 to TraceThreads.Count-1 and unlike
    /// the OS Thread ID, is  unambiguous (The OS thread ID can be reused after a
    /// thread dies).  ThreadIndex represents this index.   By using an enum rather than an int
    /// it allows stronger typing and reduces the potential for errors.
    /// <para>
    /// It is expected that users of this library might keep arrays of size TraceThreads.Count to store
    /// additional data associated with a process in the trace.
    /// </para>
    /// </summary>
    public enum ThreadIndex
    {
        /// <summary>
        /// Returned when no appropriate Thread exists.
        /// </summary>
        Invalid = -1
    };

    /// <summary>
    /// A TraceThreads represents the list of threads in a process.
    /// </summary>
    public sealed class TraceThreads : IEnumerable<TraceThread>, IFastSerializable
    {
        /// <summary>
        /// Enumerate all the threads that occurred in the trace log. It does so in order of their thread
        /// offset events in the log.
        /// </summary>
        IEnumerator<TraceThread> IEnumerable<TraceThread>.GetEnumerator()
        {
            for (int i = 0; i < threads.Count; i++)
            {
                yield return threads[i];
            }
        }
        /// <summary>
        /// The count of the number of TraceThreads in the trace log.
        /// </summary>
        public int Count { get { return threads.Count; } }
        /// <summary>
        /// Each thread that occurs in the log is given a unique index (which unlike the PID is unique), that
        /// ranges from 0 to Count - 1.   Return the TraceThread for the given index.
        /// </summary>
        public TraceThread this[ThreadIndex threadIndex]
        {
            get
            {
                if (threadIndex == ThreadIndex.Invalid)
                {
                    return null;
                }

                return threads[(int)threadIndex];
            }
        }
        /// <summary>
        /// Given an OS thread ID and a time, return the last TraceThread that has the same thread ID,
        /// and whose start time is less than 'timeRelativeMSec'. If 'timeRelativeMSec' is during the thread's lifetime this
        /// is guaranteed to be the correct thread.
        /// </summary>
        public TraceThread GetThread(int threadID, double timeRelativeMSec)
        {
            long timeQPC = log.RelativeMSecToQPC(timeRelativeMSec);
            InitThread();
            TraceThread ret = null;
            threadIDtoThread.TryGetValue(threadID, timeQPC, out ret);
            return ret;
        }
        /// <summary>
        /// An XML representation of the TraceThreads (for debugging)
        /// </summary>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<TraceThreads Count=").Append(XmlUtilities.XmlQuote(Count)).AppendLine(">");
            foreach (TraceThread thread in this)
            {
                sb.Append("  ").Append(thread.ToString()).AppendLine();
            }

            sb.AppendLine("</TraceThreads>");
            return sb.ToString();
        }
        #region Private
        internal TraceThread GetThread(int threadID, long timeQPC)
        {
            InitThread();
            TraceThread ret = null;
            threadIDtoThread.TryGetValue(threadID, timeQPC, out ret);
            return ret;
        }

        /// <summary>
        /// TraceThreads   represents the collection of threads in a process.
        ///
        /// </summary>
        internal TraceThreads(TraceLog log)
        {
            this.log = log;
        }
        private void InitThread()
        {
            // Create a cache for this because it can be common
            if (threadIDtoThread == null)
            {
                threadIDtoThread = new HistoryDictionary<int, TraceThread>(1000);
                for (int i = 0; i < threads.Count; i++)
                {
                    var thread = threads[i];
                    threadIDtoThread.Add(thread.ThreadID, thread.startTimeQPC, thread);
                }
            }
        }

        /// <summary>
        /// Get the thread for threadID and timeQPC.   Create if necessary.  If 'isThreadCreateEvent' is true,
        /// then force  the creation of a new thread EVEN if the thread exist since we KNOW it is a new thread
        /// (and somehow we missed the threadEnd event).   Process is the process associated with the thread.
        /// It can be null if you really don't know the process ID.  We will try to fill it in on another event
        /// where we DO know the process id (ThreadEnd event).
        /// </summary>
        internal TraceThread GetOrCreateThread(int threadID, long timeQPC, TraceProcess process, bool isThreadCreateEvent = false)
        {
            TraceThread retThread = GetThread(threadID, timeQPC);

            // ThreadIDs are machine wide, however, they are also reused. Thus GetThread CAN give you an OLD thread IF
            // we are missing thread Death and creation events (thus silently it gets reused on another process). This
            // can happen easily because Kernel events (which log thread creation and deaths), have a circular buffer that
            // might get exhausted before user mode events (e.g. CLR events). Thus try to keep as much sanity as possible
            // by confirming that if the thread we get back had a process, the process is the same as the process for
            // the current event. If not, we assume that there were silent thread deaths and creations and simply create
            // a new TraceThread. Note that this problem mostly goes away when we have just a single circular buffer since
            // we won't lose the Thread death and creation events.
            if (process != null && process.ProcessID != -1 && retThread != null && retThread.process.ProcessID != -1 && process.ProcessID != retThread.process.ProcessID)
            {
                retThread = null;
            }

            if (retThread == null || isThreadCreateEvent)
            {
                InitThread();

                if (process == null)
                {
                    process = log.Processes.GetOrCreateProcess(-1, timeQPC);      // Unknown process
                }

                retThread = new TraceThread(threadID, process, (ThreadIndex)threads.Count);
                if (isThreadCreateEvent)
                {
                    retThread.startTimeQPC = timeQPC;
                }

                threads.Add(retThread);
                threadIDtoThread.Add(threadID, timeQPC, retThread);
            }

            // Set the process if we had to set this threads process ID to the 'unknown' process.
            if (process != null && retThread.process.ProcessID == -1)
            {
                retThread.process = process;
            }

            return retThread;
        }

        void IFastSerializable.ToStream(Serializer serializer)
        {
            serializer.Write(log);

            serializer.Log("<WriteCollection name=\"threads\" count=\"" + threads.Count + "\">\r\n");
            serializer.Write(threads.Count);
            for (int i = 0; i < threads.Count; i++)
            {
                serializer.Write(threads[i]);
            }

            serializer.Log("</WriteCollection>\r\n");
        }

        void IFastSerializable.FromStream(Deserializer deserializer)
        {
            deserializer.Read(out log);
            Debug.Assert(threads.Count == 0);
            int count = deserializer.ReadInt();
            threads = new GrowableArray<TraceThread>(count + 1);

            for (int i = 0; i < count; i++)
            {
                TraceThread elem; deserializer.Read(out elem);
                threads.Add(elem);
            }
        }
        // State variables.
        private GrowableArray<TraceThread> threads;          // The threads ordered in time.
        private TraceLog log;
        internal HistoryDictionary<int, TraceThread> threadIDtoThread;

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            throw new NotImplementedException(); // GetEnumerator
        }
        #endregion
    }

    /// <summary>
    /// A TraceThread represents a thread of execution in a process.
    /// </summary>
    public sealed class TraceThread : IFastSerializable
    {
        /// <summary>
        /// The OS process ID associated with the process.
        /// </summary>
        public int ThreadID { get { return threadID; } }
        /// <summary>
        /// The index into the logical array of TraceThreads for this process.  Unlike ThreadId (which
        /// may be reused after the thread dies) the T index is unique over the log.
        /// </summary>
        public ThreadIndex ThreadIndex { get { return threadIndex; } }
        /// <summary>
        /// The process associated with the thread.
        /// </summary>
        public TraceProcess Process { get { return process; } }

        /// <summary>
        /// The time when the thread started.  Returns the time the trace started if the thread existed when the trace started.
        /// Returned as a DateTime
        /// </summary>
        public DateTime StartTime { get { return Process.Log.QPCTimeToDateTimeUTC(startTimeQPC).ToLocalTime(); } }
        /// <summary>
        /// The time when the thread started.  Returns the time the trace started if the thread existed when the trace started.
        /// Returned as the number of MSec from the beginning of the trace.
        /// </summary>
        public double StartTimeRelativeMSec { get { return process.Log.QPCTimeToRelMSec(startTimeQPC); } }
        /// <summary>
        /// The time when the thread ended.  Returns the time the trace ended if the thread existed when the trace ended.
        /// Returned as a DateTime
        /// </summary>
        public DateTime EndTime { get { return Process.Log.QPCTimeToDateTimeUTC(endTimeQPC).ToLocalTime(); } }
        /// <summary>
        /// The time when the thread ended.  Returns the time the trace ended if the thread existed when the trace ended.
        /// Returned as the number of MSec from the beginning of the trace.
        /// </summary>
        public double EndTimeRelativeMSec { get { return process.Log.QPCTimeToRelMSec(endTimeQPC); } }
        /// <summary>
        /// The amount of CPU time spent on this thread based on the kernel CPU sampling events.
        /// </summary>
        public float CPUMSec { get { return (float)(cpuSamples * Process.Log.SampleProfileInterval.TotalMilliseconds); } }
        /// <summary>
        /// Filters events to only those for a particular thread.
        /// </summary>
        public TraceEvents EventsInThread
        {
            get
            {
                return Process.Log.Events.Filter(startTimeQPC, endTimeQPC, delegate (TraceEvent anEvent)
                {
                    return anEvent.ThreadID == ThreadID;
                });
            }
        }
        /// <summary>
        /// Filters events to only those that occurred during the time a the thread was alive.
        /// </summary>
        public TraceEvents EventsDuringThread
        {
            get
            {
                return Process.Log.Events.FilterByTime(StartTimeRelativeMSec, EndTimeRelativeMSec);
            }
        }

        /// <summary>
        /// REturns the activity this thread was working on at the time instant 'relativeMsec'
        /// </summary>
        [Obsolete("Likely to be removed Replaced by ActivityMap.GetActivity(TraceThread, double)")]
        public ActivityIndex GetActivityIndex(double relativeMSec)
        {

            throw new InvalidOperationException("Don't use activities right now");
        }
        /// <summary>
        /// Represents the "default" activity for the thread, the activity that no one has set
        /// </summary>
        [Obsolete("Likely to be removed Replaced by ActivityComputer.GetDefaultActivity(TraceThread)")]
        public ActivityIndex DefaultActivityIndex
        {
            get
            {
                throw new InvalidOperationException("Don't use activities right now");
            }
        }

        internal void ThreadEnd(ThreadTraceData data, TraceProcess process)
        {
        }

        /// <summary>
        /// ThreadInfo is a string that identifies the thread symbolically.   (e.g. .NET Threadpool, .NET GC)  It may return null if there is no useful symbolic name.
        /// </summary>
        public string ThreadInfo { get { return threadInfo; } }
        /// <summary>
        /// VerboseThreadName is a name for the thread including the ThreadInfo and the CPU time used.
        /// </summary>
        public string VerboseThreadName
        {
            get
            {
                if (verboseThreadName == null)
                {
                    if (CPUMSec != 0)
                    {
                        verboseThreadName = string.Format("Thread ({0}) CPU={1:f0}ms", ThreadID, CPUMSec);
                    }
                    else
                    {
                        verboseThreadName = string.Format("Thread ({0})", ThreadID);
                    }

                    if (ThreadInfo != null)
                    {
                        verboseThreadName += " (" + ThreadInfo + ")";
                    }
                }
                return verboseThreadName;
            }
        }

        /// <summary>
        /// The base of the thread's stack.  This is just past highest address in memory that is part of the stack
        /// (we don't really know the lower bound (userStackLimit is this lower bound at the time the thread was created
        /// which is not very useful).
        /// </summary>
        public Address UserStackBase { get { return userStackBase; } }

        /// <summary>
        /// An XML representation of the TraceThread (for debugging)
        /// </summary>
        public override string ToString()
        {
            return "<TraceThread " +
                    "TID=" + XmlUtilities.XmlQuote(ThreadID).PadRight(5) + " " +
                    "ThreadIndex=" + XmlUtilities.XmlQuote(threadIndex).PadRight(5) + " " +
                    "StartTimeRelative=" + XmlUtilities.XmlQuote(StartTimeRelativeMSec).PadRight(8) + " " +
                    "EndTimeRelative=" + XmlUtilities.XmlQuote(EndTimeRelativeMSec).PadRight(8) + " " +
                   "/>";
        }
        #region Private
        /// <summary>
        /// Create a new TraceProcess.  It should only be done by log.CreateTraceProcess because
        /// only TraceLog is responsible for generating a new ProcessIndex which we need.   'processIndex'
        /// is a index that is unique for the whole log file (where as processID can be reused).
        /// </summary>
        internal TraceThread(int threadID, TraceProcess process, ThreadIndex threadIndex)
        {
            this.threadID = threadID;
            this.threadIndex = threadIndex;
            this.process = process;
            endTimeQPC = long.MaxValue;
            lastBlockingCSwitchEventIndex = EventIndex.Invalid;
        }


        void IFastSerializable.ToStream(Serializer serializer)
        {
            serializer.Write(threadID);
            serializer.Write((int)threadIndex);
            serializer.Write(process);
            serializer.Write(startTimeQPC);
            serializer.Write(endTimeQPC);
            serializer.Write(cpuSamples);
            serializer.Write(threadInfo);
            serializer.Write((long)userStackBase);

            serializer.Write(activityIds.Count);
            serializer.Log("<WriteCollection name=\"ActivityIDForThread\" count=\"" + activityIds.Count + "\">\r\n");
            foreach (ActivityIndex entry in activityIds)
            {
                serializer.Write((int)entry);
            }

            serializer.Log("</WriteCollection>\r\n");
        }

        void IFastSerializable.FromStream(Deserializer deserializer)
        {
            deserializer.Read(out threadID);
            int threadIndex; deserializer.Read(out threadIndex); this.threadIndex = (ThreadIndex)threadIndex;
            deserializer.Read(out process);
            deserializer.Read(out startTimeQPC);
            deserializer.Read(out endTimeQPC);
            deserializer.Read(out cpuSamples);
            deserializer.Read(out threadInfo);
            userStackBase = (Address)deserializer.ReadInt64();

            int count; deserializer.Read(out count);
            activityIds = new GrowableArray<ActivityIndex>(count);
            for (int i = 0; i < count; ++i)
            {
                activityIds.Add((ActivityIndex)deserializer.ReadInt());
            }
        }

        private int threadID;
        private ThreadIndex threadIndex;
        internal TraceProcess process;
        internal long startTimeQPC;
        internal long endTimeQPC;
        internal int cpuSamples;
        internal string threadInfo;
        internal Address userStackBase;
        private string verboseThreadName;
        /// <summary>
        /// This is a list of the activities (snippet of threads) that have run on this
        /// thread.   They are ordered by time so you can binary search for your activity based
        /// on timestamp.
        /// </summary>
        internal GrowableArray<ActivityIndex> activityIds;

        // Only used when converting stacks.  It is not serialized.
        // When you take a stack in the kernel you only do the kernel part.   Then, when you leave the
        // kernel you do the user mode part.   Thus all kernel stacks after the first one through the
        // user mode event on leaving can use the same user mode stack.   This keeps track of this entry
        // into the kernel.
        internal TraceLog.IncompleteStack lastEntryIntoKernel;
        // as an extra validation after we flush all the kernel entries with EmitStackOnExitFromKernel
        // we remember the QPC timestamp when we did this.  Any user mode stacks that are trying to
        // associated themselves with events before this time should be ignored.
        internal long lastEmitStackOnExitFromKernelQPC;

        /// <summary>
        /// We want to have the stack for when CSwtichs BLOCK as well as when they unblock.
        /// this variable keeps track of the last blocking CSWITCH on this thread so that we can
        /// compute this.   It is only used during generation of a TraceLog file.
        /// </summary>
        internal EventIndex lastBlockingCSwitchEventIndex;
        #endregion
    }

    /// <summary>
    /// TraceLoadedModules represents the collection of modules (loaded DLLs or EXEs) in a
    /// particular process.
    /// </summary>
    public sealed class TraceLoadedModules : IEnumerable<TraceLoadedModule>, IFastSerializable
    {
        /// <summary>
        /// The process in which this Module is loaded.
        /// </summary>
        public TraceProcess Process { get { return process; } }

        /// <summary>
        /// Returns the module which was mapped into memory at at 'timeRelativeMSec' and includes the address 'address'
        /// <para> Note that Jit compiled code is placed into memory that is not associated with the module and thus will not
        /// be found by this method.
        /// </para>
        /// </summary>
        public TraceLoadedModule GetModuleContainingAddress(Address address, double timeRelativeMSec)
        {
            int index;
            TraceLoadedModule module = FindModuleAndIndexContainingAddress(address, process.Log.RelativeMSecToQPC(timeRelativeMSec), out index);
            return module;
        }

        /// <summary>
        /// Returns the module representing the unmanaged load of a particular fiele at a given time.
        /// </summary>
        public TraceLoadedModule GetLoadedModule(string fileName, double timeRelativeMSec)
        {
            long timeQPC = process.Log.RelativeMSecToQPC(timeRelativeMSec);
            for (int i = 0; i < modules.Count; i++)
            {
                TraceLoadedModule module = modules[i];
                if (string.Compare(module.FilePath, fileName, StringComparison.OrdinalIgnoreCase) == 0 && module.loadTimeQPC <= timeQPC && timeQPC < module.unloadTimeQPC)
                {
                    return module;
                }
            }
            return null;
        }

        /// <summary>
        /// An XML representation of the TraceLoadedModules (for debugging)
        /// </summary>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<TraceLoadedModules Count=").Append(XmlUtilities.XmlQuote(modules.Count)).AppendLine(">");
            foreach (TraceLoadedModule module in this)
            {
                sb.Append("  ").Append(module.ToString()).AppendLine();
            }

            sb.AppendLine("</TraceLoadedModules>");
            return sb.ToString();
        }

        #region Private
        /// <summary>
        /// Returns all modules in the process.  Note that managed modules may appear twice
        /// (once for the managed load and once for an unmanaged (LoadLibrary) load.
        /// </summary>
        public IEnumerator<TraceLoadedModule> GetEnumerator()
        {
            for (int i = 0; i < modules.Count; i++)
            {
                yield return modules[i];
            }
        }
        /// <summary>
        /// This function will find the module associated with 'address' at 'timeQPC' however it will only
        /// find modules that are mapped in memory (module associated with JIT compiled methods will not be found).
        /// </summary>
        internal TraceLoadedModule GetLoadedModule(string fileName, long timeQPC)
        {
            for (int i = 0; i < modules.Count; i++)
            {
                TraceLoadedModule module = modules[i];
                if (string.Compare(module.FilePath, fileName, StringComparison.OrdinalIgnoreCase) == 0 && module.loadTimeQPC <= timeQPC && timeQPC < module.unloadTimeQPC)
                {
                    return module;
                }
            }
            return null;
        }

        // #ModuleHandlersCalledFromTraceLog
        internal TraceModuleFile ImageLoadOrUnload(ImageLoadTraceData data, bool isLoad, string dataFileName = null)
        {
            int index;
            if (dataFileName == null)
            {
                dataFileName = data.FileName;
            }

            TraceLoadedModule module = FindModuleAndIndexContainingAddress(data.ImageBase, data.TimeStampQPC, out index);
            if (module == null)
            {
                // We need to make a new module
                TraceModuleFile newModuleFile = process.Log.ModuleFiles.GetOrCreateModuleFile(dataFileName, data.ImageBase);
                newModuleFile.imageSize = data.ImageSize;
                module = new TraceLoadedModule(process, newModuleFile, data.ImageBase);
                InsertAndSetOverlap(index + 1, module);
            }

            // If we load a module higher than 32 bits can do, then we must be a 64 bit process.
            if (!process.loadedAModuleHigh && (ulong)data.ImageBase >= 0x100000000L)
            {
                //  On win8 ntdll gets loaded into 32 bit processes so ignore it
                if (!dataFileName.EndsWith("ntdll.dll", StringComparison.OrdinalIgnoreCase))
                {
                    process.loadedAModuleHigh = true;
                }
            }
            process.anyModuleLoaded = true;

            TraceModuleFile moduleFile = module.ModuleFile;
            Debug.Assert(moduleFile != null);

            // WORK-AROUND.   I have had problem on 64 bit systems with image load (but not the unload being only a prefix of
            // the full file path.   We 'fix it' here.
            if (!isLoad && module.ModuleFile.FilePath.Length < dataFileName.Length)
            {
                process.Log.DebugWarn(false, "Needed to fix up a truncated load file path at unload time.", data);
                module.ModuleFile.fileName = dataFileName;
            }

            // TODO we get different prefixes. skip it
            int len = Math.Max(Math.Min(module.ModuleFile.FilePath.Length - 4, dataFileName.Length - 4), 0);
            int start1 = module.ModuleFile.FilePath.Length - len;
            int start2 = dataFileName.Length - len;
            process.Log.DebugWarn(string.Compare(module.ModuleFile.FilePath, start1, dataFileName, start2, len, StringComparison.OrdinalIgnoreCase) == 0,
                "Filename Load/Unload mismatch.\r\n    FILE1: " + module.ModuleFile.FilePath, data);
            process.Log.DebugWarn(module.ModuleFile.ImageSize == 0 || module.ModuleFile.ImageSize == data.ImageSize,
                "ImageSize not consistent over all Loads Size 0x" + module.ModuleFile.ImageSize.ToString("x"), data);
            /* TODO this one fails.  decide what to do about it.
            process.Log.DebugWarn(module.ModuleFile.DefaultBase == 0 || module.ModuleFile.DefaultBase == data.DefaultBase,
                "DefaultBase not consistent over all Loads Size 0x" + module.ModuleFile.DefaultBase.ToString("x"), data);
             ***/

            moduleFile.imageSize = data.ImageSize;
            if (isLoad)
            {
                process.Log.DebugWarn(module.loadTimeQPC == 0 || data.Opcode == TraceEventOpcode.DataCollectionStart, "Events for module happened before load.  PrevEventTime: " + module.LoadTimeRelativeMSec.ToString("f4"), data);
                process.Log.DebugWarn(data.TimeStampQPC < module.unloadTimeQPC, "Unload time < load time!", data);

                module.loadTimeQPC = data.TimeStampQPC;
                if (data.Opcode == TraceEventOpcode.DataCollectionStart)
                {
                    module.loadTimeQPC = process.Log.sessionStartTimeQPC;
                }
            }
            else
            {
                process.Log.DebugWarn(module.loadTimeQPC < data.TimeStampQPC, "Unload time < load time!", data);
                process.Log.DebugWarn(module.unloadTimeQPC == long.MaxValue,
                    "Unloading a image twice PrevUnloadTime: " + module.UnloadTimeRelativeMSec.ToString("f4"), data);
                if (data.Opcode == TraceEventOpcode.DataCollectionStop)
                {
                    // For circular logs, we don't have the process name but we can infer it from the module DCStop events
                    if (Process.imageFileName.Length == 0 && dataFileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        Process.imageFileName = dataFileName;
                    }
                }
                else
                {
                    module.unloadTimeQPC = data.TimeStampQPC;
                    // TODO there seem to be unmatched unloads in many traces.  This has make this diagnostic too noisy.
                    // ideally we could turn this back on.
                    // process.Log.DebugWarn(module.loadTimeQPC != 0, "Unloading image not loaded.", data);
                }

                // Look for all code addresses those that don't have modules that are in my range are assumed to be mine.
                Process.Log.CodeAddresses.ForAllUnresolvedCodeAddressesInRange(process, data.ImageBase, data.ImageSize, false,
                    delegate (ref Microsoft.Diagnostics.Tracing.Etlx.TraceCodeAddresses.CodeAddressInfo info)
                    {
                        info.SetModuleFileIndex(moduleFile);
                    });
            }
            CheckClassInvarients();
            return moduleFile;
        }
        internal void ManagedModuleLoadOrUnload(ModuleLoadUnloadTraceData data, bool isLoad, bool isDCStartStop)
        {
            var ilModulePath = data.ModuleILPath;
            var nativeModulePath = data.ModuleNativePath;
            var nativePdbSignature = data.NativePdbSignature;

            // If the NGEN image is used as the IL image (happened in CoreCLR case), change the name of the
            // IL image to be a 'fake' non-nGEN image.  We need this because we need a DISTINCT file that
            // we can hang the PDB signature information on for the IL pdb.
            if (ilModulePath == nativeModulePath)
            {
                // Remove the .ni. from the path.  Note that this file won't exist, but that is OK.
                var nisuffix = ilModulePath.LastIndexOf(".ni.", ilModulePath.Length, 8, StringComparison.OrdinalIgnoreCase);
                if (0 <= nisuffix)
                {
                    ilModulePath = ilModulePath.Substring(0, nisuffix) + ilModulePath.Substring(nisuffix + 3);
                }
            }
            // This is the CoreCLR (First Generation) ReadyToRun case.   There still is a native PDB that is distinct
            // from the IL PDB.   Unlike CoreCLR NGEN, it is logged as a IL file, but it has native code (and thus an NativePdbSignature)
            // We treat the image as a native image and dummy up a il image to hang the IL PDB information on.
            else if (nativeModulePath.Length == 0 && nativePdbSignature != Guid.Empty && data.ManagedPdbSignature != Guid.Empty)
            {
                // And make up a fake .il.dll module for the IL
                var suffixPos = ilModulePath.LastIndexOf(".", StringComparison.OrdinalIgnoreCase);
                if (0 < suffixPos)
                {
                    // We treat the image as the native path
                    nativeModulePath = ilModulePath;
                    // and make up a dummy IL path.
                    ilModulePath = ilModulePath.Substring(0, suffixPos) + ".il" + ilModulePath.Substring(suffixPos);
                }
            }

            int index;
            TraceManagedModule module = FindManagedModuleAndIndex(data.ModuleID, data.TimeStampQPC, out index);
            if (module == null)
            {
                // We need to make a new module
                TraceModuleFile newModuleFile = process.Log.ModuleFiles.GetOrCreateModuleFile(ilModulePath, 0);
                module = new TraceManagedModule(process, newModuleFile, data.ModuleID);
                modules.Insert(index + 1, module);      // put it where it belongs in the sorted list
            }

            process.Log.DebugWarn(module.assemblyID == 0 || module.assemblyID == data.AssemblyID, "Inconsistent Assembly ID previous ID = 0x" + module.assemblyID.ToString("x"), data);
            module.assemblyID = data.AssemblyID;
            module.flags = data.ModuleFlags;
            if (nativeModulePath.Length > 0)
            {
                module.nativeModule = GetLoadedModule(nativeModulePath, data.TimeStampQPC);
            }

            if (module.ModuleFile.fileName == null)
            {
                process.Log.ModuleFiles.SetModuleFileName(module.ModuleFile, ilModulePath);
            }

            if (module.ModuleFile.pdbSignature == Guid.Empty && data.ManagedPdbSignature != Guid.Empty)
            {
                module.ModuleFile.pdbSignature = data.ManagedPdbSignature;
                module.ModuleFile.pdbAge = data.ManagedPdbAge;
                module.ModuleFile.pdbName = data.ManagedPdbBuildPath;
            }

            if (module.NativeModule != null)
            {
                Debug.Assert(module.NativeModule.managedModule == null ||
                    module.NativeModule.ModuleFile.managedModule.FilePath == module.ModuleFile.FilePath);

                module.NativeModule.ModuleFile.managedModule = module.ModuleFile;
                if (nativePdbSignature != Guid.Empty && module.NativeModule.ModuleFile.pdbSignature == Guid.Empty)
                {
                    module.NativeModule.ModuleFile.pdbSignature = nativePdbSignature;
                    module.NativeModule.ModuleFile.pdbAge = data.NativePdbAge;
                    module.NativeModule.ModuleFile.pdbName = data.NativePdbBuildPath;
                }

                module.InitializeNativeModuleIsReadyToRun();
            }

            // TODO factor this with the unmanaged case.
            if (isLoad)
            {
                process.Log.DebugWarn(module.loadTimeQPC == 0 || data.Opcode == TraceEventOpcode.DataCollectionStart, "Events for module happened before load.  PrevEventTime: " + module.LoadTimeRelativeMSec.ToString("f4"), data);
                process.Log.DebugWarn(data.TimeStampQPC < module.unloadTimeQPC, "Managed Unload time < load time!", data);

                module.loadTimeQPC = data.TimeStampQPC;
                if (!isDCStartStop)
                {
                    module.loadTimeQPC = process.Log.sessionStartTimeQPC;
                }
            }
            else
            {
                process.Log.DebugWarn(module.loadTimeQPC < data.TimeStampQPC, "Managed Unload time < load time!", data);
                process.Log.DebugWarn(module.unloadTimeQPC == long.MaxValue, "Unloading a managed image twice PrevUnloadTime: " + module.UnloadTimeRelativeMSec.ToString("f4"), data);
                if (!isDCStartStop)
                {
                    module.unloadTimeQPC = data.TimeStampQPC;
                }
            }
            CheckClassInvarients();
        }

        internal TraceManagedModule GetOrCreateManagedModule(long managedModuleID, long timeQPC)
        {
            int index;
            TraceManagedModule module = FindManagedModuleAndIndex(managedModuleID, timeQPC, out index);
            if (module == null)
            {
                // We need to make a new module entry (which is pretty empty)
                TraceModuleFile newModuleFile = process.Log.ModuleFiles.GetOrCreateModuleFile(null, 0);
                module = new TraceManagedModule(process, newModuleFile, managedModuleID);
                modules.Insert(index + 1, module);      // put it where it belongs in the sorted list
            }
            return module;
        }
        /// <summary>
        /// Finds the index and module for an a given managed module ID.  If not found, new module
        /// should be inserted at index + 1;
        /// </summary>
        private TraceManagedModule FindManagedModuleAndIndex(long moduleID, long timeQPC, out int index)
        {
            modules.BinarySearch((ulong)moduleID, out index, compareByKey);
            // Index now points at the last place where module.key <= moduleId;
            // Search backwards from where for a module that is loaded and in range.
            while (index >= 0)
            {
                TraceLoadedModule candidateModule = modules[index];
                if (candidateModule.key < (ulong)moduleID)
                {
                    break;
                }

                Debug.Assert(candidateModule.key == (ulong)moduleID);

                // We keep managed modules after unmanaged modules
                TraceManagedModule managedModule = candidateModule as TraceManagedModule;
                if (managedModule == null)
                {
                    break;
                }

                // we also sort all modules with the same module ID by unload time
                if (!(timeQPC < candidateModule.unloadTimeQPC))
                {
                    break;
                }

                // Is it in range?
                if (candidateModule.loadTimeQPC <= timeQPC)
                {
                    return managedModule;
                }

                --index;
            }
            return null;
        }
        /// <summary>
        /// Finds the index and module for an address that lives within the image.  If the module
        /// did not match the new entry should go at index+1.
        /// </summary>
        internal TraceLoadedModule FindModuleAndIndexContainingAddress(Address address, long timeQPC, out int index)
        {
            modules.BinarySearch((ulong)address, out index, compareByKey);
            // Index now points at the last place where module.ImageBase <= address;
            // Search backwards from where for a module that is loaded and in range.
            int candidateIndex = index;
            while (candidateIndex >= 0)
            {
                TraceLoadedModule canidateModule = modules[candidateIndex];
                // The table contains both native modules (where the key is the image base) and managed (where it is the ModuleID)
                // We only care about the native case.
                if (canidateModule.key == canidateModule.ImageBase)
                {
                    ulong candidateImageEnd = (ulong)canidateModule.ImageBase + (uint)canidateModule.ModuleFile.ImageSize;
                    if ((ulong)address < candidateImageEnd)
                    {
                        // Have we found a match?
                        if ((ulong)canidateModule.ImageBase <= (ulong)address)
                        {
                            if (canidateModule.loadTimeQPC <= timeQPC && timeQPC <= canidateModule.unloadTimeQPC)
                            {
                                index = candidateIndex;
                                return canidateModule;
                            }
                        }
                    }
                    else if (!canidateModule.overlaps)
                    {
                        break;
                    }
                }
                --candidateIndex;
            }
            // We return the index associated with the binary search.
            return null;
        }
        private void InsertAndSetOverlap(int moduleIndex, TraceLoadedModule module)
        {
            modules.Insert(moduleIndex, module);      // put it where it belongs in the sorted list

            // Does it overlap with the previous entry
            if (moduleIndex > 0)
            {
                var prevModule = modules[moduleIndex - 1];
                ulong prevImageEnd = (ulong)prevModule.ImageBase + (uint)prevModule.ModuleFile.ImageSize;
                if (prevImageEnd > (ulong)module.ImageBase)
                {
                    prevModule.overlaps = true;
                    module.overlaps = true;
                }
            }
            // does it overlap with the next entry
            if (moduleIndex + 1 < modules.Count)
            {
                var nextModule = modules[moduleIndex + 1];
                ulong moduleImageEnd = (ulong)module.ImageBase + (uint)module.ModuleFile.ImageSize;
                if (moduleImageEnd > (ulong)nextModule.ImageBase)
                {
                    nextModule.overlaps = true;
                    module.overlaps = true;
                }
            }

            // I should not have to look at entries further away
        }
        internal static readonly Func<ulong, TraceLoadedModule, int> compareByKey = delegate (ulong x, TraceLoadedModule y)
        {
            if (x > y.key)
            {
                return 1;
            }

            if (x < y.key)
            {
                return -1;
            }

            return 0;
        };

        [Conditional("DEBUG")]
        private void CheckClassInvarients()
        {
            // Modules better be sorted
            ulong lastkey = 0;
            TraceLoadedModule lastModule = null;
            for (int i = 0; i < modules.Count; i++)
            {
                TraceLoadedModule module = modules[i];
                Debug.Assert(module.key != 0);
                Debug.Assert(module.key >= lastkey, "regions not sorted!");

                TraceManagedModule asManaged = module as TraceManagedModule;
                if (asManaged != null)
                {
                    Debug.Assert((ulong)asManaged.ModuleID == module.key);
                }
                else
                {
                    Debug.Assert((ulong)module.ImageBase == module.key);
#if false // TODO FIX NOW enable fails on eventSourceDemo.etl file
                    if (lastModule != null && (ulong)lastModule.ImageBase + (uint)lastModule.ModuleFile.ImageSize > (ulong)module.ImageBase)
                        Debug.Assert(lastModule.overlaps && module.overlaps, "Modules overlap but don't delcare that they do");
#endif
                }
                lastkey = module.key;
                lastModule = module;
            }
        }

        internal TraceLoadedModules(TraceProcess process)
        {
            this.process = process;
        }
        void IFastSerializable.ToStream(Serializer serializer)
        {
            serializer.Write(process);
            serializer.Log("<WriteCollection count=\"" + modules.Count + "\">\r\n");
            serializer.Write(modules.Count);
            for (int i = 0; i < modules.Count; i++)
            {
                serializer.Write(modules[i]);
            }

            serializer.Log("</WriteCollection>\r\n");
        }
        void IFastSerializable.FromStream(Deserializer deserializer)
        {
            deserializer.Read(out process);
            Debug.Assert(modules.Count == 0);
            int count; deserializer.Read(out count);
            for (int i = 0; i < count; i++)
            {
                TraceLoadedModule elem; deserializer.Read(out elem);
                modules.Add(elem);
            }
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            throw new NotImplementedException(); // GetEnumerator
        }

        private TraceProcess process;
        private GrowableArray<TraceLoadedModule> modules;               // Contains unmanaged modules sorted by key
        #endregion
    }

    /// <summary>
    /// A TraceLoadedModule represents a module (DLL or EXE) that was loaded into a process.  It represents
    /// the time that this module was mapped into the processes address space.
    /// </summary>
    public class TraceLoadedModule : IFastSerializable
    {
        /// <summary>
        /// The address where the DLL or EXE was loaded.   Will return 0 for managed modules without NGEN images.
        /// </summary>
        public Address ImageBase
        {
            get
            {
                if (moduleFile == null)
                {
                    return 0;
                }
                else
                {
                    return moduleFile.ImageBase;
                }
            }
        }
        /// <summary>
        /// The load time is the time the LoadLibrary was done if it was loaded from a file, otherwise is the
        /// time the CLR loaded the module.  Expressed as a DateTime
        /// </summary>
        public DateTime LoadTime { get { return DateTime.FromFileTime(loadTimeQPC); } }
        /// <summary>
        /// The load time is the time the LoadLibrary was done if it was loaded from a file, otherwise is the
        /// time the CLR loaded the module.  Expressed as as MSec from the beginning of the trace.
        /// </summary>
        public double LoadTimeRelativeMSec { get { return Process.Log.QPCTimeToRelMSec(loadTimeQPC); } }

        /// <summary>
        /// The load time is the time the FreeLibrary was done if it was unmanaged, otherwise is the
        /// time the CLR unloaded the module.  Expressed as a DateTime
        /// </summary>
        public DateTime UnloadTime { get { return DateTime.FromFileTime(unloadTimeQPC); } }
        /// <summary>
        /// The load time is the time the FreeLibrary was done if it was unmanaged, otherwise is the
        /// time the CLR unloaded the module.  Expressed as MSec from the beginning of the trace.
        /// </summary>
        public double UnloadTimeRelativeMSec { get { return Process.Log.QPCTimeToRelMSec(unloadTimeQPC); } }

        /// <summary>
        /// The process that loaded this module
        /// </summary>
        public TraceProcess Process { get { return process; } }
        /// <summary>
        /// An ID that uniquely identifies the module in within the process.  Works for both the managed and unmanaged case.
        /// </summary>
        public virtual long ModuleID { get { return (long)ImageBase; } }

        /// <summary>
        /// If this managedModule was a file that was mapped into memory (eg LoadLibary), then ModuleFile points at
        /// it.  If a managed module does not have a file associated with it, this can be null.
        /// </summary>
        public TraceModuleFile ModuleFile { get { return moduleFile; } }
        /// <summary>
        /// Shortcut for ModuleFile.FilePath, but returns the empty string if ModuleFile is null
        /// </summary>
        public string FilePath
        {
            get
            {
                if (ModuleFile == null)
                {
                    return "";
                }
                else
                {
                    return ModuleFile.FilePath;
                }
            }
        }
        /// <summary>
        /// Shortcut for ModuleFile.Name, but returns the empty string if ModuleFile is null
        /// </summary>
        public string Name
        {
            get
            {
                if (ModuleFile == null)
                {
                    return "";
                }
                else
                {
                    return ModuleFile.Name;
                }
            }
        }
        // TODO: provide a way of getting at all the loaded images.
        /// <summary>
        /// Because .NET applications have AppDomains, a module that is loaded once from a process
        /// perspective, might be loaded several times (once for each AppDomain) from a .NET perspective
        /// <para> This property returns the loadedModule record for the first such managed module
        /// load associated with this load.
        /// </para>
        /// </summary>
        public TraceManagedModule ManagedModule { get { return managedModule; } }
        /// <summary>
        /// An XML representation of the TraceLoadedModule (used for debugging)
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            string moduleFileRef = "";
            return "<TraceLoadedModule " +
                    "Name=" + XmlUtilities.XmlQuote(Name).PadRight(24) + " " +
                    moduleFileRef +
                    "ImageBase=" + XmlUtilities.XmlQuoteHex((ulong)ImageBase) + " " +
                    "ImageSize=" + XmlUtilities.XmlQuoteHex((ModuleFile != null) ? ModuleFile.ImageSize : 0) + " " +
                    "LoadTimeRelative=" + XmlUtilities.XmlQuote(LoadTimeRelativeMSec) + " " +
                    "UnloadTimeRelative=" + XmlUtilities.XmlQuote(UnloadTimeRelativeMSec) + " " +
                    "FileName=" + XmlUtilities.XmlQuote(FilePath) + " " +
                   "/>";
        }

        #region Private

        internal TraceLoadedModule(TraceProcess process, TraceModuleFile moduleFile, Address imageBase)
        {
            this.process = process;
            this.moduleFile = moduleFile;
            unloadTimeQPC = long.MaxValue;
            key = (ulong)imageBase;
        }
        internal TraceLoadedModule(TraceProcess process, TraceModuleFile moduleFile, long moduleID)
        {
            this.process = process;
            this.moduleFile = moduleFile;
            unloadTimeQPC = long.MaxValue;
            key = (ulong)moduleID;
        }

        /// <summary>
        /// See IFastSerializable.ToStream.
        /// </summary>
        void IFastSerializable.ToStream(Serializer serializer) { ToStream(serializer); }
        internal void ToStream(Serializer serializer)
        {
            serializer.Write(loadTimeQPC);
            serializer.Write(unloadTimeQPC);
            serializer.Write(managedModule);
            serializer.Write(process);
            serializer.Write(moduleFile);
            serializer.Write((long)key);
            serializer.Write(overlaps);
        }
        /// <summary>
        /// See IFastSerializable.FromStream.
        /// </summary>
        void IFastSerializable.FromStream(Deserializer deserializer) { FromStream(deserializer); }
        internal void FromStream(Deserializer deserializer)
        {
            long address;

            deserializer.Read(out loadTimeQPC);
            deserializer.Read(out unloadTimeQPC);
            deserializer.Read(out managedModule);
            deserializer.Read(out process);
            deserializer.Read(out moduleFile);
            deserializer.Read(out address); key = (ulong)address;
            deserializer.Read(out overlaps);
        }

        internal ulong key;                          // Either the base address (for unmanaged) or moduleID (managed)
        internal bool overlaps;                      // address range overlaps with other modules in the list.
        internal long loadTimeQPC;
        internal long unloadTimeQPC;
        internal TraceManagedModule managedModule;
        private TraceProcess process;
        private TraceModuleFile moduleFile;         // Can be null (modules with files)

        internal int stackVisitedID;                // Used to determine if we have already visited this node or not.
        #endregion
    }
    /// <summary>
    /// A TraceManagedModule represents the loading of a .NET module into .NET AppDomain.
    /// It represents the time that that module an be used in the AppDomain.
    /// </summary>
    public sealed class TraceManagedModule : TraceLoadedModule, IFastSerializable
    {
        /// <summary>
        /// The module ID that the .NET Runtime uses to identify the file (module) associated with this managed module
        /// </summary>
        public override long ModuleID { get { return (long)key; } }
        /// <summary>
        /// The Assembly ID that the .NET Runtime uses to identify the assembly associated with this managed module.
        /// </summary>
        public long AssemblyID { get { return assemblyID; } }
        /// <summary>
        /// Returns true if the managed module was loaded AppDOmain Neutral (its code can be shared by all appdomains in the process.
        /// </summary>
        public bool IsAppDomainNeutral { get { return (flags & ModuleFlags.DomainNeutral) != 0; } }
        /// <summary>
        /// If the managed module is an IL module that has an NGEN image, return it.
        /// </summary>
        public TraceLoadedModule NativeModule { get { return nativeModule; } }
        /// <summary>
        /// An XML representation of the TraceManagedModule (used for debugging)
        /// </summary>
        public override string ToString()
        {
            string nativeInfo = "";
            if (NativeModule != null)
            {
                nativeInfo = "<NativeModule>\r\n  " + NativeModule.ToString() + "\r\n</NativeModule>\r\n";
            }

            return "<TraceManagedModule " +
                   "ModuleID=" + XmlUtilities.XmlQuoteHex((ulong)ModuleID) + " " +
                   "AssemblyID=" + XmlUtilities.XmlQuoteHex((ulong)AssemblyID) + ">\r\n" +
                   "  " + base.ToString() + "\r\n" +
                   nativeInfo +
                   "</TraceManagedModule>";
        }
        #region Private
        internal TraceManagedModule(TraceProcess process, TraceModuleFile moduleFile, long moduleID)
            : base(process, moduleFile, moduleID)
        { }

        internal void InitializeNativeModuleIsReadyToRun()
        {
            if (NativeModule != null && (flags & ModuleFlags.ReadyToRunModule) != ModuleFlags.None)
            {
                NativeModule.ModuleFile.isReadyToRun = true;
            }
        }

        // TODO use or remove
        internal TraceLoadedModule nativeModule;        // non-null for IL managed modules
        internal long assemblyID;
        internal ModuleFlags flags;

        void IFastSerializable.ToStream(Serializer serializer)
        {
            base.ToStream(serializer);
            serializer.Write(assemblyID);
            serializer.Write(nativeModule);
            serializer.Write((int)flags);
        }
        void IFastSerializable.FromStream(Deserializer deserializer)
        {
            int flags;
            base.FromStream(deserializer);
            deserializer.Read(out assemblyID);
            deserializer.Read(out nativeModule);
            deserializer.Read(out flags); this.flags = (ModuleFlags)flags;
            InitializeNativeModuleIsReadyToRun();
        }
        #endregion
    }

    /// <summary>
    /// CallStackIndex uniquely identifies a callstack within the log.  Valid values are between 0 and
    /// TraceCallStacks.Count-1. Thus, an array can be used to 'attach' data to a call stack.
    /// </summary>
    public enum CallStackIndex
    {
        /// <summary>
        /// Returned when no appropriate CallStack exists.
        /// </summary>
        Invalid = -1
    };
    /// <summary>
    /// Call stacks are so common in most traces, that having a .NET object (a TraceEventCallStack) for
    /// each one is often too expensive.   As optimization, TraceLog also assigns a call stack index
    /// to every call stack and this index uniquely identifies the call stack in a very light weight fashion.
    /// <para>
    /// To be useful, however you need to be able to ask questions about a call stack index without creating
    /// a TraceEventCallStack.   This is the primary purpose of a TraceCallStacks (accessible from TraceLog.CallStacks).
    /// It has a set of
    /// methods that take a CallStackIndex and return properties of the call stack (like its caller or
    /// its code address).
    /// </para>
    /// </summary>
    public sealed class TraceCallStacks : IFastSerializable, IEnumerable<TraceCallStack>
    {
        /// <summary>
        /// Returns the count of call stack indexes (all Call Stack indexes are strictly less than this).
        /// </summary>
        public int Count { get { return callStacks.Count; } }
        /// <summary>
        /// Given a call stack index, return the code address index representing the top most frame associated with it
        /// </summary>
        public CodeAddressIndex CodeAddressIndex(CallStackIndex stackIndex) { return callStacks[(int)stackIndex].codeAddressIndex; }
        /// <summary>
        /// Given a call stack index, look up the call stack  index for caller.  Returns CallStackIndex.Invalid at top of stack.
        /// </summary>
        public CallStackIndex Caller(CallStackIndex stackIndex)
        {
            CallStackIndex ret = callStacks[(int)stackIndex].callerIndex;
            Debug.Assert(ret < stackIndex);         // Stacks should be getting 'smaller'
            if (ret < 0)                            // We encode the threads of the stack as the negative thread index.
            {
                ret = CallStackIndex.Invalid;
            }

            return ret;
        }
        /// <summary>
        /// Given a call stack index, returns the number of callers for the call stack
        /// </summary>
        public int Depth(CallStackIndex stackIndex)
        {
            int ret = 0;
            while (stackIndex >= 0)
            {
                Debug.Assert(ret < 1000000);       // Catches infinite recursion
                ret++;
                stackIndex = callStacks[(int)stackIndex].callerIndex;
            }
            return ret;
        }

        /// <summary>
        /// Given a call stack index, returns a TraceCallStack for it.
        /// </summary>
        public TraceCallStack this[CallStackIndex callStackIndex]
        {
            get
            {
                // We don't bother interning.
                if (callStackIndex == CallStackIndex.Invalid)
                {
                    return null;
                }

                return new TraceCallStack(this, callStackIndex);
            }
        }
        /// <summary>
        /// Returns the TraceCodeAddresses instance that can resolve CodeAddressIndexes in the TraceLog
        /// </summary>
        public TraceCodeAddresses CodeAddresses { get { return codeAddresses; } }
        /// <summary>
        /// Given a call stack index, returns the ThreadIndex which represents the thread for the call stack
        /// </summary>
        public ThreadIndex ThreadIndex(CallStackIndex stackIndex)
        {
            // Go to the thread of the stack
            while (stackIndex >= 0)
            {
                Debug.Assert(callStacks[(int)stackIndex].callerIndex < stackIndex);
                stackIndex = callStacks[(int)stackIndex].callerIndex;
            }
            // The threads of the stack is marked by a negative number, which is the thread index -2
            ThreadIndex ret = (ThreadIndex)((-((int)stackIndex)) - 2);
            Debug.Assert(-1 <= (int)ret && (int)ret < log.Threads.Count);
            return ret;
        }
        /// <summary>
        /// Given a call stack index, returns the TraceThread which represents the thread for the call stack
        /// </summary>
        public TraceThread Thread(CallStackIndex stackIndex)
        {
            return log.Threads[ThreadIndex(stackIndex)];
        }
        /// <summary>
        /// An XML representation of the TraceCallStacks (used for debugging)
        /// </summary>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<TraceCallStacks Count=").Append(XmlUtilities.XmlQuote(callStacks.Count)).AppendLine(">");
            foreach (TraceCallStack callStack in this)
            {
                sb.Append("  ").Append(callStack.ToString()).AppendLine();
            }

            sb.AppendLine("</TraceCallStacks>");
            return sb.ToString();
        }
        #region private
        /// <summary>
        /// IEnumerable Support
        /// </summary>
        public IEnumerator<TraceCallStack> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
            {
                yield return this[(CallStackIndex)i];
            }
        }

        internal TraceCallStacks(TraceLog log, TraceCodeAddresses codeAddresses)
        {
            this.log = log;
            this.codeAddresses = codeAddresses;
        }

        /// <summary>
        /// Used to 'undo' the effects of adding a eventToStack that you no longer want.  This happens when we find
        /// out that a eventToStack is actually got more callers in it (when a eventToStack is split).
        /// </summary>
        /// <param name="origSize"></param>
        internal void SetSize(int origSize)
        {
            callStacks.RemoveRange(origSize, callStacks.Count - origSize);
        }

        /// <summary>
        /// Returns an index that represents the 'threads' of the stack.  It encodes the thread which owns this stack into this.
        /// We encode this as -ThreadIndex - 2 (since -1 is the Invalid node)
        /// </summary>
        internal static CallStackIndex GetRootForThread(ThreadIndex threadIndex)
        {
            return (CallStackIndex)(-((int)threadIndex) + (int)CallStackIndex.Invalid - 1);
        }
        private static ThreadIndex GetThreadForRoot(CallStackIndex root)
        {
            ThreadIndex ret = (ThreadIndex)((-((int)root)) + (int)CallStackIndex.Invalid - 1);
            Debug.Assert(ret >= 0);
            return ret;
        }

        internal unsafe CallStackIndex GetStackIndexForStackEvent(void* addresses,
            int addressCount, int pointerSize, TraceThread thread, CallStackIndex start = CallStackIndex.Invalid)
        {
            if (addressCount == 0)
            {
                return CallStackIndex.Invalid;
            }

            if (start == CallStackIndex.Invalid)
            {
                start = GetRootForThread(thread.ThreadIndex);
            }

            return (pointerSize == 8) ?
                GetStackIndexForStackEvent64((ulong*)addresses, addressCount, thread.Process, start) :
                GetStackIndexForStackEvent32((uint*)addresses, addressCount, thread.Process, start);
        }

        private unsafe CallStackIndex GetStackIndexForStackEvent32(uint* addresses, int addressCount, TraceProcess process, CallStackIndex start)
        {
            for (var it = &addresses[addressCount]; it-- != addresses;)
            {
                CodeAddressIndex codeAddress = codeAddresses.GetOrCreateCodeAddressIndex(process, *it);
                start = InternCallStackIndex(codeAddress, start);
            }

            return start;
        }

        private unsafe CallStackIndex GetStackIndexForStackEvent64(ulong* addresses, int addressCount, TraceProcess process, CallStackIndex start)
        {
            for (var it = &addresses[addressCount]; it-- != addresses;)
            {
                CodeAddressIndex codeAddress = codeAddresses.GetOrCreateCodeAddressIndex(process, *it);
                start = InternCallStackIndex(codeAddress, start);
            }

            return start;
        }

        internal CallStackIndex InternCallStackIndex(CodeAddressIndex codeAddressIndex, CallStackIndex callerIndex)
        {
            if (callStacks.Count == 0)
            {
                // allocate a reasonable size for the interning tables.
                callStacks = new GrowableArray<CallStackInfo>(10000);
                callees = new GrowableArray<List<CallStackIndex>>(10000);
            }

            List<CallStackIndex> frameCallees;
            if (callerIndex < 0)        // Hit the last stack as we unwind to the root.  We need to encode the thread.
            {
                Debug.Assert(callerIndex != CallStackIndex.Invalid);        // We always end with the thread.
                int threadIndex = (int)GetThreadForRoot(callerIndex);
                if (threadIndex >= threads.Count)
                {
                    threads.Count = threadIndex + 1;
                }

                frameCallees = threads[threadIndex] ?? (threads[threadIndex] = new List<CallStackIndex>());
            }
            else
            {
                frameCallees = callees[(int)callerIndex] ?? (callees[(int)callerIndex] = new List<CallStackIndex>(4));
            }

            // Search backwards, assuming that most recently added is the most likely hit.
            for (int i = frameCallees.Count - 1; i >= 0; --i)
            {
                CallStackIndex calleeIndex = frameCallees[i];
                if (callStacks[(int)calleeIndex].codeAddressIndex == codeAddressIndex)
                {
                    Debug.Assert(calleeIndex > callerIndex);
                    return calleeIndex;
                }
            }

            CallStackIndex ret = (CallStackIndex)callStacks.Count;
            callStacks.Add(new CallStackInfo(codeAddressIndex, callerIndex));
            frameCallees.Add(ret);
            callees.Add(null);
            Debug.Assert(callees.Count == callStacks.Count);
            return ret;
        }

        private struct CallStackInfo
        {
            internal CallStackInfo(CodeAddressIndex codeAddressIndex, CallStackIndex callerIndex)
            {
                this.codeAddressIndex = codeAddressIndex;
                this.callerIndex = callerIndex;
            }

            internal CodeAddressIndex codeAddressIndex;
            internal CallStackIndex callerIndex;
        }

        void IFastSerializable.ToStream(Serializer serializer)
        {
            serializer.Write(log);
            serializer.Write(codeAddresses);
            lazyCallStacks.Write(serializer, delegate
            {
                serializer.Log("<WriteCollection name=\"callStacks\" count=\"" + callStacks.Count + "\">\r\n");
                serializer.Write(callStacks.Count);
                for (int i = 0; i < callStacks.Count; i++)
                {
                    serializer.Write((int)callStacks[i].codeAddressIndex);
                    serializer.Write((int)callStacks[i].callerIndex);
                }
                serializer.Log("</WriteCollection>\r\n");
            });
        }

        void IFastSerializable.FromStream(Deserializer deserializer)
        {
            deserializer.Read(out log);
            deserializer.Read(out codeAddresses);

            lazyCallStacks.Read(deserializer, delegate
            {
                deserializer.Log("<Marker Name=\"callStacks\"/>");
                int count = deserializer.ReadInt();
                callStacks = new GrowableArray<CallStackInfo>(count + 1);
                CallStackInfo callStackInfo = new CallStackInfo();
                for (int i = 0; i < count; i++)
                {
                    callStackInfo.codeAddressIndex = (CodeAddressIndex)deserializer.ReadInt();
                    callStackInfo.callerIndex = (CallStackIndex)deserializer.ReadInt();
                    callStacks.Add(callStackInfo);
                }
            });
            lazyCallStacks.FinishRead();        // TODO REMOVE
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            throw new NotImplementedException(); // GetEnumerator
        }

        // This is only used when converting maps.  Maps a call stack index to a list of call stack indexes that
        // were callees of it.    This is the list you need to search when interning.  There is also 'threads'
        // which is the list of call stack indexes where stack crawling stopped.
        private GrowableArray<List<CallStackIndex>> callees;    // For each callstack, these are all the call stacks that it calls.
        private GrowableArray<List<CallStackIndex>> threads;    // callees for threads of stacks, one for each thread
        private GrowableArray<CallStackInfo> callStacks;        // a field on CallStackInfo
        private DeferedRegion lazyCallStacks;
        private TraceCodeAddresses codeAddresses;
        private TraceLog log;
        #endregion
    }
    /// <summary>
    /// A TraceCallStack is a structure that represents a call stack as a linked list. Each TraceCallStack
    /// contains two properties, the CodeAddress for the current frame, and the TraceCallStack of the
    /// caller of this frame.   The Caller property will return null at the thread start frame.
    /// </summary>
    public sealed class TraceCallStack
    {
        /// <summary>
        ///  Return the CallStackIndex that uniquely identifies this call stack in the TraceLog.
        /// </summary>
        public CallStackIndex CallStackIndex { get { return stackIndex; } }
        /// <summary>
        /// Returns the TraceCodeAddress for the current method frame in the linked list of frames.
        /// </summary>
        public TraceCodeAddress CodeAddress { get { return callStacks.CodeAddresses[callStacks.CodeAddressIndex(stackIndex)]; } }
        /// <summary>
        /// The TraceCallStack for the caller of of the method represented by this call stack.  Returns null at the end of the list.
        /// </summary>
        public TraceCallStack Caller { get { return callStacks[callStacks.Caller(stackIndex)]; } }
        /// <summary>
        /// The depth (count of callers) of this call stack.
        /// </summary>
        public int Depth { get { return callStacks.Depth(stackIndex); } }
        /// <summary>
        /// An XML representation of the TraceCallStack (used for debugging)
        /// </summary>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder(4096);
            return ToString(sb).ToString();
        }
        /// <summary>
        /// Writes an XML representation of the TraceCallStack to the stringbuilder 'sb'
        /// </summary>
        public StringBuilder ToString(StringBuilder sb)
        {
            TraceCallStack cur = this;
            while (cur != null)
            {
                sb.Append("CallStackIndex=\"").Append(cur.CallStackIndex).Append("\" ");
                cur.CodeAddress.ToString(sb).AppendLine();
                cur = cur.Caller;
            }
            return sb;
        }
        #region private
        internal TraceCallStack(TraceCallStacks stacks, CallStackIndex stackIndex)
        {
            callStacks = stacks;
            this.stackIndex = stackIndex;
        }

        private TraceCallStacks callStacks;
        private CallStackIndex stackIndex;
        #endregion
    }


    /// <summary>
    /// CodeAddressIndex uniquely identifies a symbolic codeAddress within the log .
    /// Valid values are between 0 and TraceCodeAddresses.Count. Thus, an array
    /// can be used to 'attach' data to a code address.
    /// </summary>
    public enum CodeAddressIndex
    {
        /// <summary>
        /// Returned when no appropriate Method exists.
        /// </summary>
        Invalid = -1
    };
    /// <summary>
    /// Code addresses are so common in most traces, that having a .NET object (a TraceCodeAddress) for
    /// each one is often too expensive.   As optimization, TraceLog also assigns a code address index
    /// to every code address and this index uniquely identifies the code address in a very light weight fashion.
    /// <para>
    /// To be useful, however you need to be able to ask questions about a code address index without creating
    /// a TraceCodeAddress.   This is the primary purpose of a TraceCodeAddresses (accessible from TraceLog.CodeAddresses).
    /// It has a set of
    /// methods that take a CodeAddressIndex and return properties of the code address (like its method, address, and module file)
    /// </para>
    /// </summary>
    public sealed class TraceCodeAddresses : IFastSerializable, IEnumerable<TraceCodeAddress>
    {
        /// <summary>
        /// Chunk size for <see cref="codeAddressObjects"/>
        /// </summary>
        private const int ChunkSize = 4096;

        /// <summary>
        /// Returns the count of code address indexes (all code address indexes are strictly less than this).
        /// </summary>
        public int Count { get { return codeAddresses.Count; } }

        /// <summary>
        /// Given a code address index, return the name associated with it (the method name).  It will
        /// have the form MODULE!METHODNAME.   If the module name is unknown a ? is used, and if the
        /// method name is unknown a hexadecimal number is used as the method name.
        /// </summary>
        public string Name(CodeAddressIndex codeAddressIndex)
        {
            if (this.names == null)
            {
                this.names = new string[Count];
            }

            string name = this.names[(int)codeAddressIndex];

            if (name == null)
            {
                string moduleName = "?";
                ModuleFileIndex moduleIdx = ModuleFileIndex(codeAddressIndex);
                if (moduleIdx != Microsoft.Diagnostics.Tracing.Etlx.ModuleFileIndex.Invalid)
                {
                    moduleName = moduleFiles[moduleIdx].Name;
                }

                string methodName;
                MethodIndex methodIndex = MethodIndex(codeAddressIndex);
                if (methodIndex != Microsoft.Diagnostics.Tracing.Etlx.MethodIndex.Invalid)
                {
                    methodName = Methods.FullMethodName(methodIndex);
                }
                else
                {
                    methodName = "0x" + ((ulong)Address(codeAddressIndex)).ToString("x");
                }

                this.names[(int)codeAddressIndex] = name = moduleName + "!" + methodName;
            }

            return name;
        }
        /// <summary>
        /// Given a code address index, returns the virtual address of the code in the process.
        /// </summary>
        public Address Address(CodeAddressIndex codeAddressIndex) { return codeAddresses[(int)codeAddressIndex].Address; }
        /// <summary>
        /// Given a code address index, returns the index for the module file (representing the file's path)
        /// </summary>
        public ModuleFileIndex ModuleFileIndex(CodeAddressIndex codeAddressIndex)
        {
            var ret = codeAddresses[(int)codeAddressIndex].GetModuleFileIndex(this);
            // If we have a method index, fetch the module file from the method.
            if (ret == Microsoft.Diagnostics.Tracing.Etlx.ModuleFileIndex.Invalid)
            {
                ret = Methods.MethodModuleFileIndex(MethodIndex(codeAddressIndex));
            }

            return ret;
        }
        /// <summary>
        /// Given a code address index, returns the index for the method associated with the code address (it may return MethodIndex.Invalid
        /// if no method can be found).
        /// </summary>
        public MethodIndex MethodIndex(CodeAddressIndex codeAddressIndex) { return codeAddresses[(int)codeAddressIndex].GetMethodIndex(this); }
        /// <summary>
        /// Given a code address index, returns the module file (the DLL paths) associated with it
        /// </summary>
        public TraceModuleFile ModuleFile(CodeAddressIndex codeAddressIndex) { return ModuleFiles[ModuleFileIndex(codeAddressIndex)]; }
        /// <summary>
        /// If the code address is associated with managed code, return the IL offset within the method.    If the method
        /// is unmanaged -1 is returned.   To determine the IL offset the PDB for the NGEN image (for NGENed code) or the
        /// correct .NET events (for JIT compiled code) must be present.   If this information is not present -1 is returned.
        /// </summary>
        public int ILOffset(CodeAddressIndex codeAddressIndex)
        {
            ILToNativeMap ilMap = NativeMap(codeAddressIndex);
            if (ilMap == null)
            {
                return -1;
            }

            return ilMap.GetILOffsetForNativeAddress(Address(codeAddressIndex));
        }

        public OptimizationTier OptimizationTier(CodeAddressIndex codeAddressIndex)
        {
            Debug.Assert((int)codeAddressIndex < codeAddresses.Count);
            return codeAddresses[(int)codeAddressIndex].optimizationTier;
        }

        /// <summary>
        /// Given a code address index, returns a TraceCodeAddress for it.
        /// </summary>
        public TraceCodeAddress this[CodeAddressIndex codeAddressIndex]
        {
            get
            {
                if (codeAddressIndex == CodeAddressIndex.Invalid)
                {
                    return null;
                }

                int chunk = (int)codeAddressIndex / ChunkSize;
                int offset = (int)codeAddressIndex % ChunkSize;

                if (this.codeAddressObjects == null)
                {
                    this.codeAddressObjects = new TraceCodeAddress[chunk + 1][];
                }
                else if (chunk >= this.codeAddressObjects.Length)
                {
                    Array.Resize(ref this.codeAddressObjects, Math.Max(this.codeAddressObjects.Length * 2, chunk + 1));
                }

                TraceCodeAddress[] data = this.codeAddressObjects[chunk];

                if (data == null)
                {
                    data = this.codeAddressObjects[chunk] = new TraceCodeAddress[ChunkSize];
                }

                TraceCodeAddress ret = data[offset];

                if (ret == null)
                {
                    ret = new TraceCodeAddress(this, codeAddressIndex);
                    data[offset] = ret;
                }

                return ret;
            }
        }

        /// <summary>
        /// Returns the TraceMethods object that can look up information from MethodIndexes
        /// </summary>
        public TraceMethods Methods { get { return methods; } }
        /// <summary>
        /// Returns the TraceModuleFiles that can look up information about ModuleFileIndexes
        /// </summary>
        public TraceModuleFiles ModuleFiles { get { return moduleFiles; } }
        /// <summary>
        /// Indicates the number of managed method records that were encountered.  This is useful to understand if symbolic information 'mostly works'.
        /// </summary>
        public int ManagedMethodRecordCount { get { return managedMethodRecordCount; } }
        /// <summary>
        /// Initially CodeAddresses for unmanaged code will have no useful name.  Calling LookupSymbolsForModule
        /// lets you resolve the symbols for a particular file so that the TraceCodeAddresses for that DLL
        /// will have Methods (useful names) associated with them.
        /// </summary>
        public void LookupSymbolsForModule(SymbolReader reader, TraceModuleFile file)
        {
            var codeAddrs = new List<CodeAddressIndex>();
            for (int i = 0; i < Count; i++)
            {
                if (codeAddresses[i].GetModuleFileIndex(this) == file.ModuleFileIndex &&
                    codeAddresses[i].GetMethodIndex(this) == Microsoft.Diagnostics.Tracing.Etlx.MethodIndex.Invalid)
                {
                    codeAddrs.Add((CodeAddressIndex)i);
                }
            }

            if (codeAddrs.Count == 0)
            {
                reader.m_log.WriteLine("No code addresses are in {0} that have not already been looked up.", file.Name);
                return;
            }

            // sort them.  TODO can we get away without this?
            codeAddrs.Sort(delegate (CodeAddressIndex x, CodeAddressIndex y)
            {
                ulong addrX = (ulong)Address(x);
                ulong addrY = (ulong)Address(y);
                if (addrX > addrY)
                {
                    return 1;
                }

                if (addrX < addrY)
                {
                    return -1;
                }

                return 0;
            });

            int totalAddressCount;

            // Skip to the addresses in this module
            var codeAddrEnum = codeAddrs.GetEnumerator();
            for (; ; )
            {
                if (!codeAddrEnum.MoveNext())
                {
                    return;
                }

                if (Address(codeAddrEnum.Current) >= file.ImageBase)
                {
                    break;
                }
            }
            try
            {
                LookupSymbolsForModule(reader, file, codeAddrEnum, true, out totalAddressCount);
            }
            catch (OutOfMemoryException)
            {
                // TODO find out why this happens?   I think this is because we try to do a ReadRVA
                // a managed-only module
                reader.m_log.WriteLine("Error: Caught out of memory exception on file " + file.Name + ".   Skipping.");
            }
            catch (Exception e)
            {
                reader.m_log.WriteLine("An exception occurred during symbol lookup.  Continuing...");
                reader.m_log.WriteLine("Exception: " + e.ToString());
            }
        }
        /// <summary>
        /// A TraceCodeAddress can contain a method name, but does not contain number information.   To
        /// find line number information you must read the PDB again and fetch it.   This is what
        /// GetSoruceLine does.
        /// <para>
        /// Given a SymbolReader (which knows how to look up PDBs) and a code address index (which
        /// represent a particular point in execution), find a SourceLocation (which represents a
        /// particular line number in a particular source file associated with the code address.
        /// Returns null if anything goes wrong (and diagnostic information will be written to the
        /// log file associated with the SymbolReader.
        /// </para>
        /// </summary>
        public SourceLocation GetSourceLine(SymbolReader reader, CodeAddressIndex codeAddressIndex)
        {
            reader.m_log.WriteLine("GetSourceLine: Getting source line for code address index {0:x}", codeAddressIndex);

            if (codeAddressIndex == CodeAddressIndex.Invalid)
            {
                reader.m_log.WriteLine("GetSourceLine: Invalid code address");
                return null;
            }

            var moduleFile = log.CodeAddresses.ModuleFile(codeAddressIndex);
            if (moduleFile == null)
            {
                reader.m_log.WriteLine("GetSourceLine: Could not find moduleFile {0:x}.", log.CodeAddresses.Address(codeAddressIndex));
                return null;
            }

            NativeSymbolModule windowsSymbolModule;
            ManagedSymbolModule ilSymbolModule;
            // Is this address in the native code of the module (inside the bounds of module)
            var address = log.CodeAddresses.Address(codeAddressIndex);
            reader.m_log.WriteLine("GetSourceLine: address for code address is {0:x} module {1}", address, moduleFile.Name);
            if (moduleFile.ImageBase != 0 && moduleFile.ImageBase <= address && address < moduleFile.ImageEnd)
            {
                var methodRva = (uint)(address - moduleFile.ImageBase);
                reader.m_log.WriteLine("GetSourceLine: address within module: native case, VA = {0:x}, ImageBase = {1:x}, RVA = {2:x}", address, moduleFile.ImageBase, methodRva);
                windowsSymbolModule = OpenPdbForModuleFile(reader, moduleFile) as NativeSymbolModule;
                if (windowsSymbolModule != null)
                {
                    string ilAssemblyName;
                    uint ilMetaDataToken;
                    int ilMethodOffset;

                    var ret = windowsSymbolModule.SourceLocationForRva(methodRva, out ilAssemblyName, out ilMetaDataToken, out ilMethodOffset);
                    if (ret == null && ilAssemblyName != null)
                    {
                        // We found the RVA, but this is an NGEN image, and so we could not convert it completely to a line number.
                        // Look up the IL PDB needed
                        reader.m_log.WriteLine("GetSourceLine:  Found mapping from Native to IL assembly {0} Token 0x{1:x} offset 0x{2:x}",
                        ilAssemblyName, ilMetaDataToken, ilMethodOffset);
                        if (moduleFile.ManagedModule != null)
                        {
                            // In CoreCLR, the managed image IS the native image, so has a .ni suffix, remove it if present.
                            var moduleFileName = moduleFile.ManagedModule.Name;
                            if (moduleFileName.EndsWith(".ni", StringComparison.OrdinalIgnoreCase) || moduleFileName.EndsWith(".il", StringComparison.OrdinalIgnoreCase))
                            {
                                moduleFileName = moduleFileName.Substring(0, moduleFileName.Length - 3);
                            }

                            // TODO FIX NOW work for any assembly, not just he corresponding IL assembly.
                            if (string.Compare(moduleFileName, ilAssemblyName, StringComparison.OrdinalIgnoreCase) == 0)
                            {
                                TraceModuleFile ilAssemblyModule = moduleFile.ManagedModule;
                                ilSymbolModule = OpenPdbForModuleFile(reader, ilAssemblyModule);
                                if (ilSymbolModule != null)
                                {
                                    reader.m_log.WriteLine("GetSourceLine: Found PDB for IL module {0}", ilSymbolModule.SymbolFilePath);
                                    ret = ilSymbolModule.SourceLocationForManagedCode(ilMetaDataToken, ilMethodOffset);
                                }
                            }
                            else
                            {
                                reader.m_log.WriteLine("GetSourceLine: found IL assembly name {0} != load assembly {1} ({2}) Giving up",
                                    ilAssemblyName, moduleFileName, moduleFile.ManagedModule.FilePath);
                            }
                        }
                        else
                        {
                            reader.m_log.WriteLine("GetSourceLine: Could not find managed module for NGEN image {0}", moduleFile.FilePath);
                        }
                    }

                    // TODO FIX NOW, deal with this rather than simply warn.
                    if (ret == null && windowsSymbolModule.SymbolFilePath.EndsWith(".ni.pdb", StringComparison.OrdinalIgnoreCase))
                    {
                        reader.m_log.WriteLine("GetSourceLine: Warning could not find line information in {0}", windowsSymbolModule.SymbolFilePath);
                        reader.m_log.WriteLine("GetSourceLine: Maybe because the NGEN pdb was generated without being able to reach the IL PDB");
                        reader.m_log.WriteLine("GetSourceLine: If you are on the machine where the data was collected, deleting the file may help");
                    }

                    return ret;
                }
                reader.m_log.WriteLine("GetSourceLine: Failed to look up {0:x} in a PDB, checking for JIT", log.CodeAddresses.Address(codeAddressIndex));
            }

            // The address is not in the module, or we could not find the PDB, see if we have JIT information
            var methodIndex = log.CodeAddresses.MethodIndex(codeAddressIndex);
            if (methodIndex == Microsoft.Diagnostics.Tracing.Etlx.MethodIndex.Invalid)
            {
                reader.m_log.WriteLine("GetSourceLine: Could not find method for {0:x}", log.CodeAddresses.Address(codeAddressIndex));
                return null;
            }

            var methodToken = log.CodeAddresses.Methods.MethodToken(methodIndex);
            if (methodToken == 0)
            {
                reader.m_log.WriteLine("GetSourceLine: Could not find method for {0:x}", log.CodeAddresses.Address(codeAddressIndex));
                return null;
            }
            reader.m_log.WriteLine("GetSourceLine: Found JITTed method {0}, index {1:x} token {2:x}",
                log.CodeAddresses.Methods.FullMethodName(methodIndex), methodIndex, methodToken);

            // See if we have il offset information for the method.
            // var ilOffset = log.CodeAddresses.ILOffset(codeAddressIndex);
            var ilMap = log.CodeAddresses.NativeMap(codeAddressIndex);
            int ilOffset = 0;
            if (ilMap != null)
            {
                reader.m_log.WriteLine("GetSourceLine: Found an il-to-native mapping MethodIdx {0:x} Start {1:x} Len {2:x}",
                    ilMap.MethodIndex, ilMap.MethodStart, ilMap.MethodLength);

                // TODO remove after we are happy that this works properly.
                //for (int i = 0; i < ilMap.Map.Count; i++)
                //    reader.m_log.WriteLine("GetSourceLine:    {0,3} native {1,5:x} -> {2:x}",
                //        i, ilMap.Map[i].NativeOffset, ilMap.Map[i].ILOffset);

                ilOffset = ilMap.GetILOffsetForNativeAddress(address);
                reader.m_log.WriteLine("GetSourceLine: NativeOffset {0:x} ILOffset = {1:x}", address - ilMap.MethodStart, ilOffset);

                if (ilOffset < 0)
                {
                    ilOffset = 0;       // If we return the special ILProlog or ILEpilog values.
                }
            }

            // Get the IL file even if we are in an NGEN image.
            if (moduleFile.ManagedModule != null)
            {
                moduleFile = moduleFile.ManagedModule;
            }

            ilSymbolModule = OpenPdbForModuleFile(reader, moduleFile);
            if (ilSymbolModule == null)
            {
                reader.m_log.WriteLine("GetSourceLine: Failed to look up PDB for {0}", moduleFile.FilePath);
                return null;
            }

            return ilSymbolModule.SourceLocationForManagedCode((uint)methodToken, ilOffset);
        }
        /// <summary>
        /// The number of times a particular code address appears in the log.   Unlike TraceCodeAddresses.Count, which tries
        /// to share a code address as much as possible, TotalCodeAddresses counts the same code address in different
        /// call stacks (and even if in the same stack) as distinct.    This makes TotalCodeAddresses a better measure of
        /// the 'popularity' of a particular address (which can factor into decisions about whether to call LookupSymbolsForModule)
        /// <para>
        /// The sum of ModuleFile.CodeAddressesInModule for all modules should sum to this number.
        /// </para>
        /// </summary>
        public int TotalCodeAddresses { get { return totalCodeAddresses; } }
        /// <summary>
        /// If set to true, will only use the name of the module and not the PDB GUID to confirm that a PDB is correct
        /// for a given DLL.   Setting this value is dangerous because it is easy for the PDB to be for a different
        /// version of the DLL and thus give inaccurate method names.   Nevertheless, if a log file has no PDB GUID
        /// information associated with it, unsafe PDB matching is the only way to get at least some symbolic information.
        /// </summary>
        public bool UnsafePDBMatching { get; set; }

        /// <summary>
        /// Returns an XML representation of the TraceCodeAddresses (for debugging)
        /// </summary>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<TraceCodeAddresses Count=").Append(XmlUtilities.XmlQuote(codeAddresses.Count)).AppendLine(">");
            foreach (TraceCodeAddress codeAddress in this)
            {
                sb.Append("  ").Append(codeAddress.ToString()).AppendLine();
            }

            sb.AppendLine("</TraceCodeAddresses>");
            return sb.ToString();
        }
        #region private
        /// <summary>
        /// We expose ILToNativeMap internally so we can do diagnostics.
        /// </summary>
        internal ILToNativeMap NativeMap(CodeAddressIndex codeAddressIndex)
        {
            var ilMapIdx = codeAddresses[(int)codeAddressIndex].GetILMapIndex(this);
            if (ilMapIdx == ILMapIndex.Invalid)
            {
                return null;
            }

            return ILToNativeMaps[(int)ilMapIdx];
        }

        internal IEnumerable<CodeAddressIndex> GetAllIndexes
        {
            get
            {
                for (int i = 0; i < Count; i++)
                {
                    yield return (CodeAddressIndex)i;
                }
            }
        }

        internal TraceCodeAddresses(TraceLog log, TraceModuleFiles moduleFiles)
        {
            this.log = log;
            this.moduleFiles = moduleFiles;
            methods = new TraceMethods(this);
        }

        /// <summary>
        /// IEnumerable support.
        /// </summary>
        public IEnumerator<TraceCodeAddress> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
            {
                yield return this[(CodeAddressIndex)i];
            }
        }

        /// <summary>
        /// Called when JIT CLR Rundown events are processed. It will look if there is any
        /// address that falls into the range of the JIT compiled method and if so log the
        /// symbolic information (otherwise we simply ignore it)
        /// </summary>
        internal void AddMethod(MethodLoadUnloadVerboseTraceData data)
        {
            managedMethodRecordCount++;
            MethodIndex methodIndex = Microsoft.Diagnostics.Tracing.Etlx.MethodIndex.Invalid;
            ILMapIndex ilMap = ILMapIndex.Invalid;
            ModuleFileIndex moduleFileIndex = Microsoft.Diagnostics.Tracing.Etlx.ModuleFileIndex.Invalid;
            TraceManagedModule module = null;
            TraceProcess process = log.Processes.GetOrCreateProcess(data.ProcessID, data.TimeStampQPC);
            ForAllUnresolvedCodeAddressesInRange(process, data.MethodStartAddress, data.MethodSize, true, delegate (ref CodeAddressInfo info)
            {
                // If we already resolved, that means that the address was reused, so only add something if it does not already have
                // information associated with it.
                if (info.GetMethodIndex(this) == Microsoft.Diagnostics.Tracing.Etlx.MethodIndex.Invalid)
                {
                    // Lazily create the method since many methods never have code samples in them.
                    if (module == null)
                    {
                        module = process.LoadedModules.GetOrCreateManagedModule(data.ModuleID, data.TimeStampQPC);
                        moduleFileIndex = module.ModuleFile.ModuleFileIndex;
                        methodIndex = methods.NewMethod(TraceLog.GetFullName(data), moduleFileIndex, data.MethodToken);
                        if (data.IsJitted)
                        {
                            ilMap = UnloadILMapForMethod(methodIndex, data);
                        }
                    }
                    // Set the info
                    info.SetMethodIndex(this, methodIndex);
                    if (ilMap != ILMapIndex.Invalid)
                    {
                        info.SetILMapIndex(this, ilMap);
                    }
                    info.SetOptimizationTier(data.OptimizationTier);
                }
            });
        }

        /// <summary>
        /// Adds a JScript method
        /// </summary>
        internal void AddMethod(MethodLoadUnloadJSTraceData data, Dictionary<JavaScriptSourceKey, string> sourceById)
        {
            MethodIndex methodIndex = Microsoft.Diagnostics.Tracing.Etlx.MethodIndex.Invalid;
            TraceProcess process = log.Processes.GetOrCreateProcess(data.ProcessID, data.TimeStampQPC);
            ForAllUnresolvedCodeAddressesInRange(process, data.MethodStartAddress, (int)data.MethodSize, true, delegate (ref CodeAddressInfo info)
                {
                    // If we already resolved, that means that the address was reused, so only add something if it does not already have
                    // information associated with it.
                    if (info.GetMethodIndex(this) == Microsoft.Diagnostics.Tracing.Etlx.MethodIndex.Invalid)
                    {
                        // Lazily create the method since many methods never have code samples in them.
                        if (methodIndex == Microsoft.Diagnostics.Tracing.Etlx.MethodIndex.Invalid)
                        {
                            methodIndex = MakeJavaScriptMethod(data, sourceById);
                        }
                        // Set the info
                        info.SetMethodIndex(this, methodIndex);
                    }
                });
        }

        internal MethodIndex MakeJavaScriptMethod(MethodLoadUnloadJSTraceData data, Dictionary<JavaScriptSourceKey, string> sourceById)
        {
            string sourceName = null;
            /* TODO FIX NOW decide what to do here */
            if (sourceById.TryGetValue(new JavaScriptSourceKey(data.SourceID, data.ScriptContextID), out sourceName))
            {
                var lastSlashIdx = sourceName.LastIndexOf('/');
                if (0 < lastSlashIdx)
                {
                    sourceName = sourceName.Substring(lastSlashIdx + 1);
                }
            }
            if (sourceName == null)
            {
                sourceName = "JAVASCRIPT";
            }

            var methodName = data.MethodName;
            if (data.Line != 0)
            {
                methodName = methodName + " Line: " + data.Line.ToString();
            }

            var moduleFile = log.ModuleFiles.GetOrCreateModuleFile(sourceName, 0);
            return methods.NewMethod(methodName, moduleFile.ModuleFileIndex, data.MethodID);
        }

        internal delegate void ForAllCodeAddrAction(ref CodeAddressInfo codeAddrInfo);
        /// <summary>
        /// Allows you to get a callback for each code address that is in the range from start to
        /// start+length within the process 'process'.   If 'considerResolved' is true' then the address range
        /// is considered resolved and future calls to this routine will not find the addresses (since they are resolved).
        /// </summary>
        internal void ForAllUnresolvedCodeAddressesInRange(TraceProcess process, Address start, int length, bool considerResolved, ForAllCodeAddrAction body)
        {
            if (process.codeAddressesInProcess == null)
            {
                return;
            }

            Debug.Assert(process.unresolvedCodeAddresses.Count <= process.codeAddressesInProcess.Count);
            Debug.Assert(process.ProcessID == 0 || process.unresolvedCodeAddresses.Count == process.codeAddressesInProcess.Count);

            // Trace.WriteLine(string.Format("Looking up code addresses from {0:x} len {1:x} in process {2} ({3})", start, length, process.Name, process.ProcessID));
            if (!process.unresolvedCodeAddressesIsSorted)
            {
                // Trace.WriteLine(string.Format("Sorting {0} unresolved code addresses for process {1} ({2})", process.unresolvedCodeAddresses.Count, process.Name, process.ProcessID));
                process.unresolvedCodeAddresses.Sort(
                    (CodeAddressIndex x, CodeAddressIndex y) => codeAddresses[(int)x].Address.CompareTo(codeAddresses[(int)y].Address));
                process.unresolvedCodeAddressesIsSorted = true;
            }

            // Since we know we are sorted, we do a binary search to find the first code address.
            int startIdx;
            if (!process.unresolvedCodeAddresses.BinarySearch(start, out startIdx, (addr, codeIdx) => addr.CompareTo(codeAddresses[(int)codeIdx].Address)))
            {
                startIdx++;
            }

            bool removeAddressAfterCallback = (process.ProcessID != 0);      // We remove entries unless it is the kernel (process 0) after calling back

            // since the DLL will be unloaded in that process. Kernel DLLS stay loaded.
            // Call back for ever code address >= start than that, and then remove any code addresses we called back on.
            Address end = start + (ulong)length;
            int curIdx = startIdx;
            while (curIdx < process.unresolvedCodeAddresses.Count)
            {
                CodeAddressIndex codeAddrIdx = process.unresolvedCodeAddresses[curIdx];
                Address codeAddr = codeAddresses[(int)codeAddrIdx].Address;
                if (end <= codeAddr)
                {
                    break;
                }

                body(ref codeAddresses.UnderlyingArray[(int)codeAddrIdx]);
                if (considerResolved && removeAddressAfterCallback)
                {
                    process.codeAddressesInProcess.Remove(codeAddr);
                }

                curIdx++;
            }

            if (considerResolved && curIdx != startIdx)
            {
                // OK we called back on the code addresses in the range.   Remove what we just iterated over in bulk.
                // Trace.WriteLine(string.Format("Removing {0} unresolved code addresses out of {1} because of range {2:x} len {3:x} from process {4} ({5})",
                //     curIdx - startIdx, process.unresolvedCodeAddresses.Count, start, length, process.Name, process.ProcessID));
                process.unresolvedCodeAddresses.RemoveRange(startIdx, curIdx - startIdx);
                Debug.Assert(process.unresolvedCodeAddresses.Count <= process.codeAddressesInProcess.Count);
                Debug.Assert(process.ProcessID == 0 || process.unresolvedCodeAddresses.Count == process.codeAddressesInProcess.Count);
            }
        }

        /// <summary>
        /// Gets the symbolic information entry for 'address' which can be any address.  If it falls in the
        /// range of a symbol, then that symbolic information is returned.  Regardless of whether symbolic
        /// information is found, however, an entry is created for it, so every unique address has an entry
        /// in this table.
        /// </summary>
        internal CodeAddressIndex GetOrCreateCodeAddressIndex(TraceProcess process, Address address)
        {
            // See if it is a kernel address, if so use process 0 instead of the current process
            process = ProcessForAddress(process, address);

            CodeAddressIndex ret;
            if (process.codeAddressesInProcess == null)
            {
                process.codeAddressesInProcess = new Dictionary<Address, CodeAddressIndex>();
            }

            if (!process.codeAddressesInProcess.TryGetValue(address, out ret))
            {
                ret = (CodeAddressIndex)codeAddresses.Count;
                codeAddresses.Add(new CodeAddressInfo(address, process.ProcessIndex));
                process.codeAddressesInProcess[address] = ret;

                // Trace.WriteLine(string.Format("Adding new code address for address {0:x} for process {1} ({2})",  address, process.Name, process.ProcessID));
                process.unresolvedCodeAddressesIsSorted = false;
                process.unresolvedCodeAddresses.Add(ret);
                Debug.Assert(process.ProcessID == 0 || process.unresolvedCodeAddresses.Count == process.codeAddressesInProcess.Count);
            }

            codeAddresses.UnderlyingArray[(int)ret].UpdateStats();
            return ret;
        }

        /// <summary>
        /// All processes might have kernel addresses in them, this returns the kernel process (process ID == 0) if 'address' is a kernel address.
        /// </summary>
        private TraceProcess ProcessForAddress(TraceProcess process, Address address)
        {
            if (TraceLog.IsKernelAddress(address, log.pointerSize))
            {
                return log.Processes.GetOrCreateProcess(0, log.sessionStartTimeQPC);
            }

            return process;
        }

        // TODO do we need this?
        /// <summary>
        /// Sort from lowest address to highest address.
        /// </summary>
        private IEnumerable<CodeAddressIndex> GetSortedCodeAddressIndexes()
        {
            List<CodeAddressIndex> list = new List<CodeAddressIndex>(GetAllIndexes);
            list.Sort(delegate (CodeAddressIndex x, CodeAddressIndex y)
            {
                ulong addrX = (ulong)Address(x);
                ulong addrY = (ulong)Address(y);
                if (addrX > addrY)
                {
                    return 1;
                }

                if (addrX < addrY)
                {
                    return -1;
                }

                return 0;
            });
            return list;
        }

        /// <summary>
        /// Do symbol resolution for all addresses in the log file.
        /// </summary>
        internal void LookupSymbols(TraceLogOptions options)
        {
            SymbolReader reader = null;
            int totalAddressCount = 0;
            int noModuleAddressCount = 0;
            IEnumerator<CodeAddressIndex> codeAddressIndexCursor = GetSortedCodeAddressIndexes().GetEnumerator();
            bool notDone = codeAddressIndexCursor.MoveNext();
            while (notDone)
            {
                TraceModuleFile moduleFile = moduleFiles[ModuleFileIndex(codeAddressIndexCursor.Current)];
                if (moduleFile != null)
                {
                    if (options.ShouldResolveSymbols != null && options.ShouldResolveSymbols(moduleFile.FilePath))
                    {
                        if (reader == null)
                        {
                            var symPath = SymbolPath.CleanSymbolPath();
                            if (options.LocalSymbolsOnly)
                            {
                                symPath = symPath.LocalOnly();
                            }

                            var path = symPath.ToString();
                            options.ConversionLog.WriteLine("_NT_SYMBOL_PATH={0}", path);
                            reader = new SymbolReader(options.ConversionLog, path);
                        }
                        int moduleAddressCount = 0;
                        try
                        {
                            notDone = true;
                            LookupSymbolsForModule(reader, moduleFile, codeAddressIndexCursor, false, out moduleAddressCount);
                        }
                        catch (OutOfMemoryException)
                        {
                            options.ConversionLog.WriteLine("Hit Symbol reader out of memory issue.   Skipping that module.");
                        }
                        catch (Exception e)
                        {
                            // TODO too strong.
                            options.ConversionLog.WriteLine("An exception occurred during symbol lookup.  Continuing...");
                            options.ConversionLog.WriteLine("Exception: " + e.Message);
                        }
                        totalAddressCount += moduleAddressCount;
                    }

                    // Skip the rest of the addresses for that module.
                    while ((moduleFiles[ModuleFileIndex(codeAddressIndexCursor.Current)] == moduleFile))
                    {
                        notDone = codeAddressIndexCursor.MoveNext();
                        if (!notDone)
                        {
                            break;
                        }

                        totalAddressCount++;
                    }
                }
                else
                {
                    // TraceLog.DebugWarn("Could not find a module for address " + ("0x" + Address(codeAddressIndexCursor.Current).ToString("x")).PadLeft(10));
                    notDone = codeAddressIndexCursor.MoveNext();
                    noModuleAddressCount++;
                    totalAddressCount++;
                }
            }

            if (reader != null)
            {
                reader.Dispose();
            }

            double noModulePercent = 0;
            if (totalAddressCount > 0)
            {
                noModulePercent = noModuleAddressCount * 100.0 / totalAddressCount;
            }

            options.ConversionLog.WriteLine("A total of " + totalAddressCount + " symbolic addresses were looked up.");
            options.ConversionLog.WriteLine("Addresses outside any module: " + noModuleAddressCount + " out of " + totalAddressCount + " (" + noModulePercent.ToString("f1") + "%)");
            options.ConversionLog.WriteLine("Done with symbolic lookup.");
        }

        // TODO number of args is getting messy.
        private void LookupSymbolsForModule(SymbolReader reader, TraceModuleFile moduleFile,
            IEnumerator<CodeAddressIndex> codeAddressIndexCursor, bool enumerateAll, out int totalAddressCount)
        {
            totalAddressCount = 0;
            int existingSymbols = 0;
            int distinctSymbols = 0;
            int unmatchedSymbols = 0;
            int repeats = 0;

            // We can get the same name for different addresses, which makes us for distinct methods
            // which in turn cause the treeview to have multiple children with the same name.   This
            // is confusing, so we intern the symbols, insuring that code address with the same name
            // always use the same method.   This dictionary does that.
            var methodIntern = new Dictionary<string, MethodIndex>();

            reader.m_log.WriteLine("[Loading symbols for " + moduleFile.FilePath + "]");

            NativeSymbolModule moduleReader = OpenPdbForModuleFile(reader, moduleFile) as NativeSymbolModule;
            if (moduleReader == null)
            {
                reader.m_log.WriteLine("Could not find PDB file.");
                return;
            }

            reader.m_log.WriteLine("Loaded, resolving symbols");


            string currentMethodName = "";
            MethodIndex currentMethodIndex = Microsoft.Diagnostics.Tracing.Etlx.MethodIndex.Invalid;
            Address currentMethodEnd = 0;
            Address endModule = moduleFile.ImageEnd;
            for (; ; )
            {
                // options.ConversionLog.WriteLine("Code address = " + Address(codeAddressIndexCursor.Current).ToString("x"));
                totalAddressCount++;
                Address address = Address(codeAddressIndexCursor.Current);
                if (!enumerateAll && address >= endModule)
                {
                    break;
                }

                MethodIndex methodIndex = MethodIndex(codeAddressIndexCursor.Current);
                if (methodIndex == Microsoft.Diagnostics.Tracing.Etlx.MethodIndex.Invalid)
                {
                    if (address < currentMethodEnd)
                    {
                        repeats++;
                        // options.ConversionLog.WriteLine("Repeat of " + currentMethodName + " at " + address.ToString("x"));
                    }
                    else
                    {
                        uint symbolStart = 0;
                        var newMethodName = moduleReader.FindNameForRva((uint)(address - moduleFile.ImageBase), ref symbolStart);
                        if (newMethodName.Length > 0)
                        {
                            // TODO FIX NOW
                            // Debug.WriteLine(string.Format("Info: address  0x{0:x} in sym {1}", address, newMethodName));
                            // TODO FIX NOW
                            currentMethodEnd = address + 1;     // Look up each unique address.

                            // TODO FIX NOW remove
                            // newMethodName = newMethodName +  " 0X" + address.ToString("x");

                            // If we get the exact same method name, then again we have a repeat
                            // In theory this should not happen, but in it seems to happen in
                            // practice.
                            if (newMethodName == currentMethodName)
                            {
                                repeats++;
                            }
                            else
                            {
                                currentMethodName = newMethodName;
                                if (!methodIntern.TryGetValue(newMethodName, out currentMethodIndex))
                                {
                                    currentMethodIndex = methods.NewMethod(newMethodName, moduleFile.ModuleFileIndex, -(int)symbolStart);
                                    methodIntern[newMethodName] = currentMethodIndex;
                                    distinctSymbols++;
                                }
                            }
                        }
                        else
                        {
                            unmatchedSymbols++;
                            currentMethodName = "";
                            currentMethodIndex = Microsoft.Diagnostics.Tracing.Etlx.MethodIndex.Invalid;
                        }
                    }

                    if (currentMethodIndex != Microsoft.Diagnostics.Tracing.Etlx.MethodIndex.Invalid)
                    {
                        CodeAddressInfo codeAddressInfo = codeAddresses[(int)codeAddressIndexCursor.Current];
                        Debug.Assert(codeAddressInfo.GetModuleFileIndex(this) == moduleFile.ModuleFileIndex);
                        codeAddressInfo.SetMethodIndex(this, currentMethodIndex);
                        Debug.Assert(moduleFile.ModuleFileIndex != Microsoft.Diagnostics.Tracing.Etlx.ModuleFileIndex.Invalid);
                        codeAddresses[(int)codeAddressIndexCursor.Current] = codeAddressInfo;
                    }
                }
                else
                {
                    // options.ConversionLog.WriteLine("Found existing method " + Methods[methodIndex].FullMethodName);
                    existingSymbols++;
                }

                if (!codeAddressIndexCursor.MoveNext())
                {
                    break;
                }
            }
            reader.m_log.WriteLine("    Addresses to look up       " + totalAddressCount);
            if (existingSymbols != 0)
            {
                reader.m_log.WriteLine("        Existing Symbols       " + existingSymbols);
            }

            reader.m_log.WriteLine("        Found Symbols          " + (distinctSymbols + repeats));
            reader.m_log.WriteLine("        Distinct Found Symbols " + distinctSymbols);
            reader.m_log.WriteLine("        Unmatched Symbols " + (totalAddressCount - (distinctSymbols + repeats)));
        }

        /// <summary>
        /// Look up the SymbolModule (open PDB) for a given moduleFile.   Will generate NGEN pdbs as needed.
        /// </summary>
        private unsafe ManagedSymbolModule OpenPdbForModuleFile(SymbolReader symReader, TraceModuleFile moduleFile)
        {
            string pdbFileName = null;
            // If we have a signature, use it
            if (moduleFile.PdbSignature != Guid.Empty)
            {
                pdbFileName = symReader.FindSymbolFilePath(moduleFile.PdbName, moduleFile.PdbSignature, moduleFile.PdbAge, moduleFile.FilePath, moduleFile.ProductVersion, true);
            }
            else
            {
                symReader.m_log.WriteLine("No PDB signature for {0} in trace.", moduleFile.FilePath);
            }

            if (pdbFileName == null)
            {
                // Confirm that the path from the trace points at a file that is the same (checksums match).
                // It will log messages if it does not match.
                if (TraceModuleUnchanged(moduleFile, symReader.m_log))
                {
                    pdbFileName = symReader.FindSymbolFilePathForModule(moduleFile.FilePath);
                }
            }
            if(pdbFileName == null)
            {
                // Check to see if the file is inside of an existing Windows container.
                // Create a new instance of WindowsDeviceToVolumeMap to avoid situations where the mappings have changed but we haven't noticed.
                string volumePath = new WindowsDeviceToVolumeMap().ConvertDevicePathToVolumePath(moduleFile.FilePath);
                symReader.m_log.WriteLine("Attempting to convert {0} to a volume-based path in case the file inside of a container.", moduleFile.FilePath);
                if (!moduleFile.FilePath.Equals(volumePath))
                {
                    symReader.m_log.WriteLine("Successfully converted to {0}.", volumePath);

                    // Confirm that the path from the trace points at a file that is the same (checksums match).
                    // It will log messages if it does not match.
                    if (TraceModuleUnchanged(moduleFile, symReader.m_log, volumePath))
                    {
                        pdbFileName = symReader.FindSymbolFilePathForModule(volumePath);
                    }
                }
                else
                {
                    symReader.m_log.WriteLine("Unable to convert {0} to a volume-based path.", moduleFile.FilePath);
                }

            }
            if (pdbFileName == null)
            {
                if (UnsafePDBMatching)
                {
                    var pdbSimpleName = Path.GetFileNameWithoutExtension(moduleFile.FilePath) + ".pdb";
                    symReader.m_log.WriteLine("The /UnsafePdbMatch specified.  Looking for {0} using only the file name to validate a match.", pdbSimpleName);
                    pdbFileName = symReader.FindSymbolFilePath(pdbSimpleName, Guid.Empty, 0);
                }
            }

            if (pdbFileName == null)
            {
                // We are about to fail.   output helpful warnings.
                if (moduleFile.PdbSignature == Guid.Empty)
                {
                    if (log.PointerSize == 8 && moduleFile.FilePath.IndexOf(@"\windows\System32", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        symReader.m_log.WriteLine("WARNING: could not find PDB signature of a 64 bit OS DLL.  Did you collect with a 32 bit version of XPERF?\r\n");
                    }

                    symReader.m_log.WriteLine("WARNING: The log file does not contain exact PDB signature information for {0} and the file at this path is not the file used in the trace.", moduleFile.FilePath);
                    symReader.m_log.WriteLine("PDB files cannot be unambiguously matched to the EXE.");
                    symReader.m_log.WriteLine("Did you merge the ETL file before transferring it off the collection machine?  If not, doing the merge will fix this.");
                    if (!UnsafePDBMatching)
                    {
                        symReader.m_log.WriteLine("The /UnsafePdbMatch option will force an ambiguous match, but this is not recommended.");
                    }
                }
                symReader.m_log.WriteLine("Failed to find PDB for {0}", moduleFile.FilePath);
                return null;
            }

            // At this point pdbFileName is set,we are going to succeed.
            ManagedSymbolModule symbolReaderModule = symReader.OpenSymbolFile(pdbFileName);
            if (symbolReaderModule != null)
            {
                if (!UnsafePDBMatching && moduleFile.PdbSignature != Guid.Empty && symbolReaderModule.PdbGuid != moduleFile.PdbSignature)
                {
                    symReader.m_log.WriteLine("ERROR: the PDB we opened does not match the PDB desired.  PDB GUID = " + symbolReaderModule.PdbGuid + " DESIRED GUID = " + moduleFile.PdbSignature);
                    return null;
                }
                symbolReaderModule.ExePath = moduleFile.FilePath;

                // Currently NGEN pdbs do not have source server information, but the managed version does.
                // Thus we remember the lookup info for the managed PDB too so we have it if we need source server info
                var managed = moduleFile.ManagedModule;
                if (managed != null)
                {
                    var nativePdb = symbolReaderModule as NativeSymbolModule;
                    if (nativePdb != null)
                    {
                        nativePdb.LogManagedInfo(managed.PdbName, managed.PdbSignature, managed.pdbAge);
                    }
                }
            }

            symReader.m_log.WriteLine("Opened Pdb file {0}", pdbFileName);
            return symbolReaderModule;
        }

        /// <summary>
        /// Returns true if 'moduleFile' seems to be unchanged from the time the information about it
        /// was generated.  Logs messages to 'log' if it fails.
        /// Specify overrideModuleFilePath if the path needs to be converted to a different format in order to be accessed (e.g. from device path to volume path).
        /// </summary>
        private bool TraceModuleUnchanged(TraceModuleFile moduleFile, TextWriter log, string overrideModuleFilePath = null)
        {
            string moduleFilePath = SymbolReader.BypassSystem32FileRedirection(overrideModuleFilePath != null ? overrideModuleFilePath : moduleFile.FilePath);
            if (!File.Exists(moduleFilePath))
            {
                log.WriteLine("The file {0} does not exist on the local machine", moduleFilePath);
                return false;
            }

            using (var file = new PEFile.PEFile(moduleFilePath))
            {
                if (file.Header.CheckSum != (uint)moduleFile.ImageChecksum)
                {
                    log.WriteLine("The local file {0} has a mismatched checksum found {1} != expected {2}", moduleFilePath, file.Header.CheckSum, moduleFile.ImageChecksum);
                    return false;
                }
                if (moduleFile.ImageId != 0 && file.Header.TimeDateStampSec != moduleFile.ImageId)
                {
                    log.WriteLine("The local file {0} has a mismatched Timestamp value found {1} != expected {2}", moduleFilePath, file.Header.TimeDateStampSec, moduleFile.ImageId);
                    return false;
                }
                if (file.Header.SizeOfImage != (uint)moduleFile.ImageSize)
                {
                    log.WriteLine("The local file {0} has a mismatched size found {1} != expected {2}", moduleFilePath, file.Header.SizeOfImage, moduleFile.ImageSize);
                    return false;
                }
            }
            return true;
        }

        void IFastSerializable.ToStream(Serializer serializer)
        {
            lazyCodeAddresses.Write(serializer, delegate
            {
                serializer.Write(log);
                serializer.Write(moduleFiles);
                serializer.Write(methods);

                serializer.WriteTagged(CodeAddressInfoSerializationVersion);
                serializer.Write(codeAddresses.Count);
                serializer.Log("<WriteCollection name=\"codeAddresses\" count=\"" + codeAddresses.Count + "\">\r\n");
                for (int i = 0; i < codeAddresses.Count; i++)
                {
                    serializer.WriteAddress(codeAddresses[i].Address);
                    serializer.Write((int)codeAddresses[i].moduleFileIndex);
                    serializer.Write((int)codeAddresses[i].methodOrProcessOrIlMapIndex);
                    serializer.Write(codeAddresses[i].InclusiveCount);

                    // 'CodeAddressInfoSerializationVersion' >= 1
                    serializer.Write((byte)codeAddresses[i].optimizationTier);
                }
                serializer.Write(totalCodeAddresses);
                serializer.Log("</WriteCollection>\r\n");

                serializer.Write(ILToNativeMaps.Count);
                for (int i = 0; i < ILToNativeMaps.Count; i++)
                {
                    serializer.Write(ILToNativeMaps[i]);
                }
            });
        }

        void IFastSerializable.FromStream(Deserializer deserializer)
        {
            lazyCodeAddresses.Read(deserializer, delegate
            {
                deserializer.Read(out log);
                deserializer.Read(out moduleFiles);
                deserializer.Read(out methods);

                int storedCodeAddressInfoSerializationVersion = 0;
                deserializer.TryReadTagged(ref storedCodeAddressInfoSerializationVersion);
                int count = deserializer.ReadInt();
                deserializer.Log("<Marker name=\"codeAddresses\" count=\"" + count + "\"/>");
                CodeAddressInfo codeAddressInfo = new CodeAddressInfo();
                codeAddresses = new GrowableArray<CodeAddressInfo>(count + 1);
                for (int i = 0; i < count; i++)
                {
                    deserializer.ReadAddress(out codeAddressInfo.Address);
                    codeAddressInfo.moduleFileIndex = (ModuleFileIndex)deserializer.ReadInt();
                    codeAddressInfo.methodOrProcessOrIlMapIndex = deserializer.ReadInt();
                    deserializer.Read(out codeAddressInfo.InclusiveCount);

                    if (storedCodeAddressInfoSerializationVersion >= 1)
                    {
                        codeAddressInfo.optimizationTier = (OptimizationTier)deserializer.ReadByte();
                    }

                    codeAddresses.Add(codeAddressInfo);
                }
                deserializer.Read(out totalCodeAddresses);

                ILToNativeMaps.Count = deserializer.ReadInt();
                for (int i = 0; i < ILToNativeMaps.Count; i++)
                {
                    deserializer.Read(out ILToNativeMaps.UnderlyingArray[i]);
                }
            });
            lazyCodeAddresses.FinishRead();        // TODO REMOVE
        }

        private const int CodeAddressInfoSerializationVersion = 1;

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            throw new NotImplementedException(); // GetEnumerator
        }

        /// <summary>
        /// A CodeAddressInfo is the actual data stored in the ETLX file that represents a
        /// TraceCodeAddress.     It knows its Address in the process and it knows the
        /// TraceModuleFile (which knows its base address), so it also knows its relative
        /// address in the TraceModuleFile (which is what is needed to look up the value
        /// in the PDB.
        ///
        /// Note that by the time that the CodeAddressInfo is persisted in the ETLX file
        /// it no longer knows the process it originated from (thus separate processes
        /// with the same address and same DLL file loaded at the same address can share
        /// the same CodeAddressInfo.  This is actually reasonably common, since OS tend
        /// to load at their preferred base address.
        ///
        /// We also have to handle the managed case, in which case the CodeAddressInfo may
        /// also know about the TraceMethod or the ILMapIndex (which remembers both the
        /// method and the line numbers for managed code.
        ///
        /// However when the CodeAddressInfo is first created, we don't know the TraceModuleFile
        /// so we also need to remember the Process
        ///
        /// </summary>
        internal struct CodeAddressInfo
        {
            internal CodeAddressInfo(Address address, ProcessIndex processIndex)
            {
                Address = address;
                moduleFileIndex = Microsoft.Diagnostics.Tracing.Etlx.ModuleFileIndex.Invalid;
                methodOrProcessOrIlMapIndex = -2 - ((int)processIndex);      // Encode process index to make it unambiguous with a method index.
                InclusiveCount = 0;
                optimizationTier = Parsers.Clr.OptimizationTier.Unknown;
                Debug.Assert(GetProcessIndex(null) == processIndex);
            }

            internal ILMapIndex GetILMapIndex(TraceCodeAddresses codeAddresses)
            {
                if (methodOrProcessOrIlMapIndex < 0 || (methodOrProcessOrIlMapIndex & 1) == 0)
                {
                    return ILMapIndex.Invalid;
                }

                return (ILMapIndex)(methodOrProcessOrIlMapIndex >> 1);
            }
            internal void SetILMapIndex(TraceCodeAddresses codeAddresses, ILMapIndex value)
            {
                Debug.Assert(value != ILMapIndex.Invalid);

                // We may be overwriting other values, insure that they actually don't change.
                Debug.Assert(GetMethodIndex(codeAddresses) == Microsoft.Diagnostics.Tracing.Etlx.MethodIndex.Invalid ||
                    GetMethodIndex(codeAddresses) == codeAddresses.ILToNativeMaps[(int)value].MethodIndex);
                Debug.Assert(methodOrProcessOrIlMapIndex >= 0 ||
                    GetProcessIndex(codeAddresses) == codeAddresses.ILToNativeMaps[(int)value].ProcessIndex);

                methodOrProcessOrIlMapIndex = ((int)value << 1) + 1;

                Debug.Assert(GetILMapIndex(codeAddresses) == value);
            }
            /// <summary>
            /// This is only valid until MethodIndex or ModuleFileIndex is set.
            /// </summary>
            internal ProcessIndex GetProcessIndex(TraceCodeAddresses codeAddresses)
            {
                if (methodOrProcessOrIlMapIndex < -1)
                {
                    return (Microsoft.Diagnostics.Tracing.Etlx.ProcessIndex)(-(methodOrProcessOrIlMapIndex + 2));
                }

                var ilMapIdx = GetILMapIndex(codeAddresses);
                if (ilMapIdx != ILMapIndex.Invalid)
                {
                    return codeAddresses.ILToNativeMaps[(int)ilMapIdx].ProcessIndex;
                }
                // Can't assert because we get here if we have NGEN rundown on an NGEN image
                // Debug.Assert(false, "Asking for Process after Method has been set is illegal (to save space)");
                return Microsoft.Diagnostics.Tracing.Etlx.ProcessIndex.Invalid;
            }
            /// <summary>
            /// Only for managed code.
            /// </summary>
            internal MethodIndex GetMethodIndex(TraceCodeAddresses codeAddresses)
            {
                if (methodOrProcessOrIlMapIndex < 0)
                {
                    return TryLookupMethodOrModule(codeAddresses);
                }

                if ((methodOrProcessOrIlMapIndex & 1) == 0)
                {
                    return (Microsoft.Diagnostics.Tracing.Etlx.MethodIndex)(methodOrProcessOrIlMapIndex >> 1);
                }

                return codeAddresses.ILToNativeMaps[(int)GetILMapIndex(codeAddresses)].MethodIndex;
            }

            private MethodIndex TryLookupMethodOrModule(TraceCodeAddresses codeAddresses)
            {
                if (!(codeAddresses.log.IsRealTime && methodOrProcessOrIlMapIndex < -1))
                {
                    return Microsoft.Diagnostics.Tracing.Etlx.MethodIndex.Invalid;
                }

                TraceProcess process = codeAddresses.log.Processes[GetProcessIndex(codeAddresses)];
                int index;
                TraceLoadedModule loadedModule = process.LoadedModules.FindModuleAndIndexContainingAddress(Address, long.MaxValue - 1, out index);
                if (loadedModule != null)
                {
                    SetModuleFileIndex(loadedModule.ModuleFile);
                    methodOrProcessOrIlMapIndex = -1;           //  set it as the invalid method, destroys memory of process we are in.
                    return Microsoft.Diagnostics.Tracing.Etlx.MethodIndex.Invalid;
                }
                else
                {
                    MethodIndex methodIndex = process.FindJITTEDMethodFromAddress(Address);
                    if (methodIndex != Microsoft.Diagnostics.Tracing.Etlx.MethodIndex.Invalid)
                    {
                        SetMethodIndex(codeAddresses, methodIndex);
                    }
                    else
                    {
                        methodOrProcessOrIlMapIndex = -1;           //  set it as the invalid method, destroys memory of process we are in.
                    }

                    return methodIndex;
                }
            }
            internal void SetMethodIndex(TraceCodeAddresses codeAddresses, MethodIndex value)
            {
                Debug.Assert(value != Microsoft.Diagnostics.Tracing.Etlx.MethodIndex.Invalid);

                if (GetILMapIndex(codeAddresses) == TraceCodeAddresses.ILMapIndex.Invalid)
                {
                    methodOrProcessOrIlMapIndex = (int)(value) << 1;
                }
                else
                {
                    Debug.Assert(GetMethodIndex(codeAddresses) == value, "Setting method index when ILMap already set (ignored)");
                }

                Debug.Assert(GetMethodIndex(codeAddresses) == value);
            }
            /// <summary>
            /// Only for unmanaged code.   TODO, this can be folded into methodOrProcessIlMap index and save a DWORD.
            /// since if the method or IlMap is present then you can get the ModuelFile index from there.
            /// </summary>
            internal ModuleFileIndex GetModuleFileIndex(TraceCodeAddresses codeAddresses)
            {
                if (moduleFileIndex == Etlx.ModuleFileIndex.Invalid)
                {
                    TryLookupMethodOrModule(codeAddresses);
                }

                return moduleFileIndex;
            }

            internal void SetModuleFileIndex(TraceModuleFile moduleFile)
            {
                if (moduleFileIndex != Etlx.ModuleFileIndex.Invalid)
                {
                    return;
                }

                moduleFileIndex = moduleFile.ModuleFileIndex;

                if (optimizationTier == Parsers.Clr.OptimizationTier.Unknown &&
                    moduleFile.IsReadyToRun &&
                    moduleFile.ImageBase <= Address &&
                    Address < moduleFile.ImageEnd)
                {
                    optimizationTier = Parsers.Clr.OptimizationTier.ReadyToRun;
                }
            }

            internal void SetOptimizationTier(OptimizationTier value)
            {
                if (optimizationTier == Parsers.Clr.OptimizationTier.Unknown)
                {
                    optimizationTier = value;
                }
            }

            // keep track of how popular each code stack is.
            internal void UpdateStats()
            {
                InclusiveCount++;
            }

            internal Address Address;
            /// <summary>
            /// This is a count of how many times this code address appears in any stack in the trace.
            /// It is a measure of what popular the code address is (whether we should look up its symbols).
            /// </summary>
            internal int InclusiveCount;

            // To save space, we reuse this slot during data collection
            // If x < -1 it is ProcessIndex, if > -1 and odd, it is an ILMapIndex if > -1 and even it is a MethodIndex.
            internal int methodOrProcessOrIlMapIndex;

            internal ModuleFileIndex moduleFileIndex;

            internal OptimizationTier optimizationTier;
        }

        private ILMapIndex UnloadILMapForMethod(MethodIndex methodIndex, MethodLoadUnloadVerboseTraceData data)
        {
            var process = log.Processes.GetProcess(data.ProcessID, data.TimeStampQPC);
            if (process == null)
            {
                return ILMapIndex.Invalid;
            }

            ILMapIndex ilMapIdx;
            var ilMap = FindAndRemove(data.MethodID, process.ProcessIndex, out ilMapIdx);
            if (ilMap == null)
            {
                return ilMapIdx;
            }

            Debug.Assert(ilMap.MethodStart == 0 || ilMap.MethodStart == data.MethodStartAddress);
            Debug.Assert(ilMap.MethodLength == 0 || ilMap.MethodLength == data.MethodSize);

            ilMap.MethodStart = data.MethodStartAddress;
            ilMap.MethodLength = data.MethodSize;
            Debug.Assert(ilMap.MethodIndex == 0 || ilMap.MethodIndex == methodIndex);
            ilMap.MethodIndex = methodIndex;
            return ilMapIdx;
        }

        /// <summary>
        /// Find the ILToNativeMap for 'methodId' in process associated with 'processIndex'
        /// and then remove it from the table (this is what you want to do when the method is unloaded)
        /// </summary>
        private ILToNativeMap FindAndRemove(long methodID, ProcessIndex processIndex, out ILMapIndex mapIdxRet)
        {
            ILMapIndex mapIdx;
            if (methodIDToILToNativeMap != null && methodIDToILToNativeMap.TryGetValue(methodID, out mapIdx))
            {
                ILToNativeMap prev = null;
                while (mapIdx != ILMapIndex.Invalid)
                {
                    ILToNativeMap ret = ILToNativeMaps[(int)mapIdx];
                    if (ret.ProcessIndex == processIndex)
                    {
                        if (prev != null)
                        {
                            prev.Next = ret.Next;
                        }
                        else if (ret.Next == ILMapIndex.Invalid)
                        {
                            methodIDToILToNativeMap.Remove(methodID);
                        }
                        else
                        {
                            methodIDToILToNativeMap[methodID] = ret.Next;
                        }

                        mapIdxRet = mapIdx;
                        return ret;
                    }
                    mapIdx = ret.Next;
                }
            }
            mapIdxRet = ILMapIndex.Invalid;
            return null;
        }

        internal void AddILMapping(MethodILToNativeMapTraceData data)
        {
            var ilMap = new ILToNativeMap();
            ilMap.Next = ILMapIndex.Invalid;
            var process = log.Processes.GetProcess(data.ProcessID, data.TimeStampQPC);
            if (process == null)
            {
                return;
            }

            ilMap.ProcessIndex = process.ProcessIndex;
            ILToNativeMapTuple tuple;
            for (int i = 0; i < data.CountOfMapEntries; i++)
            {
                // There are special prologue and epilogue offsets, but the best line approximation
                // happens if we simply ignore them, so this is what we do here.
                var ilOffset = data.ILOffset(i);
                if (ilOffset < 0)
                {
                    continue;
                }

                tuple.ILOffset = ilOffset;
                tuple.NativeOffset = data.NativeOffset(i);
                ilMap.Map.Add(tuple);
            }

            // They may not come back sorted, but we want to binary search so sort them by native offset (ascending)
            ilMap.Map.Sort((x, y) => x.NativeOffset - y.NativeOffset);

            ILMapIndex mapIdx = (ILMapIndex)ILToNativeMaps.Count;
            ILToNativeMaps.Add(ilMap);
            if (methodIDToILToNativeMap == null)
            {
                methodIDToILToNativeMap = new Dictionary<long, ILMapIndex>(101);
            }

            ILMapIndex prevIndex;
            if (methodIDToILToNativeMap.TryGetValue(data.MethodID, out prevIndex))
            {
                ilMap.Next = prevIndex;
            }

            methodIDToILToNativeMap[data.MethodID] = mapIdx;
        }

        internal enum ILMapIndex { Invalid = -1 };
        internal struct ILToNativeMapTuple
        {
            public int ILOffset;
            public int NativeOffset;

            internal void Deserialize(Deserializer deserializer)
            {
                deserializer.Read(out ILOffset);
                deserializer.Read(out NativeOffset);
            }
            internal void Serialize(Serializer serializer)
            {
                serializer.Write(ILOffset);
                serializer.Write(NativeOffset);
            }
        }

        internal class ILToNativeMap : IFastSerializable
        {
            public ILMapIndex Next;             // We keep a link list of maps with the same start address
                                                // (can only be from different processes);
            public ProcessIndex ProcessIndex;   // This is not serialized.
            public MethodIndex MethodIndex;
            public Address MethodStart;
            public int MethodLength;
            internal GrowableArray<ILToNativeMapTuple> Map;

            public int GetILOffsetForNativeAddress(Address nativeAddress)
            {
                int idx;
                if (nativeAddress < MethodStart || MethodStart + (uint)MethodLength < nativeAddress)
                {
                    return -1;
                }

                int nativeOffset = (int)(nativeAddress - MethodStart);
                Map.BinarySearch(nativeOffset, out idx,
                    delegate (int key, ILToNativeMapTuple elem) { return key - elem.NativeOffset; });
                if (idx < 0)
                {
                    return -1;
                }

                // After looking at the empirical results, it does seem that linear interpolation
                // Gives a significantly better approximation of the IL address.
                int retIL = Map[idx].ILOffset;
                int nativeDelta = nativeOffset - Map[idx].NativeOffset;
                int nextIdx = idx + 1;
                if (nextIdx < Map.Count && nativeDelta != 0)
                {
                    int ILDeltaToNext = Map[nextIdx].ILOffset - Map[idx].ILOffset;
                    // If the IL deltas are going down don't interpolate.
                    if (ILDeltaToNext > 0)
                    {
                        int nativeDeltaToNext = Map[nextIdx].NativeOffset - Map[idx].NativeOffset;
                        retIL += (int)(((double)nativeDelta) / nativeDeltaToNext * ILDeltaToNext + .5);
                    }
                    else
                    {
                        return retIL;
                    }
                }
                // For our use in sampling the EIP is the instruction that COMPLETED, so we actually want to
                // attribute the time to the line BEFORE this one if we are exactly on the boundary.
                // TODO This probably does not belong here, but I only want to this if the IL deltas are going up.
                if (retIL > 0)
                {
                    --retIL;
                }

                return retIL;
            }

            void IFastSerializable.ToStream(Serializer serializer)
            {
                serializer.Write((int)MethodIndex);
                serializer.Write((long)MethodStart);
                serializer.Write(MethodLength);

                serializer.Write(Map.Count);
                for (int i = 0; i < Map.Count; i++)
                {
                    Map[i].Serialize(serializer);
                }
            }
            void IFastSerializable.FromStream(Deserializer deserializer)
            {
                MethodIndex = (MethodIndex)deserializer.ReadInt();
                deserializer.ReadAddress(out MethodStart);
                deserializer.Read(out MethodLength);

                Map.Count = deserializer.ReadInt();
                for (int i = 0; i < Map.Count; i++)
                {
                    Map.UnderlyingArray[i].Deserialize(deserializer);
                }
            }
        }

        private GrowableArray<ILToNativeMap> ILToNativeMaps;                    // only Jitted code has these, indexed by ILMapIndex
        private Dictionary<long, ILMapIndex> methodIDToILToNativeMap;

        private TraceCodeAddress[][] codeAddressObjects;  // If we were asked for TraceCodeAddresses (instead of indexes) we cache them, in sparse array
        private string[] names;                         // A cache (one per code address) of the string name of the address
        private int managedMethodRecordCount;           // Remembers how many code addresses are managed methods (currently not serialized)
        internal int totalCodeAddresses;                 // Count of the number of times a code address appears in the log.

        // These are actually serialized.
        private TraceLog log;
        private TraceModuleFiles moduleFiles;
        private TraceMethods methods;
        private DeferedRegion lazyCodeAddresses;
        internal GrowableArray<CodeAddressInfo> codeAddresses;

        #endregion
    }

    /// <summary>
    /// Conceptually a TraceCodeAddress represents a particular point of execution within a particular
    /// line of code in some source code.    As a practical matter, they are represented two ways
    /// depending on whether the code is managed or not.
    /// <para>* For native code (or NGened code), it is represented as a virtual address along with the loaded native
    /// module that includes that address along with its load address.  A code address does NOT
    /// know its process because they can be shared among all processes that load a particular module
    /// at a particular location.   These code addresses will not have methods associated with them
    /// unless symbols information (PDBS) are loaded for the module using the LookupSymbolsForModule.
    /// </para>
    /// <para> * For JIT compiled managed code, the address in a process is eagerly resolved into a method, module
    /// and an IL offset and that is stored in the TraceCodeAddress.
    /// </para>
    ///<para> Sometimes it is impossible to even determine the module associated with a virtual
    ///address in a process.   These are represented as simply the virtual address.
    ///</para>
    ///<para>
    ///Because code addresses are so numerous, consider using CodeAddressIndex instead of TraceCodeAddress
    ///to represent a code address.   Methods on TraceLog.CodeAddresses can access all the information
    ///that would be in a TraceCodeAddress from a CodeAddressIndex without the overhead of creating
    ///a TraceCodeAddress object.
    ///</para>
    /// </summary>
    public sealed class TraceCodeAddress
    {
        /// <summary>
        /// The CodeAddressIndex that uniquely identifies the same code address as this TraceCodeAddress
        /// </summary>
        public CodeAddressIndex CodeAddressIndex { get { return codeAddressIndex; } }
        /// <summary>
        /// The Virtual address of the code address in the process.  (Note that the process is unknown by the code address to allow for sharing)
        /// </summary>
        public Address Address { get { return codeAddresses.Address(codeAddressIndex); } }
        /// <summary>
        /// The full name (Namespace name.class name.method name) of the method associated with this code address.
        /// Returns the empty string if no method is associated with the code address.
        /// </summary>
        public string FullMethodName
        {
            get
            {
                MethodIndex methodIndex = codeAddresses.MethodIndex(codeAddressIndex);
                if (methodIndex == MethodIndex.Invalid)
                {
                    return "";
                }

                return codeAddresses.Methods.FullMethodName(methodIndex);
            }
        }
        /// <summary>
        /// Returns the TraceMethod associated with this code address or null if there is none.
        /// </summary>
        public TraceMethod Method
        {
            get
            {
                MethodIndex methodIndex = codeAddresses.MethodIndex(codeAddressIndex);
                if (methodIndex == MethodIndex.Invalid)
                {
                    return null;
                }
                else
                {
                    return codeAddresses.Methods[methodIndex];
                }
            }
        }
        /// <summary>
        /// If the TraceCodeAddress is associated with managed code, return the IL offset within the method.    If the method
        /// is unmanaged -1 is returned.   To determine the IL offset the PDB for the NGEN image (for NGENed code) or the
        /// correct .NET events (for JIT compiled code) must be present.   If this information is not present -1 is returned.
        /// </summary>
        public int ILOffset { get { return codeAddresses.ILOffset(codeAddressIndex); } }
        /// <summary>
        /// A TraceCodeAddress can contain a method name, but does not contain number information.   To
        /// find line number information you must read the PDB again and fetch it.   This is what
        /// GetSoruceLine does.
        /// <para>
        /// Given a SymbolReader (which knows how to look up PDBs) find a SourceLocation (which represents a
        /// particular line number in a particular source file associated with the current TraceCodeAddress.
        /// Returns null if anything goes wrong (and diagnostic information will be written to the
        /// log file associated with the SymbolReader.
        /// </para>
        /// </summary>
        public SourceLocation GetSourceLine(SymbolReader reader) { return codeAddresses.GetSourceLine(reader, codeAddressIndex); }

        /// <summary>
        /// Returns the TraceModuleFile representing the DLL path associated with this code address (or null if not known)
        /// </summary>
        public TraceModuleFile ModuleFile
        {
            get
            {
                ModuleFileIndex moduleFileIndex = codeAddresses.ModuleFileIndex(codeAddressIndex);
                if (moduleFileIndex == ModuleFileIndex.Invalid)
                {
                    return null;
                }
                else
                {
                    return codeAddresses.ModuleFiles[moduleFileIndex];
                }
            }
        }
        /// <summary>
        /// ModuleName is the name of the file without path or extension.
        /// </summary>
        public string ModuleName
        {
            get
            {
                TraceModuleFile moduleFile = ModuleFile;
                if (moduleFile == null)
                {
                    return "";
                }

                return moduleFile.Name;
            }
        }
        /// <summary>
        /// The full path name of the DLL associated with this code address.  Returns empty string if not known.
        /// </summary>
        public string ModuleFilePath
        {
            get
            {
                TraceModuleFile moduleFile = ModuleFile;
                if (moduleFile == null)
                {
                    return "";
                }

                return moduleFile.FilePath;
            }
        }
        /// <summary>
        /// The CodeAddresses container that this Code Address lives within
        /// </summary>
        public TraceCodeAddresses CodeAddresses { get { return codeAddresses; } }

        /// <summary>
        /// An XML representation for the CodeAddress (for debugging)
        /// </summary>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            return ToString(sb).ToString();
        }
        /// <summary>
        /// Writes an XML representation for the CodeAddress to the stringbuilder sb
        /// </summary>
        public StringBuilder ToString(StringBuilder sb)
        {
            sb.Append("  <CodeAddress Address=\"0x").Append(((long)Address).ToString("x")).Append("\"");
            sb.Append(" CodeAddressIndex=\"").Append(XmlUtilities.XmlEscape(CodeAddressIndex, false)).Append("\"");
            if (FullMethodName.Length > 0)
            {
                sb.Append(" FullMethodName=\"").Append(XmlUtilities.XmlEscape(FullMethodName, false)).Append("\"");
            }

            if (ModuleName.Length != 0)
            {
                sb.Append(" ModuleName=\"").Append(XmlUtilities.XmlEscape(ModuleName, false)).Append("\"");
            }

            sb.Append("/>");
            return sb;
        }
        #region private
        internal TraceCodeAddress(TraceCodeAddresses codeAddresses, CodeAddressIndex codeAddressIndex)
        {
            this.codeAddresses = codeAddresses;
            this.codeAddressIndex = codeAddressIndex;
        }

        private TraceCodeAddresses codeAddresses;
        private CodeAddressIndex codeAddressIndex;
        #endregion
    }

    /// <summary>
    /// MethodIndex uniquely identifies a method within the log.  Valid values are between 0 and
    /// TraceMethods.Count-1. Thus, an array can be used to 'attach' data to a method.
    /// </summary>
    public enum MethodIndex
    {
        /// <summary>
        /// Returned when no appropriate Method exists.
        /// </summary>
        Invalid = -1
    };

    /// <summary>
    /// Methods are so common in most traces, that having a .NET object (a TraceMethod) for
    /// each one is often too expensive.   As optimization, TraceLog also assigns a method index
    /// to every method and this index uniquely identifies the method in a very light weight fashion.
    /// <para>
    /// To be useful, however you need to be able to ask questions about a method index without creating
    /// a TraceMethod.   This is the primary purpose of a TraceMethods (accessible from TraceLog.CodeAddresses.Methods).
    /// It has a set of
    /// methods that take a MethodIndex and return properties of the method (like its name, and module file)
    /// </para>
    /// </summary>
    public sealed class TraceMethods : IFastSerializable, IEnumerable<TraceMethod>
    {
        /// <summary>
        /// Returns the count of method indexes.  All MethodIndexes are strictly less than this.
        /// </summary>
        public int Count { get { return methods.Count; } }

        /// <summary>
        /// Given a method index, if the method is managed return the IL meta data MethodToken (returns 0 for native code)
        /// </summary>
        public int MethodToken(MethodIndex methodIndex)
        {
            if (methodIndex == MethodIndex.Invalid)
            {
                return 0;
            }
            else
            {
                var value = methods[(int)methodIndex].methodDefOrRva;
                if (value < 0)
                {
                    value = 0;      // unmanaged code, return 0
                }

                return value;
            }
        }
        /// <summary>
        /// Given a method index, return the Method's RVA (offset from the base of the DLL in memory)  (returns 0 for managed code)
        /// </summary>
        public int MethodRva(MethodIndex methodIndex)
        {
            if (methodIndex == MethodIndex.Invalid)
            {
                return 0;
            }
            else
            {
                var value = methods[(int)methodIndex].methodDefOrRva;
                if (value > 0)
                {
                    value = 0;      // managed code, return 0
                }

                return -value;
            }
        }
        /// <summary>
        /// Given a method index, return the index for the ModuleFile associated with the Method Index.
        /// </summary>
        public ModuleFileIndex MethodModuleFileIndex(MethodIndex methodIndex)
        {
            if (methodIndex == MethodIndex.Invalid)
            {
                return ModuleFileIndex.Invalid;
            }
            else
            {
                return methods[(int)methodIndex].moduleIndex;
            }
        }
        /// <summary>
        /// Given a method index, return the Full method name (Namespace.ClassName.MethodName) associated with the Method Index.
        /// </summary>
        public string FullMethodName(MethodIndex methodIndex)
        {
            if (methodIndex == MethodIndex.Invalid)
            {
                return "";
            }
            else
            {
                return methods[(int)methodIndex].fullMethodName;
            }
        }

        /// <summary>
        /// Given a method index, return a TraceMethod that also represents the method.
        /// </summary>
        public TraceMethod this[MethodIndex methodIndex]
        {
            get
            {
                if (methodObjects == null || (int)methodIndex >= methodObjects.Length)
                {
                    methodObjects = new TraceMethod[(int)methodIndex + 16];
                }

                if (methodIndex == MethodIndex.Invalid)
                {
                    return null;
                }

                TraceMethod ret = methodObjects[(int)methodIndex];
                if (ret == null)
                {
                    ret = new TraceMethod(this, methodIndex);
                    methodObjects[(int)methodIndex] = ret;
                }
                return ret;
            }
        }

        /// <summary>
        /// Returns an XML representation of the TraceMethods.
        /// </summary>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<TraceMethods Count=").Append(XmlUtilities.XmlQuote(methods.Count)).AppendLine(">");
            foreach (TraceMethod method in this)
            {
                sb.Append("  ").Append(method.ToString()).AppendLine();
            }

            sb.AppendLine("</TraceMethods>");
            return sb.ToString();
        }
        #region private
        internal TraceMethods(TraceCodeAddresses codeAddresses) { this.codeAddresses = codeAddresses; }

        /// <summary>
        /// IEnumerable support
        /// </summary>
        /// <returns></returns>
        public IEnumerator<TraceMethod> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
            {
                yield return this[(MethodIndex)i];
            }
        }

        // Positive is a token, negative is an RVA
        internal MethodIndex NewMethod(string fullMethodName, ModuleFileIndex moduleIndex, int methodTokenOrRva)
        {
            MethodIndex ret = (MethodIndex)methods.Count;
            methods.Add(new MethodInfo(fullMethodName, moduleIndex, methodTokenOrRva));
            return ret;
        }

        void IFastSerializable.ToStream(Serializer serializer)
        {
            lazyMethods.Write(serializer, delegate
            {
                serializer.Write(codeAddresses);
                serializer.Write(methods.Count);
                serializer.Log("<WriteCollection name=\"methods\" count=\"" + methods.Count + "\">\r\n");
                for (int i = 0; i < methods.Count; i++)
                {
                    serializer.Write(methods[i].fullMethodName);
                    serializer.Write(methods[i].methodDefOrRva);
                    serializer.Write((int)methods[i].moduleIndex);
                }
                serializer.Log("</WriteCollection>\r\n");
            });
        }

        void IFastSerializable.FromStream(Deserializer deserializer)
        {
            lazyMethods.Read(deserializer, delegate
            {
                deserializer.Read(out codeAddresses);
                int count = deserializer.ReadInt();
                deserializer.Log("<Marker name=\"methods\" count=\"" + count + "\"/>");
                MethodInfo methodInfo = new MethodInfo();
                methods = new GrowableArray<MethodInfo>(count + 1);

                for (int i = 0; i < count; i++)
                {
                    deserializer.Read(out methodInfo.fullMethodName);
                    deserializer.Read(out methodInfo.methodDefOrRva);
                    methodInfo.moduleIndex = (ModuleFileIndex)deserializer.ReadInt();
                    methods.Add(methodInfo);
                }
            });
            lazyMethods.FinishRead();        // TODO REMOVE
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            throw new NotImplementedException(); // GetEnumerator
        }

        private struct MethodInfo
        {
            // Positive is a token, negative is an RVA
            internal MethodInfo(string fullMethodName, ModuleFileIndex moduleIndex, int methodTokenOrRva)
            {
                this.fullMethodName = fullMethodName;
                this.moduleIndex = moduleIndex;
                methodDefOrRva = methodTokenOrRva;
            }
            internal string fullMethodName;
            internal ModuleFileIndex moduleIndex;
            internal int methodDefOrRva;               // For managed code, this is the token, (positive) for unmanaged it is -rva (rvas have to be < 2Gig).
        }

        private DeferedRegion lazyMethods;
        private GrowableArray<MethodInfo> methods;
        private TraceMethod[] methodObjects;
        internal TraceCodeAddresses codeAddresses;
        #endregion
    }
    /// <summary>
    /// A TraceMethod represents the symbolic information for a particular method.   To maximizes haring a TraceMethod
    /// has very little state, just the module and full method name.
    /// </summary>
    public sealed class TraceMethod
    {
        /// <summary>
        /// Each Method in the TraceLog is given an index that uniquely identifies it.  This return this index for this TraceMethod
        /// </summary>
        public MethodIndex MethodIndex { get { return methodIndex; } }
        /// <summary>
        /// The full name of the method (Namespace.ClassName.MethodName).
        /// </summary>
        public string FullMethodName { get { return methods.FullMethodName(methodIndex); } }
        /// <summary>
        /// .Net runtime methods have a token (32 bit number) that uniquely identifies it in the meta data of the managed DLL.
        /// This property returns this token. Returns 0 for unmanaged code or method not found.
        /// </summary>
        public int MethodToken { get { return methods.MethodToken(methodIndex); } }
        /// <summary>
        /// For native code the RVA (relative virtual address, which is the offset from the base of the file in memory)
        /// for the method in the file. Returns 0 for managed code or method not found;
        /// </summary>
        public int MethodRva { get { return methods.MethodRva(methodIndex); } }
        /// <summary>
        /// Returns the index for the DLL ModuleFile (which represents its file path) associated with this method
        /// </summary>
        public ModuleFileIndex MethodModuleFileIndex { get { return methods.MethodModuleFileIndex(methodIndex); } }
        /// <summary>
        /// Returns the ModuleFile (which represents its file path) associated with this method
        /// </summary>
        public TraceModuleFile MethodModuleFile { get { return methods.codeAddresses.ModuleFiles[MethodModuleFileIndex]; } }

        /// <summary>
        /// A XML representation of the TraceMethod. (Used for debugging)
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            return ToString(sb).ToString();
        }
        /// <summary>
        /// Writes an XML representation of the TraceMethod to the stringbuilder 'sb'
        /// </summary>
        /// <param name="sb"></param>
        /// <returns></returns>
        public StringBuilder ToString(StringBuilder sb)
        {
            sb.Append("  <TraceMethod ");
            if (FullMethodName.Length > 0)
            {
                sb.Append(" FullMethodName=\"").Append(XmlUtilities.XmlEscape(FullMethodName, false)).Append("\"");
            }

            sb.Append(" MethodIndex=\"").Append(XmlUtilities.XmlEscape(MethodIndex, false)).Append("\"");
            sb.Append(" MethodToken=\"").Append(XmlUtilities.XmlEscape(MethodToken, false)).Append("\"");
            sb.Append(" MethodRva=\"").Append(XmlUtilities.XmlEscape(MethodRva, false)).Append("\"");
            var moduleFile = MethodModuleFile;
            if (moduleFile != null)
            {
                sb.Append(" Module=\"").Append(moduleFile.Name).Append("\"");
            }

            sb.Append("/>");
            return sb;
        }
        #region private
        internal TraceMethod(TraceMethods methods, MethodIndex methodIndex)
        {
            this.methods = methods;
            this.methodIndex = methodIndex;
        }

        /// <summary>
        /// Returns a new string prefixed with the optimization tier if it would be useful. Typically used to adorn a method's
        /// name with the optimization tier of the specific code version of the method.
        /// </summary>
        internal static string PrefixOptimizationTier(string str, OptimizationTier optimizationTier)
        {
            if (optimizationTier == OptimizationTier.Unknown || string.IsNullOrWhiteSpace(str))
            {
                return str;
            }
            return $"[{optimizationTier}]{str}";
        }

        private TraceMethods methods;
        private MethodIndex methodIndex;
        #endregion
    }

    /// <summary>
    /// A ModuleFileIndex represents a particular file path on the disk.   It is a number
    /// from 0 to MaxModuleFileIndex, which means that you can create a side array to hold
    /// information about module files.
    ///
    /// You can look up information about the ModuleFile from the ModuleFiles type.
    /// </summary>
    public enum ModuleFileIndex
    {
        /// <summary>
        /// Returned when no appropriate ModuleFile exists.
        /// </summary>
        Invalid = -1
    };

    /// <summary>
    /// TraceModuleFiles is the list of all the ModuleFiles in the trace.   It is an IEnumerable.
    /// </summary>
    public sealed class TraceModuleFiles : IFastSerializable, IEnumerable<TraceModuleFile>
    {
        /// <summary>
        /// Each file is given an index for quick lookup.   Count is the
        /// maximum such index (thus you can create an array that is 1-1 with the
        /// files easily).
        /// </summary>
        public int Count { get { return moduleFiles.Count; } }
        /// <summary>
        /// Given a ModuleFileIndex, find the TraceModuleFile which also represents it
        /// </summary>
        public TraceModuleFile this[ModuleFileIndex moduleFileIndex]
        {
            get
            {
                if (moduleFileIndex == ModuleFileIndex.Invalid)
                {
                    return null;
                }

                return moduleFiles[(int)moduleFileIndex];
            }
        }
        /// <summary>
        /// Returns the TraceLog associated with this TraceModuleFiles
        /// </summary>
        public TraceLog Log { get { return log; } }

        /// <summary>
        /// Returns an XML representation of the TraceModuleFiles
        /// </summary>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<TraceModuleFiles Count=").Append(XmlUtilities.XmlQuote(moduleFiles.Count)).AppendLine(">");
            foreach (TraceModuleFile moduleFile in this)
            {
                sb.Append("  ").Append(moduleFile.ToString()).AppendLine();
            }

            sb.AppendLine("</TraceModuleFiles>");
            return sb.ToString();
        }
        #region private
        /// <summary>
        /// Enumerate all the files that occurred in the trace log.
        /// </summary>
        IEnumerator<TraceModuleFile> IEnumerable<TraceModuleFile>.GetEnumerator()
        {
            for (int i = 0; i < moduleFiles.Count; i++)
            {
                yield return moduleFiles[i];
            }
        }

        internal void SetModuleFileName(TraceModuleFile moduleFile, string fileName)
        {
            Debug.Assert(moduleFile.fileName == null);
            moduleFile.fileName = fileName;
            if (moduleFilesByName != null)
            {
                moduleFilesByName[fileName] = moduleFile;
            }
        }
        /// <summary>
        /// We cache information about a native image load in a TraceModuleFile.  Retrieve or create a new
        /// cache entry associated with 'nativePath' and 'moduleImageBase'.  'moduleImageBase' can be 0 for managed assemblies
        /// that were not loaded with LoadLibrary.
        /// </summary>
        internal TraceModuleFile GetOrCreateModuleFile(string nativePath, Address imageBase)
        {
            TraceModuleFile moduleFile = null;
            if (nativePath != null)
            {
                moduleFile = GetModuleFile(nativePath, imageBase);
            }

            if (moduleFile == null)
            {
                moduleFile = new TraceModuleFile(nativePath, imageBase, (ModuleFileIndex)moduleFiles.Count);
                moduleFiles.Add(moduleFile);
                if (nativePath != null)
                {
                    TraceModuleFile prevValue;
                    if (moduleFilesByName.TryGetValue(nativePath, out prevValue))
                    {
                        moduleFile.next = prevValue;
                    }

                    moduleFilesByName[nativePath] = moduleFile;
                }
            }

            Debug.Assert(moduleFilesByName == null || moduleFiles.Count >= moduleFilesByName.Count);
            return moduleFile;
        }

        /// <summary>
        /// For a given file name, get the TraceModuleFile associated with it.
        /// </summary>
        internal TraceModuleFile GetModuleFile(string fileName, Address imageBase)
        {
            TraceModuleFile moduleFile;
            if (moduleFilesByName == null)
            {
                moduleFilesByName = new Dictionary<string, TraceModuleFile>(Math.Max(256, moduleFiles.Count + 4), StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < moduleFiles.Count; i++)
                {
                    moduleFile = moduleFiles[i];
                    Debug.Assert(moduleFile.next == null);

                    if (string.IsNullOrEmpty(moduleFile.fileName))
                    {
                        continue;
                    }

                    TraceModuleFile collision;
                    if (moduleFilesByName.TryGetValue(moduleFile.fileName, out collision))
                    {
                        moduleFile.next = collision;
                    }
                    else
                    {
                        moduleFilesByName.Add(moduleFile.fileName, moduleFile);
                    }
                }
            }
            if (moduleFilesByName.TryGetValue(fileName, out moduleFile))
            {
                do
                {
                    // TODO review the imageBase == 0 condition.  Needed to get PDB signature on managed IL.
                    if (moduleFile.ImageBase == imageBase)
                    {
                        return moduleFile;
                    }
                    //                    options.ConversionLog.WriteLine("WARNING: " + fileName + " loaded with two base addresses 0x" + moduleImageBase.ToString("x") + " and 0x" + moduleFile.moduleImageBase.ToString("x"));
                    moduleFile = moduleFile.next;
                } while (moduleFile != null);
            }
            return moduleFile;
        }

        internal TraceModuleFiles(TraceLog log)
        {
            this.log = log;
        }
        void IFastSerializable.ToStream(Serializer serializer)
        {
            serializer.Write(log);
            serializer.Write(moduleFiles.Count);
            for (int i = 0; i < moduleFiles.Count; i++)
            {
                serializer.Write(moduleFiles[i]);
            }
        }
        void IFastSerializable.FromStream(Deserializer deserializer)
        {
            deserializer.Read(out log);
            int count = deserializer.ReadInt();
            moduleFiles = new GrowableArray<TraceModuleFile>(count + 1);
            for (int i = 0; i < count; i++)
            {
                TraceModuleFile elem;
                deserializer.Read(out elem);
                moduleFiles.Add(elem);
            }
            moduleFilesByName = null;
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            throw new NotImplementedException(); // GetEnumerator
        }

        private TraceLog log;
        private Dictionary<string, TraceModuleFile> moduleFilesByName;
        private GrowableArray<TraceModuleFile> moduleFiles;
        #endregion
    }
    /// <summary>
    /// The TraceModuleFile represents a executable file that can be loaded into memory (either an EXE or a
    /// DLL).  It represents the path on disk as well as the location in memory where it loads (or
    /// its ModuleID if it is a managed module), but not the load or unload time or the process in which
    /// it was loaded (this allows them to be shared within the trace).
    /// </summary>
    public sealed class TraceModuleFile : IFastSerializable
    {
        /// <summary>
        /// The ModuleFileIndex ID that uniquely identifies this module file.
        /// </summary>
        public ModuleFileIndex ModuleFileIndex { get { return moduleFileIndex; } }
        /// <summary>
        /// The moduleFile name associated with the moduleFile.  May be the empty string if the moduleFile has no moduleFile
        /// (dynamically generated).  For managed code, this is the IL moduleFile name.
        /// </summary>
        public string FilePath
        {
            get
            {
                if (fileName == null)
                {
                    return "ManagedModule";
                }

                return fileName;
            }
        }
        /// <summary>
        /// This is the short name of the moduleFile (moduleFile name without extension).
        /// </summary>
        public string Name
        {
            get
            {
                if (name == null)
                {
                    var filePath = FilePath;
                    name = TraceLog.GetFileNameWithoutExtensionNoIllegalChars(filePath);
                }
                return name;
            }
        }
        /// <summary>
        /// Returns the address in memory where the dll was loaded.
        /// </summary>
        public Address ImageBase { get { return imageBase; } }
        /// <summary>
        /// Returns the size of the DLL when loaded in memory
        /// </summary>
        public int ImageSize { get { return imageSize; } }
        /// <summary>
        /// Returns the address just past the memory the module uses.
        /// </summary>
        public Address ImageEnd { get { return (Address)((ulong)imageBase + (uint)imageSize); } }

        /// <summary>
        /// The name of the symbol file (PDB file) associated with the DLL
        /// </summary>
        public string PdbName { get { return pdbName; } }
        /// <summary>
        /// Returns the GUID that uniquely identifies the symbol file (PDB file) for this DLL
        /// </summary>
        public Guid PdbSignature { get { return pdbSignature; } }
        /// <summary>
        /// Returns the age (which is a small integer), that is also needed to look up the symbol file (PDB file) on a symbol server.
        /// </summary>
        public int PdbAge { get { return pdbAge; } }

        /// <summary>
        /// Returns the file version string that is optionally embedded in the DLL's resources.   Returns the empty string if not present.
        /// </summary>
        public string FileVersion { get { return fileVersion; } }

        /// <summary>
        /// Returns the product name  recorded in the file version information.     Returns empty string if not present
        /// </summary>
        public string ProductName { get { return productName; } }

        /// <summary>
        /// Returns a version string for the product as a whole (could include GIT source code hash).    Returns empty string if not present
        /// </summary>
        public string ProductVersion { get { return productVersion; } }

        /// <summary>
        /// This is the checksum value in the PE header. Can be used to validate
        /// that the file on disk is the same as the file from the trace.
        /// </summary>
        public int ImageChecksum { get { return imageChecksum; } }

        /// <summary>
        /// This used to be called TimeDateStamp, but linkers may not use it as a
        /// timestamp anymore because they want deterministic builds.  It still is
        /// useful as a unique ID for the image.
        /// </summary>
        public int ImageId { get { return timeDateStamp; } }

        /// <summary>
        /// Tells if the module file is ReadyToRun (the has precompiled code for some managed methods)
        /// </summary>
        public bool IsReadyToRun { get { return isReadyToRun; } }

        /// <summary>
        /// If the Product Version fields has a GIT Commit Hash component, this returns it,  Otherwise it is empty.
        /// </summary>
        public string GitCommitHash
        {
            get
            {
                // First see if the commit hash is on the file version
                if (!string.IsNullOrEmpty(fileVersion))
                {
                    Match m = Regex.Match(fileVersion, @"Commit Hash:\s*(\S+)", RegexOptions.CultureInvariant);
                    if (m.Success)
                    {
                        return m.Groups[1].Value;
                    }
                }
                // or the product version.
                if (!string.IsNullOrEmpty(productVersion))
                {
                    Match m = Regex.Match(productVersion, @"Commit Hash:\s*(\S+)", RegexOptions.CultureInvariant);
                    if (m.Success)
                    {
                        return m.Groups[1].Value;
                    }
                }
                return "";
            }
        }

        /// <summary>
        /// Returns the time the DLL was built as a DateTime.   Note that this may not
        /// work if the build system uses deterministic builds (in which case timestamps
        /// are not allowed.   We may not be able to tell if this is a bad timestamp
        /// but we include it because when it is timestamp it is useful.
        /// </summary>
        public DateTime BuildTime
        {
            get
            {
                var ret = PEFile.PEHeader.TimeDateStampToDate(timeDateStamp);
                if (ret > DateTime.Now)
                {
                    ret = DateTime.MinValue;
                }

                return ret;
            }
        }
        /// <summary>
        /// The number of code addresses included in this module.  This is useful for determining if
        /// this module is worth having its symbolic information looked up or not.   It is not
        /// otherwise a particularly interesting metric.
        /// <para>
        /// This number is defined as the number of appearances this module has in any stack
        /// or any event with a code address (If the modules appears 5 times in a stack that
        /// counts as 5 even though it is just one event's stack).
        /// </para>
        /// </summary>
        public int CodeAddressesInModule { get { return codeAddressesInModule; } }
        /// <summary>
        /// If the module file was a managed native image, this is the IL file associated with it.
        /// </summary>
        public TraceModuleFile ManagedModule { get { return managedModule; } }

        /// <summary>
        /// Returns an XML representation of the TraceModuleFile (for debugging)
        /// </summary>
        public override string ToString()
        {
            return "<TraceModuleFile " +
                    "Name=" + XmlUtilities.XmlQuote(Name) + " " +
                    "ModuleFileIndex=" + XmlUtilities.XmlQuote(ModuleFileIndex) + " " +
                    "ImageSize=" + XmlUtilities.XmlQuoteHex(ImageSize) + " " +
                    "FileName=" + XmlUtilities.XmlQuote(FilePath) + " " +
                    "ImageBase=" + XmlUtilities.XmlQuoteHex((ulong)ImageBase) + " " +
                    "BuildTime=" + XmlUtilities.XmlQuote(BuildTime) + " " +
                    "PdbName=" + XmlUtilities.XmlQuote(PdbName) + " " +
                    "PdbSignature=" + XmlUtilities.XmlQuote(PdbSignature) + " " +
                    "PdbAge=" + XmlUtilities.XmlQuote(PdbAge) + " " +
                    "FileVersion=" + XmlUtilities.XmlQuote(FileVersion) + " " +
                    "IsReadyToRun=" + XmlUtilities.XmlQuote(IsReadyToRun) + " " +
                   "/>";
        }
        #region Private
        internal TraceModuleFile(string fileName, Address imageBase, ModuleFileIndex moduleFileIndex)
        {
            if (fileName != null)
            {
                this.fileName = fileName.ToLowerInvariant();        // Normalize to lower case.
            }

            this.imageBase = imageBase;
            this.moduleFileIndex = moduleFileIndex;
            fileVersion = "";
            productVersion = "";
            pdbName = "";
        }

        internal string fileName;
        internal int imageSize;
        internal Address imageBase;
        internal string name;
        private ModuleFileIndex moduleFileIndex;
        internal bool isReadyToRun;
        internal TraceModuleFile next;          // Chain of modules that have the same path (But different image bases)

        internal string pdbName;
        internal Guid pdbSignature;
        internal int pdbAge;
        internal string fileVersion;
        internal string productName;
        internal string productVersion;
        internal int timeDateStamp;
        internal int imageChecksum;                  // used to validate if the local file is the same as the one from the trace.
        internal int codeAddressesInModule;
        internal TraceModuleFile managedModule;


        void IFastSerializable.ToStream(Serializer serializer)
        {
            serializer.Write(fileName);
            serializer.Write(imageSize);
            serializer.WriteAddress(imageBase);

            serializer.Write(pdbName);
            serializer.Write(pdbSignature);
            serializer.Write(pdbAge);
            serializer.Write(fileVersion);
            serializer.Write(productVersion);
            serializer.Write(timeDateStamp);
            serializer.Write(imageChecksum);
            serializer.Write((int)moduleFileIndex);
            serializer.Write(codeAddressesInModule);
            serializer.Write(managedModule);
        }
        void IFastSerializable.FromStream(Deserializer deserializer)
        {
            deserializer.Read(out fileName);
            deserializer.Read(out imageSize);
            deserializer.ReadAddress(out imageBase);

            deserializer.Read(out pdbName);
            deserializer.Read(out pdbSignature);
            deserializer.Read(out pdbAge);
            deserializer.Read(out fileVersion);
            deserializer.Read(out productVersion);
            deserializer.Read(out timeDateStamp);
            deserializer.Read(out imageChecksum);
            moduleFileIndex = (ModuleFileIndex)deserializer.ReadInt();
            deserializer.Read(out codeAddressesInModule);
            deserializer.Read(out managedModule);
        }
        #endregion
    }

    /// <summary>
    /// A ActivityIndex uniquely identifies an Activity in the log. Valid values are between
    /// 0 and Activities.Count-1.
    /// </summary>
    public enum ActivityIndex
    {
        /// <summary>
        /// valid activity indexes are non-negative integers
        /// </summary>
        Invalid = -1
    }

    /// <summary>
    /// Representation of an Activity. An activity can be thought of as a unit of execution associated with
    /// a task or workitem; it executes on one thread, and has a start and end time. An activity keeps track
    /// of its "creator" or "caller" -- which is the activity that scheduled it. Using the "creator" link a
    /// user can determine the chain of activities that led up to the current one.
    ///
    /// Given an event you can get the Activity for the event using the Activity() extension method.
    /// </summary>
    public sealed class TraceActivity : IFastSerializable
    {
        /// <summary>
        /// Describes the kinds of known Activities (used for descriptive purposes alone)
        /// </summary>
        internal enum ActivityKind : short
        {
            /// <summary>Invalid</summary>
            Invalid = -1,
            /// <summary>
            /// Default activity on a thread (when the thread does not execute any code on
            /// behalf of anyone else)
            /// </summary>
            Initial = 0,
            /// <summary>
            /// An activity that was initiated by a Task.Run
            /// </summary>
            TaskScheduled = 1,
            /// <summary>
            /// An activity that's a task, but for which we didn't see a "Scheduled" event
            /// </summary>
            TaskStarted = 2,
            /// <summary>
            /// An activity that allows correlation between the antecedent and continuation
            /// </summary>
            AwaitTaskScheduled = 4,
            /// <summary>A thread started with Thread.Start</summary>
            ClrThreadStart = 5,
            /// <summary>Native CLR threadpool workitem</summary>
            ClrThreadPool = 6,
            /// <summary>Native CLR IO threadpool workitem</summary>
            ClrIOThreadPool = 7,
            /// <summary>Managed threadpool workitem</summary>
            FxThreadPool = 8,
            /// <summary>Generic managed thread transfer</summary>
            FxTransfer = 9,

            /// <summary>Managed async IO workitem</summary>
            FxAsyncIO = 11,
            /// <summary>WinRT Dispatched workitem</summary>
            FxWinRTDispatch = 12,
            /// <summary>
            /// Used when we make up ones because we know that have to be there but we don't know enough to do more than that.
            /// </summary>
            Implied = 13,


            // AutoComplete codes.
            /// <summary>
            /// An activity that allows correlation between the antecedent and continuation
            /// if have bit 5 set it means you auto-compete
            /// </summary>
            TaskWait = 32,
            /// <summary>
            /// Same as TaskWait, hwoever it auto-completes
            /// </summary>
            TaskWaitSynchronous = 64 + 33,
            /// <summary>
            /// Managed timer workitem
            /// </summary>
            FxTimer = 34, // FxTransfer + kind(1)
        }

        private static string ActivityKindToString(ActivityKind kind)
        {
            switch (kind)
            {
                case ActivityKind.Invalid: return "Invalid";
                case ActivityKind.Initial: return "Initial";
                case ActivityKind.TaskScheduled: return "TaskScheduled";
                case ActivityKind.TaskStarted: return "TaskStarted";
                case ActivityKind.AwaitTaskScheduled: return "AwaitTaskScheduled";
                case ActivityKind.ClrThreadStart: return "ClrThreadStart";
                case ActivityKind.ClrThreadPool: return "ClrThreadPool";
                case ActivityKind.ClrIOThreadPool: return "ClrIOThreadPool";
                case ActivityKind.FxThreadPool: return "FxThreadPool";
                case ActivityKind.FxTransfer: return "FxTransfer";
                case ActivityKind.FxAsyncIO: return "FxAsyncIO";
                case ActivityKind.FxWinRTDispatch: return "FxWinRTDispatch";
                case ActivityKind.Implied: return "Implied";
                case ActivityKind.TaskWait: return "TaskWait";
                case ActivityKind.TaskWaitSynchronous: return "TaskWaitSynchronous";
                case ActivityKind.FxTimer: return "FxTimer";
                default:
                    Debug.Fail("Missing ActivityKind case statement.");
                    return kind.ToString();
            }
        }

        /// <summary>A trace-wide unique id identifying an activity</summary>
        public ActivityIndex Index { get { return activityIndex; } }
        /// <summary>The activity that initiated or caused the current one</summary>
        public TraceActivity Creator { get { return creator; } }

        /// <summary>
        /// This return an unique string 'name' for the activity.  It is a the Index followed by
        /// a - followed by the TPL index (if available).  It is a bit nicer since it gives
        /// more information for debugging.
        /// </summary>
        public string ID
        {
            get
            {
                uint truncatedRawId = ((uint)rawID);
                if (truncatedRawId == 0xFFFFFFFF)
                {
                    if (kind == ActivityKind.Implied)
                    {
                        return "Implied/TID=" + Thread.ThreadID + "/S=" + StartTimeRelativeMSec.ToString("f3");
                    }

                    return "Thread/TID=" + Thread.ThreadID;
                }
                string rawIdString = (truncatedRawId < 0x1000000) ? truncatedRawId.ToString() : ("0x" + truncatedRawId.ToString("x"));
                return "C=" + CreationTimeRelativeMSec.ToString("f3") + "/S=" + StartTimeRelativeMSec.ToString("f3") + "/R=" + rawIdString;
            }
        }

        // TODO make public?
        /// <summary>
        /// Computes the creator path back to root.
        /// </summary>
        internal string Path
        {
            get
            {
                string creatorPath = (Creator != null) ? Creator.Path : "/";
                return creatorPath + "/" + ((int)Index).ToString();
            }
        }

        /// <summary>The thread on which the activity is running</summary>
        public TraceThread Thread { get { return thread; } set { thread = value; } }
        /// <summary>True if there may be multiple activities that were initiated by caller (e.g. managed Timers)</summary>
        public bool MultiTrigger { get { return multiTrigger; } }
        /// <summary>A descriptive label for the activity
        ///     TODO: eliminate and use ToString()?
        /// </summary>
        public string Name
        {
            get
            {
                // PERF: Hand-optimized string.Format to minimize allocations
                var sb = StringBuilderCache.Acquire();
                sb.Append('<');
                sb.Append(IsThreadActivity ? "ThreadActivity" : ((rawID >> 32) & 1) != 0 ? "Activity (concurrent)" : "Activity (continuation)");
                sb.Append(" Index=\"");
                sb.Append((int)Index);

                if (Thread != null)
                {
                    sb.Append("\" Thread=\"");
                    sb.Append(Thread.VerboseThreadName);
                    sb.Append("\" Create=\"");
                    sb.Append(CreationTimeRelativeMSec.ToString("f3"));
                    sb.Append("\" Start=\"");
                    sb.Append(StartTimeRelativeMSec.ToString("f3"));
                    sb.Append("\" kind=\"");
                    sb.Append(ActivityKindToString(kind));
                }

                sb.Append("\" RawID=\"0x");
                sb.Append(rawID.ToString("x"));
                sb.Append("\"/>");
                return StringBuilderCache.GetStringAndRelease(sb);
            }
        }

        /// <summary>
        /// A thread activity is the activity associate with an OS thread.   It is special because it may
        /// have a region that is disjoint.
        /// </summary>
        public bool IsThreadActivity { get { return ((ulong)rawID == 0xFFFFFFFFFFFFFFFF); } }

        /// <summary>Time from beginning of trace (in msec) when activity started executing</summary>
        public double StartTimeRelativeMSec
        {
            get
            {
                if (startTimeQPC == 0)
                {
                    return 0;
                }

                return thread.Process.Log.QPCTimeToRelMSec(startTimeQPC);
            }
        }
        /// <summary>Time from beginning of trace (in msec) when activity completed execution.  Does not include children.</summary>
        public double EndTimeRelativeMSec
        {
            get
            {
                if (endTimeQPC == 0)
                {
                    return 0;
                }

                return thread.Process.Log.QPCTimeToRelMSec(endTimeQPC);
            }
        }

        /// <summary>The event index of the TraceEvent instance that created/scheduled this activity</summary>
        public EventIndex CreationEventIndex { get { return creationEventIndex; } }
        /// <summary>The call stack index of the TraceEvent instance that scheduled (caused the creation of) the activity</summary>
        public CallStackIndex CreationCallStackIndex
        {
            get
            {
                Debug.Assert(creationCallStackIndex == thread.Process.Log.GetCallStackIndexForEventIndex(creationEventIndex));
                return creationCallStackIndex;
            }
        }
        /// <summary>Time from beginning of trace (in msec) when activity was scheduled</summary>
        public double CreationTimeRelativeMSec
        {
            get
            {
                if (creationTimeQPC == 0)
                {
                    return 0;
                }

                return thread.Process.Log.QPCTimeToRelMSec(creationTimeQPC);
            }
        }

        /// <summary>
        /// To use mainly for debugging
        /// </summary>
        public override string ToString()
        {
            return Name;
        }

        #region private
        internal TraceActivity(ActivityIndex activityIndex, TraceActivity creator, EventIndex creationEventIndex,
            CallStackIndex creationCallStackIndex, long creationTimeQPC, Address rawID, bool multiTrigger, bool gcBound, TraceActivity.ActivityKind kind)
        {
            this.activityIndex = activityIndex;
            this.creator = creator;
            // TODO FIX NOW use or remove !
            this.creationCallStackIndex = creationCallStackIndex;
            this.creationEventIndex = creationEventIndex;
            this.creationTimeQPC = creationTimeQPC;
            this.multiTrigger = multiTrigger;
            this.gcBound = gcBound;
            this.kind = kind;
            this.rawID = rawID;
        }


        internal static unsafe Address GuidToLongId(ref Guid guid)
        {
            fixed (Guid* pg = &guid)
            {
                return *(Address*)pg;
            }
        }

        void IFastSerializable.ToStream(Serializer serializer)
        {
            serializer.Write((int)activityIndex);
            serializer.Write(creator);
            serializer.Write((int)creationCallStackIndex);
            serializer.Write(thread);
            serializer.Write((int)creationEventIndex);
            serializer.Write(creationTimeQPC);
            serializer.Write(startTimeQPC);
            serializer.Write(endTimeQPC);
            serializer.Write(multiTrigger);
            serializer.Write(gcBound);
            serializer.Write((short)kind);
        }

        void IFastSerializable.FromStream(Deserializer deserializer)
        {
            activityIndex = (ActivityIndex)deserializer.ReadInt();
            deserializer.Read(out creator);
            creationCallStackIndex = (CallStackIndex)deserializer.ReadInt();
            deserializer.Read(out thread);
            creationEventIndex = (EventIndex)deserializer.ReadInt();
            deserializer.Read(out creationTimeQPC);
            deserializer.Read(out startTimeQPC);
            deserializer.Read(out endTimeQPC);
            deserializer.Read(out multiTrigger);
            deserializer.Read(out gcBound);
            short kind; deserializer.Read(out kind); this.kind = (TraceActivity.ActivityKind)kind;
        }

        private ActivityIndex activityIndex;
        internal TraceActivity creator;
        private EventIndex creationEventIndex;      // The event index of the TraceEvent instance that created/scheduled this activity
        private CallStackIndex creationCallStackIndex;
        internal TraceThread thread;
        internal long creationTimeQPC;
        internal long startTimeQPC;
        internal long endTimeQPC;
        private bool multiTrigger;                  // True if there may be multiple activities that were initiated by caller (e.g. managed Timers)
        private bool gcBound;
        internal TraceActivity.ActivityKind kind;
        internal Address rawID;                      // ID used to identify this activity before we normalized it.
        internal TraceActivity prevActivityOnThread; // We can have a stack of active activities on a given thread.  This points to the one we preempted.
                                                     // This is only used in the ActivityComputer.
        #endregion
    }

    /// <summary>
    /// TraceLogOptions control the generation of a TraceLog (ETLX file) from an ETL file.
    /// </summary>
    public sealed class TraceLogOptions
    {
        /// <summary>
        /// Creates a new object containing options for constructing a TraceLog file.
        /// </summary>
        public TraceLogOptions() { }
        /// <summary>
        /// If non-null, this is a predicate that, given a file path to a dll, answers the question
        /// whether the PDB associated with that DLL be looked up and its symbolic information added
        /// to the TraceLog file as part of conversion.   Symbols can be looked up afterward when
        /// the file is later opened, so the default (which is to look up no symbols during
        /// conversion) is typically OK.
        /// </summary>
        public Predicate<string> ShouldResolveSymbols;
        /// <summary>
        /// Resolving symbols from a symbol server can take a long time. If
        /// there is a DLL that always fails, it can be quite annoying because
        /// it will always cause delays, By specifying only local symbols it
        /// will only resolve the symbols if it can do so without the delay of network traffic.
        /// Symbols that have been previously cached locally from a symbol
        /// server count as local symbols.
        /// </summary>
        public bool LocalSymbolsOnly;
        /// <summary>
        /// By default symbols are only resolved if there are stacks associated with the trace.
        /// Setting this option forces resolution even if there are no stacks.
        /// </summary>
        public bool AlwaysResolveSymbols;
        /// <summary>
        /// Writes status to this log.  Useful for debugging symbol issues.
        /// </summary>
        public TextWriter ConversionLog
        {
            get
            {
                if (m_ConversionLog == null)
                {
                    if (ConversionLogName != null)
                    {
                        m_ConversionLog = File.CreateText(ConversionLogName);
                    }
                    else
                    {
                        m_ConversionLog = new StringWriter();
                    }
                }
                return m_ConversionLog;
            }
            set
            {
                m_ConversionLog = value;
            }
        }
        /// <summary>
        /// If ConversionLogName is set, it indicates that any messages associated with creating the TraceLog should be written here.
        /// </summary>
        public string ConversionLogName;
        /// <summary>
        /// ETL files typically contain a large number of 'bookkeeping' event for resolving names of files, or methods or to indicate information
        /// about processes that existed when the trace was started (DCStart and DCStop events).   By default these events are stripped from
        /// the ETLX file because their information has already been used to do the bookkeeping as part of the conversion
        /// <para>
        /// However sometimes it is useful to keep these events (typically for debugging TraceEvent itself) and setting this
        /// property to true will cause every event in the ETL file to be copied as an event to the ETLX file.
        /// </para>
        /// </summary>
        public bool KeepAllEvents;
        /// <summary>
        /// Sometimes ETL files are too big , and you just want to look at a fraction of it to speed things up
        /// (or to keep file size under control).  The MaxEventCount property allows that.   10M will produce a 3-4GB ETLX file.
        /// 1M is a good value to keep ETLX file size under control.  Note that that the conversion still scan the entire
        /// original ETL file too look for bookkeeping events, however MaxEventCount events will be transferred to the ETLX
        /// file as events.
        /// <para>
        /// The default is 10M because ETLX has a restriction of 4GB in size.
        /// </para>
        /// </summary>
        public int MaxEventCount;
        /// <summary>
        /// If an ETL file has too many events for efficient processing the first part of the trace can be skipped by setting this
        /// property.   Any event which happens before 'SkipMSec' into the session will be filtered out.   This property is
        /// intended to be used along with the MaxEventCount property to carve out a arbitrary chunk of time from an ETL
        /// file as it is converted to an ETLX file.
        /// </summary>
        public double SkipMSec;
        /// <summary>
        /// If this delegate is non-null, it is called if there are any lost events or if the file was truncated.
        /// It is passed a bool whether the ETLX file was truncated, as well as the number of lost events and the
        /// total number of events in the ETLX file.  You can throw if you want to abort.
        /// </summary>
        public Action<bool, int, int> OnLostEvents;
        /// <summary>
        /// If you have the manifests for particular providers, you can read them in explicitly by setting this directory.
        ///  All files of the form *.manifest.xml will be read into the DynamicTraceEventParser's database before conversion
        ///  starts.
        /// </summary>
        public string ExplicitManifestDir;
        /// <summary>
        /// If errors occur during conversion, just assume the traced ended at that point and continue.
        /// </summary>
        public bool ContinueOnError;

        #region private
        private TextWriter m_ConversionLog;
        #endregion
    }

    /// <summary>
    /// The TraceEvent instances returned during the processing of a TraceLog have additional capabilities that these extension methods can access.
    /// </summary>
    public static class TraceLogExtensions
    {
        /// <summary>
        /// Finds the TraceProcess associated with a TraceEvent.
        /// Guaranteed to be non-null for non-real-time sessions if the process ID is != -1
        /// </summary>
        public static TraceProcess Process(this TraceEvent anEvent)
        {
            TraceLog log = anEvent.Source as TraceLog;
            if (null == log)
            {
                throw new InvalidOperationException("Attempted to use TraceLog support on a non-TraceLog TraceEventSource.");
            }
            TraceProcess ret = log.Processes.GetProcess(anEvent.ProcessID, anEvent.TimeStampQPC);
            // When the trace was converted, a TraceProcess should have been created for
            // every mentioned Process ID.
            // When we care, we should insure this is true for the RealTime case.
            Debug.Assert(ret != null || log.IsRealTime);
            return ret;
        }
        /// <summary>
        /// Finds the TraceThread associated with a TraceEvent.
        /// Guaranteed to be non-null for non-real-time sessions if the process ID is != -1
        /// </summary>
        public static TraceThread Thread(this TraceEvent anEvent)
        {
            TraceLog log = anEvent.Source as TraceLog;
            if (null == log)
            {
                throw new InvalidOperationException("Attempted to use TraceLog support on a non-TraceLog TraceEventSource.");
            }
            TraceThread ret = log.Threads.GetThread(anEvent.ThreadID, anEvent.TimeStampQPC);
            // When the trace was converted, a TraceThread should have been created for
            // every mentioned Thread ID.
            // When we care, we should insure this is true for the RealTime case.
            Debug.Assert(ret != null || log.IsRealTime);
            return ret;
        }
        /// <summary>
        /// Finds the TraceLog associated with a TraceEvent.
        /// </summary>
        public static TraceLog Log(this TraceEvent anEvent)
        {
            return anEvent.Source as TraceLog;
        }
        /// <summary>
        /// Finds the TraceCallStack associated with a TraceEvent.   Returns null if the event does not have callstack.
        /// </summary>
        public static TraceCallStack CallStack(this TraceEvent anEvent)
        {
            TraceLog log = anEvent.Source as TraceLog;
            if (null == log)
            {
                throw new InvalidOperationException("Attempted to use TraceLog support on a non-TraceLog TraceEventSource.");
            }
            return log.GetCallStackForEvent(anEvent);
        }
        /// <summary>
        /// Finds the CallStack index associated with a TraceEvent.   Returns Invalid if the event does not have callstack.
        /// </summary>
        public static CallStackIndex CallStackIndex(this TraceEvent anEvent)
        {
            TraceLog log = anEvent.Source as TraceLog;
            if (log == null)
            {
                return Microsoft.Diagnostics.Tracing.Etlx.CallStackIndex.Invalid;
            }

            return log.GetCallStackIndexForEvent(anEvent);
        }
        /// <summary>
        /// Finds the CallStack index associated the blocking thread for CSwitch event
        /// </summary>
        public static CallStackIndex BlockingStack(this CSwitchTraceData anEvent)
        {
            TraceLog log = anEvent.Source as TraceLog;
            if (log == null)
            {
                return Microsoft.Diagnostics.Tracing.Etlx.CallStackIndex.Invalid;
            }

            return log.GetCallStackIndexForCSwitchBlockingEventIndex(anEvent.EventIndex);
        }
        /// <summary>
        /// Finds the TraceCallStacks associated with a TraceEvent.
        /// </summary>
        public static TraceCallStacks CallStacks(this TraceEvent anEvent)
        {
            TraceLog log = anEvent.Source as TraceLog;
            if (null == log)
            {
                throw new InvalidOperationException("Attempted to use TraceLog support on a non-TraceLog TraceEventSource.");
            }
            return log.CallStacks;
        }
        /// <summary>
        /// Finds the Activity associated with a TraceEvent
        /// </summary>
        [Obsolete("Likely to be removed Replaced by ActivityMap.GetActivityC(TraceEvent)")]
        public static TraceActivity Activity(this TraceEvent anEvent)
        {
            throw new InvalidOperationException("Don't use activities right now");

        }
        /// <summary>
        /// Finds the ActivityIndex associated with a TraceEvent
        /// </summary>
        public static ActivityIndex ActivityIndex(this TraceEvent anEvent)
        {
            TraceLog log = anEvent.Source as TraceLog;
            if (log == null)
            {
                return Microsoft.Diagnostics.Tracing.Etlx.ActivityIndex.Invalid;
            }

            TraceThread thread = Thread(anEvent);
            return thread.GetActivityIndex(anEvent.TimeStampQPC);
        }
        /// <summary>
        /// For a PageFaultTraceData event, gets the TraceCodeAddress associated with the ProgramCounter address.
        /// </summary>
        public static TraceCodeAddress ProgramCounterAddress(this MemoryPageFaultTraceData anEvent)
        {
            TraceLog log = anEvent.Source as TraceLog;
            if (null == log)
            {
                throw new InvalidOperationException("Attempted to use TraceLog support on a non-TraceLog TraceEventSource.");
            }
            return log.GetCodeAddressAtEvent(anEvent.ProgramCounter, anEvent);
        }
        /// <summary>
        /// For a PageFaultTraceData event, gets the CodeAddressIndex associated with the ProgramCounter address.
        /// </summary>
        public static CodeAddressIndex ProgramCounterAddressIndex(this MemoryPageFaultTraceData anEvent)
        {
            TraceLog log = anEvent.Source as TraceLog;
            if (null == log)
            {
                throw new InvalidOperationException("Attempted to use TraceLog support on a non-TraceLog TraceEventSource.");
            }
            return log.GetCodeAddressIndexAtEvent(anEvent.ProgramCounter, anEvent);
        }
        /// <summary>
        /// For a SampledProfileTraceData event, gets the TraceCodeAddress associated with the InstructionPointer address.
        /// </summary>
        public static TraceCodeAddress IntructionPointerCodeAddress(this SampledProfileTraceData anEvent)
        {
            TraceLog log = anEvent.Source as TraceLog;
            if (null == log)
            {
                throw new InvalidOperationException("Attempted to use TraceLog support on a non-TraceLog TraceEventSource.");
            }
            return log.GetCodeAddressAtEvent(anEvent.InstructionPointer, anEvent);
        }
        /// <summary>
        /// For a SampledProfileTraceData event, gets the CodeAddressIndex associated with the InstructionPointer address.
        /// </summary>
        public static CodeAddressIndex IntructionPointerCodeAddressIndex(this SampledProfileTraceData anEvent)
        {
            TraceLog log = anEvent.Source as TraceLog;
            if (null == log)
            {
                throw new InvalidOperationException("Attempted to use TraceLog support on a non-TraceLog TraceEventSource.");
            }
            return log.GetCodeAddressIndexAtEvent(anEvent.InstructionPointer, anEvent);
        }

        /// <summary>
        /// For a SysCallEnterTraceData event, gets the CodeAddressIndex associated with the SysCallAddress address.
        /// </summary>
        public static CodeAddressIndex SysCallAddress(this SysCallEnterTraceData anEvent)
        {
            TraceLog log = anEvent.Source as TraceLog;
            if (null == log)
            {
                throw new InvalidOperationException("Attempted to use TraceLog support on a non-TraceLog TraceEventSource.");
            }
            return log.GetCodeAddressIndexAtEvent(anEvent.SysCallAddress, anEvent);
        }


        /// <summary>
        /// For a PMCCounterProfTraceData event, gets the TraceCodeAddress associated with the InstructionPointer address.
        /// </summary>
        public static TraceCodeAddress IntructionPointerCodeAddress(this PMCCounterProfTraceData anEvent)
        {
            TraceLog log = anEvent.Source as TraceLog;
            if (null == log)
            {
                throw new InvalidOperationException("Attempted to use TraceLog support on a non-TraceLog TraceEventSource.");
            }
            return log.GetCodeAddressAtEvent(anEvent.InstructionPointer, anEvent);
        }
        /// <summary>
        /// For a PMCCounterProfTraceData event, gets the CodeAddressIndex associated with the InstructionPointer address.
        /// </summary>
        public static CodeAddressIndex IntructionPointerCodeAddressIndex(this PMCCounterProfTraceData anEvent)
        {
            TraceLog log = anEvent.Source as TraceLog;
            if (null == log)
            {
                throw new InvalidOperationException("Attempted to use TraceLog support on a non-TraceLog TraceEventSource.");
            }
            return log.GetCodeAddressIndexAtEvent(anEvent.InstructionPointer, anEvent);
        }

        /// <summary>
        /// For a ISRTraceData event, gets the CodeAddressIndex associated with the Routine address.
        /// </summary>
        public static CodeAddressIndex RoutineCodeAddressIndex(this ISRTraceData anEvent)
        {
            TraceLog log = anEvent.Source as TraceLog;
            if (null == log)
            {
                throw new InvalidOperationException("Attempted to use TraceLog support on a non-TraceLog TraceEventSource.");
            }

            return log.GetCodeAddressIndexAtEvent(anEvent.Routine, anEvent);
        }

        /// <summary>
        /// For a DPCTraceData event, gets the CodeAddressIndex associated with the Routine address.
        /// </summary>
        public static CodeAddressIndex RoutineCodeAddressIndex(this DPCTraceData anEvent)
        {
            TraceLog log = anEvent.Source as TraceLog;
            if (null == log)
            {
                throw new InvalidOperationException("Attempted to use TraceLog support on a non-TraceLog TraceEventSource.");
            }

            return log.GetCodeAddressIndexAtEvent(anEvent.Routine, anEvent);
        }
    }

    #region Private Classes


    internal struct JavaScriptSourceKey : IEquatable<JavaScriptSourceKey>
    {
        public JavaScriptSourceKey(long sourceID, Address scriptContextID)
        {
            SourceID = sourceID;
            ScriptContextID = scriptContextID;
        }
        public override bool Equals(object obj)
        {
            throw new NotImplementedException();        // you should not be calling this!
        }
        public override int GetHashCode()
        {
            return (int)SourceID + (int)ScriptContextID;
        }
        public bool Equals(JavaScriptSourceKey other)
        {
            return SourceID == other.SourceID && ScriptContextID == other.ScriptContextID;
        }
        public long SourceID;
        public Address ScriptContextID;
    }

    internal static class SerializerExtentions
    {
        public static void WriteAddress(this Serializer serializer, Address address)
        {
            serializer.Write((long)address);
        }
        public static void ReadAddress(this Deserializer deserializer, out Address address)
        {
            long longAddress;
            deserializer.Read(out longAddress);
            address = (Address)longAddress;
        }
    }


    #endregion
}
