using EventSources;
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Utilities;

namespace PerfView
{
    /// <summary>
    /// Interaction logic for SelectProcess.xaml
    /// </summary>
    public partial class EventWindow : WindowBase
    {
        public EventWindow(Window parent, EventSource source) : base(parent)
        {
            throw new NotImplementedException();
        }
        public EventWindow(EventWindow template)
            : this(template.ParentWindow, template.DataSource)
        {
            TextFilterTextBox.Text = template.TextFilterTextBox.Text;
            StartTextBox.CopyFrom(template.StartTextBox);
            StartTextBox.Text = template.StartTextBox.Text;
            EndTextBox.CopyFrom(template.EndTextBox);
            EndTextBox.Text = template.EndTextBox.Text;
            MaxRetTextBox.CopyFrom(template.MaxRetTextBox);
            MaxRetTextBox.Text = template.MaxRetTextBox.Text;
            ProcessFilterTextBox.CopyFrom(template.ProcessFilterTextBox);
            ProcessFilterTextBox.Text = template.ProcessFilterTextBox.Text;
            EventTypeFilterTextBox.Text = template.EventTypeFilterTextBox.Text;
            FindTextBox.CopyFrom(template.FindTextBox);
            FindTextBox.Text = template.FindTextBox.Text;
            EventTypeFilterTextBox.Text = template.EventTypeFilterTextBox.Text;
            var selection = EventTypes.SelectedItems;
            selection.Clear();
            foreach (var item in template.EventTypes.SelectedItems)
            {
                selection.Add(item);
            }

            Update();
        }
        public EventWindow(Window parent, PerfViewEventSource data)
        {
            DataSource = data;
            ParentWindow = parent;
            InitializeComponent();
            Title = DataSource.Title;
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
                            morphedContent = PerfDataGrid.GoodPrecision((float)cellContent, clipboardContent.Column);
                        }
                        else if (cellContent is double)
                        {
                            morphedContent = PerfDataGrid.GoodPrecision((double)cellContent, clipboardContent.Column);
                        }
                        else if (cellContent != null)
                        {
                            morphedContent = cellContent.ToString();
                        }
                        else
                        {
                            morphedContent = "";
                        }
                    }

                    if (e.ClipboardRowContent.Count > 1 && i + e.StartColumnDisplayIndex != Grid.Columns.Count - 1)
                    {
                        morphedContent = PadForColumn(morphedContent, i + e.StartColumnDisplayIndex);
                    }

