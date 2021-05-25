using System;
using System.Collections.Generic;
using System.IO;

namespace ETLStackBrowse
{
    public class TreeNode
    {
        public int id;
        public int inclusive;
        public int exclusive;
        public ulong weight;
        public UInt32 mask;
        public long timeFirst;
        public long timeLast;
        public List<TreeNode> children = new List<TreeNode>();
    }

    public partial class ETLTrace
    {
        public class StackResult
        {
            public IStackParameters parms;

            public long t0 = -1;
            public long t1 = -1;
            public int cStitchedStacks = 0;
            public TreeNode treeRoot = null;
            public ByteAtomTable atomsNodeNames = null;
            public List<TreeNode> rollupStats = null;
            public int idPivot = -1;
            public bool[] stackFilters = null;
        }

        public string ComputeStacks()
        {
            StackResult result = ComputeStacksRaw();
            return FormatStacksResult(result);
        }

        public string FormatStacksResult(StackResult result)
        {
            var dumper = new TreeDumper();
            return dumper.DumpStacks(this, result);
        }

        public StackResult ComputeStacksRaw()
        {
            IStackParameters parms = Parameters.StackParameters;
            var computer = new TreeComputer();
            return computer.ComputeStacks(this, parms);
        }

        public StackResult StackStream(Action<Frame, TreeComputer, long, ulong> callback)
        {
            IStackParameters parms = Parameters.StackParameters;
            var computer = new TreeComputer();
            computer.callback = callback;
            return computer.ComputeStacks(this, parms);        // This will call back on each sample. 
        }

        public class Frame
        {
            public Frame next;
            public int id;
        }

        public class TreeComputer
        {
            private struct FrameState
            {
                public Frame root;
                public long time;
                public int threadId;
                public ulong weight;
                public int filenameId;
                public int eventId;
            }

            private enum FrameFilterType
            {
                NonePresent = 0,
                AllPresent = 1,
                AnyPresent = 2,
            }

            private const int fldStackThreadId = 2;  // the index of the threadid field in stack records (fixed)
            private const int fldStackSymbol = 5;   // the index of the symbol in stack records (fixed)

            private byte[][] byThreadDesc = null;

            internal Action<Frame, TreeComputer, long, ulong> callback = null;
            private int cStitchedStacks = 0;
            public ByteAtomTable atomsNodeNames = new ByteAtomTable();
            private List<TreeNode> rollupStats = new List<TreeNode>();
            private TreeNode treeRoot;
            private Dictionary<int, List<int>> lastKnownThreadFrames = new Dictionary<int, List<int>>();
            private List<FrameFilterType> filterRequiredState = new List<FrameFilterType>();
            private FrameState[] frameStates = null;
            private IStackParameters parms = null;
            private ByteAtomTable atomsRecords = null;
            private ByteAtomTable atomsFields = null;
            private RecordInfo[] recordInfo;
            private List<List<int>> listEventFields = null;
            private List<ThreadInfo> threads = null;
            private bool[] stackIgnoreEvents = null;
            private ETLTrace trace = null;
            public Dictionary<string, string> fullModuleNames = new Dictionary<string, string>();  // Maps short module names to full path

            private int idPivot;
            private long t0, t1;
            private bool fUseExeFrame = true;
            private bool fUsePid = true;
            private bool fUseTid = true;
            private bool fFoldModules = false;
            private bool fUseRootAI = false;
            private bool fUseIODuration = false;
            private bool fReserved = true;
            private bool fUnmangleBartok = false;
            private bool fElideGenerics = false;
            private string frameFilters = "";
            private string butterflyPivot = "";

            // some interesting record id's
            private int idFileIoOpEnd, idDiskRead, idDiskReadInit, idDiskWrite, idDiskWriteInit;
            private int idDPC, idStack, idVAlloc, idVFree, idCSwitch, idIStart, idIDCStart, idIEnd;
            private ByteWindow bsym = new ByteWindow();
            private int[] backpatchRecordType = null;

