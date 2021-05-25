using Controls;
using Diagnostics.Tracing.StackSources;
using Graphs;
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Stacks;
using Microsoft.Diagnostics.Tracing.Stacks.Formats;
using Microsoft.Diagnostics.Utilities;
using PerfView.Dialogs;
using PerfViewModel;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;
using Utilities;
using Address = System.UInt64;
using Path = System.IO.Path;

namespace PerfView
{
    /// <summary>
    /// Interaction logic for StackWindow.xaml
    /// </summary>
    public partial class StackWindow : WindowBase
    {
        public StackWindow(Window parentWindow, PerfViewStackSource dataSource) : base(parentWindow)
        {
            DataSource = dataSource;
            ParentWindow = parentWindow;
            m_history = new List<FilterParams>();

            InitializeComponent();
            MemoryStackPanel.Visibility = Visibility.Collapsed; // We do it here instead of the designer so we can see it!

            Title = DataSource.Title;
            FinishInit();
        }
        public StackWindow(Window parentWindow, StackWindow template) : base(parentWindow)
        {
            ParentWindow = parentWindow;
            DataSource = template.DataSource;
            m_history = new List<FilterParams>();
            InitializeComponent();
            MemoryStackPanel.Visibility = Visibility.Collapsed; // We do it here instead of the designer so we can see it!

            FoldPercentTextBox.Text = GetDefaultFoldPercentage();
            GuiState = template.GuiState;

            Title = DataSource.Title;
            Filter = template.Filter;
            FindTextBox.Text = template.FindTextBox.Text;
            NotesPaneHidden = template.NotesPaneHidden;

            StartTextBox.CopyFrom(template.StartTextBox);
            EndTextBox.CopyFrom(template.EndTextBox);
            ScenarioTextBox.CopyFrom(template.ScenarioTextBox);
            FindTextBox.CopyFrom(template.FindTextBox);
            GroupRegExTextBox.CopyFrom(template.GroupRegExTextBox);
            FoldPercentTextBox.CopyFrom(template.FoldPercentTextBox);
            FoldRegExTextBox.CopyFrom(template.FoldRegExTextBox);
            IncludeRegExTextBox.CopyFrom(template.IncludeRegExTextBox);
            ExcludeRegExTextBox.CopyFrom(template.ExcludeRegExTextBox);
            PriorityTextBox.CopyFrom(template.PriorityTextBox);

            m_historyPos = template.m_historyPos;
            m_history.AddRange(m_history);

            FinishInit();

            // Clone the columns available.  
            var templateColumnNames = template.ByNameDataGrid.ColumnNames();
            foreach (var colName in ByNameDataGrid.ColumnNames())
            {
                if (!templateColumnNames.Contains(colName))
                {
                    RemoveColumn(colName);
                }
            }
        }
        public string GetDefaultFoldPercentage()
        {
            string defaultFoldPercentage = App.ConfigData["DefaultFoldPercent"];
            if (defaultFoldPercentage == null)
            {
                defaultFoldPercentage = "1";
            }

            return defaultFoldPercentage;
        }
        public string GetDefaultFoldPat()
        {
            string defaultFoldPat = App.ConfigData["DefaultFoldPat"];
            if (defaultFoldPat == null)
            {
                defaultFoldPat = "ntoskrnl!%ServiceCopyEnd";
            }

            return defaultFoldPat;
        }
        public string GetDefaultGroupPat()
        {
            string defaultGroupPat = App.ConfigData["DefaultGroupPat"];

            // By default, it is Just My App.  
            if (defaultGroupPat == null)
            {
                defaultGroupPat = @"[Just My App]";
            }

            return defaultGroupPat;
        }
        public void RemoveColumn(string columnName)
        {
            // Remove View First or else GetColumnIndex will not work
            // Assumes ByNameDataGrid.Columns == CallTreeDataGrid.Columns == CalleesDataGrid.Columns == CallersDataGrid.Columns
            RemoveViewMenuColumn(ByNameDataGrid, ViewMenu, columnName);

            ByNameDataGrid.RemoveColumn(columnName);
            CallTreeDataGrid.RemoveColumn(columnName);
            CalleesDataGrid.RemoveColumn(columnName);
            CallersDataGrid.RemoveColumn(columnName);
            CallerCalleeView.RemoveCountColumn(columnName);
        }

        public void RemoveViewMenuColumn(PerfDataGrid perfDataGrid, MenuItem viewMenu, string columnName)
        {
            // First find the string displayed for the named column (e.g IncCount -> Inc Ct)
            int col = perfDataGrid.GetColumnIndex(columnName);
            if (col > -1)
            {
                string columnDisplayString = ((TextBlock)perfDataGrid.Grid.Columns[col].Header).Text;

                // Find that in the list of MenuItems, and delete it if present.  
                for (int i = 0; i < viewMenu.Items.Count; i++)
                {
                    MenuItem item = viewMenu.Items[i] as MenuItem;
                    if (item != null)
                    {
                        string name = (string)item.Header;
                        if (name == columnDisplayString)
                        {
                            viewMenu.Items.RemoveAt(i);
                            return;
                        }
                    }
                }
            }
        }

        public bool IsMemoryWindow
        {
            get { return m_IsMemoryWindow; }
            set
            {
                if (value != m_IsMemoryWindow)
                {
                    if (value == true)
                    {
                        ChangeHeaderText(CallersTab, "Referred-From");
                        ChangeHeaderText(CalleesTab, "Refs-To");
                        ChangeHeaderText(CallerCalleeTab, "RefFrom-RefTo");
                        ChangeHeaderText(CallTreeTab, "RefTree");
                        MemoryStackPanel.Visibility = Visibility.Visible;
                        m_callersView.DisplayPrimaryOnly = false;
                        m_calleesView.DisplayPrimaryOnly = false;
                        m_callTreeView.DisplayPrimaryOnly = false;
                    }
                    else
                    {
                        ChangeHeaderText(CallersTab, "Callers");
                        ChangeHeaderText(CalleesTab, "Callees");
                        ChangeHeaderText(CallerCalleeTab, "Caller-Callee");
                        ChangeHeaderText(CallTreeTab, "CallTree");
                        MemoryStackPanel.Visibility = Visibility.Collapsed;
                    }
                    m_IsMemoryWindow = value;
                }
            }
        }

        public bool IsScenarioWindow
        {
            get { return m_IsScenarioWindow; }
            set
            {
                if (value != m_IsScenarioWindow)
                {
                    if (value)
                    {
                        ScenarioStackPanel.Visibility = Visibility.Collapsed;
                        ScenarioContextMenu.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        ScenarioStackPanel.Visibility = Visibility.Collapsed;
                        ScenarioContextMenu.Visibility = Visibility.Collapsed;
                    }
                }
                m_IsScenarioWindow = value;
            }
        }

        /// <summary>
        /// Changes the text of a header of 'tab' to 'newHeaderText' without losing the '?' hyperlinks.   
        /// </summary>
        private void ChangeHeaderText(TabItem tab, string newHeaderText)
        {
            var textBlock = (TextBlock)tab.Header;
            var firstRun = (Run)textBlock.Inlines.FirstInline;
            firstRun.Text = newHeaderText + " ";
        }

        private bool m_IsMemoryWindow;
        private bool m_IsScenarioWindow = true;

        public Window ParentWindow { get; private set; }
        public PerfViewStackSource DataSource { get; private set; }

        // TODO resolve the redundancy with DataSource.  
        public StackSource StackSource
        {
            get { return m_stackSource; }
        }
        /// <summary>
        /// This sets the window to to the given stack source, this DOES triggers an update of the gridViews.  
        /// </summary>
        public void SetStackSource(StackSource newSource, Action onComplete = null)
        {
            Debug.Assert(newSource != null);
            Debug.Assert(m_callTree != null);

            if (!ValidateStartAndEnd(newSource))
            {
                return;
            }

            // Synchronize the sample rate if the source supports it.  
            // TODO - Currently nothing uses sampling.  USE OR REMOVE 
            if (newSource.SamplingRate == null)
            {
                SamplingStackPanel.Visibility = System.Windows.Visibility.Collapsed;
            }
            else
            {
                if (SamplingTextBox.Text.Length == 0)
                {
                    SamplingTextBox.Text = newSource.SamplingRate.Value.ToString("f1");
                }
                else
                {
                    var sampleRate = 1.0F;
                    float.TryParse(SamplingTextBox.Text, out sampleRate);
                    if (sampleRate < 1)
                    {
                        sampleRate = 1;
                    }

                    newSource.SamplingRate = sampleRate;
                }
            }

            // Hide scenarios view if our source doesn't support scenarios.
            IsScenarioWindow = (newSource.ScenarioCount != 0);

            FixupJustMyCodeInGroupPats(newSource);

            FilterParams filterParams = Filter;
            if (!m_settingFromHistory && (m_history.Count == 0 || !filterParams.Equals(m_history[m_history.Count - 1])))
            {
                // Remove any 'forward' history
                if (m_historyPos + 1 < m_history.Count)
                {
                    m_history.RemoveRange(m_historyPos + 1, m_history.Count - m_historyPos - 1);
                }

                if (m_history.Count > 100)
                {
                    m_history.RemoveAt(0);
                }

                m_history.Add(new FilterParams(filterParams));
                m_historyPos = m_history.Count - 1;
            }

            var asMemoryGraphSource = newSource as Graphs.MemoryGraphStackSource;
            if (asMemoryGraphSource != null)
            {
                asMemoryGraphSource.PriorityRegExs = filterParams.TypePriority;
            }

#if false    // TODO decide if we want this. 
            if (m_computingStacks)
            {
                Debug.WriteLine("Already computing stacks, aborting that work");
                Debug.Assert(false, "Understand why we are trying to compute stacks again");
                // TODO there is a race here.  In theory you could be aborting the wrong work.  
                StatusBar.AbortWork(true);
            }
#endif
            StatusBar.StartWork("Computing Stack Traces", delegate ()
            {
                CallTree newCallTree = new CallTree(ScalingPolicy);

                m_stackSource = newSource;
                if (m_stackSource == null)
                {
                    m_stackSource = new CopyStackSource();      // This is only needed for the degenerate case of no data.  
                }

                double histogramStart = 0;
                if (double.TryParse(filterParams.StartTimeRelativeMSec, out histogramStart))
                {
                    histogramStart -= .0006;
                }

                double histogramEnd = double.MaxValue;
                if (double.TryParse(filterParams.EndTimeRelativeMSec, out histogramEnd))
                {
                    histogramEnd += .0006;
                }

                if (histogramEnd > histogramStart)
                {
                    newCallTree.TimeHistogramController = new TimeHistogramController(newCallTree, histogramStart, histogramEnd);
                }

                if (m_stackSource.ScenarioCount > 0)
                {
                    if (filterParams.ScenarioList == null)
                    {
                        filterParams.ScenarioList = new int[m_stackSource.ScenarioCount];
                        for (int i = 0; i < m_stackSource.ScenarioCount; i++)
                        {
                            filterParams.ScenarioList[i] = i;
                        }
                    }
                    string[] names = null;
                    var aggregate = m_stackSource as AggregateStackSource;
                    if (aggregate != null)
                    {
                        names = aggregate.ScenarioNames;
                    }

                    if (filterParams.ScenarioList.Length > 1)
                    {
                        newCallTree.ScenarioHistogram = new ScenarioHistogramController(
                            newCallTree, filterParams.ScenarioList, m_stackSource.ScenarioCount, names);
                    }
                }

                if (App.CommandLineArgs.SafeMode)
                {
                    StatusBar.Log("SafeMode enable, turning off parallelism");
                    CallTree.DisableParallelism = true;
                }

                var filterStackSource = new FilterStackSource(filterParams, m_stackSource, ScalingPolicy);
                newCallTree.StackSource = filterStackSource;

                // TODO: do we want to expose useWholeTraceMetric = false too?
                // Fold away all small nodes.                     
                float minIncusiveTimePercent;
                if (float.TryParse(filterParams.MinInclusiveTimePercent, out minIncusiveTimePercent) && minIncusiveTimePercent > 0)
                {
                    newCallTree.FoldNodesUnder(minIncusiveTimePercent * newCallTree.Root.InclusiveMetric / 100, true);
                }

                // Compute the byName items sorted by exclusive time.  
                var byNameItems = newCallTree.ByIDSortedExclusiveMetric();

                StatusBar.EndWork(delegate ()
                {
                    var oldCallTree = m_callTree;
                    m_callTree = newCallTree;

                    // Gather current sorting information
                    var sortDescriptions = ByNameDataGrid.Grid.Items.SortDescriptions.ToArray();
                    var sortDirections = ByNameDataGrid.Grid.Columns.Select(c => c.SortDirection).ToArray();

                    // SignalPropertyChange the ByName Tab 
                    m_byNameView = byNameItems;
                    ByNameDataGrid.Grid.ItemsSource = m_byNameView;

                    // Reapply the previous sort after setting ItemsSource
                    ByNameDataGrid.Grid.Items.SortDescriptions.Clear();
                    foreach (var description in sortDescriptions)
                    {
                        ByNameDataGrid.Grid.Items.SortDescriptions.Add(description);
                    }

                    for (int i = 0; i < sortDirections.Length; i++)
                    {
                        var direction = sortDirections[i];
                        ByNameDataGrid.Grid.Columns[i].SortDirection = direction;
                    }

                    // SignalPropertyChange the Caller-Callee Tab
                    SetFocus(null);

                    ByNameDataGrid.Focus();

                    // SignalPropertyChange the CallTree Tab
                    m_callTreeView.SetRoot(m_callTree.Root);

                    // Update the threads stats
                    var stats = string.Format("Totals Metric: {0:n1}  Count: {1:n1}", CallTree.Root.InclusiveMetric, CallTree.Root.InclusiveCount);
                    if (CallTree.Root.LastTimeRelativeMSec != 0)
                    {
                        stats = string.Format("{0}  First: {1:n3} Last: {2:n3}  Last-First: {3:n3}  Metric/Interval: {4:n2}  TimeBucket: {5:n1}", stats,
                        CallTree.Root.FirstTimeRelativeMSec, CallTree.Root.LastTimeRelativeMSec, CallTree.Root.DurationMSec,
                        CallTree.Root.InclusiveMetric / CallTree.Root.DurationMSec,
                        CallTree.TimeHistogramController.BucketDuration);
                    }

                    if (ExtraTopStats != null)
                    {
                        stats = stats + " " + ExtraTopStats;
                    }

                    if (ComputeMaxInTopStats)
                    {
                        Histogram histogram = CallTree.Root.InclusiveMetricByTime;
                        TimeHistogramController controller = histogram.Controller as TimeHistogramController;

                        float cum = 0;
                        float cumMax = float.MinValue;
                        int cumMaxIdx = -1;
                        for (int i = 0; i < histogram.Count; i++)
                        {
                            var val = histogram[i];
                            cum += val;
                            if (cum > cumMax)
                            {
                                cumMax = cum;
                                cumMaxIdx = i + 1;
                            }
                        }

                        stats += string.Format(" MaxMetric: {0:n3}M at {1:n3}ms",
                            cumMax / 1000000, controller.GetStartTimeForBucket((HistogramCharacterIndex)cumMaxIdx));
                    }

                    RedrawFlameGraphIfVisible();

                    TopStats.Text = stats;

                    // TODO this is a bit of a hack, as it might replace other instances of the string.  
                    Title = Regex.Replace(Title, @" Stacks(\([^)]*\))? ", " Stacks(" + CallTree.Root.InclusiveMetric.ToString("n0") + " metric) ");
                    UpdateDiffMenus(StackWindows);
                    onComplete?.Invoke();
                });
            });
        }

