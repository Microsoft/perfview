// Copyright (c) Microsoft Corporation.  All rights reserved
// This file is best viewed using outline mode (Ctrl-M Ctrl-O)
//
// This program uses code hyperlinks available as part of the HyperAddin Visual Studio plug-in.
// It is available from http://www.codeplex.com/hyperAddin 
// 
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Microsoft.Diagnostics.Tracing.Stacks
{
    /// <summary>
    /// TraceEventStackSource is an implementation of a StackSource for ETW information (TraceLog)
    /// It takes a TraceEvents (which is a list of TraceEvents you get get from a TraceLog) and 
    /// implements that StackSource protocol for them.  (thus any code needing a StackSource 
    /// can then work on it.  
    /// 
    /// The key to the implementation is how StackSourceFrameIndex and StackSourceCallStackIndex 
    /// (part of the StackSource protocol) are mapped to the Indexes in TraceLog.   Here is
    /// the mapping.
    /// 
    /// TraceEventStackSource create the following meaning for the StackSourceCallStackIndex
    /// 
    /// * The call stacks ID consists of the following ranges concatenated together. 
    ///     * a small set of fixed Pseudo stacks (Start marks the end of these)
    ///     * CallStackIndex
    ///     * ThreadIndex
    ///     * ProcessIndex
    ///     * BrokenStacks (One per thread)
    ///     * Stacks for CPU samples without explicit stacks (we make 1 element stacks out of them)
    ///         
    /// TraceEventStackSource create the following meaning for the StackSourceFrameIndex
    /// 
    /// The frame ID consists of the following ranges concatenated together. 
    ///     * a small fixed number of Pseudo frame (Broken, and Unknown)
    ///     * MaxCodeAddressIndex - something with a TraceCodeAddress. 
    ///     * ThreadIndex         - ETW stacks don't have a thread or process node, so we add them.
    ///     * ProcessIndex
    /// </summary>
    public class TraceEventStackSource : StackSource
    {
        /// <summary>
        /// Creates a new TraceEventStackSource given a list of events 'events' from a TraceLog
        /// </summary>
        /// <param name="events"></param>
        public TraceEventStackSource(TraceEvents events)
        {
            Debug.Assert(m_log == null);
            if (events != null)
            {
                m_log = events.Log;
            }

            m_goodTopModuleIndex = ModuleFileIndex.Invalid;
            m_curSample = new StackSourceSample(this);
            m_curSample.Metric = events.Log.SampleProfileInterval.TotalMilliseconds;
            m_events = events;
            m_maxPseudoStack = m_log.CodeAddresses.Count * 2 + m_log.Threads.Count;     // This really is a guess as to how many stacks we need.   You can have as many as codeAddresses*threads   
        }

        // These are TraceEventStackSource specific.  
        /// <summary>
        /// Returns the TraceLog file that is associated with this stack source.  
        /// </summary>
        public TraceLog TraceLog { get { return m_log; } }
        /// <summary>
        /// Normally addresses without symbolic names are listed as ?, however sometimes it is useful 
        /// to see the actual address as a hexadecimal number.  Setting this will do that.  
        /// </summary>
        public bool ShowUnknownAddresses { get; set; }

        /// <summary>
        /// Displays the optimization tier of each code version executed for the method.
        /// </summary>
        public bool ShowOptimizationTiers { get; set; }

        /// <summary>
        /// Looks up symbols for all modules that have an inclusive count >= minCount. 
        /// stackSource, if given, can be used to be the filter.  If null, 'this' is used.
        /// If stackSource is given, it needs to use the same indexes for frames as 'this'.
        /// shouldLoadSymbols, if given, can be used to filter the modules.
        /// </summary>
        public void LookupWarmSymbols(int minCount, SymbolReader reader, StackSource stackSource = null, Predicate<TraceModuleFile> shouldLoadSymbols = null)
        {
            if (stackSource == null)
            {
                stackSource = this;
            }

            Debug.Assert(stackSource.CallFrameIndexLimit == CallFrameIndexLimit);
            Debug.Assert(stackSource.CallStackIndexLimit == CallStackIndexLimit);

            reader.Log.WriteLine("Resolving all symbols for modules with inclusive times > {0}", minCount);
            if ((reader.Options & SymbolReaderOptions.CacheOnly) != 0)
            {
                reader.Log.WriteLine("Cache-Only set: will only look on the local machine.");
            }

            // Get a list of all the unique frames.   We also keep track of unique stacks for efficiency
            var stackModuleLists = new ModuleList[stackSource.CallStackIndexLimit];
            var stackCounts = new int[stackSource.CallStackIndexLimit];
            var totalCount = 0;

            // Compute for each stack, the set of inclusive modules for that stack
            stackSource.ForEach(delegate (StackSourceSample sample)
            {
                stackCounts[(int)sample.StackIndex]++;
                totalCount++;
            });
            reader.Log.WriteLine("Got a total of {0} samples", totalCount);

            // for each stack in the trace, find the list of modules for that stack
            var moduleCounts = new int[TraceLog.ModuleFiles.Count];
            for (int i = 0; i < stackCounts.Length; i++)
            {
                var count = stackCounts[i];
                if (count > 0)
                {
                    var modules = GetModulesForStack(stackModuleLists, (StackSourceCallStackIndex)i);
                    // Update the counts for each module in that stack.  
                    while (modules != null)
                    {
                        moduleCounts[(int)modules.Module.ModuleFileIndex] += count;
                        modules = modules.Next;
                    }
                }
            }

            // Now that we have an list of the inclusive counts of all frames.  Find all stacks that meet the threshold
            for (int i = 0; i < moduleCounts.Length; i++)
            {
                if (moduleCounts[i] >= minCount)
                {
                    var moduleFile = TraceLog.ModuleFiles[(ModuleFileIndex)i];
                    if (shouldLoadSymbols == null || shouldLoadSymbols(moduleFile))
                    {
                        reader.Log.WriteLine("Resolving symbols (count={0}) for module {1} ", moduleCounts[i], moduleFile.FilePath);
                        TraceLog.CallStacks.CodeAddresses.LookupSymbolsForModule(reader, moduleFile);
                    }
                }
            }
            reader.Log.WriteLine("Done Resolving all symbols for modules with inclusive times > {0}", minCount);
        }
        /// <summary>
        /// Given a frame index, return the corresponding code address for it.  This is useful for looking up line number information. 
        /// </summary>
        public CodeAddressIndex GetFrameCodeAddress(StackSourceFrameIndex frameIndex)
        {
            uint codeAddressIndex = (uint)frameIndex - (uint)StackSourceFrameIndex.Start;
            if (codeAddressIndex >= m_log.CodeAddresses.Count)
            {
                return CodeAddressIndex.Invalid;
            }

            return (CodeAddressIndex)codeAddressIndex;
        }

        #region implementation of StackSource
        /// <summary>
        /// Implementation of StackSource protocol. 
        /// </summary>
        public override void ForEach(Action<StackSourceSample> callback)
        {
            var dispatcher = m_events.GetSource();
            // TODO use callback model rather than enumerator
            foreach (var event_ in ((IEnumerable<TraceEvent>)m_events))
            {
                m_curSample.StackIndex = GetStack(event_);
                Debug.Assert(m_curSample.StackIndex >= 0 || m_curSample.SampleIndex == StackSourceSampleIndex.Invalid);

                m_curSample.TimeRelativeMSec = event_.TimeStampRelativeMSec;
                Debug.Assert(event_.ProcessName != null);
                callback(m_curSample);
            };
        }
        /// <summary>
        /// Implementation of StackSource protocol. 
        /// </summary>
        public override double SampleTimeRelativeMSecLimit
        {
            get
            {
                return m_log.SessionEndTimeRelativeMSec;
            }
        }
        /// <summary>
        /// Implementation of StackSource protocol. 
        /// </summary>
        public override StackSourceFrameIndex GetFrameIndex(StackSourceCallStackIndex callStackIndex)
        {
            Debug.Assert(callStackIndex >= 0);
            Debug.Assert(StackSourceCallStackIndex.Start == 0);         // If there are any cases before start, we need to handle them here. 
            int stackIndex = (int)callStackIndex - (int)StackSourceCallStackIndex.Start;
            if (stackIndex < m_log.CallStacks.Count)
            {
                CodeAddressIndex codeAddressIndex = m_log.CallStacks.CodeAddressIndex((CallStackIndex)stackIndex);
                return (StackSourceFrameIndex)(codeAddressIndex + (int)StackSourceFrameIndex.Start);
            }
            stackIndex -= m_log.CallStacks.Count;
            if (stackIndex < m_log.Threads.Count + m_log.Processes.Count)
            {
                // At this point this is the encoded thread/process index.   We use the same encoding for both stacks and for frame names
                // so we just need to add back in the proper offset. 
                return (StackSourceFrameIndex)(stackIndex + m_log.CodeAddresses.Count + (int)StackSourceFrameIndex.Start);
            }
            stackIndex -= m_log.Threads.Count + m_log.Processes.Count;

            if (stackIndex < m_log.Threads.Count)      // Is it a broken stack 
            {
                return StackSourceFrameIndex.Broken;
            }

            stackIndex -= m_log.Threads.Count;

            // Is it a 'single node' stack (e.g. a profile sample without a stack)
            if (stackIndex < m_pseudoStacks.Count)
            {
                // From the Pseudo stack index, find the code address.  
                int codeAddressIndex = (int)m_pseudoStacks[stackIndex].CodeAddressIndex;

                // Return it as the frame.  
                return (StackSourceFrameIndex)(codeAddressIndex + (int)StackSourceFrameIndex.Start);
            }

            Debug.Assert(false, "Illegal Call Stack Index");
            return StackSourceFrameIndex.Invalid;
        }
        /// <summary>
        /// Implementation of StackSource protocol. 
        /// </summary>
        public override StackSourceCallStackIndex GetCallerIndex(StackSourceCallStackIndex callStackIndex)
        {
            Debug.Assert(callStackIndex >= 0);
            Debug.Assert(StackSourceCallStackIndex.Start == 0);         // If there are any cases before start, we need to handle them here. 

            int curIndex = (int)callStackIndex - (int)StackSourceCallStackIndex.Start;
            int nextIndex = (int)StackSourceCallStackIndex.Start;
            if (curIndex < m_log.CallStacks.Count)
            {
                var nextCallStackIndex = m_log.CallStacks.Caller((CallStackIndex)curIndex);
                if (nextCallStackIndex == CallStackIndex.Invalid)
                {
                    nextIndex += m_log.CallStacks.Count;    // Now points at the threads region.  
                    var threadIndex = m_log.CallStacks.ThreadIndex((CallStackIndex)curIndex);
                    nextIndex += (int)threadIndex;

                    if (!OnlyManagedCodeStacks && !ReasonableTopFrame(callStackIndex, threadIndex))
                    {
                        nextIndex += m_log.Threads.Count + m_log.Processes.Count;
                    }
                }
                else
                {
                    nextIndex += (int)nextCallStackIndex;
                }

                return (StackSourceCallStackIndex)nextIndex;
            }
            curIndex -= m_log.CallStacks.Count;                                 // Now is a thread index
            nextIndex += m_log.CallStacks.Count;                                // Output index points to the thread region.          

            if (curIndex < m_log.Threads.Count)
            {
                nextIndex += m_log.Threads.Count;                                  // Output index point to process region.
                nextIndex += (int)m_log.Threads[(ThreadIndex)curIndex].Process.ProcessIndex;
                return (StackSourceCallStackIndex)nextIndex;
            }
            curIndex -= m_log.Threads.Count;                                      // Now is a broken thread index

            if (curIndex < m_log.Processes.Count)
            {
                return StackSourceCallStackIndex.Invalid;                                   // Process has no parent
            }

            curIndex -= m_log.Processes.Count;                                    // Now is a broken thread index

            if (curIndex < m_log.Threads.Count)                                    // It is a broken stack
            {
                nextIndex += curIndex;                                                      // Indicate the real thread.  
                return (StackSourceCallStackIndex)nextIndex;
            }
            curIndex -= m_log.Threads.Count;                                       // Now it points at the one-element stacks. 

            if (curIndex < m_pseudoStacks.Count)
            {
                // Now points beginning of the broken stacks indexes.  
                nextIndex += m_log.Threads.Count + m_log.Processes.Count;

                // Pick the broken stack for this thread.  
                nextIndex += (int)m_pseudoStacks[curIndex].ThreadIndex;
                return (StackSourceCallStackIndex)nextIndex;
            }

            Debug.Assert(false, "Invalid CallStackIndex");
            return StackSourceCallStackIndex.Invalid;
        }
        /// <summary>
        /// Implementation of StackSource protocol. 
        /// </summary>
        public SourceLocation GetSourceLine(StackSourceFrameIndex frameIndex, SymbolReader reader)
        {
            uint codeAddressIndex = (uint)frameIndex - (uint)StackSourceFrameIndex.Start;
            if (codeAddressIndex >= m_log.CodeAddresses.Count)
            {
                return null;
            }

            return m_log.CodeAddresses.GetSourceLine(reader, (CodeAddressIndex)codeAddressIndex);
        }
        /// <summary>
        /// Implementation of StackSource protocol. 
        /// </summary>
        public override string GetFrameName(StackSourceFrameIndex frameIndex, bool verboseName)
        {
            string methodName = "?";
            var moduleFileIdx = ModuleFileIndex.Invalid;

            if (frameIndex < StackSourceFrameIndex.Start)
            {
                if (frameIndex == StackSourceFrameIndex.Broken)
                {
                    return "BROKEN";
                }
                else if (frameIndex == StackSourceFrameIndex.Overhead)
                {
                    return "OVERHEAD";
                }
                else if (frameIndex == StackSourceFrameIndex.Root)
                {
                    return "ROOT";
                }
                else
                {
                    return "?!?";
                }
            }
            int index = (int)frameIndex - (int)StackSourceFrameIndex.Start;
            if (index < m_log.CodeAddresses.Count)
            {
                var codeAddressIndex = (CodeAddressIndex)index;
                MethodIndex methodIndex = m_log.CallStacks.CodeAddresses.MethodIndex(codeAddressIndex);
                if (methodIndex != MethodIndex.Invalid)
                {
                    methodName = m_log.CodeAddresses.Methods.FullMethodName(methodIndex);
                }
                else
                {
                    if (ShowUnknownAddresses)
                    {
                        methodName = "0x" + m_log.CallStacks.CodeAddresses.Address(codeAddressIndex).ToString("x");
                    }
                }

                if (ShowOptimizationTiers)
                {
                    methodName =
                        TraceMethod.PrefixOptimizationTier(
                            methodName,
                            m_log.CodeAddresses.OptimizationTier(codeAddressIndex));
                }

                moduleFileIdx = m_log.CodeAddresses.ModuleFileIndex(codeAddressIndex);
            }
            else
            {
                index -= m_log.CodeAddresses.Count;
                if (index < m_log.Threads.Count)
                {
                    return m_log.Threads[(ThreadIndex)index].VerboseThreadName;
                }

                index -= m_log.Threads.Count;
                if (index < m_log.Processes.Count)
                {
                    TraceProcess process = m_log.Processes[(ProcessIndex)index];
                    string ptrSize = process.Is64Bit ? "64" : "32";
                    string cmdLine = process.CommandLine;
                    if (cmdLine.Length > 0)
                    {
                        // Remove the name of the EXE from the command line (thus just the args).  
                        int endExeNameIdx = -1;
                        if (cmdLine[0] == '"')
                        {
                            endExeNameIdx = cmdLine.IndexOf('"', 1);
                        }
                        else
                        {
                            endExeNameIdx = cmdLine.IndexOf(' ');
                        }

                        if (0 <= endExeNameIdx)
                        {
                            cmdLine = cmdLine.Substring(endExeNameIdx + 1, cmdLine.Length - endExeNameIdx - 1);
                        }
                    }
                    return "Process" + ptrSize + " " + process.Name + " (" + process.ProcessID + ") Args: " + cmdLine;
                }
                Debug.Assert(false, "Illegal Frame index");
                return "";
            }

            string moduleName = "?";
            if (moduleFileIdx != ModuleFileIndex.Invalid)
            {
                if (verboseName)
                {
                    moduleName = m_log.CodeAddresses.ModuleFiles[moduleFileIdx].FilePath;
                    if (moduleName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                        moduleName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        moduleName = moduleName.Substring(0, moduleName.Length - 4);        // Remove the .dll or .exe
                    }
                }
                else
                {
                    moduleName = m_log.CodeAddresses.ModuleFiles[moduleFileIdx].Name;
                }
            }

            return moduleName + "!" + methodName;
        }
        /// <summary>
        /// Implementation of StackSource protocol. 
        /// </summary>
        public override int CallStackIndexLimit
        {
            get
            {
                return (int)StackSourceCallStackIndex.Start + m_log.CallStacks.Count +
                    2 * m_log.Threads.Count + m_log.Processes.Count +     // *2 one for normal threads, one for broken threads. 
                    m_maxPseudoStack;                                     // These are for the samples with no ETW stacks but we have a codeaddress in the event.  
            }
        }
        /// <summary>
        /// Implementation of StackSource protocol. 
        /// </summary>
        public override int CallFrameIndexLimit
        {
            get
            {
                return (int)StackSourceFrameIndex.Start + m_log.CodeAddresses.Count + m_log.Threads.Count + m_log.Processes.Count;
            }
        }
        #endregion
        #region private
        /// <summary>
        /// Returns a list of modules for the stack 'stackIdx'.  It also updates the interning table stackModuleLists, so 
        /// that the entry cooresponding to stackIdx remembers the answer.  This can speed up processing a lot since many
        /// stacks have the same prefixes to root.  
        /// </summary>
        private ModuleList GetModulesForStack(ModuleList[] stackModuleLists, StackSourceCallStackIndex stackIdx)
        {
            var ret = stackModuleLists[(int)stackIdx];
            if (ret == null)
            {
                // ret = the module list for the rest of the frames. 
                var callerIdx = GetCallerIndex(stackIdx);
                if (callerIdx != StackSourceCallStackIndex.Invalid)
                {
                    ret = GetModulesForStack(stackModuleLists, callerIdx);
                }

                // Compute the module for the top most frame, and add it to the list (if we find a module)  
                TraceModuleFile module = null;
                var frameIdx = GetFrameIndex(stackIdx);
                if (frameIdx != StackSourceFrameIndex.Invalid)
                {
                    var codeAddress = GetFrameCodeAddress(frameIdx);
                    if (codeAddress != CodeAddressIndex.Invalid)
                    {
                        var moduleFileIdx = TraceLog.CallStacks.CodeAddresses.ModuleFileIndex(codeAddress);
                        if (moduleFileIdx != ModuleFileIndex.Invalid)
                        {
                            module = TraceLog.ModuleFiles[moduleFileIdx];
                            ret = ModuleList.SetAdd(module, ret);
                        }
                    }
                }
                stackModuleLists[(int)stackIdx] = ret;
            }
            return ret;
        }

        /// <summary>
        /// A ModuleList is a linked list of modules.  It is only used in GetModulesForStack and LookupWarmSymbols
        /// </summary>
        private class ModuleList
        {
            public static ModuleList SetAdd(TraceModuleFile module, ModuleList list)
            {
                if (!Member(module, list))
                {
                    return new ModuleList(module, list);
                }

                return list;
            }
            public static bool Member(TraceModuleFile module, ModuleList rest)
            {
                while (rest != null)
                {
                    if ((object)module == (object)rest.Module)
                    {
                        return true;
                    }

                    rest = rest.Next;
                }
                return false;
            }

            public ModuleList(TraceModuleFile module, ModuleList rest)
            {
                Module = module;
                Next = rest;
            }
            public TraceModuleFile Module;
            public ModuleList Next;
        }

        internal TraceEventStackSource(TraceLog log)
        {
            m_log = log;
            m_goodTopModuleIndex = ModuleFileIndex.Invalid;
            m_curSample = new StackSourceSample(this);
            m_curSample.Metric = log.SampleProfileInterval.TotalMilliseconds;
        }

        // Sometimes we just have a code address and thread, but no actual ETW stack.  Create a 'one element'
        // stack whose index is the index into the m_pseudoStacks array
        private struct PseudoStack : IEquatable<PseudoStack>
        {
            public PseudoStack(ThreadIndex threadIndex, CodeAddressIndex codeAddressIndex)
            {
                ThreadIndex = threadIndex; CodeAddressIndex = codeAddressIndex;
            }
            public ThreadIndex ThreadIndex;
            public CodeAddressIndex CodeAddressIndex;

            public override int GetHashCode() { return (int)CodeAddressIndex + ((int)ThreadIndex) * 0x10000; }
            public override bool Equals(object obj) { throw new NotImplementedException(); }
            public bool Equals(PseudoStack other) { return ThreadIndex == other.ThreadIndex && CodeAddressIndex == other.CodeAddressIndex; }
        };
        private GrowableArray<PseudoStack> m_pseudoStacks;
        private int m_maxPseudoStack;
        /// <summary>
        /// This maps pseudo-stacks to their index (thus it is the inverse of m_pseudoStack; 
        /// </summary>
        private Dictionary<PseudoStack, int> m_pseudoStacksTable;

        /// <summary>
        /// Given a thread and a call stack that does not have a stack, make up a pseudo stack for it consisting of the code address, 
        /// the broken node, the thread and process.   Will return -1 if it can't allocate another Pseudo-stack.
        /// </summary> 
        private int GetPseudoStack(ThreadIndex threadIndex, CodeAddressIndex codeAddrIndex)
        {
            if (m_pseudoStacksTable == null)
            {
                m_pseudoStacksTable = new Dictionary<PseudoStack, int>();
            }

            var pseudoStack = new PseudoStack(threadIndex, codeAddrIndex);
            int ret;
            if (m_pseudoStacksTable.TryGetValue(pseudoStack, out ret))
            {
                return ret;
            }

            ret = m_pseudoStacks.Count;
            if (ret >= m_maxPseudoStack)
            {
                return -1;
            }

            m_pseudoStacks.Add(pseudoStack);
            m_pseudoStacksTable.Add(pseudoStack, ret);
            return ret;
        }

        private StackSourceCallStackIndex GetStack(TraceEvent event_)
        {
            // Console.WriteLine("Getting Stack for sample at {0:f4}", sample.TimeStampRelativeMSec);
            int ret = (int)event_.CallStackIndex();
            if (ret == (int)CallStackIndex.Invalid)
            {
                var thread = event_.Thread();
                if (thread == null)
                {
                    return StackSourceCallStackIndex.Invalid;
                }

                // If the event is a sample profile, or page fault we can make a one element stack with the EIP in the event 
                CodeAddressIndex codeAddrIdx = CodeAddressIndex.Invalid;
                var asSampleProfile = event_ as SampledProfileTraceData;
                if (asSampleProfile != null)
                {
                    codeAddrIdx = asSampleProfile.IntructionPointerCodeAddressIndex();
                }
                else
                {
                    var asPageFault = event_ as MemoryHardFaultTraceData;
                    if (asPageFault != null)
                    {
                        codeAddrIdx = asSampleProfile.IntructionPointerCodeAddressIndex();
                    }
                }

                if (codeAddrIdx != CodeAddressIndex.Invalid)
                {
                    // Encode the code address for the given thread.  
                    int pseudoStackIndex = GetPseudoStack(thread.ThreadIndex, codeAddrIdx);
                    // Psuedostacks happen after all the others.  
                    if (0 <= pseudoStackIndex)
                    {
                        ret = m_log.CallStacks.Count + 2 * m_log.Threads.Count + m_log.Processes.Count + pseudoStackIndex;
                    }
                }

                // If we have run out of pseudo-stacks, we encode the stack as being at the thread.  
                if (ret == (int)CallStackIndex.Invalid)
                {
                    ret = m_log.CallStacks.Count + (int)thread.ThreadIndex;
                }
            }
            else
            {
                // We expect the thread we get when looking at the CallStack to match the thread of the event.  
                Debug.Assert(m_log.CallStacks.Thread((CallStackIndex)ret).ThreadID == event_.ThreadID);
                Debug.Assert(event_.Thread().Process.ProcessID == event_.ProcessID);
            }
            ret = ret + (int)StackSourceCallStackIndex.Start;
            return (StackSourceCallStackIndex)ret;
        }

        private bool ReasonableTopFrame(StackSourceCallStackIndex callStackIndex, ThreadIndex threadIndex)
        {
            uint index = (uint)callStackIndex - (uint)StackSourceCallStackIndex.Start;

            if (index < (uint)m_log.CallStacks.Count)
            {
                CodeAddressIndex codeAddressIndex = m_log.CallStacks.CodeAddressIndex((CallStackIndex)index);
                ModuleFileIndex moduleFileIndex = m_log.CallStacks.CodeAddresses.ModuleFileIndex(codeAddressIndex);
                if (m_goodTopModuleIndex == moduleFileIndex)        // optimization
                {
                    return true;
                }

                TraceModuleFile moduleFile = m_log.CallStacks.CodeAddresses.ModuleFile(codeAddressIndex);
                if (moduleFile == null)
                {
                    return false;
                }

                // We allow things that end in ntdll to be considered unbroken (TODO is this too strong?)
                if (moduleFile.FilePath.EndsWith("ntdll.dll", StringComparison.OrdinalIgnoreCase))
                {
                    m_goodTopModuleIndex = moduleFileIndex;
                    return true;
                }

                // The special processes 4 (System) and 0 (Kernel) can stay in the kernel without being broken.  
                if (moduleFile.FilePath.EndsWith("ntoskrnl.exe", StringComparison.OrdinalIgnoreCase))
                {
                    var processId = m_log.Threads[threadIndex].Process.ProcessID;
                    if (processId == 4 || processId == 0)
                    {
                        m_goodTopModuleIndex = moduleFileIndex;
                        return true;
                    }
                }
                return false;
            }
            return false;
        }

        private StackSourceSample m_curSample;
        private TraceEvents m_events;
        private ModuleFileIndex m_goodTopModuleIndex;       // This is a known good module index for a 'good' stack (probably ntDll!RtlUserStackStart
        internal /*protected*/ TraceLog m_log;
        #endregion
    }

    /// <summary>
    /// Like a TraceEventStackSource a MutableTraceEventStackSource allows you incorporate the stacks associated with
    /// a TraceEvent as a sample in the StackSource.   However in addition it allows you to create new frames for these
    /// stacks on the fly as well as add samples that did not exist in the original TraceEvent stream.   This gives you
    /// a lot of flexibility to add additional data to the original stream of TraceEvents.   
    /// 
    /// Like TraceEventStackSource MutableTraceEventStackSource supports the GetFrameCodeAddress() method that  allows
    /// you to map from the StackSourceFrameIndex back its TraceLog code address (that lets you get at the source code and
    /// line number for that frame).  
    /// </summary>
    public class MutableTraceEventStackSource : TraceEventStackSource
    {
        /// <summary>
        /// Create a new MutableTraceEventStackSource that can represent stacks coming from any events in the given TraceLog with a stack.  
        /// You use the 'AddSample' and 'DoneAddingSamples' to specify exactly which stacks you want in your source.   
        /// </summary>
        public MutableTraceEventStackSource(TraceLog log)
            : base(log)
        {
            m_Interner = new StackSourceInterner(5000, 1000, 100,
                (StackSourceFrameIndex)base.CallFrameIndexLimit, (StackSourceCallStackIndex)base.CallStackIndexLimit);
            m_emptyModuleIdx = m_Interner.ModuleIntern("");
            m_Interner.FrameNameLookup = GetFrameName;
        }
        /// <summary>
        /// After creating a MultableTraceEventStackSource, you add the samples you want using this AddSample API (you can reuse 'sample' 
        /// used as an argument to this routine.   It makes a copy.  The samples do NOT need to be added in time order (the MultableTraceEventStackSource
        /// will sort them).   When you done DoneAddingSamples must be called before using the
        /// the MutableTraceEventStackSource as a stack source.  
        /// </summary>
        public StackSourceSample AddSample(StackSourceSample sample)
        {
            var sampleCopy = new StackSourceSample(sample);
            sampleCopy.SampleIndex = (StackSourceSampleIndex)m_samples.Count;
            m_samples.Add(sampleCopy);
            if (sampleCopy.TimeRelativeMSec > m_sampleTimeRelativeMSecLimit)
            {
                m_sampleTimeRelativeMSecLimit = sampleCopy.TimeRelativeMSec;
            }

            return sampleCopy;
        }
        /// <summary>
        /// After calling 'AddSample' to add the samples that should belong to the source, DoneAddingSamples() should be called to
        /// to complete the construction of the stack source.   Only then can the reading API associated with the stack source be called. 
        /// </summary>
        public void DoneAddingSamples()
        {
            m_Interner.DoneInterning();

            // Insure that the samples are in sorted order.  
            m_samples.Sort(
                (x, y) =>
                {
                    int res = x.TimeRelativeMSec.CompareTo(y.TimeRelativeMSec);
                    if (res != 0)
                    {
                        return res;
                    }
                    else
                    {
                        return ((int)(x.StackIndex)).CompareTo((int)(y.StackIndex));
                    }
                });
            for (int i = 0; i < m_samples.Count; i++)
            {
                m_samples[i].SampleIndex = (StackSourceSampleIndex)i;
            }
        }

        /// <summary>
        /// The Interner is the class that allows you to make new indexes out of strings and other bits.  
        /// </summary>
        public StackSourceInterner Interner { get { return m_Interner; } }

        // methods for create stacks from TraceLog structures.  
        /// <summary>
        /// Returns a StackSourceCallStackIndex representing just one entry that represents the process 'process'
        /// </summary>
        public StackSourceCallStackIndex GetCallStackForProcess(TraceProcess process)
        {
            var idx = (int)StackSourceCallStackIndex.Start + m_log.CallStacks.Count + m_log.Threads.Count + (int)process.ProcessIndex;
            return (StackSourceCallStackIndex)idx;
        }
        /// <summary>
        /// Returns a StackSourceCallStackIndex representing just two entries that represent 'thread' which has a parent of its process.   
        /// </summary>
        public StackSourceCallStackIndex GetCallStackForThread(TraceThread thread)
        {
            var idx = (int)StackSourceCallStackIndex.Start + m_log.CallStacks.Count + (int)thread.ThreadIndex;
            return (StackSourceCallStackIndex)idx;
        }
        /// <summary>
        /// Returns a StackSourceCallStackIndex representing the call stack from the TraceLog represented by the CallStackIndex 'callStackIndex'.  
        /// If that stack is invalid, use 'thread' to at least return a call stack for the thread.  
        /// </summary>
        public StackSourceCallStackIndex GetCallStackThread(CallStackIndex callStackIndex, TraceThread thread)
        {
            if (callStackIndex == CallStackIndex.Invalid)
            {
                if (thread == null)
                {
                    return StackSourceCallStackIndex.Invalid;
                }

                return GetCallStackForThread(thread);
            }
            var idx = (int)StackSourceCallStackIndex.Start + (int)callStackIndex;
            return (StackSourceCallStackIndex)idx;
        }
        /// <summary>
        /// Returns a StackSourceCallStackIndex representing the call stack from the TraceLog represented by the CallStackIndex 'callStackIndex'.  
        /// Use the TraceEvent 'data' to find the stack if callStackIndex is invalid.  
        /// TODO data should be removed (or callstack derived from it) 
        /// </summary>  
        public StackSourceCallStackIndex GetCallStack(CallStackIndex callStackIndex, TraceEvent data)
        {
            if (callStackIndex == CallStackIndex.Invalid)
            {
                if (data == null)
                {
                    return StackSourceCallStackIndex.Invalid;
                }

                var thread = data.Thread();
                if (thread == null)
                {
                    return StackSourceCallStackIndex.Invalid;
                }

                return GetCallStackForThread(thread);
            }
            var idx = (int)StackSourceCallStackIndex.Start + (int)callStackIndex;
            return (StackSourceCallStackIndex)idx;
        }

        /// <summary>
        /// A very simple IDictionary-like interface for remembering values in GetCallStack()
        /// </summary>
        public interface CallStackMap
        {
            /// <summary>
            /// Fetches an value given a key
            /// </summary>
            StackSourceCallStackIndex Get(CallStackIndex key);
            /// <summary>
            /// Sets a key-value pair
            /// </summary>
            void Put(CallStackIndex key, StackSourceCallStackIndex value);
        }

        /// <summary>
        /// Find the StackSourceCallStackIndex for the TraceEvent call stack index 'callStackIndex' which has a top of its 
        /// stack (above the stack, where the thread and process would normally go) as 'top'.  If callStackMap is non-null 
        /// it is used as an interning table for CallStackIndex -> StackSourceCallStackIndex.  This can speed up the 
        /// transformation dramatically.   It will still work if it is null.  
        /// </summary>
        /// 
        public StackSourceCallStackIndex GetCallStack(CallStackIndex callStackIndex, StackSourceCallStackIndex top, CallStackMap callStackMap = null)
        {
            if (callStackIndex == CallStackIndex.Invalid)
            {
                return top;
            }

            StackSourceCallStackIndex cachedValue;
            if (callStackMap != null)
            {
                cachedValue = callStackMap.Get(callStackIndex);
                if (cachedValue != StackSourceCallStackIndex.Invalid)
                {
                    return cachedValue;
                }
            }

            var frameIdx = GetFrameIndex(m_log.CallStacks.CodeAddressIndex(callStackIndex));

            CallStackIndex nonInternedCallerIdx = m_log.CallStacks.Caller(callStackIndex);
            StackSourceCallStackIndex callerIdx;
            if (nonInternedCallerIdx == CallStackIndex.Invalid)
            {
                callerIdx = top;

                if (!OnlyManagedCodeStacks)
                {
                    var frameName = GetFrameName(frameIdx, false);
                    var bangIdx = frameName.IndexOf('!');
                    if (0 < bangIdx)
                    {
                        if (!(5 <= bangIdx && string.Compare(frameName, bangIdx - 5, "ntdll", 0, 5, StringComparison.OrdinalIgnoreCase) == 0))
                        {
                            var brokenFrame = m_Interner.FrameIntern("BROKEN", m_emptyModuleIdx);
                            callerIdx = m_Interner.CallStackIntern(brokenFrame, callerIdx);
                        }
                    }
                }
            }
            else
            {
                callerIdx = GetCallStack(nonInternedCallerIdx, top, callStackMap);
            }

            var ret = m_Interner.CallStackIntern(frameIdx, callerIdx);
            if (callStackMap != null)
            {
                callStackMap.Put(callStackIndex, ret);
            }

            return ret;
        }

        /// <summary>
        /// Create a frame name from a TraceLog code address.  
        /// </summary>
        public StackSourceFrameIndex GetFrameIndex(CodeAddressIndex codeAddressIndex)
        {
            return (StackSourceFrameIndex)((int)StackSourceFrameIndex.Start + (int)codeAddressIndex);
        }

        #region implementation of StackSource
        /// <summary>
        /// Implementation of StackSource protocol. 
        /// </summary>
        public override StackSourceCallStackIndex GetCallerIndex(StackSourceCallStackIndex callStackIndex)
        {
            if (m_Interner.CallStackStartIndex <= callStackIndex)
            {
                return m_Interner.GetCallerIndex(callStackIndex);
            }

            return base.GetCallerIndex(callStackIndex);
        }
        /// <summary>
        /// Implementation of StackSource protocol. 
        /// </summary>
        public override StackSourceFrameIndex GetFrameIndex(StackSourceCallStackIndex callStackIndex)
        {
            if (m_Interner.CallStackStartIndex <= callStackIndex)
            {
                return m_Interner.GetFrameIndex(callStackIndex);
            }

            return base.GetFrameIndex(callStackIndex);
        }
        /// <summary>
        /// Implementation of StackSource protocol. 
        /// </summary>
        public StackSourceModuleIndex GetModuleIndex(StackSourceFrameIndex frameIndex)
        {
            if (m_Interner.FrameStartIndex <= frameIndex)
            {
                return m_Interner.GetModuleIndex(frameIndex);
            }

            return StackSourceModuleIndex.Invalid;      // TODO FIX NOW this is a poor approximation
        }
        /// <summary>
        /// Implementation of StackSource protocol. 
        /// </summary>
        public override string GetFrameName(StackSourceFrameIndex frameIndex, bool fullModulePath)
        {
            if (frameIndex >= (StackSourceFrameIndex)base.CallFrameIndexLimit)
            {
                return m_Interner.GetFrameName(frameIndex, fullModulePath);
            }

            return base.GetFrameName(frameIndex, fullModulePath);
        }
        /// <summary>
        /// Implementation of StackSource protocol. 
        /// </summary>
        public override StackSourceSample GetSampleByIndex(StackSourceSampleIndex sampleIndex)
        {
            return m_samples[(int)sampleIndex];
        }
        /// <summary>
        /// Implementation of StackSource protocol. 
        /// </summary>
        public override int SampleIndexLimit { get { return m_samples.Count; } }
        /// <summary>
        /// Implementation of StackSource protocol. 
        /// </summary>
        public override void ForEach(Action<StackSourceSample> callback)
        {
            for (int i = 0; i < m_samples.Count; i++)
            {
                callback(m_samples[i]);
            }
        }
        /// <summary>
        /// Implementation of StackSource protocol. 
        /// </summary>
        public override bool SamplesImmutable { get { return true; } }
        /// <summary>
        /// Implementation of StackSource protocol. 
        /// </summary>
        public override int CallFrameIndexLimit { get { return base.CallFrameIndexLimit + m_Interner.FrameCount; } }
        /// <summary>
        /// Implementation of StackSource protocol. 
        /// </summary>
        public override int CallStackIndexLimit { get { return base.CallStackIndexLimit + m_Interner.CallStackCount; } }
        /// <summary>
        /// Implementation of StackSource protocol. 
        /// </summary>
        public override double SampleTimeRelativeMSecLimit { get { return m_sampleTimeRelativeMSecLimit; } }
        #endregion

        #region private
        /// <summary>
        /// private
        /// </summary>
        protected StackSourceInterner m_Interner;

        /// <summary>
        /// private
        /// </summary>
        protected StackSourceModuleIndex m_emptyModuleIdx;
        internal GrowableArray<StackSourceSample> m_samples;
        private double m_sampleTimeRelativeMSecLimit;
        #endregion
    }
}

