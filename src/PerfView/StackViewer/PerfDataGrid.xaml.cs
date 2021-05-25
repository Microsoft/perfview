using Microsoft.Diagnostics.Tracing.Stacks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Utilities;

namespace PerfView
{
    /// <summary>
    /// Interaction logic for PerfDataGrid.xaml
    /// </summary>
    public partial class PerfDataGrid : UserControl
    {
        public static bool NoPadOnCopyToClipboard = false;

        public PerfDataGrid()
        {
            InitializeComponent();
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

                    // Pad so that pasting into a text window works well. 
                    if (e.ClipboardRowContent.Count > 1 && !NoPadOnCopyToClipboard)
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

            // By default sort columns in descending order when they are clicked for the first time
            Grid.Sorting +=
                (sender, e) =>
                {
                    if (e.Column.SortDirection == null)
                    {
                        e.Column.SortDirection = ListSortDirection.Ascending;
                    }

                    e.Handled = false;
                };
        }

        public bool Find(string pat)
        {
            if (pat == null)
            {
                m_findPat = null;
                return true;
            }

            Grid.Focus();
            int curPos = SelectionStartIndex();
            var startingNewSearch = false;
            if (m_findPat == null || m_findPat.ToString() != pat)
            {
                startingNewSearch = true;
                m_FindEnd = curPos;
                try
                {
                    m_findPat = new Regex(pat, RegexOptions.IgnoreCase);    // TODO perf bad if you compile!
                }
                catch (ArgumentException e)
                {
                    throw new ApplicationException("Bad regular expression: " + e.Message);
                }
            }

            var list = Grid.ItemsSource as IList;
            if (list.Count == 0)
            {
                return false;
            }

            for (; ; )
            {
                if (startingNewSearch)
                {
                    startingNewSearch = false;
                }
                else
                {
                    curPos++;
                    if (curPos >= list.Count)
                    {
                        curPos = 0;
                    }

                    if (curPos == m_FindEnd)
                    {
                        m_findPat = null;
                        return false;
                    }
                }

                var item = list[curPos];
                if (m_findPat.IsMatch(GetName(item)))
                {
                    Select(item);
                    return true;
                }
            }
        }

