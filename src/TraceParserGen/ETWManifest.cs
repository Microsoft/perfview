using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml;

namespace ETWManifest
{
    /// <summary>
    /// A ProviderManifest represents the XML manifest associated with the provider.
    /// </summary>
    public sealed class Provider
    {
        public static List<Provider> ParseManifest(string manifest)
        {
            var xmlReader = XmlReader.Create(manifest, new XmlReaderSettings() { IgnoreComments = true, IgnoreWhitespace = true });
            return ParseManifest(xmlReader, manifest);
        }
        public static List<Provider> ParseManifest(XmlReader reader, string fileName = null)
        {
            var ret = new List<Provider>();
            if (reader.ReadToDescendant("events"))
            {
                using (var events = reader.ReadSubtree())
                {
                    if (events.ReadToDescendant("provider"))
                    {
                        do
                        {
                            ret.Add(new Provider(reader, fileName));
                        } while (events.ReadToNextSibling("provider"));
                    }
                }
            }

            // Resolve all the localization strings that may have been used.
            var stringMap = new Dictionary<string, string>();
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.Name == "string")
                {
                    var id = reader.GetAttribute("id");
                    var value = reader.GetAttribute("value");
                    stringMap[id] = value;
                }
            }
            if (0 < stringMap.Count)
            {
                foreach (var provider in ret)
                {
                    if (provider.m_keywordNames != null)
                    {
                        for (int i = 0; i < provider.m_keywordNames.Length; i++)
                        {
                            Provider.Replace(ref provider.m_keywordNames[i], stringMap);
                        }
                    }

                    if (provider.m_taskNames != null)
                    {
                        foreach (var taskId in new List<int>(provider.m_taskNames.Keys))
                        {
                            var taskName = provider.m_taskNames[taskId];
                            if (Provider.Replace(ref taskName, stringMap))
                            {
                                provider.m_taskNames[taskId] = taskName;
                            }
                        }
                    }

                    if (provider.m_opcodeNames != null)
                    {
                        foreach (var opcodeId in new List<int>(provider.m_opcodeNames.Keys))
                        {
                            var opcodeName = provider.m_opcodeNames[opcodeId];
                            if (Provider.Replace(ref opcodeName, stringMap))
                            {
                                provider.m_opcodeNames[opcodeId] = opcodeName;
                            }
                        }
                    }

                    foreach (Event ev in provider.Events)
                    {
                        ev.UpdateStrings(stringMap);
                    }
                }
            }

