using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.AspNet;
// Copyright (c) Microsoft Corporation.  All rights reserved
// This file is best viewed using outline mode (Ctrl-M Ctrl-O)
//
// This program uses code hyperlinks available as part of the HyperAddin Visual Studio plug-in.
// It is available from http://www.codeplex.com/hyperAddin 
// using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Diagnostics.Tracing.Stacks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using Address = System.UInt64;

namespace Microsoft.Diagnostics.Tracing
{
    // TODO FIX NOW NOT DONE

    /// <summary>
    /// A ThreadTimeComputer does a simple simulation of what each thread is doing to create stack events that represent 
    /// CPU, blocked time, disk and Network activity.  
    /// </summary>
    [Obsolete("This is not obsolete but experimental, its interface is likely to change")]
    public class ThreadTimeStackComputer
    {
        /// <summary>
        /// Create a new ThreadTimeComputer
        /// </summary>
        public ThreadTimeStackComputer(TraceLog eventLog, SymbolReader symbolReader)
        {
            m_eventLog = eventLog;
            m_symbolReader = symbolReader;

            m_threadState = new ThreadState[eventLog.Threads.Count];
            m_IRPToThread = new Dictionary<Address, TraceThread>(32);

            // We assume to begin with that all processors are idle (it will fix itself shortly).  
            m_numIdleProcs = eventLog.NumberOfProcessors;
            m_threadIDUsingProc = new int[m_numIdleProcs];

            m_lastPacketForProcess = new NetworkInfo[eventLog.Processes.Count];
            for (int i = 0; i < m_lastPacketForProcess.Length; i++)
            {
                m_lastPacketForProcess[i] = new NetworkInfo();
            }

            MiniumReadiedTimeMSec = 0.5;   // We tend to only care about this if we are being starved.  
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
        /// If set we compute blocked time 
        /// </summary>
        [Obsolete("Use Thread Time instead")]
        public bool BlockedTimeOnly;
        /// <summary>
        /// If set we don't show ready thread information 
        /// </summary>
        public bool ExcludeReadyThread;
        /// <summary>
        /// If set we group by ASP.NET Request
        /// </summary>
        public bool GroupByAspNetRequest;
        /// <summary>
        /// If we spend less then this amount of time waiting for the CPU, don't bother showing it.  
        /// </summary>
        public double MiniumReadiedTimeMSec;
        /// <summary>
        /// LIke the GroupByAspNetRequest but use start-stop activities instead of ASP.NET Requests as the grouping construct. 
        /// </summary>
        public bool GroupByStartStopActivity;

        /// <summary>
        /// Reduce nested application insights requests by using related activity id.
        /// </summary>
        /// <value></value>
        public bool IgnoreApplicationInsightsRequestsWithRelatedActivityId  { get; set; } = true;

        /// <summary>
        /// Don't show AwaitTime.  For CPU only traces showing await time is misleading since
        /// blocked time will not show up.  
        /// </summary>
        public bool NoAwaitTime;

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
            m_diskFrameIndex = outputStackSource.Interner.FrameIntern("DISK_TIME");
            m_hardFaultFrameIndex = outputStackSource.Interner.FrameIntern("HARD_FAULT");
            m_blockedFrameIndex = outputStackSource.Interner.FrameIntern("BLOCKED_TIME");
            m_cpuFrameIndex = outputStackSource.Interner.FrameIntern("CPU_TIME");
            m_readyFrameIndex = outputStackSource.Interner.FrameIntern("READIED_TIME (waiting for cpu)");
            m_networkFrameIndex = outputStackSource.Interner.FrameIntern("NETWORK_TIME (probably)");

            TraceLogEventSource eventSource = traceEvents == null ? m_eventLog.Events.GetSource() :
                                                                     traceEvents.GetSource();
            if (GroupByStartStopActivity)
            {
                UseTasks = true;
            }

            if (UseTasks)
            {
                m_activityComputer = new ActivityComputer(eventSource, m_symbolReader);

                // We don't do AWAIT_TIME if we don't have blocked time (that is we are CPU ONLY) because it is confusing
                // since we have SOME blocked time but not all of it.   
                if (!NoAwaitTime)
                {
                    m_activityComputer.AwaitUnblocks += delegate (TraceActivity activity, TraceEvent data)
                    {
                        var sample = m_sample;
                        sample.Metric = activity.StartTimeRelativeMSec - activity.CreationTimeRelativeMSec;
                        sample.TimeRelativeMSec = activity.CreationTimeRelativeMSec;

                        // The stack at the Unblock, is the stack at the time the task was created (when blocking started).  
                        sample.StackIndex = m_activityComputer.GetCallStackForActivity(m_outputStackSource, activity, GetTopFramesForActivityComputerCase(data, data.Thread(), true));

                        //Trace.WriteLine(string.Format("Tpl Proc {0} Thread {1} Start {2:f3}", data.ProcessName, data.ThreadID, data.TimeStampRelativeMSec));
                        //Trace.WriteLine(string.Format("activity Proc {0} Thread {1} Start {2:f3} End {3:f3}", activity.Thread.Process.Name, activity.Thread.ThreadID,
                        //    activity.StartTimeRelativeMSec, activity.EndTimeRelativeMSec));

                        StackSourceFrameIndex awaitFrame = m_outputStackSource.Interner.FrameIntern("AWAIT_TIME");
                        sample.StackIndex = m_outputStackSource.Interner.CallStackIntern(awaitFrame, sample.StackIndex);

                        m_outputStackSource.AddSample(sample);

                        if (m_threadToStartStopActivity != null)
                        {
                            UpdateStartStopActivityOnAwaitComplete(activity, data);
                        }
                    };
                }

                // We can provide a bit of extra value (and it is useful for debugging) if we immediately log a CPU 
                // sample when we schedule or start a task.  That we we get the very instant it starts.  
                var tplProvider = new TplEtwProviderTraceEventParser(eventSource);
                tplProvider.AwaitTaskContinuationScheduledSend += OnSampledProfile;
                tplProvider.TaskScheduledSend += OnSampledProfile;
                tplProvider.TaskExecuteStart += OnSampledProfile;
                tplProvider.TaskWaitSend += OnSampledProfile;
                tplProvider.TaskWaitStop += OnTaskUnblock;  // Log the activity stack even if you don't have a stack. 
            }

            if (!ExcludeReadyThread)
            {
                eventSource.Kernel.DispatcherReadyThread += OnReadyThread;
            }

            if (!BlockedTimeOnly)
            {
                eventSource.Kernel.PerfInfoSample += OnSampledProfile;

                // WE add these too, because they are reasonably common so they can add a level of detail.  
                eventSource.Clr.GCAllocationTick += OnSampledProfile;
                eventSource.Clr.GCSampledObjectAllocation += OnSampledProfile;
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

            if (GroupByAspNetRequest)
            {
                m_aspNetRequestInfo = new Dictionary<Guid, AspNetRequestInfo>();

                var aspNet = new AspNetTraceEventParser(eventSource);
                aspNet.AspNetReqStart += delegate (AspNetStartTraceData data)
                {
                    var thread = data.Thread();
                    if (thread == null)
                    {
                        return;
                    }

                    var url = data.Method + "('" + data.Path + "', '" + data.QueryString + "')";
                    TransferAspNetRequestToThread(data.ContextId, thread.ThreadIndex, url);
                };
                aspNet.AspNetReqStop += delegate (AspNetStopTraceData data)
                {
                    var thread = data.Thread();
                    if (thread == null)
                    {
                        return;
                    }

                    TransferAspNetRequestToThread(data.ContextId, ThreadIndex.Invalid);
                };
#if false
                    aspNet.AspNetReqEndHandler += delegate(AspNetEndHandlerTraceData data)
                    {
                        var thread = data.Thread();
                        if (thread == null)
                            return;
                        m_threadState[(int)thread.ThreadIndex].LogThreadRunninAspNetRequest(Guid.Empty);
                    };
#endif
                aspNet.AspNetReqStartHandler += delegate (AspNetStartHandlerTraceData data)
                {
                    var thread = data.Thread();
                    if (thread == null)
                    {
                        return;
                    }

                    TransferAspNetRequestToThread(data.ContextId, thread.ThreadIndex);
                };
                aspNet.AspNetReqPipelineModuleEnter += delegate (AspNetPipelineModuleEnterTraceData data)
                {
                    var thread = data.Thread();
                    if (thread == null)
                    {
                        return;
                    }

                    TransferAspNetRequestToThread(data.ContextId, thread.ThreadIndex);
                };
                aspNet.AspNetReqGetAppDomainEnter += delegate (AspNetGetAppDomainEnterTraceData data)
                {
                    var thread = data.Thread();
                    if (thread == null)
                    {
                        return;
                    }

                    TransferAspNetRequestToThread(data.ContextId, thread.ThreadIndex);
                };
            }

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

                    // Avoid weird CPU TIME node with no determined call stack location.
                    if (data.CallStack() == null)
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

                    m_threadState[(int)thread.ThreadIndex].LogCPUStack(data.TimeStampRelativeMSec, stackIndex, thread, this, false);
                };
            }

