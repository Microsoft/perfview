using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

// Triggers are things that wait for some interesting event to condition to occur and execute a callback when that happens. 
namespace Triggers
{
    /// <summary>
    /// Things that any Trigger needs to implement.  Basically you need to be able to stop it (IDispose) 
    /// and you need to ask its status (which allows the user to see that the trigger is monitoring things
    /// properly.  
    /// 
    /// Note that Triggers will NOT die on their own, since they are MONITORS and are kept alive by the
    /// monitoring thread they start.  You can only kill them by disposing them.  
    /// </summary>
    internal abstract class Trigger : IDisposable
    {
        /// <summary>
        /// Get something useful about the state of the trigger, can return the empty string if there 
        /// is nothing useful to say.  
        /// </summary>
        public virtual string Status { get { return ""; } }
        /// <summary>
        /// Used to stop the trigger (Triggers must be disposed explicitly since the timer keeps them
        /// alive unless disposed).   
        /// </summary>
        public virtual void Dispose() { }
    }

#if !DOTNET_CORE    // perfCounters don't exist on .NET Core
    /// <summary>
    /// PerformanceCounterTrigger is a class that knows how to determine if a particular performance counter has
    /// exceeded a particular threshold.   
    /// </summary>
    internal class PerformanceCounterTrigger : Trigger
    {
        /// <summary>
        /// Creates a new PerformanceCounterTrigger based on a specification.   Basically this specification is 
        /// a condition which is either true or false at any particular time.  Once the PerformanceCounterTrigger
        /// has been created, you can call 'IsCurrentlyTrue()' to see if the condition holds.  
        /// </summary>
        /// <param name="spec">This is of the form CATEGORY:COUNTERNAME:INSTANCE OP NUM  where OP is either a 
        /// greater than or less than sign, NUM is a floating point number and CATEGORY:COUNTERNAME:INSTANCE
        /// identify the performance counter to use (same as PerfMon).  For example 
        /// 
        /// .NET CLR Memory:% Time in GC:_Global_>20  
        ///    
        /// Will trigger when the % Time in GC for the _Global_ instance (which represents all processes) is
        /// greater than 20. 
        /// 
        /// Processor:% Processor Time:_Total>90
        /// 
        /// Will trigger when the % processor time exceeds 90%.  
        /// </param>
        /// <param name="decayToZeroHours">If nonzero, the threshold will decay to 0 in this amount of time.</param>
        /// <param name="log">A place to write messages.</param>
        /// <param name="onTriggered">A delegate to call when the threshold is exceeded</param>
        public PerformanceCounterTrigger(string spec, double decayToZeroHours, TextWriter log, Action<PerformanceCounterTrigger> onTriggered)
        {
            m_spec = spec;
            m_log = log;
            m_triggered = onTriggered;
            m_startTimeUtc = DateTime.UtcNow;
            DecayToZeroHours = decayToZeroHours;
            MinSecForTrigger = 3;

            var m = Regex.Match(spec, @"^\s*(.*?):(.*?):(.*?)\s*([<>])\s*(\d+\.?\d*)\s*$");
            if (!m.Success)
            {
                throw new ApplicationException(
                    "Performance monitor specification '" + spec + "' does not match syntax CATEGORY:COUNTER:INSTANCE [<>] NUM (i.e. 0.12 or 12)");
            }

            var categoryName = m.Groups[1].Value;
            var counterName = m.Groups[2].Value;
            var instanceName = m.Groups[3].Value;
            var op = m.Groups[4].Value;
            var threashold = m.Groups[5].Value;

            IsGreaterThan = (op == ">");
            Threshold = double.Parse(threashold);
            try { m_category = new PerformanceCounterCategory(categoryName); }
            catch (Exception) { throw new ApplicationException("Could not start performance counter " + m_spec); }

            if (!m_category.CounterExists(counterName))
            {
                throw new ApplicationException("Count not find performance counter " + counterName + " in category " + categoryName);
            }

            if (categoryName.StartsWith(".NET"))                // TODO FIX NOW, remove this condition after we are confident of it.  
            {
                if (SpawnCounterIn64BitProcessIfNecessary())
                {
                    return;
                }
            }

            // If the instance does not exist, you won't discover it until we fetch the counter later.   
            m_counter = new PerformanceCounter(categoryName, counterName, instanceName);

            m_task = Task.Factory.StartNew(delegate
            {
                var desiredDirection = IsGreaterThan ? "above" : "below";
                var otherDirection = IsGreaterThan ? "below" : "above";
                while (!m_monitoringDone)
                {
                    var isTriggered = IsCurrentlyTrue();
                    if (isTriggered)
                    {
                        if (m_wasUntriggered)
                        {
                            m_log.WriteLine($"[Counter is at {CurrentValue:n1} which is {desiredDirection} the threshold for {m_count} sec.  Need {MinSecForTrigger} sec to trigger.]");
                            // Perf counters are noisy, only trigger if we get MinSecForTrigger consecutive counts that are above/below the threshold.
                            m_count++;
                            if (m_count > MinSecForTrigger)
                            {
                                m_triggered?.Invoke(this);
                            }
                        }
                        else
                        {
                            if (!m_warnedAboutUntriggered)
                            {
                                m_log.WriteLine($"[WARNING: {Status}: Counter is {desiredDirection} the trigger level initially!]");
                                m_log.WriteLine($"[WARNING: PerfView will not trigger until the counter moves from {otherDirection} the trigger level to {desiredDirection} the level.]");
                                m_warnedAboutUntriggered = true;
                            }
                            m_count = 0;
                        }
                    }
                    else
                    {
                        if (!m_wasUntriggered)
                        {
                            m_count++;
                            if (m_count > MinSecForTrigger)
                            {
                                m_count = 0;
                                m_wasUntriggered = true;
                                m_log.WriteLine($"[{Status}: Waiting for trigger of {EffectiveThreshold}, CurVal {CurrentValue:n1}]");
                            }
                        }
                        else
                        {
                            m_count = 0;
                        }
                    }
                    Thread.Sleep(1000);     // Check every second
                }
            });
        }
        /// <summary>
        /// The specification of the Performance counter trigger that was given when it was constructed.  
        /// </summary>
        public string Spec { get { return m_spec; } }
        /// <summary>
        /// The threshold number that got passed in the spec in the constructor.   This never changes over time.   
        /// </summary>
        public double Threshold { get; private set; }
        /// <summary>
        /// Returns true if the perf counter must be great than the threshold to trigger.  
        /// </summary>
        public bool IsGreaterThan { get; private set; }
        /// <summary>
        /// The value of DecayToZeroHours parameter passed to the constructor of the trigger. 
        /// </summary>
        public double DecayToZeroHours { get; set; }
        /// <summary>
        /// The amount of time in seconds that the performance counter needs to be above the threshold to be considered triggered
        /// This allows you to ignore transients.   By default the value is 3 seconds.   
        /// </summary>
        public int MinSecForTrigger { get; set; }

