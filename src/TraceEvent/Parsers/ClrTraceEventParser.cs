//     Copyright (c) Microsoft Corporation.  All rights reserved.
using FastSerialization;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Utilities;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Address = System.UInt64;

// This file was generated with the following command:
//    traceParserGen CLREtwAll.man CLRTraceEventParser.cs
// And then modified by hand to add functionality (handle to name lookup, fixup of evenMethodLoadUnloadTraceDMOatats ...)
// Note: /merge option is no more available (does not even compile)
namespace Microsoft.Diagnostics.Tracing.Parsers
{
    using Microsoft.Diagnostics.Tracing.Parsers.Clr;
    using System.Collections.Generic;

    /* Parsers defined in this file */
    // ClrTraceEventParser, ClrRundownTraceEventParser, ClrStressTraceEventParser 
    /* ClrPrivateTraceEventParser  #ClrPrivateProvider */
    // [SecuritySafeCritical]
    [System.CodeDom.Compiler.GeneratedCode("traceparsergen", "1.0")]
    public sealed class ClrTraceEventParser : TraceEventParser
    {
        public static readonly string ProviderName = "Microsoft-Windows-DotNETRuntime";
        public static readonly Guid ProviderGuid = new Guid(unchecked((int)0xe13c0d23), unchecked((short)0xccbc), unchecked((short)0x4e12), 0x93, 0x1b, 0xd9, 0xcc, 0x2e, 0xee, 0x27, 0xe4);

        // Project N and the Desktop have separate guids.  
        public static readonly Guid NativeProviderGuid = new Guid(0x47c3ba0c, 0x77f1, 0x4eb0, 0x8d, 0x4d, 0xae, 0xf4, 0x47, 0xf1, 0x6a, 0x85);

        /// <summary>
        ///  Keywords are passed to TraceEventSession.EnableProvider to enable particular sets of
        /// </summary>
        [Flags]
        public enum Keywords : long
        {
            None = 0,
            All = ~StartEnumeration,        // All does not include start-enumeration.  It just is not that useful.  
            /// <summary>
            /// Logging when garbage collections and finalization happen. 
            /// </summary>
            GC = 0x1,
            /// <summary>
            /// Events when GC handles are set or destroyed.
            /// </summary>
            GCHandle = 0x2,
            Binder = 0x4,
            /// <summary>
            /// Logging when modules actually get loaded and unloaded. 
            /// </summary>
            Loader = 0x8,
            /// <summary>
            /// Logging when Just in time (JIT) compilation occurs. 
            /// </summary>
            Jit = 0x10,
            /// <summary>
            /// Logging when precompiled native (NGEN) images are loaded.
            /// </summary>
            NGen = 0x20,
            /// <summary>
            /// Indicates that on attach or module load , a rundown of all existing methods should be done
            /// </summary>
            StartEnumeration = 0x40,
            /// <summary>
            /// Indicates that on detach or process shutdown, a rundown of all existing methods should be done
            /// </summary>
            StopEnumeration = 0x80,
            /// <summary>
            /// Events associated with validating security restrictions.
            /// </summary>
            Security = 0x400,
            /// <summary>
            /// Events for logging resource consumption on an app-domain level granularity
            /// </summary>
            AppDomainResourceManagement = 0x800,
            /// <summary>
            /// Logging of the internal workings of the Just In Time compiler.  This is fairly verbose.  
            /// It details decisions about interesting optimization (like inlining and tail call) 
            /// </summary>
            JitTracing = 0x1000,
            /// <summary>
            /// Log information about code thunks that transition between managed and unmanaged code. 
            /// </summary>
            Interop = 0x2000,
            /// <summary>
            /// Log when lock contention occurs.  (Monitor.Enters actually blocks)
            /// </summary>
            Contention = 0x4000,
            /// <summary>
            /// Log exception processing.  
            /// </summary>
            Exception = 0x8000,
            /// <summary>
            /// Log events associated with the threadpool, and other threading events.  
            /// </summary>
            Threading = 0x10000,
            /// <summary>
            /// Dump the native to IL mapping of any method that is JIT compiled.  (V4.5 runtimes and above).  
            /// </summary>
            JittedMethodILToNativeMap = 0x20000,
            /// <summary>
            /// If enabled will suppress the rundown of NGEN events on V4.0 runtime (has no effect on Pre-V4.0 runtimes).
            /// </summary>
            OverrideAndSuppressNGenEvents = 0x40000,
            /// <summary>
            /// Enables the 'BulkType' event
            /// </summary>
            Type = 0x80000,
            /// <summary>
            /// Enables the events associated with dumping the GC heap
            /// </summary>
            GCHeapDump = 0x100000,
            /// <summary>
            /// Enables allocation sampling with the 'fast'.  Sample to limit to 100 allocations per second per type.  
            /// This is good for most detailed performance investigations.   Note that this DOES update the allocation
            /// path to be slower and only works if the process start with this on. 
            /// </summary>
            GCSampledObjectAllocationHigh = 0x200000,
            /// <summary>
            /// Enables events associate with object movement or survival with each GC.  
            /// </summary>
            GCHeapSurvivalAndMovement = 0x400000,
            /// <summary>
            /// Triggers a GC.  Can pass a 64 bit value that will be logged with the GC Start event so you know which GC you actually triggered.  
            /// </summary>
            GCHeapCollect = 0x800000,
            /// <summary>
            /// Indicates that you want type names looked up and put into the events (not just meta-data tokens).
            /// </summary>
            GCHeapAndTypeNames = 0x1000000,
            /// <summary>
            /// Enables allocation sampling with the 'slow' rate, Sample to limit to 5 allocations per second per type.  
            /// This is reasonable for monitoring.    Note that this DOES update the allocation path to be slower
            /// and only works if the process start with this on.  
            /// </summary>
            GCSampledObjectAllocationLow = 0x2000000,
            /// <summary>
            /// Turns on capturing the stack and type of object allocation made by the .NET Runtime.   This is only
            /// supported after V4.5.3 (Late 2014)   This can be very verbose and you should seriously using  GCSampledObjectAllocationHigh
            /// instead (and GCSampledObjectAllocationLow for production scenarios).  
            /// </summary>
            GCAllObjectAllocation = GCSampledObjectAllocationHigh | GCSampledObjectAllocationLow,
            /// <summary>
            /// This suppresses NGEN events on V4.0 (where you have NGEN PDBs), but not on V2.0 (which does not know about this 
            /// bit and also does not have NGEN PDBS).  
            /// </summary>
            SupressNGen = 0x40000,
            /// <summary>
            /// TODO document
            /// </summary>
            PerfTrack = 0x20000000,
            /// <summary>
            /// Also log the stack trace of events for which this is valuable.
            /// </summary>
            Stack = 0x40000000,
            /// <summary>
            /// This allows tracing work item transfer events (thread pool enqueue/dequeue/ioenqueue/iodequeue/a.o.)
            /// </summary>
            ThreadTransfer = 0x80000000L,
            /// <summary>
            /// .NET Debugger events
            /// </summary>
            Debugger = 0x100000000,
            /// <summary>
            /// Events intended for monitoring on an ongoing basis.  
            /// </summary>
            Monitoring = 0x200000000,
            /// <summary>
            /// Events that will dump PDBs of dynamically generated assemblies to the ETW stream.  
            /// </summary>
            Codesymbols = 0x400000000,
            /// <summary>
            /// Events that provide information about compilation.
            /// </summary>
            Compilation = 0x1000000000,
            /// <summary>
            /// Diagnostic events for diagnosing compilation and pre-compilation features.
            /// </summary>
            CompilationDiagnostic = 0x2000000000,

            /// <summary>
            /// Diagnostic events for capturing token information for events that express MethodID
            /// </summary>
            MethodDiagnostic = 0x4000000000,

            /// <summary>
            /// Diagnostic events for diagnosing issues involving the type loader.
            /// </summary>
            TypeDiagnostic = 0x8000000000,

            /// <summary>
            /// Recommend default flags (good compromise on verbosity).  
            /// </summary>
            Default = GC | Type | GCHeapSurvivalAndMovement | Binder | Loader | Jit | NGen | SupressNGen
                         | StopEnumeration | Security | AppDomainResourceManagement | Exception | Threading | Contention | Stack | JittedMethodILToNativeMap
                         | ThreadTransfer | GCHeapAndTypeNames | Codesymbols | Compilation,

            /// <summary>
            /// What is needed to get symbols for JIT compiled code.  
            /// </summary>
            JITSymbols = Jit | StopEnumeration | JittedMethodILToNativeMap | SupressNGen | Loader,

            /// <summary>
            /// This provides the flags commonly needed to take a heap .NET Heap snapshot with ETW.  
            /// </summary>
            GCHeapSnapshot = GC | GCHeapCollect | GCHeapDump | GCHeapAndTypeNames | Type,
        };
        public ClrTraceEventParser(TraceEventSource source) : base(source)
        {

            // Subscribe to the GCBulkType events and remember the TypeID -> TypeName mapping. 
            ClrTraceEventParserState state = State;
            AddCallbackForEvents<GCBulkTypeTraceData>(delegate (GCBulkTypeTraceData data)
            {
                for (int i = 0; i < data.Count; i++)
                {
                    GCBulkTypeValues value = data.Values(i);
                    string typeName = value.TypeName;
                    // The GCBulkType events are logged after the event that needed it.  It really
                    // should be before, but we compensate by setting the startTime to 0
                    // Ideally the CLR logs the types before they are used.  
                    state.SetTypeIDToName(data.ProcessID, value.TypeID, 0, typeName);
                }
            });

        }

        /// <summary>
        /// Fetch the state object associated with this parser and cast it to
        /// the ClrTraceEventParserState type.   This state object contains any
        /// information that you need from one event to another to decode events.
        /// (typically ID->Name tables).  
        /// </summary>
        internal ClrTraceEventParserState State
        {
            get
            {
                ClrTraceEventParserState ret = (ClrTraceEventParserState)StateObject;
                if (ret == null)
                {
                    ret = new ClrTraceEventParserState();
                    StateObject = ret;
                }
                return ret;
            }
        }

        public event Action<GenAwareBeginTraceData> GCGenAwareStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(GenAwareBeginTemplate(value));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 206, ProviderGuid);
                source.UnregisterEventTemplate(value, 206, GCTaskGuid);
            }
        }

        public event Action<GenAwareEndTraceData> GCGenAwareEnd
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(GenAwareEndTemplate(value));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 207, ProviderGuid);
                source.UnregisterEventTemplate(value, 207, GCTaskGuid);
            }
        }

        public event Action<GCStartTraceData> GCStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCStartTraceData(value, 1, 1, "GC", GCTaskGuid, 1, "Start", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 1, ProviderGuid);
                source.UnregisterEventTemplate(value, 1, GCTaskGuid);
            }
        }
        public event Action<GCEndTraceData> GCStop
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCEndTraceData(value, 2, 1, "GC", GCTaskGuid, 2, "Stop", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 2, ProviderGuid);
                source.UnregisterEventTemplate(value, 2, GCTaskGuid);
            }
        }
        public event Action<GCNoUserDataTraceData> GCRestartEEStop
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCNoUserDataTraceData(value, 3, 1, "GC", GCTaskGuid, 132, "RestartEEStop", ProviderGuid, ProviderName));
                // Added for V2 Runtime compatibility (Classic ETW only)
                RegisterTemplate(new GCNoUserDataTraceData(value, 0xFFFF, 1, "GC", GCTaskGuid, 8, "RestartEEStop", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 3, ProviderGuid);
                source.UnregisterEventTemplate(value, 132, GCTaskGuid);
            }
        }
        public event Action<GCHeapStatsTraceData> GCHeapStats
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCHeapStatsTraceData(value, 4, 1, "GC", GCTaskGuid, 133, "HeapStats", ProviderGuid, ProviderName));
                // Added for V2 Runtime compatibility (Classic ETW only)
                RegisterTemplate(new GCHeapStatsTraceData(value, 0xFFFF, 1, "GC", GCTaskGuid, 5, "HeapStats", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 4, ProviderGuid);
                source.UnregisterEventTemplate(value, 133, GCTaskGuid);
            }
        }
        public event Action<GCCreateSegmentTraceData> GCCreateSegment
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCCreateSegmentTraceData(value, 5, 1, "GC", GCTaskGuid, 134, "CreateSegment", ProviderGuid, ProviderName));
                // Added for V2 Runtime compatibility (Classic ETW only)
                RegisterTemplate(new GCCreateSegmentTraceData(value, 0xFFFF, 1, "GC", GCTaskGuid, 6, "CreateSegment", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 5, ProviderGuid);
                source.UnregisterEventTemplate(value, 134, GCTaskGuid);
            }
        }
        public event Action<GCFreeSegmentTraceData> GCFreeSegment
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCFreeSegmentTraceData(value, 6, 1, "GC", GCTaskGuid, 135, "FreeSegment", ProviderGuid, ProviderName));
                // Added for V2 Runtime compatibility (Classic ETW only)
                RegisterTemplate(new GCFreeSegmentTraceData(value, 0xFFFF, 1, "GC", GCTaskGuid, 7, "FreeSegment", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 6, ProviderGuid);
                source.UnregisterEventTemplate(value, 135, GCTaskGuid);
            }
        }
        public event Action<GCNoUserDataTraceData> GCRestartEEStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCNoUserDataTraceData(value, 7, 1, "GC", GCTaskGuid, 136, "RestartEEStart", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 7, ProviderGuid);
                source.UnregisterEventTemplate(value, 136, GCTaskGuid);
            }
        }
        public event Action<GCNoUserDataTraceData> GCSuspendEEStop
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCNoUserDataTraceData(value, 8, 1, "GC", GCTaskGuid, 137, "SuspendEEStop", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 8, ProviderGuid);
                source.UnregisterEventTemplate(value, 137, GCTaskGuid);
            }
        }
        public event Action<GCSuspendEETraceData> GCSuspendEEStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCSuspendEETraceData(value, 9, 1, "GC", GCTaskGuid, 10, "SuspendEEStart", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 9, ProviderGuid);
                source.UnregisterEventTemplate(value, 10, GCTaskGuid);
            }
        }
        public event Action<GCAllocationTickTraceData> GCAllocationTick
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCAllocationTickTraceData(value, 10, 1, "GC", GCTaskGuid, 11, "AllocationTick", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 10, ProviderGuid);
                source.UnregisterEventTemplate(value, 11, GCTaskGuid);
            }
        }
        public event Action<GCCreateConcurrentThreadTraceData> GCCreateConcurrentThread
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCCreateConcurrentThreadTraceData(value, 11, 1, "GC", GCTaskGuid, 12, "CreateConcurrentThread", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 11, ProviderGuid);
                source.UnregisterEventTemplate(value, 12, GCTaskGuid);
            }
        }
        public event Action<GCTerminateConcurrentThreadTraceData> GCTerminateConcurrentThread
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCTerminateConcurrentThreadTraceData(value, 12, 1, "GC", GCTaskGuid, 13, "TerminateConcurrentThread", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 12, ProviderGuid);
                source.UnregisterEventTemplate(value, 13, GCTaskGuid);
            }
        }
        public event Action<GCFinalizersEndTraceData> GCFinalizersStop
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCFinalizersEndTraceData(value, 13, 1, "GC", GCTaskGuid, 15, "FinalizersStop", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 13, ProviderGuid);
                source.UnregisterEventTemplate(value, 15, GCTaskGuid);
            }
        }
        public event Action<GCNoUserDataTraceData> GCFinalizersStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCNoUserDataTraceData(value, 14, 1, "GC", GCTaskGuid, 19, "FinalizersStart", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 14, ProviderGuid);
                source.UnregisterEventTemplate(value, 19, GCTaskGuid);
            }
        }
        public event Action<GCBulkTypeTraceData> TypeBulkType
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCBulkTypeTraceData(value, 15, 21, "Type", TypeTaskGuid, 10, "BulkType", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 15, ProviderGuid);
                source.UnregisterEventTemplate(value, 10, TypeTaskGuid);
            }
        }
        public event Action<MethodDetailsTraceData> MethodMethodDetails
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new MethodDetailsTraceData(value, 72, 9, "Method", MethodTaskGuid, 43, "MethodDetails", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 72, ProviderGuid);
                source.UnregisterEventTemplate(value, 43, MethodTaskGuid);
            }
        }
        public event Action<GCBulkRootEdgeTraceData> GCBulkRootEdge
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCBulkRootEdgeTraceData(value, 16, 1, "GC", GCTaskGuid, 20, "BulkRootEdge", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 16, ProviderGuid);
                source.UnregisterEventTemplate(value, 20, GCTaskGuid);
            }
        }
        public event Action<GCBulkRootConditionalWeakTableElementEdgeTraceData> GCBulkRootConditionalWeakTableElementEdge
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCBulkRootConditionalWeakTableElementEdgeTraceData(value, 17, 1, "GC", GCTaskGuid, 21, "BulkRootConditionalWeakTableElementEdge", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 17, ProviderGuid);
                source.UnregisterEventTemplate(value, 21, GCTaskGuid);
            }
        }
        public event Action<GCBulkNodeTraceData> GCBulkNode
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCBulkNodeTraceData(value, 18, 1, "GC", GCTaskGuid, 22, "BulkNode", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 18, ProviderGuid);
                source.UnregisterEventTemplate(value, 22, GCTaskGuid);
            }
        }
        public event Action<GCBulkEdgeTraceData> GCBulkEdge
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCBulkEdgeTraceData(value, 19, 1, "GC", GCTaskGuid, 23, "BulkEdge", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 19, ProviderGuid);
                source.UnregisterEventTemplate(value, 23, GCTaskGuid);
            }
        }
        public event Action<GCSampledObjectAllocationTraceData> GCSampledObjectAllocation
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCSampledObjectAllocationTraceData(value, 20, 1, "GC", GCTaskGuid, 24, "SampledObjectAllocation", ProviderGuid, ProviderName));
                RegisterTemplate(new GCSampledObjectAllocationTraceData(value, 32, 1, "GC", GCTaskGuid, 24, "SampledObjectAllocation", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 20, ProviderGuid);
                source.UnregisterEventTemplate(value, 24, GCTaskGuid);
                source.UnregisterEventTemplate(value, 32, ProviderGuid);
                source.UnregisterEventTemplate(value, 24, GCTaskGuid);
            }
        }
        public event Action<GCBulkSurvivingObjectRangesTraceData> GCBulkSurvivingObjectRanges
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCBulkSurvivingObjectRangesTraceData(value, 21, 1, "GC", GCTaskGuid, 25, "BulkSurvivingObjectRanges", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 21, ProviderGuid);
                source.UnregisterEventTemplate(value, 25, GCTaskGuid);
            }
        }
        public event Action<GCBulkMovedObjectRangesTraceData> GCBulkMovedObjectRanges
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCBulkMovedObjectRangesTraceData(value, 22, 1, "GC", GCTaskGuid, 26, "BulkMovedObjectRanges", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 22, ProviderGuid);
                source.UnregisterEventTemplate(value, 26, GCTaskGuid);
            }
        }
        public event Action<GCGenerationRangeTraceData> GCGenerationRange
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCGenerationRangeTraceData(value, 23, 1, "GC", GCTaskGuid, 27, "GenerationRange", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 23, ProviderGuid);
                source.UnregisterEventTemplate(value, 27, GCTaskGuid);
            }
        }
        public event Action<GCMarkTraceData> GCMarkStackRoots
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCMarkTraceData(value, 25, 1, "GC", GCTaskGuid, 28, "MarkStackRoots", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 25, ProviderGuid);
                source.UnregisterEventTemplate(value, 28, GCTaskGuid);
            }
        }
        public event Action<GCMarkTraceData> GCMarkFinalizeQueueRoots
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCMarkTraceData(value, 26, 1, "GC", GCTaskGuid, 29, "MarkFinalizeQueueRoots", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 26, ProviderGuid);
                source.UnregisterEventTemplate(value, 29, GCTaskGuid);
            }
        }
        public event Action<GCMarkTraceData> GCMarkHandles
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCMarkTraceData(value, 27, 1, "GC", GCTaskGuid, 30, "MarkHandles", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 27, ProviderGuid);
                source.UnregisterEventTemplate(value, 30, GCTaskGuid);
            }
        }
        public event Action<GCMarkTraceData> GCMarkCards
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCMarkTraceData(value, 28, 1, "GC", GCTaskGuid, 31, "MarkCards", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 28, ProviderGuid);
                source.UnregisterEventTemplate(value, 31, GCTaskGuid);
            }
        }
        public event Action<GCMarkWithTypeTraceData> GCMarkWithType
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCMarkWithTypeTraceData(value, 202, 1, "GC", GCTaskGuid, 202, "Mark", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 202, ProviderGuid);
                source.UnregisterEventTemplate(value, 202, GCTaskGuid);
            }
        }
        public event Action<GCPerHeapHistoryTraceData> GCPerHeapHistory
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCPerHeapHistoryTraceData(value, 204, 1, "GC", GCTaskGuid, 204, "PerHeapHistory", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 204, ProviderGuid);
                source.UnregisterEventTemplate(value, 204, GCTaskGuid);
            }
        }
        public event Action<GCGlobalHeapHistoryTraceData> GCGlobalHeapHistory
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCGlobalHeapHistoryTraceData(value, 205, 1, "GC", GCTaskGuid, 205, "GlobalHeapHistory", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 205, ProviderGuid);
                source.UnregisterEventTemplate(value, 205, GCTaskGuid);
            }
        }
        public event Action<GCJoinTraceData> GCJoin
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCJoinTraceData(value, 203, 1, "GC", GCTaskGuid, 203, "Join", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 203, ProviderGuid);
                source.UnregisterEventTemplate(value, 203, GCTaskGuid);
            }
        }
        public event Action<FinalizeObjectTraceData> GCFinalizeObject
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new FinalizeObjectTraceData(value, 29, 1, "GC", GCTaskGuid, 32, "FinalizeObject", ProviderGuid, ProviderName, State));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 29, ProviderGuid);
                source.UnregisterEventTemplate(value, 32, GCTaskGuid);
            }
        }
        public event Action<SetGCHandleTraceData> GCSetGCHandle
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new SetGCHandleTraceData(value, 30, 1, "GC", GCTaskGuid, 33, "SetGCHandle", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 30, ProviderGuid);
                source.UnregisterEventTemplate(value, 33, GCTaskGuid);
            }
        }
        public event Action<DestroyGCHandleTraceData> GCDestoryGCHandle
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new DestroyGCHandleTraceData(value, 31, 1, "GC", GCTaskGuid, 34, "DestoryGCHandle", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 31, ProviderGuid);
                source.UnregisterEventTemplate(value, 34, GCTaskGuid);
            }
        }
        public event Action<PinObjectAtGCTimeTraceData> GCPinObjectAtGCTime
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new PinObjectAtGCTimeTraceData(value, 33, 1, "GC", GCTaskGuid, 36, "PinObjectAtGCTime", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 33, ProviderGuid);
                source.UnregisterEventTemplate(value, 36, GCTaskGuid);
            }
        }
        public event Action<PinPlugAtGCTimeTraceData> GCPinPlugAtGCTime
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new PinPlugAtGCTimeTraceData(value, 34, 1, "GC", GCTaskGuid, 37, "PinPlugAtGCTime", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 34, ProviderGuid);
                source.UnregisterEventTemplate(value, 37, GCTaskGuid);
            }
        }
        public event Action<GCTriggeredTraceData> GCTriggered
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCTriggeredTraceData(value, 35, 1, "GC", GCTaskGuid, 35, "Triggered", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 35, ProviderGuid);
                source.UnregisterEventTemplate(value, 35, GCTaskGuid);
            }
        }
        public event Action<GCBulkRootCCWTraceData> GCBulkRootCCW
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCBulkRootCCWTraceData(value, 36, 1, "GC", GCTaskGuid, 38, "BulkRootCCW", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 36, ProviderGuid);
            }
        }
        public event Action<GCBulkRCWTraceData> GCBulkRCW
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCBulkRCWTraceData(value, 37, 1, "GC", GCTaskGuid, 39, "BulkRCW", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 37, ProviderGuid);
            }
        }
        public event Action<GCBulkRootStaticVarTraceData> GCBulkRootStaticVar
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new GCBulkRootStaticVarTraceData(value, 38, 1, "GC", GCTaskGuid, 40, "BulkRootStaticVar", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 38, ProviderGuid);
            }
        }
        public event Action<IOThreadTraceData> IOThreadCreationStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new IOThreadTraceData(value, 44, 3, "IOThreadCreation", IOThreadCreationTaskGuid, 1, "Start", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 44, ProviderGuid);
                source.UnregisterEventTemplate(value, 1, IOThreadCreationTaskGuid);
            }
        }
        public event Action<IOThreadTraceData> IOThreadCreationStop
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new IOThreadTraceData(value, 45, 3, "IOThreadCreation", IOThreadCreationTaskGuid, 2, "Stop", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 45, ProviderGuid);
                source.UnregisterEventTemplate(value, 2, IOThreadCreationTaskGuid);
            }
        }
        public event Action<IOThreadTraceData> IOThreadRetirementStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new IOThreadTraceData(value, 46, 5, "IOThreadRetirement", IOThreadRetirementTaskGuid, 1, "Start", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 46, ProviderGuid);
                source.UnregisterEventTemplate(value, 1, IOThreadRetirementTaskGuid);
            }
        }
        public event Action<IOThreadTraceData> IOThreadRetirementStop
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new IOThreadTraceData(value, 47, 5, "IOThreadRetirement", IOThreadRetirementTaskGuid, 2, "Stop", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 47, ProviderGuid);
                source.UnregisterEventTemplate(value, 2, IOThreadRetirementTaskGuid);
            }
        }
        public event Action<ThreadPoolWorkerThreadTraceData> ThreadPoolWorkerThreadStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new ThreadPoolWorkerThreadTraceData(value, 50, 16, "ThreadPoolWorkerThread", ThreadPoolWorkerThreadTaskGuid, 1, "Start", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 50, ProviderGuid);
                source.UnregisterEventTemplate(value, 1, ThreadPoolWorkerThreadTaskGuid);
            }
        }
        public event Action<ThreadPoolWorkerThreadTraceData> ThreadPoolWorkerThreadStop
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new ThreadPoolWorkerThreadTraceData(value, 51, 16, "ThreadPoolWorkerThread", ThreadPoolWorkerThreadTaskGuid, 2, "Stop", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 51, ProviderGuid);
                source.UnregisterEventTemplate(value, 2, ThreadPoolWorkerThreadTaskGuid);
            }
        }
        public event Action<ThreadPoolWorkerThreadTraceData> ThreadPoolWorkerThreadWait
        {
            add
            {
                RegisterTemplate(new ThreadPoolWorkerThreadTraceData(value, 57, 16, "ThreadPoolWorkerThread", Guid.Empty, 90, "Wait", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 57, ProviderGuid);
            }
        }
        public event Action<ThreadPoolWorkerThreadTraceData> ThreadPoolWorkerThreadRetirementStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new ThreadPoolWorkerThreadTraceData(value, 52, 17, "ThreadPoolWorkerThreadRetirement", ThreadPoolWorkerThreadRetirementTaskGuid, 1, "Start", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 52, ProviderGuid);
                source.UnregisterEventTemplate(value, 1, ThreadPoolWorkerThreadRetirementTaskGuid);
            }
        }
        public event Action<ThreadPoolWorkerThreadTraceData> ThreadPoolWorkerThreadRetirementStop
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new ThreadPoolWorkerThreadTraceData(value, 53, 17, "ThreadPoolWorkerThreadRetirement", ThreadPoolWorkerThreadRetirementTaskGuid, 2, "Stop", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 53, ProviderGuid);
                source.UnregisterEventTemplate(value, 2, ThreadPoolWorkerThreadRetirementTaskGuid);
            }
        }
        public event Action<ThreadPoolWorkerThreadAdjustmentSampleTraceData> ThreadPoolWorkerThreadAdjustmentSample
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new ThreadPoolWorkerThreadAdjustmentSampleTraceData(value, 54, 18, "ThreadPoolWorkerThreadAdjustment", ThreadPoolWorkerThreadAdjustmentTaskGuid, 100, "Sample", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 54, ProviderGuid);
                source.UnregisterEventTemplate(value, 100, ThreadPoolWorkerThreadAdjustmentTaskGuid);
            }
        }
        public event Action<ThreadPoolWorkerThreadAdjustmentTraceData> ThreadPoolWorkerThreadAdjustmentAdjustment
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new ThreadPoolWorkerThreadAdjustmentTraceData(value, 55, 18, "ThreadPoolWorkerThreadAdjustment", ThreadPoolWorkerThreadAdjustmentTaskGuid, 101, "Adjustment", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 55, ProviderGuid);
                source.UnregisterEventTemplate(value, 101, ThreadPoolWorkerThreadAdjustmentTaskGuid);
            }
        }
        public event Action<ThreadPoolWorkerThreadAdjustmentStatsTraceData> ThreadPoolWorkerThreadAdjustmentStats
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new ThreadPoolWorkerThreadAdjustmentStatsTraceData(value, 56, 18, "ThreadPoolWorkerThreadAdjustment", ThreadPoolWorkerThreadAdjustmentTaskGuid, 102, "Stats", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 56, ProviderGuid);
                source.UnregisterEventTemplate(value, 102, ThreadPoolWorkerThreadAdjustmentTaskGuid);
            }
        }
        public event Action<ThreadPoolWorkingThreadCountTraceData> ThreadPoolWorkingThreadCountStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new ThreadPoolWorkingThreadCountTraceData(value, 60, 22, "ThreadPoolWorkingThreadCount", ThreadPoolWorkingThreadCountTaskGuid, 1, "Start", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 60, ProviderGuid);
                source.UnregisterEventTemplate(value, 1, ThreadPoolWorkingThreadCountTaskGuid);
            }
        }
        public event Action<ThreadPoolWorkTraceData> ThreadPoolEnqueue
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new ThreadPoolWorkTraceData(value, 61, 23, "ThreadPool", ThreadPoolTaskGuid, 11, "Enqueue", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 61, ProviderGuid);
                source.UnregisterEventTemplate(value, 11, ThreadPoolTaskGuid);
            }
        }
        public event Action<ThreadPoolWorkTraceData> ThreadPoolDequeue
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new ThreadPoolWorkTraceData(value, 62, 23, "ThreadPool", ThreadPoolTaskGuid, 12, "Dequeue", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 62, ProviderGuid);
                source.UnregisterEventTemplate(value, 12, ThreadPoolTaskGuid);
            }
        }
        public event Action<ThreadPoolIOWorkEnqueueTraceData> ThreadPoolIOEnqueue
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new ThreadPoolIOWorkEnqueueTraceData(value, 63, 23, "ThreadPool", ThreadPoolTaskGuid, 13, "IOEnqueue", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 63, ProviderGuid);
                source.UnregisterEventTemplate(value, 13, ThreadPoolTaskGuid);
            }
        }
        public event Action<ThreadPoolIOWorkTraceData> ThreadPoolIODequeue
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new ThreadPoolIOWorkTraceData(value, 64, 23, "ThreadPool", ThreadPoolTaskGuid, 14, "IODequeue", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 64, ProviderGuid);
                source.UnregisterEventTemplate(value, 14, ThreadPoolTaskGuid);
            }
        }
        public event Action<ThreadPoolIOWorkTraceData> ThreadPoolIOPack
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new ThreadPoolIOWorkTraceData(value, 65, 23, "ThreadPool", ThreadPoolTaskGuid, 15, "IOPack", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 65, ProviderGuid);
                source.UnregisterEventTemplate(value, 15, ThreadPoolTaskGuid);
            }
        }
        public event Action<ThreadStartWorkTraceData> ThreadCreating
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new ThreadStartWorkTraceData(value, 70, 24, "Thread", ThreadTaskGuid, 11, "Creating", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 70, ProviderGuid);
                source.UnregisterEventTemplate(value, 11, ThreadTaskGuid);
            }
        }
        public event Action<ThreadStartWorkTraceData> ThreadRunning
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new ThreadStartWorkTraceData(value, 71, 24, "Thread", ThreadTaskGuid, 12, "Running", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 71, ProviderGuid);
                source.UnregisterEventTemplate(value, 12, ThreadTaskGuid);
            }
        }
        public event Action<ExceptionHandlingTraceData> ExceptionCatchStart
        {
            add
            {
                RegisterTemplate(ExceptionCatchStartTemplate(value));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 250, ProviderGuid);
            }
        }
        public event Action<EmptyTraceData> ExceptionCatchStop
        {
            add
            {
                RegisterTemplate(ExceptionCatchStopTemplate(value));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 251, ProviderGuid);
            }
        }
        public event Action<ExceptionHandlingTraceData> ExceptionFilterStart
        {
            add
            {
                RegisterTemplate(ExceptionFilterStartTemplate(value));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 254, ProviderGuid);
            }
        }
        public event Action<EmptyTraceData> ExceptionFilterStop
        {
            add
            {
                RegisterTemplate(ExceptionFilterStopTemplate(value));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 255, ProviderGuid);
            }
        }
        public event Action<ExceptionHandlingTraceData> ExceptionFinallyStart
        {
            add
            {
                RegisterTemplate(ExceptionFinallyStartTemplate(value));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 252, ProviderGuid);
            }
        }
        public event Action<EmptyTraceData> ExceptionFinallyStop
        {
            add
            {
                RegisterTemplate(ExceptionFinallyStopTemplate(value));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 253, ProviderGuid);
            }
        }
        public event Action<ExceptionTraceData> ExceptionStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new ExceptionTraceData(value, 80, 7, "Exception", ExceptionTaskGuid, 1, "Start", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 80, ProviderGuid);
                source.UnregisterEventTemplate(value, 1, ExceptionTaskGuid);
            }
        }
        public event Action<EmptyTraceData> ExceptionStop
        {
            add
            {
                RegisterTemplate(ExceptionStopTemplate(value));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 256, ProviderGuid);
            }
        }

        public event Action<ContentionStartTraceData> ContentionStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new ContentionStartTraceData(value, 81, 8, "Contention", ContentionTaskGuid, 1, "Start", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 81, ProviderGuid);
                source.UnregisterEventTemplate(value, 1, ContentionTaskGuid);
            }
        }
        public event Action<MethodILToNativeMapTraceData> MethodILToNativeMap
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new MethodILToNativeMapTraceData(value, 190, 9, "Method", MethodTaskGuid, 87, "ILToNativeMap", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 190, ProviderGuid);
                source.UnregisterEventTemplate(value, 87, MethodTaskGuid);
            }
        }
        public event Action<ClrStackWalkTraceData> ClrStackWalk
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new ClrStackWalkTraceData(value, 82, 11, "ClrStack", ClrStackTaskGuid, 82, "Walk", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 82, ProviderGuid);
                source.UnregisterEventTemplate(value, 82, ClrStackTaskGuid);
            }
        }
        public event Action<CodeSymbolsTraceData> CodeSymbolsStart
        {
            add
            {
                RegisterTemplate(CodeSymbolsStartTemplate(value));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 260, ProviderGuid);
                source.UnregisterEventTemplate(value, 1, CodeSymbolsTaskGuid);
            }
        }
        public event Action<AppDomainMemAllocatedTraceData> AppDomainResourceManagementMemAllocated
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new AppDomainMemAllocatedTraceData(value, 83, 14, "AppDomainResourceManagement", AppDomainResourceManagementTaskGuid, 48, "MemAllocated", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 83, ProviderGuid);
                source.UnregisterEventTemplate(value, 48, AppDomainResourceManagementTaskGuid);
            }
        }
        public event Action<AppDomainMemSurvivedTraceData> AppDomainResourceManagementMemSurvived
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new AppDomainMemSurvivedTraceData(value, 84, 14, "AppDomainResourceManagement", AppDomainResourceManagementTaskGuid, 49, "MemSurvived", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 84, ProviderGuid);
                source.UnregisterEventTemplate(value, 49, AppDomainResourceManagementTaskGuid);
            }
        }
        public event Action<ThreadCreatedTraceData> AppDomainResourceManagementThreadCreated
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new ThreadCreatedTraceData(value, 85, 14, "AppDomainResourceManagement", AppDomainResourceManagementTaskGuid, 50, "ThreadCreated", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 85, ProviderGuid);
                source.UnregisterEventTemplate(value, 50, AppDomainResourceManagementTaskGuid);
            }
        }
        public event Action<EventSourceTraceData> EventSourceEvent
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new EventSourceTraceData(value, 270, 0, "EventSourceEvent", Guid.Empty, 0, "", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 270, ProviderGuid);
            }
        }
        public event Action<ThreadTerminatedOrTransitionTraceData> AppDomainResourceManagementThreadTerminated
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new ThreadTerminatedOrTransitionTraceData(value, 86, 14, "AppDomainResourceManagement", AppDomainResourceManagementTaskGuid, 51, "ThreadTerminated", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 86, ProviderGuid);
                source.UnregisterEventTemplate(value, 51, AppDomainResourceManagementTaskGuid);
            }
        }
        public event Action<ThreadTerminatedOrTransitionTraceData> AppDomainResourceManagementDomainEnter
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new ThreadTerminatedOrTransitionTraceData(value, 87, 14, "AppDomainResourceManagement", AppDomainResourceManagementTaskGuid, 52, "DomainEnter", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 87, ProviderGuid);
                source.UnregisterEventTemplate(value, 52, AppDomainResourceManagementTaskGuid);
            }
        }
        public event Action<AppDomainAssemblyResolveHandlerInvokedTraceData> AssemblyLoaderAppDomainAssemblyResolveHandlerInvoked
        {
            add
            {
                RegisterTemplate(new AppDomainAssemblyResolveHandlerInvokedTraceData(value, 294, 32, "AssemblyLoader", AssemblyLoaderTaskGuid, 13, "AppDomainAssemblyResolveHandlerInvoked", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 294, AssemblyLoaderTaskGuid);
            }
        }
        public event Action<AssemblyLoadContextResolvingHandlerInvokedTraceData> AssemblyLoaderAssemblyLoadContextResolvingHandlerInvoked
        {
            add
            {
                RegisterTemplate(new AssemblyLoadContextResolvingHandlerInvokedTraceData(value, 293, 32, "AssemblyLoader", AssemblyLoaderTaskGuid, 12, "AssemblyLoadContextResolvingHandlerInvoked", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 293, AssemblyLoaderTaskGuid);
            }
        }
        public event Action<AssemblyLoadFromResolveHandlerInvokedTraceData> AssemblyLoaderAssemblyLoadFromResolveHandlerInvoked
        {
            add
            {
                RegisterTemplate(new AssemblyLoadFromResolveHandlerInvokedTraceData(value, 295, 32, "AssemblyLoader", AssemblyLoaderTaskGuid, 14, "AssemblyLoadFromResolveHandlerInvoked", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 295, AssemblyLoaderTaskGuid);
            }
        }
        public event Action<KnownPathProbedTraceData> AssemblyLoaderKnownPathProbed
        {
            add
            {
                RegisterTemplate(new KnownPathProbedTraceData(value, 296, 32, "AssemblyLoader", AssemblyLoaderTaskGuid, 15, "KnownPathProbed", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 296, AssemblyLoaderTaskGuid);
            }
        }
        public event Action<ResolutionAttemptedTraceData> AssemblyLoaderResolutionAttempted
        {
            add
            {
                RegisterTemplate(new ResolutionAttemptedTraceData(value, 292, 32, "AssemblyLoader", AssemblyLoaderTaskGuid, 11, "ResolutionAttempted", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 292, AssemblyLoaderTaskGuid);
            }
        }
        public event Action<AssemblyLoadStartTraceData> AssemblyLoaderStart
        {
            add
            {
                RegisterTemplate(new AssemblyLoadStartTraceData(value, 290, 32, "AssemblyLoader", AssemblyLoaderTaskGuid, 1, "Start", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 290, AssemblyLoaderTaskGuid);
            }
        }
        public event Action<AssemblyLoadStopTraceData> AssemblyLoaderStop
        {
            add
            {
                RegisterTemplate(new AssemblyLoadStopTraceData(value, 291, 32, "AssemblyLoader", AssemblyLoaderTaskGuid, 2, "Stop", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 291, AssemblyLoaderTaskGuid);
            }
        }
        public event Action<ILStubGeneratedTraceData> ILStubStubGenerated
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new ILStubGeneratedTraceData(value, 88, 15, "ILStub", ILStubTaskGuid, 88, "StubGenerated", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 88, ProviderGuid);
                source.UnregisterEventTemplate(value, 88, ILStubTaskGuid);
            }
        }
        public event Action<ILStubCacheHitTraceData> ILStubStubCacheHit
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new ILStubCacheHitTraceData(value, 89, 15, "ILStub", ILStubTaskGuid, 89, "StubCacheHit", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 89, ProviderGuid);
                source.UnregisterEventTemplate(value, 89, ILStubTaskGuid);
            }
        }
        public event Action<ContentionStopTraceData> ContentionStop
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new ContentionStopTraceData(value, 91, 8, "Contention", ContentionTaskGuid, 2, "Stop", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 91, ProviderGuid);
                source.UnregisterEventTemplate(value, 2, ContentionTaskGuid);
            }
        }
        public event Action<EmptyTraceData> MethodDCStartCompleteV2
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new EmptyTraceData(value, 135, 9, "Method", MethodTaskGuid, 14, "DCStartCompleteV2", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 135, ProviderGuid);
                source.UnregisterEventTemplate(value, 14, MethodTaskGuid);
            }
        }
        public event Action<EmptyTraceData> MethodDCStopCompleteV2
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new EmptyTraceData(value, 136, 9, "Method", MethodTaskGuid, 15, "DCStopCompleteV2", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 136, ProviderGuid);
                source.UnregisterEventTemplate(value, 15, MethodTaskGuid);
            }
        }
        public event Action<MethodLoadUnloadTraceData> MethodDCStartV2
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new MethodLoadUnloadTraceData(value, 137, 9, "Method", MethodTaskGuid, 35, "DCStartV2", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 137, ProviderGuid);
                source.UnregisterEventTemplate(value, 35, MethodTaskGuid);
            }
        }
        public event Action<MethodLoadUnloadTraceData> MethodDCStopV2
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new MethodLoadUnloadTraceData(value, 138, 9, "Method", MethodTaskGuid, 36, "DCStopV2", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 138, ProviderGuid);
                source.UnregisterEventTemplate(value, 36, MethodTaskGuid);
            }
        }
        public event Action<MethodLoadUnloadVerboseTraceData> MethodDCStartVerboseV2
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new MethodLoadUnloadVerboseTraceData(value, 139, 9, "Method", MethodTaskGuid, 39, "DCStartVerboseV2", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 139, ProviderGuid);
                source.UnregisterEventTemplate(value, 39, MethodTaskGuid);
            }
        }
        public event Action<MethodLoadUnloadVerboseTraceData> MethodDCStopVerboseV2
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new MethodLoadUnloadVerboseTraceData(value, 140, 9, "Method", MethodTaskGuid, 40, "DCStopVerboseV2", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 140, ProviderGuid);
                source.UnregisterEventTemplate(value, 40, MethodTaskGuid);
            }
        }
        public event Action<MethodLoadUnloadTraceData> MethodLoad
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new MethodLoadUnloadTraceData(value, 141, 9, "Method", MethodTaskGuid, 33, "Load", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 141, ProviderGuid);
                source.UnregisterEventTemplate(value, 33, MethodTaskGuid);
            }
        }
        public event Action<MethodLoadUnloadTraceData> MethodUnload
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new MethodLoadUnloadTraceData(value, 142, 9, "Method", MethodTaskGuid, 34, "Unload", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 142, ProviderGuid);
                source.UnregisterEventTemplate(value, 34, MethodTaskGuid);
            }
        }
        public event Action<MethodLoadUnloadVerboseTraceData> MethodLoadVerbose
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new MethodLoadUnloadVerboseTraceData(value, 143, 9, "Method", MethodTaskGuid, 37, "LoadVerbose", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 143, ProviderGuid);
                source.UnregisterEventTemplate(value, 37, MethodTaskGuid);
            }
        }
        public event Action<MethodLoadUnloadVerboseTraceData> MethodUnloadVerbose
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new MethodLoadUnloadVerboseTraceData(value, 144, 9, "Method", MethodTaskGuid, 38, "UnloadVerbose", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 144, ProviderGuid);
                source.UnregisterEventTemplate(value, 38, MethodTaskGuid);
            }
        }
        public event Action<MethodJitMemoryAllocatedForCodeTraceData> MethodMemoryAllocatedForJitCode
        {
            add
            {
                RegisterTemplate(new MethodJitMemoryAllocatedForCodeTraceData(value, 146, 9, "Method", MethodTaskGuid, 103, "MemoryAllocatedForJitCode", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 146, MethodTaskGuid);
            }
        }
        public event Action<R2RGetEntryPointTraceData> MethodR2RGetEntryPoint
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new R2RGetEntryPointTraceData(value, 159, 9, "Method", MethodTaskGuid, 33, "R2RGetEntryPoint", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 159, ProviderGuid);
                source.UnregisterEventTemplate(value, 33, MethodTaskGuid);
            }
        }

        public event Action<R2RGetEntryPointStartTraceData> MethodR2RGetEntryPointStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(R2RGetEntryPointStartTemplate(value));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 160, ProviderGuid);
                source.UnregisterEventTemplate(value, 33, MethodTaskGuid);
            }
        }

        public event Action<MethodJittingStartedTraceData> MethodJittingStarted
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new MethodJittingStartedTraceData(value, 145, 9, "Method", MethodTaskGuid, 42, "JittingStarted", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 145, ProviderGuid);
                source.UnregisterEventTemplate(value, 42, MethodTaskGuid);
            }
        }

        public event Action<TypeLoadStartTraceData> TypeLoadStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(TypeLoadStartTemplate(value));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 73, ProviderGuid);
                source.UnregisterEventTemplate(value, 1, LoaderTaskGuid);
            }
        }

        public event Action<TypeLoadStopTraceData> TypeLoadStop
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(TypeLoadStopTemplate(value));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 74, ProviderGuid);
                source.UnregisterEventTemplate(value, 2, LoaderTaskGuid);
            }
        }

        public event Action<ModuleLoadUnloadTraceData> LoaderModuleDCStartV2
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new ModuleLoadUnloadTraceData(value, 149, 10, "Loader", LoaderTaskGuid, 35, "ModuleDCStartV2", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 149, ProviderGuid);
                source.UnregisterEventTemplate(value, 35, LoaderTaskGuid);
            }
        }
        public event Action<ModuleLoadUnloadTraceData> LoaderModuleDCStopV2
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new ModuleLoadUnloadTraceData(value, 150, 10, "Loader", LoaderTaskGuid, 36, "ModuleDCStopV2", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 150, ProviderGuid);
                source.UnregisterEventTemplate(value, 36, LoaderTaskGuid);
            }
        }
        public event Action<DomainModuleLoadUnloadTraceData> LoaderDomainModuleLoad
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new DomainModuleLoadUnloadTraceData(value, 151, 10, "Loader", LoaderTaskGuid, 45, "DomainModuleLoad", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 151, ProviderGuid);
                source.UnregisterEventTemplate(value, 45, LoaderTaskGuid);
            }
        }
        public event Action<ModuleLoadUnloadTraceData> LoaderModuleLoad
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new ModuleLoadUnloadTraceData(value, 152, 10, "Loader", LoaderTaskGuid, 33, "ModuleLoad", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 152, ProviderGuid);
                source.UnregisterEventTemplate(value, 33, LoaderTaskGuid);
            }
        }
        public event Action<ModuleLoadUnloadTraceData> LoaderModuleUnload
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new ModuleLoadUnloadTraceData(value, 153, 10, "Loader", LoaderTaskGuid, 34, "ModuleUnload", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 153, ProviderGuid);
                source.UnregisterEventTemplate(value, 34, LoaderTaskGuid);
            }
        }
        public event Action<AssemblyLoadUnloadTraceData> LoaderAssemblyLoad
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new AssemblyLoadUnloadTraceData(value, 154, 10, "Loader", LoaderTaskGuid, 37, "AssemblyLoad", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 154, ProviderGuid);
                source.UnregisterEventTemplate(value, 37, LoaderTaskGuid);
            }
        }
        public event Action<AssemblyLoadUnloadTraceData> LoaderAssemblyUnload
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new AssemblyLoadUnloadTraceData(value, 155, 10, "Loader", LoaderTaskGuid, 38, "AssemblyUnload", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 155, ProviderGuid);
                source.UnregisterEventTemplate(value, 38, LoaderTaskGuid);
            }
        }
        public event Action<AppDomainLoadUnloadTraceData> LoaderAppDomainLoad
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new AppDomainLoadUnloadTraceData(value, 156, 10, "Loader", LoaderTaskGuid, 41, "AppDomainLoad", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 156, ProviderGuid);
                source.UnregisterEventTemplate(value, 41, LoaderTaskGuid);
            }
        }
        public event Action<AppDomainLoadUnloadTraceData> LoaderAppDomainUnload
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new AppDomainLoadUnloadTraceData(value, 157, 10, "Loader", LoaderTaskGuid, 42, "AppDomainUnload", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 157, ProviderGuid);
                source.UnregisterEventTemplate(value, 42, LoaderTaskGuid);
            }
        }
        public event Action<StrongNameVerificationTraceData> StrongNameVerificationStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new StrongNameVerificationTraceData(value, 181, 12, "StrongNameVerification", StrongNameVerificationTaskGuid, 1, "Start", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 181, ProviderGuid);
                source.UnregisterEventTemplate(value, 1, StrongNameVerificationTaskGuid);
            }
        }
        public event Action<StrongNameVerificationTraceData> StrongNameVerificationStop
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new StrongNameVerificationTraceData(value, 182, 12, "StrongNameVerification", StrongNameVerificationTaskGuid, 2, "Stop", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 182, ProviderGuid);
                source.UnregisterEventTemplate(value, 2, StrongNameVerificationTaskGuid);
            }
        }
        public event Action<AuthenticodeVerificationTraceData> AuthenticodeVerificationStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new AuthenticodeVerificationTraceData(value, 183, 13, "AuthenticodeVerification", AuthenticodeVerificationTaskGuid, 1, "Start", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 183, ProviderGuid);
                source.UnregisterEventTemplate(value, 1, AuthenticodeVerificationTaskGuid);
            }
        }
        public event Action<AuthenticodeVerificationTraceData> AuthenticodeVerificationStop
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new AuthenticodeVerificationTraceData(value, 184, 13, "AuthenticodeVerification", AuthenticodeVerificationTaskGuid, 2, "Stop", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 184, ProviderGuid);
                source.UnregisterEventTemplate(value, 2, AuthenticodeVerificationTaskGuid);
            }
        }
        public event Action<MethodJitInliningSucceededTraceData> MethodInliningSucceeded
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new MethodJitInliningSucceededTraceData(value, 185, 9, "Method", MethodTaskGuid, 83, "InliningSucceeded", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 185, ProviderGuid);
                source.UnregisterEventTemplate(value, 83, MethodTaskGuid);
            }
        }
        public event Action<MethodJitInliningFailedTraceData> MethodInliningFailed
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new MethodJitInliningFailedTraceData(value, 192, 9, "Method", MethodTaskGuid, 84, "InliningFailed", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 192, ProviderGuid);
            }
        }
        public event Action<MethodJitInliningFailedAnsiTraceData> MethodInliningFailedAnsi
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new MethodJitInliningFailedAnsiTraceData(value, 186, 9, "Method", MethodTaskGuid, 84, "InliningFailedAnsi", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 186, ProviderGuid);
                source.UnregisterEventTemplate(value, 84, MethodTaskGuid);
            }
        }
        public event Action<RuntimeInformationTraceData> RuntimeStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new RuntimeInformationTraceData(value, 187, 19, "Runtime", RuntimeTaskGuid, 1, "Start", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 187, ProviderGuid);
                source.UnregisterEventTemplate(value, 1, RuntimeTaskGuid);
            }
        }
        public event Action<MethodJitTailCallSucceededTraceData> MethodTailCallSucceeded
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new MethodJitTailCallSucceededTraceData(value, 188, 9, "Method", MethodTaskGuid, 85, "TailCallSucceeded", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 188, ProviderGuid);
                source.UnregisterEventTemplate(value, 85, MethodTaskGuid);
            }
        }
        public event Action<MethodJitTailCallFailedAnsiTraceData> MethodTailCallFailedAnsi
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new MethodJitTailCallFailedAnsiTraceData(value, 189, 9, "Method", MethodTaskGuid, 86, "TailCallFailedAnsi", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 189, ProviderGuid);
                source.UnregisterEventTemplate(value, 86, MethodTaskGuid);
            }
        }
        public event Action<MethodJitTailCallFailedTraceData> MethodTailCallFailed
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(new MethodJitTailCallFailedTraceData(value, 191, 9, "Method", MethodTaskGuid, 86, "TailCallFailed", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 191, ProviderGuid);
            }
        }
        public event Action<TieredCompilationSettingsTraceData> TieredCompilationSettings
        {
            add
            {
                RegisterTemplate(TieredCompilationSettingsTemplate(value));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 280, ProviderGuid);
                source.UnregisterEventTemplate(value, 11, TieredCompilationTaskGuid);
            }
        }
        public event Action<TieredCompilationEmptyTraceData> TieredCompilationPause
        {
            add
            {
                RegisterTemplate(TieredCompilationPauseTemplate(value));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 281, ProviderGuid);
                source.UnregisterEventTemplate(value, 12, TieredCompilationTaskGuid);
            }
        }
        public event Action<TieredCompilationResumeTraceData> TieredCompilationResume
        {
            add
            {
                RegisterTemplate(TieredCompilationResumeTemplate(value));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 282, ProviderGuid);
                source.UnregisterEventTemplate(value, 13, TieredCompilationTaskGuid);
            }
        }
        public event Action<TieredCompilationBackgroundJitStartTraceData> TieredCompilationBackgroundJitStart
        {
            add
            {
                RegisterTemplate(TieredCompilationBackgroundJitStartTemplate(value));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 283, ProviderGuid);
                source.UnregisterEventTemplate(value, 14, TieredCompilationTaskGuid);
            }
        }
        public event Action<TieredCompilationBackgroundJitStopTraceData> TieredCompilationBackgroundJitStop
        {
            add
            {
                RegisterTemplate(TieredCompilationBackgroundJitStopTemplate(value));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 284, ProviderGuid);
                source.UnregisterEventTemplate(value, 15, TieredCompilationTaskGuid);
            }
        }
        public event Action<JitInstrumentationDataTraceData> JitInstrumentationDataInstrumentationData
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(JitInstrumentationDataTemplate(value));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 297, ProviderGuid);
                source.UnregisterEventTemplate(value, 11, JitInstrumentationDataTaskGuid);
            }
        }
        public event Action<JitInstrumentationDataVerboseTraceData> JitInstrumentationDataVerboseInstrumentationData
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                RegisterTemplate(JitInstrumentationDataVerboseTemplate(value));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 298, ProviderGuid);
                source.UnregisterEventTemplate(value, 12, JitInstrumentationDataTaskGuid);
            }
        }

        #region private
        protected override string GetProviderName() { return ProviderName; }

        static private CodeSymbolsTraceData CodeSymbolsStartTemplate(Action<CodeSymbolsTraceData> action)
        {                  // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
            return new CodeSymbolsTraceData(action, 260, 30, "CodeSymbols", Guid.Empty, 1, "Start", ProviderGuid, ProviderName);
        }
        static private ExceptionHandlingTraceData ExceptionCatchStartTemplate(Action<ExceptionHandlingTraceData> action)
        {                  // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
            return new ExceptionHandlingTraceData(action, 250, 27, "ExceptionCatch", Guid.Empty, 1, "Start", ProviderGuid, ProviderName);
        }
        static private EmptyTraceData ExceptionCatchStopTemplate(Action<EmptyTraceData> action)
        {                  // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
            return new EmptyTraceData(action, 251, 27, "ExceptionCatch", Guid.Empty, 2, "Stop", ProviderGuid, ProviderName);
        }
        static private ExceptionHandlingTraceData ExceptionFilterStartTemplate(Action<ExceptionHandlingTraceData> action)
        {                  // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
            return new ExceptionHandlingTraceData(action, 254, 29, "ExceptionFilter", Guid.Empty, 1, "Start", ProviderGuid, ProviderName);
        }
        static private EmptyTraceData ExceptionFilterStopTemplate(Action<EmptyTraceData> action)
        {                  // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
            return new EmptyTraceData(action, 255, 29, "ExceptionFilter", Guid.Empty, 2, "Stop", ProviderGuid, ProviderName);
        }
        static private ExceptionHandlingTraceData ExceptionFinallyStartTemplate(Action<ExceptionHandlingTraceData> action)
        {                  // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
            return new ExceptionHandlingTraceData(action, 252, 28, "ExceptionFinally", Guid.Empty, 1, "Start", ProviderGuid, ProviderName);
        }
        static private EmptyTraceData ExceptionFinallyStopTemplate(Action<EmptyTraceData> action)
        {                  // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
            return new EmptyTraceData(action, 253, 28, "ExceptionFinally", Guid.Empty, 2, "Stop", ProviderGuid, ProviderName);
        }
        static private EmptyTraceData ExceptionStopTemplate(Action<EmptyTraceData> action)
        {                  // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
            return new EmptyTraceData(action, 256, 7, "Exception", Guid.Empty, 2, "Stop", ProviderGuid, ProviderName);
        }
        static private AppDomainAssemblyResolveHandlerInvokedTraceData AssemblyLoaderAppDomainAssemblyResolveHandlerInvokedTemplate(Action<AppDomainAssemblyResolveHandlerInvokedTraceData> action)
        {                  // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
            return new AppDomainAssemblyResolveHandlerInvokedTraceData(action, 294, 32, "AssemblyLoader", Guid.Empty, 13, "AppDomainAssemblyResolveHandlerInvoked", ProviderGuid, ProviderName);
        }
        static private AssemblyLoadContextResolvingHandlerInvokedTraceData AssemblyLoaderAssemblyLoadContextResolvingHandlerInvokedTemplate(Action<AssemblyLoadContextResolvingHandlerInvokedTraceData> action)
        {                  // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
            return new AssemblyLoadContextResolvingHandlerInvokedTraceData(action, 293, 32, "AssemblyLoader", Guid.Empty, 12, "AssemblyLoadContextResolvingHandlerInvoked", ProviderGuid, ProviderName);
        }
        static private AssemblyLoadFromResolveHandlerInvokedTraceData AssemblyLoaderAssemblyLoadFromResolveHandlerInvokedTemplate(Action<AssemblyLoadFromResolveHandlerInvokedTraceData> action)
        {                  // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
            return new AssemblyLoadFromResolveHandlerInvokedTraceData(action, 295, 32, "AssemblyLoader", Guid.Empty, 14, "AssemblyLoadFromResolveHandlerInvoked", ProviderGuid, ProviderName);
        }
        static private KnownPathProbedTraceData AssemblyLoaderKnownPathProbedTemplate(Action<KnownPathProbedTraceData> action)
        {                  // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
            return new KnownPathProbedTraceData(action, 296, 32, "AssemblyLoader", Guid.Empty, 15, "KnownPathProbed", ProviderGuid, ProviderName);
        }
        static private ResolutionAttemptedTraceData AssemblyLoaderResolutionAttemptedTemplate(Action<ResolutionAttemptedTraceData> action)
        {                  // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
            return new ResolutionAttemptedTraceData(action, 292, 32, "AssemblyLoader", Guid.Empty, 11, "ResolutionAttempted", ProviderGuid, ProviderName);
        }
        static private AssemblyLoadStartTraceData AssemblyLoaderStartTemplate(Action<AssemblyLoadStartTraceData> action)
        {                  // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
            return new AssemblyLoadStartTraceData(action, 290, 32, "AssemblyLoader", Guid.Empty, 1, "Start", ProviderGuid, ProviderName);
        }
        static private AssemblyLoadStopTraceData AssemblyLoaderStopTemplate(Action<AssemblyLoadStopTraceData> action)
        {                  // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
            return new AssemblyLoadStopTraceData(action, 291, 32, "AssemblyLoader", Guid.Empty, 2, "Stop", ProviderGuid, ProviderName);
        }
        static private TieredCompilationSettingsTraceData TieredCompilationSettingsTemplate(Action<TieredCompilationSettingsTraceData> action)
        {                  // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
            return new TieredCompilationSettingsTraceData(action, 280, 31, "TieredCompilation", TieredCompilationTaskGuid, 11, "Settings", ProviderGuid, ProviderName);
        }
        static private TieredCompilationEmptyTraceData TieredCompilationPauseTemplate(Action<TieredCompilationEmptyTraceData> action)
        {                  // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
            return new TieredCompilationEmptyTraceData(action, 281, 31, "TieredCompilation", TieredCompilationTaskGuid, 12, "Pause", ProviderGuid, ProviderName);
        }
        static private TieredCompilationResumeTraceData TieredCompilationResumeTemplate(Action<TieredCompilationResumeTraceData> action)
        {                  // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
            return new TieredCompilationResumeTraceData(action, 282, 31, "TieredCompilation", TieredCompilationTaskGuid, 13, "Resume", ProviderGuid, ProviderName);
        }
        static private TieredCompilationBackgroundJitStartTraceData TieredCompilationBackgroundJitStartTemplate(Action<TieredCompilationBackgroundJitStartTraceData> action)
        {                  // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
            return new TieredCompilationBackgroundJitStartTraceData(action, 283, 31, "TieredCompilation", TieredCompilationTaskGuid, 1, "BackgroundJitStart", ProviderGuid, ProviderName);
        }
        static private TieredCompilationBackgroundJitStopTraceData TieredCompilationBackgroundJitStopTemplate(Action<TieredCompilationBackgroundJitStopTraceData> action)
        {                  // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
            return new TieredCompilationBackgroundJitStopTraceData(action, 284, 31, "TieredCompilation", TieredCompilationTaskGuid, 2, "BackgroundJitStop", ProviderGuid, ProviderName);
        }
        static private MethodJitMemoryAllocatedForCodeTraceData MethodMemoryAllocatedForJitCodeTemplate(Action<MethodJitMemoryAllocatedForCodeTraceData> action)
        {                  // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
            return new MethodJitMemoryAllocatedForCodeTraceData(action, 146, 9, "Method", Guid.Empty, 103, "MemoryAllocatedForJitCode", ProviderGuid, ProviderName);
        }
        static private GenAwareBeginTraceData GenAwareBeginTemplate(Action<GenAwareBeginTraceData> action)
        {                  // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
            return new GenAwareBeginTraceData(action, 206, 1, "GC", GCTaskGuid, 1, "GenAwareStart", ProviderGuid, ProviderName);
        }
        static private GenAwareEndTraceData GenAwareEndTemplate(Action<GenAwareEndTraceData> action)
        {                  // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
            return new GenAwareEndTraceData(action, 207, 1, "GC", GCTaskGuid, 2, "GenAwareEnd", ProviderGuid, ProviderName);
        }
        static private R2RGetEntryPointStartTraceData R2RGetEntryPointStartTemplate(Action<R2RGetEntryPointStartTraceData> action)
        {                  // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
            return new R2RGetEntryPointStartTraceData(action, 160, 9, "Method", MethodTaskGuid, 33, "R2RGetEntryPointStart", ProviderGuid, ProviderName);
        }
        static private TypeLoadStartTraceData TypeLoadStartTemplate(Action<TypeLoadStartTraceData> action)
        {                  // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
            return new TypeLoadStartTraceData(action, 73, 33, "TypeLoad", LoaderTaskGuid, 1, "Start", ProviderGuid, ProviderName);
        }
        static private TypeLoadStopTraceData TypeLoadStopTemplate(Action<TypeLoadStopTraceData> action)
        {                  // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
            return new TypeLoadStopTraceData(action, 74, 33, "TypeLoad", LoaderTaskGuid, 2, "Stop", ProviderGuid, ProviderName);
        }
        static private JitInstrumentationDataTraceData JitInstrumentationDataTemplate(Action<JitInstrumentationDataTraceData> action)
        {                  // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
            return new JitInstrumentationDataTraceData(action, 297, 34, "JitInstrumentationData", JitInstrumentationDataTaskGuid, 11, "InstrumentationData", ProviderGuid, ProviderName);
        }
        static private JitInstrumentationDataVerboseTraceData JitInstrumentationDataVerboseTemplate(Action<JitInstrumentationDataVerboseTraceData> action)
        {                  // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
            return new JitInstrumentationDataVerboseTraceData(action, 298, 34, "JitInstrumentationDataVerbose", JitInstrumentationDataTaskGuid, 12, "InstrumentationData", ProviderGuid, ProviderName);
        }

        static private volatile TraceEvent[] s_templates;
        protected internal override void EnumerateTemplates(Func<string, string, EventFilterResponse> eventsToObserve, Action<TraceEvent> callback)
        {
            if (s_templates == null)
            {
                var templates = new TraceEvent[139];
                templates[0] = new GCStartTraceData(null, 1, 1, "GC", GCTaskGuid, 1, "Start", ProviderGuid, ProviderName);
                templates[1] = new GCEndTraceData(null, 2, 1, "GC", GCTaskGuid, 2, "Stop", ProviderGuid, ProviderName);
                templates[2] = new GCNoUserDataTraceData(null, 3, 1, "GC", GCTaskGuid, 132, "RestartEEStop", ProviderGuid, ProviderName);
                templates[3] = new GCNoUserDataTraceData(null, 0xFFFF, 1, "GC", GCTaskGuid, 8, "RestartEEStop", ProviderGuid, ProviderName);
                templates[4] = new GCHeapStatsTraceData(null, 4, 1, "GC", GCTaskGuid, 133, "HeapStats", ProviderGuid, ProviderName);
                templates[5] = new GCHeapStatsTraceData(null, 0xFFFF, 1, "GC", GCTaskGuid, 5, "HeapStats", ProviderGuid, ProviderName);
                templates[6] = new GCCreateSegmentTraceData(null, 5, 1, "GC", GCTaskGuid, 134, "CreateSegment", ProviderGuid, ProviderName);
                templates[7] = new GCCreateSegmentTraceData(null, 0xFFFF, 1, "GC", GCTaskGuid, 6, "CreateSegment", ProviderGuid, ProviderName);
                templates[8] = new GCFreeSegmentTraceData(null, 6, 1, "GC", GCTaskGuid, 135, "FreeSegment", ProviderGuid, ProviderName);
                templates[9] = new GCFreeSegmentTraceData(null, 0xFFFF, 1, "GC", GCTaskGuid, 7, "FreeSegment", ProviderGuid, ProviderName);
                templates[10] = new GCNoUserDataTraceData(null, 7, 1, "GC", GCTaskGuid, 136, "RestartEEStart", ProviderGuid, ProviderName);
                templates[11] = new GCNoUserDataTraceData(null, 8, 1, "GC", GCTaskGuid, 137, "SuspendEEStop", ProviderGuid, ProviderName);
                templates[12] = new GCSuspendEETraceData(null, 9, 1, "GC", GCTaskGuid, 10, "SuspendEEStart", ProviderGuid, ProviderName);
                templates[13] = new GCAllocationTickTraceData(null, 10, 1, "GC", GCTaskGuid, 11, "AllocationTick", ProviderGuid, ProviderName);
                templates[14] = new GCCreateConcurrentThreadTraceData(null, 11, 1, "GC", GCTaskGuid, 12, "CreateConcurrentThread", ProviderGuid, ProviderName);
                templates[15] = new GCTerminateConcurrentThreadTraceData(null, 12, 1, "GC", GCTaskGuid, 13, "TerminateConcurrentThread", ProviderGuid, ProviderName);
                templates[16] = new GCFinalizersEndTraceData(null, 13, 1, "GC", GCTaskGuid, 15, "FinalizersStop", ProviderGuid, ProviderName);
                templates[17] = new GCNoUserDataTraceData(null, 14, 1, "GC", GCTaskGuid, 19, "FinalizersStart", ProviderGuid, ProviderName);
                templates[18] = new GCBulkTypeTraceData(null, 15, 21, "Type", TypeTaskGuid, 10, "BulkType", ProviderGuid, ProviderName);
                templates[19] = new GCBulkRootEdgeTraceData(null, 16, 1, "GC", GCTaskGuid, 20, "BulkRootEdge", ProviderGuid, ProviderName);
                templates[20] = new GCBulkRootConditionalWeakTableElementEdgeTraceData(null, 17, 1, "GC", GCTaskGuid, 21, "BulkRootConditionalWeakTableElementEdge", ProviderGuid, ProviderName);
                templates[21] = new GCBulkNodeTraceData(null, 18, 1, "GC", GCTaskGuid, 22, "BulkNode", ProviderGuid, ProviderName);
                templates[22] = new GCBulkEdgeTraceData(null, 19, 1, "GC", GCTaskGuid, 23, "BulkEdge", ProviderGuid, ProviderName);
                templates[23] = new GCSampledObjectAllocationTraceData(null, 20, 1, "GC", GCTaskGuid, 24, "SampledObjectAllocation", ProviderGuid, ProviderName);
                templates[24] = new GCSampledObjectAllocationTraceData(null, 32, 1, "GC", GCTaskGuid, 24, "SampledObjectAllocation", ProviderGuid, ProviderName);
                templates[25] = new GCBulkSurvivingObjectRangesTraceData(null, 21, 1, "GC", GCTaskGuid, 25, "BulkSurvivingObjectRanges", ProviderGuid, ProviderName);
                templates[26] = new GCBulkMovedObjectRangesTraceData(null, 22, 1, "GC", GCTaskGuid, 26, "BulkMovedObjectRanges", ProviderGuid, ProviderName);
                templates[27] = new GCGenerationRangeTraceData(null, 23, 1, "GC", GCTaskGuid, 27, "GenerationRange", ProviderGuid, ProviderName);
                templates[28] = new GCMarkTraceData(null, 25, 1, "GC", GCTaskGuid, 28, "MarkStackRoots", ProviderGuid, ProviderName);
                templates[29] = new GCMarkTraceData(null, 26, 1, "GC", GCTaskGuid, 29, "MarkFinalizeQueueRoots", ProviderGuid, ProviderName);
                templates[30] = new GCMarkTraceData(null, 27, 1, "GC", GCTaskGuid, 30, "MarkHandles", ProviderGuid, ProviderName);
                templates[31] = new GCMarkTraceData(null, 28, 1, "GC", GCTaskGuid, 31, "MarkCards", ProviderGuid, ProviderName);
                templates[32] = new FinalizeObjectTraceData(null, 29, 1, "GC", GCTaskGuid, 32, "FinalizeObject", ProviderGuid, ProviderName, state: null);
                templates[33] = new SetGCHandleTraceData(null, 30, 1, "GC", GCTaskGuid, 33, "SetGCHandle", ProviderGuid, ProviderName);
                templates[34] = new DestroyGCHandleTraceData(null, 31, 1, "GC", GCTaskGuid, 34, "DestoryGCHandle", ProviderGuid, ProviderName);
                templates[35] = new PinObjectAtGCTimeTraceData(null, 33, 1, "GC", GCTaskGuid, 36, "PinObjectAtGCTime", ProviderGuid, ProviderName);
                templates[36] = new PinPlugAtGCTimeTraceData(null, 34, 1, "GC", GCTaskGuid, 37, "PinPlugAtGCTime", ProviderGuid, ProviderName);
                templates[37] = new GCTriggeredTraceData(null, 35, 1, "GC", GCTaskGuid, 35, "Triggered", ProviderGuid, ProviderName);
                templates[38] = new IOThreadTraceData(null, 44, 3, "IOThreadCreation", IOThreadCreationTaskGuid, 1, "Start", ProviderGuid, ProviderName);
                templates[39] = new IOThreadTraceData(null, 45, 3, "IOThreadCreation", IOThreadCreationTaskGuid, 2, "Stop", ProviderGuid, ProviderName);
                templates[40] = new IOThreadTraceData(null, 46, 5, "IOThreadRetirement", IOThreadRetirementTaskGuid, 1, "Start", ProviderGuid, ProviderName);
                templates[41] = new IOThreadTraceData(null, 47, 5, "IOThreadRetirement", IOThreadRetirementTaskGuid, 2, "Stop", ProviderGuid, ProviderName);
                templates[42] = new ThreadPoolWorkerThreadTraceData(null, 50, 16, "ThreadPoolWorkerThread", ThreadPoolWorkerThreadTaskGuid, 1, "Start", ProviderGuid, ProviderName);
                templates[43] = new ThreadPoolWorkerThreadTraceData(null, 51, 16, "ThreadPoolWorkerThread", ThreadPoolWorkerThreadTaskGuid, 2, "Stop", ProviderGuid, ProviderName);
                templates[44] = new ThreadPoolWorkerThreadTraceData(null, 52, 17, "ThreadPoolWorkerThreadRetirement", ThreadPoolWorkerThreadRetirementTaskGuid, 1, "Start", ProviderGuid, ProviderName);
                templates[45] = new ThreadPoolWorkerThreadTraceData(null, 53, 17, "ThreadPoolWorkerThreadRetirement", ThreadPoolWorkerThreadRetirementTaskGuid, 2, "Stop", ProviderGuid, ProviderName);
                templates[46] = new ThreadPoolWorkerThreadAdjustmentSampleTraceData(null, 54, 18, "ThreadPoolWorkerThreadAdjustment", ThreadPoolWorkerThreadAdjustmentTaskGuid, 100, "Sample", ProviderGuid, ProviderName);
                templates[47] = new ThreadPoolWorkerThreadAdjustmentTraceData(null, 55, 18, "ThreadPoolWorkerThreadAdjustment", ThreadPoolWorkerThreadAdjustmentTaskGuid, 101, "Adjustment", ProviderGuid, ProviderName);
                templates[48] = new ThreadPoolWorkerThreadAdjustmentStatsTraceData(null, 56, 18, "ThreadPoolWorkerThreadAdjustment", ThreadPoolWorkerThreadAdjustmentTaskGuid, 102, "Stats", ProviderGuid, ProviderName);
                templates[49] = new ExceptionTraceData(null, 80, 7, "Exception", ExceptionTaskGuid, 1, "Start", ProviderGuid, ProviderName);
                templates[50] = new ContentionStartTraceData(null, 81, 8, "Contention", ContentionTaskGuid, 1, "Start", ProviderGuid, ProviderName);
                templates[51] = new MethodILToNativeMapTraceData(null, 190, 9, "Method", MethodTaskGuid, 87, "ILToNativeMap", ProviderGuid, ProviderName);
                templates[52] = new ClrStackWalkTraceData(null, 82, 11, "ClrStack", ClrStackTaskGuid, 82, "Walk", ProviderGuid, ProviderName);
                templates[53] = new AppDomainMemAllocatedTraceData(null, 83, 14, "AppDomainResourceManagement", AppDomainResourceManagementTaskGuid, 48, "MemAllocated", ProviderGuid, ProviderName);
                templates[54] = new AppDomainMemSurvivedTraceData(null, 84, 14, "AppDomainResourceManagement", AppDomainResourceManagementTaskGuid, 49, "MemSurvived", ProviderGuid, ProviderName);
                templates[55] = new ThreadCreatedTraceData(null, 85, 14, "AppDomainResourceManagement", AppDomainResourceManagementTaskGuid, 50, "ThreadCreated", ProviderGuid, ProviderName);
                templates[56] = new ThreadTerminatedOrTransitionTraceData(null, 86, 14, "AppDomainResourceManagement", AppDomainResourceManagementTaskGuid, 51, "ThreadTerminated", ProviderGuid, ProviderName);
                templates[57] = new ThreadTerminatedOrTransitionTraceData(null, 87, 14, "AppDomainResourceManagement", AppDomainResourceManagementTaskGuid, 52, "DomainEnter", ProviderGuid, ProviderName);
                templates[58] = new ILStubGeneratedTraceData(null, 88, 15, "ILStub", ILStubTaskGuid, 88, "StubGenerated", ProviderGuid, ProviderName);
                templates[59] = new ILStubCacheHitTraceData(null, 89, 15, "ILStub", ILStubTaskGuid, 89, "StubCacheHit", ProviderGuid, ProviderName);
                templates[60] = new ContentionStopTraceData(null, 91, 8, "Contention", ContentionTaskGuid, 2, "Stop", ProviderGuid, ProviderName);
                templates[61] = new EmptyTraceData(null, 135, 9, "Method", MethodTaskGuid, 14, "DCStartCompleteV2", ProviderGuid, ProviderName);
                templates[62] = new EmptyTraceData(null, 136, 9, "Method", MethodTaskGuid, 15, "DCStopCompleteV2", ProviderGuid, ProviderName);
                templates[63] = new MethodLoadUnloadTraceData(null, 137, 9, "Method", MethodTaskGuid, 35, "DCStartV2", ProviderGuid, ProviderName);
                templates[64] = new MethodLoadUnloadTraceData(null, 138, 9, "Method", MethodTaskGuid, 36, "DCStopV2", ProviderGuid, ProviderName);
                templates[65] = new MethodLoadUnloadVerboseTraceData(null, 139, 9, "Method", MethodTaskGuid, 39, "DCStartVerboseV2", ProviderGuid, ProviderName);
                templates[66] = new MethodLoadUnloadVerboseTraceData(null, 140, 9, "Method", MethodTaskGuid, 40, "DCStopVerboseV2", ProviderGuid, ProviderName);
                templates[67] = new MethodLoadUnloadTraceData(null, 141, 9, "Method", MethodTaskGuid, 33, "Load", ProviderGuid, ProviderName);
                templates[68] = new MethodLoadUnloadTraceData(null, 142, 9, "Method", MethodTaskGuid, 34, "Unload", ProviderGuid, ProviderName);
                templates[69] = new MethodLoadUnloadVerboseTraceData(null, 143, 9, "Method", MethodTaskGuid, 37, "LoadVerbose", ProviderGuid, ProviderName);
                templates[70] = new MethodLoadUnloadVerboseTraceData(null, 144, 9, "Method", MethodTaskGuid, 38, "UnloadVerbose", ProviderGuid, ProviderName);
                templates[71] = new MethodJittingStartedTraceData(null, 145, 9, "Method", MethodTaskGuid, 42, "JittingStarted", ProviderGuid, ProviderName);
                templates[72] = new ModuleLoadUnloadTraceData(null, 149, 10, "Loader", LoaderTaskGuid, 35, "ModuleDCStartV2", ProviderGuid, ProviderName);
                templates[73] = new ModuleLoadUnloadTraceData(null, 150, 10, "Loader", LoaderTaskGuid, 36, "ModuleDCStopV2", ProviderGuid, ProviderName);
                templates[74] = new DomainModuleLoadUnloadTraceData(null, 151, 10, "Loader", LoaderTaskGuid, 45, "DomainModuleLoad", ProviderGuid, ProviderName);
                templates[75] = new ModuleLoadUnloadTraceData(null, 152, 10, "Loader", LoaderTaskGuid, 33, "ModuleLoad", ProviderGuid, ProviderName);
                templates[76] = new ModuleLoadUnloadTraceData(null, 153, 10, "Loader", LoaderTaskGuid, 34, "ModuleUnload", ProviderGuid, ProviderName);
                templates[77] = new AssemblyLoadUnloadTraceData(null, 154, 10, "Loader", LoaderTaskGuid, 37, "AssemblyLoad", ProviderGuid, ProviderName);
                templates[78] = new AssemblyLoadUnloadTraceData(null, 155, 10, "Loader", LoaderTaskGuid, 38, "AssemblyUnload", ProviderGuid, ProviderName);
                templates[79] = new AppDomainLoadUnloadTraceData(null, 156, 10, "Loader", LoaderTaskGuid, 41, "AppDomainLoad", ProviderGuid, ProviderName);
                templates[80] = new AppDomainLoadUnloadTraceData(null, 157, 10, "Loader", LoaderTaskGuid, 42, "AppDomainUnload", ProviderGuid, ProviderName);
                templates[81] = new StrongNameVerificationTraceData(null, 181, 12, "StrongNameVerification", StrongNameVerificationTaskGuid, 1, "Start", ProviderGuid, ProviderName);
                templates[82] = new StrongNameVerificationTraceData(null, 182, 12, "StrongNameVerification", StrongNameVerificationTaskGuid, 2, "Stop", ProviderGuid, ProviderName);
                templates[83] = new AuthenticodeVerificationTraceData(null, 183, 13, "AuthenticodeVerification", AuthenticodeVerificationTaskGuid, 1, "Start", ProviderGuid, ProviderName);
                templates[84] = new AuthenticodeVerificationTraceData(null, 184, 13, "AuthenticodeVerification", AuthenticodeVerificationTaskGuid, 2, "Stop", ProviderGuid, ProviderName);
                templates[85] = new MethodJitInliningSucceededTraceData(null, 185, 9, "Method", MethodTaskGuid, 83, "InliningSucceeded", ProviderGuid, ProviderName);
                templates[86] = new MethodJitInliningFailedTraceData(null, 192, 9, "Method", MethodTaskGuid, 84, "InliningFailed", ProviderGuid, ProviderName);
                templates[87] = new RuntimeInformationTraceData(null, 187, 19, "Runtime", RuntimeTaskGuid, 1, "Start", ProviderGuid, ProviderName);
                templates[88] = new MethodJitTailCallSucceededTraceData(null, 188, 9, "Method", MethodTaskGuid, 85, "TailCallSucceeded", ProviderGuid, ProviderName);
                templates[89] = new MethodJitTailCallFailedTraceData(null, 189, 9, "Method", MethodTaskGuid, 86, "TailCallFailed", ProviderGuid, ProviderName);
                templates[90] = new GCBulkRootCCWTraceData(null, 36, 1, "GC", GCTaskGuid, 38, "BulkRootCCW", ProviderGuid, ProviderName);
                templates[91] = new GCBulkRCWTraceData(null, 37, 1, "GC", GCTaskGuid, 39, "BulkRCW", ProviderGuid, ProviderName);
                templates[92] = new GCBulkRootStaticVarTraceData(null, 38, 1, "GC", GCTaskGuid, 40, "BulkRootStaticVar", ProviderGuid, ProviderName);
                templates[93] = new ThreadPoolWorkerThreadTraceData(null, 57, 16, "ThreadPoolWorkerThread", Guid.Empty, 90, "Wait", ProviderGuid, ProviderName);
                templates[94] = new GCMarkWithTypeTraceData(null, 202, 1, "GC", GCTaskGuid, 202, "MarkWithType", ProviderGuid, ProviderName);
                templates[95] = new GCJoinTraceData(null, 203, 1, "GC", GCTaskGuid, 203, "Join", ProviderGuid, ProviderName);
                templates[96] = new GCPerHeapHistoryTraceData(null, 204, 1, "GC", GCTaskGuid, 204, "PerHeapHistory", ProviderGuid, ProviderName);
                templates[97] = new GCGlobalHeapHistoryTraceData(null, 205, 1, "GC", GCTaskGuid, 205, "GlobalHeapHistory", ProviderGuid, ProviderName);

                // New style
                templates[98] = ExceptionCatchStartTemplate(null);
                templates[99] = ExceptionCatchStopTemplate(null);
                templates[100] = ExceptionFinallyStartTemplate(null);
                templates[101] = ExceptionFinallyStopTemplate(null);
                templates[102] = ExceptionFilterStartTemplate(null);
                templates[103] = ExceptionFilterStopTemplate(null);
                templates[104] = ExceptionStopTemplate(null);
                templates[105] = CodeSymbolsStartTemplate(null);

                // Some more old style 
                templates[106] = new ThreadStartWorkTraceData(null, 70, 24, "Thread", ThreadTaskGuid, 11, "Creating", ProviderGuid, ProviderName);
                templates[107] = new ThreadStartWorkTraceData(null, 71, 24, "Thread", ThreadTaskGuid, 12, "Running", ProviderGuid, ProviderName);
                templates[108] = new ThreadPoolWorkingThreadCountTraceData(null, 60, 22, "ThreadPoolWorkingThreadCount", ThreadPoolWorkingThreadCountTaskGuid, 1, "Start", ProviderGuid, ProviderName);
                templates[109] = new ThreadPoolWorkTraceData(null, 61, 23, "ThreadPool", ThreadPoolTaskGuid, 11, "Enqueue", ProviderGuid, ProviderName);
                templates[110] = new ThreadPoolWorkTraceData(null, 62, 23, "ThreadPool", ThreadPoolTaskGuid, 12, "Dequeue", ProviderGuid, ProviderName);
                templates[111] = new ThreadPoolIOWorkEnqueueTraceData(null, 63, 23, "ThreadPool", ThreadPoolTaskGuid, 13, "IOEnqueue", ProviderGuid, ProviderName);
                templates[112] = new ThreadPoolIOWorkTraceData(null, 64, 23, "ThreadPool", ThreadPoolTaskGuid, 14, "IODequeue", ProviderGuid, ProviderName);
                templates[113] = new ThreadPoolIOWorkTraceData(null, 65, 23, "ThreadPool", ThreadPoolTaskGuid, 15, "IOPack", ProviderGuid, ProviderName);
                templates[114] = new MethodJitInliningFailedAnsiTraceData(null, 186, 9, "Method", MethodTaskGuid, 84, "InliningFailedAnsi", ProviderGuid, ProviderName);
                templates[115] = new MethodJitTailCallFailedAnsiTraceData(null, 189, 9, "Method", MethodTaskGuid, 86, "TailCallFailedAnsi", ProviderGuid, ProviderName);
                templates[116] = new EventSourceTraceData(null, 270, 0, "EventSourceEvent", Guid.Empty, 0, "", ProviderGuid, ProviderName);
                templates[117] = new R2RGetEntryPointTraceData(null, 159, 9, "Method", MethodTaskGuid, 33, "R2RGetEntryPoint", ProviderGuid, ProviderName);
                templates[118] = new MethodDetailsTraceData(null, 72, 9, "Method", MethodTaskGuid, 43, "MethodDetails", ProviderGuid, ProviderName);
                templates[119] = new AssemblyLoadStartTraceData(null, 290, 32, "AssemblyLoader", AssemblyLoaderTaskGuid, 1, "Start", ProviderGuid, ProviderName);
                templates[120] = new AssemblyLoadStopTraceData(null, 291, 32, "AssemblyLoader", AssemblyLoaderTaskGuid, 2, "Stop", ProviderGuid, ProviderName);
                templates[121] = new ResolutionAttemptedTraceData(null, 292, 32, "AssemblyLoader", AssemblyLoaderTaskGuid, 11, "ResolutionAttempted", ProviderGuid, ProviderName);
                templates[122] = new AssemblyLoadContextResolvingHandlerInvokedTraceData(null, 293, 32, "AssemblyLoader", AssemblyLoaderTaskGuid, 12, "AssemblyLoadContextResolvingHandlerInvoked", ProviderGuid, ProviderName);
                templates[123] = new AppDomainAssemblyResolveHandlerInvokedTraceData(null, 294, 32, "AssemblyLoader", AssemblyLoaderTaskGuid, 13, "AppDomainAssemblyResolveHandlerInvoked", ProviderGuid, ProviderName);
                templates[124] = new AssemblyLoadFromResolveHandlerInvokedTraceData(null, 295, 32, "AssemblyLoader", AssemblyLoaderTaskGuid, 14, "AssemblyLoadFromResolveHandlerInvoked", ProviderGuid, ProviderName);
                templates[125] = new KnownPathProbedTraceData(null, 296, 32, "AssemblyLoader", AssemblyLoaderTaskGuid, 15, "KnownPathProbed", ProviderGuid, ProviderName);

                // Some more new style
                templates[126] = TieredCompilationSettingsTemplate(null);
                templates[127] = TieredCompilationPauseTemplate(null);
                templates[128] = TieredCompilationResumeTemplate(null);
                templates[129] = TieredCompilationBackgroundJitStartTemplate(null);
                templates[130] = TieredCompilationBackgroundJitStopTemplate(null);

                templates[131]  = R2RGetEntryPointStartTemplate(null);
                templates[132]  = TypeLoadStartTemplate(null);
                templates[133]  = TypeLoadStopTemplate(null);
                templates[134]  = MethodMemoryAllocatedForJitCodeTemplate(null);
                templates[135] = GenAwareBeginTemplate(null);
                templates[136] = GenAwareEndTemplate(null);
                templates[137] = JitInstrumentationDataTemplate(null);
                templates[138] = JitInstrumentationDataVerboseTemplate(null);

                s_templates = templates;
            }

            List<TraceEvent> enumeratedTemplates = new List<TraceEvent>();
            foreach (var template in s_templates)
            {
                if (eventsToObserve == null || eventsToObserve(template.ProviderName, template.EventName) == EventFilterResponse.AcceptEvent)
                {
                    // The CLR parser has duplicate template definitions that differ only by name
                    // The eventsToObserve delegate could filter to select only one of them, but if
                    // it doesn't then select one on a first-come-first-served basis
                    bool match = false;
                    foreach(var prevTemplate in enumeratedTemplates)
                    {
                        if(prevTemplate.Matches(template))
                        {
                            match = true;
                            break;
                        }
                    }
                    if (match)
                        continue;
                    enumeratedTemplates.Add(template);

                    callback(template);

                    // Project N support.   If this is not a classic event, then also register with project N
                    if (template.ID != TraceEventID.Illegal)
                    {
                        var projectNTemplate = template.Clone();
                        projectNTemplate.providerGuid = ClrTraceEventParser.NativeProviderGuid;
                        callback(projectNTemplate);
                    }
                }
            }
        }

        private static readonly Guid GCTaskGuid = new Guid(unchecked((int)0x044973cd), unchecked((short)0x251f), unchecked((short)0x4dff), 0xa3, 0xe9, 0x9d, 0x63, 0x07, 0x28, 0x6b, 0x05);
        private static readonly Guid WorkerThreadCreationV2TaskGuid = new Guid(unchecked((int)0xcfc4ba53), unchecked((short)0xfb42), unchecked((short)0x4757), 0x8b, 0x70, 0x5f, 0x5d, 0x51, 0xfe, 0xe2, 0xf4);
        private static readonly Guid IOThreadCreationTaskGuid = new Guid(unchecked((int)0xc71408de), unchecked((short)0x42cc), unchecked((short)0x4f81), 0x9c, 0x93, 0xb8, 0x91, 0x2a, 0xbf, 0x2a, 0x0f);
        private static readonly Guid WorkerThreadRetirementV2TaskGuid = new Guid(unchecked((int)0xefdf1eac), unchecked((short)0x1d5d), unchecked((short)0x4e84), 0x89, 0x3a, 0x19, 0xb8, 0x0f, 0x69, 0x21, 0x76);
        private static readonly Guid IOThreadRetirementTaskGuid = new Guid(unchecked((int)0x840c8456), unchecked((short)0x6457), unchecked((short)0x4eb7), 0x9c, 0xd0, 0xd2, 0x8f, 0x01, 0xc6, 0x4f, 0x5e);
        private static readonly Guid ThreadpoolSuspensionV2TaskGuid = new Guid(unchecked((int)0xc424b3e3), unchecked((short)0x2ae0), unchecked((short)0x416e), 0xa0, 0x39, 0x41, 0x0c, 0x5d, 0x8e, 0x5f, 0x14);
        private static readonly Guid ExceptionTaskGuid = new Guid(unchecked((int)0x300ce105), unchecked((short)0x86d1), unchecked((short)0x41f8), 0xb9, 0xd2, 0x83, 0xfc, 0xbf, 0xf3, 0x2d, 0x99);
        private static readonly Guid ContentionTaskGuid = new Guid(unchecked((int)0x561410f5), unchecked((short)0xa138), unchecked((short)0x4ab3), 0x94, 0x5e, 0x51, 0x64, 0x83, 0xcd, 0xdf, 0xbc);
        private static readonly Guid MethodTaskGuid = new Guid(unchecked((int)0x3044f61a), unchecked((short)0x99b0), unchecked((short)0x4c21), 0xb2, 0x03, 0xd3, 0x94, 0x23, 0xc7, 0x3b, 0x00);
        private static readonly Guid LoaderTaskGuid = new Guid(unchecked((int)0xd00792da), unchecked((short)0x07b7), unchecked((short)0x40f5), 0x97, 0xeb, 0x5d, 0x97, 0x4e, 0x05, 0x47, 0x40);
        private static readonly Guid ClrStackTaskGuid = new Guid(unchecked((int)0xd3363dc0), unchecked((short)0x243a), unchecked((short)0x4620), 0xa4, 0xd0, 0x8a, 0x07, 0xd7, 0x72, 0xf5, 0x33);
        private static readonly Guid StrongNameVerificationTaskGuid = new Guid(unchecked((int)0x15447a14), unchecked((short)0xb523), unchecked((short)0x46ae), 0xb7, 0x5b, 0x02, 0x3f, 0x90, 0x0b, 0x43, 0x93);
        private static readonly Guid AuthenticodeVerificationTaskGuid = new Guid(unchecked((int)0xb17304d9), unchecked((short)0x5afa), unchecked((short)0x4da6), 0x9f, 0x7b, 0x5a, 0x4f, 0xa7, 0x31, 0x29, 0xb6);
        private static readonly Guid AppDomainResourceManagementTaskGuid = new Guid(unchecked((int)0x88e83959), unchecked((short)0x6185), unchecked((short)0x4e0b), 0x95, 0xb8, 0x0e, 0x4a, 0x35, 0xdf, 0x61, 0x22);
        private static readonly Guid ILStubTaskGuid = new Guid(unchecked((int)0xd00792da), unchecked((short)0x07b7), unchecked((short)0x40f5), 0x00, 0x00, 0x5d, 0x97, 0x4e, 0x05, 0x47, 0x40);
        private static readonly Guid ThreadPoolWorkerThreadTaskGuid = new Guid(unchecked((int)0x8a9a44ab), unchecked((short)0xf681), unchecked((short)0x4271), 0x88, 0x10, 0x83, 0x0d, 0xab, 0x9f, 0x56, 0x21);
        private static readonly Guid ThreadPoolWorkerThreadRetirementTaskGuid = new Guid(unchecked((int)0x402ee399), unchecked((short)0xc137), unchecked((short)0x4dc0), 0xa5, 0xab, 0x3c, 0x2d, 0xea, 0x64, 0xac, 0x9c);
        private static readonly Guid ThreadPoolWorkerThreadAdjustmentTaskGuid = new Guid(unchecked((int)0x94179831), unchecked((short)0xe99a), unchecked((short)0x4625), 0x88, 0x24, 0x23, 0xca, 0x5e, 0x00, 0xca, 0x7d);
        private static readonly Guid RuntimeTaskGuid = new Guid(unchecked((int)0xcd7d3e32), unchecked((short)0x65fe), unchecked((short)0x40cd), 0x92, 0x25, 0xa2, 0x57, 0x7d, 0x20, 0x3f, 0xc3);
        private static readonly Guid ClrPerfTrackTaskGuid = new Guid(unchecked((int)0xeac685f6), unchecked((short)0x2104), unchecked((short)0x4dec), 0x88, 0xfd, 0x91, 0xe4, 0x25, 0x42, 0x21, 0xec);
        private static readonly Guid TypeTaskGuid = new Guid(unchecked((int)0x003e5a9b), unchecked((short)0x4757), unchecked((short)0x4d3e), 0xb4, 0xa1, 0xe4, 0x7b, 0xfb, 0x48, 0x94, 0x08);
        private static readonly Guid ThreadPoolWorkingThreadCountTaskGuid = new Guid(unchecked((int)0x1b032b96), unchecked((short)0x767c), unchecked((short)0x42e4), 0x84, 0x81, 0xcb, 0x52, 0x8a, 0x66, 0xd7, 0xbd);
        private static readonly Guid ThreadPoolTaskGuid = new Guid(unchecked((int)0xead685f6), unchecked((short)0x2104), unchecked((short)0x4dec), 0x88, 0xfd, 0x91, 0xe4, 0x25, 0x42, 0x21, 0xe9);
        private static readonly Guid ThreadTaskGuid = new Guid(unchecked((int)0x641994c5), unchecked((short)0x16f2), unchecked((short)0x4123), 0x91, 0xa7, 0xa2, 0x99, 0x9d, 0xd7, 0xbf, 0xc3);
        private static readonly Guid CodeSymbolsTaskGuid = new Guid(unchecked((int)0x53aedf69), unchecked((short)0x2049), unchecked((short)0x4f7d), 0x93, 0x45, 0xd3, 0x01, 0x8b, 0x5c, 0x4d, 0x80);
        private static readonly Guid AssemblyLoaderTaskGuid = new Guid(unchecked((int)0xbcf2339e), unchecked((short)0xb0a6), unchecked((short)0x452d), 0x96, 0x6c, 0x33, 0xac, 0x9d, 0xd8, 0x25, 0x73);
        private static readonly Guid TieredCompilationTaskGuid = new Guid(unchecked((int)0xa77f474d), unchecked((short)0x9d0d), unchecked((short)0x4311), 0xb9, 0x8e, 0xcf, 0xbc, 0xf8, 0x4b, 0x9e, 0xf);
        private static readonly Guid TypeLoadTaskGuid = new Guid(unchecked((int)0x9db1562b), unchecked((short)0x512f), unchecked((short)0x475d), 0x8d, 0x4c, 0x0c, 0x6d, 0x97, 0xc1, 0xe7, 0x3c);
        private static readonly Guid JitInstrumentationDataTaskGuid = new Guid(unchecked((int)0xf8666925), unchecked((short)0x22c8), unchecked((short)0x4b70), 0xa1, 0x31, 0x07, 0x38, 0x13, 0x7e, 0x7f, 0x25);

        // TODO remove if project N's Guids are harmonized with the desktop 
        private void RegisterTemplate(TraceEvent template)
        {
            Debug.Assert(template.ProviderGuid == ClrTraceEventParser.ProviderGuid);        // It is the desktop GUID 
            var projectNTemplate = template.Clone();
            projectNTemplate.providerGuid = ClrTraceEventParser.NativeProviderGuid;

            source.RegisterEventTemplate(template);
            source.RegisterEventTemplate(projectNTemplate);

            // TODO FIX NOW also have to unregister the project N templates.  
        }

        #endregion
    }
}

namespace Microsoft.Diagnostics.Tracing.Parsers.Clr
{
    public sealed class JitInstrumentationDataTraceData : TraceEvent
    {
        public int ClrInstanceID { get { return GetInt16At(0); } }
        public int MethodFlags { get { return GetInt32At(2); } }
        public int DataSize { get { return GetInt32At(6); } }
        public long MethodID { get { return GetInt64At(10); } }
        public byte[] Data { get { return GetByteArrayAt(18, DataSize); } }

        #region Private
        internal JitInstrumentationDataTraceData(Action<JitInstrumentationDataTraceData> target, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            m_target = target;
        }
        protected internal override void Dispatch()
        {
            m_target(this);
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 6));
        }
        protected internal override Delegate Target
        {
            get { return m_target; }
            set { m_target = (Action<JitInstrumentationDataTraceData>)value; }
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            XmlAttrib(sb, "MethodFlags", MethodFlags);
            XmlAttrib(sb, "DataSize", MethodFlags);
            XmlAttrib(sb, "MethodID", MethodFlags);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[] { "ClrInstanceID", "MethodFlags", "DataSize", "MethodID" };
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ClrInstanceID;
                case 1:
                    return MethodFlags;
                case 2:
                    return DataSize;
                case 3:
                    return MethodID;
                case 4:
                    return Data;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<JitInstrumentationDataTraceData> m_target;
        #endregion
    }
    public sealed class JitInstrumentationDataVerboseTraceData : TraceEvent
    {
        public int ClrInstanceID { get { return GetInt16At(0); } }
        public int MethodFlags { get { return GetInt32At(2); } }
        public int DataSize { get { return GetInt32At(6); } }
        public long MethodID { get { return GetInt64At(10); } }
        public long ModuleID { get { return GetInt64At(18); } }
        public int MethodToken { get { return GetInt32At(24); } }
        public string MethodNamespace { get { return GetUnicodeStringAt(28); } }
        public string MethodName { get { return GetUnicodeStringAt(SkipUnicodeString(28)); } }
        public string MethodSignature { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(28))); } }
        public byte[] Data { get { return GetByteArrayAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(28))), DataSize); } }

        #region Private
        internal JitInstrumentationDataVerboseTraceData(Action<JitInstrumentationDataVerboseTraceData> target, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            m_target = target;
        }
        protected internal override void Dispatch()
        {
            m_target(this);
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 6));
        }
        protected internal override Delegate Target
        {
            get { return m_target; }
            set { m_target = (Action<JitInstrumentationDataVerboseTraceData>)value; }
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            XmlAttrib(sb, "MethodFlags", MethodFlags);
            XmlAttrib(sb, "DataSize", MethodFlags);
            XmlAttrib(sb, "MethodID", MethodFlags);
            XmlAttribHex(sb, "ModuleID", ModuleID);
            XmlAttribHex(sb, "MethodToken", MethodToken);
            XmlAttrib(sb, "MethodNamespace", MethodNamespace);
            XmlAttrib(sb, "MethodName", MethodName);
            XmlAttrib(sb, "MethodSignature", MethodSignature);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[] { "ClrInstanceID", "MethodFlags", "DataSize", "MethodID", "ModuleID", "MethodToken", "MethodNamespace", "MethodName", "MethodSignature" };
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ClrInstanceID;
                case 1:
                    return MethodFlags;
                case 2:
                    return DataSize;
                case 3:
                    return MethodID;
                case 4:
                    return ModuleID;
                case 5:
                    return MethodToken;
                case 6:
                    return MethodNamespace;
                case 7:
                    return MethodName;
                case 8:
                    return MethodSignature;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<JitInstrumentationDataVerboseTraceData> m_target;
        #endregion
    }
    public sealed class GenAwareBeginTraceData : TraceEvent
    {
        public int Count { get { return GetInt32At(0); } }
        public int ClrInstanceID { get { return GetInt16At(4); } }

        #region Private
        internal GenAwareBeginTraceData(Action<GenAwareBeginTraceData> target, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            m_target = target;
        }
        protected internal override void Dispatch()
        {
            m_target(this);
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 6));
        }
        protected internal override Delegate Target
        {
            get { return m_target; }
            set { m_target = (Action<GenAwareBeginTraceData>)value; }
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "Count", Count);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[] { "Count", "ClrInstanceID" };
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Count;
                case 1:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<GenAwareBeginTraceData> m_target;
        #endregion
    }
    public sealed class GenAwareEndTraceData : TraceEvent
    {
        public int Count { get { return GetInt32At(0); } }
        public int ClrInstanceID { get { return GetInt16At(4); } }

        #region Private
        internal GenAwareEndTraceData(Action<GenAwareEndTraceData> target, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            m_target = target;
        }
        protected internal override void Dispatch()
        {
            m_target(this);
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 6));
        }
        protected internal override Delegate Target
        {
            get { return m_target; }
            set { m_target = (Action<GenAwareEndTraceData>)value; }
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "Count", Count);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[] { "Count", "ClrInstanceID" };
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Count;
                case 1:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<GenAwareEndTraceData> m_target;
        #endregion
    }
    public sealed class GCStartTraceData : TraceEvent
    {
        public int Count { get { return GetInt32At(0); } }
        public GCReason Reason { get { if (EventDataLength >= 16) { return (GCReason)GetInt32At(8); } return (GCReason)GetInt32At(4); } }
        public int Depth { get { if (EventDataLength >= 16) { return GetInt32At(4); } return 0; } }
        public GCType Type { get { if (EventDataLength >= 16) { return (GCType)GetInt32At(12); } return (GCType)0; } }
        public int ClrInstanceID { get { if (Version >= 1) { return GetInt16At(16); } return 0; } }
        public long ClientSequenceNumber { get { if (Version >= 2) { return GetInt64At(18); } return 0; } }

        #region Private
        internal GCStartTraceData(Action<GCStartTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<GCStartTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength < 8));       // FIXed manually to be < 8 
            Debug.Assert(!(Version == 1 && EventDataLength != 18));
            Debug.Assert(!(Version > 1 && EventDataLength < 18));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "Count", Count);
            XmlAttrib(sb, "Reason", Reason);
            XmlAttrib(sb, "Depth", Depth);
            XmlAttrib(sb, "Type", Type);
            XmlAttrib(sb, "ClientSequenceNumber", ClientSequenceNumber);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Count", "Reason", "Depth", "Type", "ClrInstanceID", "ClientSequenceNumber" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Count;
                case 1:
                    return Reason;
                case 2:
                    return Depth;
                case 3:
                    return Type;
                case 4:
                    return ClrInstanceID;
                case 5:
                    return ClientSequenceNumber;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<GCStartTraceData> Action;
        #endregion
    }
    public sealed class GCEndTraceData : TraceEvent
    {
        public int Count { get { return GetInt32At(0); } }
        public int Depth { get { if (Version >= 1) { return GetInt32At(4); } return GetInt16At(4); } }
        public int ClrInstanceID { get { if (Version >= 1) { return GetInt16At(8); } return 0; } }

        #region Private
        internal GCEndTraceData(Action<GCEndTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<GCEndTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength < 6));           // HAND_MODIFIED <
            Debug.Assert(!(Version == 1 && EventDataLength != 10));
            Debug.Assert(!(Version > 1 && EventDataLength < 10));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "Count", Count);
            XmlAttrib(sb, "Depth", Depth);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Count", "Depth", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Count;
                case 1:
                    return Depth;
                case 2:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<GCEndTraceData> Action;
        #endregion
    }
    public sealed class GCNoUserDataTraceData : TraceEvent
    {
        public int ClrInstanceID { get { if (Version >= 1) { return GetInt16At(0); } return 0; } }

        #region Private
        internal GCNoUserDataTraceData(Action<GCNoUserDataTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<GCNoUserDataTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 1 && EventDataLength != 2));
            Debug.Assert(!(Version > 1 && EventDataLength < 2));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<GCNoUserDataTraceData> Action;
        #endregion
    }

    public sealed class GCHeapStatsTraceData : TraceEvent
    {
        // GCHeap stats are reported AFTER the GC has completed.  Thus these number are the 'After' heap size for each generation
        // The sizes INCLUDE fragmentation (holes in the segement)

        // The TotalPromotedSize0 is the amount that SURVIVED Gen0 (thus it is now in Gen1, thus TotalPromoted0 <= GenerationSize1)
        public long TotalHeapSize { get { return GenerationSize0 + GenerationSize1 + GenerationSize2 + GenerationSize3 + GenerationSize4; } }
        public long TotalPromoted { get { return TotalPromotedSize0 + TotalPromotedSize1 + TotalPromotedSize2 + TotalPromotedSize3 + TotalPromotedSize4; } }
        /// <summary>
        /// Note that this field is derived from the TotalPromotedSize* fields.  If nothing was promoted, it is possible
        /// that this could give a number that is smaller than what GC/Start or GC/Stop would indicate.  
        /// </summary>
        public int Depth
        {
            get
            {
                if (TotalPromotedSize2 != 0)
                {
                    return 2;
                }

                if (TotalPromotedSize1 != 0)
                {
                    return 1;
                }

                return 0;
            }
        }

        public long GenerationSize0 { get { return GetInt64At(0); } }
        public long TotalPromotedSize0 { get { return GetInt64At(8); } }
        public long GenerationSize1 { get { return GetInt64At(16); } }
        public long TotalPromotedSize1 { get { return GetInt64At(24); } }
        public long GenerationSize2 { get { return GetInt64At(32); } }
        public long TotalPromotedSize2 { get { return GetInt64At(40); } }
        public long GenerationSize3 { get { return GetInt64At(48); } }
        public long TotalPromotedSize3 { get { return GetInt64At(56); } }
        public long FinalizationPromotedSize { get { return GetInt64At(64); } }
        public long FinalizationPromotedCount { get { return GetInt64At(72); } }
        public int PinnedObjectCount { get { return GetInt32At(80); } }
        public int SinkBlockCount { get { return GetInt32At(84); } }
        public int GCHandleCount { get { return GetInt32At(88); } }
        public int ClrInstanceID { get { if (Version >= 1) { return GetInt16At(92); } return 0; } }
        public long GenerationSize4 { get { if (Version >= 2) { return GetInt64At(94); } return 0; } }
        public long TotalPromotedSize4 { get { if (Version >= 2) { return GetInt64At(102); } return 0; } }

        #region Private
        internal GCHeapStatsTraceData(Action<GCHeapStatsTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<GCHeapStatsTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 96));          // HAND_MODIFIED C++ pads to 96
            Debug.Assert(!(Version == 1 && EventDataLength != 94));
            Debug.Assert(!(Version == 2 && EventDataLength != 110));
            Debug.Assert(!(Version > 2 && EventDataLength < 110));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "TotalPromoted", TotalPromoted);
            XmlAttribHex(sb, "TotalHeapSize", TotalHeapSize);
            XmlAttribHex(sb, "Depth", Depth);
            XmlAttribHex(sb, "GenerationSize0", GenerationSize0);
            XmlAttribHex(sb, "TotalPromotedSize0", TotalPromotedSize0);
            XmlAttribHex(sb, "GenerationSize1", GenerationSize1);
            XmlAttribHex(sb, "TotalPromotedSize1", TotalPromotedSize1);
            XmlAttribHex(sb, "GenerationSize2", GenerationSize2);
            XmlAttribHex(sb, "TotalPromotedSize2", TotalPromotedSize2);
            XmlAttribHex(sb, "GenerationSize3", GenerationSize3);
            XmlAttribHex(sb, "TotalPromotedSize3", TotalPromotedSize3);
            XmlAttribHex(sb, "GenerationSize4", GenerationSize4);
            XmlAttribHex(sb, "TotalPromotedSize4", TotalPromotedSize4);
            XmlAttribHex(sb, "FinalizationPromotedSize", FinalizationPromotedSize);
            XmlAttrib(sb, "FinalizationPromotedCount", FinalizationPromotedCount);
            XmlAttrib(sb, "PinnedObjectCount", PinnedObjectCount);
            XmlAttrib(sb, "SinkBlockCount", SinkBlockCount);
            XmlAttrib(sb, "GCHandleCount", GCHandleCount);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "TotalHeapSize", "TotalPromoted", "Depth", "GenerationSize0", "TotalPromotedSize0", "GenerationSize1", "TotalPromotedSize1", "GenerationSize2", "TotalPromotedSize2", "GenerationSize3", "TotalPromotedSize3", "FinalizationPromotedSize", "FinalizationPromotedCount", "PinnedObjectCount", "SinkBlockCount", "GCHandleCount", "ClrInstanceID", "GenerationSize4", "TotalPromotedSize4" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return TotalHeapSize;
                case 1:
                    return TotalPromoted;
                case 2:
                    return Depth;
                case 3:
                    return GenerationSize0;
                case 4:
                    return TotalPromotedSize0;
                case 5:
                    return GenerationSize1;
                case 6:
                    return TotalPromotedSize1;
                case 7:
                    return GenerationSize2;
                case 8:
                    return TotalPromotedSize2;
                case 9:
                    return GenerationSize3;
                case 10:
                    return TotalPromotedSize3;
                case 11:
                    return FinalizationPromotedSize;
                case 12:
                    return FinalizationPromotedCount;
                case 13:
                    return PinnedObjectCount;
                case 14:
                    return SinkBlockCount;
                case 15:
                    return GCHandleCount;
                case 16:
                    return ClrInstanceID;
                case 17:
                    return GenerationSize4;
                case 18:
                    return TotalPromotedSize4;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<GCHeapStatsTraceData> Action;
        #endregion
    }
    public sealed class GCCreateSegmentTraceData : TraceEvent
    {
        public ulong Address { get { return (Address)GetInt64At(0); } }
        public ulong Size { get { return (Address)GetInt64At(8); } }
        public GCSegmentType Type { get { return (GCSegmentType)GetInt32At(16); } }
        public int ClrInstanceID { get { if (Version >= 1) { return GetInt16At(20); } return 0; } }

        #region Private
        internal GCCreateSegmentTraceData(Action<GCCreateSegmentTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<GCCreateSegmentTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength < 20));      // HAND_MODIFIED V0 has 24  because of C++ rounding
            Debug.Assert(!(Version == 1 && EventDataLength != 22));
            Debug.Assert(!(Version > 1 && EventDataLength < 22));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "Address", Address);
            XmlAttribHex(sb, "Size", Size);
            XmlAttrib(sb, "Type", Type);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Address", "Size", "Type", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Address;
                case 1:
                    return Size;
                case 2:
                    return Type;
                case 3:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<GCCreateSegmentTraceData> Action;
        #endregion
    }
    public sealed class GCFreeSegmentTraceData : TraceEvent
    {
        public long Address { get { return GetInt64At(0); } }
        public int ClrInstanceID { get { if (Version >= 1) { return GetInt16At(8); } return 0; } }

        #region Private
        internal GCFreeSegmentTraceData(Action<GCFreeSegmentTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<GCFreeSegmentTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 8));
            Debug.Assert(!(Version == 1 && EventDataLength != 10));
            Debug.Assert(!(Version > 1 && EventDataLength < 10));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "Address", Address);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Address", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Address;
                case 1:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<GCFreeSegmentTraceData> Action;
        #endregion
    }
    public sealed class GCSuspendEETraceData : TraceEvent
    {
        public GCSuspendEEReason Reason { get { if (Version >= 1) { return (GCSuspendEEReason)GetInt32At(0); } return (GCSuspendEEReason)GetInt16At(0); } }
        public int Count { get { if (Version >= 1) { return GetInt32At(4); } return 0; } }
        public int ClrInstanceID { get { if (Version >= 1) { return GetInt16At(8); } return 0; } }

        #region Private
        internal GCSuspendEETraceData(Action<GCSuspendEETraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<GCSuspendEETraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength < 2));       // HAND_MODIFIED 
            Debug.Assert(!(Version == 1 && EventDataLength != 10));
            Debug.Assert(!(Version > 1 && EventDataLength < 10));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "Reason", Reason);
            XmlAttrib(sb, "Count", Count);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Reason", "Count", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Reason;
                case 1:
                    return Count;
                case 2:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<GCSuspendEETraceData> Action;
        #endregion
    }
    public sealed class ExceptionHandlingTraceData : TraceEvent
    {
        public long EntryEIP { get { return GetInt64At(0); } }
        public long MethodID { get { return GetInt64At(8); } }
        public string MethodName { get { return GetUnicodeStringAt(16); } }
        public int ClrInstanceID { get { return GetInt16At(SkipUnicodeString(16)); } }

        #region Private
        internal ExceptionHandlingTraceData(Action<ExceptionHandlingTraceData> target, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            m_target = target;
        }
        protected internal override void Dispatch()
        {
            m_target(this);
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUnicodeString(16) + 2));
            Debug.Assert(!(Version > 0 && EventDataLength < SkipUnicodeString(16) + 2));
        }
        protected internal override Delegate Target
        {
            get { return m_target; }
            set { m_target = (Action<ExceptionHandlingTraceData>)value; }
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "EntryEIP", EntryEIP);
            XmlAttrib(sb, "MethodID", MethodID);
            XmlAttrib(sb, "MethodName", MethodName);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "EntryEIP", "MethodID", "MethodName", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return EntryEIP;
                case 1:
                    return MethodID;
                case 2:
                    return MethodName;
                case 3:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ExceptionHandlingTraceData> m_target;
        #endregion
    }

    public sealed class GCAllocationTickTraceData : TraceEvent
    {
        public int AllocationAmount { get { return GetInt32At(0); } }
        public GCAllocationKind AllocationKind { get { return (GCAllocationKind)GetInt32At(4); } }
        public int ClrInstanceID { get { if (Version >= 1) { return GetInt16At(8); } return 0; } }
        public long AllocationAmount64 { get { if (Version >= 2) { return GetInt64At(10); } return 0; } }
        public Address TypeID { get { if (Version >= 2) { return GetAddressAt(18); } return 0; } }
        public string TypeName { get { if (Version >= 2) { return GetUnicodeStringAt(18 + PointerSize); } return ""; } }
        public int HeapIndex { get { if (Version >= 2) { return GetInt32At(SkipUnicodeString(18 + PointerSize)); } return 0; } }
        public Address Address { get { if (Version >= 3) { return GetAddressAt(SkipUnicodeString(HostOffset(22, 1)) + 4); } return 0; } }
        #region Private
        internal GCAllocationTickTraceData(Action<GCAllocationTickTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<GCAllocationTickTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 8));
            Debug.Assert(!(Version == 1 && EventDataLength != 10));
            Debug.Assert(!(Version > 1 && EventDataLength < 10));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "AllocationAmount", AllocationAmount);
            XmlAttrib(sb, "AllocationKind", AllocationKind);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            if (Version >= 2)
            {
                XmlAttrib(sb, "AllocationAmount64", AllocationAmount64);
                XmlAttrib(sb, "TypeID", TypeID);
                XmlAttrib(sb, "TypeName", TypeName);
                XmlAttrib(sb, "HeapIndex", HeapIndex);
                if (Version >= 3)
                {
                    XmlAttribHex(sb, "Address", Address);
                }
            }
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "AllocationAmount", "AllocationKind", "ClrInstanceID", "AllocationAmount64", "TypeID", "TypeName", "HeapIndex", "Address" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return AllocationAmount;
                case 1:
                    return AllocationKind;
                case 2:
                    return ClrInstanceID;
                case 3:
                    return AllocationAmount64;
                case 4:
                    return TypeID;
                case 5:
                    return TypeName;
                case 6:
                    return HeapIndex;
                case 7:
                    return Address;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private const int OneKB = 1024;
        private const int OneMB = OneKB * OneKB;

        public long GetAllocAmount(ref bool seenBadAllocTick)
        {
            // We get bad values in old runtimes.   once we see a bad value 'fix' all values. 
            // TODO warn the user...
            long amount = AllocationAmount64; // AllocationAmount is truncated for allocation larger than 2Gb, use 64-bit value if available.

            if (amount == 0)
            {
                amount = AllocationAmount;
            }

            if (amount < 0)
            {
                seenBadAllocTick = true;
            }

            if (seenBadAllocTick)
            {
                // Clap this between 90K and 110K (for small objs) and 90K to 2Meg (for large obects).  
                amount = Math.Max(amount, 90 * OneKB);
                amount = Math.Min(amount, (AllocationKind == GCAllocationKind.Small) ? 110 * OneKB : 2 * OneMB);
            }

            return amount;
        }

        private event Action<GCAllocationTickTraceData> Action;
        #endregion
    }
    public sealed class GCCreateConcurrentThreadTraceData : TraceEvent
    {
        public int ClrInstanceID { get { if (Version >= 1) { return GetInt16At(0); } return 0; } }

        #region Private
        internal GCCreateConcurrentThreadTraceData(Action<GCCreateConcurrentThreadTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<GCCreateConcurrentThreadTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 1 && EventDataLength != 2));
            Debug.Assert(!(Version > 1 && EventDataLength < 2));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<GCCreateConcurrentThreadTraceData> Action;
        #endregion
    }
    public sealed class GCTerminateConcurrentThreadTraceData : TraceEvent
    {
        public int ClrInstanceID { get { if (Version >= 1) { return GetInt16At(0); } return 0; } }

        #region Private
        internal GCTerminateConcurrentThreadTraceData(Action<GCTerminateConcurrentThreadTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<GCTerminateConcurrentThreadTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 1 && EventDataLength != 2));
            Debug.Assert(!(Version > 1 && EventDataLength < 2));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<GCTerminateConcurrentThreadTraceData> Action;
        #endregion
    }
    public sealed class GCFinalizersEndTraceData : TraceEvent
    {
        public int Count { get { return GetInt32At(0); } }
        public int ClrInstanceID { get { if (Version >= 1) { return GetInt16At(4); } return 0; } }

        #region Private
        internal GCFinalizersEndTraceData(Action<GCFinalizersEndTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<GCFinalizersEndTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 4));
            Debug.Assert(!(Version == 1 && EventDataLength != 6));
            Debug.Assert(!(Version > 1 && EventDataLength < 6));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "Count", Count);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Count", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Count;
                case 1:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<GCFinalizersEndTraceData> Action;
        #endregion
    }

    public sealed class MethodDetailsTraceData : TraceEvent
    {
        public long MethodID { get { return GetInt64At(0); } }
        public long TypeID { get { return GetInt64At(8); } }
        public int MethodToken { get { return GetInt32At(16); } }
        public int TypeParameterCount { get { return GetInt32At(20); } }
        public long LoaderModuleID { get { return GetInt64At(24); } }
        public long TypeParameters(int arrayIndex) { return GetInt64At(32 + (arrayIndex * HostOffset(8, 0))); }

        #region Private
        internal MethodDetailsTraceData(Action<MethodDetailsTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 0 + (TypeParameterCount * 8) + 36));
            Debug.Assert(!(Version > 0 && EventDataLength < 0 + (TypeParameterCount * 8) + 36));
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<MethodDetailsTraceData>)value; }
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "MethodID", MethodID);
            XmlAttrib(sb, "TypeID", TypeID);
            XmlAttrib(sb, "MethodToken", MethodToken);
            XmlAttrib(sb, "TypeParameterCount", TypeParameterCount);
            XmlAttrib(sb, "LoaderModuleID", LoaderModuleID);
            string typeParams = "";
            if (TypeParameterCount != 0)
            {
                StringBuilder typeParamsBuilder = new StringBuilder();
                for (int i = 0; i < TypeParameterCount; i++)
                {
                    if (typeParamsBuilder.Length != 0)
                        typeParamsBuilder.Append(',');
                    typeParamsBuilder.Append(TypeParameters(i).ToString("x"));
                }
                typeParams = typeParamsBuilder.ToString();
            }
            XmlAttrib(sb, "TypeParameters", typeParams);
            sb.Append("/>");
            return sb;
        }
        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[] { "MethodID", "TypeID", "MethodToken", "TypeParameterCount", "LoaderModuleID", "TypeParameters" };
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return MethodID;
                case 1:
                    return TypeID;
                case 2:
                    return MethodToken;
                case 4:
                    return TypeParameterCount;
                case 5:
                    return LoaderModuleID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        public static ulong GetKeywords() { return 0x4000000000; }
        public static string GetProviderName() { return "Microsoft-Windows-DotNETRuntime"; }
        public static Guid GetProviderGuid() { return new Guid("e13c0d23-ccbc-4e12-931b-d9cc2eee27e4"); }
        private event Action<MethodDetailsTraceData> Action;
        #endregion
    }
    public sealed class GCBulkTypeTraceData : TraceEvent
    {
        public int Count { get { return GetInt32At(0); } }
        public int ClrInstanceID { get { return GetInt16At(4); } }

        /// <summary>
        /// Returns the edge at the given zero-based index (index less than Count).   The returned BulkTypeValues 
        /// points the the data in GCBulkRootEdgeTraceData so it cannot live beyond that lifetime.  
        /// </summary>
        public GCBulkTypeValues Values(int index) { return new GCBulkTypeValues(this, OffsetForIndexInValuesArray(index)); }

        #region Private
        internal GCBulkTypeTraceData(Action<GCBulkTypeTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            m_lastOffset = 6;           // Initialize it to something valid
        }
        protected internal override void Dispatch()
        {
            m_lastIdx = 0xFFFF; // Invalidate the cache
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<GCBulkTypeTraceData>)value; }
        }
        protected internal override void Validate()
        {
            m_lastIdx = 0xFFFF; // Invalidate the cache     
            Debug.Assert(!(Version == 0 && EventDataLength != OffsetForIndexInValuesArray(Count)));
            Debug.Assert(Count == 0 || Values(Count - 1).TypeParameterCount < 256);     // This just makes the asserts in the BulkType kick in 
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "Count", Count);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.AppendLine(">");
            for (int i = 0; i < Count; i++)
            {
                Values(i).ToXml(sb).AppendLine();
            }

            sb.Append("</Event>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Count", "ClrInstanceID", "TypeID_0", "TypeName_0" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Count;
                case 1:
                    return ClrInstanceID;
                case 2:
                    if (Count == 0)
                    {
                        return 0;
                    }

                    return Values(0).TypeID;
                case 3:
                    if (Count == 0)
                    {
                        return 0;
                    }

                    return Values(0).TypeName;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private int OffsetForIndexInValuesArray(int targetIdx)
        {
            Debug.Assert(targetIdx <= Count);
            int offset;
            int idx;
            if (m_lastIdx <= targetIdx)
            {
                idx = m_lastIdx;
                offset = m_lastOffset;
            }
            else
            {
                idx = 0;
                offset = 6;
            }
            Debug.Assert(6 <= offset);
            while (idx < targetIdx)
            {
                // Get to to type parameter count 
                int typeParamCountOffset = SkipUnicodeString(offset + 25);
                // fetch it
                int typeParamCount = GetInt32At(typeParamCountOffset);
                Debug.Assert(typeParamCount < 256);
                // skip the count and the type parameters.  
                offset = typeParamCount * 8 + 4 + typeParamCountOffset;
                idx++;
            }
            Debug.Assert(offset <= EventDataLength);
            m_lastIdx = (ushort)targetIdx;
            m_lastOffset = (ushort)offset;
            Debug.Assert(idx == targetIdx);
            Debug.Assert(m_lastIdx == targetIdx && m_lastOffset == offset);     // No truncation
            return offset;
        }
        private ushort m_lastIdx;
        private ushort m_lastOffset;

        private event Action<GCBulkTypeTraceData> Action;

        #endregion
    }

    /// <summary>
    /// This structure just POINTS at the data in the BulkTypeTraceData.  It can only be used as long as
    /// the BulkTypeTraceData is alive which (unless you cloned it) is only for the lifetime of the callback.  
    /// </summary>
    public struct GCBulkTypeValues
    {
        /// <summary>
        /// On the desktop this is the Method Table Pointer
        /// In project N this is the pointer to the EE Type
        /// </summary>
        public Address TypeID { get { return m_data.GetAddressAt(m_baseOffset); } }
        /// <summary>
        /// For Desktop this is the Module*
        /// For project N it is image base for the module that the type lives in?
        /// </summary>
        public Address ModuleID { get { return m_data.GetAddressAt(m_baseOffset + 8); } }
        /// <summary>
        /// On desktop this is the Meta-data token?
        /// On project N it is the RVA of the typeID
        /// </summary>
        public int TypeNameID { get { return m_data.GetInt32At(m_baseOffset + 16); } }
        public TypeFlags Flags { get { return (TypeFlags)m_data.GetInt32At(m_baseOffset + 20); } }
        public byte CorElementType { get { return (byte)m_data.GetByteAt(m_baseOffset + 24); } }

        /// <summary>
        /// Note that this method returns the type name with generic parameters in .NET Runtime
        /// syntax   e.g. System.WeakReference`1[System.Diagnostics.Tracing.EtwSession]
        /// </summary>
        public string TypeName { get { return m_data.GetUnicodeStringAt(m_baseOffset + 25); } }

        public int TypeParameterCount
        {
            get
            {
                if (m_typeParamCountOffset == 0)
                {
                    m_typeParamCountOffset = (ushort)m_data.SkipUnicodeString(m_baseOffset + 25);
                }

                int ret = m_data.GetInt32At(m_typeParamCountOffset);
                Debug.Assert(0 <= ret && ret <= 128);           // Not really true, but it deserves investigation.  
                return ret;
            }
        }
        public Address TypeParameterID(int index)
        {
            if (m_typeParamCountOffset == 0)
            {
                m_typeParamCountOffset = (ushort)m_data.SkipUnicodeString(m_baseOffset + 25);
            }

            return m_data.GetAddressAt(m_typeParamCountOffset + 4 + index * 8);
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            return ToXml(sb).ToString();
        }
        public StringBuilder ToXml(StringBuilder sb)
        {
            sb.Append(" <GCBulkTypeValue ");
            TraceEvent.XmlAttrib(sb, "TypeName", TypeName);
            TraceEvent.XmlAttribHex(sb, "TypeID", TypeID);
            TraceEvent.XmlAttribHex(sb, "ModuleID", ModuleID);
            TraceEvent.XmlAttribHex(sb, "TypeNameID", TypeNameID).AppendLine().Append("  ");
            TraceEvent.XmlAttrib(sb, "Flags", Flags);
            TraceEvent.XmlAttrib(sb, "CorElementType", CorElementType);
            TraceEvent.XmlAttrib(sb, "TypeParameterCount", TypeParameterCount);
            sb.Append("/>");
            // TODO display the type parameters IDs
            return sb;
        }
        #region private
        internal GCBulkTypeValues(TraceEvent data, int baseOffset)
        {
            m_data = data; m_baseOffset = (ushort)baseOffset; m_typeParamCountOffset = 0;
            Debug.Assert(CorElementType < 64);
            Debug.Assert(0 <= CorElementType && CorElementType <= 128);           // Sanity checks.  Not really true, but it deserves investigation.  
            Debug.Assert(0 <= TypeParameterCount && TypeParameterCount < 128);
            Debug.Assert((TypeID & 0xFF00000000000001L) == 0);
            Debug.Assert((ModuleID & 0xFF00000000000003L) == 0);
            Debug.Assert((((int)Flags) & 0xFFFFFF00) == 0);
        }

        private TraceEvent m_data;
        private ushort m_baseOffset;
        private ushort m_typeParamCountOffset;
        #endregion
    }

    public sealed class GCBulkRootEdgeTraceData : TraceEvent
    {
        public int Index { get { return GetInt32At(0); } }
        public int Count { get { return GetInt32At(4); } }
        public int ClrInstanceID { get { return GetInt16At(8); } }

        /// <summary>
        /// Returns the edge at the given zero-based index (index less than Count).   The returned GCBulkRootEdgeValues
        /// points the the data in GCBulkRootEdgeTraceData so it cannot live beyond that lifetime.  
        /// </summary>
        public GCBulkRootEdgeValues Values(int index) { return new GCBulkRootEdgeValues(this, 10 + index * HostOffset(13, 2)); }
        #region Private
        internal GCBulkRootEdgeTraceData(Action<GCBulkRootEdgeTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<GCBulkRootEdgeTraceData>)value; }
        }
        protected internal override void Validate()
        {
            // The != 12 was added because I think we accidentally used the same event ID for a update of the runtime. 
            Debug.Assert(!(Version == 0 && EventDataLength != (Count * HostOffset(13, 2)) + 10 && EventDataLength != 12));
            Debug.Assert(!(Version > 0 && EventDataLength < (Count * HostOffset(13, 2)) + 10));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "Index", Index);
            XmlAttrib(sb, "Count", Count);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.AppendLine(">");
            for (int i = 0; i < Count; i++)
            {
                Values(i).ToXml(sb).AppendLine();
            }

            sb.Append("</Event>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Index", "Count", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Index;
                case 1:
                    return Count;
                case 2:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<GCBulkRootEdgeTraceData> Action;
        #endregion
    }

    /// <summary>
    /// This structure just POINTS at the data in the GCBulkEdgeTraceData.  It can only be used as long as
    /// the GCBulkEdgeTraceData is alive which (unless you cloned it) is only for the lifetime of the callback.  
    /// </summary>
    public struct GCBulkRootEdgeValues
    {
        public Address RootedNodeAddress { get { return m_data.GetAddressAt(m_baseOffset); } }
        public GCRootKind GCRootKind { get { return (GCRootKind)m_data.GetByteAt(m_data.HostOffset(m_baseOffset + 4, 1)); } }
        public GCRootFlags GCRootFlag { get { return (GCRootFlags)m_data.GetInt32At(m_data.HostOffset(m_baseOffset + 5, 1)); } }
        public Address GCRootID { get { return m_data.GetAddressAt(m_data.HostOffset(m_baseOffset + 9, 1)); } }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            return ToXml(sb).ToString();
        }
        public StringBuilder ToXml(StringBuilder sb)
        {
            sb.Append(" <GCBulkRootEdgeValue ");
            TraceEvent.XmlAttribHex(sb, "RootedNodeAddress", RootedNodeAddress);
            TraceEvent.XmlAttrib(sb, "GCRootKind", GCRootKind);
            TraceEvent.XmlAttrib(sb, "GCRootFlag", GCRootFlag);
            TraceEvent.XmlAttribHex(sb, "GCRootID", GCRootID);
            sb.Append("/>");
            return sb;
        }
        #region private
        internal GCBulkRootEdgeValues(TraceEvent data, int baseOffset)
        {
            m_data = data; m_baseOffset = baseOffset;
            Debug.Assert((RootedNodeAddress & 0xFF00000000000003L) == 0);
            Debug.Assert((GCRootID & 0xFF00000000000003L) == 0);
            Debug.Assert((int)GCRootFlag < 256);
        }

        private TraceEvent m_data;
        private int m_baseOffset;
        #endregion
    }

    public sealed class GCBulkRootConditionalWeakTableElementEdgeTraceData : TraceEvent
    {
        public int Index { get { return GetInt32At(0); } }
        public int Count { get { return GetInt32At(4); } }
        public int ClrInstanceID { get { return GetInt16At(8); } }

        /// <summary>
        /// Returns the range at the given zero-based index (index less than Count).   The returned GCBulkRootConditionalWeakTableElementEdgeValues 
        /// points the the data in GCBulkRootConditionalWeakTableElementEdgeTraceData so it cannot live beyond that lifetime.  
        /// </summary>
        public GCBulkRootConditionalWeakTableElementEdgeValues Values(int index)
        {
            return new GCBulkRootConditionalWeakTableElementEdgeValues(this, 10 + (index * HostOffset(12, 3)));
        }
        #region Private
        internal GCBulkRootConditionalWeakTableElementEdgeTraceData(Action<GCBulkRootConditionalWeakTableElementEdgeTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<GCBulkRootConditionalWeakTableElementEdgeTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != Count * HostOffset(12, 3) + 10));
            Debug.Assert(!(Version > 0 && EventDataLength < Count * HostOffset(12, 3) + 10));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "Index", Index);
            XmlAttrib(sb, "Count", Count);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.AppendLine(">");
            for (int i = 0; i < Count; i++)
            {
                Values(i).ToXml(sb).AppendLine();
            }

            sb.Append("</Event>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Index", "Count", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Index;
                case 1:
                    return Count;
                case 2:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<GCBulkRootConditionalWeakTableElementEdgeTraceData> Action;
        #endregion
    }

    /// <summary>
    /// This structure just POINTS at the data in the GCBulkRootConditionalWeakTableElementEdgeTraceData.  It can only be used as long as
    /// the GCBulkRootConditionalWeakTableElementEdgeTraceData is alive which (unless you cloned it) is only for the lifetime of the callback.  
    /// </summary>
    public struct GCBulkRootConditionalWeakTableElementEdgeValues
    {
        public Address GCKeyNodeID { get { return m_data.GetAddressAt(m_baseOffset); } }
        public Address GCValueNodeID { get { return m_data.GetAddressAt(m_data.HostOffset(m_baseOffset + 4, 1)); } }
        public Address GCRootID { get { return m_data.GetAddressAt(m_data.HostOffset(m_baseOffset + 8, 2)); } }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            return ToXml(sb).ToString();
        }
        public StringBuilder ToXml(StringBuilder sb)
        {
            sb.Append(" <GCBulkRootConditionalWeakTableElementEdgeValue ");
            TraceEvent.XmlAttribHex(sb, "GCKeyNodeID", GCKeyNodeID);
            TraceEvent.XmlAttribHex(sb, "GCValueNodeID", GCValueNodeID);
            TraceEvent.XmlAttribHex(sb, "GCRootID", GCRootID);
            sb.Append("/>");
            return sb;
        }
        #region private
        internal GCBulkRootConditionalWeakTableElementEdgeValues(TraceEvent data, int baseOffset)
        {
            m_data = data; m_baseOffset = baseOffset;
            Debug.Assert((GCKeyNodeID & 0xFF00000000000003L) == 0);
            Debug.Assert((GCValueNodeID & 0xFF00000000000003L) == 0);
            Debug.Assert((GCRootID & 0xFF00000000000003L) == 0);
        }

        private TraceEvent m_data;
        private int m_baseOffset;
        #endregion
    }

    public sealed class GCBulkNodeTraceData : TraceEvent
    {
        public int Index { get { return GetInt32At(0); } }
        public int Count { get { return GetInt32At(4); } }
        public int ClrInstanceID { get { return GetInt16At(8); } }

        /// <summary>
        /// Returns the node at the given zero-based index (idx less than Count).   The returned GCBulkNodeNodes 
        /// points the the data in GCBulkNodeTraceData so it cannot live beyond that lifetime.  
        /// </summary>
        public GCBulkNodeValues Values(int index) { return new GCBulkNodeValues(this, 10 + index * HostOffset(28, 1)); }

        /// <summary>
        /// This unsafe interface may go away.   Use the 'Nodes(idx)' instead 
        /// </summary>
        public unsafe GCBulkNodeUnsafeNodes* UnsafeNodes(int arrayIdx, GCBulkNodeUnsafeNodes* buffer)
        {
            Debug.Assert(0 <= arrayIdx && arrayIdx < Count);
            GCBulkNodeUnsafeNodes* ret;
            if (PointerSize != 8)
            {
                GCBulkNodeUnsafeNodes32* basePtr = (GCBulkNodeUnsafeNodes32*)(((byte*)DataStart) + 10);
                GCBulkNodeUnsafeNodes32* value = basePtr + arrayIdx;

                buffer->Address = value->Address;
                buffer->Size = value->Size;
                buffer->TypeID = value->TypeID;
                buffer->EdgeCount = value->EdgeCount;
                ret = buffer;
            }
            else
            {
                GCBulkNodeUnsafeNodes* basePtr = (GCBulkNodeUnsafeNodes*)(((byte*)DataStart) + 10);
                ret = basePtr + arrayIdx;
            }
            Debug.Assert((ret->Address & 0xFF00000000000003L) == 0);
            Debug.Assert((ret->TypeID & 0xFF00000000000001L) == 0);
            Debug.Assert(ret->Size < 0x80000000L);
            Debug.Assert(ret->EdgeCount < 100000);
            return ret;
        }
        #region Private
        internal GCBulkNodeTraceData(Action<GCBulkNodeTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<GCBulkNodeTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != (Count * HostOffset(28, 1)) + 10));
            Debug.Assert(!(Version > 0 && EventDataLength < (Count * HostOffset(28, 1)) + 10));
            Debug.Assert(Count == 0 || Values(Count - 1).EdgeCount < 100000);            // THis is to let the GCBulkNodeValues asserts kick in
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "Index", Index);
            XmlAttrib(sb, "Count", Count);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.AppendLine(">");
            for (int i = 0; i < Count; i++)
            {
                Values(i).ToXml(sb).AppendLine();
            }

            sb.Append("</Event>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Index", "Count", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Index;
                case 1:
                    return Count;
                case 2:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<GCBulkNodeTraceData> Action;
        #endregion
    }

    /// <summary>
    /// This structure just POINTS at the data in the GCBulkNodeTraceData.  It can only be used as long as
    /// the GCBulkNodeTraceData is alive which (unless you cloned it) is only for the lifetime of the callback.  
    /// </summary>
    public struct GCBulkNodeValues
    {
        public Address Address { get { return m_data.GetAddressAt(m_baseOffset); } }
        public Address Size { get { return m_data.GetAddressAt(m_data.HostOffset(m_baseOffset + 4, 1)); } }
        public Address TypeID { get { return m_data.GetAddressAt(m_data.HostOffset(m_baseOffset + 12, 1)); } }
        public long EdgeCount { get { return m_data.GetInt64At(m_data.HostOffset(m_baseOffset + 20, 1)); } }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            return ToXml(sb).ToString();
        }
        public StringBuilder ToXml(StringBuilder sb)
        {
            sb.Append(" <GCBulkNodeValue ");
            TraceEvent.XmlAttribHex(sb, "Address", Address);
            TraceEvent.XmlAttribHex(sb, "Size", Size);
            TraceEvent.XmlAttribHex(sb, "TypeID", TypeID);
            TraceEvent.XmlAttrib(sb, "EdgeCount", EdgeCount);
            sb.Append("/>");
            return sb;
        }
        #region private
        internal GCBulkNodeValues(TraceEvent data, int baseOffset)
        {
            m_data = data; m_baseOffset = baseOffset;
            Debug.Assert((Address & 0xFF00000000000003L) == 0);
            Debug.Assert((TypeID & 0xFF00000000000001L) == 0);
            Debug.Assert(Size < 0x80000000L);
            Debug.Assert(EdgeCount < 100000);
        }

        private TraceEvent m_data;
        private int m_baseOffset;
        #endregion
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct GCBulkNodeUnsafeNodes32
    {
        public uint Address;
        public Address Size;
        public Address TypeID;
        public long EdgeCount;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct GCBulkNodeUnsafeNodes
    {
        public Address Address;
        public Address Size;
        public Address TypeID;
        public long EdgeCount;
    }

    public sealed class GCBulkEdgeTraceData : TraceEvent
    {
        public int Index { get { return GetInt32At(0); } }
        public int Count { get { return GetInt32At(4); } }
        public int ClrInstanceID { get { return GetInt16At(8); } }

        /// <summary>
        /// Returns the 'idx' th edge.  
        /// The returned GCBulkEdgeEdges cannot live beyond the TraceEvent that it comes from.  
        /// </summary>
        public GCBulkEdgeValues Values(int index) { return new GCBulkEdgeValues(this, 10 + (index * HostOffset(8, 1))); }

        #region Private
        internal GCBulkEdgeTraceData(Action<GCBulkEdgeTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<GCBulkEdgeTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != Count * HostOffset(8, 1) + 10));
            Debug.Assert(!(Version > 0 && EventDataLength < Count * HostOffset(8, 1) + 10));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "Index", Index);
            XmlAttrib(sb, "Count", Count);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.AppendLine(">");
            for (int i = 0; i < Count; i++)
            {
                Values(i).ToXml(sb).AppendLine();
            }

            sb.Append("</Event>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Index", "Count", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Index;
                case 1:
                    return Count;
                case 2:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<GCBulkEdgeTraceData> Action;
        #endregion
    }

    /// <summary>
    /// This structure just POINTS at the data in the GCBulkNodeTraceData.  It can only be used as long as
    /// the GCBulkNodeTraceData is alive which (unless you cloned it) is only for the lifetime of the callback.  
    /// </summary>
    public struct GCBulkEdgeValues
    {
        public Address Target { get { return m_data.GetAddressAt(m_baseOffset); } }
        public int ReferencingField { get { return m_data.GetInt32At(m_data.HostOffset(m_baseOffset + 4, 1)); } }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            return ToXml(sb).ToString();
        }
        public StringBuilder ToXml(StringBuilder sb)
        {
            sb.Append(" <GCBulkEdgeValue ");
            TraceEvent.XmlAttribHex(sb, "Target", Target);
            TraceEvent.XmlAttrib(sb, "ReferencingField", ReferencingField);
            sb.Append("/>");
            return sb;
        }
        #region private
        internal GCBulkEdgeValues(TraceEvent data, int baseOffset)
        {
            m_data = data; m_baseOffset = baseOffset;
            Debug.Assert((Target & 0xFF00000000000003L) == 0);
            Debug.Assert(ReferencingField < 0x10000);
        }

        private TraceEvent m_data;
        private int m_baseOffset;
        #endregion
    }

    public sealed class GCSampledObjectAllocationTraceData : TraceEvent
    {
        public Address Address { get { return GetAddressAt(0); } }
        public Address TypeID { get { return GetAddressAt(HostOffset(4, 1)); } }
        public int ObjectCountForTypeSample { get { return GetInt32At(HostOffset(8, 2)); } }
        public long TotalSizeForTypeSample { get { return GetInt64At(HostOffset(12, 2)); } }
        public int ClrInstanceID { get { return GetInt16At(HostOffset(20, 2)); } }

        #region Private
        internal GCSampledObjectAllocationTraceData(Action<GCSampledObjectAllocationTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<GCSampledObjectAllocationTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(22, 2)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(22, 2)));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "Address", Address);
            XmlAttribHex(sb, "TypeID", TypeID);
            XmlAttrib(sb, "ObjectCountForTypeSample", ObjectCountForTypeSample);
            XmlAttrib(sb, "TotalSizeForTypeSample", TotalSizeForTypeSample);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Address", "TypeID", "ObjectCountForTypeSample", "TotalSizeForTypeSample", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Address;
                case 1:
                    return TypeID;
                case 2:
                    return ObjectCountForTypeSample;
                case 3:
                    return TotalSizeForTypeSample;
                case 4:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<GCSampledObjectAllocationTraceData> Action;
        #endregion
    }
    public sealed class GCBulkSurvivingObjectRangesTraceData : TraceEvent
    {
        public int Index { get { return GetInt32At(0); } }
        public int Count { get { return GetInt32At(4); } }
        public int ClrInstanceID { get { return GetInt16At(8); } }

        /// <summary>
        /// Returns the range at the given zero-based index (index less than Count).   The returned GCBulkSurvivingObjectRangesValues 
        /// points the the data in GCBulkSurvivingObjectRangesTraceData so it cannot live beyond that lifetime.  
        /// </summary>
        public GCBulkSurvivingObjectRangesValues Values(int index) { return new GCBulkSurvivingObjectRangesValues(this, 10 + (index * HostOffset(12, 1))); }
        #region Private
        internal GCBulkSurvivingObjectRangesTraceData(Action<GCBulkSurvivingObjectRangesTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<GCBulkSurvivingObjectRangesTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 10 + (Count * HostOffset(12, 1))));
            Debug.Assert(!(Version > 0 && EventDataLength < 10 + (Count * HostOffset(12, 1))));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "Index", Index);
            XmlAttrib(sb, "Count", Count);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.AppendLine(">");
            for (int i = 0; i < Count; i++)
            {
                Values(i).ToXml(sb).AppendLine();
            }

            sb.Append("</Event>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Index", "Count", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Index;
                case 1:
                    return Count;
                case 2:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<GCBulkSurvivingObjectRangesTraceData> Action;
        #endregion
    }

    /// <summary>
    /// This structure just POINTS at the data in the GCBulkEdgeTraceData.  It can only be used as long as
    /// the GCBulkEdgeTraceData is alive which (unless you cloned it) is only for the lifetime of the callback.  
    /// </summary>
    public struct GCBulkSurvivingObjectRangesValues
    {
        public Address RangeBase { get { return m_data.GetAddressAt(m_baseOffset); } }
        public Address RangeLength { get { return (Address)m_data.GetInt64At(m_data.HostOffset(m_baseOffset + 4, 1)); } }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            return ToXml(sb).ToString();
        }
        public StringBuilder ToXml(StringBuilder sb)
        {
            sb.Append(" <GCBulkMovedObjectRangesValues ");
            TraceEvent.XmlAttribHex(sb, "RangeBase", RangeBase);
            TraceEvent.XmlAttribHex(sb, "RangeLength", RangeLength);
            sb.Append("/>");
            return sb;
        }
        #region private
        internal GCBulkSurvivingObjectRangesValues(TraceEvent data, int baseOffset)
        {
            m_data = data; m_baseOffset = baseOffset;
            Debug.Assert((RangeBase & 0xFF00000000000003L) == 0);
            Debug.Assert((RangeLength & 0xFFFFFFF000000003L) == 0);
        }

        private TraceEvent m_data;
        private int m_baseOffset;
        #endregion
    }

    public sealed class GCBulkMovedObjectRangesTraceData : TraceEvent
    {
        public int Index { get { return GetInt32At(0); } }
        public int Count { get { return GetInt32At(4); } }
        public int ClrInstanceID { get { return GetInt16At(8); } }

        /// <summary>
        /// Returns the range at the given zero-based index (index less than Count).   The returned GCBulkSurvivingObjectRangesValues 
        /// points the the data in GCBulkSurvivingObjectRangesTraceData so it cannot live beyond that lifetime.  
        /// </summary>
        public GCBulkMovedObjectRangesValues Values(int index) { return new GCBulkMovedObjectRangesValues(this, 10 + (index * HostOffset(16, 2))); }
        #region Private
        internal GCBulkMovedObjectRangesTraceData(Action<GCBulkMovedObjectRangesTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<GCBulkMovedObjectRangesTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(16, 2) * Count + 10));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(16, 2) * Count + 10));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "Index", Index);
            XmlAttrib(sb, "Count", Count);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.AppendLine(">");
            for (int i = 0; i < Count; i++)
            {
                Values(i).ToXml(sb).AppendLine();
            }

            sb.Append("</Event>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Index", "Count", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Index;
                case 1:
                    return Count;
                case 2:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<GCBulkMovedObjectRangesTraceData> Action;
        #endregion
    }

    /// <summary>
    /// This structure just POINTS at the data in the GCBulkEdgeTraceData.  It can only be used as long as
    /// the GCBulkEdgeTraceData is alive which (unless you cloned it) is only for the lifetime of the callback.  
    /// </summary>
    public struct GCBulkMovedObjectRangesValues
    {
        public Address OldRangeBase { get { return m_data.GetAddressAt(m_baseOffset); } }
        public Address NewRangeBase { get { return m_data.GetAddressAt(m_data.HostOffset(m_baseOffset + 4, 1)); } }
        public Address RangeLength { get { return (Address)m_data.GetInt64At(m_data.HostOffset(m_baseOffset + 8, 2)); } }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            return ToXml(sb).ToString();
        }
        public StringBuilder ToXml(StringBuilder sb)
        {
            sb.Append(" <GCBulkMovedObjectRangesValues ");
            TraceEvent.XmlAttribHex(sb, "OldRangeBase", OldRangeBase);
            TraceEvent.XmlAttribHex(sb, "NewRangeBase", NewRangeBase);
            TraceEvent.XmlAttribHex(sb, "RangeLength", RangeLength);
            sb.Append("/>");
            return sb;
        }
        #region private
        internal GCBulkMovedObjectRangesValues(TraceEvent data, int baseOffset)
        {
            m_data = data; m_baseOffset = baseOffset;
            Debug.Assert((OldRangeBase & 0xFF00000000000003L) == 0);
            Debug.Assert((NewRangeBase & 0xFF00000000000003L) == 0);
            Debug.Assert((RangeLength & 0xFFFFFFF000000003L) == 0);
        }

        private TraceEvent m_data;
        private int m_baseOffset;
        #endregion
    }

    public sealed class GCGenerationRangeTraceData : TraceEvent
    {
        public int Generation { get { return GetByteAt(0); } }
        public Address RangeStart { get { return GetAddressAt(1); } }
        public Address RangeUsedLength { get { return (Address)GetInt64At(HostOffset(5, 1)); } }
        public Address RangeReservedLength { get { return (Address)GetInt64At(HostOffset(13, 1)); } }
        public int ClrInstanceID { get { return GetInt16At(HostOffset(21, 1)); } }

        #region Private
        internal GCGenerationRangeTraceData(Action<GCGenerationRangeTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<GCGenerationRangeTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(23, 1)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(23, 1)));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "Generation", Generation);
            XmlAttribHex(sb, "RangeStart", RangeStart);
            XmlAttrib(sb, "RangeUsedLength", RangeUsedLength);
            XmlAttrib(sb, "RangeReservedLength", RangeReservedLength);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Generation", "RangeStart", "RangeUsedLength", "RangeReservedLength", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Generation;
                case 1:
                    return RangeStart;
                case 2:
                    return RangeUsedLength;
                case 3:
                    return RangeReservedLength;
                case 4:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<GCGenerationRangeTraceData> Action;
        #endregion
    }

    public enum MarkRootType
    {
        MarkStack = 0,
        MarkFQ = 1,
        MarkHandles = 2,
        MarkOlder = 3,
        MarkSizedRef = 4,
        MarkOverflow = 5,
        MarkMax = 6,
    }

    public sealed class GCMarkTraceData : TraceEvent
    {
        public int HeapNum { get { return GetInt32At(0); } }
        public int ClrInstanceID { get { return GetInt16At(4); } }

        #region Private
        internal GCMarkTraceData(Action<GCMarkTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<GCMarkTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 6));
            Debug.Assert(!(Version > 0 && EventDataLength < 6));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "HeapNum", HeapNum);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "HeapNum", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return HeapNum;
                case 1:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<GCMarkTraceData> Action;
        #endregion
    }
    public sealed class GCMarkWithTypeTraceData : TraceEvent
    {
        public int HeapNum { get { return GetInt32At(0); } }
        public int ClrInstanceID { get { return GetInt16At(4); } }
        public int Type { get { return GetInt32At(6); } }
        public long Promoted { get { return GetInt64At(10); } }

        #region Private
        internal GCMarkWithTypeTraceData(Action<GCMarkWithTypeTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<GCMarkWithTypeTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 18));
            Debug.Assert(!(Version > 0 && EventDataLength < 18));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "HeapNum", HeapNum);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            XmlAttrib(sb, "Type", Type);
            XmlAttrib(sb, "Promoted", Promoted);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "HeapNum", "ClrInstanceID", "Type", "Promoted" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return HeapNum;
                case 1:
                    return ClrInstanceID;
                case 2:
                    return Type;
                case 3:
                    return Promoted;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<GCMarkWithTypeTraceData> Action;
        #endregion
    }
    /// <summary>
    /// We keep Heap history for every Generation in 'Gens' 
    /// </summary>
    public enum Gens
    {
        Gen0,
        Gen1,
        Gen2,
        GenLargeObj,
        GenPinObj,
        MaxGenCount, 
    }

    /// <summary>
    /// Taken from gcrecords.h, used to differentiate heap expansion and compaction reasons
    /// </summary>
    public enum gc_heap_expand_mechanism : int
    {
        expand_reuse_normal = 0,
        expand_reuse_bestfit = 1,
        expand_new_seg_ep = 2, // new seg with ephemeral promotion
        expand_new_seg = 3,
        expand_no_memory = 4, // we can't get a new seg.
        expand_next_full_gc = 5,
        max_expand_mechanisms_count = 6,
        not_specified = 1024
    }
    public enum gc_heap_compact_reason : int
    {
        compact_low_ephemeral = 0,
        compact_high_frag = 1,
        compact_no_gaps = 2,
        compact_loh_forced = 3,
        compact_last_gc = 4,
        compact_induced_compacting = 5,
        compact_fragmented_gen0 = 6,
        compact_high_mem_load = 7,
        compact_high_mem_frag = 8,
        compact_vhigh_mem_frag = 9,
        compact_no_gc_mode = 10,
        max_compact_reasons_count = 11,
        not_specified = 1024
    }
    public enum gc_concurrent_compact_reason : int
    {
        concurrent_compact_high_frag = 0,
        concurrent_compact_c_mark = 1,
        max_concurrent_compat_reason = 2,
        not_specified = 1024
    }

    /// <summary>
    /// Version 0, PreciseVersion 0.1: Silverlight (x86)
    /// 0:041> dt -r2 coreclr!WKS::gc_history_per_heap
    ///    +0x000 gen_data         : [5] WKS::gc_generation_data
    ///       +0x000 size_before      : Uint4B/8B       : [0 - 40), [40 - 80), [80 - 120), [120 - 160), [160 - 200)
    ///       +0x004 size_after       : Uint4B/8B
    ///       +0x008 current_size     : Uint4B/8B
    ///       +0x00c previous_size    : Uint4B/8B
    ///       +0x010 fragmentation    : Uint4B/8B
    ///       +0x014 in               : Uint4B/8B
    ///       +0x018 out              : Uint4B/8B
    ///       +0x01c new_allocation   : Uint4B/8B
    ///       +0x020 surv             : Uint4B/8B
    ///       +0x024 growth           : Uint4B/8B
    ///    +0x0c8 mem_pressure        : Uint4B      : 200
    ///    +0x0cc mechanisms          : [2] Uint4B  : 204 (expand), 208 (compact)
    ///    +0x0d4 gen_condemn_reasons : Uint4B      : 212
    ///    +0x0d8 heap_index          : Uint4B      : 216
    ///  
    ///    clrInstanceId              : byte        : 220
    /// 
    /// Version 0, PreciseVersion 0.2: .NET 4.0
    /// 0:000> dt -r2 clr!WKS::gc_history_per_heap
    ///    +0x000 gen_data         : [5] WKS::gc_generation_data
    ///       +0x000 size_before      : Uint4B/8B      : [0 - 40), [40 - 80), [80 - 120), [120 - 160), [160 - 200)
    ///       +0x004 size_after       : Uint4B/8B
    ///       +0x008 current_size     : Uint4B/8B
    ///       +0x00c previous_size    : Uint4B/8B
    ///       +0x010 fragmentation    : Uint4B/8B
    ///       +0x014 in               : Uint4B/8B
    ///       +0x018 out              : Uint4B/8B
    ///       +0x01c new_allocation   : Uint4B/8B
    ///       +0x020 surv             : Uint4B/8B
    ///       +0x024 growth           : Uint4B/8B
    ///     +0x0c8 mem_pressure     : Uint4B        : 200
    ///     +0x0cc mechanisms       : [3] Uint4B    : 204 (expand), 208 (compact), 212 (concurrent_compact)
    ///    +0x0d8 gen_condemn_reasons : Uint4B      : 216
    ///    +0x0dc heap_index       : Uint4B         : 220
    ///    
    ///    clrInstanceId              : byte        : 224
    /// 
    /// vm\gcrecord.h
    /// Etw_GCDataPerHeapSpecial(...)
    /// ...
    ///     EventDataDescCreate(EventData[0], gc_data_per_heap, datasize);
    ///     EventDataDescCreate(EventData[1], ClrInstanceId, sizeof(ClrInstanceId));
    /// 
    /// Version 1: ???
    /// 
    /// Version 2, PreciseVersion 2.1: .NET 4.5 (x86)
    /// 0:000> dt -r2 WKS::gc_history_per_heap
    ///  clr!WKS::gc_history_per_heap
    /// +0x000 gen_data         : [5] WKS::gc_generation_data
    ///    +0x000 size_before      : Uint4B/8B         : [0 - 40), [40 - 80), [80 - 120), [120 - 160), [160 - 200)
    ///    +0x004 free_list_space_before : Uint4B/8B
    ///    +0x008 free_obj_space_before : Uint4B/8B
    ///    +0x00c size_after       : Uint4B/8B
    ///    +0x010 free_list_space_after : Uint4B/8B
    ///    +0x014 free_obj_space_after : Uint4B/8B
    ///    +0x018 in               : Uint4B/8B
    ///    +0x01c out              : Uint4B/8B
    ///    +0x020 new_allocation   : Uint4B/8B
    ///    +0x024 surv             : Uint4B/8B
    /// +0x0c8 gen_to_condemn_reasons : WKS::gen_to_condemn_tuning
    ///    +0x000 condemn_reasons_gen : Uint4B          : 200
    ///    +0x004 condemn_reasons_condition : Uint4B    : 204
    /// +0x0d0 mem_pressure     : Uint4B                : 208
    /// +0x0d4 mechanisms       : [2] Uint4B            : 212 (expand), 216 (compact)
    /// +0x0dc heap_index       : Uint4B                : 220
    /// 
    /// vm\gcrecord.h
    /// Etw_GCDataPerHeapSpecial(...)
    /// ...
    ///     EventDataDescCreate(EventData[0], gc_data_per_heap, datasize);
    ///     EventDataDescCreate(EventData[1], ClrInstanceId, sizeof(ClrInstanceId));
    /// 
    /// Version 2, PreciseVersion 2.2: .NET 4.5.2 (x86)
    /// 0:000> dt -r2 WKS::gc_history_per_heap
    ///  clr!WKS::gc_history_per_heap
    /// +0x000 gen_data         : [5] WKS::gc_generation_data
    ///    +0x000 size_before      : Uint4B/8B          : [0 - 40), [40 - 80), [80 - 120), [120 - 160), [160 - 200)
    ///    +0x004 free_list_space_before : Uint4B/8B
    ///    +0x008 free_obj_space_before : Uint4B/8B
    ///    +0x00c size_after       : Uint4B/8B
    ///    +0x010 free_list_space_after : Uint4B/8B
    ///    +0x014 free_obj_space_after : Uint4B/8B
    ///    +0x018 in               : Uint4B/8B
    ///    +0x01c out              : Uint4B/8B
    ///    +0x020 new_allocation   : Uint4B/8B
    ///    +0x024 surv             : Uint4B/8B
    /// +0x0c8 gen_to_condemn_reasons : WKS::gen_to_condemn_tuning
    ///    +0x000 condemn_reasons_gen : Uint4B          : 200
    ///    +0x004 condemn_reasons_condition : Uint4B    : 204
    /// +0x0d0 mem_pressure     : Uint4B                : 208
    /// +0x0d4 mechanisms       : [2] Uint4B            : 212 (expand), 216 (compact)
    /// +0x0dc heap_index       : Uint4B                : 220
    /// +0x0e0 extra_gen0_committed : Uint8B            : 224
    /// 
    /// vm\gcrecord.h
    /// Etw_GCDataPerHeapSpecial(...)
    /// ...
    ///     EventDataDescCreate(EventData[0], gc_data_per_heap, datasize);
    ///     EventDataDescCreate(EventData[1], ClrInstanceId, sizeof(ClrInstanceId));
    /// 
    /// Version 3: .NET 4.6 (x86)
    /// 0:000> dt -r2 WKS::gc_history_per_heap
    /// clr!WKS::gc_history_per_heap
    ///    +0x000 gen_data         : [4]                                
    ///     WKS::gc_generation_data                                     
    ///       +0x000 size_before      : Uint4B/8B                          
    ///       +0x004 free_list_space_before : Uint4B/8B                    
    ///       +0x008 free_obj_space_before : Uint4B/8B                     
    ///       +0x00c size_after       : Uint4B/8B                          
    ///       +0x010 free_list_space_after : Uint4B/8B
    ///       +0x014 free_obj_space_after : Uint4B/8B
    ///       +0x018 in               : Uint4B/8B
    ///       +0x01c pinned_surv      : Uint4B/8B
    ///       +0x020 npinned_surv     : Uint4B/8B
    ///       +0x024 new_allocation   : Uint4B/8B
    ///    +0x0a0 maxgen_size_info : WKS::maxgen_size_increase          
    ///       +0x000 free_list_allocated : Uint4B/8B                       
    ///       +0x004 free_list_rejected : Uint4B/8B                        
    ///       +0x008 end_seg_allocated : Uint4B/8B                         
    ///       +0x00c condemned_allocated : Uint4B/8B                       
    ///       +0x010 pinned_allocated : Uint4B/8B                          
    ///       +0x014 pinned_allocated_advance : Uint4B/8B                  
    ///       +0x018 running_free_list_efficiency : Uint4B/8B              
    ///    +0x0bc gen_to_condemn_reasons : WKS::gen_to_condemn_tuning   
    ///       +0x000 condemn_reasons_gen : Uint4B                       
    ///       +0x004 condemn_reasons_condition : Uint4B                 
    ///    +0x0c4 mechanisms       : [2] Uint4B                         
    ///    +0x0cc machanism_bits   : Uint4B                             
    ///    +0x0d0 heap_index       : Uint4B                             
    ///    +0x0d4 extra_gen0_committed : Uint4B/8B                         
    /// 
    /// pal\src\eventprovider\lttng\eventprovdotnetruntime.cpp
    /// FireEtXplatGCPerHeapHistory_V3(...)
    /// 
    ///      tracepoint(
    ///         DotNETRuntime,
    ///         GCPerHeapHistory_V3,                      x86 offsets
    ///         ClrInstanceID,                          : 0
    ///         (const size_t) FreeListAllocated,       : 2
    ///         (const size_t) FreeListRejected,        : 6
    ///         (const size_t) EndOfSegAllocated,       : 10
    ///         (const size_t) CondemnedAllocated,      : 14
    ///         (const size_t) PinnedAllocated,         : 18
    ///         (const size_t) PinnedAllocatedAdvance,  : 22
    ///         RunningFreeListEfficiency,              : 26
    ///         CondemnReasons0,                        : 30
    ///         CondemnReasons1                         : 34
    ///         );
    ///     tracepoint(
    ///         DotNETRuntime,
    ///         GCPerHeapHistory_V3_1,
    ///         CompactMechanisms,                      : 38
    ///         ExpandMechanisms,                       : 42
    ///         HeapIndex,                              : 46
    ///         (const size_t) ExtraGen0Commit,         : 50
    ///         Count,                                  : 54 (number of WKS::gc_generation_data's)
    ///         Arg15_Struct_Len_,                      : ?? not really sent
    ///         (const int*) Arg15_Struct_Pointer_      : [58 - 98), ...
    ///         );
    /// 
    /// Version 3 is now setup to allow "add to the end" scenarios
    /// 
    /// </summary>
    public sealed class GCPerHeapHistoryTraceData : TraceEvent
    {
        public int ClrInstanceID
        {
            get
            {
                int cid = -1;

                if (Version == 0)
                {
                    cid = GetByteAt(EventDataLength - 1);
                }
                else if (Version == 2)
                {
                    cid = GetByteAt(EventDataLength - 1);
                }
                else if (Version >= 3)
                {
                    cid = GetInt16At(0);
                }
                else
                {
                    Debug.Assert(false, "ClrInstanceId invalid Version : " + Version);
                }

                Debug.Assert(cid >= 0);
                return cid;
            }
        }
        public long FreeListAllocated
        {
            get
            {
                long ret = long.MinValue;

                if (Version >= 3)
                {
                    ret = (long)GetAddressAt(2);
                }
                else
                {
                    Debug.Assert(false, "FreeListAllocated invalid Version : " + Version);
                }

                Debug.Assert(ret >= 0);
                return ret;
            }
        }
        public bool HasFreeListAllocated { get { return Version >= 3; } }
        public long FreeListRejected
        {
            get
            {
                long ret = long.MinValue;

                if (Version >= 3)
                {
                    ret = (long)GetAddressAt(HostOffset(6, 1));
                }
                else
                {
                    Debug.Assert(false, "FreeListRejected invalid Version : " + Version);
                }

                Debug.Assert(ret >= 0);
                return ret;
            }
        }
        public bool HasFreeListRejected { get { return Version >= 3; } }
        public long EndOfSegAllocated
        {
            get
            {
                long ret = long.MinValue;

                if (Version >= 3)
                {
                    ret = (long)GetAddressAt(HostOffset(10, 2));
                }
                else
                {
                    Debug.Assert(false, "EndOfSegAllocated invalid Version : " + Version);
                }

                Debug.Assert(ret >= 0);
                return ret;
            }
        }
        public bool HasEndOfSegAllocated { get { return Version >= 3; } }
        public long CondemnedAllocated
        {
            get
            {
                long ret = long.MinValue;

                if (Version >= 3)
                {
                    ret = (long)GetAddressAt(HostOffset(14, 3));
                }
                else
                {
                    Debug.Assert(false, "CondemnedAllocated invalid Version : " + Version);
                }

                Debug.Assert(ret >= 0);
                return ret;
            }
        }
        public bool HasCondemnedAllocated { get { return Version >= 3; } }
        public long PinnedAllocated
        {
            get
            {
                long ret = long.MinValue;

                if (Version >= 3)
                {
                    ret = (long)GetAddressAt(HostOffset(18, 4));
                }
                else
                {
                    Debug.Assert(false, "PinnedAllocated invalid Version : " + Version);
                }

                Debug.Assert(ret >= 0);
                return ret;
            }
        }
        public bool HasPinnedAllocated { get { return Version >= 3; } }
        public long PinnedAllocatedAdvance
        {
            get
            {
                long ret = long.MinValue;

                if (Version >= 3)
                {
                    ret = (long)GetAddressAt(HostOffset(22, 5));
                }
                else
                {
                    Debug.Assert(false, "PinnedAllocatedAdvance invalid Version : " + Version);
                }

                Debug.Assert(ret >= 0);
                return ret;
            }
        }
        public bool HasPinnedAllocatedAdvance { get { return Version >= 3; } }
        public int RunningFreeListEfficiency
        {
            get
            {
                int ret = int.MinValue;

                if (Version >= 3)
                {
                    ret = GetInt32At(HostOffset(26, 6));
                }
                else
                {
                    Debug.Assert(false, "RunningFreeListEfficiency invalid Version : " + Version);
                }

                Debug.Assert(ret >= 0);
                return ret;
            }
        }
        public bool HasRunningFreeListEfficiency { get { return Version >= 3; } }
        /// <summary>
        /// Returns the condemned generation number
        /// </summary>
        public int CondemnReasons0
        {
            get
            {
                int ret = int.MinValue;

                if (Version == 0 && (MinorVersion == 0 || MinorVersion == 1))
                {
                    ret = GetInt32At(SizeOfGenData * maxGenData + sizeof(int) * 3);
                }
                else if (Version == 0 && MinorVersion == 2)
                {
                    ret = GetInt32At(SizeOfGenData * maxGenData + sizeof(int) * 4);
                }
                else if (Version == 2)
                {
                    ret = GetInt32At(SizeOfGenData * maxGenData);
                }
                else if (Version >= 3)
                {
                    ret = GetInt32At(HostOffset(30, 6));
                }
                else
                {
                    Debug.Assert(false, "CondenReasons0 invalid Version : " + Version + " " + MinorVersion);
                }

                Debug.Assert(ret >= 0);
                return ret;
            }
        }
        /// <summary>
        /// Returns the condemned condition
        /// </summary>
        public int CondemnReasons1
        {
            get
            {
                int ret = int.MinValue;

                if (Version == 2)
                {
                    ret = GetInt32At(SizeOfGenData * maxGenData + sizeof(int));
                }
                else if (Version >= 3)
                {
                    ret = GetInt32At(HostOffset(34, 6));
                }
                else
                {
                    Debug.Assert(false, "CondenReasons1 invalid Version : " + Version);
                }

                Debug.Assert(ret >= 0);
                return ret;
            }
        }
        public bool HasCondemnReasons1 { get { return (Version == 2 || Version >= 3); } }
        public gc_heap_compact_reason CompactMechanisms
        {
            get
            {
                int ret = 0;

                if (Version == 0)
                {
                    ret = GetInt32At(SizeOfGenData * maxGenData + sizeof(int) * 2);
                }
                else if (Version == 2)
                {
                    ret = GetInt32At(SizeOfGenData * maxGenData + sizeof(int) * 4);
                }
                else if (Version >= 3)
                {
                    ret = GetInt32At(HostOffset(38, 6));
                }
                else
                {
                    Debug.Assert(false, "CompactMechanisms invalid Version : " + Version);
                }

                Debug.Assert(ret <= 0);

                return (gc_heap_compact_reason)IndexOfSetBit(ret, (int)gc_heap_compact_reason.max_compact_reasons_count,
                                                                  (int)gc_heap_compact_reason.not_specified);
            }
        }
        public gc_heap_expand_mechanism ExpandMechanisms
        {
            get
            {
                int ret = 0;

                if (Version == 0)
                {
                    ret = GetInt32At(SizeOfGenData * maxGenData + sizeof(int) * 1);
                }
                else if (Version == 2)
                {
                    ret = GetInt32At(SizeOfGenData * maxGenData + sizeof(int) * 3);
                }
                else if (Version >= 3)
                {
                    ret = GetInt32At(HostOffset(42, 6));
                }
                else
                {
                    Debug.Assert(false, "ExpandMechanisms invalid Version : " + Version);
                }

                Debug.Assert(ret <= 0);

                return (gc_heap_expand_mechanism)IndexOfSetBit(ret, (int)gc_heap_expand_mechanism.max_expand_mechanisms_count,
                                                                    (int)gc_heap_expand_mechanism.not_specified);
            }
        }
        public gc_concurrent_compact_reason ConcurrentCompactMechanisms
        {
            get
            {
                int ret = 0;

                if (Version == 0 && MinorVersion == 2)
                {
                    ret = GetInt32At(SizeOfGenData * maxGenData + sizeof(int) * 3);
                }
                else
                {
                    Debug.Assert(false, "ConcurrentCompactMechanisms invalid Version : " + Version + " " + MinorVersion);
                }

                Debug.Assert(ret <= 0);

                return (gc_concurrent_compact_reason)IndexOfSetBit(ret, (int)gc_concurrent_compact_reason.max_concurrent_compat_reason,
                                                                        (int)gc_concurrent_compact_reason.not_specified);
            }
        }
        public bool HasConcurrentCompactMechanisms { get { return Version == 0 && MinorVersion == 2; } }
        public int HeapIndex
        {
            get
            {
                int ret = int.MinValue;

                if (Version == 0)
                {
                    ret = GetInt32At(EventDataLength - (sizeof(int) + sizeof(byte)));
                }
                else if (Version == 2 && (MinorVersion == 0 || MinorVersion == 1))
                {
                    ret = GetInt32At(EventDataLength - (sizeof(int) + sizeof(Int16)));
                }
                else if (Version == 2 && MinorVersion == 2)
                {
                    ret = GetInt32At(EventDataLength - (sizeof(int) + sizeof(Int16) + sizeof(Int64)));
                }
                else if (Version >= 3)
                {
                    ret = GetInt32At(HostOffset(46, 6));
                }
                else
                {
                    Debug.Assert(false, "HeapIndex invalid Version : " + Version + " " + MinorVersion);
                }

                Debug.Assert(ret >= 0);
                if (Version >= 0 && Version < 3)
                {
                    Debug.Assert(ret < maxGenData);
                }
                else if (Version >= 3)
                {
                    Debug.Assert(ret < Environment.ProcessorCount); // This is really GCGlobalHeapHistoryTraceData.NumHeaps, but we don't have access to that here
                                                                    // It is VERY unlikely that we make more heaps than there are processors.   
                }

                if (ret < 0)
                {
                    return 0; // on retail avoid array out of range exceptions
                }
                else
                {
                    return ret;
                }
            }
        }
        public long ExtraGen0Commit
        {
            get
            {
                long ret = -1;

                if (Version == 2 && MinorVersion == 2)
                {
                    ret = GetInt32At(EventDataLength - (sizeof(Int16) + sizeof(Int64)));
                }
                else if (Version >= 3)
                {
                    ret = (long)GetAddressAt(HostOffset(50, 6));
                }
                else
                {
                    Debug.Assert(false, "ExtraGen0Commit invalid Version : " + Version + " " + MinorVersion);
                }

                Debug.Assert(ret >= 0);
                return ret;
            }
        }
        public bool HasExtraGen0Commit { get { return (Version == 2 && MinorVersion == 2) || Version >= 3; } }
        public int Count
        {
            get
            {
                int ret = int.MinValue;

                if (Version >= 3)
                {
                    ret = GetInt32At(HostOffset(54, 7));
                }
                else
                {
                    Debug.Assert(false, "Count invalid Version : " + Version);
                }

                Debug.Assert(ret >= 0);

                if (ret < 0)
                {
                    return 0; // on retail avoid array out of range exceptions
                }
                else
                {
                    return ret;
                }
            }
        }
        public bool HasCount { get { return Version >= 3; } }
        public int NumGenerations { get { return HasCount ? Count : 4; } }
        public int MemoryPressure
        {
            get
            {
                int ret = int.MinValue;

                if (Version == 0)
                {
                    ret = GetInt32At(SizeOfGenData * maxGenData);
                }
                else if (Version == 2)
                {
                    ret = GetInt32At(SizeOfGenData * maxGenData + sizeof(Int32) * 2);
                }
                else
                {
                    Debug.Assert(false, "MemoryPressure invalid Version : " + Version);
                }

                Debug.Assert(ret >= 0);
                return ret;
            }
        }
        public bool HasMemoryPressure { get { return Version == 0 || Version == 2; } }

        /// <summary>
        /// genNumber is a number from 0 to maxGenData-1.  These are for generation 0, 1, 2, 3 = Large Object Heap
        /// genNumber = 4 is that second pass for Gen 0.  
        /// </summary>
        public GCPerHeapHistoryGenData GenData(Gens genNumber)
        {
            if (Version == 0 || Version == 2)
            {
                Debug.Assert((int)genNumber < maxGenData);
                // each GenData structure contains 10 pointers sized integers 
                return new GCPerHeapHistoryGenData(Version, GetIntPtrArray(SizeOfGenData * (int)genNumber, EntriesInGenData));
            }
            else if (Version >= 3)
            {
                Debug.Assert((int)genNumber < Count);
                return new GCPerHeapHistoryGenData(Version, GetIntPtrArray((HostOffset(54, 7) + sizeof(Int32)) + SizeOfGenData * (int)genNumber, EntriesInGenData));
            }
            else
            {
                Debug.Assert(false, "GenData invalid Version : " + Version);
                return new GCPerHeapHistoryGenData(Version, GetIntPtrArray(SizeOfGenData * (int)genNumber, EntriesInGenData));
            }
        }

        public bool VersionRecognized { get { int ver; return ParseMinorVersion(out ver); } }

        #region Private
        public int MinorVersion
        {
            get
            {
                if (m_minorVersion == -1)
                {
                    ParseMinorVersion(out m_minorVersion);
                }

                return m_minorVersion;
            }
        }
        private int m_minorVersion = -1;
        private bool ParseMinorVersion(out int mversion)
        {
            mversion = -1;
            int size = 0;
            bool exactMatch = false;

            if (Version == 0)
            {
                size = (SizeOfGenData * 5) + 25;
                // For silverlight, there is one less mechanism.  It only affects the layout of gen_condemended_reasons. 
                if (base.EventDataLength == (size - sizeof(int)))
                {
                    mversion = 1; // Silverlight
                }
                else if (base.EventDataLength == size)
                {
                    mversion = 2; // .NET 4.0
                }

                if (mversion > 0)
                {
                    exactMatch = true;
                }
                else
                {
                    mversion = 0;
                }
            }
            else if (Version == 2)
            {
                size = (SizeOfGenData * 5) + sizeof(Int32) * 6 + sizeof(byte) + sizeof(Int64);
                if (base.EventDataLength == (size - sizeof(Int64)))
                {
                    mversion = 1; // .NET 4.5
                }
                else if (base.EventDataLength == size)
                {
                    mversion = 2; // .NET 4.5.2
                }

                if (mversion > 0)
                {
                    exactMatch = true;
                }
                else
                {
                    mversion = 0;
                }
            }
            else if (Version >= 3)
            {
                size = sizeof(Int16) + HostSizePtr(7) + sizeof(Int32) * 7 + SizeOfGenData * 4;
                // set this check up to enable future versions that "add to the end"
                if (base.EventDataLength >= size)
                {
                    mversion = 0; // .NET 4.6+
                }

                if (mversion >= 0)
                {
                    exactMatch = true;
                }
                else
                {
                    mversion = 0;
                }
            }

            Debug.Assert(exactMatch, "Unrecognized version (" + Version + ") and stream size (" + base.EventDataLength + ") not equal to expected (" + size + ")");

            return exactMatch;
        }

        private int IndexOfSetBit(int pow2, int count, int notSpecifiedValue)
        {
            if (pow2 == 0)
                return notSpecifiedValue;
            int index = 0;
            while ((pow2 & 1) != 1)
            {
                pow2 >>= 1;
                index++;
            }

            if (index >= 0 && index < count)
                return index;
            Debug.Assert(false, index + " >= 0 && " + index + " < " + count);
            return notSpecifiedValue;
        }

        private long[] GetIntPtrArray(int offset, int count)
        {
            long[] arr = new long[count];
            for (int i = 0; i < count; i++)
            {
                arr[i] = GetIntPtrAt(offset);
                offset += base.HostSizePtr(1);
            }

            return arr;
        }

        // This is used only for traces obtained prior to GCPerHeapHistory_V3. These traces are expected to begin with exactly 5 gen_data.
        // Please refer to the beginning of this class for the exact layout of these traces.
        private const int maxGenData = 5;

        internal GCPerHeapHistoryTraceData(Delegate action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            ((Action<GCPerHeapHistoryTraceData>)Action)(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(VersionRecognized);
        }

        public int EntriesInGenData
        {
            get
            {
                return 10;
            }
        }

        public int SizeOfGenData
        {
            get
            {
                return base.HostSizePtr(EntriesInGenData);
            }
        }

        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            if (HasFreeListAllocated)
            {
                XmlAttrib(sb, "FreeListAllocated", FreeListAllocated);
            }

            if (HasFreeListRejected)
            {
                XmlAttrib(sb, "FreeListRejected", FreeListRejected);
            }

            if (HasEndOfSegAllocated)
            {
                XmlAttrib(sb, "EndOfSegAllocated", EndOfSegAllocated);
            }

            if (HasCondemnedAllocated)
            {
                XmlAttrib(sb, "CondemnedAllocated", CondemnedAllocated);
            }

            if (HasPinnedAllocated)
            {
                XmlAttrib(sb, "PinnedAllocated", PinnedAllocated);
            }

            if (HasPinnedAllocatedAdvance)
            {
                XmlAttrib(sb, "PinnedAllocatedAdvance", PinnedAllocatedAdvance);
            }

            if (HasRunningFreeListEfficiency)
            {
                XmlAttrib(sb, "RunningFreeListEfficiency", RunningFreeListEfficiency);
            }

            XmlAttrib(sb, "CondemnReasons0", CondemnReasons0);
            if (HasCondemnReasons1)
            {
                XmlAttrib(sb, "CondemnReasons1", CondemnReasons1);
            }

            XmlAttrib(sb, "CompactMechanisms", CompactMechanisms);
            XmlAttrib(sb, "ExpandMechanisms", ExpandMechanisms);
            if (HasConcurrentCompactMechanisms)
            {
                XmlAttrib(sb, "ConcurrentCompactMechanisms", ConcurrentCompactMechanisms);
            }

            XmlAttrib(sb, "HeapIndex", HeapIndex);
            if (HasExtraGen0Commit)
            {
                XmlAttrib(sb, "ExtraGen0Commit", ExtraGen0Commit);
            }

            if (HasCount)
            {
                XmlAttrib(sb, "Count", Count);
            }

            if (HasMemoryPressure)
            {
                XmlAttrib(sb, "MemoryPressure", MemoryPressure);
            }

            sb.Append("/>");
            for (var gens = Gens.Gen0; gens < (Gens)NumGenerations; gens++)
            {
                GenData(gens).ToXml(gens, sb).AppendLine();
            }

            sb.AppendLine("</Event>");

            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] {"ClrInstanceID", "FreeListAllocated", "FreeListRejected", "EndOfSegAllocated", "CondemnedAllocated"
                        , "PinnedAllocated", "PinnedAllocatedAdvance", "RunningFreeListEfficiency", "CondemnReasons0", "CondemnReasons1", "CompactMechanisms", "ExpandMechanisms"
                        , "ConcurrentCompactMechanisms", "HeapIndex", "ExtraGen0Commit", "Count", "MemoryPressure"
                    };
                }
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ClrInstanceID;
                case 1:
                    if (HasFreeListAllocated)
                    {
                        return FreeListAllocated;
                    }

                    return null;
                case 2:
                    if (HasFreeListRejected)
                    {
                        return FreeListRejected;
                    }

                    return null;
                case 3:
                    if (HasEndOfSegAllocated)
                    {
                        return EndOfSegAllocated;
                    }

                    return null;
                case 4:
                    if (HasCondemnedAllocated)
                    {
                        return CondemnedAllocated;
                    }
                    else
                    {
                        return null;
                    }

                case 5:
                    if (HasPinnedAllocated)
                    {
                        return PinnedAllocated;
                    }
                    else
                    {
                        return null;
                    }

                case 6:
                    if (HasPinnedAllocatedAdvance)
                    {
                        return PinnedAllocatedAdvance;
                    }
                    else
                    {
                        return null;
                    }

                case 7:
                    if (HasRunningFreeListEfficiency)
                    {
                        return RunningFreeListEfficiency;
                    }
                    else
                    {
                        return null;
                    }

                case 8:
                    return CondemnReasons0;
                case 9:
                    if (HasCondemnReasons1)
                    {
                        return CondemnReasons1;
                    }
                    else
                    {
                        return null;
                    }

                case 10:
                    return CompactMechanisms;
                case 11:
                    return ExpandMechanisms;
                case 12:
                    if (HasConcurrentCompactMechanisms)
                    {
                        return ConcurrentCompactMechanisms;
                    }
                    else
                    {
                        return null;
                    }

                case 13:
                    return HeapIndex;
                case 14:
                    if (HasExtraGen0Commit)
                    {
                        return ExtraGen0Commit;
                    }
                    else
                    {
                        return null;
                    }

                case 15:
                    if (HasCount)
                    {
                        return Count;
                    }
                    else
                    {
                        return null;
                    }

                case 16:
                    if (HasMemoryPressure)
                    {
                        return MemoryPressure;
                    }
                    else
                    {
                        return null;
                    }

                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private Delegate Action;
        #endregion
    }

    public enum GCExpandMechanism : uint
    {
        None = 0,
        ReuseNormal = 0x80000000,
        ReuseBestFit = 0x80000001,
        NewSegEphemeralPromotion = 0x80000002,
        NewSeg = 0x80000003,
        NoMemory = 0x80000004,
    };

    /// <summary>
    /// Version 0: Silverlight (x86), .NET 4.0
    /// [5] WKS::gc_generation_data
    ///    +0x000 size_before      : Uint4B/8B
    ///    +0x004 size_after       : Uint4B/8B
    ///    +0x008 current_size     : Uint4B/8B
    ///    +0x00c previous_size    : Uint4B/8B
    ///    +0x010 fragmentation    : Uint4B/8B
    ///    +0x014 in               : Uint4B/8B
    ///    +0x018 out              : Uint4B/8B
    ///    +0x01c new_allocation   : Uint4B/8B
    ///    +0x020 surv             : Uint4B/8B
    ///    +0x024 growth           : Uint4B/8B
    ///    
    /// Version 1: ???
    /// 
    /// Version 2, PreciseVersion 2.1: .NET 4.5 (x86), .NET 4.5.2 (x86)
    ///  [5] WKS::gc_generation_data
    ///    +0x000 size_before            : Uint4B/8B
    ///    +0x004 free_list_space_before : Uint4B/8B
    ///    +0x008 free_obj_space_before  : Uint4B/8B
    ///    +0x00c size_after             : Uint4B/8B
    ///    +0x010 free_list_space_after  : Uint4B/8B
    ///    +0x014 free_obj_space_after   : Uint4B/8B
    ///    +0x018 in                     : Uint4B/8B
    ///    +0x01c out                    : Uint4B/8B
    ///    +0x020 new_allocation         : Uint4B/8B
    ///    +0x024 surv                   : Uint4B/8B
    /// 
    /// Version 3: .NET 4.6 (x86)
    /// [4] WKS::gc_generation_data                                     
    ///    +0x000 size_before            : Uint4B/8B                          
    ///    +0x004 free_list_space_before : Uint4B/8B                    
    ///    +0x008 free_obj_space_before  : Uint4B/8B                     
    ///    +0x00c size_after             : Uint4B/8B                          
    ///    +0x010 free_list_space_after  : Uint4B/8B
    ///    +0x014 free_obj_space_after   : Uint4B/8B
    ///    +0x018 in                     : Uint4B/8B
    ///    +0x01c pinned_surv            : Uint4B/8B
    ///    +0x020 npinned_surv           : Uint4B/8B
    ///    +0x024 new_allocation         : Uint4B/8B
    /// </summary>
    public sealed class GCPerHeapHistoryGenData
    {
        /// <summary>
        /// Size of the generation before the GC, includes fragmentation
        /// </summary>
        public long SizeBefore
        {
            get
            {
                long ret = m_genDataArray[0];

                Debug.Assert(ret >= 0);
                return ret;
            }
        }
        /// <summary>
        /// Size of the generation after GC.  Includes fragmentation
        /// </summary>
        public long SizeAfter
        {
            get
            {
                long ret = long.MinValue;

                if (m_version == 0)
                {
                    ret = m_genDataArray[1];
                }
                else if (m_version >= 2)
                {
                    ret = m_genDataArray[3];
                }
                else
                {
                    Debug.Assert(false, "SizeAfter invalid version : " + m_version);
                }

                Debug.Assert(ret >= 0);
                return ret;
            }
        }
        /// <summary>
        /// Size occupied by objects at the beginning of the GC, discounting fragmentation. 
        /// Only exits on 4.5 RC and beyond.
        /// </summary>
        public long ObjSpaceBefore
        {
            get
            {
                long ret = long.MinValue;

                if (m_version >= 2)
                {
                    ret = (SizeBefore - FreeListSpaceBefore - FreeObjSpaceBefore);
                }
                else
                {
                    Debug.Assert(false, "ObjSpaceBefore invalid version : " + m_version);
                }

                Debug.Assert(ret >= 0);
                return ret;
            }
        }
        public bool HasObjSpaceBefore { get { return m_version >= 2; } }
        /// <summary>
        /// This is the fragmenation at the end of the GC.
        /// </summary>
        public long Fragmentation
        {
            get
            {
                long ret = long.MinValue;

                if (m_version == 0)
                {
                    ret = m_genDataArray[4];
                }
                else if (m_version >= 2)
                {
                    ret = (FreeListSpaceAfter + FreeObjSpaceAfter);
                }
                else
                {
                    Debug.Assert(false, "Fragmentation invalid version : " + m_version);
                }

                Debug.Assert(ret >= 0);
                return ret;
            }
        }
        /// <summary>
        /// Size occupied by objects, discounting fragmentation.
        /// </summary>
        public long ObjSizeAfter
        {
            get
            {
                long ret = SizeAfter - Fragmentation;

                Debug.Assert(ret >= 0);
                return ret;
            }
        }
        /// <summary>
        /// This is the free list space (ie, what's threaded onto the free list) at the beginning of the GC.
        /// Only exits on 4.5 RC and beyond.
        /// </summary>
        public long FreeListSpaceBefore
        {
            get
            {
                long ret = long.MinValue;

                if (m_version >= 2)
                {
                    ret = m_genDataArray[1];
                }
                else
                {
                    Debug.Assert(false, "FreeListSpaceBefore invalid version : " + m_version);
                }

                Debug.Assert(ret >= 0);
                return ret;
            }
        }
        public bool HasFreeListSpaceBefore { get { return m_version >= 2; } }
        /// <summary>
        /// This is the free obj space (ie, what's free but not threaded onto the free list) at the beginning of the GC.
        /// Only exits on 4.5 RC and beyond.
        /// </summary>
        public long FreeObjSpaceBefore
        {
            get
            {
                long ret = long.MinValue;
                if (m_version >= 2)
                {
                    ret = m_genDataArray[2];
                }
                else
                {
                    Debug.Assert(false, "FreeObjSpaceBefore invalid version : " + m_version);
                }

                Debug.Assert(ret >= 0);
                return ret;
            }
        }
        public bool HasFreeObjSpaceBefore { get { return m_version >= 2; } }
        /// <summary>
        /// This is the free list space (ie, what's threaded onto the free list) at the end of the GC.
        /// Only exits on 4.5 Beta and beyond.
        /// </summary>
        public long FreeListSpaceAfter
        {
            get
            {
                long ret = long.MinValue;
                if (m_version >= 2)
                {
                    ret = m_genDataArray[4];
                }
                else
                {
                    Debug.Assert(false, "FreeListSpaceAfter invalid version : " + m_version);
                }

                Debug.Assert(ret >= 0);
                return ret;
            }
        }
        public bool HasFreeListSpaceAfter { get { return m_version >= 2; } }
        /// <summary>
        /// This is the free obj space (ie, what's free but not threaded onto the free list) at the end of the GC.
        /// Only exits on 4.5 Beta and beyond.
        /// </summary>
        public long FreeObjSpaceAfter
        {
            get
            {
                long ret = long.MinValue;

                if (m_version >= 2)
                {
                    ret = m_genDataArray[5];
                }
                else
                {
                    Debug.Assert(false, "FreeObjSpaceAfter invalid version : " + m_version);
                }

                Debug.Assert(ret >= 0);
                return ret;
            }
        }
        public bool HasFreeObjSpaceAfter { get { return m_version >= 2; } }
        /// <summary>
        /// This is the amount that came into this generation on this GC
        /// </summary>
        public long In
        {
            get
            {
                long ret = long.MinValue;

                if (m_version == 0)
                {
                    ret = m_genDataArray[5];
                }
                else if (m_version >= 2)
                {
                    ret = m_genDataArray[6];
                }
                else
                {
                    Debug.Assert(false, "In invalid version : " + m_version);
                }

                Debug.Assert(ret >= 0);
                return ret;
            }
        }
        /// <summary>
        /// This is the number of bytes survived in this generation.
        /// </summary>
        public long Out
        {
            get
            {
                long ret = long.MinValue;

                if (m_version == 0)
                {
                    ret = m_genDataArray[6];
                }
                else if (m_version == 2)
                {
                    ret = m_genDataArray[7];
                }
                else if (m_version >= 3)
                {
                    ret = (PinnedSurv + NonePinnedSurv);
                }
                else
                {
                    Debug.Assert(false, "Out invalid version : " + m_version);
                }

                Debug.Assert(ret >= 0);
                return ret;
            }
        }
        /// <summary>
        /// This is the new budget for the generation
        /// </summary>
        public long Budget
        {
            get
            {
                long ret = long.MinValue;

                if (m_version == 0)
                {
                    ret = m_genDataArray[7];
                }
                else if (m_version == 2)
                {
                    ret = m_genDataArray[8];
                }
                else if (m_version >= 3)
                {
                    ret = m_genDataArray[9];
                }
                else
                {
                    Debug.Assert(false, "Budget invalid version : " + m_version);
                }

                Debug.Assert(ret >= 0);
                return ret;
            }
        }
        /// <summary>
        /// This is the survival rate
        /// </summary>
        public long SurvRate
        {
            get
            {
                long ret = long.MinValue;

                if (m_version == 0)
                {
                    ret = m_genDataArray[8];
                }
                else if (m_version == 2)
                {
                    ret = m_genDataArray[9];
                }
                else if (m_version >= 3)
                {
                    if (ObjSpaceBefore == 0)
                    {
                        ret = 0;
                    }
                    else
                    {
                        ret = (long)((double)Out * 100.0 / (double)ObjSpaceBefore);
                    }
                }
                else
                {
                    Debug.Assert(false, "SurvRate invalid version : " + m_version);
                }

                Debug.Assert(ret >= 0);
                return ret;
            }
        }

        public long PinnedSurv
        {
            get
            {
                long ret = long.MinValue;

                if (m_version >= 3)
                {
                    ret = m_genDataArray[7];
                }
                else
                {
                    Debug.Assert(false, "PinnedSurv invalid version : " + m_version);
                }

                Debug.Assert(ret >= 0);
                return ret;
            }
        }
        public bool HasPinnedSurv { get { return (m_version >= 3); } }
        public long NonePinnedSurv
        {
            get
            {
                long ret = long.MinValue;

                if (m_version >= 3)
                {
                    ret = m_genDataArray[8];
                }
                else
                {
                    Debug.Assert(false, "NonePinnedSurv invalid version : " + m_version);
                }

                Debug.Assert(ret >= 0);
                return ret;
            }
        }
        public bool HasNonePinnedSurv { get { return (m_version >= 3); } }

        public StringBuilder ToXml(Gens genName, StringBuilder sb)
        {
            sb.Append(" <GenData");
            TraceEvent.XmlAttrib(sb, "Name", genName);
            TraceEvent.XmlAttrib(sb, "SizeBefore", SizeBefore);
            TraceEvent.XmlAttrib(sb, "SizeAfter", SizeAfter);
            if (HasObjSpaceBefore)
            {
                TraceEvent.XmlAttrib(sb, "ObjSpaceBefore", ObjSpaceBefore);
            }

            TraceEvent.XmlAttrib(sb, "Fragmentation", Fragmentation);
            TraceEvent.XmlAttrib(sb, "ObjSizeAfter", ObjSizeAfter);
            if (HasFreeListSpaceBefore)
            {
                TraceEvent.XmlAttrib(sb, "FreeListSpaceBefore", FreeListSpaceBefore);
            }

            if (HasFreeObjSpaceBefore)
            {
                TraceEvent.XmlAttrib(sb, "FreeObjSpaceBefore", FreeObjSpaceBefore);
            }

            if (HasFreeListSpaceAfter)
            {
                TraceEvent.XmlAttrib(sb, "FreeListSpaceAfter", FreeListSpaceAfter);
            }

            if (HasFreeObjSpaceAfter)
            {
                TraceEvent.XmlAttrib(sb, "FreeObjSpaceAfter", FreeObjSpaceAfter);
            }

            TraceEvent.XmlAttrib(sb, "In", In);
            TraceEvent.XmlAttrib(sb, "Out", Out);
            TraceEvent.XmlAttrib(sb, "NewAllocation", Budget); // not sure why this is not called Budget
            TraceEvent.XmlAttrib(sb, "SurvRate", SurvRate);
            if (HasPinnedSurv)
            {
                TraceEvent.XmlAttrib(sb, "PinnedSurv", PinnedSurv);
            }

            if (HasNonePinnedSurv)
            {
                TraceEvent.XmlAttrib(sb, "NonePinnedSurv", NonePinnedSurv);
            }

            sb.Append("/>");
            return sb;
        }
        public override string ToString()
        {
            return ToXml(Gens.Gen0, new StringBuilder()).ToString();
        }
        #region private
        internal GCPerHeapHistoryGenData(int version, long[] genDataArray)
        {
            m_version = version;
            m_genDataArray = genDataArray;
        }

        private int m_version;
        private long[] m_genDataArray;
        #endregion
    }

    /// <summary>
    /// Version 0: ???
    /// 
    /// Version 1: Silverlight (x86), .NET 4.0, .NET 4.5, .NET 4.5.2
    /// VM\gc.cpp
    /// 0:041> dt -r3 WKS::gc_history_global
    /// coreclr!WKS::gc_history_global
    ///    +0x000 final_youngest_desired : Uint4B/8B
    ///    +0x004 num_heaps        : Uint4B
    ///    +0x008 condemned_generation : Int4B
    ///    +0x00c gen0_reduction_count : Int4B
    ///    +0x010 reason           : 
    ///     reason_alloc_soh = 0n0
    ///     reason_induced = 0n1
    ///     reason_lowmemory = 0n2
    ///     reason_empty = 0n3
    ///     reason_alloc_loh = 0n4
    ///     reason_oos_soh = 0n5
    ///     reason_oos_loh = 0n6
    ///     reason_induced_noforce = 0n7
    ///     reason_gcstress = 0n8
    ///     reason_max = 0n9
    ///    +0x014 global_mechanims_p : Uint4B
    ///   
    /// FireEtwGCGlobalHeapHistory_V1(gc_data_global.final_youngest_desired, // upcast on 32bit to __int64
    ///                          gc_data_global.num_heaps,
    ///                          gc_data_global.condemned_generation,
    ///                          gc_data_global.gen0_reduction_count,
    ///                          gc_data_global.reason,
    ///                          gc_data_global.global_mechanims_p,
    ///                          GetClrInstanceId());
    /// Version 2: .NET 4.6
    /// clr!WKS::gc_history_global
    ///    +0x000 final_youngest_desired : Uint4B/8B
    ///    +0x004 num_heaps        : Uint4B
    ///    +0x008 condemned_generation : Int4B
    ///    +0x00c gen0_reduction_count : Int4B
    ///    +0x010 reason           : 
    ///     reason_alloc_soh = 0n0
    ///     reason_induced = 0n1
    ///     reason_lowmemory = 0n2
    ///     reason_empty = 0n3
    ///     reason_alloc_loh = 0n4
    ///     reason_oos_soh = 0n5
    ///     reason_oos_loh = 0n6
    ///     reason_induced_noforce = 0n7
    ///     reason_gcstress = 0n8
    ///     reason_lowmemory_blocking = 0n9
    ///     reason_induced_compacting = 0n10
    ///     reason_lowmemory_host = 0n11
    ///     reason_max = 0n12
    ///    +0x014 pause_mode       : Int4B
    ///    +0x018 mem_pressure     : Uint4B
    ///    +0x01c global_mechanims_p : Uint4B
    /// 
    /// FireEtwGCGlobalHeapHistory_V2(gc_data_global.final_youngest_desired, // upcast on 32bit to __int64
    ///                          gc_data_global.num_heaps,
    ///                          gc_data_global.condemned_generation,
    ///                          gc_data_global.gen0_reduction_count,
    ///                          gc_data_global.reason,
    ///                          gc_data_global.global_mechanims_p,
    ///                          GetClrInstanceId());
    ///                          gc_data_global.pause_mode, 
    ///                          gc_data_global.mem_pressure);
    ///                          
    /// </summary>
    public sealed class GCGlobalHeapHistoryTraceData : TraceEvent
    {
        public long FinalYoungestDesired { get { return GetInt64At(0); } }
        public int NumHeaps { get { return GetInt32At(8); } }
        public int CondemnedGeneration { get { return GetInt32At(12); } }
        public int Gen0ReductionCount { get { return GetInt32At(16); } }
        public GCReason Reason { get { return (GCReason)GetInt32At(20); } }
        public GCGlobalMechanisms GlobalMechanisms { get { return (GCGlobalMechanisms)GetInt32At(24); } }
        public int ClrInstanceID { get { if (Version >= 1) { return GetInt16At(28); } return 0; } }
        public bool HasClrInstanceID { get { return Version >= 1; } }
        public GCPauseMode PauseMode { get { if (Version >= 2) { return (GCPauseMode)GetInt32At(30); } return GCPauseMode.Invalid; } }
        public bool HasPauseMode { get { return (Version >= 2); } }
        public int MemoryPressure { get { if (Version >= 2) { return GetInt32At(34); } return 0; } }
        public bool HasMemoryPressure { get { return (Version >= 2); } }
        public int CondemnReasons0 { get { if (Version >= 3) { return GetInt32At(38); } return 0; } }
        public bool HasCondemnReasons0 { get { return (Version >= 3); } }
        public int CondemnReasons1 { get { if (Version >= 3) { return GetInt32At(42); } return 0; } }
        public bool HasCondemnReasons1 { get { return (Version >= 3); } }
        #region Private
        internal GCGlobalHeapHistoryTraceData(Action<GCGlobalHeapHistoryTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<GCGlobalHeapHistoryTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 28));
            Debug.Assert(!(Version == 1 && EventDataLength != 30));
            Debug.Assert(!(Version == 2 && EventDataLength != 38));
            Debug.Assert(!(Version == 3 && EventDataLength != 46));
            Debug.Assert(!(Version > 3 && EventDataLength < 46));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "FinalYoungestDesired", FinalYoungestDesired);
            XmlAttrib(sb, "NumHeaps", NumHeaps);
            XmlAttrib(sb, "CondemnedGeneration", CondemnedGeneration);
            XmlAttrib(sb, "Gen0ReductionCount", Gen0ReductionCount);
            XmlAttrib(sb, "Reason", Reason);
            XmlAttrib(sb, "GlobalMechanisms", GlobalMechanisms);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "FinalYoungestDesired", "NumHeaps", "CondemnedGeneration", "Gen0ReductionCount", "Reason", "GlobalMechanisms", "ClrInstanceID", "PauseMode", "MemoryPressure", "CondemnReasons0", "CondemnReasons1" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return FinalYoungestDesired;
                case 1:
                    return NumHeaps;
                case 2:
                    return CondemnedGeneration;
                case 3:
                    return Gen0ReductionCount;
                case 4:
                    return Reason;
                case 5:
                    return GlobalMechanisms;
                case 6:
                    if (HasClrInstanceID)
                    {
                        return ClrInstanceID;
                    }
                    else
                    {
                        return null;
                    }

                case 7:
                    if (HasPauseMode)
                    {
                        return PauseMode;
                    }
                    else
                    {
                        return null;
                    }

                case 8:
                    if (HasMemoryPressure)
                    {
                        return MemoryPressure;
                    }
                    else
                    {
                        return null;
                    }

                case 9:
                    if (HasCondemnReasons0)
                    {
                        return CondemnReasons0;
                    }
                    else
                    {
                        return null;
                    }


                case 10:
                    if (HasCondemnReasons1)
                    {
                        return CondemnReasons1;
                    }
                    else
                    {
                        return null;
                    }

                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<GCGlobalHeapHistoryTraceData> Action;
        #endregion
    }

    public enum GcJoinType : int
    {
        LastJoin = 0,
        Join = 1,
        Restart = 2,
        FirstJoin = 3
    }

    public enum GcJoinTime : int
    {
        Start = 0,
        End = 1
    }

    public enum GcJoinID : int
    {
        Restart = -1,
        Invalid = ~(int)0xff
    }

    public sealed class GCJoinTraceData : TraceEvent
    {
        public int Heap { get { return GetInt32At(0); } }
        public GcJoinTime JoinTime { get { return (GcJoinTime)GetInt32At(4); } }
        public GcJoinType JoinType { get { return (GcJoinType)GetInt32At(8); } }
        public int ClrInstanceID { get { if (Version >= 1) { return GetInt16At(12); } return 0; } }
        public int GCID { get { if (Version >= 2) { return GetInt32At(14); } return (int)(GcJoinID.Invalid); } }

        #region Private
        internal GCJoinTraceData(Action<GCJoinTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<GCJoinTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 12));
            Debug.Assert(!(Version == 1 && EventDataLength != 14));
            Debug.Assert(!(Version > 1 && EventDataLength < 14));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "Heap", Heap);
            XmlAttrib(sb, "JoinTime", JoinTime);
            XmlAttrib(sb, "JoinType", JoinType);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            XmlAttrib(sb, "ID", GCID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Heap", "JoinTime", "JoinType", "ClrInstanceID", "ID" };
                }
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Heap;
                case 1:
                    return JoinTime;
                case 2:
                    return JoinType;
                case 3:
                    return ClrInstanceID;
                case 4:
                    return GCID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<GCJoinTraceData> Action;
        #endregion
    }

    public sealed class FinalizeObjectTraceData : TraceEvent
    {
        public Address TypeID { get { return GetAddressAt(0); } }
        public Address ObjectID { get { return GetAddressAt(HostOffset(4, 1)); } }
        public int ClrInstanceID { get { return GetInt16At(HostOffset(8, 2)); } }

        /// <summary>
        /// Gets the full type name including generic parameters in runtime syntax
        /// For example System.WeakReference`1[System.Diagnostics.Tracing.EtwSession]
        /// </summary>
        public string TypeName
        {
            get
            {
                return state.TypeIDToName(ProcessID, TypeID, TimeStampQPC);
            }
        }

        #region Private
        internal FinalizeObjectTraceData(Action<FinalizeObjectTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName, ClrTraceEventParserState state)

            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
            this.state = state;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<FinalizeObjectTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(10, 2)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(10, 2)));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "TypeName", TypeName);
            XmlAttribHex(sb, "ObjectID", ObjectID);
            XmlAttribHex(sb, "TypeID", TypeID);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "TypeName", "ObjectID", "TypeID", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return TypeName;
                case 1:
                    return ObjectID;
                case 2:
                    return TypeID;
                case 3:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<FinalizeObjectTraceData> Action;
        protected internal override void SetState(object newState) { state = (ClrTraceEventParserState)newState; }
        private ClrTraceEventParserState state;
        #endregion
    }
    public sealed class SetGCHandleTraceData : TraceEvent
    {
        public Address HandleID { get { return GetAddressAt(0); } }
        public Address ObjectID { get { return GetAddressAt(HostOffset(4, 1)); } }
        public GCHandleKind Kind { get { return (GCHandleKind)GetInt32At(HostOffset(8, 2)); } }
        public int Generation { get { return GetInt32At(HostOffset(12, 2)); } }
        public long AppDomainID { get { return GetInt64At(HostOffset(16, 2)); } }
        public int ClrInstanceID { get { return GetInt16At(HostOffset(24, 2)); } }

        #region Private
        internal SetGCHandleTraceData(Action<SetGCHandleTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<SetGCHandleTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(26, 2)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(26, 2)));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "HandleID", HandleID);
            XmlAttribHex(sb, "ObjectID", ObjectID);
            XmlAttrib(sb, "Kind", Kind);
            XmlAttrib(sb, "Generation", Generation);
            XmlAttribHex(sb, "AppDomainID", AppDomainID);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "HandleID", "ObjectID", "Kind", "Generation", "AppDomainID", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return HandleID;
                case 1:
                    return ObjectID;
                case 2:
                    return Kind;
                case 3:
                    return Generation;
                case 4:
                    return AppDomainID;
                case 5:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<SetGCHandleTraceData> Action;
        #endregion
    }
    public sealed class DestroyGCHandleTraceData : TraceEvent
    {
        public Address HandleID { get { return GetAddressAt(0); } }
        public int ClrInstanceID { get { return GetInt16At(HostOffset(4, 1)); } }

        #region Private
        internal DestroyGCHandleTraceData(Action<DestroyGCHandleTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<DestroyGCHandleTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(6, 1)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(6, 1)));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "HandleID", HandleID);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "HandleID", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return HandleID;
                case 1:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<DestroyGCHandleTraceData> Action;
        #endregion
    }
    public sealed class PinObjectAtGCTimeTraceData : TraceEvent
    {
        public Address HandleID { get { return GetAddressAt(0); } }
        public Address ObjectID { get { return GetAddressAt(HostOffset(4, 1)); } }
        public long ObjectSize { get { return GetInt64At(HostOffset(8, 2)); } }
        // TODO you can remove the length test after 2104.  It was an old internal case 
        public string TypeName { get { if (HostOffset(16, 2) < EventDataLength) { return GetUnicodeStringAt(HostOffset(16, 2)); } return ""; } }
        public int ClrInstanceID { get { return GetInt16At(SkipUnicodeString(HostOffset(16, 2))); } }

        #region Private
        internal PinObjectAtGCTimeTraceData(Action<PinObjectAtGCTimeTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<PinObjectAtGCTimeTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUnicodeString(HostOffset(16, 2)) + 2));
            Debug.Assert(!(Version > 0 && EventDataLength < SkipUnicodeString(HostOffset(16, 2)) + 2));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "HandleID", HandleID);
            XmlAttribHex(sb, "ObjectID", ObjectID);
            XmlAttrib(sb, "ObjectSize", ObjectSize);
            XmlAttrib(sb, "TypeName", TypeName);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "HandleID", "ObjectID", "ObjectSize", "TypeName", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return HandleID;
                case 1:
                    return ObjectID;
                case 2:
                    return ObjectSize;
                case 3:
                    return TypeName;
                case 4:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<PinObjectAtGCTimeTraceData> Action;
        #endregion
    }
    public sealed class PinPlugAtGCTimeTraceData : TraceEvent
    {
        public Address PlugStart { get { return GetAddressAt(0); } }
        public Address PlugEnd { get { return GetAddressAt(HostOffset(4, 1)); } }
        public Address GapBeforeSize { get { return GetAddressAt(HostOffset(8, 2)); } }
        public int ClrInstanceID { get { return GetInt16At(HostOffset(12, 3)); } }

        #region Private
        internal PinPlugAtGCTimeTraceData(Action<PinPlugAtGCTimeTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<PinPlugAtGCTimeTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(14, 3)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(14, 3)));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "PlugStart", PlugStart);
            XmlAttribHex(sb, "PlugEnd", PlugEnd);
            XmlAttribHex(sb, "GapBeforeSize", GapBeforeSize);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "PlugStart", "PlugEnd", "GapBeforeSize", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return PlugStart;
                case 1:
                    return PlugEnd;
                case 2:
                    return GapBeforeSize;
                case 3:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<PinPlugAtGCTimeTraceData> Action;
        #endregion
    }
    public sealed class GCTriggeredTraceData : TraceEvent
    {
        public GCReason Reason { get { return (GCReason)GetInt32At(0); } }
        public int ClrInstanceID { get { return GetInt16At(4); } }

        #region Private
        internal GCTriggeredTraceData(Action<GCTriggeredTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<GCTriggeredTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 6));
            Debug.Assert(!(Version > 0 && EventDataLength < 6));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "Reason", Reason);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Reason", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Reason;
                case 1:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<GCTriggeredTraceData> Action;
        #endregion
    }
    public sealed class GCBulkRootCCWTraceData : TraceEvent
    {
        public int Count
        {
            get
            {
                int len = EventDataLength;
                int ret = GetInt32At(0);
                // V4.5.1 uses this same event for somethings else.  Ignore it by setting the count to 0.  
                if (EventDataLength < ret * 40 + 6)
                {
                    ret = 0;
                }

                return ret;
            }
        }
        public int ClrInstanceID { get { return GetInt16At(4); } }

        /// <summary>
        /// Returns the CCW at the given zero-based index (index less than Count).   The returned GCBulkRootCCWValues 
        /// points the the data in GCBulkRootCCWTraceData so it cannot live beyond that lifetime.  
        /// </summary>
        public GCBulkRootCCWValues Values(int index) { return new GCBulkRootCCWValues(this, 6 + index * ValueSize); }
        #region Private
        internal GCBulkRootCCWTraceData(Action<GCBulkRootCCWTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<GCBulkRootCCWTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 0 + (Count * ValueSize) + 6 && Count != 0)); // The Count==0 fixes a old bad event using the same ID. 
            Debug.Assert(!(Version > 0 && EventDataLength < (Count * ValueSize) + 6));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "Count", Count);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.AppendLine(">");
            for (int i = 0; i < Count; i++)
            {
                Values(i).ToXml(sb).AppendLine();
            }

            sb.Append("</Event>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Count", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Count;
                case 1:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<GCBulkRootCCWTraceData> Action;
        /// <summary>
        /// Computes the size of one GCBulkRootCCWValues structure.  
        /// TODO FIX NOW Can rip out and make a constant 44 after 6/2014
        /// </summary>
        private int ValueSize
        {
            get
            {
                if (m_valueSize == 0)
                {
                    m_valueSize = 44;
                    // Project N rounds up on 64 bit It did go out for build in 4/2014 but soon we won't care.  
                    if (EventDataLength == (Count * 48) + 6)
                    {
                        Debug.Assert(PointerSize == 8);
                        m_valueSize = 48;
                    }
                }
                return m_valueSize;
            }
        }
        private int m_valueSize;

        #endregion
    }

    /// <summary>
    /// This structure just POINTS at the data in the GCBulkRootCCWTraceData.  It can only be used as long as
    /// the GCBulkRootCCWTraceData is alive which (unless you cloned it) is only for the lifetime of the callback.  
    /// </summary>
    public struct GCBulkRootCCWValues
    {
        public Address GCRootID { get { return m_data.GetAddressAt(m_baseOffset); } }
        public Address ObjectID { get { return m_data.GetAddressAt(m_baseOffset + 8); } }
        public Address TypeID { get { return m_data.GetAddressAt(m_baseOffset + 16); } }
        public Address IUnknown { get { return m_data.GetAddressAt(m_baseOffset + 24); } }
        public int RefCount { get { return m_data.GetInt32At(m_baseOffset + 32); } }
        public int PeggedRefCount { get { return m_data.GetInt32At(m_baseOffset + 36); } }
        public GCRootCCWFlags Flags { get { return (GCRootCCWFlags)m_data.GetInt32At(m_baseOffset + 40); } }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            return ToXml(sb).ToString();
        }
        public StringBuilder ToXml(StringBuilder sb)
        {
            sb.Append(" <GCBulkRootCCWValue ");
            TraceEvent.XmlAttribHex(sb, "GCRootID", GCRootID);
            TraceEvent.XmlAttribHex(sb, "ObjectID", ObjectID);
            TraceEvent.XmlAttribHex(sb, "TypeID", TypeID);
            TraceEvent.XmlAttribHex(sb, "IUnknown", IUnknown).AppendLine().Append("  ");
            TraceEvent.XmlAttrib(sb, "RefCount", RefCount);
            TraceEvent.XmlAttrib(sb, "PeggedRefCount", PeggedRefCount);
            TraceEvent.XmlAttrib(sb, "Flags", Flags);
            sb.Append("/>");
            return sb;
        }
        #region private
        internal GCBulkRootCCWValues(TraceEvent data, int baseOffset)
        {
            m_data = data; m_baseOffset = baseOffset;
            Debug.Assert(PeggedRefCount < 10000);
            Debug.Assert(RefCount < 10000);
            Debug.Assert((GCRootID & 0x0000000000000003L) == 0);
            Debug.Assert((ObjectID & 00000000000000003L) == 0);
            Debug.Assert((TypeID & 0x0000000000000003L) == 0);
            Debug.Assert((IUnknown & 0x0000000000000003L) == 0);
        }

        private TraceEvent m_data;
        private int m_baseOffset;
        #endregion
    }

    public sealed class GCBulkRCWTraceData : TraceEvent
    {
        public int Count
        {
            get
            {
                int len = EventDataLength;
                int ret = GetInt32At(0);
                // V4.5.1 uses this same event for somethings else.  Ignore it by setting the count to 0.  
                if (EventDataLength < ret * 40 + 6)
                {
                    ret = 0;
                }

                return ret;
            }
        }
        public int ClrInstanceID { get { return GetInt16At(4); } }

        /// <summary>
        /// Returns the edge at the given zero-based index (index less than Count).   The returned GCBulkRCWValues 
        /// points the the data in GCBulkRCWTraceData so it cannot live beyond that lifetime.  
        /// </summary>
        public GCBulkRCWValues Values(int index) { return new GCBulkRCWValues(this, 6 + index * 40); }

        #region Private
        internal GCBulkRCWTraceData(Action<GCBulkRCWTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<GCBulkRCWTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 0 + (Count * 40) + 6 && Count != 0)); // The Count==0 fixes a old bad event using the same ID. 
            Debug.Assert(!(Version > 0 && EventDataLength < 0 + (Count * 40) + 6));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "Count", Count);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.AppendLine(">");
            for (int i = 0; i < Count; i++)
            {
                Values(i).ToXml(sb).AppendLine();
            }

            sb.Append("</Event>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Count", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Count;
                case 1:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<GCBulkRCWTraceData> Action;
        #endregion
    }

    /// <summary>
    /// This structure just POINTS at the data in the GCBulkRCWTraceData.  It can only be used as long as
    /// the GCBulkRCWTraceData is alive which (unless you cloned it) is only for the lifetime of the callback.  
    /// </summary>
    public struct GCBulkRCWValues
    {
        public Address ObjectID { get { return m_data.GetAddressAt(m_baseOffset); } }
        public Address TypeID { get { return m_data.GetAddressAt(m_baseOffset + 8); } }
        public Address IUnknown { get { return m_data.GetAddressAt(m_baseOffset + 16); } }
        public Address VTable { get { return m_data.GetAddressAt(m_baseOffset + 24); } }
        public int RefCount { get { return m_data.GetInt32At(m_baseOffset + 32); } }
        public GCRootRCWFlags Flags { get { return (GCRootRCWFlags)m_data.GetInt32At(m_baseOffset + 36); } }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            return ToXml(sb).ToString();
        }
        public StringBuilder ToXml(StringBuilder sb)
        {
            sb.Append(" <GCBulkRCWValue ");
            TraceEvent.XmlAttribHex(sb, "ObjectID", ObjectID);
            TraceEvent.XmlAttribHex(sb, "TypeID", TypeID);
            TraceEvent.XmlAttribHex(sb, "IUnknown", IUnknown);
            TraceEvent.XmlAttribHex(sb, "VTable", VTable).AppendLine().Append("  ");
            TraceEvent.XmlAttrib(sb, "RefCount", RefCount);
            TraceEvent.XmlAttrib(sb, "Flags", Flags);
            sb.Append("/>");
            return sb;
        }
        #region private
        internal GCBulkRCWValues(TraceEvent data, int baseOffset)
        {
            m_data = data; m_baseOffset = baseOffset;
            Debug.Assert((ObjectID & 0x0000000000000003L) == 0);
            Debug.Assert((TypeID & 0x0000000000000003L) == 0);
            Debug.Assert((IUnknown & 0x0000000000000003L) == 0);
            Debug.Assert((VTable & 0xFF00000000000003L) == 0);
            Debug.Assert(RefCount < 10000);
        }

        private TraceEvent m_data;
        private int m_baseOffset;
        #endregion
    }

    public sealed class GCBulkRootStaticVarTraceData : TraceEvent
    {
        public int Count { get { return GetInt32At(0); } }
        public long AppDomainID { get { return GetInt64At(4); } }
        public int ClrInstanceID { get { return GetInt16At(12); } }

        /// <summary>
        /// Returns 'idx'th static root.   
        /// The returned GCBulkRootStaticVarStatics cannot live beyond the TraceEvent that it comes from.  
        /// The implementation is highly tuned for sequential access.  
        /// </summary>
        public GCBulkRootStaticVarValues Values(int index)
        {
            return new GCBulkRootStaticVarValues(this, OffsetForIndexInValuesArray(index));
        }

        #region Private
        internal GCBulkRootStaticVarTraceData(Action<GCBulkRootStaticVarTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            m_lastIdx = 0xFFFF; // Invalidate the cache    
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<GCBulkRootStaticVarTraceData>)value; }
        }
        protected internal override void Validate()
        {
            m_lastIdx = 0xFFFF; // Invalidate the cache    
            Debug.Assert(!(EventDataLength != OffsetForIndexInValuesArray(Count)));
            Debug.Assert(Count == 0 || (int)Values(Count - 1).Flags < 256);     // This just makes the asserts in the BulkType kick in 
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "Count", Count);
            XmlAttrib(sb, "AppDomainID", AppDomainID);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.AppendLine(">");
            for (int i = 0; i < Count; i++)
            {
                Values(i).ToXml(sb).AppendLine();
            }

            sb.Append("</Event>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Count", "AppDomainID", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Count;
                case 1:
                    return AppDomainID;
                case 2:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private int OffsetForIndexInValuesArray(int targetIdx)
        {
            Debug.Assert(targetIdx <= Count);
            int offset;
            int idx;
            if (m_lastIdx <= targetIdx)
            {
                idx = m_lastIdx;
                offset = m_lastOffset;
            }
            else
            {
                idx = 0;
                offset = 14;
            }
            while (idx < targetIdx)
            {
                offset = SkipUnicodeString(offset + 28);
                idx++;
            }
            Debug.Assert(offset <= EventDataLength);
            m_lastIdx = (ushort)targetIdx;
            m_lastOffset = (ushort)offset;
            Debug.Assert(m_lastIdx == targetIdx && m_lastOffset == offset);     // No truncation
            return offset;
        }
        // These remember the last offset of the element in Statics to optimize a linear scan.  
        private ushort m_lastIdx;
        private ushort m_lastOffset;

        private event Action<GCBulkRootStaticVarTraceData> Action;
        #endregion
    }

    /// <summary>
    /// This structure just POINTS at the data in the GCBulkRootStaticVarTraceData.  It can only be used as long as
    /// the GCBulkRootStaticVarTraceData is alive which (unless you cloned it) is only for the lifetime of the callback.  
    /// </summary>
    public struct GCBulkRootStaticVarValues
    {
        public Address GCRootID { get { return (Address)m_data.GetInt64At(m_baseOffset); } }
        public Address ObjectID { get { return (Address)m_data.GetInt64At(m_baseOffset + 8); } }
        public Address TypeID { get { return (Address)m_data.GetInt64At(m_baseOffset + 16); } }
        public GCRootStaticVarFlags Flags { get { return (GCRootStaticVarFlags)m_data.GetInt32At(m_baseOffset + 24); } }
        public string FieldName { get { return m_data.GetUnicodeStringAt(m_baseOffset + 28); } }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            return ToXml(sb).ToString();
        }
        public StringBuilder ToXml(StringBuilder sb)
        {
            sb.Append(" <GCBulkRootStaticVarValue ");
            TraceEvent.XmlAttrib(sb, "FieldName", FieldName);
            TraceEvent.XmlAttribHex(sb, "GCRootID", GCRootID);
            TraceEvent.XmlAttribHex(sb, "ObjectID", ObjectID).AppendLine().Append("  ");
            TraceEvent.XmlAttribHex(sb, "TypeID", TypeID);
            TraceEvent.XmlAttrib(sb, "Flags", Flags);
            sb.Append("/>");
            return sb;
        }
        #region private
        internal GCBulkRootStaticVarValues(TraceEvent data, int baseOffset)
        {
            m_data = data; m_baseOffset = baseOffset;
            Debug.Assert((GCRootID & 0xFF00000000000003L) == 0);
            Debug.Assert((ObjectID & 0xFF00000000000003L) == 0);
            Debug.Assert((TypeID & 0xFF00000000000001L) == 0);
            Debug.Assert(((int)Flags & 0xFFFFFFF0) == 0);      // We don't use the upper bits presently so we can assert they are not used as a validity check. 
        }

        private TraceEvent m_data;
        private int m_baseOffset;
        #endregion
    }

    public sealed class ClrWorkerThreadTraceData : TraceEvent
    {
        public int WorkerThreadCount { get { return GetInt32At(0); } }
        public int RetiredWorkerThreads { get { return GetInt32At(4); } }

        #region Private
        internal ClrWorkerThreadTraceData(Action<ClrWorkerThreadTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ClrWorkerThreadTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 8));
            Debug.Assert(!(Version > 0 && EventDataLength < 8));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "WorkerThreadCount", WorkerThreadCount);
            XmlAttrib(sb, "RetiredWorkerThreads", RetiredWorkerThreads);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "WorkerThreadCount", "RetiredWorkerThreads" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return WorkerThreadCount;
                case 1:
                    return RetiredWorkerThreads;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ClrWorkerThreadTraceData> Action;
        #endregion
    }
    public sealed class IOThreadTraceData : TraceEvent
    {
        public int IOThreadCount { get { return GetInt32At(0); } }
        public int RetiredIOThreads { get { return GetInt32At(4); } }
        public int ClrInstanceID { get { if (Version >= 1) { return GetInt16At(8); } return 0; } }

        #region Private
        internal IOThreadTraceData(Action<IOThreadTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<IOThreadTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 8));
            Debug.Assert(!(Version == 1 && EventDataLength != 10));
            Debug.Assert(!(Version > 1 && EventDataLength < 10));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "IOThreadCount", IOThreadCount);
            XmlAttrib(sb, "RetiredIOThreads", RetiredIOThreads);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "IOThreadCount", "RetiredIOThreads", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return IOThreadCount;
                case 1:
                    return RetiredIOThreads;
                case 2:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<IOThreadTraceData> Action;
        #endregion
    }
    public sealed class ClrThreadPoolSuspendTraceData : TraceEvent
    {
        public int ClrThreadID { get { return GetInt32At(0); } }
        public int CpuUtilization { get { return GetInt32At(4); } }

        #region Private
        internal ClrThreadPoolSuspendTraceData(Action<ClrThreadPoolSuspendTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ClrThreadPoolSuspendTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 8));
            Debug.Assert(!(Version > 0 && EventDataLength < 8));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ClrThreadID", ClrThreadID);
            XmlAttrib(sb, "CpuUtilization", CpuUtilization);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "ClrThreadID", "CpuUtilization" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ClrThreadID;
                case 1:
                    return CpuUtilization;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ClrThreadPoolSuspendTraceData> Action;
        #endregion
    }
    public sealed class ThreadPoolWorkerThreadTraceData : TraceEvent
    {
        public int ActiveWorkerThreadCount { get { return GetInt32At(0); } }
        public int RetiredWorkerThreadCount { get { return GetInt32At(4); } }
        public int ClrInstanceID { get { return GetInt16At(8); } }

        #region Private
        internal ThreadPoolWorkerThreadTraceData(Action<ThreadPoolWorkerThreadTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ThreadPoolWorkerThreadTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 10));
            Debug.Assert(!(Version > 0 && EventDataLength < 10));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ActiveWorkerThreadCount", ActiveWorkerThreadCount);
            XmlAttrib(sb, "RetiredWorkerThreadCount", RetiredWorkerThreadCount);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "ActiveWorkerThreadCount", "RetiredWorkerThreadCount", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ActiveWorkerThreadCount;
                case 1:
                    return RetiredWorkerThreadCount;
                case 2:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ThreadPoolWorkerThreadTraceData> Action;
        #endregion
    }
    public sealed class ThreadPoolWorkerThreadAdjustmentSampleTraceData : TraceEvent
    {
        public double Throughput { get { return GetDoubleAt(0); } }
        public int ClrInstanceID { get { return GetInt16At(8); } }

        #region Private
        internal ThreadPoolWorkerThreadAdjustmentSampleTraceData(Action<ThreadPoolWorkerThreadAdjustmentSampleTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ThreadPoolWorkerThreadAdjustmentSampleTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 10));
            Debug.Assert(!(Version > 0 && EventDataLength < 10));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "Throughput", Throughput);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Throughput", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Throughput;
                case 1:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ThreadPoolWorkerThreadAdjustmentSampleTraceData> Action;
        #endregion
    }
    public sealed class ThreadPoolWorkerThreadAdjustmentTraceData : TraceEvent
    {
        public double AverageThroughput { get { return GetDoubleAt(0); } }
        public int NewWorkerThreadCount { get { return GetInt32At(8); } }
        public ThreadAdjustmentReason Reason { get { return (ThreadAdjustmentReason)GetInt32At(12); } }
        public int ClrInstanceID { get { return GetInt16At(16); } }

        #region Private
        internal ThreadPoolWorkerThreadAdjustmentTraceData(Action<ThreadPoolWorkerThreadAdjustmentTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ThreadPoolWorkerThreadAdjustmentTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 18));
            Debug.Assert(!(Version > 0 && EventDataLength < 18));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "AverageThroughput", AverageThroughput);
            XmlAttrib(sb, "NewWorkerThreadCount", NewWorkerThreadCount);
            XmlAttrib(sb, "Reason", Reason);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "AverageThroughput", "NewWorkerThreadCount", "Reason", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return AverageThroughput;
                case 1:
                    return NewWorkerThreadCount;
                case 2:
                    return Reason;
                case 3:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ThreadPoolWorkerThreadAdjustmentTraceData> Action;
        #endregion
    }
    public sealed class ThreadPoolWorkerThreadAdjustmentStatsTraceData : TraceEvent
    {
        public double Duration { get { return GetDoubleAt(0); } }
        public double Throughput { get { return GetDoubleAt(8); } }
        public double ThreadWave { get { return GetDoubleAt(16); } }
        public double ThroughputWave { get { return GetDoubleAt(24); } }
        public double ThroughputErrorEstimate { get { return GetDoubleAt(32); } }
        public double AverageThroughputErrorEstimate { get { return GetDoubleAt(40); } }
        public double ThroughputRatio { get { return GetDoubleAt(48); } }
        public double Confidence { get { return GetDoubleAt(56); } }
        public double NewControlSetting { get { return GetDoubleAt(64); } }
        public int NewThreadWaveMagnitude { get { return GetInt16At(72); } }
        public int ClrInstanceID { get { return GetInt16At(74); } }

        #region Private
        internal ThreadPoolWorkerThreadAdjustmentStatsTraceData(Action<ThreadPoolWorkerThreadAdjustmentStatsTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ThreadPoolWorkerThreadAdjustmentStatsTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 76));
            Debug.Assert(!(Version > 0 && EventDataLength < 76));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "Duration", Duration);
            XmlAttrib(sb, "Throughput", Throughput);
            XmlAttrib(sb, "ThreadWave", ThreadWave);
            XmlAttrib(sb, "ThroughputWave", ThroughputWave);
            XmlAttrib(sb, "ThroughputErrorEstimate", ThroughputErrorEstimate);
            XmlAttrib(sb, "AverageThroughputErrorEstimate", AverageThroughputErrorEstimate);
            XmlAttrib(sb, "ThroughputRatio", ThroughputRatio);
            XmlAttrib(sb, "Confidence", Confidence);
            XmlAttrib(sb, "NewControlSetting", NewControlSetting);
            XmlAttrib(sb, "NewThreadWaveMagnitude", NewThreadWaveMagnitude);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Duration", "Throughput", "ThreadWave", "ThroughputWave", "ThroughputErrorEstimate", "AverageThroughputErrorEstimate", "ThroughputRatio", "Confidence", "NewControlSetting", "NewThreadWaveMagnitude", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Duration;
                case 1:
                    return Throughput;
                case 2:
                    return ThreadWave;
                case 3:
                    return ThroughputWave;
                case 4:
                    return ThroughputErrorEstimate;
                case 5:
                    return AverageThroughputErrorEstimate;
                case 6:
                    return ThroughputRatio;
                case 7:
                    return Confidence;
                case 8:
                    return NewControlSetting;
                case 9:
                    return NewThreadWaveMagnitude;
                case 10:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ThreadPoolWorkerThreadAdjustmentStatsTraceData> Action;
        #endregion
    }
    public sealed class ThreadPoolWorkingThreadCountTraceData : TraceEvent
    {
        public int Count { get { return GetInt32At(0); } }
        public int ClrInstanceID { get { return GetInt16At(4); } }

        #region Private
        internal ThreadPoolWorkingThreadCountTraceData(Action<ThreadPoolWorkingThreadCountTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ThreadPoolWorkingThreadCountTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 6));
            Debug.Assert(!(Version > 0 && EventDataLength < 6));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "Count", Count);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Count", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Count;
                case 1:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ThreadPoolWorkingThreadCountTraceData> Action;
        #endregion
    }
    public sealed class ThreadPoolWorkTraceData : TraceEvent
    {
        public Address WorkID { get { return GetAddressAt(0); } }
        public int ClrInstanceID { get { return GetInt16At(HostOffset(4, 1)); } }

        #region Private
        internal ThreadPoolWorkTraceData(Action<ThreadPoolWorkTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ThreadPoolWorkTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(6, 1)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(6, 1)));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "WorkID", WorkID);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "WorkID", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return WorkID;
                case 1:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ThreadPoolWorkTraceData> Action;
        #endregion
    }
    public sealed class ThreadPoolIOWorkEnqueueTraceData : TraceEvent
    {
        public Address NativeOverlapped { get { return GetAddressAt(0); } }
        public Address Overlapped { get { return GetAddressAt(HostOffset(4, 1)); } }
        public bool MultiDequeues { get { return GetInt32At(HostOffset(8, 2)) != 0; } }
        public int ClrInstanceID { get { return GetInt16At(HostOffset(12, 2)); } }

        #region Private
        internal ThreadPoolIOWorkEnqueueTraceData(Action<ThreadPoolIOWorkEnqueueTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ThreadPoolIOWorkEnqueueTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(14, 2)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(14, 2)));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "NativeOverlapped", NativeOverlapped);
            XmlAttribHex(sb, "Overlapped", Overlapped);
            XmlAttrib(sb, "MultiDequeues", MultiDequeues);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "NativeOverlapped", "Overlapped", "MultiDequeues", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return NativeOverlapped;
                case 1:
                    return Overlapped;
                case 2:
                    return MultiDequeues;
                case 3:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ThreadPoolIOWorkEnqueueTraceData> Action;
        #endregion
    }
    public sealed class ThreadPoolIOWorkTraceData : TraceEvent
    {
        public Address NativeOverlapped { get { return GetAddressAt(0); } }
        public Address Overlapped { get { return GetAddressAt(HostOffset(4, 1)); } }
        public int ClrInstanceID { get { return GetInt16At(HostOffset(8, 2)); } }

        #region Private
        internal ThreadPoolIOWorkTraceData(Action<ThreadPoolIOWorkTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ThreadPoolIOWorkTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(10, 2)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(10, 2)));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "NativeOverlapped", NativeOverlapped);
            XmlAttribHex(sb, "Overlapped", Overlapped);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "NativeOverlapped", "Overlapped", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return NativeOverlapped;
                case 1:
                    return Overlapped;
                case 2:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ThreadPoolIOWorkTraceData> Action;
        #endregion
    }
    public sealed class ThreadStartWorkTraceData : TraceEvent
    {
        public Address ThreadStartWorkID { get { return GetAddressAt(0); } }
        public int ClrInstanceID { get { return GetInt16At(HostOffset(4, 1)); } }

        #region Private
        internal ThreadStartWorkTraceData(Action<ThreadStartWorkTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ThreadStartWorkTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != HostOffset(6, 1)));
            Debug.Assert(!(Version > 0 && EventDataLength < HostOffset(6, 1)));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "ID", ThreadStartWorkID);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "ID", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ThreadStartWorkID;
                case 1:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ThreadStartWorkTraceData> Action;
        #endregion
    }
    public sealed class TieredCompilationEmptyTraceData : TraceEvent
    {
        public int ClrInstanceID { get { return GetInt16At(0); } }

        #region Private
        internal TieredCompilationEmptyTraceData(Action<TieredCompilationEmptyTraceData> target, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            m_target = target;
        }
        protected internal override void Dispatch()
        {
            m_target(this);
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 2));
            Debug.Assert(!(Version > 0 && EventDataLength < 2));
        }
        protected internal override Delegate Target
        {
            get { return m_target; }
            set { m_target = (Action<TieredCompilationEmptyTraceData>)value; }
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[] { "ClrInstanceID" };
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<TieredCompilationEmptyTraceData> m_target;
        #endregion
    }
    public sealed class TieredCompilationSettingsTraceData : TraceEvent
    {
        public int ClrInstanceID { get { return GetInt16At(0); } }
        public TieredCompilationSettingsFlags Flags { get { return (TieredCompilationSettingsFlags)GetInt32At(2); } }

        #region Private
        internal TieredCompilationSettingsTraceData(Action<TieredCompilationSettingsTraceData> target, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            m_target = target;
        }
        protected internal override void Dispatch()
        {
            m_target(this);
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 6));
            Debug.Assert(!(Version > 0 && EventDataLength < 6));
        }
        protected internal override Delegate Target
        {
            get { return m_target; }
            set { m_target = (Action<TieredCompilationSettingsTraceData>)value; }
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            XmlAttrib(sb, "Flags", Flags);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[] { "ClrInstanceID", "Flags" };
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ClrInstanceID;
                case 1:
                    return Flags;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<TieredCompilationSettingsTraceData> m_target;
        #endregion
    }
    public sealed class TieredCompilationResumeTraceData : TraceEvent
    {
        public int ClrInstanceID { get { return GetInt16At(0); } }
        public int NewMethodCount { get { return GetInt32At(2); } }

        #region Private
        internal TieredCompilationResumeTraceData(Action<TieredCompilationResumeTraceData> target, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            m_target = target;
        }
        protected internal override void Dispatch()
        {
            m_target(this);
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 6));
            Debug.Assert(!(Version > 0 && EventDataLength < 6));
        }
        protected internal override Delegate Target
        {
            get { return m_target; }
            set { m_target = (Action<TieredCompilationResumeTraceData>)value; }
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            XmlAttrib(sb, "NewMethodCount", NewMethodCount);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[] { "ClrInstanceID", "NewMethodCount" };
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ClrInstanceID;
                case 1:
                    return NewMethodCount;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<TieredCompilationResumeTraceData> m_target;
        #endregion
    }
    public sealed class TieredCompilationBackgroundJitStartTraceData : TraceEvent
    {
        public int ClrInstanceID { get { return GetInt16At(0); } }
        public int PendingMethodCount { get { return GetInt32At(2); } }

        #region Private
        internal TieredCompilationBackgroundJitStartTraceData(Action<TieredCompilationBackgroundJitStartTraceData> target, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            m_target = target;
        }
        protected internal override void Dispatch()
        {
            m_target(this);
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 6));
            Debug.Assert(!(Version > 0 && EventDataLength < 6));
        }
        protected internal override Delegate Target
        {
            get { return m_target; }
            set { m_target = (Action<TieredCompilationBackgroundJitStartTraceData>)value; }
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            XmlAttrib(sb, "PendingMethodCount", PendingMethodCount);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[] { "ClrInstanceID", "PendingMethodCount" };
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ClrInstanceID;
                case 1:
                    return PendingMethodCount;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<TieredCompilationBackgroundJitStartTraceData> m_target;
        #endregion
    }
    public sealed class TieredCompilationBackgroundJitStopTraceData : TraceEvent
    {
        public int ClrInstanceID { get { return GetInt16At(0); } }
        public int PendingMethodCount { get { return GetInt32At(2); } }
        public int JittedMethodCount { get { return GetInt32At(6); } }

        #region Private
        internal TieredCompilationBackgroundJitStopTraceData(Action<TieredCompilationBackgroundJitStopTraceData> target, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            m_target = target;
        }
        protected internal override void Dispatch()
        {
            m_target(this);
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 10));
            Debug.Assert(!(Version > 0 && EventDataLength < 10));
        }
        protected internal override Delegate Target
        {
            get { return m_target; }
            set { m_target = (Action<TieredCompilationBackgroundJitStopTraceData>)value; }
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            XmlAttrib(sb, "PendingMethodCount", PendingMethodCount);
            XmlAttrib(sb, "JittedMethodCount", JittedMethodCount);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[] { "ClrInstanceID", "PendingMethodCount", "JittedMethodCount" };
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ClrInstanceID;
                case 1:
                    return PendingMethodCount;
                case 2:
                    return JittedMethodCount;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<TieredCompilationBackgroundJitStopTraceData> m_target;
        #endregion
    }
    public sealed class ExceptionTraceData : TraceEvent
    {
        public string ExceptionType { get { if (Version >= 1) { return GetUnicodeStringAt(0); } return ""; } }
        public string ExceptionMessage { get { if (Version >= 1) { return GetUnicodeStringAt(SkipUnicodeString(0)); } return ""; } }
        public Address ExceptionEIP { get { if (Version >= 1) { return GetAddressAt(SkipUnicodeString(SkipUnicodeString(0))); } return 0; } }
        public int ExceptionHRESULT { get { if (Version >= 1) { return GetInt32At(HostOffset(SkipUnicodeString(SkipUnicodeString(0)) + 4, 1)); } return 0; } }
        public ExceptionThrownFlags ExceptionFlags { get { if (Version >= 1) { return (ExceptionThrownFlags)GetInt16At(HostOffset(SkipUnicodeString(SkipUnicodeString(0)) + 8, 1)); } return (ExceptionThrownFlags)0; } }
        public int ClrInstanceID { get { if (Version >= 1) { return GetInt16At(HostOffset(SkipUnicodeString(SkipUnicodeString(0)) + 10, 1)); } return 0; } }

        #region Private
        internal ExceptionTraceData(Action<ExceptionTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ExceptionTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 1 && EventDataLength != HostOffset(SkipUnicodeString(SkipUnicodeString(0)) + 12, 1)));
            Debug.Assert(!(Version > 1 && EventDataLength < HostOffset(SkipUnicodeString(SkipUnicodeString(0)) + 12, 1)));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ExceptionType", ExceptionType);
            XmlAttrib(sb, "ExceptionMessage", ExceptionMessage);
            XmlAttribHex(sb, "ExceptionEIP", ExceptionEIP);
            XmlAttribHex(sb, "ExceptionHRESULT", ExceptionHRESULT);
            XmlAttrib(sb, "ExceptionFlags", ExceptionFlags);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "ExceptionType", "ExceptionMessage", "ExceptionEIP", "ExceptionHRESULT", "ExceptionFlags", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ExceptionType;
                case 1:
                    return ExceptionMessage;
                case 2:
                    return ExceptionEIP;
                case 3:
                    return ExceptionHRESULT;
                case 4:
                    return ExceptionFlags;
                case 5:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ExceptionTraceData> Action;
        #endregion
    }
    public sealed class ContentionStartTraceData : TraceEvent
    {
        public ContentionFlags ContentionFlags { get { if (Version >= 1) return (ContentionFlags)GetByteAt(0); return (ContentionFlags)0; } }
        public int ClrInstanceID { get { if (Version >= 1) return GetInt16At(1); return 0; } }

        #region Private
        internal ContentionStartTraceData(Action<ContentionStartTraceData> target, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            m_target = target;
        }
        protected internal override void Dispatch()
        {
            m_target(this);
        }
        protected internal override void Validate()
        {
            // Not sure if hand editing is appropriate but the start event is size 3 whereas the stop event is size 11
            // and both of them come here
            Debug.Assert(!(Version == 1 && EventDataLength != 3 && EventDataLength != 11));
            Debug.Assert(!(Version > 1 && EventDataLength < 3));
        }
        protected internal override Delegate Target
        {
            get { return m_target; }
            set { m_target = (Action<ContentionStartTraceData>)value; }
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ContentionFlags", ContentionFlags);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[] { "ContentionFlags", "ClrInstanceID" };
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ContentionFlags;
                case 1:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ContentionStartTraceData> m_target;
        #endregion
    }
    public sealed class ContentionStopTraceData : TraceEvent
    {
        public ContentionFlags ContentionFlags { get { return (ContentionFlags)GetByteAt(0); } }
        public int ClrInstanceID { get { return GetInt16At(1); } }
        public double DurationNs { get { if (Version >= 1) return GetDoubleAt(3); return 0; } }

        #region Private
        internal ContentionStopTraceData(Action<ContentionStopTraceData> target, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            m_target = target;
        }
        protected internal override void Dispatch()
        {
            m_target(this);
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 3));
            Debug.Assert(!(Version == 1 && EventDataLength != 11));
            Debug.Assert(!(Version > 1 && EventDataLength < 11));
        }
        protected internal override Delegate Target
        {
            get { return m_target; }
            set { m_target = (Action<ContentionStopTraceData>)value; }
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ContentionFlags", ContentionFlags);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            XmlAttrib(sb, "DurationNs", DurationNs);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[] { "ContentionFlags", "ClrInstanceID", "DurationNs" };
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ContentionFlags;
                case 1:
                    return ClrInstanceID;
                case 2:
                    return DurationNs;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ContentionStopTraceData> m_target;
        #endregion
    }
    public sealed class R2RGetEntryPointTraceData : TraceEvent
    {
        public long MethodID { get { return GetInt64At(0); } }
        public string MethodNamespace { get { return GetUnicodeStringAt(8); } }
        public string MethodName { get { return GetUnicodeStringAt(SkipUnicodeString(8)); } }
        public string MethodSignature { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(8))); } }
        public long EntryPoint { get { return GetInt64At(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(8)))); } }
        public int ClrInstanceID { get { return GetInt16At(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(8))) + 8); } }

        #region Private
        internal R2RGetEntryPointTraceData(Action<R2RGetEntryPointTraceData> target, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            this.m_target = target;
        }
        protected internal override void Dispatch()
        {
            m_target(this);
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(8))) + 10));
            Debug.Assert(!(Version > 0 && EventDataLength < SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(8))) + 10));
        }
        protected internal override Delegate Target
        {
            get { return m_target; }
            set { m_target = (Action<R2RGetEntryPointTraceData>)value; }
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "MethodID", MethodID);
            XmlAttrib(sb, "MethodNamespace", MethodNamespace);
            XmlAttrib(sb, "MethodName", MethodName);
            XmlAttrib(sb, "MethodSignature", MethodSignature);
            XmlAttrib(sb, "EntryPoint", EntryPoint);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[] { "MethodID", "MethodNamespace", "MethodName", "MethodSignature", "EntryPoint", "ClrInstanceID" };
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return MethodID;
                case 1:
                    return MethodNamespace;
                case 2:
                    return MethodName;
                case 3:
                    return MethodSignature;
                case 4:
                    return EntryPoint;
                case 5:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<R2RGetEntryPointTraceData> m_target;
        #endregion
    }

    public sealed class R2RGetEntryPointStartTraceData : TraceEvent
    {
        public long MethodID { get { return GetInt64At(0); } }
        public int ClrInstanceID { get { return GetInt16At(8); } }

        #region Private
        internal R2RGetEntryPointStartTraceData(Action<R2RGetEntryPointStartTraceData> target, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            this.m_target = target;
        }
        protected internal override void Dispatch()
        {
            m_target(this);
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 10));
        }
        protected internal override Delegate Target
        {
            get { return m_target; }
            set { m_target = (Action<R2RGetEntryPointStartTraceData>)value; }
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "MethodID", MethodID);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[] { "MethodID", "ClrInstanceID" };
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return MethodID;
                case 1:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<R2RGetEntryPointStartTraceData> m_target;
        #endregion
    }

    public sealed class MethodJitMemoryAllocatedForCodeTraceData : TraceEvent
    {
        public long MethodID { get { return GetInt64At(0); } }
        public long ModuleID { get { return GetInt64At(8); } }
        public long JitHotCodeRequestSize { get { return GetInt64At(16); } }
        public long JitRODataRequestSize { get { return GetInt64At(24); } }
        public long AllocatedSizeForJitCode { get { return GetInt64At(32); } }
        public int JitAllocFlag { get { return GetInt32At(40); } }
        public int ClrInstanceID { get { return GetInt16At(44); } }

        #region Private
        internal MethodJitMemoryAllocatedForCodeTraceData(Action<MethodJitMemoryAllocatedForCodeTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 46));
            Debug.Assert(!(Version > 0 && EventDataLength < 46));
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<MethodJitMemoryAllocatedForCodeTraceData>)value; }
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "MethodID", MethodID);
            XmlAttrib(sb, "ModuleID", ModuleID);
            XmlAttrib(sb, "JitHotCodeRequestSize", JitHotCodeRequestSize);
            XmlAttrib(sb, "JitRODataRequestSize", JitRODataRequestSize);
            XmlAttrib(sb, "AllocatedSizeForJitCode", AllocatedSizeForJitCode);
            XmlAttrib(sb, "JitAllocFlag", JitAllocFlag);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[] { "MethodID", "ModuleID", "JitHotCodeRequestSize", "JitRODataRequestSize", "AllocatedSizeForJitCode", "JitAllocFlag", "ClrInstanceID" };
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return MethodID;
                case 1:
                    return ModuleID;
                case 2:
                    return JitHotCodeRequestSize;
                case 3:
                    return JitRODataRequestSize;
                case 4:
                    return AllocatedSizeForJitCode;
                case 5:
                    return JitAllocFlag;
                case 6:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        public static ulong GetKeywords() { return 16; }
        public static string GetProviderName() { return "Microsoft-Windows-DotNETRuntime"; }
        public static Guid GetProviderGuid() { return new Guid("e13c0d23-ccbc-4e12-931b-d9cc2eee27e4"); }
        private event Action<MethodJitMemoryAllocatedForCodeTraceData> Action;
        #endregion
    }

    public sealed class TypeLoadStartTraceData : TraceEvent
    {
        public int TypeLoadStartID { get { return GetInt32At(0); } }
        public int ClrInstanceID { get { return GetInt16At(4); } }

        #region Private
        internal TypeLoadStartTraceData(Action<TypeLoadStartTraceData> target, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            this.m_target = target;
        }
        protected internal override void Dispatch()
        {
            m_target(this);
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 6));
        }
        protected internal override Delegate Target
        {
            get { return m_target; }
            set { m_target = (Action<TypeLoadStartTraceData>)value; }
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "TypeLoadStartID", TypeLoadStartID);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[] { "TypeLoadStartID", "ClrInstanceID" };
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return TypeLoadStartID;
                case 1:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<TypeLoadStartTraceData> m_target;
        #endregion
    }

    public sealed class TypeLoadStopTraceData : TraceEvent
    {
        public int TypeLoadStartID { get { return GetInt32At(0); } }
        public int ClrInstanceID { get { return GetInt16At(4); } }
        public int LoadLevel { get { return GetInt16At(6); } }
        public long TypeID { get { return GetInt64At(8); } }
        public string TypeName { get { return GetUnicodeStringAt(16); } }

        #region Private
        internal TypeLoadStopTraceData(Action<TypeLoadStopTraceData> target, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            this.m_target = target;
        }
        protected internal override void Dispatch()
        {
            m_target(this);
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != (16 + SkipUnicodeString(16))));
        }
        protected internal override Delegate Target
        {
            get { return m_target; }
            set { m_target = (Action<TypeLoadStopTraceData>)value; }
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "TypeLoadStartID", TypeLoadStartID);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            XmlAttrib(sb, "LoadLevel", LoadLevel);
            XmlAttrib(sb, "TypeID", TypeID);
            XmlAttrib(sb, "TypeName", TypeName);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[] { "TypeLoadStartID", "ClrInstanceID", "LoadLevel", "TypeID", "TypeName" };
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return TypeLoadStartID;
                case 1:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<TypeLoadStopTraceData> m_target;
        #endregion
    }

    public sealed class MethodILToNativeMapTraceData : TraceEvent
    {
        private const int ILProlog = -2;    // Returned by ILOffset to represent the prologue of the method
        private const int ILEpilog = -3;    // Returned by ILOffset to represent the epilogue of the method

        public long MethodID { get { return GetInt64At(0); } }
        public long ReJITID { get { return GetInt64At(8); } }
        public int MethodExtent { get { return GetByteAt(16); } }
        public int CountOfMapEntries { get { return GetInt16At(17); } }
        // May also return the special values ILProlog (-2) and ILEpilog (-3) 
        public int ILOffset(int index) { return GetInt32At(index * 4 + 19); }
        public int NativeOffset(int index) { return GetInt32At((CountOfMapEntries + index) * 4 + 19); }
        public int ClrInstanceID { get { return GetInt16At(CountOfMapEntries * 8 + 19); } }

        internal unsafe int* ILOffsets { get { return (int*)(((byte*)DataStart) + 19); } }
        internal unsafe int* NativeOffsets { get { return (int*)(((byte*)DataStart) + CountOfMapEntries * 4 + 19); } }

        #region Private
        internal MethodILToNativeMapTraceData(Action<MethodILToNativeMapTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<MethodILToNativeMapTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(Version != 0 || EventDataLength == CountOfMapEntries * 8 + 21);
            Debug.Assert(Version > 0 || EventDataLength >= CountOfMapEntries * 8 + 21);
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "MethodID", MethodID);
            XmlAttrib(sb, "ReJITID", ReJITID);
            XmlAttrib(sb, "MethodExtent", MethodExtent);
            XmlAttrib(sb, "CountOfMapEntries", CountOfMapEntries);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.AppendLine(">");
            for (int i = 0; i < CountOfMapEntries; i++)
            {
                sb.Append("  ").Append(ILOffset(i)).Append("->").Append(NativeOffset(i)).AppendLine();
            }

            sb.Append("</Event>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "MethodID", "ReJITID", "MethodExtent", "CountOfMapEntries", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return MethodID;
                case 1:
                    return ReJITID;
                case 2:
                    return MethodExtent;
                case 3:
                    return CountOfMapEntries;
                case 4:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<MethodILToNativeMapTraceData> Action;
        #endregion
    }

    public sealed class ClrStackWalkTraceData : TraceEvent
    {
        public int ClrInstanceID { get { return GetInt16At(0); } }
        // Skipping Reserved1
        // Skipping Reserved2
        public int FrameCount { get { return GetInt32At(4); } }
        /// <summary>
        /// Fetches the instruction pointer of a eventToStack frame 0 is the deepest frame, and the maximum should
        /// be a thread offset routine (if you get a complete eventToStack).  
        /// </summary>
        /// <param name="index">The index of the frame to fetch.  0 is the CPU EIP, 1 is the Caller of that
        /// routine ...</param>
        /// <returns>The instruction pointer of the specified frame.</returns>
        public Address InstructionPointer(int index)
        {
            Debug.Assert(0 <= index && index < FrameCount);
            return GetAddressAt(8 + index * PointerSize);
        }

        /// <summary>
        /// Access to the instruction pointers as a unsafe memory blob
        /// </summary>
        internal unsafe void* InstructionPointers { get { return ((byte*)DataStart) + 8; } }

        #region Private
        internal ClrStackWalkTraceData(Action<ClrStackWalkTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ClrStackWalkTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(EventDataLength < 6));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            XmlAttrib(sb, "FrameCount", FrameCount);
            sb.AppendLine(">");
            for (int i = 0; i < FrameCount; i++)
            {
                sb.Append("  ");
                sb.Append("0x").Append(((ulong)InstructionPointer(i)).ToString("x"));
            }
            sb.AppendLine();
            sb.Append("</Event>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "ClrInstanceID", "FrameCount" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ClrInstanceID;
                case 1:
                    return FrameCount;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ClrStackWalkTraceData> Action;
        #endregion
    }

    public sealed class CodeSymbolsTraceData : TraceEvent
    {
        public long ModuleId { get { return GetInt64At(0); } }
        public int TotalChunks { get { return GetInt16At(8); } }
        public int ChunkNumber { get { return GetInt16At(10); } }
        public int ChunkLength { get { return GetInt32At(12); } }
        public byte[] Chunk { get { return GetByteArrayAt(16, ChunkLength); } }
        public int ClrInstanceID { get { return GetInt16At(0 + (ChunkLength * 1) + 16); } }

        #region Private
        internal CodeSymbolsTraceData(Action<CodeSymbolsTraceData> target, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            m_target = target;
        }
        protected internal override void Dispatch()
        {
            m_target(this);
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 0 + (ChunkLength * 1) + 18));
            Debug.Assert(!(Version > 0 && EventDataLength < 0 + (ChunkLength * 1) + 18));
        }
        protected internal override Delegate Target
        {
            get { return m_target; }
            set { m_target = (Action<CodeSymbolsTraceData>)value; }
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ModuleId", ModuleId);
            XmlAttrib(sb, "TotalChunks", TotalChunks);
            XmlAttrib(sb, "ChunkNumber", ChunkNumber);
            XmlAttrib(sb, "ChunkLength", ChunkLength);
            XmlAttrib(sb, "Chunk", Chunk);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "ModuleId", "TotalChunks", "ChunkNumber", "ChunkLength", "Chunk", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ModuleId;
                case 1:
                    return TotalChunks;
                case 2:
                    return ChunkNumber;
                case 3:
                    return ChunkLength;
                case 4:
                    return Chunk;
                case 5:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<CodeSymbolsTraceData> m_target;
        #endregion
    }


    public sealed class AppDomainMemAllocatedTraceData : TraceEvent
    {
        public long AppDomainID { get { return GetInt64At(0); } }
        public long Allocated { get { return GetInt64At(8); } }
        public int ClrInstanceID { get { return GetInt16At(16); } }

        #region Private
        internal AppDomainMemAllocatedTraceData(Action<AppDomainMemAllocatedTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<AppDomainMemAllocatedTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 18));
            Debug.Assert(!(Version > 0 && EventDataLength < 18));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "AppDomainID", AppDomainID);
            XmlAttribHex(sb, "Allocated", Allocated);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "AppDomainID", "Allocated", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return AppDomainID;
                case 1:
                    return Allocated;
                case 2:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<AppDomainMemAllocatedTraceData> Action;
        #endregion
    }
    public sealed class AppDomainMemSurvivedTraceData : TraceEvent
    {
        public long AppDomainID { get { return GetInt64At(0); } }
        public long Survived { get { return GetInt64At(8); } }
        public long ProcessSurvived { get { return GetInt64At(16); } }
        public int ClrInstanceID { get { return GetInt16At(24); } }

        #region Private
        internal AppDomainMemSurvivedTraceData(Action<AppDomainMemSurvivedTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<AppDomainMemSurvivedTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 26));
            Debug.Assert(!(Version > 0 && EventDataLength < 26));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "AppDomainID", AppDomainID);
            XmlAttribHex(sb, "Survived", Survived);
            XmlAttribHex(sb, "ProcessSurvived", ProcessSurvived);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "AppDomainID", "Survived", "ProcessSurvived", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return AppDomainID;
                case 1:
                    return Survived;
                case 2:
                    return ProcessSurvived;
                case 3:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<AppDomainMemSurvivedTraceData> Action;
        #endregion
    }
    public sealed class ThreadCreatedTraceData : TraceEvent
    {
        public long ManagedThreadID { get { return GetInt64At(0); } }
        public long AppDomainID { get { return GetInt64At(8); } }
        public int Flags { get { return GetInt32At(16); } }
        public int ManagedThreadIndex { get { return GetInt32At(20); } }
        public int OSThreadID { get { return GetInt32At(24); } }
        public int ClrInstanceID { get { return GetInt16At(28); } }

        #region Private
        internal ThreadCreatedTraceData(Action<ThreadCreatedTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ThreadCreatedTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 30));
            Debug.Assert(!(Version > 0 && EventDataLength < 30));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "ManagedThreadID", ManagedThreadID);
            XmlAttribHex(sb, "AppDomainID", AppDomainID);
            XmlAttribHex(sb, "Flags", Flags);
            XmlAttrib(sb, "ManagedThreadIndex", ManagedThreadIndex);
            XmlAttrib(sb, "OSThreadID", OSThreadID);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "ManagedThreadID", "AppDomainID", "Flags", "ManagedThreadIndex", "OSThreadID", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ManagedThreadID;
                case 1:
                    return AppDomainID;
                case 2:
                    return Flags;
                case 3:
                    return ManagedThreadIndex;
                case 4:
                    return OSThreadID;
                case 5:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ThreadCreatedTraceData> Action;
        #endregion
    }
    public sealed class ThreadTerminatedOrTransitionTraceData : TraceEvent
    {
        public long ManagedThreadID { get { return GetInt64At(0); } }
        public long AppDomainID { get { return GetInt64At(8); } }
        public int ClrInstanceID { get { return GetInt16At(16); } }

        #region Private
        internal ThreadTerminatedOrTransitionTraceData(Action<ThreadTerminatedOrTransitionTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ThreadTerminatedOrTransitionTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 18));
            Debug.Assert(!(Version > 0 && EventDataLength < 18));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "ManagedThreadID", ManagedThreadID);
            XmlAttribHex(sb, "AppDomainID", AppDomainID);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "ManagedThreadID", "AppDomainID", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ManagedThreadID;
                case 1:
                    return AppDomainID;
                case 2:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ThreadTerminatedOrTransitionTraceData> Action;
        #endregion
    }
    public sealed class AppDomainAssemblyResolveHandlerInvokedTraceData : TraceEvent
    {
        public int ClrInstanceID { get { return GetInt16At(0); } }
        public string AssemblyName { get { return GetUnicodeStringAt(2); } }
        public string HandlerName { get { return GetUnicodeStringAt(SkipUnicodeString(2)); } }
        public string ResultAssemblyName { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(2))); } }
        public string ResultAssemblyPath { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(2)))); } }

        #region Private
        internal AppDomainAssemblyResolveHandlerInvokedTraceData(Action<AppDomainAssemblyResolveHandlerInvokedTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(2))))));
            Debug.Assert(!(Version > 0 && EventDataLength < SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(2))))));
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<AppDomainAssemblyResolveHandlerInvokedTraceData>)value; }
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            XmlAttrib(sb, "AssemblyName", AssemblyName);
            XmlAttrib(sb, "HandlerName", HandlerName);
            XmlAttrib(sb, "ResultAssemblyName", ResultAssemblyName);
            XmlAttrib(sb, "ResultAssemblyPath", ResultAssemblyPath);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[] { "ClrInstanceID", "AssemblyName", "HandlerName", "ResultAssemblyName", "ResultAssemblyPath" };
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ClrInstanceID;
                case 1:
                    return AssemblyName;
                case 2:
                    return HandlerName;
                case 3:
                    return ResultAssemblyName;
                case 4:
                    return ResultAssemblyPath;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        public static ulong GetKeywords() { return 4; }
        public static string GetProviderName() { return "Microsoft-Windows-DotNETRuntime"; }
        public static Guid GetProviderGuid() { return new Guid("e13c0d23-ccbc-4e12-931b-d9cc2eee27e4"); }
        private event Action<AppDomainAssemblyResolveHandlerInvokedTraceData> Action;
        #endregion
    }
    public sealed class AssemblyLoadContextResolvingHandlerInvokedTraceData : TraceEvent
    {
        public int ClrInstanceID { get { return GetInt16At(0); } }
        public string AssemblyName { get { return GetUnicodeStringAt(2); } }
        public string HandlerName { get { return GetUnicodeStringAt(SkipUnicodeString(2)); } }
        public string AssemblyLoadContext { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(2))); } }
        public string ResultAssemblyName { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(2)))); } }
        public string ResultAssemblyPath { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(2))))); } }

        #region Private
        internal AssemblyLoadContextResolvingHandlerInvokedTraceData(Action<AssemblyLoadContextResolvingHandlerInvokedTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(2)))))));
            Debug.Assert(!(Version > 0 && EventDataLength < SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(2)))))));
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<AssemblyLoadContextResolvingHandlerInvokedTraceData>)value; }
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            XmlAttrib(sb, "AssemblyName", AssemblyName);
            XmlAttrib(sb, "HandlerName", HandlerName);
            XmlAttrib(sb, "AssemblyLoadContext", AssemblyLoadContext);
            XmlAttrib(sb, "ResultAssemblyName", ResultAssemblyName);
            XmlAttrib(sb, "ResultAssemblyPath", ResultAssemblyPath);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[] { "ClrInstanceID", "AssemblyName", "HandlerName", "AssemblyLoadContext", "ResultAssemblyName", "ResultAssemblyPath" };
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ClrInstanceID;
                case 1:
                    return AssemblyName;
                case 2:
                    return HandlerName;
                case 3:
                    return AssemblyLoadContext;
                case 4:
                    return ResultAssemblyName;
                case 5:
                    return ResultAssemblyPath;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        public static ulong GetKeywords() { return 4; }
        public static string GetProviderName() { return "Microsoft-Windows-DotNETRuntime"; }
        public static Guid GetProviderGuid() { return new Guid("e13c0d23-ccbc-4e12-931b-d9cc2eee27e4"); }
        private event Action<AssemblyLoadContextResolvingHandlerInvokedTraceData> Action;
        #endregion
    }
    public sealed class AssemblyLoadFromResolveHandlerInvokedTraceData : TraceEvent
    {
        public int ClrInstanceID { get { return GetInt16At(0); } }
        public string AssemblyName { get { return GetUnicodeStringAt(2); } }
        public bool IsTrackedLoad { get { return GetInt32At(SkipUnicodeString(2)) != 0; } }
        public string RequestingAssemblyPath { get { return GetUnicodeStringAt(SkipUnicodeString(2) + 4); } }
        public string ComputedRequestedAssemblyPath { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(2) + 4)); } }

        #region Private
        internal AssemblyLoadFromResolveHandlerInvokedTraceData(Action<AssemblyLoadFromResolveHandlerInvokedTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(2) + 4))));
            Debug.Assert(!(Version > 0 && EventDataLength < SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(2) + 4))));
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<AssemblyLoadFromResolveHandlerInvokedTraceData>)value; }
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            XmlAttrib(sb, "AssemblyName", AssemblyName);
            XmlAttrib(sb, "IsTrackedLoad", IsTrackedLoad);
            XmlAttrib(sb, "RequestingAssemblyPath", RequestingAssemblyPath);
            XmlAttrib(sb, "ComputedRequestedAssemblyPath", ComputedRequestedAssemblyPath);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[] { "ClrInstanceID", "AssemblyName", "IsTrackedLoad", "RequestingAssemblyPath", "ComputedRequestedAssemblyPath" };
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ClrInstanceID;
                case 1:
                    return AssemblyName;
                case 2:
                    return IsTrackedLoad;
                case 3:
                    return RequestingAssemblyPath;
                case 4:
                    return ComputedRequestedAssemblyPath;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        public static ulong GetKeywords() { return 4; }
        public static string GetProviderName() { return "Microsoft-Windows-DotNETRuntime"; }
        public static Guid GetProviderGuid() { return new Guid("e13c0d23-ccbc-4e12-931b-d9cc2eee27e4"); }
        private event Action<AssemblyLoadFromResolveHandlerInvokedTraceData> Action;
        #endregion
    }
    public sealed class KnownPathProbedTraceData : TraceEvent
    {
        public int ClrInstanceID { get { return GetInt16At(0); } }
        public string FilePath { get { return GetUnicodeStringAt(2); } }
        public KnownPathSource PathSource { get { return (KnownPathSource)GetInt16At(SkipUnicodeString(2)); } }
        public int Result { get { return GetInt32At(SkipUnicodeString(2) + 2); } }

        #region Private
        internal KnownPathProbedTraceData(Action<KnownPathProbedTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUnicodeString(2) + 6));
            Debug.Assert(!(Version > 0 && EventDataLength < SkipUnicodeString(2) + 6));
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<KnownPathProbedTraceData>)value; }
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            XmlAttrib(sb, "FilePath", FilePath);
            XmlAttrib(sb, "Source", PathSource);
            XmlAttrib(sb, "Result", Result);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[] { "ClrInstanceID", "FilePath", "Source", "Result" };
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ClrInstanceID;
                case 1:
                    return FilePath;
                case 2:
                    return PathSource;
                case 3:
                    return Result;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        public static ulong GetKeywords() { return 4; }
        public static string GetProviderName() { return "Microsoft-Windows-DotNETRuntime"; }
        public static Guid GetProviderGuid() { return new Guid("e13c0d23-ccbc-4e12-931b-d9cc2eee27e4"); }
        private event Action<KnownPathProbedTraceData> Action;
        #endregion
    }
    public sealed class ResolutionAttemptedTraceData : TraceEvent
    {
        public int ClrInstanceID { get { return GetInt16At(0); } }
        public string AssemblyName { get { return GetUnicodeStringAt(2); } }
        public ResolutionAttemptedStage Stage { get { return (ResolutionAttemptedStage)GetInt16At(SkipUnicodeString(2)); } }
        public string AssemblyLoadContext { get { return GetUnicodeStringAt(SkipUnicodeString(2) + 2); } }
        public ResolutionAttemptedResult Result { get { return (ResolutionAttemptedResult)GetInt16At(SkipUnicodeString(SkipUnicodeString(2) + 2)); } }
        public string ResultAssemblyName { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(2) + 2) + 2); } }
        public string ResultAssemblyPath { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(2) + 2) + 2)); } }
        public string ErrorMessage { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(2) + 2) + 2))); } }

        #region Private
        internal ResolutionAttemptedTraceData(Action<ResolutionAttemptedTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(2) + 2) + 2)))));
            Debug.Assert(!(Version > 0 && EventDataLength < SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(2) + 2) + 2)))));
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ResolutionAttemptedTraceData>)value; }
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            XmlAttrib(sb, "AssemblyName", AssemblyName);
            XmlAttrib(sb, "Stage", Stage);
            XmlAttrib(sb, "AssemblyLoadContext", AssemblyLoadContext);
            XmlAttrib(sb, "Result", Result);
            XmlAttrib(sb, "ResultAssemblyName", ResultAssemblyName);
            XmlAttrib(sb, "ResultAssemblyPath", ResultAssemblyPath);
            XmlAttrib(sb, "ErrorMessage", ErrorMessage);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[] { "ClrInstanceID", "AssemblyName", "Stage", "AssemblyLoadContext", "Result", "ResultAssemblyName", "ResultAssemblyPath", "ErrorMessage" };
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ClrInstanceID;
                case 1:
                    return AssemblyName;
                case 2:
                    return Stage;
                case 3:
                    return AssemblyLoadContext;
                case 4:
                    return Result;
                case 5:
                    return ResultAssemblyName;
                case 6:
                    return ResultAssemblyPath;
                case 7:
                    return ErrorMessage;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        public static ulong GetKeywords() { return 4; }
        public static string GetProviderName() { return "Microsoft-Windows-DotNETRuntime"; }
        public static Guid GetProviderGuid() { return new Guid("e13c0d23-ccbc-4e12-931b-d9cc2eee27e4"); }
        private event Action<ResolutionAttemptedTraceData> Action;
        #endregion
    }
    public sealed class AssemblyLoadStartTraceData : TraceEvent
    {
        public int ClrInstanceID { get { return GetInt16At(0); } }
        public string AssemblyName { get { return GetUnicodeStringAt(2); } }
        public string AssemblyPath { get { return GetUnicodeStringAt(SkipUnicodeString(2)); } }
        public string RequestingAssembly { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(2))); } }
        public string AssemblyLoadContext { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(2)))); } }
        public string RequestingAssemblyLoadContext { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(2))))); } }

        #region Private
        internal AssemblyLoadStartTraceData(Action<AssemblyLoadStartTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(2)))))));
            Debug.Assert(!(Version > 0 && EventDataLength < SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(2)))))));
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<AssemblyLoadStartTraceData>)value; }
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            XmlAttrib(sb, "AssemblyName", AssemblyName);
            XmlAttrib(sb, "AssemblyPath", AssemblyPath);
            XmlAttrib(sb, "RequestingAssembly", RequestingAssembly);
            XmlAttrib(sb, "AssemblyLoadContext", AssemblyLoadContext);
            XmlAttrib(sb, "RequestingAssemblyLoadContext", RequestingAssemblyLoadContext);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[] { "ClrInstanceID", "AssemblyName", "AssemblyPath", "RequestingAssembly", "AssemblyLoadContext", "RequestingAssemblyLoadContext" };
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ClrInstanceID;
                case 1:
                    return AssemblyName;
                case 2:
                    return AssemblyPath;
                case 3:
                    return RequestingAssembly;
                case 4:
                    return AssemblyLoadContext;
                case 5:
                    return RequestingAssemblyLoadContext;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        public static ulong GetKeywords() { return 4; }
        public static string GetProviderName() { return "Microsoft-Windows-DotNETRuntime"; }
        public static Guid GetProviderGuid() { return new Guid("e13c0d23-ccbc-4e12-931b-d9cc2eee27e4"); }
        private event Action<AssemblyLoadStartTraceData> Action;
        #endregion
    }
    public sealed class AssemblyLoadStopTraceData : TraceEvent
    {
        public int ClrInstanceID { get { return GetInt16At(0); } }
        public string AssemblyName { get { return GetUnicodeStringAt(2); } }
        public string AssemblyPath { get { return GetUnicodeStringAt(SkipUnicodeString(2)); } }
        public string RequestingAssembly { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(2))); } }
        public string AssemblyLoadContext { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(2)))); } }
        public string RequestingAssemblyLoadContext { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(2))))); } }
        public bool Success { get { return GetInt32At(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(2)))))) != 0; } }
        public string ResultAssemblyName { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(2))))) + 4); } }
        public string ResultAssemblyPath { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(2))))) + 4)); } }
        public bool Cached { get { return GetInt32At(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(2))))) + 4))) != 0; } }

        #region Private
        internal AssemblyLoadStopTraceData(Action<AssemblyLoadStopTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(2))))) + 4)) + 4));
            Debug.Assert(!(Version > 0 && EventDataLength < SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(2))))) + 4)) + 4));
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<AssemblyLoadStopTraceData>)value; }
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            XmlAttrib(sb, "AssemblyName", AssemblyName);
            XmlAttrib(sb, "AssemblyPath", AssemblyPath);
            XmlAttrib(sb, "RequestingAssembly", RequestingAssembly);
            XmlAttrib(sb, "AssemblyLoadContext", AssemblyLoadContext);
            XmlAttrib(sb, "RequestingAssemblyLoadContext", RequestingAssemblyLoadContext);
            XmlAttrib(sb, "Success", Success);
            XmlAttrib(sb, "ResultAssemblyName", ResultAssemblyName);
            XmlAttrib(sb, "ResultAssemblyPath", ResultAssemblyPath);
            XmlAttrib(sb, "Cached", Cached);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                    payloadNames = new string[] { "ClrInstanceID", "AssemblyName", "AssemblyPath", "RequestingAssembly", "AssemblyLoadContext", "RequestingAssemblyLoadContext", "Success", "ResultAssemblyName", "ResultAssemblyPath", "Cached" };
                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ClrInstanceID;
                case 1:
                    return AssemblyName;
                case 2:
                    return AssemblyPath;
                case 3:
                    return RequestingAssembly;
                case 4:
                    return AssemblyLoadContext;
                case 5:
                    return RequestingAssemblyLoadContext;
                case 6:
                    return Success;
                case 7:
                    return ResultAssemblyName;
                case 8:
                    return ResultAssemblyPath;
                case 9:
                    return Cached;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        public static ulong GetKeywords() { return 4; }
        public static string GetProviderName() { return "Microsoft-Windows-DotNETRuntime"; }
        public static Guid GetProviderGuid() { return new Guid("e13c0d23-ccbc-4e12-931b-d9cc2eee27e4"); }
        private event Action<AssemblyLoadStopTraceData> Action;
        #endregion
    }
    public sealed class ILStubGeneratedTraceData : TraceEvent
    {
        public int ClrInstanceID { get { return GetInt16At(0); } }
        public long ModuleID { get { return GetInt64At(2); } }
        public long StubMethodID { get { return GetInt64At(10); } }
        public ILStubGeneratedFlags StubFlags { get { return (ILStubGeneratedFlags)GetInt32At(18); } }
        public int ManagedInteropMethodToken { get { return GetInt32At(22); } }
        public string ManagedInteropMethodNamespace { get { return GetUnicodeStringAt(26); } }
        public string ManagedInteropMethodName { get { return GetUnicodeStringAt(SkipUnicodeString(26)); } }
        public string ManagedInteropMethodSignature { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(26))); } }
        public string NativeMethodSignature { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(26)))); } }
        public string StubMethodSignature { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(26))))); } }
        public string StubMethodILCode { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(26)))))); } }

        #region Private
        internal ILStubGeneratedTraceData(Action<ILStubGeneratedTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ILStubGeneratedTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(26))))))));
            Debug.Assert(!(Version > 0 && EventDataLength < SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(26))))))));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            XmlAttribHex(sb, "ModuleID", ModuleID);
            XmlAttribHex(sb, "StubMethodID", StubMethodID);
            XmlAttrib(sb, "StubFlags", StubFlags);
            XmlAttribHex(sb, "ManagedInteropMethodToken", ManagedInteropMethodToken);
            XmlAttrib(sb, "ManagedInteropMethodNamespace", ManagedInteropMethodNamespace);
            XmlAttrib(sb, "ManagedInteropMethodName", ManagedInteropMethodName);
            XmlAttrib(sb, "ManagedInteropMethodSignature", ManagedInteropMethodSignature);
            XmlAttrib(sb, "NativeMethodSignature", NativeMethodSignature);
            XmlAttrib(sb, "StubMethodSignature", StubMethodSignature);
            XmlAttrib(sb, "StubMethodILCode", StubMethodILCode);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "ClrInstanceID", "ModuleID", "StubMethodID", "StubFlags", "ManagedInteropMethodToken", "ManagedInteropMethodNamespace", "ManagedInteropMethodName", "ManagedInteropMethodSignature", "NativeMethodSignature", "StubMethodSignature", "StubMethodILCode" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ClrInstanceID;
                case 1:
                    return ModuleID;
                case 2:
                    return StubMethodID;
                case 3:
                    return StubFlags;
                case 4:
                    return ManagedInteropMethodToken;
                case 5:
                    return ManagedInteropMethodNamespace;
                case 6:
                    return ManagedInteropMethodName;
                case 7:
                    return ManagedInteropMethodSignature;
                case 8:
                    return NativeMethodSignature;
                case 9:
                    return StubMethodSignature;
                case 10:
                    return StubMethodILCode;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ILStubGeneratedTraceData> Action;
        #endregion
    }
    public sealed class ILStubCacheHitTraceData : TraceEvent
    {
        public int ClrInstanceID { get { return GetInt16At(0); } }
        public long ModuleID { get { return GetInt64At(2); } }
        public long StubMethodID { get { return GetInt64At(10); } }
        public int ManagedInteropMethodToken { get { return GetInt32At(18); } }
        public string ManagedInteropMethodNamespace { get { return GetUnicodeStringAt(22); } }
        public string ManagedInteropMethodName { get { return GetUnicodeStringAt(SkipUnicodeString(22)); } }
        public string ManagedInteropMethodSignature { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(22))); } }

        #region Private
        internal ILStubCacheHitTraceData(Action<ILStubCacheHitTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ILStubCacheHitTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(22)))));
            Debug.Assert(!(Version > 0 && EventDataLength < SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(22)))));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            XmlAttribHex(sb, "ModuleID", ModuleID);
            XmlAttribHex(sb, "StubMethodID", StubMethodID);
            XmlAttribHex(sb, "ManagedInteropMethodToken", ManagedInteropMethodToken);
            XmlAttrib(sb, "ManagedInteropMethodNamespace", ManagedInteropMethodNamespace);
            XmlAttrib(sb, "ManagedInteropMethodName", ManagedInteropMethodName);
            XmlAttrib(sb, "ManagedInteropMethodSignature", ManagedInteropMethodSignature);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "ClrInstanceID", "ModuleID", "StubMethodID", "ManagedInteropMethodToken", "ManagedInteropMethodNamespace", "ManagedInteropMethodName", "ManagedInteropMethodSignature" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ClrInstanceID;
                case 1:
                    return ModuleID;
                case 2:
                    return StubMethodID;
                case 3:
                    return ManagedInteropMethodToken;
                case 4:
                    return ManagedInteropMethodNamespace;
                case 5:
                    return ManagedInteropMethodName;
                case 6:
                    return ManagedInteropMethodSignature;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ILStubCacheHitTraceData> Action;
        #endregion
    }

    public abstract class MethodLoadUnloadTraceDataBase : TraceEvent
    {
        public long MethodID { get { return GetInt64At(0); } }
        public long ModuleID { get { return GetInt64At(8); } }
        public Address MethodStartAddress { get { return (Address)GetInt64At(16); } }
        public int MethodSize { get { return GetInt32At(24); } }
        public int MethodToken { get { return GetInt32At(28); } }
        public MethodFlags MethodFlags { get { return (MethodFlags)((uint)GetInt32At(32) & MethodFlagsMask); } }
        public bool IsDynamic { get { return (MethodFlags & MethodFlags.Dynamic) != 0; } }
        public bool IsGeneric { get { return (MethodFlags & MethodFlags.Generic) != 0; } }
        public bool IsJitted { get { return (MethodFlags & MethodFlags.Jitted) != 0; } }

        public OptimizationTier OptimizationTier
        {
            get
            {
                var methodFlags = (MethodFlags)GetInt32At(32);
                if ((methodFlags & MethodFlags.Jitted) == MethodFlags.None)
                {
                    // .NET Framework running on v2.0 runtimes may send this event for NGen'ed methods. The method is most
                    // likely optimized, but we'll treat it similarly to an older runtime.
                    return OptimizationTier.Unknown;
                }

                // A runtime that supports the optimization tier would not report an unknown optimization tier. An Unknown value
                // indicates an older runtime.
                return (OptimizationTier)(((uint)methodFlags >> OptimizationTierShift) & OptimizationTierLowMask);
            }
        }

        public int MethodExtent { get { return (int)((uint)GetInt32At(32) >> MethodExtentShift); } }

        #region Private
        internal MethodLoadUnloadTraceDataBase(int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
        }

        private const byte OptimizationTierShift = 7;
        private const uint OptimizationTierLowMask = 0x7;
        private const byte MethodExtentShift = 28;
        private const uint MethodExtentLowMask = 0xf;

        private const uint OptimizationTierMask = OptimizationTierLowMask << OptimizationTierShift;
        private const uint MethodExtentMask = MethodExtentLowMask << MethodExtentShift;

        private const uint MethodFlagsMask = ~0u ^ (OptimizationTierMask | MethodExtentMask);
        #endregion
    }

    public sealed class MethodLoadUnloadTraceData : MethodLoadUnloadTraceDataBase
    {
        public int ClrInstanceID { get { if (Version >= 1) { return GetInt16At(36); } return 0; } }

        #region Private
        internal MethodLoadUnloadTraceData(Action<MethodLoadUnloadTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<MethodLoadUnloadTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != 36));
            Debug.Assert(!(Version == 1 && EventDataLength != 38));
            Debug.Assert(!(Version > 1 && EventDataLength < 38));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "MethodID", MethodID);
            XmlAttribHex(sb, "ModuleID", ModuleID);
            XmlAttribHex(sb, "MethodStartAddress", MethodStartAddress);
            XmlAttribHex(sb, "MethodSize", MethodSize);
            XmlAttribHex(sb, "MethodToken", MethodToken);
            XmlAttrib(sb, "MethodFlags", MethodFlags);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            XmlAttrib(sb, "OptimizationTier", OptimizationTier);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "MethodID", "ModuleID", "MethodStartAddress", "MethodSize", "MethodToken", "MethodFlags", "ClrInstanceID", "OptimizationTier" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return MethodID;
                case 1:
                    return ModuleID;
                case 2:
                    return MethodStartAddress;
                case 3:
                    return MethodSize;
                case 4:
                    return MethodToken;
                case 5:
                    return MethodFlags;
                case 6:
                    return ClrInstanceID;
                case 7:
                    return OptimizationTier;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<MethodLoadUnloadTraceData> Action;
        #endregion
    }
    public sealed class MethodLoadUnloadVerboseTraceData : MethodLoadUnloadTraceDataBase
    {
        public string MethodNamespace { get { return GetUnicodeStringAt(36); } }
        public string MethodName { get { return GetUnicodeStringAt(SkipUnicodeString(36)); } }
        public string MethodSignature { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(36))); } }
        public int ClrInstanceID { get { if (Version >= 1) { return GetInt16At(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(36)))); } return 0; } }
        public long ReJITID { get { if (Version >= 2) { return GetInt64At(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(36))) + 2); } return 0; } }

        #region Private
        internal MethodLoadUnloadVerboseTraceData(Action<MethodLoadUnloadVerboseTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<MethodLoadUnloadVerboseTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(36)))));
            Debug.Assert(!(Version == 1 && EventDataLength != SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(36))) + 2));
            Debug.Assert(!(Version == 2 && EventDataLength != SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(36))) + 10));
            Debug.Assert(!(Version > 2 && EventDataLength < SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(36))) + 10));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "MethodID", MethodID);
            XmlAttribHex(sb, "ModuleID", ModuleID);
            XmlAttribHex(sb, "MethodStartAddress", MethodStartAddress);
            XmlAttribHex(sb, "MethodSize", MethodSize);
            XmlAttribHex(sb, "MethodToken", MethodToken);
            XmlAttrib(sb, "MethodFlags", MethodFlags);
            XmlAttrib(sb, "MethodNamespace", MethodNamespace);
            XmlAttrib(sb, "MethodName", MethodName);
            XmlAttrib(sb, "MethodSignature", MethodSignature);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            XmlAttribHex(sb, "ReJITID", ReJITID);
            XmlAttrib(sb, "OptimizationTier", OptimizationTier);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "MethodID", "ModuleID", "MethodStartAddress", "MethodSize", "MethodToken", "MethodFlags", "MethodNamespace", "MethodName", "MethodSignature", "ClrInstanceID", "ReJITID", "OptimizationTier" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return MethodID;
                case 1:
                    return ModuleID;
                case 2:
                    return MethodStartAddress;
                case 3:
                    return MethodSize;
                case 4:
                    return MethodToken;
                case 5:
                    return MethodFlags;
                case 6:
                    return MethodNamespace;
                case 7:
                    return MethodName;
                case 8:
                    return MethodSignature;
                case 9:
                    return ClrInstanceID;
                case 10:
                    return ReJITID;
                case 11:
                    return OptimizationTier;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<MethodLoadUnloadVerboseTraceData> Action;
        #endregion
    }

    public sealed class MethodJittingStartedTraceData : TraceEvent
    {
        public long MethodID { get { return GetInt64At(0); } }
        public long ModuleID { get { return GetInt64At(8); } }
        public int MethodToken { get { return GetInt32At(16); } }
        public int MethodILSize { get { return GetInt32At(20); } }
        public string MethodNamespace { get { return GetUnicodeStringAt(24); } }
        public string MethodName { get { return GetUnicodeStringAt(SkipUnicodeString(24)); } }
        public string MethodSignature { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(24))); } }
        public int ClrInstanceID { get { if (Version >= 1) { return GetInt16At(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(24)))); } return 0; } }

        #region Private
        internal MethodJittingStartedTraceData(Action<MethodJittingStartedTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<MethodJittingStartedTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(24)))));
            Debug.Assert(!(Version == 1 && EventDataLength != SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(24))) + 2));
            Debug.Assert(!(Version > 1 && EventDataLength < SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(24))) + 2));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "MethodID", MethodID);
            XmlAttribHex(sb, "ModuleID", ModuleID);
            XmlAttribHex(sb, "MethodToken", MethodToken);
            XmlAttribHex(sb, "MethodILSize", MethodILSize);
            XmlAttrib(sb, "MethodNamespace", MethodNamespace);
            XmlAttrib(sb, "MethodName", MethodName);
            XmlAttrib(sb, "MethodSignature", MethodSignature);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "MethodID", "ModuleID", "MethodToken", "MethodILSize", "MethodNamespace", "MethodName", "MethodSignature", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return MethodID;
                case 1:
                    return ModuleID;
                case 2:
                    return MethodToken;
                case 3:
                    return MethodILSize;
                case 4:
                    return MethodNamespace;
                case 5:
                    return MethodName;
                case 6:
                    return MethodSignature;
                case 7:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<MethodJittingStartedTraceData> Action;
        #endregion
    }
    public sealed class ModuleLoadUnloadTraceData : TraceEvent
    {
        public long ModuleID { get { return GetInt64At(0); } }
        public long AssemblyID { get { return GetInt64At(8); } }
        public ModuleFlags ModuleFlags { get { return (ModuleFlags)GetInt32At(16); } }
        // Skipping Reserved1
        public string ModuleILPath { get { return GetUnicodeStringAt(24); } }
        public string ModuleNativePath { get { return GetUnicodeStringAt(SkipUnicodeString(24)); } }
        public int ClrInstanceID { get { if (Version >= 1) { return GetInt16At(SkipUnicodeString(SkipUnicodeString(24))); } return 0; } }
        public Guid ManagedPdbSignature { get { if (Version >= 2) { return GetGuidAt(SkipUnicodeString(SkipUnicodeString(24)) + 2); } return Guid.Empty; } }
        public int ManagedPdbAge { get { if (Version >= 2) { return GetInt32At(SkipUnicodeString(SkipUnicodeString(24)) + 18); } return 0; } }
        public string ManagedPdbBuildPath { get { if (Version >= 2) { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(24)) + 22); } return ""; } }

        public Guid NativePdbSignature { get { if (Version >= 2) { return GetGuidAt(GetNativePdbSigStart); } return Guid.Empty; } }
        public int NativePdbAge { get { if (Version >= 2) { return GetInt32At(GetNativePdbSigStart + 16); } return 0; } }
        public string NativePdbBuildPath { get { if (Version >= 2) { return GetUnicodeStringAt(GetNativePdbSigStart + 20); } return ""; } }

        /// <summary>
        /// This is simply the file name part of the ModuleILPath.  It is a convenience method. 
        /// </summary>
        public string ModuleILFileName { get { return System.IO.Path.GetFileName(ModuleILPath); } }
        #region Private
        internal ModuleLoadUnloadTraceData(Action<ModuleLoadUnloadTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }

        private int GetNativePdbSigStart { get { return SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(24)) + 22); } }

        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<ModuleLoadUnloadTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUnicodeString(SkipUnicodeString(24))));
            Debug.Assert(!(Version == 1 && EventDataLength != SkipUnicodeString(SkipUnicodeString(24)) + 2));
            Debug.Assert(!(Version == 2 && EventDataLength != SkipUnicodeString(GetNativePdbSigStart + 20)));
            Debug.Assert(!(Version > 2 && EventDataLength < SkipUnicodeString(GetNativePdbSigStart + 20)));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "ModuleID", ModuleID);
            XmlAttribHex(sb, "AssemblyID", AssemblyID);
            XmlAttrib(sb, "ModuleFlags", ModuleFlags);
            XmlAttrib(sb, "ModuleILPath", ModuleILPath);
            XmlAttrib(sb, "ModuleNativePath", ModuleNativePath);
            if (ManagedPdbSignature != Guid.Empty)
            {
                XmlAttrib(sb, "ManagedPdbSignature", ManagedPdbSignature);
            }

            if (ManagedPdbAge != 0)
            {
                XmlAttrib(sb, "ManagedPdbAge", ManagedPdbAge);
            }

            if (ManagedPdbBuildPath.Length != 0)
            {
                XmlAttrib(sb, "ManagedPdbBuildPath", ManagedPdbBuildPath);
            }

            if (NativePdbSignature != Guid.Empty)
            {
                XmlAttrib(sb, "NativePdbSignature", NativePdbSignature);
            }

            if (NativePdbAge != 0)
            {
                XmlAttrib(sb, "NativePdbAge", NativePdbAge);
            }

            if (NativePdbBuildPath.Length != 0)
            {
                XmlAttrib(sb, "NativePdbBuildPath", NativePdbBuildPath);
            }

            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "ModuleID", "AssemblyID", "ModuleFlags", "ModuleILPath", "ModuleNativePath",
                        "ManagedPdbSignature", "ManagedPdbAge", "ManagedPdbBuildPath",
                        "NativePdbSignature", "NativePdbAge", "NativePdbBuildPath", "ModuleILFileName" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ModuleID;
                case 1:
                    return AssemblyID;
                case 2:
                    return ModuleFlags;
                case 3:
                    return ModuleILPath;
                case 4:
                    return ModuleNativePath;
                case 5:
                    return ManagedPdbSignature;
                case 6:
                    return ManagedPdbAge;
                case 7:
                    return ManagedPdbBuildPath;
                case 8:
                    return NativePdbSignature;
                case 9:
                    return NativePdbAge;
                case 10:
                    return NativePdbBuildPath;
                case 11:
                    return ModuleILFileName;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<ModuleLoadUnloadTraceData> Action;
        #endregion
    }
    public sealed class DomainModuleLoadUnloadTraceData : TraceEvent
    {
        public long ModuleID { get { return GetInt64At(0); } }
        public long AssemblyID { get { return GetInt64At(8); } }
        public long AppDomainID { get { return GetInt64At(16); } }
        public ModuleFlags ModuleFlags { get { return (ModuleFlags)GetInt32At(24); } }
        // Skipping Reserved1
        public string ModuleILPath { get { return GetUnicodeStringAt(32); } }
        public string ModuleNativePath { get { return GetUnicodeStringAt(SkipUnicodeString(32)); } }
        public int ClrInstanceID { get { if (Version >= 1) { return GetInt16At(SkipUnicodeString(SkipUnicodeString(32))); } return 0; } }

        #region Private
        internal DomainModuleLoadUnloadTraceData(Action<DomainModuleLoadUnloadTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<DomainModuleLoadUnloadTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUnicodeString(SkipUnicodeString(32))));
            Debug.Assert(!(Version == 1 && EventDataLength != SkipUnicodeString(SkipUnicodeString(32)) + 2));
            Debug.Assert(!(Version > 1 && EventDataLength < SkipUnicodeString(SkipUnicodeString(32)) + 2));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "ModuleID", ModuleID);
            XmlAttribHex(sb, "AssemblyID", AssemblyID);
            XmlAttribHex(sb, "AppDomainID", AppDomainID);
            XmlAttrib(sb, "ModuleFlags", ModuleFlags);
            XmlAttrib(sb, "ModuleILPath", ModuleILPath);
            XmlAttrib(sb, "ModuleNativePath", ModuleNativePath);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "ModuleID", "AssemblyID", "AppDomainID", "ModuleFlags", "ModuleILPath", "ModuleNativePath", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ModuleID;
                case 1:
                    return AssemblyID;
                case 2:
                    return AppDomainID;
                case 3:
                    return ModuleFlags;
                case 4:
                    return ModuleILPath;
                case 5:
                    return ModuleNativePath;
                case 6:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<DomainModuleLoadUnloadTraceData> Action;
        #endregion
    }
    public sealed class AssemblyLoadUnloadTraceData : TraceEvent
    {
        public long AssemblyID { get { return GetInt64At(0); } }
        public long AppDomainID { get { return GetInt64At(8); } }
        public AssemblyFlags AssemblyFlags { get { if (Version >= 1) { return (AssemblyFlags)GetInt32At(24); } return (AssemblyFlags)GetInt32At(16); } }
        public string FullyQualifiedAssemblyName { get { if (Version >= 1) { return GetUnicodeStringAt(28); } return GetUnicodeStringAt(20); } }
        public long BindingID { get { if (Version >= 1) { return GetInt64At(16); } return 0; } }
        public int ClrInstanceID { get { if (Version >= 1) { return GetInt16At(SkipUnicodeString(28)); } return 0; } }

        #region Private
        internal AssemblyLoadUnloadTraceData(Action<AssemblyLoadUnloadTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<AssemblyLoadUnloadTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUnicodeString(20)));
            Debug.Assert(!(Version == 1 && EventDataLength != SkipUnicodeString(28) + 2));
            Debug.Assert(!(Version > 1 && EventDataLength < SkipUnicodeString(28) + 2));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "AssemblyID", AssemblyID);
            XmlAttribHex(sb, "AppDomainID", AppDomainID);
            XmlAttrib(sb, "AssemblyFlags", AssemblyFlags);
            XmlAttrib(sb, "FullyQualifiedAssemblyName", FullyQualifiedAssemblyName);
            XmlAttribHex(sb, "BindingID", BindingID);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "AssemblyID", "AppDomainID", "AssemblyFlags", "FullyQualifiedAssemblyName", "BindingID", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return AssemblyID;
                case 1:
                    return AppDomainID;
                case 2:
                    return AssemblyFlags;
                case 3:
                    return FullyQualifiedAssemblyName;
                case 4:
                    return BindingID;
                case 5:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<AssemblyLoadUnloadTraceData> Action;
        #endregion
    }
    public sealed class AppDomainLoadUnloadTraceData : TraceEvent
    {
        public long AppDomainID { get { return GetInt64At(0); } }
        public AppDomainFlags AppDomainFlags { get { return (AppDomainFlags)GetInt32At(8); } }
        public string AppDomainName { get { return GetUnicodeStringAt(12); } }
        public int AppDomainIndex { get { if (Version >= 1) { return GetInt32At(SkipUnicodeString(12)); } return 0; } }
        public int ClrInstanceID { get { if (Version >= 1) { return GetInt16At(SkipUnicodeString(12) + 4); } return 0; } }

        #region Private
        internal AppDomainLoadUnloadTraceData(Action<AppDomainLoadUnloadTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<AppDomainLoadUnloadTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUnicodeString(12)));
            Debug.Assert(!(Version == 1 && EventDataLength != SkipUnicodeString(12) + 6));
            Debug.Assert(!(Version > 1 && EventDataLength < SkipUnicodeString(12) + 6));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "AppDomainID", AppDomainID);
            XmlAttrib(sb, "AppDomainFlags", AppDomainFlags);
            XmlAttrib(sb, "AppDomainName", AppDomainName);
            XmlAttrib(sb, "AppDomainIndex", AppDomainIndex);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "AppDomainID", "AppDomainFlags", "AppDomainName", "AppDomainIndex", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return AppDomainID;
                case 1:
                    return AppDomainFlags;
                case 2:
                    return AppDomainName;
                case 3:
                    return AppDomainIndex;
                case 4:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<AppDomainLoadUnloadTraceData> Action;
        #endregion
    }
    public sealed class EventSourceTraceData : TraceEvent
    {
        public int EventID { get { return GetInt32At(0); } }
        public string Name { get { return GetUnicodeStringAt(4); } }
        public string EventSourceName { get { return GetUnicodeStringAt(SkipUnicodeString(4)); } }
        public string Payload { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(4))); } }

        #region Private
        internal EventSourceTraceData(Action<EventSourceTraceData> target, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            m_target = target;
        }
        protected internal override void Dispatch()
        {
            m_target(this);
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(4)))));
            Debug.Assert(!(Version > 0 && EventDataLength < SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(4)))));
        }
        protected internal override Delegate Target
        {
            get { return m_target; }
            set { m_target = (Action<EventSourceTraceData>)value; }
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "EventID", EventID);
            XmlAttrib(sb, "Name", Name);
            XmlAttrib(sb, "EventSourceName", EventSourceName);
            XmlAttrib(sb, "Payload", Payload);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "EventID", "Name", "EventSourceName", "Payload" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return EventID;
                case 1:
                    return Name;
                case 2:
                    return EventSourceName;
                case 3:
                    return Payload;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<EventSourceTraceData> m_target;
        #endregion
    }

    public sealed class StrongNameVerificationTraceData : TraceEvent
    {
        public int VerificationFlags { get { return GetInt32At(0); } }
        public int ErrorCode { get { return GetInt32At(4); } }
        public string FullyQualifiedAssemblyName { get { return GetUnicodeStringAt(8); } }
        public int ClrInstanceID { get { if (Version >= 1) { return GetInt16At(SkipUnicodeString(8)); } return 0; } }

        #region Private
        internal StrongNameVerificationTraceData(Action<StrongNameVerificationTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<StrongNameVerificationTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUnicodeString(8)));
            Debug.Assert(!(Version == 1 && EventDataLength != SkipUnicodeString(8) + 2));
            Debug.Assert(!(Version > 1 && EventDataLength < SkipUnicodeString(8) + 2));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "VerificationFlags", VerificationFlags);
            XmlAttribHex(sb, "ErrorCode", ErrorCode);
            XmlAttrib(sb, "FullyQualifiedAssemblyName", FullyQualifiedAssemblyName);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "VerificationFlags", "ErrorCode", "FullyQualifiedAssemblyName", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return VerificationFlags;
                case 1:
                    return ErrorCode;
                case 2:
                    return FullyQualifiedAssemblyName;
                case 3:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<StrongNameVerificationTraceData> Action;
        #endregion
    }
    public sealed class AuthenticodeVerificationTraceData : TraceEvent
    {
        public int VerificationFlags { get { return GetInt32At(0); } }
        public int ErrorCode { get { return GetInt32At(4); } }
        public string ModulePath { get { return GetUnicodeStringAt(8); } }
        public int ClrInstanceID { get { if (Version >= 1) { return GetInt16At(SkipUnicodeString(8)); } return 0; } }

        #region Private
        internal AuthenticodeVerificationTraceData(Action<AuthenticodeVerificationTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<AuthenticodeVerificationTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUnicodeString(8)));
            Debug.Assert(!(Version == 1 && EventDataLength != SkipUnicodeString(8) + 2));
            Debug.Assert(!(Version > 1 && EventDataLength < SkipUnicodeString(8) + 2));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "VerificationFlags", VerificationFlags);
            XmlAttribHex(sb, "ErrorCode", ErrorCode);
            XmlAttrib(sb, "ModulePath", ModulePath);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "VerificationFlags", "ErrorCode", "ModulePath", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return VerificationFlags;
                case 1:
                    return ErrorCode;
                case 2:
                    return ModulePath;
                case 3:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<AuthenticodeVerificationTraceData> Action;
        #endregion
    }
    public sealed class MethodJitInliningSucceededTraceData : TraceEvent
    {
        public string MethodBeingCompiledNamespace { get { return GetUnicodeStringAt(0); } }
        public string MethodBeingCompiledName { get { return GetUnicodeStringAt(SkipUnicodeString(0)); } }
        public string MethodBeingCompiledNameSignature { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(0))); } }
        public string InlinerNamespace { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0)))); } }
        public string InlinerName { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))); } }
        public string InlinerNameSignature { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0)))))); } }
        public string InlineeNamespace { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))); } }
        public string InlineeName { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0)))))))); } }
        public string InlineeNameSignature { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))))); } }
        public int ClrInstanceID { get { return GetInt16At(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0)))))))))); } }

        #region Private
        internal MethodJitInliningSucceededTraceData(Action<MethodJitInliningSucceededTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<MethodJitInliningSucceededTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))))) + 2));
            Debug.Assert(!(Version > 0 && EventDataLength < SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))))) + 2));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "MethodBeingCompiledNamespace", MethodBeingCompiledNamespace);
            XmlAttrib(sb, "MethodBeingCompiledName", MethodBeingCompiledName);
            XmlAttrib(sb, "MethodBeingCompiledNameSignature", MethodBeingCompiledNameSignature);
            XmlAttrib(sb, "InlinerNamespace", InlinerNamespace);
            XmlAttrib(sb, "InlinerName", InlinerName);
            XmlAttrib(sb, "InlinerNameSignature", InlinerNameSignature);
            XmlAttrib(sb, "InlineeNamespace", InlineeNamespace);
            XmlAttrib(sb, "InlineeName", InlineeName);
            XmlAttrib(sb, "InlineeNameSignature", InlineeNameSignature);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "MethodBeingCompiledNamespace", "MethodBeingCompiledName", "MethodBeingCompiledNameSignature", "InlinerNamespace", "InlinerName", "InlinerNameSignature", "InlineeNamespace", "InlineeName", "InlineeNameSignature", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return MethodBeingCompiledNamespace;
                case 1:
                    return MethodBeingCompiledName;
                case 2:
                    return MethodBeingCompiledNameSignature;
                case 3:
                    return InlinerNamespace;
                case 4:
                    return InlinerName;
                case 5:
                    return InlinerNameSignature;
                case 6:
                    return InlineeNamespace;
                case 7:
                    return InlineeName;
                case 8:
                    return InlineeNameSignature;
                case 9:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<MethodJitInliningSucceededTraceData> Action;
        #endregion
    }
    public sealed class MethodJitInliningFailedAnsiTraceData : TraceEvent
    {
        public string MethodBeingCompiledNamespace { get { return GetUnicodeStringAt(0); } }
        public string MethodBeingCompiledName { get { return GetUnicodeStringAt(SkipUnicodeString(0)); } }
        public string MethodBeingCompiledNameSignature { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(0))); } }
        public string InlinerNamespace { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0)))); } }
        public string InlinerName { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))); } }
        public string InlinerNameSignature { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0)))))); } }
        public string InlineeNamespace { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))); } }
        public string InlineeName { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0)))))))); } }
        public string InlineeNameSignature { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))))); } }
        public bool FailAlways { get { return GetInt32At(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0)))))))))) != 0; } }
        public string FailReason { get { return GetUTF8StringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))))) + 4); } }
        public int ClrInstanceID { get { return GetInt16At(SkipUTF8String(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))))) + 4)); } }

        #region Private
        internal MethodJitInliningFailedAnsiTraceData(Action<MethodJitInliningFailedAnsiTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<MethodJitInliningFailedAnsiTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUTF8String(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))))) + 4) + 2));
            Debug.Assert(!(Version > 0 && EventDataLength < SkipUTF8String(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))))) + 4) + 2));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "MethodBeingCompiledNamespace", MethodBeingCompiledNamespace);
            XmlAttrib(sb, "MethodBeingCompiledName", MethodBeingCompiledName);
            XmlAttrib(sb, "MethodBeingCompiledNameSignature", MethodBeingCompiledNameSignature);
            XmlAttrib(sb, "InlinerNamespace", InlinerNamespace);
            XmlAttrib(sb, "InlinerName", InlinerName);
            XmlAttrib(sb, "InlinerNameSignature", InlinerNameSignature);
            XmlAttrib(sb, "InlineeNamespace", InlineeNamespace);
            XmlAttrib(sb, "InlineeName", InlineeName);
            XmlAttrib(sb, "InlineeNameSignature", InlineeNameSignature);
            XmlAttrib(sb, "FailAlways", FailAlways);
            XmlAttrib(sb, "FailReason", FailReason);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "MethodBeingCompiledNamespace", "MethodBeingCompiledName", "MethodBeingCompiledNameSignature", "InlinerNamespace", "InlinerName", "InlinerNameSignature", "InlineeNamespace", "InlineeName", "InlineeNameSignature", "FailAlways", "FailReason", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return MethodBeingCompiledNamespace;
                case 1:
                    return MethodBeingCompiledName;
                case 2:
                    return MethodBeingCompiledNameSignature;
                case 3:
                    return InlinerNamespace;
                case 4:
                    return InlinerName;
                case 5:
                    return InlinerNameSignature;
                case 6:
                    return InlineeNamespace;
                case 7:
                    return InlineeName;
                case 8:
                    return InlineeNameSignature;
                case 9:
                    return FailAlways;
                case 10:
                    return FailReason;
                case 11:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<MethodJitInliningFailedAnsiTraceData> Action;
        #endregion
    }

    public sealed class MethodJitInliningFailedTraceData : TraceEvent
    {
        public string MethodBeingCompiledNamespace { get { return GetUnicodeStringAt(0); } }
        public string MethodBeingCompiledName { get { return GetUnicodeStringAt(SkipUnicodeString(0)); } }
        public string MethodBeingCompiledNameSignature { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(0))); } }
        public string InlinerNamespace { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0)))); } }
        public string InlinerName { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))); } }
        public string InlinerNameSignature { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0)))))); } }
        public string InlineeNamespace { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))); } }
        public string InlineeName { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0)))))))); } }
        public string InlineeNameSignature { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))))); } }
        public bool FailAlways { get { return GetInt32At(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0)))))))))) != 0; } }
        public string FailReason { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))))) + 4); } }
        public int ClrInstanceID { get { return GetInt16At(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))))) + 4)); } }

        #region Private
        internal MethodJitInliningFailedTraceData(Action<MethodJitInliningFailedTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<MethodJitInliningFailedTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUTF8String(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))))) + 4) + 2));
            Debug.Assert(!(Version > 0 && EventDataLength < SkipUTF8String(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))))) + 4) + 2));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "MethodBeingCompiledNamespace", MethodBeingCompiledNamespace);
            XmlAttrib(sb, "MethodBeingCompiledName", MethodBeingCompiledName);
            XmlAttrib(sb, "MethodBeingCompiledNameSignature", MethodBeingCompiledNameSignature);
            XmlAttrib(sb, "InlinerNamespace", InlinerNamespace);
            XmlAttrib(sb, "InlinerName", InlinerName);
            XmlAttrib(sb, "InlinerNameSignature", InlinerNameSignature);
            XmlAttrib(sb, "InlineeNamespace", InlineeNamespace);
            XmlAttrib(sb, "InlineeName", InlineeName);
            XmlAttrib(sb, "InlineeNameSignature", InlineeNameSignature);
            XmlAttrib(sb, "FailAlways", FailAlways);
            XmlAttrib(sb, "FailReason", FailReason);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "MethodBeingCompiledNamespace", "MethodBeingCompiledName", "MethodBeingCompiledNameSignature", "InlinerNamespace", "InlinerName", "InlinerNameSignature", "InlineeNamespace", "InlineeName", "InlineeNameSignature", "FailAlways", "FailReason", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return MethodBeingCompiledNamespace;
                case 1:
                    return MethodBeingCompiledName;
                case 2:
                    return MethodBeingCompiledNameSignature;
                case 3:
                    return InlinerNamespace;
                case 4:
                    return InlinerName;
                case 5:
                    return InlinerNameSignature;
                case 6:
                    return InlineeNamespace;
                case 7:
                    return InlineeName;
                case 8:
                    return InlineeNameSignature;
                case 9:
                    return FailAlways;
                case 10:
                    return FailReason;
                case 11:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<MethodJitInliningFailedTraceData> Action;
        #endregion
    }

    public sealed class RuntimeInformationTraceData : TraceEvent
    {
        public int ClrInstanceID { get { return GetInt16At(0); } }
        public RuntimeSku Sku { get { return (RuntimeSku)GetInt16At(2); } }
        public int BclMajorVersion { get { return (ushort)GetInt16At(4); } }
        public int BclMinorVersion { get { return (ushort)GetInt16At(6); } }
        public int BclBuildNumber { get { return (ushort)GetInt16At(8); } }
        public int BclQfeNumber { get { return (ushort)GetInt16At(10); } }
        public int VMMajorVersion { get { return (ushort)GetInt16At(12); } }
        public int VMMinorVersion { get { return (ushort)GetInt16At(14); } }
        public int VMBuildNumber { get { return (ushort)GetInt16At(16); } }
        public int VMQfeNumber { get { return (ushort)GetInt16At(18); } }
        public StartupFlags StartupFlags { get { return (StartupFlags)GetInt32At(20); } }
        public StartupMode StartupMode { get { return (StartupMode)GetByteAt(24); } }
        public string CommandLine { get { return GetUnicodeStringAt(25); } }
        public Guid ComObjectGuid { get { return GetGuidAt(SkipUnicodeString(25)); } }
        public string RuntimeDllPath { get { return GetUnicodeStringAt(SkipUnicodeString(25) + 16); } }

        #region Private
        internal RuntimeInformationTraceData(Action<RuntimeInformationTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<RuntimeInformationTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUnicodeString(SkipUnicodeString(25) + 16)));
            Debug.Assert(!(Version > 0 && EventDataLength < SkipUnicodeString(SkipUnicodeString(25) + 16)));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            XmlAttrib(sb, "Sku", Sku);
            XmlAttrib(sb, "BclMajorVersion", BclMajorVersion);
            XmlAttrib(sb, "BclMinorVersion", BclMinorVersion);
            XmlAttrib(sb, "BclBuildNumber", BclBuildNumber);
            XmlAttrib(sb, "BclQfeNumber", BclQfeNumber);
            XmlAttrib(sb, "VMMajorVersion", VMMajorVersion);
            XmlAttrib(sb, "VMMinorVersion", VMMinorVersion);
            XmlAttrib(sb, "VMBuildNumber", VMBuildNumber);
            XmlAttrib(sb, "VMQfeNumber", VMQfeNumber);
            XmlAttrib(sb, "StartupFlags", StartupFlags);
            XmlAttrib(sb, "StartupMode", StartupMode);
            XmlAttrib(sb, "CommandLine", CommandLine);
            XmlAttrib(sb, "ComObjectGuid", ComObjectGuid);
            XmlAttrib(sb, "RuntimeDllPath", RuntimeDllPath);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "ClrInstanceID", "Sku", "BclMajorVersion", "BclMinorVersion", "BclBuildNumber", "BclQfeNumber", "VMMajorVersion", "VMMinorVersion", "VMBuildNumber", "VMQfeNumber", "StartupFlags", "StartupMode", "CommandLine", "ComObjectGuid", "RuntimeDllPath" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ClrInstanceID;
                case 1:
                    return Sku;
                case 2:
                    return BclMajorVersion;
                case 3:
                    return BclMinorVersion;
                case 4:
                    return BclBuildNumber;
                case 5:
                    return BclQfeNumber;
                case 6:
                    return VMMajorVersion;
                case 7:
                    return VMMinorVersion;
                case 8:
                    return VMBuildNumber;
                case 9:
                    return VMQfeNumber;
                case 10:
                    return StartupFlags;
                case 11:
                    return StartupMode;
                case 12:
                    return CommandLine;
                case 13:
                    return ComObjectGuid;
                case 14:
                    return RuntimeDllPath;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<RuntimeInformationTraceData> Action;
        #endregion
    }
    public sealed class MethodJitTailCallSucceededTraceData : TraceEvent
    {
        public string MethodBeingCompiledNamespace { get { return GetUnicodeStringAt(0); } }
        public string MethodBeingCompiledName { get { return GetUnicodeStringAt(SkipUnicodeString(0)); } }
        public string MethodBeingCompiledNameSignature { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(0))); } }
        public string CallerNamespace { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0)))); } }
        public string CallerName { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))); } }
        public string CallerNameSignature { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0)))))); } }
        public string CalleeNamespace { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))); } }
        public string CalleeName { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0)))))))); } }
        public string CalleeNameSignature { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))))); } }
        public bool TailPrefix { get { return GetInt32At(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0)))))))))) != 0; } }
        public TailCallType TailCallType { get { return (TailCallType)GetInt32At(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))))) + 4); } }
        public int ClrInstanceID { get { return GetInt16At(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))))) + 8); } }

        #region Private
        internal MethodJitTailCallSucceededTraceData(Action<MethodJitTailCallSucceededTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<MethodJitTailCallSucceededTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))))) + 10));
            Debug.Assert(!(Version > 0 && EventDataLength < SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))))) + 10));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "MethodBeingCompiledNamespace", MethodBeingCompiledNamespace);
            XmlAttrib(sb, "MethodBeingCompiledName", MethodBeingCompiledName);
            XmlAttrib(sb, "MethodBeingCompiledNameSignature", MethodBeingCompiledNameSignature);
            XmlAttrib(sb, "CallerNamespace", CallerNamespace);
            XmlAttrib(sb, "CallerName", CallerName);
            XmlAttrib(sb, "CallerNameSignature", CallerNameSignature);
            XmlAttrib(sb, "CalleeNamespace", CalleeNamespace);
            XmlAttrib(sb, "CalleeName", CalleeName);
            XmlAttrib(sb, "CalleeNameSignature", CalleeNameSignature);
            XmlAttrib(sb, "TailPrefix", TailPrefix);
            XmlAttrib(sb, "TailCallType", TailCallType);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "MethodBeingCompiledNamespace", "MethodBeingCompiledName", "MethodBeingCompiledNameSignature", "CallerNamespace", "CallerName", "CallerNameSignature", "CalleeNamespace", "CalleeName", "CalleeNameSignature", "TailPrefix", "TailCallType", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return MethodBeingCompiledNamespace;
                case 1:
                    return MethodBeingCompiledName;
                case 2:
                    return MethodBeingCompiledNameSignature;
                case 3:
                    return CallerNamespace;
                case 4:
                    return CallerName;
                case 5:
                    return CallerNameSignature;
                case 6:
                    return CalleeNamespace;
                case 7:
                    return CalleeName;
                case 8:
                    return CalleeNameSignature;
                case 9:
                    return TailPrefix;
                case 10:
                    return TailCallType;
                case 11:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<MethodJitTailCallSucceededTraceData> Action;
        #endregion
    }

    public sealed class MethodJitTailCallFailedAnsiTraceData : TraceEvent
    {
        public string MethodBeingCompiledNamespace { get { return GetUnicodeStringAt(0); } }
        public string MethodBeingCompiledName { get { return GetUnicodeStringAt(SkipUnicodeString(0)); } }
        public string MethodBeingCompiledNameSignature { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(0))); } }
        public string CallerNamespace { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0)))); } }
        public string CallerName { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))); } }
        public string CallerNameSignature { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0)))))); } }
        public string CalleeNamespace { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))); } }
        public string CalleeName { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0)))))))); } }
        public string CalleeNameSignature { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))))); } }
        public bool TailPrefix { get { return GetInt32At(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0)))))))))) != 0; } }
        public string FailReason { get { return GetUTF8StringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))))) + 4); } }
        public int ClrInstanceID { get { return GetInt16At(SkipUTF8String(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))))) + 4)); } }

        #region Private
        internal MethodJitTailCallFailedAnsiTraceData(Action<MethodJitTailCallFailedAnsiTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<MethodJitTailCallFailedAnsiTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUTF8String(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))))) + 4) + 2));
            Debug.Assert(!(Version > 0 && EventDataLength < SkipUTF8String(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))))) + 4) + 2));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "MethodBeingCompiledNamespace", MethodBeingCompiledNamespace);
            XmlAttrib(sb, "MethodBeingCompiledName", MethodBeingCompiledName);
            XmlAttrib(sb, "MethodBeingCompiledNameSignature", MethodBeingCompiledNameSignature);
            XmlAttrib(sb, "CallerNamespace", CallerNamespace);
            XmlAttrib(sb, "CallerName", CallerName);
            XmlAttrib(sb, "CallerNameSignature", CallerNameSignature);
            XmlAttrib(sb, "CalleeNamespace", CalleeNamespace);
            XmlAttrib(sb, "CalleeName", CalleeName);
            XmlAttrib(sb, "CalleeNameSignature", CalleeNameSignature);
            XmlAttrib(sb, "TailPrefix", TailPrefix);
            XmlAttrib(sb, "FailReason", FailReason);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "MethodBeingCompiledNamespace", "MethodBeingCompiledName", "MethodBeingCompiledNameSignature", "CallerNamespace", "CallerName", "CallerNameSignature", "CalleeNamespace", "CalleeName", "CalleeNameSignature", "TailPrefix", "FailReason", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return MethodBeingCompiledNamespace;
                case 1:
                    return MethodBeingCompiledName;
                case 2:
                    return MethodBeingCompiledNameSignature;
                case 3:
                    return CallerNamespace;
                case 4:
                    return CallerName;
                case 5:
                    return CallerNameSignature;
                case 6:
                    return CalleeNamespace;
                case 7:
                    return CalleeName;
                case 8:
                    return CalleeNameSignature;
                case 9:
                    return TailPrefix;
                case 10:
                    return FailReason;
                case 11:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<MethodJitTailCallFailedAnsiTraceData> Action;
        #endregion
    }

    public sealed class MethodJitTailCallFailedTraceData : TraceEvent
    {
        public string MethodBeingCompiledNamespace { get { return GetUnicodeStringAt(0); } }
        public string MethodBeingCompiledName { get { return GetUnicodeStringAt(SkipUnicodeString(0)); } }
        public string MethodBeingCompiledNameSignature { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(0))); } }
        public string CallerNamespace { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0)))); } }
        public string CallerName { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))); } }
        public string CallerNameSignature { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0)))))); } }
        public string CalleeNamespace { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))); } }
        public string CalleeName { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0)))))))); } }
        public string CalleeNameSignature { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))))); } }
        public bool TailPrefix { get { return GetInt32At(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0)))))))))) != 0; } }
        public string FailReason { get { return GetUnicodeStringAt(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))))) + 4); } }
        public int ClrInstanceID { get { return GetInt16At(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))))) + 4)); } }

        #region Private
        internal MethodJitTailCallFailedTraceData(Action<MethodJitTailCallFailedTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<MethodJitTailCallFailedTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))))) + 4) + 2));
            Debug.Assert(!(Version > 0 && EventDataLength < SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(SkipUnicodeString(0))))))))) + 4) + 2));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "MethodBeingCompiledNamespace", MethodBeingCompiledNamespace);
            XmlAttrib(sb, "MethodBeingCompiledName", MethodBeingCompiledName);
            XmlAttrib(sb, "MethodBeingCompiledNameSignature", MethodBeingCompiledNameSignature);
            XmlAttrib(sb, "CallerNamespace", CallerNamespace);
            XmlAttrib(sb, "CallerName", CallerName);
            XmlAttrib(sb, "CallerNameSignature", CallerNameSignature);
            XmlAttrib(sb, "CalleeNamespace", CalleeNamespace);
            XmlAttrib(sb, "CalleeName", CalleeName);
            XmlAttrib(sb, "CalleeNameSignature", CalleeNameSignature);
            XmlAttrib(sb, "TailPrefix", TailPrefix);
            XmlAttrib(sb, "FailReason", FailReason);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "MethodBeingCompiledNamespace", "MethodBeingCompiledName", "MethodBeingCompiledNameSignature", "CallerNamespace", "CallerName", "CallerNameSignature", "CalleeNamespace", "CalleeName", "CalleeNameSignature", "TailPrefix", "FailReason", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return MethodBeingCompiledNamespace;
                case 1:
                    return MethodBeingCompiledName;
                case 2:
                    return MethodBeingCompiledNameSignature;
                case 3:
                    return CallerNamespace;
                case 4:
                    return CallerName;
                case 5:
                    return CallerNameSignature;
                case 6:
                    return CalleeNamespace;
                case 7:
                    return CalleeName;
                case 8:
                    return CalleeNameSignature;
                case 9:
                    return TailPrefix;
                case 10:
                    return FailReason;
                case 11:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<MethodJitTailCallFailedTraceData> Action;
        #endregion
    }

    [Flags]
    public enum AppDomainFlags
    {
        None = 0,
        Default = 0x1,
        Executable = 0x2,
        Shared = 0x4,
    }
    [Flags]
    public enum AssemblyFlags
    {
        None = 0,
        DomainNeutral = 0x1,
        Dynamic = 0x2,
        Native = 0x4,
        Collectible = 0x8,
        ReadyToRun = 0x10,
    }
    [Flags]
    public enum ModuleFlags
    {
        None = 0,
        DomainNeutral = 0x1,
        Native = 0x2,
        Dynamic = 0x4,
        Manifest = 0x8,
        IbcOptimized = 0x10,
        ReadyToRunModule = 0x20,
        PartialReadyToRunModule = 0x40,
    }
    [Flags]
    public enum MethodFlags
    {
        None = 0,
        Dynamic = 0x1,
        Generic = 0x2,
        HasSharedGenericCode = 0x4,
        Jitted = 0x8,
        JitHelper = 0x10,
        ProfilerRejectedPrecompiledCode = 0x20,
        ReadyToRunRejectedPrecompiledCode = 0x40,
        // 0x80 to 0x100 are used for the tier
    }
    public enum OptimizationTier : byte
    {
        Unknown, // to identify older runtimes that would send this value

        // Jitted, sent by the runtime
        MinOptJitted,
        Optimized,
        QuickJitted,
        OptimizedTier1,

        // Pregenerated code, not sent by the runtime
        ReadyToRun,

        // This value is not sent by the runtime through this parser, but
        // does get sent through Linux traces.  Set the value to byte.MaxValue
        // to avoid clobbering a real value.
        PreJIT = byte.MaxValue
    }
    [Flags]
    public enum StartupMode
    {
        None = 0,
        ManagedExe = 0x1,
        HostedClr = 0x2,
        IjwDll = 0x4,
        ComActivated = 0x8,
        Other = 0x10,
    }
    [Flags]
    public enum RuntimeSku
    {
        None = 0,
        DesktopClr = 0x1,
        CoreClr = 0x2,
    }
    [Flags]
    public enum ExceptionThrownFlags
    {
        None = 0,
        HasInnerException = 0x1,
        Nested = 0x2,
        ReThrown = 0x4,
        CorruptedState = 0x8,
        CLSCompliant = 0x10,
    }
    [Flags]
    public enum ILStubGeneratedFlags
    {
        None = 0,
        ReverseInterop = 0x1,
        ComInterop = 0x2,
        NGenedStub = 0x4,
        Delegate = 0x8,
        VarArg = 0x10,
        UnmanagedCallee = 0x20,
    }
    [Flags]
    public enum StartupFlags
    {
        None = 0,
        CONCURRENT_GC = 0x000001,
        LOADER_OPTIMIZATION_SINGLE_DOMAIN = 0x000002,
        LOADER_OPTIMIZATION_MULTI_DOMAIN = 0x000004,
        LOADER_SAFEMODE = 0x000010,
        LOADER_SETPREFERENCE = 0x000100,
        SERVER_GC = 0x001000,
        HOARD_GC_VM = 0x002000,
        SINGLE_VERSION_HOSTING_INTERFACE = 0x004000,
        LEGACY_IMPERSONATION = 0x010000,
        DISABLE_COMMITTHREADSTACK = 0x020000,
        ALWAYSFLOW_IMPERSONATION = 0x040000,
        TRIM_GC_COMMIT = 0x080000,
        ETW = 0x100000,
        SERVER_BUILD = 0x200000,
        ARM = 0x400000,
    }
    [Flags]
    public enum TypeFlags
    {
        None = 0,
        Delegate = 0x1,
        Finalizable = 0x2,
        ExternallyImplementedCOMObject = 0x4,       // RCW.  
        Array = 0x8,
        ModuleBaseAddress = 0x10,

        // TODO FIX NOW, need to add ContainsPointer
        // Also want ElementSize.  (not in flags of course)
        ArrayRankBit0 = 0x100,
        ArrayRankBit1 = 0x200,
        ArrayRankBit2 = 0x400,
        ArrayRankBit3 = 0x800,
        ArrayRankBit4 = 0x1000,
        ArrayRankBit5 = 0x2000,
    }
    public static class TypeFlagsHelpers
    {
        public static int GetArrayRank(this TypeFlags flags)
        {
            int rank = (((int)flags) >> 8) & 0x3F;
            if (rank == 0)
                return 1; // SzArray case
            return rank;
        }
    }
    [Flags]
    public enum GCRootFlags
    {
        None = 0,
        Pinning = 0x1,
        WeakRef = 0x2,
        Interior = 0x4,
        RefCounted = 0x8,
    }
    [Flags]
    public enum GCRootStaticVarFlags
    {
        None = 0,
        ThreadLocal = 0x1,
    }
    [Flags]
    public enum ThreadFlags
    {
        None = 0,
        GCSpecial = 0x1,
        Finalizer = 0x2,
        ThreadPoolWorker = 0x4,
    }
    public enum GCSegmentType
    {
        SmallObjectHeap = 0x0,
        LargeObjectHeap = 0x1,
        ReadOnlyHeap = 0x2,
    }
    public enum GCAllocationKind
    {
        Small = 0x0,
        Large = 0x1,
    }
    public enum GCType
    {
        NonConcurrentGC = 0x0,      // A 'blocking' GC.  
        BackgroundGC = 0x1,         // A Gen 2 GC happening while code continues to run
        ForegroundGC = 0x2,         // A Gen 0 or Gen 1 blocking GC which is happening when a Background GC is in progress.  
    }
    public enum GCReason
    {
        AllocSmall = 0x0,
        Induced = 0x1,
        LowMemory = 0x2,
        Empty = 0x3,
        AllocLarge = 0x4,
        OutOfSpaceSOH = 0x5,
        OutOfSpaceLOH = 0x6,
        InducedNotForced = 0x7,
        Internal = 0x8,
        InducedLowMemory = 0x9,
        InducedCompacting = 0xa,
        LowMemoryHost = 0xb,
        PMFullGC = 0xc,
        LowMemoryHostBlocking = 0xd
    }
    public enum GCSuspendEEReason
    {
        SuspendOther = 0x0,
        SuspendForGC = 0x1,
        SuspendForAppDomainShutdown = 0x2,
        SuspendForCodePitching = 0x3,
        SuspendForShutdown = 0x4,
        SuspendForDebugger = 0x5,
        SuspendForGCPrep = 0x6,
        SuspendForDebuggerSweep = 0x7,
    }

    public enum GCPauseMode
    {
        Invalid = -1,
        Batch = 0,
        Interactive = 1,
        LowLatency = 2,
        SustainedLowLatency = 3,
        NoGC = 4
    }

    public enum ContentionFlags
    {
        Managed = 0x0,
        Native = 0x1,
    }
    public enum TailCallType
    {
        Unknown = -1,
        OptimizedTailCall = 0x0,
        RecursiveLoop = 0x1,
        HelperAssistedTailCall = 0x2,
    }
    public enum ThreadAdjustmentReason
    {
        Warmup = 0x0,
        Initializing = 0x1,
        RandomMove = 0x2,
        ClimbingMove = 0x3,
        ChangePoint = 0x4,
        Stabilizing = 0x5,
        Starvation = 0x6,
        ThreadTimedOut = 0x7,
        CooperativeBlocking = 0x8,
    }

    [Flags]
    public enum GCRootRCWFlags
    {
        None = 0,
        Duplicate = 1,
        XAMLObject = 2,
        ExtendsComObject = 4,
    }

    [Flags]
    public enum GCRootCCWFlags
    {
        None = 0,
        Strong = 1,
        XAMLObject = 2,
        ExtendsComObject = 4,
    }

    public enum GCRootKind
    {
        Stack = 0,
        Finalizer = 1,
        Handle = 2,
        Older = 0x3,
        SizedRef = 0x4,
        Overflow = 0x5,

    }

    public enum GCHandleKind
    {
        WeakShort = 0x0,
        WeakLong = 0x1,
        Strong = 0x2,
        Pinned = 0x3,
        Variable = 0x4,
        RefCounted = 0x5,
        Dependent = 0x6,
        AsyncPinned = 0x7,
        SizedRef = 0x8,
        DependendAsyncPinned = -0x7,
    }

    public enum KnownPathSource
    {
        ApplicationAssemblies = 0x0,
        AppNativeImagePaths = 0x1,
        AppPaths = 0x2,
        PlatformResourceRoots = 0x3,
        SatelliteSubdirectory = 0x4,
    }
    public enum ResolutionAttemptedResult
    {
        Success = 0x0,
        AssemblyNotFound = 0x1,
        MismatchedAssemblyName = 0x2,
        IncompatibleVersion = 0x3,
        Failure = 0x4,
        Exception = 0x5,
    }
    public enum ResolutionAttemptedStage
    {
        FindInLoadContext = 0x0,
        AssemblyLoadContextLoad = 0x1,
        ApplicationAssemblies = 0x2,
        DefaultAssemblyLoadContextFallback = 0x3,
        ResolveSatelliteAssembly = 0x4,
        AssemblyLoadContextResolvingEvent = 0x5,
        AppDomainAssemblyResolveEvent = 0x6,
    }

    [Flags]
    public enum TieredCompilationSettingsFlags : uint
    {
        None = 0x0,
        QuickJit = 0x1,
        QuickJitForLoops = 0x2,
    }

    // [SecuritySafeCritical]
    [System.CodeDom.Compiler.GeneratedCode("traceparsergen", "1.0")]
    public sealed class ClrRundownTraceEventParser : TraceEventParser
    {
        public static readonly string ProviderName = "Microsoft-Windows-DotNETRuntimeRundown";
        public static readonly Guid ProviderGuid = new Guid(unchecked((int)0xa669021c), unchecked((short)0xc450), unchecked((short)0x4609), 0xa0, 0x35, 0x5a, 0xf5, 0x9a, 0xf4, 0xdf, 0x18);
        public enum Keywords : long
        {
            Loader = 0x8,
            Jit = 0x10,
            NGen = 0x20,
            StartEnumeration = 0x40,                   // Do rundown at DC_START
            StopEnumeration = 0x80,                    // Do rundown at DC_STOP
            ForceEndRundown = 0x100,
            AppDomainResourceManagement = 0x800,
            /// <summary>
            /// Log events associated with the threadpool, and other threading events.  
            /// </summary>
            Threading = 0x10000,
            /// <summary>
            /// Dump the native to IL mapping of any method that is JIT compiled.  (V4.5 runtimes and above).  
            /// </summary>
            JittedMethodILToNativeMap = 0x20000,
            /// <summary>
            /// This suppresses NGEN events on V4.0 (where you have NGEN PDBs), but not on V2.0 (which does not know about this 
            /// bit and also does not have NGEN PDBS).  
            /// </summary>
            SupressNGen = 0x40000,
            /// <summary>
            /// TODO document
            /// </summary>
            PerfTrack = 0x20000000,
            Stack = 0x40000000,
            /// <summary>
            /// Dump PDBs for dynamically generated modules.  
            /// </summary>
            CodeSymbolsRundown = 0x80000000,
            /// <summary>
            /// Events that provide information about compilation.
            /// </summary>
            Compilation = 0x1000000000,

            Default = ForceEndRundown + NGen + Jit + SupressNGen + JittedMethodILToNativeMap + Loader + CodeSymbolsRundown +
                      Compilation,
        };

        public ClrRundownTraceEventParser(TraceEventSource source) : base(source) { }

        public event Action<MethodILToNativeMapTraceData> MethodILToNativeMapDCStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new MethodILToNativeMapTraceData(value, 149, 1, "Method", MethodTaskGuid, 41, "ILToNativeMapDCStart", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 149, ProviderGuid);
                source.UnregisterEventTemplate(value, 41, MethodTaskGuid);
            }
        }
        public event Action<MethodILToNativeMapTraceData> MethodILToNativeMapDCStop
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new MethodILToNativeMapTraceData(value, 150, 1, "Method", MethodTaskGuid, 42, "ILToNativeMapDCStop", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 150, ProviderGuid);
                source.UnregisterEventTemplate(value, 42, MethodTaskGuid);
            }
        }
        public event Action<ClrStackWalkTraceData> ClrStackWalk
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ClrStackWalkTraceData(value, 0, 11, "ClrStack", ClrStackTaskGuid, 82, "Walk", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 0, ProviderGuid);
                source.UnregisterEventTemplate(value, 82, ClrStackTaskGuid);
            }
        }
        public event Action<MethodLoadUnloadTraceData> MethodDCStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new MethodLoadUnloadTraceData(value, 141, 1, "Method", MethodTaskGuid, 35, "DCStart", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 141, ProviderGuid);
                source.UnregisterEventTemplate(value, 35, MethodTaskGuid);
            }
        }
        public event Action<MethodLoadUnloadTraceData> MethodDCStop
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new MethodLoadUnloadTraceData(value, 142, 1, "Method", MethodTaskGuid, 36, "DCStop", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 142, ProviderGuid);
                source.UnregisterEventTemplate(value, 36, MethodTaskGuid);
            }
        }
        public event Action<MethodLoadUnloadVerboseTraceData> MethodDCStartVerbose
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new MethodLoadUnloadVerboseTraceData(value, 143, 1, "Method", MethodTaskGuid, 39, "DCStartVerbose", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 143, ProviderGuid);
                source.UnregisterEventTemplate(value, 39, MethodTaskGuid);
            }
        }
        public event Action<MethodLoadUnloadVerboseTraceData> MethodDCStopVerbose
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new MethodLoadUnloadVerboseTraceData(value, 144, 1, "Method", MethodTaskGuid, 40, "DCStopVerbose", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 144, ProviderGuid);
                source.UnregisterEventTemplate(value, 40, MethodTaskGuid);
            }
        }
        public event Action<DCStartEndTraceData> MethodDCStartComplete
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new DCStartEndTraceData(value, 145, 1, "Method", MethodTaskGuid, 14, "DCStartComplete", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 145, ProviderGuid);
                source.UnregisterEventTemplate(value, 14, MethodTaskGuid);
            }
        }
        public event Action<DCStartEndTraceData> MethodDCStopComplete
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new DCStartEndTraceData(value, 146, 1, "Method", MethodTaskGuid, 15, "DCStopComplete", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 146, ProviderGuid);
                source.UnregisterEventTemplate(value, 15, MethodTaskGuid);
            }
        }
        public event Action<DCStartEndTraceData> MethodDCStartInit
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new DCStartEndTraceData(value, 147, 1, "Method", MethodTaskGuid, 16, "DCStartInit", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 147, ProviderGuid);
                source.UnregisterEventTemplate(value, 16, MethodTaskGuid);
            }
        }
        public event Action<DCStartEndTraceData> MethodDCStopInit
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new DCStartEndTraceData(value, 148, 1, "Method", MethodTaskGuid, 17, "DCStopInit", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 148, ProviderGuid);
                source.UnregisterEventTemplate(value, 17, MethodTaskGuid);
            }
        }
        public event Action<DomainModuleLoadUnloadTraceData> LoaderDomainModuleDCStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new DomainModuleLoadUnloadTraceData(value, 151, 2, "Loader", LoaderTaskGuid, 46, "DomainModuleDCStart", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 151, ProviderGuid);
                source.UnregisterEventTemplate(value, 46, LoaderTaskGuid);
            }
        }
        public event Action<DomainModuleLoadUnloadTraceData> LoaderDomainModuleDCStop
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new DomainModuleLoadUnloadTraceData(value, 152, 2, "Loader", LoaderTaskGuid, 47, "DomainModuleDCStop", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 152, ProviderGuid);
                source.UnregisterEventTemplate(value, 47, LoaderTaskGuid);
            }
        }
        public event Action<ModuleLoadUnloadTraceData> LoaderModuleDCStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ModuleLoadUnloadTraceData(value, 153, 2, "Loader", LoaderTaskGuid, 35, "ModuleDCStart", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 153, ProviderGuid);
                source.UnregisterEventTemplate(value, 35, LoaderTaskGuid);
            }
        }
        public event Action<ModuleLoadUnloadTraceData> LoaderModuleDCStop
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ModuleLoadUnloadTraceData(value, 154, 2, "Loader", LoaderTaskGuid, 36, "ModuleDCStop", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 154, ProviderGuid);
                source.UnregisterEventTemplate(value, 36, LoaderTaskGuid);
            }
        }
        public event Action<AssemblyLoadUnloadTraceData> LoaderAssemblyDCStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new AssemblyLoadUnloadTraceData(value, 155, 2, "Loader", LoaderTaskGuid, 39, "AssemblyDCStart", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 155, ProviderGuid);
                source.UnregisterEventTemplate(value, 39, LoaderTaskGuid);
            }
        }
        public event Action<AssemblyLoadUnloadTraceData> LoaderAssemblyDCStop
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new AssemblyLoadUnloadTraceData(value, 156, 2, "Loader", LoaderTaskGuid, 40, "AssemblyDCStop", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 156, ProviderGuid);
                source.UnregisterEventTemplate(value, 40, LoaderTaskGuid);
            }
        }
        public event Action<AppDomainLoadUnloadTraceData> LoaderAppDomainDCStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new AppDomainLoadUnloadTraceData(value, 157, 2, "Loader", LoaderTaskGuid, 43, "AppDomainDCStart", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 157, ProviderGuid);
                source.UnregisterEventTemplate(value, 43, LoaderTaskGuid);
            }
        }
        public event Action<AppDomainLoadUnloadTraceData> LoaderAppDomainDCStop
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new AppDomainLoadUnloadTraceData(value, 158, 2, "Loader", LoaderTaskGuid, 44, "AppDomainDCStop", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 158, ProviderGuid);
                source.UnregisterEventTemplate(value, 44, LoaderTaskGuid);
            }
        }
        public event Action<ThreadCreatedTraceData> LoaderThreadDCStop
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ThreadCreatedTraceData(value, 159, 2, "Loader", LoaderTaskGuid, 48, "ThreadDCStop", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 159, ProviderGuid);
                source.UnregisterEventTemplate(value, 48, LoaderTaskGuid);
            }
        }
        public event Action<RuntimeInformationTraceData> RuntimeStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new RuntimeInformationTraceData(value, 187, 19, "Runtime", RuntimeTaskGuid, 1, "Start", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 187, ProviderGuid);
                source.UnregisterEventTemplate(value, 1, RuntimeTaskGuid);
            }
        }
        public event Action<CodeSymbolsTraceData> CodeSymbolsRundownStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new CodeSymbolsTraceData(value, 188, 21, "CodeSymbolsRundown", CodeSymbolsRundownTaskGuid, 1, "Start", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 188, ProviderGuid);
                source.UnregisterEventTemplate(value, 1, CodeSymbolsRundownTaskGuid);
            }
        }
        public event Action<TieredCompilationSettingsTraceData> TieredCompilationRundownSettingsDCStart
        {
            add
            {
                source.RegisterEventTemplate(TieredCompilationSettingsDCStartTemplate(value));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 280, ProviderGuid);
                source.UnregisterEventTemplate(value, 11, TieredCompilationRundownTaskGuid);
            }
        }

        #region Event ID Definitions
        private const TraceEventID ClrStackWalkEventID = (TraceEventID)0;
        private const TraceEventID MethodDCStartEventID = (TraceEventID)141;
        private const TraceEventID MethodDCStopEventID = (TraceEventID)142;
        private const TraceEventID MethodDCStartVerboseEventID = (TraceEventID)143;
        private const TraceEventID MethodDCStopVerboseEventID = (TraceEventID)144;
        private const TraceEventID MethodDCStartCompleteEventID = (TraceEventID)145;
        private const TraceEventID MethodDCStopCompleteEventID = (TraceEventID)146;
        private const TraceEventID MethodDCStartInitEventID = (TraceEventID)147;
        private const TraceEventID MethodDCStopInitEventID = (TraceEventID)148;
        private const TraceEventID LoaderDomainModuleDCStartEventID = (TraceEventID)151;
        private const TraceEventID LoaderDomainModuleDCStopEventID = (TraceEventID)152;
        private const TraceEventID LoaderModuleDCStartEventID = (TraceEventID)153;
        private const TraceEventID LoaderModuleDCStopEventID = (TraceEventID)154;
        private const TraceEventID LoaderAssemblyDCStartEventID = (TraceEventID)155;
        private const TraceEventID LoaderAssemblyDCStopEventID = (TraceEventID)156;
        private const TraceEventID LoaderAppDomainDCStartEventID = (TraceEventID)157;
        private const TraceEventID LoaderAppDomainDCStopEventID = (TraceEventID)158;
        private const TraceEventID LoaderThreadDCStopEventID = (TraceEventID)159;
        private const TraceEventID RuntimeStartEventID = (TraceEventID)187;
        private const TraceEventID CodeSymbolsRundownStartEventID = (TraceEventID)188;
        #endregion

        public sealed class DCStartEndTraceData : TraceEvent
        {
            public int ClrInstanceID { get { if (Version >= 1) return GetInt16At(0); return 0; } }

            #region Private
            internal DCStartEndTraceData(Action<DCStartEndTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
                : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
            {
                this.Action = action;
            }
            protected internal override void Dispatch()
            {
                Action(this);
            }
            protected internal override Delegate Target
            {
                get { return Action; }
                set { Action = (Action<DCStartEndTraceData>)value; }
            }
            protected internal override void Validate()
            {
                Debug.Assert(!(Version == 1 && EventDataLength != 2));
                Debug.Assert(!(Version > 1 && EventDataLength < 2));
            }
            public override StringBuilder ToXml(StringBuilder sb)
            {
                Prefix(sb);
                XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
                sb.Append("/>");
                return sb;
            }

            public override string[] PayloadNames
            {
                get
                {
                    if (payloadNames == null)
                        payloadNames = new string[] { "ClrInstanceID" };
                    return payloadNames;
                }
            }

            public override object PayloadValue(int index)
            {
                switch (index)
                {
                    case 0:
                        return ClrInstanceID;
                    default:
                        Debug.Assert(false, "Bad field index");
                        return null;
                }
            }

            private event Action<DCStartEndTraceData> Action;
            #endregion
        }
        #region private
        protected override string GetProviderName() { return ProviderName; }

        static private TieredCompilationSettingsTraceData TieredCompilationSettingsDCStartTemplate(Action<TieredCompilationSettingsTraceData> action)
        {                  // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
            return new TieredCompilationSettingsTraceData(action, 280, 31, "TieredCompilationRundown", TieredCompilationRundownTaskGuid, 11, "SettingsDCStart", ProviderGuid, ProviderName);
        }

        static private volatile TraceEvent[] s_templates;
        protected internal override void EnumerateTemplates(Func<string, string, EventFilterResponse> eventsToObserve, Action<TraceEvent> callback)
        {
            if (s_templates == null)
            {
                var templates = new TraceEvent[23];
                templates[0] = new MethodILToNativeMapTraceData(null, 149, 1, "Method", MethodTaskGuid, 41, "ILToNativeMapDCStart", ProviderGuid, ProviderName);
                templates[1] = new MethodILToNativeMapTraceData(null, 150, 1, "Method", MethodTaskGuid, 42, "ILToNativeMapDCStop", ProviderGuid, ProviderName);
                templates[2] = new ClrStackWalkTraceData(null, 0, 11, "ClrStack", ClrStackTaskGuid, 82, "Walk", ProviderGuid, ProviderName);
                templates[3] = new MethodLoadUnloadTraceData(null, 141, 1, "Method", MethodTaskGuid, 35, "DCStart", ProviderGuid, ProviderName);
                templates[4] = new MethodLoadUnloadTraceData(null, 142, 1, "Method", MethodTaskGuid, 36, "DCStop", ProviderGuid, ProviderName);
                templates[5] = new MethodLoadUnloadVerboseTraceData(null, 143, 1, "Method", MethodTaskGuid, 39, "DCStartVerbose", ProviderGuid, ProviderName);
                templates[6] = new MethodLoadUnloadVerboseTraceData(null, 144, 1, "Method", MethodTaskGuid, 40, "DCStopVerbose", ProviderGuid, ProviderName);
                templates[7] = new DCStartEndTraceData(null, 145, 1, "Method", MethodTaskGuid, 14, "DCStartComplete", ProviderGuid, ProviderName);
                templates[8] = new DCStartEndTraceData(null, 146, 1, "Method", MethodTaskGuid, 15, "DCStopComplete", ProviderGuid, ProviderName);
                templates[9] = new DCStartEndTraceData(null, 147, 1, "Method", MethodTaskGuid, 16, "DCStartInit", ProviderGuid, ProviderName);
                templates[10] = new DCStartEndTraceData(null, 148, 1, "Method", MethodTaskGuid, 17, "DCStopInit", ProviderGuid, ProviderName);
                templates[11] = new DomainModuleLoadUnloadTraceData(null, 151, 2, "Loader", LoaderTaskGuid, 46, "DomainModuleDCStart", ProviderGuid, ProviderName);
                templates[12] = new DomainModuleLoadUnloadTraceData(null, 152, 2, "Loader", LoaderTaskGuid, 47, "DomainModuleDCStop", ProviderGuid, ProviderName);
                templates[13] = new ModuleLoadUnloadTraceData(null, 153, 2, "Loader", LoaderTaskGuid, 35, "ModuleDCStart", ProviderGuid, ProviderName);
                templates[14] = new ModuleLoadUnloadTraceData(null, 154, 2, "Loader", LoaderTaskGuid, 36, "ModuleDCStop", ProviderGuid, ProviderName);
                templates[15] = new AssemblyLoadUnloadTraceData(null, 155, 2, "Loader", LoaderTaskGuid, 39, "AssemblyDCStart", ProviderGuid, ProviderName);
                templates[16] = new AssemblyLoadUnloadTraceData(null, 156, 2, "Loader", LoaderTaskGuid, 40, "AssemblyDCStop", ProviderGuid, ProviderName);
                templates[17] = new AppDomainLoadUnloadTraceData(null, 157, 2, "Loader", LoaderTaskGuid, 43, "AppDomainDCStart", ProviderGuid, ProviderName);
                templates[18] = new AppDomainLoadUnloadTraceData(null, 158, 2, "Loader", LoaderTaskGuid, 44, "AppDomainDCStop", ProviderGuid, ProviderName);
                templates[19] = new ThreadCreatedTraceData(null, 159, 2, "Loader", LoaderTaskGuid, 48, "ThreadDCStop", ProviderGuid, ProviderName);
                templates[20] = new RuntimeInformationTraceData(null, 187, 19, "Runtime", RuntimeTaskGuid, 1, "Start", ProviderGuid, ProviderName);
                templates[21] = new CodeSymbolsTraceData(null, 188, 21, "CodeSymbolsRundown", CodeSymbolsRundownTaskGuid, 1, "Start", ProviderGuid, ProviderName);

                // New style
                templates[22] = TieredCompilationSettingsDCStartTemplate(null);

                s_templates = templates;
            }
            foreach (var template in s_templates)
                if (eventsToObserve == null || eventsToObserve(template.ProviderName, template.EventName) == EventFilterResponse.AcceptEvent)
                    callback(template);
        }

        private static readonly Guid MethodTaskGuid = new Guid(unchecked((int)0x0bcd91db), unchecked((short)0xf943), unchecked((short)0x454a), 0xa6, 0x62, 0x6e, 0xdb, 0xcf, 0xbb, 0x76, 0xd2);
        private static readonly Guid LoaderTaskGuid = new Guid(unchecked((int)0x5a54f4df), unchecked((short)0xd302), unchecked((short)0x4fee), 0xa2, 0x11, 0x6c, 0x2c, 0x0c, 0x1d, 0xcb, 0x1a);
        private static readonly Guid ClrStackTaskGuid = new Guid(unchecked((int)0xd3363dc0), unchecked((short)0x243a), unchecked((short)0x4620), 0xa4, 0xd0, 0x8a, 0x07, 0xd7, 0x72, 0xf5, 0x33);
        private static readonly Guid RuntimeTaskGuid = new Guid(unchecked((int)0xcd7d3e32), unchecked((short)0x65fe), unchecked((short)0x40cd), 0x92, 0x25, 0xa2, 0x57, 0x7d, 0x20, 0x3f, 0xc3);
        private static readonly Guid CodeSymbolsRundownTaskGuid = new Guid(unchecked((int)0x86b6c496), unchecked((short)0x0d9e), unchecked((short)0x4ba6), 0x81, 0x93, 0xca, 0x58, 0xe6, 0xe8, 0xc5, 0x15);
        private static readonly Guid TieredCompilationRundownTaskGuid = new Guid(unchecked((int)0xa1673472), unchecked((short)0x564), unchecked((short)0x48ea), 0xa9, 0x5d, 0xb4, 0x9d, 0x41, 0x73, 0xf1, 0x5);
        #endregion
    }

    [System.CodeDom.Compiler.GeneratedCode("traceparsergen", "1.0")]
    public sealed class ClrStressTraceEventParser : TraceEventParser
    {
        public static readonly string ProviderName = "Microsoft-Windows-DotNETRuntimeStress";
        public static readonly Guid ProviderGuid = new Guid(unchecked((int)0xcc2bcbba), unchecked((short)0x16b6), unchecked((short)0x4cf3), 0x89, 0x90, 0xd7, 0x4c, 0x2e, 0x8a, 0xf5, 0x00);
        public enum Keywords : long
        {
            Stack = 0x40000000,
        };

        public ClrStressTraceEventParser(TraceEventSource source) : base(source) { }

        public event Action<StressLogTraceData> StressLogStart
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new StressLogTraceData(value, 0, 1, "StressLog", StressLogTaskGuid, 1, "Start", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 0, ProviderGuid);
                source.UnregisterEventTemplate(value, 1, StressLogTaskGuid);
            }
        }
        public event Action<ClrStackWalkTraceData> ClrStackWalk
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new ClrStackWalkTraceData(value, 1, 11, "ClrStack", ClrStackTaskGuid, 82, "Walk", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 1, ProviderGuid);
                source.UnregisterEventTemplate(value, 82, ClrStackTaskGuid);
            }
        }

        #region Event ID Definitions
        private const TraceEventID StressLogStartEventID = (TraceEventID)0;
        private const TraceEventID ClrStackWalkEventID = (TraceEventID)1;
        #endregion

        #region private
        protected override string GetProviderName() { return ProviderName; }

        static private volatile TraceEvent[] s_templates;
        protected internal override void EnumerateTemplates(Func<string, string, EventFilterResponse> eventsToObserve, Action<TraceEvent> callback)
        {
            if (s_templates == null)
            {
                var templates = new TraceEvent[2];
                templates[0] = new StressLogTraceData(null, 0, 1, "StressLog", StressLogTaskGuid, 1, "Start", ProviderGuid, ProviderName);
                templates[1] = new ClrStackWalkTraceData(null, 1, 11, "ClrStack", ClrStackTaskGuid, 82, "Walk", ProviderGuid, ProviderName);
                s_templates = templates;
            }
            foreach (var template in s_templates)
                if (eventsToObserve == null || eventsToObserve(template.ProviderName, template.EventName) == EventFilterResponse.AcceptEvent)
                    callback(template);
        }

        private static readonly Guid StressLogTaskGuid = new Guid(unchecked((int)0xea40c74d), unchecked((short)0x4f65), unchecked((short)0x4561), 0xbb, 0x26, 0x65, 0x62, 0x31, 0xc8, 0x96, 0x7f);
        private static readonly Guid ClrStackTaskGuid = new Guid(unchecked((int)0xd3363dc0), unchecked((short)0x243a), unchecked((short)0x4620), 0xa4, 0xd0, 0x8a, 0x07, 0xd7, 0x72, 0xf5, 0x33);
        #endregion
    }

    public sealed class StressLogTraceData : TraceEvent
    {
        public int Facility { get { return GetInt32At(0); } }
        public int LogLevel { get { return GetByteAt(4); } }
        public string Message { get { return GetUTF8StringAt(5); } }
        public int ClrInstanceID { get { if (Version >= 1) { return GetInt16At(SkipUTF8String(5)); } return 0; } }

        #region Private
        internal StressLogTraceData(Action<StressLogTraceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }
        protected internal override void Dispatch()
        {
            Action(this);
        }
        protected internal override Delegate Target
        {
            get { return Action; }
            set { Action = (Action<StressLogTraceData>)value; }
        }
        protected internal override void Validate()
        {
            Debug.Assert(!(Version == 0 && EventDataLength != SkipUTF8String(5)));
            Debug.Assert(!(Version == 1 && EventDataLength != SkipUTF8String(5) + 2));
            Debug.Assert(!(Version > 1 && EventDataLength < SkipUTF8String(5) + 2));
        }
        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "Facility", Facility);
            XmlAttrib(sb, "LogLevel", LogLevel);
            XmlAttrib(sb, "Message", Message);
            XmlAttrib(sb, "ClrInstanceID", ClrInstanceID);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames
        {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "Facility", "LogLevel", "Message", "ClrInstanceID" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return Facility;
                case 1:
                    return LogLevel;
                case 2:
                    return Message;
                case 3:
                    return ClrInstanceID;
                default:
                    Debug.Assert(false, "Bad field index");
                    return null;
            }
        }

        private event Action<StressLogTraceData> Action;
        #endregion
    }

    [Flags]
    public enum GCGlobalMechanisms
    {
        None = 0,
        Concurrent = 0x1,
        Compaction = 0x2,
        Promotion = 0x4,
        Demotion = 0x8,
        CardBundles = 0x10,
    }

    #region private types
    /// <summary>
    /// ClrTraceEventParserState holds all information that is shared among all events that is
    /// needed to decode Clr events.   This class is registered with the source so that it will be
    /// persisted.  Things in here include
    /// 
    ///     * TypeID to TypeName mapping, 
    /// </summary>
    internal class ClrTraceEventParserState : IFastSerializable
    {
        internal void SetTypeIDToName(int processID, Address typeId, long timeQPC, string typeName)
        {
            if (_typeIDToName == null)
            {
                _typeIDToName = new HistoryDictionary<Address, string>(500);
            }

            _typeIDToName.Add(typeId + ((ulong)processID << 48), timeQPC, typeName);
        }

        internal string TypeIDToName(int processID, Address typeId, long timeQPC)
        {
            // We don't read lazyTypeIDToName from the disk unless we need to, check
            lazyTypeIDToName.FinishRead();
            string ret;
            if (_typeIDToName == null || !_typeIDToName.TryGetValue(typeId + ((ulong)processID << 48), timeQPC, out ret))
            {
                return "";
            }

            return ret;
        }

        #region private 

        void IFastSerializable.ToStream(Serializer serializer)
        {
            lazyTypeIDToName.Write(serializer, delegate
            {
                if (_typeIDToName == null)
                {
                    serializer.Write(0);
                    return;
                }
                serializer.Log("<WriteCollection name=\"typeIDToName\" count=\"" + _typeIDToName.Count + "\">\r\n");
                serializer.Write(_typeIDToName.Count);
                foreach (HistoryDictionary<Address, string>.HistoryValue entry in _typeIDToName.Entries)
                {
                    serializer.WriteAddress(entry.Key);
                    serializer.Write(entry.StartTime);
                    serializer.Write(entry.Value);
                }
                serializer.Log("</WriteCollection>\r\n");
            });
        }

        void IFastSerializable.FromStream(Deserializer deserializer)
        {
            lazyTypeIDToName.Read(deserializer, delegate
            {
                int count;
                deserializer.ReadInt32(out count);
                Debug.Assert(count >= 0);
                deserializer.Log("<Marker name=\"typeIDToName\"/ count=\"" + count + "\">");
                if (count > 0)
                {
                    if (_typeIDToName == null)
                    {
                        _typeIDToName = new HistoryDictionary<Address, string>(count);
                    }

                    for (int i = 0; i < count; i++)
                    {
                        Address key; deserializer.ReadAddress(out key);
                        long startTimeQPC; deserializer.ReadInt64(out startTimeQPC);
                        string value; deserializer.ReadString(out value);
                        _typeIDToName.Add(key, startTimeQPC, value);
                    }
                }
            });
        }

        private DeferedRegion lazyTypeIDToName;
        private HistoryDictionary<Address, string> _typeIDToName;
        #endregion // private 
    }
    #endregion  // private types

}