            // Add my own callbacks.  
            eventSource.Kernel.ThreadCSwitch += OnThreadCSwitch;
            eventSource.Kernel.AddCallbackForEvents<DiskIOTraceData>(OnDiskIO);
            eventSource.Kernel.AddCallbackForEvents<DiskIOInitTraceData>(OnDiskIOInit);
            eventSource.Kernel.ThreadStart += OnThreadStart;
            eventSource.Kernel.ThreadStop += OnThreadEnd;
            eventSource.Kernel.TcpIpRecv += OnTcIpRecv;
            eventSource.Kernel.MemoryHardFault += OnHardFault;

            eventSource.Process();

            var endSessionRelativeMSec = m_eventLog.SessionDuration.TotalMilliseconds;
            // log a m_sample for any threads that went to sleep but did not wake up
            // At least you see the time, if not a useful stack trace.  
            // TODO do we care that we left CPU samples abandoned?   Probably not....
            for (int i = 0; i < m_threadState.Length; i++)
            {
                if (m_threadState[i].ThreadBlocked && m_traceHasCSwitches)
                {
                    var thread = m_eventLog.Threads[(ThreadIndex)i];
                    var nodeName = "LAST_BLOCK (Last blocking operation in trace)";
                    var nodeIndex = m_outputStackSource.Interner.FrameIntern(nodeName);

                    StackSourceCallStackIndex stackIndex;
                    if (GroupByAspNetRequest)
                    {
                        stackIndex = m_outputStackSource.GetCallStackForProcess(thread.Process);
                        stackIndex = m_outputStackSource.Interner.CallStackIntern(m_outputStackSource.Interner.FrameIntern("Not In Requests"), stackIndex);
                        stackIndex = m_outputStackSource.Interner.CallStackIntern(m_outputStackSource.Interner.FrameIntern(thread.VerboseThreadName), stackIndex);
                    }
                    // WE purposely avoid adding these 'orphan' event to to the activity because they are very likely part of the activity.  
                    else if (GroupByStartStopActivity)
                    {
                        stackIndex = m_startStopActivities.GetCurrentStartStopActivityStack(m_outputStackSource, thread, thread);
                    }
                    else
                    {
                        stackIndex = m_outputStackSource.GetCallStackForThread(thread);
                    }

                    stackIndex = m_outputStackSource.Interner.CallStackIntern(nodeIndex, stackIndex);
                    m_threadState[i].LogBlockingEnd(endSessionRelativeMSec, m_threadState[i].ProcessorNumberWhereBlocked, stackIndex, thread, this);
                }
            }

