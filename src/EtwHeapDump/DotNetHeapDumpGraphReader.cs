using Graphs;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Parsers.Symbol;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Address = System.UInt64;

/// <summary>
/// Reads a .NET Heap dump generated from ETW
/// </summary>
public class DotNetHeapDumpGraphReader
{
    /// <summary>
    /// A class for reading ETW events from the .NET runtime and creating a MemoryGraph from it.   This only works on V4.5.1 of the runtime or later.  
    /// </summary>
    /// <param name="log">A place to put diagnostic messages.</param>
    public DotNetHeapDumpGraphReader(TextWriter log)
    {
        m_log = log;
    }

    /// <summary>
    /// Read in the memory dump from javaScriptEtlName.   Since there can be more than one, choose the first one
    /// after double startTimeRelativeMSec.  If processId is non-zero only that process is considered, otherwise it considered
    /// the first heap dump regardless of process.  
    /// </summary>
    public MemoryGraph Read(string etlFilePath, string processNameOrId = null, double startTimeRelativeMSec = 0)
    {
        m_etlFilePath = etlFilePath;
        var ret = new MemoryGraph(10000);
        Append(ret, etlFilePath, processNameOrId, startTimeRelativeMSec);
        ret.AllowReading();
        return ret;
    }

    public MemoryGraph Read(TraceEventDispatcher source, string processNameOrId = null, double startTimeRelativeMSec = 0)
    {
        var ret = new MemoryGraph(10000);
        Append(ret, source, processNameOrId, startTimeRelativeMSec);
        ret.AllowReading();
        return ret;
    }
    public void Append(MemoryGraph memoryGraph, string etlName, string processNameOrId = null, double startTimeRelativeMSec = 0)
    {
        using (var source = TraceEventDispatcher.GetDispatcherFromFileName(etlName))
        {
            Append(memoryGraph, source, processNameOrId, startTimeRelativeMSec);
        }
    }
    public void Append(MemoryGraph memoryGraph, TraceEventDispatcher source, string processNameOrId = null, double startTimeRelativeMSec = 0)
    {
        SetupCallbacks(memoryGraph, source, processNameOrId, startTimeRelativeMSec);
        source.Process();
        ConvertHeapDataToGraph();
    }

    /// <summary>
    /// If set before Read or Append is called, keep track of the additional information about GC generations associated with .NET Heaps.  
    /// </summary>
    public DotNetHeapInfo DotNetHeapInfo
    {
        get { return m_dotNetHeapInfo; }
        set { m_dotNetHeapInfo = value; }
    }