        // The 'Just My App' pattern depends on the directory of the EXE and thus has to be fixed up to be the
        // correct pattern.  This code does this.
        private void FixupJustMyCodeInGroupPats(StackSource stackSource)
        {
            if (m_fixedUpJustMyCode)
            {
                return;
            }

            m_fixedUpJustMyCode = true;

            // Get tbe name and create the 'justMyApp pattern. 
            string justMyApp = null;
            string exeName = DataSource.DataFile.FindExeName(IncludeRegExTextBox.Text);
            if (exeName != null)
            {
                if (string.Compare(exeName, "w3wp", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    justMyApp = @"[ASP.NET Just My App] \Temporary ASP.NET Files\->;!dynamicClass.S->;!=>OTHER";
                }
                else if (!exeName.StartsWith("IISAspHost", StringComparison.OrdinalIgnoreCase) &&
                        string.Compare(exeName, "WWAHost", StringComparison.OrdinalIgnoreCase) != 0 &&
                        string.Compare(exeName, "iexplore", StringComparison.OrdinalIgnoreCase) != 0 &&
                        string.Compare(exeName, "dotnet", StringComparison.OrdinalIgnoreCase) != 0)
                {
                    string exePath = FindExePath(stackSource, exeName);
                    if (exePath != null)
                    {
                        var dirName = Path.GetDirectoryName(exePath);
                        if (!string.IsNullOrEmpty(dirName))
                        {
                            justMyApp = @"[Just My App]           \" + Path.GetFileName(dirName) + @"\%!->;!=>OTHER";
                        }
                    }

                    if (justMyApp == null)
                    {
                        StatusBar.Log("Could not determine EXE path, could not add 'Just my app' group");
                    }
                }
            }

            // If we asked for just my app in the TextBox, then set it to the specialized pattern
            if (GroupRegExTextBox.Text.StartsWith("[Just My App]"))
            {
                if (justMyApp == null)
                {
                    justMyApp = @"[group module entries]  {%}!=>module $1";
                }

                GroupRegExTextBox.Text = justMyApp;
            }


            // If we have a JustMyApp, add it to list of Group Pattern possibilities 
            if (justMyApp != null)
            {
                GroupRegExTextBox.Items.Insert(0, justMyApp);
            }
        }

        /// <summary>
        /// Update causes the gridview's to be recalculated based on the current stack source filter parameters. 
        /// </summary>
        public void Update()
        {
            if (m_stackSource != null)
            {
                SetStackSource(m_stackSource);  //  This forces a recomputation of the calltree.  
            }
        }

        public CallTree CallTree
        {
            get { return m_callTree; }
        }
        public CallTreeView CallTreeView { get { return m_callTreeView; } }

        /// <summary>
        /// Note that setting the filter does NOT trigger an update of the gridViews.  You have to call Update()
        /// </summary>
        public FilterParams Filter
        {
            get
            {
                var ret = new FilterParams();
                ret.StartTimeRelativeMSec = StartTextBox.Text;
                ret.EndTimeRelativeMSec = EndTextBox.Text;
                ret.Scenarios = ScenarioTextBox.Text;
                ret.MinInclusiveTimePercent = FoldPercentTextBox.Text;
                ret.FoldRegExs = FoldRegExTextBox.Text;
                ret.IncludeRegExs = IncludeRegExTextBox.Text;
                ret.ExcludeRegExs = ExcludeRegExTextBox.Text;
                ret.GroupRegExs = GroupRegExTextBox.Text;
                ret.TypePriority = PriorityTextBox.Text;
                return ret;
            }
            set
            {
                StartTextBox.Text = value.StartTimeRelativeMSec;
                EndTextBox.Text = value.EndTimeRelativeMSec;
                ScenarioTextBox.Text = value.Scenarios;
                FoldPercentTextBox.Text = value.MinInclusiveTimePercent;
                FoldRegExTextBox.Text = value.FoldRegExs;
                IncludeRegExTextBox.Text = value.IncludeRegExs;
                ExcludeRegExTextBox.Text = value.ExcludeRegExs;
                GroupRegExTextBox.Text = value.GroupRegExs;
                PriorityTextBox.Text = value.TypePriority;
            }
        }

        /// <summary>
        /// FilterGuiState is like 'Filter' in that it can set the filter paramters, but it goes further that it 
        /// can also set the history of each filter parameter (and other things that only the GUI cares about)
        /// </summary>
        public FilterGuiState FilterGuiState
        {
            get
            {
                var ret = new FilterGuiState();
                WriteFromTextBox(StartTextBox, ret.Start);
                WriteFromTextBox(EndTextBox, ret.End);
                WriteFromTextBox(ScenarioTextBox, ret.Scenarios);
                WriteFromTextBox(GroupRegExTextBox, ret.GroupRegEx);
                WriteFromTextBox(FoldPercentTextBox, ret.FoldPercent);
                WriteFromTextBox(FoldRegExTextBox, ret.FoldRegEx);
                WriteFromTextBox(IncludeRegExTextBox, ret.IncludeRegEx);
                WriteFromTextBox(ExcludeRegExTextBox, ret.ExcludeRegEx);
                WriteFromTextBox(PriorityTextBox, ret.TypePriority);
                return ret;
            }
            set
            {
                ReadIntoTextBox(StartTextBox, value.Start);
                ReadIntoTextBox(EndTextBox, value.End);
                ReadIntoTextBox(ScenarioTextBox, value.Scenarios);
                ReadIntoTextBox(GroupRegExTextBox, value.GroupRegEx);
                ReadIntoTextBox(FoldPercentTextBox, value.FoldPercent);
                ReadIntoTextBox(FoldRegExTextBox, value.FoldRegEx);
                ReadIntoTextBox(IncludeRegExTextBox, value.IncludeRegEx);
                ReadIntoTextBox(ExcludeRegExTextBox, value.ExcludeRegEx);
                ReadIntoTextBox(PriorityTextBox, value.TypePriority);
            }
        }

        private void ReadIntoTextBox(HistoryComboBox textBox, TextBoxGuiState guiState)
        {
            if (guiState == null)
            {
                return;
            }

            if (guiState.Value != null)
            {
                textBox.Text = guiState.Value;
            }

            if (guiState.History != null)
            {
                textBox.SetHistory(guiState.History);
            }
        }

        private void WriteFromTextBox(HistoryComboBox textBox, TextBoxGuiState guiState)
        {
            var val = textBox.Text;
            if (!string.IsNullOrWhiteSpace(val))
            {
                guiState.Value = val;
            }

            if (textBox.Items.Count > 0)
            {
                var itemList = new List<string>();
                foreach (string item in textBox.Items)
                {
                    if (!string.IsNullOrWhiteSpace(item))
                    {
                        itemList.Add(item);
                    }
                }

                if (itemList.Count > 0)
                {
                    guiState.History = itemList;
                }
            }
        }

        public string FocusName { get { return CallerCalleeView.FocusName; } }
        public bool SetFocus(string name)
        {
            if (name == null)
            {
                name = FocusName;       // Use the old focus name
                if (name == null)
                {
                    name = "ROOT";
                }
            }

            // TODO FIX NOW in case of duplicates
            // TODO FIX NOW make this a utility function 
            // Find the node in the ByName view.  
            CallTreeNodeBase node = null;
            foreach (var byName in m_callTree.ByID)
            {
                if (byName.Name == name)
                {
                    node = byName;
                    break;
                }
            }
            if (node == null)
            {
                // We want to aways succeed for root. 
                if (name != "ROOT")
                {
                    StatusBar.LogError("Could not find node named " + name + " (folded away?)");
                }

                node = m_callTree.Root;
                name = node.Name;
            }

            CallerCalleeView.SetFocus(name, m_callTree);

            m_calleesView.SetRoot(AggregateCallTreeNode.CalleeTree(node));
            if (IsMemoryWindow)
            {
                CalleesTitle.Text = "Objects that are referred to by " + node.Name;
            }
            else
            {
                CalleesTitle.Text = "Methods that are called by " + node.Name;
            }

            m_callersView.SetRoot(AggregateCallTreeNode.CallerTree(node));

            if (IsMemoryWindow)
            {
                CallersTitle.Text = "Objects that refer to " + node.Name;
            }
            else
            {
                CallersTitle.Text = "Methods that call " + node.Name;
            }

            DataContext = node;
            return true;
        }

        /// <summary>
        /// Find a pattern in the appropriate window.  The pattern is a .NET regular expression (case insensitive). 
        /// </summary>
        public bool Find(string pat)
        {
            FindTextBox.Text = pat;
            FindNext(null);         // Restart the find operation. 
            return FindNext(pat);
        }
        public bool FindNext()
        {
            return FindNext(FindTextBox.Text);
        }
        /// <summary>
        /// Finds oin the ByName view.
        /// </summary>
        /// <param name="name"></param>
        public void FindByName(string name)
        {
            for (int i = 0; i < m_byNameView.Count; i++)
            {
                var item = m_byNameView[i];
                if (name == item.DisplayName)
                {
                    ByNameDataGrid.Grid.SelectedIndex = i;
                    // Hack!  Wait for items to be populated
                    try
                    {
                        ByNameDataGrid.Grid.ScrollIntoView(item);
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine("Caught Exception while scrolling " + e.ToString());
                    }
                    ByNameTab.IsSelected = true;
                    return;
                }
            }
            StatusBar.LogError("Name '" + name + "' not found");
        }

        /// <summary>
        /// If we save this view as a file, this is its name (may be null) 
        /// </summary>
        public string FileName { get { return m_fileName; } set { m_fileName = value; } }

        private void DoBack(object sender, RoutedEventArgs e)
        {
            // TODO FIX NOW, clone this for Forward too. 
            if (m_historyPos > 0)
            {
                --m_historyPos;
                m_settingFromHistory = true;        // TODO, can we pass as a parameter?
                bool success = false;
                var origFilter = Filter;
                try
                {
                    Filter = m_history[m_historyPos];
                    Update();
                    success = true;
                }
                finally
                {
                    if (!success)
                    {
                        Filter = origFilter;
                    }
                }

                m_settingFromHistory = false;
            }
        }
        private void CanDoBack(object sender, CanExecuteRoutedEventArgs e)
        {
            if (m_historyPos > 0)
            {
                e.CanExecute = true;
            }
        }
        private void DoForward(object sender, RoutedEventArgs e)
        {
            if (m_historyPos + 1 < m_history.Count)
            {
                m_historyPos++;
                m_settingFromHistory = true;
                Filter = m_history[m_historyPos];
                Update();
                m_settingFromHistory = false;
            }
        }
        private void CanDoForward(object sender, CanExecuteRoutedEventArgs e)
        {
            if (m_historyPos + 1 < m_history.Count)
            {
                e.CanExecute = true;
            }
        }
        private void DoClose(object sender, RoutedEventArgs e)
        {
            Close();
        }
        private void DoOpenParent(object sender, RoutedEventArgs e)
        {
            for (; ; )
            {
                try
                {
                    if (ParentWindow != null)
                    {
                        ParentWindow.Visibility = System.Windows.Visibility.Visible;
                        ParentWindow.Focus();
                    }
                    return;
                }
                catch (InvalidOperationException)
                {
                    // This means the window was closed, fix our parent to skip it.  
                    var asStackWindow = ParentWindow as PerfView.StackWindow;
                    if (asStackWindow != null)
                    {
                        ParentWindow = asStackWindow.ParentWindow;
                        continue;
                    }
                    var asEventWindow = ParentWindow as EventWindow;
                    if (asEventWindow != null)
                    {
                        ParentWindow = asEventWindow.ParentWindow;
                        continue;
                    }
                    break;
                }
            }
        }
        private void DoSetSymbolPath(object sender, RoutedEventArgs e)
        {
            GuiApp.MainWindow.DoSetSymbolPath(sender, e);
        }
        private void DoSetSourcePath(object sender, RoutedEventArgs e)
        {
            var symPathDialog = new SymbolPathDialog(this, App.SourcePath, "Source", delegate (string newPath)
            {
                App.SourcePath = newPath;
            });
            symPathDialog.Show();
        }
        private void DoSetStartupPreset(object sender, RoutedEventArgs e)
        {
            App.ConfigData["DefaultFoldPercent"] = FoldPercentTextBox.Text;
            App.ConfigData["DefaultFoldPat"] = FoldRegExTextBox.Text;

            var defaultGroupPat = GroupRegExTextBox.Text;
            if (defaultGroupPat.StartsWith("[Just My App]"))
            {
                defaultGroupPat = defaultGroupPat.Substring(0, 13);
            }

            App.ConfigData["DefaultGroupPat"] = defaultGroupPat;
        }
        private void DoSaveAs(object sender, RoutedEventArgs e)
        {
            m_fileName = null;
            DoSave(sender, e);
        }
        internal void DoSave(object sender, RoutedEventArgs e)
        {
            if (m_fileName == null)
            {
                var saveDialog = new Microsoft.Win32.SaveFileDialog();
                var baseName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(DataSource.FilePath));

                for (int i = 1; ; i++)
                {
                    saveDialog.FileName = baseName + ".View" + i.ToString() + ".perfView.xml.zip";
                    if (!File.Exists(saveDialog.FileName))
                    {
                        break;
                    }
                }
                saveDialog.InitialDirectory = Path.GetDirectoryName(DataSource.FilePath);
                saveDialog.Title = "File to save view";
                saveDialog.DefaultExt = ".perfView.xml.zip";
                saveDialog.Filter = "PerfView view file|*.perfView.xml.zip|Comma Separated Value|*.csv|Speed Scope|*.speedscope.json|Chromium Trace Event|*.chromium.json|All Files|*.*";
                saveDialog.AddExtension = true;
                saveDialog.OverwritePrompt = true;

                Nullable<bool> result = saveDialog.ShowDialog();
                if (!(result == true))
                {
                    StatusBar.Log("View save canceled.");
                    return;
                }
                m_fileName = saveDialog.FileName;
            }

            if (m_fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                if (!ByNameTab.IsSelected)
                {
                    throw new ApplicationException("Saving as a CSV is only supported in the ByName tab");
                }

                string listSeparator = Thread.CurrentThread.CurrentCulture.TextInfo.ListSeparator;

                bool hasWhichColumn = (DataSource.DataFile as ScenarioSetPerfViewFile != null);
                string whichColumnHeader = hasWhichColumn ? listSeparator + "Which" : "";
                string whichValue = "";

                using (var csvFile = File.CreateText(m_fileName))
                {
                    // TODO add all the other columns.  
                    // Write out column header
                    csvFile.WriteLine("Name{0}Exc %{0}Exc{0}Exc Ct{1}", listSeparator, whichColumnHeader);

                    // Write out events 
                    List<CallTreeNodeBase> items = CallTree.ByIDSortedExclusiveMetric();
                    foreach (var item in items)
                    {
                        if (hasWhichColumn)
                        {
                            whichValue = item.InclusiveMetricByScenario.ToString();
                        }

                        csvFile.WriteLine("{0}{1}{2:f1}{1}{3:f0}{1}{4}{5}", EventWindow.EscapeForCsv(item.Name, listSeparator), listSeparator,
                            item.ExclusiveMetricPercent, item.ExclusiveMetric, item.ExclusiveCount, whichValue);
                    }
                }
            }
            else if(m_fileName.EndsWith(".speedscope.json", StringComparison.OrdinalIgnoreCase))
            {
                SpeedScopeStackSourceWriter.WriteStackViewAsJson(CallTree.StackSource, m_fileName);
            }
            else if (m_fileName.EndsWith(".chromium.json", StringComparison.OrdinalIgnoreCase))
            {
                ChromiumStackSourceWriter.WriteStackViewAsJson(CallTree.StackSource, m_fileName, false);
            }
            else
            {
                if (m_fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    m_fileName = m_fileName + ".zip";
                }

                if (!m_fileName.EndsWith(".xml.zip"))
                {
                    throw new ApplicationException("File names for views must end in .xml.zip");
                }

                // Intern to compact it, only take samples in the view but leave the names unmorphed. 
                var filteredSource = new FilterStackSource(Filter, StackSource, ScalingPolicy);
                InternStackSource source = new InternStackSource(filteredSource, StackSource);

                XmlStackSourceWriter.WriteStackViewAsZippedXml(source, m_fileName, delegate (XmlWriter writer)
                {
                    GuiState.WriteToXml("StackWindowGuiState", writer);
                });
            }

            StatusBar.Log("Wrote stack view as " + Path.GetFullPath(m_fileName));
            if (m_ViewsShouldBeSaved)
            {
                m_ViewsShouldBeSaved = false;
                --GuiApp.MainWindow.NumWindowsNeedingSaving;
            }
        }
        private void DoOpenRegressionItem(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var baselineWindow = menuItem.Tag as StackWindow;

            var reportName = "Regression Report between " + Name + " and " + baselineWindow.Name;
            StatusBar.StartWork("Computing: " + reportName, delegate ()
            {
                var htmlReport = Path.Combine(CacheFiles.CacheDir, "OverweightAnalysis." + DateTime.Now.ToString("MM-dd.HH.mm.ss.fff") + ".html");
                OverWeigthReport.GenerateOverweightReport(htmlReport, this, baselineWindow);
                StatusBar.EndWork(delegate ()
                {
                    OverWeigthReport.ViewOverweightReport(htmlReport, reportName);
                });
            });
        }
        private void DoOpenDiffItem(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var baselineWindow = menuItem.Tag as StackWindow;

            var dataFile = new DiffPerfViewData(DataSource, baselineWindow.DataSource);
            var testFilter = new FilterParams(Filter);
            testFilter.MinInclusiveTimePercent = "";
            var baselineFilter = new FilterParams(baselineWindow.Filter);
            baselineFilter.MinInclusiveTimePercent = "";

            // Create a new stack window. 
            var stackWindow = new StackWindow(this, dataFile);
            // TODO this is a hack that this is here.  
            if (DataSource.DataFile.SupportsProcesses)
            {
                stackWindow.GroupRegExTextBox.Items.Insert(0, "[group module entries]  ^Process% {%}->$1;^Thread->Thread;{%}!=>module $1");
                stackWindow.GroupRegExTextBox.Items.Insert(0, "[group modules]           ^Process% {%}->$1;^Thread->Thread;{%}!->module $1");
                stackWindow.GroupRegExTextBox.Items.Insert(0, "[Ignore PID/TID]          ^Process% {%}->$1;^Thread->Thread;");
                stackWindow.GroupRegExTextBox.Items.Insert(0, "[Ignore Paths]               ^Process% {%}->$1;^Thread->Thread;{%}!{*}->$1!$2");

                var osGroupings = @"^Process% {%}->$1;^Thread->Thread;\Temporary ASP.NET Files\->;v4.0.30319\%!=>CLR;v2.0.50727\%!=>CLR;mscoree=>CLR;\mscorlib.*!=>LIB;\System.Xaml.*!=>WPF;\System.*!=>LIB;Presentation%=>WPF;WindowsBase%=>WPF;system32\*!=>OS;syswow64\*!=>OS;{%}!=> module $1";
                stackWindow.GroupRegExTextBox.Items.Insert(0, "[group CLR/OS ignore paths] " + osGroupings + ";{%}!{*}->$1!$2");

                var defaultGroup = "[group CLR/OS entries] " + osGroupings;
                stackWindow.GroupRegExTextBox.Items.Insert(0, defaultGroup);
                stackWindow.GroupRegExTextBox.Text = defaultGroup;
                stackWindow.PriorityTextBox.Text = MemoryGraphStackSource.DefaultPriorities;
            }
            else
            {
                // Use the same configuration as the base window.  
                DataSource.DataFile.ConfigureStackWindow("", stackWindow);
            }

            stackWindow.GuiState = GuiState;

            stackWindow.Show();

            stackWindow.StatusBar.StartWork("Computing " + dataFile.Name, delegate ()
            {
                var source = InternStackSource.Diff(
                    new FilterStackSource(testFilter, StackSource, ScalingPolicy), StackSource,
                    new FilterStackSource(baselineFilter, baselineWindow.StackSource, ScalingPolicy), baselineWindow.StackSource);
                stackWindow.StatusBar.EndWork(delegate ()
                {
                    stackWindow.SetStackSource(source);
                });
            });
        }
        internal void DoUpdate(object sender, RoutedEventArgs e)
        {
            Update();
        }
        private void DoFindNext(object sender, RoutedEventArgs e)
        {
            FindNext();
        }
        private void DoFindEnter(object sender, RoutedEventArgs e)
        {
            Find(FindTextBox.Text);
        }