            m_outputStackSource.DoneAddingSamples();
            m_threadState = null;
        }

        private void Clr_GCAllocationTick(GCAllocationTickTraceData obj)
        {
            throw new NotImplementedException();
        }

        #region private
        private void UpdateStartStopActivityOnAwaitComplete(TraceActivity activity, TraceEvent data)
        {
            // If we are creating 'UNKNOWN_ASYNC nodes, make sure that AWAIT_TIME does not overlap with UNKNOWN_ASYNC time

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
            if (NoAwaitTime)
                return;

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
            sample.Metric = delta;
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

        // Callbacks for the main state machine that logs blocked or CPU usage per thread.   
        private void OnThreadCSwitch(CSwitchTraceData data)
        {
            m_traceHasCSwitches = true;
            // We are starting blocked time for some thread
            if (data.OldThreadID != 0)      // Optimization, we don't care about the idle thread.  
            {
                TraceThread oldThread = m_eventLog.Threads.GetThread(data.OldThreadID, data.TimeStampRelativeMSec);
                if (oldThread != null)
                {
                    m_threadState[(int)oldThread.ThreadIndex].LogBlockingStart(
                        data.TimeStampRelativeMSec, data.ProcessorNumber, oldThread, this);
                }
                else
                {
                    Debug.WriteLine("Warning, no thread at " + data.TimeStampRelativeMSec.ToString("f3"));
                }
            }

            // We are ending a blocked time.  
            if (data.ThreadID != 0)     // Optimization, we don't care about the idle thread. 
            {
                TraceThread newThread = data.Thread();
                if (newThread != null)
                {
                    StackSourceCallStackIndex stackIndex = GetCallStack(data, newThread);
                    m_threadState[(int)newThread.ThreadIndex].LogBlockingEnd(
                        data.TimeStampRelativeMSec, data.ProcessorNumber, stackIndex, newThread, this);
                }
                else
                {
                    Debug.WriteLine("Warning, no thread at " + data.TimeStampRelativeMSec.ToString("f3"));
                }
            }

            var proc = data.ProcessorNumber;
            if (m_threadIDUsingProc[proc] == 0)
            {
                if (data.ThreadID != 0)
                {
                    --m_numIdleProcs;
                    Debug.Assert(0 <= m_numIdleProcs);
                }
            }
            else
            {
                if (data.ThreadID == 0)
                {
                    m_numIdleProcs++;
                    Debug.Assert(m_numIdleProcs <= m_threadIDUsingProc.Length);
                }
            }
            m_threadIDUsingProc[proc] = data.ThreadID;
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
                m_threadState[(int)thread.ThreadIndex].LogCPUStack(data.TimeStampRelativeMSec, stackIndex, thread, this, data is SampledProfileTraceData);
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
                m_threadState[(int)thread.ThreadIndex].LogCPUStack(data.TimeStampRelativeMSec, stackIndex, thread, this, false);
            }
            else
            {
                Debug.WriteLine("Warning, no thread at " + data.TimeStampRelativeMSec.ToString("f3"));
            }
        }

        private void OnThreadStart(ThreadTraceData data)
        {
            TraceThread thread = data.Thread();
            if (thread != null)
            {
                Debug.Assert(m_threadState[(int)thread.ThreadIndex].ThreadUninitialized);
                Debug.Assert(m_threadState[(int)thread.ThreadIndex].LastCPUStackRelativeMSec == 0);
                // Threads start off blocked.  
                m_threadState[(int)thread.ThreadIndex].LogBlockingStart(data.TimeStampRelativeMSec, data.ProcessorNumber, thread, this);
            }
        }
        private void OnThreadEnd(ThreadTraceData data)
        {
            TraceThread thread = data.Thread();
            if (thread != null)
            {
                // Should be running.  
                // I have seen this fire because there are two thread-stops for the same thread in the trace.   
                // I have only seen this once so I am leaving this assert (it seems it does more good than harm)
                // But if it happens habitually, we should pull it.  
                Debug.Assert(m_threadState[(int)thread.ThreadIndex].ThreadRunning || m_threadState[(int)thread.ThreadIndex].ThreadUninitialized);
                m_threadState[(int)thread.ThreadIndex].BlockTimeStartRelativeMSec = double.NegativeInfinity;       // Indicate we are dead
            }
        }
        // These additional callbacks log information that is used by the main state machine to give better info on blocked time
        private void OnReadyThread(DispatcherReadyThreadTraceData data)
        {
            // For each thread, remember the thing that woke it up.  
            TraceThread awakenedThread = m_eventLog.Threads.GetThread(data.AwakenedThreadID, data.TimeStampRelativeMSec);
            if (awakenedThread != null)
            {
                m_threadState[(int)awakenedThread.ThreadIndex].LogReadyThread(data.TimeStampRelativeMSec, data.CallStackIndex());
            }
        }
        private void OnDiskIO(DiskIOTraceData data)
        {
            TraceThread thread;
            if (m_IRPToThread.TryGetValue(data.Irp, out thread))
            {
                m_IRPToThread.Remove(data.Irp);
            }
            else
            {
                thread = data.Thread();
            }

            if (thread != null)
            {
                m_threadState[(int)thread.ThreadIndex].LogDiskIO(data.TimeStampRelativeMSec, data.ElapsedTimeMSec, data.TransferSize, data.FileName);
            }
        }
        private void OnDiskIOInit(DiskIOInitTraceData data)
        {
            // Remember the thread we started a request on.  
            TraceThread thread = data.Thread();
            if (thread != null)
            {
                m_IRPToThread[data.Irp] = thread;
            }
        }
        private void OnHardFault(MemoryHardFaultTraceData data)
        {
            TraceThread thread = data.Thread();
            if (thread != null)
            {
                m_threadState[(int)thread.ThreadIndex].LogPageFault(data.TimeStampRelativeMSec, data.FileName, this);
            }
        }
        private void OnTcIpRecv(TcpIpTraceData data)
        {
            var process = data.Process();
            if (process != null)
            {
                var packet = m_lastPacketForProcess[(int)process.ProcessIndex];
                packet.TimeRelativeMSec = data.TimeStampRelativeMSec;
                packet.SourceAddress = data.saddr;
                packet.SourcePort = data.sport;
                packet.DestAddress = data.daddr;
                packet.DestPort = data.dport;
            }
        }

