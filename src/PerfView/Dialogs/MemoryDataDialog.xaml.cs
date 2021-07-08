using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PerfView.Dialogs
{
    /// <summary>
    /// Interaction logic for HeapDumpDialog.xaml
    /// </summary>
    public partial class MemoryDataDialog : WindowBase
    {
        public MemoryDataDialog(CommandLineArgs args, MainWindow mainWindow, Action continuation) : base(mainWindow)
        {
            m_continuation = continuation;
            m_args = args;
            m_mainWindow = mainWindow;
            Owner = mainWindow;
            InitializeComponent();
            if (App.IsElevated)
            {
                Title = Title + " (Administrator)";
            }

            m_mainWindow.StatusBar.Status = "Memory Collection dialog open.";

            // TODO FIX NOW when clrProfilerFormat is selected Freeze must be.
            FreezeCheckBox.IsChecked = m_args.Freeze;
            SaveETLCheckBox.IsChecked = m_args.SaveETL;
            // DumpDataCheckBox.IsChecked = m_args.DumpData;
            MaxDumpTextBox.Text = m_args.MaxDumpCountK.ToString();

            if (args.ProcessDumpFile != null)
            {
                ProcessDumpTextBox.Text = args.ProcessDumpFile;
                DataFileNameTextBox.Text = System.IO.Path.ChangeExtension(args.ProcessDumpFile, ".gcDump");
                SizeToContent = SizeToContent.Height;
                ProcessRow.Visibility = Visibility.Collapsed;
                StatusBar.Status = "Confirm parameters and hit enter to extract the GC heap from the dump.";
                ProcessDumpTextBox.Focus();
                GCButton.IsEnabled = false;
            }
            else
            {
                ProcessDumpRow.Visibility = Visibility.Collapsed;
                StatusBar.Status = "Select a process from which the GC heap will be dumped.";

                // Show the warning if we are not elevated.  
                bool isElevated = App.IsElevated;
                if (isElevated)
                {
                    ElevateWarning.Visibility = Visibility.Collapsed;
                }

                MakeProcessList();
            }
        }

        private void ProcessesMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            DumpHeap(true);
        }
        private void Processes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0)
            {
                var newSelection = e.AddedItems[0] as ProcessInfo;
                if (newSelection != null)
                {
                    var name = newSelection.Name;
                    if (name.StartsWith("WWAHost", StringComparison.OrdinalIgnoreCase))
                    {
                        var m = Regex.Match(newSelection.CommandLine, @" -ServerName:\s*(\S+).wwa");
                        if (m.Success)
                        {
                            name = m.Groups[1].Value;
                        }
                    }
                    if (m_args.DumpHeap)
                    {
                        string heapSnapshotFileName = GCPinnedObjectAnalyzer.GetHeapSnapshotPath(m_args.DataFile);
                        DataFileNameTextBox.Text = System.IO.Path.GetFileName(heapSnapshotFileName);
                    }
                    else
                    {
                        DataFileNameTextBox.Text = CommandProcessor.GetNewFile(name + ".gcDump");
                    }
                }
            }
        }

        private void DumpClick(object sender, RoutedEventArgs e)
        {
            var closeOnComplete = m_args.ProcessDumpFile != null;       // We should close on complete if we are processing a dump file.  
            DumpHeap(closeOnComplete);
        }
        private void CloseClick(object sender, RoutedEventArgs e)
        {
            Close();
            if (m_tookASnapshot && m_continuation != null)
            {
                m_continuation();
            }
        }
        private void DumpHeap(bool closeOnComplete)
        {
            // Are we collecting from a live process?
            m_args.Process = null;
            if (m_processList != null)
            {
                // Set the process ID 
                var selectedProcess = Processes.SelectedItem as ProcessInfo;
                if (selectedProcess != null)
                {
                    m_args.Process = selectedProcess.ProcessID.ToString();
                }
                else
                {
                    StatusBar.Log("No selection made");
                    return;
                }
                m_args.DoCommand = App.CommandProcessor.HeapSnapshot;
            }
            else
            {
                m_args.ProcessDumpFile = ProcessDumpTextBox.Text;
                m_args.DoCommand = App.CommandProcessor.HeapSnapshotFromProcessDump;
            }

            var dataFile = DataFileNameTextBox.Text;
            if (dataFile.Length == 0)
            {
                StatusBar.Log("Error: Output data file not specififed.");
                return;
            }
            m_args.DataFile = dataFile;
            m_args.Freeze = FreezeCheckBox.IsChecked ?? false;
            m_args.SaveETL = SaveETLCheckBox.IsChecked ?? false;
            m_args.DumpData = false;  // TODO FIX NOW actually use
            if (!int.TryParse(MaxDumpTextBox.Text, out m_args.MaxDumpCountK))
            {
                StatusBar.LogError("Could not parse MaxDump " + MaxDumpTextBox.Text);
                return;
            }

            m_args.NoView = true;
            m_tookASnapshot = true;
            m_mainWindow.ExecuteCommand("Dumping GC Heap to " + System.IO.Path.GetFullPath(App.CommandLineArgs.DataFile),
                m_args.DoCommand, StatusBar, closeOnComplete ? m_continuation : null,
            delegate
            {
                m_mainWindow.StatusBar.Status = StatusBar.Status;
                if (closeOnComplete)
                {
                    Close();
                }
                else
                {

                    StatusBar.Status = "Data in: " + DataFileNameTextBox.Text + ".  Press 'Close' or 'Dump GC Heap' to continue.";
                    DataFileNameTextBox.Text = CommandProcessor.GetNewFile(DataFileNameTextBox.Text);
                }
            });
        }

        private void DoHyperlinkHelp(object sender, ExecutedRoutedEventArgs e)
        {
            MainWindow.DisplayUsersGuide(e.Parameter as string);
        }

        private void ProcessDumpKeyDown(object sender, KeyEventArgs e)
        {
            // TODO FIX NOW 
        }

        private void DataFileKeyDown(object sender, KeyEventArgs e)
        {
            // TODO FIX NOW 
        }

        private void DataFileButtonClick(object sender, RoutedEventArgs e)
        {
            // TODO FIX NOW 
        }

        private void ElevateToAdminClick(object sender, RoutedEventArgs e)
        {
            // TODO FIX NOW dump state to CommandLineArgs
            App.CommandProcessor.LaunchPerfViewElevatedIfNeeded("GuiHeapSnapshot", App.CommandLineArgs);
        }
        private void FilterTextKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down)
            {
                Processes.Focus();
            }
        }
        private void FilterTextChanged(object sender, TextChangedEventArgs e)
        {
            var filterText = FilterTextBox.Text;
            if (filterText.Length == 0)
            {
                Processes.ItemsSource = m_processList;
                return;
            }

            Regex filterRegex = null;
            try
            {
                // TODO this is lame no feedback on errors. 
                filterRegex = new Regex(filterText, RegexOptions.IgnoreCase);
            }
            catch (Exception)
            {
                StatusBar.LogError("Illegal .NET Regular Expression " + filterText + " Entered.");
                return;
            }
            var filteredList = new List<ProcessInfo>();
            foreach (var elem in m_processList)
            {
                var wwaAppName = "";
                var commandLine = elem.CommandLine;
                var wwaIndex = commandLine.IndexOf("-ServerName:", StringComparison.OrdinalIgnoreCase);
                if (0 <= wwaIndex)
                {
                    wwaAppName = commandLine.Substring(wwaIndex);
                }

                if (elem.Name != null && (filterRegex.IsMatch(elem.Name)) || filterRegex.IsMatch(wwaAppName) || filterRegex.IsMatch(elem.ProcessID.ToString()))
                {
                    filteredList.Add(elem);
                }
            }
            Processes.ItemsSource = filteredList;

            if (filteredList.Count > 0)
            {
                Processes.SelectedItem = filteredList[0];
            }
        }
        private void AllProcsClick(object sender, RoutedEventArgs e)
        {
            MakeProcessList();
            FilterTextChanged(null, null);
        }
        private void GCButtonClick(object sender, RoutedEventArgs e)
        {
            // Set the process ID 
            var selectedProcess = Processes.SelectedItem as ProcessInfo;
            if (selectedProcess != null)
            {
                m_args.Process = selectedProcess.ProcessID.ToString();
            }
            else
            {
                StatusBar.LogError("No selection made.");
                return;
            }

            m_args.DoCommand = App.CommandProcessor.ForceGC;
            m_mainWindow.ExecuteCommand("Forcing a GC to process " + m_args.Process, m_args.DoCommand, StatusBar, null, delegate
            {
                m_mainWindow.StatusBar.Status = StatusBar.Status;
            });
        }

        private void MakeProcessList()
        {
            var processInfos = new ProcessInfos();
            var myProcessId = Process.GetCurrentProcess().Id;

            // TODO FIX NOW maek the call to GetProcessesWithGCHeaps async.  
            var allProcs = AllProcsCheckBox.IsChecked ?? false;
            m_procsWithHeaps = null;
            if (!allProcs && m_procsWithHeaps == null)
            {
                m_procsWithHeaps = GCHeapDump.GetProcessesWithGCHeaps();
            }

            // Create a list of processes, exclude myself
            m_processList = new List<ProcessInfo>();
            foreach (var process in processInfos.Processes)
            {
                // If the name is null, it is likely a system process, it will not have managed code, so don't bother.   
                if (process.Name == null)
                {
                    continue;
                }

                if (process.ProcessID == myProcessId)
                {
                    continue;
                }

                // Only show processes with GC heaps.  
                if (!allProcs && !m_procsWithHeaps.ContainsKey(process.ProcessID))
                {
                    continue;
                }

                m_processList.Add(process);
            }
            m_processList.Sort(delegate (ProcessInfo x, ProcessInfo y)
            {
                // Sort by name 
                var ret = string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
                if (ret != 0)
                {
                    return ret;
                }
                // Then by process ID 
                return x.ProcessID - y.ProcessID;
            });

            Processes.ItemsSource = m_processList;
            FilterTextBox.Focus();
        }

        private List<ProcessInfo> m_processList;
        private Dictionary<int, GCHeapDump.ProcessInfo> m_procsWithHeaps;
        private Action m_continuation;
        private CommandLineArgs m_args;
        private MainWindow m_mainWindow;
        private bool m_tookASnapshot;
    }
}