    #region private
    /// <summary>
    /// Sets up the callbacks needed to do a heap dump (work need before processing the events()
    /// </summary>
    internal void SetupCallbacks(MemoryGraph memoryGraph, TraceEventDispatcher source, string processNameOrId = null, double startTimeRelativeMSec = 0)
    {
        m_graph = memoryGraph;
        m_typeID2TypeIndex = new Dictionary<Address, NodeTypeIndex>(1000);
        m_moduleID2Name = new Dictionary<Address, string>(16);
        m_arrayNametoIndex = new Dictionary<string, NodeTypeIndex>(32);
        m_objectToRCW = new Dictionary<Address, RCWInfo>(100);
        m_nodeBlocks = new Queue<GCBulkNodeTraceData>();
        m_edgeBlocks = new Queue<GCBulkEdgeTraceData>();
        m_typeBlocks = new Queue<GCBulkTypeTraceData>();
        m_staticVarBlocks = new Queue<GCBulkRootStaticVarTraceData>();
        m_ccwBlocks = new Queue<GCBulkRootCCWTraceData>();
        m_typeIntern = new Dictionary<string, NodeTypeIndex>();
        m_root = new MemoryNodeBuilder(m_graph, "[.NET Roots]");
        m_typeStorage = m_graph.AllocTypeNodeStorage();

        // We also keep track of the loaded modules in the target process just in case it is a project N scenario.  
        // (Not play for play but it is small).  
        m_modules = new Dictionary<Address, Module>(32);

        m_ignoreEvents = true;
        m_ignoreUntilMSec = startTimeRelativeMSec;

        m_processId = 0;        // defaults to a wildcard.  
        if (processNameOrId != null)
        {
            if (!int.TryParse(processNameOrId, out m_processId))
            {
                m_processId = -1;       // an illegal value.  
                m_processName = processNameOrId;
            }
        }

        // Remember the module IDs too.              
        Action<ModuleLoadUnloadTraceData> moduleCallback = delegate (ModuleLoadUnloadTraceData data)
        {
            if (data.ProcessID != m_processId)
            {
                return;
            }

            if (!m_moduleID2Name.ContainsKey((Address)data.ModuleID))
            {
                m_moduleID2Name[(Address)data.ModuleID] = data.ModuleILPath;
            }

            m_log.WriteLine("Found Module {0} ID 0x{1:x}", data.ModuleILFileName, (Address)data.ModuleID);
        };
        source.Clr.AddCallbackForEvents<ModuleLoadUnloadTraceData>(moduleCallback); // Get module events for clr provider
        // TODO should not be needed if we use CAPTURE_STATE when collecting.  
        var clrRundown = new ClrRundownTraceEventParser(source);
        clrRundown.AddCallbackForEvents<ModuleLoadUnloadTraceData>(moduleCallback); // and its rundown provider.  

        DbgIDRSDSTraceData lastDbgData = null;
        var symbolParser = new SymbolTraceEventParser(source);
        symbolParser.ImageIDDbgID_RSDS += delegate (DbgIDRSDSTraceData data)
        {
            if (data.ProcessID != m_processId)
            {
                return;
            }

            lastDbgData = (DbgIDRSDSTraceData)data.Clone();
        };

        source.Kernel.ImageGroup += delegate (ImageLoadTraceData data)
        {
            if (m_processId == 0)
            {
                return;
            }

            if (data.ProcessID != m_processId)
            {
                return;
            }

            Module module = new Module(data.ImageBase);
            module.Path = data.FileName;
            module.Size = data.ImageSize;
            module.BuildTime = data.BuildTime;
            if (lastDbgData != null && data.TimeStampRelativeMSec == lastDbgData.TimeStampRelativeMSec)
            {
                module.PdbGuid = lastDbgData.GuidSig;
                module.PdbAge = lastDbgData.Age;
                module.PdbName = lastDbgData.PdbFileName;
            }
            m_modules[module.ImageBase] = module;
        };

        // TODO this does not work in the circular case
        source.Kernel.ProcessGroup += delegate (ProcessTraceData data)
        {
            if (0 <= m_processId || m_processName == null)
            {
                return;
            }

            if (string.Compare(data.ProcessName, processNameOrId, StringComparison.OrdinalIgnoreCase) == 0)
            {
                m_log.WriteLine("Found process id {0} for process Name {1}", processNameOrId, data.ProcessName);
                m_processId = data.ProcessID;
            }
            else
            {
                m_log.WriteLine("Found process {0} but does not match {1}", data.ProcessName, processNameOrId);
            }
        };

        source.Clr.GCGenAwareStart += delegate (GenAwareBeginTraceData data)
        {
            m_seenStart = true;
            m_ignoreEvents = false;
        };

        source.Clr.GCStart += delegate (GCStartTraceData data)
        {
            // If this GC is not part of a heap dump, ignore it.  
            // TODO FIX NOW if (data.ClientSequenceNumber == 0)
            //     return;

            if (data.TimeStampRelativeMSec < m_ignoreUntilMSec)
            {
                return;
            }

            if (m_processId == 0)
            {
                m_processId = data.ProcessID;
                m_log.WriteLine("Process wildcard selects process id {0}", m_processId);
            }
            if (data.ProcessID != m_processId)
            {
                m_log.WriteLine("GC Start found but Process ID {0} != {1} desired ID", data.ProcessID, m_processId);
                return;
            }

            if (!IsProjectN && data.ProviderGuid == ClrTraceEventParser.NativeProviderGuid)
            {
                IsProjectN = true;
            }

            if (data.Depth < 2 || data.Type != GCType.NonConcurrentGC)
            {
                m_log.WriteLine("GC Start found but not a Foreground Gen 2 GC");
                return;
            }

            if (data.Reason != GCReason.Induced)
            {
                m_log.WriteLine("GC Start not induced. Skipping.");
                return;
            }

            if (!m_seenStart)
            {
                m_gcID = data.Count;
                m_log.WriteLine("Found a Gen2 Induced non-background GC Start at {0:n3} msec GC Count {1}", data.TimeStampRelativeMSec, m_gcID);
                m_ignoreEvents = false;
                m_seenStart = true;
                memoryGraph.Is64Bit = (data.PointerSize == 8);
            }
        };

        source.Clr.GCStop += delegate (GCEndTraceData data)
        {
            if (m_ignoreEvents || data.ProcessID != m_processId)
            {
                return;
            }

            if (data.Count == m_gcID)
            {
                m_log.WriteLine("Found a GC Stop at {0:n3} for GC {1}, ignoring events from now on.", data.TimeStampRelativeMSec, m_gcID);
                m_ignoreEvents = true;

                if (m_nodeBlocks.Count == 0 && m_typeBlocks.Count == 0 && m_edgeBlocks.Count == 0)
                {
                    m_log.WriteLine("Found no node events, looking for another GC");
                    m_seenStart = false;
                    return;
                }

                // TODO we have to continue processing to get the module rundown events.    
                // If we could be sure to get these early, we could optimized this. 
                // source.StopProcessing();
            }
            else
            {
                m_log.WriteLine("Found a GC Stop at {0:n3} but id {1} != {2} Target ID", data.TimeStampRelativeMSec, data.Count, m_gcID);
            }
        };

        source.Clr.GCGenAwareEnd += delegate (GenAwareEndTraceData data)
        {
            m_ignoreEvents = true;
            if (m_nodeBlocks.Count == 0 && m_typeBlocks.Count == 0 && m_edgeBlocks.Count == 0)
            {
                m_log.WriteLine("Found no node events, looking for another GC");
                m_seenStart = false;
                return;
            }
        };

        source.Clr.TypeBulkType += delegate (GCBulkTypeTraceData data)
        {
            // Don't check m_ignoreEvents here, as BulkType events can be emitted by other events...such as the GC allocation event.
            // This means that when setting m_processId to 0 in the command line may still lose type events.
            if (data.ProcessID != m_processId)
            {
                return;
            }

            m_typeBlocks.Enqueue((GCBulkTypeTraceData)data.Clone());
        };

        source.Clr.GCBulkNode += delegate (GCBulkNodeTraceData data)
        {
            if (m_ignoreEvents || data.ProcessID != m_processId)
            {
                return;
            }

            m_nodeBlocks.Enqueue((GCBulkNodeTraceData)data.Clone());
        };

        source.Clr.GCBulkEdge += delegate (GCBulkEdgeTraceData data)
        {
            if (m_ignoreEvents || data.ProcessID != m_processId)
            {
                return;
            }

            m_edgeBlocks.Enqueue((GCBulkEdgeTraceData)data.Clone());
        };

        source.Clr.GCBulkRootEdge += delegate (GCBulkRootEdgeTraceData data)
        {
            if (m_ignoreEvents || data.ProcessID != m_processId)
            {
                return;
            }

            MemoryNodeBuilder staticRoot = m_root.FindOrCreateChild("[static vars]");
            for (int i = 0; i < data.Count; i++)
            {
                var value = data.Values(i);
                var flags = value.GCRootFlag;
                if ((flags & GCRootFlags.WeakRef) == 0)     // ignore weak references. they are not roots.  
                {
                    GCRootKind kind = value.GCRootKind;
                    MemoryNodeBuilder root = m_root;
                    string name;
                    if (kind == GCRootKind.Stack)
                    {
                        name = "[local vars]";
                    }
                    else
                    {
                        root = m_root.FindOrCreateChild("[other roots]");

                        if ((flags & GCRootFlags.RefCounted) != 0)
                        {
                            name = "[COM/WinRT Objects]";
                        }
                        else if (kind == GCRootKind.Finalizer)
                        {
                            name = "[finalizer Handles]";
                        }
                        else if (kind == GCRootKind.Handle)
                        {
                            if (flags == GCRootFlags.Pinning)
                            {
                                name = "[pinning Handles]";
                            }
                            else
                            {
                                name = "[strong Handles]";
                            }
                        }
                        else
                        {
                            name = "[other Handles]";
                        }

                        // Remember the root for later processing.  
                        if (value.RootedNodeAddress != 0)
                        {
                            Address gcRootId = value.GCRootID;
                            if (gcRootId != 0 && IsProjectN)
                            {
                                Module gcRootModule = GetModuleForAddress(gcRootId);
                                if (gcRootModule != null)
                                {
                                    var staticRva = (int)(gcRootId - gcRootModule.ImageBase);
                                    var staticTypeIdx = m_graph.CreateType(staticRva, gcRootModule, 0, " (static var)");
                                    var staticNodeIdx = m_graph.CreateNode();
                                    m_children.Clear();
                                    m_children.Add(m_graph.GetNodeIndex(value.RootedNodeAddress));
                                    m_graph.SetNode(staticNodeIdx, staticTypeIdx, 0, m_children);
                                    staticRoot.AddChild(staticNodeIdx);
                                    Trace.WriteLine("Got Static 0x" + gcRootId.ToString("x") + " pointing at 0x" + value.RootedNodeAddress.ToString("x") + " kind " + value.GCRootKind + " flags " + value.GCRootFlag);
                                    continue;
                                }
                            }

                            Trace.WriteLine("Got GC Root 0x" + gcRootId.ToString("x") + " pointing at 0x" + value.RootedNodeAddress.ToString("x") + " kind " + value.GCRootKind + " flags " + value.GCRootFlag);
                        }
                    }

                    root = root.FindOrCreateChild(name);
                    Address objId = value.RootedNodeAddress;
                    root.AddChild(m_graph.GetNodeIndex(objId));
                }
            }
        };

        source.Clr.GCBulkRCW += delegate (GCBulkRCWTraceData data)
        {
            if (m_ignoreEvents || data.ProcessID != m_processId)
            {
                return;
            }

            for (int i = 0; i < data.Count; i++)
            {
                GCBulkRCWValues comInfo = data.Values(i);
                m_objectToRCW[comInfo.ObjectID] = new RCWInfo(comInfo);
            }
        };

        source.Clr.GCBulkRootCCW += delegate (GCBulkRootCCWTraceData data)
        {
            if (m_ignoreEvents || data.ProcessID != m_processId)
            {
                return;
            }

            m_ccwBlocks.Enqueue((GCBulkRootCCWTraceData)data.Clone());
        };

        source.Clr.GCBulkRootStaticVar += delegate (GCBulkRootStaticVarTraceData data)
        {
            if (m_ignoreEvents || data.ProcessID != m_processId)
            {
                return;
            }

            m_staticVarBlocks.Enqueue((GCBulkRootStaticVarTraceData)data.Clone());
        };

        source.Clr.GCBulkRootConditionalWeakTableElementEdge += delegate (GCBulkRootConditionalWeakTableElementEdgeTraceData data)
        {
            if (m_ignoreEvents || data.ProcessID != m_processId)
            {
                return;
            }

            var otherRoots = m_root.FindOrCreateChild("[other roots]");
            var dependentHandles = otherRoots.FindOrCreateChild("[Dependent Handles]");
            for (int i = 0; i < data.Count; i++)
            {
                var value = data.Values(i);
                // TODO fix this so that they you see this as an arc from source to target.  
                // The target is alive only if the source ID (which is a weak handle) is alive (non-zero)
                if (value.GCKeyNodeID != 0)
                {
                    dependentHandles.AddChild(m_graph.GetNodeIndex(value.GCValueNodeID));
                }
            }
        };

        source.Clr.GCGenerationRange += delegate (GCGenerationRangeTraceData data)
        {
            if (m_ignoreEvents || data.ProcessID != m_processId)
            {
                return;
            }

            if (m_dotNetHeapInfo == null)
            {
                return;
            }

            // We want the 'after' ranges so we wait 
            if (m_nodeBlocks.Count == 0)
            {
                return;
            }

            Address start = data.RangeStart;
            Address end = start + data.RangeUsedLength;

            if (m_dotNetHeapInfo.Segments == null)
            {
                m_dotNetHeapInfo.Segments = new List<GCHeapDumpSegment>();
            }

            GCHeapDumpSegment segment = new GCHeapDumpSegment();
            segment.Start = start;
            segment.End = end;

            switch (data.Generation)
            {
                case 0:
                    segment.Gen0End = end;
                    break;
                case 1:
                    segment.Gen1End = end;
                    break;
                case 2:
                    segment.Gen2End = end;
                    break;
                case 3:
                    segment.Gen3End = end;
                    break;
                case 4:
                    segment.Gen4End = end;
                    break;
                default:
                    throw new Exception("Invalid generation in GCGenerationRangeTraceData");
            }
            m_dotNetHeapInfo.Segments.Add(segment);
        };
    }

