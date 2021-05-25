//     Copyright (c) Microsoft Corporation.  All rights reserved.
using FastSerialization;
using Microsoft.Diagnostics.Tracing.Compatibility;
using Microsoft.Diagnostics.Tracing.EventPipe;
using Microsoft.Diagnostics.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Address = System.UInt64;

namespace Microsoft.Diagnostics.Tracing.Parsers
{
    /// <summary>
    /// A DynamicTraceEventParser is a parser that understands how to read the embedded manifests that occur in the 
    /// dataStream (System.Diagnostics.Tracing.EventSources do this).   
    /// 
    /// See also TDHDynamicTraceEventParser which knows how to read the manifest that are registered globally with
    /// the machine.   
    /// </summary>
    public class DynamicTraceEventParser : TraceEventParser
    {
        /// <summary>
        /// The event ID for the EventSource manifest emission event.
        /// </summary>
        public const TraceEventID ManifestEventID = (TraceEventID)0xFFFE;

        /// <summary>
        /// Create a new DynamicTraceEventParser (which can parse ETW providers that dump their manifests
        /// to the ETW data stream) an attach it to the ETW data stream 'source'.  
        /// </summary>
        public DynamicTraceEventParser(TraceEventSource source)
            : base(source)
        {
            // Try to retrieve persisted state 
            state = (DynamicTraceEventParserState)StateObject;
            if (state == null)
            {
                StateObject = state = new DynamicTraceEventParserState();
                partialManifests = new Dictionary<Guid, List<PartialManifestInfo>>();
                this.source.RegisterUnhandledEvent(CheckForDynamicManifest);
            }

            // make a registeredParser to resolve self-describing events (and more).  
            registeredParser = new RegisteredTraceEventParser(source, true);
            // But cause any of its new definitions to work on my subscriptions.  
            registeredParser.NewEventDefinition = OnNewEventDefintion;
            // make an eventPipeTraceEventParser to resolve EventPipe events
            eventPipeTraceEventParser = new EventPipeTraceEventParser(source, dontRegister: true);
            eventPipeTraceEventParser.NewEventDefinition = OnNewEventDefintion;
        }

        /// <summary>
        /// Returns a list of providers (their manifest) that this TraceParser knows about.   
        /// </summary>
        public IEnumerable<ProviderManifest> DynamicProviders
        {
            get
            {
                return state.providers.Values;
            }
        }

        /// <summary>
        /// Given a manifest describing the provider add its information to the parser.  
        /// </summary>
        public void AddDynamicProvider(ProviderManifest providerManifest, bool noThrowOnError = false)
        {
            // Debug.WriteLine("callback count = " + ((source is ETWTraceEventSource) ? ((ETWTraceEventSource)source).CallbackCount() : -1));
            // Trace.WriteLine("Dynamic: Found provider " + providerManifest.Name + " Guid " + providerManifest.Guid);

            ProviderManifest prevManifest = null;
            if (state.providers.TryGetValue(providerManifest.Guid, out prevManifest))
            {
                // If the new manifest is not strictly better than the one we already have, ignore it.   
                if (!providerManifest.BetterThan(prevManifest))
                {
                    // Trace.WriteLine("Dynamic: existing manifest just as good, returning");
                    return;
                }
            }

            // Register the new definitions. 
            providerManifest.ParseProviderEvents(delegate (DynamicTraceEventData template)
            {
                return OnNewEventDefintion(template, prevManifest != null);
            }, noThrowOnError);

            // Remember this serialized information.(do it afterward so ContainKey call above is accurate)
            state.providers[providerManifest.Guid] = providerManifest;

            // Register the manifest event with myself so that I continue to get updated manifests.  
            // TODO we are 'leaking' these today.  Clean them up on Dispose.  
            var callback = new DynamicManifestTraceEventData(delegate (TraceEvent data) { CheckForDynamicManifest(data); }, providerManifest);
            source.RegisterEventTemplate(callback);

            // Raise the event that says we found a new provider.   
            var newProviderCallback = DynamicProviderAdded;
            if (newProviderCallback != null)
            {
                newProviderCallback(providerManifest);
            }

            // Debug.WriteLine("callback count = " + ((source is ETWTraceEventSource) ? ((ETWTraceEventSource)source).CallbackCount() : -1));
            // Trace.WriteLine("Dynamic finished registering " + providerManifest.Name);
        }

        /// <summary>
        /// Utility method that stores all the manifests known to the DynamicTraceEventParser to the directory 'directoryPath'
        /// </summary>
        public void WriteAllManifests(string directoryPath)
        {
            Directory.CreateDirectory(directoryPath);
            foreach (var providerManifest in DynamicProviders)
            {
                var filePath = Path.Combine(directoryPath, providerManifest.Name + ".manifest.xml");
                providerManifest.WriteToFile(filePath);
            }
        }

        /// <summary>
        /// Utility method that read all the manifests the directory 'directoryPath' into the parser.   
        /// Manifests must end in a .man or .manifest.xml suffix.   It will throw an error if
        /// the manifest is incorrect or using unsupported options.  
        /// </summary>        
        public void ReadAllManifests(string directoryPath)
        {
            foreach (var fileName in Directory.GetFiles(directoryPath, "*.manifest.xml"))
            {
                AddDynamicProvider(new ProviderManifest(fileName));
            }
            foreach (var fileName in Directory.GetFiles(directoryPath, "*.man"))
            {
                AddDynamicProvider(new ProviderManifest(fileName));
            }
        }

        /// <summary>
        /// Override.  
        /// </summary>
        public override bool IsStatic { get { return false; } }

        /// <summary>
        /// This event, will be fired any time a new Provider is added to the table
        /// of ETW providers known to this DynamicTraceEventParser.   This includes
        /// when the EventSource manifest events are encountered as well as any
        /// explicit calls to AddDynamicProvider.  (including ReadAllManifests).
        /// 
        /// The Parser will filter out duplicate manifest events, however if an
        /// old version of a provider's manifest is encountered, and later a newer
        /// version is encountered, you can receive this event more than once for
        /// a single provider.  
        /// </summary>
        public event Action<ProviderManifest> DynamicProviderAdded;

#if false 
        public event Action<EventCounterTraceData> EventCounter
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                // RegisterTemplate(new EventCounterTraceData(value, 0xFFFD, 0xFFFE, "EventSource", Guid.Empty, 253, "EventCounter", ProviderGuid, ProviderName));
            }
            remove
            {
                // source.UnregisterEventTemplate(value, 15, ProviderGuid);
            }
        }
