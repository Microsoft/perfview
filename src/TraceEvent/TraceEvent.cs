//     Copyright (c) Microsoft Corporation.  All rights reserved.
// This file is best viewed using outline mode (Ctrl-M Ctrl-O)
//
// This program uses code hyperlinks available as part of the HyperAddin Visual Studio plug-in.
// It is available from http://www.codeplex.com/hyperAddin 
// 
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
#if !NETSTANDARD1_6
using System.Dynamic;
#endif
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Address = System.UInt64;

// #Introduction 
// 
// Note that TraceEvent lives in a Nuget package.   See 
// http://blogs.msdn.com/b/vancem/archive/2014/03/15/walk-through-getting-started-with-etw-traceevent-nuget-samples-package.aspx
// and 
//  http://blogs.msdn.com/b/vancem/archive/2013/08/15/traceevent-etw-library-published-as-a-nuget-package.aspx
// 
// For more details.  In particular the second blog post will contain the TraceEventProgrammersGuide.docx, which has
// more background.
//
// Finally if you are interested in creating your own TraceEventParsers for your ETW provider, inside Microsoft you can access
// the TraceParserGen tool at http://toolbox/TraceParserGen.   There is also a copy available externally at http://1drv.ms/1Rxk2iD 
// in the TraceParserGen.zip file and the TraceParserGen.src.zip file.   
//
// The the heart of the ETW reader are two important classes.
// 
//     * TraceEventSource which is an abstract represents the stream of events as a whole. Thus it
//         has holds things like session start and stop times, number of lost events etc.
//         
//     * TraceEvent is a base class that represents an individual event payload. Because different
//         events have different fields, this is actually the base of a class hierarchy. TraceEvent
//         itself holds all properties that are common to all events (like TimeDateStamp, ProcessID,
//         ThreadID, etc). Subclasses then add properties that know how to parse that specific event
//         type.
//         
// However these two classes are not enough. ETW has a model where there can be many independent
// providers each of which contributed to the event stream. Since the number of providers is unknown,
// TraceEventSource can not know the details of decoding all possible events. Instead we introduce
// a class
// 
//    TraceEventParser
//  
// one for each provider. This class knows the details of taking a binary blob representing a event and
// turning it into a TraceEvent. Since each provider has different details of how to do this,
// TraceEventParser is actually just a base class and specific subclasses of TraceEventParser
// like KernelTraceEventParser or ClrTraceEventParser do the real work.
// 
// TraceEventParsers have a very ridged layout that closely parallels the data in the providers's ETW
// manifest (in fact there is a tool for creating TraceEventParser's from a ETW manifest). A
// TraceEventParser has a C# event (callback) for each different event the provider can generate. These
// events callbacks have an argument that is the specific subclass of TraceEvent that represents
// the payload for that event. This allows client code to 'subscribe' to events in a strongly typed way.
// For example:
// 
// * ETWTraceEventSource source = new ETWTraceEventSource("output.etl"); // open an ETL file
// * KernelTraceEventParser kernelEvents = new KernelTraceEventParser(source); // Attach Kernel Parser.
// *
// * // Subscribe to the ImageLoad event that the KernelTraceEventParser knows about.
// * kernelEvents.ImageLoad += delegate(ImageLoadTraceData data) {
//      * Console.WriteLine("Got Image Base {0} ModuleFile {1} ", data.BaseAddress, data.FileName);
// * };
// *
// * // Attach more parsers, and subscribe to more events.
// * source.Process(); // Read through the stream, calling all the callbacks in one pass.
// 
// In the example above, ETWTraceEventSource (a specific subclass of TraceEventSource that understand
// ETL files) is created by opening the 'output.etl' file. Then the KernelTraceEventParser is 'attached'
// to the source so that kernel events can be decoded. Finally a callback is registered with the
// KernelTraceEventParser, to call user code when the 'ImageLoad' event is found in the stream. The user
// code has access to an ImageLoadTraceData which is a subclass of TraceEvent and has properties
// like 'BaseAddress' and 'FileName' which are specific to that particular event. The user can subscribe
// to many such events (each having different event-specific data), and then finally call Process() which
// causes the source to enumerate the event stream, calling the appropriate callbacks.
// 
// This model has the important attribute that new TraceEventParsers (ETW providers), can be crated and
// used by user code WITHOUT changing the code associated with TraceEventSource. Unfortunately, it
// has a discoverability drawback. Given a TraceEventSource (like ETWTraceEventSource), it is difficult
// discover that you need classes like KernelTraceEventParser to do anything useful with the
// source. As a concession to discoverabilty, TraceEventSource provides properties ('Kernel' and CLR)
// for two 'well known' parsers. Thus the above example can be written
// 
// * ETWTraceEventSource source = new ETWTraceEventSource("output.etl"); // open an ETL file
// * source.Kernel.ImageLoad += delegate(ImageLoadTraceData data) {
//      * Console.WriteLine("Got Image Base {0} ModuleFile {1} ", data.BaseAddress, data.FileName);
// * };
// * source.Process(); // Read through the stream, calling all the callbacks in one pass.
// 
// To keep efficiently high, this basic decode in Process() does NOT allocate new event every time a
// callback is made. Instead TraceEvent passed to the callback is reused on later events, so
// clients must copy the data out if they need to persist it past the time the callback returns. The
// TraceEvent.Clone method can be used to form a copy of a TraceEvent that will not be reused
// by the TraceEventSource.
// 
// Another important attribute of the system is that decoding of the fields of TraceEvent is done
// lazily. For example ImageLoadTraceData does not actually contain fields for things like
// 'BaseAddress' or 'FileName', but simply a pointer to the raw bits from the file. It is only when a
// property like ImageLoadTraceData.FileName it invoked that the raw bits are actually read converted
// to a string. The rationale for this approach is that it is common that substantial parts of an
// event's payload may be ignored by any particular client. A consequence of this approach is that for
// properties that do non-trivial work (like create a string from the raw data) it is better not to call
// the property multiple times (instead cache it locally in a local variable).
// 
// Supporting Sources that don't implement a callback model
// 
// In the previous example ETWTraceEventSource supported the subscription model where the client
// registers a set of callbacks and then calls Process() to cause the callbacks to happen. This model
// is very efficient and allows a lot of logically distinct processing to be done in 'one pass'. However
// we also want to support sources that do not wish to support the callback model (opting instead for a
// iteration model). To support this TraceEventSource that knows how to do this dispatch (as well
// as the Process()) method), is actually put in a subclass of TraceEventSource called
// TraceEventDispatcher. Those sources that support the subscription model inherit from
// TraceEventSource, and those that do not inherit directly from TraceEventSource.
// 
// The Protocol between TraceEventParser and TraceEventSource
// 
// What is common among all TraceEventSources (even if they do not support callbacks), is that parsers
// need to be registered with the source so that the source can decode the events. This is the purpose
// of the TraceEventSource.RegisterParser and TraceEventSource.RegisterEventTemplate methods.
// The expectation is that when a subclass of TraceEventParser is constructed, it will be passed a
// TraceEventSource. The parser should call the RegisterParser method, so that the source knows
// about this new parser. Also any time a user subscribes to a particular event in the parser, the
// source needs to know about so that its (shared) event dispatch table can be updated this is what
// RegisterEventTemplate is for.
// 
// * See also
//     * code:ETWTraceEventSource a trace event source for a .ETL file or a 'real time' ETW stream.
//     * code:ETLXTraceEventSource a trace event source for a ETLX file (post-processes ETL file).
//     * code:TraceEventParser is the base class for all event parsers for TraceEvents.
//     * code:TraceEventDispatcher contains logic for dispatching events in the callback model
//         * The heart of the callback logic is code:TraceEventDispatcher.Dispatch
namespace Microsoft.Diagnostics.Tracing
{
    /// <summary>
    /// TraceEventSource is an abstract base class that represents the output of a ETW session (e.g. a ETL file 
    /// or ETLX file or a real time stream).   This base class is NOT responsible for actually processing
    /// the events, but contains methods for properties associated with the session
    /// like its start and end time, filename, and characteristics of the machine it was collected on.
    /// <para>This class has two main subclasses:</para>
    /// <para>* <see cref="TraceEventDispatcher"/> which implements a 'push' (callback) model and is the only mode for ETL files.  
    /// ETWTraceEventSource is the most interesting subclass of TraceEventDispatcher.</para>
    /// <para>* see TraceLog which implements both a 'push' (callback) as well as pull (foreach) model but only works on ETLX files.</para>
    /// <para>This is the end.</para>
    /// <para>The normal user pattern is to create a TraceEventSource, create TraceEventParsers attached to the TraceEventSource, and then subscribe
    /// event callbacks using the TraceEventParsers</para>
    /// </summary>
    public abstract unsafe class TraceEventSource : ITraceParserServices, IDisposable
    {
        // Properties to subscribe to find important parsers (these are convenience routines). 

        /// <summary>
        /// For convenience, we provide a property returns a ClrTraceEventParser that knows 
        /// how to parse all the Common Language Runtime (CLR .NET) events into callbacks.
        /// </summary>
        public ClrTraceEventParser Clr
        {
            get
            {
                if (_CLR == null)
                {
                    _CLR = new ClrTraceEventParser(this);
                }

                return _CLR;
            }
        }

        /// <summary>
        /// For convenience, we provide a property returns a KernelTraceEventParser that knows 
        /// how to parse all the Kernel events into callbacks.
        /// </summary>
        public KernelTraceEventParser Kernel
        {
            // [SecuritySafeCritical]
            get
            {
                if (_Kernel == null)
                {
                    _Kernel = new KernelTraceEventParser(this);
                }

                return _Kernel;
            }
        }

#if !NOT_WINDOWS && !NO_DYNAMIC_TRACEEVENTPARSER
        /// <summary>
        /// For convenience, we provide a property returns a DynamicTraceEventParser that knows 
        /// how to parse all event providers that dynamically log their schemas into the event streams.
        /// In particular, it knows how to parse any events from a System.Diagnostics.Tracing.EventSources. 
        /// 
        /// Note that the DynamicTraceEventParser has subsumed the functionality of RegisteredTraceEventParser
        /// so any registered providers are also looked up here.  
        /// </summary>
        public DynamicTraceEventParser Dynamic
        {
            get
            {
                if (_Dynamic == null)
                {
                    _Dynamic = new DynamicTraceEventParser(this);
                }

                return _Dynamic;
            }
        }
        /// <summary>
        /// For convenience, we provide a property returns a RegisteredTraceEventParser that knows 
        /// how to parse all providers that are registered with the operating system.
        /// 
        /// Because the DynamicTraceEventParser has will parse all providers that that RegisteredTraceEventParser
        /// will parse, this function is obsolete, you should use Dynamic instead.  
        /// </summary>
        [Obsolete("Use Dynamic instead.   DynamicTraceEventParser decodes everything that RegisteredTraceEventParser can.")]
        public RegisteredTraceEventParser Registered
        {
            get
            {
                if (_Registered == null)
                {
                    _Registered = new RegisteredTraceEventParser(this);
                }

                return _Registered;
            }
        }
#endif // !NOT_WINDOWS

        /// <summary>
        /// The time when session started logging. 
        /// </summary>
        public DateTime SessionStartTime
        {
            get
            {
                var ret = QPCTimeToDateTimeUTC(sessionStartTimeQPC);
                return ret.ToLocalTime();
            }
        }

        /// <summary>
        /// The time that the session stopped logging.
        /// </summary>
        public DateTime SessionEndTime
        {
            get
            {
                var ret = QPCTimeToDateTimeUTC(sessionEndTimeQPC).ToLocalTime();
                Debug.Assert(SessionStartTime <= ret);
                return ret;
            }
        }
        /// <summary>
        /// The Session End time expressed as milliseconds from the start of the session
        /// </summary>
        public double SessionEndTimeRelativeMSec
        {
            get
            {
                Debug.Assert(sessionStartTimeQPC <= sessionEndTimeQPC);
                Debug.Assert((sessionEndTimeQPC - sessionStartTimeQPC) < _QPCFreq * 3600 * 24 * 10);    // less than 10 days.   
                var ret = QPCTimeToRelMSec(sessionEndTimeQPC);
                return ret;
            }
        }
        /// <summary>
        /// The difference between SessionEndTime and SessionStartTime;
        /// </summary>
        public TimeSpan SessionDuration { get { return SessionEndTime - SessionStartTime; } }

        /// <summary>
        /// The size of the trace, if it is known.  Will return 0 if it is not known.  
        /// </summary>
        public virtual long Size { get { return 0; } }
        /// <summary>
        /// Returns the size of a pointer on the machine where events were collected (4 for 32 bit or 8 for 64 bit)
        /// </summary>
        public int PointerSize { get { return pointerSize; } }
        /// <summary>
        /// The number of events that were dropped (e.g. because the incoming event rate was too fast)
        /// </summary>
        public abstract int EventsLost { get; }
        /// <summary>
        /// The number of processors on the machine doing the logging. 
        /// </summary>
        public int NumberOfProcessors { get { return numberOfProcessors; } }
        /// <summary>
        /// Cpu speed of the machine doing the logging. 
        /// </summary>
        public int CpuSpeedMHz { get { return cpuSpeedMHz; } }
        /// <summary>
        /// The version of the windows operating system on the machine doing the logging.
        /// </summary>
        public Version OSVersion { get { return osVersion; } }
        /// <summary>
        /// Returns true if this is a real time session.  
        /// </summary>
        public bool IsRealTime { get; protected set; }

        /// <summary>
        /// Time based threshold for how long data should be retained 
        /// by accumulates that are processing this TraceEventSource.
        /// A value of 0, the default, indicates an infinite accumulation.
        /// </summary>
        public double DataLifetimeMsec { get; set; }

        /// <summary>
        /// Check if a DataLifetime model is enabled
        /// </summary>
        /// <returns>True - lifetime tracking is enabled</returns>
        /// <returns>False - lifetime tracking is not enabled</returns>
        public bool DataLifetimeEnabled() { return DataLifetimeMsec > 0; }

        /// <summary>
        /// Closes any files and cleans up any resources associated with this TraceEventSource
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// TraceEventSource supports attaching arbitrary user data to the source.  This property returns a key-value bag of these attached values.  
        /// <para>
        /// One convention that has been established is that TraceEventParsers that need additional state to parse their events should
        /// store them in UserData under the key 'parsers\(ParserName)' 
        /// </para>
        /// </summary>
        public IDictionary<string, object> UserData { get { return userData; } }

        #region protected

        internal /*protected*/ TraceEventSource()
        {
            userData = new Dictionary<string, object>();
            _QPCFreq = 1;   // Anything non-zero so we don't get divide by zero failures in degenerate cases.  
        }

        // [SecuritySafeCritical]
        internal abstract void RegisterEventTemplateImpl(TraceEvent template);
        // [SecuritySafeCritical]
        internal abstract void UnregisterEventTemplateImpl(Delegate action, Guid providerGuid, int eventId);
        // [SecuritySafeCritical]
        internal abstract void RegisterParserImpl(TraceEventParser parser);
        // [SecuritySafeCritical]
        internal abstract void RegisterUnhandledEventImpl(Func<TraceEvent, bool> callback);
        // [SecuritySafeCritical]
        internal virtual string TaskNameForGuidImpl(Guid guid) { return null; }
        // [SecuritySafeCritical]
        internal virtual string ProviderNameForGuidImpl(Guid taskOrProviderGuid) { return null; }

        /// <summary>
        /// Dispose pattern
        /// </summary>
        protected virtual void Dispose(bool disposing) { }

        #region ITraceParserServices Members
        // [SecuritySafeCritical]
        void ITraceParserServices.RegisterEventTemplate(TraceEvent template)
        {
            Debug.Assert(template.traceEventSource == null);
            template.traceEventSource = this;

            Debug.Assert(template.eventRecord == null);
            Debug.Assert(template.next == null);
            template.next = null;       // Should be a no-op but I want to be extra sure. 

            RegisterEventTemplateImpl(template);
        }
        void ITraceParserServices.UnregisterEventTemplate(Delegate action, int eventId, Guid providerGuid)
        {
            UnregisterEventTemplateImpl(action, providerGuid, eventId);
        }

        // [SecuritySafeCritical]
        void ITraceParserServices.RegisterParser(TraceEventParser parser)
        {
            RegisterParserImpl(parser);
        }
        // [SecuritySafeCritical]
        void ITraceParserServices.RegisterUnhandledEvent(Func<TraceEvent, bool> callback)
        {
            RegisterUnhandledEventImpl(callback);
        }
        string ITraceParserServices.TaskNameForGuid(Guid guid)
        {
            return TaskNameForGuidImpl(guid);
        }
        string ITraceParserServices.ProviderNameForGuid(Guid taskOrProviderGuid)
        {
            return ProviderNameForGuidImpl(taskOrProviderGuid);
        }
        #endregion

        internal /*protected*/ IDictionary<string, object> userData;

        internal /*protected*/ int pointerSize;
        internal /*protected*/ int numberOfProcessors;
        internal /*protected*/ int cpuSpeedMHz;
        internal /*protected*/ int? utcOffsetMinutes;
        internal /*protected*/ Version osVersion;

        // Used to convert from Query Performance Counter (QPC) units to DateTime.
        internal /*protected*/ long _QPCFreq;
        internal /*protected*/ long _syncTimeQPC;       // An instant in time measured in QPC units (of _QPCFreq)
        internal /*protected*/ DateTime _syncTimeUTC;   // The same instant as a DateTime.  This is the only fundamental DateTime in the object. 

        internal /*protected*/ long sessionStartTimeQPC;
        internal /*protected*/ long sessionEndTimeQPC;
        internal /*protected*/ bool useClassicETW;
        internal /*protected*/ ClrTraceEventParser _CLR;
        internal /*protected*/ KernelTraceEventParser _Kernel;
#if !NOT_WINDOWS && !NO_DYNAMIC_TRACEEVENTPARSER
        internal /*protected*/ DynamicTraceEventParser _Dynamic;
        internal /*protected*/ RegisteredTraceEventParser _Registered;
#endif //  !NOT_WINDOWS
        #endregion
        #region private
        /// <summary>
        /// This is the high frequency tick clock on the processor (what QueryPerformanceCounter uses).  
        /// You should not need 
        /// </summary>
        internal long QPCFreq { get { return _QPCFreq; } }

        /// <summary>
        /// Converts the Query Performance Counter (QPC) ticks to a number of milliseconds from the start of the trace.   
        /// </summary>
        internal double QPCTimeToRelMSec(long QPCTime)
        {
            // Insure that we have a certain amount of sanity (events don't occur before sessionStartTime).  
            if (QPCTime < sessionStartTimeQPC)
            {
                QPCTime = sessionStartTimeQPC;
            }

            // We used to have a sanity check to insure that the time was always inside sessionEndTimeQPC
            // ETLX files enforce this, but sometimes ETWTraceEventParser (ETL) traces have bad session times.
            // After some thought, the best answer seems to be not to try to enforce this consistantancy.
            // (it will be true for ETLX but maybe not for ETWTraceEventParser scenarios).  

            Debug.Assert(sessionStartTimeQPC != 0 && _syncTimeQPC != 0 && _syncTimeUTC.Ticks != 0 && _QPCFreq != 0);
            // TODO this does not work for very long traces.   
            long diff = (QPCTime - sessionStartTimeQPC);
            // For real time providers, the session start time is the time when the TraceEventSource was turned on
            // but the session was turned on before that and events might have been buffered, which means you can
            // have negative numbers.  
            return diff * 1000.0 / QPCFreq;
        }

        /// <summary>
        /// Converts a Relative MSec time to the Query Performance Counter (QPC) ticks 
        /// </summary>
        internal long RelativeMSecToQPC(double relativeMSec)
        {
            Debug.Assert(sessionStartTimeQPC != 0 && _syncTimeQPC != 0 && _syncTimeUTC.Ticks != 0 && _QPCFreq != 0);
            return (long)(relativeMSec * _QPCFreq / 1000) + sessionStartTimeQPC;
        }

        /// <summary>
        /// Converts a DateTime to the Query Performance Counter (QPC) ticks 
        /// </summary>
        internal long UTCDateTimeToQPC(DateTime time)
        {
            Debug.Assert(_QPCFreq != 0);
            long ret = (long)((time.Ticks - _syncTimeUTC.Ticks) / 10000000.0 * _QPCFreq) + _syncTimeQPC;

            // The sessionEndTimeQPC == 0  effectively means 'called during trace startup' and we use it here to disable the 
            // assert.   During that time we get a wrong QPC we only use this to initialize sessionStartTimeQPC and 
            // sessionEndTimeQPC and we fix these up when we see the first event (see kernelParser.EventTraceHeader += handler).  
            Debug.Assert(sessionEndTimeQPC == 0 || (QPCTimeToDateTimeUTC(ret) - time).TotalMilliseconds < 1);
            return ret;
        }