    /// <summary>
    /// After reading the events the graph is not actually created, you need to post process the information we gathered 
    /// from the events.  This is where that happens.   Thus 'SetupCallbacks, Process(), ConvertHeapDataToGraph()' is how
    /// you dump a heap.  
    /// </summary>
    internal unsafe void ConvertHeapDataToGraph()
    {
        int maxNodeCount = 10_000_000;

        if (m_converted)
        {
            return;
        }

        m_converted = true;

        if (!m_seenStart)
        {
            if (m_processName != null)
            {
                throw new ApplicationException("ETL file did not include a Heap Dump for process " + m_processName);
            }

            throw new ApplicationException("ETL file did not include a Heap Dump for process ID " + m_processId);
        }

        if (!m_ignoreEvents)
        {
            throw new ApplicationException("ETL file shows the start of a heap dump but not its completion.");
        }

        m_log.WriteLine("Processing Heap Data, BulkTypeEventCount:{0}  BulkNodeEventCount:{1}  BulkEdgeEventCount:{2}",
            m_typeBlocks.Count, m_nodeBlocks.Count, m_edgeBlocks.Count);

        // Process the type information (we can't do it on the fly because we need the module information, which may be
        // at the end of the trace.  
        while (m_typeBlocks.Count > 0)
        {
            GCBulkTypeTraceData data = m_typeBlocks.Dequeue();
            for (int i = 0; i < data.Count; i++)
            {
                GCBulkTypeValues typeData = data.Values(i);
                var typeName = typeData.TypeName;
                if (IsProjectN)
                {
                    // For project N we only log the type ID and module base address.  
                    Debug.Assert(typeName.Length == 0);
                    Debug.Assert((typeData.Flags & TypeFlags.ModuleBaseAddress) != 0);
                    var moduleBaseAddress = typeData.TypeID - (ulong)typeData.TypeNameID;   // Tricky way of getting the image base. 
                    Debug.Assert((moduleBaseAddress & 0xFFFF) == 0);       // Image loads should be on 64K boundaries.  

                    Module module = GetModuleForImageBase(moduleBaseAddress);
                    if (module.Path == null)
                    {
                        m_log.WriteLine("Error: Could not find DLL name for imageBase 0x{0:x} looking up typeID 0x{1:x} with TypeNameID {2:x}",
                            moduleBaseAddress, typeData.TypeID, typeData.TypeNameID);
                    }

                    m_typeID2TypeIndex[typeData.TypeID] = m_graph.CreateType(typeData.TypeNameID, module);
                }
                else
                {
                    if (typeName.Length == 0)
                    {
                        if ((typeData.Flags & TypeFlags.Array) != 0)
                        {
                            typeName = "ArrayType(0x" + typeData.TypeNameID.ToString("x") + ")";
                        }
                        else
                        {
                            typeName = "Type(0x" + typeData.TypeNameID.ToString("x") + ")";
                        }
                    }
                    // TODO FIX NOW these are kind of hacks
                    typeName = Regex.Replace(typeName, @"`\d+", "");
                    typeName = typeName.Replace("[", "<");
                    typeName = typeName.Replace("]", ">");
                    typeName = typeName.Replace("<>", "[]");

                    string moduleName;
                    if (!m_moduleID2Name.TryGetValue(typeData.ModuleID, out moduleName))
                    {
                        moduleName = "Module(0x" + typeData.ModuleID.ToString("x") + ")";
                        m_moduleID2Name[typeData.ModuleID] = moduleName;
                    }

                    // Is this type a an RCW?   If so mark the type name that way.   
                    if ((typeData.Flags & TypeFlags.ExternallyImplementedCOMObject) != 0)
                    {
                        typeName = "[RCW " + typeName + "]";
                    }

                    m_typeID2TypeIndex[typeData.TypeID] = CreateType(typeName, moduleName);
                    // Trace.WriteLine(string.Format("Type 0x{0:x} = {1}", typeData.TypeID, typeName));
                }
            }
        }

        // Process all the ccw root information (which also need the type information complete)
        var ccwRoot = m_root.FindOrCreateChild("[COM/WinRT Objects]");
        while (m_ccwBlocks.Count > 0)
        {
            GCBulkRootCCWTraceData data = m_ccwBlocks.Dequeue();
            GrowableArray<NodeIndex> ccwChildren = new GrowableArray<NodeIndex>(1);
            for (int i = 0; i < data.Count; i++)
            {
                unsafe
                {
                    GCBulkRootCCWValues ccwInfo = data.Values(i);
                    // TODO Debug.Assert(ccwInfo.IUnknown != 0);
                    if (ccwInfo.IUnknown == 0)
                    {
                        // TODO currently there are times when a CCWs IUnknown pointer is not set (it is set lazily).  
                        // m_log.WriteLine("Warning seen a CCW with IUnknown == 0");
                        continue;
                    }

                    // Create a CCW node that represents the COM object that has one child that points at the managed object.  
                    var ccwNode = m_graph.GetNodeIndex(ccwInfo.IUnknown);

                    var ccwTypeIndex = GetTypeIndex(ccwInfo.TypeID, 200);
                    var ccwType = m_graph.GetType(ccwTypeIndex, m_typeStorage);

                    var typeName = "[CCW 0x" + ccwInfo.IUnknown.ToString("x") + " for type " + ccwType.Name + "]";
                    ccwTypeIndex = CreateType(typeName);

                    ccwChildren.Clear();
                    ccwChildren.Add(m_graph.GetNodeIndex(ccwInfo.ObjectID));
                    m_graph.SetNode(ccwNode, ccwTypeIndex, 200, ccwChildren);
                    ccwRoot.AddChild(ccwNode);
                }
            }
        }

        // Process all the static variable root information (which also need the module information complete
        var staticVarsRoot = m_root.FindOrCreateChild("[static vars]");
        while (m_staticVarBlocks.Count > 0)
        {
            GCBulkRootStaticVarTraceData data = m_staticVarBlocks.Dequeue();
            for (int i = 0; i < data.Count; i++)
            {
                GCBulkRootStaticVarValues staticVarData = data.Values(i);
                var rootToAddTo = staticVarsRoot;
                if ((staticVarData.Flags & GCRootStaticVarFlags.ThreadLocal) != 0)
                {
                    rootToAddTo = m_root.FindOrCreateChild("[thread static vars]");
                }

                // Get the type name.  
                NodeTypeIndex typeIdx;
                string typeName;
                if (m_typeID2TypeIndex.TryGetValue(staticVarData.TypeID, out typeIdx))
                {
                    var type = m_graph.GetType(typeIdx, m_typeStorage);
                    typeName = type.Name;
                }
                else
                {
                    typeName = "Type(0x" + staticVarData.TypeID.ToString("x") + ")";
                }

                string fullFieldName = typeName + "." + staticVarData.FieldName;

                rootToAddTo = rootToAddTo.FindOrCreateChild("[static var " + fullFieldName + "]");
                var nodeIdx = m_graph.GetNodeIndex(staticVarData.ObjectID);
                rootToAddTo.AddChild(nodeIdx);
            }
        }

        // var typeStorage = m_graph.AllocTypeNodeStorage();
        GCBulkNodeUnsafeNodes nodeStorage = new GCBulkNodeUnsafeNodes();

        // Process all the node and edge nodes we have collected.  
        bool doCompletionCheck = true;
        for (; ; )
        {
            GCBulkNodeUnsafeNodes* node = GetNextNode(&nodeStorage);
            if (node == null)
            {
                break;
            }

            // Get the node index
            var nodeIdx = m_graph.GetNodeIndex((Address)node->Address);
            var objSize = (int)node->Size;
            Debug.Assert(node->Size < 0x1000000000);
            var typeIdx = GetTypeIndex(node->TypeID, objSize);

            // TODO FIX NOW REMOVE 
            // var type = m_graph.GetType(typeIdx, typeStorage);
            // Trace.WriteLine(string.Format("Got Object 0x{0:x} Type {1} Size {2} #children {3}  nodeIdx {4}", (Address)node->Address, type.Name, objSize, node->EdgeCount, nodeIdx));

            // Process the edges (which can add children)
            m_children.Clear();
            for (int i = 0; i < node->EdgeCount; i++)
            {
                Address edge = GetNextEdge();
                var childIdx = m_graph.GetNodeIndex(edge);
                m_children.Add(childIdx);
                // Trace.WriteLine(string.Format("   Child 0x{0:x}", edge));
            }

            // TODO we can use the nodes type to see if this is an RCW before doing this lookup which may be a bit more efficient.  
            RCWInfo info;
            if (m_objectToRCW.TryGetValue((Address)node->Address, out info))
            {
                // Add the COM object this RCW points at as a child of this node.  
                m_children.Add(m_graph.GetNodeIndex(info.IUnknown));

                // We add 1000 to account for the overhead of the RCW that is NOT on the GC heap.
                objSize += 1000;
            }

            Debug.Assert(!m_graph.IsDefined(nodeIdx));
            m_graph.SetNode(nodeIdx, typeIdx, objSize, m_children);

            if (m_graph.NodeCount >= maxNodeCount)
            {
                doCompletionCheck = false;
                var userMessage = string.Format("Exceeded max node count {0}", maxNodeCount);
                m_log.WriteLine("[WARNING: ]", userMessage);
                break;
            }
        }

        if (doCompletionCheck && m_curEdgeBlock != null && m_curEdgeBlock.Count != m_curEdgeIdx)
        {
            throw new ApplicationException("Error: extra edge data.  Giving up on heap dump.");
        }

        m_root.Build();
        m_graph.RootIndex = m_root.Index;
    }

