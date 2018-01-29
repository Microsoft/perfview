﻿using FastSerialization;
using Microsoft.Diagnostics.Tracing.EventPipe;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Session;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

// See https://github.com/Microsoft/perfview/blob/master/src/TraceEvent/EventPipe/EventPipeFormat.md
// for details on the file format.  
namespace Microsoft.Diagnostics.Tracing
{
    /// <summary>
    /// EventPipeEventSource knows how to decode EventPipe (generated by the .NET 
    /// core runtime).    Please see 
    /// 
    /// By conventions files of such a format are given the .netperf suffix and are logically
    /// very much like a ETL file in that they have a header that indicete things about
    /// the trace as a whole, and a list of events.    Like more modern ETL files the
    /// file as a whole is self-describing.    Some of the events are 'MetaData' events
    /// that indicate the provider name, event name, and payload field names and types.   
    /// Ordinary events then point at these meta-data event so that logically all 
    /// events have a name some basic information (process, thread, timestamp, activity
    /// ID) and user defined field names and values of various types.  
    /// 
    /// See the E
    /// </summary>
    unsafe public class EventPipeEventSource : TraceEventDispatcher, IFastSerializable, IFastSerializableVersion
    {
        public EventPipeEventSource(string fileName)
        {
            _processName = "ProcessBeingTraced";
            osVersion = new Version("0.0.0.0");
            cpuSpeedMHz = 10;

            _deserializer = new Deserializer(new PinnedStreamReader(fileName, 0x20000), fileName);

            // This is only here for V2 and V1.  V3+ should use the name EventTrace, it can be removed when we drop support.
            _deserializer.RegisterFactory("Microsoft.DotNet.Runtime.EventPipeFile", delegate { return this; });
            _deserializer.RegisterFactory("EventTrace", delegate { return this; });
            _deserializer.RegisterFactory("EventBlock", delegate { return new EventPipeEventBlock(this); });


            var entryObj = _deserializer.GetEntryObject(); // this call invokes FromStream and reads header data

            // V3+ simply starts deserializing after the header object to parse the events.  
            if (3 <= _fileFormatVersionNumber)
                _objectsAfterHeaderObject = _deserializer.Current;

            // Because we told the deserialize to use 'this' when creating a EventPipeFile, we 
            // expect the entry object to be 'this'.
            Debug.Assert(entryObj == this);

            _eventParser = new EventPipeTraceEventParser(this);
        }

        #region private
        // I put these in the private section because they are overrides, and thus don't ADD to the API.  
        public override int EventsLost => 0;

        /// <summary>
        /// This is the version number reader and writer (although we don't don't have a writer at the moment)
        /// It MUST be updated (as well as MinimumReaderVersion), if breaking changes have been made.
        /// If your changes are forward compatible (old readers can still read the new format) you 
        /// don't have to update the version number but it is useful to do so (while keeping MinimumReaderVersion unchanged)
        /// so that readers can quickly determine what new content is available.  
        /// </summary>
        public int Version => 3;

        /// <summary>
        /// This field is only used for writers, and this code does not have writers so it is not used.
        /// It should be set to Version unless changes since the last version are forward compatible
        /// (old readers can still read this format), in which case this shoudl be unchanged.  
        /// </summary>
        public int MinimumReaderVersion => Version;

        /// <summary>
        /// This is the smallest version that the deserializer here can read.   Currently 
        /// we are careful about backward compat so our deserializer can read anything that
        /// has ever been produced.   We may change this when we believe old writers basically
        /// no longer exist (and we can remove that support code). 
        /// </summary>
        public int MinimumVersionCanRead => 0;

        protected override void Dispose(bool disposing)
        {
            _deserializer.Dispose();

            base.Dispose(disposing);
        }