        /// <summary>
        /// If DecayToZeroHours is set, the threshold changes over time.  This property returns the value after
        /// being adjusted by DecayToZeroHours. 
        /// </summary>
        public double EffectiveThreshold
        {
            get
            {
                var threshold = Threshold;
                if (DecayToZeroHours != 0)
                {
                    threshold = threshold * (1 - (DateTime.UtcNow - m_startTimeUtc).TotalHours / DecayToZeroHours);
                }

                return threshold;
            }
        }
        /// <summary>
        /// This is the value of the performance counter since the last tie 'Update()' was called.  
        /// </summary>
        public double CurrentValue { get; private set; }

        public override void Dispose()
        {
            m_monitoringDone = true;

#if PERFVIEW
            var cmd = m_cmd;
            if (cmd != null)
            {
                cmd.Kill();
            }
#endif
        }
        public override string Status
        {
            get
            {
                var exception = m_task.Exception;
                if (exception != null)
                {
                    return string.Format("Error: Exception thrown during monitoring: {0}", exception.InnerException.Message);
                }

                if (m_counter == null)
                {
                    return "";
                }

                var instanceExists = "";
                if (!m_instanceExists)
                {
                    instanceExists = " " + m_counter.InstanceName + " does not exist";
                }

                return string.Format("{0}:{1}:{2}={3:n1}{4}",
                    m_counter.CategoryName, m_counter.CounterName, m_counter.InstanceName, CurrentValue, instanceExists);
            }
        }
        #region private

        /// <summary>
        /// If you are in a 32 bit process you don't see 64 bit perf counters.  Returns true if we needed to do this.  
        /// </summary>
        private bool SpawnCounterIn64BitProcessIfNecessary()
        {
#if PERFVIEW   // TODO FIX NOW turn this on and test.
            // Do we have to do this? 
            if (m_triggered == null)
            {
                return false;
            }

            if (!(Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess))
            {
                return false;
            }

            m_task = Task.Factory.StartNew(delegate
            {
                m_log.WriteLine("To allow 64 bit processes to participate in the perf counters, we launch a 64 bit process to do the monitoring");
                string heapDumpExe = Path.Combine(Utilities.SupportFiles.SupportFileDir, @"AMD64\HeapDump.exe");
                string commandLine = heapDumpExe + " \"/StopOnPerfCounter:" + m_spec + "\"";
                m_log.WriteLine("Exec: {0}", commandLine);
                var options = new Utilities.CommandOptions().AddNoThrow().AddTimeout(Utilities.CommandOptions.Infinite).AddOutputStream(m_log);
                m_cmd = Utilities.Command.Run(commandLine, options);
                if (m_cmd.ExitCode != 0)
                {
                    m_log.WriteLine("Error: heapdump failed with error code {0}", m_cmd.ExitCode);
                }
                else
                {
                    m_triggered?.Invoke(this);
                }
                m_cmd = null;
            });
            return true;
#else
            return false;
#endif
        }

        private bool IsCurrentlyTrue()
        {
            Update();

            if (IsGreaterThan)
            {
                return CurrentValue > EffectiveThreshold;
            }
            else
            {
                return CurrentValue < EffectiveThreshold;
            }
        }
        /// <summary>
        /// Update 'CurrentValue' to the live value of the performance counter. 
        /// </summary>
        private double Update()
        {
            CurrentValue = 0;
            m_instanceExists = m_category.InstanceExists(m_counter.InstanceName);
            if (m_instanceExists || m_counter.InstanceName.Length == 0)
            {
                // There is a race here where the instance dies, so we need to protect against this and ignore any failures
                // because the instance does not exist.   
                try
                {
                    CurrentValue = m_counter.NextValue();
                    m_instanceExists = true;
                }
                catch (InvalidOperationException e)
                {
                    // ignore any 'does not exist exceptions
                    if (!e.Message.Contains("does not exist") || m_counter.InstanceName.Length == 0)
                    {
                        throw;
                    }
                }
            }
            return CurrentValue;
        }

        private string m_spec;
        private PerformanceCounterCategory m_category;
        private PerformanceCounter m_counter;
        private bool m_instanceExists;
        private TextWriter m_log;
        public event Action<PerformanceCounterTrigger> m_triggered;
#if PERFVIEW
        private Utilities.Command m_cmd;
#endif

        private Task m_task;
        private volatile bool m_monitoringDone;
        // Perf Counters can be noisy, be default we require 3 consecutive samples that succeed.  
        // This variable keeps track of this. 
        private int m_count;
        private bool m_wasUntriggered;  // We only trigger if we go from untriggered to triggered.  
        private bool m_warnedAboutUntriggered;
        private DateTime m_startTimeUtc;
        #endregion
    }
#endif

    /// <summary>
    /// A class that triggers when 
    /// </summary>
    internal class ETWEventTrigger : Trigger
    {
        // Convenience methods.  
        /// <summary>
        /// Triggers on a .NET Exception of a particular name.  
        /// </summary>
        public static ETWEventTrigger StopOnException(string exceptionRegEx, string processFilter, TextWriter log, Action<ETWEventTrigger> onTriggered)
        {
            var ret = new ETWEventTrigger(log);
            ret.OnTriggered = onTriggered;
            ret.m_triggerName = "StopOnException " + exceptionRegEx;
            ret.ProcessFilter = processFilter;

            ret.ProviderGuid = ClrTraceEventParser.ProviderGuid;
            ret.ProviderLevel = TraceEventLevel.Informational;
            ret.ProviderKeywords = (ulong)ClrTraceEventParser.Keywords.Exception;
            ret.TriggerPredicate = delegate (TraceEvent e)
            {
                log.WriteLine("Got a CLR Exception");
                if (exceptionRegEx.Length == 0)
                {
                    return true;
                }

                var asException = (ExceptionTraceData)e;
                if (asException == null)
                {
                    return false;
                }

                if (string.IsNullOrEmpty(asException.ExceptionType) || string.IsNullOrEmpty(asException.ExceptionMessage))
                {
                    return false;
                }

                string fullMessage = asException.ExceptionType + ": " + asException.ExceptionMessage;
                log.WriteLine("Exception: {0}", fullMessage);
                log.WriteLine("Exception Pattern: {0}", exceptionRegEx);

                return Regex.IsMatch(fullMessage, exceptionRegEx);
            };
            ret.StartEvent = "Exception/Start";

            ret.Start();
            return ret;
        }
        /// <summary>
        /// Triggers if an .NET GC takes longer than triggerDurationMSec
        /// </summary>
        public static ETWEventTrigger GCTooLong(int triggerDurationMSec, double decayToZeroHours, string processFilter, TextWriter log, Action<ETWEventTrigger> onTriggered)
        {
            var ret = new ETWEventTrigger(log);
            ret.TriggerMSec = triggerDurationMSec;
            ret.DecayToZeroHours = decayToZeroHours;
            ret.OnTriggered = onTriggered;
            ret.m_triggerName = "StopOnGCOverMSec";
            ret.ProcessFilter = processFilter;
            // ret.m_verbose = true;

            ret.ProviderGuid = ClrTraceEventParser.ProviderGuid;
            ret.ProviderLevel = TraceEventLevel.Informational;
            ret.ProviderKeywords = (ulong)ClrTraceEventParser.Keywords.GC;
            ret.StartEvent = "GC/Start";

            ret.TriggerPredicate = delegate (TraceEvent data)
            {
                var asGCStart = (Microsoft.Diagnostics.Tracing.Parsers.Clr.GCStartTraceData)data;
                if (asGCStart.Type == GCType.BackgroundGC)
                {
                    log.WriteLine("Got a GC, It is a background GC");
                    return false;
                }
                return true;
            };

            log.WriteLine("WARNING: on V3.5 runtimes StopOnGCOverMSec will cause CLR events to NOT be logged to the ETL file!");
            ret.Start();
            return ret;
        }