        /// <summary>
        /// Converts the Query Performance Counter (QPC) ticks to a DateTime  
        /// </summary>
        internal DateTime QPCTimeToDateTimeUTC(long QPCTime)
        {
            if (QPCTime == long.MaxValue)   // We treat maxvalue as a special case.  
            {
                return DateTime.MaxValue;
            }

            // We expect all the time variables used to compute this to be set.   
            Debug.Assert(_syncTimeQPC != 0 && _syncTimeUTC.Ticks != 0 && _QPCFreq != 0);
            long inTicks = (long)((QPCTime - _syncTimeQPC) * 10000000.0 / _QPCFreq) + _syncTimeUTC.Ticks;
            // Avoid illegal DateTime values.   
            if (inTicks < 0 || DateTime.MaxValue.Ticks < inTicks)
            {
                inTicks = DateTime.MaxValue.Ticks;
            }

            var ret = new DateTime(inTicks, DateTimeKind.Utc);
            return ret;
        }

        internal virtual string ProcessName(int processID, long timeQPC)
        {
            return "Process(" + processID.ToString() + ")";
        }

        /// <summary>
        /// Some events (like HardFault) do not have a thread ID or a process ID, but they MIGHT have a Stack
        /// If they do try to get the ThreadID for the event from that.  Return -1 if not successful.   
        /// This is intended to be overridden by the TraceLog class that has this additional information. 
        /// </summary>
        internal virtual int LastChanceGetThreadID(TraceEvent data) { return -1; }
        internal virtual int LastChanceGetProcessID(TraceEvent data) { return -1; }
        internal virtual unsafe Guid GetRelatedActivityID(TraceEventNativeMethods.EVENT_RECORD* eventRecord)
        {
            var extendedData = eventRecord->ExtendedData;
            Debug.Assert((ulong)extendedData > 0x10000);          // Make sure this looks like a pointer.  
            for (int i = 0; i < eventRecord->ExtendedDataCount; i++)
            {
                if (extendedData[i].ExtType == TraceEventNativeMethods.EVENT_HEADER_EXT_TYPE_RELATED_ACTIVITYID)
                {
                    return *((Guid*)extendedData[i].DataPtr);
                }
            }

            return Guid.Empty;
        }

        internal virtual unsafe string GetContainerID(TraceEventNativeMethods.EVENT_RECORD* eventRecord)
        {
            string id = null;
            if (eventRecord->ExtendedDataCount != 0)
            {
                var ptr = eventRecord->ExtendedData;
                var end = &eventRecord->ExtendedData[eventRecord->ExtendedDataCount];
                while (ptr < end)
                {
                    if (ptr->ExtType == TraceEventNativeMethods.EVENT_HEADER_EXT_TYPE_CONTAINER_ID)
                    {
                        id = Marshal.PtrToStringAnsi((IntPtr)ptr->DataPtr, (int)ptr->DataSize);
                        break;
                    }

                    ptr++;
                }
            }

            return id;
        }
        #endregion
    }

