using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Stacks;
using System;
using System.Diagnostics;

namespace PerfView
{
    /// <summary>
    /// ComputingResourceStateMachine is the heart of a general framework for computing new views of computing resources
    /// based on the logical semantics of the program.    The basic idea is when program structure is async or parallel
    /// you want roll ups of costs (e.g. CPU, thread time, allocations), not by thread, but by some other logical entity
    /// (e.g. a request, or other 'causal chain').   Effectively this engine collates all the costs of a given logical
    /// entity (request) together so that you can understand them logically.  
    /// 
    /// The basic idea is that semantically at any given point in time a thread might be doing work on behalf of some
    /// logical entity (like a request).   Each thread is effectively a 'contractor' that can 'charge' its work to 
    /// any of the logical entities.  Thus the ComputingResourceStateMachine keeps track of the 'charge back state' 
    /// of each thread, and keeps them straight.
    /// 
    /// Now ComputingResourceStateMachine wishes to NOT understand the details of how to determine who to charge.  
    /// This is the job of the ScenarioConfiguration class.   This frees up ComputingResourceStateMachine to just
    /// worry about those things that are independent of particular charge back schemes.  This includes
    ///
    ///     1) Understanding the basics of thread tracking (threads starts and stops ...)
    ///     2) Keeping track of the resources themselves (CPU, Allocation, Thread Time)
    ///     3) In the case of Thread Time, keeping track of the state machine for calculating it from CSWITCH data.  
    ///     4) It also acts as a repository for the stackSource being created.   
    /// </summary>
    public sealed class ComputingResourceStateMachine
    {
        public ComputingResourceStateMachine(MutableTraceEventStackSource outputStackSource, ScenarioConfiguration configuration, ComputingResourceViewType viewType)
        {
            m_OutputStackSource = outputStackSource;
            m_Sample = new StackSourceSample(outputStackSource);
            m_Configuration = configuration;
            m_ViewType = viewType;
            m_ThreadState = new ComputingResourceThreadState[configuration.TraceLog.Threads.Count];
            for (int i = 0; i < m_ThreadState.Length; ++i)
            {
                m_ThreadState[i] = new ComputingResourceThreadState(i);
            }
        }

        /// <summary>
        /// Run the state machine to produce the stackSource given in the constructor.  
        /// </summary>
        public void Execute()
        {
            // Get the trace log.
            TraceLog traceLog = m_Configuration.TraceLog;

            // Get the event dispatcher.
            TraceEventDispatcher eventDispatcher = traceLog.Events.GetSource();

            // Register computing resource event handlers.
            if ((m_ViewType == ComputingResourceViewType.CPU) || (m_ViewType == ComputingResourceViewType.ThreadTime))
            {
                eventDispatcher.Kernel.PerfInfoSample += OnCpuSample;
            }

            if (m_ViewType == ComputingResourceViewType.ThreadTime)
            {
                eventDispatcher.Kernel.ThreadStart += OnThreadStart;
                eventDispatcher.Kernel.ThreadStop += OnThreadEnd;
                eventDispatcher.Kernel.ThreadCSwitch += OnThreadCSwitch;
            }
            if (m_ViewType == ComputingResourceViewType.Allocations)
            {
                eventDispatcher.Clr.GCAllocationTick += OnGCAllocationTick;
            }

            // Register scenario event handlers.
            m_Configuration.ScenarioStateMachine.RegisterEventHandlers(eventDispatcher);

            // Process events.
            eventDispatcher.Process();

            // Sort samples.
            m_OutputStackSource.DoneAddingSamples();
        }

        /// <summary>
        /// Get the scenario configuration.
        /// </summary>
        internal ScenarioConfiguration Configuration { get { return m_Configuration; } }
        /// <summary>
        /// The stackSource being generated.   
        /// </summary>
        internal MutableTraceEventStackSource StackSource { get { return m_OutputStackSource; } }
        /// <summary>
        /// For efficiency, we reuse the same sample to place items in the stacksource.  This is that reused sample.  
        /// </summary>
        internal StackSourceSample Sample { get { return m_Sample; } }

        #region private
        // callbacks for pariticular ETW events
        private void OnCpuSample(SampledProfileTraceData data)
        {
            TraceThread thread = data.Thread();
            Debug.Assert(thread != null);
            if (null == thread)
            {
                return;
            }

            // Log the CPU sample.
            ComputingResourceThreadState threadState = m_ThreadState[(int)thread.ThreadIndex];
            threadState.LogCPUSample(this, thread, data);
        }