            public StackResult ComputeStacks(ETLTrace trace, IStackParameters parms)
            {
                this.parms = parms;
                this.trace = trace;

                t0 = trace.Parameters.T0;
                t1 = trace.Parameters.T1;

                if (t0 == t1)
                {
                    t1 = t0 + 1; // just add 1ms to get a non-zero window
                }

                atomsRecords = trace.atomsRecords;
                atomsFields = trace.atomsFields;
                recordInfo = trace.CommonFieldIds;
                listEventFields = trace.EventFields;
                threads = trace.Threads;
                stackIgnoreEvents = trace.StackIgnoreEvents;
                fElideGenerics = trace.UIParameters.ElideGenerics;
                fUnmangleBartok = trace.UIParameters.UnmangleBartokSymbols;

                frameFilters = parms.FrameFilters;
                butterflyPivot = parms.ButterflyPivot;
                fUseExeFrame = parms.UseExeFrame;
                fUsePid = parms.UsePid;
                fUseTid = parms.UseTid;
                fFoldModules = parms.FoldModules;
                fUseRootAI = parms.UseRootAI;
                fUseIODuration = parms.UseIODuration;
                fReserved = parms.AnalyzeReservedMemory;

                backpatchRecordType = new int[atomsRecords.Count];

                MemProcessor memProcessor = new MemProcessor(trace);
                MemEffect memEffect = new MemEffect();

                ParseFrameFilters();

                PrepareNewTreeRoot();

                idDPC = atomsRecords.Lookup("DPC");
                idStack = atomsRecords.Lookup("Stack");
                idVAlloc = atomsRecords.Lookup("VirtualAlloc");
                idVFree = atomsRecords.Lookup("VirtualFree");
                idCSwitch = atomsRecords.Lookup("CSwitch");

                idFileIoOpEnd = atomsRecords.Lookup("FileIoOpEnd");
                idDiskRead = atomsRecords.Lookup("DiskRead");
                idDiskReadInit = atomsRecords.Lookup("DiskReadInit");
                idDiskWrite = atomsRecords.Lookup("DiskWrite");
                idDiskWriteInit = atomsRecords.Lookup("DiskWriteInit");

                idIStart = atomsRecords.Lookup("I-Start");
                idIDCStart = atomsRecords.Lookup("I-DCStart");
                idIEnd = atomsRecords.Lookup("I-End");

                // we're tracking previous context switch events for each thread 
                // so we can use the delay as a cost metric
                ThreadStat[] stats = trace.NewThreadStats();

                byThreadDesc = new byte[threads.Count][];

                for (int i = 0; i < threads.Count; i++)
                {
                    ThreadInfo ti = threads[i];
                    byThreadDesc[i] = ByteWindow.MakeBytes(String.Format("tid ({0,5})", ti.threadid));
                }

                for (int i = 0; i < stats.Length; i++)
                {
                    stats[i].time = t0;
                }

                // we keep previous events in the event stream so that
                // we can associate a stack with one of those events
                // the stacks usually come right after the event
                // but there could me intervening events
                // and those might have stacks as well
                // the stack has to match the thread and the timestamp
                // of the previous event for it to be associated with 
                // that event

                const int maxEvent = 16;
                PreviousEvent[] prev = new PreviousEvent[maxEvent];
                int iNextEvent = 0;

                // we keep a goodly number of previous stacks so that we can resolve pending I/O's from the past
                // there is no great magic number here but we want enough for the number of CPUs and plenty
                // for pending IOs.  In the event that we have not figured out the number of CPU's 
                // the IO suggested size is probably enough anyway

                int maxPendingStacks = Math.Max(trace.MaxCPU, 32); // should be enough for a large number of outstanding IOs

                frameStates = new FrameState[maxPendingStacks];
                int victim = 0;

                for (int i = 0; i < maxPendingStacks; i++)
                {
                    frameStates[i].threadId = -1;
                }

                bool[] threadFilters = trace.Parameters.GetThreadFilters();
                bool[] filters = ComputeStackEventFilters(trace);

                bool fIsMemAnalysis = ((idVAlloc >= 0 && filters[idVAlloc]) || (idVFree >= 0 && filters[idVFree]));

                IdentifyRecordsToBackpatch(filters);

                ETWLineReader l = trace.StandardLineReader();
                foreach (ByteWindow b in l.Lines())
                {
                    // if this is an event that we don't recognized then skip it
                    if (l.idType < 0)
                    {
                        continue;
                    }

                    // if it's not a stack event then this might be an event that is introducing a stack
                    // so record the event and the time in the circular buffer in that case
                    if (l.idType != idStack)
                    {
                        if (stackIgnoreEvents[l.idType])
                        {
                            continue;
                        }

                        if (l.idType == idIStart || l.idType == idIDCStart)
                        {
                            // TODO This is a hack.  We try to determine the full path of a module name by
                            // looking what modules are loaded.  Currently machine wide, could make process wide
                            // pretty easily. 
                            var filePathBytes = new ByteWindow(b, fldImageFileName);
                            filePathBytes.Trim();
                            filePathBytes.ib++;
                            filePathBytes.len -= 2;
                            if (filePathBytes.len > 0)
                            {
                                var filePath = filePathBytes.GetString();
                                var index = filePath.LastIndexOf('\\');
                                if (index >= 0)
                                {
                                    var fileName = filePath.Substring(index + 1);
                                    fullModuleNames[fileName] = filePath;
                                }
                            }
                        }

                        // this is where we use DiskRead, DiskWrite, and FileIoOpEnd events to patch the past
                        if (TryChangeWeightOfPastEvent(l, b))
                        {
                            continue;
                        }

                        prev[iNextEvent].time = l.t;

                        if (!l.MatchingTextFilter() || !l.MatchingMemory())
                        {
                            prev[iNextEvent].eventId = -1;
                            prev[iNextEvent].tid = -1;
                            continue;
                        }
                        else
                        {
                            prev[iNextEvent].eventId = l.idType;

                            int iThreadField = recordInfo[l.idType].threadField;
                            if (iThreadField >= 0)
                            {
                                // we know a specific thread that generated this stack, require the match
                                prev[iNextEvent].tid = b.GetInt(iThreadField);
                            }
                            else
                            {
                                // -1 matches anything, unknown thread triggered the stack event
                                prev[iNextEvent].tid = -1;
                            }

                            prev[iNextEvent].weight = 0;

                            if (l.idType == idCSwitch)
                            {
                                int oldTid = b.GetInt(fldCSwitchOldTID);
                                int idxOld = trace.FindThreadInfoIndex(l.t, oldTid);
                                stats[idxOld].time = l.t;

                                int newTid = b.GetInt(fldCSwitchNewTID);
                                int idxNew = trace.FindThreadInfoIndex(l.t, newTid);

                                ulong waitTime = (ulong)(l.t - stats[idxNew].time);

                                prev[iNextEvent].weight = waitTime;
                                prev[iNextEvent].tid = newTid;
                            }
                            else if (fIsMemAnalysis && (l.idType == idVAlloc || l.idType == idVFree))
                            {
                                memProcessor.ProcessMemRecord(l, b, memEffect);

                                ulong size = 0;

                                if (l.idType == idVAlloc)
                                {
                                    if (fReserved)
                                    {
                                        size = memEffect.reserved;
                                    }
                                    else
                                    {
                                        size = memEffect.committed;
                                    }
                                }
                                else
                                {
                                    if (fReserved)
                                    {
                                        size = memEffect.released;
                                    }
                                    else
                                    {
                                        size = memEffect.decommitted;
                                    }
                                }

                                // this allocation didn't affect the statistic of interest... ignore it
                                if (size == 0)
                                {
                                    prev[iNextEvent].eventId = -1;
                                    prev[iNextEvent].tid = -1;
                                    continue;
                                }
                                else
                                {
                                    prev[iNextEvent].weight = size;
                                }
                            }
                            else if (l.idType == idIStart || l.idType == idIEnd)
                            {
                                ulong addrBase = b.GetHex(fldImageBaseAddr);
                                ulong addrEnd = b.GetHex(fldImageEndAddr);
                                ulong size = addrEnd - addrBase;

                                prev[iNextEvent].weight = size;
                            }
                            else if (recordInfo[l.idType].sizeField > 0)
                            {
                                // get IO Size if appropriate
                                if (!fUseIODuration)
                                {
                                    prev[iNextEvent].weight = (ulong)b.GetLong(recordInfo[l.idType].sizeField);
                                }
                            }

                            // add the synthetic filename field if there is one in the leaf record type
                            if (filters[l.idType] && recordInfo[l.idType].goodNameField >= 0)
                            {
                                bsym.Assign(b, recordInfo[l.idType].goodNameField).Trim();
                                prev[iNextEvent].filenameId = atomsNodeNames.EnsureContains(bsym);
                            }
                            else
                            {
                                prev[iNextEvent].filenameId = -1;
                            }
                        }

                        iNextEvent = (iNextEvent + 1) % maxEvent;
                        continue;
                    }

                    // ok at this point we definitely have a stack event

                    // make sure this stack is for a thread we are measuring

                    int threadId = b.GetInt(fldStackThreadId);

                    int idx = trace.FindThreadInfoIndex(l.t, threadId);

                    if (!threadFilters[idx])
                    {
                        continue;
                    }

                    // we care about this thread, look up the state this thread is in
                    // do we have a pending stack already

                    int iStack = 0;
                    for (iStack = 0; iStack < maxPendingStacks; iStack++)
                    {
                        if (frameStates[iStack].threadId == threadId && frameStates[iStack].time == l.t)
                        {
                            break;
                        }
                    }

                    // this is a new stack, not a continuation, so we may have to flush
                    if (iStack == maxPendingStacks)
                    {
                        // check to see if this new stack is of the correct type before we flush one

                        // first we look backwards to see if we can find the event that introduced this stack
                        bool fFound = true;

                        int i = iNextEvent;
                        for (; ; )
                        {
                            if (--i < 0)
                            {
                                i = maxEvent - 1;
                            }

                            // if the time matches and this is a desired event type then many we can use it
                            if (prev[i].time == l.t && prev[i].eventId >= 0 && filters[prev[i].eventId])
                            {
                                // the previous event has to be non-thread-specific (like VirtualAlloc)
                                // or else the thread has to match (for context switches)
                                if (prev[i].tid == -1)
                                {
                                    break;
                                }

                                if (prev[i].tid == threadId)
                                {
                                    break;
                                }
                            }

                            if (i == iNextEvent)
                            {
                                fFound = false;
                                break;
                            }
                        }

                        // if we don't have a previous record that corresonds to this stack
                        // or if we have a record but it is not one that we are collecting stacks for
                        // then we skip this stack entirely

                        if (!fFound)
                        {
                            continue;
                        }

                        victim++;
                        if (victim == maxPendingStacks)
                        {
                            victim = 0;
                        }

                        // ok we're going with this stack, so we have to flush a stack we've been building up (it's done by now)
                        // because only one stack can be pending for any given CPU at any time index

                        if (frameStates[victim].root != null)
                        {
                            ProcessFrames(treeRoot, victim);
                        }

                        frameStates[victim].root = null;
                        frameStates[victim].threadId = threadId;
                        frameStates[victim].time = l.t;
                        frameStates[victim].weight = prev[i].weight;
                        frameStates[victim].filenameId = prev[i].filenameId;
                        frameStates[victim].eventId = prev[i].eventId;

                        // this event is consumed, we can't use it twice even if another stack otherwise might seem to match
                        prev[i].eventId = -1;

                        iStack = victim;
                    }

                    bsym.Assign(b, fldStackSymbol).Trim();

                    if (fElideGenerics || fUnmangleBartok)
                    {
                        PostProcessSymbol(bsym);
                    }

                    if (fFoldModules)
                    {
                        bsym.Truncate((byte)'!');
                    }

                    int id = atomsNodeNames.EnsureContains(bsym);

                    if (!fFoldModules || frameStates[iStack].root == null || frameStates[iStack].root.id != id)
                    {
                        Frame f = new Frame();
                        f.next = frameStates[iStack].root;
                        f.id = id;
                        frameStates[iStack].root = f;
                    }
                }

                // flush pending frames
                for (int i = 0; i < maxPendingStacks; i++)
                {
                    if (frameStates[i].root != null)
                    {
                        ProcessFrames(treeRoot, i);
                    }
                }

                frameStates = null;

                StackResult result = new StackResult();

                result.parms = parms;
                result.t0 = t0;
                result.t1 = t1;
                result.cStitchedStacks = cStitchedStacks;
                result.treeRoot = treeRoot;
                result.atomsNodeNames = atomsNodeNames;
                result.rollupStats = rollupStats;
                result.idPivot = idPivot;
                result.stackFilters = filters;

                return result;
            }

