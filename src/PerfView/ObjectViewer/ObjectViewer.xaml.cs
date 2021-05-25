using Graphs;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;

namespace PerfView
{
    /// <summary>
    /// ObjectViewer is PerFView's view that displays individual object in the heap graph.   It is a TreeeViewGrid 
    /// that is wired up to get its data from the 'graph' and 'refGraph' starting with a list of 'focus nodes' as
    /// the root of the tree.  
    /// </summary>
    public partial class ObjectViewer : WindowBase
    {
        public ObjectViewer(Window parentWindow, MemoryGraph graph, RefGraph refGraph, List<NodeIndex> focusNodes = null) : base(parentWindow)
        {
            InitializeComponent();

            // Wire up our behavior into the generic TreeViewGrid.  This defines the columns and how to get a child nodes.  
            TreeViewGrid.SetController(new ObjectViewerTreeViewController(graph, refGraph, focusNodes));
        }

        #region private
        public static RoutedUICommand FindCommand = new RoutedUICommand("Find", "Find", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.F, ModifierKeys.Control) });
        public static RoutedUICommand FindNextCommand = new RoutedUICommand("Find Next", "FindNext", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.F3) });
        public static RoutedUICommand ExpandCommand = new RoutedUICommand("Expand", "Expand", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.Space) });

        private void DoHyperlinkHelp(object sender, RoutedEventArgs e)
        {
            var asHyperLink = sender as Hyperlink;
            if (asHyperLink != null)
            {
                MainWindow.DisplayUsersGuide((string)asHyperLink.Tag);
                return;
            }
            var asCommand = e as ExecutedRoutedEventArgs;
            if (asCommand != null)
            {
                var param = asCommand.Parameter as string;
                if (param == null)
                {
                    param = "ObjectViewerQuickStart";       // This is the F1 help
                }

                MainWindow.DisplayUsersGuide(param);
                return;
            }

            // TODO FIX NOW define ObjectViewerQuickStart ObjectViewerTips in the help, ValueColumn NameColumn.   
        }
        private void DoFind(object sender, ExecutedRoutedEventArgs e)
        {
        }

        private void DoFindNext(object sender, ExecutedRoutedEventArgs e)
        {
        }
        private void DoExpand(object sender, ExecutedRoutedEventArgs e)
        {
#if false // TODO FIX NOW 
            var selectedNode = TreeViewGrid.SelectedNode;
            if (selectedNode != null)
            {
                for (; ; )
                {
                    if (!selectedNode.IsExpanded)
                    {
                        selectedNode.IsExpanded = true;
                        break;
                    }

                    var children = selectedNode.VisibleChildren;
                    if (children.Count < 1)
                        break;
                    selectedNode = children[0];
                }
            }
#endif 
        }

        /// <summary>
        /// ObjectViewerTreeViewController is the thing that describes the tree the viewer will display
        /// Thus it tells you how to get the root node and for each node how to get its children.   
        /// It also tells how to get the column data to display.   We simply wire this up to the
        /// graph.   We do this by creating ITreeViewControllerNode that represent nodes in the 
        /// tree which hold a NodeIndex that lets us get at all addition information about the node.  
        /// </summary>
        private class ObjectViewerTreeViewController : ITreeViewController
        {
            public ObjectViewerTreeViewController(MemoryGraph graph, RefGraph refGraph, List<NodeIndex> focusNodes)
            {
                m_graph = graph;
                m_refGraph = refGraph;
                m_focusNodes = focusNodes;

                m_typeStorage = m_graph.AllocTypeNodeStorage();
                m_nodeStorage = m_graph.AllocNodeStorage();
                m_refNodeStorage = m_refGraph.AllocNodeStorage();
                m_columnNames = new List<string>(3) { "Value", "Size", "Type" };
            }

            public object Root
            {
                get
                {
                    if (m_focusNodes != null)
                    {
                        return new ITreeViewControllerNode(m_focusNodes, 0);
                    }
                    else
                    {
                        return new ITreeViewControllerNode(m_graph.RootIndex, 0);
                    }
                }
            }
            public string Name(object objNode)
            {
                ITreeViewControllerNode treeNode = (ITreeViewControllerNode)objNode;
                return treeNode.Name;
            }

            public int ChildCount(object objNode)
            {
                ITreeViewControllerNode treeNode = (ITreeViewControllerNode)objNode;
                if (treeNode.m_nodeList != null)
                {
                    return treeNode.m_nodeList.Count;                                   // Case 1 we are the root node
                }
                else
                {
                    if (treeNode.IsRefByNode)
                    {
                        RefNode refNode = m_refGraph.GetNode(treeNode.m_nodeIdx, m_refNodeStorage);
                        return refNode.ChildCount;
                    }
                    else
                    {
                        Node node = m_graph.GetNode(treeNode.m_nodeIdx, m_nodeStorage);     // Case 2 all other nodes.  
                        return node.ChildCount + 1;                                         // +1 is for the Referenced By Node. 
                    }
                }
            }
            public IEnumerable<object> Children(object objNode)
            {
                ITreeViewControllerNode treeNode = (ITreeViewControllerNode)objNode;
                if (treeNode.m_nodeList != null)
                {
                    // Case 1 the root node
                    for (int i = 0; i < treeNode.m_nodeList.Count; i++)
                    {
                        yield return new ITreeViewControllerNode(treeNode.m_nodeList[i], i + 1);
                    }
                }
                else
                {
                    // Case 2 all other nodes.  
                    if (treeNode.IsRefByNode)
                    {
                        RefNode node = m_refGraph.GetNode(treeNode.m_nodeIdx, m_refGraph.AllocNodeStorage());
                        int childNum = 1;
                        var nextIdx = node.GetFirstChildIndex();
                        while (nextIdx != NodeIndex.Invalid)
                        {
                            yield return new ITreeViewControllerNode(nextIdx, childNum, true);
                            nextIdx = node.GetNextChildIndex();
                            childNum++;
                        }
                    }
                    else
                    {
                        // Normal nodes.  
                        yield return new ITreeViewControllerNode(treeNode.m_nodeIdx, -1);       // Return a 'Referenced By Node'

                        Node node = m_graph.GetNode(treeNode.m_nodeIdx, m_graph.AllocNodeStorage());
                        node.ResetChildrenEnumeration();
                        int childNum = 1;
                        while (true)
                        {
                            var nextIdx = node.GetNextChildIndex();
                            if (nextIdx == NodeIndex.Invalid)
                            {
                                break;
                            }

                            yield return new ITreeViewControllerNode(nextIdx, childNum);
                            childNum++;
                        }
                    }
                }
            }

            public IList<string> ColumnNames { get { return m_columnNames; } }
            public string ColumnValue(object objNode, int columnNumber)
            {
                if (columnNumber >= 3)
                {
                    return "";
                }

                ITreeViewControllerNode treeNode = (ITreeViewControllerNode)objNode;
                if (treeNode.IsRootNode || treeNode.IsRefByNode)
                {
                    return "";
                }

                // Case 2 all other nodes.  
                MemoryNode node = (MemoryNode)m_graph.GetNode(treeNode.m_nodeIdx, m_nodeStorage);
                if (columnNumber == 0) // Value
                {
                    return "0x" + node.Address.ToString("x");
                }
                else if (columnNumber == 1) // Size
                {
                    return node.Size.ToString();
                    // return "0x" + node.Size.ToString("x");
                }
                else if (columnNumber == 2) // Type
                {
                    var type = node.GetType(m_typeStorage);
                    return type.FullName;
                }
                return "";
            }

            public void HelpForColumn(int columnNumber)
            {
                Debug.Assert(columnNumber <= 0 && columnNumber < 2);
                // TODO FIX NOW 
            }

            #region private
            /// <summary>
            /// The graph does not have object but rather indexes that represent nodes. 
            /// We fix this mismatch by creating ITreeViewControllerNode which 'wraps' the NodeIndex
            /// but also remembers enough to create a useful name for the node.  
            /// </summary>
            private class ITreeViewControllerNode
            {
                public override int GetHashCode()
                {
                    return (int)m_nodeIdx + m_childNum;
                }
                public override bool Equals(object obj)
                {
                    var asITreeViewControllerNode = obj as ITreeViewControllerNode;
                    if (asITreeViewControllerNode == null)
                    {
                        return false;
                    }

                    return m_nodeList == asITreeViewControllerNode.m_nodeList &&
                           m_childNum == asITreeViewControllerNode.m_childNum &&
                           m_nodeIdx == asITreeViewControllerNode.m_nodeIdx;
                }
                internal ITreeViewControllerNode(List<NodeIndex> nodes, int childNum)
                {
                    m_nodeList = nodes;
                    m_childNum = childNum;
                }
                internal ITreeViewControllerNode(NodeIndex nodeIdx, int childNum, bool isRefByChild = false)
                {
                    m_nodeIdx = nodeIdx;
                    m_childNum = childNum;
                    m_isRefByChild = isRefByChild;
                }
                internal string Name
                {
                    get
                    {
                        if (IsRootNode)
                        {
                            return ".";         // This is the root
                        }

                        if (IsRefByNode)
                        {
                            return "Referenced By";
                        }

                        if (m_isRefByChild)
                        {
                            return "refBy" + m_childNum.ToString();
                        }
                        else
                        {
                            return "child" + m_childNum.ToString();
                        }
                    }
                }

                internal bool IsRootNode { get { return m_childNum == 0; } }
                internal bool IsRefByNode { get { return m_childNum == -1; } }

                internal NodeIndex m_nodeIdx;               // This is the common case where a node represents a single graph node
                internal int m_childNum;
                internal bool m_isRefByChild;

                internal List<NodeIndex> m_nodeList;        // However you can also make a node that is a arbitrary list of nodes (m_childNum will always be 0)
            }

            private MemoryGraph m_graph;
            private RefGraph m_refGraph;
            private List<NodeIndex> m_focusNodes;
            private NodeType m_typeStorage;
            private Node m_nodeStorage;                             // Used for things that CAN'T be reentrant, just assume it is not in use
            private RefNode m_refNodeStorage;
            private List<string> m_columnNames;
            #endregion
        }
        #endregion

    }
}