        private void OnThreadCSwitch(CSwitchTraceData data)
        {
            // Start blocking on the old thread.
            // Ignore the idle thread.
            if (data.OldThreadID != 0)
            {
                TraceThread oldThread = m_Configuration.TraceLog.Threads.GetThread(data.OldThreadID, data.TimeStampRelativeMSec);
                if (null != oldThread)
                {
                    ComputingResourceThreadState oldThreadState = m_ThreadState[(int)oldThread.ThreadIndex];
                    oldThreadState.LogBlockingStart(this, oldThread, data);
                }
            }

            // Stop blocking on the new thread.
            // Ignore the idle thread.
            if (data.ThreadID != 0)
            {
                TraceThread newThread = data.Thread();
                if (null != newThread)
                {
                    ComputingResourceThreadState newThreadState = m_ThreadState[(int)newThread.ThreadIndex];
                    newThreadState.LogBlockingStop(this, newThread, data);
                }
            }
        }

        private void OnThreadStart(ThreadTraceData data)
        {
            TraceThread thread = data.Thread();
            Debug.Assert(thread != null);
            if (null == thread)
            {
                return;
            }

            // Get the thread state.
            ComputingResourceThreadState threadState = m_ThreadState[(int)thread.ThreadIndex];

            // Mark that blocking has started.
            threadState.LogBlockingStart(this, thread, data);
        }

        private void OnThreadEnd(ThreadTraceData data)
        {
            TraceThread thread = data.Thread();
            Debug.Assert(thread != null);
            if (null == thread)
            {
                return;
            }

            // Get the thread state.
            ComputingResourceThreadState threadState = m_ThreadState[(int)thread.ThreadIndex];

            // Mark the thread as dead.
            threadState.BlockTimeStartRelativeMSec = double.NegativeInfinity;
        }

        private void OnGCAllocationTick(GCAllocationTickTraceData data)
        {
            TraceThread thread = data.Thread();
            Debug.Assert(thread != null);
            if (null == thread)
            {
                return;
            }

            // Attempt to charge the allocation to a request.
            ScenarioThreadState scenarioThreadState = m_Configuration.ScenarioThreadState[(int)thread.ThreadIndex];

            CallStackIndex traceLogCallStackIndex = data.CallStackIndex();
            StackSourceCallStackIndex callStackIndex = scenarioThreadState.GetCallStackIndex(m_OutputStackSource, thread, data);

            // Add the thread.
            StackSourceFrameIndex threadFrameIndex = m_OutputStackSource.Interner.FrameIntern(thread.VerboseThreadName);
            callStackIndex = m_OutputStackSource.Interner.CallStackIntern(threadFrameIndex, callStackIndex);

            // Get the allocation call stack index.
            callStackIndex = m_OutputStackSource.GetCallStack(traceLogCallStackIndex, callStackIndex, null);

            // Add the type.
            string typeName = data.TypeName;
            if (typeName.Length > 0)
            {
                StackSourceFrameIndex nodeIndex = m_OutputStackSource.Interner.FrameIntern("Type " + data.TypeName);
                callStackIndex = m_OutputStackSource.Interner.CallStackIntern(nodeIndex, callStackIndex);
            }

            // Add a notification for large objects.
            if (data.AllocationKind == GCAllocationKind.Large)
            {

                StackSourceFrameIndex nodeIndex = m_OutputStackSource.Interner.FrameIntern("LargeObject");
                callStackIndex = m_OutputStackSource.Interner.CallStackIntern(nodeIndex, callStackIndex);
            }

            // Set the time.
            m_Sample.TimeRelativeMSec = data.TimeStampRelativeMSec;

            // Set the metric.
            bool seenBadAllocTick = false;
            m_Sample.Metric = data.GetAllocAmount(ref seenBadAllocTick);

            // Set the stack index.
            m_Sample.StackIndex = callStackIndex;

            // Add the sample.
            m_OutputStackSource.AddSample(m_Sample);
        }


        // Fields from the constructor 
        private MutableTraceEventStackSource m_OutputStackSource;
        private ScenarioConfiguration m_Configuration;
        private ComputingResourceViewType m_ViewType;

        // Other Fields 
        /// <summary>
        /// As an optimization we reuse the same sample when adding samples to the m_StackSource, this is that sample 
        /// </summary>
        private StackSourceSample m_Sample;

        /// <summary>
        /// The thread state required for profiling computing resources.
        /// </summary>
        private ComputingResourceThreadState[] m_ThreadState;
        #endregion
    }

    /// <summary>
    /// Connects the two state machines together.
    /// </summary>
    /// <remarks>
    /// Responsible for creation of the scenario state machine and scenario thread state.
    /// Tells the computing resource state machine about the scenario state machine.
    /// Tells the computing resource about the custom data that must be stored in the thread state.
    /// Stores and manages the scenario thread state for every thread in the trace.
    ///      (A given scenario thread state needs unfettered access to scenario thread state on other threads to e.g. clear a request off another thread.)
    /// </remarks>
    public abstract class ScenarioConfiguration
    {
        public ScenarioConfiguration(TraceLog traceLog)
        {
            m_TraceLog = traceLog;
        }

