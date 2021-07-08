using Graphs;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.JSDumpHeap;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Address = System.UInt64;

/// <summary>
/// Reads a JavaScript Heap dump generated from ETW
/// </summary>
public class JavaScriptDumpGraphReader
{
    /// <summary>
    /// A class for reading ETW events from JScript9.dll and creating a MemoryGraph from it.  
    /// </summary>
    /// <param name="log">A place to put diagnostic messages.</param>
    public JavaScriptDumpGraphReader(TextWriter log)
    {
        m_log = log;
    }

    /// <summary>
    /// Read in the memory dump from javaScriptEtlName.   Since there can be more than one, choose the first one
    /// after double startTimeRelativeMSec.  If processId is non-zero only that process is considered, otherwise it considered
    /// the first heap dump regardless of process.  
    /// </summary>
    public MemoryGraph Read(string javaScriptEtlName, int processId = 0, double startTimeRelativeMSec = 0)
    {
        var ret = new MemoryGraph(10000);
        Append(ret, javaScriptEtlName, processId, startTimeRelativeMSec);
        ret.AllowReading();
        return ret;
    }
    public void Append(MemoryGraph memoryGraph, string javaScriptEtlName, int processId, double startTimeRelativeMSec = 0)
    {
        using (var source = new ETWTraceEventSource(javaScriptEtlName))
        {
            Append(memoryGraph, source, processId, startTimeRelativeMSec);
        }
    }
    public void Append(MemoryGraph memoryGraph, TraceEventDispatcher source, int processId, double startTimeRelativeMSec = 0)
    {
        SetupCallbacks(memoryGraph, source, processId, startTimeRelativeMSec);
        source.Process();
        ConvertHeapDataToGraph();
    }

    #region private
    internal void SetupCallbacks(MemoryGraph memoryGraph, TraceEventDispatcher source, int processID = 0, double startTimeRelativeMSec = 0)
    {
        m_graph = memoryGraph;
        m_children = new HashSet<NodeIndex>();
        m_types = new Dictionary<string, NodeTypeIndex>(10000);
        m_nodeBlocks = new Queue<BulkNodeTraceData>();
        m_attributeBlocks = new Queue<BulkAttributeTraceData>();
        m_edgeBlocks = new Queue<BulkEdgeTraceData>();
        m_ignoreEvents = true;
        m_ignoreUntilMSec = startTimeRelativeMSec;
        m_processId = processID;

        var jsDump = new JSDumpHeapTraceEventParser(source);
        jsDump.JSDumpHeapEnvelopeStart += delegate (SettingsTraceData data)
        {
            if (data.TimeStampRelativeMSec < m_ignoreUntilMSec)
            {
                return;
            }

            if (m_processId == 0)
            {
                m_processId = data.ProcessID;
            }

            if (data.ProcessID != m_processId)
            {
                return;
            }

            if (!m_seenStart)
            {
                m_ignoreEvents = false;
            }

            m_seenStart = true;
        };
        jsDump.JSDumpHeapEnvelopeStop += delegate (SummaryTraceData data)
        {
            if (m_ignoreEvents || data.ProcessID != m_processId)
            {
                return;
            }

            m_ignoreEvents = true;
            source.StopProcessing();
        };
        jsDump.JSDumpHeapBulkNode += delegate (BulkNodeTraceData data)
        {
            if (m_ignoreEvents || data.ProcessID != m_processId)
            {
                return;
            }

            m_nodeBlocks.Enqueue((BulkNodeTraceData)data.Clone());
        };
        jsDump.JSDumpHeapBulkAttribute += delegate (BulkAttributeTraceData data)
        {
            if (m_ignoreEvents || data.ProcessID != m_processId)
            {
                return;
            }

            m_attributeBlocks.Enqueue((BulkAttributeTraceData)data.Clone());
        };
        jsDump.JSDumpHeapBulkEdge += delegate (BulkEdgeTraceData data)
        {
            if (m_ignoreEvents || data.ProcessID != m_processId)
            {
                return;
            }

            m_edgeBlocks.Enqueue((BulkEdgeTraceData)data.Clone());
        };
        jsDump.JSDumpHeapStringTable += delegate (StringTableTraceData data)
        {
            if (m_ignoreEvents || data.ProcessID != m_processId)
            {
                return;
            }

            for (int i = 0; i < data.Count; i++)
            {
                m_stringTable.Add(data.Strings(i));
            }
        };
        jsDump.JSDumpHeapDoubleTable += delegate (DoubleTableTraceData data)
        {
            if (m_ignoreEvents || data.ProcessID != m_processId)
            {
                return;
            }

            for (int i = 0; i < data.Count; i++)
            {
                m_doubleTable.Add(data.Doubles(i));
            }
        };
    }