    /// <summary>
    /// Given a module image base, return a Module instance that has all the information we have on it.  
    /// </summary>
    private Module GetModuleForImageBase(Address moduleBaseAddress)
    {
        Module module;
        if (!m_modules.TryGetValue(moduleBaseAddress, out module))
        {
            module = new Module(moduleBaseAddress);
            m_modules.Add(moduleBaseAddress, module);
        }

        if (module.PdbName == null && module.Path != null)
        {
            m_log.WriteLine("No PDB information for {0} in ETL file, looking for it directly", module.Path);
            if (File.Exists(module.Path))
            {
                using (var modulePEFile = new PEFile.PEFile(module.Path))
                {
                    if (!modulePEFile.GetPdbSignature(out module.PdbName, out module.PdbGuid, out module.PdbAge))
                    {
                        m_log.WriteLine("Could not get PDB information for {0}", module.Path);
                    }
                }
            }
        }
        return module;
    }

    /// <summary>
    /// if 'addressInModule' points inside any loaded module return that module.  Otherwise return null
    /// </summary>
    private Module GetModuleForAddress(Address addressInModule)
    {
        if (m_lastModule != null && m_lastModule.ImageBase <= addressInModule && addressInModule < m_lastModule.ImageBase + (uint)m_lastModule.Size)
        {
            return m_lastModule;
        }

        foreach (Module module in m_modules.Values)
        {
            if (module.ImageBase <= addressInModule && addressInModule < module.ImageBase + (uint)module.Size)
            {
                m_lastModule = module;
                return module;
            }
        }
        return null;
    }