#endif

        #region private
        /// <summary>
        /// override
        /// </summary>
        protected override string GetProviderName()
        {
            // This parser covers more than one provider, so the convention is that you return null for the provider name. 
            return null;
        }

        /// <summary>
        /// Called on unhandled events to look for manifests.    Returns true if we added a new manifest (which may have updated the lookup table)
        /// </summary>
        private bool CheckForDynamicManifest(TraceEvent data)
        {
            if (data.ID != ManifestEventID)
            {
                return false;
            }

            // We also are expecting only these tasks and opcodes.  
            if (data.Opcode != (TraceEventOpcode)0xFE || data.Task != (TraceEventTask)0xFFFE)
            {
                return false;
            }

            // Look up our information. 
            List<PartialManifestInfo> partialManifestsForGuid;
            if (!partialManifests.TryGetValue(data.ProviderGuid, out partialManifestsForGuid))
            {
                partialManifestsForGuid = new List<PartialManifestInfo>();
                partialManifests.Add(data.ProviderGuid, partialManifestsForGuid);
            }

            PartialManifestInfo partialManifest = null;
            // PERF: Expansion of 
            //    partialManifest = partialManifestsForGuid.Find(e => data.ProcessID == e.ProcessID && data.ThreadID == e.ThreadID);
            // that avoids the delegate allocation.
            foreach (var p in partialManifestsForGuid)
            {
                if (p.ProcessID == data.ProcessID && p.ThreadID == data.ThreadID)
                {
                    partialManifest = p;
                    break;
                }
            }

            if (partialManifest == null)
            {
                partialManifest = new PartialManifestInfo() { ProcessID = data.ProcessID, ThreadID = data.ThreadID };
                partialManifestsForGuid.Add(partialManifest);
            }

            ProviderManifest provider = partialManifest.AddChunk(data);
            // We have a completed manifest, add it to our list.  
            if (provider != null)
            {
                partialManifestsForGuid.Remove(partialManifest);

                // Throw away empty lists or lists that are old
                var nowUtc = DateTime.UtcNow;
                if (partialManifestsForGuid.Count == 0 || partialManifestsForGuid.TrueForAll(e => (nowUtc - e.StartedUtc).TotalSeconds > 10))
                {
                    partialManifests.Remove(data.ProviderGuid);
                }

                AddDynamicProvider(provider, true);
                return true;  // I should have added a manifest event, so re-lookup the event 
            }
            return false;
        }

        /// <summary>
        /// Override 
        /// </summary>
        protected internal override void EnumerateTemplates(Func<string, string, EventFilterResponse> eventsToObserve, Action<TraceEvent> callback)
        {
            // Normally state is setup in the constructor, but call can be invoked before the constructor has finished, 
            if (state == null)
            {
                state = (DynamicTraceEventParserState)StateObject;
            }

            foreach (var provider in state.providers.Values)
            {
                provider.ParseProviderEvents(delegate (DynamicTraceEventData template)
                {
                    if (eventsToObserve != null)
                    {
                        var response = eventsToObserve(template.ProviderName, template.EventName);
                        if (response != EventFilterResponse.AcceptEvent)
                        {
                            return response;
                        }
                    }

                    // We should only return a particular template at most once.   
                    // However registredParser can overlap with dynamicParser 
                    // (this can happen if the ETL was merged and has KernelTraceControler events for the provider
                    // or someone registers an EventSource with the OS).     
                    // Since we will Enumerate all the events the registeredParser knows
                    // about below, we filter out any duplicates here. 
                    if (!registeredParser.HasDefinitionForTemplate(template))
                    {
                        callback(template);
                    }

                    return EventFilterResponse.AcceptEvent;
                }, true);
            }
            // also enumerate any events from the registeredParser.  
            registeredParser.EnumerateTemplates(eventsToObserve, callback);

            // also enumerate any events from the eventPipeTraceEventParser
            eventPipeTraceEventParser.EnumerateTemplates(eventsToObserve, callback);
        }

        private class PartialManifestInfo
        {
            internal PartialManifestInfo() { StartedUtc = DateTime.UtcNow; }

            internal DateTime StartedUtc;    // When we started
            private byte[][] Chunks;
            private int ChunksLeft;
            internal int ProcessID;          // The process and thread ID that is emitting this manifest (acts as a stream ID)
            internal int ThreadID;           // The process and thread ID that is emitting this manifest (acts as a stream ID)

            private ProviderManifest provider;
            private byte majorVersion;
            private byte minorVersion;
            private ManifestEnvelope.ManifestFormats format;

            internal unsafe ProviderManifest AddChunk(TraceEvent data)
            {
                if (provider != null)
                {
                    goto Fail;
                }

                if (data.EventDataLength <= sizeof(ManifestEnvelope) || data.GetByteAt(3) != 0x5B)  // magic number 
                {
                    goto Fail;
                }

                ushort totalChunks = (ushort)data.GetInt16At(4);
                ushort chunkNum = (ushort)data.GetInt16At(6);
                if (chunkNum >= totalChunks || totalChunks == 0)
                {
                    goto Fail;
                }

                if (Chunks == null)
                {
                    // To allow for resyncing at 0, otherwise we fail aggressively. 
                    if (chunkNum != 0)
                    {
                        goto Fail;
                    }

                    format = (ManifestEnvelope.ManifestFormats)data.GetByteAt(0);
                    majorVersion = (byte)data.GetByteAt(1);
                    minorVersion = (byte)data.GetByteAt(2);
                    ChunksLeft = totalChunks;
                    Chunks = new byte[ChunksLeft][];
                }
                else
                {
                    // Chunks have to agree with the format and version information. 
                    if (format != (ManifestEnvelope.ManifestFormats)data.GetByteAt(0) ||
                        majorVersion != data.GetByteAt(1) || minorVersion != data.GetByteAt(2))
                    {
                        goto Fail;
                    }
                }

                if (Chunks.Length <= chunkNum || Chunks[chunkNum] != null)
                {
                    goto Fail;
                }

                byte[] chunk = new byte[data.EventDataLength - 8];
                Chunks[chunkNum] = data.EventData(chunk, 0, 8, chunk.Length);
                --ChunksLeft;
                if (ChunksLeft > 0)
                {
                    return null;
                }

                // OK we have a complete set of chunks
                byte[] serializedData = Chunks[0];
                if (Chunks.Length > 1)
                {
                    int totalLength = 0;
                    for (int i = 0; i < Chunks.Length; i++)
                    {
                        totalLength += Chunks[i].Length;
                    }

                    // Concatenate all the arrays. 
                    serializedData = new byte[totalLength];
                    int pos = 0;
                    for (int i = 0; i < Chunks.Length; i++)
                    {
                        Array.Copy(Chunks[i], 0, serializedData, pos, Chunks[i].Length);
                        pos += Chunks[i].Length;
                    }
                }
                Chunks = null;
                // string str = Encoding.UTF8.GetString(serializedData);
                provider = new ProviderManifest(serializedData, format, majorVersion, minorVersion,
                    "Event at " + data.TimeStampRelativeMSec.ToString("f3") + " MSec");
                provider.ISDynamic = true;
                return provider;

                Fail:
                Chunks = null;
                return null;
            }
        }

        private DynamicTraceEventParserState state;
        private Dictionary<Guid, List<PartialManifestInfo>> partialManifests;

        // It is not intuitive that self-describing events (which are arguably 'dynamic') are resolved by 
        // the RegisteredTraceEventParser.  This is even more wacky in a mixed EventSource where some events 
        // are resolved by dynamic manifest and some are self-describing.     To avoid these issues DynamicTraceEventParsers
        // be able to handle both (it can resolve anything a RegisteredTraceEventParser can).  This 
        // RegisteredTraceEventParser is how this gets accomplished.   
        private RegisteredTraceEventParser registeredParser;

        // It is enabling DynamicTraceEventParsers to handle the EventSource events from EventPipe.
        private EventPipeTraceEventParser eventPipeTraceEventParser;

        #endregion
    }

#if false 
    public sealed class EventCounterTraceData : TraceEvent
    {
        public int DataCount { get { return GetInt32At(0); } }
        public double Data(int index) { return GetDoubleAt(4 + index * 8); }
        public int NamesCount { get { return GetInt32At(4 + 8 * DataCount); } }
        public string Names(int index) { return GetUnicodeStringAt(OffsetForIndexInNamesArray(index)); }

    #region Private
        internal EventCounterTraceData(Action<EventCounterTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            this.Action = action;
            m_lastIdx = 0xFFFF; // Invalidate the cache
        }
        protected internal override void Dispatch()
        {
            m_lastIdx = 0xFFFF; // Invalidate the cache
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<EventCounterTraceData>)value; }
        }
        protected internal override void Validate()
        {
            m_lastIdx = 0xFFFF; // Invalidate the cache     
            Debug.Assert(!(Version == 0 && EventDataLength != OffsetForIndexInNamesArray(NamesCount)));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "DataCount", DataCount);
            XmlAttrib(sb, "NamesCount", NamesCount);
            sb.AppendLine(">");
            for (int i = 0; i < DataCount; i++)
            {
                string name = "";
                if (i < NamesCount)
                    name = Names(i);
                sb.Append("  ").AppendLine(name).Append("=").Append(Data(i)).AppendLine();
            }
            sb.Append("</Event>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[] { "DataCount", "NamesCount" };
                return payloadNames;
            }
        }
        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return DataCount;
                case 1:
                    return NamesCount;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private int OffsetForIndexInNamesArray(int targetIdx)
        {
            Debug.Assert(targetIdx <= NamesCount);
            int idx = m_lastIdx;
            int offset = m_lastOffset;
            if (targetIdx < idx)
            {
                idx = 0;
                offset = 8 + 8 * DataCount;
            }

            while (idx < targetIdx)
            {
                offset = SkipUnicodeString(offset);
                idx++;
            }
            Debug.Assert(offset <= EventDataLength);
            m_lastIdx = (ushort)idx;
            m_lastOffset = (ushort)offset;
            Debug.Assert(idx == targetIdx);
            Debug.Assert(m_lastIdx == targetIdx && m_lastOffset == offset);     // No truncation
            return offset;
        }
        private ushort m_lastIdx;
        private ushort m_lastOffset;

        private event Action<EventCounterTraceData> Action;

    #endregion
    }