        public override bool Process()
        {


            if (3 <= _fileFormatVersionNumber)
            {
                Debug.Assert(_deserializer.Reader.Current != _objectsAfterHeaderObject);

                // loop through the stream until we hit a null object.  Deserialization of 
                // EventPipeEventBlocks will cause dispatch to happen.  
                while (_deserializer.ReadObject() != null)
                { }
            }
            else
            {
                // This can be removed when we drop V1 and V2 support 
                PinnedStreamReader deserializerReader = (PinnedStreamReader)_deserializer.Reader;
                while (deserializerReader.Current < _endOfEventStream)
                {
                    TraceEventNativeMethods.EVENT_RECORD* eventRecord = ReadEvent(deserializerReader);
                    if (eventRecord != null)
                    {
                        // in the code below we set sessionEndTimeQPC to be the timestamp of the last event.  
                        // Thus the new timestamp should be later, and not more than 1 day later.  
                        Debug.Assert(sessionEndTimeQPC <= eventRecord->EventHeader.TimeStamp);
                        Debug.Assert(sessionEndTimeQPC == 0 || eventRecord->EventHeader.TimeStamp - sessionEndTimeQPC < _QPCFreq * 24 * 3600);

                        TraceEvent event_ = Lookup(eventRecord);
                        Dispatch(event_);
                        sessionEndTimeQPC = eventRecord->EventHeader.TimeStamp;
                    }
                }
            }

            return true;
        }

        internal override string ProcessName(int processID, long timeQPC)
        {
            return _processName;
        }

        internal TraceEventNativeMethods.EVENT_RECORD* ReadEvent(PinnedStreamReader reader)
        {
            // Guess that the event is < 1000 bytes or whatever is left in the stream.  
            int eventSizeGuess = Math.Min(1000, _endOfEventStream.Sub(reader.Current));
            EventPipeEventHeader* eventData = (EventPipeEventHeader*)reader.GetPointer(eventSizeGuess);
            // Basic sanity checks.  Are the timestamps and sizes sane.  
            Debug.Assert(sessionEndTimeQPC <= eventData->TimeStamp);
            Debug.Assert(sessionEndTimeQPC == 0 || eventData->TimeStamp - sessionEndTimeQPC < _QPCFreq * 24 * 3600);
            Debug.Assert(0 <= eventData->PayloadSize && eventData->PayloadSize <= eventData->TotalEventSize);
            Debug.Assert(0 < eventData->TotalEventSize && eventData->TotalEventSize < 0x20000);  // TODO really should be 64K but BulkSurvivingObjectRanges needs fixing.

            if (eventSizeGuess < eventData->TotalEventSize)
                eventData = (EventPipeEventHeader*)reader.GetPointer(eventData->TotalEventSize);

            Debug.Assert(0 <= EventPipeEventHeader.StackBytesSize(eventData) && EventPipeEventHeader.StackBytesSize(eventData) <= eventData->TotalEventSize);
            // This asserts that the header size + payload + stackSize field + StackSize == TotalEventSize;
            Debug.Assert(eventData->PayloadSize + EventPipeEventHeader.HeaderSize + sizeof(int) + EventPipeEventHeader.StackBytesSize(eventData) == eventData->TotalEventSize);

            TraceEventNativeMethods.EVENT_RECORD* ret = null;
            EventPipeEventMetaData metaData;
            if (eventData->MetaDataId == 0)     // Is this a Meta-data event?  
            {
#if DEBUG
                var eventStartLocation = reader.Current;
#endif
                int totalEventSize = eventData->TotalEventSize;
                int payloadSize = eventData->PayloadSize;
                // Note that this skip invalidates the eventData pointer, so it is important to pull any fields out we need first.  
                reader.Skip(EventPipeEventHeader.HeaderSize);
                metaData = new EventPipeEventMetaData(reader, payloadSize, _fileFormatVersionNumber, PointerSize, _processId);
                _eventMetadataDictionary.Add(metaData.MetaDataId, metaData);
                _eventParser.AddTemplate(metaData);
                int stackBytes = reader.ReadInt32();        // Meta-data events should always have a empty stack.  
                Debug.Assert(stackBytes == 0);

                // We have read all the bytes in the event
                Debug.Assert(reader.Current == eventStartLocation.Add(totalEventSize));
            }
            else
            {

                if (_eventMetadataDictionary.TryGetValue(eventData->MetaDataId, out metaData))
                    ret = metaData.GetEventRecordForEventData(eventData);
                else
                    Debug.Assert(false, "Warning can't find metaData for ID " + eventData->MetaDataId.ToString("x"));
                reader.Skip(eventData->TotalEventSize);
            }

            return ret;
        }