        private void DoCancel(object sender, ExecutedRoutedEventArgs e)
        {
            StatusBar.AbortWork();
        }
        private void DoToggleNoPadOnCopy(object sender, ExecutedRoutedEventArgs e)
        {
            PerfDataGrid.NoPadOnCopyToClipboard = !PerfDataGrid.NoPadOnCopyToClipboard;
            StatusBar.Status = "No Pad On Copy is now " + PerfDataGrid.NoPadOnCopyToClipboard;
        }

        private bool GetSamplesForSelection(out bool[] sampleSet, out string name)
        {
            name = "";
            sampleSet = null;

            var cells = SelectedCells();
            if (cells == null || cells.Count <= 0)
            {
                StatusBar.LogError("No cells selected.");
                return false;
            }

            // TODO FIX NOW make this work. 
            if (CallerCalleeTab.IsSelected)
            {
                StatusBar.LogError("Sorry, Drill Into and other operations that select samples is not implemented in the caller-callee view.  " +
                    "Often you can get what you need from the ByName view.");
                return false;
            }

            var addedDots = false;
            Debug.Assert(CallTree.StackSource.BaseStackSource == m_stackSource);
            var localSampleSet = new bool[m_stackSource.SampleIndexLimit];

            // TODO do I need to do this off the GUI thread?  
            foreach (var cell in cells)
            {
                bool exclusiveSamples = false;
                var colName = ((TextBlock)cell.Column.Header).Name;
                if (colName.StartsWith("Exc"))
                {
                    exclusiveSamples = true;
                }

                if (colName.StartsWith("Fold"))
                {
                    StatusBar.LogError("Cannot drill into folded samples.  Use Exc instead.");
                    return false;
                }

                var item = cell.Item;
                var asCallTreeNodeBase = item as CallTreeNodeBase;
                if (asCallTreeNodeBase == null)
                {
                    var asCallTreeViewNode = item as CallTreeViewNode;
                    if (asCallTreeViewNode != null)
                    {
                        asCallTreeNodeBase = asCallTreeViewNode.Data;
                    }
                    else
                    {
                        StatusBar.LogError("Could not find data item.");
                        return false;
                    }
                }

                asCallTreeNodeBase.GetSamples(exclusiveSamples, delegate (StackSourceSampleIndex sampleIdx)
                {
                    // We should only count a sample once unless we are combining different cells. 
                    Debug.Assert((int)sampleIdx < localSampleSet.Length);
                    Debug.Assert(!localSampleSet[(int)sampleIdx] || cells.Count > 1);
                    localSampleSet[(int)sampleIdx] = true;
                    return true;
                });
                if (name.Length == 0)
                {
                    name = (exclusiveSamples ? "Exc" : "Inc") + " of " + asCallTreeNodeBase.Name;
                }
                else if (!addedDots)
                {
                    name = name + "...";
                    addedDots = true;
                }
            }
            sampleSet = localSampleSet;
            return true;
        }

        private void DoDumpObject(object sender, ExecutedRoutedEventArgs e)
        {
            var asMemoryStackSource = m_stackSource as MemoryGraphStackSource;
            if (asMemoryStackSource == null)
            {
                StatusBar.LogError("DumpObject only works when operating on Memory views");
                return;
            }

            bool[] sampleSet;
            string sampleSetName;
            if (GetSamplesForSelection(out sampleSet, out sampleSetName))
            {
                return;
            }

            int count = 0;
            List<NodeIndex> nodeIdxs = new List<NodeIndex>();
            for (int i = 0; i < sampleSet.Length; i++)
            {
                if (sampleSet[i])
                {
                    count++;
                    if (nodeIdxs.Count < 3)
                    {
                        nodeIdxs.Add(asMemoryStackSource.GetNodeIndexForSample((StackSourceSampleIndex)i));
                    }
                }
            }

            StatusBar.LogWriter.WriteLine("Selected node has {0} instances, dumping the first 3", count);
            MemoryGraph graph = (MemoryGraph)asMemoryStackSource.Graph;
            RefGraph refGraph = asMemoryStackSource.RefGraph;
            Node storage = graph.AllocNodeStorage();
            Node childStorage = graph.AllocNodeStorage();
            NodeType typeStorage = graph.AllocTypeNodeStorage();
            RefNode refStorage = refGraph.AllocNodeStorage();
            foreach (NodeIndex nodeIdx in nodeIdxs)
            {
                MemoryNode node = (MemoryNode)graph.GetNode(nodeIdx, storage);
                RefNode refNode = refGraph.GetNode(nodeIdx, refStorage);
                NodeType type = node.GetType(typeStorage);

                StatusBar.LogWriter.WriteLine();
                StatusBar.LogWriter.WriteLine("{0}<Node Index=\"{1}\" Address=\"0x{2}\" Size=\"{3}\" Type=\"{4}\" NumChildren=\"{5}\" NumRefsTo=\"{6}\">",
                    "  ", (int)node.Index, node.Address.ToString("x"), node.Size, XmlUtilities.XmlEscape(type.Name), node.ChildCount, refNode.ChildCount);

                if (node.ChildCount != 0)
                {
                    StatusBar.LogWriter.WriteLine("    <NodeReferTo>");
                    for (NodeIndex childIdx = node.GetFirstChildIndex(); childIdx != NodeIndex.Invalid; childIdx = node.GetNextChildIndex())
                    {
                        MemoryNode child = (MemoryNode)graph.GetNode(childIdx, childStorage);
                        NodeType childType = child.GetType(typeStorage);
                        StatusBar.LogWriter.WriteLine("{0}<Child Index=\"{1}\" Address=\"0x{2}\" Type=\"{3}\">",
                            "      ", (int)child.Index, child.Address, XmlUtilities.XmlEscape(childType.Name));
                    }
                    StatusBar.LogWriter.WriteLine("    </NodeReferTo>");
                }

                if (refNode.ChildCount != 0)
                {
                    StatusBar.LogWriter.WriteLine("    <NodeReferencedBy>");
                    for (NodeIndex childIdx = refNode.GetFirstChildIndex(); childIdx != NodeIndex.Invalid; childIdx = refNode.GetNextChildIndex())
                    {
                        MemoryNode child = (MemoryNode)graph.GetNode(childIdx, childStorage);
                        NodeType childType = child.GetType(typeStorage);
                        StatusBar.LogWriter.WriteLine("{0}<Child Index=\"{1}\" Address=\"0x{2}\" Type=\"{3}\">",
                            "      ", (int)child.Index, child.Address, XmlUtilities.XmlEscape(childType.Name));
                    }
                    StatusBar.LogWriter.WriteLine("    </NodeReferencedBy>");
                }
                StatusBar.LogWriter.WriteLine("  </Node>");
            }
            StatusBar.OpenLog();
            StatusBar.Status = "Dumped " + nodeIdxs.Count + " objects to log.";
        }

        private void DoViewObjects(object sender, ExecutedRoutedEventArgs e)
        {
            var asMemoryStackSource = m_stackSource as MemoryGraphStackSource;
            if (asMemoryStackSource == null)
            {
                StatusBar.LogError("View Objects only works when operating on Memory views");
                return;
            }

            List<NodeIndex> nodeIdxs = null;
            int nodeCount = 0;
            var cells = SelectedCells();
            if (cells != null && 0 < cells.Count)
            {
                bool[] sampleSet;
                string sampleSetName;
                if (!GetSamplesForSelection(out sampleSet, out sampleSetName))
                {
                    return;
                }

                nodeIdxs = new List<NodeIndex>();
                for (int i = 0; i < sampleSet.Length; i++)
                {
                    if (sampleSet[i])
                    {
                        nodeIdxs.Add(asMemoryStackSource.GetNodeIndexForSample((StackSourceSampleIndex)i));
                    }
                }
                nodeCount = nodeIdxs.Count;
            }

            StatusBar.Status = "Opening object view on  " + nodeCount + " objects.";
            var objectViewer = new ObjectViewer(this, asMemoryStackSource.Graph, asMemoryStackSource.RefGraph, nodeIdxs);
            objectViewer.Show();
        }

        // Context Menu ETWCommands
        private void DoDrillInto(object sender, ExecutedRoutedEventArgs e)
        {
            bool[] sampleSet;
            string sampleSetName;
            if (!GetSamplesForSelection(out sampleSet, out sampleSetName))
            {
                return;
            }

            var drillIntoSamples = new CopyStackSource(m_stackSource);
            for (int i = 0; i < sampleSet.Length; i++)
            {
                if (sampleSet[i])
                {
                    drillIntoSamples.AddSample(m_stackSource.GetSampleByIndex((StackSourceSampleIndex)i));
                }
            }
            var newStackWindow = new StackWindow(this, this);
            newStackWindow.ExcludeRegExTextBox.Text = "";
            newStackWindow.IncludeRegExTextBox.Text = "";
            newStackWindow.Show();
            newStackWindow.SetStackSource(drillIntoSamples);
        }

        private void DoFlatten(object sender, ExecutedRoutedEventArgs e)
        {
            var origStackSource = new FilterStackSource(Filter, m_stackSource, ScalingPolicy);
            var stackSource = Flatten(origStackSource, GetSelectedSamples());
            var newStackWindow = new StackWindow(this, this);
            newStackWindow.ExcludeRegExTextBox.Text = "";
            newStackWindow.IncludeRegExTextBox.Text = "";
            newStackWindow.FoldPercentTextBox.Text = "";
            newStackWindow.FoldRegExTextBox.Text = "";
            newStackWindow.GroupRegExTextBox.Text = "";
            newStackWindow.Show();
            newStackWindow.SetStackSource(stackSource);
        }

        private IEnumerable<StackSourceSample> GetSelectedSamples()
        {
            bool[] sampleSet;
            string sampleSetName;
            if (GetSamplesForSelection(out sampleSet, out sampleSetName))
            {
                for (int i = 0; i < sampleSet.Length; i++)
                {
                    if (sampleSet[i])
                    {
                        yield return m_stackSource.GetSampleByIndex((StackSourceSampleIndex)i);
                    }
                }
            }
        }

        private InternStackSource Flatten(StackSource source, IEnumerable<StackSourceSample> samples)
        {
            InternStackSource ret = new InternStackSource();

            var sampleCopy = new StackSourceSample(ret);
            foreach (var sample in samples)
            {
                sampleCopy.Metric = sample.Metric;
                sampleCopy.TimeRelativeMSec = sample.TimeRelativeMSec;
                sampleCopy.Count = sample.Count;
                sampleCopy.Scenario = sample.Scenario;
                sampleCopy.StackIndex = StackSourceCallStackIndex.Invalid;
                if (sample.StackIndex != StackSourceCallStackIndex.Invalid)
                {
                    // Get the first frame.
                    var firstFrameIndex = source.GetFrameIndex(sample.StackIndex);
                    var baseFullFrameName = source.GetFrameName(firstFrameIndex, true);
                    var moduleName = "";
                    var frameName = baseFullFrameName;
                    var index = baseFullFrameName.IndexOf('!');
                    if (index >= 0)
                    {
                        moduleName = baseFullFrameName.Substring(0, index);
                        frameName = baseFullFrameName.Substring(index + 1);
                    }

                    // Make a new frame that is flattened. 
                    var myModuleIndex = ret.Interner.ModuleIntern(moduleName);
                    var myFrameIndex = ret.Interner.FrameIntern(frameName, myModuleIndex);
                    sampleCopy.StackIndex = ret.Interner.CallStackIntern(myFrameIndex, StackSourceCallStackIndex.Invalid);
                    ret.AddSample(sampleCopy);
                }
            }
            ret.Interner.DoneInterning();
            return ret;
        }

        private void DoNewWindow(object sender, ExecutedRoutedEventArgs e)
        {
            var newStackWindow = new StackWindow(this, this);

            newStackWindow.Show();
            newStackWindow.SetStackSource(StackSource);
        }
        private void DoFind(object sender, ExecutedRoutedEventArgs e)
        {
            FindTextBox.Focus();
        }
        private void DoFindInByName(object sender, ExecutedRoutedEventArgs e)
        {
            string str = SelectedCellStringValue();
            if (str != null)
            {
                FindByName(str);
            }
            else
            {
                StatusBar.LogError("No selected cells found.");
            }
        }
        private void DoFindInCallTreeName(object sender, ExecutedRoutedEventArgs e)
        {
            string str = SelectedCellStringValue();
            if (str != null)
            {
                CallTreeTab.IsSelected = true;
                // TODO support some sort of escape sequence 
                Find(Regex.Escape(str));
            }
            else
            {
                StatusBar.LogError("No selected cells found.");
            }
        }
        private void DoViewInCallerCallee(object sender, RoutedEventArgs e)
        {
            if (SetFocusNodeToSelection())
            {
                CallerCalleeTab.IsSelected = true;
            }
        }
        private void DoViewInCallers(object sender, ExecutedRoutedEventArgs e)
        {
            if (SetFocusNodeToSelection())
            {
                CallersTab.IsSelected = true;
            }
        }
        private void DoViewInCallees(object sender, ExecutedRoutedEventArgs e)
        {
            if (SetFocusNodeToSelection())
            {
                CalleesTab.IsSelected = true;
            }
        }

        private void DoEntryGroupModule(object sender, ExecutedRoutedEventArgs e)
        {
            DoGroupModuleHelper("=>");
        }
        private void DoGroupModule(object sender, ExecutedRoutedEventArgs e)
        {
            DoGroupModuleHelper("->");
        }
        private void DoGroupModuleHelper(string op)
        {
            var str = GroupRegExTextBox.Text;
            var badStrs = "";
            foreach (string cellStr in SelectedCellsStringValue())
            {
                Match m = Regex.Match(cellStr, @"\b([\w.]*?)!");
                if (m.Success)
                {
                    var groupPat = FilterParams.EscapeRegEx(m.Groups[1].Value) + "!" + op + m.Groups[1].Value;
                    str = AddSet(groupPat, str);
                }
                else
                {
                    if (badStrs.Length > 0)
                    {
                        badStrs += " ";
                    }

                    badStrs += cellStr;
                }
            }
            if (badStrs.Length > 0)
            {
                StatusBar.LogError("Could not find a module pattern in text " + badStrs + ".");
            }

            GroupRegExTextBox.Text = str;
            Update();
        }
        private void DoUngroup(object sender, ExecutedRoutedEventArgs e)
        {
            bool matchedSomething = false;
            foreach (string cellStr in SelectedCellsStringValue())
            {
                // Is it an entry point group? 
                var match = Regex.Match(cellStr, "<<(.*)>>");
                if (match.Success)
                {
                    var ungroupPat = match.Groups[1].Value;

                    // Remove the comment (we put it back at the end)
                    match = Regex.Match(GroupRegExTextBox.Text, @"(^\s*(\[.*?\])?\s*)(.*?)\s*$");
                    var comment = match.Groups[1].Value;
                    var groups = match.Groups[3].Value;

                    GroupRegExTextBox.Text = ungroupPat + "->;" + groups;
                    matchedSomething = true;
                    StatusBar.Log("Excluded " + ungroupPat + " from grouping");
                }
                else
                {
                    // It is not a entry group, Then it is a 'normal' group and to ungroup it we remove it from the grouping spec.
                    if (GroupRegExTextBox.Text.Length != 0)
                    {
                        string newGroups = "";
                        match = Regex.Match(GroupRegExTextBox.Text, @"(^\s*(\[.*?\])?\s*)(.*?)\s*$");
                        var comment = match.Groups[1].Value;
                        var groups = match.Groups[3].Value;

                        foreach (var groupSpec in groups.Split(';'))
                        {
                            match = Regex.Match(groupSpec, "(.*?)([-=]>)(.*)");
                            if (match.Success)
                            {
                                var pat = match.Groups[1].Value;
                                var oper = match.Groups[2].Value;
                                var group = match.Groups[3].Value;
                                // (?<V1>.*) is .NET syntax that names the group V1
                                var groupPat = Regex.Replace(group, @"\$(\d)", "(?<V$1>.*)");
                                match = Regex.Match(cellStr, groupPat);
                                if (match.Success)
                                {
                                    matchedSomething = true;
                                    if (groupPat == group)
                                    {
                                        // If there are no substitution patterns at all then simply remove the
                                        // pattern from the list.  However we keep exclusion patterns (empty groups)
                                        if (groupPat.Length > 0)
                                        {
                                            continue;
                                        }
                                    }
                                    else
                                    {
                                        // The cells value could have been created via this grouping pattern. 
                                        // Create a pattern that matches the {} in the original pattern with 
                                        // the specific instances needed to form the resulting groups (that is
                                        // if we have {*}->module $1 and we are ungrouping 'module mscorlib' then
                                        // make mscorlib->;
                                        char captureNum = '0';
                                        var newPat = Regex.Replace(pat, "{.*?}", delegate (Match m)
                                        {
                                            captureNum++;
                                            var varValue = match.Groups["V" + new string(captureNum, 1)];
                                            if (varValue != null)
                                            {
                                                return varValue.Value;
                                            }

                                            return m.Groups[0].Value;
                                        });
                                        newGroups = ConcatinateSeparatedBy(newGroups, newPat + oper, ";");
                                    }
                                }
                            }
                            newGroups = ConcatinateSeparatedBy(newGroups, groupSpec, ";");
                        }
                        GroupRegExTextBox.Text = newGroups;
                    }
                }
            }
            if (matchedSomething)
            {
                Update();
            }
            else
            {
                StatusBar.LogError("Cell does not have the pattern that can be ungrouped.");
            }
        }

