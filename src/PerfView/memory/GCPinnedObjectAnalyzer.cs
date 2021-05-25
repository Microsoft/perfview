using Graphs;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Parsers.PerfView;
using Microsoft.Diagnostics.Tracing.Stacks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Address = System.UInt64;

namespace PerfView
{
    /// <summary>
    /// Represents the type of view to generated data for.
    /// </summary>
    internal enum GCPinnedObjectViewType
    {
        PinnedHandles,
        PinnedObjectAllocations,
    }

    /// <summary>
    /// Represents a pinned object in the GC heap.
    /// </summary>
    internal sealed class PinnedObject
    {
        private Address _ObjectAddress;
        private string _ObjectType;
        private UInt32 _ObjectSize;
        private UInt16 _Generation;

        internal PinnedObject(
            Address objectAddress,
            string objectType,
            UInt32 objectSize,
            UInt16 generation)
        {
            _ObjectAddress = objectAddress;
            _ObjectType = objectType;
            _ObjectSize = objectSize;
            _Generation = generation;
        }

        internal Address ObjectAddress
        {
            get { return _ObjectAddress; }
        }

        internal string ObjectType
        {
            get { return _ObjectType; }
        }

        internal UInt32 ObjectSize
        {
            get { return _ObjectSize; }
        }

        internal UInt16 Generation
        {
            get { return _Generation; }
        }
    }

    /// <summary>
    /// Represents a pinning root that references one or more objects in the GC heap.
    /// </summary>
    internal sealed class PinningRoot
    {
        private GCHandleKind _RootType;
        private PinnedObject[] _PinnedObjects;

        internal PinningRoot(
            GCHandleKind rootType)
        {
            if ((rootType != GCHandleKind.AsyncPinned) && (rootType != GCHandleKind.Pinned))
            {
                throw new ArgumentException("Invalid Handle Type", "rootType");
            }

            _RootType = rootType;
        }

        internal GCHandleKind RootType
        {
            get { return _RootType; }
        }

        internal PinnedObject[] PinnedObjects
        {
            get { return _PinnedObjects; }
            set { _PinnedObjects = value; }
        }
    }

    /// <summary>
    /// The class responsible for generating data for pinned object analysis.
    /// This analysis is based on an ETW trace and a corresponding heap snapshot.
    /// </summary>
    internal sealed class GCPinnedObjectAnalyzer
    {
        /// <summary>
        /// The file path to the heap snapshot.
        /// </summary>
        private string _HeapSnapshotFilePath;

        /// <summary>
        /// The trace log representing the ETW trace.
        /// </summary>
        private TraceLog _TraceLog;

        /// <summary>
        /// The stack source that should be used when generating stacks.
        /// </summary>
        private MutableTraceEventStackSource _StackSource;

        /// <summary>
        /// The sample to be used when generating data points.
        /// </summary>
        private StackSourceSample _Sample;

        /// <summary>
        /// The process that we intend to analyze.
        /// </summary>
        private int _ProcessID;

        /// <summary>
        /// The log for diagnostic data.
        /// </summary>
        private TextWriter _Log;

        /// <summary>
        /// The set of pinning roots and pinned objects in the heap snapshot.
        /// </summary>
        /// <remarks>
        /// TKey == The address of the pinned object.
        /// TValue == The pinning root, which also contains references to objects in the GC heap that it pins.
        /// </remarks>
        private Dictionary<Address, PinningRoot> _RootTable;

        internal GCPinnedObjectAnalyzer(
            string etlFilePath,
            TraceLog traceLog,
            MutableTraceEventStackSource stackSource,
            StackSourceSample sample,
            TextWriter log)
        {
            _HeapSnapshotFilePath = GetHeapSnapshotPath(etlFilePath);
            _TraceLog = traceLog;
            _StackSource = stackSource;
            _Sample = sample;
            _Log = log;
        }