            private static byte[] unescaped = new byte[40960];

            private void PostProcessSymbol(ByteWindow bsym)
            {
                int len = bsym.len;
                byte[] buf = bsym.buffer;

                int ibStart = bsym.ib;
                int ibStop = ibStart + bsym.len;
                int c2 = 0;

                int skip = 0;

                for (int ib = ibStart; ib < ibStop; ib++)
                {
                    byte by = buf[ib];


                    if (fUnmangleBartok && by == (byte)'$')
                    {
                        switch ((char)buf[ib + 1])
                        {
                            case 'L':
                                if (skip == 0)
                                {
                                    unescaped[c2++] = (byte)'<';
                                }

                                if (fElideGenerics)
                                {
                                    skip++;
                                }

                                ib++;
                                break;

                            case 'G':
                                if (fElideGenerics)
                                {
                                    skip--;
                                }

                                ib++;
                                if (skip == 0)
                                {
                                    unescaped[c2++] = (byte)'>';
                                }

                                break;

                            case 'S':
                                ib++;
                                if (skip == 0)
                                {
                                    unescaped[c2++] = (byte)'-';
                                }

                                break;

                            case '_':
                                ib++;
                                if (skip == 0)
                                {
                                    unescaped[c2++] = (byte)'_';
                                }

                                break;

                            case '0':
                                ib++;
                                int t = 0;
                                for (int i = 0; i < 3; i++)
                                {
                                    by = buf[++ib];
                                    t = t * 16;

                                    if (by >= '0' && by <= '9')
                                    {
                                        t += by - '0';
                                    }
                                    else if (by >= 'a' && by <= 'f')
                                    {
                                        t += by - 'a' + 10;
                                    }
                                    else if (by >= 'A' && by <= 'F')
                                    {
                                        t += by - 'A' + 10;
                                    }
                                }
                                if (skip == 0)
                                {
                                    unescaped[c2++] = (byte)t;
                                }

                                break;

                            case 'A':
                                while (buf[++ib] != ':')
                                {
                                    continue;
                                }

                                ib++;
                                break;

                            default:
                                ib++;
                                break;
                        }
                        continue;
                    }

                    if (by == (byte)'>' && fElideGenerics)
                    {
                        skip--;
                    }

                    if (skip == 0)
                    {
                        unescaped[c2++] = by;
                    }

                    if (by == (byte)'<' && fElideGenerics)
                    {
                        skip++;
                    }
                }

                bsym.buffer = unescaped;
                bsym.ib = 0;
                bsym.len = c2;
            }

