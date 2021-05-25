using Microsoft.Diagnostics.Tracing.Stacks;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace PerfView
{
    public class CallTreeView : IDisposable
    {
        public CallTreeView(PerfDataGrid perfGrid, DataTemplate template)
        {
            m_flattenedTree = new ObservableCollectionEx<CallTreeViewNode>();
            m_perfGrid = perfGrid;
            m_perfGrid.Grid.ItemsSource = m_flattenedTree;
            DisplayPrimaryOnly = true;          // we assume we are a non-memory window, which has no secondary.  

            // Make the name column have tree control behavior
            var nameColumn = perfGrid.Grid.Columns[0] as DataGridTemplateColumn;
            Debug.Assert(nameColumn != null);
            nameColumn.CellTemplate = template;

            // Put the indentation in when we cut and paste
            nameColumn.ClipboardContentBinding = new Binding("IndentedName");
        }
        /// <summary>
        /// Returns the root node of the calltree being displayed
        /// </summary>
        public CallTreeNode Root { get { return m_root; } }
        /// <summary>
        /// I did not make this a property because it is too profound an operation (heavy)
        /// This sets the root of the tree.  This is how you change the display to a new tree.  
        /// </summary>
        public void SetRoot(CallTreeNode root)
        {
            List<CallTreeViewNode> newFlattenedTree = new List<CallTreeViewNode>();
            newFlattenedTree.Add(new CallTreeViewNode(this, root, 0));

            // Copy over the nodes to the new flattened tree (as best we can)
            if (m_flattenedTree.Count > 0 && m_flattenedTree[0].Data.DisplayName == root.DisplayName)
            {
                CallTreeViewNode.CopyExpandedStateForNode(newFlattenedTree, 0, m_flattenedTree, 0);
            }

            // Destroy old nodes (to save memory because GUI keeps references to them)
            foreach (var node in m_flattenedTree)
            {
                node.Dispose();
            }

            // Update the whole tree with the new tree. 
            m_flattenedTree.ReplaceRange(0, m_flattenedTree.Count, newFlattenedTree);
            Validate();
            m_root = root;
            m_curPosition = null;
            m_endPosition = null;

            // Expand the root element
            var rootView = InsureVisible(m_root);
            rootView.IsExpanded = true;
        }
        /// <summary>
        /// Finds the .NET Regular expression 'pat' (case insensitive) in the call tree (does not matter if the node is visible or not).  
        /// Returns true if found.  
        /// </summary>
        public bool Find(string pat)
        {
            if (pat == null)
            {
                m_findPat = null;
                return true;
            }

            int startPos = m_perfGrid.SelectionStartIndex();
            if (m_flattenedTree.Count <= startPos)
            {
                return false;
            }

            m_curPosition = m_flattenedTree[startPos].Data;

            m_perfGrid.Focus();
            var startingNewSearch = false;
            if (m_findPat == null || m_findPat.ToString() != pat)
            {
                try
                {
                    m_findPat = new Regex(pat, RegexOptions.IgnoreCase);    // TODO perf bad if you compile!
                }
                catch (ArgumentException e)
                {
                    throw new ApplicationException("Bad regular expression: " + e.Message);
                }

                m_endPosition = m_curPosition;
                startingNewSearch = true;
            }

            for (; ; )
            {
                if (startingNewSearch)
                {
                    startingNewSearch = false;
                }
                else
                {
                    m_curPosition = NextNode(m_curPosition);
                    if (m_curPosition == null)
                    {
                        m_curPosition = m_root;
                    }

                    if (m_curPosition == m_endPosition)
                    {
                        m_findPat = null;
                        return false;
                    }
                }
                if (m_findPat.IsMatch(m_curPosition.DisplayName))
                {
                    Select(m_curPosition);
                    return true;
                }
            }
        }
        /// <summary>
        /// Gets the first CallTreeViewNode for selected cells.   Returns null if there is no selected nodes. 
        /// </summary>
        public CallTreeViewNode SelectedNode
        {
            get
            {
                var selectedCells = m_perfGrid.Grid.SelectedCells;
                if (selectedCells.Count != 0)
                {
                    var selectedCell = selectedCells[0];
                    return (CallTreeViewNode)selectedCell.Item;
                }
                return null;
            }
        }
        /// <summary>
        /// Highlights (Selects) the given node.   This is how you navigate.  
        /// </summary>
        public bool Select(CallTreeNode node)
        {
            var viewNode = InsureVisible(node);
            Debug.Assert(viewNode != null);
            Debug.Assert(m_flattenedTree.IndexOf(viewNode) >= 0);
            if (viewNode == null)
            {
                return false;
            }

            m_perfGrid.Select(viewNode);
            return true;
        }
        /// <summary>
        /// If this set only primary nodes are displayed.   
        /// </summary>
        public bool DisplayPrimaryOnly { get; set; }

        public void Dispose()
        {
            m_root.FreeMemory();
        }
        #region private
        internal IList<CallTreeNode> DisplayCallees(CallTreeNode node)
        {
            if (DisplayPrimaryOnly)
            {
                return node.Callees;
            }
            else
            {
                return node.AllCallees;
            }
        }


        [Conditional("DEBUG")]
        public void Validate()
        {
#if DEBUG
            Debug.Assert(m_flattenedTree.Count > 0);
            Debug.Assert(m_flattenedTree[0].m_depth == 0);

            int ret = ValidateChildren(0, 1000);
            Debug.Assert(ret == m_flattenedTree.Count);
#endif
        }

#if DEBUG
        public int ValidateChildren(int nodeIndex, int maxDepth)
        {
            Debug.Assert(maxDepth > 0);
            var node = m_flattenedTree[nodeIndex];
            int curViewNodeIndex = nodeIndex + 1;
            if (node.m_isExpanded)
            {
                var callees = DisplayCallees(node.Data);
                if (callees != null)
                {
                    for (int i = 0; i < callees.Count; i++)
                    {
                        var calleeViewNode = m_flattenedTree[curViewNodeIndex];
                        Debug.Assert(calleeViewNode.m_depth == node.m_depth + 1);
                        Debug.Assert(calleeViewNode.Data == callees[i]);
                        curViewNodeIndex = ValidateChildren(curViewNodeIndex, maxDepth-1);
                    }
                }
            }
            Debug.Assert(curViewNodeIndex >= m_flattenedTree.Count ||
                    m_flattenedTree[curViewNodeIndex].m_depth <= node.m_depth);
            return curViewNodeIndex;
        }
#endif

        /// <summary>
        /// Given a CallTreeNode, find a CallTreeViewNode for it (insuring that it is displayed
        /// </summary>
        private CallTreeViewNode InsureVisible(CallTreeNode treeNode)
        {
            if (treeNode.Caller == null)
            {
                return m_flattenedTree[0];
            }

            CallTreeViewNode caller = InsureVisible(treeNode.Caller);
            if (caller == null)         // should never happen, but we can fall back to giving up. 
            {
                return null;
            }

            caller.IsExpanded = true;
            caller.ValidateTree();

            int callerPos = caller.MyIndex + 1;
            while (callerPos < m_flattenedTree.Count)
            {
                var child = m_flattenedTree[callerPos];
                if (child.m_depth <= caller.m_depth)
                {
                    break;
                }

                if (child.Data == treeNode)
                {
                    return child;
                }

                callerPos++;
            }
            Debug.Assert(false, "Should have found call node");
            return null;
        }

        // Iterates through CallTreeNodes in preorder (parent first, then children), this 'flattens' the tree.  
        private CallTreeNode NextNode(CallTreeNode node)
        {
            // After me is my first child (if it exists)

            // In the search, we assume that graph nodes have no children.  This avoids infinite search.  
            if (!node.IsGraphNode)
            {
                var callees = DisplayCallees(node);
                if (callees != null && callees.Count > 0)
                {
                    return callees[0];
                }
            }

            // Otherwise it is my next sibling
            while (node != null)
            {
                var nextSibling = NextSibling(node);
                if (nextSibling != null)
                {
                    return nextSibling;
                }

                node = node.Caller;
            }
            return null;
        }
        private CallTreeNode NextSibling(CallTreeNode node)
        {
            var parent = node.Caller;
            if (parent == null)
            {
                return null;
            }

            int nextIndex = IndexInParent(node) + 1;
            var parentCallees = DisplayCallees(parent);

            if (nextIndex >= parentCallees.Count)
            {
                return null;
            }

            return parentCallees[nextIndex];
        }
        /// <summary>
        /// Find the sampleIndex in the 'Callees' list of the parent for 'node'
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        private int IndexInParent(CallTreeNode node)
        {
            // TODO avoid search 
            var callerCallees = DisplayCallees(node.Caller);
            int i = 0;
            while (i < callerCallees.Count)
            {
                var callee = callerCallees[i];
                if (callee == node)
                {
                    break;
                }

                i++;
            }
            return i;
        }

        private CallTreeNode m_root;                                                    // The base of the tree being displayed
        internal PerfDataGrid m_perfGrid;                                       // The GridControl where we display the data (depends on check-boxes)
        internal ObservableCollectionEx<CallTreeViewNode> m_flattenedTree;      // The list that the GridControl displays

        // Find Support 
        private Regex m_findPat;                    // The pattern that FindNext will look for
        private CallTreeNode m_curPosition;         // The position the selection (where FindNext starts) 
        private CallTreeNode m_endPosition;         // The position where FindNext will stop searching (it wrapped around).  

        #endregion
    }

    /// <summary>
    /// This is basically a CallTreeNode with extra state (state of expand boxes) associated needed for the viewer 
    /// </summary>
    public class CallTreeViewNode
    {
        [Conditional("DEBUG")]
        public void ValidateTree()
        {
#if DEBUG
            m_treeView.Validate();
#endif
        }

        public void SetBackgroundColor(System.Drawing.Color color)
        {
            BackgroundColor = color.Name;
        }

        public string BackgroundColor { get; set; }

        /// <summary>
        /// Is the node expanded or not.  
        /// </summary>
        public bool IsExpanded
        {
            get { return m_isExpanded; }
            set
            {
                if (m_isExpanded == value)
                {
                    return;
                }

                if (value == true)
                {
                    ExpandNode();
                }
                else
                {
                    CollapseNode();
                }
            }
        }

        private void ExpandNode(bool selectExpandedNode = true)
        {
            if (m_isExpanded)
            {
                return;
            }

            ValidateTree();

            // We are trying to expand the treeNode, add the children after me. 
            var children = MakeChildren();
            m_treeView.m_flattenedTree.InsertRange(MyIndex + 1, children);
            m_isExpanded = true;

            ValidateTree();
            // Auto expand nodes that have only one real child.  (Don't do this for graph nodes as it may not terminate.  
            if (children.Count == 1 && !Data.IsGraphNode)
            {
                var onlyChild = children[0];
                if (onlyChild.HasChildren)
                {
                    onlyChild.ExpandNode(selectExpandedNode);
                }
            }
            else if (selectExpandedNode)
            {
                m_treeView.m_perfGrid.Select(this);         // We want expanding the node to select the node
            }

            ValidateTree();
        }

        private void CollapseNode()
        {
            if (!m_isExpanded)
            {
                return;
            }

            ValidateTree();

            int firstChild = MyIndex + 1;
            int lastChild = firstChild;
            int myDepth = m_depth;
            while (lastChild < m_treeView.m_flattenedTree.Count)
            {
                if (m_treeView.m_flattenedTree[lastChild].m_depth <= myDepth)
                {
                    break;
                }

                lastChild++;
            }

            m_treeView.m_flattenedTree.RemoveRange(firstChild, lastChild - firstChild);
            m_isExpanded = false;

            // set the selected node to my caller (if available) or myself if there is none.  
            m_treeView.Select(Data.Caller ?? Data);

            ValidateTree();
        }

        // The actual Tree node that this OpenStacks node represents.  
        public CallTreeNode Data { get; set; }
        /// <summary>
        /// The TreeVieeNode uses the nodes DisplayName (which has a suffix string (surrounded by []) with extra information. 
        /// </summary>
        public string Name { get { return Data.DisplayName; } }        /// <summary>
                                                                       /// This is IndentString followed by the display name.  
                                                                       /// </summary>
        public string IndentedName { get { return (IndentString + " " + Data.DisplayName); } }
        /// <summary>
        /// Creates a string that has spaces | and + signs that represent the indentation level 
        /// for the tree node.  (Called from XAML)
        /// </summary>
        public string IndentString { get { return Data.IndentString(m_treeView.DisplayPrimaryOnly); } }
        /// <summary>
        /// Does this node have any children (invisible (unexpanded) children count))
        /// </summary>
        public virtual bool HasChildren
        {
            get
            {
                if (m_treeView.DisplayPrimaryOnly)
                {
                    var callees = Data.Callees;
                    return callees != null && callees.Count > 0;
                }
                else
                {
                    return Data.HasChildren;
                }
            }
        }

        /// <summary>
        /// A weak child is secondary
        /// </summary>
        public bool IsSecondaryChild { get; set; }

        // TODO FIX NOW, put this in the XAML instead
        public FontWeight FontWeight
        {
            get
            {
                if (!m_treeView.DisplayPrimaryOnly && !IsSecondaryChild)
                {
                    return FontWeights.Bold;
                }

                return FontWeights.Normal;
            }
        }
        public Visibility VisibleIfDisplayingSecondary
        {
            get
            {
                if (m_treeView.DisplayPrimaryOnly)
                {
                    return Visibility.Collapsed;
                }
                else
                {
                    return Visibility.Visible;
                }
            }
        }

        // TODO FIX NOW use or remove 
#if false 
        public InlineCollection NameContent
        {
            get
            {
                var textBlock = new TextBlock();
                var hyperlink = new Hyperlink(new Run("testing"));
                hyperlink.Tag = "hello";
                hyperlink.Click = null;
            }
        }
#endif
        /// <summary>
        /// Returns the list of code:CallTreeViewNode (rather than just code:CallTreeNode) associated
        /// with the children of this node (thus invible nodes are not present).   
        /// </summary>
        public IList<CallTreeViewNode> VisibleChildren
        {
            get
            {
                List<CallTreeViewNode> ret = new List<CallTreeViewNode>();
                int i = MyIndex + 1;
                while (i < m_treeView.m_flattenedTree.Count)
                {
                    var node = m_treeView.m_flattenedTree[i];
                    if (node.m_depth <= m_depth)
                    {
                        break;
                    }

                    if (node.m_depth == m_depth + 1)
                    {
                        ret.Add(node);
                    }

                    i++;
                }
                return ret;
            }
        }
        public float InclusiveMetric { get { return Data.InclusiveMetric; } }
        public float AverageInclusiveMetric { get { return Data.AverageInclusiveMetric; } }
        public float ExclusiveMetric { get { return Data.ExclusiveMetric; } }
        public float InclusiveCount { get { return Data.InclusiveCount; } }
        public float ExclusiveCount { get { return Data.ExclusiveCount; } }
        public float ExclusiveFoldedCount { get { return Data.ExclusiveFoldedCount; } }
        public float ExclusiveFoldedMetric { get { return Data.ExclusiveFoldedMetric; } }
        public float InclusiveMetricPercent { get { return Data.InclusiveMetricPercent; } }
        public float ExclusiveMetricPercent { get { return Data.ExclusiveMetricPercent; } }
        public Histogram InclusiveMetricByTime { get { return Data.InclusiveMetricByTime; } }
        public string InclusiveMetricByTimeString
        {
            get { return Data.InclusiveMetricByTimeString; }
            set { } // TODO See if there is a better way of getting the GUI working.  
        }
        public Histogram InclusiveMetricByScenario { get { return Data.InclusiveMetricByScenario; } }
        public string InclusiveMetricByScenarioString
        {
            get { return Data.InclusiveMetricByScenarioString; }
            set { }
        }

        public double FirstTimeRelativeMSec { get { return Data.FirstTimeRelativeMSec; } }
        public double LastTimeRelativeMSec { get { return Data.LastTimeRelativeMSec; } }
        public double DurationMSec { get { return Data.DurationMSec; } }

        /// <summary>
        /// Set 'IsExpanded of all nodes to a certain depth.  
        /// </summary>
        /// <param name="maxDepth">Maximum depth to expand</param>
        /// <param name="expandGraphNodes">If true graph nodes (which are not guarnteed to terminate) are expanded. </param>
        public void ExpandToDepth(int maxDepth, bool expandGraphNodes = false, bool selectExpandedNode = true)
        {
            if (maxDepth == 0)
            {
                return;
            }

            if (!expandGraphNodes && Data.IsGraphNode)
            {
                return;
            }

            ExpandNode(selectExpandedNode);

            foreach (var child in VisibleChildren)
            {
                child.ExpandToDepth(maxDepth - 1, expandGraphNodes, selectExpandedNode);
            }
        }


#if DEBUG   // WPF calles the ToString nodes sometimes to do automation.   Don't waste time in retail builds!
        public override string ToString()
        {

            if (Data == null)
                return "";
            return Data.ToString();
        }
#endif
        public virtual void Dispose()
        {
            m_treeView = null;
            Data = null;
        }
        #region private
        internal CallTreeViewNode(CallTreeView treeView, CallTreeNode data, int depth)
        {
            m_treeView = treeView;
            Data = data;
            m_isExpanded = !HasChildren;
            m_depth = depth;
        }

        /// <summary>
        /// Returns the sampleIndex in the flattened tree (m_flattenedTree) of this tree node.  
        /// </summary>
        internal int MyIndex
        {
            get
            {
                if (m_treeView.m_flattenedTree.Count <= m_indexGuess || m_treeView.m_flattenedTree[m_indexGuess] != this)
                {
                    m_indexGuess = m_treeView.m_flattenedTree.IndexOf(this);
                }

                Debug.Assert(m_indexGuess >= 0);
                return m_indexGuess;
            }
        }
        /// <summary>
        /// An Unexpanded CallTreeViewNode does not have any children even if the Data (CallTreeNode) does
        /// This routine will make the necessary children (it is part of expanding the node).  
        /// </summary>
        private List<CallTreeViewNode> MakeChildren()
        {
            var callees = m_treeView.DisplayCallees(Data);
            Debug.Assert(callees != null);
            var ret = new List<CallTreeViewNode>(callees.Count);
            for (int i = 0; i < callees.Count; i++)
            {
                CallTreeNode elem = callees[i];
                var newNode = new CallTreeViewNode(m_treeView, elem, m_depth + 1);

                if (IsSecondaryChild || elem.IsGraphNode)
                {
                    newNode.IsSecondaryChild = true;
                }

                ret.Add(newNode);
            }
            return ret;
        }

        /// <summary>
        /// It is assumed that the node oldFlattenedTree[oldIndex] and newFlattenedTree[newIndex] coorespond 
        /// to one another (have the same path to root) 
        /// Copies the expandedness of the node 'oldFlattenedTree[oldIndex]' to the new node at 
        /// newFlattenedTree[newIndex], as well as all the state for child node.  
        /// </summary>
        internal static void CopyExpandedStateForNode(List<CallTreeViewNode> newFlattenedTree, int newIndex,
            ObservableCollectionEx<CallTreeViewNode> oldFlattenedTree, int oldIndex)
        {
            Debug.Assert(newIndex == newFlattenedTree.Count - 1);
            CallTreeViewNode oldNode = oldFlattenedTree[oldIndex];
            if (oldNode.m_isExpanded)
            {
                CallTreeViewNode newNode = newFlattenedTree[newIndex];
                Debug.Assert(newNode.Data.DisplayName == oldNode.Data.DisplayName);
                newNode.m_isExpanded = true;
                if (newNode.HasChildren)
                {
                    var children = newNode.MakeChildren();
                    for (int i = 0; i < children.Count; i++)
                    {
                        var newChild = children[i];
                        newFlattenedTree.Add(newChild);

                        // This is N squared so can take a long time if there are many children 
                        // We can simply give up in that case.  
                        if (i < 50)
                        {
                            int oldChildIndex = FindChild(oldFlattenedTree, oldNode, oldIndex, newChild.Data.DisplayName);
                            if (oldChildIndex >= 0)
                            {
                                CopyExpandedStateForNode(newFlattenedTree, newFlattenedTree.Count - 1, oldFlattenedTree, oldChildIndex);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Given a node == flattenedTree[sampleIndex] find the sampleIndex in flattenedTree of a child with Data.NameBase == 'nameBame'
        /// </summary>
        private static int FindChild(ObservableCollectionEx<CallTreeViewNode> flattenedTree, CallTreeViewNode node, int index, string nameBase)
        {
            Debug.Assert(flattenedTree[index] == node);
            int childDepth = node.m_depth + 1;
            int childIndex = index;
            for (; ; )
            {
                childIndex++;
                if (childIndex >= flattenedTree.Count)
                {
                    break;
                }

                var child = flattenedTree[childIndex];
                if (child.m_depth < childDepth)
                {
                    break;
                }

                if (child.m_depth == childDepth && child.Data.DisplayName == nameBase)
                {
                    return childIndex;
                }
            }
            return -1;
        }

        private CallTreeView m_treeView;                        // The view represents the 'root' of the entire tree (owns m_flattenedTree). 
        internal bool m_isExpanded;                     // Is this node expanded.  
        private int m_indexGuess;                               // Where we think we are in the flattened tree, may not be accurate but wortch checking  
        internal int m_depth;                           // My nesting level from root.   (root == 0);
        #endregion
    }

    #region private
    /// <summary>
    /// An observable colletion with the ability to insert a range of nodes without being super-inefficient.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal class ObservableCollectionEx<T> : ObservableCollection<T>
    {
        public void ReplaceRange(int index, int count, IEnumerable<T> collection)
        {
            CheckReentrancy();
            var asList = Items as List<T>;
            asList.RemoveRange(index, count);
            asList.InsertRange(index, collection);
            OnPropertyChanged(new PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
        public void InsertRange(int index, IEnumerable<T> collection)
        {
            CheckReentrancy();
            var asList = Items as List<T>;
            asList.InsertRange(index, collection);
            OnPropertyChanged(new PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
        public void RemoveRange(int index, int count)
        {
            if (count == 0)
            {
                return;
            }

            CheckReentrancy();
            var asList = Items as List<T>;
            asList.RemoveRange(index, count);
            OnPropertyChanged(new PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
    #endregion
}