        /// <summary>
        /// Used to determine if a matching heap snapshot file exists.
        /// </summary>
        internal static bool ExistsMatchingHeapSnapshot(
            string etlFilePath)
        {
            string heapSnapshotFilePath = GetHeapSnapshotPath(etlFilePath);
            return File.Exists(heapSnapshotFilePath);
        }

        /// <summary>
        /// Get the heap snapshot file path that matches the input ETL file.
        /// </summary>
        /// <remarks>
        /// We match the file in the same directory with the same prefix.  E.g. PerfViewData.etl.zip as the input will generate PerfViewData.gcdump.
        /// </remarks>
        internal static string GetHeapSnapshotPath(
            string etlFilePath)
        {
            // Get the full path to the directory.
            string directoryPath = Path.GetDirectoryName(etlFilePath);
            string filePrefix = null;
            if (Path.GetExtension(etlFilePath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                string intermediateFilePrefix = Path.GetFileNameWithoutExtension(etlFilePath);
                if (Path.GetExtension(intermediateFilePrefix).Equals(".etl", StringComparison.OrdinalIgnoreCase))
                {
                    filePrefix = Path.GetFileNameWithoutExtension(intermediateFilePrefix);
                }
                else
                {
                    throw new FormatException("Invalid filename format.");
                }
            }
            else if (Path.GetExtension(etlFilePath).Equals(".etl", StringComparison.OrdinalIgnoreCase))
            {
                filePrefix = Path.GetFileNameWithoutExtension(etlFilePath);
            }

            return Path.Combine(directoryPath, filePrefix + ".gcdump");
        }

        /// <summary>
        /// Execute the pinned object analyzer.
        /// </summary>
        internal void Execute(
            GCPinnedObjectViewType viewType)
        {
            // Process the heap snapshot, and populate the root table and process id.
            ProcessHeapSnapshot();

            // Instantiate the necessary trace event parsers.
            TraceEventDispatcher eventDispatcher = _TraceLog.Events.GetSource();
            PerfViewTraceEventParser perfViewParser = new PerfViewTraceEventParser(eventDispatcher);

            // we want the state of the heap at the time the snapshot was taken.  
            perfViewParser.TriggerHeapSnapshot += delegate (TriggerHeapSnapshotTraceData data)
            {
                eventDispatcher.StopProcessing();
            };

            var heapWithPinningInfo = new PinningStackAnalysis(eventDispatcher, _TraceLog.Processes.GetProcess(_ProcessID, _TraceLog.SessionDuration.TotalMilliseconds), _StackSource, _Log);
            // Process the ETL file up until we detect that the heap snapshot was taken.
            eventDispatcher.Process();

            // Iterate through all pinned objects in the heap snapshot.
            foreach (KeyValuePair<Address, PinningRoot> pinnedPair in _RootTable)
            {
                // Try to match the object in the heap snapshot with an object in the ETL.
                PinningStackAnalysisObject liveObjectInfo = heapWithPinningInfo.GetPinningInfo(pinnedPair.Key);
                if (liveObjectInfo != null)
                {
                    // Found a match, write the appropriate call stacks.
                    if (viewType == GCPinnedObjectViewType.PinnedObjectAllocations)
                    {
                        WriteAllocationStack(pinnedPair.Key, pinnedPair.Value, liveObjectInfo);
                    }
                    else if (viewType == GCPinnedObjectViewType.PinnedHandles)
                    {
                        WritePinningStacks(pinnedPair.Key, pinnedPair.Value, liveObjectInfo);
                    }
                }
            }
        }

        internal void ProcessHeapSnapshot()
        {
            // Constants.
            const string PinnedHandlesNodeName = "[Pinned handle]";
            const string AsyncPinnedHandlesNodeName = "[AsyncPinned handle]";
            const string OverlappedDataTypeName = "System.Threading.OverlappedData";
            const string ByteArrayTypeName = "System.Byte[]";
            const string ObjectArrayTypeName = "System.Object[]";

            // Open the heap dump.
            GCHeapDump dump = new GCHeapDump(_HeapSnapshotFilePath);
            _ProcessID = dump.ProcessID;

            // Get the heap info.
            DotNetHeapInfo heapInfo = dump.DotNetHeapInfo;

            // Get the memory graph.
            MemoryGraph memoryGraph = dump.MemoryGraph;

            // Get the root node.
            NodeIndex rootIndex = memoryGraph.RootIndex;
            Node rootNode = memoryGraph.GetNode(rootIndex, memoryGraph.AllocNodeStorage());

            // Allocate additional nodes and node types.
            Node handleClassNodeStorage = memoryGraph.AllocNodeStorage();
            NodeType handleClassNodeTypeStorage = memoryGraph.AllocTypeNodeStorage();

            Node pinnedObjectNodeStorage = memoryGraph.AllocNodeStorage();
            NodeType pinnedObjectNodeTypeStorage = memoryGraph.AllocTypeNodeStorage();

            Node pinnedObjectChildNodeStorage = memoryGraph.AllocNodeStorage();
            NodeType pinnedObjectChildNodeTypeStorage = memoryGraph.AllocTypeNodeStorage();

            Node userObjectNodeStorage = memoryGraph.AllocNodeStorage();
            NodeType userObjectNodeTypeStorage = memoryGraph.AllocTypeNodeStorage();

            Node arrayBufferNodeStorage = memoryGraph.AllocNodeStorage();
            NodeType arrayBufferNodeTypeStorage = memoryGraph.AllocTypeNodeStorage();

            // Create a dictionary of pinned roots by pinned object address.
            Dictionary<Address, PinningRoot> pinnedRoots = new Dictionary<Address, PinningRoot>();

            // Iterate over the nodes that represent handle type (e.g. [AsyncPinned Handle], [Pinned Handle], etc.)
            for (NodeIndex handleClassNodeIndex = rootNode.GetFirstChildIndex(); handleClassNodeIndex != NodeIndex.Invalid; handleClassNodeIndex = rootNode.GetNextChildIndex())
            {
                // Get the node.
                Node handleClassNode = memoryGraph.GetNode(handleClassNodeIndex, handleClassNodeStorage);
                NodeType handleClassNodeType = handleClassNode.GetType(handleClassNodeTypeStorage);

                // Iterate over all pinned handles.
                if (PinnedHandlesNodeName.Equals(handleClassNodeType.Name))
                {
                    for (NodeIndex pinnedObjectNodeIndex = handleClassNode.GetFirstChildIndex(); pinnedObjectNodeIndex != NodeIndex.Invalid; pinnedObjectNodeIndex = handleClassNode.GetNextChildIndex())
                    {
                        Node pinnedObjectNode = memoryGraph.GetNode(pinnedObjectNodeIndex, pinnedObjectNodeStorage);
                        NodeType pinnedObjectNodeType = pinnedObjectNode.GetType(pinnedObjectNodeTypeStorage);

                        // Create an object to represent the pinned objects.
                        PinningRoot pinnedRoot = new PinningRoot(GCHandleKind.Pinned);

                        // Get the address of the OverlappedData and add it to the list of pinned objects.
                        Address pinnedObjectAddress = memoryGraph.GetAddress(pinnedObjectNodeIndex);
                        UInt16 pinnedObjectGeneration = (UInt16)heapInfo.GenerationFor(pinnedObjectAddress);

                        pinnedRoot.PinnedObjects = new PinnedObject[] { new PinnedObject(pinnedObjectAddress, pinnedObjectNodeType.Name, (uint)pinnedObjectNode.Size, pinnedObjectGeneration) };
                        pinnedRoots.Add(pinnedObjectAddress, pinnedRoot);
                    }
                }

                // Iterate over asyncpinned handles.
                if (AsyncPinnedHandlesNodeName.Equals(handleClassNodeType.Name))
                {
                    for (NodeIndex pinnedObjectNodeIndex = handleClassNode.GetFirstChildIndex(); pinnedObjectNodeIndex != NodeIndex.Invalid; pinnedObjectNodeIndex = handleClassNode.GetNextChildIndex())
                    {
                        Node pinnedObjectNode = memoryGraph.GetNode(pinnedObjectNodeIndex, pinnedObjectNodeStorage);
                        NodeType pinnedObjectNodeType = pinnedObjectNode.GetType(pinnedObjectNodeTypeStorage);

                        // Iterate over all OverlappedData objects.
                        if (OverlappedDataTypeName.Equals(pinnedObjectNodeType.Name))
                        {
                            // Create an object to represent the pinned objects.
                            PinningRoot pinnedRoot = new PinningRoot(GCHandleKind.AsyncPinned);
                            List<Address> objectAddresses = new List<Address>();
                            List<PinnedObject> pinnedObjects = new List<PinnedObject>();

                            // Get the address of the OverlappedData and add it to the list of pinned objects.
                            Address pinnedObjectAddress = memoryGraph.GetAddress(pinnedObjectNodeIndex);
                            UInt16 pinnedObjectGeneration = (UInt16)heapInfo.GenerationFor(pinnedObjectAddress);
                            objectAddresses.Add(pinnedObjectAddress);
                            pinnedObjects.Add(new PinnedObject(pinnedObjectAddress, pinnedObjectNodeType.Name, (uint)pinnedObjectNode.Size, pinnedObjectGeneration));

                            // Get the buffer or list of buffers that are pinned by the asyncpinned handle.
                            for (NodeIndex userObjectNodeIndex = pinnedObjectNode.GetFirstChildIndex(); userObjectNodeIndex != NodeIndex.Invalid; userObjectNodeIndex = pinnedObjectNode.GetNextChildIndex())
                            {
                                Node userObjectNode = memoryGraph.GetNode(userObjectNodeIndex, userObjectNodeStorage);
                                NodeType userObjectNodeType = userObjectNode.GetType(userObjectNodeTypeStorage);

                                if (userObjectNodeType.Name.StartsWith(ByteArrayTypeName))
                                {
                                    // Get the address.
                                    Address bufferAddress = memoryGraph.GetAddress(userObjectNodeIndex);
                                    UInt16 bufferGeneration = (UInt16)heapInfo.GenerationFor(bufferAddress);
                                    objectAddresses.Add(bufferAddress);
                                    pinnedObjects.Add(new PinnedObject(bufferAddress, userObjectNodeType.Name, (uint)userObjectNode.Size, bufferGeneration));
                                }
                                else if (userObjectNodeType.Name.StartsWith(ObjectArrayTypeName))
                                {
                                    for (NodeIndex arrayBufferNodeIndex = userObjectNode.GetFirstChildIndex(); arrayBufferNodeIndex != NodeIndex.Invalid; arrayBufferNodeIndex = userObjectNode.GetNextChildIndex())
                                    {
                                        Node arrayBufferNode = memoryGraph.GetNode(arrayBufferNodeIndex, arrayBufferNodeStorage);
                                        NodeType arrayBufferNodeType = arrayBufferNode.GetType(arrayBufferNodeTypeStorage);
                                        if (arrayBufferNodeType.Name.StartsWith(ByteArrayTypeName))
                                        {
                                            // Get the address.
                                            Address bufferAddress = memoryGraph.GetAddress(arrayBufferNodeIndex);
                                            UInt16 bufferGeneration = (UInt16)heapInfo.GenerationFor(bufferAddress);
                                            objectAddresses.Add(bufferAddress);
                                            pinnedObjects.Add(new PinnedObject(bufferAddress, arrayBufferNodeType.Name, (uint)arrayBufferNode.Size, bufferGeneration));
                                        }
                                    }
                                }
                            }

                            // Assign the list of objects into the pinned root.
                            pinnedRoot.PinnedObjects = pinnedObjects.ToArray();

                            foreach (Address objectAddress in objectAddresses)
                            {
                                // TODO: Handle objects that are pinned multiple times (?)
                                pinnedRoots.Add(objectAddress, pinnedRoot);
                            }
                        }
                    }
                }
            }

            _RootTable = pinnedRoots;
        }

        /// <summary>
        /// Add a sample representing the pinned object allocation.
        /// </summary>
        private void WriteAllocationStack(
            Address objectAddress,
            PinningRoot pinnedRoot,
            PinningStackAnalysisObject liveObjectInfo)
        {
            // Get the pinned object from the pinned root.
            PinnedObject pinnedObject = null;
            foreach (PinnedObject o in pinnedRoot.PinnedObjects)
            {
                if (o.ObjectAddress == objectAddress)
                {
                    pinnedObject = o;
                    break;
                }
            }

            // This should not happen, but we put this here to ensure that we don't crash.
            if (null == pinnedObject)
            {
                System.Diagnostics.Debug.Assert(false, "Pinned object could not be found, but was found in the _RootTable.");
                return;
            }

            // Get the allocation call stack.
            StackSourceCallStackIndex rootCallStackIndex = liveObjectInfo.AllocStack;

            // Add the generation pseudo-node.
            string generationString = "GENERATION " + pinnedObject.Generation;
            StackSourceFrameIndex generationFrameIndex = _StackSource.Interner.FrameIntern(generationString);
            StackSourceCallStackIndex callStackIndex = _StackSource.Interner.CallStackIntern(generationFrameIndex, rootCallStackIndex);

            // Add the type of the object.
            string objectTypeString = "OBJECT_TYPE " + pinnedObject.ObjectType;
            StackSourceFrameIndex objectTypeFrameIndex = _StackSource.Interner.FrameIntern(objectTypeString);
            callStackIndex = _StackSource.Interner.CallStackIntern(objectTypeFrameIndex, callStackIndex);

            // Set the object instance.
            string objectInstanceString = "OBJECT_INSTANCE " + pinnedObject.ObjectAddress;
            StackSourceFrameIndex objectInstanceFrameIndex = _StackSource.Interner.FrameIntern(objectInstanceString);
            callStackIndex = _StackSource.Interner.CallStackIntern(objectInstanceFrameIndex, callStackIndex);

            // Setup the sample.
            _Sample.TimeRelativeMSec = liveObjectInfo.AllocationTimeRelativeMSec;
            _Sample.Metric = pinnedObject.ObjectSize;
            _Sample.StackIndex = callStackIndex;
            _StackSource.AddSample(_Sample);
        }

        /// <summary>
        /// Write out a sample for each pin operation of the input object.
        /// </summary>
        private void WritePinningStacks(
            Address objectAddress,
            PinningRoot pinnedRoot,
            PinningStackAnalysisObject liveObjectInfo)
        {
            if (liveObjectInfo.PinInfo != null)
            {
                foreach (PinningStackAnalysisPinInfo pinInfo in liveObjectInfo.PinInfo)
                {
                    // Get the pinning stack and the time.

                    foreach (PinnedObject pinnedObject in pinnedRoot.PinnedObjects)
                    {
                        // Add the generation pseudo-node.
                        string generationString = "GENERATION " + pinnedObject.Generation;
                        StackSourceFrameIndex generationFrameIndex = _StackSource.Interner.FrameIntern(generationString);
                        StackSourceCallStackIndex callStackIndex = _StackSource.Interner.CallStackIntern(generationFrameIndex, pinInfo.PinStack);

                        // Add the root type.
                        string rootTypeString = "ROOT_TYPE " + pinnedRoot.RootType.ToString();
                        StackSourceFrameIndex rootTypeFrameIndex = _StackSource.Interner.FrameIntern(rootTypeString);
                        callStackIndex = _StackSource.Interner.CallStackIntern(rootTypeFrameIndex, callStackIndex);

                        // Add the type of the object.
                        string objectTypeString = "OBJECT_TYPE " + pinnedObject.ObjectType;
                        StackSourceFrameIndex objectTypeFrameIndex = _StackSource.Interner.FrameIntern(objectTypeString);
                        callStackIndex = _StackSource.Interner.CallStackIntern(objectTypeFrameIndex, callStackIndex);

                        // Set the object instance.
                        string objectInstanceString = "OBJECT_INSTANCE " + pinnedObject.ObjectAddress;
                        StackSourceFrameIndex objectInstanceFrameIndex = _StackSource.Interner.FrameIntern(objectInstanceString);
                        callStackIndex = _StackSource.Interner.CallStackIntern(objectInstanceFrameIndex, callStackIndex);

                        // Set the metric to 1 since the metric represents the number of pin operations.
                        _Sample.Metric = 1;

                        // Set the call stack.
                        _Sample.TimeRelativeMSec = pinInfo.PinTimeRelativeMSec;
                        _Sample.StackIndex = callStackIndex;
                        _StackSource.AddSample(_Sample);
                    }
                }
            }
        }

    }