            private bool[] ComputeStackEventFilters(ETLTrace trace)
            {
                bool[] filters = trace.Parameters.StackFilters.GetFilters();

                // you really want stacks from the init events, the other ones are bogus
                if (idDiskRead >= 0 && idDiskReadInit >= 0)
                {
                    if (filters[idDiskRead] || filters[idDiskReadInit])
                    {
                        filters[idDiskRead] = true;
                        filters[idDiskReadInit] = true;
                    }
                }

                // you really want stacks from the init events, the other ones are bogus
                if (idDiskWrite >= 0 && idDiskWriteInit >= 0)
                {
                    if (filters[idDiskWrite] || filters[idDiskWriteInit])
                    {
                        filters[idDiskWrite] = true;
                        filters[idDiskWriteInit] = true;
                    }
                }
                return filters;
            }

            private static bool ByteArrayStartsWith(byte[] main, byte[] prefix)
            {
                if (main.Length < prefix.Length)
                {
                    return false;
                }

                for (int i = 0; i < prefix.Length; i++)
                {
                    if (main[i] != prefix[i])
                    {
                        return false;
                    }
                }

                return true;
            }

            private void IdentifyRecordsToBackpatch(bool[] filters)
            {
                // candidate for backpatching are the fileio related records
                // as well as the diskinit records but we never try to backpatch
                // on any record that we have not selected for analysis

                // we are willing to try to backpatch any FileIo* record
                byte[] byFileIo = ByteWindow.MakeBytes("FileIo");

                for (int i = 0; i < atomsRecords.Count; i++)
                {
                    backpatchRecordType[i] = -1;

                    // don't backpatch anything that we aren't analyzing
                    if (!filters[i])
                    {
                        continue;
                    }

                    byte[] byRecordName = atomsRecords.GetBytes(i);

                    if (ByteArrayStartsWith(byRecordName, byFileIo))
                    {
                        backpatchRecordType[i] = idFileIoOpEnd;
                    }
                }

                if (idDiskReadInit >= 0 && filters[idDiskReadInit])
                {
                    backpatchRecordType[idDiskReadInit] = idDiskRead;
                }

                if (idDiskWriteInit >= 0 && filters[idDiskWriteInit])
                {
                    backpatchRecordType[idDiskWriteInit] = idDiskWrite;
                }
            }

            private bool TryChangeWeightOfPastEvent(ETWLineReader l, ByteWindow b)
            {
                // if the init events are present then diskread and diskwrite are backpatch cost processed
                // in the old format the stack was associated with the read/write directly
                // new new format logs the stack when requested, which helps with async I/O I guess
                // and other things like that... but there are two events in the newer traces
                // that have to be consolidated.  We used to only do this for FileIOOpEnd
                //
                if ((l.idType == idFileIoOpEnd && fUseIODuration) ||
                     (l.idType == idDiskRead && idDiskReadInit >= 0) ||
                     (l.idType == idDiskWrite && idDiskWriteInit >= 0))
                {
                    // overwrite the weight of a pending stack with the relevant cost instead 
                    int tid = b.GetInt(recordInfo[l.idType].threadField);
                    long elapsed = b.GetLong(recordInfo[l.idType].elapsedTimeField);
                    long time = l.t - elapsed;
                    long cost = elapsed;

                    if (!fUseIODuration)
                    {
                        cost = b.GetLong(recordInfo[l.idType].sizeField);
                    }

                    for (int i = 0; i < frameStates.Length; i++)
                    {
                        if (frameStates[i].threadId == tid &&
                            backpatchRecordType[frameStates[i].eventId] == l.idType &&
                            System.Math.Abs(frameStates[i].time - time) < 5)
                        {
                            frameStates[i].weight = (ulong)cost;

                            // try to get the correct filename while we are here if we don't already have it
                            if (recordInfo[l.idType].goodNameField >= 0 && frameStates[i].filenameId == -1)
                            {
                                bsym.Assign(b, recordInfo[l.idType].goodNameField).Trim();
                                frameStates[i].filenameId = atomsNodeNames.EnsureContains(bsym);
                            }

                            return true;
                        }
                    }
                }

                return false;
            }

            private void PrepareNewTreeRoot()
            {
                treeRoot = new TreeNode();

                treeRoot.timeFirst = Int64.MaxValue;

                if (butterflyPivot.Length > 0)
                {
                    idPivot = atomsNodeNames.EnsureContains(new ByteWindow(butterflyPivot));
                    treeRoot.id = atomsNodeNames.EnsureContains(new ByteWindow(butterflyPivot + " Butterfly"));

                    TreeNode treeCallers = new TreeNode();
                    treeCallers.timeFirst = Int64.MaxValue;
                    treeCallers.id = atomsNodeNames.EnsureContains(new ByteWindow(butterflyPivot + " Callers"));
                    treeRoot.children.Add(treeCallers);

                    TreeNode treeCalls = new TreeNode();
                    treeCalls.timeFirst = Int64.MaxValue;
                    treeCalls.id = atomsNodeNames.EnsureContains(new ByteWindow(butterflyPivot + " Calls"));
                    treeRoot.children.Add(treeCalls);
                }
                else
                {
                    idPivot = -1;
                    treeRoot.id = atomsNodeNames.EnsureContains(new ByteWindow("Root"));
                }
            }

