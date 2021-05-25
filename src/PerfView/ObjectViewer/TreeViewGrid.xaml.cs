using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using Utilities;

namespace PerfView
{
    /// <summary>
    /// TreeViewGrid if a reusable, multi-column grid view where the first column can be a tree.  That first column can be
    /// expanded like a tree view and the other columns work as expected. 
    /// 
    /// All of this is controlled by the ITreeViewController interface.  By implementing this interface, and then passing
    /// an instance of this interface to SetController, you wire up data to the TreeViewGrid.  
    /// </summary>
    public partial class TreeViewGrid : UserControl
    {
        public TreeViewGrid()
        {
            InitializeComponent();

            // Create a list of items to put in the view (will be populated in SetController() call.  
            m_flattenedTree = new ObservableCollectionEx<TreeViewGridNode>();
            Grid.ItemsSource = m_flattenedTree;

            // Make the name column have tree control behavior
            var nameColumn = Grid.Columns[0] as DataGridTemplateColumn;
            Debug.Assert(nameColumn != null);
            var template = (DataTemplate)Resources["TreeControlCell"];
            Debug.Assert(template != null);
            nameColumn.CellTemplate = template;

            // Put the indentation in when we cut and paste
            nameColumn.ClipboardContentBinding = new Binding("IndentedName");

            Grid.CopyingRowClipboardContent += delegate (object sender, DataGridRowClipboardEventArgs e)
            {
                for (int i = 0; i < e.ClipboardRowContent.Count; i++)
                {
                    var clipboardContent = e.ClipboardRowContent[i];

                    string morphedContent = null;
                    if (e.IsColumnHeadersRow)
                    {
                        morphedContent = GetColumnHeaderText(clipboardContent.Column);
                    }
                    else
                    {
                        var cellContent = clipboardContent.Content;
                        if (cellContent is float)
                        {
                            morphedContent = GoodPrecision((float)cellContent, clipboardContent.Column);
                        }
                        else if (cellContent is double)
                        {
                            morphedContent = GoodPrecision((double)cellContent, clipboardContent.Column);
                        }
                        else if (cellContent != null)
                        {
                            morphedContent = cellContent.ToString();
                        }
                        else
                        {
                            morphedContent = "";
                        }

                        morphedContent = CompressContent(morphedContent);
                    }

                    if (e.ClipboardRowContent.Count > 1)
                    {
                        morphedContent = PadForColumn(morphedContent, i + e.StartColumnDisplayIndex);
                    }

                    // TODO Ugly, morph two cells on different rows into one line for the correct cut/paste experience 
                    // for ranges.  
                    if (m_clipboardRangeEnd != m_clipboardRangeStart)  // If we have just 2 things selected (and I can tell them apart)
                    {
                        if (VeryClose(morphedContent, m_clipboardRangeStart))
                        {
                            e.ClipboardRowContent.Clear();
                            morphedContent = morphedContent + " " + m_clipboardRangeEnd;
                            e.ClipboardRowContent.Add(new DataGridClipboardCellContent(clipboardContent.Item, clipboardContent.Column, morphedContent));
                            return;
                        }
                        else if (VeryClose(morphedContent, m_clipboardRangeEnd))
                        {
                            e.ClipboardRowContent.Clear();
                            return;
                        }
                    }
                    e.ClipboardRowContent[i] = new DataGridClipboardCellContent(clipboardContent.Item, clipboardContent.Column, morphedContent);
                }
            };
        }

        // TODO FIX NOW use re remove. 
        /// <summary>
        /// Gets the object (the model   Returns null if there is no selected nodes. 
        /// </summary>
        public object SelectedNode
        {
            get
            {
                var selectedCells = Grid.SelectedCells;
                if (selectedCells.Count != 0)
                {
                    var selectedCell = selectedCells[0];
                    return selectedCell.Item;
                }
                return null;
            }
        }