        internal override unsafe Guid GetRelatedActivityID(TraceEventNativeMethods.EVENT_RECORD* eventRecord)
        {
            // Recover the EventPipeEventHeader from the payload pointer and then fetch from the header.  
            EventPipeEventHeader* event_ = EventPipeEventHeader.HeaderFromPayloadPointer((byte*)eventRecord->UserData);
            return event_->RelatedActivityID;
        }

        // We dont ever serialize one of these in managed code so we don't need to implement ToSTream
        public void ToStream(Serializer serializer)
        {
            throw new NotImplementedException();
        }

        public void FromStream(Deserializer deserializer)
        {
            _fileFormatVersionNumber = deserializer.VersionBeingRead;

            if (deserializer.VersionBeingRead < 3)
            {
                ForwardReference reference = deserializer.ReadForwardReference();
                _endOfEventStream = deserializer.ResolveForwardReference(reference, preserveCurrent: true);
            }

            // The start time is stored as a SystemTime which is a bunch of shorts, convert to DateTime.  
            short year = deserializer.ReadInt16();
            short month = deserializer.ReadInt16();
            short dayOfWeek = deserializer.ReadInt16();
            short day = deserializer.ReadInt16();
            short hour = deserializer.ReadInt16();
            short minute = deserializer.ReadInt16();
            short second = deserializer.ReadInt16();
            short milliseconds = deserializer.ReadInt16();
            _syncTimeUTC = new DateTime(year, month, day, hour, minute, second, milliseconds, DateTimeKind.Utc);
            deserializer.Read(out _syncTimeQPC);
            deserializer.Read(out _QPCFreq);

            sessionStartTimeQPC = _syncTimeQPC;

            if (3 <= deserializer.VersionBeingRead)
            {
                deserializer.Read(out pointerSize);
                deserializer.Read(out _processId);
                deserializer.Read(out numberOfProcessors);
                deserializer.Read(out _expectedCPUSamplingRate);
            }
            else
            {
                _processId = 0; // V1 && V2 tests expect 0 for process Id
                pointerSize = 8; // V1 EventPipe only supports Linux which is x64 only.
                numberOfProcessors = 1;
            }
        }

        int _fileFormatVersionNumber;
        StreamLabel _objectsAfterHeaderObject;
        StreamLabel _endOfEventStream;                  // Only needed for < V2 support. 

        Dictionary<int, EventPipeEventMetaData> _eventMetadataDictionary = new Dictionary<int, EventPipeEventMetaData>();
        Deserializer _deserializer;
        EventPipeTraceEventParser _eventParser; // TODO does this belong here?
        string _processName;
        internal int _processId;
        internal int _expectedCPUSamplingRate;

        #endregion
    }

    #region private classes