    internal unsafe void ConvertHeapDataToGraph()
    {
        if (m_converted)
        {
            return;
        }

        m_converted = true;

        if (!m_seenStart)
        {
            throw new ApplicationException("ETL file did not include a Start Heap Dump Event");
        }

        if (!m_ignoreEvents)
        {
            throw new ApplicationException("ETL file did not include a Stop Heap Dump Event");
        }

        // Since we may have multiple roots, I create a pseudo-node to act as its parent. 
        var root = new MemoryNodeBuilder(m_graph, "[JS Roots]");

        var nodeNames = new GrowableArray<string>(1000);
        for (; ; )
        {
            BulkNodeValues node;
            if (!GetNextNode(out node))
            {
                break;
            }

            // Get the node index
            var nodeIdx = m_graph.GetNodeIndex(node.Address);
            m_children.Clear();

            // Get the basic type name
            var typeName = "?";
            if (0 <= node.TypeNameId)
            {
                typeName = m_stringTable[node.TypeNameId];
                if (typeName.Length > 6 && typeName.EndsWith("Object"))
                {
                    typeName = "JS" + typeName.Substring(0, typeName.Length - 6);
                }
            }
            var relationships = "";

            // Process the edges (which can add children)
            for (int i = 0; i < node.EdgeCount; i++)
            {
                BulkEdgeValues edge;
                if (!GetNextEdge(out edge))
                {
                    throw new ApplicationException("Missing Edge Nodes in ETW data");
                }

                // Is this an edge to another object?  (externals count)
                if (edge.TargetType == EdgeTargetType.Object || edge.TargetType == EdgeTargetType.External)
                {
                    var childIdx = m_graph.GetNodeIndex((Address)edge.Value);

                    // Get the property name if it has one
                    string childPropertyName = null;
                    if (edge.RelationshipType == EdgeRelationshipType.NamedProperty || edge.RelationshipType == EdgeRelationshipType.Event)
                    {
                        childPropertyName = m_stringTable[edge.NameId];
                    }
                    else if (edge.RelationshipType == EdgeRelationshipType.IndexedProperty)
                    {
                        // The edge is an element of an array and the NameID is the index in the array.  
                        // We want to treat all index properties as a single field
                        childPropertyName = "[]";
                    }
                    else if (edge.RelationshipType == EdgeRelationshipType.InternalProperty)
                    {
                        childPropertyName = "InternalProperty";
                    }

                    if (childPropertyName != null)
                    {
                        // Remember the property so that when we display the target object, we show that too. 
                        if ((int)childIdx >= nodeNames.Count)
                        {
                            nodeNames.Count = (int)childIdx + 100;   // expand by at least 100.  
                        }

                        nodeNames[(int)childIdx] = childPropertyName;
                    }
                    m_children.Add(childIdx);
                }
                else if (edge.TargetType == EdgeTargetType.BSTR)
                {
                    if (edge.RelationshipType == EdgeRelationshipType.RelationShip)
                    {
                        // This is extra information (typically the tag or a class of a HTML DOM object).  Add it to the type name. 
                        var relationshipName = m_stringTable[edge.NameId];
                        var relationshipValue = m_stringTable[(int)edge.Value];
                        if (relationships.Length > 0)
                        {
                            relationships += " ";
                        }

                        relationships += relationshipName + ":" + relationshipValue;
                    }
                }
            }

            // Get the property name we saved from things that refer to this object.  
            string propertyName = null;
            if ((int)nodeIdx < nodeNames.Count)
            {
                propertyName = nodeNames[(int)nodeIdx];
                if (propertyName != null)
                {
                    nodeNames[(int)nodeIdx] = null;
                }
            }

            // Process the attributes.  We can get a good function name as well as some more children from the attributes.  
            int objSize = node.Size;
            for (int i = 0; i < node.AttributeCount; i++)
            {
                BulkAttributeValues attribute;
                if (!GetNextAttribute(out attribute))
                {
                    throw new ApplicationException("Missing Attribute Nodes in ETW data");
                }

                // TODO FIX NOW Currently I include the prototype link.  is this a good idea? 
                if (attribute.Type == AttributeType.Prototype)
                {
                    m_children.Add(m_graph.GetNodeIndex(attribute.Value));
                }
                else if (attribute.Type == AttributeType.Scope)
                {
                    // TODO FIX NOW: it seems that Value is truncated to 32 bits and we have to restore it.  
                    // Feels like a hack (and not clear if it is correct)
                    var target = attribute.Value;
                    if ((target >> 32) == 0 && (node.Address >> 32) != 0)
                    {
                        target += node.Address & 0xFFFFFFFF00000000;
                    }

                    m_children.Add(m_graph.GetNodeIndex(target));
                }
                // WPA does this, I don't really understand it.  
                if (attribute.Type == AttributeType.TextChildrenSize)
                {
                    objSize += (int)attribute.Value;
                }
                else if (attribute.Type == AttributeType.FunctionName)
                {
                    propertyName = m_stringTable[(int)attribute.Value];
                }
            }


            if (relationships.Length > 0)
            {
                typeName += " <|" + relationships + "|>";
            }
            // Create the complete type name
            if ((node.Flags & ObjectFlags.WINRT) != 0)
            {
                typeName = "(WinRT " + " " + typeName + ")";
            }
            else
            {
                typeName = "(Type " + " " + typeName + ")";
            }

            if (propertyName != null)
            {
                typeName = propertyName + " " + typeName;
            }

            // typeName += " [0x" + node.Address.ToString("x") + "]";
            var typeIdx = GetTypeIndex(typeName, node.Size);

            if ((node.Flags & ObjectFlags.IS_ROOT) != 0)
            {
                root.AddChild(nodeIdx);
            }

            if (!m_graph.IsDefined(nodeIdx))
            {
                m_graph.SetNode(nodeIdx, typeIdx, objSize, m_children);
            }
            else
            {
                // Only external objects might be listed twice.  
                Debug.Assert((node.Flags & (ObjectFlags.EXTERNAL | ObjectFlags.EXTERNAL_UNKNOWN | ObjectFlags.EXTERNAL_DISPATCH |
                                            ObjectFlags.WINRT_DELEGATE | ObjectFlags.WINRT_INSTANCE | ObjectFlags.WINRT_NAMESPACE |
                                            ObjectFlags.WINRT_RUNTIMECLASS)) != 0);
            }
        }

        root.Build();
        m_graph.RootIndex = root.Index;
    }