        /// <summary>
        /// Typically the GUI instantiates the TreeViewGrid as part of some XAML.   However to do its job that
        /// object needs to know what data it is operating on.  This is what 'SetController' does.   By defining
        /// an object implementing ITreeViewController and passing it to this method, you populate the TreeViewGrid
        /// with data.  
        /// </summary>
        public void SetController(ITreeViewController controller)
        {
            m_controller = controller;

            // Set up the columns
            for (int i = 0; i < m_controller.ColumnNames.Count; i++)
            {
                var columnName = m_controller.ColumnNames[i];
                var columnIdx = i + 1;      // Skip the name column
                if (columnIdx < Grid.Columns.Count)
                {
                    var column = Grid.Columns[columnIdx];
                    column.Header = columnName;
                    column.Visibility = System.Windows.Visibility.Visible;
                }
            }

            List<TreeViewGridNode> newFlattenedTree = new List<TreeViewGridNode>();
            newFlattenedTree.Add(new TreeViewGridNode(this, controller.Root, null));

            // Copy over the nodes to the new flattened tree (as best we can)
            if (m_flattenedTree.Count > 0 && m_flattenedTree[0].Name == controller.Name(controller.Root))
            {
                TreeViewGridNode.CopyExpandedStateForNode(newFlattenedTree, 0, m_flattenedTree, 0);
            }

            // Destroy old nodes (to save memory because GUI keeps references to them)
            foreach (var node in m_flattenedTree)
            {
                node.Dispose();
            }

            // Update the whole tree with the new tree. 
            m_flattenedTree.ReplaceRange(0, m_flattenedTree.Count, newFlattenedTree);
            Validate();

            // Expand the root element
            m_flattenedTree[0].IsExpanded = true;
        }