            private void ParseFrameFilters()
            {

                if (frameFilters == null)
                {
                    frameFilters = "";
                }

                if (butterflyPivot.Length > 0)
                {
                    frameFilters = "+" + butterflyPivot + "\r\n" + frameFilters;
                }

                StringReader sr = new StringReader(frameFilters);

                string line = null;

                while ((line = sr.ReadLine()) != null)
                {
                    string sym;

                    line = line.Trim();
                    if (line.StartsWith("+"))
                    {
                        sym = line.Substring(1);
                        ByteWindow by = new ByteWindow(sym);

                        if (atomsNodeNames.Lookup(by) >= 0)
                        {
                            continue;
                        }

                        atomsNodeNames.EnsureContains(by);
                        filterRequiredState.Add(FrameFilterType.AllPresent);

                    }
                    else if (line.StartsWith("-"))
                    {
                        sym = line.Substring(1);
                        ByteWindow by = new ByteWindow(sym);

                        if (atomsNodeNames.Lookup(by) >= 0)
                        {
                            continue;
                        }

                        atomsNodeNames.EnsureContains(by);
                        filterRequiredState.Add(FrameFilterType.NonePresent);
                    }
                    else if (line.StartsWith("|"))
                    {
                        sym = line.Substring(1);
                        ByteWindow by = new ByteWindow(sym);

                        if (atomsNodeNames.Lookup(by) >= 0)
                        {
                            continue;
                        }

                        atomsNodeNames.EnsureContains(by);
                        filterRequiredState.Add(FrameFilterType.AnyPresent);
                    }
                }
            }

            private void ProcessFrames(TreeNode treeRoot, int iFrames)
            {
                Frame frameRoot = frameStates[iFrames].root;
                long time = frameStates[iFrames].time;
                int threadId = frameStates[iFrames].threadId;
                ulong weight = frameStates[iFrames].weight;
                int filenameId = frameStates[iFrames].filenameId;

                int idx = trace.FindThreadInfoIndex(time, threadId);

                if (fUseRootAI)
                {
                    List<int> lastKnownFrames = null;

                    if (!lastKnownThreadFrames.ContainsKey(idx))
                    {
                        lastKnownFrames = new List<int>();
                        Frame fr = frameRoot;
                        while (fr != null)
                        {
                            lastKnownFrames.Add(fr.id);
                            fr = fr.next;
                        }

                        lastKnownThreadFrames.Add(idx, lastKnownFrames);
                    }
                    else
                    {
                        int cFramesOriginal = 0;
                        Frame frameRootOriginal = frameRoot;
                        while (frameRootOriginal != null)
                        {
                            cFramesOriginal++;
                            frameRootOriginal = frameRootOriginal.next;
                        }

                        frameRootOriginal = frameRoot;

                        // this isn't a very deep stack, so it didn't get trimmed
                        // no AI is needed and furthermore we probably don't want to keep
                        // it because it might represent a DPC or something like that

                        if (cFramesOriginal >= 20)
                        {
                            lastKnownFrames = lastKnownThreadFrames[idx];

                            int i = 0;
                            for (i = 0; i < lastKnownFrames.Count; i++)
                            {
                                if (lastKnownFrames[i] == frameRoot.id)
                                {
                                    break;
                                }
                            }

                            if (i < lastKnownFrames.Count)
                            {
                                if (i > 0)
                                {
                                    for (int j = i - 1; j >= 0; j--)
                                    {
                                        Frame frAdd = new Frame();
                                        frAdd.next = frameRoot;
                                        frAdd.id = lastKnownFrames[j];
                                        frameRoot = frAdd;
                                    }

                                    cStitchedStacks++;
                                }

                            }
                            else
                            {
                                i = 0; // we're keeping nothing, we have at least 20 good new frames
                            }

                            lastKnownFrames.RemoveRange(i, lastKnownFrames.Count - i);
                            Frame fr = frameRootOriginal;
                            while (fr != null)
                            {
                                lastKnownFrames.Add(fr.id);
                                fr = fr.next;
                            }
                        }
                    }
                }

                if (fUseExeFrame || fUseTid)
                {
                    ThreadInfo ti = threads[idx];

                    if (fUseTid)
                    {
                        int id = atomsNodeNames.EnsureContains(byThreadDesc[idx]);
                        Frame f = new Frame();
                        f.next = frameRoot;
                        f.id = id;
                        frameRoot = f;
                    }

                    if (fUseExeFrame && fUsePid)
                    {
                        int id = atomsNodeNames.EnsureContains(ti.processPid);
                        Frame f = new Frame();
                        f.next = frameRoot;
                        f.id = id;
                        frameRoot = f;
                    }

                    if (fUseExeFrame && !fUsePid)
                    {
                        int id = atomsNodeNames.EnsureContains(ti.processNopid);
                        Frame f = new Frame();
                        f.next = frameRoot;
                        f.id = id;
                        frameRoot = f;
                    }
                }

                if (filenameId >= 0 && frameRoot != null)
                {
                    Frame fLast = frameRoot;
                    while (fLast.next != null)
                    {
                        fLast = fLast.next;
                    }

                    Frame f = new Frame();
                    fLast.next = f;
                    f.id = filenameId;
                }

                if (filterRequiredState.Count > 0)
                {
                    bool[] found = new bool[filterRequiredState.Count];

                    Frame fr = frameRoot;
                    while (fr != null)
                    {
                        if (fr.id < found.Length)
                        {
                            found[fr.id] = true;
                        }

                        fr = fr.next;
                    }

                    bool anyRequired = false;
                    bool anyPresent = false;

                    for (int i = 0; i < found.Length; i++)
                    {
                        switch (filterRequiredState[i])
                        {
                            case FrameFilterType.NonePresent:
                                if (found[i])
                                {
                                    return;
                                }

                                break;

                            case FrameFilterType.AllPresent:
                                if (!found[i])
                                {
                                    return;
                                }

                                break;

                            case FrameFilterType.AnyPresent:
                                anyRequired = true;
                                if (found[i])
                                {
                                    anyPresent = true;
                                }

                                break;
                        }
                    }

                    if (anyRequired != anyPresent)
                    {
                        return;
                    }
                }

                // If all we are asked do to is get a stream of samples, then send it out and we are done.  
                if (callback != null)
                {
                    callback(frameRoot, this, time, weight);
                    return;
                }

                while (rollupStats.Count < atomsNodeNames.Count)
                {
                    TreeNode node = new TreeNode();
                    node.id = rollupStats.Count;
                    node.timeFirst = time;
                    rollupStats.Add(node);
                }

                Frame frStats = frameRoot;
                while (frStats != null)
                {
                    TreeNode tr = rollupStats[frStats.id];

                    if (tr.timeLast != time)
                    {
                        UInt32 bit = (UInt32)(((time - t0) * 32 / (t1 - t0)));
                        tr.mask |= (((UInt32)1) << (int)bit);

                        tr.timeLast = time;
                        tr.inclusive++;
                        tr.weight += weight;
                    }

                    if (frStats.next == null)
                    {
                        tr.exclusive++;
                    }

                    frStats = frStats.next;
                }

                if (idPivot >= 0)
                {
                    Frame fr = frameRoot;
                    while (fr != null && fr.id != idPivot)
                    {
                        fr = fr.next;
                    }

                    // skip this stack, it doesn't contribute
                    if (fr == null)
                    {
                        return;
                    }

                    Frame frRev = frameRoot;
                    Frame frPrev = null;
                    while (frRev != fr)
                    {
                        Frame frNext = frRev.next;
                        frRev.next = frPrev;
                        frPrev = frRev;
                        frRev = frNext;
                    }

                    TreeNode treeCallers = treeRoot.children[0];
                    TreeNode treeCalls = treeRoot.children[1];

                    // treeRoot.inclusive++;
                    // treeRoot.weight += weight;
                    fr.id = treeCalls.id;

                    ProcessOrderedFrames(treeRoot, fr, time, weight);
                    ProcessOrderedFrames(treeCallers, frPrev, time, weight);

                    return;
                }

                ProcessOrderedFrames(treeRoot, frameRoot, time, weight);
            }