    /// <summary>
    /// TraceEvent an abstract class represents the data from one event in the stream of events in a TraceEventSource.   
    /// The TraceEvent class has all the properties of an event that are common to all ETW events, including TimeStamp
    /// ProviderGuid, ProcessID etc.   Subclasses of TraceEvent then extend this abstract class to include properties
    /// specific to a particular payload.   
    /// <para>
    /// An important architectural point is that TraceEvent classes are aggressively reused by default.   The TraceEvent that is
    /// passed to any TraceEventParser callback or in a foreach is ONLY valid for the duration for that callback (or one
    /// iteration of the foreach).  If you need save a copy of the event data, you must call the Clone() method to make
    /// a copy.   The IObservable interfaces (TraceEventParser.Observe* methods) however implicitly call Clone() so you
    /// do not have to call Clone() when processing with IObservables (but these are slower).  
    /// </para>
    /// </summary>
    public abstract unsafe class TraceEvent
#if !NETSTANDARD1_6
        // To support DLR access of dynamic payload data ("((dynamic) myEvent).MyPayloadName"),
        // we derive from DynamicObject and override a couple of methods. If for some reason in
        // the future we wanted to derive from a different base class, we could also accomplish
        // this by implementing the IDynamicMetaObjectProvider interface instead.
        : DynamicObject
#endif
    {
        /// <summary>
        /// The GUID that uniquely identifies the Provider for this event.  This can return Guid.Empty for classic (Pre-VISTA) ETW providers.  
        /// </summary>        
        public Guid ProviderGuid { get { return providerGuid; } }

        /// <summary>
        /// Unique GUID for Pre-VISTA ETW providers.
        /// </summary>
        public Guid TaskGuid { get { return taskGuid; } }

        /// <summary>
        /// The name of the provider associated with the event.  It may be of the form Provider(GUID) or UnknownProvider in some cases but is never null.  
        /// </summary>
        public string ProviderName
        {
            get
            {
                if (providerName == null)
                {
                    Guid guid = providerGuid;
                    if (guid == Guid.Empty)
                    {
                        guid = taskGuid;
                    }

                    ITraceParserServices asParserServces = Source as ITraceParserServices;
                    if (asParserServces != null)
                    {
                        providerName = asParserServces.ProviderNameForGuid(guid);
                    }

                    if (providerName == null)
                    {
                        if (providerGuid == Guid.Empty)
                        {
                            providerName = "UnknownProvider";
                        }
                        else
                        {
                            providerName = "Provider(" + providerGuid.ToString() + ")";
                        }
                    }
                }
                return providerName;
            }
        }
        /// <summary>
        /// A name for the event.  This is simply the concatenation of the task and opcode names (separated by a /).  If the 
        /// event has no opcode, then the event name is just the task name.  
        /// </summary>
        public string EventName
        {
            get
            {
                if (eventName == null)
                {
                    var taskName = TaskName;
                    if (Opcode == TraceEventOpcode.Info || eventNameIsJustTaskName || string.IsNullOrEmpty(OpcodeName))
                    {
                        eventName = taskName;
                    }
                    else
                    {
                        eventName = taskName + "/" + OpcodeName;
                    }
                }
                return eventName;
            }
        }
        /// <summary>
        /// Returns the provider-specific integer value that uniquely identifies event within the scope of
        /// the provider. (Returns 0 for classic (Pre-VISTA) ETW providers).
        /// </summary>
        public TraceEventID ID
        {
            // [SecuritySafeCritical]
            get
            {
                Debug.Assert(eventRecord == null || IsClassicProvider || eventID == (TraceEventID)eventRecord->EventHeader.Id);
                return eventID;
            }
        }
        /// <summary>
        /// Events for a given provider can be given a group identifier (integer) called a Task that indicates the
        /// broad area within the provider that the event pertains to (for example the Kernel provider has
        /// Tasks for Process, Threads, etc).   
        /// </summary>
        public TraceEventTask Task { get { return task; } }
        /// <summary>
        /// The human readable name for the event's task (group of related events) (eg. process, thread,
        /// image, GC, ...).  May return a string Task(GUID) or Task(TASK_NUM) if no good symbolic name is
        /// available.  It never returns null.  
        /// </summary>
        public string TaskName
        {
            get
            {
                if (taskName == null)
                {
                    if (taskGuid != Guid.Empty)
                    {
                        ITraceParserServices asParserServces = Source as ITraceParserServices;
                        if (asParserServces != null)
                        {
                            taskName = asParserServces.TaskNameForGuid(taskGuid);
                        }

                        if (taskName == null)
                        {
                            taskName = "Task(" + taskGuid + ")";
                        }
                    }
                    else
                    {
                        eventNameIsJustTaskName = true;     // Don't suffix this with the opcode.  
                        if (eventID == 0)
                        {
                            taskName = "EventWriteString";
                        }
                        else
                        {
                            taskName = "EventID(" + ID + ")";
                        }
                    }
                }
                // Old EventSources did not have tasks for event names so we make an exception for these 
#if !NOT_WINDOWS && !NO_DYNAMIC_TRACEEVENTPARSER
                Debug.Assert(!string.IsNullOrEmpty(taskName) || (this is DynamicTraceEventData && ProviderName == "TplEtwProvider"));
#endif
                return taskName;
            }
        }
        /// <summary>
        /// An opcode is a numeric identifier (integer) that identifies the particular event within the group of events 
        /// identified by the event's task.  Often events have opcode 'Info' (0), which is the default.   This value
        /// is interpreted as having no-opcode (the task is sufficient to identify the event).
        /// <para>
        /// Generally the most useful opcodes are the Start and Stop opcodes which are used to indicate the beginning and the
        /// end of a interval of time.   Many tools will match up start and stop opcodes automatically and compute durations.  
        /// </para>
        /// </summary>
        public TraceEventOpcode Opcode { get { return opcode; } }
        /// <summary>
        /// Returns the human-readable string name for the Opcode property. 
        /// </summary>
        public string OpcodeName
        {
            get
            {
                if (opcodeName == null)
                {
                    opcodeName = ToString(Opcode);
                }

                return opcodeName;
            }
        }
        /// <summary>
        /// The verbosity of the event (Fatal, Error, ..., Info, Verbose)
        /// </summary>
        public TraceEventLevel Level
        {
            // [SecuritySafeCritical]
            get
            {
                // Debug.Assert(eventRecord->EventHeader.Level < 6, "Level out of range");
                return (TraceEventLevel)eventRecord->EventHeader.Level;
            }
        }
        /// <summary>
        /// The version number for this event.  The only compatible change to an event is to add new properties at the end.
        /// When this is done the version numbers is incremented.  
        /// </summary>
        public int Version
        {
            // [SecuritySafeCritical]
            get { return eventRecord->EventHeader.Version; }
        }
        /// <summary>
        /// ETW Event providers can specify a 64 bit bitfield called 'keywords' that define provider-specific groups of 
        /// events which can be enabled and disabled independently.   
        /// Each event is given a keywords mask that identifies which groups the event belongs to.   This property returns this mask.   
        /// </summary>
        public TraceEventKeyword Keywords
        {
            // [SecuritySafeCritical]
            get { return (TraceEventKeyword)eventRecord->EventHeader.Keyword; }
        }
        /// <summary>
        /// A Channel is a identifier (integer) that defines an 'audience' for the event (admin, operational, ...).   
        /// Channels are only used for Windows Event Log integration.  
        /// </summary>
        public TraceEventChannel Channel
        {
            // [SecuritySafeCritical]
            get { return (TraceEventChannel)eventRecord->EventHeader.Channel; }
        }

        /// <summary>
        /// The time of the event. You may find TimeStampRelativeMSec more convenient.  
        /// </summary>
        public DateTime TimeStamp
        {
            get { return traceEventSource.QPCTimeToDateTimeUTC(TimeStampQPC).ToLocalTime(); }
        }
        /// <summary>
        /// Returns a double representing the number of milliseconds since the beginning of the session.     
        /// </summary>
        public double TimeStampRelativeMSec
        {
            get
            {
                return traceEventSource.QPCTimeToRelMSec(TimeStampQPC);
            }
        }
        /// <summary>
        /// The thread ID for the thread that logged the event
        /// <para>This field may return -1 for some events when the thread ID is not known.</para>
        /// </summary>
        public int ThreadID
        {
            // [SecuritySafeCritical]
            get
            {
                var ret = eventRecord->EventHeader.ThreadId;
                if (ret == -1)
                {
                    ret = traceEventSource.LastChanceGetThreadID(this);     // See if the source has additional information (like a stack event associated with it)
                }

                return ret;
            }
        }
        /// <summary>
        /// The process ID of the process which logged the event. 
        /// <para>This field may return -1 for some events when the process ID is not known.</para>
        /// </summary>
        public virtual int ProcessID
        {
            // [SecuritySafeCritical]
            get
            {
                var ret = eventRecord->EventHeader.ProcessId;
                if (ret == -1)
                {
                    ret = traceEventSource.LastChanceGetProcessID(this);     // See if the source has additional information (like a stack event associated with it)
                }

                return ret;
            }
        }
        /// <summary>
        /// Returns a short name for the process. This the image file name (without the path or extension),
        /// or if that is not present, then the string 'Process(XXXX)' 
        /// </summary>
        public string ProcessName
        {
            get
            {
                return traceEventSource.ProcessName(ProcessID, TimeStampQPC);
            }
        }
        /// <summary>
        /// The processor Number (from 0 to TraceEventSource.NumberOfProcessors) that logged this event. 
        /// event. 
        /// </summary>
        public int ProcessorNumber
        {
            get
            {
                int ret = eventRecord->BufferContext.ProcessorNumber;
                Debug.Assert(0 <= ret && ret < traceEventSource.NumberOfProcessors);
                return ret;
            }
        }
        /// <summary>
        /// Get the size of a pointer associated with process that logged the event (thus it is 4 for a 32 bit process). 
        /// </summary>
        public int PointerSize
        {
            // [SecuritySafeCritical]
            get
            {
                Debug.Assert((eventRecord->EventHeader.Flags & TraceEventNativeMethods.EVENT_HEADER_FLAG_64_BIT_HEADER) != 0 ||
                             (eventRecord->EventHeader.Flags & TraceEventNativeMethods.EVENT_HEADER_FLAG_32_BIT_HEADER) != 0);
                return (eventRecord->EventHeader.Flags & TraceEventNativeMethods.EVENT_HEADER_FLAG_64_BIT_HEADER) != 0 ? 8 : 4;
            }
        }
        /// <summary>
        /// Conceptually every ETW event can be given a ActivityID (GUID) that uniquely identifies the logical
        /// work being carried out (the activity).  This property returns this GUID.   Can return Guid.Empty
        /// if the thread logging the event has no activity ID associated with it.  
        /// </summary>
        public Guid ActivityID { get { return eventRecord->EventHeader.ActivityId; } }
        /// <summary>
        /// ETW supports the ability to take events with another GUID called the related activity that is either
        /// causes or is caused by the current activity.   This property returns that GUID (or Guid.Empty if the
        /// event has not related activity.  
        /// </summary>
        public Guid RelatedActivityID
        {
            get
            {
                // handle the cloned case, we put it first in the buffer. 
                if (myBuffer != IntPtr.Zero)
                {
                    if (myBuffer != (IntPtr)eventRecord)
                    {
                        return *((Guid*)myBuffer);
                    }
                }
                else
                {
                    if (eventRecord->ExtendedData != null)
                    {
                        return traceEventSource.GetRelatedActivityID(eventRecord);
                    }
                }
                return Guid.Empty;
            }
        }
        /// <summary>
        /// Event Providers can define a 'message' for each event that are meant for human consumption.   
        /// FormattedMessage returns this string with the values of the payload filled in at the appropriate places.
        /// <para>It will return null if the event provider did not define a 'message'  for this event</para>
        /// </summary>
        public virtual string FormattedMessage { get { return GetFormattedMessage(null); } }

        /// <summary>
        /// Creates and returns the value of the 'message' for the event with payload values substituted.
        /// Payload values are formatted using the given formatProvider. 
        /// </summary>
        public virtual string GetFormattedMessage(IFormatProvider formatProvider)
        {
            // This lets simple string payloads be shown as the FormattedMessage.  
            if (IsEventWriteString ||
               (eventRecord->EventHeader.Id == 0 && eventRecord->EventHeader.Opcode == 0 && eventRecord->EventHeader.Task == 0))
            {
                return EventDataAsString();
            }

            return null;
        }

        /// <summary>
        /// An EventIndex is a integer that is guaranteed to be unique for this event over the entire log.  Its
        /// primary purpose is to act as a key that allows side tables to be built up that allow value added
        /// processing to 'attach' additional data to this particular event unambiguously.  
        /// <para>This property is only set for ETLX file.  For ETL or real time streams it returns 0</para>
        /// <para>EventIndex is currently a 4 byte quantity.  This does limit this property to 4Gig of events</para>
        /// </summary>
        public EventIndex EventIndex
        {
            get
            {
#if DEBUG
                // This is a guard against code running in TraceLog.CopyRawEvents that attempts to use
                // the EventIndex for an event returned by ETWTraceEventSource. It is unsafe to do so
                // because the EventIndex returned represents the index in the ETW stream, but user
                // code needs the index in the newly created ETLX stream (which does not include 
                // "bookkeeping" events. User code should use the captured variable eventCount instead.
                Debug.Assert(!DisallowEventIndexAccess, "Illegal access to EventIndex");
#endif
                return eventIndex;
            }
        }
        /// <summary>
        /// The TraceEventSource associated with this event.  
        /// </summary>
        public TraceEventSource Source { get { return traceEventSource; } }
        /// <summary>
        /// Returns true if this event is from a Classic (Pre-VISTA) provider
        /// </summary>
        public bool IsClassicProvider
        {
            // [SecuritySafeCritical]
            get { return (eventRecord->EventHeader.Flags & TraceEventNativeMethods.EVENT_HEADER_FLAG_CLASSIC_HEADER) != 0; }
        }

        /// <summary>
        /// The ID of the container that emitted the event, if available.
        /// </summary>
        public string ContainerID
        {
            get
            {
                // Handle the cloned case.
                if(instanceContainerID != null)
                {
                    return instanceContainerID;
                }

                // Non-cloned case.
                return traceEventSource.GetContainerID(eventRecord);
            }
        }

#if !NETSTANDARD1_6
        // These overloads allow integration with the DLR (Dynamic Language Runtime). That
        // enables getting at payload data in a more convenient fashion, directly by name.
        // In PowerShell, it "just works" (e.g. "$myEvent.MyPayload" will just work); in
        // C# you can activate it by casting to 'dynamic' (e.g. "var myEvent = (dynamic)
        // GetEventSomehow(); Console.WriteLine(myEvent.MyPayload);").

        public override IEnumerable<string> GetDynamicMemberNames()
        {
            return PayloadNames;
        }

        public override bool TryGetMember(GetMemberBinder binder, out object result)
        {
            result = PayloadByName(binder.Name);
            return result != null;
        }
#endif


        // Getting at payload values.  
        /// <summary>
        /// Returns the names of all the manifest declared field names for the event.    May be empty if the manifest is not available.  
        /// </summary>
        public abstract string[] PayloadNames { get; }
        /// <summary>
        /// Given an index from 0 to PayloadNames.Length-1, return the value for that payload item as an object (boxed if necessary).  
        /// </summary>
        public abstract object PayloadValue(int index);

        /// <summary>
        /// PayloadString is like PayloadValue(index).ToString(), however it can do a better job in some cases.  In particular
        /// if the payload is a enumeration or a bitfield and the manifest defined the enumeration values, then it will print the string name
        /// of the enumeration value instead of the integer value.  
        /// </summary>
        public virtual string PayloadString(int index, IFormatProvider formatProvider = null)
        {
            try
            {
                var value = PayloadValue(index);

                if (value == null)
                {
                    return "";
                }

                if (value is Address)
                {
                    return "0x" + ((Address)value).ToString("x8", formatProvider);
                }

                if (value is int)
                {

                    int intValue = (int)value;
                    if (intValue != 0 && payloadNames[index] == "IPv4Address")
                    {
                        return (intValue & 0xFF).ToString() + "." +
                               ((intValue >> 8) & 0xFF).ToString() + "." +
                               ((intValue >> 16) & 0xFF).ToString() + "." +
                               ((intValue >> 24) & 0xFF).ToString();
                    }
                    if (formatProvider != null)
                    {
                        return intValue.ToString(formatProvider);
                    }
                    else
                    {
                        return intValue.ToString("n0");
                    }
                }

                if (value is long)
                {
                    if (payloadNames[index] == "objectId")      // TODO this is a hack.  
                    {
                        return "0x" + ((long)value).ToString("x8");
                    }

                    if (formatProvider != null)
                    {
                        return ((long)value).ToString(formatProvider);
                    }
                    else
                    {
                        return ((long)value).ToString("n0");
                    }
                }

                if (value is double)
                {
                    if (formatProvider != null)
                    {
                        return ((double)value).ToString(formatProvider);
                    }
                    else
                    {
                        return ((double)value).ToString("n3");
                    }
                }

                if (value is DateTime)
                {
                    DateTime asDateTime = (DateTime)value;
                    string ret;
                    if (formatProvider == null && traceEventSource.SessionStartTime <= asDateTime)
                    {
                        ret = asDateTime.ToString("HH:mm:ss.ffffff");
                        ret += " (" + (asDateTime - traceEventSource.SessionStartTime).TotalMilliseconds.ToString("n3") + " MSec)";
                    }
                    else
                    {
                        ret = asDateTime.ToString(formatProvider);
                    }

                    return ret;
                }

                var asByteArray = value as byte[];
                if (asByteArray != null)
                {
                    StringBuilder sb = new StringBuilder();
                    if (payloadNames[index].EndsWith("Address") || payloadNames[index].EndsWith("Addr"))
                    {
                        if (asByteArray.Length == 16 && asByteArray[0] == 2 && asByteArray[1] == 0)         // FAMILY = 2 = IPv4
                        {
                            sb.Append(asByteArray[4].ToString()).Append('.');
                            sb.Append(asByteArray[5].ToString()).Append('.');
                            sb.Append(asByteArray[6].ToString()).Append('.');
                            sb.Append(asByteArray[7].ToString()).Append(':');
                            int port = (asByteArray[2] << 8) + asByteArray[3];
                            sb.Append(port);
                        }
                        else if (asByteArray.Length == 28 && asByteArray[0] == 23 && asByteArray[1] == 0)   // FAMILY = 23 = IPv6
                        {
                            var ipV6 = new byte[16];
                            Array.Copy(asByteArray, 8, ipV6, 0, 16);
                            int port = (asByteArray[2] << 8) + asByteArray[3];
                            sb.Append('[').Append(new System.Net.IPAddress(ipV6).ToString()).Append("]:").Append(port);
                        }
                    }
                    // If we did not find a way of pretty printing int, dump it as bytes. 
                    if (sb.Length == 0)
                    {
                        var limit = Math.Min(asByteArray.Length, 16);
                        for (int i = 0; i < limit; i++)
                        {
                            var b = asByteArray[i];
                            sb.Append(HexDigit((b / 16)));
                            sb.Append(HexDigit((b % 16)));
                        }
                        if (limit < asByteArray.Length)
                        {
                            sb.Append("...");
                        }
                    }
                    return sb.ToString();
                }
                var asArray = value as System.Array;
                if (asArray != null && asArray.Rank == 1)
                {
                    StringBuilder sb = new StringBuilder();
                    sb.Append('[');
                    bool first = true;
                    foreach (var elem in asArray)
                    {
                        if (!first)
                        {
                            sb.Append(',');
                        }

                        first = false;

                        var asStruct = elem as IDictionary<string, object>;
                        if (asStruct != null && asStruct.Count == 2 && asStruct.ContainsKey("Key") && asStruct.ContainsKey("Value"))
                        {
                            sb.Append(asStruct["Key"]).Append("->\"").Append(asStruct["Value"]).Append("\"");
                        }
                        else
                        {
                            sb.Append(elem.ToString());
                        }
                    }
                    sb.Append(']');
                    return sb.ToString();
                }

                return value.ToString();
            }
            catch (Exception e)
            {
                return "<<<EXCEPTION_DURING_VALUE_LOOKUP " + e.GetType().Name + ">>>";
            }
        }
        /// <summary>
        /// Returns the index in 'PayloadNames for field 'propertyName'.  Returns something less than 0 if not found. 
        /// </summary>
        public int PayloadIndex(string propertyName)
        {
            string[] propertyNames = PayloadNames;
            for (int i = 0; i < propertyNames.Length; i++)
            {
                if (propertyName == propertyNames[i])
                {
                    return i;
                }
            }

            return -1;
        }
        /// <summary>
        /// PayloadByName fetches the value of a payload property by the name of the property. 
        /// <para>It will return null if propertyName is not found.</para>
        /// <para>This method is not intended to be used in performance critical code.</para>
        /// </summary>
        public object PayloadByName(string propertyName)
        {
            int index = PayloadIndex(propertyName);
            if (0 <= index)
            {
                return PayloadValue(index);
            }

            return null;
        }
        /// <summary>
        /// PayloadStringByName functions the same as PayloadByName, but uses PayloadString instead of PayloadValue. 
        /// <para>It will return null if propertyName is not found.</para>
        /// <para>This method is not intended to be used in performance critical code.</para>
        /// </summary>
        public string PayloadStringByName(string propertyName, IFormatProvider formatProvider = null)
        {
            int index = PayloadIndex(propertyName);
            if (0 <= index)
            {
                return PayloadString(index, formatProvider);
            }

            return null;
        }
        // Raw payload bytes
        /// <summary>
        /// The size of the event-specific data payload.  (see EventData)
        /// <para>Normally this property is not used because some TraceEventParser has built a subclass of
        /// TraceEvent that parses the payload</para>
        /// </summary>
        public int EventDataLength
        {
            // [SecuritySafeCritical]
            get { return eventRecord->UserDataLength; }
        }
        /// <summary>
        /// Returns an array of bytes representing the event-specific payload associated with the event.  
        /// <para>Normally this method is not used because some TraceEventParser has built a subclass of
        /// TraceEvent that parses the payload</para>
        /// </summary>
        public byte[] EventData()
        {
            return EventData(null, 0, 0, EventDataLength);
        }
        /// <summary>
        /// Gets the event data and puts it in 'targetBuffer' at 'targetStartIndex' and returns the resulting buffer.
        /// If 'targetBuffer is null, it will allocate a buffer of the correct size. 
        /// <para>Normally this method is not used because some TraceEventParser has built a subclass of
        /// TraceEvent that parses the payload</para>
        /// </summary>
        public byte[] EventData(byte[] targetBuffer, int targetStartIndex, int sourceStartIndex, int length)
        {
            if (targetBuffer == null)
            {
                Debug.Assert(targetStartIndex == 0);
                targetBuffer = new byte[length + targetStartIndex];
            }
            // TODO overflow
            if (sourceStartIndex + length > EventDataLength)
            {
                throw new IndexOutOfRangeException();
            }

            IntPtr start = (IntPtr)((byte*)DataStart.ToPointer() + sourceStartIndex);
            if (length > 0)
            {
                Marshal.Copy(start, targetBuffer, targetStartIndex, length);
            }

            return targetBuffer;
        }

        /// <summary>
        /// The events passed to the callback functions only last as long as the callback, so if you need to
        /// keep the information around after that you need to copy it.   This method makes that copy.
        /// <para>This method is more expensive than copy out all the event data from the TraceEvent instance
        /// to a type of your construction.</para>
        /// </summary>
        public virtual unsafe TraceEvent Clone()
        {
            TraceEvent ret = (TraceEvent)MemberwiseClone();     // Clone myself. 
            ret.next = null;                                    // the clone is not in any linked list.  
            if (eventRecord != null)
            {
                int userDataLength = (EventDataLength + 3) / 4 * 4;            // DWORD align
                int extendedDataLength = 0;

                // We need to copy out the RelatedActivityID if it is there.  
                Guid relatedActivityID = RelatedActivityID;
                if (relatedActivityID != default(Guid))
                {
                    extendedDataLength += sizeof(Guid);
                }

                IntPtr extendedDataBuffer = Marshal.AllocHGlobal(extendedDataLength + sizeof(TraceEventNativeMethods.EVENT_RECORD) + userDataLength);
                IntPtr eventRecordBuffer = (IntPtr)(((byte*)extendedDataBuffer) + extendedDataLength);
                IntPtr userDataBuffer = (IntPtr)(((byte*)eventRecordBuffer) + sizeof(TraceEventNativeMethods.EVENT_RECORD));

                // store the related activity ID
                if (extendedDataLength != 0)
                {
                    *((Guid*)extendedDataBuffer) = relatedActivityID;
                }

                ret.myBuffer = extendedDataBuffer;
                ret.instanceContainerID = ContainerID;

                CopyBlob((IntPtr)eventRecord, eventRecordBuffer, sizeof(TraceEventNativeMethods.EVENT_RECORD));
                ret.eventRecord = (TraceEventNativeMethods.EVENT_RECORD*)eventRecordBuffer;

                CopyBlob(userData, userDataBuffer, userDataLength);
                ret.userData = userDataBuffer;
                ret.eventRecord->UserData = ret.userData;

                // we don't have extended data (we have to handle each case specially.  Related Activity ID above)
                ret.eventRecord->ExtendedDataCount = 0;
                ret.eventRecord->ExtendedData = (TraceEventNativeMethods.EVENT_HEADER_EXTENDED_DATA_ITEM*)IntPtr.Zero;
            }
            return ret;
        }
        /// <summary>
        /// Pretty print the event.  It uses XML syntax.. 
        /// </summary>
        public override string ToString()
        {
            return ToXml(new StringBuilder()).ToString();
        }

        /// <summary>
        /// Pretty print the event using XML syntax, formatting data using the supplied IFormatProvider
        /// </summary>
        public virtual string ToString(IFormatProvider formatProvider)
        {
            return ToXml(new StringBuilder(), formatProvider).ToString();
        }

        /// <summary>
        /// Write an XML representation to the stringBuilder sb and return it.  
        /// </summary>
        public virtual StringBuilder ToXml(StringBuilder sb)
        {
            return ToXml(sb, null);
        }

        /// <summary>
        /// Writes an XML representation of the event to a StringBuilder sb, formatting data using the passed format provider. 
        /// Returns the StringBuilder.
        /// </summary>
        public virtual StringBuilder ToXml(StringBuilder sb, IFormatProvider formatProvider)
        {
            Prefix(sb);
            if (ProviderGuid != Guid.Empty)
            {
                XmlAttrib(sb, "ProviderName", ProviderName);
            }

            string message = GetFormattedMessage(formatProvider);
            if (message != null)
            {
                XmlAttrib(sb, "FormattedMessage", message);
            }

            string[] payloadNames = PayloadNames;
            for (int i = 0; i < payloadNames.Length; i++)
            {
                string payloadName = payloadNames[i];

                // XML does not allow you to repeat attributes, so we need change the name if that happens.   
                // Note that this is not perfect, but avoids the likley cases
                if (payloadName == "ProviderName" || payloadName == "FormattedMessage" || payloadName == "MSec" ||
                    payloadName == "PID" || payloadName == "PName" || payloadName == "TID" || payloadName == "ActivityID")
                {
                    payloadName = "_" + payloadName;
                }

                XmlAttrib(sb, payloadName, PayloadString(i, formatProvider));
            }
            sb.Append("/>");
            return sb;
        }
        /// <summary>
        /// Dumps a very verbose description of the event, including a dump of they payload bytes. It is in
        /// XML format. This is very useful in debugging (put it in a watch window) when parsers are not
        /// interpreting payloads properly.
        /// </summary>
        public string Dump(bool includePrettyPrint = false, bool truncateDump = false)
        {
            StringBuilder sb = new StringBuilder();
            Prefix(sb);
            sb.AppendLine().Append(" ");
            XmlAttrib(sb, "TimeStamp", TimeStamp.ToString("MM/dd/yy HH:mm:ss.ffffff"));
            XmlAttrib(sb, "ID", ID);
            XmlAttrib(sb, "Version", Version);
            XmlAttribHex(sb, "Keywords", (ulong)Keywords);
            XmlAttrib(sb, "TimeStampQPC", TimeStampQPC);
            sb.Append(" QPCTime=\"").Append((1000000.0 / Source.QPCFreq).ToString("f3")).Append("us\"");
            sb.AppendLine().Append(" ");

            XmlAttrib(sb, "Level", Level);
            XmlAttrib(sb, "ProviderName", ProviderName);
            if (ProviderGuid != Guid.Empty && !ProviderName.Contains(ProviderGuid.ToString()))
            {
                XmlAttrib(sb, "ProviderGuid", ProviderGuid);
            }

            XmlAttrib(sb, "ClassicProvider", IsClassicProvider);
            XmlAttrib(sb, "ProcessorNumber", ProcessorNumber);
            sb.AppendLine().Append(" ");

#if !DOTNET_V35
            if (ActivityID != Guid.Empty || RelatedActivityID != Guid.Empty)
            {
                if (ActivityID != Guid.Empty)
                {
                    XmlAttrib(sb, "ActivityID", StartStopActivityComputer.ActivityPathString(ActivityID));
                    if (StartStopActivityComputer.IsActivityPath(ActivityID, ProcessID))        // Also print out the raw GUID
                    {
                        XmlAttrib(sb, "RawActivityID", ActivityID);
                    }
                }
                if (RelatedActivityID != Guid.Empty)
                {
                    XmlAttrib(sb, "RelatedActivityID", StartStopActivityComputer.ActivityPathString(RelatedActivityID));
                    if (StartStopActivityComputer.IsActivityPath(RelatedActivityID, ProcessID))   // Also print out the raw GUID
                    {
                        XmlAttrib(sb, "RawRelatedActivityID", RelatedActivityID);
                    }
                }
                sb.AppendLine().Append(" ");
            }
#endif

            XmlAttrib(sb, "Opcode", (int)Opcode);
            if (taskGuid != Guid.Empty)
            {
                if (!TaskName.Contains(taskGuid.ToString()))
                {
                    XmlAttrib(sb, "TaskGuid", taskGuid);
                }
            }
            else
            {
                XmlAttrib(sb, "Task", Task);
            }

            XmlAttrib(sb, "Channel", eventRecord->EventHeader.Channel);
            XmlAttrib(sb, "PointerSize", PointerSize);
            sb.AppendLine().Append(" ");
            XmlAttrib(sb, "CPU", ProcessorNumber);
            XmlAttrib(sb, "EventIndex", EventIndex);
            XmlAttrib(sb, "TemplateType", GetType().Name);
            sb.Append('>').AppendLine();

            if (includePrettyPrint && !(this is UnhandledTraceEvent))
            {
                sb.AppendLine("  <PrettyPrint>");
                try
                {
                    sb.Append("    ").Append(ToString().Replace("\n", "\n    ")).AppendLine();
                }
                catch (Exception)
                {
                    sb.AppendLine().AppendLine(" Exception thrown during PrettyPrinting");
                }
                sb.AppendLine("  </PrettyPrint>");
            }

            byte[] data = EventData();
            sb.Append("  <Payload");
            XmlAttrib(sb, "Length", EventDataLength).Append(">").AppendLine();

            StringWriter dumpSw = new StringWriter();
            int len = data.Length;
            DumpBytes(data, len, dumpSw, "    ", truncateDump ? 256 : int.MaxValue);
            sb.Append(XmlUtilities.XmlEscape(dumpSw.ToString(), false));

            sb.AppendLine("  </Payload>");
            sb.Append("</Event>");
            return sb.ToString();
        }

        /// <summary>
        /// EventTypeUserData is a field users get to use to attach their own data on a per-event-type basis.    
        /// </summary>
        public object EventTypeUserData;

        /// <summary>
        /// Returns the raw IntPtr pointer to the data blob associated with the event.  This is the way the
        /// subclasses of TraceEvent get at the data to display it in a efficient (but unsafe) manner.  
        /// </summary>
        public IntPtr DataStart { get { return userData; } }

        #region Protected
        /// <summary>
        /// Create a template with the given event meta-data.  Used by TraceParserGen.  
        /// </summary>
        protected TraceEvent(int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
        {

            Debug.Assert((ushort)eventID == eventID);
            this.eventID = (TraceEventID)eventID;
            this.task = (TraceEventTask)task;
            this.taskName = taskName;
            this.taskGuid = taskGuid;
            Debug.Assert((byte)opcode == opcode);
            this.opcode = (TraceEventOpcode)opcode;
            this.opcodeName = opcodeName;
            this.providerGuid = providerGuid;
            this.providerName = providerName;
            ParentThread = -1;
            if (this.eventID == TraceEventID.Illegal)
            {
                lookupAsClassic = true;
            }
        }

        /// <summary>
        /// Skip UTF8 string starting at 'offset' bytes into the payload blob.
        /// </summary>  
        /// <returns>Offset just after the string</returns>
        protected internal int SkipUTF8String(int offset)
        {
            IntPtr mofData = DataStart;
            while (TraceEventRawReaders.ReadByte(mofData, offset) != 0)
            {
                offset++;
            }

            offset++;
            return offset;
        }
        /// <summary>
        /// Skip Unicode string starting at 'offset' bytes into the payload blob.
        /// </summary>  
        /// <returns>Offset just after the string</returns>
        protected internal int SkipUnicodeString(int offset)
        {
            IntPtr mofData = DataStart;
            while (TraceEventRawReaders.ReadInt16(mofData, offset) != 0)
            {
                offset += 2;
            }

            offset += 2;
            return offset;
        }
        /// <summary>
        /// Skip 'stringCount' Unicode strings starting at 'offset' bytes into the payload blob.
        /// </summary>  
        /// <returns>Offset just after the last string</returns>
        protected internal int SkipUnicodeString(int offset, int stringCount)
        {
            while (stringCount > 0)
            {
                offset = SkipUnicodeString(offset);
                --stringCount;
            }
            return offset;
        }
        /// <summary>
        /// Skip a Security ID (SID) starting at 'offset' bytes into the payload blob.
        /// </summary>  
        /// <returns>Offset just after the Security ID</returns>
        internal int SkipSID(int offset)
        {
            IntPtr mofData = DataStart;
            // This is a Security Token.  Either it is null, which takes 4 bytes, 
            // Otherwise it is an 8 byte structure (TOKEN_USER) followed by SID, which is variable
            // size (sigh) depending on the 2nd byte in the SID
            int sid = TraceEventRawReaders.ReadInt32(mofData, offset);
            if (sid == 0)
            {
                return offset + 4;      // TODO confirm 
            }
            else
            {
                int tokenSize = HostOffset(8, 2);
                int numAuthorities = TraceEventRawReaders.ReadByte(mofData, offset + (tokenSize + 1));
                return offset + tokenSize + 8 + 4 * numAuthorities;
            }
        }

        /// <summary>
        /// Trivial helper that allows you to get the Offset of a field independent of 32 vs 64 bit pointer size.
        /// </summary>
        /// <param name="offset">The Offset as it would be on a 32 bit system</param>
        /// <param name="numPointers">The number of pointer-sized fields that came before this field.
        /// </param>
        protected internal int HostOffset(int offset, int numPointers)
        {
            return offset + (PointerSize - 4) * numPointers;
        }
        /// <summary>
        /// Computes the size of 'numPointers' pointers on the machine where the event was collected.  
        /// </summary>
        internal int HostSizePtr(int numPointers)
        {
            return PointerSize * numPointers;
        }
        /// <summary>
        /// Given an Offset to a null terminated ASCII string in an event blob, return the string that is
        /// held there.   
        /// </summary>
        protected internal string GetUTF8StringAt(int offset)
        {
            if (offset >= EventDataLength)
            {
                Debug.Assert(false, "Read past end of string");
                return "<<ERROR EOB>>";
            }
            else
            {
                return TraceEventRawReaders.ReadUTF8String(DataStart, offset, EventDataLength);
            }
        }
        /// <summary>
        /// Returns the string represented by a fixed length ASCII string starting at 'offset' of length 'charCount'
        /// </summary>
        internal string GetFixedAnsiStringAt(int charCount, int offset)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < charCount; i++)
            {
                char c = (char)GetByteAt(offset + i);
                if (c == 0)
                {
                    break;
                }
#if DEBUG
                // TODO review. 
                if ((c < ' ' || c > '~') && !char.IsWhiteSpace(c))
                {
                    Debug.WriteLine("Warning: Found unprintable chars in string truncating to " + sb.ToString());
                    break;
                }
#endif
                sb.Append(c);
            }
            return sb.ToString();
        }
        /// <summary>
        /// Given an Offset to a fixed sized string at 'offset', whose buffer size is 'charCount'
        /// Returns the string value.  A null in the string will terminate the string before the
        /// end of the buffer. 
        /// </summary>        
        internal string GetFixedUnicodeStringAt(int charCount, int offset)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < charCount; i++)
            {
                char c = (char)GetInt16At(offset + i * 2);
                if (c == 0)
                {
                    break;
                }
#if DEBUG
                // TODO review. 
                if ((c < ' ' || c > '~') && !char.IsWhiteSpace(c))
                {
                    Debug.WriteLine("Warning: Found unprintable chars in string truncating to " + sb.ToString());
                    break;
                }
#endif
                sb.Append(c);
            }
            return sb.ToString();
        }
        /// <summary>
        /// Returns the encoding of a Version 6 IP address that has been serialized at 'offset' in the payload bytes.  
        /// </summary>
        internal System.Net.IPAddress GetIPAddrV6At(int offset)
        {
            byte[] addrBytes = new byte[16];
            for (int i = 0; i < addrBytes.Length; i++)
            {
                addrBytes[i] = TraceEventRawReaders.ReadByte(DataStart, offset + i);
            }

            return new System.Net.IPAddress(addrBytes);
        }
        /// <summary>
        /// Returns the GUID serialized at 'offset' in the payload bytes. 
        /// </summary>
        protected internal Guid GetGuidAt(int offset)
        {
            return TraceEventRawReaders.ReadGuid(DataStart, offset);
        }
        /// <summary>
        /// Get the DateTime that serialized (as a windows FILETIME) at 'offset' in the payload bytes. 
        /// </summary>
        protected internal DateTime GetDateTimeAt(int offset)
        {
            return DateTime.FromFileTime(GetInt64At(offset));
        }
        /// <summary>
        /// Given an Offset to a null terminated Unicode string in an payload bytes, return the string that is
        /// held there.   
        /// </summary>
        protected internal string GetUnicodeStringAt(int offset)
        {
            if (offset >= EventDataLength)
            {
                throw new Exception("Reading past end of event");
            }
            else
            {
                return TraceEventRawReaders.ReadUnicodeString(DataStart, offset, EventDataLength);
            }
        }
        /// <summary>
        /// Give an offset to a byte array of size 'size' in the payload bytes, return a byte[] that contains
        /// those bytes.
        /// </summary>
        protected internal byte[] GetByteArrayAt(int offset, int size)
        {
            byte[] res = new byte[size];
            for (int i = 0; i < size; i++)
            {
                res[i] = (byte)GetByteAt(offset + i);
            }
            return res;
        }
        /// <summary>
        /// Returns a byte value that was serialized at 'offset' in the payload bytes
        /// </summary>
        protected internal int GetByteAt(int offset)
        {
            return TraceEventRawReaders.ReadByte(DataStart, offset);
        }
        /// <summary>
        /// Returns a short value that was serialized at 'offset' in the payload bytes
        /// </summary>
        protected internal int GetInt16At(int offset)
        {
            return TraceEventRawReaders.ReadInt16(DataStart, offset);
        }
        /// <summary>
        /// Returns an int value that was serialized at 'offset' in the payload bytes
        /// </summary>
        protected internal int GetInt32At(int offset)
        {
            return TraceEventRawReaders.ReadInt32(DataStart, offset);
        }
        /// <summary>
        /// Returns a long value that was serialized at 'offset' in the payload bytes
        /// </summary>
        protected internal long GetInt64At(int offset)
        {
            return TraceEventRawReaders.ReadInt64(DataStart, offset);
        }
        /// <summary>
        /// Get something that is machine word sized for the provider that collected the data, but is an
        /// integer (and not an address)
        /// </summary>
        protected internal long GetIntPtrAt(int offset)
        {
            Debug.Assert(PointerSize == 4 || PointerSize == 8);
            if (PointerSize == 4)
            {
                return (long)(uint)GetInt32At(offset);
            }
            else
            {
                return GetInt64At(offset);
            }
        }
        /// <summary>
        /// Gets something that is pointer sized for the provider that collected the data.  
        /// </summary>
        protected internal Address GetAddressAt(int offset)
        {
            return (Address)GetIntPtrAt(offset);
        }

        /// <summary>
        /// Returns an int float (single) that was serialized at 'offset' in the payload bytes
        /// </summary>
        protected internal float GetSingleAt(int offset)
        {
            return TraceEventRawReaders.ReadSingle(DataStart, offset);
        }
        /// <summary>
        /// Returns an int double precision floating point value that was serialized at 'offset' in the payload bytes
        /// </summary>
        protected internal double GetDoubleAt(int offset)
        {
            return TraceEventRawReaders.ReadDouble(DataStart, offset);
        }

        /// <summary>
        /// Write the XML attribute 'attribName' with value 'value' to the string builder
        /// </summary>
        protected internal static StringBuilder XmlAttrib(StringBuilder sb, string attribName, string value)
        {
            return XmlAttribPrefix(sb, attribName).Append(XmlUtilities.XmlEscape(value, false)).Append('"');
        }
        /// <summary>
        /// Write the XML attribute 'attribName' with value 'value' to the string builder
        /// </summary>
        protected internal static StringBuilder XmlAttrib(StringBuilder sb, string attribName, int value)
        {
            return XmlAttribPrefix(sb, attribName).Append(value.ToString("n0")).Append('"');
        }
        /// <summary>
        /// Write the XML attribute 'attribName' with value 'value' to the string builder
        /// </summary>
        protected internal static StringBuilder XmlAttrib(StringBuilder sb, string attribName, long value)
        {
            return XmlAttribPrefix(sb, attribName).Append(value.ToString("n0")).Append('"');
        }
        /// <summary>
        /// Write the XML attribute 'attribName' with value 'value' to the string builder
        /// </summary>
        protected internal static StringBuilder XmlAttribHex(StringBuilder sb, string attribName, ulong value)
        {
            XmlAttribPrefix(sb, attribName);
            sb.Append('0').Append('x');
            uint intValue = (uint)(value >> 32);
            for (int i = 0; i < 2; i++)
            {
                if (i != 0 || intValue != 0)
                {
                    for (int j = 28; j >= 0; j -= 4)
                    {
                        uint digit = (intValue >> j) & 0xF;
                        uint charDigit = ('0' + digit);
                        if (charDigit > '9')
                        {
                            charDigit += ('A' - '9' - 1);
                        }

                        sb.Append((char)charDigit);
                    }
                }
                intValue = (uint)value;
            }
            sb.Append('"');
            return sb;
        }
        /// <summary>
        /// Write the XML attribute 'attribName' with value 'value' to the string builder
        /// </summary>
        protected internal static StringBuilder XmlAttribHex(StringBuilder sb, string attribName, long value)
        {
            return XmlAttribHex(sb, attribName, (ulong)value);
        }
        /// <summary>
        /// Write the XML attribute 'attribName' with value 'value' to the string builder
        /// </summary>
        protected internal static StringBuilder XmlAttribHex(StringBuilder sb, string attribName, uint value)
        {
            return XmlAttribHex(sb, attribName, (ulong)value);
        }
        /// <summary>
        /// Write the XML attribute 'attribName' with value 'value' to the string builder
        /// </summary>
        protected internal static StringBuilder XmlAttribHex(StringBuilder sb, string attribName, int value)
        {
            return XmlAttribHex(sb, attribName, (ulong)value);
        }
        /// <summary>
        /// Write the XML attribute 'attribName' with value 'value' to the string builder
        /// </summary>
        protected internal static StringBuilder XmlAttrib(StringBuilder sb, string attribName, object value)
        {
            if (value is Address)
            {
                return XmlAttribHex(sb, attribName, (Address)value);
            }

            return XmlAttrib(sb, attribName, value.ToString());
        }

        private static StringBuilder XmlAttribPrefix(StringBuilder sb, string attribName)
        {
            sb.Append(' ').Append(attribName).Append('=').Append('"');
            return sb;
        }

        /// <summary>
        /// Prints a standard prefix for a event (includes the time of the event, the process ID and the
        /// thread ID.  
        /// </summary>
        protected internal StringBuilder Prefix(StringBuilder sb)
        {
            sb.Append("<Event MSec="); QuotePadLeft(sb, TimeStampRelativeMSec.ToString("f4"), 13);
            sb.Append(" PID="); QuotePadLeft(sb, ProcessID.ToString(), 6);
            sb.Append(" PName="); QuotePadLeft(sb, ProcessName, 10);
            sb.Append(" TID="); QuotePadLeft(sb, ThreadID.ToString(), 6);
            if (ActivityID != Guid.Empty)
            {
                sb.AppendFormat(" ActivityID=\"{0:n}\"", ActivityID);
            }

            sb.Append(" EventName=\"").Append(EventName).Append('"');
            return sb;
        }

        // If non-null, when reading from ETL files, call this routine to fix poorly formed Event headers.  
        // Ideally this would not be needed, and is never used on ETLX files.
        internal /*protected*/ bool NeedsFixup;
        internal /*protected*/ virtual void FixupData() { }

        internal /*protected*/ int ParentThread;

        /// <summary>
        /// Because we want the ThreadID to be the ID of the CREATED thread, and the stack 
        /// associated with the event is the parentThreadID 
        /// </summary>
        internal int ThreadIDforStacks()
        {
#if !NOT_WINDOWS 
            if (0 <= ParentThread)
            {
                Debug.Assert(this is ProcessTraceData || this is ThreadTraceData);
                return ParentThread;
            }
#endif
            return ThreadID;
        }

