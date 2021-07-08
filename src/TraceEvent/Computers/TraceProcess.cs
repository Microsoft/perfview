// Copyright (c) Microsoft Corporation.  All rights reserved
// This file is best viewed using outline mode (Ctrl-M Ctrl-O)
//
// This program uses code hyperlinks available as part of the HyperAddin Visual Studio plug-in.
// It is available from http://www.codeplex.com/hyperAddin 
// using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Microsoft.Diagnostics.Tracing.Analysis
{
    /// <summary>
    /// TraceProcess Extension methods
    /// </summary>
    public static class TraceProcessesExtensions
    {
        public static void NeedProcesses(this TraceEventDispatcher source)
        {
            TraceProcesses processes = source.Processes();
            if (processes == null || m_weakCurrentSource.Target != source)
            {
                processes = new TraceProcesses(null /* TraceLog */, source);
                // establish listeners
                if (m_weakCurrentSource.Target != source)
                {
                    SetupCallbacks(source);
                }

                source.UserData["Computers/Processes"] = processes;
            }

            m_weakCurrentSource.Target = source;
        }
        public static TraceProcesses Processes(this TraceEventSource source)
        {
            if (source.UserData.ContainsKey("Computers/Processes"))
            {
                return source.UserData["Computers/Processes"] as TraceProcesses;
            }
            else
            {
                return null;
            }
        }
        public static TraceProcess Process(this TraceEvent _event)
        {
            var process = _event.traceEventSource.Processes().GetOrCreateProcess(_event.ProcessID, _event.TimeStampQPC);
            if (process.StartTimeRelativeMsec == -1 || process.StartTimeRelativeMsec > _event.TimeStampRelativeMSec)
            {
                process.StartTimeRelativeMsec = _event.TimeStampRelativeMSec;
            }

            if (process.EndTimeRelativeMsec == -1 || process.EndTimeRelativeMsec < _event.TimeStampRelativeMSec)
            {
                process.EndTimeRelativeMsec = _event.TimeStampRelativeMSec;
            }

            return process;
        }

        public static void AddCallbackOnProcessStart(this TraceEventDispatcher source, Action<TraceProcess> OnProcessStart)
        {
            var processes = source.Processes();
            Debug.Assert(processes != null);
            processes.OnProcessStart += OnProcessStart;
        }

        public static void AddCallbackOnProcessStop(this TraceEventDispatcher source, Action<TraceProcess> OnProcessStop)
        {
            var processes = source.Processes();
            Debug.Assert(processes != null);
            processes.OnProcessStop += OnProcessStop;
        }

        public static void SetSampleIntervalMSec(this TraceProcess process, double sampleIntervalMSec)
        {
            if (!process.Source.UserData.ContainsKey("Computers/Processes/SampleIntervalMSec"))
            {
                process.Source.UserData.Add("Computers/Processes/SampleIntervalMSec", new Dictionary<ProcessIndex, double>());
            }

            var map = (Dictionary<ProcessIndex, double>)process.Source.UserData["Computers/Processes/SampleIntervalMSec"];
            if (!map.ContainsKey(process.ProcessIndex))
            {
                map[process.ProcessIndex] = sampleIntervalMSec;
            }
        }

        public static double SampleIntervalMSec(this TraceProcess process)
        {
            if (!process.Source.UserData.ContainsKey("Computers/Processes/SampleIntervalMSec"))
            {
                process.Source.UserData.Add("Computers/Processes/SampleIntervalMSec", new Dictionary<ProcessIndex, double>());
            }

            var map = (Dictionary<ProcessIndex, double>)process.Source.UserData["Computers/Processes/SampleIntervalMSec"];
            if (map.ContainsKey(process.ProcessIndex))
            {
                return map[process.ProcessIndex];
            }
            else
            {
                return 1; // defualt 1 ms
            }
        }

        #region private
        public static void SetupCallbacks(TraceEventDispatcher source)
        {
            //
            // Code lifted from TraceLog.cs to create the TraceProcess
            //

            // These parsers create state and we want to collect that so we put it on our 'parsers' list that we serialize.  
            var kernelParser = source.Kernel;

            // Process level events. 
            kernelParser.ProcessStartGroup += delegate (ProcessTraceData data)
            {
                // do not use .Process() to retrive the process, since you need to pass in a special flag indicating that 
                //  this is a process start event
                var process = data.traceEventSource.Processes().GetOrCreateProcess(data.ProcessID, data.TimeStampQPC, data.Opcode == TraceEventOpcode.Start);

                process.ProcessStart(data);
                // Don't filter them out (not that many, useful for finding command line)
            };

            kernelParser.ProcessEndGroup += delegate (ProcessTraceData data)
            {
                data.traceEventSource.Processes().ProcessStop(data);
                // Don't filter them out (not that many, useful for finding command line) unless a lifetime is being applied
            };
            // Thread level events
            kernelParser.ThreadStartGroup += delegate (ThreadTraceData data)
            {
                data.Process();
            };
            kernelParser.ThreadEndGroup += delegate (ThreadTraceData data)
            {
                data.Process();
            };

            kernelParser.ImageGroup += delegate (ImageLoadTraceData data)
            {
                data.Process();
            };

            // Attribute CPU samples to processes.
            kernelParser.PerfInfoSample += delegate (SampledProfileTraceData data)
            {
                if (data.ThreadID == 0)    // Don't count process 0 (idle)
                {
                    return;
                }

                var process = data.Process();
                process.CPUMSec += process.SampleIntervalMSec();
            };

            kernelParser.AddCallbackForEvents<ProcessCtrTraceData>(delegate (ProcessCtrTraceData data)
            {
                var process = data.Process();
                process.PeakVirtual = (double)data.PeakVirtualSize;
                process.PeakWorkingSet = (double)data.PeakWorkingSetSize;
            });
        }

        private static WeakReference m_weakCurrentSource = new WeakReference(null); // used to verify non-concurrent usage
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
    /// 
    /// This is a copy of the reduced code from TraceLog!TraceProcesses (removal of elements that
    /// depend on TraceLog - there is a lot of them)
    /// </summary>
    public sealed class TraceProcesses : IEnumerable<TraceProcess>
    {
        /// <summary>
        /// The log associated with this collection of processes. 
        /// </summary> 
        public TraceLog Log { get { return log; } }
        /// <summary>
        /// The count of the number of TraceProcess instances in the TraceProcesses list. 
        /// </summary>
        public int Count { get { return processesByPID.Count; } }
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
            internal set
            {
                if (processIndex != ProcessIndex.Invalid)
                {
                    processes[(int)processIndex] = value;

                    int index;
                    FindProcessAndIndex(value.ProcessID, value.startTimeQPC, out index);
                    processesByPID[index] = value;
                }
            }
        }

#if TRACE_LOG
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
                    return process;
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
                    ret = process;
            }
            return ret;
        }