            private void ProcessOrderedFrames(TreeNode treeRoot, Frame frameRoot, long time, ulong weight)
            {
                if (frameRoot == null)
                {
                    return;
                }

                TreeNode tr = treeRoot;
                tr.inclusive++;
                tr.weight += weight;

                if (time < tr.timeFirst)
                {
                    tr.timeFirst = time;
                }

                if (time > tr.timeLast)
                {
                    tr.timeLast = time;
                }

                UInt32 bit = (UInt32)(((time - t0) * 32 / (t1 - t0)));
                UInt32 mask = (((UInt32)1) << (int)bit);
                tr.mask |= mask;

                while (tr != null)
                {
                    TreeNode ch = null;
                    foreach (TreeNode c in tr.children)
                    {
                        if (c.id == frameRoot.id)
                        {
                            ch = c;
                            goto process;
                        }
                    }

                    ch = new TreeNode();
                    ch.timeFirst = time;
                    ch.id = frameRoot.id;
                    tr.children.Add(ch);

                    process:
                    ch.mask |= mask;

                    if (time < ch.timeFirst)
                    {
                        ch.timeFirst = time;
                    }

                    if (time > ch.timeLast)
                    {
                        ch.timeLast = time;
                    }

                    if (frameRoot.next != null)
                    {
                        ch.inclusive++;
                        ch.weight += weight;
                        frameRoot = frameRoot.next;
                    }
                    else
                    {
                        ch.inclusive++;
                        ch.weight += weight;
                        ch.exclusive++;
                        ch = null;
                    }

                    tr = ch;
                }
            }
        }

        private class TreeDumper
        {
            private const int maxStackSize = 1024;
            private char[] stackLevelChars = new char[maxStackSize];
            private int idOther;
            private int cStitchedStacks = 0;
            private ulong totalweight = 0;
            private int totalsamples = 0;
            private ByteAtomTable atomsNodeNames = null;
            private ByteAtomTable atomsRecords = null;
            private IStackParameters parms = null;
            private StackResult result = null;
            private StringWriter sw = null;

            // this is to capture the 'magic' points in the stack with new interesting masks
            // and significant cost
            private Dictionary<int, bool> idsMagic = new Dictionary<int, bool>();
            private Dictionary<uint, int> masksMagic = new Dictionary<uint, int>();
            private bool fSkipThunks = true;
            private bool fShowWhen = false;
            private bool fIndentLess = false;
            private double minIncl = 2.0;
            private bool[] stackTypes = null;