                    // TODO Ugly, morph two cells on different rows into one line for the correct cut/paste experience 
                    // for ranges.  
                    if (m_clipboardRangeEnd != m_clipboardRangeStart)  // If we have just 2 things selected (and I can tell them apart)
                    {
                        if (PerfDataGrid.VeryClose(morphedContent, m_clipboardRangeStart))
                        {
                            e.ClipboardRowContent.Clear();
                            morphedContent = morphedContent + " " + m_clipboardRangeEnd;
                            e.ClipboardRowContent.Add(new DataGridClipboardCellContent(clipboardContent.Item, clipboardContent.Column, morphedContent));
                            return;
                        }
                        else if (PerfDataGrid.VeryClose(morphedContent, m_clipboardRangeEnd))
                        {
                            e.ClipboardRowContent.Clear();
                            return;
                        }
                    }
                    e.ClipboardRowContent[i] = new DataGridClipboardCellContent(clipboardContent.Item, clipboardContent.Column, morphedContent);
                }
            };
            Closing += delegate (object sender, CancelEventArgs e)
            {
                if (StatusBar.IsWorking)
                {
                    StatusBar.LogError("Cancel work before closing window.");
                    e.Cancel = true;
                    return;
                }
                DataSource.Viewer = null;
            };

            Loaded += delegate
            {
                EventTypeFilterTextBox.Focus();
            };
            m_source = DataSource.GetEventSource();

            var processNames = m_source.ProcessNames;
            if (processNames != null)
            {
                ProcessFilterTextBox.HistoryLength = processNames.Count + 5;
                ProcessFilterTextBox.SetHistory(processNames);
            }

            m_userDefinedColumns = new List<DataGridColumn>();
            foreach (var gridColumn in Grid.Columns)
            {
                if (((string)gridColumn.Header).StartsWith("Field"))
                {
                    m_userDefinedColumns.Add(gridColumn);
                }
            }

            EventTypes.ItemsSource = m_source.EventNames;
        }

        public PerfViewEventSource DataSource { get; private set; }
        public Window ParentWindow { get; private set; }

        public void SaveDataToCsvFile(string csvFileName, int maxNonRestFields = int.MaxValue)
        {
            var savedNonRestFields = m_source.NonRestFields;
            try
            {
                string listSeparator = Thread.CurrentThread.CurrentCulture.TextInfo.ListSeparator;

                m_source.NonRestFields = Math.Min(m_source.ColumnsToDisplay == null ? 0 : m_source.ColumnsToDisplay.Count, maxNonRestFields);
                using (var csvFile = File.CreateText(csvFileName))
                {
                    // Write out column header
                    csvFile.Write("Event Name{0}Time MSec{0}Process Name", listSeparator);
                    var maxField = 0;
                    var hasRest = true;
                    if (m_source.ColumnsToDisplay != null)
                    {
                        hasRest = false;
                        foreach (var columnName in m_source.ColumnsToDisplay)
                        {
                            Debug.Assert(!columnName.Contains(listSeparator));
                            if (maxField >= m_source.NonRestFields)
                            {
                                hasRest = true;
                                break;
                            }
                            maxField++;
                            csvFile.Write("{0}{1}", listSeparator, columnName);
                        }
                    }
                    if (hasRest)
                    {
                        csvFile.Write("{0}Rest", listSeparator);
                    }

                    csvFile.WriteLine();

                    // Write out events 
                    m_source.ForEach(delegate (EventRecord _event)
                    {
                        // We have exceeded MaxRet, skip it.  
                        if (_event.EventName == null)
                        {
                            return false;
                        }

                        csvFile.Write("{0}{1}{2:f3}{1}{3}", _event.EventName, listSeparator, _event.TimeStampRelatveMSec, EscapeForCsv(_event.ProcessName, listSeparator));
                        var fields = _event.DisplayFields;
                        for (int i = 0; i < maxField; i++)
                        {
                            csvFile.Write("{0}{1}", listSeparator, EscapeForCsv(fields[i], listSeparator));
                        }

                        if (hasRest)
                        {
                            csvFile.Write("{0}{1}", listSeparator, EscapeForCsv(_event.Rest, listSeparator));
                        }

                        csvFile.WriteLine();
                        return true;
                    });
                }
            }
            finally
            {
                m_source.NonRestFields = savedNonRestFields;
            }
        }
        public void SaveDataToXmlFile(string xmlFileName)
        {
            // Sadly, streamWriter does not have a way of setting the IFormatProvider property
            // So we have to do it in this ugly, global variable way.  
            var savedCulture = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
                var xmlExcapesExceptQuote = new char[] { '<', '>', '\'', '&' };
                using (var xmlFile = File.CreateText(xmlFileName))
                {
                    // Write out column header
                    xmlFile.WriteLine("<Events>");
                    m_source.ForEach(delegate (EventRecord _event)
                    {
                        // We have exceeded MaxRet, skip it.  
                        if (_event.EventName == null)
                        {
                            return false;
                        }

                        xmlFile.Write(" <Event EventName=\"{0}\" TimeMsec=\"{1:f3}\" ProcessName=\"{2}\"",
                            _event.EventName, _event.TimeStampRelatveMSec, XmlUtilities.XmlEscape(_event.ProcessName));

                        bool displayRest = true;
                        if (m_source.ColumnsToDisplay != null)
                        {
                            displayRest = m_source.ColumnsToDisplay.Count > m_source.NonRestFields;
                            var limit = Math.Min(m_source.ColumnsToDisplay.Count, m_source.NonRestFields);
                            for (int i = 0; i < limit; i++)
                            {
                                var columnName = m_source.ColumnsToDisplay[i];
                                xmlFile.Write("{0}=\"{1}\"", columnName, XmlUtilities.XmlEscape(_event.DisplayFields[i]));
                            }
                        }

                        if (displayRest)
                        {
                            var rest = _event.Rest;
                            if (rest.Contains("\\\"") || rest.IndexOfAny(xmlExcapesExceptQuote) >= 0)
                            {
                                // Rest contains name="XXXX"  and we have determined that the XXX has either
                                // XML special characters or quoted quotes e.g. \"   
                                // So we need to transform this to legal XML data.  

                                // TODO painfully slow, fragile, trickly 
                                rest = XmlUtilities.XmlEscape(_event.Rest);                      // First escape all XML special chars (including quotes)
                                rest = rest.Replace("&quot;", "\"");                             // Put back all the quotes
                                rest = Regex.Replace(rest, "\\\\(\\\\*)\"", "$1&quote;");        // But escape the escaped quotes.  
                            }
                            xmlFile.Write(" ");
                            xmlFile.Write(rest);
                        }

                        xmlFile.WriteLine("/>");
                        return true;
                    });
                    xmlFile.WriteLine("</Events>");
                }
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = savedCulture;
            }
        }

        private void DoHyperlinkHelp(object sender, ExecutedRoutedEventArgs e)
        {
            var param = e.Parameter as string;
            if (param == null)
            {
                param = "EventViewerQuickStart";       // This is the F1 help
            }

            StatusBar.Log("Displaying Users Guide in Web Browser.");
            MainWindow.DisplayUsersGuide(param);

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
        internal void DoUpdate(object sender, RoutedEventArgs e)
        {
            Update();
        }
        private void DoFind(object sender, RoutedEventArgs e)
        {
            FindTextBox.Focus();
        }
        private void DoFindEnter(object sender, RoutedEventArgs e)
        {
            Find(null);
            DoFindNext(sender, e);
        }

        private void DoFindNext(object sender, RoutedEventArgs e)
        {
            StatusBar.Status = "";
            bool ret = Find(FindTextBox.Text);
            if (!ret)
            {
                StatusBar.LogError("Could not find " + FindTextBox.Text + ".");
            }
        }
        private void DoOpenCpuStacks(object sender, ExecutedRoutedEventArgs e)
        {
            OpenStacks(null);
        }
        private void DoOpenAnyStacks(object sender, ExecutedRoutedEventArgs e)
        {
            OpenStacks("Any");
        }
        private void DoOpenAnyStartStopStacks(object sender, ExecutedRoutedEventArgs e)
        {
            OpenStacks("Any Stacks (with StartStop Activities)");
        }
        private void DoOpenAnyTaskTreeStacks(object sender, ExecutedRoutedEventArgs e)
        {
            OpenStacks("Any TaskTree");
        }
        private void DoOpenThreadStacks(object sender, ExecutedRoutedEventArgs e)
        {
            OpenStacks("Thread Time (with Tasks)");
        }
        private void OpenStacks(string stackSourceName)
        {
            // TODO this could be confusing as we have filtered out everything before a range and can't get it back.  
            if (DataSource != null)
            {
                // If we have selected exactly two items, use that as the time limits, otherwise use what is the my dialog.  
                var startTimeRelativeMSec = m_source.StartTimeRelativeMSec;
                var endTimeRelativeMSec = m_source.EndTimeRelativeMSec;
                var selectedCells = Grid.SelectedCells;
                if (selectedCells.Count == 1 || selectedCells.Count == 2)
                {
                    string start = GetCellStringValue(selectedCells[0]);
                    double parsedStart;
                    if (!double.TryParse(start, out parsedStart))
                    {
                        if (selectedCells.Count != 1)
                        {
                            StatusBar.LogError("Could not parse " + start + " as a number.");
                            return;
                        }
                        StatusBar.Log("Assuming total current time range");
                    }
                    else
                    {
                        startTimeRelativeMSec = parsedStart;
                        endTimeRelativeMSec = parsedStart;
                        if (selectedCells.Count == 2)
                        {
                            string end = GetCellStringValue(selectedCells[1]);
                            if (!double.TryParse(end, out endTimeRelativeMSec))
                            {
                                StatusBar.LogError("Could not parse " + end + " as a number.");
                                return;
                            }
                        }

                        // Make sure that start < end 
                        if (endTimeRelativeMSec < startTimeRelativeMSec)
                        {
                            var tmp = startTimeRelativeMSec;
                            startTimeRelativeMSec = endTimeRelativeMSec;
                            endTimeRelativeMSec = tmp;
                        }
                    }
                }

                // TODO FIX NOW: this should call a routine that does the opening of the stack view 
                // (m_lookedUpCachedSymbolsForETLData should not be needed ...)
                StatusBar.StartWork("Reading " + DataSource.Name, delegate ()
                {
                    // This is where the work gets done.  

                    PerfViewStackSource dataSource = null;
                    var dataFile = DataSource.DataFile;
                    if (dataFile != null)
                    {
                        if (stackSourceName == null)
                        {
                            stackSourceName = dataFile.DefaultStackSourceName;
                        }

                        dataSource = dataFile.GetStackSource(stackSourceName);
                    }
                    if (dataSource == null)
                    {
                        throw new ApplicationException("Could not find stack source " + stackSourceName);
                    }

                    var stackSource = dataSource.GetStackSource(StatusBar.LogWriter, startTimeRelativeMSec - .001, endTimeRelativeMSec + .001);

                    if (!m_lookedUpCachedSymbolsForETLData)
                    {
                        // Lookup all the symbols you can from the cache.  
                        m_lookedUpCachedSymbolsForETLData = true;
                        StatusBar.Log("Quick Looking up symbols from PDB cache.");
                        var etlDataFile = dataFile as ETLPerfViewData;
                        if (etlDataFile != null)
                        {
                            var traceLog = etlDataFile.GetTraceLog(StatusBar.LogWriter);
                            using (var reader = etlDataFile.GetSymbolReader(StatusBar.LogWriter,
                                SymbolReaderOptions.CacheOnly | SymbolReaderOptions.NoNGenSymbolCreation))
                            {
                                // TODO FIX NOW, make this so that it uses the stacks in the view.  
                                var moduleFiles = ETLPerfViewData.GetInterestingModuleFiles(etlDataFile, 5.0, StatusBar.LogWriter, null);
                                foreach (var moduleFile in moduleFiles)
                                {
                                    traceLog.CodeAddresses.LookupSymbolsForModule(reader, moduleFile);
                                }
                            }
                        }
                        StatusBar.Log("Quick Done looking up symbols from PDB cache.");
                    }
                    StatusBar.EndWork(delegate ()
                    {
                        App.CommandProcessor.NoExitOnElevate = true;        // Don't exit because we might have state 

                        var stackWindow = new PerfView.StackWindow(this, dataSource);
                        stackWindow.StatusBar.Log("Read " + DataSource.Name);
                        dataSource.ConfigureStackWindow(stackWindow);
                        stackWindow.StartTextBox.Text = startTimeRelativeMSec.ToString();
                        stackWindow.EndTextBox.Text = endTimeRelativeMSec.ToString();
                        stackWindow.Show();
                        stackWindow.SetStackSource(stackSource);
                    });
                });
            }
        }
        private void DoProcessFilter(object sender, ExecutedRoutedEventArgs e)
        {
            var selectedCells = Grid.SelectedCells;
            if (selectedCells.Count != 1)
            {
                throw new ApplicationException("No cells selected.");
            }

            ProcessFilterTextBox.Text = GetCellStringValue(selectedCells[0]);
            Update();
        }
        private void DoShowEventCounterGraph(object sender, ExecutedRoutedEventArgs e)
        {
            if (EventTypes.SelectedItems.Count != 1)
            {
                return;
            }
            if (!((string)EventTypes.SelectedItems[0]).EndsWith("/EventCounters"))
            {
                return;
            }
            Update();

            string templatePath = Path.Combine(SupportFiles.SupportFileDir, "EventCounterVisualization.html");
            string template = File.ReadAllText(templatePath);

            var counters = BuildCounters(m_source);

            var firstCounter = true;
            var sb = new StringBuilder();
            sb.Append("var data = [");
            foreach (var counter in counters)
            {
                if (firstCounter)
                {
                    firstCounter = false;
                }
                else
                {
                    sb.Append(",");
                }
                sb.Append("{");
                sb.Append(@"name:""");
                sb.Append(counter.Key);
                sb.Append(@""", points:[");
                var firstPoint = true;
                foreach (var point in counter.Value)
                {
                    if (firstPoint)
                    {
                        firstPoint = false;
                    }
                    else
                    {
                        sb.Append(",");
                    }
                    sb.Append("{ X:");
                    sb.Append(point.Item1.ToString(CultureInfo.InvariantCulture));
                    sb.Append(", Y:");
                    sb.Append(point.Item2.ToString(CultureInfo.InvariantCulture));
                    sb.Append("}");
                }
                sb.Append("]}");
            }
            sb.Append("];");
            string html = Path.GetTempFileName() + ".html";
            File.WriteAllText(html, template.Replace("// REPLACE-DATA-HERE", sb.ToString()));

            string uri = "file:///" + html.Replace('\\', '/').Replace(" ", "%20");
            Process.Start(uri);
        }

        private const string PayloadToken = "Payload=\"{";
        private const string PayloadTokenNetCore = "Payload:{";
        private const string NameToken = "Name:";
        private const string DisplayNameToken = "DisplayName:";
        private const string MeanToken = "Mean:";
        private const string IncrementToken = "Increment:";
        private const string IntervalToken = "IntervalSec:";

        private Dictionary<string, List<Tuple<double, double>>> BuildCounters(EventSource source)
        {
            // look for events from "EventCounters"
            // i.e. within Payload={...}, need to find Name, DisplayName and IntervalSec fields
            // however, two counter types exist:
            //  - Mean: Min, Max, Mean fields
            //  - Sum: Increment field with the delta of the values between the last fetch and the current one
            //
            double t = 0;
            var counters = new Dictionary<string, List<Tuple<double, double>>>();
            source.ForEach(delegate (EventRecord event_)
            {
                string rest = event_.Rest;
                if (rest == null)
                {
                    return false;
                }

                // ensure that a payload is available
                var pos = rest.IndexOf(PayloadToken);
                if (pos == -1)
                {
                    pos = rest.IndexOf(PayloadTokenNetCore);
                    if (pos == -1)
                    {
                        return false;
                    }
                    else
                    {
                        pos += PayloadTokenNetCore.Length;
                    }
                }
                else
                {
                    pos += PayloadToken.Length;
                }

                // get Name and DisplayName fields value
                // i.e. use display name if available (.NET Core) or name otherwise
                string name = GetStringField(rest, NameToken, ref pos);
                if (name == null)
                    return false;

                string displayName = GetStringField(rest, DisplayNameToken, ref pos);
                if (displayName == null)
                    displayName = name;

                // check for Mean or Sum type of counter value
                var value = GetNumericField(rest, IncrementToken, ref pos);
                if (value == null)
                {
                    value = GetNumericField(rest, MeanToken, ref pos);
                    if (value == null)
                        return false;
                }

                var interval = GetNumericField(rest, IntervalToken, ref pos);
                if (interval == null)
                    return false;

                string namePart = displayName;
                string meanPart = value;
                string intervalSecPart = interval;

                double mean;
                double intervalSec;
                if (!double.TryParse(meanPart, out mean))
                {
                    return false;
                }
                if (!double.TryParse(intervalSecPart, out intervalSec))
                {
                    return false;
                }

                if (!counters.TryGetValue(namePart, out var points))
                {
                    points = new List<Tuple<double, double>>();
                    counters.Add(namePart, points);
                }
                points.Add(Tuple.Create(t, mean));

                t += intervalSec;

                return true;
            });

            return counters;
        }

        private string GetStringField(string payload, string token, ref int pos)
        {
            var next = pos;

            // a string field is stored in the payload as:
            //    <token>"<value>"
            // note that <token> has the following format: <field>=
            //
            next = payload.IndexOf(token, next);
            if (next == -1)
                return null;

            next += token.Length;
            if (payload[next] != '"')
                return null;
            // skip the " at the beginning of the field value
            next++;

            var end = payload.IndexOf('"', next);
            if (end == -1)
                return null;

            var length = end - next;
            pos = end;
            return payload.Substring(next, length);

        }

        private string GetNumericField(string payload, string field, ref int pos)
        {
            var next = pos;

            // a numeric field is stored in the payload as:
            //    <token><value>
            // note that <token> has the following format: <field>:
            //
            next = payload.IndexOf(field, next);
            if (next == -1)
                return null;

            next += field.Length;

            var end = payload.IndexOf(',', next);
            // handle the case of the last numeric value of the payload
            // i.e. look for " }" instead of ","
            if (end == -1)
            {
                end = payload.IndexOf(" }", next);
                if (end == -1)
                    return null;
            }

            var length = end - next;
            pos = next;
            return payload.Substring(next, length);
        }


        private void DoRangeFilter(object sender, ExecutedRoutedEventArgs e)
        {
            if (Histogram.IsFocused)
            {
                var start = Histogram.SelectionStart;
                var end = Histogram.SelectionLength + start;
                if (start < 0 || end == start)
                {
                    StatusBar.LogError("No selection in the Histogram was made.");
                    return;
                }
                StartTextBox.Text = (m_bucketTimeMSec * start + m_source.StartTimeRelativeMSec).ToString("n3");
                EndTextBox.Text = (m_bucketTimeMSec * end + m_source.StartTimeRelativeMSec).ToString("n3");
                Update();
                return;
            }

            var selectedCells = Grid.SelectedCells;
            if (selectedCells.Count != 2)
            {
                StatusBar.LogError("You must select two cells to set the range.");
                return;
            }
            StartTextBox.Text = GetCellStringValue(selectedCells[0]);
            EndTextBox.Text = GetCellStringValue(selectedCells[1]);
            Update();
        }
        private void DoEventTypesKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
            {
                DoUpdate(sender, e);
            }
        }
        private void DoCancel(object sender, ExecutedRoutedEventArgs e)
        {
            StatusBar.AbortWork();
        }
        private void DoNewWindow(object sender, ExecutedRoutedEventArgs e)
        {
            var newEventViewer = new EventWindow(this);
            newEventViewer.Show();
            Update();
        }
        private void DoColumnsToDisplayListClick(object sender, RoutedEventArgs e)
        {
            if (EventTypes.SelectedItems.Count == 0)
            {
                StatusBar.LogError("No event types selected.");
                return;
            }

            var eventFilter = new List<string>(EventTypes.SelectedItems.Count);
            foreach (var item in EventTypes.SelectedItems)
            {
                eventFilter.Add((string)item);
            }

            var columns = m_source.AllColumnNames(eventFilter);
            if (columns == null)
            {
                StatusBar.LogError("This EventSource does not support column names.");
                return;
            }
            var columnsWithWildCard = new List<string>(columns);
            columnsWithWildCard.Add("*");
            ColumnsToDisplayListBox.ItemsSource = columnsWithWildCard;
            ColumnsToDisplayPopup.IsOpen = true;
        }
        private void DoColumnsToDisplayListBoxKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                UpdateColumnsToDisplay();
                DoUpdate(sender, e);
            }
            else if (e.Key == Key.Tab)
            {
                UpdateColumnsToDisplay();
            }
            else if (e.Key == Key.Escape)
            {
                ColumnsToDisplayPopup.IsOpen = false;
            }
        }
        private void DoColumnsToDisplayListBoxDoubleClick(object sender, MouseButtonEventArgs e)
        {
            UpdateColumnsToDisplay();
            DoUpdate(sender, e);
        }
        private void DoSaveAsCsv(object sender, ExecutedRoutedEventArgs e)
        {
            var saveDialog = new Microsoft.Win32.SaveFileDialog();
            var baseName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(DataSource.FilePath));

            saveDialog.FileName = baseName + ".csv";
            saveDialog.InitialDirectory = Path.GetDirectoryName(DataSource.FilePath);
            saveDialog.Title = "Save Event View as CSV file";
            saveDialog.DefaultExt = ".csv";
            saveDialog.Filter = "Comma Separated Value|*.csv|All Files|*.*";
            saveDialog.AddExtension = true;
            saveDialog.CheckFileExists = false;
            saveDialog.OverwritePrompt = true;
            Nullable<bool> result = saveDialog.ShowDialog();
            if (!(result == true))
            {
                StatusBar.Log("Save csv file canceled.");
                return;
            }

            StatusBar.StartWork("Saving file", delegate ()
            {
                SaveDataToCsvFile(saveDialog.FileName);
                StatusBar.EndWork(delegate ()
                {
                    StatusBar.Log("Saved data to file " + saveDialog.FileName + ".");
                });
            });
        }
        private void DoSaveAsXml(object sender, ExecutedRoutedEventArgs e)
        {
            var saveDialog = new Microsoft.Win32.SaveFileDialog();
            var baseName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(DataSource.FilePath));

            saveDialog.FileName = baseName + ".xml";
            saveDialog.InitialDirectory = Path.GetDirectoryName(DataSource.FilePath);
            saveDialog.Title = "Save View as XML File";
            saveDialog.DefaultExt = ".xml";
            saveDialog.Filter = "Xml File|*.xml|All Files|*.*";
            saveDialog.AddExtension = true;
            saveDialog.OverwritePrompt = true;

            Nullable<bool> result = saveDialog.ShowDialog();
            if (!(result == true))
            {
                StatusBar.Log("Save xml file canceled.");
                return;
            }
            StatusBar.StartWork("Saving file", delegate ()
            {
                SaveDataToXmlFile(saveDialog.FileName);
                StatusBar.EndWork(delegate ()
                {
                    StatusBar.Log("Saved data to file " + saveDialog.FileName + ".");
                });
            });
        }
        private void DoOpenInExcel(object sender, ExecutedRoutedEventArgs e)
        {
            StatusBar.StartWork("Opening in SpreadSheet.", delegate ()
            {
                var csvFile = CacheFiles.FindFile(DataSource.FilePath, ".excel.csv");
                if (File.Exists(csvFile))
                {
                    FileUtilities.TryDelete(csvFile);

                    var baseFile = csvFile.Substring(0, csvFile.Length - 9);
                    for (int i = 1; ; i++)
                    {
                        csvFile = baseFile + i.ToString() + ".excel.csv";
                        if (!File.Exists(csvFile))
                        {
                            break;
                        }
                    }
                }

                SaveDataToCsvFile(csvFile);
                Command.Run(Command.Quote(csvFile), new CommandOptions().AddStart().AddTimeout(CommandOptions.Infinite));
                StatusBar.EndWork(delegate ()
                {
                    StatusBar.Log("CSV launched.");
                });
            });
        }
        private void DoCopyTimeRange(object sender, ExecutedRoutedEventArgs e)
        {
            Clipboard.SetText(RangeUtilities.ToString(StartTextBox.Text, EndTextBox.Text));
        }
        private void DoHistogramSelectionChanged(object sender, RoutedEventArgs e)
        {
            var start = Histogram.SelectionStart;
            var end = Histogram.SelectionLength + start;
            if (start < 0 || end == start)
            {
                StatusBar.Status = "";
                return;
            }

            double total = 0;
            for (int i = start; i < end; i++)
            {
                if (i < m_buckets.Length)
                {
                    total += m_buckets[i];
                }
            }

            var startTimeMSec = m_bucketTimeMSec * start + m_source.StartTimeRelativeMSec;
            var endTimeMSec = m_bucketTimeMSec * end + m_source.StartTimeRelativeMSec;

            var durationMSec = endTimeMSec - startTimeMSec;
            StatusBar.Status = string.Format(
                "Selection: [{0,12:n3} {1,12:n3}] MSec, Length: {2,8:n1} MSec EventCount: {3,8:n0} Rate: {4,6:n1} Ev/Sec",
                startTimeMSec, endTimeMSec, durationMSec, total, total * 1000 / durationMSec);
        }
        private void DoDumpEvent(object sender, ExecutedRoutedEventArgs e)
        {
            int guiIdx = SelectionStartIndex();
            var list = Grid.ItemsSource as System.Collections.IList;
            if (list == null)
            {
                return;
            }

            if (list.Count <= guiIdx)
            {
                return;
            }

            if (DataSource == null)
            {
                return;
            }

            var traceLog = TryGetTraceLog(DataSource.DataFile);
            if (traceLog == null)
            {
                return;
            }

            var elem = list[guiIdx] as PerfView.ETWEventSource.ETWEventRecord;
            if (elem == null)
            {
                return;
            }

            var eventData = traceLog.GetEvent(elem.Index);
            if (eventData != null)
            {
                string eventDataAsString = eventData.Dump(true, true);
                StatusBar.LogWriter.WriteLine(eventDataAsString);
                StatusBar.OpenLog();
                StatusBar.Status = "Event Dumped to log.";
            }
        }

        private TraceLog TryGetTraceLog(PerfViewFile dataFile)
        {
            if (dataFile is ETLPerfViewData)
            {
                return ((ETLPerfViewData)dataFile).TryGetTraceLog();
            }
            if (dataFile is LinuxPerfViewData)
            {
                return ((LinuxPerfViewData)dataFile).TryGetTraceLog();
            }
            else if (dataFile is EventPipePerfViewData)
            {
                return ((EventPipePerfViewData)dataFile).TryGetTraceLog();
            }
            else
            {
                return null;
            }
        }

        private void DoHighlightInHistogram(object sender, ExecutedRoutedEventArgs e)
        {
            var cells = Grid.SelectedCells;
            if (cells.Count > 0)
            {
                double min = 0;
                double max = 0;
                if (double.TryParse(GetCellStringValue(cells[0]), out min) &&
                    double.TryParse(GetCellStringValue(cells[cells.Count - 1]), out max))
                {
                    // Swap them if necessary.  
                    if (max < min)
                    {
                        var tmp = max;
                        max = min;
                        min = tmp;
                    }

                    if (m_source.StartTimeRelativeMSec <= min && max <= m_source.EndTimeRelativeMSec)
                    {
                        int firstPos = (int)((min - m_source.StartTimeRelativeMSec) / m_bucketTimeMSec);
                        int lastPos = (int)Math.Ceiling((max - m_source.StartTimeRelativeMSec) / m_bucketTimeMSec);
                        if (firstPos == lastPos)
                        {
                            lastPos++;
                        }

                        int totalLength = Histogram.Text.Length;
                        if (lastPos <= totalLength)
                        {
                            Histogram.SelectionStart = firstPos;
                            Histogram.SelectionLength = lastPos - firstPos;
                            Histogram.Focus();
                        }
                    }
                }
                else
                {
                    StatusBar.LogError("Cells are not numbers.");
                }
            }
            else
            {
                StatusBar.LogError("No Cells Selected.");
            }
        }

        private void UpdateColumnsToDisplay()
        {
            ColumnsToDisplayPopup.IsOpen = false;
            string columnsToDisplay = "";
            string sep = "";
            foreach (var item in ColumnsToDisplayListBox.SelectedItems)
            {
                columnsToDisplay += sep + ((string)item);
                sep = " ";
            }
            ColumnsToDisplayTextBox.Text = columnsToDisplay;
        }

        private void DoEventTypeFilterTextBoxChanged(object sender, TextChangedEventArgs e)
        {
            var filteredList = new List<string>();

            var filterPat = EventTypeFilterTextBox.Text;
            Regex regEx = null;
            try { regEx = new Regex(filterPat, RegexOptions.IgnoreCase); }
            catch { }

            foreach (var name in m_source.EventNames)
            {
                if (regEx == null || regEx.IsMatch(name))
                {
                    filteredList.Add(name);
                }
            }

            EventTypes.ItemsSource = filteredList;
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
                m_findPat = new Regex(pat, RegexOptions.IgnoreCase);    // TODO perf bad if you compile!
            }

            var list = Grid.ItemsSource as System.Collections.IList;
            if (list == null || list.Count == 0)
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

                var item = list[curPos] as EventRecord;
                var foundItem = m_findPat.IsMatch(item.Rest) || m_findPat.IsMatch(item.EventName) ||
                    m_findPat.IsMatch(item.ProcessName) || m_findPat.IsMatch(item.TimeStampRelatveMSec.ToString());
                var fields = item.DisplayFields;
                for (int i = 0; i < fields.Length; i++)
                {
                    if (foundItem)
                    {
                        break;
                    }

                    var field = fields[i];
                    if (field != null)
                    {
                        foundItem = m_findPat.IsMatch(field);
                    }
                }

                if (foundItem)
                {
                    Select(item);
                    return true;
                }
            }
        }
        public void Update()
        {
            if (string.IsNullOrWhiteSpace(EndTextBox.Text))
            {
                m_source.EndTimeRelativeMSec = m_source.MaxEventTimeRelativeMsec;
            }
            else if (!double.TryParse(EndTextBox.Text, out m_source.EndTimeRelativeMSec))
            {
                StatusBar.LogError("Invalid number " + EndTextBox.Text);
                return;
            }

            // See if we are pasting a range.  
            if (string.IsNullOrWhiteSpace(StartTextBox.Text))
            {
                m_source.StartTimeRelativeMSec = 0;
            }
            else
            {
                if (RangeUtilities.TryParse(StartTextBox.Text, out var startTimeRelativeMSec, out var endTimeRelativeMSec))
                {
                    m_source.StartTimeRelativeMSec = startTimeRelativeMSec;
                    m_source.EndTimeRelativeMSec = endTimeRelativeMSec;
                }
                else
                {
                    if (!double.TryParse(StartTextBox.Text, out m_source.StartTimeRelativeMSec))
                    {
                        StatusBar.LogError("Invalid number " + StartTextBox.Text);
                        return;
                    }
                }
            }
            // Fix it if they are out of order
            if (m_source.StartTimeRelativeMSec > m_source.EndTimeRelativeMSec)
            {
                var temp = m_source.StartTimeRelativeMSec;
                m_source.StartTimeRelativeMSec = m_source.EndTimeRelativeMSec;
                m_source.EndTimeRelativeMSec = temp;
            }

            EndTextBox.Text = m_source.EndTimeRelativeMSec.ToString("n3");
            StartTextBox.Text = m_source.StartTimeRelativeMSec.ToString("n3");
            m_source.StartTimeRelativeMSec -= .0006;         // Insure that we are inclusive as far as rounding goes.
            m_source.EndTimeRelativeMSec += .0006;

            if (!int.TryParse(MaxRetTextBox.Text, out m_source.MaxRet))
            {
                if (MaxRetTextBox.Text == "")
                {
                    m_source.MaxRet = 10000;
                }
                else
                {
                    StatusBar.LogError("Invalid number " + MaxRetTextBox.Text);
                    return;
                }
            }
            MaxRetTextBox.Text = m_source.MaxRet.ToString();

            if (EventTypes.SelectedItems.Count == 0)
            {
                StatusBar.LogError("No event types are selected.");
                return;
            }

            var eventFilter = new List<string>(EventTypes.SelectedItems.Count);
            foreach (var item in EventTypes.SelectedItems)
            {
                eventFilter.Add((string)item);
            }

            m_source.SetEventFilter(eventFilter);
            m_source.ColumnsToDisplay = EventSource.ParseColumns(ColumnsToDisplayTextBox.Text, m_source.AllColumnNames(eventFilter));
            for (int i = 0; i < m_userDefinedColumns.Count; i++)
            {
                if (m_source.ColumnsToDisplay != null && i < m_source.ColumnsToDisplay.Count)
                {
                    m_userDefinedColumns[i].Visibility = System.Windows.Visibility.Visible;
                    // For some reason underscores in the name of the column header get removed
                    // (it probably means something special to the Grid), we fix this by replacing
                    // them with __.  
                    m_userDefinedColumns[i].Header = m_source.ColumnsToDisplay[i].Replace("_", "__");
                }
                else
                {
                    m_userDefinedColumns[i].Visibility = System.Windows.Visibility.Hidden;
                }
            }

            // Change 'spin.exe (32434)' into 'spin.exe \(32434\)'   
            m_source.ProcessFilterRegex = Regex.Replace(ProcessFilterTextBox.Text, @"\\*\((\d+)\\*\)", @"\($1\)");
            m_source.TextFilterRegex = TextFilterTextBox.Text;

            // Can make this incremental by using an ObservableCollection.  
            var events = new ObservableCollection<EventRecord>();
            Grid.ItemsSource = events;
            Grid.Background = Brushes.Gray;
            EventTypes.Background = Brushes.Gray;
            Grid.RowBackground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200));
            Histogram.Text = "";

            StatusBar.StartWork("Scanning Events", delegate ()
            {
                const int numBuckets = 100;
                m_buckets = new double[numBuckets];
                m_bucketTimeMSec = (m_source.EndTimeRelativeMSec - m_source.StartTimeRelativeMSec) / numBuckets;
                var maxBucketCount = 0.0;
                var lastBucketNum = -1;
                var eventCount = 0;

                m_source.ForEach(delegate (EventRecord event_)
                {
                    eventCount++;
                    if (event_.EventName != null)
                    {
                        Add(events, event_);
                    }

                    // Compute the histogram of counts over time for the events.  
                    var bucketNum = (int)((event_.TimeStampRelatveMSec - m_source.StartTimeRelativeMSec) / m_bucketTimeMSec);
                    if (bucketNum < 0)
                    {
                        bucketNum = 0;
                    }
                    else if (bucketNum >= m_buckets.Length)
                    {
                        bucketNum = m_buckets.Length - 1;
                    }

                    var bucketVal = m_buckets[bucketNum] + 1;
                    m_buckets[bucketNum] = bucketVal;
                    if (bucketVal > maxBucketCount)
                    {
                        maxBucketCount = bucketVal;
                    }

                    // When we move on to a new bucket, update the Histogram string.   
                    if (lastBucketNum < bucketNum)
                    {
                        Dispatcher.BeginInvoke((Action)delegate ()
                        {
                            Histogram.Text = Microsoft.Diagnostics.Tracing.Stacks.HistogramController.HistogramString(
                                m_buckets, maxBucketCount, bucketNum + 1);
                        });
                        lastBucketNum = bucketNum;
                    }
                    return true;
                });

                // Compute the final histogram string
                var histString = Microsoft.Diagnostics.Tracing.Stacks.HistogramController.HistogramString(m_buckets, maxBucketCount);
                StatusBar.EndWork(delegate ()
                {
                    if (events.Count == m_source.MaxRet)
                    {
                        StatusBar.Log("WARNING, returned the maximum " + events.Count + " records.");
                    }

                    Histogram.Text = histString;
                    StatusBar.Log("Histogram: " + histString + " Time Bucket " + m_bucketTimeMSec.ToString("n1") + " MSec");

                    var sb = new StringBuilder();
                    sb.Append("[Found ").Append(events.Count);
                    if (events.Count >= m_source.MaxRet)
                    {
                        sb.Append(" (TRUNCATED. Set MaxRet for more)");
                    }

                    sb.Append(" Records.  ").Append(eventCount.ToString("n0")).Append(" total events.");

                    if (events.Count == 0 && !string.IsNullOrWhiteSpace(m_source.TextFilterRegex))
                    {
                        sb.Append("  WARNING: TextFilter is active.");
                    }

                    // Display any column sums that are available.  
                    if (m_source.ColumnSums != null && m_source.ColumnsToDisplay != null)
                    {
                        bool first = true;
                        for (int i = 0; i < m_source.ColumnSums.Length; i++)
                        {
                            var sum = m_source.ColumnSums[i];
                            if (sum != 0)
                            {
                                if (first)
                                {
                                    sb.Append(" ColumnSums:");
                                    first = false;
                                }
                                sb.Append(' ').Append(m_source.ColumnsToDisplay[i]).Append('=').Append(sum.ToString("n3"));
                            }
                        }
                    }
                    sb.AppendLine("]");
                    StatusBar.Log(sb.ToString());
                });
            }, delegate ()       // This is the finally clause.  Happens even on exceptions and cancelations.  
            {
                Grid.Background = Brushes.White;
                Grid.RowBackground = Brushes.White;
                EventTypes.Background = Brushes.White;
            });
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
        }
        public int SelectionStartIndex()
        {
            var ret = 0;
            var cells = Grid.SelectedCells;
            if (cells.Count > 0)
            {
                var cell = cells[0];

                // TODO should not have to be linear
                var list = Grid.ItemsSource as System.Collections.IList;
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

        /// <summary>
        /// given the content string, and the columnIndex, return a string that is propertly padded
        /// so that when displayed the rows will line up by columns nicely  
        /// </summary>
        public string PadForColumn(string content, int columnIndex)
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
                    m_maxColumnInSelection[idx] = Math.Max(m_maxColumnInSelection[idx], GetCellStringValue(cellInfo).Length);
                }
                maxString = m_maxColumnInSelection[columnIndex];
            }

            // TODO use the alignment attribute 
            if (columnIndex == 1)
            {
                return content.PadLeft(maxString);
            }
            else
            {
                return content.PadRight(maxString);
            }
        }
        public string GetCellStringValue(DataGridCellInfo cell)
        {
            var record = cell.Item as EventRecord;
            if (record != null)
            {
                if (cell.Column == EventNameColumn)
                {
                    return record.EventName;
                }

                if (cell.Column == ProcessNameColumn)
                {
                    return record.ProcessName;
                }

                if (cell.Column == TimeMSecColumn)
                {
                    return record.TimeStampRelatveMSec.ToString("n3");
                }

                for (int i = 0; i < m_userDefinedColumns.Count; i++)
                {
                    if (cell.Column == m_userDefinedColumns[i])
                    {
                        var value = record.DisplayFields[i];
                        if (value == null)
                        {
                            value = "";
                        }

                        return value;

                    }
                }
            }
            // Fallback see if we can scrape it from the GUI object.  
            FrameworkElement contents = cell.Column.GetCellContent(cell.Item);
            if (contents == null)
            {
                return "";
            }

            return Helpers.GetText(contents);
        }

        public static string GetColumnHeaderText(DataGridColumn column)
        {
            return column.Header as string;
        }

        #region commandDefintions
        public static RoutedUICommand UsersGuideCommand = new RoutedUICommand("UsersGuide", "UsersGuide", typeof(EventWindow));
        public static RoutedUICommand UpdateCommand = new RoutedUICommand("Update", "Update", typeof(EventWindow),
            new InputGestureCollection() { new KeyGesture(Key.F5) });
        public static RoutedUICommand FindCommand = new RoutedUICommand("Find", "Find", typeof(EventWindow),
            new InputGestureCollection() { new KeyGesture(Key.F, ModifierKeys.Control) });
        public static RoutedUICommand FindNextCommand = new RoutedUICommand("Find Next", "FindNext", typeof(EventWindow),
            new InputGestureCollection() { new KeyGesture(Key.F3) });
        public static RoutedUICommand OpenCpuStacksCommand = new RoutedUICommand("Open Cpu Stacks", "OpenCpuStacks",
            typeof(EventWindow));
        public static RoutedUICommand OpenThreadStacksCommand = new RoutedUICommand("Open Thread Stacks", "OpenThreadStacks",
            typeof(EventWindow));
        public static RoutedUICommand OpenAnyStacksCommand = new RoutedUICommand("Open Any Stacks", "OpenAnyStacks",
            typeof(EventWindow), new InputGestureCollection() { new KeyGesture(Key.S, ModifierKeys.Alt) });
        public static RoutedUICommand OpenAnyStartStopStacksCommand = new RoutedUICommand("Open Any Start Stop Stacks", "OpenAnyStartStopStacks", typeof(EventWindow));
        public static RoutedUICommand OpenAnyTaskTreeStacksCommand = new RoutedUICommand("Open Any TaskTree Stacks", "OpenAnyTaskTreeStacks", typeof(EventWindow));

        public static RoutedUICommand ShowEventCounterGraphCommand = new RoutedUICommand("Show EventCounter Graph", "ShowEventCounterGraph",
            typeof(EventWindow), new InputGestureCollection() { new KeyGesture(Key.G, ModifierKeys.Control) });

        public static RoutedUICommand SetProcessFilterCommand = new RoutedUICommand("Set Process Filter", "SetProcessFilter",
            typeof(EventWindow), new InputGestureCollection() { new KeyGesture(Key.P, ModifierKeys.Control) });
        public static RoutedUICommand SetRangeFilterCommand = new RoutedUICommand("Set Range Filter", "SetRangeFilter",
            typeof(EventWindow), new InputGestureCollection() { new KeyGesture(Key.R, ModifierKeys.Alt) });
        public static RoutedUICommand CancelCommand = new RoutedUICommand("Cancel", "Cancel", typeof(EventWindow),
            new InputGestureCollection() { new KeyGesture(Key.Escape) });
        public static RoutedUICommand NewWindowCommand = new RoutedUICommand("New Window", "NewWindow", typeof(EventWindow),
            new InputGestureCollection() { new KeyGesture(Key.N, ModifierKeys.Control) });
        public static RoutedUICommand SaveAsCsvCommand = new RoutedUICommand("Save View as CSV", "SaveAsCsv",
            typeof(EventWindow), new InputGestureCollection() { new KeyGesture(Key.S, ModifierKeys.Control) });
        public static RoutedUICommand SaveAsXmlCommand = new RoutedUICommand("Save View as XML", "SaveAsXml",
             typeof(EventWindow), new InputGestureCollection() { new KeyGesture(Key.X, ModifierKeys.Control) });
        public static RoutedUICommand OpenInExcelCommand = new RoutedUICommand("Open View In Excel", "OpenInExcel",
            typeof(EventWindow), new InputGestureCollection() { new KeyGesture(Key.E, ModifierKeys.Control) });
        public static RoutedUICommand CopyTimeRangeCommand = new RoutedUICommand("Copy Time Range", "CopyTimeRange", typeof(EventWindow),
            new InputGestureCollection() { new KeyGesture(Key.T, ModifierKeys.Alt) });
        public static RoutedUICommand DumpEventCommand = new RoutedUICommand("Dump Event", "DumpEvent", typeof(EventWindow),
            new InputGestureCollection() { new KeyGesture(Key.D, ModifierKeys.Alt) });
        public static RoutedUICommand HighlightInHistogramCommand = new RoutedUICommand("Highlight In Histogram", "HighlightInHistogram", typeof(EventWindow),
            new InputGestureCollection() { new KeyGesture(Key.H, ModifierKeys.Alt) });
        #endregion

        #region private

        /// <summary>
        /// Returns a string that is will be exactly one field of a CSV file.  Thus it escapes , and ""
        /// </summary>
        internal static string EscapeForCsv(string str, string listSeparator)
        {
            // TODO FIX NOW is this a hack?
            if (str == null)
            {
                return "";
            }
            // If you don't have a comma, you are OK (we are losing leading and trailing whitespace but I don't care about that. 
            if (str.IndexOf(listSeparator) < 0)
            {
                return str;
            }

            // Escape all " by repeating them
            str = str.Replace("\"", "\"\"");
            return "\"" + str + "\"";       // then quote the whole thing
        }

        private void SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
        {
            m_clipboardRangeStart = "";
            m_clipboardRangeEnd = "";

            var dataGrid = sender as DataGrid;

            var cells = dataGrid.SelectedCells;
            bool seenHexValue = false;
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

                foreach (DataGridCellInfo cell in cells)
                {
                    string cellStringValue = GetCellStringValue(cell);
                    if (0 < cellStringValue.Length)
                    {
                        double num;

                        bool parseSuccessful = false;
                        if (cellStringValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        {
                            long asLong = 0;
                            parseSuccessful = long.TryParse(cellStringValue.Substring(2), NumberStyles.AllowHexSpecifier | NumberStyles.AllowTrailingWhite, null, out asLong);
                            num = asLong;
                            seenHexValue = true;
                        }
                        else
                        {
                            parseSuccessful = double.TryParse(cellStringValue, out num);
                        }

                        if (parseSuccessful)
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
                        text += string.Format("   Diff={0:n3}", max - min);
                        double ratio = Math.Abs(first / second);
                        if (.001 <= ratio && ratio <= 1000)
                        {
                            text += string.Format("   X/Y={0:n3}   Y/X={1:n3}", ratio, 1 / ratio);
                        }
                    }
                    else
                    {
                        text += string.Format("   Count={0}", count);
                    }

                    if (seenHexValue)
                    {
                        text += string.Format(" HexSum=0x{0:x}", (long)sum);
                        if (count == 2)
                        {
                            text += string.Format(" HexDiff=0x{0:x}", (long)(max - min));
                        }
                    }
                    StatusBar.Status = text;
                }
            }

            // TODO: we really need to combine PerfDataGrid and EventViewer so that all this ugly logic is in one place. 
            if (cells.Count <= 2)
            {
                if (cells.Count == 2)
                {
                    m_clipboardRangeStart = GetCellStringValue(cells[0]);
                    m_clipboardRangeEnd = GetCellStringValue(cells[1]);
                }
                if (cells.Count == 1)
                {
                    // We have only one cell copy its contents to the status box
                    var cellStr = GetCellStringValue(cells[0]);

                    var cellAsHexDec = cellStr;
                    if (cellStr != null && cellStr.Length != 0)
                    {
                        long asNum;
                        if (cellStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && long.TryParse(cellStr.Substring(2), NumberStyles.HexNumber, null, out asNum))
                        {
                            cellAsHexDec = cellAsHexDec + " (" + asNum.ToString("n") + ")";
                        }
                        else if (long.TryParse(cellStr.Replace(",", ""), out asNum))
                        {
                            cellAsHexDec = cellAsHexDec + " (0x" + asNum.ToString("x") + ")";
                        }

                        StatusBar.Status = "CellContents: " + cellAsHexDec;
                    }

                    try
                    {
                        var clipBoardStr = Clipboard.GetText().Trim();
                        if (clipBoardStr.Length > 0)
                        {
                            double clipBoardVal;
                            double cellVal;
                            if (double.TryParse(clipBoardStr, out clipBoardVal) &&
                                double.TryParse(cellStr, out cellVal))
                            {
                                var reply = string.Format("Cell Contents: {0:f3} ClipBoard: {1:n3}   Sum={2:n3}   Diff={3:n3}",
                                    cellAsHexDec, clipBoardVal, cellVal + clipBoardVal, Math.Abs(cellVal - clipBoardVal));

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
                    catch (Exception eClipBoard)
                    {
                        StatusBar.Log("Warning: exception trying to get Clipboard: " + eClipBoard.Message);
                    }
                }
                Grid.ClipboardCopyMode = DataGridClipboardCopyMode.ExcludeHeader;
            }
            else
            {
                Grid.ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader;
            }

            m_maxColumnInSelection = null;
        }

        /// <summary>
        /// This needs to be separate routine so that the event_ local variable is a copy of the one that was passed
        /// </summary>
        private void Add(ObservableCollection<EventRecord> events, EventRecord event_)
        {
            // TODO we currently have a problem where we make the GUI unresponsive because we flood it with BeginInvoke request here.  
            // We fix this currently by sleeping every 20 adds, we should probably batch these but that complicates the interface.  
            --m_adds;
            if (m_adds <= 0)
            {
                m_adds = 20;
                System.Threading.Thread.Sleep(1);
            }

            Dispatcher.BeginInvoke((Action)delegate ()
            {
                events.Add(event_);
            });
        }

        private int m_adds;

        /// <summary>
        /// If we have only two cells selected, even if they are on different rows we want to morph them
        /// to a single row.  These variables are for detecting this situation.  
        /// </summary>
        private string m_clipboardRangeStart;
        private string m_clipboardRangeEnd;
        private int[] m_maxColumnInSelection;
        private int m_FindEnd;
        private Regex m_findPat;
        private bool m_lookedUpCachedSymbolsForETLData;       // have we try to resolve symbols
        private EventSource m_source;
        private List<DataGridColumn> m_userDefinedColumns;
        private double[] m_buckets;                              // Keep track of the counts of events.  
        private double m_bucketTimeMSec;                        // Size for each bucket
        #endregion
    }
}
