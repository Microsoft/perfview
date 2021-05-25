//     Copyright (c) Microsoft Corporation.  All rights reserved.
using FastSerialization;
using Microsoft.Diagnostics.Tracing.Compatibility;
using Microsoft.Diagnostics.Tracing.Extensions;
using Microsoft.Diagnostics.Tracing.Session;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Microsoft.Diagnostics.Tracing.Parsers
{
    /// <summary>
    /// RegisteredTraceEventParser uses the standard windows provider database (TDH, what gets registered with wevtutil)
    /// to find the names of events and fields of the events).   
    /// </summary>
    public sealed unsafe class RegisteredTraceEventParser : ExternalTraceEventParser
    {
        /// <summary>
        /// Create a new RegisteredTraceEventParser and attach it to the given TraceEventSource
        /// </summary>
        public RegisteredTraceEventParser(TraceEventSource source, bool dontRegister = false)
            : base(source, dontRegister)
        {

#if !DOTNET_V35
            var symbolSource = new SymbolTraceEventParser(source);

            symbolSource.MetaDataEventInfo += delegate (EmptyTraceData data)
            {
                DynamicTraceEventData template = (new RegisteredTraceEventParser.TdhEventParser((byte*)data.userData, null, MapTable)).ParseEventMetaData();

                // Uncomment this if you want to see the template in the debugger at this point
                // template.source = data.source;
                // template.eventRecord = data.eventRecord;
                // template.userData = data.userData;  
                m_state.m_templates[template] = template;
            };

            // Try to parse bitmap and value map information.  
            symbolSource.MetaDataEventMapInfo += delegate (EmptyTraceData data)
            {
                try
                {
                    Guid providerID = *((Guid*)data.userData);
                    byte* eventInfoBuffer = (byte*)(data.userData + sizeof(Guid));
                    RegisteredTraceEventParser.EVENT_MAP_INFO* eventInfo = (RegisteredTraceEventParser.EVENT_MAP_INFO*)eventInfoBuffer;
                    IDictionary<long, string> map = RegisteredTraceEventParser.TdhEventParser.ParseMap(eventInfo, eventInfoBuffer);
                    if (eventInfo->NameOffset < data.EventDataLength - 16)
                    {
                        string mapName = new string((char*)(&eventInfoBuffer[eventInfo->NameOffset]));
                        MapTable.Add(new MapKey(providerID, mapName), map);
                    }
                }
                catch (Exception) { };
            };

#endif


        }

        /// <summary>
        /// Given a provider name that has been registered with the operating system, get
        /// a string representing the ETW manifest for that provider.    Note that this
        /// manifest is not as rich as the original source manifest because some information
        /// is not actually compiled into the binary manifest that is registered with the OS.  
        /// </summary>
        public static string GetManifestForRegisteredProvider(string providerName)
        {
            var providerGuid = TraceEventProviders.GetProviderGuidByName(providerName);
            if (providerGuid == Guid.Empty)
            {
                throw new ApplicationException("Could not find provider with name " + providerName);
            }

            return GetManifestForRegisteredProvider(providerGuid);
        }
        /// <summary>
        /// Given a provider GUID that has been registered with the operating system, get
        /// a string representing the ETW manifest for that provider.    Note that this
        /// manifest is not as rich as the original source manifest because some information
        /// is not actually compiled into the binary manifest that is registered with the OS.  
        /// </summary>
        public static string GetManifestForRegisteredProvider(Guid providerGuid)
        {
            int buffSize = 84000;       // Still in the small object heap.  
            byte* buffer = (byte*)System.Runtime.InteropServices.Marshal.AllocHGlobal(buffSize);
            byte* enumBuffer = null;

            TraceEventNativeMethods.EVENT_RECORD eventRecord = new TraceEventNativeMethods.EVENT_RECORD();
            eventRecord.EventHeader.ProviderId = providerGuid;

            // We keep events of a given event number together in the output
            string providerName = null;
            SortedDictionary<int, StringWriter> events = new SortedDictionary<int, StringWriter>();
            // We keep tasks separated by task ID
            SortedDictionary<int, TaskInfo> tasks = new SortedDictionary<int, TaskInfo>();
            // Templates where the KEY is the template string and the VALUE is the template name (backwards)  
            Dictionary<string, string> templateIntern = new Dictionary<string, string>(8);

            // Remember any enum types we have.   Value is XML for the enum (normal) 
            Dictionary<string, string> enumIntern = new Dictionary<string, string>();
            StringWriter enumLocalizations = new StringWriter();

            // Any task names used so far 
            Dictionary<string, int> taskNames = new Dictionary<string, int>();
            // Any  es used so far 
            Dictionary<string, int> opcodeNames = new Dictionary<string, int>();
            // This insures that we have unique event names. 
            Dictionary<string, int> eventNames = new Dictionary<string, int>();

            SortedDictionary<ulong, string> keywords = new SortedDictionary<ulong, string>();
            List<ProviderDataItem> keywordsItems = TraceEventProviders.GetProviderKeywords(providerGuid);
            if (keywordsItems != null)
            {
                foreach (var keywordItem in keywordsItems)
                {
                    // Skip the reserved keywords.   
                    if (keywordItem.Value >= 1000000000000UL)
                    {
                        continue;
                    }

                    keywords[keywordItem.Value] = MakeLegalIdentifier(keywordItem.Name);
                }
            }

            for (int ver = 0; ver <= 255; ver++)
            {
                eventRecord.EventHeader.Version = (byte) ver;
                int count;
                int status;
                for (; ; )
                {
                    int dummy;
                    status = ETWParsing.TdhGetAllEventsInformation(&eventRecord, IntPtr.Zero, out dummy, out count, buffer, ref buffSize);
                    if (status != 122 || 20000000 < buffSize) // 122 == Insufficient buffer keep it under 2Meg
                    {
                        break;
                    }

                    Marshal.FreeHGlobal((IntPtr)buffer);
                    buffer = (byte*)Marshal.AllocHGlobal(buffSize);
                }

                // TODO FIX NOW deal with too small of a buffer.  
                if (status == 0)
                {
                    TRACE_EVENT_INFO** eventInfos = (TRACE_EVENT_INFO**)buffer;
                    for (int i = 0; i < count; i++)
                    {
                        TRACE_EVENT_INFO* eventInfo = eventInfos[i];
                        byte* eventInfoBuff = (byte*)eventInfo;
                        EVENT_PROPERTY_INFO* propertyInfos = &eventInfo->EventPropertyInfoArray;

                        if (providerName == null)
                        {
                            if (eventInfo->ProviderNameOffset != 0)
                            {
                                providerName = new string((char*)(&eventInfoBuff[eventInfo->ProviderNameOffset]));
                            }
                            else
                            {
                                providerName = "provider(" + eventInfo->ProviderGuid.ToString() + ")";
                            }
                        }

                        // Compute task name
                        string taskName = null;
                        if (eventInfo->TaskNameOffset != 0)
                        {
                            taskName = MakeLegalIdentifier((new string((char*)(&eventInfoBuff[eventInfo->TaskNameOffset]))));
                        }
                        if (taskName == null)
                        {
                            taskName = "task_" + eventInfo->EventDescriptor.Task.ToString();
                        }

                        // Insure task name is unique.  
                        int taskNumForName;
                        if (taskNames.TryGetValue(taskName, out taskNumForName) && taskNumForName != eventInfo->EventDescriptor.Task)
                        {
                            taskName = taskName + "_" + eventInfo->EventDescriptor.Task.ToString();
                        }

                        taskNames[taskName] = eventInfo->EventDescriptor.Task;

                        // Compute opcode name
                        string opcodeName = "";
                        if (eventInfo->EventDescriptor.Opcode != 0)
                        {
                            if (eventInfo->OpcodeNameOffset != 0)
                            {
                                opcodeName = MakeLegalIdentifier((new string((char*)(&eventInfoBuff[eventInfo->OpcodeNameOffset]))));
                            }
                            else
                            {
                                opcodeName = "opcode_" + eventInfo->EventDescriptor.Opcode.ToString();
                            }
                        }

                        // Insure opcode name is unique.  
                        int opcodeNumForName;
                        if (opcodeNames.TryGetValue(opcodeName, out opcodeNumForName) && opcodeNumForName != eventInfo->EventDescriptor.Opcode)
                        {
                            // If we did not find a name, use 'opcode and the disambiguator
                            if (eventInfo->OpcodeNameOffset == 0)
                            {
                                opcodeName = "opcode";
                            }

                            opcodeName = opcodeName + "_" + eventInfo->EventDescriptor.Task.ToString() + "_" + eventInfo->EventDescriptor.Opcode.ToString();
                        }
                        opcodeNames[opcodeName] = eventInfo->EventDescriptor.Opcode;

                        // And event name 
                        string eventName = taskName;
                        if (!taskName.EndsWith(opcodeName, StringComparison.OrdinalIgnoreCase))
                        {
                            eventName += Capitalize(opcodeName);
                        }

                        // Insure uniqueness of the event name
                        int eventNumForName;
                        if (eventNames.TryGetValue(eventName, out eventNumForName) && eventNumForName != eventInfo->EventDescriptor.Id)
                        {
                            eventName = eventName + eventInfo->EventDescriptor.Id.ToString();
                        }

                        eventNames[eventName] = eventInfo->EventDescriptor.Id;

                        // Get task information
                        TaskInfo taskInfo;
                        if (!tasks.TryGetValue(eventInfo->EventDescriptor.Task, out taskInfo))
                        {
                            tasks[eventInfo->EventDescriptor.Task] = taskInfo = new TaskInfo() { Name = taskName };
                        }

                        var symbolName = eventName;
                        if (eventInfo->EventDescriptor.Version > 0)
                        {
                            symbolName += "_V" + eventInfo->EventDescriptor.Version;
                        }

                        StringWriter eventWriter;
                        if (!events.TryGetValue(eventInfo->EventDescriptor.Id, out eventWriter))
                        {
                            events[eventInfo->EventDescriptor.Id] = eventWriter = new StringWriter();
                        }

                        eventWriter.Write("     <event value=\"{0}\" symbol=\"{1}\" version=\"{2}\" task=\"{3}\"",
                            eventInfo->EventDescriptor.Id,
                            symbolName,
                            eventInfo->EventDescriptor.Version,
                            taskName);
                        if (eventInfo->EventDescriptor.Opcode != 0)
                        {
                            string opcodeId;
                            if (eventInfo->EventDescriptor.Opcode < 10)       // It is a reserved opcode.  
                            {
                                // For some reason opcodeName does not have the underscore, which we need. 
                                if (eventInfo->EventDescriptor.Opcode == (byte)TraceEventOpcode.DataCollectionStart)
                                {
                                    opcodeId = "win:DC_Start";
                                }
                                else if (eventInfo->EventDescriptor.Opcode == (byte)TraceEventOpcode.DataCollectionStop)
                                {
                                    opcodeId = "win:DC_Stop";
                                }
                                else
                                {
                                    opcodeId = "win:" + opcodeName;
                                }
                            }
                            else
                            {
                                opcodeId = opcodeName;
                                if (taskInfo.Opcodes == null)
                                {
                                    taskInfo.Opcodes = new SortedDictionary<int, string>();
                                }

                                if (!taskInfo.Opcodes.ContainsKey(eventInfo->EventDescriptor.Opcode))
                                {
                                    taskInfo.Opcodes[eventInfo->EventDescriptor.Opcode] = opcodeId;
                                }
                            }
                            eventWriter.Write(" opcode=\"{0}\"", opcodeId);
                        }
                        // TODO handle cases outside standard levels 
                        if ((int)TraceEventLevel.Always <= eventInfo->EventDescriptor.Level && eventInfo->EventDescriptor.Level <= (int)TraceEventLevel.Verbose)
                        {
                            var asLevel = (TraceEventLevel)eventInfo->EventDescriptor.Level;
                            var levelName = "win:" + asLevel;
                            eventWriter.Write(" level=\"{0}\"", levelName);
                        }

                        var keywordStr = GetKeywordStr(keywords, (ulong)eventInfo->EventDescriptor.Keyword);
                        if (keywordStr.Length > 0)
                        {
                            eventWriter.Write(" keywords=\"" + keywordStr + "\"", eventInfo->EventDescriptor.Keyword);
                        }

                        if (eventInfo->TopLevelPropertyCount != 0)
                        {
                            var templateWriter = new StringWriter();
                            string[] propertyNames = new string[eventInfo->TopLevelPropertyCount];
                            for (int j = 0; j < eventInfo->TopLevelPropertyCount; j++)
                            {
                                EVENT_PROPERTY_INFO* propertyInfo = &propertyInfos[j];
                                var propertyName = new string((char*)(&eventInfoBuff[propertyInfo->NameOffset]));
                                propertyNames[j] = propertyName;
                                var enumAttrib = "";

                                // Deal with any maps (bit fields or enumerations)
                                if (propertyInfo->MapNameOffset != 0)
                                {
                                    string mapName = new string((char*)(&eventInfoBuff[propertyInfo->MapNameOffset]));

                                    if (enumBuffer == null)
                                    {
                                        enumBuffer = (byte*)System.Runtime.InteropServices.Marshal.AllocHGlobal(buffSize);
                                    }

                                    if (!enumIntern.ContainsKey(mapName))
                                    {
                                        EVENT_MAP_INFO* enumInfo = (EVENT_MAP_INFO*)enumBuffer;
                                        var hr = TdhGetEventMapInformation(&eventRecord, mapName, enumInfo, ref buffSize);
                                        if (hr == 0)
                                        {
                                            // We only support manifest enums for now.  
                                            if (enumInfo->Flag == MAP_FLAGS.EVENTMAP_INFO_FLAG_MANIFEST_VALUEMAP ||
                                                enumInfo->Flag == MAP_FLAGS.EVENTMAP_INFO_FLAG_MANIFEST_BITMAP)
                                            {
                                                StringWriter enumWriter = new StringWriter();
                                                string enumName = new string((char*)(&enumBuffer[enumInfo->NameOffset]));
                                                enumAttrib = " map=\"" + enumName + "\"";
                                                if (enumInfo->Flag == MAP_FLAGS.EVENTMAP_INFO_FLAG_MANIFEST_VALUEMAP)
                                                {
                                                    enumWriter.WriteLine("     <valueMap name=\"{0}\">", enumName);
                                                }
                                                else
                                                {
                                                    enumWriter.WriteLine("     <bitMap name=\"{0}\">", enumName);
                                                }

                                                EVENT_MAP_ENTRY* mapEntries = &enumInfo->MapEntryArray;
                                                for (int k = 0; k < enumInfo->EntryCount; k++)
                                                {
                                                    int value = mapEntries[k].Value;
                                                    string valueName = new string((char*)(&enumBuffer[mapEntries[k].NameOffset])).Trim();
                                                    enumWriter.WriteLine("      <map value=\"0x{0:x}\" message=\"$(string.map_{1}{2})\"/>", value, enumName, valueName);
                                                    enumLocalizations.WriteLine("    <string id=\"map_{0}{1}\" value=\"{2}\"/>", enumName, valueName, valueName);
                                                }
                                                if (enumInfo->Flag == MAP_FLAGS.EVENTMAP_INFO_FLAG_MANIFEST_VALUEMAP)
                                                {
                                                    enumWriter.WriteLine("     </valueMap>", enumName);
                                                }
                                                else
                                                {
                                                    enumWriter.WriteLine("     </bitMap>", enumName);
                                                }

                                                enumIntern[mapName] = enumWriter.ToString();
                                            }
                                        }
                                    }
                                }

                                // Remove anything that does not look like an ID (.e.g space)
                                propertyName = Regex.Replace(propertyName, "[^A-Za-z0-9_]", "");
                                TdhInputType propertyType = propertyInfo->InType;
                                string countOrLengthAttrib = "";

                                if ((propertyInfo->Flags & PROPERTY_FLAGS.ParamCount) != 0)
                                {
                                    countOrLengthAttrib = " count=\"" + propertyNames[propertyInfo->CountOrCountIndex] + "\"";
                                }
                                else if ((propertyInfo->Flags & PROPERTY_FLAGS.ParamLength) != 0)
                                {
                                    countOrLengthAttrib = " length=\"" + propertyNames[propertyInfo->LengthOrLengthIndex] + "\"";
                                }

                                templateWriter.WriteLine("      <data name=\"{0}\" inType=\"win:{1}\"{2}{3}/>", propertyName, propertyType.ToString(), enumAttrib, countOrLengthAttrib);
                            }
                            var templateStr = templateWriter.ToString();

                            // See if this template already exists, and if not make it 
                            string templateName;
                            if (!templateIntern.TryGetValue(templateStr, out templateName))
                            {
                                templateName = eventName + "Args";
                                if (eventInfo->EventDescriptor.Version > 0)
                                {
                                    templateName += "_V" + eventInfo->EventDescriptor.Version;
                                }

                                templateIntern[templateStr] = templateName;
                            }
                            eventWriter.Write(" template=\"{0}\"", templateName);
                        }
                        eventWriter.WriteLine("/>");
                    }
                }
                else if (status == 1168 && ver != 0)        // Not Found give up
                {
                    break;
                }
            }
            if (enumBuffer != null)
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal((IntPtr)enumBuffer);
            }

            System.Runtime.InteropServices.Marshal.FreeHGlobal((IntPtr)buffer);
            if (providerName == null)
            {
                throw new ApplicationException("Could not find provider with at GUID of " + providerGuid.ToString());
            }

            StringWriter manifest = new StringWriter();
            manifest.WriteLine("<instrumentationManifest xmlns=\"http://schemas.microsoft.com/win/2004/08/events\">");
            manifest.WriteLine(" <instrumentation xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:win=\"http://manifests.microsoft.com/win/2004/08/windows/events\">");
            manifest.WriteLine("  <events>");
            manifest.WriteLine("   <provider name=\"{0}\" guid=\"{{{1}}}\" resourceFileName=\"{0}\" messageFileName=\"{0}\" symbol=\"{2}\" source=\"Xml\" >",
                    providerName, providerGuid, Regex.Replace(providerName, @"[^\w]", ""));

            StringWriter localizedStrings = new StringWriter();

            if (keywords != null)
            {
                manifest.WriteLine("    <keywords>");
                foreach (var keyValue in keywords)
                {
                    manifest.WriteLine("     <keyword name=\"{0}\" message=\"$(string.keyword_{1})\" mask=\"0x{2:x}\"/>",
                        keyValue.Value, keyValue.Value, keyValue.Key);
                    localizedStrings.WriteLine("    <string id=\"keyword_{0}\" value=\"{1}\"/>", keyValue.Value, keyValue.Value);
                }
                manifest.WriteLine("    </keywords>");
            }

            manifest.WriteLine("    <tasks>");
            foreach (var taskValue in tasks.Keys)
            {
                var task = tasks[taskValue];
                manifest.WriteLine("     <task name=\"{0}\" message=\"$(string.task_{1})\" value=\"{2}\"{3}>", task.Name, task.Name, taskValue,
                    task.Opcodes == null ? "/" : "");       // If no opcodes, terminate immediately.  
                localizedStrings.WriteLine("    <string id=\"task_{0}\" value=\"{1}\"/>", task.Name, task.Name);
                if (task.Opcodes != null)
                {
                    manifest.WriteLine(">");
                    manifest.WriteLine("      <opcodes>");
                    foreach (var keyValue in task.Opcodes)
                    {
                        manifest.WriteLine("       <opcode name=\"{0}\" message=\"$(string.opcode_{1}{2})\" value=\"{3}\"/>",
                            keyValue.Value, task.Name, keyValue.Value, keyValue.Key);
                        localizedStrings.WriteLine("    <string id=\"opcode_{0}{1}\" value=\"{2}\"/>", task.Name, keyValue.Value, keyValue.Value);
                    }
                    manifest.WriteLine("      </opcodes>");
                    manifest.WriteLine("     </task>");
                }
            }
            manifest.WriteLine("    </tasks>");

            if (enumIntern.Count > 0)
            {
                manifest.WriteLine("    <maps>");
                foreach (var map in enumIntern.Values)
                {
                    manifest.Write(map);
                }

                manifest.WriteLine("    </maps>");
                localizedStrings.Write(enumLocalizations.ToString());
            }

            manifest.WriteLine("    <events>");
            foreach (StringWriter eventStr in events.Values)
            {
                manifest.Write(eventStr.ToString());
            }

            manifest.WriteLine("    </events>");

            manifest.WriteLine("    <templates>");
            foreach (var keyValue in templateIntern)
            {
                manifest.WriteLine("     <template tid=\"{0}\">", keyValue.Value);
                manifest.Write(keyValue.Key);
                manifest.WriteLine("     </template>");
            }
            manifest.WriteLine("    </templates>");
            manifest.WriteLine("   </provider>");
            manifest.WriteLine("  </events>");
            manifest.WriteLine(" </instrumentation>");
            string strings = localizedStrings.ToString();
            if (strings.Length > 0)
            {
                manifest.WriteLine(" <localization>");
                manifest.WriteLine("  <resources culture=\"{0}\">", IetfLanguageTag(CultureInfo.CurrentCulture));
                manifest.WriteLine("   <stringTable>");
                manifest.Write(strings);
                manifest.WriteLine("   </stringTable>");
                manifest.WriteLine("  </resources>");
                manifest.WriteLine(" </localization>");
            }

            manifest.WriteLine("</instrumentationManifest>");
            return manifest.ToString(); ;
        }

        #region private
        // Borrowed from Core CLR System.Globalization.CultureInfo
        private static string IetfLanguageTag(CultureInfo culture)
        {
            // special case the compatibility cultures
            switch (culture.Name)
            {
                case "zh-CHT":
                    return "zh-Hant";
                case "zh-CHS":
                    return "zh-Hans";
                default:
                    return culture.Name;
            }
        }


        private static string MakeLegalIdentifier(string name)
        {
            // TODO FIX NOW beef this up.
            name = name.Replace(" ", "");
            name = name.Replace("-", "_");
            return name;
        }

        /// <summary>
        /// Generates a space separated list of set of keywords 'keywordSet' using the table 'keywords'
        /// It will generate new keyword names if needed and add them to 'keywords' if they are not present.  
        /// </summary>
        private static string GetKeywordStr(SortedDictionary<ulong, string> keywords, ulong keywordSet)
        {
            var ret = "";
            // TODO FIX NOW  what should we be doing here?   I do want pass along channel information
            // We skip the reserved keywords (48 and above)
            for (int i = 0; i < 48; i++)
            {
                ulong keyword = 1UL << i;
                if ((keyword & keywordSet) != 0)
                {
                    string keywordStr;
                    if (!keywords.TryGetValue(keyword, out keywordStr))
                    {
                        keywordStr = "keyword_" + keyword.ToString("x");
                        keywords[keyword] = keywordStr;
                    }
                    if (ret.Length != 0)
                    {
                        ret += " ";
                    }

                    ret += keywordStr;
                }
            }
            return ret;
        }

        /// <summary>
        /// Class used to accumulate information about Tasks in the implementation of GetManifestForRegisteredProvider
        /// </summary>
        private class TaskInfo
        {
            public string Name;
            public SortedDictionary<int, string> Opcodes;
        }

        private static string Capitalize(string str)
        {
            if (str.Length == 0)
            {
                return str;
            }

            char c = str[0];
            if (Char.IsUpper(c))
            {
                return str;
            }

            return (str.Substring(1).ToUpper() + str.Substring(1));
        }

        internal override DynamicTraceEventData TryLookup(TraceEvent unknownEvent)
        {
            return TryLookupWorker(unknownEvent, MapTable);
        }

        /// <summary>
        /// Try to look up 'unknonwEvent using TDH or the TraceLogging mechanism.   if 'mapTable' is non-null it will be used
        /// look up the string names for fields that have bitsets or enumerated values.   This is only need for the KernelTraceControl
        /// case where the map information is logged as special events and can't be looked up with TDH APIs.  
        /// </summary>
        internal static DynamicTraceEventData TryLookupWorker(TraceEvent unknownEvent, Dictionary<MapKey, IDictionary<long, string>> mapTable = null)
        {
            // Is this a TraceLogging style 
            DynamicTraceEventData ret = null;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Trace logging events are not guaranteed to be on channel 11.
                // Trace logging events will have one of these headers.
                bool hasETWEventInformation = false;
                for (int i = 0; i != unknownEvent.eventRecord->ExtendedDataCount; i++)
                {
                    var extType = unknownEvent.eventRecord->ExtendedData[i].ExtType;
                    if (extType == TraceEventNativeMethods.EVENT_HEADER_EXT_TYPE_EVENT_KEY ||
                        extType == TraceEventNativeMethods.EVENT_HEADER_EXT_TYPE_EVENT_SCHEMA_TL)
                    {
                        hasETWEventInformation = true;
                        break;
                    }
                }


                // TODO cache the buffer?, handle more types, handle structs...
                int buffSize = 9000;
                byte* buffer = (byte*)System.Runtime.InteropServices.Marshal.AllocHGlobal(buffSize);
                int status = TdhGetEventInformation(unknownEvent.eventRecord, 0, null, buffer, &buffSize);
                if (status == 122)      // Buffer too small
                {
                    System.Runtime.InteropServices.Marshal.FreeHGlobal((IntPtr)buffer);
                    buffer = (byte*)System.Runtime.InteropServices.Marshal.AllocHGlobal(buffSize);
                    status = TdhGetEventInformation(unknownEvent.eventRecord, 0, null, buffer, &buffSize);
                }

                if (status == 0)
                {
                    ret = (new TdhEventParser(buffer, unknownEvent.eventRecord, mapTable)).ParseEventMetaData();
                    ret.containsSelfDescribingMetadata = hasETWEventInformation;
                }

                System.Runtime.InteropServices.Marshal.FreeHGlobal((IntPtr)buffer);
            }

            return ret;
        }

        /// <summary>
        /// TdhEventParser takes the Trace Diagnostics Helper (TDH) TRACE_EVENT_INFO structure and
        /// (passed as a byte*) and converts it to a DynamicTraceEventData which which 
        /// can be used to parse events of that type.   You first create TdhEventParser and then
        /// call ParseEventMetaData to do the parsing.  
        /// </summary>
        internal class TdhEventParser
        {
            /// <summary>
            /// Creates a new parser from the TRACE_EVENT_INFO held in 'buffer'.  Use
            /// ParseEventMetaData to then parse it into a DynamicTraceEventData structure.
            ///  EventRecord can be null and mapTable if present allow the parser to resolve maps (enums), and can be null.  
            /// </summary>
            public TdhEventParser(byte* eventInfo, TraceEventNativeMethods.EVENT_RECORD* eventRecord, Dictionary<MapKey, IDictionary<long, string>> mapTable)
            {
                eventBuffer = eventInfo;
                this.eventInfo = (TRACE_EVENT_INFO*)eventInfo;
                propertyInfos = &this.eventInfo->EventPropertyInfoArray;
                this.eventRecord = eventRecord;
                this.mapTable = mapTable;
            }

            /// <summary>
            /// Actually performs the parsing of the TRACE_EVENT_INFO passed in the constructor
            /// </summary>
            /// <returns></returns>
            public DynamicTraceEventData ParseEventMetaData()
            {
                EVENT_PROPERTY_INFO* propertyInfos = &eventInfo->EventPropertyInfoArray;
                string taskName = null;
                if (eventInfo->TaskNameOffset != 0)
                {
                    taskName = MakeLegalIdentifier(new string((char*)(&eventBuffer[eventInfo->TaskNameOffset])));
                }

                string opcodeName = null;
                if (eventInfo->OpcodeNameOffset != 0)
                {
                    opcodeName = new string((char*)(&eventBuffer[eventInfo->OpcodeNameOffset]));
                    if (opcodeName.StartsWith("win:"))
                    {
                        opcodeName = opcodeName.Substring(4);
                    }

                    opcodeName = MakeLegalIdentifier(opcodeName);
                }

                string providerName = "UnknownProvider";
                if (eventInfo->ProviderNameOffset != 0)
                {
                    providerName = new string((char*)(&eventBuffer[eventInfo->ProviderNameOffset]));
                }

                var eventID = eventInfo->EventDescriptor.Id;
                // Mark it as a classic event if necessary. 
                if (eventInfo->DecodingSource == 1) // means it is from MOF (Classic)
                {
                    eventID = (int)TraceEventID.Illegal;
                }

                var newTemplate = new DynamicTraceEventData(null, eventID,
                    eventInfo->EventDescriptor.Task, taskName,
                    eventInfo->EventGuid,
                    eventInfo->EventDescriptor.Opcode, opcodeName,
                    eventInfo->ProviderGuid, providerName);

                if (eventID == (int)TraceEventID.Illegal)
                {
                    newTemplate.lookupAsClassic = true;
                }

                if (eventInfo->EventMessageOffset != 0)
                {
                    newTemplate.MessageFormat = new string((char*)(&eventBuffer[eventInfo->EventMessageOffset]));
                }

                Debug.WriteLine("In TdhEventParser for event" + providerName + "/" + taskName + "/" + opcodeName + " with " + eventInfo->TopLevelPropertyCount + " fields");
                DynamicTraceEventData.PayloadFetchClassInfo fields = ParseFields(0, eventInfo->TopLevelPropertyCount);
                newTemplate.payloadNames = fields.FieldNames;
                newTemplate.payloadFetches = fields.FieldFetches;

                return newTemplate;      // return this as the event template for this lookup. 
            }

            /// <summary>
            /// Parses at most 'maxFields' fields starting at the current position.  
            /// Will return the parse fields in 'payloadNamesRet' and 'payloadFetchesRet'
            /// Will return true if successful, false means an error occurred.  
            /// </summary>
            private DynamicTraceEventData.PayloadFetchClassInfo ParseFields(int startField, int numFields)
            {
                ushort fieldOffset = 0;
                var fieldNames = new List<string>(numFields);
                var fieldFetches = new List<DynamicTraceEventData.PayloadFetch>(numFields);

                for (int curField = 0; curField < numFields; curField++)
                {
                    DynamicTraceEventData.PayloadFetch propertyFetch = new DynamicTraceEventData.PayloadFetch();
                    var propertyInfo = &propertyInfos[curField + startField];
                    var propertyName = new string((char*)(&eventBuffer[propertyInfo->NameOffset]));
                    // Remove anything that does not look like an ID (.e.g space)
                    propertyName = Regex.Replace(propertyName, "[^A-Za-z0-9_]", "");

                    // If it is an array, the field offset starts over at 0 because they are 
                    // describing the ELMEMENT not the array and thus each element starts at 0
                    // Strings do NOT describe the element and thus don't get this treatment. 
                    var arrayFieldOffset = fieldOffset;
                    if ((propertyInfo->Flags & (PROPERTY_FLAGS.ParamCount | PROPERTY_FLAGS.ParamLength)) != 0 &&
                        propertyInfo->InType != TdhInputType.UnicodeString && propertyInfo->InType != TdhInputType.AnsiString)
                    {
                        fieldOffset = 0;
                    }

                    // Is this a nested struct?
                    if ((propertyInfo->Flags & PROPERTY_FLAGS.Struct) != 0)
                    {
                        int numStructFields = propertyInfo->NumOfStructMembers;
                        Debug.WriteLine("   " + propertyName + " Is a nested type with " + numStructFields + " fields {");
                        DynamicTraceEventData.PayloadFetchClassInfo classInfo = ParseFields(propertyInfo->StructStartIndex, numStructFields);
                        if (classInfo == null)
                        {
                            Debug.WriteLine("    Failure parsing nested struct.");
                            goto Exit;
                        }
                        Debug.WriteLine(" } " + propertyName + " Nested struct completes.");
                        propertyFetch = DynamicTraceEventData.PayloadFetch.StructPayloadFetch(fieldOffset, classInfo);
                    }
                    else // A normal type
                    {
                        propertyFetch = new DynamicTraceEventData.PayloadFetch(fieldOffset, propertyInfo->InType, propertyInfo->OutType);
                        if (propertyFetch.Size == DynamicTraceEventData.UNKNOWN_SIZE)
                        {
                            Trace.WriteLine("    Unknown type for  " + propertyName + " " + propertyInfo->InType.ToString() + " fields from here will be missing.");
                            goto Exit;
                        }

                        // Deal with any maps (bit fields or enumerations)
                        if (propertyInfo->MapNameOffset != 0)
                        {
                            string mapName = new string((char*)(&eventBuffer[propertyInfo->MapNameOffset]));

                            // Normal case, you can look up the enum information immediately. 
                            if (eventRecord != null)
                            {
                                int buffSize = 84000;  // TODO this is inefficient (and incorrect for very large enums).  
                                byte* enumBuffer = (byte*)System.Runtime.InteropServices.Marshal.AllocHGlobal(buffSize);

                                EVENT_MAP_INFO* enumInfo = (EVENT_MAP_INFO*)enumBuffer;
                                var hr = TdhGetEventMapInformation(eventRecord, mapName, enumInfo, ref buffSize);
                                if (hr == 0)
                                {
                                    propertyFetch.Map = ParseMap(enumInfo, enumBuffer);
                                }

                                System.Runtime.InteropServices.Marshal.FreeHGlobal((IntPtr)enumBuffer);
                            }
                            else
                            {
                                // This is the kernelTraceControl case,  the map information will be provided
                                // later, so we have to set up a LAZY map which will be evaluated when we need the
                                // enum (giving time for the enum definition to be processed. 
                                var mapKey = new MapKey(eventInfo->ProviderGuid, mapName);

                                // Set the map to be a lazyMap, which is a Func that returns a map.  
                                Func<IDictionary<long, string>> lazyMap = delegate ()
                                {
                                    IDictionary<long, string> map = null;
                                    if (mapTable != null)
                                    {
                                        mapTable.TryGetValue(mapKey, out map);
                                    }

                                    return map;
                                };
                                propertyFetch.LazyMap = lazyMap;
                            }
                        }
                    }

                    // is this dynamically sized with another field specifying the length?
                    // Is it an array (binary and not a struct) (seems InType is not valid if property is a struct, so need to test for both.
                    if ((propertyInfo->Flags & (PROPERTY_FLAGS.ParamCount | PROPERTY_FLAGS.ParamLength | PROPERTY_FLAGS.ParamFixedCount)) != 0 || propertyInfo->CountOrCountIndex > 1 || (propertyInfo->InType == TdhInputType.Binary && (propertyInfo->Flags & PROPERTY_FLAGS.Struct) == 0))
                    {
                        // silliness where if it is a byte[] they use Length otherwise they use count.  Normalize it.  
                        var countOrCountIndex = propertyInfo->CountOrCountIndex;
                        if ((propertyInfo->Flags & PROPERTY_FLAGS.ParamLength) != 0 || propertyInfo->InType == TdhInputType.Binary)
                        {
                            countOrCountIndex = propertyInfo->LengthOrLengthIndex;
                        }

                        ushort fixedCount = 0;
                        ushort arraySize;
                        if ((propertyInfo->Flags & (PROPERTY_FLAGS.ParamFixedLength | PROPERTY_FLAGS.ParamFixedCount)) != 0)
                        {
                            fixedCount = countOrCountIndex;
                            arraySize = fixedCount;
                        }
                        else
                        {
                            // We only support the case where the length/count is right before the array.   We remove this field
                            // and use the PREFIX size to indicate that the size of the array is determined by the 32 or 16 bit number before 
                            // the array data.   
                            if (countOrCountIndex == startField + curField - 1)
                            {
                                var lastFieldIdx = fieldFetches.Count - 1;
                                arraySize = DynamicTraceEventData.COUNTED_SIZE + DynamicTraceEventData.CONSUMES_FIELD + DynamicTraceEventData.ELEM_COUNT;
                                if (fieldFetches[lastFieldIdx].Size == 4)
                                {
                                    arraySize += DynamicTraceEventData.BIT_32;
                                }
                                else if (fieldFetches[lastFieldIdx].Size != 2)
                                {
                                    Trace.WriteLine("WARNING: Unexpected dynamic length size, giving up");
                                    goto Exit;
                                }

                                // remove the previous field (so we have to adjust our offset)
                                if (arrayFieldOffset != ushort.MaxValue)
                                {
                                    arrayFieldOffset -= fieldFetches[lastFieldIdx].Size;
                                }

                                fieldNames.RemoveAt(lastFieldIdx);
                                fieldFetches.RemoveAt(lastFieldIdx);
                            }
                            else
                            {
                                Trace.WriteLine("    Error: Array is variable sized and does not follow  prefix convention.");
                                goto Exit;
                            }
                        }

                        // Strings are treated specially (we don't treat them as an array of chars).  
                        // They don't need an arrayFetch but DO need set the size and offset appropriately
                        if (propertyFetch.Type == typeof(string))
                        {
                            // This is a string with its size determined by another field.   Set the size
                            // based on 'arraySize' but preserver the IS_ANSI that we got from looking at the tdhInType.  
                            propertyFetch.Size = (ushort)(arraySize | (propertyFetch.Size & DynamicTraceEventData.IS_ANSI));
                            propertyFetch.Offset = arrayFieldOffset;
                        }
                        else
                        {
                            Debug.WriteLine("     Field is an array of size " + ((fixedCount != 0) ? fixedCount.ToString() : "VARIABLE") + " of type " + ((propertyFetch.Type ?? typeof(void))) + " at offset " + arrayFieldOffset.ToString("x"));
                            propertyFetch = DynamicTraceEventData.PayloadFetch.ArrayPayloadFetch(arrayFieldOffset, propertyFetch, arraySize, fixedCount);
                        }

                        fieldOffset = ushort.MaxValue;           // Indicate that the next offset must be computed at run time. 
                    }

                    fieldFetches.Add(propertyFetch);
                    fieldNames.Add(propertyName);
                    var size = propertyFetch.Size;
                    Debug.WriteLine("    Got TraceLogging Field " + propertyName + " " + (propertyFetch.Type ?? typeof(void)) + " size " + size.ToString("x") + " offset " + fieldOffset.ToString("x") + " (void probably means array)");

                    Debug.Assert(0 < size);
                    if (size >= DynamicTraceEventData.SPECIAL_SIZES)
                    {
                        fieldOffset = ushort.MaxValue;           // Indicate that the offset must be computed at run time. 
                    }
                    else if (fieldOffset != ushort.MaxValue)
                    {
                        Debug.Assert(fieldOffset + size < ushort.MaxValue);
                        fieldOffset += size;
                    }
                }

                Exit:
                var ret = new DynamicTraceEventData.PayloadFetchClassInfo() { FieldNames = fieldNames.ToArray(), FieldFetches = fieldFetches.ToArray() };
                return ret; ;
            }

            // Parses a EVENT_MAP_INFO into a Dictionary for a Value map or a SortedDictionary for a Bitmap
            // returns null if it does not know how to parse it.  
            internal static unsafe IDictionary<long, string> ParseMap(EVENT_MAP_INFO* enumInfo, byte* enumBuffer)
            {
                IDictionary<long, string> map = null;
                // We only support manifest enums for now.  
                if (enumInfo->Flag == MAP_FLAGS.EVENTMAP_INFO_FLAG_MANIFEST_VALUEMAP ||
                    enumInfo->Flag == MAP_FLAGS.EVENTMAP_INFO_FLAG_MANIFEST_BITMAP)
                {
                    StringWriter enumWriter = new StringWriter();
                    string enumName = new string((char*)(&enumBuffer[enumInfo->NameOffset]));

                    if (enumInfo->Flag == MAP_FLAGS.EVENTMAP_INFO_FLAG_MANIFEST_VALUEMAP)
                    {
                        map = new Dictionary<long, string>();
                    }
                    else
                    {
                        map = new SortedDictionary<long, string>();
                    }

                    EVENT_MAP_ENTRY* mapEntries = &enumInfo->MapEntryArray;
                    for (int k = 0; k < enumInfo->EntryCount; k++)
                    {
                        int value = mapEntries[k].Value;
                        string valueName = new string((char*)(&enumBuffer[mapEntries[k].NameOffset])).Trim();
                        map[value] = valueName;
                    }
                }
                return map;
            }

            #region private
            private TRACE_EVENT_INFO* eventInfo;
            private TraceEventNativeMethods.EVENT_RECORD* eventRecord;
            private Dictionary<MapKey, IDictionary<long, string>> mapTable;     // table of enums that have defined. 
            private EVENT_PROPERTY_INFO* propertyInfos;
            private byte* eventBuffer;                           // points at the eventInfo, but in increments of bytes 
            #endregion // private
        }

        [DllImport("tdh.dll")]
        internal static extern int TdhGetEventInformation(
            TraceEventNativeMethods.EVENT_RECORD* pEvent,
            uint TdhContextCount,
            void* pTdhContext,
            byte* pBuffer,
            int* pBufferSize);


        [DllImport("tdh.dll", CharSet = CharSet.Unicode)]
        internal static extern int TdhGetEventMapInformation(
            TraceEventNativeMethods.EVENT_RECORD* pEvent,
            string pMapName,
            EVENT_MAP_INFO* info,
            ref int infoSize
        );

        [Flags]
        internal enum MAP_FLAGS
        {
            EVENTMAP_INFO_FLAG_MANIFEST_VALUEMAP = 1,
            EVENTMAP_INFO_FLAG_MANIFEST_BITMAP = 2,
            EVENTMAP_INFO_FLAG_MANIFEST_PATTERNMAP = 4,
            EVENTMAP_INFO_FLAG_WBEM_VALUEMAP = 8,
            EVENTMAP_INFO_FLAG_WBEM_BITMAP = 16,
            EVENTMAP_INFO_FLAG_WBEM_FLAG = 32,
            EVENTMAP_INFO_FLAG_WBEM_NO_MAP = 64
        };

        [StructLayout(LayoutKind.Sequential)]
        internal struct EVENT_MAP_ENTRY
        {
            public int NameOffset;
            public int Value;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct EVENT_MAP_INFO
        {
            public int NameOffset;
            public MAP_FLAGS Flag;
            public int EntryCount;
            public int ValueType;                  // I don't expect patterns, I expect this to be 0 (which means normal enums).  
            public EVENT_MAP_ENTRY MapEntryArray;  // Actually an array, this is the first element.  
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct TRACE_EVENT_INFO
        {
            public Guid ProviderGuid;
            public Guid EventGuid;
            public EVENT_DESCRIPTOR EventDescriptor;
            public int DecodingSource;
            public int ProviderNameOffset;
            public int LevelNameOffset;
            public int ChannelNameOffset;
            public int KeywordsNameOffset;
            public int TaskNameOffset;
            public int OpcodeNameOffset;
            public int EventMessageOffset;
            public int ProviderMessageOffset;
            public int BinaryXmlOffset;
            public int BinaryXmlSize;
            public int ActivityIDNameOffset;
            public int RelatedActivityIDNameOffset;
            public int PropertyCount;
            public int TopLevelPropertyCount;
            public int Flags;
            public EVENT_PROPERTY_INFO EventPropertyInfoArray;  // Actually an array, this is the first element.  
        }

        public struct EVENT_DESCRIPTOR
        {
            public ushort Id;
            public byte Version;
            public byte Channel;
            public byte Level;
            public byte Opcode;
            public ushort Task;
            public ulong Keyword;
        }

        internal struct EVENT_PROPERTY_INFO
        {
            public PROPERTY_FLAGS Flags;
            public int NameOffset;

            // These are valid if Flags & Struct not set. 
            public TdhInputType InType;
            public ushort OutType;             // Really TDH_OUT_TYPE
            public int MapNameOffset;

            // These are valid if Flags & Struct is set.  
            public int StructStartIndex
            {
                get
                {
                    System.Diagnostics.Debug.Assert((Flags & PROPERTY_FLAGS.Struct) != 0);
                    return (ushort)InType;
                }
            }
            public int NumOfStructMembers
            {
                get
                {
                    System.Diagnostics.Debug.Assert((Flags & PROPERTY_FLAGS.Struct) != 0);
                    return (ushort)OutType;
                }
            }

            // Normally Count is 1 (thus every field in an array, it is just that most array have fixed size of 1)
            public ushort CountOrCountIndex;                // Flags & ParamFixedLength determines if it count, otherwise countIndex 
                                                            // Normally Length is the size of InType (thus is fixed), but can be variable for blobs.
            public ushort LengthOrLengthIndex;              // Flags & ParamLength determines if it lengthIndex otherwise it is the length InType
            public int Reserved;
        }

        [Flags]
        internal enum PROPERTY_FLAGS
        {
            None = 0,
            Struct = 0x1,
            ParamLength = 0x2,
            ParamCount = 0x4,
            WbemXmlFragment = 0x8,
            ParamFixedLength = 0x10,
            ParamFixedCount = 0x20
        }

        internal enum TdhInputType : ushort
        {
            Null,
            UnicodeString,
            AnsiString,
            Int8,
            UInt8,
            Int16,
            UInt16,
            Int32,
            UInt32,
            Int64,
            UInt64,
            Float,
            Double,
            Boolean,
            Binary,
            GUID,
            Pointer,
            FILETIME,
            SYSTEMTIME,
            SID,
            HexInt32,
            HexInt64,                   // End of winmeta intypes 21
            CountedUtf16String = 22,    // new for TraceLogging
            CountedMbcsString = 23,
            Struct = 24,

            CountedString = 300,        // Start of TDH intypes for WBEM
            CountedAnsiString,
            ReversedCountedString,
            ReversedCountedAnsiString,
            NonNullTerminatedString,
            NonNullTerminatedAnsiString,
            UnicodeChar,
            AnsiChar,
            SizeT,
            HexDump,
            WbemSID
        };
        #endregion
    }

    /// <summary>
    /// ExternalTraceEventParser is an abstract class that acts as a parser for any 'External' resolution
    /// This include the TDH (RegisteredTraceEventParser) as well as the WPPTraceEventParser.   
    /// </summary>
    public abstract unsafe class ExternalTraceEventParser : TraceEventParser
    {
        /// <summary>
        /// Create a new ExternalTraceEventParser and attach it to the given TraceEventSource
        /// </summary>
        protected unsafe ExternalTraceEventParser(TraceEventSource source, bool dontRegister = false)
            : base(source, dontRegister)
        {
            m_state = (ExternalTraceEventParserState)StateObject;
            if (m_state == null)
            {
                StateObject = m_state = new ExternalTraceEventParserState();
                m_state.m_templates = new Dictionary<TraceEvent, DynamicTraceEventData>(new ExternalTraceEventParserState.TraceEventComparer());

                this.source.RegisterUnhandledEvent(delegate (TraceEvent unknown)
                {
                    // See if we already have this definition 
                    DynamicTraceEventData parsedTemplate = null;

                    if (!m_state.m_templates.TryGetValue(unknown, out parsedTemplate))
                    {
                        parsedTemplate = TryLookup(unknown);
                        if (parsedTemplate == null)
                        {
                            m_state.m_templates.Add(unknown.Clone(), null);         // add an entry to remember that we tried and failed.  
                        }
                    }
                    if (parsedTemplate == null)
                    {
                        return false;
                    }

                    // registeredWithTraceEventSource is a fail safe.   Basically if you added yourself to the table
                    // (In OnNewEventDefinition) then you should not come back as unknown, however because of dual events
                    // and just general fragility we don't want to rely on that.  So we keep a bit and insure that we
                    // only add the event definition once.  
                    if (!parsedTemplate.registeredWithTraceEventSource)
                    {
                        parsedTemplate.registeredWithTraceEventSource = true;
                        bool ret = OnNewEventDefintion(parsedTemplate, false) == EventFilterResponse.AcceptEvent;

                        // If we have subscribers, notify them as well.  
                        var newEventDefinition = NewEventDefinition;
                        if (newEventDefinition != null)
                        {
                            ret |= (NewEventDefinition(parsedTemplate, false) == EventFilterResponse.AcceptEvent);
                        }

                        return ret;
                    }
                    return false;
                });
            }
        }

        /// <summary>
        /// Override.  
        /// </summary>
        public override bool IsStatic { get { return false; } }

        #region private
        internal Func<TraceEvent, bool, EventFilterResponse> NewEventDefinition;

        /// <summary>
        /// Override
        /// </summary>
        protected override string GetProviderName()
        {
            // We handle more than one provider, so the convention is to return null. 
            return null;
        }

        /// <summary>
        /// Returns true if the RegisteredTraceEventParser would return 'template' in EnumerateTemplates
        /// </summary>
        internal bool HasDefinitionForTemplate(TraceEvent template)
        {
            if (m_state == null)
            {
                m_state = (ExternalTraceEventParserState)StateObject;
            }

            if (m_state != null)
            {
                return m_state.m_templates.ContainsKey(template);
            }

            return false;
        }

        /// <summary>
        /// override
        /// </summary>
        protected internal override void EnumerateTemplates(Func<string, string, EventFilterResponse> eventsToObserve, Action<TraceEvent> callback)
        {
            // Normally state is setup in the constructor, but call can be invoked before the constructor has finished, 
            if (m_state == null)
            {
                m_state = (ExternalTraceEventParserState)StateObject;
            }

            if (m_state != null)
            {
                foreach (var template in m_state.m_templates.Values)
                {
                    if (template == null)
                    {
                        continue;
                    }

                    if (eventsToObserve == null || eventsToObserve(template.ProviderName, template.EventName) == EventFilterResponse.AcceptEvent)
                    {
                        callback(template);
                    }
                }
            }
        }

        /// <summary>
        /// Register 'template' so that if there are any subscriptions to template they get registered with the source.    
        /// </summary>
        internal override EventFilterResponse OnNewEventDefintion(TraceEvent template, bool mayHaveExistedBefore)
        {
            m_state.m_templates[template] = (DynamicTraceEventData)template;
            return base.OnNewEventDefintion(template, mayHaveExistedBefore);
        }

        internal abstract DynamicTraceEventData TryLookup(TraceEvent unknownEvent);

        internal ExternalTraceEventParserState m_state;

        internal Dictionary<MapKey, IDictionary<long, string>> MapTable
        {
            get
            {
                if (m_maps == null)
                {
                    m_maps = new Dictionary<MapKey, IDictionary<long, string>>();
                }

                return m_maps;
            }
        }

        private Dictionary<MapKey, IDictionary<long, string>> m_maps;       // Any maps (enums or bitsets) defined by KernelTraceControl events.  

        #endregion
    }

    #region internal classes
    /// <summary>
    /// Used to look up Enums (provider x enumName);  Very boring class.  
    /// </summary>
    internal class MapKey : IEquatable<MapKey>
    {
        public MapKey(Guid providerID, string mapName)
        {
            ProviderID = providerID;
            MapName = mapName;
        }
        public Guid ProviderID;
        public string MapName;

        public override int GetHashCode()
        {
            return MapName.GetHashCode() + ProviderID.GetHashCode();
        }
        public override bool Equals(object obj)
        {
            var asEnumKey = obj as MapKey;
            return asEnumKey != null && Equals(asEnumKey);
        }

        public bool Equals(MapKey other)
        {
            return ProviderID == other.ProviderID && MapName == other.MapName;
        }
    }


    /// <summary>
    /// TDHDynamicTraceEventParserState represents the state of a  TDHDynamicTraceEventParser that needs to be
    /// serialized to a log file.  It does NOT include information about what events are chosen but DOES contain
    /// any other necessary information that came from the ETL data file or the OS TDH APIs.  
    /// </summary>
    internal class ExternalTraceEventParserState : IFastSerializable
    {
        public ExternalTraceEventParserState() { }

        // This is set of all dynamic event templates that this parser has TRIED to resolve.  If resolution 
        // failed then the value is null, otherwise it is the same as the key.   
        internal Dictionary<TraceEvent, DynamicTraceEventData> m_templates;

        /// <summary>
        /// This defines what it means to be the same event.   For manifest events it means provider and event ID
        /// for classic, it means that taskGuid and opcode match.  
        /// </summary>
        internal class TraceEventComparer : IEqualityComparer<TraceEvent>
        {
            public bool Equals(TraceEvent x, TraceEvent y)
            {
                Debug.Assert(!(x.lookupAsWPP && x.lookupAsClassic));
                if (x.lookupAsClassic != y.lookupAsClassic)
                {
                    return false;
                }

                if (x.lookupAsWPP != y.lookupAsWPP)
                {
                    return false;
                }

                if (x.lookupAsClassic)
                {
                    Debug.Assert(x.taskGuid != Guid.Empty && y.taskGuid != Guid.Empty);
                    return (x.taskGuid == y.taskGuid) && (x.Opcode == y.Opcode);
                }
                else if (x.lookupAsWPP)
                {
                    Debug.Assert(x.taskGuid != Guid.Empty && y.taskGuid != Guid.Empty);
                    return (x.taskGuid == y.taskGuid) && (x.ID == y.ID);
                }
                else
                {
                    Debug.Assert(x.ProviderGuid != Guid.Empty && y.ProviderGuid != Guid.Empty);
                    return (x.ProviderGuid == y.ProviderGuid) && (x.ID == y.ID);
                }
            }
            public int GetHashCode(TraceEvent obj)
            {
                if (obj.lookupAsClassic)
                {
                    return obj.taskGuid.GetHashCode() + (int)obj.Opcode;
                }
                else if (obj.lookupAsWPP)
                {
                    return obj.taskGuid.GetHashCode() + (int)obj.ID;
                }
                else
                {
                    return obj.ProviderGuid.GetHashCode() + (int)obj.ID;
                }
            }
        }

        #region IFastSerializable Members
        /// <summary>
        /// Implements IFastSerializable interface
        /// </summary>
        public virtual void ToStream(Serializer serializer)
        {
            // Calculate the count.  
            var count = 0;
            foreach (var template in m_templates.Values)
            {
                if (template != null)
                {
                    count++;
                }
            }

            serializer.Write(count);
            foreach (var template in m_templates.Values)
            {
                if (template != null)
                {
#if DEBUG
                    --count;
#endif
                    serializer.Write(template);
                }
            }
            Debug.Assert(count == 0);
        }

        /// <summary>
        /// Implements IFastSerializable interface
        /// </summary>
        public virtual void FromStream(Deserializer deserializer)
        {
            int count;
            deserializer.Read(out count);
            m_templates = new Dictionary<TraceEvent, DynamicTraceEventData>(count, new TraceEventComparer());
            for (int i = 0; i < count; i++)
            {
                DynamicTraceEventData template;
                deserializer.Read(out template);
                m_templates.Add(template, template);
            }
        }
        #endregion
    }

    #endregion
}