    /**********************************************************************************************************/
    // PinningStackAnalysis works on ETW stacks and indexes them by object reference.   Thus you can do
    // 'GetPinningInfo' on any object and find the allocation stack (and time) as well as a list of 
    // all times where at Pinning GC handle was set to point it it.  
    /// <summary>
    /// Does a GC simulation of the events in 'source' for process with id 'processID'.   It also 
    /// tracks GC pinning handel set operations and adds that information to the objects being
    /// tracked by the simulation. 
    /// </summary>
    internal sealed class PinningStackAnalysis : GCHeapSimulator
    {
        public PinningStackAnalysis(TraceEventDispatcher source, TraceProcess process, MutableTraceEventStackSource stackSource, TextWriter log)
            : base(source, process, stackSource, log)
        {
            var clrPrivateParser = new ClrPrivateTraceEventParser(source);
            clrPrivateParser.GCSetGCHandle += OnSetGCHandle;
            source.Clr.GCSetGCHandle += OnSetGCHandle;

            AllocateObject = () => new PinningStackAnalysisObject();
        }
        public PinningStackAnalysisObject GetPinningInfo(Address objectAddress)
        {
            return base.GetObjectInfo(objectAddress) as PinningStackAnalysisObject;
        }

        #region private


        private void OnSetGCHandle(SetGCHandleTraceData data)
        {
            if (Process.ProcessID != data.ProcessID)
            {
                return;
            }

            // This is not a pinned handle.
            if ((GCHandleKind.AsyncPinned != data.Kind) && (GCHandleKind.Pinned != data.Kind))
            {
                return;
            }

            PinningStackAnalysisObject objectInfo = GetPinningInfo(data.ObjectID);
            Debug.Assert(objectInfo != null);
            if (objectInfo == null)
            {
                return;
            }

            // TODO FIX NOW worry about duplicates between the public and private CLR providers. 

            if (objectInfo.PinInfo == null)
            {
                objectInfo.PinInfo = new List<PinningStackAnalysisPinInfo>();
            }

            var stackIndex = StackSource.GetCallStack(data.CallStackIndex(), data);
            objectInfo.PinInfo.Add(new PinningStackAnalysisPinInfo(data.TimeStampRelativeMSec, stackIndex, data.Kind));
        }
        #endregion
    }

    /// <summary>
    /// GCHeapSimulatorObject, is what we know about any object on the GC heap.  PinningAnalysis adds extra i
    /// nfromation to GCHeapSimulatorObject about pinning.  
    /// </summary>
    internal class PinningStackAnalysisObject : GCHeapSimulatorObject
    {
        // we also want to keep track of every place the object was pinned. 
        public List<PinningStackAnalysisPinInfo> PinInfo;
    }

    /// <summary>
    /// Information we know about one particular set operation of a pinning GC handle 
    /// </summary>
    internal class PinningStackAnalysisPinInfo
    {
        public double PinTimeRelativeMSec;
        public StackSourceCallStackIndex PinStack;
        public GCHandleKind PinKind;           // Either AsyncPinned or Pinned.  

        public PinningStackAnalysisPinInfo(double pinTimeRelativeMSec, StackSourceCallStackIndex pinStack, GCHandleKind pinKind)
        {
            // TODO: Complete member initialization
            PinTimeRelativeMSec = pinTimeRelativeMSec;
            PinStack = pinStack;
            PinKind = pinKind;
        }
    }
}