        private void DoRaiseItemPriority(object sender, ExecutedRoutedEventArgs e)
        {
            ChangePriority(1, false);
        }
        private void DoLowerItemPriority(object sender, ExecutedRoutedEventArgs e)
        {
            ChangePriority(-1, false);
        }
        private void DoRaiseModulePriority(object sender, ExecutedRoutedEventArgs e)
        {
            ChangePriority(1, true);
        }
        private void DoLowerModulePriority(object sender, ExecutedRoutedEventArgs e)
        {
            ChangePriority(-1, true);
        }
        private void ChangePriority(int delta, bool module)
        {
            var priorities = PriorityTextBox.Text;
            var badStrs = "";
            foreach (string cellStr in SelectedCellsStringValue())
            {
                string str = Regex.Replace(cellStr, @"\s+\[\w.*?\]\s*$", "");   // Remove any [] at the end 
                if (module)
                {
                    Match m = Regex.Match(cellStr, @"\b([\w.]*?)!");
                    if (m.Success)
                    {
                        str = m.Groups[1].Value + "!";
                    }
                    else
                    {
                        if (badStrs.Length > 0)
                        {
                            badStrs += " ";
                        }

                        badStrs += cellStr;
                    }
                }

                var priorityPats = priorities.Split(';');
                priorities = "";
                bool found = false;
                foreach (var priorityPat in priorityPats)
                {
                    var updatedPriorityPat = priorityPat;
                    if (!found)
                    {
                        Match m = Regex.Match(priorityPat, @"^\s*(.*?)\s*->\s*((-)?\d+(\.\d+)?)\s*$");
                        if (m.Success)
                        {
                            string pat = m.Groups[1].Value;
                            string num = m.Groups[2].Value;
                            if (string.Compare(str, pat, StringComparison.OrdinalIgnoreCase) == 0)
                            {
                                found = true;
                                var newPriority = float.Parse(num) + delta;
                                updatedPriorityPat = str + "->" + newPriority.ToString();
                            }
                        }
                        else
                        {
                            StatusBar.LogError("Priority string '" + priorityPat + "' does not have the syntax Pat->Num.");
                        }
                    }

                    if (priorities.Length == 0)
                    {
                        priorities = updatedPriorityPat;
                    }
                    else
                    {
                        priorities = priorities + ";" + updatedPriorityPat;
                    }
                }
                if (!found)
                {
                    var newPat = str + "->" + delta.ToString();
                    if (priorities.Length == 0)
                    {
                        priorities = newPat;
                    }
                    else
                    {
                        priorities = newPat + ";" + priorities;
                    }
                }
            }
            if (badStrs.Length > 0)
            {
                StatusBar.LogError("Could not find a module pattern in text " + badStrs + ".");
            }

            PriorityTextBox.Text = priorities;
            Update();
        }

        private static string ConcatinateSeparatedBy(string str1, string str2, string sep)
        {
            if (str1.Length == 0)
            {
                return str2;
            }

            return str1 + sep + str2;
        }

        private void DoUngroupModule(object sender, ExecutedRoutedEventArgs e)
        {
            bool matchedSomething = false;
            foreach (string cellStr in SelectedCellsStringValue())
            {
                string module = null;
                var match = Regex.Match(cellStr, @"([\w.]+)!");
                if (match.Success)
                {
                    module = match.Groups[1].Value;
                }
                else
                {
                    match = Regex.Match(cellStr, @"module (\S+)");
                    if (match.Success)
                    {
                        module = match.Groups[1].Value;
                    }
                }

                if (module != null)
                {
                    // Remove the comment (we put it back at the end)
                    match = Regex.Match(GroupRegExTextBox.Text, @"(^\s*(\[.*?\])?\s*)(.*?)\s*$");
                    var comment = match.Groups[1].Value;
                    var groups = match.Groups[3].Value;

                    GroupRegExTextBox.Text = module + "!->;" + groups;
                    matchedSomething = true;
                    StatusBar.Log("Excluded module " + module + " from grouping");
                }
            }
            if (matchedSomething)
            {
                Update();
            }
            else
            {
                StatusBar.LogError("Cell does not have the pattern of a entry group.");
            }
        }
        private void DoFoldModule(object sender, ExecutedRoutedEventArgs e)
        {
            var str = FoldRegExTextBox.Text;
            DoForSelectedModules(delegate (string moduleName)
            {
                str = AddSet(str, FilterParams.EscapeRegEx(moduleName) + "!");
            });
            FoldRegExTextBox.Text = str;
            Update();
        }
        private void DoFoldItem(object sender, ExecutedRoutedEventArgs e)
        {
            var str = FoldRegExTextBox.Text;
            foreach (string cellStr in SelectedCellsStringValue())
            {
                str = AddSet(str, FilterParams.EscapeRegEx(cellStr));        // TODO need a good anchor
            }

            FoldRegExTextBox.Text = str;
            Update();
        }
        private void DoRemoveAllFolding(object sender, ExecutedRoutedEventArgs e)
        {
            FoldRegExTextBox.Text = "";
            FoldPercentTextBox.Text = "";
            Update();
        }
        private void DoCopyTimeRange(object sender, ExecutedRoutedEventArgs e)
        {
            Clipboard.SetText(RangeUtilities.ToString(StartTextBox.Text, EndTextBox.Text));
        }
        private void CanDoOpenEvents(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = false;
            var asETLData = DataSource.DataFile as ETLPerfViewData;
            if (asETLData != null)
            {
                e.CanExecute = true;
            }
        }
        private void DoOpenEvents(object sender, ExecutedRoutedEventArgs e)
        {
            var cells = SelectedCells();
            if (cells != null && (cells.Count == 1 || cells.Count == 2))
            {
                var start = GetCellStringValue(cells[0]);
                var end = start;
                if (cells.Count == 2)
                {
                    end = GetCellStringValue(cells[1]);
                }

                double dummy;
                if (!double.TryParse(start, out dummy) || !double.TryParse(end, out dummy))
                {
                    StatusBar.LogError("Could not parse cells as a time range.");
                    return;
                }

                PerfViewEventSource eventSource = null;
                foreach (var child in DataSource.DataFile.Children)
                {
                    eventSource = child as PerfViewEventSource;
                    if (eventSource != null)
                    {
                        break;
                    }
                }
                if (eventSource == null)
                {
                    StatusBar.Log("This data file does not support the Events view");
                    return;
                }

                eventSource.Open(ParentWindow, StatusBar, delegate
                {
                    var viewer = eventSource.Viewer;
                    viewer.StartTextBox.Text = start;
                    viewer.EndTextBox.Text = end;
                    viewer.EventTypes.SelectAll();
                    viewer.Update();
                });

            }
            else
            {
                StatusBar.LogError("You must select one or two cells to act as the focus region.");
            }
        }

        private void DoSetTimeRange(object sender, ExecutedRoutedEventArgs e)
        {
            var focusTextBox = Keyboard.FocusedElement as TextBox;
            if (focusTextBox == null && PerfDataGrid.EditingBox != null && PerfDataGrid.EditingBox.IsFocused)
            {
                focusTextBox = PerfDataGrid.EditingBox;
            }

            if (focusTextBox != null)
            {
                if (focusTextBox.SelectionLength != 0)
                {
                    var selectionStartIndex = focusTextBox.SelectionStart;
                    var selectionLen = focusTextBox.SelectionLength;
                    var text = focusTextBox.Text;

                    // If you accidentally select the space before the selection, skip it
                    if (0 <= selectionStartIndex && selectionStartIndex < text.Length && text[selectionStartIndex] == ' ')
                    {
                        selectionStartIndex++;
                    }

                    // grab the last 32 bytes of the string.  
                    var histStart = text.Length - CallTree.TimeHistogramController.BucketCount;
                    if (histStart >= 0)
                    {
                        var histStr = text.Substring(histStart);
                        if (Regex.IsMatch(histStr, @"^[_*\w.]") && selectionStartIndex >= histStart)
                        {
                            var bucketStartIndex = (HistogramCharacterIndex)(selectionStartIndex - histStart);
                            var bucketEndIndex = (HistogramCharacterIndex)(bucketStartIndex + selectionLen);

                            StartTextBox.Text = CallTree.TimeHistogramController.GetStartTimeForBucket(bucketStartIndex).ToString("n3");
                            EndTextBox.Text = CallTree.TimeHistogramController.GetStartTimeForBucket(bucketEndIndex).ToString("n3");
                            Update();
                        }
                    }
                    return;
                }
            }

            var cells = SelectedCells();
            if (cells != null)
            {
                var callTreeNodes = cells.Select(cell => ToCallTreeNodeBase(cell.Item)).Where(cell => cell != null);
                if (callTreeNodes.Any())
                {
                    StartTextBox.Text = callTreeNodes.Min(node => node.FirstTimeRelativeMSec).ToString("n3");
                    EndTextBox.Text = callTreeNodes.Max(node => node.LastTimeRelativeMSec).ToString("n3");
                    Update();
                }
                else
                {
                    StatusBar.LogError("Could not set time range.");
                }
            }
        }

        // Given a CallTreeViewNode or a CallTreeNodeBase (this is what might be in the 'Item' list of a view)
        // return a CallTreeNodeBase or null if it is none of those things.   
        private CallTreeNodeBase ToCallTreeNodeBase(object viewOrDataObject)
        {
            var asViewNode = viewOrDataObject as CallTreeViewNode;
            if (asViewNode != null)
            {
                return asViewNode.Data;
            }

            return viewOrDataObject as CallTreeNodeBase;
        }

        // Scenario-related stuff
        private void CanDoScenario(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = IsScenarioWindow;
        }

        private bool IsScenarioElement(object originalSource)
        {
            var asDO = originalSource as DependencyObject;

            if (asDO == null)
            {
                return false;
            }

            var cell = asDO.AncestorOfType<DataGridCell>();
            if (cell == null || cell.Column == null)
            {
                return false;
            }

            var grid = cell.AncestorOfType<PerfDataGrid>();
            if (grid == null)
            {
                return false;
            }

            return (cell.Column == grid.ScenarioHistogramColumn);
        }

        private void CanSortScenariosByNode(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = IsScenarioWindow && IsScenarioElement(e.OriginalSource);
        }

        private void CanSetScenarioList(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = IsScenarioWindow && e.OriginalSource is TextBox && IsScenarioElement(e.OriginalSource);
        }

        private void DoSetScenarioList(object sender, ExecutedRoutedEventArgs e)
        {
            var box = Keyboard.FocusedElement as TextBox;
            if (box == null)
            {
                return;
            }

            var scenarioList = m_callTree.ScenarioHistogram.GetScenariosForCharacterRange(
                (HistogramCharacterIndex)(box.SelectionStart),
                (HistogramCharacterIndex)(box.SelectionStart + box.SelectionLength));

            var f = Filter;
            f.ScenarioList = scenarioList;
            Filter = f;
            Update();
        }

        private void DoCopyScenarioList(object sender, ExecutedRoutedEventArgs e)
        {
            var box = Keyboard.FocusedElement as TextBox;
            var result = ScenarioTextBox.Text;
            if (box != null)
            {
                var scenarioList = m_callTree.ScenarioHistogram.GetScenariosForCharacterRange(
                    (HistogramCharacterIndex)(box.SelectionStart),
                    (HistogramCharacterIndex)(box.SelectionStart + box.SelectionLength));

                result = String.Join(",", Array.ConvertAll(scenarioList, x => x.ToString()));
            }
            Clipboard.SetText(result);
        }

        private void DoCopyScenarioListNames(object sender, ExecutedRoutedEventArgs e)
        {
            var box = Keyboard.FocusedElement as TextBox;
            var sb = new StringBuilder();
            if (box != null)
            {
                var scenarioList = m_callTree.ScenarioHistogram.GetScenariosForCharacterRange(
                    (HistogramCharacterIndex)(box.SelectionStart),
                    (HistogramCharacterIndex)(box.SelectionStart + box.SelectionLength));
                foreach (var scenario in scenarioList)
                {
                    sb.AppendLine(m_callTree.ScenarioHistogram.GetNameForScenario(scenario));
                }
            }
            else
            {
                for (int i = 0; i < m_callTree.StackSource.ScenarioCount; i++)
                {
                    sb.AppendLine(m_callTree.ScenarioHistogram.GetNameForScenario(i));
                }
            }
            Clipboard.SetText(sb.ToString());
        }

        private void DoSortScenariosByDefault(object sender, ExecutedRoutedEventArgs e)
        {
            var scenarios = Filter.ScenarioList ?? Enumerable.Range(0, m_callTree.StackSource.ScenarioCount).ToArray();

            Array.Sort(scenarios);

            var f = Filter;
            f.ScenarioList = scenarios;
            Filter = f;
            Update();
        }

        private void SortScenariosByNode(CallTreeNodeBase node)
        {
            var histogram = node.InclusiveMetricByScenario;

            var f = Filter;
            var scenarioList = f.ScenarioList ?? Enumerable.Range(0, m_callTree.StackSource.ScenarioCount);
            var newScenarioList =
                from scenario in scenarioList
                orderby histogram[scenario] descending
                select scenario;

            f.ScenarioList = newScenarioList.ToArray();
            Filter = f;
            Update();
        }

        private void DoSortScenariosByRootNode(object sender, ExecutedRoutedEventArgs e)
        {
            SortScenariosByNode(m_callTree.Root);
        }

        private void DoSortScenariosByThisNode(object sender, ExecutedRoutedEventArgs e)
        {
            var asDO = Keyboard.FocusedElement as DependencyObject;
            if (asDO == null)
            {
                return;
            }

            var cell = asDO.AncestorOfType<DataGridCell>();
            if (cell == null)
            {
                return;
            }

            var context = cell.DataContext as CallTreeNodeBase;
            if (context == null)
            {
                return;
            }

            SortScenariosByNode(context);
        }

        private void DoIncludeItem(object sender, ExecutedRoutedEventArgs e)
        {
            // Add a | operator between all the values 
            var incPat = "";
            foreach (string cellStr in SelectedCellsStringValue())
            {
                if (incPat.Length != 0)
                {
                    incPat += "|";
                }

                var pat = cellStr;
                if (pat.IndexOf('!') < 0)
                {
                    pat = "^" + pat;
                }

                incPat += FilterParams.EscapeRegEx(pat);
            }

            IncludeRegExTextBox.Text = AddSet(IncludeRegExTextBox.Text, incPat);
            Update();
        }
        private void DoExcludeItem(object sender, ExecutedRoutedEventArgs e)
        {
            var str = ExcludeRegExTextBox.Text;
            foreach (string cellStr in SelectedCellsStringValue())
            {
                var pat = cellStr;
                if (pat.IndexOf('!') < 0)
                {
                    pat = "^" + pat;
                }

                str = AddSet(str, FilterParams.EscapeRegEx(pat));
            }
            ExcludeRegExTextBox.Text = str;
            Update();
        }
        private void DoCopyFilterParams(object sender, ExecutedRoutedEventArgs e)
        {
            StringBuilder sb = new StringBuilder();
            using (XmlWriter writer = XmlWriter.Create(sb, new XmlWriterSettings() { Indent = true, NewLineOnAttributes = true }))
            {
                FilterGuiState.WriteToXml("FilterGuiState", writer);
            }

            Clipboard.SetText(sb.ToString());
        }
        private void DoMergeFilterParams(object sender, ExecutedRoutedEventArgs e)
        {
            string text = Clipboard.GetText();

            // Read XML into filterGuiState.  
            XmlReaderSettings settings = new XmlReaderSettings() { IgnoreWhitespace = true, IgnoreComments = true };
            XmlReader reader = XmlReader.Create(new StringReader(text), settings);
            var filterGuiState = new FilterGuiState();
            filterGuiState.ReadFromXml(reader);

            FilterGuiState = filterGuiState;
            Update();
        }
        internal void DoLookupWarmSymbols(object sender, ExecutedRoutedEventArgs e)
        {
            int processID = 0;
            var m = Regex.Match(IncludeRegExTextBox.Text, @"Process[^;()]*\((\d+)\)");
            if (m.Success)
            {
                // TODO we can do a better job here.  If we have mulitple processe specs we simply don't focus right now. 
                if (!m.Groups[2].Value.Contains("Process"))
                {
                    processID = int.Parse(m.Groups[1].Value);
                }
            }

            var etlDataFile = DataSource.DataFile as ETLPerfViewData;
            if (etlDataFile == null)
            {
                StatusBar.Log("DataSource does not support symbol lookup");
                return;
            }

            var filter = Filter;        // Fetch when you are still on the GUI thread.  
            StatusBar.StartWork("Warm Symbol Lookup", delegate ()
            {
                var filteredSource = new FilterStackSource(filter, m_stackSource, ScalingPolicy);
                PrimeWarmSymbols(filteredSource, processID, etlDataFile, StatusBar.LogWriter);
                StatusBar.EndWork(delegate ()
                {
                    Update();
                });
            });
        }

        private void DoPri1Only(object sender, RoutedEventArgs e)
        {
            var isChecked = (Pri1OnlyCheckBox.IsChecked ?? false);
            m_callTreeView.DisplayPrimaryOnly = isChecked;
            m_callersView.DisplayPrimaryOnly = isChecked;
            m_calleesView.DisplayPrimaryOnly = isChecked;
            Update();
        }