        /// <summary>
        /// The trace log to be consumed.
        /// </summary>
        public TraceLog TraceLog
        {
            get { return m_TraceLog; }
        }

        /// <summary>
        /// Get the scenario state machine.
        /// </summary>
        /// <remarks>
        /// This member is responsible for storage of the state machine, and must not create a new one on each call.
        /// </remarks>
        public abstract ScenarioStateMachine ScenarioStateMachine { get; }

        /// <summary>
        /// Get the thread state associated with the scenario.
        /// </summary>
        /// <remarks>
        /// This class is responsible for initialization of the thread state, based on input to the constructor (e.g. the trace log is required to know how many threads exist).
        /// Index into this array by using the ThreadIndex.
        /// </remarks>
        public abstract ScenarioThreadState[] ScenarioThreadState { get; }

        /// <summary>
        /// Fired if any changes in the stack occur during blocked times
        /// </summary>
        public Action<TraceThread> StackChanged;

        #region private
        private TraceLog m_TraceLog;
        #endregion
    }

    /// <summary>
    /// The state machine that represents the actual scenario.
    /// </summary>
    /// <remarks>
    /// Understands the scenario that the data is used for.
    /// Knows how to slice the data to make it useful (e.g. into requests).
    /// Determines whether samples should be attributed or thrown out (?)
    /// </remarks>
    public abstract class ScenarioStateMachine
    {
        private ScenarioConfiguration m_Configuration;

        public ScenarioStateMachine(
            ScenarioConfiguration configuration)
        {
            m_Configuration = configuration;
        }

        /// <summary>
        /// Get the configuration associated with the state machine.
        /// </summary>
        /// <remarks>
        /// For performance reasons, it is recommended that state machines keep a reference to the scenario thread state, so that it does not need to be casted to the actual type multiple times.
        /// </remarks>
        protected ScenarioConfiguration Configuration
        {
            get { return m_Configuration; }
        }

        /// <summary>
        /// Register event handlers before data is processed.
        /// </summary>
        public abstract void RegisterEventHandlers(
            TraceEventDispatcher eventDispatcher);
    }

    /// <summary>
    /// Contains the thread state associated with the scenario.
    /// </summary>
    public class ScenarioThreadState
    {
        /// <summary>
        /// Get the 'logcal' call stack from PROCESS ROOT (the root of all stacks) to (but not including) the frame for the
        /// thread.   By default (if you can't attribute it to anything else) it will just be attributed to the process, however
        /// it is likley that you want to insert pseudo-frames for the request and other logical groupings here.  
        /// 
        /// The actual method frames within a thread, as well as any resource specific pseduo-frames (e.g. BLOCKING, ...)
        /// are added by the ComputingResourceMachine itself.  
        ///</summary>   
        public virtual StackSourceCallStackIndex GetCallStackIndex(MutableTraceEventStackSource stackSource, TraceThread thread, TraceEvent data)
        {
            var callStackIndex = stackSource.GetCallStackForProcess(thread.Process);
            // There is no request, so add this stack as an unattributed sample.
            string frameName = "Unattributed";
            StackSourceFrameIndex requestsFrameIndex = stackSource.Interner.FrameIntern(frameName);
            callStackIndex = stackSource.Interner.CallStackIntern(requestsFrameIndex, callStackIndex);
            return callStackIndex;
        }
    }

    /// <summary>
    /// The types of resources ComputingResourceStateMachine understands how to gather.  
    /// </summary>
    public enum ComputingResourceViewType
    {
        CPU,
        ThreadTime,
        Allocations
    }

    #region private classes
    /// <summary>
    /// ComputingResourceThreadState contains the state of the thread that ComputingResourceStateMachine needs to do its work.   
    /// 
    /// It is only used by ComputingResourceStateMachine and it basically keeps track of the thread state (blocked, running etc)
    /// of each thread.   
    /// </summary>
    internal sealed class ComputingResourceThreadState
    {
        public ComputingResourceThreadState(
            int threadIndex)
        {
            m_ThreadIndex = threadIndex;
        }

        /// <summary>
        /// The thread index associated with this thread.
        /// </summary>
        public int ThreadIndex
        {
            get { return m_ThreadIndex; }
        }

        /// <summary>
        /// The start time associated with a blocked thread.
        /// </summary>
        public double BlockTimeStartRelativeMSec { get; set; }

        /// <summary>
        /// True iff the thread is dead.
        /// </summary>
        public bool ThreadDead
        {
            get { return double.IsNegativeInfinity(BlockTimeStartRelativeMSec); }
        }

        /// <summary>
        /// True iff the threadd isrunning.
        /// </summary>
        public bool ThreadRunning
        {
            get { return BlockTimeStartRelativeMSec < 0 && !ThreadDead; }
        }