    private Module m_lastModule;        // one element cache

    private unsafe GCBulkNodeUnsafeNodes* GetNextNode(GCBulkNodeUnsafeNodes* buffer)
    {
        if (m_curNodeBlock == null || m_curNodeBlock.Count <= m_curNodeIdx)
        {
            m_curNodeBlock = null;
            if (m_nodeBlocks.Count == 0)
            {
                return null;
            }

            var nextBlock = m_nodeBlocks.Dequeue();
            if (m_curNodeBlock != null && nextBlock.Index != m_curNodeBlock.Index + 1)
            {
                throw new ApplicationException("Error expected Node Index " + (m_curNodeBlock.Index + 1) + " Got " + nextBlock.Index + " Giving up on heap dump.");
            }

            m_curNodeBlock = nextBlock;
            m_curNodeIdx = 0;
        }
        return m_curNodeBlock.UnsafeNodes(m_curNodeIdx++, buffer);
    }

    private Address GetNextEdge()
    {
        if (m_curEdgeBlock == null || m_curEdgeBlock.Count <= m_curEdgeIdx)
        {
            m_curEdgeBlock = null;
            if (m_edgeBlocks.Count == 0)
            {
                throw new ApplicationException("Error not enough edge data.  Giving up on heap dump.");
            }

            var nextEdgeBlock = m_edgeBlocks.Dequeue();
            if (m_curEdgeBlock != null && nextEdgeBlock.Index != m_curEdgeBlock.Index + 1)
            {
                throw new ApplicationException("Error expected Node Index " + (m_curEdgeBlock.Index + 1) + " Got " + nextEdgeBlock.Index + " Giving up on heap dump.");
            }

            m_curEdgeBlock = nextEdgeBlock;
            m_curEdgeIdx = 0;
        }
        return m_curEdgeBlock.Values(m_curEdgeIdx++).Target;
    }