        /// <summary>
        /// Get the call stack for 'data'  Note that you thread must be data.Thread().   We pass it just to save the lookup.  
        /// </summary>
        private StackSourceCallStackIndex GetCallStack(TraceEvent data, TraceThread thread)
        {
            Debug.Assert(data.Thread() == thread);
            StackSourceCallStackIndex ret;

            if (m_activityComputer != null)
            {
                ret = m_activityComputer.GetCallStack(m_outputStackSource, data, GetTopFramesForActivityComputerCase(data, thread));
            }
            else
            {
                Debug.Assert(!GroupByStartStopActivity);        // Handled in above case
                if (GroupByAspNetRequest)
                {
                    var aspNetGuid = m_threadState[(int)thread.ThreadIndex].AspNetRequestGuid;
                    StackSourceCallStackIndex top = GetAspNetFromProcessFrameThroughThreadFrameStack(aspNetGuid, data, thread);
                    ret = m_outputStackSource.GetCallStack(data.CallStackIndex(), top, null);       // TODO use the cache...
                }
                else
                {
                    ret = m_outputStackSource.GetCallStack(data.CallStackIndex(), data);
                }
            }
            return ret;
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
            if (GroupByAspNetRequest)
            {
                Guid aspNetGuid = GetAspNetGuid(m_activityComputer.GetCurrentActivity(thread));
                return (topThread => GetAspNetFromProcessFrameThroughThreadFrameStack(aspNetGuid, data, topThread));
            }
            else if (GroupByStartStopActivity)
            {
                return (topThread => m_startStopActivities.GetCurrentStartStopActivityStack(m_outputStackSource, thread, topThread, getAtCreationTime));
            }

            return null;
        }

