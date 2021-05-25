using ETLStackBrowse;
using EventSources;
using Microsoft.Diagnostics.Tracing.Stacks;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CSVReader
{
    /// <summary>
    /// This class knows how to read XPERF CSV files (or compressed CSV files)
    /// </summary>
    public class CSVReader : ITraceParameters, ITraceUINotify, IStackParameters, INamedFilter, IDisposable
    {
        public CSVReader(string xperfCSVFile)
        {
            m_trace = new ETLTrace(this, this, xperfCSVFile);

            FilterText = "";
            MemoryFilters = "";
            FrameFilters = "";
            ButterflyPivot = "";
            T1 = long.MaxValue - 1000000;
            EnableProcessFilter = true;

            UseExeFrame = true;
            UsePid = true;
            UseTid = true;
            UseRootAI = true;
            UseIODuration = true;
        }

        public void Dispose()
        {
            m_trace.Close();
        }

        /// <summary>
        /// A list of event names that are have stacks associated with them in the trace.
        /// 
        /// These are storted. 
        /// </summary>
        public List<string> StackEventNames
        {
            get
            {
                if (m_stackEventNames == null)
                {
                    m_stackEventNames = new List<string>();
                    for (int i = 0; i < m_trace.StackTypes.Length; i++)
                    {
                        if (m_trace.StackTypes[i])
                        {
                            m_stackEventNames.Add(m_trace.RecordAtoms.MakeString(i));
                        }
                    }
                    m_stackEventNames.Sort();
                }
                return m_stackEventNames;
            }
        }
        public StackSource StackSamples(string eventName = "SampledProfile", double startRelativeMSec = 0, double endRelativeMSec = double.PositiveInfinity)
        {
            return new CSVStackSource(this, eventName, startRelativeMSec, endRelativeMSec);
        }

        /// <summary>
        /// The list of any event in the trace
        /// 
        /// These are storted. 
        /// </summary>
        public List<string> EventNames
        {
            get
            {
                if (m_EventNames == null)
                {
                    m_EventNames = new List<string>();
                    for (int i = 0; i < m_trace.RecordAtoms.Count; i++)
                    {
                        if (m_trace.CommonFieldIds[i].count > 0)
                        {
                            m_EventNames.Add(m_trace.RecordAtoms.MakeString(i));
                        }
                    }
                    m_EventNames.Sort();
                }
                return m_EventNames;
            }
        }
        public EventSource GetEventSource() { return new CsvEventSource(this); }

        #region private
        public INamedFilter StackFilters
        {
            get { return this; }
        }
        public INamedFilter EventFilters
        {
            get { throw new NotImplementedException(); }
        }
        public IIndexedFilter ThreadFilters
        {
            get { throw new NotImplementedException(); }
        }
        public IIndexedFilter ProcessFilters
        {
            get { throw new NotImplementedException(); }
        }
        public IStackParameters StackParameters
        {
            get
            {
                return this;
            }
        }
        public IRollupParameters RollupParameters
        {
            get { throw new NotImplementedException(); }
        }
        public IContextSwitchParameters ContextSwitchParameters
        {
            get { throw new NotImplementedException(); }
        }
        public string MemoryFilters { get; set; }
        public bool[] GetProcessFilters()
        {
            // By default allow all processes except Idle
            bool[] ret = new bool[m_trace.Processes.Count];
            for (int i = 0; i < ret.Length; i++)
            {
                if (!m_trace.Processes[i].ProcessName.StartsWith("Idle "))
                {
                    ret[i] = true;
                }
            }
            return ret;
        }
        public bool[] GetThreadFilters()
        {
            // By default look at all threads. 
            bool[] ret = new bool[m_trace.Threads.Count];
            for (int i = 0; i < ret.Length; i++)
            {
                if (!m_trace.Threads[i].ProcessName.StartsWith("Idle "))
                {
                    ret[i] = true;
                }
            }
            return ret;
        }
        public string FilterText { get; set; }
        public long T0 { get; set; }
        public long T1 { get; set; }
        public bool EnableThreadFilter { get; set; }
        public bool EnableProcessFilter { get; set; }
        public bool UnmangleBartokSymbols { get; set; }
        public bool ElideGenerics { get; set; }

        void ITraceUINotify.ClearZoomedTimes() { }
        void ITraceUINotify.ClearEventFields() { }
        void ITraceUINotify.AddEventField(string s) { }
        void ITraceUINotify.AddEventToStackEventList(string s) { }
        void ITraceUINotify.AddEventToEventList(string s) { }
        void ITraceUINotify.AddThreadToThreadList(string s) { }
        void ITraceUINotify.AddProcessToProcessList(string s) { }
        void ITraceUINotify.AddTimeToTimeList(string s) { }
        void ITraceUINotify.AddTimeToZoomedTimeList(string s) { }

        #region stack parameters
        public bool SkipThunks { get; set; }
        public bool UseExeFrame { get; set; }
        public bool UsePid { get; set; }
        public bool UseTid { get; set; }
        public bool FoldModules { get; set; }
        public bool UseRootAI { get; set; }
        public bool ShowWhen { get; set; }
        public bool UseIODuration { get; set; }
        public bool AnalyzeReservedMemory { get; set; }
        public bool IndentLess { get; set; }
        public double MinInclusive { get; set; }
        public string FrameFilters { get; set; }
        public string ButterflyPivot { get; set; }
        #endregion

        #region filterByEventType
        public bool this[int index]
        {
            get
            {
                throw new NotImplementedException();
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public bool this[string index]
        {
            get
            {
                throw new NotImplementedException();
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public void Clear()
        {
            throw new NotImplementedException();
        }

        public void SetAll()
        {
            throw new NotImplementedException();
        }

        public int Count
        {
            get { throw new NotImplementedException(); }
        }

        public bool IsValidKey(string key)
        {
            throw new NotImplementedException();
        }

        public bool[] GetFilters()
        {
            var ret = new bool[m_trace.RecordAtoms.Count];
            int idSampleProfile = m_trace.RecordAtoms.Lookup(m_stackEventType);
            if (idSampleProfile < 0)
            {
                throw new ApplicationException("Could not find StackEventType " + m_stackEventType);
            }

            ret[idSampleProfile] = true;
            return ret;
        }
        #endregion

        internal ETLTrace m_trace;
        private List<string> m_stackEventNames;
        internal string m_stackEventType;   // typically "SampledProfile", and be any of the StackEventNames

        private List<string> m_EventNames;
        #endregion
    }

    public class CSVStackSource : InternStackSource
    {
        public CSVStackSource(CSVReader reader, string eventName, double startRelativeMSec, double endRelativeMSec)
        {
            lock (reader)
            {
                reader.m_stackEventType = eventName;
                reader.T0 = (long)(startRelativeMSec * 1000);
                reader.T1 = long.MaxValue - 1000000;
                double endusec = endRelativeMSec * 1000;
                if (endusec < reader.T1)
                {
                    reader.T1 = (long)endusec;
                }

                reader.m_trace.Parameters.T0 = reader.T0;
                reader.m_trace.Parameters.T1 = reader.T1;

                var result = reader.m_trace.StackStream(delegate (ETLTrace.Frame frame, ETLTrace.TreeComputer treeComputer, long timeUsec, ulong weight)
                {
                    m_fullModulePaths = treeComputer.fullModuleNames;
                    StackSourceSample sample = new StackSourceSample(this);
                    sample.TimeRelativeMSec = timeUsec / 1000.0;
                    sample.Metric = weight;

                    if (reader.m_stackEventType == "CSwitch")
                    {
                        sample.Metric = sample.Metric / 1000.0F;
                    }

                    if (sample.Metric == 0)
                    {
                        sample.Metric = 1;
                    }

                    // Get rid of quotes.  
                    treeComputer.fullModuleNames["\"Unknown\""] = "UNKNOWN";

                    // We are traversing frames from the root (threadStart), to leaf (caller before callee).  
                    StackSourceCallStackIndex stackIndex = StackSourceCallStackIndex.Invalid;
                    bool callerFrameIsThread = false;
                    while (frame != null)
                    {
                        var fullFrameName = treeComputer.atomsNodeNames.MakeString(frame.id);
                        string moduleName = "";

                        // Parse it into module and function name
                        var frameName = fullFrameName;
                        var index = fullFrameName.IndexOf('!');
                        if (index >= 0)
                        {
                            frameName = fullFrameName.Substring(index + 1);
                            frameName = frameName.Replace(';', ',');    // They use ';' for template separators for some reason, fix it.  
                            moduleName = fullFrameName.Substring(0, index);
                            string fullModuleName;
                            if (treeComputer.fullModuleNames.TryGetValue(moduleName, out fullModuleName))
                            {
                                moduleName = fullModuleName;
                            }

                            if (moduleName.Length > 4 && moduleName[moduleName.Length - 4] == '.')
                            {
#if false // TODO decide if we want to ignore the .NI.DLL and if so do it uniformly.  
                                if (moduleName.Length > 7 && moduleName[moduleName.Length - 7] == '.' && 
                                    moduleName[moduleName.Length - 6] == 'n' &&
                                    moduleName[moduleName.Length - 5] == 'i')
                                    moduleName = moduleName.Substring(0, moduleName.Length - 7);
                                else 
#endif
                                moduleName = moduleName.Substring(0, moduleName.Length - 4);
                            }

                            // If the thread does not call into ntdll, we consider it broken
                            if (callerFrameIsThread && !moduleName.EndsWith("ntdll", StringComparison.Ordinal))
                            {
                                var brokenFrame = Interner.FrameIntern("BROKEN", Interner.ModuleIntern(""));
                                stackIndex = Interner.CallStackIntern(brokenFrame, stackIndex);
                            }
                        }
                        else
                        {
                            Match m = Regex.Match(frameName, @"^tid *\( *(\d+)\)");
                            if (m.Success)
                            {
                                frameName = "Thread (" + m.Groups[1].Value + ")";
                            }
                            else
                            {
                                m = Regex.Match(frameName, @"^(.*?)(\.exe)? *\( *(\d+)\) *$");
                                if (m.Success)
                                {
                                    frameName = "Process " + m.Groups[1].Value + " (" + m.Groups[3].Value + ")";
                                }
                            }
                        }

                        var myModuleIndex = Interner.ModuleIntern(moduleName);
                        var myFrameIndex = Interner.FrameIntern(frameName, myModuleIndex);
                        stackIndex = Interner.CallStackIntern(myFrameIndex, stackIndex);
                        callerFrameIsThread = frameName.StartsWith("tid ");
                        frame = frame.next;
                    }

                    sample.StackIndex = stackIndex;
                    AddSample(sample);
                });
                Interner.DoneInterning();
            }
        }

        #region private
        private Dictionary<string, string> m_fullModulePaths;
        #endregion
    }

    internal class CsvEventSource : EventSource
    {
        public override ICollection<string> EventNames { get { return m_reader.EventNames; } }
        public override void SetEventFilter(List<string> eventNames) { m_EventFilter = eventNames; }

        public override void ForEach(Func<EventRecord, bool> callback)
        {
            foreach (var ev in Events)
            {
                if (!callback(ev))
                {
                    break;
                }
            }
        }
        private IEnumerable<EventRecord> Events
        {
            get
            {
                lock (m_reader)
                {
                    var trace = m_reader.m_trace;
                    // Create a filter for the selected events. 
                    // The filter holds the names of the columns for the record.   
                    var filter = new string[trace.RecordAtoms.Count][];

                    foreach (string eventName in m_EventFilter)
                    {
                        var recordId = trace.RecordAtoms.Lookup(eventName);
                        filter[recordId] = GetColumnNames(recordId);
                    }

                    Dictionary<string, int> columnOrder = null;
                    ColumnSums = null;
                    if (ColumnsToDisplay != null)
                    {
                        columnOrder = new Dictionary<string, int>(ColumnsToDisplay.Count);
                        for (int i = 0; i < ColumnsToDisplay.Count; i++)
                        {
                            columnOrder.Add(ColumnsToDisplay[i], i);
                        }

                        ColumnSums = new double[ColumnsToDisplay.Count];
                    }

                    trace.Parameters.T0 = (long)(StartTimeRelativeMSec * 1000);
                    var time = (EndTimeRelativeMSec * 1000.0);
                    if (time < long.MaxValue)
                    {
                        trace.Parameters.T1 = (long)time;
                    }
                    else
                    {
                        trace.Parameters.T1 = long.MaxValue - 100000;
                    }

                    var l = trace.StandardLineReader();

                    Regex processFilter = null;
                    if (!string.IsNullOrEmpty(ProcessFilterRegex))
                    {
                        processFilter = new Regex(ProcessFilterRegex, RegexOptions.IgnoreCase);
                    }

                    Regex textFilter = null;
                    if (!string.IsNullOrEmpty(TextFilterRegex))
                    {
                        textFilter = new Regex(TextFilterRegex, RegexOptions.IgnoreCase);
                    }

                    var count = 0;
                    var bTmp = new ByteWindow();
                    foreach (ByteWindow b in l.Lines())
                    {
                        string[] colNames = filter[l.idType];
                        if (colNames == null)
                        {
                            continue;
                        }

                        if (processFilter != null)
                        {
                            if (!processFilter.IsMatch(bTmp.Assign(b, 2).Trim().ToString()))
                            {
                                continue;
                            }
                        }

                        if (textFilter != null && !textFilter.IsMatch(b.ToString()))
                        {
                            continue;
                        }

                        var ret = new CsvEventRecord(b, this, colNames, columnOrder, ColumnSums);

                        // If we have exceeded MaxRet, then mark that fact TODO inefficient as we parse all other fields too!
                        count++;
                        if (MaxRet < count)
                        {
                            ret.m_EventName = null;
                        }

                        yield return ret;
                    }
                }
            }
        }
        public override ICollection<string> AllColumnNames(List<string> eventNames)
        {
            var ret = new List<string>();
            foreach (var eventName in eventNames)
            {
                var recordId = m_reader.m_trace.RecordAtoms.Lookup(eventName);
                foreach (var name in GetColumnNames(recordId))
                {
                    if (name != null)
                    {
                        ret.Add(name);
                    }
                }
            }
            ret.Sort();
            return ret;
        }
        public override EventSource Clone()
        {
            return new CsvEventSource(m_reader);
        }

        /// <summary>
        /// Returns an array of column names for the record with record type 'recordId'. 
        /// If 'columnSpec' is non-null only those columns specified are returned (the rest are null)
        /// </summary>
        private string[] GetColumnNames(int recordId)
        {
            List<int> colNames = m_reader.m_trace.EventFields[recordId];
            var ret = new string[colNames.Count];

            for (int i = 3; i < ret.Length; i++)
            {
                ret[i] = m_reader.m_trace.FieldAtoms.MakeString(colNames[i]).Replace(" ", "");
            }

            return ret;
        }

        #region private
        internal CsvEventSource(CSVReader reader)
        {
            m_reader = reader;
            m_sb = new StringBuilder();
            MaxEventTimeRelativeMsec = double.PositiveInfinity;
        }

        private CSVReader m_reader;
        private List<string> m_EventFilter;
        internal StringBuilder m_sb;
        #endregion
    }

    internal class CsvEventRecord : EventRecord
    {
        public override string EventName { get { return m_EventName; } }
        public override double TimeStampRelatveMSec { get { return m_TimeStampRelativeMSec; } }
        public override string ProcessName { get { return m_ProcessName; } }
        public override string Rest { get { return m_Data; } set { } }
        #region private
        internal CsvEventRecord(ByteWindow window, CsvEventSource source, string[] colNames, Dictionary<string, int> columnOrder, double[] columnSums) : base(4)
        {
            m_Data = "";
            source.m_sb.Length = 0;
            // Debug.Assert(window.fieldsLen == colNames.Length);
            bool putInRest;
            int val;
            for (int fieldIdx = 3; fieldIdx < window.fieldsLen; fieldIdx++)
            {
                putInRest = true;
                string name;
                if (fieldIdx < colNames.Length)
                {
                    name = colNames[fieldIdx];
                }
                else
                {
                    name = "unknown";
                }

                string valueStr = null;
                if (columnOrder != null)
                {
                    putInRest = false;
                    if (columnOrder.TryGetValue(name, out val))
                    {
                        putInRest = false;
                        valueStr = window.Field(fieldIdx).Trim().ToString();
                        if (val < m_displayFields.Length)
                        {
                            m_displayFields[val] = valueStr;

                            // Sum the column.  
                            if (valueStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                            {
                                long value;
                                if (long.TryParse(valueStr.Substring(2), out value))
                                {
                                    columnSums[val] += value;
                                }
                            }
                            else
                            {
                                double numericVal;
                                if (double.TryParse(valueStr, out numericVal))
                                {
                                    columnSums[val] += numericVal;
                                }
                            }
                        }
                        else
                        {
                            putInRest = true;
                        }
                    }
                }
                if (putInRest)
                {
                    if (valueStr == null)
                    {
                        valueStr = window.Field(fieldIdx).Trim().ToString();
                    }

                    source.m_sb.Append(name).Append("=").Append(Quote(valueStr)).Append(' ');
                }
            }
            m_Data = source.m_sb.ToString();

            m_EventName = window.Field(0).Trim().ToString();
            m_TimeStampRelativeMSec = window.GetLong(1) / 1000.0;
            m_ProcessName = window.Field(2).Trim().ToString();
        }

        public static string Quote(string str)
        {
            if (str.IndexOf('"') < 0)
            {
                // Replace any " with \"  (and any \" with \\" and and \\" with \\\"  ...)
                str = Regex.Replace(str, "\\*\"", @"\$1");
            }
            return "\"" + str + "\"";
        }

        internal string m_EventName;
        private double m_TimeStampRelativeMSec;
        private string m_ProcessName;
        private string m_Data;
        #endregion
    }
}