    private NodeTypeIndex GetTypeIndex(Address typeID, int objSize)
    {
        NodeTypeIndex ret;
        if (!m_typeID2TypeIndex.TryGetValue(typeID, out ret))
        {
            m_log.WriteLine("Error: Did not have a type definition for typeID 0x{0:x}", typeID);
            Trace.WriteLine(string.Format("Error: Did not have a type definition for typeID 0x{0:x}", typeID));

            var typeName = "UNKNOWN 0x" + typeID.ToString("x");
            ret = CreateType(typeName);
            m_typeID2TypeIndex[typeID] = ret;
        }

        if (objSize > 1000)
        {
            var type = m_graph.GetType(ret, m_typeStorage);
            var suffix = GetObjectSizeSuffix(objSize);      // indicates the size range
            var typeName = type.Name + suffix;

            // TODO FIX NOW worry about module collision
            if (!m_arrayNametoIndex.TryGetValue(typeName, out ret))
            {
                if (IsProjectN)
                {
                    ret = m_graph.CreateType(type.RawTypeID, type.Module, objSize, suffix);
                }
                else
                {
                    ret = CreateType(typeName, type.ModuleName);
                }

                m_arrayNametoIndex.Add(typeName, ret);
            }
        }
        return ret;
    }

    // Returns a string suffix that discriminates interesting size ranges. 
    private static string GetObjectSizeSuffix(int objSize)
    {
        if (objSize < 1000)
        {
            return "";
        }

        string size;
        if (objSize < 10000)
        {
            size = "1K";
        }
        else if (objSize < 100000)
        {
            size = "10K";
        }
        else if (objSize < 1000000)
        {
            size = "100K";
        }
        else if (objSize < 10000000)
        {
            size = "1M";
        }
        else if (objSize < 100000000)
        {
            size = "10M";
        }
        else
        {
            size = "100M";
        }

        return " (Bytes > " + size + ")";
    }