            public string DumpStacks(ETLTrace trace, StackResult result)
            {
                sw = new StringWriter();

                atomsRecords = trace.atomsRecords;
                stackTypes = trace.StackTypes;

                atomsNodeNames = result.atomsNodeNames;
                totalweight = result.treeRoot.weight;
                totalsamples = result.treeRoot.inclusive;

                this.result = result;
                parms = result.parms;

                fSkipThunks = parms.SkipThunks;
                fShowWhen = parms.ShowWhen;
                minIncl = parms.MinInclusive / 100.0;
                fIndentLess = parms.IndentLess;

                int idSampledProfile = atomsRecords.Lookup("SampledProfile");
                int idVAlloc = atomsRecords.Lookup("VirtualAlloc");
                int idVFree = atomsRecords.Lookup("VirtualFree");

                cStitchedStacks = result.cStitchedStacks;

                if (fSkipThunks)
                {
                    for (int i = 0; i < stackTypes.Length; i++)
                    {
                        if (result.stackFilters[i] && i != idSampledProfile)
                        {
                            // skipping thunks is a bad idea for any kind of profile other than sampled
                            fSkipThunks = false;
                            break;
                        }
                    }
                }

                sw.WriteLine("Start time: {0:n0}   End time: {1:n0}  Interval Length: {2:n0}", result.t0, result.t1, result.t1 - result.t0);
                sw.WriteLine("");

                if (cStitchedStacks > 0)
                {
                    sw.WriteLine("The AI to algorithmically guess the right root for partial stacks stitched {0:n0} stacks together", cStitchedStacks);
                    sw.WriteLine("");
                }

                if (trace.Parameters.FilterText.Length > 0)
                {
                    sw.WriteLine("Text filter in effect: {0}", trace.Parameters.FilterText);
                    sw.WriteLine("");
                }

                if (trace.Parameters.StackParameters.UseIODuration)
                {
                    sw.WriteLine("The elapsed time of I/O operations was used as the cost rather than size of the operation in bytes.");
                    sw.WriteLine("");
                }

                for (int i = 0; i < stackTypes.Length; i++)
                {
                    if (result.stackFilters[i])
                    {
                        sw.WriteLine("Stack Type Analyzed: {0}", atomsRecords.MakeString(i));
                    }
                }
                sw.WriteLine("");

                if (parms.FrameFilters != null && parms.FrameFilters.Length > 0)
                {
                    sw.WriteLine("Frame filters were in effect");
                    sw.WriteLine("----------------------------");
                    sw.WriteLine(parms.FrameFilters);
                    sw.WriteLine();
                }

                if (fIndentLess)
                {
                    sw.WriteLine("The => notation indicates that there was a straight call chain and it was not indented to save space.");
                    sw.WriteLine("");
                }

                if ((idVAlloc >= 0 && result.stackFilters[idVAlloc]) || (idVFree >= 0 && result.stackFilters[idVFree]))
                {
                    if (parms.AnalyzeReservedMemory)
                    {
                        sw.WriteLine("Memory costs were based on RESERVED memory");
                    }
                    else
                    {
                        sw.WriteLine("Memory costs were based on COMMITTED memory");
                    }

                    sw.WriteLine("");
                }


                TreeNode treeRoot = result.treeRoot;

                DumpHeaders();

                idOther = atomsNodeNames.EnsureContains(ByteWindow.MakeBytes("Other"));

                for (int i = 0; i < stackLevelChars.Length; i++)
                {
                    stackLevelChars[i] = ' ';
                }

                if (totalsamples > 0)
                {
                    ComputeMagicIds();

                    DumpTree(treeRoot, null, 0, false, 0);

                    DumpStats();
                }

                return sw.ToString();
            }

            private void DumpHeaders()
            {
                sw.Write("  Incl#  Excl#   Incl%   Excl% ");

                if (totalweight > 0)
                {
                    sw.Write("           Cost   Cost% ");
                }

                if (fShowWhen)
                {
                    sw.Write("    First       Last        Occurrence Mask           ");
                }

                sw.WriteLine("  Name");

                sw.Write("------------------------------");

                if (fShowWhen)
                {
                    sw.Write("--------------------------------------------------------");
                }

                if (totalweight > 0)
                {
                    sw.Write("-----------------------");
                }

                sw.WriteLine("------");
            }

            private void DumpTree(TreeNode current, TreeNode parent, int indent, bool fLastChild, int chainCount)
            {
                if (!fIndentLess)
                {
                    chainCount = 0;
                }

                bool fSkipped = false;

                top:

                if (fSkipThunks && parent != null)
                {
                    if (parent.children.Count == 1 &&
                        parent.exclusive == 0 &&
                        current.children.Count == 1 &&
                        current.exclusive == 0)
                    {
                        fSkipped = true;
                        parent = current;
                        current = current.children[0];
                        goto top;
                    }
                }

                var othernode = new TreeNode();
                othernode.id = idOther;
                othernode.timeFirst = Int64.MaxValue;

                int effectiveChildren = 0;

                foreach (TreeNode ch in current.children)
                {
                    if (IncludeNode(ch))
                    {
                        effectiveChildren++;
                    }
                    else
                    {
                        othernode.inclusive += ch.inclusive;
                        othernode.weight += ch.weight;

                        if (othernode.timeFirst > ch.timeFirst)
                        {
                            othernode.timeFirst = ch.timeFirst;
                        }

                        if (ch.timeLast > othernode.timeLast)
                        {
                            othernode.timeLast = ch.timeLast;
                        }

                        othernode.mask |= ch.mask;
                    }
                }

                if (IncludeNode(othernode))
                {
                    effectiveChildren++;
                }

                // abort the chain if this would have been the first guy to use => notation
                // and he's also the last guy to use that notation

                if (effectiveChildren != 1 && chainCount == 3)
                {
                    // my parent didn't indent this node because it expected I would be prefixed with =>
                    // but I am declining that since I would be the one and only such node
                    // I have to fix my own indenting by one level
                    chainCount = 0;
                    indent++;
                }

                WriteTreeNode(current, indent, fLastChild, fSkipped, chainCount);

                if (fSkipped && chainCount <= 2)
                {
                    indent += 3;
                }

                if (effectiveChildren > 1 && chainCount > 2)
                {
                    indent += 3;
                }

                if (effectiveChildren == 1)
                {
                    chainCount++;
                }
                else
                {
                    chainCount = 0;
                }

                if (effectiveChildren > 1)
                {
                    stackLevelChars[indent] = '|';
                }
                else
                {
                    stackLevelChars[indent] = ' ';
                }

                int indentNew = indent;

                // keep indenting until we get 2 singleton children in a row, then enconomize indenting
                if (chainCount <= 2)
                {
                    indentNew++;
                }

                int currentChild = 0;

                foreach (TreeNode ch in current.children)
                {
                    if (IncludeNode(ch))
                    {
                        currentChild++;

                        DumpTree(ch, current, indentNew, (currentChild == effectiveChildren), chainCount);
                    }
                }

                if (IncludeNode(othernode))
                {
                    DumpTree(othernode, current, indentNew, true, chainCount);
                }

                stackLevelChars[indent] = ' ';
            }

            private bool IncludeNode(TreeNode ch)
            {
                if (totalweight == 0)
                {
                    return ch.inclusive > 0 && ch.inclusive / (double)totalsamples >= minIncl;
                }
                else
                {
                    return ch.weight > 0 && ch.weight / (double)totalweight >= minIncl;
                }
            }