#if DEBUG
        internal bool DisallowEventIndexAccess { get; set; }
#endif

        /// <summary>
        /// Returns (or sets) the delegate associated with this event.   
        /// </summary>
        protected internal abstract Delegate Target { get; set; }

        /// <summary>
        /// If this TraceEvent belongs to a parser that needs state, then this callback will set the state.  
        /// Parsers with state are reasonably rare, the main examples are KernelTraceEventParser and ClrTraceEventParser.    
        /// </summary>
        protected internal virtual void SetState(object state) { }

        #endregion
        #region Private
        private static char HexDigit(int digit)
        {
            if (digit < 10)
            {
                return (char)('0' + digit);
            }
            else
            {
                return (char)('A' - 10 + digit);
            }
        }
        /// <summary>
        /// Returns the Timestamp for the event using Query Performance Counter (QPC) ticks.   
        /// The start time for the QPC tick counter is arbitrary and the units  also vary.  
        /// </summary>
        [Obsolete("Not Obsolete but Discouraged.  Please use TimeStampRelativeMSec.")]
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public long TimeStampQPC { get { return eventRecord->EventHeader.TimeStamp; } }

        /// <summary>
        /// A standard way for events to are that certain addresses are addresses in code and ideally have
        /// symbolic information associated with them.  Returns true if successful.  
        /// </summary>
        internal virtual bool LogCodeAddresses(Func<TraceEvent, Address, bool> callBack)
        {
            return true;
        }

        /// <summary>
        /// Was this written with the windows EventWriteString API? (see also EventDataAsString)
        /// </summary>
        internal bool IsEventWriteString
        {
            // [SecuritySafeCritical]
            get { return (eventRecord->EventHeader.Flags & TraceEventNativeMethods.EVENT_HEADER_FLAG_STRING_ONLY) != 0; }
        }

        /// <summary>
        /// Used for binary searching of event IDs.    Abstracts the size (currently a int, could go to long) 
        /// </summary>
        internal static int Compare(EventIndex id1, EventIndex id2)
        {
            return (int)id1 - (int)id2;
        }

        /// <summary>
        /// Returns true if the two traceEvents have the same identity.  
        /// </summary>
        internal bool Matches(TraceEvent other)
        {
            if (lookupAsClassic != other.lookupAsClassic)
            {
                return false;
            }

            if (lookupAsWPP != other.lookupAsWPP)
            {
                return false;
            }

            if (lookupAsClassic)
            {
                return taskGuid == other.taskGuid && Opcode == other.Opcode;
            }

            if (lookupAsWPP)
            {
                return taskGuid == other.taskGuid && ID == other.ID;
            }
            else
            {
                return providerGuid == other.providerGuid && ID == other.ID;
            }
        }


        /// <summary>
        /// Normally TraceEvent does not have unmanaged data, but if you call 'Clone' it will.  
        /// </summary>
        ~TraceEvent()
        {
            // Most Data does not own its data, so this is usually a no-op. 

            if (myBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(myBuffer);
            }
        }

        /// <summary>
        /// For debugging. dumps an array.   If you specify a size of 0 (the default) it dumps the whole array.  
        /// </summary>
        internal static string DumpArray(byte[] bytes, int size = 0)
        {
            if (size == 0)
            {
                size = bytes.Length;
            }

            StringWriter sw = new StringWriter();
            DumpBytes(bytes, size, sw, "");

            return sw.ToString();
        }

        internal static void DumpBytes(byte[] bytes, int length, TextWriter output, string indent, int startTruncate = int.MaxValue)
        {
            startTruncate &= ~0xF;  // Make a multiple of 16

            int rowStartIdx = 0;
            while (rowStartIdx < length)
            {
                // if we have asked for truncation and we have written 4 rows, skip to the last 4 rows.  
                if (rowStartIdx == startTruncate)
                {
                    var afterSkipIdx = (length - startTruncate + 15) & ~0xF;  // round down to the next row of 15
                    if (rowStartIdx < afterSkipIdx)
                    {
                        output.Write(indent);
                        output.WriteLine("...");
                        rowStartIdx = afterSkipIdx;
                    }
                }
                output.Write(indent);
                output.Write("{0,4:x}:  ", rowStartIdx);
                for (int i = 0; i < 16; i++)
                {
                    if (i == 8)
                    {
                        output.Write("| ");
                    }

                    if (i + rowStartIdx < length)
                    {
                        output.Write("{0,2:x} ", bytes[i + rowStartIdx]);
                    }
                    else
                    {
                        output.Write("   ");
                    }
                }
                output.Write("  ");
                for (int i = 0; i < 16; i++)
                {
                    if (i == 8)
                    {
                        output.Write(" ");
                    }

                    if (i + rowStartIdx >= length)
                    {
                        break;
                    }

                    byte val = bytes[i + rowStartIdx];
                    if (32 <= val && val < 128)
                    {
                        output.Write((Char)val);
                    }
                    else
                    {
                        output.Write(".");
                    }
                }
                output.WriteLine();
                rowStartIdx += 16;
            }
        }
        internal static unsafe void CopyBlob(IntPtr source, IntPtr destination, int byteCount)
        {
            // TODO: currently most uses the source aligned so
            // I don't bother trying to insure that the copy is aligned.
            // Consider moving to Buffer.MemoryCopy
            Debug.Assert((long)destination % 4 == 0);
            Debug.Assert(byteCount % 4 == 0);
            int* sourcePtr = (int*)source;
            int* destationPtr = (int*)destination;
            int intCount = byteCount >> 2;
            while (intCount > 0)
            {
                *destationPtr++ = *sourcePtr++;
                --intCount;
            }
        }

        internal static void QuotePadLeft(StringBuilder sb, string str, int totalSize)
        {
            int spaces = totalSize - 2 - str.Length;
            if (spaces > 0)
            {
                sb.Append(' ', spaces);
            }

            sb.Append('"').Append(str).Append('"');
        }
        private static string ToString(TraceEventOpcode opcode)
        {
            switch (opcode)
            {
                case TraceEventOpcode.Info: return "Info";
                case TraceEventOpcode.Start: return "Start";
                case TraceEventOpcode.Stop: return "Stop";
                case TraceEventOpcode.DataCollectionStart: return "DCStart";
                case TraceEventOpcode.DataCollectionStop: return "DCStop";
                case TraceEventOpcode.Extension: return "Extension";
                case TraceEventOpcode.Reply: return "Reply";
                case TraceEventOpcode.Resume: return "Resume";
                case TraceEventOpcode.Suspend: return "Suspend";
                case TraceEventOpcode.Transfer: return "Send";
                default: return "Opcode(" + ((int)opcode).ToString() + ")";
            }
        }

        /// <summary>
        ///  If the event data looks like a unicode string, then return it.  This is heuristic.  (See also IsEventWriteString)
        /// </summary>
        /// <returns></returns>
        internal unsafe string EventDataAsString()
        {
            if (EventDataLength % 2 != 0)
            {
                return null;
            }

            int numChars = EventDataLength / 2;
            if (numChars < 4)
            {
                return null;
            }

            char* ptr = (char*)DataStart;
            if (ptr[numChars - 1] != '\0')          // Needs to be null terminated. 
            {
                return null;
            }

            for (int i = 0; i < numChars - 1; i++)  // Rest need to be printable ASCII chars.  
            {
                char c = ptr[i];
                if (!((' ' <= c && c <= '~') || c == '\n' || c == '\r'))
                {
                    return null;
                }
            }

            return TraceEventRawReaders.ReadUnicodeString(DataStart, 0, EventDataLength);
        }

        /// <summary>
        /// Each TraceEvent items knows where it should Dispatch to.
        /// ETWTraceEventSource.Dispatch calls this function to go to the right placed. By default we
        /// do nothing. Typically a subclass just dispatches to another callback that passes itself to a
        /// type-specific event callback.
        /// </summary>
        protected internal virtual void Dispatch()
        {
            Debug.Assert(false, "Dispatching through base class!");
        }

        /// <summary>
        /// This is a DEBUG-ONLY routine that allows a routine to do consistency checking in a debug build.  
        /// </summary>
        protected internal virtual void Validate()
        {
        }

        /// <summary>
        /// Validate that the events is not trash.  
        /// </summary>
        [Conditional("DEBUG")]
        protected internal void DebugValidate()
        {
            Validate();
        }

        // Note that you can't use the ExtendedData, UserData or UserContext fields, they are not set
        // properly in all cases.  
        internal TraceEventNativeMethods.EVENT_RECORD* eventRecord; // points at the record data itself.  (fixed size)
        internal IntPtr userData;                                   // The event-specific payload.  

        /// <summary>
        /// TraceEvent knows where to dispatch to. To support many subscriptions to the same event we chain
        /// them.
        /// </summary>
        internal TraceEvent next;
        // If true we are using TaskGuid and Opcode
        // If False we are using ProviderGuid and EventId
        internal bool lookupAsClassic;          // Use the TaskGuid and Opcode to look things up
        internal bool lookupAsWPP;              // Variation on classic where you lookup on TaskGuid and EventID
        internal bool containsSelfDescribingMetadata;

        // These are constant over the TraceEvent's lifetime (after setup) (except for the UnhandledTraceEvent
        internal TraceEventID eventID;                  // The ID you should switch on.  
        internal /*protected*/ TraceEventOpcode opcode;
        internal /*protected*/ string opcodeName;
        internal /*protected*/ TraceEventTask task;
        internal /*protected*/ string taskName;
        internal /*protected*/ Guid taskGuid;
        internal /*protected*/ Guid providerGuid;
        internal /*protected*/ string providerName;
        internal /*protected*/ bool eventNameIsJustTaskName;
        internal /*protected*/ string eventName;

        /// <summary>
        /// The array of names for each property in the payload (in order).  
        /// </summary>
        protected internal string[] payloadNames;
        internal TraceEventSource traceEventSource;
        internal EventIndex eventIndex;               // something that uniquely identifies this event in the stream.  
        internal IntPtr myBuffer;                     // If the raw data is owned by this instance, this points at it.  Normally null.
        internal string instanceContainerID;          // If the raw data is owned by this instance (e.g. the event has been cloned), then if there is a container ID it will be saved here.  Normally null.
        #endregion
    }

    /// <summary>
    /// Individual event providers can supply many different types of events.  These are distinguished from each
    /// other by a TraceEventID, which is just a 16 bit number.  Its meaning is provider-specific.  
    /// </summary>
    public enum TraceEventID : ushort
    {
        /// <summary>
        /// Illegal is a EventID that is not used by a normal event.   
        /// </summary>
        Illegal = 0xFFFF,
    }

    /// <summary>
    /// Providers can define different audiences or Channels for an event (eg Admin, Developer ...).
    /// It is only used for Windows Event log support.  
    /// </summary>
    public enum TraceEventChannel : byte
    {
        /// <summary>
        /// The default channel.
        /// </summary>
        Default = 0
    }

    /// <summary>
    /// There are certain classes of events (like start and stop) which are common across a broad variety of
    /// event providers for which it is useful to treat uniformly (for example, determining the elapsed time
    /// between a start and stop event).  To facilitate this, event can have opcode which defines these
    /// common operations.  Below are the standard ones but providers can define additional ones.
    /// </summary>
    public enum TraceEventOpcode : byte
    {
        /// <summary>
        /// Generic opcode that does not have specific semantics associated with it. 
        /// </summary>
        Info = 0,
        /// <summary>
        /// The entity (process, thread, ...) is starting
        /// </summary>
        Start = 1,
        /// <summary>
        /// The entity (process, thread, ...) is stoping (ending)
        /// </summary>
        Stop = 2,
        /// <summary>
        /// The entity (process, thread, ...) did not terminate before data collection ended, so indicate
        /// this at data collection termination time.
        /// </summary>
        DataCollectionStart = 3,
        /// <summary>
        /// The entity (process, thread, ...) did not terminate before data collection ended, so indicate
        /// this at data collection termination time. This is mostly for 'flight recorder' scenarios where
        /// you only have the 'tail' of the data and would like to know about everything that existed. 
        /// </summary>
        DataCollectionStop = 4,
        /// <summary>
        /// Reserved
        /// </summary>
        Extension = 5,
        /// <summary>
        /// Reserved
        /// </summary>
        Reply = 6,
        /// <summary>
        /// Reserved
        /// </summary>
        Resume = 7,
        /// <summary>
        /// Reserved
        /// </summary>
        Suspend = 8,
        /// <summary>
        /// Reserved
        /// </summary>
        Transfer = 9,
        // Receive = 240,
        // 255 is used as in 'illegal opcode' and signifies a WPP style event.  These events 
        // use the event ID and the TASK Guid as their lookup key.  
    };

    /// <summary>
    /// Indicates to a provider whether verbose events should be logged.  
    /// </summary>
    public enum TraceEventLevel
    {
        /// <summary>
        /// Always log the event (It also can mean that the provider decides the verbosity)  You probably should not use it....
        /// </summary>
        Always = 0,
        /// <summary>
        /// Events that indicate critical conditions
        /// </summary>
        Critical = 1,
        /// <summary>
        /// Events that indicate error conditions
        /// </summary>
        Error = 2,
        /// <summary>
        /// Events that indicate warning conditions
        /// </summary>
        Warning = 3,
        /// <summary>
        /// Events that indicate information
        /// </summary>
        Informational = 4,
        /// <summary>
        /// Events that verbose information
        /// </summary>
        Verbose = 5,
    };

    /// <summary>
    /// ETW defines the concept of a Keyword, which is a 64 bit bitfield. Each bit in the bitfield
    /// represents some provider defined 'area' that is useful for filtering. When processing the events, it
    /// is then possible to filter based on whether various bits in the bitfield are set.  There are some
    /// standard keywords, but most are provider specific. 
    /// </summary>
    [Flags]
    public enum TraceEventKeyword : long
    {
        /// <summary>
        /// No event groups (keywords) selected
        /// </summary>
        None = 0L,

        /* The top 16 bits are reserved for system use (TODO define them) */

        /// <summary>
        /// All event groups (keywords) selected
        /// </summary>
        All = -1,
    }

    /// <summary>
    /// Tasks are groups of related events for a given provider (for example Process, or Thread, Kernel Provider).  
    /// They are defined by the provider.  
    /// </summary>
    public enum TraceEventTask : ushort
    {
        /// <summary>
        /// If you don't explicitly choose a task you get the default 
        /// </summary>
        Default = 0
    }

    /// <summary>
    /// EventIdex is a unsigned integer that is unique to a particular event. EventIndex is guaranteed to be 
    /// unique over the whole log.  It is only used by ETLX files.  
    /// <para>
    /// Currently the event ID simply the index in the log file of the event.  We don't however guarantee ordering.
    /// In the future we may add new events to the log and given them IDs 'at the end' even if the events are not
    /// at the end chronologically.  
    /// </para>
    /// <para>
    /// EventIndex is a 32 bit number limits it to 4Gig events in an ETLX file.  
    /// </para>
    /// </summary>
    public enum EventIndex : uint
    {
        /// <summary>
        /// Invalid is an EventIndex that will not be used by a normal event. 
        /// </summary>
        Invalid = unchecked((uint)-1)
    };

    /// <summary>
    /// TraceEventSource has two roles.  The first is the obvious one of providing some properties
    /// like 'SessionStartTime' for clients.  The other role is provide an interface for TraceEventParsers
    /// to 'hook' to so that events can be decoded.  ITraceParserServices is the API service for this
    /// second role.  It provides the methods that parsers register templates for subclasses of 
    /// the TraceEvent class that know how to parse particular events.   
    /// </summary>
    public interface ITraceParserServices
    {
        /// <summary>
        /// RegisterEventTemplate is the mechanism a particular event payload description 'template' 
        /// (a subclass of TraceEvent) is injected into the event processing stream. Once registered, an
        /// event is 'parsed' simply by setting the 'rawData' field in the event. It is up to the template
        /// then to take this raw data an present it in a useful way to the user (via properties). Note that
        /// parsing is thus 'lazy' in no processing of the raw data is not done at event dispatch time but
        /// only when the properties of an event are accessed.
        /// 
        /// Ownership of the template transfers when this call is made.   The source will modify this and
        /// assumes it has exclusive use (thus you should clone the template if necessary).  
        /// <para>
        /// Another important aspect is that templates are reused by TraceEventSource aggressively. The
        /// expectation is that no memory needs to be allocated during a normal dispatch 
        /// </para>
        /// </summary>
        void RegisterEventTemplate(TraceEvent template);

        /// <summary>
        /// UnregisterEventTemplate undoes the action of RegisterEventTemplate.   Logically you would 
        /// pass the template to unregister, but typically you don't have that at unregistration time.
        /// To avoid forcing clients to remember the templates they registered, UnregisterEventTemplate
        /// takes three things that will uniquely identify the template to unregister.   These are
        /// the eventID, and provider ID and the Action (callback) for the template.  
        /// </summary>
        void UnregisterEventTemplate(Delegate action, int eventId, Guid providerGuid);

        /// <summary>
        /// It is expected that when a subclass of TraceEventParser is created, it calls this
        /// method on the source.  This allows the source to do any Parser-specific initialization.  
        /// </summary>
        void RegisterParser(TraceEventParser parser);

        // TODO should have an UnRegisterParser(TraceEventParser parser) API.  

        /// <summary>
        /// Indicates that this callback should be called on any unhandled event.   The callback
        /// returns true if the lookup should be retried after calling this (that is there is
        /// the unhandled event was found).  
        /// </summary>
        void RegisterUnhandledEvent(Func<TraceEvent, bool> callback);
        // TODO Add an unregister API.  

        /// <summary>
        /// Looks if any provider has registered an event with task with 'taskGuid'. Will return null if
        /// there is no registered event.
        /// </summary>
        string TaskNameForGuid(Guid taskGuid);
        /// <summary>
        /// Looks if any provider has registered with the given GUID OR has registered any task that matches
        /// the GUID. Will return null if there is no registered event.
        /// </summary>
        string ProviderNameForGuid(Guid taskOrProviderGuid);
    }

    /// <summary>
    /// TraceEventParser Represents a class that knows how to decode particular set of events (typically
    /// all the events of a single ETW provider).  It is expected that subclasses of TraceEventParser 
    /// have a constructor that takes a TraceEventSource as an argument that 'attaches' th parser 
    /// to the TraceEventSource.  TraceEventParsers break into two groups.
    /// <para>
    /// * Those that work on a single provider, and thus the provider name is implicit in th parser.  This is the common case.
    /// The AddCallbackForEvent* methods are meant to be used for these TraceEventParsers</para>
    /// <para>
    /// * Those that work on multiple providers.  There are only a handful of these (DynamicTraceEventParser, ...). 
    /// The AddCallbackForProviderEvent* methods which take 'Provider' parameters are meant to be used for these TraceEventParsers
    /// </para>
    /// <para>
    /// In addition to the AddCallback* methods on TraceEventParser, there are also Observe* extension methods that
    /// provide callbacks using the IObservable style.  
    /// </para>
    /// </summary>
    public abstract class TraceEventParser
    {
        /// <summary>
        /// Get the source this TraceEventParser is attached to. 
        /// </summary>
        public TraceEventSource Source { get { return (TraceEventSource)source; } }

        /// <summary>
        /// Subscribe to all the events this parser can parse.  It is shorthand for AddCallback{TraceEvent}(value)/RemoveCallback(value)
        /// </summary>
        public virtual event Action<TraceEvent> All
        {
            add
            {
                AddCallbackForProviderEvents(null, value);
            }
            remove
            {
                RemoveCallback(value);
            }
        }

        // subscribe to a single event for parsers that handle a single provider 
        /// <summary>
        /// A shortcut that adds 'callback' in the provider associated with this parser (ProvderName) and an event name 'eventName'.  'eventName'
        /// can be null in which case any event that matches 'Action{T}' will call the callback.    
        /// 'eventName is of the form 'TaskName/OpcodeName'   if the event has a non-trivial opcode, otherwise it is 'TaskName'.   
        /// <para>
        /// The callback alone is used as the subscription id for unregistration, so the callback delegate should be unique (by delegate comparison)
        /// </para>
        /// </summary>
        public void AddCallbackForEvent<T>(string eventName, Action<T> callback) where T : TraceEvent
        {
            if (eventName == null)
            {
                AddCallbackForEvents<T>(callback: callback);
            }
            else
            {
                AddCallbackForEvents<T>(s => eventName == s, callback: callback);
            }
        }
        // subscribe to a set of events for parsers that handle a single provider.  (These are more strongly typed to provider specific payloads)
        /// <summary>
        /// Causes 'callback' to be called for any event in the provider associated with this parser (ProviderName) whose type is compatible with T and 
        /// whose eventName will pass 'eventNameFilter'.    
        /// </summary>
        public virtual void AddCallbackForEvents<T>(Action<T> callback) where T : TraceEvent
        {
            AddCallbackForEvents<T>(null, null, callback);
        }
        /// <summary>
        /// Causes 'callback' to be called for any event in the provider associated with this parser (ProviderName) whose type is compatible with T and 
        /// whose eventName will pass 'eventNameFilter'.    The eventNameFilter parameter can be null, in which case all events that are compatible 
        /// with T will be selected. 
        /// 
        /// eventNames passed to the filer are of the form 'TaskName/OpcodeName'   if the event has a non-trivial opcode, otherwise it is 'TaskName'.  
        /// </summary>
        public virtual void AddCallbackForEvents<T>(Predicate<string> eventNameFilter, Action<T> callback) where T : TraceEvent
        {
            AddCallbackForEvents<T>(eventNameFilter, null, callback);
        }

        /// <summary>
        /// Causes 'callback' to be called for any event in the provider associated with this parser (ProviderName) whose type is compatible with T and 
        /// whose eventName will pass 'eventNameFilter'.    The eventNameFilter parameter can be null, in which case all events that are compatible 
        /// with T will be selected.  
        /// <para>
        /// A 'subscriptionID' can be passed and this value along with the callback can be used
        /// to uniquely identify subscription to remove using the 'RemoveCallback' API.   If null is passed, then only the identity of the callback can
        /// be used to identify the subscription to remove.  
        /// 
        /// eventNames passed to the filer are of the form 'TaskName/OpcodeName'   if the event has a non-trivial opcode, otherwise it is 'TaskName'. 
        /// </para>        
        /// </summary>
        public void AddCallbackForEvents<T>(Predicate<string> eventNameFilter, object subscriptionId, Action<T> callback) where T : TraceEvent
        {
            if (GetProviderName() == null)
            {
                throw new InvalidOperationException("Need to use AddCalbackForProviderEvents for Event Parsers that handle more than one provider");
            }

            // Convert the eventNameFilter to the more generic filter that take a provider name as well.   
            Func<string, string, EventFilterResponse> eventsToObserve = delegate (string pName, string eName)
            {
                if (pName != GetProviderName())
                {
                    return EventFilterResponse.RejectProvider;
                }

                if (eventNameFilter == null)
                {
                    return EventFilterResponse.AcceptEvent;
                }

                if (eventNameFilter(eName))
                {
                    return EventFilterResponse.AcceptEvent;
                }

                return EventFilterResponse.RejectEvent;
            };

            // This is almost the same code as in AddCallbackForProviderEvents, however it is different because it
            // checks that the template is of type T (not just that the name and provider match).  This is important.  
            var newSubscription = new SubscriptionRequest(eventsToObserve, callback, subscriptionId);
            m_subscriptionRequests.Add(newSubscription);
            var templateState = StateObject;
            EnumerateTemplates(eventsToObserve, delegate (TraceEvent template)
            {
                if (template is T)
                {
                    Subscribe(newSubscription, template, templateState, false);
                }
            });
        }

        // subscribe to a single event for parsers that handle more than one provider (These can't be strongly typed to provider specific payloads)
        /// <summary>
        /// A shortcut that adds 'callback' for the event in 'providerName' and an event name 'eventName'
        /// The callback alone is used as the subscription id for unregistration, so the callback delegate should be unique (by delegate comparison)
        /// 
        /// eventName is of the of the form 'TaskName/OpcodeName'   if the event has a non-trivial opcode, otherwise it is 'TaskName'. 
        /// 
        /// </summary>
        public void AddCallbackForProviderEvent(string providerName, string eventName, Action<TraceEvent> callback)
        {
            Debug.Assert(providerName != null);
            AddCallbackForProviderEvents(
                delegate (string pName, string eName)
                {
                    if (pName != providerName)
                    {
                        return EventFilterResponse.RejectProvider;
                    }

                    if (eventName == null)
                    {
                        return EventFilterResponse.AcceptEvent;
                    }

                    if (eventName == eName)
                    {
                        return EventFilterResponse.AcceptEvent;
                    }

                    return EventFilterResponse.RejectEvent;
                }, callback);
        }
        // subscribe to a set of events for TraceEventParsers that handle more than one provider  
        /// <summary>
        /// Cause 'callback' to be called for any event that this parser recognizes for which the function 'eventsToObserve'
        /// returns 'AcceptEvent'.   The 'eventsToObserve is given both the provider name (first) and the event name and can return
        /// 'AcceptEvent' 'RejectEvent' or 'RejectProvider' (in which case it may not be called again for that provider).  
        /// eventsToObserver can be null in which case all events that match the parser recognizes are selected. 
        /// 
        /// eventNames passed to the filer are of the form 'TaskName/OpcodeName'   if the event has a non-trivial opcode, otherwise it is 'TaskName'. 
        /// 
        /// <para>
        /// Thus this method works for parsers that parse more than one provider (e.g. DynamicTraceEventParser).   
        /// </para>
        /// </summary>
        public virtual void AddCallbackForProviderEvents(Func<string, string, EventFilterResponse> eventsToObserve, Action<TraceEvent> callback)
        {
            AddCallbackForProviderEvents(eventsToObserve, null, callback);
        }
        /// <summary>
        /// Cause 'callback' to be called for any event that this parser recognizes for which the function 'eventsToObserve'
        /// returns 'AcceptEvent'.   The 'eventsToObserve is given both the provider name (first) and the event name and can return
        /// 'AcceptEvent' 'RejectEvent' or 'RejectProvider' (in which case it may not be called again for that provider).  
        /// eventsToObserver can be null in which case all events that match the parser recognizes are selected. 
        /// 
        /// eventNames passed to the filer are of the form 'TaskName/OpcodeName'   if the event has a non-trivial opcode, otherwise it is 'TaskName'.         /// 
        /// <para>
        /// Thus this method works for parsers that parse more than one provider (e.g. DynamicTraceEventParser).   
        /// </para><para>
        /// A subscriptionID can optionally be passed.  This is used (along with the callback identity) to identify this to the 'RemoveCallback' If you
        /// don't need to remove the callback or you will do it in bulk, you don't need this parameter.  
        /// </para>
        /// </summary>
        public virtual void AddCallbackForProviderEvents(Func<string, string, EventFilterResponse> eventsToObserve, object subscriptionId, Action<TraceEvent> callback)
        {
            var newSubscription = new SubscriptionRequest(eventsToObserve, callback, subscriptionId);
            m_subscriptionRequests.Add(newSubscription);

            var templateState = StateObject;
            EnumerateTemplates(eventsToObserve, delegate (TraceEvent template)
            {
                Subscribe(newSubscription, template, templateState, false);
            });
        }

        /// <summary>
        /// Remove all subscriptions added with 'AddCallback' (any overload), that is compatible with T, has a callback 'callback' and subscriptionId 'subscriptionId' 
        /// where 'subscriptionId' was the value that was optionally passed to 'AddCallback' to provide exactly this disambiguation.  
        /// <para>
        /// 'callback' or 'subscriptionId' can be null, in which case it acts as a wild card.  Thus RemoveCallback{TraceEvent}(null, null) will remove all callbacks 
        /// that were registered through this parser.  
        /// </para>
        /// </summary>
        public virtual void RemoveCallback<T>(Action<T> callback, object subscriptionId = null) where T : TraceEvent
        {
            for (int i = 0; i < m_subscriptionRequests.Count; i++)
            {
                var cur = m_subscriptionRequests[i];
                if ((subscriptionId == null || cur.m_subscriptionId == subscriptionId) &&
                    (callback == null || Delegate.Equals(cur.m_callback, callback)))
                {
                    foreach (var template in cur.m_activeSubscriptions)
                    {
                        if (template.taskGuid != Guid.Empty)
                        {
                            source.UnregisterEventTemplate(template.Target, (int)template.Opcode, template.taskGuid);
                        }

                        if (template.ID != TraceEventID.Illegal)
                        {
                            source.UnregisterEventTemplate(template.Target, (int)template.ID, template.ProviderGuid);
                        }
                    }
                    m_subscriptionRequests.RemoveRange(i, 1);
                }
            }
        }

        /// <summary>
        /// A static TraceEventParser is a parser where the set of events that can be subscribed to (and their payload fields) are known at 
        /// compile time.  There are very few dynamic TraceEventParsers (DynamicTraceEventParser, RegisteredTraceEventParser and WPPTraceEventParser)
        /// </summary>
        public virtual bool IsStatic { get { return true; } }

        #region protected
        /// <summary>
        /// All TraceEventParsers invoke this constructor.  If 'dontRegister' is true it is not registered with the source. 
        /// </summary>
        protected TraceEventParser(TraceEventSource source, bool dontRegister = false)
        {
            Debug.Assert(source != null);
            this.source = source;
            stateKey = @"parsers\" + GetType().FullName;

            if (!dontRegister)
            {
                this.source.RegisterParser(this);
            }

#if DEBUG
            if (GetProviderName() != null && !m_ConfirmedAllEventsAreInEnumeration)
            {
                ConfirmAllEventsAreInEnumeration();
                m_ConfirmedAllEventsAreInEnumeration = true;
            }
#endif
        }

        /// <summary>
        /// Normally a TraceEvent parser knows how to parse only one provider.   If this is true
        /// ProviderName returns the name of this provider.  If the parser knows how to parse 
        /// more than one provider, this property returns null.     
        /// </summary>
        protected abstract string GetProviderName();

        /// <summary>
        /// If the parser needs to persist data along with the events we put it in a separate object.   
        /// This object and then implement serialization functionality that allows it to be persisted (this is for ETLX support).  
        /// </summary>
        protected internal object StateObject
        {
            get
            {
                object ret;
                ((TraceEventSource)source).UserData.TryGetValue(stateKey, out ret);
                return ret;
            }
            set
            {
                ((TraceEventSource)source).UserData[stateKey] = value;
            }
        }

        /// <summary>
        /// Returns a list of all templates currently existing (new ones can come in, but OnNewEventDefintion must be called 
        /// whenever that happens.   Note that the returned templates MUST be cloned and do not have their source or parser state
        /// fields set.  These must be set as part of subscription (after you know if you care about them or not).  
        /// 
        /// eventsToObserver is given the provider name and event name and those events that return AcceptEvent will
        /// have the 'callback' function called on that template.   eventsToObserver can be null which mean all events.  
        /// 
        /// The returned template IS READ ONLY!   If you need a read-write copy (typical), clone it first.   
        /// </summary>
        protected internal abstract void EnumerateTemplates(Func<string, string, EventFilterResponse> eventsToObserve, Action<TraceEvent> callback);

        #endregion
        #region private

#if DEBUG
        /// <summary>
        /// Debug-only code  Confirm that some did not add an event, but forgot to add it to the enumeration that EnumerateTemplates 
        /// returns.  
        /// </summary>
        private bool m_ConfirmedAllEventsAreInEnumeration;

        private void ConfirmAllEventsAreInEnumeration()
        {
            var declaredSet = new SortedDictionary<string, string>();

            // TODO FIX NOW currently we have a hack where we know we are not correct 
            // for JScriptTraceEventParser.  
            if (GetType().Name == "JScriptTraceEventParser")
            {
                return;
            }

            // Antimalware events do heavy sharing of templates.
            if (GetType().Name == "MicrosoftAntimalwareEngineTraceEventParser" || GetType().Name == "MicrosoftAntimalwareAMFilterTraceEventParser")
            {
                return;
            }

            // Use reflection to see what events have declared 
            MethodInfo[] methods = GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            for (int i = 0; i < methods.Length; i++)
            {
                var addMethod = methods[i];
                if (!addMethod.IsSpecialName)
                {
                    continue;
                }

                var addMethodName = addMethod.Name;
                if (!addMethodName.StartsWith("add_"))
                {
                    continue;
                }

                var eventName = addMethodName.Substring(4);

                if (eventName.EndsWith("Group"))
                {
                    continue;
                }

                ParameterInfo[] paramInfos = addMethod.GetParameters();
                if (paramInfos.Length != 1)
                {
                    continue;
                }

                if (eventName == "MemoryPageAccess" || eventName == "MemoryProcessMemInfo")  // One event has two templates.  
                {
                    continue;
                }

                if (eventName == "GCSampledObjectAllocation")       // One event has two templates. 
                {
                    continue;
                }

                if (eventName == "PerfInfoISR")                     // One event has two templates.  
                {
                    continue;
                }

                if (eventName == "MethodTailCallFailedAnsi")        // One event has two templates.  
                {
                    continue;
                }

                // The IIs parser uses Cap _ instead of PascalCase, normalize
                if (GetType().Name == "IisTraceEventParser")
                {
                    eventName = eventName.ToLower();
                }

                declaredSet.Add(eventName, eventName);
            }

            var enumSet = new SortedDictionary<string, string>();

            // Make sure that we have all the event we should have 
            EnumerateTemplates(null, delegate (TraceEvent template)
            {
                // the CLR provider calls this callback twice. Rather then refactoring EnumerateTemplates for all parsers
                // we'll "special case" project N templates and ignore them...
                if (template.ProviderGuid == ClrTraceEventParser.NativeProviderGuid)
                {
                    return;
                }

                var eventName = template.EventName.Replace("/", "");
                if (eventName == "MemoryPageAccess" || eventName == "MemoryProcessMemInfo")  // One event has two templates.  
                {
                    return;
                }

                if (eventName == "GCSampledObjectAllocation")       // One event has two templates. 
                {
                    return;
                }

                if (eventName == "PerfInfoISR")                     // One event has two templates. 
                {
                    return;
                }

                if (eventName == "FileIO")
                {
                    eventName = "FileIOName";       // They use opcode 0 which gets truncated.  
                }

                if (eventName == "EventTrace")
                {
                    eventName = "EventTraceHeader"; // They use opcode 0 which gets truncated.  
                }

                if (eventName == "Jscript_GC_IdleCollect")
                {
                    eventName = eventName + template.OpcodeName;  // They use opcode 0 which gets truncated.
                }

                // The IIs parser uses Cap _ instead of PascalCase, normalize
                if (template.ProviderGuid == IisTraceEventParser.ProviderGuid)
                {
                    eventName = eventName.ToLower().Replace("_", "");
                }

                // We register the same name for old classic and manifest for some old GC events (
                if (eventName.StartsWith("GC") && template.ID == (TraceEventID)0xFFFF &&
                    (template.ProviderGuid == ClrTraceEventParser.ProviderGuid || template.providerGuid == ClrTraceEventParser.NativeProviderGuid))
                {
                    return;
                }

                if (declaredSet.ContainsKey(eventName))
                {
                    declaredSet.Remove(eventName);
                }
                else
                {
                    Debug.Assert(!enumSet.ContainsKey(eventName));
                    enumSet[eventName] = eventName;
                }
            });

            // As this point any events that are both in the declared set and the static set represent a mismatch
            if (0 < enumSet.Count)
            {
                var provider = GetProviderName();
                var typeName = GetType().FullName;
                foreach (var methodName in enumSet.Keys)
                {
                    Debug.Assert(false, "The template " + methodName +
                        " for the parser " + typeName +
                        " for provider " + provider +
                        " exists in EnumerateTemplates enumeration but not as a C# event, please add it.");
                }
            }
            if (0 < declaredSet.Count)
            {
                var provider = GetProviderName();
                var typeName = GetType().FullName;
                foreach (var methodName in declaredSet.Keys)
                {
                    Debug.Assert(false, "The C# event " + methodName +
                        " for the parser " + typeName +
                        " for provider " + provider +
                        " does NOT exist in the EnumerateTemplates enumeration, please add it.");
                }
            }
        }
#endif


        /// <summary>
        /// If the parser can change over time (it can add new definitions),  It needs to support this interface.  See EnumerateDynamicTemplates for details.
        /// This function should be called any time a new event is now parsable by the parser.   If it is guaranteed that the particular event is 
        /// definitely being ADDED (it never existed in the past), then you can set 'mayHaveExistedBefore' to false and save some time.  
        ///  
        /// It returns false if there are no definitions for that particular Provider (and thus you can skip callback if desired).  
        /// </summary>
        internal virtual EventFilterResponse OnNewEventDefintion(TraceEvent template, bool mayHaveExistedBefore)
        {
#if !NOT_WINDOWS && !NO_DYNAMIC_TRACEEVENTPARSER
            Debug.Assert(template is DynamicTraceEventData);
#endif
            EventFilterResponse combinedResponse = EventFilterResponse.RejectProvider;      // This is the combined result from all subscriptions. 
            var templateState = StateObject;
            // Does it match any subscription request we already have?  
            for (int i = 0; i < m_subscriptionRequests.Count; i++)
            {
                var cur = m_subscriptionRequests[i];
                // TODO sort template by provider so we can optimize.  
                Debug.Assert(GetProviderName() == null);         // Static parsers (providerName != null) don't support OnNewEventDefintion. 
                if (cur.m_eventToObserve != null)
                {
                    var response = cur.m_eventToObserve(template.ProviderName, template.EventName);
                    if (response == EventFilterResponse.RejectProvider)
                    {
                        continue;
                    }

                    // We know that we are not rejecting the provider as a whole.   
                    if (combinedResponse == EventFilterResponse.RejectProvider)
                    {
                        combinedResponse = EventFilterResponse.RejectEvent;
                    }

                    if (response == EventFilterResponse.RejectEvent)
                    {
                        continue;
                    }
                }

                combinedResponse = EventFilterResponse.AcceptEvent;         // There is at least one subscription for this template.  
                Subscribe(cur, template, templateState, mayHaveExistedBefore);
            }
            return combinedResponse;
        }

        /// <summary>
        /// Given a subscription request, and a template that can now be parsed (and its state, which is just TraceEventParser.StateObj) 
        /// If subscription states that the template should be registered with the source, then do the registration.   
        /// 
        /// if 'mayHaveExistedBefore' means that this template definition may have been seen before (DynamicTraceEventParsers do this as
        /// you may get newer versions dynamically registering themselves).   In that case this should be set.  If you can guaranteed that
        /// a particular template (provider-eventID pair) will only be subscribed at most once you can set this to false.  
        /// </summary>
        private void Subscribe(SubscriptionRequest cur, TraceEvent template, object templateState, bool mayHaveExistedBefore)
        {
            // Configure the template with callback associated with the subscription
            var templateWithCallback = template.Clone();
            Debug.Assert(templateWithCallback.traceEventSource == null);
            templateWithCallback.SetState(templateState);
            templateWithCallback.Target = cur.m_callback;

            // See if we can update in place
            if (mayHaveExistedBefore)
            {
                for (int i = 0; i < cur.m_activeSubscriptions.Count; i++)
                {
                    var activeSubscription = cur.m_activeSubscriptions[i];
                    if (activeSubscription.Matches(templateWithCallback))
                    {
                        // Trace.WriteLine("Subscribe: Found existing subscription, removing it for update");

                        // Unsubscribe 
                        source.UnregisterEventTemplate(activeSubscription.Target, (int)activeSubscription.ID, activeSubscription.ProviderGuid);
                        // TODO support WPP.  
                        if (activeSubscription.taskGuid != Guid.Empty)
                        {
                            source.UnregisterEventTemplate(activeSubscription.Target, (int)activeSubscription.Opcode, activeSubscription.taskGuid);
                        }

                        // Update it in place the active subscription in place.  
                        cur.m_activeSubscriptions[i] = templateWithCallback;
                    }
                }
            }
            else
            {
#if DEBUG   // Assert the template did NOT exist before.
                if (cur.m_callback != null)
                {
                    for (int i = 0; i < cur.m_activeSubscriptions.Count; i++)
                    {
                        var activeSubscription = cur.m_activeSubscriptions[i];
                        Debug.Assert(!activeSubscription.Matches(templateWithCallback));
                    }
                }
#endif
                // we can simply add to our list of subscriptions.  
                cur.m_activeSubscriptions.Add(templateWithCallback);
            }

            // Actually Register it with the source.     
            source.RegisterEventTemplate(templateWithCallback);
#if !DOTNET_V35
            Debug.Assert(templateWithCallback.traceEventSource == Source ||
                (templateWithCallback.traceEventSource is Microsoft.Diagnostics.Tracing.Etlx.TraceLog &&
                 Source is Microsoft.Diagnostics.Tracing.Etlx.TraceLogEventSource));
#endif
        }

        /// <summary>
        /// Keeps track of a single 'AddCallback' request so it can be removed later.   It also handles lazy addition of events.  
        /// </summary>
        private class SubscriptionRequest
        {
            /// <summary>
            /// Create a subscription request.  'eventsToObserve takes a provider name (first) and a event name and returns a three valued EventFilterResponse
            /// value (accept, reject, reject provider)
            /// </summary>
            internal SubscriptionRequest(Func<string, string, EventFilterResponse> eventsToObserve, Delegate callback, object subscriptionId)
            {
                m_eventToObserve = eventsToObserve;
                m_callback = callback;
                m_subscriptionId = subscriptionId;
            }

            // We need the original arguments to the AddCallback so that we can repeat them when new events are discovered.  
            internal Func<string, string, EventFilterResponse> m_eventToObserve;    // (Gives provider name then eventName).  This is null for parsers that don't support OnNewEventDefintion (the strongly types ones)
            internal Delegate m_callback;
            internal object m_subscriptionId;                                   // Identifies this subscription so we could delete it.  
            internal GrowableArray<TraceEvent> m_activeSubscriptions;           // The actual TraceEvents that have been registered with the TraceEventSource for this subscription.  
        }

        /// <summary>
        /// The source that this parser is connected to.  
        /// </summary>
        protected internal ITraceParserServices source;
        private GrowableArray<SubscriptionRequest> m_subscriptionRequests;

        private string stateKey;
        #endregion
    }

    /// <summary>
    /// EventFilterResponse is the set of responses  a user-defined filtering routine, might return.  This is used in the TraceEventParser.AddCallbackForProviderEvents method.  
    /// </summary>
    public enum EventFilterResponse
    {
        /// <summary>
        /// Not an interesting event, but other events in the same provider may be
        /// </summary>
        RejectEvent,
        /// <summary>
        /// No event in the provider will be accepted
        /// </summary>
        RejectProvider,
        /// <summary>
        ///  An interesting event
        /// </summary>
        AcceptEvent,
    }

    /// <summary>
    /// An options class for the TraceEventDispatcher
    /// </summary>
    public sealed class TraceEventDispatcherOptions
    {
        /// <summary>
        /// StartTime from which you want to start analyzing the events for file formats that support this.
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// EndTime till when you want to analyze events for file formats that support this.
        /// </summary>
        public DateTime EndTime { get; set; }
    }

    /// <summary>
    /// A TraceEventDispatcher is a TraceEventSource that supports a callback model for dispatching events.  
    /// </summary>
    public abstract unsafe class TraceEventDispatcher : TraceEventSource
    {
        /// <summary>
        /// Obtains the correct TraceEventDispatcher for the given trace file name.
        /// </summary>
        /// <param name="traceFileName">A path to a trace file.</param>
        /// <returns>A TraceEventDispatcher for the given trace file.</returns>
        public static TraceEventDispatcher GetDispatcherFromFileName(string traceFileName, TraceEventDispatcherOptions options = null)
        {
#if !DOTNET_V35
            if (traceFileName.EndsWith(".trace.zip", StringComparison.OrdinalIgnoreCase))
            {
                return new CtfTraceEventSource(traceFileName);
            }
            else if (traceFileName.EndsWith(".netperf", StringComparison.OrdinalIgnoreCase))
            {
                return new EventPipeEventSource(traceFileName);
            }
            else if (traceFileName.EndsWith(".nettrace", StringComparison.OrdinalIgnoreCase))
            {
                return new EventPipeEventSource(traceFileName);
            }
#if !NOT_WINDOWS
            else if (traceFileName.EndsWith(".etl", StringComparison.OrdinalIgnoreCase) ||
                     traceFileName.EndsWith(".etlx", StringComparison.OrdinalIgnoreCase) ||
                     traceFileName.EndsWith(".etl.zip", StringComparison.OrdinalIgnoreCase))
            {
                return new ETWTraceEventSource(traceFileName);
            }
            else if (traceFileName.EndsWith(".btl", StringComparison.OrdinalIgnoreCase))
            {
                return new BPerfEventSource(traceFileName, options);
            }
#endif
            else
            {
#endif
                return null;
            }
        }

        // Normally you subscribe to events using parsers that 'attach' themselves to the source. However
        // there are a couple of events that TraceEventDispatcher can handle directly.
        /// <summary>
        /// Subscribers to UnhandledEvent are called if no other hander has processed the event.   It is
        /// generally used in DEBUG builds to validate that events are getting to the source at all.  
        /// </summary>
        public event Action<TraceEvent> UnhandledEvents
        {
            add
            {
                unhandledEventTemplate.Action += value;
            }
            remove
            {
                unhandledEventTemplate.Action -= value;
            }
        }
        /// <summary>
        /// Subscribers to EveryEvent are called on every event in the trace.   Normally you don't want
        /// to subscribe to this but rather use a TraceEvenParser (which knows how to decode the payloads)
        /// and subscribe to particular events through that.   For example Using TraceEventSource.Dynamic.All 
        /// or TraceEventSource.Dynamic.All is more likely to be what you are looking for.   AllEvents is only
        /// an event callback of last resort, that only gives you the 'raw' data (common fields but no
        /// payload).  
        /// <para>
        /// This is called AFTER any event-specific handlers.
        /// </para>
        /// </summary>
        public event Action<TraceEvent> AllEvents;

        /// <summary>
        /// Subscribers to UnhandledEvent are called if no other hander has processed the event.   It is
        /// generally used in DEBUG builds to validate that events are getting to the source at all.  
        /// </summary>
        [Obsolete("Use UnhandledEvents")]
        public event Action<TraceEvent> UnhandledEvent
        {
            add
            {
                unhandledEventTemplate.Action += value;
            }
            remove
            {
                unhandledEventTemplate.Action -= value;
            }
        }
        /// <summary>
        /// Subscribers to EveryEvent are called on every event in the trace.   Normally you don't want
        /// to subscribe to this but rather use a TraceEvenParser and subscribe to particular events
        /// through that.   
        /// <para>
        /// This is called AFTER any event-specific handlers.
        /// </para>
        /// </summary>
        [Obsolete("Use AllEvents")]
        public event Action<TraceEvent> EveryEvent
        {
            add
            {
                AllEvents += value;
            }
            remove
            {
                AllEvents -= value;
            }
        }

        /// <summary>
        /// Once a client has subscribed to the events of interest, calling Process actually causes
        /// the callbacks to happen.   
        /// <para>
        /// Subclasses implementing this method should call 'OnCompleted' 
        /// before returning.  
        /// </para>
        /// </summary>
        /// <returns>false If StopProcessing was called</returns>
        public abstract bool Process();
        /// <summary>
        /// Calling StopProcessing in a callback when 'Process()' is running will indicate that processing
        /// should be stopped immediately and that the Process() method should return.  
        /// 
        /// Note that this stop request will not be honored until the next event from the source.   Thus
        /// for real time sessions there is an indeterminate delay before the stop will complete.   
        /// If you need to force the stop you should instead call Dispose() on the session associated with 
        /// the real time session.  This will cause the source to be shut down and thus also stop processing
        /// (Process() will return) but is guaranteed to complete in a timely manner.  
        /// </summary>
        public virtual void StopProcessing()
        {
            stopProcessing = true;
        }
        /// <summary>
        /// Subscribers of Completed will be called after processing is complete (right before TraceEventDispatcher.Process returns.    
        /// </summary>
        public event Action Completed;

        /// <summary>
        /// Wrap (or filter) the dispatch of every event from the TraceEventDispatcher stream.   
        /// Instead of calling the normal code it calls 'hook' with both the event to be dispatched
        /// and the method the would normally do the processing.    Thus the routine has 
        /// the option to call normal processing, surround it with things like a lock
        /// or skip it entirely.  This can be called more than once, in which case the last
        /// hook method gets called first (which may end up calling the second ...)
        /// 
        /// For example,here is an example that uses AddDispatchHook to 
        /// take a lock is taken whenever dispatch work is being performed.  
        /// 
        /// AddDispatchHook((anEvent, dispatcher) => { lock (this) { dispatcher(anEvent); } });
        /// </summary>
        public void AddDispatchHook(Action<TraceEvent, Action<TraceEvent>> hook)
        {
            if (hook == null)
            {
                throw new ArgumentException("Must provide a non-null callback", nameof(hook), null);
            }

            // Make a new dispatcher which calls the hook with the old dispatcher.   
            Action<TraceEvent> oldUserDefinedDispatch = userDefinedDispatch ?? DoDispatch;
            userDefinedDispatch = delegate (TraceEvent anEvent) { hook(anEvent, oldUserDefinedDispatch); };
        }

        #region protected
        /// <summary>
        /// Called when processing is complete.  You can call this more than once if your not sure if it has already been called.  
        /// however we do guard against races.  
        /// </summary>
        protected void OnCompleted()
        {
            var completed = Completed;
            Completed = null;               // We set it to null so we only do it once.  
            if (completed != null)
            {
                completed();
            }
        }
        #endregion
        #region private
#if DEBUG
        /// <summary>
        /// For debugging, dump the Dispatcher table.
        /// </summary>
        [Conditional("DEBUG")]
        internal void DumpToDebugString()
        {
            Debug.WriteLine(string.Format("Dumping TraceEventDispatcher GetHashCode=0x{0:x}", GetHashCode()));
            for (int i = 0; i < templates.Length; i++)
            {
                var template = templates[i];
                while (template != null)
                {
                    Debug.WriteLine(string.Format(" Hash 0x{0:x} Type {1} lookupAsClassic {2} Provider {3} EventName {4} Guid {5} EventID {6} TaskGuid {7} Opcode {8} ",
                        i, template.GetType().Name, template.lookupAsClassic, template.ProviderName, template.EventName,
                        template.ProviderGuid, template.ID, template.taskGuid, template.Opcode));
                    template = template.next;
                }
            }
        }
#endif
        internal bool AllEventsHasCallback { get { return AllEvents != null; } }
        /// <summary>
        ///  Number of different events that have callbacks associated with them 
        /// </summary>
        internal int DistinctCallbackCount()
        {
            int ret = 0;
            for (int i = 0; i < templates.Length; i++)
            {
                if (templates[i] != null)
                {
                    ret++;
                }
            }

            return ret;
        }

        /// <summary>
        /// Total number of callbacks that are registered.  Even if they are for the same event.  
        /// </summary>
        /// <returns></returns>
        internal int CallbackCount()
        {
            int ret = 0;
            for (int i = 0; i < templates.Length; i++)
            {
                var curTemplate = templates[i];
                while (curTemplate != null)
                {
                    ret++;
                    curTemplate = curTemplate.next;
                }
            }
            return ret;
        }

        internal string DumpHash()
        {
            var sw = new StringWriter();
            sw.WriteLine("TableDump");
            for (int i = 0; i < templates.Length; i++)
            {
                var template = templates[i];
                if (template != null)
                {
                    sw.WriteLine("Hash Bucket {0}", i);
                    {
                        while (template != null)
                        {
                            sw.WriteLine("  Item {0} HashCode {1} Provider {2} EventNum {3}",
                                template.GetType().Name, template.GetHashCode(), template.ProviderGuid, template.eventID);
                            template = template.next;
                        }
                    }
                }
            }
            sw.WriteLine("End TableDump");
            return sw.ToString();
        }

        internal int TemplateLength() { return templates.Length; }

        internal /*protected*/ TraceEventDispatcher()
        {
            // Initialize our data structures. 
            unhandledEventTemplate = new UnhandledTraceEvent();
            unhandledEventTemplate.traceEventSource = this;
            ReHash();       // Allocates the hash table
        }
        internal override void RegisterUnhandledEventImpl(Func<TraceEvent, bool> callback)
        {
            if (lastChanceHandlers == null)
            {
                lastChanceHandlers = new Func<TraceEvent, bool>[] { callback };
            }
            else
            {
                // Put it on the end of the array.  
                var newLastChanceHandlers = new Func<TraceEvent, bool>[lastChanceHandlers.Length + 1];
                Array.Copy(lastChanceHandlers, newLastChanceHandlers, lastChanceHandlers.Length);
                newLastChanceHandlers[lastChanceHandlers.Length] = callback;
                lastChanceHandlers = newLastChanceHandlers;
            }
        }

        /// <summary>
        /// This is the routine that is called back when any event arrives.  Basically it looks up the GUID
        /// and the opcode associated with the event and finds right subclass of TraceEvent that
        /// knows how to decode the packet, and calls its virtual TraceEvent.Dispatch method.  Note
        /// that TraceEvent does NOT have a copy of the data, but rather just a pointer to it. 
        /// This data is ONLY valid during the callback. 
        /// </summary>
        protected internal void Dispatch(TraceEvent anEvent)
        {
            if (userDefinedDispatch == null)
            {
                DoDispatch(anEvent);
            }
            else
            {
                // Rare case, there is a dispatch hook, call it (which may call the original Dispatch logic)
                userDefinedDispatch(anEvent);
                anEvent.eventRecord = null;
            }
        }

        private void DoDispatch(TraceEvent anEvent)
        {
#if DEBUG
            try
            {
#endif
            if (anEvent.Target != null)
            {
                anEvent.Dispatch();
            }

            if (anEvent.next != null)
            {
                TraceEvent nextEvent = anEvent;
                for (; ; )
                {
                    nextEvent = nextEvent.next;
                    if (nextEvent == null)
                    {
                        break;
                    }

                    if (nextEvent.Target != null)
                    {
                        nextEvent.eventRecord = anEvent.eventRecord;
                        nextEvent.userData = anEvent.userData;
                        nextEvent.eventIndex = anEvent.eventIndex;
                        nextEvent.Dispatch();
                        nextEvent.eventRecord = null;
                    }
                }
            }
            if (AllEvents != null)
            {
                if (unhandledEventTemplate == anEvent)
                {
                    unhandledEventTemplate.PrepForCallback();
                }

                AllEvents(anEvent);
            }
            anEvent.eventRecord = null;
#if DEBUG
            }
            catch (Exception e)
            {
                Debug.WriteLine("Error: exception thrown during callback.  Will be swallowed!");
                Debug.WriteLine("Exception: " + e.Message);
                Debug.Assert(false, "Thrown exception " + e.GetType().Name + " '" + e.Message + "'");
            }
#endif
        }

        /// <summary>
        /// Lookup up the event based on its ProviderID (GUID) and EventId (Classic use the TaskId and the
        /// Opcode field for lookup, but use these same fields (see ETWTraceEventSource.RawDispatchClassic)
        /// </summary>
        internal TraceEvent Lookup(TraceEventNativeMethods.EVENT_RECORD* eventRecord)
        {
            int lastChanceHandlerChecked = 0;       // We have checked no last chance handlers to begin with
            RetryLookup:
            ushort eventID = eventRecord->EventHeader.Id;

            //double relTime = QPCTimeToRelMSec(eventRecord->EventHeader.TimeStamp);
            //if (relTime > 2897.300)
            //    GC.KeepAlive(this);

            // Classic events use the opcode field as the discriminator instead of the event ID
            if ((eventRecord->EventHeader.Flags & TraceEventNativeMethods.EVENT_HEADER_FLAG_CLASSIC_HEADER) != 0)
            {
                // The += is really shorthand for if (opcode == 0) eventId == eventRecord->EventHeader.Id.
                // This is needed for WPP events where they say they are 'classic' but they really want to be
                // looked up by EventId and Task GUID.   Note that WPP events should always have an EventHeader.Opcode of 0
                // and normal Classic Events should always haveEventHeader.Id == 0.
                Debug.Assert(eventRecord->EventHeader.Id == 0 || eventRecord->EventHeader.Opcode == 0);
                eventID += eventRecord->EventHeader.Opcode;
            }

            // calculate the hash, and look it up in the table please note that this was hand
            // inlined, and is replicated in TraceEventDispatcher.Insert
            int* guidPtr = (int*)&eventRecord->EventHeader.ProviderId;   // This is the taskGuid for Classic events.  
            int hash = (*guidPtr + eventID * 9) & templatesLengthMask;
            for (; ; )
            {
                TemplateEntry* entry = &templatesInfo[hash];
                int* tableGuidPtr = (int*)&entry->eventGuid;
                if (tableGuidPtr[0] == guidPtr[0] && tableGuidPtr[1] == guidPtr[1] &&
                    tableGuidPtr[2] == guidPtr[2] && tableGuidPtr[3] == guidPtr[3] &&
                    entry->eventID == eventID)
                {
                    TraceEvent curTemplate = templates[hash];
                    if (curTemplate != null)
                    {
                        // Since provider and task guids can not overlap, we can only match if
                        // we are using the correct format.  
                        curTemplate.eventRecord = eventRecord;
                        curTemplate.userData = eventRecord->UserData;
                        curTemplate.eventIndex = currentID;
                        currentID = currentID + 1;      // TODO overflow. 

                        if ((((int)currentID) & 0xFFFF) == 0) // Every 64K events allow Thread.Interrupt.  
                        {
                            System.Threading.Thread.Sleep(0);
                        }

#if DEBUG                   // ASSERT we found the event using the mechanism we expected to use.
                        Debug.Assert(((eventRecord->EventHeader.Flags & TraceEventNativeMethods.EVENT_HEADER_FLAG_CLASSIC_HEADER) != 0) == curTemplate.lookupAsClassic);
                        if (curTemplate.lookupAsClassic)
                        {
                            Debug.Assert(curTemplate.taskGuid == eventRecord->EventHeader.ProviderId);
                            if (curTemplate.lookupAsWPP)
                            {
                                Debug.Assert((ushort)curTemplate.eventID == eventRecord->EventHeader.Id);
                            }
                            else
                            {
                                Debug.Assert((byte)curTemplate.opcode == eventRecord->EventHeader.Opcode);
                            }
                        }
                        else
                        {
                            Debug.Assert(curTemplate.ProviderGuid == eventRecord->EventHeader.ProviderId);
                            Debug.Assert((ushort)curTemplate.eventID == eventRecord->EventHeader.Id);
                        }
#endif
                        return curTemplate;
                    }
                    else
                    {
                        break;      // Found an exact match but it was empty (deleted)
                    }
                }
                if (!entry->inUse)
                {
                    break;
                }

                // Trace.Write("Collision " + *asGuid + " opcode " + opcode + " and " + templatesInfo[hash].providerGuid + " opcode " + templatesInfo[hash].opcode);
                hash = (hash + (int)eventID * 2 + 1) & templatesLengthMask;
            }
            unhandledEventTemplate.eventRecord = eventRecord;
            unhandledEventTemplate.userData = eventRecord->UserData;
            unhandledEventTemplate.eventIndex = currentID;
            unhandledEventTemplate.lookupAsClassic = unhandledEventTemplate.IsClassicProvider;
            currentID = currentID + 1;                  // TODO overflow.
            if ((((int)currentID) & 0xFFFF) == 0)       // Every 64K events allow Thread.Interrupt.  
            {
                System.Threading.Thread.Sleep(0);
            }

            unhandledEventTemplate.opcode = unchecked((TraceEventOpcode)(-1));      // Marks it as unhandledEvent;

#if DEBUG
            // Set some illegal values to highlight missed PrepForCallback() calls
            unhandledEventTemplate.task = unchecked((TraceEventTask)(-1));
            unhandledEventTemplate.providerName = "ERRORPROVIDER";
            unhandledEventTemplate.taskName = "ERRORTASK";
            unhandledEventTemplate.opcodeName = "ERROROPCODE";
            unhandledEventTemplate.eventID = TraceEventID.Illegal;
            unhandledEventTemplate.taskGuid = Guid.Empty;
#endif

            // Allow the last chance handlers to get a crack at it.   
            if (lastChanceHandlers != null && lastChanceHandlerChecked < lastChanceHandlers.Length)
            {
                unhandledEventTemplate.PrepForCallback();
                do
                {
                    // Check the next last chance handler.  If it returns true it may have modified the lookup table, so retry the lookup.  
                    // In ether case we move past the current last chance handler to the next one (insuring termination). 
                    var lastChanceHandlerModifiedLookup = lastChanceHandlers[lastChanceHandlerChecked](unhandledEventTemplate);
                    lastChanceHandlerChecked++;
                    if (lastChanceHandlerModifiedLookup)
                    {
                        goto RetryLookup;
                    }
                }
                while (lastChanceHandlerChecked < lastChanceHandlers.Length);
            }
            return unhandledEventTemplate;
        }
        internal unsafe TraceEvent LookupTemplate(Guid guid, TraceEventID eventID_)
        {
            // calculate the hash, and look it up in the table please note that this was hand
            // inlined, and is replicated in TraceEventDispatcher.Insert
            ushort eventID = (ushort)eventID_;
            int* guidPtr = (int*)&guid;
            int hash = (*guidPtr + ((ushort)eventID) * 9) & templatesLengthMask;
            for (; ; )
            {
                TemplateEntry* entry = &templatesInfo[hash];
                int* tableGuidPtr = (int*)&entry->eventGuid;
                if (tableGuidPtr[0] == guidPtr[0] && tableGuidPtr[1] == guidPtr[1] &&
                    tableGuidPtr[2] == guidPtr[2] && tableGuidPtr[3] == guidPtr[3] &&
                    entry->eventID == eventID)
                {
                    return templates[hash];     // can be null and its OK
                }
                if (!entry->inUse)
                {
                    break;
                }

                hash = (hash + (int)eventID * 2 + 1) & templatesLengthMask;
            }
            return null;
        }

        /// <summary>
        /// Dispose pattern. 
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                OnCompleted();
            }
            else
            {
                // To avoid AVs after someone calls Dispose, only clean up this memory on finalization.  
                if (templatesInfo != null)
                {
                    Marshal.FreeHGlobal((IntPtr)templatesInfo);
                    templatesInfo = null;
                }
            }
            base.Dispose(disposing);
        }
        /// <summary>
        /// Dispose pattern
        /// </summary>
        ~TraceEventDispatcher()
        {
            Dispose(false);
        }

        private void ReHash()
        {
            // Figure out the good new size.  We may in fact shrink if entries are deleted.
            int newLength = 256;
            if (templates != null)
            {
                // Find the number of real entries.  
                int minSize = DistinctCallbackCount() * 2;

                // round up to a power of 2 because we use a mask to do modulus
                while (newLength < minSize)
                {
                    newLength = newLength * 2;
                }
            }
            templatesLengthMask = newLength - 1;

            TemplateEntry* oldTemplatesInfo = templatesInfo;
            TraceEvent[] oldTemplates = templates;

            templates = new TraceEvent[newLength];
            // Reuse the memory if you can, otherwise free what we have and allocate new.  
            if (oldTemplates != null)
            {
                if (newLength != oldTemplates.Length)
                {
                    Debug.Assert(templatesInfo != null);
                    Marshal.FreeHGlobal((IntPtr)templatesInfo);
                    templatesInfo = null;
                }
            }

            if (templatesInfo == null)
            {
                templatesInfo = (TemplateEntry*)Marshal.AllocHGlobal(sizeof(TemplateEntry) * newLength);
            }

            for (int i = 0; i < newLength; i++)
            {
                templatesInfo[i] = default(TemplateEntry);
            }

            numTemplates = 0;
            if (oldTemplates != null)
            {
                for (int i = 0; i < oldTemplates.Length; i++)
                {
                    if (oldTemplates[i] != null)
                    {
                        Insert(oldTemplates[i]);
                    }
                }
            }
        }

        /// <summary>
        /// Inserts 'template' into the hash table, using 'providerGuid' and and 'eventID' as the key. 
        /// For Vista ETW events 'providerGuid' must match the provider GUID and the 'eventID' the ID filed.
        /// For PreVist ETW events 'providerGuid must match the task GUID the 'eventID' is the Opcode
        /// </summary>
        private unsafe void Insert(TraceEvent template)
        {
#if !DOTNET_V35
            Debug.Assert(template.traceEventSource is Microsoft.Diagnostics.Tracing.Etlx.TraceLog || template.traceEventSource == this);
            Debug.Assert(!(template.traceEventSource is Microsoft.Diagnostics.Tracing.Etlx.TraceLogEventSource));
#endif

            if (numTemplates * 4 > templates.Length * 3)    // Are we over 3/4 full?
            {
                ReHash();
            }

            // We need the size to be a power of two since we use a mask to do the modulus. 
            Debug.Assert(((templates.Length - 1) & templates.Length) == 0, "array length must be a power of 2");

            // Which conventions are we using?
            ushort eventID = (ushort)template.eventID;
            Guid eventGuid = template.providerGuid;
            if (template.lookupAsClassic)
            {
                // If we are on XP (classic), we could be dispatching classic (by taskGuid and opcode) even
                // if the event is manifest based. Manifest based providers however are NOT required to have
                // a taskGuid (since they don't need it). To make up for that, when running on XP the CLR's
                // EventProvider creates a taskGuid from the provider Guid and the task number. We mimic this
                // algorithm here to match.
                if (template.taskGuid == Guid.Empty)
                {
                    eventGuid = GenTaskGuidFromProviderGuid(template.ProviderGuid, (ushort)template.task);
                }
                else
                {
                    eventGuid = template.taskGuid;
                }

                // The eventID is the opcode for non WPP classic events, it is the eventID for WPP events (no change)
                if (!template.lookupAsWPP)
                {
                    eventID = (ushort)template.Opcode;
                }
            }
            Debug.Assert(eventGuid != Guid.Empty);

            // compute the hash, and look it up in the table please note that this was
            // hand inlined, and is replicated in TraceEventDispatcher.Lookup
            int* guidPtr = (int*)&eventGuid;
            int hash = (*guidPtr + (int)eventID * 9) & templatesLengthMask;
            TemplateEntry* entry;
            for (; ; )
            {
                entry = &templatesInfo[hash];
                int* tableGuidPtr = (int*)&entry->eventGuid;
                if (tableGuidPtr[0] == guidPtr[0] && tableGuidPtr[1] == guidPtr[1] &&
                    tableGuidPtr[2] == guidPtr[2] && tableGuidPtr[3] == guidPtr[3] &&
                    entry->eventID == eventID)
                {
                    TraceEvent curTemplate = templates[hash];
                    if (curTemplate != null)
                    {
                        Debug.Assert(curTemplate != template);
                        // In the rehash scenario we expect curTemplate to be a list (not a singleton), however we never go
                        // in that case templates[hash] == null and we don't go down this branch.  
                        Debug.Assert(template.next == null);

                        // Normally goto the end of the list (callbacks happen
                        // in the order of registration).
                        if (template.Target != null)
                        {
                            while (curTemplate.next != null)
                            {
                                curTemplate = curTemplate.next;
                            }

                            curTemplate.next = template;
                        }
                        else
                        {
                            // However the template is null, this is the 'canonical' template 
                            // and should be first (so that adding callbacks does not change the
                            // canonical template)  There is no point in having more than one
                            // so ignore it if there already was one, but otherwise put it in 
                            // the front of the list 
                            if (curTemplate.Target != null)
                            {
                                template.next = curTemplate;
                                templates[hash] = template;
                            }
                        }
                    }
                    else
                    {
                        templates[hash] = template;
                    }

                    return;
                }
                if (!entry->inUse)
                {
                    break;
                }

                hash = (hash + (int)eventID * 2 + 1) & templatesLengthMask;
            }
            templates[hash] = template;
            entry->eventID = eventID;
            entry->eventGuid = eventGuid;
            entry->inUse = true;
            numTemplates++;
        }

        /// <summary>
        /// A helper for creating a set of related guids (knowing the providerGuid can can deduce the
        /// 'taskNumber' member of this group.  All we do is add the taskNumber to GUID as a number.  
        /// </summary>
        private static Guid GenTaskGuidFromProviderGuid(Guid providerGuid, ushort taskNumber)
        {
            byte[] bytes = providerGuid.ToByteArray();

            bytes[15] += (byte)taskNumber;
            bytes[14] += (byte)(taskNumber >> 8);
            return new Guid(bytes);
        }

        #region TemplateHashTable
        private struct TemplateEntry
        {
            public Guid eventGuid;
            public ushort eventID;              // Event ID for Vista events, Opcode for Classic events.  
            public bool inUse;                  // This entry is in use. 
        }

        private TemplateEntry* templatesInfo;   // unmanaged array, this is the hash able.  

        private TraceEvent[] templates;         // Logically a field in TemplateEntry 
        private struct NamesEntry
        {
            public NamesEntry(string taskName, string providerName) { this.taskName = taskName; this.providerName = providerName; }
            public string taskName;
            public string providerName;
        }

        private Dictionary<Guid, NamesEntry> guidToNames; // Used to find Provider and Task names from their Guids.  Only rarely used
        private int templatesLengthMask;
        private int numTemplates;
        internal /*protected*/ UnhandledTraceEvent unhandledEventTemplate;
        internal /*protected*/ IEnumerable<TraceEvent> Templates
        {
            get
            {
                for (int i = 0; i < templates.Length; i++)
                {
                    var template = templates[i];
                    while (template != null)
                    {
                        yield return template;
                        template = template.next;
                    }
                }
            }
        }
        #endregion

        #region ITraceParserServices Members
        // [SecuritySafeCritical]
        internal override void RegisterEventTemplateImpl(TraceEvent template)
        {
            // Trace.WriteLine("Registering template " + template.ProviderName + " " + template.ID + " Name " + template.EventName + " Guid " + template.ProviderGuid + " Task " + template.taskGuid + " opcode " + template.Opcode);
            // Debug.WriteLine("callback count = " + CallbackCount());

            // Use the old style exclusive if we are using old ETW APIs, or the provider does not
            // support it (This currently includes the Kernel Events)
            // If the event is tracelogging, do not register it as classic.
#if !NOT_WINDOWS
            Debug.Assert(!(template.ProviderGuid == KernelTraceEventParser.ProviderGuid && template.eventID != TraceEventID.Illegal));
#endif
            if (useClassicETW || template.eventID == TraceEventID.Illegal)
            {
                // Use classic lookup mechanism (Task Guid, Opcode)
                template.lookupAsClassic = true;
                Insert(template);
            }
            else if (template.containsSelfDescribingMetadata)
            {
                template.lookupAsClassic = false;
                Insert(template);
            }
            else
            {
                if (template.lookupAsWPP)
                {
                    // Use WPP lookup mechanism (Task Guid, EventID)
                    template.lookupAsClassic = true;
                    Insert(template);
                }
                else
                {
                    // Use WPP lookup mechanism (Task Guid, EventID)
                    template.lookupAsClassic = false;
                    Insert(template);

                    // If the provider supports both pre-vista events (Guid non-empty), (The CLR does this)
                    // Because the template is chained, we need to clone the template to insert it
                    // again.  
                    if (template.taskGuid != Guid.Empty)
                    {
                        template = template.Clone();
                        template.lookupAsClassic = true;
                        Insert(template);
                    }
                }
            }

            // Debug.WriteLine("Register Done callback count = " + CallbackCount());
        }

        internal override void UnregisterEventTemplateImpl(Delegate action, Guid providerGuid, int eventID)
        {
            // Trace.WriteLine("Unregistering TEMPLATE " + providerGuid + " " + eventID);

            // The dispatcher was disposed.  
            if (templatesInfo == null)
            {
                return;
            }

            int* guidPtr = (int*)&providerGuid;
            int hash = (*guidPtr + eventID * 9) & templatesLengthMask;
            for (; ; )
            {
                TemplateEntry* entry = &templatesInfo[hash];
                int* tableGuidPtr = (int*)&entry->eventGuid;
                if (tableGuidPtr[0] == guidPtr[0] && tableGuidPtr[1] == guidPtr[1] &&
                    tableGuidPtr[2] == guidPtr[2] && tableGuidPtr[3] == guidPtr[3] &&
                    entry->eventID == eventID)
                {
                    var curTemplate = templates[hash];
                    TraceEvent prevTemplate = null;
                    while (curTemplate != null)
                    {
                        if (curTemplate.Target == action)
                        {
                            // Remove the first matching entry from the list.  
                            if (prevTemplate == null)
                            {
                                templates[hash] = curTemplate.next;
                            }
                            else
                            {
                                prevTemplate.next = curTemplate.next;
                            }

                            Debug.WriteLine("Unregister Done callback count = " + CallbackCount());
                            return;
                        }
                        prevTemplate = curTemplate;
                        curTemplate = curTemplate.next;
                    }
                    break;
                }
                if (!entry->inUse)
                {
                    break;
                }

                hash = (hash + (int)eventID * 2 + 1) & templatesLengthMask;
            }
            Debug.Assert(false, "Could not find delegate to unregister!");
            Debug.WriteLine("Unregister Done callback count = " + CallbackCount());
        }

        // [SecuritySafeCritical]
        internal override void RegisterParserImpl(TraceEventParser parser) { }

        // [SecuritySafeCritical]
        internal override string TaskNameForGuidImpl(Guid guid)
        {
            NamesEntry entry;
            LookupGuid(guid, out entry);
            return entry.taskName;
        }
        internal override string ProviderNameForGuidImpl(Guid taskOrProviderName)
        {
            NamesEntry entry;
            LookupGuid(taskOrProviderName, out entry);
            return entry.providerName;
        }

        private void LookupGuid(Guid guid, out NamesEntry ret)
        {
            ret.providerName = null;
            ret.taskName = null;
            if (guidToNames == null)
            {
                if (templates == null)
                {
                    return;
                }
                // Populate the map
                guidToNames = new Dictionary<Guid, NamesEntry>();
                foreach (TraceEvent template in templates)
                {
                    if (template != null)
                    {
                        if (template.providerName != null && template.providerGuid != Guid.Empty && !guidToNames.ContainsKey(template.providerGuid))
                        {
                            guidToNames[template.providerGuid] = new NamesEntry(null, template.providerName);
                        }

                        if (template.taskName != null && template.taskGuid != Guid.Empty)
                        {
                            guidToNames[template.taskGuid] = new NamesEntry(template.taskName, template.providerName);
                        }
                    }
                }
            }
            guidToNames.TryGetValue(guid, out ret);
        }
        #endregion

        internal /*protected*/ bool stopProcessing;
        internal EventIndex currentID;
        private Func<TraceEvent, bool>[] lastChanceHandlers;

        private Action<TraceEvent> userDefinedDispatch; // If non-null, call this when dispatching
        #endregion
    }

    // Generic events for very simple cases (no payload, one value)
    /// <summary>
    /// TraceEventParsers can use this template to define the event for the trivial case where the event has no user-defined payload  
    /// <para>This is only useful to TraceEventParsers.</para>
    /// </summary>
    public sealed class EmptyTraceData : TraceEvent
    {
        /// <summary>
        /// Construct a TraceEvent template which has no payload fields with the given metadata and action
        /// </summary>
        public EmptyTraceData(Action<EmptyTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        #region Private
        /// <summary>
        /// implementation of TraceEvent Interface. 
        /// </summary>
        public override StringBuilder ToXml(StringBuilder sb)
        {
            return Prefix(sb).Append("/>");
        }
        /// <summary>
        /// implementation of TraceEvent Interface. 
        /// </summary>
        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[0];
                }

                return payloadNames;
            }
        }
        /// <summary>
        /// implementation of TraceEvent Interface. 
        /// </summary>
        public override object PayloadValue(int index)
        {
            Debug.Assert(false);
            return null;
        }

        private event Action<EmptyTraceData> Action;
        /// <summary>
        /// Dispatches the event to the action associated with the template. 
        /// </summary>
        protected internal override void Dispatch()
        {
            Action(this);
        }
        /// <summary>
        /// override
        /// </summary>
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<EmptyTraceData>)value; }
        }
        #endregion
    }


    /// <summary>
    /// When the event has just a single string value associated with it, you can use this shared event
    /// template rather than making an event-specific class.
    /// </summary>
    public sealed class StringTraceData : TraceEvent
    {
        /// <summary>
        /// The value of the one string payload property.  
        /// </summary>
        public string Value
        {
            get
            {
                if (isUnicode)
                {
                    return GetUnicodeStringAt(0);
                }
                else
                {
                    return GetUTF8StringAt(0);
                }
            }
        }
        /// <summary>
        /// Construct a TraceEvent template which has one string payload field with the given metadata and action
        /// </summary>
        public StringTraceData(Action<StringTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, bool isUnicode)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.isUnicode = isUnicode;
        }
        #region Private
        /// <summary>
        /// implementation of TraceEvent Interface. 
        /// </summary>
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "Value", Value);
            sb.Append("/>");
            return sb;
        }
        /// <summary>
        /// implementation of TraceEvent Interface. 
        /// </summary>
        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Value" };
                }

                return payloadNames;
            }
        }
        /// <summary>
        /// implementation of TraceEvent Interface. 
        /// </summary>
        public override object PayloadValue(int index)
        {
            Debug.Assert(index < 1);
            switch (index)
            {
                case 0:
                    return Value;
                default:
                    return null;
            }
        }

        private event Action<StringTraceData> Action;
        /// <summary>
        /// implementation of TraceEvent Interface. 
        /// </summary>
        protected internal override void Dispatch()
        {
            Action(this);
        }
        /// <summary>
        /// override
        /// </summary>
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<StringTraceData>)value; }
        }

        private bool isUnicode;
        #endregion
    }

    /// <summary>
    /// UnhandledTraceEvent is a TraceEvent when is used when no manifest information is available for the event. 
    /// </summary>
    public unsafe class UnhandledTraceEvent : TraceEvent
    {
        #region private
        /// <summary>
        /// implementation of TraceEvent Interface. 
        /// </summary>
        public override StringBuilder ToXml(StringBuilder sb)
        {
            sb.Append("<Event MSec="); QuotePadLeft(sb, TimeStampRelativeMSec.ToString("f4"), 13);
            sb.Append(" PID="); QuotePadLeft(sb, ProcessID.ToString(), 6);
            sb.Append(" PName="); QuotePadLeft(sb, ProcessName, 10);
            sb.Append(" TID="); QuotePadLeft(sb, ThreadID.ToString(), 6);
            XmlAttrib(sb, "IsClassic", IsClassicProvider);
            if (IsClassicProvider)
            {
                var providerName = ProviderName;
                if (providerName != "UnknownProvider")
                {
                    XmlAttrib(sb, "ProviderName", providerName);
                }

                var taskName = TaskName;
                XmlAttrib(sb, "TaskName", taskName);
                if (!taskName.StartsWith("Task"))
                {
                    XmlAttrib(sb, "TaskGuid", taskGuid);
                }
            }
            else
            {
                var providerName = ProviderName;
                XmlAttrib(sb, "ProviderName", providerName);
                var formattedMessage = FormattedMessage;
                if (formattedMessage != null)
                {
                    XmlAttrib(sb, "FormattedMessage", formattedMessage);
                }

                if (IsEventWriteString)
                {
                    sb.Append("/>");
                    return sb;
                }

                if (!providerName.StartsWith("Provider("))
                {
                    XmlAttrib(sb, "ProviderGuid", providerGuid);
                }

                XmlAttrib(sb, "eventID", (int)ID);

                var taskName = TaskName;
                XmlAttrib(sb, "TaskName", taskName);
                if (!taskName.StartsWith("Task"))
                {
                    XmlAttrib(sb, "TaskNum", (int)Task);
                }
            }
            XmlAttrib(sb, "OpcodeNum", (int)Opcode);
            XmlAttrib(sb, "Version", Version);
            XmlAttrib(sb, "Level", (int)Level);
            XmlAttrib(sb, "PointerSize", PointerSize);
            XmlAttrib(sb, "EventDataLength", EventDataLength);
            if (EventDataLength > 0)
            {
                sb.AppendLine(">");
                StringWriter dumpSw = new StringWriter();
                TraceEvent.DumpBytes(EventData(), EventDataLength, dumpSw, "  ");
                sb.Append(XmlUtilities.XmlEscape(dumpSw.ToString(), false));
                sb.AppendLine("</Event>");
            }
            else
            {
                sb.Append("/>");
            }

            return sb;
        }
        /// <summary>
        /// implementation of TraceEvent Interface. 
        /// </summary>
        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[0];
                }

                return payloadNames;
            }
        }
        /// <summary>
        /// implementation of TraceEvent Interface. 
        /// </summary>
        public override object PayloadValue(int index)
        {
            Debug.Assert(false);
            return null;
        }

        internal event Action<TraceEvent> Action;
        internal UnhandledTraceEvent() : base(0, 0, null, Guid.Empty, 0, null, Guid.Empty, null) { }
        /// <summary>
        /// implementation of TraceEvent Interface. 
        /// </summary>
        protected internal override void Dispatch()
        {
            if (Action != null)
            {
                PrepForCallback();
                Action(this);
            }
        }
        /// <summary>
        /// override
        /// </summary>
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<TraceEvent>)value; }
        }
        /// <summary>
        /// implementation of TraceEvent Interface. 
        /// </summary>
        public override string ToString()
        {
            // This is only needed so that when we print when debugging we get sane results.  
            if (eventID == TraceEventID.Illegal)
            {
                PrepForCallback();
            }

            return base.ToString();
        }

        /// <summary>
        /// There is some work needed to prepare the generic unhandledTraceEvent that we defer
        /// late (since we often don't care about unhandled events)  
        /// 
        /// TODO this is probably not worht the complexity...
        /// </summary>
        internal void PrepForCallback()
        {
            // Could not find the event, populate the shared 'unhandled event' information.   
            if (IsClassicProvider)
            {
                providerGuid = Guid.Empty;
                taskGuid = eventRecord->EventHeader.ProviderId;
            }
            else
            {
                taskGuid = Guid.Empty;
                providerGuid = eventRecord->EventHeader.ProviderId;
            }
            eventID = (TraceEventID)eventRecord->EventHeader.Id;
            opcode = (TraceEventOpcode)eventRecord->EventHeader.Opcode;
            task = (TraceEventTask)eventRecord->EventHeader.Task;
            taskName = null;        // Null them out so that they get repopulated with this data's
            providerName = null;
            opcodeName = null;
            eventName = null;
            eventNameIsJustTaskName = false;
        }
        #endregion
    }

    #region Private Classes

    internal sealed class TraceEventRawReaders
    {
        internal static unsafe IntPtr Add(IntPtr pointer, int offset)
        {
            return (IntPtr)(((byte*)pointer) + offset);
        }
        internal static unsafe Guid ReadGuid(IntPtr pointer, int offset)
        {
            return *((Guid*)((byte*)pointer.ToPointer() + offset));
        }
        internal static unsafe double ReadDouble(IntPtr pointer, int offset)
        {
            return Unsafe.ReadUnaligned<double>((byte*)pointer.ToPointer() + offset);
        }
        internal static unsafe float ReadSingle(IntPtr pointer, int offset)
        {
            return Unsafe.ReadUnaligned<float>((byte*)pointer.ToPointer() + offset);
        }
        internal static unsafe long ReadInt64(IntPtr pointer, int offset)
        {
            return Unsafe.ReadUnaligned<long>((byte*)pointer.ToPointer() + offset);
        }
        internal static unsafe int ReadInt32(IntPtr pointer, int offset)
        {
            return Unsafe.ReadUnaligned<int>((byte*)pointer.ToPointer() + offset);
        }
        internal static unsafe short ReadInt16(IntPtr pointer, int offset)
        {
            return *((short*)((byte*)pointer.ToPointer() + offset));
        }
        internal static unsafe IntPtr ReadIntPtr(IntPtr pointer, int offset)
        {
            return *((IntPtr*)((byte*)pointer.ToPointer() + offset));
        }
        internal static unsafe byte ReadByte(IntPtr pointer, int offset)
        {
            return *((byte*)((byte*)pointer.ToPointer() + offset));
        }
        internal static unsafe string ReadUnicodeString(IntPtr pointer, int offset, int bufferLength)
        {
            // Really we should be able to count on pointers being null terminated.  However we have had instances
            // where this is not true.   To avoid scanning the string twice we first check if the last character
            // in the buffer is a 0 if so, we KNOW we can use the 'fast path'  Otherwise we check 
            byte* ptr = (byte*)pointer;
            char* charEnd = (char*)(ptr + bufferLength);
            if (charEnd[-1] == 0)       // Is the last character a null?
            {
                return new string((char*)(ptr + offset));       // We can count on a null, so we do an optimized path 
            }
            // (works for last string, and other times by luck). 
            // but who cares as long as it stays in the buffer.  

            // unoptimized path.  Carefully count characters and create a string up to the null.  
            char* charStart = (char*)(ptr + offset);
            int maxPos = (bufferLength - offset) / sizeof(char);
            int curPos = 0;
            while (curPos < maxPos && charStart[curPos] != 0)
            {
                curPos++;
            }
            // CurPos now points at the end (either buffer end or null terminator, make just the right sized string.  
            return new string(charStart, 0, curPos);
        }
        internal static unsafe string ReadUTF8String(IntPtr pointer, int offset, int bufferLength)
        {
            var buff = new byte[bufferLength];
            byte* ptr = ((byte*)pointer) + offset;
            int i = 0;
            while (i < buff.Length)
            {
                byte c = ptr[i];
                if (c == 0)
                {
                    break;
                }

                buff[i++] = c;
            }
            return Encoding.UTF8.GetString(buff, 0, i);     // Convert to unicode.  
        }
    }

    #endregion