    /// <summary>
    /// An EVentPipeEventBlock represents a block of events.   It basicaly only has
    /// one field, which is the size in bytes of the block.  But when its FromStream
    /// is called, it will perform the callbacks for the events (thus deserializing
    /// it performs dispatch).  
    /// </summary>
    internal class EventPipeEventBlock : IFastSerializable
    {
        public EventPipeEventBlock(EventPipeEventSource source)
        {
            _source = source;
        }
        unsafe public void FromStream(Deserializer deserializer)
        {
            // blockSizeInBytes INCLUDES any padding bytes to insure alignment.  
            var blockSizeInBytes = deserializer.ReadInt();
            _startEventData = deserializer.Current;
            _endEventData = _startEventData.Add(blockSizeInBytes);

            // Dispatch through all the events.  
            PinnedStreamReader deserializerReader = (PinnedStreamReader)deserializer.Reader;

            // Align to a 4 byte boundary
            byte* ptr = deserializerReader.GetPointer(4);
            deserializerReader.Skip((-((int)ptr)) % 4);  

            while (deserializerReader.Current < _endEventData)
            {
                TraceEventNativeMethods.EVENT_RECORD* eventRecord = _source.ReadEvent(deserializerReader);
                if (eventRecord != null)
                {
                    // in the code below we set sessionEndTimeQPC to be the timestamp of the last event.  
                    // Thus the new timestamp should be later, and not more than 1 day later.  
                    Debug.Assert(_source.sessionEndTimeQPC <= eventRecord->EventHeader.TimeStamp);
                    Debug.Assert(_source.sessionEndTimeQPC == 0 || eventRecord->EventHeader.TimeStamp - _source.sessionEndTimeQPC < _source._QPCFreq * 24 * 3600);

                    TraceEvent event_ = _source.Lookup(eventRecord);
                    _source.Dispatch(event_);
                    _source.sessionEndTimeQPC = eventRecord->EventHeader.TimeStamp;
                }
            }
        }

        public void ToStream(Serializer serializer)
        {
            throw new NotImplementedException();
        }

        StreamLabel _startEventData;
        StreamLabel _endEventData;
        EventPipeEventSource _source;
    }


    /// <summary>
    /// Private utility class.
    /// 
    /// An EventPipeEventMetaData holds the information that can be shared among all
    /// instances of an EventPIpe event from a particular provider.   Thus it contains
    /// things like the event name, provider, as well as well as data on how many
    /// user defined fields and their names and types.   
    /// 
    /// This class has two main functions
    ///    1. The constructor takes a PinnedStreamReader and decodes the serialized metadata
    ///       so you can access the data conviniently.
    ///    2. It remembers a EVENT_RECORD structure (from ETW) that contains this data)
    ///       and has a function GetEventRecordForEventData which converts from a 
    ///       EventPipeEventHeader (the raw serialized data) to a EVENT_RECORD (which
    ///       is what TraceEvent needs to look up the event an pass it up the stack.  
    /// </summary>
    unsafe class EventPipeEventMetaData
    {
        /// <summary>
        /// Creates a new MetaData instance from the serialized data at the current position of 'reader'
        /// of length 'length'.   This typically points at the PAYLOAD AREA of a meta-data events)
        /// 'fileFormatVersionNumber' is the version number of the file as a whole
        /// (since that affects the parsing of this data) and 'processID' is the process ID for the 
        /// whole stream (since it needs to be put into the EVENT_RECORD.
        /// 
        /// When this constructor returns the reader has read all data given to it (thus it has
        /// move the read pointer by 'length')
        /// </summary>
        public EventPipeEventMetaData(PinnedStreamReader reader, int length, int fileFormatVersionNumber, int pointerSize, int processId)
        {
            // Get the event record and fill in fields that we can without deserializing anything.  
            _eventRecord = (TraceEventNativeMethods.EVENT_RECORD*)Marshal.AllocHGlobal(sizeof(TraceEventNativeMethods.EVENT_RECORD));
            ClearMemory(_eventRecord, sizeof(TraceEventNativeMethods.EVENT_RECORD));

            if (pointerSize == 4)
                _eventRecord->EventHeader.Flags = TraceEventNativeMethods.EVENT_HEADER_FLAG_32_BIT_HEADER;
            else
                _eventRecord->EventHeader.Flags = TraceEventNativeMethods.EVENT_HEADER_FLAG_64_BIT_HEADER;

            _eventRecord->EventHeader.ProcessId = processId;

            // Read the metaData
            StreamLabel eventDataEnd = reader.Current.Add(length);
            if (3 <= fileFormatVersionNumber)
            {
                MetaDataId = reader.ReadInt32();
                ProviderName = reader.ReadNullTerminatedUnicodeString();
                ReadEventMetaData(reader, fileFormatVersionNumber);
            }
            else
                ReadObsoleteEventMetaData(reader, fileFormatVersionNumber);

            Debug.Assert(reader.Current == eventDataEnd);
        }