        /// <summary>
        /// True iff the thread is blocked.
        /// </summary>
        public bool ThreadBlocked
        {
            get { return 0 < BlockTimeStartRelativeMSec; }
        }

        /// <summary>
        /// True iff the thread is unitialized.
        /// </summary>
        public bool ThreadUninitialized
        {
            get { return BlockTimeStartRelativeMSec == 0; }
        }

        /// <summary>
        /// Mark the thread as blocked.
        /// </summary>
        public void LogBlockingStart(
            ComputingResourceStateMachine stateMachine,
            TraceThread thread,
            TraceEvent data)
        {
            if ((null == thread) || (null == data))
            {
                return;
            }

            if (!ThreadDead)
            {
                // TODO: Fix (we'll need the last CPU stack as well).
                // AddCPUSample(timeRelativeMSec, thread, computer);

                BlockTimeStartRelativeMSec = data.TimeStampRelativeMSec;
            }
        }

        /// <summary>
        /// Mark the thread as unblocked.
        /// </summary>
        public void LogBlockingStop(
            ComputingResourceStateMachine stateMachine,
            TraceThread thread,
            TraceEvent data)
        {
            if ((null == stateMachine) || (null == thread) || (null == data))
            {
                return;
            }

            // Only add a sample if the thread was blocked.
            if (ThreadBlocked)
            {
                StackSourceSample sample = stateMachine.Sample;
                MutableTraceEventStackSource stackSource = stateMachine.StackSource;

                // Set the time and metric.
                sample.TimeRelativeMSec = BlockTimeStartRelativeMSec;
                sample.Metric = (float)(data.TimeStampRelativeMSec - BlockTimeStartRelativeMSec);

                /* Generate the stack trace. */

                CallStackIndex traceLogCallStackIndex = data.CallStackIndex();
                ScenarioThreadState scenarioThreadState = stateMachine.Configuration.ScenarioThreadState[ThreadIndex];
                StackSourceCallStackIndex callStackIndex = scenarioThreadState.GetCallStackIndex(stateMachine.StackSource, thread, data);

                // Add the thread.
                StackSourceFrameIndex threadFrameIndex = stackSource.Interner.FrameIntern(thread.VerboseThreadName);
                callStackIndex = stackSource.Interner.CallStackIntern(threadFrameIndex, callStackIndex);

                // Add the full call stack.
                callStackIndex = stackSource.GetCallStack(traceLogCallStackIndex, callStackIndex, null);

                // Add Pseud-frames representing the kind of resource.  
                StackSourceFrameIndex frameIndex = stackSource.Interner.FrameIntern("BLOCKED TIME");
                callStackIndex = stackSource.Interner.CallStackIntern(frameIndex, callStackIndex);

                // Add the call stack to the sample.
                sample.StackIndex = callStackIndex;

                // Add the sample.
                stackSource.AddSample(sample);

                // Mark the thread as executing.
                BlockTimeStartRelativeMSec = -1;
            }
        }

        /// <summary>
        /// Log a CPU sample on this thread.
        /// </summary>
        public void LogCPUSample(
            ComputingResourceStateMachine stateMachine,
            TraceThread thread,
            TraceEvent data)
        {
            if ((null == stateMachine) || (null == thread) || (null == data))
            {
                return;
            }

            StackSourceSample sample = stateMachine.Sample;
            sample.Metric = 1;
            sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
            MutableTraceEventStackSource stackSource = stateMachine.StackSource;

            // Attempt to charge the CPU to a request.
            CallStackIndex traceLogCallStackIndex = data.CallStackIndex();

            ScenarioThreadState scenarioThreadState = stateMachine.Configuration.ScenarioThreadState[ThreadIndex];
            StackSourceCallStackIndex callStackIndex = scenarioThreadState.GetCallStackIndex(stackSource, thread, data);

            // Add the thread.
            StackSourceFrameIndex threadFrameIndex = stackSource.Interner.FrameIntern(thread.VerboseThreadName);
            callStackIndex = stackSource.Interner.CallStackIntern(threadFrameIndex, callStackIndex);

            // Rest of the stack.
            // NOTE: Do not pass a call stack map into this method, as it will skew results.
            callStackIndex = stackSource.GetCallStack(traceLogCallStackIndex, callStackIndex, null);

            // Add the CPU frame.
            StackSourceFrameIndex cpuFrameIndex = stackSource.Interner.FrameIntern("CPU");
            callStackIndex = stackSource.Interner.CallStackIntern(cpuFrameIndex, callStackIndex);

            // Add the sample.
            sample.StackIndex = callStackIndex;
            stackSource.AddSample(sample);
        }

        private int m_ThreadIndex;
    }


    #endregion
}