        #region private
        private void Select(TreeViewGridNode item)
        {
            Grid.SelectedCells.Clear();
            Grid.SelectedItem = item;
            if (item == null)
            {
                Debug.Assert(false, "Null item selected:");
                return;
            }
            Grid.ScrollIntoView(item);

            // TODO This stuff feels like a hack.  At some point review.  
            var row = (DataGridRow)Grid.ItemContainerGenerator.ContainerFromItem(item);
            if (row != null)
            {
                row.MoveFocus(
                    new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next));
            }
        }
        private int SelectionStartIndex()
        {
            var ret = 0;
            var cells = Grid.SelectedCells;
            if (cells.Count > 0)
            {
                var cell = cells[0];

                // TODO should not have to be linear
                var list = Grid.ItemsSource as IList;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] == cell.Item)
                    {
                        return i;
                    }
                }
            }
            return ret;
        }

        /// <summary>
        /// Tries to make content smaller for cut and paste
        /// </summary>
        private string CompressContent(string content)
        {
            if (content.Length < 70)
            {
                return content;
            }

            // Trim method names !*.XXX.YYY(*) -> !XXX.YYY
            content = Regex.Replace(content, @"![\w\.]+\.(\w+\.\w+)\(.*\)", "!$1");
            if (content.Length < 70)
            {
                return content;
            }

            // Trim out generic parameters 
            for (; ; )
            {
                var result = Regex.Replace(content, @"(\w+)<[^>]+>", "$1");
                if (result == content)
                {
                    break;
                }

                content = result;
                if (content.Length < 70)
                {
                    return content;
                }
            }

            return content;
        }

        /// <summary>
        /// given the content string, and the columnIndex, return a string that is propertly padded
        /// so that when displayed the rows will line up by columns nicely  
        /// </summary>
        private string PadForColumn(string content, int columnIndex)
        {
            if (m_maxColumnInSelection == null)
            {
                m_maxColumnInSelection = new int[Grid.Columns.Count];
            }

            int maxString = m_maxColumnInSelection[columnIndex];
            if (maxString == 0)
            {
                for (int i = 0; i < m_maxColumnInSelection.Length; i++)
                {
                    m_maxColumnInSelection[i] = GetColumnHeaderText(Grid.Columns[i]).Length;
                }

                foreach (var cellInfo in Grid.SelectedCells)
                {
                    var idx = cellInfo.Column.DisplayIndex;
                    var contents = GetCellStringValue(cellInfo);
                    contents = CompressContent(contents);
                    // TODO the +1 is a bit of a hack.   In treeviews we seem to loose a space 
                    // near the checkbox making this estimate 1 too short.   
                    m_maxColumnInSelection[idx] = Math.Max(m_maxColumnInSelection[idx], contents.Length + 1);
                }
                maxString = m_maxColumnInSelection[columnIndex];
            }

            if (columnIndex == 0)
            {
                return content.PadRight(maxString);
            }
            else
            {
                return content.PadLeft(maxString);
            }
        }
        private static string GetCellStringValue(DataGridCellInfo cell)
        {
            var frameworkElement = cell.Column.GetCellContent(cell.Item);
            if (frameworkElement == null)
            {
                return "";
            }

            return GetCellStringValue(frameworkElement);
        }
        private static string GetCellStringValue(FrameworkElement contents)
        {
            string ret = Helpers.GetText(contents);
            return ret;
        }
        private static string GetColumnHeaderText(DataGridColumn column)
        {
            // TODO  I would like get the columnHeader text not from the column name but from what is displayed in the hyperlink
            var header = column.Header;
            var ret = header as string;
            if (ret == null)
            {
                var asTextBlock = column.Header as TextBlock;
                if (asTextBlock != null)
                {
                    ret = asTextBlock.Name;
                }
                else
                {
                    ret = "UNKNONWN";
                }
            }
            ret = ret.Replace("Column", "");
            return ret;
        }

        // TODO this is a bit of a hack.  remove the context menu from the textBox so that you get
        // my context menu. 
        private void Grid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
        {
            var asTextBox = e.EditingElement as TextBox;
            var window = this.AncestorOfType<StackWindow>();

            if (asTextBox != null)
            {
                EditingBox = asTextBox;
                asTextBox.ContextMenu = null;
            }
        }

        // TODO FIX NOW.  This is an ugly hack.  
        private static TextBox EditingBox;

        private static string GoodPrecision(double num, DataGridColumn column)
        {
            var format = "n3";

            string headerName = column.Header as string;
            if (headerName == null)
            {
                var header = column.Header as TextBlock;
                if (header != null)
                {
                    headerName = header.Name;
                }
            }

            if (headerName != null)
            {
                switch (headerName)
                {
                    case "ExcPercentColumn":
                    case "IncPercentColumn":
                        format = "n1";
                        break;
                    default:
                        if ((int)num == num)
                        {
                            format = "n0";
                        }

                        break;
                }
            }
            return num.ToString(format);
        }
        // TODO this is all a hack.
        private static bool VeryClose(string val1, string val2)
        {
            if (val1 == val2)
            {
                return true;
            }

            double dval1, dval2;
            if (!double.TryParse(val1, out dval1))
            {
                return false;
            }

            if (!double.TryParse(val2, out dval2))
            {
                return false;
            }

            return (dval1 == dval2);
        }

        private void SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            m_clipboardRangeStart = "";
            m_clipboardRangeEnd = "";

            var window = Helpers.AncestorOfType<StackWindow>(this);
            // If the visual tree changes, we may not get our parent.  This just give up.  
            if (window != null)
            {
                // We don't want the header for single values, or for 2 (for cutting and pasting ranges).  
                int numSelectedCells = window.SelectedCellsChanged(sender, e);
                if (numSelectedCells <= 2)
                {
                    if (numSelectedCells == 2)
                    {
                        var dataGrid = sender as DataGrid;
                        var cells = dataGrid.SelectedCells;
                        m_clipboardRangeStart = GetCellStringValue(cells[0]);
                        m_clipboardRangeEnd = GetCellStringValue(cells[1]);
                    }
                    Grid.ClipboardCopyMode = DataGridClipboardCopyMode.ExcludeHeader;
                }
                else
                {
                    Grid.ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader;
                }
            }
            m_maxColumnInSelection = null;
        }
        private void DoHyperlinkHelp(object sender, System.Windows.RoutedEventArgs e)
        {
            var asHyperLink = sender as Hyperlink;
            if (asHyperLink != null)
            {
                MainWindow.DisplayUsersGuide((string)asHyperLink.Tag);
            }
        }

        /// <summary>
        /// If we have only two cells selected, even if they are on differnet rows we want to morph them
        /// to a single row.  These variables are for detecting this situation.  
        /// </summary>
        private string m_clipboardRangeStart;
        private string m_clipboardRangeEnd;
        private int[] m_maxColumnInSelection;

        [Conditional("DEBUG")]
        public void Validate()
        {
#if DEBUG
            Debug.Assert(m_flattenedTree.Count > 0);
            Debug.Assert(m_flattenedTree[0].m_depth == 0);
            Debug.Assert(m_flattenedTree[0].Data.Equals(m_controller.Root));

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
                var callees = m_controller.Children(node.Data);
                Debug.Assert(callees != null);
                foreach (var callee in callees)
                {
                    var calleeViewNode = m_flattenedTree[curViewNodeIndex];
                    Debug.Assert(calleeViewNode.m_depth == node.m_depth + 1);
                    Debug.Assert(calleeViewNode.Data.Equals(callee));
                    curViewNodeIndex = ValidateChildren(curViewNodeIndex, maxDepth - 1);
                }
            }
            Debug.Assert(curViewNodeIndex >= m_flattenedTree.Count ||
                    m_flattenedTree[curViewNodeIndex].m_depth <= node.m_depth);
            return curViewNodeIndex;
        }