        /// <summary>
        /// Represents all the information that we need to track for each thread.  
        /// </summary>
        private struct ThreadState
        {
            public void LogCPUStack(double timeRelativeMSec, StackSourceCallStackIndex stackIndex, TraceThread thread, ThreadTimeStackComputer computer, bool isCPUSample)
            {
                // THere is a small amount of cleanup after logging thread death.  We will ignore this.  
                if (!ThreadDead)
                {
                    // If we don't have CSwitches, then for 1msec sampling starting now for that time 
                    if (!computer.m_traceHasCSwitches)
                    {
                        // If we don't have CSWITCHES only log true CPU samples
                        if (!isCPUSample)
                        {
                            return;
                        }

                        var sampleDurationMsec = computer.m_eventLog.SampleProfileInterval.Ticks / 10000.0F;
                        LastCPUStackRelativeMSec = timeRelativeMSec;
                        timeRelativeMSec += sampleDurationMsec;
                        LastCPUCallStack = stackIndex;
                    }

                    AddCPUSample(timeRelativeMSec, thread, computer);
                    LastCPUStackRelativeMSec = timeRelativeMSec;
                    LastCPUCallStack = stackIndex;
                }
            }
            public void LogBlockingStart(double timeRelativeMSec, int processorNumber, TraceThread thread, ThreadTimeStackComputer computer)
            {
                // If we are not dead.  
                if (!ThreadDead)
                {
                    Debug.Assert(!ThreadBlocked);
                    AddCPUSample(timeRelativeMSec, thread, computer);

                    ProcessorNumberWhereBlocked = (ushort)processorNumber;
                    LastCPUStackRelativeMSec = 0;                    // Indicate there is not a last CPU sample
                    LastUnblockEvent = null;
                    BlockTimeStartRelativeMSec = timeRelativeMSec;        // Indicate we are blocked.  
                }
            }
            public void LogBlockingEnd(double timeRelativeMSec, int processorNumber, StackSourceCallStackIndex stackIndex, TraceThread thread, ThreadTimeStackComputer computer)
            {
                if (ThreadBlocked)
                {
                    var sample = computer.m_sample;
                    sample.TimeRelativeMSec = BlockTimeStartRelativeMSec;
                    sample.Metric = timeRelativeMSec - BlockTimeStartRelativeMSec;
                    var morphedStackIndex = stackIndex;
                    double schedulingDelayMSec = 0;
                    if (ReadyThreadCallStack != CallStackIndex.Invalid)
                    {
                        var delay = timeRelativeMSec - ReadyThreadRelativeMSec;
                        // Debug.Assert(delay < sample.Metric);            // you can't be readied before you went to sleep!
                        if (delay > computer.MiniumReadiedTimeMSec && delay < sample.Metric)   // Don't bother showing if this time is small 
                        {
                            // Don't count the scheduling delay as part of blocked time, make a separate call stack for it.   
                            schedulingDelayMSec = delay;    // This triggers adding a sample just for the CPU scheduling delay  
                            sample.Metric -= delay;
                        }

                        // Add the ready thread stacks. 
                        morphedStackIndex = GenerateReadyThreadNodes(computer.m_outputStackSource, morphedStackIndex, ReadyThreadCallStack,
                            delay, computer.m_numIdleProcs);
                    }

                    // If the disk time is about the same size as the blocking time, then we attribute blocking to that disk I/O
                    if (DiskIOFilePath != null && DiskElapsedMSec * 0.8 - .1 < sample.Metric && sample.Metric < 1.2 * DiskElapsedMSec + .1)
                    {
                        var diskIoFrameIndex = computer.m_outputStackSource.Interner.FrameIntern("DiskFile: " + DiskIOFilePath);
                        morphedStackIndex = computer.m_outputStackSource.Interner.CallStackIntern(diskIoFrameIndex, morphedStackIndex);
                        morphedStackIndex = computer.m_outputStackSource.Interner.CallStackIntern(computer.m_diskFrameIndex, morphedStackIndex);
                    }
                    else
                    {
                        // See if we were blocked on the network
                        // TODO review this 
                        var lastPacket = computer.m_lastPacketForProcess[(int)thread.Process.ProcessIndex];
                        var delta = timeRelativeMSec - lastPacket.TimeRelativeMSec;
                        if (0 < delta && delta < 5) // TODO hack, give up after 5 msec
                        {
                            var packetStr = string.Format("NETWORK (probably) From: {0} To: {1}",
                                lastPacket.SourceAddress, lastPacket.DestAddress);
                            var diskIoFrameIndex = computer.m_outputStackSource.Interner.FrameIntern(packetStr);
                            morphedStackIndex = computer.m_outputStackSource.Interner.CallStackIntern(diskIoFrameIndex, morphedStackIndex);
                            morphedStackIndex = computer.m_outputStackSource.Interner.CallStackIntern(computer.m_networkFrameIndex, morphedStackIndex);
                            // Now that we used it, Kill this information
                            lastPacket.TimeRelativeMSec = double.NegativeInfinity;
                        }
                        else
                        {
                            if (!computer.BlockedTimeOnly)
                            {
                                morphedStackIndex = computer.m_outputStackSource.Interner.CallStackIntern(computer.m_blockedFrameIndex, morphedStackIndex);
                            }
                        }
                    }

                    // Round up to the next power of 10, intern the names.  
                    // TODO use an array instead of a dictionary.  
                    StackSourceFrameIndex nodeIndex;
                    if (computer.BlockedTimeOnly)
                    {
                        var roundToLog = Math.Pow(10.0, Math.Ceiling(Math.Log10(sample.Metric)));

                        if (!computer.m_nodeNameInternTable.TryGetValue(roundToLog, out nodeIndex))
                        {
                            var nodeName = "BLOCKED_FOR <= " + roundToLog + " msec";
                            nodeIndex = computer.m_outputStackSource.Interner.FrameIntern(nodeName);
                            computer.m_nodeNameInternTable[roundToLog] = nodeIndex;
                        }
                        morphedStackIndex = computer.m_outputStackSource.Interner.CallStackIntern(nodeIndex, morphedStackIndex);
                    }

                    sample.StackIndex = morphedStackIndex;
                    LastUnblockEvent = computer.m_outputStackSource.AddSample(sample);  // Thread unblocking

                    // Did we have a non-trivial delay trying to get the CPU?  
                    if (schedulingDelayMSec > 0)
                    {
                        // TODO FIX  NOW this does not handle the case where the thread was ready from the start (preempted).  
                        sample.Metric = schedulingDelayMSec;
                        sample.TimeRelativeMSec = ReadyThreadRelativeMSec;

                        var morphedStack = stackIndex;

                        var nodeName = "BLOCKED on CPU " + ProcessorNumberWhereBlocked +
                            " UNBLOCKED on CPU " + ProcessorNumberWhereAwakened +
                            " IDLE CPUS = " + computer.m_numIdleProcs;
                        nodeIndex = computer.m_outputStackSource.Interner.FrameIntern(nodeName);
                        morphedStack = computer.m_outputStackSource.Interner.CallStackIntern(nodeIndex, morphedStack);

                        sample.StackIndex = computer.m_outputStackSource.Interner.CallStackIntern(computer.m_readyFrameIndex, morphedStack);
                        computer.m_outputStackSource.AddSample(sample); // Thread unblocking
                    }

                    BlockTimeStartRelativeMSec = -timeRelativeMSec;               // Thread is now unblocked. 
                }
                else if (ThreadRunning)
                {
                    Debug.WriteLine("Unblocking unblocked thread at " + timeRelativeMSec.ToString("f3"));
                    Debug.Assert(false);
                }
                ProcessorNumberWhereAwakened = (ushort)processorNumber;
                LastCPUStackRelativeMSec = timeRelativeMSec;                   // Indicate the last CPU sample taken 
                LastCPUCallStack = stackIndex;

                DiskIOSize = int.MinValue;
                DiskIOFilePath = null;

                ReadyThreadCallStack = CallStackIndex.Invalid;
            }
            // These log 
            public void LogReadyThread(double timeRelativeMSec, CallStackIndex stackIndex)
            {
                ReadyThreadRelativeMSec = timeRelativeMSec;
                ReadyThreadCallStack = stackIndex;
            }
            public void LogDiskIO(double timeRelativeMSec, double elapsedMSec, int ioSize, string filePath)
            {
                DiskIOSize = ioSize;
                DiskTimeStampRelativeMSec = timeRelativeMSec;
                DiskElapsedMSec = elapsedMSec;
                DiskIOFilePath = filePath;
            }
            public void LogTcpIp(double timeRelativeMSec, int ioSize, IPAddress sourceAddr, int sourcePort, int destPort)
            {

                // TODO FILL IN 
            }
            public void LogPageFault(double timeRelativeMSec, string fileName, ThreadTimeStackComputer computer)
            {
                if (LastUnblockEvent != null)
                {
                    // Update the event to indicate that it is a page fault.  
                    var stack = LastUnblockEvent.StackIndex;
                    var frame = StackSourceFrameIndex.Invalid;
                    if (stack != StackSourceCallStackIndex.Invalid)
                    {
                        // If it is a disk or blocked node, remove it before appending the 'HardFault' node.  
                        frame = computer.m_outputStackSource.GetFrameIndex(stack);
                        if (frame == computer.m_diskFrameIndex || frame == computer.m_blockedFrameIndex)
                        {
                            stack = computer.m_outputStackSource.GetCallerIndex(stack);
                        }
                    }

                    // If this is not a disk node, and we have file information, put the file information next 
                    if (frame != computer.m_diskFrameIndex && string.IsNullOrEmpty(fileName))
                    {
                        var fileNameNode = computer.m_outputStackSource.Interner.FrameIntern("File " + fileName);
                        stack = computer.m_outputStackSource.Interner.CallStackIntern(fileNameNode, stack);
                    }

                    // Finish off with the HARD_FAULT node.  
                    LastUnblockEvent.StackIndex = computer.m_outputStackSource.Interner.CallStackIntern(computer.m_hardFaultFrameIndex, stack);
                    LastUnblockEvent = null;
                }
            }