#endif

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
                if (processes[i] != null)
                {
                    yield return processes[i];
                }
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
        internal TraceProcesses(TraceLog log, TraceEventDispatcher source)
        {
            this.log = log;
            this.source = source;
            processes = new GrowableArray<TraceProcess>(64);
            processesByPID = new GrowableArray<TraceProcess>(64);
        }
        internal TraceProcess GetOrCreateProcess(int processID, long timeQPC, bool isProcessStartEvent = false)
        {
            Debug.Assert(source.DataLifetimeEnabled() /* lifetime tracking enabled */ || processes.Count == processesByPID.Count);
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
#if TRACE_LOG
                        // it should be within 10msec (or it is the Process DCStart and this firstEvent was the log header (which has time offset 0
                        log.DebugWarn(timeQPC - retProcess.firstEventSeenQPC < log.QPCFreq / 100 || retProcess.firstEventSeenQPC == 0,
                            "Events occurred > 10msec before process " + processID.ToString() +
                            " start at " + log.QPCTimeToRelMSec(retProcess.firstEventSeenQPC).ToString("n3") + " msec", null);
#endif
                        return retProcess;
                    }
                }
                var processIndex = processes.Count;
                if (source.DataLifetimeEnabled())
                {
                    // a lifetime policy is being applied which means that we may have removed
                    //  processes leading to holes
                    // find an available hole - linear search (list should be short)
                    for (processIndex = 0; processIndex < processes.Count; processIndex++)
                    {
                        if (processes[processIndex] == null)
                        {
                            break;
                        }
                    }
                }
                retProcess = new TraceProcess(processID, log, (ProcessIndex)processIndex, source);
                retProcess.firstEventSeenQPC = timeQPC;
                if (processIndex < processes.Count)
                {
                    processes[processIndex] = retProcess;
                }
                else
                {
                    processes.Add(retProcess);
                }

                processesByPID.Insert(index + 1, retProcess);
                // fire event
                if (OnProcessStart != null)
                {
                    OnProcessStart(retProcess);
                }
            }
            return retProcess;
        }
        internal void ProcessStop(ProcessTraceData data)
        {
            // handle process stop and fire stop event
            var process = data.Process();
            process.ProcessStop(data);
            if (OnProcessStop != null)
            {
                OnProcessStop(process);
            }

            // if there is a lifetime policy, cleanup old processes
            if (source.DataLifetimeEnabled())
            {
                CleanupOldProcesses(data);
            }
        }
        internal void CleanupOldProcesses(ProcessTraceData data)
        {
            // check processes if they are outside of the requested lifetime and remove them
            // not relying on the process/end event since it is not always the last event
            int index = 0;
            while (index < processesByPID.Count)
            {
                var process = processesByPID[index];

                System.Diagnostics.Debug.Assert(process != null);

                // if the process has not yet received a stop event, then continue
                if (!process.ExitStatus.HasValue)
                {
                    index++;
                    continue;
                }

                // if a process has had an event within the last DataLifetime msec, then keep it
                if (process.EndTimeRelativeMsec >= (data.TimeStampRelativeMSec - source.DataLifetimeMsec))
                {
                    index++;
                }
                else
                {
                    // remove from processesByPID - this is a shift copy operation, ugh
                    processesByPID.RemoveRange(index, 1);
                    // remove this process from the stream and return this processIndex back to rotation
                    processes[(int)process.ProcessIndex] = null;

                    // do not advance index, since we should inspect the one that was shifted into this spot
                }
            }
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
        private TraceEventDispatcher source;
        internal event Action<TraceProcess> OnProcessStart;
        internal event Action<TraceProcess> OnProcessStop;

        private static readonly Func<int, TraceProcess, int> compareByProcessID = delegate (int processID, TraceProcess process)
        {
            return (processID - process.ProcessID);
        };
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            for (int i = 0; i < processes.Count; i++)
            {
                if (processes[i] != null)
                {
                    yield return processes[i];
                }
            }
        }

        #endregion
    }

    /// <summary>
    /// A step towards a refactored TraceProcess that will move down the dependcy chain from
    /// TraceLog to Source.  This is only the portion of TraceProcess that is needed for ManagedProcess
    /// to exist.  Also note, that the surface area is intended to match 100% with
    /// Microsoft.Diagnostics.Tracing.Etlx.TraceProcess.  The namespace change is intention to avoid 
    /// collision of the name and to indicate that it is moving down the depdnency chain.
    /// 
    /// This is a slightly modified copy of the code from TraceLog!TraceProcess
    /// </summary>
    public class TraceProcess
    {
        /// <summary>
        /// The OS process ID associated with the process. It is NOT unique across the whole log.  Use
        /// ProcessIndex for that. 
        /// </summary>
        public int ProcessID { get; internal set; }
        /// <summary>
        /// The index into the logical array of TraceProcesses for this process. Unlike ProcessID (which
        /// may be reused after the process dies, the process index is unique in the log. 
        /// </summary>
        public ProcessIndex ProcessIndex { get; internal set; }
        /// <summary>
        /// This is a short name for the process.  It is the image file name without the path or suffix.  
        /// </summary>
        public string Name
        {
            get
            {
                if (name == null)
                {
                    // This is GetFileNameWithoutExtension without the error checking for illegal characters
                    // name = Path.GetFileNameWithoutExtension(ImageFileName);
                    //try
                    //{
                    //    name = Path.GetFileNameWithoutExtension(ImageFileName);
                    //    return name;    
                    //}
                    //catch (Exception e) { }

                    int lastBackslashIdx = ImageFileName.LastIndexOf('\\');
                    if (lastBackslashIdx < 0)
                    {
                        lastBackslashIdx = 0;
                    }
                    else
                    {
                        lastBackslashIdx++;
                    }

                    int dotIdx = ImageFileName.LastIndexOf('.');
                    if (dotIdx < lastBackslashIdx)
                    {
                        dotIdx = ImageFileName.Length;
                    }

                    name = ImageFileName.Substring(lastBackslashIdx, dotIdx - lastBackslashIdx);
                }
                return name;
            }
            internal set
            {
                name = value;
            }
        }
        /// <summary>
        /// The command line that started the process (may be empty string if unknown)
        /// </summary>
        public string CommandLine { get; internal set; }
        /// <summary>
        /// The path name of the EXE that started the process (may be empty string if unknown)
        /// </summary>
        public string ImageFileName { get; internal set; }
        /// <summary>
        /// The time when the process started.  Returns the time the trace started if the process existed when the trace started.  
        /// </summary>
        public DateTime StartTime { get; private set; }
        /// <summary>
        /// The time when the process started.  Returns the time the trace started if the process existed when the trace started.  
        /// Returned as the number of MSec from the beginning of the trace. 
        /// </summary>
        public double StartTimeRelativeMsec { get; internal set; }
        /// <summary>
        /// The time when the process ended.  Returns the time the trace ended if the process existed when the trace ended.  
        /// Returned as a DateTime
        /// </summary>
        public DateTime EndTime { get; private set; }
        /// <summary>
        /// The time when the process ended.  Returns the time the trace ended if the process existed when the trace ended. 
        /// Returned as the number of MSec from the beginning of the trace. 
        /// </summary>
        public double EndTimeRelativeMsec { get; internal set; }
        /// <summary>
        /// The process ID of the parent process 
        /// </summary>
        public int ParentID { get; internal set; }
        /// <summary>
        /// The process that started this process.  Returns null if unknown.  
        /// </summary>
        public TraceProcess Parent { get; private set; }
        /// <summary>
        /// If the process exited, the exit status of the process.  Otherwise null. 
        /// </summary>
        public int? ExitStatus { get; internal set; }
        /// <summary>
        /// The amount of CPU time spent in this process based on the kernel CPU sampling events.   
        /// </summary>
        public double CPUMSec { get; internal set; }
        /// <summary>
        /// Returns true if the process is a 64 bit process
        /// </summary>
        public bool Is64Bit { get; internal set; }
        /// <summary>
        /// The log file associated with the process. 
        /// </summary>
        public Microsoft.Diagnostics.Tracing.Etlx.TraceLog Log { get; set; }

        /// <summary>
        /// Peak working set
        /// </summary>
        public double PeakWorkingSet { get; internal set; }
        /// <summary>
        /// Peak virtual size
        /// </summary>
        public double PeakVirtual { get; internal set; }

        /// <summary>
        /// A list of all the threads that occurred in this process.  
        /// </summary> 
        public IEnumerable<TraceThread> Threads { get; private set; }
        /// <summary>
        /// Returns the list of modules that were loaded by the process.  The modules may be managed or
        /// native, and include native modules that were loaded event before the trace started.  
        /// </summary>
        public TraceLoadedModules LoadedModules { get; private set; }
        /// <summary>
        /// Filters events to only those for a particular process. 
        /// </summary>
        public TraceEvents EventsInProcess { get; private set; }
        /// <summary>
        /// Filters events to only that occurred during the time the process was alive. 
        /// </summary>
        /// 
        public TraceEvents EventsDuringProcess { get; private set; }

        /// <summary>
        /// An XML representation of the TraceEventProcess (for debugging)
        /// </summary>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<TraceProcess ");
            sb.Append("PID=").Append(XmlUtilities.XmlQuote(ProcessID)).Append(" ");
            sb.Append("ProcessIndex=").Append(XmlUtilities.XmlQuote(ProcessIndex)).Append(" ");
            if (ParentID > 0)
            {
                sb.Append("ParentPID=").Append(XmlUtilities.XmlQuote(ParentID)).Append(" ");
            }

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
            sb.Append("ImageName=").Append(XmlUtilities.XmlQuote(ImageFileName)).Append(" ");
            if (PeakVirtual != 0)
            {
                sb.Append("PeakVirtual=").Append(XmlUtilities.XmlQuote(PeakVirtual)).Append(" ");
            }

            if (PeakWorkingSet != 0)
            {
                sb.Append("PeakWorkingSet=").Append(XmlUtilities.XmlQuote(PeakWorkingSet)).Append(" ");
            }

            sb.Append("/>");
            return sb.ToString();
        }

        #region private
        #region EventHandlersCalledFromTraceLog
        // #ProcessHandlersCalledFromTraceLog
        // 
        // called from TraceLog.CopyRawEvents
        internal void ProcessStart(ProcessTraceData data)
        {
            if (data.Opcode == TraceEventOpcode.DataCollectionStart)
            {
                startTimeQPC = 0; // this.startTimeQPC = log.sessionStartTimeQPC;
            }
            else
            {
                Debug.Assert(data.Opcode == TraceEventOpcode.Start);
                Debug.Assert(endTimeQPC == long.MaxValue); // We would create a new Process record otherwise 
                startTimeQPC = data.TimeStampQPC;
            }
            CommandLine = data.CommandLine;
            ImageFileName = data.ImageFileName;
            ParentID = data.ParentID;
        }
        internal void ProcessStop(ProcessTraceData data)
        {
            if (CommandLine.Length == 0)
            {
                CommandLine = data.CommandLine;
            }

            ImageFileName = data.ImageFileName;        // Always overwrite as we might have guessed via the image loads
            if (ParentID == 0 && data.ParentID != 0)
            {
                ParentID = data.ParentID;
            }

            if (data.Opcode != TraceEventOpcode.DataCollectionStop)
            {
                Debug.Assert(data.Opcode == TraceEventOpcode.Stop);
                // Only set the exit code if it really is a process exit (not a DCStop). 
                if (data.Opcode == TraceEventOpcode.Stop)
                {
                    ExitStatus = data.ExitStatus;
                }

                endTimeQPC = data.TimeStampQPC;
            }
        }

        #endregion
        internal TraceProcess() { }

        internal TraceProcess(int processID, ProcessIndex processIndex)
        {
            Initialize(processID, processIndex, null /* TraceEventDispatcher */);
        }

        internal TraceProcess(int processID, TraceLog log, ProcessIndex processIndex, TraceEventDispatcher source)
        {
            Initialize(processID, processIndex, source);
        }

        private void Initialize(int processID, ProcessIndex processIndex, TraceEventDispatcher source)
        {
            ProcessID = processID;
            ParentID = -1;
            ProcessIndex = processIndex;
            endTimeQPC = long.MaxValue;
            CommandLine = "";
            ImageFileName = "";
            Source = source;
            Is64Bit = false;
            LoadedModules = null;
            Log = null;
            Parent = null;
            Threads = null;
            EventsInProcess = null;
            EventsDuringProcess = null;
            StartTime = EndTime = default(DateTime);
            StartTimeRelativeMsec = EndTimeRelativeMsec = -1;
        }

        internal string name;
        internal long firstEventSeenQPC;      // Sadly there are events before process start.   This is minimum of those times.  Note that there may be events before this 
        internal long startTimeQPC;
        internal long endTimeQPC;
        internal TraceEventDispatcher Source;
        #endregion
    }

    /// <summary>
    /// Dummy stubs so Microsoft.Diagnostics.Tracing.Etlx namespace is not necessary
    /// </summary>
    public class TraceLog { }
    public class TraceThread { }
    public class TraceLoadedModules { }
    public class TraceEvents { }
}