using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;

using Microsoft.Diagnostics.Tracing.Stacks;

namespace PerfView
{
    enum IssueType
    {
        Profiling,
        Cpu,
        Memory,
        Allocation,
        Pinning,
        Fragmentation,
        Handle,
        HeapBalance
    }

    class Issue
    {
        public int       Id          { get; set; }
        public IssueType Type        { get; set; }
        public string    Description { get; set; }
        public string    Suggestion  { get; set; }

        public string Action { get; set; }
        
        public Visibility Visible 
        { 
            get 
            {
                if (OnClick != null)
                {
                    return Visibility.Visible;
                }
                else
                {
                    return Visibility.Collapsed;
                } 
            } 
        }

        internal RoutedEventHandler OnClick;
    }

    /// <summary>
    /// Issue Panel
    /// </summary>
    public class IssueView
    {
        int LeftPanelWidth = 240;

        StackPanel             m_leftPanel;
        DataGrid               m_grid;
        ProcessMemoryInfo      m_heapInfo;
        TextBox                m_help;

        void OnClick(object sender, RoutedEventArgs e)
        {
            FrameworkElement elm = sender as FrameworkElement;

            if ((elm != null) && (elm.Tag != null))
            {
                Issue issue = elm.Tag as Issue;

                if (issue != null)
                {
                    issue.OnClick(sender, e);
                }
            }
        }
        
        internal Panel CreateIssuePanel(TextBox help)
        {
            m_help = help;

            m_grid = new DataGrid();
            m_grid.AutoGenerateColumns = false;
            m_grid.IsReadOnly = true;

            // Columns
            m_grid.AddColumn("Id",          "Id");
            m_grid.AddColumn("Type",        "Type");
            m_grid.AddColumn("Description", "Description");
            m_grid.AddColumn("Suggestion",  "Suggestion");
            m_grid.AddButtonColumn(typeof(Issue), "Action", "Action", OnClick);

            m_leftPanel = new StackPanel();
            m_leftPanel.Width = LeftPanelWidth;
            m_leftPanel.Background = Brushes.LightGray;

            DockPanel issuePanel = Toolbox.DockTopLeft(null, m_leftPanel, m_grid);

            return issuePanel;
        }

        List<Issue> m_issues;

        internal void SetData(ProcessMemoryInfo heapInfo)
        {
            m_heapInfo = heapInfo;

            m_issues = m_heapInfo.GetIssues();

            m_grid.ItemsSource = m_issues;
        }
    }

    partial class ProcessMemoryInfo : HeapDiagramGenerator
    {
        List<Issue> m_issues;
        Issue       m_issue;

        void AddIssue(IssueType typ, string description, string suggestion = null)
        {
            m_issue = new Issue();

            m_issue.Id = m_issues.Count + 1;
            m_issue.Type = typ;
            m_issue.Description = description;
            m_issue.Suggestion = suggestion;

            m_issues.Add(m_issue);
        }

        int    m_induced;
        double m_inducedPause;

        int    m_allocLarge;
        double m_allocLargePause;
            
        public List<Issue> GetIssues()
        {
            m_issues = new List<Issue>();

            if (m_gcProcess.Total.TotalAllocatedMB == 0)
            {
                AddIssue(IssueType.Profiling, "No .Net heap allocation found.", "Turn on Clr/ClrPrivate ETW event providers and profile again.");
            }

            if (m_allocSites.Count == 0)
            {
                AddIssue(IssueType.Profiling, "No .Net allocation tick event found.", "Turn on Clr allocation tick event and profile again.");
            }

            if (m_gcProcess.ProcessCpuMSec == 0)
            {
                AddIssue(IssueType.Profiling, "No CPU sample event found.", "Turn on CPU sample event and profile again.");
            }
            else
            {
                double gcCpu = m_gcProcess.Total.TotalGCCpuMSec * 100.0 / m_gcProcess.ProcessCpuMSec;

                if (gcCpu >= 40)
                {
                    AddIssue(IssueType.Cpu, String.Format("GC CPU usage extremely high ({0:N1} %)", gcCpu), "Check memory allocation, fragmentation, data structure, object refereence");
                }
                else if (gcCpu >= 10)
                {
                    AddIssue(IssueType.Cpu, String.Format("GC CPU usage higher than normal ({0:N1} %)", gcCpu), "Check memory allocation, fragmentation, data structure, object refereence");
                }
            }

            List<Stats.GCEvent> events = m_gcProcess.Events;

            for (int i = 0; i < events.Count; i++)
            {
                Stats.GCEvent e = events[i];

                if (e.IsInduced())
                {
                    m_induced ++;
                    m_inducedPause += e.PauseDurationMSec;
                }

                if (e.IsAllocLarge())
                {
                    m_allocLarge++;
                    m_allocLargePause += e.PauseDurationMSec;
                }
            }

            if (m_induced != 0)
            {
                AddIssue(
                    IssueType.Cpu, 
                    String.Format("There are {0:N0} induced GCs, causing total {1:N3} ms pause", m_induced, m_inducedPause),
                    "Check call stack to figure out who is inducing GC");

                m_issue.Action = "Induced GC Stacks";
                m_issue.OnClick = OnOpenInducedStacks;
            }

            if (m_allocLarge != 0)
            {
                AddIssue(
                   IssueType.Cpu,
                   String.Format("There are {0:N0} large object GCs, causing total {1:N3} ms pause", m_allocLarge, m_allocLargePause),
                   "Check call stacks to find LOH allocations");

                m_issue.Action = "LOH Allocation Stacks";
                m_issue.OnClick = OnOpenLargeAllocStacks;
            }

            return m_issues;
        }

        void OnOpenLargeAllocStacks(object sender, RoutedEventArgs e)
        {
            StackSource stacks = m_dataFile.CreateStackSource("GC Heap Alloc Ignore Free (Coarse Sampling)", m_gcProcess.ProcessID, m_statusBar.LogWriter, true);

            StackWindow stackWin = null;

            m_dataFile.StackWindowTo(null, ref stackWin, stacks, "LOH Heap Alloc");
        }

        void OnOpenInducedStacks(object sender, RoutedEventArgs e)
        {
            StackSourceBuilder builder = new StackSourceBuilder(m_traceLog);

            List<Stats.GCEvent> events = m_gcProcess.Events;

            for (int i = 0; i < events.Count; i ++)
            {
                Stats.GCEvent ev = events[i];

                if (ev.IsInduced())
                {
                    GcEventExtra extra = GetGcEventExtra(ev.GCNumber, false);

                    if (extra != null)
                    {
                        builder.AddSample(extra.GCStartThread, ev.PauseDurationMSec, ev.GCStartRelativeMSec,
                            String.Format("StartGC({0}, {1}, G{2})", ev.Reason, ev.Type, ev.GCGeneration),
                            extra.GCStartIndex);
                    }
                }
            }

            StackSource source = builder.Stacks;

            if (source.SampleIndexLimit == 0)
            {
                MessageBox.Show("No stacks found for induced GC", ".Net Heap Analyzer", MessageBoxButton.OK);
            }
            else
            {
                StackWindow stackWindow = null;

                m_dataFile.StackWindowTo(null, ref stackWindow, source, "Induced GC", FirstEventTime, LastEventTime);
            }
        }
    }

}