            public void AddCPUSample(double timeRelativeMSec, TraceThread thread, ThreadTimeStackComputer computer)
            {
                // Log the last sample if it was present
                if (LastCPUStackRelativeMSec > 0 && !computer.BlockedTimeOnly)
                {
                    var sample = computer.m_sample;
                    sample.Metric = timeRelativeMSec - LastCPUStackRelativeMSec;
                    if (sample.Metric >= 1.5)
                    {
                        Debug.WriteLine("Warning CPU sample Metric " + sample.Metric.ToString("f3") + " > 1.5Msec at " + LastCPUStackRelativeMSec.ToString("f3"));
                    }

                    sample.TimeRelativeMSec = LastCPUStackRelativeMSec;

                    var nodeIndex = computer.m_cpuFrameIndex;
                    sample.StackIndex = LastCPUCallStack;
                    if (computer.m_traceHasCSwitches)
                    {
                        sample.StackIndex = computer.m_outputStackSource.Interner.CallStackIntern(nodeIndex, sample.StackIndex);
                    }

                    computer.m_outputStackSource.AddSample(sample); // CPU
                }
            }

            public bool ThreadDead { get { return double.IsNegativeInfinity(BlockTimeStartRelativeMSec); } }
            public bool ThreadRunning { get { return BlockTimeStartRelativeMSec < 0 && !ThreadDead; } }
            public bool ThreadBlocked { get { return 0 < BlockTimeStartRelativeMSec; } }
            public bool ThreadUninitialized { get { return BlockTimeStartRelativeMSec == 0; } }

            /* State */
            internal double BlockTimeStartRelativeMSec;        // Negative means not blocked, NegativeInfinity means dead.  0 means uninitialized.  

            public ushort ProcessorNumberWhereBlocked;
            public ushort ProcessorNumberWhereAwakened;

            internal double LastCPUStackRelativeMSec;
            private StackSourceCallStackIndex LastCPUCallStack;

            private double ReadyThreadRelativeMSec;
            private CallStackIndex ReadyThreadCallStack;

            // Because we don't know if something is a hard fault until after the context switch, we have to defer 
            // the logging of the last unblocking event until we know it is not a page fault.  
            private StackSourceSample LastUnblockEvent;

            // Information about a pending Disk I/O (if any) 
            private int DiskIOSize;                      // TODO FIX NOW use or remove
            private double DiskElapsedMSec;
            private double DiskTimeStampRelativeMSec;
            private string DiskIOFilePath;

            // The ASP.NET Request that this thread is operating on (or Guid.Empty if not servicing an ASP.NET Request)
            internal Guid AspNetRequestGuid;
        }

        /// <summary>
        /// Given and activity, return the ASP.NET Guid associated with it (or Guid.Empty if there is not one). 
        /// </summary>
        /// <returns></returns>
        private Guid GetAspNetGuid(TraceActivity activity)
        {
            Debug.Assert(UseTasks);
            Guid ret = Guid.Empty;
            while (activity != null)
            {
                ret = m_activityToASPRequestGuid.Get((int)activity.Index);
                if (ret != Guid.Empty)
                {
                    return ret;
                }

                activity = activity.Creator;
            }
            return ret;
        }