        public static ETWEventTrigger BgcFinalPauseTooLong(int triggerDurationMSec, double decayToZeroHours, string processFilter, TextWriter log, Action<ETWEventTrigger> onTriggered)
        {
            var ret = new ETWEventTrigger(log);
            ret.TriggerMSec = triggerDurationMSec;
            ret.DecayToZeroHours = decayToZeroHours;
            ret.OnTriggered = onTriggered;
            ret.m_triggerName = "StopOnBGCFinalPauseOverMsec";
            ret.ProcessFilter = processFilter;
            ret.StartStopID = "ThreadID";

            ret.ProviderGuid = ClrTraceEventParser.ProviderGuid;
            ret.ProviderLevel = TraceEventLevel.Informational;
            ret.ProviderKeywords = (ulong)ClrTraceEventParser.Keywords.GC;
            ret.StartEvent = "GC/SuspendEEStart";
            ret.StopEvent = "GC/RestartEEStop";

            ret.TriggerPredicate = delegate (TraceEvent data)
            {
                var suspendEETraceData = (Microsoft.Diagnostics.Tracing.Parsers.Clr.GCSuspendEETraceData)data;
                return suspendEETraceData.Reason == GCSuspendEEReason.SuspendForGCPrep;
            };

            ret.Start();
            return ret;
        }

        /// <summary>
        /// Stops on a Gen 2 GC.  
        /// </summary>
        public static ETWEventTrigger StopOnGen2GC(string processFilter, TextWriter log, Action<ETWEventTrigger> onTriggered)
        {
            var ret = new ETWEventTrigger(log);
            ret.OnTriggered = onTriggered;
            ret.m_triggerName = "StopOnGen2GC";
            ret.ProcessFilter = processFilter;

            ret.ProviderGuid = ClrTraceEventParser.ProviderGuid;
            ret.ProviderLevel = TraceEventLevel.Informational;
            ret.ProviderKeywords = (ulong)ClrTraceEventParser.Keywords.GC;
            ret.StartEvent = "GC/Start";

            ret.TriggerPredicate = delegate (TraceEvent data)
            {
                var asGCStart = (Microsoft.Diagnostics.Tracing.Parsers.Clr.GCStartTraceData)data;
                if (asGCStart.Depth < 2)
                {
                    log.WriteLine("Got a GC, not a Gen2");
                    return false;
                }
                if (asGCStart.Type == GCType.BackgroundGC)
                {
                    log.WriteLine("Got a GC, It is s a background GC");
                    return false;
                }
                return true;
            };

            ret.Start();
            return ret;
        }
        /// <summary>
        /// Triggers if AppFabric Cache service takes longer than triggerDurationMSec
        /// </summary>
        public static ETWEventTrigger AppFabricTooLong(int triggerDurationMSec, double decayToZeroHours, string processFilter, TextWriter log, Action<ETWEventTrigger> onTriggered)
        {
            var ret = new ETWEventTrigger(log);
            ret.TriggerMSec = triggerDurationMSec;
            ret.DecayToZeroHours = decayToZeroHours;
            ret.OnTriggered = onTriggered;
            ret.m_triggerName = "StopOnAppFabricOverMSec";
            ret.ProcessFilter = processFilter;

            ret.ProviderGuid = new Guid("A77DCF21-545F-4191-B3D0-C396CF2683F2");
            ret.ProviderLevel = TraceEventLevel.Verbose;
            ret.ProviderKeywords = ulong.MaxValue;
            ret.StartEvent = "EventID(123)";
            ret.StopEvent = "EventID(127)";
            ret.StartStopID = "1";      // TODO FIX NOW: Don't know what the first arg is. 
            ret.Start();
            return ret;
        }

        /// <summary>
        /// Create a new trigger that uses a specification to indicate what ETW events to trigger on.   
        /// 
        /// spec Syntax
        ///     PROVIDER/TASK/OPCODE;NAME1=VALUE1;NAME2=VALUE2 ...      // Opcode is optional and defaults to Info
        ///     PROVIDER/EVENTNAME;NAME1=VALUE1;NAME2=VALUE2 ...        
        /// 
        /// TASK can be Task(NNN)
        /// OPCODE can be Opcode(NNN)
        /// EVENTNAME can be EventID(NNN)
        /// 
        /// Names can begin with @ which mean they are reserved for the implementation.  Defined ones are
        ///      Keywords=XXXX                             // In hex, default ulong.MaxValue.  
        ///      Level=XXXX                                // 1 (Critical) - 5 (Verbose) Default = 4 (Info)
        ///      Process=ProcessNameOrID                   // restricts to a particular process
        ///      FieldFilter=FieldName Op Value            // Allows you to filter on a particular field value OP can be &lt; &gt; = and ~ (which means RegEx match)
        ///                                                // You can repeat this and you get the logical AND operator (sorry no OR operator right now). 
        ///      BufferSizeMB=NNN                          // Size of buffer used for trigger session.  
        /// If TriggerMSec is non-zero then it measures the duration of a start-stop pair.   
        ///      TriggerMSec=NNN                           // Number of milliseconds to trigger on.  
        ///      DecayToZeroHours=NNN                      // Trigger decays to 0 in this amount of time. 
        ///      StopEvent=PROVIDER/TASK/OPOCDE            // Default is stop if event is a start, otherwise it is 1 opcode larger (unless 0 in which case 1 event ID larger). 
        ///      StartStopID=XXXX                          // Indicates the payload field name that is used as the correlation ID to pair up start-stop pairs
        ///                                                 // can be 'ThreadID' or ActivityID if those are to be used.  
        /// </summary>
        public ETWEventTrigger(string spec, TextWriter log, Action<ETWEventTrigger> onTriggered)
            : this(log)
        {
            OnTriggered = onTriggered;
            ParseSpec(spec);
            Start();
        }