#if !DOTNET_V35
    /// <summary>
    /// ObservableExtensions defines methods on TraceEventParser that implement the IObservable protocol for implementing callbacks.
    /// </summary>
    public static class ObservableExtensions
    {
        /// <summary>
        /// Returns an IObjservable that observes all events that 'parser' knows about that  return a T.  If eventName is
        /// non-null, the event's name must match 'eventName', but if eventName is null, any event that returns a T is observed. 
        /// <para>
        /// This means that Observe{TraceEvent}(parser) will observe all events that the parser can parse.  
        /// 
        /// Note that unlike the methods on TraceEventParser, the TraceEvent object returned is already Cloned() and thus can be 
        /// referenced for as long as you like.  
        /// </para>
        /// </summary>
        public static IObservable<T> Observe<T>(this TraceEventParser parser, string eventName) where T : TraceEvent
        {
            return parser.Observe<T>(s => s == eventName);
        }
        /// <summary>
        /// Returns an IObjservable that observes all events that 'parser' knows about that return a T and whose event
        /// name matches the 'eventNameFilter' predicate.  
        /// 
        /// Note that unlike the methods on TraceEventParser, the TraceEvent object returned is already Cloned() and thus can be 
        /// referenced for as long as you like.   
        /// </summary>
        public static IObservable<T> Observe<T>(this TraceEventParser parser, Predicate<string> eventNameFilter = null) where T : TraceEvent
        {
            Action<Action<T>, object> addHandler = (Action<T> callback, object subscriptionId) => parser.AddCallbackForEvents<T>(eventNameFilter, subscriptionId, callback);
            Action<Action<T>, object> removeHandler = (Action<T> callback, object subscriptionId) => parser.RemoveCallback<T>(callback, subscriptionId);
            TraceEventDispatcher source = (TraceEventDispatcher)parser.Source;
            var ret = new TraceEventObservable<T>(source, addHandler, removeHandler);
            return ret;
        }
        /// <summary>
        /// Observe a particular event from a particular provider.   If eventName is null, it will return every event from the provider
        ///  
        /// Note that unlike the methods on TraceEventParser, the TraceEvent object returned is already Cloned() and thus can be 
        /// referenced for as long as you like.  
        /// </summary>
        public static IObservable<TraceEvent> Observe(this TraceEventParser parser, string providerName, string eventName)
        {
            return parser.Observe(delegate (string pName, string eName)
            {
                if (pName != providerName)
                {
                    return EventFilterResponse.RejectProvider;
                }

                if (eventName == null)
                {
                    return EventFilterResponse.AcceptEvent;
                }

                if (eventName == eName)
                {
                    return EventFilterResponse.AcceptEvent;
                }

                return EventFilterResponse.RejectEvent;
            });
        }
        /// <summary>
        /// Given a predicate 'eventToObserve' which takes the name of a provider (which may be of the form Provider(GUID)) (first) and 
        /// an event name (which may be of the form EventID(NUM)) and indicates which events to observe, return an IObservable
        /// that observes those events. 
        /// 
        /// Note that unlike the methods on TraceEventParser, the TraceEvent object returned is already Cloned() and thus can be 
        /// referenced for as long as you like.  . 
        /// </summary>
        public static IObservable<TraceEvent> Observe(this TraceEventParser parser, Func<string, string, EventFilterResponse> eventsToObserve)
        {
            Action<Action<TraceEvent>, object> addHandler = (Action<TraceEvent> callback, object subscriptionId) => parser.AddCallbackForProviderEvents(eventsToObserve, subscriptionId, callback);
            Action<Action<TraceEvent>, object> removeHandler = (Action<TraceEvent> callback, object subscriptionId) => parser.RemoveCallback(callback, subscriptionId);
            TraceEventDispatcher source = (TraceEventDispatcher)parser.Source;
            return new TraceEventObservable<TraceEvent>(source, addHandler, removeHandler);
        }
        /// <summary>
        /// Returns an observable that observes all events from the event source 'source'
        /// 
        /// Note that unlike the methods on TraceEventParser, the TraceEvent object returned is already Cloned() and thus can be 
        /// referenced for as long as you like.  
        /// </summary>
        public static IObservable<TraceEvent> ObserveAll(this TraceEventDispatcher source)
        {
            Action<Action<TraceEvent>, object> addHandler = (Action<TraceEvent> callback, object subscriptionId) => source.AllEvents += callback;
            Action<Action<TraceEvent>, object> removeHandler = (Action<TraceEvent> callback, object subscriptionId) => source.AllEvents -= callback;
            return new TraceEventObservable<TraceEvent>(source, addHandler, removeHandler);
        }
        /// <summary>
        /// Returns an observable that observes all events from the event source 'source' which are not handled by a callback connected to 'source'
        /// 
        /// Note that unlike the methods on TraceEventParser, the TraceEvent object returned is already Cloned() and thus can be 
        /// referenced for as long as you like.  
        /// </summary>
        public static IObservable<TraceEvent> ObserveUnhandled(this TraceEventDispatcher source)
        {
            Action<Action<TraceEvent>, object> addHandler = (Action<TraceEvent> callback, object subscriptionId) => source.UnhandledEvents += callback;
            Action<Action<TraceEvent>, object> removeHandler = (Action<TraceEvent> callback, object subscriptionId) => source.UnhandledEvents -= callback;
            return new TraceEventObservable<TraceEvent>(source, addHandler, removeHandler);
        }

        #region private
        /// <summary>
        /// A TraceEventObservable is a helper class that implements the IObservable pattern for TraceEventDispatcher 
        /// (like ETWTraceEventDispatcher).  It is called from the TraceEventParser.Observe*{T} methods.  
        /// </summary>
        /// <typeparam name="T"></typeparam>
        internal class TraceEventObservable<T> : IObservable<T> where T : TraceEvent
        {
            public TraceEventObservable(TraceEventDispatcher source, Action<Action<T>, object> addHander, Action<Action<T>, object> removeHander)
            {
                m_source = source;
                m_addHander = addHander;
                m_removeHander = removeHander;
            }
            // Implement IObservable<T>
            public IDisposable Subscribe(IObserver<T> observer)
            {
                return new TraceEventSubscription(delegate (T data) { observer.OnNext((T)data.Clone()); }, observer.OnCompleted, this);
            }

            #region private
            /// <summary>
            /// A TraceEventSubscription is helper class that hooks 'callback' and 'completedCallback' to the 'observable' and 
            /// unhooks them when 'Dispose' is called.  
            /// </summary>
            private class TraceEventSubscription : IDisposable
            {
                internal TraceEventSubscription(Action<T> callback, Action completedCallback, TraceEventObservable<T> observable)
                {
                    // Remember stuff for 'dispose'
                    m_callback = callback;
                    m_completedCallback = completedCallback;
                    m_observable = observable;

                    // Add the event callback handler
                    m_observable.m_addHander(m_callback, this);
                    // And the comleted callback handler
                    m_observable.m_source.Completed += m_completedCallback;
                }
                public void Dispose()
                {
                    // Clean things up.  
                    m_observable.m_source.Completed -= m_completedCallback;
                    m_observable.m_removeHander(m_callback, this);
                }
                #region private
                private Action<T> m_callback;
                private Action m_completedCallback;
                private TraceEventObservable<T> m_observable;
                #endregion
            }

            private TraceEventDispatcher m_source;
            internal Action<Action<T>, object> m_removeHander;
            internal Action<Action<T>, object> m_addHander;
            #endregion
        }
        #endregion
    }
#endif

}