#endif

#if false // TODO FIX NOW use or remove
        /// <summary>
        /// Given a TreeViewGridNode, find a TreeViewGridNode for it (insuring that it is displayed)
        /// </summary>
        private TreeViewGridNode InsureVisible(object treeNode)
        {
            if (treeNode == null)
                return m_flattenedTree[0];

            throw new NotImplementedException();
            TreeViewGridNode caller = InsureVisible(treeNode.Caller);
            if (caller == null)         // should never happen, but we can fall back to giving up. 
                return null;
            caller.IsExpanded = true;
            caller.ValidateTree();

            int callerPos = caller.MyIndex + 1;
            while (callerPos < m_flattenedTree.Count)
            {
                var child = m_flattenedTree[callerPos];
                if (child.m_depth <= caller.m_depth)
                    break;

                if (child.Data == treeNode)
                    return child;

                callerPos++;
            }
            Debug.Assert(false, "Should have found call node");
            return null;
        }

        // Iterates through TreeViewGridNodes in preorder (parent first, then children), this 'flattens' the tree.  
        private TreeViewGridNode NextNode(TreeViewGridNode node)
        {
            // After me is my first child (if it exists)

            // In the search, we assume that graph nodes have no children.  This avoids infinite search.  
            if (!node.IsGraphNode)
            {
                var callees = callees;
                if (callees != null && callees.Count > 0)
                    return callees[0];
            }

            // Otherwise it is my next sibling
            while (node != null)
            {
                var nextSibling = NextSibling(node);
                if (nextSibling != null)
                    return nextSibling;
                node = node.Caller;
            }
            return null;
        }
        private TreeViewGridNode NextSibling(TreeViewGridNode node)
        {
            var parent = node.Caller;
            if (parent == null)
                return null;
            int nextIndex = IndexInParent(node) + 1;
            var parentCallees = DisplayCallees(parent);

            if (nextIndex >= parentCallees.Count)
                return null;
            return parentCallees[nextIndex];
        }
        /// <summary>
        /// Find the sampleIndex in the 'Callees' list of the parent for 'node'
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        private int IndexInParent(TreeViewGridNode node)
        {
            // TODO avoid search 
            var callerCallees = DisplayCallees(node.Caller);
            int i = 0;
            while (i < callerCallees.Count)
            {
                var callee = callerCallees[i];
                if (callee == node)
                    break;
                i++;
            }
            return i;
        }