        /// <summary>
        /// Actually starts listening for ETW events.  Stops when 'Dispose()' is called.  
        /// </summary>
        public void Start()
        {
            bool listening = false;
            m_requestCount = 0;
            m_requestMaxMSec = 0;
            m_requestTotalMSec = 0;
            m_sessionEventCount = 0;
            string sessionName = SessionNamePrefix + "_" + Process.GetCurrentProcess().Id.ToString() + "_" + Interlocked.Increment(ref s_maxSessionName).ToString();

            m_readerTask = Task.Factory.StartNew(delegate
            {
                using (m_session = new TraceEventSession(sessionName, null))
                {
                    if (ProcessFilter != null)
                    {
                        if (int.TryParse(ProcessFilter, out m_processID))
                        {
                            m_log.WriteLine("[Only allowing process with ID {0} to stop the trace.]", m_processID);
                        }
                        else
                        {
                            m_processID = WaitingForProcessID; // WaitingForProcessID is an illegal process ID
                            // monitor kernel process events to late bind the process name
                            if (ProviderGuid != KernelTraceEventParser.ProviderGuid)
                            {
                                m_session.EnableKernelProvider(KernelTraceEventParser.Keywords.Process);
                            }
                            m_log.WriteLine("[Only allowing process with Name {0} to stop the trace.]", ProcessFilter);
                        }
                    }

                    if (BufferSizeMB != 0)
                    {
                        m_session.BufferSizeMB = BufferSizeMB;
                    }

                    m_log.WriteLine("Additional Trigger debugging messages are logged to the ETL file as PerfView/StopTriggerDebugMessage events.");
                    using (m_source = new ETWTraceEventSource(sessionName, TraceEventSourceType.Session))
                    {
                        Dictionary<StartStopKey, StartEventData> startStopRecords = null;
                        if (TriggerMSec != 0)
                        {
                            startStopRecords = new Dictionary<StartStopKey, StartEventData>(20);
                        }

                        if (m_processID == WaitingForProcessID)
                        {
                            m_source.Kernel.ProcessStop += delegate (ProcessTraceData data)
                            {
                                if (m_processID == data.ProcessID)
                                {
                                    m_processID = WaitingForProcessID;
                                }
                            };
                            m_source.Kernel.ProcessStartGroup += delegate (ProcessTraceData data)
                            {
                                if (string.Compare(data.ProcessName, ProcessFilter, StringComparison.OrdinalIgnoreCase) == 0)
                                {
                                    m_processID = data.ProcessID;
                                    m_log.WriteLine("[Only allowing process {0} with ID {1} stop the trace.]", data.ProcessName, m_processID);
                                }
                                else
                                {
                                    if (ShouldLogVerbose)
                                    {
                                        LogVerbose(data.TimeStamp, "Process " + data.ProcessName + " does not match process filter " + ProcessFilter);
                                    }
                                }
                            };
                        }

                        Action<TraceEvent> onEvent = delegate (TraceEvent data)
                        {
                            if (m_session == null || m_source == null)
                            {
                                return;
                            }

                            m_sessionEventCount++;
                            if (m_sessionEventCount <= 3)
                            {
                                m_log.WriteLine("Got event {0} from trigger session: {1}", m_sessionEventCount, data.EventName);
                            }


                            // Do we have a process filter?
                            if (m_processID != 0)
                            {
                                if (m_processID == WaitingForProcessID)
                                {
                                    if (ShouldLogVerbose)
                                    {
                                        LogVerbose(data.TimeStamp, "Dropping event, we have not mapped " + ProcessFilter + " to a process ID yet");
                                    }

                                    return;
                                }
                                if (m_processID != data.ProcessID)
                                {
                                    if (ShouldLogVerbose)
                                    {
                                        LogVerbose(data.TimeStamp, "Dropping event because process ID  " + data.ProcessID + " != " + m_processID);
                                    }

                                    return;
                                }
                            }

                            // Are we doing the case where we are looking for a single event?  
                            if (startStopRecords == null)
                            {
                                if (m_startEvent.Matches(data))
                                {
                                    // Track that we got an event of interest. Helps to debug why a trigger may not be firing.
                                    m_requestCount++;

                                    // Check field filters
                                    if (!PassesFieldFilters(data))
                                    {
                                        return;
                                    }

                                    // Yeah we triggered for the single event case.  
                                    if (OnTriggered != null)
                                    {
                                        if (TriggerPredicate != null && !TriggerPredicate(data))
                                        {
                                            m_log.WriteLine("Trigger predicate failed, continuing to search.");
                                            return;
                                        }
                                        TriggeredMessage = string.Format("{0} triggered by event at Process {1}({2}) Thread {3} at {4:HH:mm:ss.ffffff} approximately {5:f3} Msec ago.",
                                            m_triggerName, data.ProcessName, data.ProcessID, data.ThreadID, data.TimeStamp, (DateTime.Now - data.TimeStamp).TotalMilliseconds);

                                        m_log.WriteLine("[{0}]", TriggeredMessage);
                                        PerfViewLogger.Log.EventStopTrigger(data.TimeStamp.ToUniversalTime(), data.ProcessID, data.ThreadID, data.ProcessName, data.EventName, 0);
                                        OnTriggered(this);
                                        OnTriggered = null;     // we only trigger at most once.  
                                    }
                                }
                                if (ShouldLogVerbose)
                                {
                                    // Optimization, we can have a fair number of these
                                    if (data.EventName != "GC/GenerationRange")
                                    {
                                        LogVerbose(data.TimeStamp, "Dropping event because name " + data.EventName + " != " + m_startEvent);
                                    }
                                }
                                return;     // Did not see the event we want.  
                            }

                            // If we reach this point, we are doing a start-stop over a trigger time.  
                            bool matchesStart = m_startEvent.Matches(data);
                            if (!matchesStart && (m_stopEvent == null || !m_stopEvent.Matches(data)))
                            {
                                if (ShouldLogVerbose)
                                {
                                    string tail = "";
                                    if (m_stopEvent != null)
                                    {
                                        tail = " or " + m_stopEvent.ToString();
                                    }

                                    LogVerbose(data.TimeStamp, "Dropping event because name " + data.EventName + " != " + m_startEvent + tail);
                                }
                                return;
                            }

                            // We have a start or stop

                            // Get the context ID that will correlate the start and stop time 
                            Guid contextID = GetContextIDForEvent(data);
                            var key = new StartStopKey(data.ProviderGuid, data.Task, contextID);
                            if (matchesStart)
                            {
                                // Check field filters
                                if (!PassesFieldFilters(data))
                                {
                                    return;
                                }

                                // If the user did not specify a stop event provide a default. 
                                if (m_stopEvent == null)
                                {
                                    m_stopEvent = m_startEvent.DefaultStopEvent();
                                    m_log.WriteLine("Seen Trigger Start Event {0} Defining Stop Event to be {1}",
                                        m_startEvent, m_stopEvent);
                                }

                                if (TriggerPredicate != null && !TriggerPredicate(data))
                                {
                                    m_log.WriteLine("Trigger predicate failed, continuing to search.");
                                    return;
                                }

                                if (ShouldLogVerbose)
                                {
                                    LogVerbose(data.TimeStamp, "Start Request Context: " + contextID.ToString() + " Thread " + data.ThreadID);
                                }

                                startStopRecords[key] = new StartEventData(data.TimeStampRelativeMSec);
                                return;         // Once we have logged the start, we are done. 
                            }

                            StartEventData startEventData;
                            if (!startStopRecords.TryGetValue(key, out startEventData))
                            {
                                // We don't warn on orphans if there is a trigger predicate because predicates create orphans.  
                                if (TriggerPredicate == null && ShouldLogVerbose)
                                {
                                    LogVerbose(data.TimeStamp, "Dropped Orphan Stop Request Context: " + contextID.ToString() + " ignoring");
                                }

                                return;
                            }
                            startStopRecords.Remove(key);
                            var durationMSec = data.TimeStampRelativeMSec - startEventData.StartTime;

                            // Compute aggregate stats.  
                            m_requestCount++;
                            m_requestTotalMSec += (int)durationMSec;
                            if (m_requestMaxMSec < durationMSec)
                            {
                                m_requestMaxMSec = durationMSec;
                            }

                            // See if we should trigger.  
                            var triggerMSec = EffectiveTriggerDurationMSec;
                            if (ShouldLogVerbose)
                            {
                                LogVerbose(data.TimeStamp, "Stop Request Context " + contextID.ToString() + " Thread " + data.ThreadID + " Duration " + durationMSec.ToString("f2") + " Trigger: " + triggerMSec.ToString("f1") + " MSec");
                            }

                            if (durationMSec <= triggerMSec)
                            {
                                return;
                            }

                            // Yeah we get to trigger.  
                            if (OnTriggered != null)
                            {
                                if (m_triggerName == null)
                                {
                                    m_triggerName = "Stop";
                                }

                                var triggerMessage = "";
                                if (DecayToZeroHours != 0)
                                {
                                    triggerMessage = " (orig " + TriggerMSec.ToString() + " msec)";
                                }

                                TriggeredMessage = string.Format("{0} triggered.  Duration {1:f0} > {2} Msec{3}.  Triggering by Process {4}({5}) Thread {6} at {7:HH:mm:ss.ffffff} approximately {8:f3} Msec ago.",
                                    m_triggerName, durationMSec, triggerMSec, triggerMessage, data.ProcessName, data.ProcessID, data.ThreadID,
                                    data.TimeStamp, (DateTime.Now - data.TimeStamp).TotalMilliseconds);
                                PerfViewLogger.Log.EventStopTrigger(data.TimeStamp.ToUniversalTime(), data.ProcessID, data.ThreadID, data.ProcessName, data.EventName, durationMSec);
                                OnTriggered(this);
                                m_log.WriteLine("[{0}]", TriggeredMessage);
                                OnTriggered = null;     // we only trigger at most once.  
                            }
                        };

                        // m_source.Registered.All += onEvent;
                        m_source.Kernel.All += onEvent;
                        m_source.Clr.All += onEvent;
                        m_source.Dynamic.All += onEvent;

                        m_log.WriteLine("[Enabling ETW session for monitoring requests.]");
                        m_log.WriteLine("In Trigger session {0} enabling Provider {1} ({2}) Level {3} Keywords 0x{4:x}",
                            sessionName, ProviderName, ProviderGuid, ProviderLevel, ProviderKeywords);

                        // Windows Kernel Trace has to be enabled via EnableKernelProvider
                        if (ProviderGuid == KernelTraceEventParser.ProviderGuid)
                        {
                            var kernelKeywords = KernelTraceEventParser.Keywords.Default & ~KernelTraceEventParser.Keywords.Profile;
                            if (ProviderKeywords != ulong.MaxValue)
                            {
                                kernelKeywords = (KernelTraceEventParser.Keywords)ProviderKeywords;
                            }
                            if (m_processID == WaitingForProcessID) // need to add process events to late bind ProcessFilter
                            {
                                kernelKeywords |= KernelTraceEventParser.Keywords.Process;
                            }
                            m_session.EnableKernelProvider(kernelKeywords);
                        }
                        else
                        {
                            m_session.EnableProvider(ProviderGuid, ProviderLevel, ProviderKeywords);
                        }

                        LogVerbose(DateTime.Now, "Starting Provider " + ProviderName + " GUID " + ProviderGuid);

                        listening = true;
                        m_source.Process();
                    }
                }
            });
            while (!listening)
            {
                m_readerTask.Wait(1);
            }
        }