    private unsafe bool GetNextNode(out BulkNodeValues node)
    {
        if (m_curNodeBlock == null)
        {
            if (m_nodeBlocks.Count == 0)
            {
                node = new BulkNodeValues();
                return false;
            }
            m_curNodeBlock = m_nodeBlocks.Dequeue();
            m_curNodeIdx = 0;
        }
        if (m_curNodeBlock.Count <= m_curNodeIdx)
        {
            m_curNodeBlock = null;
            return GetNextNode(out node);
        }
        node = m_curNodeBlock.Values(m_curNodeIdx++);
        return true;
    }

    private unsafe bool GetNextEdge(out BulkEdgeValues edge)
    {
        if (m_curEdgeBlock == null)
        {
            if (m_edgeBlocks.Count == 0)
            {
                edge = new BulkEdgeValues();
                return false;
            }
            m_curEdgeBlock = m_edgeBlocks.Dequeue();
            m_curEdgeIdx = 0;
        }
        if (m_curEdgeBlock.Count <= m_curEdgeIdx)
        {
            m_curEdgeBlock = null;
            return GetNextEdge(out edge);
        }
        edge = m_curEdgeBlock.Values(m_curEdgeIdx++);
        return true;
    }

    private unsafe bool GetNextAttribute(out BulkAttributeValues attribute)
    {
        if (m_curAttributeBlock == null)
        {
            if (m_attributeBlocks.Count == 0)
            {
                attribute = new BulkAttributeValues();
                return false;
            }
            m_curAttributeBlock = m_attributeBlocks.Dequeue();
            m_curAttributeIdx = 0;
        }
        if (m_curAttributeBlock.Count <= m_curAttributeIdx)
        {
            m_curAttributeBlock = null;
            return GetNextAttribute(out attribute);
        }
        attribute = m_curAttributeBlock.Values(m_curAttributeIdx++);
        return true;
    }

    private NodeTypeIndex GetTypeIndex(string typeName, int size)
    {
        NodeTypeIndex ret;
        if (!m_types.TryGetValue(typeName, out ret))
        {
            ret = m_graph.CreateType(typeName, null, size);
            m_types[typeName] = ret;
        }
        return ret;
    }

    private bool m_converted;
    private bool m_seenStart;
    private bool m_ignoreEvents;
    private int m_curNodeIdx;
    private BulkNodeTraceData m_curNodeBlock;
    private Queue<BulkNodeTraceData> m_nodeBlocks;
    private int m_curEdgeIdx;
    private BulkEdgeTraceData m_curEdgeBlock;
    private Queue<BulkEdgeTraceData> m_edgeBlocks;
    private int m_curAttributeIdx;
    private BulkAttributeTraceData m_curAttributeBlock;
    private Queue<BulkAttributeTraceData> m_attributeBlocks;
    private GrowableArray<string> m_stringTable;
    private GrowableArray<double> m_doubleTable;
    private HashSet<NodeIndex> m_children;
    private Dictionary<string, NodeTypeIndex> m_types;
    private MemoryGraph m_graph;
    private TextWriter m_log;
    private double m_ignoreUntilMSec;        // ignore until we see this
    private int m_processId;
    #endregion
}