        // TODO FIX NOW clean up symbols
        /// <summary>
        /// Given a source of stacks, a process ID, and and ETL file look up all the symbols for any module in
        /// that process that has more than 5% CPU time inclusive.   
        /// </summary>
        private static void PrimeWarmSymbols(StackSource stackSource, int processID, ETLPerfViewData etlFile, TextWriter log)
        {
            // Compute inclusive metric for every module into moduleMetrics
            var moduleMetrics = new GrowableArray<float>(50);
            moduleMetrics.Add(0);                                // Index 0 is illegal.  
            var modIdxes = new Dictionary<string, int>(50);      // maps a module name to its count index
            var frameIdxToCountIdx = new int[stackSource.CallFrameIndexLimit];
            var totalMetric = 0.0F;
            var modulesSeenOnStack = new Dictionary<int, int>(16);

            stackSource.ForEach(delegate (StackSourceSample sample)
            {
                totalMetric += sample.Metric;
                var stackIdx = sample.StackIndex;
                modulesSeenOnStack.Clear();
                while (stackIdx != StackSourceCallStackIndex.Invalid)
                {
                    var frameIdx = stackSource.GetFrameIndex(stackIdx);
                    var moduleIdx = frameIdxToCountIdx[(int)frameIdx];
                    if (moduleIdx == 0)
                    {
                        moduleIdx = -1;
                        var frameName = stackSource.GetFrameName(frameIdx, false);
                        var m = Regex.Match(frameName, @"([\w.]+)!");
                        if (m.Success)
                        {
                            var modName = m.Groups[1].Value;
                            if (!modIdxes.TryGetValue(modName, out moduleIdx))
                            {
                                modIdxes[modName] = moduleIdx = moduleMetrics.Count;
                                moduleMetrics.Add(0);
                            }
                        }
                        frameIdxToCountIdx[(int)frameIdx] = moduleIdx;
                    }
                    if (moduleIdx > 0)
                    {
                        if (!modulesSeenOnStack.ContainsKey(moduleIdx))
                        {
                            modulesSeenOnStack[moduleIdx] = 1;  // I don't actually us
                            moduleMetrics[moduleIdx] += sample.Metric;
                        }
                    }
                    stackIdx = stackSource.GetCallerIndex(stackIdx);
                }
            });

            // For any module with more than 5% inclusive time, lookup symbols.  
            var modulesToLookUp = new List<string>(10);

            foreach (string moduleName in modIdxes.Keys)
            {
                var metric = moduleMetrics[modIdxes[moduleName]];
                var percent = metric * 100.0 / totalMetric;
                if (percent > 2)
                {
                    log.WriteLine("Module " + moduleName + " has " + percent.ToString("f1") + "% metric (inclusive), looking up symbols.");
                    modulesToLookUp.Add(moduleName);
                }
            }

            foreach (var moduleToLookup in modulesToLookUp)
            {
                try
                {
                    etlFile.LookupSymbolsForModule(moduleToLookup, log, processID);
                }
                catch (ApplicationException ex)
                {
                    log.WriteLine("Error looking up " + moduleToLookup + "\r\n    " + ex.Message);
                }
            }

        }

        private void DoLookupSymbols(object sender, ExecutedRoutedEventArgs e)
        {
            int processID = 0;
            var m = Regex.Match(IncludeRegExTextBox.Text, @"Process[^;()]*\((\d+)\)(.*)");
            if (m.Success)
            {
                // TODO we can do a better job here.  If we have multiple processes specs we simply don't focus right now. 
                if (!m.Groups[2].Value.Contains("Process"))
                {
                    processID = int.Parse(m.Groups[1].Value);
                }
            }

            //create the list of module names to look up
            var moduleNames = new HashSet<string>();
            var success = DoForSelectedModules(delegate (string moduleName)
            {
                moduleNames.Add(moduleName);
            });

            if (DataSource.Title.StartsWith("Diff"))
            {
                StatusBar.LogError("Symbol lookup for Diff not supported.  You must look up symbols before doing the diff.");
                return;
            }


            // Look them up.
            StatusBar.StartWork("Symbol Lookup", delegate ()
            {
                foreach (var moduleName in moduleNames)
                {
                    StatusBar.LogWriter.WriteLine();
                    StatusBar.LogWriter.WriteLine("***************************************************************************");
                    StatusBar.LogWriter.WriteLine("[Looking up symbols for " + moduleName + "]");
                    try
                    {
                        DataSource.DataFile.LookupSymbolsForModule(moduleName, StatusBar.LogWriter, processID);
                        StatusBar.Log("Finished Lookup up symbols for " + moduleName + " Elapsed Time = " +
                            StatusBar.Duration.TotalSeconds.ToString("n3"));
                    }
                    catch (ApplicationException ex)
                    {
                        StatusBar.LogError("Error looking up " + moduleName + "\r\n    " + ex.Message);
                    }
                }
                StatusBar.EndWork(delegate ()
                {
                    Update();
                });
            });
        }
        private void DoGotoSource(object sender, ExecutedRoutedEventArgs e)
        {
            var cells = SelectedCells();
            if (cells == null || cells.Count == 0)
            {
                StatusBar.LogError("No cells selected.");
                return;
            }
            if (cells.Count != 1)
            {
                StatusBar.LogError("More than one cell selected.");
                return;
            }
            var cell = cells[0];
            var cellText = GetCellStringValue(cell);
            if (cellText.EndsWith("!?"))
            {
                StatusBar.LogError("You must lookup symbols before looking up source.");
                return;
            }
            if (!cellText.Contains("!"))
            {
                StatusBar.LogError("Source lookup only works on cells of the form dll!method.");
                return;
            }
            var item = cell.Item;
            var asCallTreeNodeBase = item as CallTreeNodeBase;
            if (asCallTreeNodeBase == null)
            {
                var asCallTreeViewNode = item as CallTreeViewNode;
                if (asCallTreeViewNode != null)
                {
                    asCallTreeNodeBase = asCallTreeViewNode.Data;
                }
                else
                {
                    StatusBar.LogError("Could not find data item.");
                    return;
                }
            }

            StatusBar.StartWork("Fetching Source code for " + cellText, delegate ()
            {
                // Try to default to TLS1.2
                try
                {
                    System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
                }
                catch(NotSupportedException ex)
                {
                    StatusBar.Log($"Failed to set security protocol to TLS1.2: {ex}");
                    throw;
                }

                SortedDictionary<int, float> metricOnLine;
                var sourceLocation = GetSourceLocation(asCallTreeNodeBase, cellText, out metricOnLine);

                string sourcePathToOpen = null;
                string logicalSourcePath = null;
                bool checksumMatches = false;
                if (sourceLocation != null)
                {
                    StatusBar.Log("Found source at Line: " + sourceLocation.LineNumber + " in build time source path " +
                        sourceLocation.SourceFile.BuildTimeFilePath);
                    var sourceFile = sourceLocation.SourceFile;
                    logicalSourcePath = sourceFile.GetSourceFile();
                    if (logicalSourcePath != null)
                    {
                        checksumMatches = sourceFile.ChecksumMatches;
                    }

                    sourcePathToOpen = logicalSourcePath;
                    if (sourcePathToOpen != null)
                    {
                        StatusBar.Log("Resolved source file to " + sourcePathToOpen);
                        if (metricOnLine != null)
                        {
                            sourcePathToOpen = CacheFiles.FindFile(sourcePathToOpen, Path.GetExtension(sourcePathToOpen));
                            StatusBar.Log("Annotating source with metric to the file " + sourcePathToOpen);
                            AnnotateLines(logicalSourcePath, sourcePathToOpen, metricOnLine);
                        }
                    }
                }
                StatusBar.EndWork(delegate ()
                {
                    if (sourcePathToOpen != null)
                    {
                        StatusBar.Log("Viewing line " + sourceLocation.LineNumber + " in " + logicalSourcePath);

                        // TODO FIX NOW this is a hack
                        var notepad2 = Command.FindOnPath("notepad2.exe");
                        Window dialogParentWindow = this;
                        if (notepad2 != null)
                        {
                            Command.Run(Command.Quote(notepad2) + " /g " + sourceLocation.LineNumber + " "
                                + Command.Quote(sourcePathToOpen), new CommandOptions().AddStart());
                        }
                        else
                        {
                            StatusBar.Log("Opening editor on " + sourcePathToOpen);
                            var textEditorWindow = new TextEditorWindow(this);
                            dialogParentWindow = textEditorWindow;
                            textEditorWindow.TextEditor.IsReadOnly = true;
                            textEditorWindow.TextEditor.OpenText(sourcePathToOpen);
                            textEditorWindow.Show();
                            textEditorWindow.TextEditor.GotoLine(sourceLocation.LineNumber);
                        }
                        if (!checksumMatches)
                        {
                            StatusBar.LogError("Warning: Source code Mismatch for " + Path.GetFileName(logicalSourcePath));
                        }
                    }
                });
            });
        }

        // TODO FIX NOW review 
        private SourceLocation GetSourceLocation(CallTreeNodeBase asCallTreeNodeBase, string cellText,
            out SortedDictionary<int, float> metricOnLine)
        {
            metricOnLine = null;
            var m = Regex.Match(cellText, "<<(.*!.*)>>");
            if (m.Success)
            {
                cellText = m.Groups[1].Value;
            }

            // Find the most numerous call stack
            // TODO this can be reasonably expensive.   If it is a problem do something about it (e.g. sampling)
            var frameIndexCounts = new Dictionary<StackSourceFrameIndex, float>();
            asCallTreeNodeBase.GetSamples(false, delegate (StackSourceSampleIndex sampleIdx)
            {
                // Find the callStackIdx which corresponds to the name in the cell, and log it to callStackIndexCounts
                var matchingFrameIndex = StackSourceFrameIndex.Invalid;
                var sample = m_stackSource.GetSampleByIndex(sampleIdx);
                var callStackIdx = sample.StackIndex;
                while (callStackIdx != StackSourceCallStackIndex.Invalid)
                {
                    var frameIndex = m_stackSource.GetFrameIndex(callStackIdx);
                    var frameName = m_stackSource.GetFrameName(frameIndex, false);
                    if (frameName == cellText)
                    {
                        matchingFrameIndex = frameIndex;        // We keep overwriting it, so we get the entry closest to the root.  
                    }

                    callStackIdx = m_stackSource.GetCallerIndex(callStackIdx);
                }
                if (matchingFrameIndex != StackSourceFrameIndex.Invalid)
                {
                    float count = 0;
                    frameIndexCounts.TryGetValue(matchingFrameIndex, out count);
                    frameIndexCounts[matchingFrameIndex] = count + sample.Metric;
                }
                return true;
            });

            // Get the frame with the most counts, we go to THAT line and only open THAT file.
            // If other samples are in that file we also display them but it is this maximum
            // that drives which file we open and where we put the editor's focus.  
            StackSourceFrameIndex maxFrameIdx = StackSourceFrameIndex.Invalid;
            float maxFrameIdxCount = -1;
            foreach (var keyValue in frameIndexCounts)
            {
                if (keyValue.Value >= maxFrameIdxCount)
                {
                    maxFrameIdxCount = keyValue.Value;
                    maxFrameIdx = keyValue.Key;
                }
            }

            StatusBar.LogWriter.WriteLine("Maximum count for {0} = {1}", cellText, maxFrameIdxCount);

            if (maxFrameIdx == StackSourceFrameIndex.Invalid)
            {
                StatusBar.LogError("Could not find " + cellText + " in call stack!");
                return null;
            }

            // Find the most primitive TraceEventStackSource
            TraceEventStackSource asTraceEventStackSource = PerfViewExtensibility.Stacks.GetTraceEventStackSource(m_stackSource);
            if (asTraceEventStackSource == null)
            {
                StatusBar.LogError("Source does not support symbolic lookup.");
                return null;
            }

            var reader = DataSource.DataFile.GetSymbolReader(StatusBar.LogWriter);

            var frameToLine = new Dictionary<StackSourceFrameIndex, int>();

            // OK actually get the source location of the maximal value (our return value). 
            var sourceLocation = asTraceEventStackSource.GetSourceLine(maxFrameIdx, reader);
            if (sourceLocation != null)
            {
                var filePathForMax = sourceLocation.SourceFile.BuildTimeFilePath;
                metricOnLine = new SortedDictionary<int, float>();
                // Accumulate the counts on a line basis
                foreach (StackSourceFrameIndex frameIdx in frameIndexCounts.Keys)
                {
                    var loc = asTraceEventStackSource.GetSourceLine(frameIdx, reader);
                    if (loc != null && loc.SourceFile.BuildTimeFilePath == filePathForMax)
                    {
                        frameToLine[frameIdx] = loc.LineNumber;
                        float metric;
                        metricOnLine.TryGetValue(loc.LineNumber, out metric);
                        metric += frameIndexCounts[frameIdx];
                        metricOnLine[loc.LineNumber] = metric;
                    }
                }
            }

            // show the frequency on a per address form.  

            bool commonMethodIdxSet = false;
            MethodIndex commonMethodIdx = MethodIndex.Invalid;

            var nativeAddressFreq = new SortedDictionary<Address, Tuple<int, float>>();
            foreach (var keyValue in frameIndexCounts)
            {
                var codeAddr = asTraceEventStackSource.GetFrameCodeAddress(keyValue.Key);
                if (codeAddr != CodeAddressIndex.Invalid)
                {
                    var methodIdx = asTraceEventStackSource.TraceLog.CodeAddresses.MethodIndex(codeAddr);
                    if (methodIdx != MethodIndex.Invalid)
                    {
                        if (!commonMethodIdxSet)
                        {
                            commonMethodIdx = methodIdx;            // First time, set it as the common method.  
                        }
                        else if (methodIdx != commonMethodIdx)
                        {
                            methodIdx = MethodIndex.Invalid;        // More than one method, give up.  
                        }

                        commonMethodIdxSet = true;
                    }

                    var nativeAddr = asTraceEventStackSource.TraceLog.CodeAddresses.Address(codeAddr);
                    var lineNum = 0;
                    frameToLine.TryGetValue(keyValue.Key, out lineNum);
                    nativeAddressFreq[nativeAddr] = new Tuple<int, float>(lineNum, keyValue.Value);
                }
            }
            StatusBar.LogWriter.WriteLine();
            StatusBar.LogWriter.WriteLine("Metric as a function of code address");
            StatusBar.LogWriter.WriteLine("      Address    :   Line     Metric");
            foreach (var keyValue in nativeAddressFreq)
            {
                StatusBar.LogWriter.WriteLine("    {0,12:x} : {1,6} {2,10:f1}", keyValue.Key, keyValue.Value.Item1, keyValue.Value.Item2);
            }

            if (sourceLocation == null)
            {
                StatusBar.LogError("Source could not find a source location for the given Frame.");
                return null;
            }

            StatusBar.LogWriter.WriteLine();
            StatusBar.LogWriter.WriteLine("Metric per line in the file {0}", Path.GetFileName(sourceLocation.SourceFile.BuildTimeFilePath));
            foreach (var keyVal in metricOnLine)
            {
                StatusBar.LogWriter.WriteLine("    Line {0,5}:  Metric {1,5:n1}", keyVal.Key, keyVal.Value);
            }

            return sourceLocation;
        }

        private void AnnotateLines(string inFileName, string outFileName, SortedDictionary<int, float> lineData)
        {
            using (var inFile = File.OpenText(inFileName))
            using (var outFile = File.CreateText(outFileName))
            {
                int lineNum = 0;
                for (; ; )
                {
                    var line = inFile.ReadLine();
                    if (line == null)
                    {
                        break;
                    }

                    lineNum++;

                    float value;
                    if (lineData.TryGetValue(lineNum, out value))
                    {
                        outFile.Write(ToCompactString(value));
                    }
                    else if (lineNum == 1)
                    {
                        outFile.Write("Metric|");
                    }
                    else
                    {
                        outFile.Write("       ");
                    }

                    outFile.WriteLine(line);
                }
            }
        }

        /// <summary>
        /// Creat a string that fits in 4 chars + a trailing space. 
        /// </summary>
        private string ToCompactString(float value)
        {
            var suffix = " |";
            for (int i = 0; ; i++)
            {
                if (value < 999.95)
                {
                    return value.ToString("f1").PadLeft(5) + suffix;
                }

                value = value / 1000;
                if (i == 0)
                {
                    suffix = "K|";
                }
                else if (i == 1)
                {
                    suffix = "M|";
                }
                else if (i == 2)
                {
                    suffix = "G|";
                }
                else
                {
                    return "******|";
                }
            }
        }

        private void DoExpandAll(object sender, ExecutedRoutedEventArgs e)
        {
            CallTreeViewNode selectedNode = null;
            if (CallTreeTab.IsSelected)
            {
                selectedNode = CallTreeView.SelectedNode;
            }
            else if (CallersTab.IsSelected)
            {
                selectedNode = m_callersView.SelectedNode;
            }
            else if (CalleesTab.IsSelected)
            {
                selectedNode = m_calleesView.SelectedNode;
            }

            if (selectedNode != null)
            {
                selectedNode.ExpandToDepth(int.MaxValue, selectExpandedNode: false); // we don't want to select every node while expanding, it takes too much time
            }
        }