        /// <summary>
        /// Returns true of 'data' passes any field filters we might have.  
        /// </summary>
        private unsafe bool PassesFieldFilters(TraceEvent data)
        {
            // Do we have any field filters?
            if (FieldFilters != null)
            {
                foreach (var fieldFilter in FieldFilters)
                {
                    int fieldIndex = data.PayloadIndex(fieldFilter.FieldName);
                    if (fieldIndex < 0)
                    {
                        if (ShouldLogVerbose)
                        {
                            m_log.WriteLine("Dropping event {0} could not find field {1}", data.EventName, fieldFilter.FieldName);
                        }

                        return false;
                    }
                    string payloadValue = data.PayloadString(fieldIndex);
                    if (!fieldFilter.Succeeds(payloadValue))
                    {
                        if (ShouldLogVerbose)
                        {
                            m_log.WriteLine("Dropping event {0} field filter {1} does not succeed on event value {2}",
                                data.EventName, fieldFilter, payloadValue);
                        }

                        return false;
                    }
                    m_log.WriteLine("FieldFilter {0} passes with event value {1}", fieldFilter, payloadValue);
                }
            }
            return true;
        }

        /// <summary>
        /// The ETWEventTrigger has a bunch of configuration options.  You set them and then call 'Start()' to begin
        /// waiting for the proper ETW event.  
        /// </summary>
        public ETWEventTrigger(TextWriter log)
        {
            m_log = log;
            m_startTimeUtc = DateTime.UtcNow;
            TriggeredMessage = "Not Triggered";
            ProviderLevel = TraceEventLevel.Informational;
            ProviderKeywords = ulong.MaxValue;
        }

        public string ProviderName
        {
            get
            {
                if (m_providerName == null)
                {
                    m_providerName = ProviderGuid.ToString();
                }

                return m_providerName;
            }
        }
        /// <summary>
        /// The provider to listen for
        /// </summary>
        public Guid ProviderGuid { get; set; }
        /// <summary>
        /// Only used if OpcodeName is null (assumed to be 'Stop') and this is the duration between start and stop.  
        /// </summary>
        public int TriggerMSec { get; set; }
        public ulong ProviderKeywords { get; set; }
        public TraceEventLevel ProviderLevel { get; set; }

        public string StartEvent { set { m_startEvent = new ETWEventTriggerInfo(value); } }
        public string StopEvent { set { m_stopEvent = new ETWEventTriggerInfo(value); } }
        /// <summary>
        /// This is the name of the argument that correlates start-stop pairs.  
        /// It can be ThreadID as well as ActivityID as well as payload field names, If null it uses the first argument.  
        /// </summary>
        public string StartStopID { get; set; }
        /// <summary>
        /// Called when Task/Opcode is matched to see if you really want to trigger
        /// </summary>
        public Predicate<TraceEvent> TriggerPredicate { get; set; }
        /// <summary>
        /// If non-null, this string is process ID or the name of the process (exe name without path or extension)
        /// If this is present only processes that match this filter will trigger the stop.  Note that if a 
        /// process name is given, only one process with that name (at any one time) will trigger the stop.    
        /// </summary>
        public string ProcessFilter { get; set; }
        /// <summary>
        /// The buffer size used for the session that listens for the ETW trigger.  
        /// </summary>
        public int BufferSizeMB { get; set; }
        /// <summary>
        /// If TriggerForceToZeroHours is set then the effective TriggerDurationMSec is decrease over time so
        /// that it is 0 after TriggerForceToZeroHours.  Thus if TriggerDurationMSec is 10,000 and TriggerForceToZeroHours
        /// is 24 after 6 hours the trigger will be 7,500 and after 6 hourse it is 5000.  This insures that eventually
        /// you will trigger.  
        /// </summary>
        public double DecayToZeroHours { get; set; }