#endif

    #region internal classes
    /// <summary>
    /// DynamicTraceEventData is an event that knows how to take runtime information to parse event fields (and payload)
    /// 
    /// This meta-data is distilled down to a array of field names and an array of PayloadFetches which contain enough
    /// information to find the field data in the payload blob.   This meta-data is used in the 
    /// DynamicTraceEventData.PayloadNames and DynamicTraceEventData.PayloadValue methods.  
    /// </summary>
    internal class DynamicTraceEventData : TraceEvent, IFastSerializable
    {
        internal DynamicTraceEventData(Action<TraceEvent> target, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            m_target = target;
        }

        internal event Action<TraceEvent> m_target;

        #region overrides
        /// <summary>
        /// Implements TraceEvent interface
        /// </summary>
        protected internal override void Dispatch()
        {
            if (m_target != null)
            {
                m_target(this);
            }
        }
        /// <summary>
        /// Implements TraceEvent interface
        /// </summary>
        public override string[] PayloadNames
        {
            get { Debug.Assert(payloadNames != null); return payloadNames; }
        }
        /// <summary>
        /// Implements TraceEvent interface
        /// </summary>
        public override object PayloadValue(int index)
        {
            try
            {
#if DEBUG
                // Confirm that the serialization 'adds up'
                var computedSize = SkipToField(payloadFetches, payloadFetches.Length, 0, EventDataLength, false);
                Debug.Assert(computedSize <= this.EventDataLength);
                if ((int)ID != 0xFFFE) // If it is not a manifest event
                {
                    // TODO FIX NOW the || condition is a hack because PerfVIew.ClrEnableParameters fails.  
                    Debug.Assert(computedSize <= this.EventDataLength || this.ProviderName == "PerfView");
                }
#endif
                int offset = payloadFetches[index].Offset;
                if (offset == ushort.MaxValue)
                {
                    offset = SkipToField(payloadFetches, index, 0, EventDataLength, true);
                }

                // Fields that are simply not present, (perfectly) we simply return null for.  
                if (offset == EventDataLength)
                {
                    return null;
                }

                return GetPayloadValueAt(ref payloadFetches[index], offset, EventDataLength);
            }
            catch (Exception e)
            {
                return "<<<EXCEPTION_DURING_VALUE_LOOKUP " + e.GetType().Name + ">>>";
            }
        }

        private object GetPayloadValueAt(ref PayloadFetch payloadFetch, int offset, int payloadLength)
        {
            if (payloadLength <= offset)
            {
                throw new ArgumentOutOfRangeException("Payload size exceeds buffer size.");
            }

            // Is this a struct field? 
            PayloadFetchClassInfo classInfo = payloadFetch.Class;
            if (classInfo != null)
            {
                var ret = new StructValue();

                for (int i = 0; i < classInfo.FieldFetches.Length; i++)
                {
                    ret.Add(classInfo.FieldNames[i], GetPayloadValueAt(ref classInfo.FieldFetches[i], offset, payloadLength));
                    offset = OffsetOfNextField(ref classInfo.FieldFetches[i], offset, payloadLength);
                }
                return ret;
            }

            PayloadFetchArrayInfo arrayInfo = payloadFetch.Array;
            if (arrayInfo != null)
            {
                var arrayCount = GetCountForArray(payloadFetch, arrayInfo, ref offset);

                // TODO this is very inefficient for blitable types. Optimize that.
                var elementType = arrayInfo.Element.Type;

                // Byte array short-circuit.
                if (elementType == typeof(byte))
                {
                    return GetByteArrayAt(offset, arrayCount);
                }

                var ret = Array.CreateInstance(elementType, arrayCount);
                for (int i = 0; i < arrayCount; i++)
                {
                    object value = GetPayloadValueAt(ref arrayInfo.Element, offset, payloadLength);
                    if (value.GetType() != elementType)
                    {
                        value = ((IConvertible)value).ToType(elementType, null);
                    }

                    ret.SetValue(value, i);
                    offset = OffsetOfNextField(ref arrayInfo.Element, offset, payloadLength);
                }
                return ret;
            }

            Type type = payloadFetch.Type;
            if (type == null)
            {
                return "[CANT PARSE]";
            }

            if ((uint)ushort.MaxValue < (uint)offset)
            {
                return "[CANT PARSE OFFSET]";
            }

            // CONSIDER:  The code below insures that if you have fields that are
            // 'off the end' of a data that you return the default value.  That
            // allows the parser to gracefully handle old events that have fewer
            // fields but does NOT guarantee we don't read past the end of the 
            // buffer in all cases (if you have corrupt/mismatched data).   The
            // code below does insure this but is more expensive.   For now I have
            // chosen the cheaper solution.   
            //
            // if ((uint)EventDataLength < OffsetOfNextField(offset, index))
            //     return GetDefaultValueByType(payloadFetches[index].type);

            if ((uint)EventDataLength <= (uint)offset)
            {
                return GetDefaultValueByType(type);
            }

            switch (Type.GetTypeCode(type))
            {
                case TypeCode.String:
                    {
                        var size = payloadFetch.Size;
                        var isAnsi = false;
                        if (size >= SPECIAL_SIZES)
                        {
                            isAnsi = ((size & IS_ANSI) != 0);
                            if (IsNullTerminated(size))
                            {
                                if (isAnsi)
                                {
                                    return GetUTF8StringAt(offset);
                                }
                                else
                                {
                                    return GetUnicodeStringAt(offset);
                                }
                            }
                            else if (IsCountedSize(size))
                            {
                                bool unicodeByteCountString = !isAnsi && (size & ELEM_COUNT) == 0;
                                if (((size & BIT_32) != 0))
                                {
                                    size = (ushort)GetInt32At(offset);
                                    offset += 4;        // skip size;
                                }
                                else
                                {
                                    size = (ushort)GetInt16At(offset);
                                    offset += 2;        // skip size;
                                }
                                if (unicodeByteCountString)
                                {
                                    size /= 2;     // Unicode string with BYTE count.   Element count is half that.  
                                }
                            }
                            else
                            {
                                return "[CANT PARSE STRING]";
                            }
                        }
                        else if (size > 0x8000)     // What is this? looks like a hack.  
                        {
                            size -= 0x8000;
                            isAnsi = true;
                        }
                        if (isAnsi)
                        {
                            return GetFixedAnsiStringAt(size, offset);
                        }
                        else
                        {
                            return GetFixedUnicodeStringAt(size, offset);
                        }
                    }
                case TypeCode.Boolean:
                    return GetByteAt(offset) != 0;
                case TypeCode.Char:
                    return (Char)GetInt16At(offset);
                case TypeCode.Byte:
                    return (byte)GetByteAt(offset);
                case TypeCode.SByte:
                    return (SByte)GetByteAt(offset);
                case TypeCode.Int16:
                    return GetInt16At(offset);
                case TypeCode.UInt16:
                    return (UInt16)GetInt16At(offset);
                case TypeCode.Int32:
                    return GetInt32At(offset);
                case TypeCode.UInt32:
                    return (UInt32)GetInt32At(offset);
                case TypeCode.Int64:
                    return GetInt64At(offset);
                case TypeCode.UInt64:
                    return (UInt64)GetInt64At(offset);
                case TypeCode.Single:
                    return GetSingleAt(offset);
                case TypeCode.Double:
                    return GetDoubleAt(offset);
                default:
                    if (type == typeof(IntPtr))
                    {
                        if (PointerSize == 4)
                        {
                            return (Address)GetInt32At(offset);
                        }
                        else
                        {
                            return (Address)GetInt64At(offset);
                        }
                    }
                    else if (type == typeof(Guid))
                    {
                        return GetGuidAt(offset);
                    }
                    else if (type == typeof(DateTime))
                    {
                        return DateTime.FromFileTime(GetInt64At(offset));
                    }
                    else
                    {
                        return "[UNSUPPORTED TYPE]";
                    }
            }
        }

        /// <summary>
        ///  Used by PayloadValue to represent a structure.   It is basically a IDictionary with a ToString() that 
        ///  returns the value as JSON. 
        /// </summary>
        internal class StructValue : IDictionary<string, object>
        {
            public IEnumerator<KeyValuePair<string, object>> GetEnumerator() { return m_values.GetEnumerator(); }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return m_values.GetEnumerator(); }
            public bool IsReadOnly { get { return true; } }
            public object this[string key]
            {
                get
                {
                    foreach (var keyValue in m_values)
                    {
                        if (key == keyValue.Key)
                        {
                            return keyValue.Value;
                        }
                    }

                    return null;
                }
                set { throw new NotImplementedException(); }
            }
            public bool TryGetValue(string key, out object value)
            {
                foreach (var keyValue in m_values)
                {
                    if (key == keyValue.Key)
                    {
                        value = keyValue.Value;
                        return true;
                    }
                }
                value = null;
                return false;
            }
            public void Add(string key, object value) { m_values.Add(new KeyValuePair<string, object>(key, value)); }
            public void Add(KeyValuePair<string, object> item) { m_values.Add(item); }
            public void Clear() { m_values.Clear(); }
            public int Count { get { return m_values.Count; } }
            public override string ToString()
            {
                return WriteAsJSon(new StringBuilder(), this).ToString();
            }

            private static StringBuilder WriteAsJSon(StringBuilder sb, object value)
            {
                var asStructValue = value as StructValue;
                if (asStructValue != null)
                {
                    sb.Append("{ ");
                    bool first = true;
                    foreach (var keyvalue in asStructValue)
                    {
                        if (!first)
                        {
                            sb.Append(", ");
                        }
                        else
                        {
                            first = false;
                        }

                        sb.Append(keyvalue.Key).Append(":");
                        WriteAsJSon(sb, keyvalue.Value);
                    }
                    sb.Append(" }");
                    return sb;
                }

                var asArray = value as System.Array;
                if (asArray != null && asArray.Rank == 1)
                {
                    sb.Append("[ ");
                    bool first = true;
                    for (int i = 0; i < asArray.Length; i++)
                    {
                        if (!first)
                        {
                            sb.Append(", ");
                        }
                        else
                        {
                            first = false;
                        }

                        WriteAsJSon(sb, asArray.GetValue(i));
                    }
                    sb.Append(" ]");
                    return sb;
                }

                if (value is int || value is bool || value is double || value is float)
                {
                    sb.Append(value);
                    return sb;
                }
                else if (value == null)
                {
                    sb.Append("null");
                }
                else
                {
                    sb.Append("\"");
                    Quote(sb, value.ToString());
                    sb.Append("\"");
                }
                return sb;
            }

            #region private
            /// <summary>
            ///  Uses C style conventions to quote a string 'value' and append to the string builder 'sb'.
            ///  Thus all \ are turned into \\ and all " into \"
            /// </summary>
            private static void Quote(StringBuilder output, string value)
            {
                for (int i = 0; i < value.Length; i++)
                {
                    var c = value[i];
                    if (c == '\\' || c == '"')
                    {
                        output.Append('\\');
                    }

                    output.Append(c);
                }
            }

            public bool ContainsKey(string key)
            {
                object value;
                return TryGetValue(key, out value);
            }
            public ICollection<string> Keys { get { throw new NotImplementedException(); } }
            public bool Remove(string key) { throw new NotImplementedException(); }
            public ICollection<object> Values { get { throw new NotImplementedException(); } }

            public bool Contains(KeyValuePair<string, object> item) { throw new NotImplementedException(); }
            public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex) { throw new NotImplementedException(); }
            public bool Remove(KeyValuePair<string, object> item) { throw new NotImplementedException(); }

            private List<KeyValuePair<string, object>> m_values = new List<KeyValuePair<string, object>>();
            #endregion
        }

        // Return the default value for a given type  
        private object GetDefaultValueByType(Type type)
        {
            if (type == typeof(string))     // Activator.CreateInstance does not work on strings.  
            {
                return String.Empty;
            }
            else
            {
                return Activator.CreateInstance(type);
            }
        }

        /// <summary>
        /// Implements TraceEvent interface
        /// </summary>
        public override string PayloadString(int index, IFormatProvider formatProvider = null)
        {
            // See if you can do enumeration mapping.  
            var map = payloadFetches[index].Map;
            if (map != null)
            {
                object value = PayloadValue(index);
                if (value == null)
                {
                    return "";
                }

                long asLong = (long)((IConvertible)value).ToInt64(formatProvider);
                if (map is SortedDictionary<long, string>)
                {
                    StringBuilder sb = new StringBuilder();
                    // It is a bitmap, compute the bits from the bitmap.  
                    foreach (var keyValue in map)
                    {
                        if (asLong == 0)
                        {
                            break;
                        }

                        if ((keyValue.Key & asLong) != 0)
                        {
                            if (sb.Length != 0)
                            {
                                sb.Append('|');
                            }

                            sb.Append(keyValue.Value);
                            asLong &= ~keyValue.Key;
                        }
                    }
                    if (asLong != 0)
                    {
                        if (sb.Length != 0)
                        {
                            sb.Append('|');
                        }

                        sb.Append("0x");
                        if (asLong == (int)asLong)
                        {
                            sb.Append(((int)asLong).ToString("x", formatProvider));
                        }
                        else
                        {
                            sb.Append(asLong.ToString("x", formatProvider));
                        }
                    }
                    else if (sb.Length == 0)
                    {
                        sb.Append('0');
                    }

                    return sb.ToString();
                }
                else
                {
                    // It is a value map, just look up the value
                    string ret;
                    if (map.TryGetValue(asLong, out ret))
                    {
                        return ret;
                    }
                }
            }

            // Otherwise do the default transformations. 
            return base.PayloadString(index, formatProvider);
        }
        /// <summary>
        /// Implements TraceEvent interface
        /// </summary>
        protected internal override Delegate Target
        {
            get { return m_target; }
            set
            {
                Debug.Assert(m_target == null);
                m_target = (Action<TraceEvent>)value;
            }
        }

        private static readonly Regex paramReplacer = new Regex(@"%(\d+)", RegexOptions.Compiled);

        public override string GetFormattedMessage(IFormatProvider formatProvider)
        {
            if (MessageFormat == null)
            {
                return base.GetFormattedMessage(formatProvider);
            }

            // TODO is this error handling OK?  
            // Replace all %N with the string value for that parameter.  
            return paramReplacer.Replace(MessageFormat, delegate (Match m)
            {
                int targetIndex = int.Parse(m.Groups[1].Value) - 1;

                // for some array and string values, we remove the length field.  Account
                // for that when we are resolving the %X qualifers by searching up from
                // 0 adjusting along the way for removed fields.  
                int index = 0;
                for (int fixedIndex = 0; fixedIndex < payloadFetches.Length; fixedIndex++)
                {
                    // This field is the length field that was removed from the payloafFetches array. 
                    if (DynamicTraceEventData.ConsumesFields(payloadFetches[fixedIndex].Size))
                    {
                        if (index == targetIndex)
                        {
                            // Try to output the correct length by getting the next value and computing its length.  
                            object obj = PayloadValue(fixedIndex);
                            string asString = obj as string;
                            if (asString != null)
                            {
                                return asString.Length.ToString();
                            }

                            Array asArray = obj as Array;
                            if (asArray != null)
                            {
                                return asArray.Length.ToString();
                            }

                            return ""; // give up and return an empty string.  
                        }
                        index++;        // skip the removed field.  
                    }
                    if (index == targetIndex)
                    {
                        return PayloadString(fixedIndex, formatProvider);
                    }

                    index++;
                }
                return "<<BadFieldIdx>>";
            });
        }
        #endregion

        #region private
        private int SkipToField(PayloadFetch[] payloadFetches, int targetFieldIdx, int startOffset, int payloadLength, bool useCache)
        {
            int fieldOffset;
            int fieldIdx;

            // First find a valid fieldIdx, fieldOffset pair
            if (useCache && cachedEventId == EventIndex && cachedFieldIdx <= targetFieldIdx && startOffset == 0)
            {
                // We fetched a previous field, great, start from there.  
                fieldOffset = cachedFieldOffset;
                fieldIdx = cachedFieldIdx;
            }
            else
            {
                // no cached value, search backwards for the first field that has a fixed offset. 
                fieldOffset = 0;
                fieldIdx = targetFieldIdx;
                while (0 < fieldIdx)
                {
                    --fieldIdx;
                    if (payloadFetches[fieldIdx].Offset != ushort.MaxValue)
                    {
                        fieldOffset = payloadFetches[fieldIdx].Offset;
                        break;
                    }
                }
                fieldOffset += startOffset;
            }

            // If we try to skip t fields that are not present, we simply stop at the end of the buffer.  
            if (payloadLength <= fieldOffset)
            {
                return payloadLength;
            }

            // This can be N*N but because of our cache, it is not in the common case when you fetch
            // fields in order.   
            while (fieldIdx < targetFieldIdx)
            {
                fieldOffset = OffsetOfNextField(ref payloadFetches[fieldIdx], fieldOffset, payloadLength);

                // If we try to skip to fields that are not present, we simply stop at the end of the buffer.  
                if (fieldOffset == payloadLength)
                {
                    return payloadLength;
                }

                // however if we truly go past the end of the buffer, something went wrong and we want to signal that. 
                if (payloadLength < fieldOffset)
                {
                    throw new ArgumentOutOfRangeException("Payload size exceeds buffer size.");
                }

                fieldIdx++;
            }

            // Remember our answer since can start there for the next field efficiently.  
            if (useCache && startOffset == 0)
            {
#if DEBUG
                // If we computed the result using the cache,  compute it again without the cache and we should get the same answer.  
                if (cachedEventId == this.EventIndex)
                {
                    cachedEventId = EventIndex.Invalid;
                    Debug.Assert(fieldOffset == SkipToField(payloadFetches, targetFieldIdx, startOffset, payloadLength, true));
                }
#endif
                cachedFieldOffset = fieldOffset;
                cachedFieldIdx = targetFieldIdx;
                cachedEventId = EventIndex;
            }
            return fieldOffset;
        }

        /// <summary>
        /// Returns the count of elements for the array represented by 'arrayInfo'
        /// It also will adjust 'offset' so that it points at the beginning of the
        /// array data (skips past the count). 
        /// </summary>
        private int GetCountForArray(PayloadFetch payloadFetch, PayloadFetchArrayInfo arrayInfo, ref int offset)
        {
            var arrayCount = arrayInfo.FixedCount;
            if (arrayCount == 0)
            {
                // Arrays are not strings and thus should not have the ANSI bit set.  
                Debug.Assert((payloadFetch.Size & IS_ANSI) == 0);
                // Arrays never use a byte count.  
                Debug.Assert((payloadFetch.Size & ELEM_COUNT) != 0);

                if (DynamicTraceEventData.IsCountedSize(payloadFetch.Size))
                {
                    if (((payloadFetch.Size & DynamicTraceEventData.BIT_32) != 0))
                    {
                        arrayCount = GetInt32At(offset);
                        offset += 4;
                    }
                    else
                    {
                        arrayCount = GetInt16At(offset);
                        offset += 2;
                    }
                }
                else
                {
                    Debug.Assert(false);
                    throw new NotSupportedException();      // Actually an assert.  
                }
            }
            if (0x10000 <= arrayCount)
            {
                throw new ArgumentOutOfRangeException();
            }

            return arrayCount;
        }

        internal int OffsetOfNextField(ref PayloadFetch payloadFetch, int offset, int payloadLength)
        {
            PayloadFetchClassInfo classInfo = payloadFetch.Class;
            if (classInfo != null)
            {
                return SkipToField(classInfo.FieldFetches, classInfo.FieldFetches.Length, offset, payloadLength, false);
            }

            // TODO cache this when you parse the value so that you don't need to do it twice.  Right now it is pretty inefficient. 
            PayloadFetchArrayInfo arrayInfo = payloadFetch.Array;
            if (arrayInfo != null)
            {
                if (payloadLength <= offset)
                {
                    throw new ArgumentOutOfRangeException();
                }

                var arrayCount = GetCountForArray(payloadFetch, arrayInfo, ref offset);

                if (arrayInfo.Element.Array == null && arrayInfo.Element.Class == null && arrayInfo.Element.Size < SPECIAL_SIZES)
                {
                    return offset + arrayCount * arrayInfo.Element.Size;
                }

                for (ushort i = 0; i < arrayCount; i++)
                {
                    offset = OffsetOfNextField(ref arrayInfo.Element, offset, payloadLength);
                }

                return offset;
            }

            ushort size = payloadFetch.Size;
            if (size >= SPECIAL_SIZES)
            {
                if (size == NULL_TERMINATED)
                {
                    return SkipUnicodeString(offset);
                }
                else if (size == (NULL_TERMINATED | IS_ANSI))
                {
                    return SkipUTF8String(offset);
                }
                else if (size == POINTER_SIZE)
                {
                    return offset + PointerSize;
                }
                else if (IsCountedSize(size) && payloadFetch.Type == typeof(string))
                {
                    int elemSize;
                    if (((size & BIT_32) != 0))
                    {
                        elemSize = GetInt32At(offset);
                        offset += 4;        // skip size;
                    }
                    else
                    {
                        elemSize = GetInt16At(offset);
                        offset += 2;        // skip size;
                    }
                    if ((size & IS_ANSI) == 0 && (size & ELEM_COUNT) != 0)
                    {
                        elemSize *= 2;     // Counted (not byte counted) unicode string. chars are 2 wide. 
                    }

                    return offset + elemSize;
                }
                else
                {
                    return ushort.MaxValue;     // Something sure to fail 
                }
            }
            else
            {
                return offset + size;
            }
        }

        internal static ushort SizeOfType(Type type)
        {
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.String:
                    return NULL_TERMINATED;
                case TypeCode.SByte:
                case TypeCode.Byte:
                    return 1;
                case TypeCode.UInt16:
                case TypeCode.Int16:
                    return 2;
                case TypeCode.UInt32:
                case TypeCode.Int32:
                case TypeCode.Boolean:      // We follow windows conventions and use 4 bytes for bool.  
                case TypeCode.Single:
                    return 4;
                case TypeCode.UInt64:
                case TypeCode.Int64:
                case TypeCode.Double:
                case TypeCode.DateTime:
                    return 8;
                default:
                    if (type == typeof(Guid))
                    {
                        return 16;
                    }

                    if (type == typeof(IntPtr))
                    {
                        return POINTER_SIZE;
                    }

                    throw new Exception("Unsupported type " + type.Name); // TODO 
            }
        }

        // IS_ANSI can be used to modify COUNTED_SIZE as well as NULL_TERMINATED
        internal const ushort IS_ANSI = 1;        // If set the string is ASCII, unset is UNICODE 
        // The following 3 bits are used to modify the 'COUNTED_SIZE' constant 
        internal const ushort BIT_32 = 2;         // If set the count is a 32 bit number.  unset is 16 bit
        internal const ushort CONSUMES_FIELD = 4; // If set there was a explicit count field in the manifest, unset means no explicit field 
        internal const ushort ELEM_COUNT = 8;     // If set count is a char/element count.  unset means count is a count of BYTES.  Does not include the size prefix itself  

        internal static bool IsNullTerminated(ushort size) { return (size & ~IS_ANSI) == NULL_TERMINATED; }
        internal static bool IsCountedSize(ushort size) { return size >= COUNTED_SIZE; }

        internal static bool ConsumesFields(ushort size) { return IsCountedSize(size) && (size & CONSUMES_FIELD) != 0; }

        // These are special sizes 
        // sizes from 0xFFF0 through 0xFFFF are variations of VAR_SIZE
        internal const ushort COUNTED_SIZE = 0xFFF0;   // The size is variable.  Size preceded the data, bits above tell more.   

        // Size 0xFFEF is NULL_TERMINATED | IS_ANSI
        internal const ushort NULL_TERMINATED = 0xFFEE; // value is a null terminated string.   

        internal const ushort POINTER_SIZE = 0xFFED;        // It is the pointer size of the target machine. 
        internal const ushort UNKNOWN_SIZE = 0xFFEC;        // Generic unknown.
        internal const ushort SPECIAL_SIZES = UNKNOWN_SIZE; // This is always the smallest size as an unsiged number.    

        internal struct PayloadFetch
        {
            /// <summary>
            /// Constructor for normal types, (int, string) ...)   Also handles Enums (which are ints with a map)
            /// </summary>
            public PayloadFetch(ushort offset, ushort size, Type type, IDictionary<long, string> map = null)
            {
                Offset = offset;
                Size = size;
                Type = type;
                info = map;
            }

            /// <summary>
            /// Initialized a PayloadFetch for a given inType.  REturns Size = DynamicTraceEventData.UNKNOWN_SIZE
            /// if the type is unknown.  
            /// </summary>

            public PayloadFetch(ushort offset, RegisteredTraceEventParser.TdhInputType inType, int outType)
            {
                Offset = offset;
                info = null;

                switch (inType)
                {
                    case RegisteredTraceEventParser.TdhInputType.UnicodeString:
                        Type = typeof(string);
                        Size = DynamicTraceEventData.NULL_TERMINATED;
                        break;
                    case RegisteredTraceEventParser.TdhInputType.AnsiString:
                        Type = typeof(string);
                        Size = DynamicTraceEventData.NULL_TERMINATED | DynamicTraceEventData.IS_ANSI;
                        break;
                    case RegisteredTraceEventParser.TdhInputType.UInt8:
                        if (outType == 13)       // Encoding for boolean
                        {
                            Type = typeof(bool);
                            Size = 1;
                            break;
                        }
                        goto case RegisteredTraceEventParser.TdhInputType.Int8; // Fall through
                    case RegisteredTraceEventParser.TdhInputType.Binary:
                    // Binary is an array of bytes.  The later logic will transform it to array, thus Binary is like byte 
                    case RegisteredTraceEventParser.TdhInputType.Int8:
                        Type = typeof(byte);
                        Size = 1;
                        break;
                    case RegisteredTraceEventParser.TdhInputType.Int16:
                    case RegisteredTraceEventParser.TdhInputType.UInt16:
                        Size = 2;
                        if (outType == 1)       // Encoding for String
                        {
                            Type = typeof(char);
                            break;
                        }
                        Type = typeof(short);
                        break;
                    case RegisteredTraceEventParser.TdhInputType.Int32:
                    case RegisteredTraceEventParser.TdhInputType.UInt32:
                    case RegisteredTraceEventParser.TdhInputType.HexInt32:
                        Type = typeof(int);
                        Size = 4;
                        break;
                    case RegisteredTraceEventParser.TdhInputType.Int64:
                    case RegisteredTraceEventParser.TdhInputType.UInt64:
                    case RegisteredTraceEventParser.TdhInputType.HexInt64:
                        Type = typeof(long);
                        Size = 8;
                        break;
                    case RegisteredTraceEventParser.TdhInputType.Float:
                        Type = typeof(float);
                        Size = 4;
                        break;
                    case RegisteredTraceEventParser.TdhInputType.Double:
                        Type = typeof(double);
                        Size = 8;
                        break;
                    case RegisteredTraceEventParser.TdhInputType.Boolean:
                        Type = typeof(bool);
                        Size = 4;
                        break;
                    case RegisteredTraceEventParser.TdhInputType.GUID:
                        Type = typeof(Guid);
                        Size = 16;
                        break;
                    case RegisteredTraceEventParser.TdhInputType.Pointer:
                    case RegisteredTraceEventParser.TdhInputType.SizeT:
                        Type = typeof(IntPtr);
                        Size = DynamicTraceEventData.POINTER_SIZE;
                        break;
                    case RegisteredTraceEventParser.TdhInputType.FILETIME:
                        Type = typeof(DateTime);
                        Size = 8;
                        break;
                    case RegisteredTraceEventParser.TdhInputType.CountedUtf16String:
                    case RegisteredTraceEventParser.TdhInputType.CountedString:
                        Type = typeof(string);
                        Size = DynamicTraceEventData.COUNTED_SIZE;  // Unicode, 16 bit, byteCount
                        break;
                    case RegisteredTraceEventParser.TdhInputType.CountedAnsiString:
                    case RegisteredTraceEventParser.TdhInputType.CountedMbcsString:
                        Type = typeof(string);
                        Size = DynamicTraceEventData.COUNTED_SIZE | DynamicTraceEventData.IS_ANSI + DynamicTraceEventData.ELEM_COUNT; // 16 Bit. 
                        break;
                    case RegisteredTraceEventParser.TdhInputType.Struct:
                        Type = typeof(DynamicTraceEventData.StructValue);
                        Size = DynamicTraceEventData.UNKNOWN_SIZE;
                        break;
                    case RegisteredTraceEventParser.TdhInputType.SYSTEMTIME:
                        Type = typeof(DateTime);
                        Size = 16;
                        break;
                    default:
                        Size = DynamicTraceEventData.UNKNOWN_SIZE;
                        Type = null;
                        break;
                }
            }

            /// <summary>
            /// Returns a payload fetch for a Array.   If you know the count, then you can give it. 
            /// </summary>
            public static PayloadFetch ArrayPayloadFetch(ushort offset, PayloadFetch element, ushort size, ushort fixedCount = 0)
            {
                var ret = new PayloadFetch();
                ret.Offset = offset;
                ret.Size = size;
                ret.info = new PayloadFetchArrayInfo() { Element = element, FixedCount = fixedCount };
                return ret;
            }
            public static PayloadFetch StructPayloadFetch(ushort offset, PayloadFetchClassInfo fields)
            {
                var ret = new PayloadFetch();
                ret.Offset = offset;
                ret.Size = DynamicTraceEventData.UNKNOWN_SIZE;
                ret.Type = typeof(StructValue);
                ret.info = fields;
                return ret;
            }

            /// <summary>
            /// Offset from the beginning of the struct.  
            /// </summary>
            public ushort Offset;       // offset == MaxValue means variable size.

            // TODO come up with a real encoding for variable sized things
            // See special encodings above (also size > 0x8000 means fixed length ANSI).  
            public ushort Size;

            // Non null of 'Type' is a class (record with fields)
            internal PayloadFetchClassInfo Class
            {
                get { return info as PayloadFetchClassInfo; }
            }

            // Non null if 'Type' is an array 
            public PayloadFetchArrayInfo Array
            {
                get { return info as PayloadFetchArrayInfo; }
            }

            public Type Type;       // Currently null for arrays.  

            // Non null of 'Type' is a enum
            public IDictionary<long, string> Map
            {
                get
                {
                    if (info == null)
                    {
                        return null;
                    }

                    var ret = info as IDictionary<long, string>;
                    if (ret == null)
                    {
                        var asLazyMap = LazyMap;
                        if (asLazyMap != null)
                        {
                            ret = asLazyMap();      // resolve it.  
                            if (ret != null)
                            {
                                info = ret;         // If it resolves, remember the resolution for next time.  
                            }
                        }
                    }
                    return ret;
                }
                set
                {
                    Debug.Assert(info == null);         // We only expect one time initialization.   Not a class or an array
                    info = value;
                }
            }

            /// <summary>
            /// LazyMap allow out to set a function that returns a map 
            /// instead of the map itself.   This will be evaluated when the map
            /// is fetched (which gives time for the map table to be populated.  
            /// </summary>
            public Func<IDictionary<long, string>> LazyMap
            {
                get { return info as Func<IDictionary<long, string>>; }
                set
                {
                    Debug.Assert(info == null);         // We only expect one time initialization.   Not a class or an array
                    info = value;
                }

            }

            #region private
            public override string ToString()
            {
                StringWriter sw = new StringWriter();
                sw.Write("<PayloadFetch Size=\"{0}\" Offset=\"{1}\" Type=\"{2}\"", Size, Offset, Type != null ? Type.Name : "");
                if (Map != null)
                {
                    sw.Write("HasMap=\"true\"/>");
                }
                else if (Array != null || Class != null)
                {
                    sw.WriteLine(">");
                    if (Array != null)
                    {
                        sw.WriteLine(Array.ToString());
                    }
                    else if (Class != null)
                    {
                        for (int i = 0; i < Class.FieldFetches.Length; i++)
                        {
                            sw.WriteLine("<Field Name=\"{0}\">{1}</Field>", Class.FieldNames[i], Class.FieldFetches[i].ToString());
                        }
                    }
                    sw.WriteLine("<PayloadFetch>");
                }
                else
                {
                    sw.WriteLine("/>");
                }

                return sw.ToString();
            }
            public void ToStream(Serializer serializer)
            {
                serializer.Write((short)Offset);
                serializer.Write((short)Size);
                if (Type == null)
                {
                    serializer.Write((string)null);
                }
                else
                {
                    serializer.Write(Type.FullName);
                }

                var map = Map;
                if (map != null)
                {
                    var asSortedList = map as SortedDictionary<long, string>;
                    if (asSortedList != null)
                    {
                        serializer.Write((byte)1);
                    }
                    else
                    {
                        serializer.Write((byte)2);
                    }

                    serializer.Write(map.Count);
                    foreach (var keyValue in map)
                    {
                        serializer.Write(keyValue.Key);
                        serializer.Write(keyValue.Value);
                    }
                }
                else if (Class != null)
                {
                    PayloadFetchClassInfo classInfo = Class;
                    serializer.Write((byte)3);

                    serializer.Write(classInfo.FieldNames.Length);
                    foreach (var name in classInfo.FieldNames)
                    {
                        serializer.Write(name);
                    }

                    serializer.Write(classInfo.FieldFetches.Length);
                    for (int i = 0; i < classInfo.FieldFetches.Length; i++)
                    {
                        classInfo.FieldFetches[i].ToStream(serializer);
                    }
                }
                else if (Array != null)
                {
                    PayloadFetchArrayInfo arrayInfo = Array;
                    serializer.Write((byte)4);
                    serializer.Write(arrayInfo.FixedCount);
                    arrayInfo.Element.ToStream(serializer);
                }
                else
                {
                    serializer.Write((byte)0);
                }
            }
            public void FromStream(Deserializer deserializer)
            {
                Offset = (ushort)deserializer.ReadInt16();
                Size = (ushort)deserializer.ReadInt16();
                var typeName = deserializer.ReadString();
                if (typeName != null)
                {
                    Type = Type.GetType(typeName);
                }

                var fetchType = deserializer.ReadByte();
                if (fetchType == 1 || fetchType == 2)
                {
                    IDictionary<long, string> map = null;
                    int mapCount = deserializer.ReadInt();
                    if (fetchType == 1)
                    {
                        map = new SortedDictionary<long, string>();
                    }
                    else
                    {
                        map = new Dictionary<long, string>(mapCount);
                    }

                    for (int j = 0; j < mapCount; j++)
                    {
                        long key = deserializer.ReadInt64();
                        string value = deserializer.ReadString();
                        map.Add(key, value);
                    }
                    Map = map;
                }
                else if (fetchType == 3)  // Class 
                {
                    PayloadFetchClassInfo classInfo = new PayloadFetchClassInfo();

                    var fieldNamesCount = deserializer.ReadInt();
                    classInfo.FieldNames = new string[fieldNamesCount];
                    for (int i = 0; i < fieldNamesCount; i++)
                    {
                        classInfo.FieldNames[i] = deserializer.ReadString();
                    }

                    var fieldFetchCount = deserializer.ReadInt();
                    classInfo.FieldFetches = new DynamicTraceEventData.PayloadFetch[fieldFetchCount];
                    for (int i = 0; i < fieldFetchCount; i++)
                    {
                        classInfo.FieldFetches[i].FromStream(deserializer);
                    }

                    info = classInfo;
                }
                else if (fetchType == 4)  // Array
                {
                    PayloadFetchArrayInfo arrayInfo = new PayloadFetchArrayInfo();
                    deserializer.Read(out arrayInfo.FixedCount);
                    arrayInfo.Element.FromStream(deserializer);
                    info = arrayInfo;
                }
                else if (fetchType != 0)
                {
                    Debug.Assert(false, "Unknown fetch type");
                }
            }

            private object info;        // different things for enums, structs, or arrays.  
            #endregion
        };

        // Supports nested structural types
        internal class PayloadFetchClassInfo
        {
            public PayloadFetch[] FieldFetches;
            public string[] FieldNames;

        }

        internal class PayloadFetchArrayInfo
        {
            public PayloadFetch Element;
            public int FixedCount;          // Normally 0 which means dynamic size

            public override string ToString()
            {
                return "<Array size = \"" + FixedCount + "\">\r\n" + Element.ToString() + "\r\n</Array>";
            }
        }

        public void ToStream(Serializer serializer)
        {
            serializer.Write((int)eventID);
            serializer.Write((int)task);
            serializer.Write(taskName);
            serializer.Write(taskGuid);
            serializer.Write((int)opcode);
            serializer.Write(opcodeName);
            serializer.Write(providerGuid);
            serializer.Write(providerName);
            serializer.Write(MessageFormat);
            serializer.Write(lookupAsClassic);
            serializer.Write(lookupAsWPP);
            serializer.Write(containsSelfDescribingMetadata);

            serializer.Write(payloadNames.Length);
            foreach (var payloadName in payloadNames)
            {
                serializer.Write(payloadName);
            }

            serializer.Write(payloadFetches.Length);
            foreach (var payloadFetch in payloadFetches)
            {
                payloadFetch.ToStream(serializer);
            }
        }
        public void FromStream(Deserializer deserializer)
        {
            eventID = (TraceEventID)deserializer.ReadInt();
            task = (TraceEventTask)deserializer.ReadInt();
            deserializer.Read(out taskName);
            deserializer.Read(out taskGuid);
            opcode = (TraceEventOpcode)deserializer.ReadInt();
            deserializer.Read(out opcodeName);
            deserializer.Read(out providerGuid);
            deserializer.Read(out providerName);
            deserializer.Read(out MessageFormat);
            deserializer.Read(out lookupAsClassic);
            deserializer.Read(out lookupAsWPP);
            deserializer.Read(out containsSelfDescribingMetadata);
            int count;
            deserializer.Read(out count);
            payloadNames = new string[count];
            for (int i = 0; i < count; i++)
            {
                deserializer.Read(out payloadNames[i]);
            }

            deserializer.Read(out count);
            payloadFetches = new PayloadFetch[count];
            for (int i = 0; i < count; i++)
            {
                payloadFetches[i].FromStream(deserializer);
            }
        }

        // Fields
        internal PayloadFetch[] payloadFetches;
        internal string MessageFormat; // This is in ETW conventions (%N)
        internal bool registeredWithTraceEventSource;

        // These are used to improve the performance of SkipToField.  
        private EventIndex cachedEventId;
        private int cachedFieldIdx;
        private int cachedFieldOffset;
        #endregion
    }

    /// <summary>
    /// This class is only used to pretty-print the manifest event itself.   It is pretty special purpose
    /// </summary>
    internal class DynamicManifestTraceEventData : DynamicTraceEventData
    {
        internal DynamicManifestTraceEventData(Action<TraceEvent> action, ProviderManifest manifest)
            : base(action, (int)DynamicTraceEventParser.ManifestEventID, 0xFFFE, "ManifestData", Guid.Empty, 0xFE, "", manifest.Guid, manifest.Name)
        {
            this.manifest = manifest;
            payloadNames = new string[] { "Format", "MajorVersion", "MinorVersion", "Magic", "TotalChunks", "ChunkNumber", "PayloadLength" };
            payloadFetches = new PayloadFetch[] {
                new PayloadFetch(0, 1, typeof(byte)),
                new PayloadFetch(1, 1, typeof(byte)),
                new PayloadFetch(2, 1, typeof(byte)),
                new PayloadFetch(3, 1, typeof(byte)),
                new PayloadFetch(4, 2, typeof(ushort)),
                new PayloadFetch(6, 2, typeof(ushort)),
            };
            m_target += action;
        }

        public override object PayloadValue(int index)
        {
            // The length of the manifest chunk is useful, so we expose it as an explict 'field' 
            if (index == 6)
            {
                return EventDataLength;
            }

            return base.PayloadValue(index);
        }

        public override string PayloadString(int index, IFormatProvider formatProvider = null)
        {
            if (index == 6)
            {
                return PayloadValue(index).ToString();
            }

            return base.PayloadString(index, formatProvider);
        }

        public override StringBuilder ToXml(StringBuilder sb)
        {
            int totalChunks = GetInt16At(4);
            int chunkNumber = GetInt16At(6);
            if (chunkNumber + 1 == totalChunks)
            {
                StringBuilder baseSb = new StringBuilder();
                base.ToXml(baseSb);
                sb.AppendLine(XmlUtilities.OpenXmlElement(baseSb.ToString()));
                sb.AppendLine();
                sb.AppendLine();
                sb.Append("<Warning>*********************************************************************************************************************</Warning>").AppendLine();
                sb.Append("<Warning>This Manifest Represents the manifest at the event in ManifestId Element.  It may not represent THIS event's payload.</Warning>").AppendLine();
                sb.Append("<ManifestId>").Append(manifest.Id).Append("</ManifestId>").AppendLine();
                sb.Append("<Warning>*********************************************************************************************************************</Warning>").AppendLine();
                sb.AppendLine();
                sb.AppendLine();
                sb.Append(manifest.Manifest);
                sb.Append("</Event>");
                return sb;
            }
            else
            {
                return base.ToXml(sb);
            }
        }
        #region private
        private ProviderManifest manifest;
        #endregion
    }

    /// <summary>
    /// DynamicTraceEventParserState represents the state of a  DynamicTraceEventParser that needs to be
    /// serialized to a log file.  It does NOT include information about what events are chosen but DOES contain
    /// any other necessary information that came from the ETL data file.  
    /// </summary>
    internal class DynamicTraceEventParserState : IFastSerializable
    {
        public DynamicTraceEventParserState() { providers = new Dictionary<Guid, ProviderManifest>(); }

        #region IFastSerializable Members

        void IFastSerializable.ToStream(Serializer serializer)
        {
            serializer.Write(providers.Count);
            foreach (ProviderManifest provider in providers.Values)
            {
                serializer.Write(provider);
            }
        }

        void IFastSerializable.FromStream(Deserializer deserializer)
        {
            int count;
            deserializer.Read(out count);
            for (int i = 0; i < count; i++)
            {
                ProviderManifest provider;
                deserializer.Read(out provider);
                providers.Add(provider.Guid, provider);
            }
        }

        #endregion

        internal Dictionary<Guid, ProviderManifest> providers;
    }
    #endregion

    /// <summary>
    /// A ProviderManifest represents the XML manifest associated with the provider.    
    /// </summary>
    public sealed class ProviderManifest : IFastSerializable
    {
        // create a manifest from a stream or a file
        /// <summary>
        /// Read a ProviderManifest from a stream
        /// </summary>
        public ProviderManifest(Stream manifestStream, int manifestLen = int.MaxValue)
        {
            format = ManifestEnvelope.ManifestFormats.SimpleXmlFormat;
            id = "Stream";
            int len = Math.Min((int)(manifestStream.Length - manifestStream.Position), manifestLen);
            serializedManifest = new byte[len];
            manifestStream.Read(serializedManifest, 0, len);
        }
        /// <summary>
        /// Read a ProviderManifest from a file. 
        /// </summary>
        public ProviderManifest(string manifestFilePath)
        {
            format = ManifestEnvelope.ManifestFormats.SimpleXmlFormat;
            id = manifestFilePath;
            serializedManifest = File.ReadAllBytes(manifestFilePath);
        }

        /// <summary>
        /// Normally ProviderManifest will fail silently if there is a problem with the manifest.  If
        /// you want to see this error you can all this method to force it explicitly  It will
        /// throw if there is a problem parsing the manifest.  
        /// </summary>
        public void ValidateManifest()
        {
            ParseProviderEvents((DynamicTraceEventData data) => EventFilterResponse.AcceptEvent, false);
        }

        // write a manifest to a stream or a file.  
        /// <summary>
        /// Writes the manifest to 'outputStream' (as UTF8 XML text)
        /// </summary>
        public void WriteToStream(Stream outputStream)
        {
            outputStream.Write(serializedManifest, 0, serializedManifest.Length);
        }
        /// <summary>
        /// Writes the manifest to a file 'filePath' (as a UTF8 XML)
        /// </summary>
        /// <param name="filePath"></param>
        public void WriteToFile(string filePath)
        {
            using (var stream = File.Create(filePath))
            {
                WriteToStream(stream);
            }
        }

        /// <summary>
        ///  Set if this manifest came from the ETL data stream file.  
        /// </summary>
        public bool ISDynamic { get; internal set; }
        /// <summary>
        /// The name of the ETW provider
        /// </summary>
        public string Name { get { if (!inited) { Init(); } return name; } }
        /// <summary>
        /// The GUID that uniquey identifies the ETW provider
        /// </summary>
        public Guid Guid { get { if (!inited) { Init(); } return guid; } }
        /// <summary>
        /// The version is defined as the sum of all the version numbers of event version numbers + the number of events defined. 
        /// This has the property that if you follow correct versioning protocol (all versions for a linear sequence where a new  
        /// versions is only modifies is predecessor by adding new events or INCREASING the version numbers of existing events) 
        /// then the version number defined below will always strictly increase.   
        ///
        /// It turns out that .NET Core removed some events from the TplEtwProvider.   To allow removal of truly old events
        /// we also add 100* the largest event ID defined to the version number.  That way if you add new events, even if you
        /// removes some (less than 100) it will consider your 'better'.   
        /// </summary>
        public int Version
        {
            get
            {
                if (version == 0)
                {
                    try
                    {
                        var verReader = ManifestReader;
                        var maxEventId = 0;
                        while (verReader.Read())
                        {
                            if (verReader.NodeType != XmlNodeType.Element)
                                continue;

                            if (verReader.Name == "event")
                            {
                                version++;
                                string ver = verReader.GetAttribute("version");
                                if (ver != null && int.TryParse(ver, out int intVer))
                                    version += intVer;

                                string id = verReader.GetAttribute("value");
                                if (id != null && int.TryParse(id, out int intId) && intId > maxEventId)
                                    maxEventId = intId;
                            }
                        }

                        version += maxEventId * 100;
                    }
                    catch (Exception e)
                    {
                        Debug.Assert(false, "Exception during version parsing");
                        error = e;
                        version = -1;
                    }
                }
                return version;
            }
        }
        /// <summary>
        /// This is an arbitrary id given when the Manifest is created that
        /// identifies where the manifest came from (e.g. a file name or an event etc). 
        /// </summary>
        public string Id { get { return id; } }

        /// <summary>
        /// Returns true if the current manifest is better to use than 'otherManifest'   A manifest is
        /// better if it has a larger version number OR, they have the same version number and it is
        /// physically larger (we assume what happened is people added more properties but did not
        /// update the version field appropriately).  
        /// </summary>
        public bool BetterThan(ProviderManifest otherManifest)
        {
            int ver = Version;
            int otherVer = otherManifest.Version;

            if (ver != otherVer)
            {
                return (ver > otherVer);
            }

            return serializedManifest.Length > otherManifest.serializedManifest.Length;
        }


        /// <summary>
        /// Retrieve manifest as one big string.  Mostly for debugging
        /// </summary>
        public string Manifest { get { return Encoding.UTF8.GetString(serializedManifest); } }
        /// <summary>
        /// Retrieve the manifest as XML
        /// </summary>
        public XmlReader ManifestReader
        {
            get
            {
                XmlReaderSettings settings = new XmlReaderSettings();
                settings.IgnoreComments = true;
                settings.IgnoreWhitespace = true;

                System.IO.MemoryStream stream = new System.IO.MemoryStream(serializedManifest);
                return XmlReader.Create(stream, settings);
            }
        }

        /// <summary>
        /// For debugging
        /// </summary>
        public override string ToString() { return Name + " " + Guid; }
        #region private
        internal ProviderManifest(byte[] serializedManifest, ManifestEnvelope.ManifestFormats format, byte majorVersion, byte minorVersion, string id)
        {
            this.serializedManifest = serializedManifest;
            this.majorVersion = majorVersion;
            this.minorVersion = minorVersion;
            this.format = format;
            this.id = id;
        }

        /// <summary>
        /// Call 'callback the the parsed templates for this provider.  If 'callback' returns RejectProvider, bail early
        /// Note that the DynamicTraceEventData passed to the delegate needs to be cloned if you use subscribe to it.   
        /// </summary>
        internal void ParseProviderEvents(Func<DynamicTraceEventData, EventFilterResponse> callback, bool noThrowOnError)
        {
            if (error != null)
            {
                goto THROW;
            }

            Init();
            try
            {
                Dictionary<string, int> opcodes = new Dictionary<string, int>(11)
                {
                    {"win:Info", 0},
                    {"win:Start", 1},
                    {"win:Stop", 2},
                    {"win:DC_Start", 3},
                    {"win:DC_Stop", 4},
                    {"win:Extension", 5},
                    {"win:Reply", 6},
                    {"win:Resume", 7},
                    {"win:Suspend", 8},
                    {"win:Send", 9},
                    {"win:Receive", 240}
                };
                Dictionary<string, TaskInfo> tasks = new Dictionary<string, TaskInfo>();
                Dictionary<string, TemplateInfo> templates = new Dictionary<string, TemplateInfo>();
                Dictionary<string, IDictionary<long, string>> maps = null;
                Dictionary<string, string> strings = new Dictionary<string, string>();
                IDictionary<long, string> map = null;
                List<EventInfo> events = new List<EventInfo>();
                bool alreadyReadMyCulture = false;            // I read my culture some time in the past (I can ignore things)
                string cultureBeingRead = null;
                bool inEventsElement = false;
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.EndElement && reader.Name == "events")
                    {
                        inEventsElement = false;
                    }

                    if (reader.NodeType != XmlNodeType.Element)
                    {
                        continue;
                    }

                    try
                    {
                        // TODO I currently require opcodes,and tasks BEFORE events BEFORE templates.  
                        // Can be fixed by going multi-pass. 
                        switch (reader.Name)
                        {
                            case "events":
                                inEventsElement = true;
                                break;
                            case "event":
                                {
                                    // Only look at event elements under the 'events' element.   This avoids 
                                    // perfTrack elements being considered.  
                                    if (!inEventsElement)
                                    {
                                        continue;
                                    }

                                    int taskNum = 0;
                                    Guid taskGuid = Guid.Empty;

                                    int eventID = int.Parse(reader.GetAttribute("value"));
                                    int opcode = 0;
                                    string opcodeName = reader.GetAttribute("opcode");
                                    if (opcodeName != null)
                                    {
                                        opcodes.TryGetValue(opcodeName, out opcode);
                                        // Strip off any namespace prefix.  TODO is this a good idea?
                                        int colon = opcodeName.IndexOf(':');
                                        if (colon >= 0)
                                        {
                                            opcodeName = opcodeName.Substring(colon + 1);
                                        }
                                    }
                                    else
                                    {
                                        opcodeName = "";
                                        // opcodeName = "UnknownEvent" + eventID.ToString();
                                    }

                                    string taskName = reader.GetAttribute("task");
                                    if (taskName != null)
                                    {
                                        TaskInfo taskInfo;
                                        if (tasks.TryGetValue(taskName, out taskInfo))
                                        {
                                            taskNum = taskInfo.id;
                                            taskGuid = taskInfo.guid;
                                        }
                                    }
                                    else
                                    {
                                        taskName = "";
                                        // This is sort of a hack but it allows people to use the symbol name as the task name
                                        // in a pinch.   
                                        string symbolName = reader.GetAttribute("symbol");
                                        if (symbolName != null && opcodeName == "")
                                        {
                                            taskName = symbolName;
                                        }
                                    }

                                    DynamicTraceEventData eventTemplate = new DynamicTraceEventData(
                                    null, eventID, taskNum, taskName, taskGuid, opcode, opcodeName, Guid, Name);
                                    events.Add(new EventInfo(eventTemplate, reader.GetAttribute("template")));

                                    // This will be looked up in the string table in a second pass.  
                                    eventTemplate.MessageFormat = reader.GetAttribute("message");
                                }
                                break;
                            case "template":
                                {
                                    string templateName = reader.GetAttribute("tid");
                                    Debug.Assert(templateName != null);
                                    using (var template = reader.ReadSubtree())
                                    {
                                        templates[templateName] = ComputeFieldInfo(template, maps);
                                    }
                                }
                                break;
                            case "opcode":
                                // TODO use message for opcode if it is available so it is localized.  
                                opcodes[reader.GetAttribute("name")] = int.Parse(reader.GetAttribute("value"));
                                break;
                            case "task":
                                {
                                    TaskInfo info = new TaskInfo();
                                    info.id = int.Parse(reader.GetAttribute("value"));
                                    string guidString = reader.GetAttribute("eventGUID");
                                    if (guidString != null)
                                    {
                                        info.guid = new Guid(guidString);
                                    }

                                    tasks[reader.GetAttribute("name")] = info;
                                }
                                break;
                            case "valueMap":
                                map = new Dictionary<long, string>();           // value maps use dictionaries
                                goto DoMap;
                            case "bitMap":
                                map = new SortedDictionary<long, string>();    // Bitmaps stored as sorted dictionaries
                                goto DoMap;
                                DoMap:
                                string name = reader.GetAttribute("name");
                                using (var mapValues = reader.ReadSubtree())
                                {
                                    while (mapValues.Read())
                                    {
                                        if (mapValues.Name == "map")
                                        {
                                            string keyStr = reader.GetAttribute("value");
                                            long key;
                                            if (keyStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                                            {
                                                // This is a work-around because some manifests have a 0x with no number afterward.  
                                                key = 0;
                                                if (keyStr.Length > 2)
                                                {
                                                    key = long.Parse(keyStr.Substring(2), System.Globalization.NumberStyles.AllowHexSpecifier);
                                                }
                                            }
                                            else
                                            {
                                                key = long.Parse(keyStr);
                                            }

                                            string value = reader.GetAttribute("message");
                                            map[key] = value;
                                        }
                                    }
                                }
                                if (maps == null)
                                {
                                    maps = new Dictionary<string, IDictionary<long, string>>();
                                }

                                maps[name] = map;
                                break;
                            case "resources":
                                {
                                    if (!alreadyReadMyCulture)
                                    {
                                        string desiredCulture = System.Globalization.CultureInfo.CurrentCulture.Name;
                                        if (cultureBeingRead != null && string.Compare(cultureBeingRead, desiredCulture, StringComparison.OrdinalIgnoreCase) == 0)
                                        {
                                            alreadyReadMyCulture = true;
                                        }

                                        cultureBeingRead = reader.GetAttribute("culture");
                                    }
                                }
                                break;
                            case "string":
                                if (!alreadyReadMyCulture)
                                {
                                    strings[reader.GetAttribute("id")] = reader.GetAttribute("value");
                                }

                                break;
                        }
                    }
                    catch (Exception e)
                    {
                        Trace.WriteLine("An exception occurred while processing the manifest: " + e.Message);
                        Trace.WriteLine("Trying to continue reading, to get a partial result but it is only a best effort.");
                    }
                }

                // localize strings for maps.
                if (maps != null)
                {
                    foreach (IDictionary<long, string> amap in maps.Values)
                    {
                        foreach (var keyValue in new List<KeyValuePair<long, string>>(amap))
                        {
                            Match m = Regex.Match(keyValue.Value, @"^\$\(string\.(.*)\)$");
                            if (m.Success)
                            {
                                string newValue;
                                if (strings.TryGetValue(m.Groups[1].Value, out newValue))
                                {
                                    amap[keyValue.Key] = newValue;
                                }
                            }
                        }
                    }
                }

                reader = null;      // Save some space 

                // Register all the events
                foreach (var eventInfo in events)
                {
                    var event_ = eventInfo.eventTemplate;
                    // Set the template if there is any. 
                    if (eventInfo.templateName != null)
                    {
                        var templateInfo = templates[eventInfo.templateName];
                        event_.payloadNames = templateInfo.payloadNames.ToArray();
                        event_.payloadFetches = templateInfo.payloadFetches.ToArray();
                    }
                    else
                    {
                        event_.payloadNames = new string[0];
                        event_.payloadFetches = new DynamicTraceEventData.PayloadFetch[0];
                    }

                    // before registering, localize any message format strings.  
                    string message = event_.MessageFormat;
                    if (message != null)
                    {
                        // Expect $(STRINGNAME) where STRINGNAME needs to be looked up in the string table
                        // TODO currently we just ignore messages without a valid string name.  Is that OK?
                        event_.MessageFormat = null;
                        Match m = Regex.Match(message, @"^\$\(string\.(.*)\)$");
                        if (m.Success)
                        {
                            strings.TryGetValue(m.Groups[1].Value, out event_.MessageFormat);
                        }
                    }

                    Debug.Assert(event_.Source == null);
                    if (callback(event_) == EventFilterResponse.RejectProvider)
                    {
                        return;
                    }
                }
                // Log the manifest event definition as well.  
                callback(new DynamicManifestTraceEventData(null, this));
            }
            catch (Exception e)
            {
                // TODO FIX NOW, log this!
                Trace.WriteLine("Error parsing the manifest for the provider " + (name ?? "UNKNOWN") + " " + e.ToString());
                version = -1;
                error = e;
            }

            THROW:
            if (!noThrowOnError && error != null)
            {
                throw new ApplicationException("Error parsing the manifest for the provider " + (this.name ?? "UNKNOWN"), error);
            }
        }

        private class EventInfo
        {
            public EventInfo(DynamicTraceEventData eventTemplate, string templateName)
            {
                this.eventTemplate = eventTemplate;
                this.templateName = templateName;
            }
            public DynamicTraceEventData eventTemplate;
            public string templateName;
        };

        private class TaskInfo
        {
            public int id;
            public Guid guid;
        };

        private class TemplateInfo
        {
            public List<string> payloadNames;
            public List<DynamicTraceEventData.PayloadFetch> payloadFetches;
        };

        private static TemplateInfo ComputeFieldInfo(XmlReader reader, Dictionary<string, IDictionary<long, string>> maps)
        {
            var ret = new TemplateInfo();

            ret.payloadNames = new List<string>();
            ret.payloadFetches = new List<DynamicTraceEventData.PayloadFetch>();
            ushort offset = 0;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.Name == "data")
                {
                    string inType = reader.GetAttribute("inType");
                    Type type = GetTypeForManifestTypeName(inType);
                    if (type == null)
                    {
                        Trace.WriteLine("Found an unsupported type " + inType + " skipping all fields after that.");
                        break;
                    }
                    ushort size = DynamicTraceEventData.SizeOfType(type);
                    // Strings are weird in that they are encoded multiple ways.  
                    if (type == typeof(string) && inType == "win:AnsiString")
                    {
                        size = DynamicTraceEventData.NULL_TERMINATED | DynamicTraceEventData.IS_ANSI;
                    }

                    var fieldName = reader.GetAttribute("name");
                    IDictionary<long, string> map = null;
                    string mapName = reader.GetAttribute("map");
                    if (mapName != null && maps != null)
                    {
                        maps.TryGetValue(mapName, out map);
                    }

                    var fieldFetch = new DynamicTraceEventData.PayloadFetch(offset, size, type, map);
                    if (inType == "win:Binary")
                    {
                        // Check to insure that the length field is the preceding field. 
                        int prevFieldIdx = ret.payloadNames.Count - 1;
                        string lengthStr = reader.GetAttribute("length");
                        if (lengthStr != null && 0 <= prevFieldIdx &&
                            lengthStr == ret.payloadNames[prevFieldIdx] &&
                                (ret.payloadFetches[prevFieldIdx].Type == typeof(int) || ret.payloadFetches[prevFieldIdx].Type == typeof(uint)))
                        {
                            // Remove the previous field, since it was just there to encode the length of the blob.   
                            if (offset != ushort.MaxValue)
                            {
                                offset -= 4;
                            }

                            ret.payloadNames.RemoveAt(prevFieldIdx);
                            ret.payloadFetches.RemoveAt(prevFieldIdx);

                            // Now the length is a prefix to the bytes. 
                            ushort fetchSize = DynamicTraceEventData.COUNTED_SIZE + DynamicTraceEventData.BIT_32 + DynamicTraceEventData.CONSUMES_FIELD + DynamicTraceEventData.ELEM_COUNT;
                            fieldFetch = DynamicTraceEventData.PayloadFetch.ArrayPayloadFetch(offset, fieldFetch, fetchSize);
                            size = fieldFetch.Size;
                        }
                        else
                        {
                            Trace.WriteLine("Only support win:Binary with preceding length fields");
                            break;
                        }
                    }
                    ret.payloadNames.Add(fieldName);
                    ret.payloadFetches.Add(fieldFetch);
                    if (offset != ushort.MaxValue)
                    {
                        Debug.Assert(size != 0);
                        if (size < DynamicTraceEventData.SPECIAL_SIZES)
                        {
                            offset += size;
                        }
                        else
                        {
                            offset = ushort.MaxValue;
                        }
                    }
                }
            }
            return ret;
        }

        /// <summary>
        /// Returns the .NET type corresponding to the manifest type 'manifestTypeName'
        /// Returns null if it could not be found. 
        /// </summary>
        private static Type GetTypeForManifestTypeName(string manifestTypeName)
        {
            switch (manifestTypeName)
            {
                case "win:Pointer":
                case "trace:SizeT":
                    return typeof(IntPtr);
                case "win:Boolean":
                    return typeof(bool);
                case "win:UInt8":
                    return typeof(byte);
                case "win:Int8":
                    return typeof(sbyte);
                case "win:Int16":
                    return typeof(short);
                case "win:UInt16":
                case "trace:Port":
                    return typeof(ushort);
                case "win:Int32":
                    return typeof(int);
                case "win:UInt32":
                case "trace:    ":
                case "trace:IPAddrV4":
                    return typeof(uint);
                case "win:Int64":
                case "trace:WmiTime":
                    return typeof(long);
                case "win:UInt64":
                    return typeof(ulong);
                case "win:Double":
                    return typeof(double);
                case "win:Float":
                    return typeof(float);
                case "win:AnsiString":
                case "win:UnicodeString":
                    return typeof(string);
                case "win:Binary":
                    return typeof(byte);        // We special case this later to make it an array of this type. 
                case "win:GUID":
                    return typeof(Guid);
                case "win:FILETIME":
                    return typeof(DateTime);
                default:
                    return null;
            }
        }

        #region IFastSerializable Members

        void IFastSerializable.ToStream(Serializer serializer)
        {
            serializer.Write(majorVersion);
            serializer.Write(minorVersion);
            serializer.Write((int)format);
            serializer.Write(id);
            int count = 0;
            if (serializedManifest != null)
            {
                count = serializedManifest.Length;
            }

            serializer.Write(count);
            for (int i = 0; i < count; i++)
            {
                serializer.Write(serializedManifest[i]);
            }
        }

        void IFastSerializable.FromStream(Deserializer deserializer)
        {
            deserializer.Read(out majorVersion);
            deserializer.Read(out minorVersion);
            format = (ManifestEnvelope.ManifestFormats)deserializer.ReadInt();
            deserializer.Read(out id);
            int count = deserializer.ReadInt();
            serializedManifest = new byte[count];
            for (int i = 0; i < count; i++)
            {
                serializedManifest[i] = deserializer.ReadByte();
            }

            Init();
        }

        /// <summary>
        /// Initialize the provider.  This means to advance the instance variable 'reader' until it it is at the 'provider' node
        /// in the XML.   It also has the side effect of setting the name and guid.  The rest waits until events are registered. 
        /// </summary>
        private void Init()
        {
            try
            {
                reader = ManifestReader;
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element)
                    {
                        continue;
                    }

                    if (reader.Name == "provider")
                    {
                        guid = new Guid(reader.GetAttribute("guid"));
                        name = reader.GetAttribute("name");
                        fileName = reader.GetAttribute("resourceFileName");
                        break;
                    }
                }

                if (name == null)
                {
                    throw new Exception("No provider element found in manifest");
                }
            }
            catch (Exception e)
            {
                Debug.Assert(false, "Exception during manifest parsing");
                name = "";
                error = e;
            }
            inited = true;
        }

        #endregion
        private XmlReader reader;
        private byte[] serializedManifest;
        private byte majorVersion;
        private byte minorVersion;
        private ManifestEnvelope.ManifestFormats format;
        private string id;      // simply identifies where this manifest came from (e.g. a file, or event)
        private Guid guid;
        private string name;
        private int version;
        private string fileName;
        private bool inited;
        private Exception error;

        #endregion
    }
}