        /// <summary>
        /// Given a EventPipeEventHeader takes a EventPipeEventHeader that is specific to an event, copies it
        /// on top of the static information in its EVENT_RECORD which is specialized this this meta-data 
        /// and returns a pinter to it.  Thus this makes the EventPipe look like an ETW provider from
        /// the point of view of the upper level TraceEvent logic.  
        /// </summary>
        internal TraceEventNativeMethods.EVENT_RECORD* GetEventRecordForEventData(EventPipeEventHeader* eventData)
        {

            // We have already initialize all the fields of _eventRecord that do no vary from event to event. 
            // Now we only have to copy over the fields that are specific to particular event.  
            _eventRecord->EventHeader.ThreadId = eventData->ThreadId;
            _eventRecord->EventHeader.TimeStamp = eventData->TimeStamp;
            _eventRecord->EventHeader.ActivityId = eventData->ActivityID;
            // EVENT_RECORD does not field for ReleatedActivityID (because it is rarely used).  See GetRelatedActivityID;
            _eventRecord->UserDataLength = (ushort)eventData->PayloadSize;

            // TODO the extra || operator is a hack becase the runtime actually tries to emit events that
            // exceed this for the GC/BulkSurvivingObjectRanges (event id == 21).  We supress that assert 
            // for now but this is a real bug in the runtime's event logging.  ETW can't handle payloads > 64K.  
            Debug.Assert(_eventRecord->UserDataLength == eventData->PayloadSize ||
                _eventRecord->EventHeader.ProviderId == ClrTraceEventParser.ProviderGuid && _eventRecord->EventHeader.Id == 21);
            _eventRecord->UserData = (IntPtr)eventData->Payload;

            int stackBytesSize = EventPipeEventHeader.StackBytesSize(eventData);

            // TODO remove once .NET Core has been fixed to not emit stacks on CLR method events which are just for bookeeping.  
            if (ProviderId == ClrRundownTraceEventParser.ProviderGuid ||
               (ProviderId == ClrTraceEventParser.ProviderGuid && (140 <= EventId && EventId <= 144 || EventId == 190)))     // These are various CLR method Events.  
                stackBytesSize = 0;

            if (0 < stackBytesSize)
            {
                // Lazy allocation (destructor frees it). 
                if (_eventRecord->ExtendedData == null)
                    _eventRecord->ExtendedData = (TraceEventNativeMethods.EVENT_HEADER_EXTENDED_DATA_ITEM*)Marshal.AllocHGlobal(sizeof(TraceEventNativeMethods.EVENT_HEADER_EXTENDED_DATA_ITEM));

                if ((_eventRecord->EventHeader.Flags & TraceEventNativeMethods.EVENT_HEADER_FLAG_32_BIT_HEADER) != 0)
                    _eventRecord->ExtendedData->ExtType = TraceEventNativeMethods.EVENT_HEADER_EXT_TYPE_STACK_TRACE32;
                else
                    _eventRecord->ExtendedData->ExtType = TraceEventNativeMethods.EVENT_HEADER_EXT_TYPE_STACK_TRACE64;

                // DataPtr should point at a EVENT_EXTENDED_ITEM_STACK_TRACE*.  These have a ulong MatchID field which is NOT USED before the stack data.
                // Since that field is not used, I can backup the pointer by 8 bytes and synthesize a EVENT_EXTENDED_ITEM_STACK_TRACE from the raw buffer 
                // of stack data without having to copy.  
                _eventRecord->ExtendedData->DataSize = (ushort)(stackBytesSize + 8);
                _eventRecord->ExtendedData->DataPtr = (ulong)(EventPipeEventHeader.StackBytes(eventData) - 8);

                _eventRecord->ExtendedDataCount = 1;        // Mark that we have the stack data.  
            }
            else
                _eventRecord->ExtendedDataCount = 0;

            return _eventRecord;
        }