        /// <summary>
        /// This is the callback when something is finally triggered.  
        /// </summary>
        private Action<ETWEventTrigger> OnTriggered { get; set; }

        /// <summary>
        /// These represent filters (they are logically AND if there is more than one) that 
        /// operatin on field values of the event.  
        /// </summary>
        private IList<EventFieldFilter> FieldFilters { get; set; }

        /// <summary>
        /// Returns the actual threshold that will trigger a stop taking TriggerForceToZeroHours in to account
        /// </summary>
        public int EffectiveTriggerDurationMSec
        {
            get
            {
                var triggerMSec = TriggerMSec;
                if (DecayToZeroHours != 0)
                {
                    triggerMSec = (int)(triggerMSec * (1 - (DateTime.UtcNow - m_startTimeUtc).TotalHours / DecayToZeroHours));
                }

                return triggerMSec;
            }
        }
        /// <summary>
        /// A detailed message about exactly what caused the triggering.   Useful to display to the user after the trigger has fired.  
        /// </summary>
        public string TriggeredMessage { get; private set; }

        public override string Status
        {
            get
            {
                var exception = m_readerTask.Exception;
                if (exception != null)
                {
                    return string.Format("Error: Exception thrown during monitoring: {0}", exception.InnerException.Message);
                }

                string extraData = "";

                var ret = string.Format("Requests: {0:n0}  AverageDuration: {1:n1} MSec  MaxDuration: {2:n1} Trigger: {3} MSec{4}",
                    m_requestCount, m_requestTotalMSec / m_requestCount, m_requestMaxMSec, EffectiveTriggerDurationMSec, extraData);
                m_requestMaxMSec = 0;
                m_requestCount = 0;
                m_requestTotalMSec = 0;
                return ret;
            }
        }
        public override void Dispose()
        {
            OnTriggered = null;
            if (m_session != null)
            {
                // It is OK to dispose twice, this will happen if the finalizer case, but that is OK
                m_session.Dispose();
                m_session = null;
            }
            if (m_source != null)
            {
                // It is OK to dispose twice, this will happen if the finalizer case, but that is OK
                m_source.StopProcessing();
                m_source = null;
            }
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// This is the name that is used for the Session to listen to ETW Trigger events.  
        /// </summary>
        public static string SessionNamePrefix = "ETWTriggerSession";

        #region private
        private static int s_maxSessionName = 0;

        ~ETWEventTrigger()
        {
            Dispose();
        }

        private void ParseSpec(string spec)
        {
            var m = Regex.Match(spec, @"^(.*?)/([^;]*);?(.*)$");
            if (!m.Success)
            {
                throw new ApplicationException("Specification for ETW Trigger did not match Provider/EventName;Key1=Value1;...");
            }

            m_providerName = m.Groups[1].Value;
            if (m_providerName.StartsWith("*"))
            {
                m_providerName = m_providerName.Substring(1);
                ProviderGuid = TraceEventProviders.GetEventSourceGuidFromName(m_providerName);
            }
            else
            {
                ProviderGuid = TraceEventProviders.GetProviderGuidByName(m_providerName);
                if (ProviderGuid == Guid.Empty)
                {
                    throw new ApplicationException("Could not find Provider with the name " + m_providerName + " did you forget the * for EventSources");
                }
            }
            var eventName = m.Groups[2].Value;
            StartEvent = eventName;

            var keyValues = m.Groups[3].Value;
            while (keyValues.Length != 0)
            {
                m = Regex.Match(keyValues, @"([\w@]+)=([^;]*);?(.*)");
                if (!m.Success)
                {
                    throw new ApplicationException("Keywords not of the form key=value.");
                }

                var key = m.Groups[1].Value;
                var value = m.Groups[2].Value;

                if (key == "Keywords")
                {
                    if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        value = value.Substring(2);
                    }

                    ProviderKeywords = ulong.Parse(value, System.Globalization.NumberStyles.HexNumber);
                }
                else if (key == "TriggerMSec")
                {
                    TriggerMSec = int.Parse(value);
                }
                else if (key == "Level")
                {
                    ProviderLevel = (TraceEventLevel)Enum.Parse(typeof(TraceEventLevel), value);
                }
                else if (key == "StartStopID")
                {
                    StartStopID = value;
                }
                else if (key == "Process")
                {
                    ProcessFilter = value;
                }
                else if (key == "DecayToZeroHours")
                {
                    DecayToZeroHours = double.Parse(value);
                }
                else if (key == "StopEvent")
                {
                    StopEvent = value;
                }
                else if (key == "Verbose")
                {
                    m_verbose = value != "false";
                }
                else if (key == "FieldFilter")
                {
                    var filterMatch = Regex.Match(value, @"^(\w+)(<|>|~|=|!=)(.*)$");
                    if (!filterMatch.Success)
                    {
                        throw new ApplicationException("Syntax error in field filter '" + value + "'");
                    }

                    var newFilter = new EventFieldFilter()
                    {
                        FieldName = filterMatch.Groups[1].Value,
                        Op = filterMatch.Groups[2].Value,
                        Value = filterMatch.Groups[3].Value
                    };

                    // Try to convert it to an integer or double value if possible.
                    if (newFilter.Op != "~")
                    {
                        long longValue;
                        if (long.TryParse((string)newFilter.Value, out longValue))
                        {
                            newFilter.Value = longValue;
                        }
                        else if (double.TryParse((string)newFilter.Value, out double doubleValue))
                        {
                            newFilter.Value = doubleValue;
                        }
                    }
                    if (FieldFilters == null)
                    {
                        FieldFilters = new List<EventFieldFilter>();
                    }

                    FieldFilters.Add(newFilter);
                }
                else
                {
                    throw new ApplicationException("Did not recognize key " + key);
                }

                keyValues = m.Groups[3].Value;
            }
        }

        private bool ShouldLogVerbose
        {
            get
            {
                if (m_verbose)
                {
                    return true;
                }

                var utcNow = DateTime.UtcNow;
                if (utcNow > m_nextTime)
                {
                    if (m_samplingOn)
                    {
                        m_nextTime = m_nextTime + new TimeSpan(0, 0, 90);       // Turn off for 90 seconds;
                        if (utcNow < m_nextTime)
                        {
                            PerfViewLogger.Log.StopTriggerDebugMessage(utcNow, "Turning off logging for 90 seconds, use @Verbose=true to avoid");
                            m_samplingOn = false;
                        }
                        else
                        {
                            m_nextTime = utcNow + new TimeSpan(0, 0, 10);
                        }
                    }
                    else
                    {
                        m_nextTime = utcNow + new TimeSpan(0, 0, 10);       // Turn on for 10 seconds;
                        PerfViewLogger.Log.StopTriggerDebugMessage(utcNow, "Turning on logging for 10 seconds");
                        m_samplingOn = true;
                    }
                }
                return m_samplingOn;
            }
        }
        private void LogVerbose(DateTime eventTime, string message)
        {
            PerfViewLogger.Log.StopTriggerDebugMessage(eventTime.ToUniversalTime(), message);
        }