            private void WriteTreeNode(TreeNode current, int indent, bool fLastChild, bool fSkipped, int chainCount)
            {
                string s = atomsNodeNames.MakeString(current.id);

                char[] maskbuff = new char[32];

                for (int i = 0; i < 32; i++)
                {
                    if ((current.mask & (1 << i)) != 0)
                    {
                        maskbuff[i] = 'X';
                    }
                    else
                    {
                        maskbuff[i] = '_';
                    }
                }

                sw.Write(" {0,6} {1,6} {2,6}% {3,6}%",
                        current.inclusive,
                        current.exclusive,
                        100 * current.inclusive / totalsamples,
                        100 * current.exclusive / totalsamples);

                if (totalweight > 0)
                {
                    sw.Write(" {0,15:n0} {1,6}%",
                        current.weight,
                        100 * current.weight / totalweight);
                }

                if (fShowWhen)
                {
                    sw.Write("{0,10} {1,10} {2}",
                        current.timeFirst,
                        current.timeLast,
                        new String(maskbuff));
                }

                if (indent > 0 && idsMagic.ContainsKey(current.id))
                {
                    sw.Write(" * ");
                }
                else
                {
                    sw.Write("   ");
                }

                for (int i = 0; i < indent; i++)
                {
                    sw.Write(stackLevelChars[i]);
                }

                if (fLastChild)
                {
                    stackLevelChars[indent - 1] = ' ';
                }

                if (chainCount > 2)
                {
                    sw.Write("=> ");
                }

                if (fSkipped)
                {
                    sw.Write("...");
                }

                sw.WriteLine(s);
            }

            private void ComputeMagicIds()
            {
                SortStatsInclusive();

                foreach (TreeNode current in result.rollupStats)
                {
                    double inc = 0;

                    if (totalweight == 0)
                    {
                        inc = current.inclusive / (double)totalsamples;
                    }
                    else
                    {
                        inc = current.weight / (double)totalweight;
                    }

                    if (inc > .70)
                    {
                        continue;
                    }

                    if (masksMagic.ContainsKey(current.mask))
                    {
                        continue;
                    }

                    string name = atomsNodeNames.MakeString(current.id);

                    if (name.StartsWith("Unknown!"))
                    {
                        continue;
                    }

                    if (name.EndsWith("IL_STUB"))
                    {
                        continue;
                    }

                    if (name.Contains("!0x"))
                    {
                        continue;
                    }

                    masksMagic.Add(current.mask, current.id);
                    idsMagic.Add(current.id, true);

                    if (idsMagic.Count >= 20)
                    {
                        break;
                    }
                }
            }

            private void DumpStats()
            {
                sw.WriteLine("");
                sw.WriteLine("-------------------------------------");
                sw.WriteLine("Top Inclusive Cost");
                sw.WriteLine("");

                DumpHeaders();

                // SortStatsInclusive();
                // the stats are already sorted by inclusive cost for an earlier computation

                foreach (TreeNode current in result.rollupStats)
                {
                    double inc = 0;

                    if (totalweight == 0)
                    {
                        inc = current.inclusive / (double)totalsamples;
                    }
                    else
                    {
                        inc = current.weight / (double)totalweight;
                    }

                    if (inc < minIncl)
                    {
                        break;
                    }

                    WriteTreeNode(current, 0, false, false, 0);
                }

                sw.WriteLine("");
                sw.WriteLine("-------------------------------------");
                sw.WriteLine("Rico's Magic 20");
                sw.WriteLine("");

                DumpHeaders();

                foreach (TreeNode current in result.rollupStats)
                {
                    if (!idsMagic.ContainsKey(current.id))
                    {
                        continue;
                    }

                    WriteTreeNode(current, 0, false, false, 0);
                }

                sw.WriteLine("");
                sw.WriteLine("-------------------------------------");
                sw.WriteLine("Top 20 Exclusive Cost");
                sw.WriteLine("");

                DumpHeaders();

                SortStatsExclusive();

                int count = 0;

                foreach (TreeNode current in result.rollupStats)
                {
                    if (count++ > 20)
                    {
                        break;
                    }

                    if (current.exclusive == 0)
                    {
                        break;
                    }

                    WriteTreeNode(current, 0, false, false, 0);
                }
            }

            private void SortStatsExclusive()
            {
                if (totalweight == 0)
                {
                    result.rollupStats.Sort(
                        (TreeNode t1, TreeNode t2) =>
                        {
                            if (t1.exclusive < t2.exclusive)
                            {
                                return 1;
                            }

                            if (t1.exclusive > t2.exclusive)
                            {
                                return -1;
                            }

                            if (t1.id > t2.id)
                            {
                                return 1;
                            }

                            if (t1.id < t2.id)
                            {
                                return -1;
                            }

                            return 0;
                        });
                }
                else
                {
                    result.rollupStats.Sort(
                        (TreeNode t1, TreeNode t2) =>
                        {
                            bool e1 = t1.exclusive > 0;
                            bool e2 = t2.exclusive > 0;

                            if (!e1 && e2)
                            {
                                return 1;
                            }

                            if (e1 && !e2)
                            {
                                return -1;
                            }

                            if (t1.weight < t2.weight)
                            {
                                return 1;
                            }

                            if (t1.weight > t2.weight)
                            {
                                return -1;
                            }

                            if (t1.exclusive < t2.exclusive)
                            {
                                return 1;
                            }

                            if (t1.exclusive > t2.exclusive)
                            {
                                return -1;
                            }

                            if (t1.id > t2.id)
                            {
                                return 1;
                            }

                            if (t1.id < t2.id)
                            {
                                return -1;
                            }

                            return 0;
                        });
                }

            }

            private void SortStatsInclusive()
            {
                if (totalweight == 0)
                {
                    result.rollupStats.Sort(
                        (TreeNode t1, TreeNode t2) =>
                        {
                            if (t1.inclusive < t2.inclusive)
                            {
                                return 1;
                            }

                            if (t1.inclusive > t2.inclusive)
                            {
                                return -1;
                            }

                            if (t1.id > t2.id)
                            {
                                return 1;
                            }

                            if (t1.id < t2.id)
                            {
                                return -1;
                            }

                            return 0;
                        });
                }
                else
                {
                    result.rollupStats.Sort(
                        (TreeNode t1, TreeNode t2) =>
                        {
                            if (t1.weight < t2.weight)
                            {
                                return 1;
                            }

                            if (t1.weight > t2.weight)
                            {
                                return -1;
                            }

                            if (t1.id > t2.id)
                            {
                                return 1;
                            }

                            if (t1.id < t2.id)
                            {
                                return -1;
                            }

                            return 0;
                        });
                }
            }
        }
    }
}