        public void Select(object item)
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
        public int SelectionStartIndex()
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
                // var row = Grid.ItemContainerGenerator.ContainerFromItem(cell.Item);
                // ret = Grid.ItemContainerGenerator.IndexFromContainer(row);

            }
            return ret;
        }
        public void RemoveColumn(string columnName)
        {
            int col = GetColumnIndex(columnName);
            if (0 <= col)
            {
                Grid.Columns.RemoveAt(col);
            }
        }

        public int GetColumnIndex(string columnName)
        {
            int i = 0;
            while (i < Grid.Columns.Count)
            {
                var name = ((TextBlock)Grid.Columns[i].Header).Name;
                if (name == columnName)
                {
                    return i;
                }
                else
                {
                    i++;
                }
            }
            return -1;
        }
        public List<string> ColumnNames()
        {
            var ret = new List<string>(Grid.Columns.Count);
            foreach (var column in Grid.Columns)
            {
                ret.Add(((TextBlock)column.Header).Name);
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
        public static string GetCellStringValue(DataGridCellInfo cell)
        {
            CallTreeNodeBase model = cell.Item as CallTreeNodeBase;
            if (model != null)
            {
                switch (((TextBlock)cell.Column.Header).Name)
                {
                    case "NameColumn": return model.DisplayName;
                    case "IncPercentColumn": return model.InclusiveMetricPercent.ToString("n1");
                    case "IncColumn": return model.InclusiveMetric.ToString("n1");
                    case "IncAvgColumn": return model.AverageInclusiveMetric.ToString("n1");
                    case "IncCountColumn": return model.InclusiveCount.ToString("n0");
                    case "ExcPercentColumn": return model.ExclusiveMetricPercent.ToString("n1");
                    case "ExcColumn": return model.ExclusiveMetric.ToString("n0");
                    case "ExcCountColumn": return model.ExclusiveCount.ToString("n0");
                    case "FoldColumn": return model.ExclusiveFoldedMetric.ToString("n0");
                    case "FoldCountColumn": return model.ExclusiveFoldedCount.ToString("n0");
                    case "TimeHistogramColumn": return model.InclusiveMetricByTimeString;
                    case "ScenarioHistogramColumn": return model.InclusiveMetricByScenarioString;
                    case "FirstColumn": return model.FirstTimeRelativeMSec.ToString("n3");
                    case "LastColumn": return model.LastTimeRelativeMSec.ToString("n3");
                }
            }
            var frameworkElement = cell.Column.GetCellContent(cell.Item);
            if (frameworkElement == null)
            {
                return "";
            }

            return GetCellStringValue(frameworkElement);
        }
        public static string GetCellStringValue(FrameworkElement contents)
        {
            string ret = Helpers.GetText(contents);
            return ret;
        }
        public static string GetColumnHeaderText(DataGridColumn column)
        {
            // TODO  I would like get the columnHeader text not from the column name but from what is displayed in the hyperlink
            var header = column.Header as TextBlock;
            string ret = header.Name;
            ret = ret.Replace("Column", "");
            ret = ret.Replace("Percent", " %");
            ret = ret.Replace("Count", " Ct");
            return ret;
        }

        #region private
        private void HistogramCell_CellSelectionChanged(object sender, RoutedEventArgs e, HistogramController controller, Histogram histogram)
        {
            var asTextBox = sender as TextBox;
            var window = Helpers.AncestorOfType<StackWindow>(this);

            if (asTextBox != null && window != null && 0 < asTextBox.SelectionLength)
            {
                window.StatusBar.Status = controller.GetInfoForCharacterRange(
                    (HistogramCharacterIndex)(asTextBox.SelectionStart),
                    (HistogramCharacterIndex)(asTextBox.SelectionStart + asTextBox.SelectionLength), histogram);
            }
        }

        // TODO this is a bit of a hack.  remove the context menu from the textBox so that you get
        // my context menu. 
        private void Grid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
        {
            var asTextBox = e.EditingElement as TextBox;
            var window = this.AncestorOfType<StackWindow>();

            // Get the histogram for this cell
            Histogram histogram = null;
            var item = e.Row.Item;
            var asCallTreeNodeBase = item as CallTreeNodeBase;
            if (asCallTreeNodeBase == null)
            {
                var asCallTreeViewNode = item as CallTreeViewNode;
                if (asCallTreeViewNode != null)
                {
                    asCallTreeNodeBase = asCallTreeViewNode.Data;
                }
            }
            if (asCallTreeNodeBase != null)
            {
                histogram = asCallTreeNodeBase.InclusiveMetricByTime;
            }

            if (asTextBox != null)
            {
                EditingBox = asTextBox;
                asTextBox.ContextMenu = null;

                if (e.Column == TimeHistogramColumn)
                {
                    asTextBox.SelectionChanged += (s, ea) => HistogramCell_CellSelectionChanged(s, ea, window.CallTree.TimeHistogramController, histogram);
                }
                else if (e.Column == ScenarioHistogramColumn)
                {
                    asTextBox.SelectionChanged += (s, ea) => HistogramCell_CellSelectionChanged(s, ea, window.CallTree.ScenarioHistogram, histogram);
                }
                else
                {
                    Debug.Assert(false, "Edit from unknown column!");
                }
            }
        }

        // TODO FIX NOW.  This is an ugly hack.  
        public static TextBox EditingBox;

        internal static string GoodPrecision(double num, DataGridColumn column)
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
        internal static bool VeryClose(string val1, string val2)
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

        private static string GetName(object item)
        {
            var asCallTreeNodeBase = item as CallTreeNodeBase;
            if (asCallTreeNodeBase != null)
            {
                return asCallTreeNodeBase.DisplayName;
            }

            var asCallTreeViewNode = item as CallTreeViewNode;
            if (asCallTreeViewNode != null)
            {
                return asCallTreeViewNode.Name;
            }

            return "";
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
                        if (dataGrid != null)
                        {
                            var cells = dataGrid.SelectedCells;
                            if (cells != null)
                            {
                                m_clipboardRangeStart = GetCellStringValue(cells[0]);
                                m_clipboardRangeEnd = GetCellStringValue(cells[1]);
                            }
                        }
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
        private int m_FindEnd;
        private Regex m_findPat;
        #endregion
    }
}