        /// <summary>
        /// Given a traceEvent payload 'data', return a contextID that will be used to correlate 'Start' and 'Stop'
        /// opcode events
        /// </summary>
        private unsafe Guid GetContextIDForEvent(TraceEvent data)
        {
            // Get the value to use as the context ID.  

            // If the user explicitly defines the corelation ID then us it.  
            object value = null;
            if (StartStopID != null)
            {
                if (StartStopID == "ActivityID")
                {
                    return data.ActivityID;
                }
                else if (StartStopID == "ThreadID")
                {
                    return new Guid((int)data.ThreadID, 0, (short)data.ProcessID, 0, 0, 0, 0, 0xFF, 0xFE, 0xFD, 0x03);
                }
                else
                {
                    value = data.PayloadByName(StartStopID);
                    if (value == null)
                    {
                        m_errorCount++;
                        if (m_errorCount <= 3)
                        {
                            m_log.WriteLine("Error: could not find payload field " + StartStopID + " in event " + data.EventName + " Using ProcessID as start-stop ID");
                        }

                        return new Guid(0, 0, (short)data.ProcessID, 0, 0, 0, 0, 0xFF, 0xFE, 0xFD, 0x05);
                    }
                }
            }

            // If the activity ID is a Activity path, then we accept that.  
            var activityID = data.ActivityID;
            if (StartStopActivityComputer.IsActivityPath(activityID, data.ProcessID))
            {
                return activityID;
            }

            if (value == null)
            {
                if (0 < data.PayloadNames.Length)
                {
                    value = data.PayloadValue(0);               // By default we choose the first argument. 
                }

                // If we did not find a value, by default use the activity ID or the thread ID 
                if (value == null)
                {
                    if (activityID != Guid.Empty)
                    {
                        return activityID;
                    }
                    else
                    {
                        return new Guid((int)data.ThreadID, 0, (short)data.ProcessID, 0, 0, 0, 0, 0xFF, 0xFE, 0xFD, 0x03);
                    }
                }
            }

            // Turn the value into a GUID.  
            if (value is Guid)
            {
                return (Guid)value;
            }
            else if (value is long)
            {
                var longValue = (long)value;
                return new Guid((int)(longValue >> 32), (short)(longValue >> 16), (short)longValue, 0, 0, 0, 0, 0xFF, 0xFE, 0xFD, 0x01);
            }
            else if (value is ulong)
            {
                var longValue = (ulong)value;
                return new Guid((int)(longValue >> 32), (short)(longValue >> 16), (short)longValue, 0, 0, 0, 0, 0xFF, 0xFE, 0xFD, 0x02);
            }
            else if (value is int)
            {
                return new Guid((int)value, 0, (short)data.ProcessID, 0, 0, 0, 0, 0xFF, 0xFE, 0xFD, 0x03);
            }
            else if (value is uint)
            {
                return new Guid((int)((uint)value), 0, (short)data.ProcessID, 0, 0, 0, 0, 0xFF, 0xFE, 0xFD, 0x04);
            }
            else
            {
                // Give up and just use the hash code of the value as the context ID  
                return new Guid(value.GetHashCode(), 0, (short)data.ProcessID, 0, 0, 0, 0, 0xFF, 0xFE, 0xFD, 0x00);
            }
        }

        private struct StartEventData
        {
            public StartEventData(double StartTime) { this.StartTime = StartTime; }
            public double StartTime;
        };

        /// <summary>
        /// Used to hold the event name for the start or stop events.   
        /// </summary>
        private class ETWEventTriggerInfo
        {
            public ETWEventTriggerInfo(string eventName)
            {
                m_eventName = eventName;
                if (m_eventName.StartsWith("EventID(") && m_eventName.EndsWith(")"))
                {
                    int id;
                    if (int.TryParse(m_eventName.Substring(8, m_eventName.Length - 9), out id))
                    {
                        m_eventID = (TraceEventID)id;
                        m_resolved = true;
                    }
                }
            }

            public bool Matches(TraceEvent data)
            {
                if (m_resolved)
                {
                    return data.ID == m_eventID;
                }

                if (data.EventName == m_eventName)
                {
                    if (data.ID != TraceEventID.Illegal)
                    {
                        m_eventID = data.ID;
                        m_resolved = true;
                    }
                    return true;
                }

                return false;
            }

            /// <summary>
            /// Given an start event, get the stop event.   If the event Name ends in 'Start' we simply change it to 'Stop'
            /// Otherwise the stop event is the the next event ID after the start event.  
            /// </summary>
            /// <returns></returns>
            public ETWEventTriggerInfo DefaultStopEvent()
            {
                Debug.Assert(m_resolved);       // Must be called after we have been resolved to an event ID.  

                string stopName;
                if (m_eventName.EndsWith("Start", StringComparison.OrdinalIgnoreCase))
                {
                    stopName = m_eventName.Substring(0, m_eventName.Length - 5) + "Stop";
                }
                else
                {
                    stopName = "EventID(" + (((int)m_eventID) + 1).ToString() + ")";
                }

                return new ETWEventTriggerInfo(stopName);
            }

            public override string ToString()
            {
                return m_eventName;
            }

            private string m_eventName;
            private bool m_resolved;
            internal TraceEventID m_eventID;
        }

        private int m_errorCount;
        private bool m_verbose;
        private DateTime m_nextTime;        // Used to control sampling of verbose logging. 
        private bool m_samplingOn;
        private int m_sessionEventCount;
        private TextWriter m_log;
        private TraceEventSession m_session;
        private ETWTraceEventSource m_source;
        private Task m_readerTask;
        private string m_providerName;
        private ETWEventTriggerInfo m_startEvent;
        private ETWEventTriggerInfo m_stopEvent;
        private int m_requestCount;
        private double m_requestMaxMSec;
        private double m_requestTotalMSec;
        private DateTime m_startTimeUtc;           // When the trigger started. 
        private string m_triggerName;
        private const int WaitingForProcessID = -1;         // An illegal process ID 
        private int m_processID;                   // 0 is a wildcard, WaitingForProcessID means no matching at all. 
        #endregion
    }

    /// <summary>
    /// Describes a field filter Thus Foo > 5 means that the field Foo has to be greater than 5.   
    /// </summary>
    internal struct EventFieldFilter
    {
        public string FieldName;
        public string Op;         // Must be < > = != ~  (which means match regular expression)
        public object Value;    // String for ~