#endif

        /// <summary>
        /// This is basically a TreeViewGridNode with extra state (state of expand boxes) associated needed for the viewer 
        /// </summary>
        private class TreeViewGridNode
        {
            /// <summary>
            /// Is the node expanded or not.  
            /// </summary>
            public bool IsExpanded
            {
                get { return m_isExpanded; }
                set
                {
                    m_treeView.Validate();

                    if (m_isExpanded == value)
                    {
                        return;
                    }

                    if (value == true)
                    {

                        // We are trying to expand the treeNode, add the children after me. 
                        var children = MakeChildren();
                        m_treeView.m_flattenedTree.InsertRange(MyIndex + 1, children);
                        m_isExpanded = true;

                        m_treeView.Select(this);         // We want expanding the node to select the node
                    }
                    else
                    {
                        // Get the index ranges of all my children. 
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

                        // And remove them from the array.  
                        m_treeView.m_flattenedTree.RemoveRange(firstChild, lastChild - firstChild);
                        m_isExpanded = false;
                    }
                    m_treeView.Validate();
                }
            }

            // The actual data object that this node represents.   These object get passed to the ITreeViewController to get information 
            // about them.  
            public object Data { get; set; }

            /// <summary>
            /// The name of the node in the tree
            /// </summary>
            public string Name { get { return m_treeView.m_controller.Name(Data); } }

            /// <summary>
            /// This is IndentString followed by the display name.   It is what the view binds to (what is desiplayed in the Name column)
            /// </summary>
            public string IndentedName { get { return (IndentString + " " + Name); } }
            /// <summary>
            /// Creates a string that has spaces | and + signs that represent the indentation level 
            /// for the tree node.  (Called from XAML)
            /// </summary>
            public string IndentString
            {
                get
                {
                    if (m_indentString == null)
                    {
                        var chars = new char[m_depth];
                        var i = m_depth - 1;
                        if (0 <= i)
                        {
                            chars[i] = '+';
                            var ancestor = m_parent;
                            --i;
                            while (i >= 0)
                            {
                                chars[i] = ancestor.m_isLastChild ? ' ' : '|';
                                ancestor = ancestor.m_parent;
                                --i;
                            }
                        }
                        m_indentString = new string(chars);
                    }
                    return m_indentString;
                }
            }
            /// <summary>
            /// Does this node have any children (invisible (unexpanded) children count))
            /// </summary>
            public virtual bool HasChildren
            {
                get
                {
                    return 0 < m_treeView.m_controller.ChildCount(Data);
                }
            }

            /// <summary>
            /// Returns the list of code:TreeViewGridNode associated with the children of this node (thus invisible nodes are not present).   
            /// </summary>
            public IList<TreeViewGridNode> VisibleChildren
            {
                get
                {
                    List<TreeViewGridNode> ret = new List<TreeViewGridNode>();
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

            /// <summary>
            /// Set 'IsExpanded of all nodes to a certain depth.  
            /// </summary>
            /// <param name="maxDepth">Maximum depth to expand</param>
            /// <param name="expandGraphNodes">If true graph nodes (which are not guaranteed to terminate) are expanded. </param>
            public void ExpandToDepth(int maxDepth, bool expandGraphNodes = false)
            {
                if (maxDepth == 0)
                {
                    return;
                }

                IsExpanded = true;
                foreach (var child in VisibleChildren)
                {
                    child.ExpandToDepth(maxDepth - 1, expandGraphNodes);
                }
            }

            // The properties are for binding in the GUI.   
            // set property is a hack to allow selection in the GUI (which wants two way binding for that case)
            public string DisplayField1 { get { return m_treeView.m_controller.ColumnValue(Data, 0); } set { } }
            public string DisplayField2 { get { return m_treeView.m_controller.ColumnValue(Data, 1); } set { } }
            public string DisplayField3 { get { return m_treeView.m_controller.ColumnValue(Data, 2); } set { } }
            public string DisplayField4 { get { return m_treeView.m_controller.ColumnValue(Data, 2); } set { } }
            public string DisplayField5 { get { return m_treeView.m_controller.ColumnValue(Data, 2); } set { } }
            public string DisplayField6 { get { return m_treeView.m_controller.ColumnValue(Data, 2); } set { } }


#if DEBUG   // WPF calls the ToString nodes sometimes to do automation.   Don't waste time in retail builds!
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
            internal TreeViewGridNode(TreeViewGrid treeView, object data, TreeViewGridNode parent)
            {
                m_treeView = treeView;
                Data = data;
                m_isExpanded = !HasChildren;
                m_parent = parent;
                if (parent != null)
                {
                    m_depth = parent.m_depth + 1;
                }
            }

            /// <summary>
            /// Returns the sampleIndex in the flattened tree (m_flattenedTree) of this tree node.  
            /// </summary>
            internal int MyIndex
            {
                get
                {
                    // See if our cached index is still accurate. 
                    if (m_treeView.m_flattenedTree.Count <= m_indexGuess || !m_treeView.m_flattenedTree[m_indexGuess].Equals(this))
                    {
                        if (m_parent != null)
                        {
                            // It is not, so recalculate it by getting my parent, and looking in the array right after it (all its children)
                            m_indexGuess = m_parent.MyIndex + 1;
                            while (!m_treeView.m_flattenedTree[m_indexGuess].Equals(this))
                            {
                                Debug.Assert(m_depth <= m_treeView.m_flattenedTree[m_indexGuess].m_depth);
                                m_indexGuess = m_indexGuess + 1;
                                Debug.Assert(m_indexGuess < m_treeView.m_flattenedTree.Count);
                            }
                        }
                        else
                        {
                            Debug.Assert(m_indexGuess == 0);
                        }
                    }
                    // We must find ourselves!
                    Debug.Assert(m_treeView.m_flattenedTree[m_indexGuess].Equals(this));
                    return m_indexGuess;
                }
            }
            /// <summary>
            /// An Unexpanded TreeViewGridNode does not have any children even if the Data (TreeViewGridNode) does
            /// This routine will make the necessary children (it is part of expanding the node).  
            /// </summary>
            private List<TreeViewGridNode> MakeChildren()
            {
                Debug.Assert(HasChildren);
                var ret = new List<TreeViewGridNode>();
                TreeViewGridNode lastChild = null;
                foreach (var modelNode in m_treeView.m_controller.Children(Data))
                {
                    lastChild = new TreeViewGridNode(m_treeView, modelNode, this);
                    ret.Add(lastChild);
                }
                lastChild.m_isLastChild = true;
                return ret;
            }

            /// <summary>
            /// It is assumed that the node oldFlattenedTree[oldIndex] and newFlattenedTree[newIndex] correspond 
            /// to one another (have the same path to root) 
            /// Copies the expandedness of the node 'oldFlattenedTree[oldIndex]' to the new node at 
            /// newFlattenedTree[newIndex], as well as all the state for child node.  
            /// </summary>
            internal static void CopyExpandedStateForNode(List<TreeViewGridNode> newFlattenedTree, int newIndex,
                ObservableCollectionEx<TreeViewGridNode> oldFlattenedTree, int oldIndex)
            {
                Debug.Assert(newIndex == newFlattenedTree.Count - 1);
                TreeViewGridNode oldNode = oldFlattenedTree[oldIndex];
                if (oldNode.m_isExpanded)
                {
                    TreeViewGridNode newNode = newFlattenedTree[newIndex];
                    Debug.Assert(newNode.Name == oldNode.Name);
                    newNode.m_isExpanded = true;
                    if (newNode.HasChildren)
                    {
                        var children = newNode.MakeChildren();
                        for (int i = 0; i < children.Count; i++)
                        {
                            var newChild = children[i];
                            newFlattenedTree.Add(newChild);
                            int oldChildIndex = FindChild(oldFlattenedTree, oldNode, oldIndex, newChild.Name);
                            if (oldChildIndex >= 0)
                            {
                                CopyExpandedStateForNode(newFlattenedTree, newFlattenedTree.Count - 1, oldFlattenedTree, oldChildIndex);
                            }
                        }
                    }
                }
            }

            /// <summary>
            /// Given a node == flattenedTree[sampleIndex] find the sampleIndex in flattenedTree of a child with Name == 'name'
            /// </summary>
            private static int FindChild(ObservableCollectionEx<TreeViewGridNode> flattenedTree, TreeViewGridNode node, int index, string name)
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

                    if (child.m_depth == childDepth && child.Name == name)
                    {
                        return childIndex;
                    }
                }
                return -1;
            }

            private TreeViewGrid m_treeView;                        // The view represents the 'root' of the entire tree (owns m_flattenedTree). 
            internal bool m_isExpanded;                     // Is this node expanded.  
            private string m_indentString;                          // The + and | that make it look like a tree. 
            private int m_indexGuess;                               // Where we think we are in the flattened tree, may not be accurate but worth checking  

            internal int m_depth;                           // My nesting level from root.   (root == 0);
            private bool m_isLastChild;                             // Am I the last child of my parent.  
            private TreeViewGridNode m_parent;                      // My parent.  
            #endregion
        }


        private ObservableCollectionEx<TreeViewGridNode> m_flattenedTree;      // The list that the GridControl displays
        private ITreeViewController m_controller;                              // Describes the data model  (tree can columns)
        #endregion
    }

    /// <summary>
    /// A TreeViewController allow contains all the necessary callback that the TreeView needs to perform its job.
    /// This is what hooks up the TreeView to its model.   Basically it lets you define what the name of
    /// each node of the tree is as well as how to get the children of a node (as well as the root node).  
    /// 
    /// It also lets you get a the column names and values.    Note that what node is represented by is
    /// completely arbitrary (it can be any object).  All that is necessary is that implementer of this
    /// interface know what to do with that object to get the necessary data.  
    /// </summary>
    public interface ITreeViewController
    {
        // Getting the root (starting point)
        object Root { get; }

        // Getting the tree info about a node (its name and children) 
        string Name(object node);
        int ChildCount(object node);
        IEnumerable<object> Children(object node);

        // Getting the other columns associated with the node.  
        IList<string> ColumnNames { get; }
        string ColumnValue(object node, int columnNumber);

        // GUI support.  
        void HelpForColumn(int columnNumber);
    }
}