    private NodeTypeIndex CreateType(string typeName, string moduleName = null)
    {
        var fullTypeName = typeName;
        if (moduleName != null)
        {
            fullTypeName = moduleName + "!" + typeName;
        }

        NodeTypeIndex ret;
        if (!m_typeIntern.TryGetValue(fullTypeName, out ret))
        {
            ret = m_graph.CreateType(typeName, moduleName);
            m_typeIntern.Add(fullTypeName, ret);
        }
        return ret;
    }

    /// <summary>
    /// Converts a raw TypeID (From the ETW data), to the graph type index)
    /// </summary>
    private Dictionary<Address, NodeTypeIndex> m_typeID2TypeIndex;
    private Dictionary<Address, string> m_moduleID2Name;
    private Dictionary<string, NodeTypeIndex> m_arrayNametoIndex;

    /// <summary>
    /// Remembers addition information about RCWs.  
    /// </summary>
    private class RCWInfo
    {
        public RCWInfo(GCBulkRCWValues data) { IUnknown = data.IUnknown; }
        public Address IUnknown;

    };

    private Dictionary<Address, RCWInfo> m_objectToRCW;

    /// <summary>
    /// We gather all the BulkTypeTraceData into a list m_typeBlocks which we then process as a second pass (because we need module info which may be after the type info).  
    /// </summary>
    private Queue<GCBulkTypeTraceData> m_typeBlocks;