        /// <summary>
        /// This is a number that is unique to this meta-data blob.  It is expected to be a small integer
        /// that starts at 1 (since 0 is reserved) and increases from there (thus an array can be used).  
        /// It is what is matched up with EventPipeEventHeader.MetaDataId
        /// </summary>
        public int MetaDataId { get; private set; }
        public string ProviderName { get; private set; }
        public string EventName { get; private set; }
        public Tuple<TypeCode, string>[] ParameterDefinitions { get; private set; }
        public Guid ProviderId { get { return _eventRecord->EventHeader.ProviderId; } }
        public int EventId { get { return _eventRecord->EventHeader.Id; } }
        public int Version { get { return _eventRecord->EventHeader.Version; } }
        public ulong Keywords { get { return _eventRecord->EventHeader.Keyword; } }
        public int Level { get { return _eventRecord->EventHeader.Level; } }

        #region private 

        /// <summary>
        /// Reads the meta data for information specific to one event.  
        /// </summary>
        private void ReadEventMetaData(PinnedStreamReader reader, int fileFormatVersionNumber)
        {
            int eventId = (ushort)reader.ReadInt32();
            _eventRecord->EventHeader.Id = (ushort)eventId;
            Debug.Assert(_eventRecord->EventHeader.Id == eventId);  // No truncation

            EventName = reader.ReadNullTerminatedUnicodeString();

            // Deduce the opcode from the name.   
            if (EventName.EndsWith("Start", StringComparison.OrdinalIgnoreCase))
                _eventRecord->EventHeader.Opcode = (byte)TraceEventOpcode.Start;
            else if (EventName.EndsWith("Stop", StringComparison.OrdinalIgnoreCase))
                _eventRecord->EventHeader.Opcode = (byte)TraceEventOpcode.Stop;

            _eventRecord->EventHeader.Keyword = (ulong)reader.ReadInt64();

            int version = reader.ReadInt32();
            _eventRecord->EventHeader.Version = (byte)version;
            Debug.Assert(_eventRecord->EventHeader.Version == version);  // No truncation

            _eventRecord->EventHeader.Level = (byte)reader.ReadInt32();
            Debug.Assert(_eventRecord->EventHeader.Level <= 5);

            // Fetch the parameter information
            int parameterCount = reader.ReadInt32();
            Debug.Assert(0 <= parameterCount && parameterCount < 0x4000); 
            if (0 < parameterCount)
            {
                ParameterDefinitions = new Tuple<TypeCode, string>[parameterCount];
                for (int i = 0; i < parameterCount; i++)
                {
                    var type = (TypeCode)reader.ReadInt32();
                    Debug.Assert((uint)type < 24);      // There only a handful of type codes. 
                    var name = reader.ReadNullTerminatedUnicodeString();
                    ParameterDefinitions[i] = new Tuple<TypeCode, string>(type, name);
                }
            }
        }

        private void ReadObsoleteEventMetaData(PinnedStreamReader reader, int fileFormatVersionNumber)
        {
            Debug.Assert(fileFormatVersionNumber < 3);

            // Old versions use the stream offset as the MetaData ID, but the reader has advanced to the payload so undo it.  
            MetaDataId = ((int)reader.Current) - EventPipeEventHeader.HeaderSize;

            if (fileFormatVersionNumber == 1)
                _eventRecord->EventHeader.ProviderId = reader.ReadGuid();
            else
            {
                ProviderName = reader.ReadNullTerminatedUnicodeString();
                _eventRecord->EventHeader.ProviderId = GetProviderGuidFromProviderName(ProviderName);
            }

            var eventId = (ushort)reader.ReadInt32();
            _eventRecord->EventHeader.Id = eventId;
            Debug.Assert(_eventRecord->EventHeader.Id == eventId);  // No truncation

            var version = reader.ReadInt32();
            _eventRecord->EventHeader.Version = (byte)version;
            Debug.Assert(_eventRecord->EventHeader.Version == version);  // No truncation

            int metadataLength = reader.ReadInt32();
            if (0 < metadataLength)
                ReadEventMetaData(reader, fileFormatVersionNumber);
        }