            return ret;
        }

        /// <summary>
        /// The name of the ETW provider
        /// </summary>
        public string Name { get; private set; }
        /// <summary>
        /// The GUID that uniquely identifies the ETW provider
        /// </summary>
        public Guid Id { get; private set; }
        /// <summary>
        /// The events for the
        /// </summary>
        public IList<Event> Events { get { return m_events; } }

        /* Support for pretty printing the Keywords */
        /// <summary>
        /// returns the name of the keyword which has the bit position bitPos
        /// (bitPos is 0 through 63).   It may return null if there is no
        /// keyword name for 'bitPos'.
        /// </summary>
        public string GetKeywordName(int bitPos)
        {
            Debug.Assert(0 <= bitPos && bitPos < 64);
            if (m_keywordNames == null)
            {
                return null;
            }

            return m_keywordNames[bitPos];
        }
        /// <summary>
        /// returns a string that is the best human readable representation of the keyword set
        /// represented by 'keywords'.   It the concatenation of all the keyword names separated
        /// by a comma, as well as a hexadecimal number (if there is anything that can't be
        /// represented by names).
        /// </summary>
        public string GetKeywordSetString(ulong keywords, string separator = ",")
        {
            var ret = "";
            ulong bitsWithNoName = 0;
            ulong bit = 1;
            for (int bitPos = 0; bitPos < 64; bitPos++)
            {
                if (keywords == 0)
                {
                    break;
                }

                if ((bit & keywords) != 0)
                {
                    var name = m_keywordNames[bitPos];
                    if (name != null)
                    {
                        if (ret.Length != 0)
                        {
                            ret += separator;
                        }

                        ret += name;
                    }
                    else
                    {
                        bitsWithNoName |= bit;
                    }
                }
                bit = bit << 1;
            }
            if (bitsWithNoName != 0)
            {
                if (ret.Length != 0)
                {
                    ret += separator;
                }

                ret += "0x" + bitsWithNoName.ToString("x");
            }
            if (ret.Length == 0)
            {
                ret = "0";
            }

            return ret;
        }

        /* These are relatively rare APIs, normally you only care in the context of an event */
        public string GetTaskName(ushort taskId)
        {
            if (m_taskNames == null)
            {
                return "";
            }

            string ret;
            if (m_taskNames.TryGetValue(taskId, out ret))
            {
                return ret;
            }

            if (taskId == 0)
            {
                return "";
            }

            return "Task" + taskId.ToString();
        }

        public string GetOpcodeName(ushort taskId, byte opcodeId)
        {
            int value = opcodeId + (taskId * 256);
            string ret;
            if (m_opcodeNames.TryGetValue(value, out ret))
            {
                return ret;
            }

            value = opcodeId + (GlobalScope * 256);
            if (m_opcodeNames.TryGetValue(value, out ret))
            {
                return ret;
            }

            return "Opcode" + opcodeId;
        }

        /// <summary>
        /// For debugging
        /// </summary>
        public override string ToString() { return Name + " " + Id; }

        #region private
        // create a manifest from a stream or a file
        /// <summary>
        /// Read a ProviderManifest from a stream
        /// </summary>
        internal Provider(XmlReader reader, string fileName)
        {
            var lineInfo = (IXmlLineInfo)reader;
            try
            {
                Debug.Assert(reader.NodeType == XmlNodeType.Element && reader.Name == "provider",
                    "Must advance to provider element (e.g. call ReadToDescendant)");

                m_enums = new Dictionary<string, Enumeration>();
                m_templateValues = new Dictionary<string, List<Field>>();
                InitStandardOpcodes();

                Name = reader.GetAttribute("name");
                Id = Guid.Parse(reader.GetAttribute("guid"));

                var inputDepth = reader.Depth;
                reader.Read();      // Advance to children
                var curTask = GlobalScope;
                var curTaskDepth = int.MaxValue;
                while (inputDepth < reader.Depth)
                {
                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        switch (reader.Name)
                        {
                            case "keyword":
                                {
                                    if (m_keywordNames == null)
                                    {
                                        m_keywordNames = new string[64];
                                        m_keywordValues = new Dictionary<string, ulong>();
                                    }
                                    string name = reader.GetAttribute("name");
                                    string valueString = reader.GetAttribute("mask");
                                    ulong value = ParseNumber(valueString);
                                    int keywordIndex = GetBitPosition(value);

                                    string message = reader.GetAttribute("message");
                                    if (message == null)
                                    {
                                        message = name;
                                    }

                                    m_keywordNames[keywordIndex] = message;
                                    m_keywordValues.Add(name, value);
                                    reader.Skip();
                                }
                                break;
                            case "task":
                                {
                                    if (m_taskNames == null)
                                    {
                                        m_taskNames = new Dictionary<int, string>();
                                        m_taskValues = new Dictionary<string, int>();
                                    }
                                    string name = reader.GetAttribute("name");
                                    int value = (int)ParseNumber(reader.GetAttribute("value"));

                                    string message = reader.GetAttribute("message");
                                    if (message == null)
                                    {
                                        message = name;
                                    }

                                    m_taskNames.Add(value, message);
                                    m_taskValues.Add(name, value);

                                    // Remember enuough to resolve opcodes nested inside this task.
                                    curTask = value;
                                    curTaskDepth = reader.Depth;
                                    reader.Read();
                                }
                                break;
                            case "opcode":
                                {
                                    string name = reader.GetAttribute("name");
                                    int value = (int)ParseNumber(reader.GetAttribute("value"));
                                    int taskForOpcode = GlobalScope;
                                    if (reader.Depth > curTaskDepth)
                                    {
                                        taskForOpcode = curTask;
                                    }

                                    string message = reader.GetAttribute("message");
                                    if (message == null)
                                    {
                                        message = name;
                                    }

                                    AddOpcode(taskForOpcode, value, message, name);
                                    reader.Skip();
                                    break;
                                }
                            case "event":
                                m_events.Add(new Event(reader, this, lineInfo.LineNumber));
                                break;
                            case "template":
                                ReadTemplate(reader);
                                break;
                            case "bitMap":
                                ReadMap(reader, true);
                                break;
                            case "valueMap":
                                ReadMap(reader, false);
                                break;
                            default:
                                Debug.WriteLine("Skipping unknown element: {0}", (object)reader.Name);
                                reader.Read();
                                break;
                        }
                    }
                    else if (!reader.Read())
                    {
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                if (fileName == null)
                {
                    fileName = "";
                }

                var message = "Error on line " + fileName + "(" + lineInfo.LineNumber + "," + lineInfo.LinePosition + "): " + e.Message;
                throw new ApplicationException(message, e);
            }

            // Second pass, look up any Ids used in the events.
            foreach (var ev in m_events)
            {
                ev.ResolveIdsInEvent(fileName);
            }

            // We are done with these.  Free up some space.
            m_taskValues = null;
            m_opcodeValues = null;
            m_keywordValues = null;
            m_templateValues = null;
            m_enums = null;
        }


        // opcodes live inside a particular task, or you can have 'global' scope for the
        // opcode.   'GlobalScope' is a 'task' for this global scope.  Just needs to be an illegal task number.
        private const int GlobalScope = 0x10000;
        /// <summary>
        /// Adds an opcode with a  given name to the database.   'taskId' can be GlobalScope
        /// if the opcode works for any task.   'manifestName' is the name in the manifest (e.g. "win:Stop")
        /// and 'name' is the name that you will print (e.g. "Stop").   If manifestName is null then
        /// name and manifest name are considered the same.
        /// </summary>
        private void AddOpcode(int taskId, int opcodeId, string name, string manifestName = null)
        {
            Debug.Assert(0 <= taskId && taskId <= GlobalScope);
            Debug.Assert(0 <= opcodeId && opcodeId < 256);
            int value = opcodeId + (taskId * 256);
            m_opcodeNames.Add(value, name);
            if (manifestName == null)
            {
                manifestName = name;
            }

            // Prefix the key with the taskID if the opcode is local to the task.
            string key = manifestName;
            if (taskId != GlobalScope)
                key = taskId + ":" + key;

            m_opcodeValues.Add(key, value);
        }

        internal static ulong ParseNumber(string valueString)
        {
            if (valueString.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return ulong.Parse(valueString.Substring(2), System.Globalization.NumberStyles.AllowHexSpecifier);
            }

            return ulong.Parse(valueString);
        }

        private static int GetBitPosition(ulong value)
        {
            if (value == 0)
            {
                throw new ApplicationException("Keyword is not a power of 2");
            }

            int ret = 0;
            for (; ; )
            {
                if (((int)value & 1) != 0)
                {
                    if (value == 0)
                    {
                        throw new ApplicationException("Keyword is not a power of 2");
                    }

                    break;
                }
                value = value >> 1;
                ret++;
            }
            return ret;
        }

        internal static bool Replace(ref string value, Dictionary<string, string> map)
        {
            if (value == null)
            {
                return false;
            }

            if (!value.Contains("$(string."))
            {
                return false;
            }

            var ret = false;
            value = Regex.Replace(value, @"\$\(string\.(.*)\)", delegate (Match m)
            {
                ret = true;
                var key = m.Groups[1].Value;
                return map[key];
            });
            return ret;
        }

        private void InitStandardOpcodes()
        {
            m_opcodeNames = new Dictionary<int, string>();
            m_opcodeValues = new Dictionary<string, int>();

            AddOpcode(GlobalScope, 0, "", "win:Info");
            AddOpcode(GlobalScope, 1, "Start", "win:Start");
            AddOpcode(GlobalScope, 2, "Stop", "win:Stop");
            AddOpcode(GlobalScope, 3, "DC_Start", "win:DC_Start");
            AddOpcode(GlobalScope, 4, "DC_Stop", "win:DC_Stop");
            AddOpcode(GlobalScope, 5, "Extension", "win:Extension");
            AddOpcode(GlobalScope, 6, "Reply", "win:Reply");
            AddOpcode(GlobalScope, 7, "Resume", "win:Resume");
            AddOpcode(GlobalScope, 8, "Suspend", "win:Suspend");
            AddOpcode(GlobalScope, 9, "Send", "win:Send");
            AddOpcode(GlobalScope, 240, "Receive", "win:Receive");
        }

        private void ReadMap(XmlReader reader, bool isBitMap)
        {
            Debug.Assert(reader.NodeType == XmlNodeType.Element && (reader.Name == "bitMap" || reader.Name == "valueMap"),
                "Must advance to bitMap/valueMap element (e.g. call ReadToDescendant)");

            string name = reader.GetAttribute("name");
            Enumeration enumeration = new Enumeration(name, isBitMap);
            var inputDepth = reader.Depth;
            reader.Read();      // Advance to children
            while (inputDepth < reader.Depth)
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    switch (reader.Name)
                    {
                        case "map":
                            {
                                int value = (int)ParseNumber(reader.GetAttribute("value"));
                                string message = reader.GetAttribute("message");
                                enumeration.Add(value, message);
                            }
                            break;
                        default:
                            Debug.WriteLine("Skipping unknown element {0}", reader.Name);
                            break;
                    }
                }
                if (!reader.Read())
                {
                    break;
                }
            }
            m_enums.Add(name, enumeration);
        }

        private void ReadTemplate(XmlReader reader)
        {
            Debug.Assert(reader.NodeType == XmlNodeType.Element && reader.Name == "template",
                 "Must advance to template element (e.g. call ReadToDescendant)");

            string tid = reader.GetAttribute("tid");
            List<Field> template;
            if (m_templateValues.TryGetValue(tid, out template))
            {
                if (template.Count != 0)
                {
                    throw new ApplicationException("Template " + tid + " is defined twice");
                }
            }
            else
            {
                m_templateValues[tid] = template = new List<Field>();
            }

            var inputDepth = reader.Depth;
            reader.Read();      // Advance to children
            while (inputDepth < reader.Depth)
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    switch (reader.Name)
                    {
                        case "data":
                            {
                                string name = reader.GetAttribute("name");
                                string inTypeStr = reader.GetAttribute("inType");
                                var mapId = reader.GetAttribute("map");
                                var countField = reader.GetAttribute("count");
                                countField = countField ?? reader.GetAttribute("length");
                                template.Add(new Field(name, inTypeStr, mapId, countField));
                            }
                            break;
                        default:
                            Debug.WriteLine("Skipping unknown element {0}", reader.Name);
                            break;
                    }
                }
                if (!reader.Read())
                {
                    break;
                }
            }
        }

        private List<Event> m_events = new List<Event>();
        internal string[] m_keywordNames;
        internal Dictionary<int, string> m_taskNames;
        // Note that the key is task << 8 + opcode to allow for private opcode names
        internal Dictionary<int, string> m_opcodeNames;

        // These are not used after parsing.
        internal Dictionary<string, int> m_taskValues;
        // Note that the value is task << 8 + opcode to allow for private opcode names
        // Also the key is taskId : opcodeName again to allow private opcode names or simply opcodeName if it is global.
        internal Dictionary<string, int> m_opcodeValues;
        internal Dictionary<string, ulong> m_keywordValues;
        internal Dictionary<string, List<Field>> m_templateValues;
        internal Dictionary<string, Enumeration> m_enums;
        #endregion
    }

    /// <summary>
    /// An event represents the Event Element in the manifest.   It describes the meta-data of one ETW event
    /// </summary>
    public sealed class Event
    {
        public Provider Provider { get { return m_provider; } }
        public ushort Id { get; private set; }
        public ulong Keywords { get; private set; }
        /// <summary>
        /// A convenience method that returns the keywords as symbol names (comma separated)
        /// </summary>
        public string KeywordsString { get { return Provider.GetKeywordSetString(Keywords); } }
        public byte Version { get; private set; }
        public byte Level { get; private set; }
        public byte Opcode { get; private set; }
        public ushort Task { get; private set; }
        public string Channel { get; private set; }
        /// <summary>
        /// The fields associated with this event.  This can be null if there are no fields.
        /// </summary>
        public IList<Field> Fields { get; private set; }
        public string TaskName { get { return m_provider.GetTaskName(Task); } }
        public string OpcodeName { get { return m_provider.GetOpcodeName(Task, Opcode); } }
        /// <summary>
        /// The event name is synthesis (concatenation) of the Task and Opcode names.   This is never null.
        /// </summary>
        public string EventName
        {
            get
            {
                if (TaskName == null)
                {
                    return Capitalize(OpcodeName);
                }

                // could be that the task name is the prefix of the Opcode name
                if (OpcodeName.StartsWith(TaskName, StringComparison.OrdinalIgnoreCase))
                {
                    return Capitalize(OpcodeName);
                }

                return TaskName + Capitalize(OpcodeName);
            }
        }
        public string Message { get; internal set; }

        /* These are not guaranteed to be present */
        /// <summary>
        /// Technically the Symbol attribute on an event is not part of the model (it is not stored in the binary manifest)
        /// but if it is present, it can be used to create better names.   This value can be null.
        /// </summary>
        public string Symbol { get; internal set; }
        /// <summary>
        /// Technically the name of the Template for the fields on an event is not part of the model (it is not stored in the binary manifest)
        /// but if it is present, it can be used to create better names.   This value can be null.
        /// </summary>
        public string TemplateName { get; internal set; }
        /// <summary>
        /// Get the line number in the file.  Used for error messages.
        /// </summary>
        public int LineNum { get; private set; }
        #region private
        internal static string Capitalize(string str)
        {
            if (0 < str.Length)
            {
                str = str.Substring(0, 1).ToUpper() + str.Substring(1);
            }

            return str;
        }

        internal static string CamelCase(string str)
        {
            if (str.IndexOf(' ') < 0)
            {
                return str;
            }

            string[] compontents = str.Split(' ');
            for (int i = 1; i < compontents.Length; i++)
            {
                compontents[i] = Capitalize(compontents[i]);
            }

            return string.Join("", compontents);
        }

        private byte ParseLevel(string levelStr)
        {
            switch (levelStr)
            {
                case "win:Always":
                    return 0;
                case "win:Critical":
                    return 1;
                case "win:Error":
                    return 2;
                case "win:Warning":
                    return 3;
                case "win:Informational":
                    return 4;
                case "win:Verbose":
                    return 5;
                default:
                    int ret;
                    if (int.TryParse(levelStr, out ret) && (byte)ret == ret)
                    {
                        return (byte)ret;
                    }

                    return 4;   // Informational
            }
        }
        internal Event(XmlReader reader, Provider provider, int lineNum)
        {
            m_lineNum = lineNum;
            m_provider = provider;
            Debug.Assert(reader.NodeType == XmlNodeType.Element && reader.Name == "event",
                 "Must advance to event element (e.g. call ReadToDescendant)");

            LineNum = ((IXmlLineInfo)reader).LineNumber;

            for (bool doMore = reader.MoveToFirstAttribute(); doMore; doMore = reader.MoveToNextAttribute())
            {
                switch (reader.Name)
                {
                    case "symbol":
                        Symbol = reader.Value;
                        break;
                    case "version":
                        Version = (byte)ETWManifest.Provider.ParseNumber(reader.Value);
                        break;
                    case "value":
                        Id = (ushort)ETWManifest.Provider.ParseNumber(reader.Value);
                        break;
                    case "task":
                        m_taskId = reader.Value;
                        break;
                    case "opcode":
                        m_opcodeId = reader.Value;
                        break;
                    case "keywords":
                        m_keywordsId = reader.Value;
                        break;
                    case "level":
                        Level = ParseLevel(reader.Value);
                        break;
                    case "template":
                        TemplateName = reader.Value;
                        break;
                    case "message":
                        Message = reader.Value;     // This is actually just a ref to the localization data, but we don't have that yet.
                        break;
                    case "channel":
                        Channel = reader.Value;
                        break;
                    default:
                        Debug.WriteLine("Skipping unknown event attribute " + reader.Name);
                        break;
                }
            }
        }

        /// <summary>
        /// We need a two pass system where after all the definitions are parsed, we go back and link
        /// up uses to their defs.   This routine does this for Events.
        /// </summary>
        internal void ResolveIdsInEvent(string fileName = null)
        {
            string id = "";
            try
            {
                id = m_taskId;
                m_taskId = null;
                if (id != null)
                {
                    Task = (ushort)m_provider.m_taskValues[id];
                }

                id = m_opcodeId;
                m_opcodeId = null;
                if (id != null)
                {
                    int opcode;

                    // Try the task-specific one, then the global scope.
                    if (!m_provider.m_opcodeValues.TryGetValue(Task + ":" + id, out opcode))
                        opcode = m_provider.m_opcodeValues[id];

                    Opcode = (byte) opcode;
                }

                id = m_keywordsId;
                m_keywordsId = null;
                if (id != null)
                    if (m_provider.m_keywordValues != null)
                    {
                        var keywordsList = id.Split(' ');
                        foreach (var currKeyword in keywordsList)
                        {
                            Keywords |= m_provider.m_keywordValues[currKeyword];
                        }
                    }

                id = TemplateName;
                if (id != null)
                {
                    List<Field> fields;
                    if (!m_provider.m_templateValues.TryGetValue(id, out fields))
                    {
                        m_provider.m_templateValues[id] = fields = new List<Field>();
                    }

                    Fields = fields;
                    foreach (var field in Fields)
                    {
                        if (field.m_mapId != null)
                        {
                            field.Enumeration = m_provider.m_enums[field.m_mapId];
                        }
                    }
                }
            }
            catch(Exception)
            {
                if (fileName == null)
                {
                    fileName = "";
                }

                throw new ApplicationException("Error " + fileName + "(" + m_lineNum + "): Undefined Id " + id);
            }
        }

        internal void UpdateStrings(Dictionary<string, string> stringMap)
        {
            var message = Message;
            if (Provider.Replace(ref message, stringMap))
            {
                Message = message;
            }

            if (Fields != null)
            {
                foreach (var parameter in Fields)
                {
                    if (parameter.Enumeration != null)
                    {
                        parameter.Enumeration.UpdateStrings(stringMap);
                    }
                }
            }
        }

        private Provider m_provider;
        private int m_lineNum;          // used for error messages
        private string m_opcodeId;      // String name used to look up opcode needed because def may be later in file
        private string m_keywordsId;    // String name used to look up keywords needed because def may be later in file
        private string m_taskId;        // String name used to look up tasks needed because def may be later in file
        #endregion
    }

    /// <summary>
    /// A field represents one field in a ETW event
    /// </summary>
    public sealed class Field
    {
        /// <summary>
        /// The name of the field.
        /// </summary>
        public string Name { get; private set; }
        /// <summary>
        /// If the type is a structure, then this points at its type.   'Enumeration' and 'Type' will be null in this case.
        /// </summary>
        public Struct Struct { get; private set; }
        /// <summary>
        /// If Type is a integral type, then Enumeration can be set which gives values either
        /// a bitfield or a enumerated type for the values of the parameter.    Otherwise it is null.
        /// </summary>
        public Enumeration Enumeration { get; internal set; }
        /// <summary>
        /// If the type is 'built in' then it has a string name, and this is it.   This is null for structs and an integer type for Enumerations.
        /// </summary>
        public string Type { get; private set; }

        /// <summary>
        /// There is no Array type.  Instead all field can have a 'Count' which might be a number
        /// (for fixed sized arrays) or a name of a Field (for variable sized arrays).    This
        /// can be null, which means the field is not an array.
        /// </summary>
        public string CountField { get; private set; }
        /// <summary>
        /// If set, it indicates that the integer value should be pretty-printed as hexadecimal rather than decimal.
        /// </summary>
        public bool HexFormat { get; private set; }
        #region private
        internal Field(string name, string type, string mapId, string countField = null) { Name = name; Type = type; m_mapId = mapId; CountField = countField; }
        internal string m_mapId;

        #endregion
    }

    /// <summary>
    /// A struct represents a composite type and can be used in field definitions
    /// </summary>
    public sealed class Struct
    {
        /// <summary>
        /// Then name of the type of the structure as a whole.
        /// </summary>
        public string Name { get; private set; }
        public IList<Field> Fields { get; private set; }
    }

    /// <summary>
    /// A Enumeration represents an enumerated type and can be used in field definitions.
    /// </summary>
    public sealed class Enumeration
    {
        public string Name { get; private set; }
        public bool IsBitField { get; private set; }
        public string GetNameForValue(int value)
        {
            return null;
        }
        public IEnumerable<KeyValuePair<int, string>> Values { get { return m_values; } }
        #region private
        internal Enumeration(string name, bool isBitField) { Name = name; IsBitField = isBitField; }
        internal void Add(int value, string name)
        {
            string otherName;
            if (m_values.TryGetValue(value, out otherName))
            {
                Console.WriteLine("Error: in Enumeration {0} the names {1} and {2} have the same value {3}",
                    Name, name, otherName, value);
                return;
            }
            m_values[value] = name;
        }

        internal void UpdateStrings(Dictionary<string, string> stringMap)
        {
            if (m_stringsLookedUp)
            {
                return;
            }

            m_stringsLookedUp = true;

            int[] keys = new int[m_values.Keys.Count];
            m_values.Keys.CopyTo(keys, 0);

            foreach (var key in keys)
            {
                var value = m_values[key];
                if (Provider.Replace(ref value, stringMap))
                {
                    value = Event.CamelCase(value);
                    value = Regex.Replace(value, @"[^\w\d_]", "");
                    if (value.Length == 0)
                    {
                        value = "_";
                    }

                    m_values[key] = value;
                }
            }
        }

        private SortedDictionary<int, string> m_values = new SortedDictionary<int, string>();
        private bool m_stringsLookedUp;
        #endregion
    }
}