        private void DoExpand(object sender, ExecutedRoutedEventArgs e)
        {
            CallTreeViewNode selectedNode = null;
            CallTreeView view = null;
            if (CallTreeTab.IsSelected)
            {
                view = CallTreeView;
            }
            else if (CallersTab.IsSelected)
            {
                view = m_callersView;
            }
            else if (CalleesTab.IsSelected)
            {
                view = m_calleesView;
            }

            if (view != null)
            {
                selectedNode = view.SelectedNode;
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
                        {
                            break;
                        }

                        selectedNode = children[0];
                    }
                }
            }
        }

        private void DoCollapse(object sender, ExecutedRoutedEventArgs e)
        {
            CallTreeViewNode selectedNode = null;
            CallTreeView view = null;
            if (CallTreeTab.IsSelected)
            {
                view = CallTreeView;
            }
            else if (CallersTab.IsSelected)
            {
                view = m_callersView;
            }
            else if (CalleesTab.IsSelected)
            {
                view = m_calleesView;
            }

            if (view != null)
            {
                selectedNode = view.SelectedNode;
                if (selectedNode != null)
                {
                    selectedNode.IsExpanded = false;
                }
            }
        }

        private void CanExpand(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = (CallTreeTab.IsSelected && CallTreeView.SelectedNode != null) ||
                           (CallersTab.IsSelected && m_callersView.SelectedNode != null) ||
                           (CalleesTab.IsSelected && m_calleesView.SelectedNode != null);
        }

        private void DoSetBrownBackgroundColor(object sender, ExecutedRoutedEventArgs e)
        {
            DoSetBackgroundColor(sender, e, System.Drawing.Color.BurlyWood);
        }

        private void DoSetBlueBackgroundColor(object sender, ExecutedRoutedEventArgs e)
        {
            DoSetBackgroundColor(sender, e, System.Drawing.Color.LightSkyBlue);
        }

        private void DoSetRedBackgroundColor(object sender, ExecutedRoutedEventArgs e)
        {
            DoSetBackgroundColor(sender, e, System.Drawing.Color.Coral);
        }

        private void DoSetBackgroundColor(object sender, ExecutedRoutedEventArgs e, System.Drawing.Color color)
        {
            CallTreeViewNode selectedNode = null;
            CallTreeView view = null;
            if (CallTreeTab.IsSelected)
            {
                view = CallTreeView;
            }
            else if (CallersTab.IsSelected)
            {
                view = m_callersView;
            }
            else if (CalleesTab.IsSelected)
            {
                view = m_calleesView;
            }

            if (view != null)
            {
                selectedNode = view.SelectedNode;
                if (selectedNode != null)
                {
                    selectedNode.SetBackgroundColor(color);
                    view.m_perfGrid.Grid.Items.Refresh();
                }
            }
        }

        private void CanSetBackgroundColor(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = (CallTreeTab.IsSelected && CallTreeView.SelectedNode != null) ||
                           (CallersTab.IsSelected && m_callersView.SelectedNode != null) ||
                           (CalleesTab.IsSelected && m_calleesView.SelectedNode != null);
        }

        private void DoFoldPercent(object sender, ExecutedRoutedEventArgs e)
        {
            FoldPercentTextBox.Focus();
        }
        private void DoIncreaseFoldPercent(object sender, ExecutedRoutedEventArgs e)
        {
            float newVal;
            if (float.TryParse(FoldPercentTextBox.Text, out newVal))
            {
                FoldPercentTextBox.Text = (newVal * 1.6).ToString("f2");
            }

            Update();
        }
        private void DoDecreaseFoldPercent(object sender, ExecutedRoutedEventArgs e)
        {
            float newVal;
            if (float.TryParse(FoldPercentTextBox.Text, out newVal))
            {
                FoldPercentTextBox.Text = (newVal / 1.6).ToString("f2");
            }

            Update();
        }
        // turns off menus for options that only make sense in the callTree view.  
        private void InCallTree(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = CallTreeTab.IsSelected;
        }
        private void DoHyperlinkHelp(object sender, ExecutedRoutedEventArgs e)
        {
            var param = e.Parameter as string;
            if (param == null)
            {
                param = "StackViewerQuickStart";       // This is the F1 help
            }

            // We have specific help for GC heap stacks 
            if (DataSource.DataFile is ClrProfilerHeapPerfViewFile ||
                DataSource.DataFile is HeapDumpPerfViewFile)
            {
                if (param == "StartingAnAnalysis" || param == "UnderstandingPerfData" || param == "StackViewerQuickStart" || param == "Tutorial")
                {
                    param += "GCHeap";
                }
            }
            else if (DataSource.SourceName.Contains("Thread Time"))
            {
                if (param == "StartingAnAnalysis")
                {
                    param = "BlockedTimeInvestigation";
                }
                else if (param == "UnderstandingPerfData")
                {
                    if (DataSource.SourceName.Contains("with Tasks"))
                    {
                        param = "UnderstandingPerfDataThreadTimeWithTasks";
                    }
                    else
                    {
                        param = "UnderstandingPerfDataThreadTime";
                    }
                }
            }
            else if (DataSource.SourceName.StartsWith("GC Heap"))
            {
                if (param == "StartingAnAnalysis")
                {
                    param = "GCHeapNetMemStacks";
                }
                else if (param == "UnderstandingPerfData")
                {
                    param = "GCHeapNetMemStacks";
                }
            }
            else if (DataSource.SourceName.StartsWith("Net Virtual") || DataSource.SourceName == "Net OS Heap Alloc")
            {
                if (param == "StartingAnAnalysis")
                {
                    param = "UnmanagedMemoryAnalysis";
                }
                else if (param == "UnderstandingPerfData")
                {
                    param = "UnmanagedMemoryAnalysis";
                }
            }

            StatusBar.Log("Displaying Users Guide in Web Browser.");
            MainWindow.DisplayUsersGuide(param);
        }

        /// <summary>
        /// Sets the focus node to the currently selected cell, returns true if successful.  
        /// </summary>
        /// <returns></returns>
        private bool SetFocusNodeToSelection()
        {
            var str = SelectedCellStringValue();
            if (str != null)
            {
                // if it looks like a number, don't event try, just ignore 
                if (Regex.IsMatch(str, @"^[_\d,.]*$"))
                {
                    return false;
                }

                if (!SetFocus(str))
                {
                    return true;
                }

                return true;
            }
            else
            {
                StatusBar.LogError("No selected cells found.");
                return true;
            }
        }

        private void ByName_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            DoViewInCallers(sender, null);
        }
        internal void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var uiElement = sender as UIElement;
            Point point = e.GetPosition(uiElement);
            HitTestResult visualHitResult = VisualTreeHelper.HitTest(uiElement, point);
            var nameHit = visualHitResult != null ? Helpers.FindVisualNode(visualHitResult.VisualHit, "Name") : null;
            var asTextBlock = nameHit as TextBlock;

            // If it is the 'name' field, then set that to be the focus treeNode. 
            if (asTextBlock != null)
            {
                SetFocus(asTextBlock.Text);
            }
        }

        private void Notes_GotFocus(object sender, RoutedEventArgs e)
        {
            HelpMessage.Visibility = Visibility.Hidden;
        }
        public bool NotesPaneHidden
        {
            get { return m_NotesPaneHidden; }
            set
            {
                if (value == m_NotesPaneHidden)
                {
                    return;
                }

                if (value)
                {
                    App.ConfigData["NotesPaneHidden"] = "true";
                    m_NotesPaneHidden = true;
                    NodePaneRowDef.MaxHeight = 0;
                }
                else
                {
                    App.ConfigData["NotesPaneHidden"] = "false";
                    m_NotesPaneHidden = false;
                    NodePaneRowDef.MaxHeight = Double.PositiveInfinity;
                }
            }
        }

        private bool m_NotesPaneHidden;

        /// <summary>
        /// This is whether the sample being shown represent time and thus should be divided up or not.  
        /// </summary>
        public ScalingPolicyKind ScalingPolicy;

        private void DoToggleNotesPane(object sender, ExecutedRoutedEventArgs e)
        {
            NotesPaneHidden = !NotesPaneHidden;
        }
        private bool m_ViewsShouldBeSaved;
        private void Notes_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (m_IgnoreNotesChange)
            {
                return;
            }

            if (!m_ViewsShouldBeSaved)
            {
                m_ViewsShouldBeSaved = true;
                GuiApp.MainWindow.NumWindowsNeedingSaving++;
            }
        }

        private bool m_NotesTabActive;          // Are we looking at the Notes tab (rather than the Notes pane).  
        private bool m_IgnoreNotesChange;       // If set, we don't assume a change in the Notes text is done by the user 
        private void NotesTab_GotFocus(object sender, RoutedEventArgs e)
        {
            m_IgnoreNotesChange = true;
            NotesTabBody.Text = Notes.Text;
            m_IgnoreNotesChange = false;

            m_NotesTabActive = true;
            NodePaneRowDef.MaxHeight = 0;
        }

        private void NotesTab_LostFocus(object sender, RoutedEventArgs e)
        {
            m_IgnoreNotesChange = true;
            Notes.Text = NotesTabBody.Text;
            m_IgnoreNotesChange = false;

            if (!m_NotesPaneHidden)
            {
                NodePaneRowDef.MaxHeight = Double.PositiveInfinity;
            }

            m_NotesTabActive = false;
        }

        private bool m_RedrawFlameGraphWhenItBecomesVisible = false;

        private void FlameGraphTab_GotFocus(object sender, RoutedEventArgs e)
        {
            if (FlameGraphCanvas.IsEmpty || m_RedrawFlameGraphWhenItBecomesVisible)
            {
                RedrawFlameGraph();
            }
        }

        private void FlameGraphCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RedrawFlameGraphIfVisible();

        private void RedrawFlameGraphIfVisible()
        {
            if (FlameGraphTab.IsSelected)
            {
                RedrawFlameGraph();
            }
            else
            {
                m_RedrawFlameGraphWhenItBecomesVisible = true;
            }
        }

        private void RedrawFlameGraph()
        {
            FlameGraphCanvas.Draw(
                  CallTree.Root.HasChildren
                      ? FlameGraph.Calculate(CallTree, FlameGraphCanvas.ActualWidth, FlameGraphCanvas.ActualHeight)
                      : Enumerable.Empty<FlameGraph.FlameBox>());

            m_RedrawFlameGraphWhenItBecomesVisible = false;
        }

        private void FlameGraphCanvas_CurrentFlameBoxChanged(object sender, string toolTipText)
        {
            if (StatusBar.LoggedError)
            {
                return;
            }

            StatusBar.Status = toolTipText;
        }

        private void DoSaveFlameGraph(object sender, RoutedEventArgs e)
        {
            var saveDialog = new Microsoft.Win32.SaveFileDialog();
            var baseName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(DataSource.FilePath));

            for (int i = 1; ; i++)
            {
                saveDialog.FileName = baseName + ".flameGraph" + i.ToString() + ".png";
                if (!File.Exists(saveDialog.FileName))
                {
                    break;
                }
            }
            saveDialog.InitialDirectory = Path.GetDirectoryName(DataSource.FilePath);
            saveDialog.Title = "File to save flame graph";
            saveDialog.DefaultExt = ".png";
            saveDialog.Filter = "Image files (*.png)|*.png|All files (*.*)|*.*";
            saveDialog.AddExtension = true;
            saveDialog.OverwritePrompt = true;

            var result = saveDialog.ShowDialog();
            if (result == true)
            {
                FlameGraph.Export(FlameGraphCanvas, saveDialog.FileName);
            }
        }

        private TabItem SelectedTab
        {
            get
            {
                if (ByNameTab.IsSelected)
                {
                    return ByNameTab;
                }
                else if (CallerCalleeTab.IsSelected)
                {
                    return CallerCalleeTab;
                }
                else if (CallTreeTab.IsSelected)
                {
                    return CallTreeTab;
                }
                else if (CallersTab.IsSelected)
                {
                    return CallersTab;
                }
                else if (CalleesTab.IsSelected)
                {
                    return CalleesTab;
                }
                else if (FlameGraphTab.IsSelected)
                {
                    return FlameGraphTab;
                }
                else if (NotesTab.IsSelected)
                {
                    return NotesTab;
                }

                Debug.Assert(false, "No tab selected!");
                return null;
            }
        }

        public StackWindowGuiState GuiState
        {
            get
            {
                var ret = new StackWindowGuiState();

                ret.FilterGuiState = FilterGuiState;

                ret.Notes = m_NotesTabActive ? NotesTabBody.Text : Notes.Text;

                var logText = Regex.Replace(StatusBar.LogWindow.TextEditor.Text, @"[^ -~\s]", "");
                ret.Log = logText;

                var selectedTab = SelectedTab;
                if (selectedTab != null)
                {
                    ret.TabSelected = SelectedTab.Name;
                }

                ret.FocusName = FocusName;

                var columns = new List<string>(ByNameDataGrid.Grid.Columns.Count);
                foreach (var column in ByNameDataGrid.Grid.Columns)
                {
                    var name = ((TextBlock)column.Header).Name;
                    columns.Add(name);
                }
                ret.Columns = columns;
                ret.NotesPaneHidden = NotesPaneHidden;
                ret.ScalingPolicy = ScalingPolicy;

                return ret;
            }
            set
            {
                // We set the FilterGui state earlier so we don't set it here.   TODO can we avoid this and keep it simpler?
                // FilterGuiState = value.FilterGuiState;
                if (!string.IsNullOrWhiteSpace(value.Notes))
                {
                    m_IgnoreNotesChange = true;
                    Notes.Text = value.Notes;
                }

                if (!string.IsNullOrWhiteSpace(value.Log))
                {
                    StatusBar.Log("********** The following is the log file that was caputured when the stack view was saved *************");
                    StatusBar.Log(value.Log);
                    StatusBar.Log("********** End of the saved log *************");
                }

                if (value.TabSelected != null)
                {
                    switch (value.TabSelected)
                    {
                        case nameof(ByNameTab):
                            ByNameTab.IsSelected = true;
                            break;
                        case nameof(CallerCalleeTab):
                            CallerCalleeTab.IsSelected = true;
                            break;
                        case nameof(CallTreeTab):
                            CallTreeTab.IsSelected = true;
                            break;
                        case nameof(CalleesTab):
                            CalleesTab.IsSelected = true;
                            break;
                        case nameof(CallersTab):
                            CallersTab.IsSelected = true;
                            break;
                        case nameof(FlameGraphTab):
                            FlameGraphTab.IsSelected = true;
                            break;
                        case nameof(NotesTab):
                            NotesTab.IsSelected = true;
                            break;
                    }
                }

                if (m_callTree != null)
                {
                    if (value.FocusName != null)
                    {
                        SetFocus(value.FocusName);
                    }
                }

                if (value.Columns != null)
                {
                    foreach (var columnName in ByNameDataGrid.ColumnNames())
                    {
                        if (!value.Columns.Contains(columnName) && columnName != "NameColumn")
                        {
                            RemoveColumn(columnName);
                        }
                    }
                }

                NotesPaneHidden = value.NotesPaneHidden;
                ScalingPolicy = value.ScalingPolicy;

                // TODO FIX NOW we don't remember the highlighted entry in the callers callees, or tree view. 
            }
        }

        /// <summary>
        /// Intended to be called from ConfigureStackWindow
        /// </summary>
        public string ExtraTopStats { get; set; }

        public bool ComputeMaxInTopStats;

        #region commandDefintions
        // Global
        public static RoutedUICommand UsersGuideCommand = new RoutedUICommand("UsersGuide", "UsersGuide", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.F1) });
        public static RoutedUICommand SaveCommand = new RoutedUICommand("Save", "Save", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.S, ModifierKeys.Control) });
        public static RoutedUICommand SaveAsCommand = new RoutedUICommand("SaveAs", "SaveAs", typeof(StackWindow));
        public static RoutedUICommand SaveFlameGraphCommand = new RoutedUICommand("SaveFlameGraph", "SaveFlameGraph", typeof(StackWindow));
        public static RoutedUICommand CancelCommand = new RoutedUICommand("Cancel", "Cancel", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.Escape) });
        public static RoutedUICommand UpdateCommand = new RoutedUICommand("Update", "Update", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.F5) });
        public static RoutedUICommand NewWindowCommand = new RoutedUICommand("New Window", "NewWindow", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.N, ModifierKeys.Control) });
        public static RoutedUICommand DrillIntoCommand = new RoutedUICommand("Drill Into", "DrillInto", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.D, ModifierKeys.Control) });
        public static RoutedUICommand FlattenCommand = new RoutedUICommand("Flatten", "Flatten", typeof(StackWindow));
        public static RoutedUICommand FindCommand = new RoutedUICommand("Find", "Find", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.F, ModifierKeys.Control) });
        public static RoutedUICommand FindNextCommand = new RoutedUICommand("Find Next", "FindNext", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.F3) });

        // Navigation 
        public static RoutedUICommand ViewInCallerCalleeCommand = new RoutedUICommand("Goto Item in Caller-Callee", "ViewInCallerCallee", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.C, ModifierKeys.Alt) });
        public static RoutedUICommand ViewInCallersCommand = new RoutedUICommand("Goto Item in Callers", "ViewInCallers", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.F10) });
        public static RoutedUICommand ViewInCalleesCommand = new RoutedUICommand("Goto Item in Callees", "ViewInCallees", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.F10, ModifierKeys.Shift) });
        public static RoutedUICommand FindInCallTreeCommand = new RoutedUICommand("Goto Item in CallTree", "FindInCallTree", typeof(StackWindow),
             new InputGestureCollection() { new KeyGesture(Key.T, ModifierKeys.Alt) });
        public static RoutedUICommand FindInByNameCommand = new RoutedUICommand("Goto Item in ByName", "FindInByName", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.N, ModifierKeys.Alt) });
        public static RoutedUICommand OpenEventsCommand = new RoutedUICommand("Open Events", "OpenEvents", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.V, ModifierKeys.Alt) });

        // Grouping / Folding 
        public static RoutedUICommand GroupModuleCommand = new RoutedUICommand("Group Module", "GroupModule", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.G, ModifierKeys.Alt) });
        public static RoutedUICommand EntryGroupModuleCommand = new RoutedUICommand("Entry Group Module", "EntryGroupModule", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.H, ModifierKeys.Alt) });
        public static RoutedUICommand UngroupCommand = new RoutedUICommand("Ungroup", "Ungroup", typeof(StackWindow),
             new InputGestureCollection() { new KeyGesture(Key.U, ModifierKeys.Alt) });
        public static RoutedUICommand UngroupModuleCommand = new RoutedUICommand("Ungroup Module", "UngroupModule", typeof(StackWindow),
             new InputGestureCollection() { new KeyGesture(Key.W, ModifierKeys.Alt) });     // TODO need multi-key shortcuts.  
        public static RoutedUICommand FoldModuleCommand = new RoutedUICommand("Fold Module", "FoldModule", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.M, ModifierKeys.Alt) });
        public static RoutedUICommand FoldItemCommand = new RoutedUICommand("Fold Item", "FoldItem", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.F, ModifierKeys.Alt | ModifierKeys.Control) });
        public static RoutedUICommand RemoveAllFoldingCommand = new RoutedUICommand("Remove All Folding", "RemoveAllFolding", typeof(StackWindow),
             new InputGestureCollection() { new KeyGesture(Key.X, ModifierKeys.Alt) });

        // Priority
        public static RoutedUICommand RaiseItemPriorityCommand = new RoutedUICommand("Raise Item Priority", "RaiseItemPriority", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.P, ModifierKeys.Alt) });
        public static RoutedUICommand LowerItemPriorityCommand = new RoutedUICommand("Lower Item Priority", "LowerItemPriority", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.P, ModifierKeys.Alt | ModifierKeys.Shift) });
        public static RoutedUICommand RaiseModulePriorityCommand = new RoutedUICommand("Raise Module Priority", "RaiseModulePriority", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.Q, ModifierKeys.Alt) });
        public static RoutedUICommand LowerModulePriorityCommand = new RoutedUICommand("Lower Module Priority", "LowerModulePriority", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.Q, ModifierKeys.Alt | ModifierKeys.Shift) });

        // Symbols 
        public static RoutedUICommand LookupSymbolsCommand = new RoutedUICommand("Lookup Symbols", "LookupSymbols", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.S, ModifierKeys.Alt) });
        public static RoutedUICommand LookupWarmSymbolsCommand = new RoutedUICommand("Lookup Warm Symbols", "LookupWarmSymbols", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.S, ModifierKeys.Alt | ModifierKeys.Control) });
        public static RoutedUICommand GotoSourceCommand = new RoutedUICommand("Goto Source (Def)", "GotoSource", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.D, ModifierKeys.Alt) });

        // Filtering
        public static RoutedUICommand SetTimeRangeCommand = new RoutedUICommand("Set Time Range", "SetTimeRange", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.R, ModifierKeys.Alt) });
        public static RoutedUICommand CopyTimeRangeCommand = new RoutedUICommand("Copy Time Range", "CopyTimeRange", typeof(StackWindow));

        public static RoutedUICommand SetScenarioListCommand = new RoutedUICommand("Set Scenario List", "SetScenarioList", typeof(StackWindow));
        public static RoutedUICommand CopyScenarioListCommand = new RoutedUICommand("Copy Scenario List", "CopyScenarioList", typeof(StackWindow));
        public static RoutedUICommand CopyScenarioListNamesCommand = new RoutedUICommand("Copy Scenario List Names", "CopyScenarioListNames", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.C, ModifierKeys.Control | ModifierKeys.Shift) });
        public static RoutedUICommand SortScenariosByDefaultCommand = new RoutedUICommand("By Default", "SortScenariosByDefault", typeof(StackWindow));
        public static RoutedUICommand SortScenariosByRootNodeCommand = new RoutedUICommand("By Root Node", "SortScenariosByRootNode", typeof(StackWindow));
        public static RoutedUICommand SortScenariosByThisNodeCommand = new RoutedUICommand("By This Node", "SortScenariosByThisNode", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.S, ModifierKeys.Control | ModifierKeys.Shift) });

        // misc 
        public static RoutedUICommand IncludeItemCommand = new RoutedUICommand("Include Item", "IncludeItem", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.I, ModifierKeys.Alt) });
        public static RoutedUICommand ExcludeItemCommand = new RoutedUICommand("Exclude Item", "ExcludeItem", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.E, ModifierKeys.Alt) });
        public static RoutedUICommand ToggleNoPadOnCopyCommand = new RoutedUICommand("Toggle No Pad On Copy", "ToggleNoPadOnCopy", typeof(StackWindow));

        // memory
        public static RoutedUICommand ViewObjectsCommand = new RoutedUICommand("View Objects", "ViewObjects", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.O, ModifierKeys.Alt) });
        public static RoutedUICommand DumpObjectCommand = new RoutedUICommand("Dump Object", "DumpObject", typeof(StackWindow));


        public static RoutedUICommand ToggleNotesPaneCommand = new RoutedUICommand("Toggle Notes Pane", "ToggleNotesPane", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.F2) });

        // FilterParams
        public static RoutedUICommand CopyFilterParamsCommand = new RoutedUICommand("Copy Filter Params", "CopyFilterParams", typeof(StackWindow));
        public static RoutedUICommand MergeFilterParamsCommand = new RoutedUICommand("Merge Filter Params", "MergeFilterParams", typeof(StackWindow));

        // TreeView specific
        public static RoutedUICommand ExpandAllCommand = new RoutedUICommand("Expand All", "ExpandAll", typeof(StackWindow));
        public static RoutedUICommand ExpandCommand = new RoutedUICommand("Expand", "Expand", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.Space) });
        public static RoutedUICommand CollapseCommand = new RoutedUICommand("Collapse", "Collapse", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.Space, ModifierKeys.Shift) });
        public static RoutedUICommand SetBrownBackgroundColorCommand = new RoutedUICommand("Set Brown Background Color", "SetBrownBackgroundColor", typeof(StackWindow));
        public static RoutedUICommand SetBlueBackgroundColorCommand = new RoutedUICommand("Set Blue Background Color", "SetBlueBackgroundColor", typeof(StackWindow));
        public static RoutedUICommand SetRedBackgroundColorCommand = new RoutedUICommand("Set Red Background Color", "SetRedBackgroundColor", typeof(StackWindow));
        public static RoutedUICommand FoldPercentCommand = new RoutedUICommand("Fold %", "FoldPercent", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.F6) });
        public static RoutedUICommand IncreaseFoldPercentCommand = new RoutedUICommand("Increase Fold %", "Increase FoldPercent", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.F7) });
        public static RoutedUICommand DecreaseFoldPercentCommand = new RoutedUICommand("Decrease Fold %", "DecreaseFoldPercent", typeof(StackWindow),
            new InputGestureCollection() { new KeyGesture(Key.F7, ModifierKeys.Shift) });
        #endregion
        #region private
        private void FinishInit()
        {
            DataContext = this;

            // Customize the control
            ByNameDataGrid.Grid.CanUserSortColumns = true;
            var columns = ByNameDataGrid.Grid.Columns;


            // Put the exlusive columns first if they are not already there.  
            var col = ByNameDataGrid.GetColumnIndex("ExcPercentColumn");
            if (0 <= col && col != 1)
            {
                ByNameDataGrid.Grid.Columns.Move(col, 1);
            }

            col = ByNameDataGrid.GetColumnIndex("ExcColumn");
            if (0 <= col && col != 2)
            {
                ByNameDataGrid.Grid.Columns.Move(col, 2);
            }

            col = ByNameDataGrid.GetColumnIndex("ExcCountColumn");
            if (0 <= col && col != 3)
            {
                ByNameDataGrid.Grid.Columns.Move(col, 3);
            }

            // Initialize the CallTree, Callers, and Callees tabs
            // TODO:  Gross that the caller has to pass this in.  
            var template = (DataTemplate)Resources["TreeControlCell"];
            m_callTreeView = new CallTreeView(CallTreeDataGrid, template);
            m_callersView = new CallTreeView(CallersDataGrid, template);
            m_calleesView = new CallTreeView(CalleesDataGrid, template);

            List<PerfDataGrid> perfDataGrids = new List<PerfDataGrid>(4)
            {
                ByNameDataGrid,
                CallTreeDataGrid,
                CallersDataGrid,
                CalleesDataGrid
            };

            // Populate ViewMenu items for showing/hiding columns
            PopulateViewMenuWithPerfDataGridItems(perfDataGrids);

            // Make up a trivial call tree (so that the rest of the code works).  
            m_callTree = new CallTree(ScalingPolicy);

            // Configure the Preset menu (add standard commands and known presets)
            ConfigurePresetMenu();

            StackWindows.Add(this);

            // TODO really should simply update Diff Menu lazily
            IsVisibleChanged += delegate (object sender, DependencyPropertyChangedEventArgs e)
            {
                UpdateDiffMenus(StackWindows);
            };
            Closing += delegate (object sender, CancelEventArgs e)
            {
                if (StatusBar.IsWorking)
                {
                    StatusBar.LogError("Cancel work before closing window.");
                    e.Cancel = true;
                    return;
                }

                if (m_ViewsShouldBeSaved)
                {
                    var result = MessageBox.Show("You have created Notes that have not been saved\r\nDo you wish to save?", "Unsaved Notes", MessageBoxButton.YesNoCancel);
                    if (result == MessageBoxResult.Cancel)
                    {
                        e.Cancel = true;
                        return;
                    }
                    if (result == MessageBoxResult.Yes)
                    {
                        DoSave(null, null);
                    }

                    if (m_ViewsShouldBeSaved)
                    {
                        m_ViewsShouldBeSaved = false;
                        --GuiApp.MainWindow.NumWindowsNeedingSaving;
                    }
                }

                if (StackWindows[0] == this && WindowState != System.Windows.WindowState.Maximized)
                {
                    App.ConfigData["StackWindowTop"] = Top.ToString("f0", CultureInfo.InvariantCulture);
                    App.ConfigData["StackWindowLeft"] = Left.ToString("f0", CultureInfo.InvariantCulture);
                    App.ConfigData["StackWindowWidth"] = RenderSize.Width.ToString("f0", CultureInfo.InvariantCulture);
                    App.ConfigData["StackWindowHeight"] = RenderSize.Height.ToString("f0", CultureInfo.InvariantCulture);
                }

                StackWindows.Remove(this);
                UpdateDiffMenus(StackWindows);
                if (DataSource != null)
                {
                    DataSource.ViewClosing(this);
                }
                // Insure our parent is visible
                DoOpenParent(null, null);

                // Throw the old call tree nodes away (GUI keeps reference to them, so disconnecting saves memory)
                if (m_callTree != null)
                {
                    m_callTree.FreeMemory();
                }

                if (m_callTreeView != null)
                {
                    m_calleesView.Dispose();
                }
            };
            TopStats.PreviewMouseDoubleClick += delegate (object sender, MouseButtonEventArgs e)
            {
                e.Handled = StatusBar.ExpandSelectionByANumber(TopStats);
                return;
            };

#if false // TODO FIX NOW remove 
            Loaded += delegate(object sender, RoutedEventArgs e)
            {
                FindTextBox.GetTextBox().Focus(); // Put the focus on the find box to begin with
            };
#endif

            // The act of even setting the default text looks like a user action, ignore it.  
            if (m_ViewsShouldBeSaved)
            {
                m_ViewsShouldBeSaved = false;
                --GuiApp.MainWindow.NumWindowsNeedingSaving;
            }

            NotesPaneHidden = (App.ConfigData["NotesPaneHidden"] == "true");

            if (StackWindows.Count == 1)
            {
                // Make sure the location is sane so it can be displayed. 
                var top = App.ConfigData.GetDouble("StackWindowTop", Top);
                Top = Math.Min(Math.Max(top, 0), System.Windows.SystemParameters.PrimaryScreenHeight - 200);

                var left = App.ConfigData.GetDouble("StackWindowLeft", Left);
                Left = Math.Min(Math.Max(left, 0), System.Windows.SystemParameters.PrimaryScreenWidth - 200);

                Height = App.ConfigData.GetDouble("StackWindowHeight", Height);
                Width = App.ConfigData.GetDouble("StackWindowWidth", Width);
            }
        }

        /// <summary>
        /// Populate the View MenuItem
        /// 
        /// Creates checkable boxes for each Column in the PerfDataGrid, then adds a seperator and Save View Settings button.
        /// </summary>
        /// <param name="perfDataGrids"></param>
        private void PopulateViewMenuWithPerfDataGridItems(List<PerfDataGrid> perfDataGrids)
        {
            List<Tuple<string, MenuItem>> perfDataGridMenuItems = new List<Tuple<string, MenuItem>>();

            foreach (PerfDataGrid perfDataGrid in perfDataGrids)
            {
                foreach (DataGridColumn col in perfDataGrid.Grid.Columns)
                {
                    MenuItem menuItem = null;

                    // Find the associated PerfDataGridMenuItem by name
                    IEnumerable<Tuple<string, MenuItem>> temp = perfDataGridMenuItems.Where(x => x.Item1 == ((TextBlock)col.Header).Text);

                    // If it has not been created yet. Create an instance.
                    if (temp.Count() == 0)
                    {
                        // Create MenuItem based off of column header name, make it checkable.
                        menuItem = new MenuItem()
                        {
                            IsCheckable = true
                        };

                        string header = ((TextBlock)col.Header).Text;
                        menuItem.Header = header;

                        // Checked value and visibliity of column is based off of ConfigData.
                        // If there is no ConfigData property for it, it is defaulted to display. 
                        string configValue = App.ConfigData[XmlConvert.EncodeName(header + "ColumnView")];
                        if (configValue == null || configValue == "1")
                        {
                            menuItem.IsChecked = true;
                            col.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            menuItem.IsChecked = false;
                            col.Visibility = Visibility.Collapsed;
                        }

                        perfDataGridMenuItems.Add(new Tuple<string, MenuItem>(header, menuItem));
                        ViewMenu.Items.Add(menuItem);
                    }
                    // If it exists, retrieve the MenuItem.
                    else
                    {
                        menuItem = temp.First().Item2;
                    }

                    // Attach Click handler to collapse if unchecked, and make it visible when checked.
                    menuItem.Click += delegate (object sender, RoutedEventArgs e)
                    {
                        MenuItem source = sender as MenuItem;
                        if (source.IsChecked)
                        {
                            col.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            col.Visibility = Visibility.Collapsed;
                        }
                    };
                }
            }

            // Add Seperator
            ViewMenu.Items.Add(new Separator());

            // Add Save View Settings Menu Item
            string saveViewSettingsStr = "Save View Settings";
            MenuItem saveSettings = new MenuItem()
            {
                Header = saveViewSettingsStr,
            };

            saveSettings.Click += delegate (object sender, RoutedEventArgs e)
            {
                for (int i = 0; i < ViewMenu.Items.Count; i++)
                {
                    // Ignore if it is a Sperator
                    if (ViewMenu.Items[i] is MenuItem)
                    {
                        MenuItem mItem = ViewMenu.Items[i] as MenuItem;

                        string header = mItem.Header.ToString();

                        // Skip Save View Settings Menu Item
                        if (header == saveViewSettingsStr)
                        {
                            continue;
                        }

                        // Format of key is menuItemName + ColumnView
                        // XmlConvert.EncodeName is used to handle symbols like %
                        // E.g. CallTreeViewNameColumnView
                        string name = XmlConvert.EncodeName(header + "ColumnView");
                        App.ConfigData[name] = mItem.IsChecked ? "1" : "0";
                    }
                }
            };

            ViewMenu.Items.Add(saveSettings);
        }

        private bool DoForSelectedModules(Action<string> moduleAction)
        {
            // TODO see if we can use this in as many places as possible. 
            var badStrs = "";
            foreach (string cellStr in SelectedCellsStringValue())
            {
                Match m = Regex.Match(cellStr, @"([ \w.-]+)!");
                if (m.Success)
                {
                    moduleAction(m.Groups[1].Value);
                }
                else
                {
                    m = Regex.Match(cellStr, @"^module ([ \w.-]+)");
                    if (m.Success)
                    {
                        moduleAction(m.Groups[1].Value);
                    }
                    else
                    {
                        if (badStrs.Length > 0)
                        {
                            badStrs += " ";
                        }

                        badStrs += cellStr;
                    }
                }
            }
            if (badStrs.Length > 0)
            {
                StatusBar.LogError("Could not find a module pattern in text " + badStrs + ".");
                return false;
            }
            return true;
        }

        private static string FindExePath(StackSource stackSource, string procName)
        {
            if (procName == null)
            {
                return null;
            }

            // Get the unfiltered source 
            while (stackSource.BaseStackSource != stackSource)
            {
                stackSource = stackSource.BaseStackSource;
            }

            // Look in the frames for a path that ends in the process name 
            for (int frameidx = (int)StackSourceFrameIndex.Start; frameidx < stackSource.CallFrameIndexLimit; frameidx++)
            {
                var frameName = stackSource.GetFrameName((StackSourceFrameIndex)frameidx, true);
                var match = Regex.Match(frameName, @"^([^!]*\\(.*?))!");
                if (match.Success)
                {
                    if (string.Compare(match.Groups[2].Value, procName, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        return match.Groups[1].Value;
                    }
                }
            }
            return null;
        }

        private bool ValidateStartAndEnd(StackSource newSource)
        {
            double start = 0, end = double.PositiveInfinity;
            // Set the end text box.
            if (string.IsNullOrWhiteSpace(EndTextBox.Text))
            {
                double limit = newSource.SampleTimeRelativeMSecLimit;
                if (limit != 0)
                {
                    EndTextBox.Text = (newSource.SampleTimeRelativeMSecLimit).ToString("n3");
                }
            }
            else if (!double.TryParse(EndTextBox.Text, out end))
            {
                StatusBar.LogError("Invalid number " + EndTextBox.Text);
                EndTextBox.Text = "Infinity";
                return false;
            }
            else
            {
                EndTextBox.Text = end.ToString("n3");
            }

            // See if we are pasting a range into the start text box
            if (double.TryParse(StartTextBox.Text, out start))
            {
                StartTextBox.Text = start.ToString("n3");
            }
            else if (string.IsNullOrWhiteSpace(StartTextBox.Text))
            {
                StartTextBox.Text = "0";
            }
            else
            {
                if (RangeUtilities.TryParse(StartTextBox.Text, out start, out end))
                {
                    StartTextBox.Text = start.ToString("n3");
                    EndTextBox.Text = end.ToString("n3");
                }
                else
                {
                    StatusBar.LogError("Invalid number " + StartTextBox.Text);
                    StartTextBox.Text = "0";
                    return false;
                }
            }

            if (end < start)
            {
                var str = StartTextBox.Text;
                StartTextBox.Text = EndTextBox.Text;
                EndTextBox.Text = str;
            }
            return true;
        }

        private bool FindNext(string pat)
        {
            StatusBar.Status = "";
            bool ret = true;

            if (ByNameTab.IsSelected)
            {
                ret = ByNameDataGrid.Find(pat);
            }
            else if (CallTreeTab.IsSelected)
            {
                ret = CallTreeView.Find(pat);
            }
            else if (CallerCalleeTab.IsSelected)
            {
                ret = CallerCalleeView.Find(pat);
            }
            else if (CallersTab.IsSelected)
            {
                ret = m_callersView.Find(pat);
            }
            else if (CalleesTab.IsSelected)
            {
                ret = m_calleesView.Find(pat);
            }
            //             else if (NotesTab.IsSelected)        // TODO FIX NOW implement find.  
            else
            {
                StatusBar.LogError("Find not support on this tab.");
                return false;
            }

            if (!ret)
            {
                StatusBar.LogError("Could not find " + pat + ".");
            }

            return ret;
        }

        internal static string GetCellStringValue(DataGridCellInfo cell)
        {
            string ret = PerfDataGrid.GetCellStringValue(cell);
            ret = Regex.Replace(ret, @"\s*\{.*?\}\s*$", "");        // Remove {} stuff at the end.  
            ret = Regex.Replace(ret, @"^[ |+]*", "");               // Remove spaces or | (for tree view) at the start).  
            return ret;
        }
        private IList<DataGridCellInfo> SelectedCells()
        {
            var dataGrid = GetDataGrid();
            if (dataGrid == null)
            {
                return null;
            }

            return dataGrid.SelectedCells;
        }
        internal DataGrid GetDataGrid()
        {
            if (ByNameTab.IsSelected)
            {
                return ByNameDataGrid.Grid;
            }
            else if (CallerCalleeTab.IsSelected)
            {
                // Find the focus
                var dependencyObject = FocusManager.GetFocusedElement(this) as DependencyObject;
                if (dependencyObject != null)
                {
                    if (CallerCalleeView.CallersGrid.Grid.IsAncestorOf(dependencyObject))
                    {
                        return CallerCalleeView.CallersGrid.Grid;
                    }
                    else if (CallerCalleeView.CalleesGrid.Grid.IsAncestorOf(dependencyObject))
                    {
                        return CallerCalleeView.CalleesGrid.Grid;
                    }

                    if (CallerCalleeView.FocusGrid.Grid.IsAncestorOf(dependencyObject))
                    {
                        return CallerCalleeView.FocusGrid.Grid;
                    }
                }

                return null;
            }
            else if (CallTreeTab.IsSelected)
            {
                return CallTreeDataGrid.Grid;
            }
            else if (CallersTab.IsSelected)
            {
                return CallersDataGrid.Grid;
            }
            else if (CalleesTab.IsSelected)
            {
                return CalleesDataGrid.Grid;
            }
            else
            {
                return null;
            }
        }
        private IEnumerable<string> SelectedCellsStringValue()
        {
            var dataGrid = GetDataGrid();
            if (dataGrid == null)
            {
                yield break;
            }

            var cells = dataGrid.SelectedCells;
            if (cells == null)
            {
                yield break;
            }

            for (int i = 0; i < cells.Count; i++)
            {
                DataGridCellInfo cell = cells[i];
                var str = GetCellStringValue(cell);
                if (str.Length != 0)
                {
                    yield return str;
                }

                if (StartsFullRow(cells, i, dataGrid.Columns.Count))
                {
                    i += dataGrid.Columns.Count - 1;
                }
            }
        }
        /// <summary>
        /// Returns true if the cells starting at 'startIndex' begin a complete full row.  
        /// </summary>
        private static bool StartsFullRow(IList<DataGridCellInfo> cells, int startIdx, int numColumns)
        {
            if (startIdx + numColumns > cells.Count)
            {
                return false;
            }

            for (int i = 0; i < numColumns; i++)
            {
                if (cells[i + startIdx].Column.DisplayIndex != i)
                {
                    return false;
                }
            }

            return true;
        }
        /// <summary>
        /// Returns the string value for a single selected cell.  Will return null on error 
        /// </summary>
        private string SelectedCellStringValue()
        {
            var dataGrid = GetDataGrid();
            if (dataGrid == null)
            {
                return null;
            }

            var cells = dataGrid.SelectedCells;
            if (cells == null)
            {
                return null;
            }

            if (cells.Count == 0)
            {
                return null;
            }

            if (cells.Count > 1)
            {
                int numCols = dataGrid.Columns.Count;
                // fail unless we have selected a whole row
                // TODO should we bother?
                if (cells.Count != numCols || !StartsFullRow(cells, 0, numCols))
                {
                    return null;
                }
            }
            return GetCellStringValue(cells[0]);
        }
        // We keep a list of stack windows for use with the 'Diff' feature.  
        public static List<StackWindow> StackWindows = new List<StackWindow>();

        /// <summary>
        /// Insure that there is an entry for each element in 'stackWindows' in the diff menu. 
        /// </summary>
        private static void UpdateDiffMenus(List<StackWindow> stackWindows)
        {
            foreach (var stackWindow in stackWindows)
            {
                UpdateDiffMenu("Diff", stackWindow.DiffMenu, stackWindow.DoOpenDiffItem, stackWindow, stackWindows);
                UpdateDiffMenu("Regression", stackWindow.RegressionMenu, stackWindow.DoOpenRegressionItem, stackWindow, stackWindows);
            }
        }

        private static void UpdateDiffMenu(string diffName, MenuItem diffMenuItem, Action<object, RoutedEventArgs> onSelectAction, StackWindow stackWindow, List<StackWindow> stackWindows)
        {
            diffMenuItem.Items.Clear();
            foreach (var menuEntry in stackWindows)
            {
                if (menuEntry == stackWindow)
                {
                    continue;
                }

                var childMenuItem = new MenuItem();
                childMenuItem.Header = "With Baseline: " + menuEntry.Title;
                childMenuItem.Tag = menuEntry;
                childMenuItem.Click += new RoutedEventHandler(onSelectAction);
                diffMenuItem.Items.Add(childMenuItem);
            }

            var helpMenuItem = new MenuItem();
            helpMenuItem.Header = "Help for " + diffName;
            helpMenuItem.Click += delegate (object sender, RoutedEventArgs e) { MainWindow.DisplayUsersGuide(diffName); };
            diffMenuItem.Items.Add(helpMenuItem);
        }

        private void ConfigurePresetMenu()
        {
            var presets = App.ConfigData["Presets"];
            m_presets = Preset.ParseCollection(presets);

            foreach (var preset in m_presets)
            {
                var presetMenuItem = new MenuItem();
                presetMenuItem.Header = preset.Name;
                presetMenuItem.Tag = preset.Name;
                presetMenuItem.Click += DoSelectPreset;
                PresetMenu.Items.Add(presetMenuItem);
            }

            PresetMenu.Items.Add(new Separator());

            var setDefaultPresetMenuItem = new MenuItem();
            setDefaultPresetMenuItem.Header = "S_et As Startup Preset";
            setDefaultPresetMenuItem.Click += DoSetStartupPreset;
            setDefaultPresetMenuItem.ToolTip =
                "Sets the default values of Group Patterns and Fold Patterns and % to the current values.";
            PresetMenu.Items.Add(setDefaultPresetMenuItem);

            var newPresetMenuItem = new MenuItem();
            newPresetMenuItem.Header = "_Save As Preset";
            newPresetMenuItem.Click += DoSaveAsPreset;
            PresetMenu.Items.Add(newPresetMenuItem);

            var managePresetsMenuItem = new MenuItem();
            managePresetsMenuItem.Header = "_Manage Presets";
            managePresetsMenuItem.Click += DoManagePresets;
            PresetMenu.Items.Add(managePresetsMenuItem);

            var helpMenuItem = new MenuItem();
            helpMenuItem.Header = "_Help for Preset";
            helpMenuItem.Click += delegate { MainWindow.DisplayUsersGuide("Preset"); };
            PresetMenu.Items.Add(helpMenuItem);
        }

        private void DoUpdatePresetMenu()
        {
            // Clean existing preset items
            while (!(PresetMenu.Items[0] is Separator))
            {
                PresetMenu.Items.RemoveAt(0);
            }
            // Sort in reverse order since menu items are created from last to first.
            m_presets.Sort((x, y) => Comparer<string>.Default.Compare(y.Name, x.Name));
            foreach (var preset in m_presets)
            {
                var presetMenuItem = new MenuItem();
                presetMenuItem.Header = preset.Name;
                presetMenuItem.Tag = preset.Name;
                presetMenuItem.Click += DoSelectPreset;
                PresetMenu.Items.Insert(0, presetMenuItem);
            }
        }

        private void DoSaveAsPreset(object sender, RoutedEventArgs e)
        {
            string groupPat = GroupRegExTextBox.Text.Trim();
            string nameCandidate = "Preset " + (m_presets.Count + 1).ToString();
            // Try to extract pattern name as a [Name] prefix
            if (groupPat.Length > 0 && groupPat[0] == '[')
            {
                int closingBracketIndex = groupPat.IndexOf(']');
                if (closingBracketIndex > 0)
                {
                    nameCandidate = groupPat.Substring(1, closingBracketIndex - 1);
                    groupPat = groupPat.Substring(closingBracketIndex + 1).Trim();
                }
            }

            var newPresetDialog = new NewPresetDialog(this, nameCandidate, m_presets.Select(x => x.Name).ToList());
            newPresetDialog.Owner = this;
            if (!(newPresetDialog.ShowDialog() ?? false))
            {
                return;
            }

            Preset preset = m_presets.FirstOrDefault(x => x.Name == newPresetDialog.PresetName) ?? new Preset();
            preset.Name = newPresetDialog.PresetName;
            preset.GroupPat = groupPat;
            preset.FoldPercentage = FoldPercentTextBox.Text;
            preset.FoldPat = FoldRegExTextBox.Text;

            if (m_presets.FindIndex(x => x.Name == newPresetDialog.PresetName) == -1)
            {
                m_presets.Insert(0, preset);
                m_presets.Sort((x, y) => Comparer<string>.Default.Compare(x.Name, y.Name));
            }
            App.ConfigData["Presets"] = Preset.Serialize(m_presets);

            DoUpdatePresetMenu();
        }

        private void DoManagePresets(object sender, RoutedEventArgs e)
        {
            var managePresetsDialog = new ManagePresetsDialog(this, m_presets, Path.GetDirectoryName(DataSource.FilePath), StatusBar);
            managePresetsDialog.Owner = this;
            managePresetsDialog.ShowDialog();
            m_presets = managePresetsDialog.Presets;
            App.ConfigData["Presets"] = Preset.Serialize(m_presets);
            DoUpdatePresetMenu();
        }

        private void DoSelectPreset(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            string presetName = menuItem.Tag as string;

            var preset = m_presets.Find(x => x.Name == presetName);
            GroupRegExTextBox.AddToHistory($"[{preset.Name}] {preset.GroupPat}");
            FoldPercentTextBox.AddToHistory(preset.FoldPercentage);
            FoldRegExTextBox.AddToHistory(preset.FoldPat);
            Update();
        }

        private void CopyTo(ItemCollection toCollection, IEnumerable fromCollection)
        {
            toCollection.Clear();
            foreach (var item in fromCollection)
            {
                toCollection.Add(item);
            }
        }
        private double GetDouble(string value, double defaultValue)
        {
            if (value.Length == 0)
            {
                return defaultValue;
            }

            return double.Parse(value);
        }
        private static string AddSet(string target, string addend)
        {
            if (target.Length == 0)
            {
                return addend;
            }

            if (addend.Length == 0)
            {
                return target;
            }

            // Remove the comment (we put it back at the end)
            var match = Regex.Match(target, @"(^\s*(\[.*?\])?\s*)(.*?)\s*$");
            var comment = match.Groups[1].Value;
            target = match.Groups[3].Value;
            int pos = 0;
            for (; ; )
            {
                int index = target.IndexOf(addend, pos);
                if (index < 0)
                {
                    break;
                }

                // Already exists, nothing to do.  
                int next = index + addend.Length;
                if ((index == 0 || target[index] == ';') &&
                    (next == target.Length || target[next] == ';'))
                {
                    return target;
                }

                pos = next + 1;
                if (pos >= target.Length)
                {
                    break;
                }
            }
            return comment + target + ";" + addend;
        }
        internal int SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            var dataGrid = sender as DataGrid;

            var cells = dataGrid.SelectedCells;
            StatusBar.Status = "";
            if (cells.Count > 1)
            {
                double sum = 0;
                int count = 0;
                double max = double.NegativeInfinity;
                double min = double.PositiveInfinity;
                double first = 0;
                double second = 0;
                bool firstCell = true;
                foreach (var cell in cells)
                {
                    var content = PerfDataGrid.GetCellStringValue(cell);
                    if (content != null)
                    {
                        double num;
                        if (double.TryParse(content, out num))
                        {
                            if (count == 0)
                            {
                                first = num;
                            }

                            if (count == 1)
                            {
                                second = num;
                            }

                            count++;
                            max = Math.Max(max, num);
                            min = Math.Min(min, num);
                            sum += num;
                        }
                    }
                    if (firstCell)
                    {
                        if (count == 0)     // Give up if the first cell is not a double.  
                        {
                            break;
                        }

                        firstCell = false;
                    }
                }
                if (count != 0)
                {
                    var mean = sum / count;
                    var text = string.Format("Sum={0:n3}  Mean={1:n3}  Min={2:n3}  Max={3:n3}", sum, mean, min, max);
                    if (count == 2)
                    {
                        text += string.Format("   X-Y={0:n3}", max - min);
                        double ratio = Math.Abs(first / second);
                        if (.0000001 <= ratio && ratio <= 10000000)
                        {
                            text += string.Format("   X/Y={0:n3}   Y/X={1:n3}", ratio, 1 / ratio);
                        }
                    }
                    else
                    {
                        text += string.Format("   Count={0}", count);
                    }

                    StatusBar.Status = text;
                }
            }
            else if (cells.Count == 1 && !StatusBar.LoggedError)
            {
                // We have only one cell copy its contents to the status box
                string cellStr = StackWindow.GetCellStringValue(cells[0]);
                string cellContentsToPrint = cellStr;
                if (cellStr.Length != 0)
                {
                    double asNum;
                    if (double.TryParse(cellStr, out asNum))
                    {
                        var asLong = (long)asNum;
                        if (Math.Abs(asLong - asNum) < .005)
                        {
                            cellContentsToPrint += " (0x" + asLong.ToString("x") + ")";
                        }
                    }
                    StatusBar.Status = "Cell Contents: " + cellContentsToPrint;
                }

                var clipBoardStr = Clipboard.GetText().Trim();
                if (clipBoardStr.Length > 0)
                {
                    double clipBoardVal;
                    double cellVal;
                    // The < 32 prevents a 'when' cell from being interpreted as a number
                    if (double.TryParse(clipBoardStr, out clipBoardVal) &&
                        double.TryParse(cellStr, out cellVal) && cellStr.Length < 32)
                    {
                        var reply = string.Format("Cell Contents: {0} ClipBoard: {1:n3}   Sum={2:n3}   Diff={3:n3}",
                            cellContentsToPrint, clipBoardVal, cellVal + clipBoardVal, Math.Abs(cellVal - clipBoardVal));

                        double product = cellVal * clipBoardVal;
                        if (Math.Abs(product) <= 1000)
                        {
                            reply += string.Format("   X*Y={0:n3}", product);
                        }

                        double ratio = cellVal / clipBoardVal;
                        if (.001 <= Math.Abs(ratio) && Math.Abs(ratio) <= 1000000)
                        {
                            reply += string.Format("   X/Y={0:n3}   Y/X={1:n3}", ratio, 1 / ratio);
                        }

                        StatusBar.Status = reply;
                    }
                }
            }

            return cells.Count;
        }
        internal void RestoreWindow(StackWindowGuiState guiState, string fileName)
        {
            if (fileName != null)
            {
                m_fileName = fileName;
            }

            if (guiState != null)
            {
                FilterGuiState = guiState.FilterGuiState;
            }

            if (m_ViewsShouldBeSaved)
            {
                m_ViewsShouldBeSaved = false;
                --GuiApp.MainWindow.NumWindowsNeedingSaving;
            }
        }

        private StackSource m_stackSource;
        internal CallTree m_callTree;

        // Keep track of the parameters we have already seeen. 
        private List<FilterParams> m_history;
        private int m_historyPos;
        private bool m_settingFromHistory;      // true if the filter parameters are being udpated from the history list
        private bool m_fixedUpJustMyCode;

        // State for the ByName OpenStacks
        internal List<CallTreeNodeBase> m_byNameView;

        /* State for Caller-Caller view is in User Control CallerCalleeView */

        // State for the Call Tree OpenStacks 
        internal CallTreeView m_callTreeView;
        internal CallTreeView m_calleesView;
        internal CallTreeView m_callersView;

        // What fileName to save as
        private string m_fileName;

        // List of presets loaded from configuration (and then maybe adjusted later)
        private List<Preset> m_presets;

        #endregion
    }
}