        ~EventPipeEventMetaData()
        {
            if (_eventRecord != null)
            {
                if (_eventRecord->ExtendedData != null)
                    Marshal.FreeHGlobal((IntPtr)_eventRecord->ExtendedData);
                Marshal.FreeHGlobal((IntPtr)_eventRecord);
                _eventRecord = null;
            }

        }

        private void ClearMemory(void* buffer, int length)
        {
            byte* ptr = (byte*)buffer;
            while (length > 0)
            {
                *ptr++ = 0;
                --length;
            }

        }
        public static Guid GetProviderGuidFromProviderName(string name)
        {
            if (String.IsNullOrEmpty(name))
            {
                return Guid.Empty;
            }

            // Legacy GUID lookups (events which existed before the current Guid generation conventions)
            if (name == TplEtwProviderTraceEventParser.ProviderName)
            {
                return TplEtwProviderTraceEventParser.ProviderGuid;
            }
            else if (name == ClrTraceEventParser.ProviderName)
            {
                return ClrTraceEventParser.ProviderGuid;
            }
            else if (name == ClrPrivateTraceEventParser.ProviderName)
            {
                return ClrPrivateTraceEventParser.ProviderGuid;
            }
            else if (name == ClrRundownTraceEventParser.ProviderName)
            {
                return ClrRundownTraceEventParser.ProviderGuid;
            }
            else if (name == ClrStressTraceEventParser.ProviderName)
            {
                return ClrStressTraceEventParser.ProviderGuid;
            }
            else if (name == FrameworkEventSourceTraceEventParser.ProviderName)
            {
                return FrameworkEventSourceTraceEventParser.ProviderGuid;
            }
            // Needed as long as eventpipeinstance v1 objects are supported
            else if (name == SampleProfilerTraceEventParser.ProviderName)
            {
                return SampleProfilerTraceEventParser.ProviderGuid;
            }

            // Hash the name according to current event source naming conventions
            else
            {
                return TraceEventProviders.GetEventSourceGuidFromName(name);
            }
        }


        TraceEventNativeMethods.EVENT_RECORD* _eventRecord;
        #endregion
    }

    /// <summary>
    /// Private utilty class.
    /// 
    /// At the start of every event from an EventPipe is a header that contains
    /// common fields like its size, threadID timestamp etc.  EventPipeEventHeader
    /// is the layout of this.  Events have two variable sized parts: the user
    /// defined fields, and the stack.   EventPipEventHeader knows how to 
    /// decode these pieces (but provides no semantics for it. 
    /// 
    /// It is not a public type, but used in low level parsing of EventPipeEventSource.  
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    unsafe struct EventPipeEventHeader
    {
        private int EventSize;          // Size bytes of this header and the payload and stacks if any.  does NOT incode the size of the EventSize field itself. 
        public int MetaDataId;          // a number identifying the description of this event.  
        public int ThreadId;
        public long TimeStamp;
        public Guid ActivityID;
        public Guid RelatedActivityID;
        public int PayloadSize;         // size in bytes of the user defined payload data. 
        public fixed byte Payload[4];   // Actually of variable size.  4 is used to avoid potential alignment issues.   This 4 also appears in HeaderSize below. 

        public int TotalEventSize { get { return EventSize + sizeof(int); } } // Includes the size of the EventSize field itself 

        /// <summary>
        /// Header Size is defined to be the number of bytes before the Payload bytes.  
        /// </summary>
        static public int HeaderSize { get { return sizeof(EventPipeEventHeader) - 4; } }
        static public EventPipeEventHeader* HeaderFromPayloadPointer(byte* payloadPtr) { return (EventPipeEventHeader*)(payloadPtr - HeaderSize); }

        static public int StackBytesSize(EventPipeEventHeader* header)
        {
            return *((int*)(&header->Payload[header->PayloadSize]));
        }
        static public byte* StackBytes(EventPipeEventHeader* header)
        {
            return (byte*)(&header->Payload[header->PayloadSize + 4]);
        }
    }
    #endregion
}
