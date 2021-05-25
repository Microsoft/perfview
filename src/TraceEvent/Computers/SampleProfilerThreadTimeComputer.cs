using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.EventPipe;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Diagnostics.Tracing.Stacks;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Microsoft.Diagnostics.Tracing
{

    /// <summary>
    /// A EventPipeThreadTimeComputer does a simple simulation of what each thread is doing to create stack events that represent 
    /// CPU, blocked time
    /// </summary>
    public class SampleProfilerThreadTimeComputer
    {
        /// <summary>
        /// Create a new ThreadTimeComputer
        /// </summary>
        public SampleProfilerThreadTimeComputer(TraceLog eventLog, SymbolReader symbolReader)
        {
            m_eventLog = eventLog;
            m_symbolReader = symbolReader;

            m_threadState = new ThreadState[eventLog.Threads.Count];

            UseTasks = true;

            GroupByStartStopActivity = true;
        }

        /// <summary>
        /// If set we compute thread time using Tasks
        /// </summary>
        public bool UseTasks;

        /// <summary>
        /// Track additional info on like EventName or so.
        /// Default to true to keep backward compatibility.
        /// </summary>
        public bool IncludeEventSourceEvents = true;

        /// <summary>
        /// Use start-stop activities as the grouping construct. 
        /// </summary>
        public bool GroupByStartStopActivity;

        /// <summary>
        /// Reduce nested application insights requests by using related activity id.
        /// </summary>
        /// <value></value>
        public bool IgnoreApplicationInsightsRequestsWithRelatedActivityId  { get; set; } = true;

        /// <summary>
        /// Generate the thread time stacks, outputting to 'stackSource'.  
        /// </summary>
        /// <param name="outputStackSource"></param>
        /// <param name="traceEvents">Optional filtered trace events.</param>
        public void GenerateThreadTimeStacks(MutableTraceEventStackSource outputStackSource, TraceEvents traceEvents = null)
        {
            m_outputStackSource = outputStackSource;
            m_sample = new StackSourceSample(outputStackSource);
            m_nodeNameInternTable = new Dictionary<double, StackSourceFrameIndex>(10);
            m_ExternalFrameIndex = outputStackSource.Interner.FrameIntern("UNMANAGED_CODE_TIME");
            m_cpuFrameIndex = outputStackSource.Interner.FrameIntern("CPU_TIME");

            TraceLogEventSource eventSource = traceEvents == null ? m_eventLog.Events.GetSource() :
                                                                     traceEvents.GetSource();

            if (GroupByStartStopActivity)
            {
                UseTasks = true;
            }

            if (UseTasks)
            {
                m_activityComputer = new ActivityComputer(eventSource, m_symbolReader);
                m_activityComputer.AwaitUnblocks += delegate (TraceActivity activity, TraceEvent data)
                {
                    var sample = m_sample;
                    sample.Metric = (float)(activity.StartTimeRelativeMSec - activity.CreationTimeRelativeMSec);
                    sample.TimeRelativeMSec = activity.CreationTimeRelativeMSec;

                    // The stack at the Unblock, is the stack at the time the task was created (when blocking started).  
                    sample.StackIndex = m_activityComputer.GetCallStackForActivity(m_outputStackSource, activity, GetTopFramesForActivityComputerCase(data, data.Thread(), true));

                    StackSourceFrameIndex awaitFrame = m_outputStackSource.Interner.FrameIntern("AWAIT_TIME");
                    sample.StackIndex = m_outputStackSource.Interner.CallStackIntern(awaitFrame, sample.StackIndex);

                    m_outputStackSource.AddSample(sample);

                    if (m_threadToStartStopActivity != null)
                    {
                        UpdateStartStopActivityOnAwaitComplete(activity, data);
                    }
                };

                // We can provide a bit of extra value (and it is useful for debugging) if we immediately log a CPU 
                // sample when we schedule or start a task.  That we we get the very instant it starts.  
                var tplProvider = new TplEtwProviderTraceEventParser(eventSource);
                tplProvider.AwaitTaskContinuationScheduledSend += OnSampledProfile;
                tplProvider.TaskScheduledSend += OnSampledProfile;
                tplProvider.TaskExecuteStart += OnSampledProfile;
                tplProvider.TaskWaitSend += OnSampledProfile;
                tplProvider.TaskWaitStop += OnTaskUnblock;  // Log the activity stack even if you don't have a stack. 
            }

            if (GroupByStartStopActivity)
            {
                m_startStopActivities = new StartStopActivityComputer(eventSource, m_activityComputer, IgnoreApplicationInsightsRequestsWithRelatedActivityId);

                // Maps thread Indexes to the start-stop activity that they are executing.  
                m_threadToStartStopActivity = new StartStopActivity[m_eventLog.Threads.Count];

                /*********  Start Unknown Async State machine for StartStop activities ******/
                // The delegates below along with the AddUnkownAsyncDurationIfNeeded have one purpose:
                // To inject UNKNOWN_ASYNC stacks when there is an active start-stop activity that is
                // 'missing' time.   It has the effect of insuring that Start-Stop tasks always have
                // a metric that is not unrealistically small.  
                m_activityComputer.Start += delegate (TraceActivity activity, TraceEvent data)
                {
                    StartStopActivity newStartStopActivityForThread = m_startStopActivities.GetCurrentStartStopActivity(activity.Thread, data);
                    UpdateThreadToWorkOnStartStopActivity(activity.Thread, newStartStopActivityForThread, data);
                };

                m_activityComputer.AfterStop += delegate (TraceActivity activity, TraceEvent data, TraceThread thread)
                {
                    StartStopActivity newStartStopActivityForThread = m_startStopActivities.GetCurrentStartStopActivity(thread, data);
                    UpdateThreadToWorkOnStartStopActivity(thread, newStartStopActivityForThread, data);
                };

                m_startStopActivities.Start += delegate (StartStopActivity startStopActivity, TraceEvent data)
                {
                    // We only care about the top-most activities since unknown async time is defined as time
                    // where a top  most activity is running but no thread (or await time) is associated with it 
                    // fast out otherwise (we just insure that we mark the thread as doing this activity)
                    if (startStopActivity.Creator != null)
                    {
                        UpdateThreadToWorkOnStartStopActivity(data.Thread(), startStopActivity, data);
                        return;
                    }

                    // Then we have a refcount of exactly one
                    Debug.Assert(m_unknownTimeStartMsec.Get((int)startStopActivity.Index) >= 0);    // There was nothing running before.  

                    m_unknownTimeStartMsec.Set((int)startStopActivity.Index, -1);       // Set it so just we are running.  
                    m_threadToStartStopActivity[(int)data.Thread().ThreadIndex] = startStopActivity;
                };

                m_startStopActivities.Stop += delegate (StartStopActivity startStopActivity, TraceEvent data)
                {
                    // We only care about the top-most activities since unknown async time is defined as time
                    // where a top  most activity is running but no thread (or await time) is associated with it 
                    // fast out otherwise   
                    if (startStopActivity.Creator != null)
                    {
                        return;
                    }

                    double unknownStartTime = m_unknownTimeStartMsec.Get((int)startStopActivity.Index);
                    if (0 < unknownStartTime)
                    {
                        AddUnkownAsyncDurationIfNeeded(startStopActivity, unknownStartTime, data);
                    }

                    // Actually emit all the async unknown events.  
                    List<StackSourceSample> samples = m_startStopActivityToAsyncUnknownSamples.Get((int)startStopActivity.Index);
                    if (samples != null)
                    {
                        foreach (var sample in samples)
                        {
                            m_outputStackSource.AddSample(sample);  // Adding Unknown ASync
                        }

                        m_startStopActivityToAsyncUnknownSamples.Set((int)startStopActivity.Index, null);
                    }

                    m_unknownTimeStartMsec.Set((int)startStopActivity.Index, 0);
                    Debug.Assert(m_threadToStartStopActivity[(int)data.Thread().ThreadIndex] == startStopActivity ||
                        m_threadToStartStopActivity[(int)data.Thread().ThreadIndex] == null);
                    m_threadToStartStopActivity[(int)data.Thread().ThreadIndex] = null;
                };
            }

            eventSource.Clr.GCAllocationTick += OnSampledProfile;
            eventSource.Clr.GCSampledObjectAllocation += OnSampledProfile;

            var eventPipeTraceEventPraser = new SampleProfilerTraceEventParser(eventSource);
            eventPipeTraceEventPraser.ThreadSample += OnSampledProfile;

            if (IncludeEventSourceEvents)
            {
                eventSource.Dynamic.All += delegate (TraceEvent data)
                {
                    // TODO decide what the correct heuristic is.  
                    // Currently I only do this for things that might be an EventSoruce (uses the name->Guid hashing)
                    // Most importantly, it excludes the high volume CLR providers.   
                    if (!TraceEventProviders.MaybeAnEventSource(data.ProviderGuid))
                    {
                        return;
                    }

                    //  We don't want most of the FrameworkEventSource events either.  
                    if (data.ProviderGuid == FrameworkEventSourceTraceEventParser.ProviderGuid)
                    {
                        if (!((TraceEventID)140 <= data.ID && data.ID <= (TraceEventID)143))    // These are the GetResponce and GetResestStream events  
                        {
                            return;
                        }
                    }

                    // We don't care about EventPipe sample profiler events.  
                    if (data.ProviderGuid == SampleProfilerTraceEventParser.ProviderGuid)
                        return;

                    // We don't care about the TPL provider.  Too many events.  
                    if (data.ProviderGuid == TplEtwProviderTraceEventParser.ProviderGuid)
                    {
                        return;
                    }

                    // We don't care about ManifestData events.  
                    if (data.ID == (TraceEventID)0xFFFE)
                    {
                        return;
                    }

                    TraceThread thread = data.Thread();
                    if (thread == null)
                    {
                        return;
                    }

                    StackSourceCallStackIndex stackIndex = GetCallStack(data, thread);

                    // Tack on additional info about the event.
                    var fieldNames = data.PayloadNames;
                    for (int i = 0; i < fieldNames.Length; i++)
                    {
                        var fieldName = fieldNames[i];
                        var value = data.PayloadString(i);
                        var fieldNodeName = "EventData: " + fieldName + "=" + value;
                        var fieldNodeIndex = m_outputStackSource.Interner.FrameIntern(fieldNodeName);
                        stackIndex = m_outputStackSource.Interner.CallStackIntern(fieldNodeIndex, stackIndex);
                    }
                    stackIndex = m_outputStackSource.Interner.CallStackIntern(m_outputStackSource.Interner.FrameIntern("EventName: " + data.ProviderName + "/" + data.EventName), stackIndex);

                    m_threadState[(int)thread.ThreadIndex].LogThreadStack(data.TimeStampRelativeMSec, stackIndex, thread, this, false);
                };
            }

            eventSource.Process();

            m_outputStackSource.DoneAddingSamples();
            m_threadState = null;
        }

        #region private
        private void UpdateStartStopActivityOnAwaitComplete(TraceActivity activity, TraceEvent data)
        {
            // If we are createing 'UNKNOWN_ASYNC nodes, make sure that AWAIT_TIME does not overlap with UNKNOWN_ASYNC time

            var startStopActivity = m_startStopActivities.GetStartStopActivityForActivity(activity);
            if (startStopActivity == null)
            {
                return;
            }

            while (startStopActivity.Creator != null)
            {
                startStopActivity = startStopActivity.Creator;
            }

            // If the await finishes before the ASYNC_UNKNOWN, simply adust the time.  
            if (0 <= m_unknownTimeStartMsec.Get((int)startStopActivity.Index))
            {
                m_unknownTimeStartMsec.Set((int)startStopActivity.Index, data.TimeStampRelativeMSec);
            }

            // It is possible that the ASYNC_UNKOWN has already completed.  In that case, remove overlapping ones
            List<StackSourceSample> async_unknownSamples = m_startStopActivityToAsyncUnknownSamples.Get((int)startStopActivity.Index);
            if (async_unknownSamples != null)
            {
                int removeStart = async_unknownSamples.Count;
                while (0 < removeStart)
                {
                    int probe = removeStart - 1;
                    var sample = async_unknownSamples[probe];
                    if (activity.CreationTimeRelativeMSec <= sample.TimeRelativeMSec + sample.Metric) // There is overlap
                    {
                        removeStart = probe;
                    }
                    else
                    {
                        break;
                    }
                }
                int removeCount = async_unknownSamples.Count - removeStart;
                if (removeCount > 0)
                {
                    async_unknownSamples.RemoveRange(removeStart, removeCount);
                }
            }
        }

        /// <summary>
        /// Updates it so that 'thread' is now working on newStartStop, which can be null which means that it is not working on any 
        /// start-stop task. 
        /// </summary>
        private void UpdateThreadToWorkOnStartStopActivity(TraceThread thread, StartStopActivity newStartStop, TraceEvent data)
        {
            // Make the new-start stop activity be the top most one.   This is all we need and is more robust in the case
            // of unusual state transitions (e.g. lost events non-nested start-stops ...).  Ref-counting is very fragile
            // after all...
            if (newStartStop != null)
            {
                while (newStartStop.Creator != null)
                {
                    newStartStop = newStartStop.Creator;
                }
            }

            StartStopActivity oldStartStop = m_threadToStartStopActivity[(int)thread.ThreadIndex];
            Debug.Assert(oldStartStop == null || oldStartStop.Creator == null);
            if (oldStartStop == newStartStop)       // No change, nothing to do, quick exit.  
            {
                return;
            }

            // Decrement the start-stop which lost its thread. 
            if (oldStartStop != null)
            {
                double unknownStartTimeMSec = m_unknownTimeStartMsec.Get((int)oldStartStop.Index);
                Debug.Assert(unknownStartTimeMSec < 0);
                if (unknownStartTimeMSec < 0)
                {
                    unknownStartTimeMSec++;     //We represent the ref count as a negative number, here we are decrementing the ref count
                    if (unknownStartTimeMSec == 0)
                    {
                        unknownStartTimeMSec = data.TimeStampRelativeMSec;      // Remember when we dropped to zero.  
                    }

                    m_unknownTimeStartMsec.Set((int)oldStartStop.Index, unknownStartTimeMSec);
                }
            }
            m_threadToStartStopActivity[(int)thread.ThreadIndex] = newStartStop;

            // Increment refcount on the new startStop activity 
            if (newStartStop != null)
            {
                double unknownStartTimeMSec = m_unknownTimeStartMsec.Get((int)newStartStop.Index);
                // If we were off before (a positive number) then log the unknown time.  
                if (0 < unknownStartTimeMSec)
                {
                    AddUnkownAsyncDurationIfNeeded(newStartStop, unknownStartTimeMSec, data);
                    unknownStartTimeMSec = 0;
                }
                --unknownStartTimeMSec;     //We represent the ref count as a negative number, here we are incrementing the ref count
                m_unknownTimeStartMsec.Set((int)newStartStop.Index, unknownStartTimeMSec);
            }
        }

        private void AddUnkownAsyncDurationIfNeeded(StartStopActivity startStopActivity, double unknownStartTimeMSec, TraceEvent data)
        {
            Debug.Assert(0 < unknownStartTimeMSec);
            Debug.Assert(unknownStartTimeMSec <= data.TimeStampRelativeMSec);

            if (startStopActivity.IsStopped)
            {
                return;
            }

            // We dont bother with times that are too small, we consider 1msec the threshold  
            double delta = data.TimeStampRelativeMSec - unknownStartTimeMSec;
            if (delta < 1)
            {
                return;
            }

            // Add a sample with the amount of unknown duration.  
            var sample = new StackSourceSample(m_outputStackSource);
            sample.Metric = (float)delta;
            sample.TimeRelativeMSec = unknownStartTimeMSec;

            StackSourceCallStackIndex stackIndex = m_startStopActivities.GetStartStopActivityStack(m_outputStackSource, startStopActivity, data.Process());
            StackSourceFrameIndex unknownAsyncFrame = m_outputStackSource.Interner.FrameIntern("UNKNOWN_ASYNC");
            stackIndex = m_outputStackSource.Interner.CallStackIntern(unknownAsyncFrame, stackIndex);
            sample.StackIndex = stackIndex;

            // We can't add the samples right now because AWAIT nodes might overlap and we have to take these back. 
            // The add the to this list so that they can be trimmed at that time if needed. 

            List<StackSourceSample> list = m_startStopActivityToAsyncUnknownSamples.Get((int)startStopActivity.Index);
            if (list == null)
            {
                list = new List<StackSourceSample>();
                m_startStopActivityToAsyncUnknownSamples.Set((int)startStopActivity.Index, list);
            }
            list.Add(sample);
        }

        /// <summary>
        /// This can actually be called with any event that has a stack.   Basically it will log a CPU sample whose
        /// size is the time between the last such call and the current one.  
        /// </summary>
        private void OnSampledProfile(TraceEvent data)
        {
            TraceThread thread = data.Thread();
            if (thread != null)
            {
                StackSourceCallStackIndex stackIndex = GetCallStack(data, thread);

                bool onCPU = (data is ClrThreadSampleTraceData) ? ((ClrThreadSampleTraceData)data).Type == ClrThreadSampleType.Managed : true;

                m_threadState[(int)thread.ThreadIndex].LogThreadStack(data.TimeStampRelativeMSec, stackIndex, thread, this, onCPU);
            }
            else
            {
                Debug.WriteLine("Warning, no thread at " + data.TimeStampRelativeMSec.ToString("f3"));
            }
        }

        // THis is for the TaskWaitEnd.  We want to have a stack event if 'data' does not have one, we lose the fact that
        // ANYTHING happened on this thread.   Thus we log the stack of the activity so that data does not need a stack.  
        private void OnTaskUnblock(TraceEvent data)
        {
            if (m_activityComputer == null)
            {
                return;
            }

            TraceThread thread = data.Thread();
            if (thread != null)
            {
                TraceActivity activity = m_activityComputer.GetCurrentActivity(thread);

                StackSourceCallStackIndex stackIndex = m_activityComputer.GetCallStackForActivity(m_outputStackSource, activity, GetTopFramesForActivityComputerCase(data, data.Thread()));
                m_threadState[(int)thread.ThreadIndex].LogThreadStack(data.TimeStampRelativeMSec, stackIndex, thread, this, onCPU: true);
            }
            else
            {
                Debug.WriteLine("Warning, no thread at " + data.TimeStampRelativeMSec.ToString("f3"));
            }
        }

        /// <summary>
        /// Get the call stack for 'data'  Note that you thread must be data.Thread().   We pass it just to save the lookup.  
        /// </summary>
        private StackSourceCallStackIndex GetCallStack(TraceEvent data, TraceThread thread)
        {
            Debug.Assert(data.Thread() == thread);

            return m_activityComputer.GetCallStack(m_outputStackSource, data, GetTopFramesForActivityComputerCase(data, thread));
        }

        /// <summary>
        /// Returns a function that figures out the top (closest to stack root) frames for an event.  Often
        /// this returns null which means 'use the normal thread-process frames'. 
        /// Normally this stack is for the current time, but if 'getAtCreationTime' is true, it will compute the
        /// stack at the time that the current activity was CREATED rather than the current time.  This works 
        /// better for await time.  
        /// </summary>
        private Func<TraceThread, StackSourceCallStackIndex> GetTopFramesForActivityComputerCase(TraceEvent data, TraceThread thread, bool getAtCreationTime = false)
        {
            Debug.Assert(m_activityComputer != null);
            return (topThread => m_startStopActivities.GetCurrentStartStopActivityStack(m_outputStackSource, thread, topThread, getAtCreationTime));
        }

        /// <summary>
        /// Represents all the information that we need to track for each thread.  
        /// </summary>
        private struct ThreadState
        {
            public void LogThreadStack(double timeRelativeMSec, StackSourceCallStackIndex stackIndex, TraceThread thread, SampleProfilerThreadTimeComputer computer, bool onCPU)
            {
                if (onCPU)
                {
                    if (ThreadUninitialized) // First event is onCPU
                    {
                        AddCPUSample(timeRelativeMSec, thread, computer);
                        LastBlockStackRelativeMSec = -1; // make ThreadRunning true
                    }
                    else if (ThreadRunning) // continue running 
                    {
                        AddCPUSample(timeRelativeMSec, thread, computer);
                    }
                    else if (ThreadBlocked) // unblocked
                    {
                        AddBlockTimeSample(timeRelativeMSec, thread, computer);
                        LastBlockStackRelativeMSec = -timeRelativeMSec;
                    }

                    LastCPUStackRelativeMSec = timeRelativeMSec;
                    LastCPUCallStack = stackIndex;
                }
                else
                {
                    if (ThreadBlocked || ThreadUninitialized) // continue blocking or assume we started blocked
                    {
                        AddBlockTimeSample(timeRelativeMSec, thread, computer);
                    }
                    else if (ThreadRunning) // blocked
                    {
                        AddCPUSample(timeRelativeMSec, thread, computer);
                    }

                    LastBlockStackRelativeMSec = timeRelativeMSec;
                    LastBlockCallStack = stackIndex;
                }
            }

            public void AddCPUSample(double timeRelativeMSec, TraceThread thread, SampleProfilerThreadTimeComputer computer)
            {
                // Log the last sample if it was present
                if (LastCPUStackRelativeMSec > 0)
                {
                    var sample = computer.m_sample;
                    sample.Metric = (float)(timeRelativeMSec - LastCPUStackRelativeMSec);
                    sample.TimeRelativeMSec = LastCPUStackRelativeMSec;

                    var nodeIndex = computer.m_cpuFrameIndex;
                    sample.StackIndex = LastCPUCallStack;

                    sample.StackIndex = computer.m_outputStackSource.Interner.CallStackIntern(nodeIndex, sample.StackIndex);
                    computer.m_outputStackSource.AddSample(sample); // CPU
                }
            }

            public void AddBlockTimeSample(double timeRelativeMSec, TraceThread thread, SampleProfilerThreadTimeComputer computer)
            {
                // Log the last sample if it was present
                if (LastBlockStackRelativeMSec > 0)
                {
                    var sample = computer.m_sample;
                    sample.Metric = (float)(timeRelativeMSec - LastBlockStackRelativeMSec);
                    sample.TimeRelativeMSec = LastBlockStackRelativeMSec;

                    var nodeIndex = computer.m_ExternalFrameIndex;       // BLOCKED_TIME
                    sample.StackIndex = LastBlockCallStack;

                    sample.StackIndex = computer.m_outputStackSource.Interner.CallStackIntern(nodeIndex, sample.StackIndex);
                    computer.m_outputStackSource.AddSample(sample);
                }
            }

            public bool ThreadDead { get { return double.IsNegativeInfinity(LastBlockStackRelativeMSec); } }
            public bool ThreadRunning { get { return LastBlockStackRelativeMSec < 0 && !ThreadDead; } }
            public bool ThreadBlocked { get { return 0 < LastBlockStackRelativeMSec; } }
            public bool ThreadUninitialized { get { return LastBlockStackRelativeMSec == 0; } }

            /* State */
            internal double LastBlockStackRelativeMSec;        // Negative means not blocked, NegativeInfinity means dead.  0 means uninitialized.  
            internal StackSourceCallStackIndex LastBlockCallStack;

            internal double LastCPUStackRelativeMSec;
            private StackSourceCallStackIndex LastCPUCallStack;
        }

        private StartStopActivityComputer m_startStopActivities;    // Tracks start-stop activities so we can add them to the top above thread in the stack.  

        // UNKNOWN_ASYNC support 
        /// <summary>
        /// Used to create UNKNOWN frames for start-stop activities.   This is indexed by StartStopActivityIndex.
        /// and for each start-stop activity indicates when unknown time starts.   However if that activity still
        /// has known activities associated with it then the number will be negative, and its value is the 
        /// ref-count of known activities (thus when it falls to 0, it we set it to the start of unknown time. 
        /// This is indexed by the TOP-MOST start-stop activity.  
        /// </summary>
        private GrowableArray<double> m_unknownTimeStartMsec;

        /// <summary>
        /// maps thread ID to the current TOP-MOST start-stop activity running on that thread.   Used to updated m_unknownTimeStartMsec 
        /// to figure out when to put in UNKNOWN_ASYNC nodes.  
        /// </summary>
        private StartStopActivity[] m_threadToStartStopActivity;

        /// <summary>
        /// Sadly, with AWAIT nodes might come into existence AFTER we would have normally identified 
        /// a region as having no thread/await working on it.  Thus you have to be able to 'undo' ASYNC_UNKONWN
        /// nodes.   We solve this by remembering all of our ASYNC_UNKNOWN nodes on a list (basically provisional)
        /// and only add them when the start-stop activity dies (when we know there can't be another AWAIT.  
        /// Note that we only care about TOP-MOST activities.  
        /// </summary>
        private GrowableArray<List<StackSourceSample>> m_startStopActivityToAsyncUnknownSamples;

        // End UNKNOWN_ASYNC support 

        private ThreadState[] m_threadState;            // This maps thread (indexes) to what we know about the thread

        private StackSourceSample m_sample;                 // Reusable scratch space
        private MutableTraceEventStackSource m_outputStackSource; // The output source we are generating. 
        private TraceLog m_eventLog;                        // The event log associated with m_stackSource.  
        private SymbolReader m_symbolReader;

        // These are boring caches of frame names which speed things up a bit.  
        private Dictionary<double, StackSourceFrameIndex> m_nodeNameInternTable;
        private StackSourceFrameIndex m_ExternalFrameIndex;
        private StackSourceFrameIndex m_cpuFrameIndex;
        private ActivityComputer m_activityComputer;                        // Used to compute stacks for Tasks 
        #endregion
    }
}