        /// <summary>
        /// Computes the ASP.NET Pseudo frames from the process frame through the thread frame (which includes all 
        /// the pseudo-frames for the ASP.NET groupings. 
        /// </summary>
        private StackSourceCallStackIndex GetAspNetFromProcessFrameThroughThreadFrameStack(Guid aspNetRequestGuid, TraceEvent data, TraceThread thread)
        {
            // Start with the process
            StackSourceCallStackIndex stackIdx = m_outputStackSource.GetCallStackForProcess(thread.Process);


            // TODO Kind of a hack.  
            // If we block in the thread pool we assume that the ASP.NET Request is complete.  
            if (aspNetRequestGuid != Guid.Empty)
            {
                CSwitchTraceData asCSwitch = data as CSwitchTraceData;
                if (asCSwitch != null && ActivityComputer.IsThreadParkedInThreadPool(m_eventLog, asCSwitch.BlockingStack()))
                {
                    m_symbolReader.Log.WriteLine("GetCallStackIndex CSWITCH in threadpool EXCLUDE at {0:n3} Thread {1}", data.TimeStampRelativeMSec, data.ThreadID);
                    aspNetRequestGuid = Guid.Empty;
                }
            }

            if (aspNetRequestGuid != Guid.Empty)
            {
                // Put under a 'requests' node
                stackIdx = m_outputStackSource.Interner.CallStackIntern(m_outputStackSource.Interner.FrameIntern("Requests"), stackIdx);

                // Group by URL if possible 
                string urlString = "UNKNOWN";
                AspNetRequestInfo info;
                if (m_aspNetRequestInfo.TryGetValue(aspNetRequestGuid, out info))
                {
                    stackIdx = m_outputStackSource.Interner.CallStackIntern(m_outputStackSource.Interner.FrameIntern("Request URL " + info.Url), stackIdx);
                    urlString = info.Url;
                }
                else
                {
                    stackIdx = m_outputStackSource.Interner.CallStackIntern(m_outputStackSource.Interner.FrameIntern("Request URL Unknown"), stackIdx);
                }

                // And then by request ID.  
                stackIdx = m_outputStackSource.Interner.CallStackIntern(m_outputStackSource.Interner.FrameIntern("Request ID " + aspNetRequestGuid + " URL: " + urlString), stackIdx);
            }
            else
            {
                stackIdx = m_outputStackSource.Interner.CallStackIntern(m_outputStackSource.Interner.FrameIntern("Not In Requests"), stackIdx);
            }

            // Add the thread.  
            stackIdx = m_outputStackSource.Interner.CallStackIntern(m_outputStackSource.Interner.FrameIntern(thread.VerboseThreadName), stackIdx);
            return stackIdx;
        }

        /// <summary>
        /// Indicates that the aspNet request represented by aspNetGuid is now being  handled by the thread with index 
        /// newThreadIndex.  Thus any old threads handling this request are 'cleared' and replaced with 'newThreadIndex'
        /// If 'newThreadIndex == Invalid then the entry for aspNetGuid is removed.  
        /// </summary>
        private void TransferAspNetRequestToThread(Guid aspNetGuid, ThreadIndex newThreadIndex, string url = null)
        {
            ActivityIndex activityIndex = ActivityIndex.Invalid;

            if (newThreadIndex != ThreadIndex.Invalid && UseTasks)
            {
                // Remember the activity that is associated with this ASP.NET request.  
                TraceThread thread = m_eventLog.Threads[newThreadIndex];
                activityIndex = m_activityComputer.GetCurrentActivity(thread).Index;
            }

            AspNetRequestInfo aspNetInfo;
            if (m_aspNetRequestInfo.TryGetValue(aspNetGuid, out aspNetInfo))
            {
                // Optimization: if we are transferring to the same thread there is nothing to do.  
                if (newThreadIndex == aspNetInfo.ThreadIndex && (!UseTasks || activityIndex == aspNetInfo.ActivityIndex))
                {
                    return;
                }

                // Clear the previous threads association with this request.  
                Debug.Assert(m_threadState[(int)aspNetInfo.ThreadIndex].AspNetRequestGuid == aspNetGuid);
                if (m_threadState[(int)aspNetInfo.ThreadIndex].AspNetRequestGuid == aspNetGuid)
                {
                    m_threadState[(int)aspNetInfo.ThreadIndex].AspNetRequestGuid = Guid.Empty;
                }

                if (aspNetInfo.ActivityIndex != ActivityIndex.Invalid)
                {
                    m_activityToASPRequestGuid.Set((int)aspNetInfo.ActivityIndex, Guid.Empty);
                }
            }
            else
            {
                aspNetInfo.ThreadIndex = ThreadIndex.Invalid;
                aspNetInfo.ActivityIndex = ActivityIndex.Invalid;
            }

            if (newThreadIndex != ThreadIndex.Invalid && aspNetGuid != Guid.Empty)
            {
                aspNetInfo.ThreadIndex = newThreadIndex;
                if (activityIndex != ActivityIndex.Invalid)
                {
                    aspNetInfo.ActivityIndex = activityIndex;
                    m_activityToASPRequestGuid.Set((int)activityIndex, aspNetGuid);
                }

                if (url != null)
                {
                    aspNetInfo.Url = url;
                }

                m_aspNetRequestInfo[aspNetGuid] = aspNetInfo;
                m_threadState[(int)newThreadIndex].AspNetRequestGuid = aspNetGuid;
            }
            else
            {
                m_aspNetRequestInfo.Remove(aspNetGuid);
            }
        }