    /// <summary>
    /// We gather all the BulkTypeTraceData into a list m_typeBlocks which we then process as a second pass (because we need module info which may be after the type info).  
    /// </summary>
    private Queue<GCBulkRootStaticVarTraceData> m_staticVarBlocks;

    /// <summary>
    /// We gather all the GCBulkRootCCWTraceData into a list m_ccwBlocks which we then process as a second pass (because we need type info which may be after the ccw info).  
    /// </summary>
    private Queue<GCBulkRootCCWTraceData> m_ccwBlocks;

    /// <summary>
    /// We gather all the GCBulkNodeTraceData events into a list m_nodeBlocks.  m_curNodeBlock is the current block we are processing and 'm_curNodeIdx' is the node within the event 
    /// </summary>
    private Queue<GCBulkNodeTraceData> m_nodeBlocks;
    private GCBulkNodeTraceData m_curNodeBlock;
    private int m_curNodeIdx;

    /// <summary>
    /// We gather all the GCBulkEdgeTraceData events into a list m_edgeBlocks.  m_curEdgeBlock is the current block we are processing and 'm_curEdgeIdx' is the edge within the event 
    /// </summary>
    private Queue<GCBulkEdgeTraceData> m_edgeBlocks;
    private int m_curEdgeIdx;
    private GCBulkEdgeTraceData m_curEdgeBlock;

    /// <summary>
    /// We want type indexes to be shared as much as possible, so this table remembers the ones we have already created.  
    /// </summary>
    private Dictionary<string, NodeTypeIndex> m_typeIntern;

    // scratch location for creating nodes. 
    private GrowableArray<NodeIndex> m_children;

    // This is a 'scratch location' we use to fetch type information. 
    private NodeType m_typeStorage;

    // m_modules is populated as types are defined, and then we look up all the necessary module info later.  
    private Dictionary<Address, Module> m_modules;      // Currently only non-null if it is a project N heap dump
    private bool IsProjectN;                            // only set after we see the GCStart

    // Information from the constructor 
    private string m_etlFilePath;
    private double m_ignoreUntilMSec;        // ignore until we see this
    private int m_processId;
    private string m_processName;
    private TextWriter m_log;

    // State that lets up pick the particular heap dump int the ETL file and ignore the rest.  
    private bool m_converted;
    private bool m_seenStart;
    private bool m_ignoreEvents;
    private int m_gcID;

    // The graph we generating.  
    private MemoryGraph m_graph;
    private MemoryNodeBuilder m_root;       // Used to create pseduo-nodes for the roots of the graph.  

    // Heap information for .NET heaps.
    private DotNetHeapInfo m_dotNetHeapInfo;
    #endregion
}