        public bool Succeeds(string fieldValue)
        {
            if (Op == "~")
            {
                return Regex.IsMatch(fieldValue, Value.ToString(), RegexOptions.IgnoreCase);         // Match as a regular expression
            }
            else
            {
                int result = int.MinValue;
                if (Value is long || Value is double)
                {
                    // if it's a number, try to parse a long first, then as a double
                    if (long.TryParse(fieldValue, System.Globalization.NumberStyles.Number, null, out long longValue))
                    {
                        var expressionValue = Convert.ToInt64(Value);
                        result = -expressionValue.CompareTo(longValue);     // negated because the x and y arguments are swapped.  
                    }
                    else if (double.TryParse(fieldValue, System.Globalization.NumberStyles.Number, null, out double doubleValue))
                    {
                        var expressionValue = Convert.ToDouble(Value);
                        result = -expressionValue.CompareTo(doubleValue);   // negated because the x and y arguments are swapped.
                    }
                }

                // If not yet set, then use string comparison. 
                if (result == int.MinValue)
                {
                    result = -Value.ToString().CompareTo(fieldValue);       // negated because the x and y arguments are swapped.  
                }

                if (Op == "<")
                {
                    return result < 0;
                }
                else if (Op == "<=")
                {
                    return result <= 0;
                }
                else if (Op == ">")
                {
                    return result > 0;
                }
                else if (Op == ">=")
                {
                    return result >= 0;
                }
                else if (Op == "=")
                {
                    return result == 0;
                }
                else if (Op == "!=")
                {
                    return result != 0;
                }
            }
            return false;
        }

        public override string ToString()
        {
            return FieldName + Op + Value;
        }
    }

#if !DOTNET_CORE // EventLog doesn't exist on .NET Core
    /// <summary>
    /// A class that will cause a callback if a particular event is written to the windows event log.  
    /// </summary>
    internal class EventLogTrigger : Trigger
    {
        /// <summary>
        /// Will cause a callback if the an event is written to the Windows Application Event Log that matches the regular expression
        /// 'spec'.   'spec' can also have the form EventLogName@RegExp
        /// </summary>
        public EventLogTrigger(string spec, TextWriter log, Action<EventLogTrigger> onTrigger)
        {
            var eventLogName = "Application";
            var m = Regex.Match(spec, @"^([\w\s]+?)@(.*)$");
            if (m.Success)
            {
                eventLogName = m.Groups[1].Value;
                spec = m.Groups[2].Value;
            }
            m_log = log;
            m_log.WriteLine("Using event log {0}", eventLogName);
            m_onTriggered = onTrigger;
            m_pat = new Regex(spec, RegexOptions.IgnoreCase);

            m_eventLog = new EventLog(eventLogName);
            m_eventLog.EntryWritten += delegate (object sender, EntryWrittenEventArgs e)
            {
                var evnt = e.Entry;
                var eventString = string.Format("{0}: Type: {1} EventId: {2} Message: {3}",
                    evnt.TimeGenerated.ToShortTimeString(), evnt.EntryType, evnt.InstanceId, evnt.Message);

                m_log.WriteLine("EVENT_LOG: {0}", eventString);
                if (m_pat.IsMatch(eventString) && m_onTriggered != null)
                {
                    m_onTriggered(this);
                }
            };
            m_eventLog.EnableRaisingEvents = true;
        }
        public override void Dispose()
        {
            if (m_eventLog != null)
            {
                m_eventLog.Dispose();
            }
        }

        #region private
        private EventLog m_eventLog;
        private TextWriter m_log;
        private Action<EventLogTrigger> m_onTriggered;
        private Regex m_pat;
        #endregion
    };
#endif 

#if !DOTNET_CORE    // perfCounters don't exist on .NET Core
    /// <summary>
    /// Used to log a particular counter to the ETL file as a PerfViewLogger event. 
    /// </summary>
    public sealed class PerformanceCounterMonitor : IDisposable
    {
        /// <summary>
        /// Started monitoring of a performance counter.  Spec is of the form CATEGORY:COUNTERNAME:INSTANCE@INTERVAL
        /// log is a TextWriter to write diagnostic information to.  
        /// </summary>
        public PerformanceCounterMonitor(string spec, TextWriter log)
        {
            var m = Regex.Match(spec, @"^\s*((.*):(.*?):(.*?))(@(\d+\.?\d*))?\s*$");
            if (!m.Success)
            {
                throw new ApplicationException(
                    "Performance monitor specification does not match syntax CATEGORY:COUNTER:INSTANCE");
            }

            m_spec = m.Groups[1].Value;
            m_log = log;

            string categoryName = m.Groups[2].Value;
            string counterName = m.Groups[3].Value;
            string instanceName = m.Groups[4].Value;
            string intervalSecStr = m.Groups[6].Value;
            double intervalSec = 2;
            if (intervalSecStr.Length > 0)
            {
                intervalSec = double.Parse(intervalSecStr);
            }

            try { m_category = new PerformanceCounterCategory(categoryName); }
            catch (Exception) { throw new ApplicationException("Could not start performance counter " + m_spec); }

            if (!m_category.CounterExists(counterName))
            {
                throw new ApplicationException("Count not find performance counter " + counterName + " in category " + categoryName);
            }

            // If the instance does not exist, this will not throw until we try to get a value.   
            m_counter = new PerformanceCounter(categoryName, counterName, instanceName);

            // Don't allow the interval to be less than .1 seconds
            if (intervalSec < .1)
            {
                log.WriteLine("Error interval {0:f2} too small, rounding up to .1 sec", intervalSec);
                intervalSec = 0.1;
            }

            log.WriteLine("Starting monitoring of performance counter {0} every {1} sec.", m_spec, intervalSec);
            m_timer = new System.Threading.Timer(TimerTick, null, 0, (int)(intervalSec * 1000));
        }
        /// <summary>
        /// Stops the monitoring.  
        /// </summary>
        public void Dispose()
        {
            m_counter.Dispose();
            m_counter = null;
            m_timer.Dispose();
            m_timer = null;
            GC.SuppressFinalize(this);
        }

        #region private
        private void TimerTick(object obj)
        {
            try
            {
                double value = m_counter.NextValue();
                PerfViewLogger.Log.PerformanceCounterUpdate(m_spec, value);
            }
            catch (InvalidOperationException e)
            {
                // ignore any 'does not exist exceptions
                if (!e.Message.Contains("does not exist") || m_counter.InstanceName.Length == 0)
                {
                    m_log.WriteLine("Error logging performance counter {0}: {1}", m_spec, e.Message);
                }
            }
        }

        private string m_spec;
        private PerformanceCounterCategory m_category;
        private PerformanceCounter m_counter;
        private System.Threading.Timer m_timer;
        private TextWriter m_log;
        #endregion
    }
#endif

    // TODO FIX NOW USE THE ONE IN TraceEvent
    #region private classes
    /// <summary>
    /// The key used to correlate start and stop events;
    /// </summary>
    internal class StartStopKey : IEquatable<StartStopKey>
    {
        public StartStopKey(Guid provider, TraceEventTask task, Guid activityID) { Provider = provider; this.task = task; ActivityId = activityID; }
        public Guid Provider;
        public Guid ActivityId;
        public TraceEventTask task;

        public override int GetHashCode()
        {
            return Provider.GetHashCode() + ActivityId.GetHashCode() + (int)task;
        }

        public bool Equals(StartStopKey other)
        {
            return other.Provider == Provider && other.ActivityId == ActivityId && other.task == task;
        }

        public override bool Equals(object obj) { throw new NotImplementedException(); }

        public override string ToString()
        {
            return "<Key Provider=\"" + Provider + "\" ActivityId=\"" + ActivityId + "\" Task=\"" + ((int)task) + ">";
        }
    }
    #endregion
}