        // TODO FIX NOW put this somewhere better. 
        /// <summary>
        /// Generate a stack that from the root looks like 'stackIndex followed by 'READIED BY TID(XXXX)' 
        /// followed by frames of 'readyThreadCallStack' (suffixed by READIED_BY)
        /// </summary>
        private static StackSourceCallStackIndex GenerateReadyThreadNodes(MutableTraceEventStackSource stackSource,
            StackSourceCallStackIndex stackIndex, CallStackIndex readyThreadCallStack, double msecWaitingForCpu, int idleCPUs)
        {
            Debug.Assert(readyThreadCallStack != CallStackIndex.Invalid);

            var codeAddress = stackSource.TraceLog.CallStacks.CodeAddressIndex(readyThreadCallStack);
            var readyThreadCaller = stackSource.TraceLog.CallStacks.Caller(readyThreadCallStack);
            if (readyThreadCaller != CallStackIndex.Invalid)
            {
                stackIndex = GenerateReadyThreadNodes(stackSource, stackIndex, readyThreadCaller, msecWaitingForCpu, idleCPUs);
            }
            else
            {
                var thread = stackSource.TraceLog.CallStacks.Thread(readyThreadCallStack);
                var process = thread.Process;
                var cpuWait = "< 1ms";
                if (msecWaitingForCpu > 1)
                {
                    if (msecWaitingForCpu < 5)
                    {
                        cpuWait = "< 5ms";
                    }
                    else if (msecWaitingForCpu < 10)
                    {
                        cpuWait = "< 10ms";
                    }
                    else
                    {
                        cpuWait = "> 10ms";
                    }
                }
                var nodeName = "READIED BY TID(" + thread.ThreadID + ") " + process.Name + " (" + process.ProcessID + ")" +
                    " CPU Wait " + cpuWait + " IdleCPUs " + idleCPUs;
                stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern(nodeName), stackIndex);
            }

            var baseFrameIdx = stackSource.GetFrameIndex(codeAddress);
            var frameIdx = stackSource.Interner.FrameIntern(baseFrameIdx, "(READIED_BY)");
            stackIndex = stackSource.Interner.CallStackIntern(frameIdx, stackIndex);
            return stackIndex;
        }

        /// <summary>
        ///  NetworkInfo remembers useful information to tag blocked time that seems to be network related. 
        ///  It is the value of the m_lastPacketForProcess table mapping threads to network information. 
        /// </summary>
        private class NetworkInfo
        {
            public double TimeRelativeMSec;              // when this packet arrived.
            public IPAddress SourceAddress;
            public int SourcePort;
            public IPAddress DestAddress;
            public int DestPort;
        }

        private NetworkInfo[] m_lastPacketForProcess;   // for each process, what was the last packet that arrived.  

        /// <summary>
        /// AspNetRequestInfo remembers everything we care about associate with an single ASP.NET request.  
        /// It is the value of the m_aspNetRequestInfo table. 
        /// </summary>
        private struct AspNetRequestInfo
        {
            public string Url;                  // URL for this request
            public ThreadIndex ThreadIndex;     // The thread that is currently processing this request.  
            public ActivityIndex ActivityIndex; // The activity that is currently processing this request.  
        }

        private Dictionary<Guid, AspNetRequestInfo> m_aspNetRequestInfo;
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

        /// <summary>
        /// m_IRPToThread maps the I/O request to the thread that initiated it.  This way we can associate
        /// the disk read size and file with the thread that asked for it.  
        /// </summary>
        private Dictionary<Address, TraceThread> m_IRPToThread;

        /// <summary>
        /// Maps processor number to the OS threadID of the thread that is using it.   Allows you 
        /// to determine how (CPU) idle the machine is.  
        /// </summary>
        private int[] m_threadIDUsingProc;              // what thread every processor is using.  

        /// <summary>
        /// Using m_threadIDUsingProc, we compute how many processor are current doing nothing 
        /// </summary>
        private int m_numIdleProcs;                     // Count of bits idle threads in m_threadIDUsingProc

        private bool m_traceHasCSwitches;               // Does the trace have CSwitches (decide what kind of view to create)

        private StackSourceSample m_sample;                 // Reusable scratch space
        private MutableTraceEventStackSource m_outputStackSource; // The output source we are generating. 
        private TraceLog m_eventLog;                        // The event log associated with m_stackSource.  
        private SymbolReader m_symbolReader;

        // These are boring caches of frame names which speed things up a bit.  
        private Dictionary<double, StackSourceFrameIndex> m_nodeNameInternTable;
        private StackSourceFrameIndex m_diskFrameIndex;
        private StackSourceFrameIndex m_hardFaultFrameIndex;
        private StackSourceFrameIndex m_blockedFrameIndex;
        private StackSourceFrameIndex m_cpuFrameIndex;
        private StackSourceFrameIndex m_networkFrameIndex;
        private StackSourceFrameIndex m_readyFrameIndex;
        private GrowableArray<Guid> m_activityToASPRequestGuid;             // Only used when ASP.NET and UseTasks are both set.      
        private ActivityComputer m_activityComputer;                        // Used to compute stacks for Tasks 
        #endregion
    }

    // TODO FIX NOW use or remove 
    internal class NewThreadTimeComputer
    {
        public NewThreadTimeComputer(TraceLog eventLog, SymbolReader symbolReader)
        {
            m_eventLog = eventLog;
            m_symbolReader = symbolReader;
        }

        /* properties of the computer itself */
        /// <summary>
        /// Returns the TraceLog that is associated with the computer (at construction time)
        /// </summary>
        public TraceLog Log { get { return m_eventLog; } }

        /* Callbacks on certain interesting events */
        //public event Action<TraceThread, double, TraceEvent> OnGetCpu;

        //public event Action<TraceThread, double, TraceEvent> OnBlock;


        #region private 
        private TraceLog m_eventLog;                        // The event log associated with m_stackSource.  
        private SymbolReader m_symbolReader;
        #endregion
    }

}
