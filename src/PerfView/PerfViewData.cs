using Diagnostics.Tracing.StackSources;
using global::DiagnosticsHub.Packaging.Interop;
using Graphs;
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.AutomatedAnalysis;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.EventPipe;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.AspNet;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Parsers.ClrPrivate;
using Microsoft.Diagnostics.Tracing.Parsers.ETWClrProfiler;
using Microsoft.Diagnostics.Tracing.Parsers.IIS_Trace;
using Microsoft.Diagnostics.Tracing.Parsers.InteropEventProvider;
using Microsoft.Diagnostics.Tracing.Parsers.JSDumpHeap;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Parsers.MicrosoftAntimalwareAMFilter;
using Microsoft.Diagnostics.Tracing.Parsers.MicrosoftAntimalwareEngine;
using Microsoft.Diagnostics.Tracing.Stacks;
using Microsoft.Diagnostics.Tracing.StackSources;
using Microsoft.Diagnostics.Utilities;
using Microsoft.DiagnosticsHub.Packaging.InteropEx;
using PerfView.GuiUtilities;
using PerfViewExtensibility;
using PerfViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Xml;
using Utilities;
using Address = System.UInt64;
using EventSource = EventSources.EventSource;

namespace PerfView
{
    /// <summary>
    /// PerfViewTreeItem is a common base class for something that can be represented in the 
    /// TreeView GUI.  in particular it has a name, and children.   This includes both
    /// file directories as well as data file (that might have multiple sources inside them)
    /// </summary>
    public abstract class PerfViewTreeItem : INotifyPropertyChanged
    {
        /// <summary>
        /// The name to place in the treeview (should be short).  
        /// </summary>
        public string Name { get; protected internal set; }
        /// <summary>
        /// If the entry should have children in the TreeView, this is them.
        /// </summary>
        public virtual IList<PerfViewTreeItem> Children { get { return m_Children; } }
        /// <summary>
        /// All items have some sort of file path that is associated with them.  
        /// </summary>
        public virtual string FilePath { get { return m_filePath; } }
        public virtual string HelpAnchor { get { return GetType().Name; } }

        public bool IsExpanded { get { return m_isExpanded; } set { m_isExpanded = value; FirePropertyChanged("IsExpanded"); } }
        public bool IsSelected { get { return m_isSelected; } set { m_isSelected = value; FirePropertyChanged("IsSelected"); } }

        /// <summary>
        /// Open the file (This might be expensive (but maybe not).  It should not be run on
        /// the GUI thread.  This should populate the Children property if that is appropriate.  
        /// 
        /// if 'doAfter' is present, it will be run after the window has been opened.   It is always
        /// executed on the GUI thread.  
        /// </summary>
        public abstract void Open(Window parentWindow, StatusBar worker, Action doAfter = null);
        /// <summary>
        /// Once something is opened, it should be closed.  
        /// </summary>
        public abstract void Close();

        // Support so that we can update children and the view will update
        public event PropertyChangedEventHandler PropertyChanged;
        protected void FirePropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// The Icon to show next to the entry.  
        /// </summary>
        public virtual ImageSource Icon { get { return GuiApp.MainWindow.Resources["StackSourceBitmapImage"] as ImageSource; } }
        #region private
        public override string ToString() { if (FilePath != null) { return FilePath; } return Name; }

        protected List<PerfViewTreeItem> m_Children;
        protected List<PerfViewReport> m_UserDeclaredChildren;
        protected string m_filePath;
        private bool m_isExpanded;
        private bool m_isSelected;
        #endregion
    }

    public class PerfViewDirectory : PerfViewTreeItem
    {
        // Only names that match this filter are displayed. 
        public Regex Filter
        {
            get { return m_filter; }
            set
            {
                if (m_filter != value)
                {
                    m_filter = value;
                    m_Children = null;
                    FirePropertyChanged("Children");
                }
            }
        }
        public PerfViewDirectory(string path)
        {
            m_filePath = path;
            Name = System.IO.Path.GetFileName(path);
        }

        public override IList<PerfViewTreeItem> Children
        {
            get
            {
                if (m_Children == null)
                {
                    m_Children = new List<PerfViewTreeItem>();
                    if (Name != "..")
                    {
                        try
                        {
                            foreach (var filePath in FilesInDirectory(m_filePath))
                            {
                                var template = PerfViewFile.TryGet(filePath);
                                if (template != null)
                                {
                                    // Filter out kernel, rundown files etc, if the base file exists.  
                                    Match m = Regex.Match(filePath, @"^(.*)\.(kernel|clr|user)[^.]*\.etl$", RegexOptions.IgnoreCase);
                                    if (m.Success && File.Exists(m.Groups[1].Value + ".etl"))
                                        continue;

                                    // Filter out any items we were asked to filter out.  
                                    if (m_filter != null && !m_filter.IsMatch(Path.GetFileName(filePath)))
                                        continue;

                                    m_Children.Add(PerfViewFile.Get(filePath, template));
                                }
                            }

                            foreach (var dir in DirsInDirectory(m_filePath))
                            {
                                // Filter out any items we were asked to filter out.  
                                if (m_filter != null && !m_filter.IsMatch(Path.GetFileName(dir)))
                                {
                                    continue;
                                }
                                // We know that .NGENPDB directories are uninteresting, filter them out.  
                                if (dir.EndsWith(".NGENPDB", StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                m_Children.Add(new PerfViewDirectory(dir));
                            }

                            // We always have the parent directory.  
                            m_Children.Add(new PerfViewDirectory(System.IO.Path.Combine(m_filePath, "..")));
                        }
                        // FIX NOW review
                        catch (Exception) { }
                    }
                }
                return m_Children;
            }
        }
        public override string HelpAnchor { get { return null; } }      // Don't bother with help for this.  

        /// <summary>
        /// Open the file (This might be expensive (but maybe not).  This should populate the Children property 
        /// too.  
        /// </summary>
        public override void Open(Window parentWindow, StatusBar worker, Action doAfter)
        {
            var mainWindow = parentWindow as MainWindow;
            if (mainWindow != null)
            {
                mainWindow.OpenPath(FilePath);
            }

            doAfter?.Invoke();
        }
        /// <summary>
        /// Close the file
        /// </summary>
        public override void Close() { }

        public override ImageSource Icon { get { return GuiApp.MainWindow.Resources["FolderOpenBitmapImage"] as ImageSource; } }

        #region private

        private class DirCacheEntry
        {
            public string[] FilesInDirectory;
            public string[] DirsInDirectory;
            public DateTime LastWriteTimeUtc;
        }

        // To speed things up we remember the list list of directory items we fetched from disk
        private static Dictionary<string, DirCacheEntry> s_dirCache = new Dictionary<string, DirCacheEntry>();

        private static string[] FilesInDirectory(string directoryPath)
        {
            var entry = GetDirEntry(directoryPath);
            if (entry.FilesInDirectory == null)
            {
                entry.FilesInDirectory = Directory.GetFiles(directoryPath);
            }

            return entry.FilesInDirectory;
        }

        private static string[] DirsInDirectory(string directoryPath)
        {
            var entry = GetDirEntry(directoryPath);
            if (entry.DirsInDirectory == null)
            {
                entry.DirsInDirectory = Directory.GetDirectories(directoryPath);
            }

            return entry.DirsInDirectory;
        }

        /// <summary>
        /// Gets a cache entry, nulls it out if it is out of date.  
        /// </summary>
        private static DirCacheEntry GetDirEntry(string directoryPath)
        {
            DateTime lastWrite = Directory.GetLastWriteTimeUtc(directoryPath);
            DirCacheEntry entry;
            if (!s_dirCache.TryGetValue(directoryPath, out entry))
            {
                s_dirCache[directoryPath] = entry = new DirCacheEntry();
            }

            if (lastWrite != entry.LastWriteTimeUtc)
            {
                entry.DirsInDirectory = null;
                entry.FilesInDirectory = null;
            }
            entry.LastWriteTimeUtc = lastWrite;
            return entry;
        }

        private Regex m_filter;
        #endregion
    }

    /// <summary>
    /// A PerfViewTreeGroup simply groups other Items.  Thus it has a name, and you use the Children
    /// to add Child nodes to the group.  
    /// </summary>
    public class PerfViewTreeGroup : PerfViewTreeItem
    {
        public PerfViewTreeGroup(string name)
        {
            Name = name;
            m_Children = new List<PerfViewTreeItem>();
        }

        public PerfViewTreeGroup AddChild(PerfViewTreeItem child)
        {
            m_Children.Add(child);
            return this;
        }

        // Groups do no semantic action.   All the work is in the visual GUI part.  
        public override void Open(Window parentWindow, StatusBar worker, Action doAfter = null)
        {
            doAfter?.Invoke();
        }
        public override void Close() { }

        public override IList<PerfViewTreeItem> Children { get { return m_Children; } }

        public override string HelpAnchor { get { return Name.Replace(" ", ""); } }

        public override ImageSource Icon { get { return GuiApp.MainWindow.Resources["FolderOpenBitmapImage"] as ImageSource; } }
    }

    /// <summary>
    /// PerfViewData is an abstraction of something that PerfViewGui knows how to display.   It is 
    /// </summary>
    public abstract class PerfViewFile : PerfViewTreeItem
    {
        public bool IsOpened { get { return m_opened; } }
        public bool IsUpToDate { get { return m_utcLastWriteAtOpen == File.GetLastWriteTimeUtc(FilePath); } }

        /// <summary>
        /// Get does not actually open the file (which might be expensive).   It also does not
        /// populate the children for the node (which again might be expensive).  Instead it 
        /// just looks at the file name).   It DOES however determine how this data will be
        /// treated from here on (based on file extension or an explicitly passed template parameter.
        /// 
        /// Get implements interning, so if you Get the same full file path, then you will get the
        /// same PerfViewDataFile structure. 
        /// 
        /// After you have gotten a PerfViewData, you use instance methods to manipulate it
        /// 
        /// This routine throws if the path name does not have a suffix we understand.  
        /// </summary>
        public static PerfViewFile Get(string filePath, PerfViewFile format = null)
        {
            var ret = TryGet(filePath, format);
            if (ret == null)
            {
                throw new ApplicationException("Could not determine data Template from the file extension for " + filePath + ".");
            }

            return ret;
        }
        /// <summary>
        /// Tries to to a 'Get' operation on filePath.   If format == null (indicating
        /// that we should try to determine the type of the file from the file suffix) and 
        /// it does not have a suffix we understand, then we return null.   
        /// </summary>
        public static PerfViewFile TryGet(string filePath, PerfViewFile format = null)
        {
            if (format == null)
            {
                // See if it is any format we recognize.  
                foreach (PerfViewFile potentalFormat in Formats)
                {
                    if (potentalFormat.IsMyFormat(filePath))
                    {
                        format = potentalFormat;
                        break;
                    };
                }
                if (format == null)
                {
                    return null;
                }
            }

            string fullPath = Path.GetFullPath(filePath);
            PerfViewFile ret;
            if (!s_internTable.TryGetValue(fullPath, out ret))
            {
                ret = (PerfViewFile)format.MemberwiseClone();
                ret.m_filePath = filePath;
                s_internTable[fullPath] = ret;
            }
            ret.Name = Path.GetFileName(ret.FilePath);
            if (ret.Name.EndsWith(".etl", StringComparison.OrdinalIgnoreCase))
            {
                var dir = Path.GetDirectoryName(ret.FilePath);
                if (dir.Length == 0)
                {
                    dir = ".";
                }

                var wildCard = ret.Name.Insert(ret.Name.Length - 4, ".*");
                if (Directory.GetFiles(dir, wildCard).Length > 0)
                {
                    ret.Name += " (unmerged)";
                }
            }
            return ret;
        }

        /// <summary>
        /// Logs the fact that the GUI should call a user defined method when a file is opened.  
        /// </summary>
        public static PerfViewFile GetTemplateForExtension(string extension)
        {
            foreach (PerfViewFile potentalFormat in Formats)
            {
                if (potentalFormat.IsMyFormat(extension))
                {
                    return potentalFormat;
                }
            }
            var ret = new PerfViewUserFile(extension + " file", new string[] { extension });
            Formats.Add(ret);
            return ret;
        }

        /// <summary>
        /// Declares that the user command 'userCommand' (that takes one string argument) 
        /// should be called when the file is opened.  
        /// </summary>
        public void OnOpenFile(string userCommand)
        {
            if (userCommands == null)
            {
                userCommands = new List<string>();
            }

            userCommands.Add(userCommand);
        }

        /// <summary>
        /// Declares that the file should have a view called 'viewName' and the user command
        /// 'userCommand' (that takes two string arguments (file, viewName)) should be called 
        /// when that view is opened 
        /// </summary>
        public void DeclareFileView(string viewName, string userCommand)
        {
            if (m_UserDeclaredChildren == null)
            {
                m_UserDeclaredChildren = new List<PerfViewReport>();
            }

            m_UserDeclaredChildren.Add(new PerfViewReport(viewName, delegate (string reportFileName, string reportViewName)
            {
                PerfViewExtensibility.Extensions.ExecuteUserCommand(userCommand, new string[] { reportFileName, reportViewName });
            }));
        }

        internal void ExecuteOnOpenCommand(StatusBar worker)
        {
            if (m_UserDeclaredChildren != null)
            {
                // The m_UserDeclaredChildren are templates.  We need to instantiate them to this file before adding them as children. 
                foreach (var userDeclaredChild in m_UserDeclaredChildren)
                {
                    m_Children.Add(new PerfViewReport(userDeclaredChild, this));
                }

                m_UserDeclaredChildren = null;
            }
            // Add the command to the list 
            if (userCommands == null)
            {
                return;
            }

            var args = new string[] { FilePath };
            foreach (string command in userCommands)
            {
                worker.LogWriter.WriteLine("Running User defined OnFileOpen command " + command);
                try
                {
                    PerfViewExtensibility.Extensions.ExecuteUserCommand(command, args);
                }
                catch (Exception e)
                {
                    worker.LogError(@"Error executing OnOpenFile command " + command + "\r\n" + e.ToString());
                }
            }
            return;
        }

        // A list of user commands to be executed when a file is opened 
        private List<string> userCommands;

        /// <summary>
        /// Retrieves the base file name from a PerfView data source's name.
        /// </summary>
        /// <example>
        /// GetBaseName(@"C:\data\foo.bar.perfView.xml.zip") == "foo.bar"
        /// </example>
        /// <param name="filePath">The path to the data source.</param>
        /// <returns>The base name, without extensions or path, of <paramref name="filePath"/>.</returns>
        public static string GetFileNameWithoutExtension(string filePath)
        {
            string fileName = Path.GetFileName(filePath);

            foreach (var fmt in Formats)
            {
                foreach (string ext in fmt.FileExtensions)
                {
                    if (fileName.EndsWith(ext))
                    {
                        return fileName.Substring(0, fileName.Length - ext.Length);
                    }
                }
            }

            return fileName;
        }
        /// <summary>
        /// Change the extension of a PerfView data source path.
        /// </summary>
        /// <param name="filePath">The path to change.</param>
        /// <param name="newExtension">The new extension to add.</param>
        /// <returns>The path to a file with the same directory and base name of <paramref name="filePath"/>, 
        /// but with extension <paramref name="newExtension"/>.</returns>
        public static string ChangeExtension(string filePath, string newExtension)
        {
            string dirName = Path.GetDirectoryName(filePath);
            string fileName = GetFileNameWithoutExtension(filePath) + newExtension;
            return Path.Combine(dirName, fileName);
        }

        public override void Open(Window parentWindow, StatusBar worker, Action doAfter = null)
        {
            if (!m_opened)
            {
                worker.StartWork("Opening " + Name, delegate ()
            {
                Action<Action> continuation = OpenImpl(parentWindow, worker);
                ExecuteOnOpenCommand(worker);

                worker.EndWork(delegate ()
                {
                    m_opened = true;
                    FirePropertyChanged("Children");

                    IsExpanded = true;
                    var defaultSource = GetStackSource();
                    if (defaultSource != null)
                    {
                        defaultSource.IsSelected = true;
                    }

                    if (continuation != null)
                    {
                        continuation(doAfter);
                    }
                    else
                    {
                        doAfter?.Invoke();
                    }
                });
            });
            }
            else
            {
                if (m_singletonStackSource != null && m_singletonStackSource.Viewer != null)
                {
                    m_singletonStackSource.Viewer.Focus();
                }
            }
        }
        public override void Close()
        {
            if (m_opened)
            {
                m_opened = false;
                s_internTable.Remove(FilePath);
            }

            if (m_Children != null)
            {
                m_Children.Clear();
                FirePropertyChanged("Children");
            }
        }

        public virtual PerfViewStackSource GetStackSource(string sourceName = null)
        {
            if (sourceName == null)
            {
                sourceName = DefaultStackSourceName;
                if (sourceName == null)
                {
                    return null;
                }
            }

            Debug.Assert(m_opened);
            if (m_Children != null)
            {
                foreach (var child in m_Children)
                {
                    var asStackSource = child as PerfViewStackSource;
                    if (asStackSource != null && asStackSource.SourceName == sourceName)
                    {
                        return asStackSource;
                    }

                    var asGroup = child as PerfViewTreeGroup;
                    if (asGroup != null && asGroup.Children != null)
                    {
                        foreach (var groupChild in asGroup.Children)
                        {
                            asStackSource = groupChild as PerfViewStackSource;
                            if (asStackSource != null && asStackSource.SourceName == sourceName)
                            {
                                return asStackSource;
                            }
                        }
                    }
                }
            }
            else if (m_singletonStackSource != null)
            {
                var asStackSource = m_singletonStackSource as PerfViewStackSource;
                if (asStackSource != null)
                {
                    return asStackSource;
                }
            }
            return null;
        }
        public virtual string DefaultStackSourceName { get { return "CPU"; } }

        /// <summary>
        /// Gets or sets the processes to be initially included in stack views. The default value is null. 
        /// The names are not case sensitive.
        /// </summary>
        /// <remarks>
        /// If this is null, a dialog will prompt the user to choose the initially included processes
        /// from a list that contains all processes from the trace.
        /// If this is NOT null, the 'Choose Process' dialog will never show and the initially included
        /// processes will be all processes with a name in InitiallyIncludedProcesses.
        /// </remarks>
        /// <example>
        /// Let's say these are the processes in the trace: devenv (104), PerfWatson2, (67) devenv (56)
        /// and InitiallyIncludedProcesses = ["devenv", "vswinexpress"].
        /// When the user opens a stack window, the included filter will be set to "^Process32% devenv (104)|^Process32% devenv (56)"
        /// </example>
        public string[] InitiallyIncludedProcesses { get; set; }
        /// <summary>
        /// If the stack sources have their first tier being the Process, then SupportsProcesses should be true.  
        /// </summary>
        public virtual bool SupportsProcesses { get { return false; } }
        /// <summary>
        /// If the source logs data from multiple processes, this gives a list
        /// of those processes.  Returning null means you don't support this.  
        /// 
        /// This can take a while.  Don't call on the GUI thread.  
        /// </summary>
        public virtual List<IProcess> GetProcesses(TextWriter log)
        {
            // This can take a while, should not be on GUI thread.  
            Debug.Assert(GuiApp.MainWindow.Dispatcher.Thread != System.Threading.Thread.CurrentThread);

            var dataSource = GetStackSource(DefaultStackSourceName);
            if (dataSource == null)
            {
                return null;
            }

            StackSource stackSource = dataSource.GetStackSource(log);

            // maps call stack indexes to callStack closest to the root.
            var rootMostFrameCache = new Dictionary<StackSourceCallStackIndex, IProcessForStackSource>();
            var processes = new List<IProcess>();

            DateTime start = DateTime.Now;
            stackSource.ForEach(delegate (StackSourceSample sample)
            {
                if (sample.StackIndex != StackSourceCallStackIndex.Invalid)
                {
                    var process = GetProcessFromStack(sample.StackIndex, stackSource, rootMostFrameCache, processes);
                    if (process != null)
                    {
                        process.CPUTimeMSec += sample.Metric;
                        long sampleTicks = start.Ticks + (long)(sample.TimeRelativeMSec * 10000);
                        if (sampleTicks < process.StartTime.Ticks)
                        {
                            process.StartTime = new DateTime(sampleTicks);
                        }

                        if (sampleTicks > process.EndTime.Ticks)
                        {
                            process.EndTime = new DateTime(sampleTicks);
                        }

                        Debug.Assert(process.EndTime >= process.StartTime);
                    }
                }
            });
            processes.Sort();
            if (processes.Count == 0)
            {
                processes = null;
            }

            return processes;
        }

        public virtual string Title
        {
            get
            {
                // Arrange the title putting most descriptive inTemplateion first.  
                var fullName = m_filePath;
                Match m = Regex.Match(fullName, @"(([^\\]+)\\)?([^\\]+)$");
                return m.Groups[3].Value + " in " + m.Groups[2].Value + " (" + fullName + ")";
            }
        }

        public SymbolReader GetSymbolReader(TextWriter log, SymbolReaderOptions symbolFlags = SymbolReaderOptions.None)
        {
            return App.GetSymbolReader(FilePath, symbolFlags);
        }
        public virtual void LookupSymbolsForModule(string simpleModuleName, TextWriter log, int processId = 0)
        {
            throw new ApplicationException("This file type does not support lazy symbol resolution.");
        }

        // Things a subclass should be overriding 
        /// <summary>
        /// The name of the file format.
        /// </summary>
        public abstract string FormatName { get; }
        /// <summary>
        /// The file extensions that this format knows how to read.  
        /// </summary>
        public abstract string[] FileExtensions { get; }
        /// <summary>
        /// Implements the open operation.   Executed NOT on the GUI thread.   Typically returns null
        /// which means the open is complete.  If some operation has to be done on the GUI thread afterward
        /// then  action(doAfter) continuation is returned.  This function is given an addition action 
        /// that must be done at the every end.   
        /// </summary>
        protected virtual Action<Action> OpenImpl(Window parentWindow, StatusBar worker)
        {
            return delegate (Action doAfter)
            {
                // By default we have a singleton source (which we don't show on the GUI) and we immediately open it
                m_singletonStackSource = new PerfViewStackSource(this, "");
                m_singletonStackSource.Open(parentWindow, worker);
                doAfter?.Invoke();
            };
        }

        protected internal virtual void ConfigureStackWindow(string stackSourceName, StackWindow stackWindow) { }
        /// <summary>
        /// Allows you to do a first action after everything is done.  
        /// </summary>
        protected internal virtual void FirstAction(StackWindow stackWindow) { }
        protected internal virtual StackSource OpenStackSourceImpl(
            string streamName, TextWriter log, double startRelativeMSec = 0, double endRelativeMSec = double.PositiveInfinity, Predicate<TraceEvent> predicate = null)
        {
            return null;
        }
        /// <summary>
        /// Simplified form, you should implement one overload or the other.  
        /// </summary>
        protected internal virtual StackSource OpenStackSourceImpl(TextWriter log) { return null; }
        protected internal virtual EventSource OpenEventSourceImpl(TextWriter log) { return null; }

        // Helper functions for ConfigStackWindowImpl (we often configure windows the same way)
        internal static void ConfigureAsMemoryWindow(string stackSourceName, StackWindow stackWindow)
        {
            bool walkableObjectView = HeapDumpPerfViewFile.Gen0WalkableObjectsViewName.Equals(stackSourceName) || HeapDumpPerfViewFile.Gen1WalkableObjectsViewName.Equals(stackSourceName);

            // stackWindow.ScalingPolicy = ScalingPolicyKind.TimeMetric;
            stackWindow.IsMemoryWindow = true;
            stackWindow.FoldPercentTextBox.Text = stackWindow.GetDefaultFoldPercentage();
            stackWindow.FoldRegExTextBox.Items.Insert(0, "[];mscorlib!String");

            var defaultExclusions = "[not reachable from roots]";
            stackWindow.ExcludeRegExTextBox.Text = defaultExclusions;
            stackWindow.ExcludeRegExTextBox.Items.Insert(0, defaultExclusions);

            stackWindow.GroupRegExTextBox.Items.Insert(0, "[group modules]           {%}!->module $1");
            stackWindow.GroupRegExTextBox.Items.Insert(0, "[group full path module entries]  {*}!=>module $1");
            stackWindow.GroupRegExTextBox.Items.Insert(0, "[group module entries]  {%}!=>module $1");

            var defaultGroup = @"[group Framework] mscorlib!=>LIB;System%!=>LIB;";
            stackWindow.GroupRegExTextBox.Items.Insert(0, defaultGroup);

            stackWindow.PriorityTextBox.Text = Graphs.MemoryGraphStackSource.DefaultPriorities;

            stackWindow.RemoveColumn("WhenColumn");
            stackWindow.RemoveColumn("WhichColumn");
            stackWindow.RemoveColumn("FirstColumn");
            stackWindow.RemoveColumn("LastColumn");
            stackWindow.RemoveColumn("IncAvgColumn");
        }
        internal static void ConfigureAsEtwStackWindow(StackWindow stackWindow, bool removeCounts = true, bool removeScenarios = true, bool removeIncAvg = true, bool windows = true)
        {
            if (removeCounts)
            {
                stackWindow.RemoveColumn("IncCountColumn");
                stackWindow.RemoveColumn("ExcCountColumn");
                stackWindow.RemoveColumn("FoldCountColumn");
            }

            if (removeScenarios)
            {
                stackWindow.RemoveColumn("WhichColumn");
            }

            if (removeIncAvg)
            {
                stackWindow.RemoveColumn("IncAvgColumn");
            }

            var defaultEntry = stackWindow.GetDefaultFoldPat();
            stackWindow.FoldRegExTextBox.Text = defaultEntry;
            stackWindow.FoldRegExTextBox.Items.Clear();
            if (!string.IsNullOrWhiteSpace(defaultEntry))
            {
                stackWindow.FoldRegExTextBox.Items.Add(defaultEntry);
            }

            if (windows && defaultEntry != "ntoskrnl!%ServiceCopyEnd")
            {
                stackWindow.FoldRegExTextBox.Items.Add("ntoskrnl!%ServiceCopyEnd");
            }

            ConfigureGroupRegExTextBox(stackWindow, windows);
        }

        internal static void ConfigureGroupRegExTextBox(StackWindow stackWindow, bool windows)
        {
            stackWindow.GroupRegExTextBox.Text = stackWindow.GetDefaultGroupPat();
            stackWindow.GroupRegExTextBox.Items.Clear();
            stackWindow.GroupRegExTextBox.Items.Add(@"[no grouping]");
            if (windows)
            {
                stackWindow.GroupRegExTextBox.Items.Add(@"[group CLR/OS entries] \Temporary ASP.NET Files\->;v4.0.30319\%!=>CLR;v2.0.50727\%!=>CLR;mscoree=>CLR;\mscorlib.*!=>LIB;\System.Xaml.*!=>WPF;\System.*!=>LIB;Presentation%=>WPF;WindowsBase%=>WPF;system32\*!=>OS;syswow64\*!=>OS;{%}!=> module $1");
            }

            stackWindow.GroupRegExTextBox.Items.Add(@"[group modules]           {%}!->module $1");
            stackWindow.GroupRegExTextBox.Items.Add(@"[group module entries]  {%}!=>module $1");
            stackWindow.GroupRegExTextBox.Items.Add(@"[group full path module entries]  {*}!=>module $1");
            stackWindow.GroupRegExTextBox.Items.Add(@"[group class entries]     {%!*}.%(=>class $1;{%!*}::=>class $1");
            stackWindow.GroupRegExTextBox.Items.Add(@"[group classes]            {%!*}.%(->class $1;{%!*}::->class $1");
            stackWindow.GroupRegExTextBox.Items.Add(@"[fold threads]            Thread -> AllThreads");
        }

        // ideally this function would not exist.  Does the open logic on the current thread (likely GUI thread)
        // public is consumed by external extensions
        public void OpenWithoutWorker()
        {
            OpenWithoutWorker(GuiApp.MainWindow, GuiApp.MainWindow.StatusBar);
        }

        internal void OpenWithoutWorker(Window parentWindow, StatusBar worker)
        {
            OpenImpl(parentWindow, worker);
        }

        // This is the global list of all known file types.  
        private static List<PerfViewFile> Formats = new List<PerfViewFile>(18)
        {
            new CSVPerfViewData(),
            new ETLPerfViewData(),
            new WTPerfViewFile(),
            new ClrProfilerCodeSizePerfViewFile(),
            new ClrProfilerAllocStacksPerfViewFile(),
            new XmlPerfViewFile(),
            new ClrProfilerHeapPerfViewFile(),
            new PdbScopePerfViewFile(),
            new VmmapPerfViewFile(),
            new DebuggerStackPerfViewFile(),
            new HeapDumpPerfViewFile(),
            new ProcessDumpPerfViewFile(),
            new ScenarioSetPerfViewFile(),
            new OffProfPerfViewFile(),
            new DiagSessionPerfViewFile(),
            new LinuxPerfViewData(),
            new XmlTreeFile(),
            new EventPipePerfViewData()
        };

        #region private
        internal void StackSourceClosing(PerfViewStackSource dataSource)
        {
            // TODO FIX NOW.   WE need reference counting 
            if (m_singletonStackSource != null)
            {
                m_opened = false;
            }
        }

        protected PerfViewFile() { }        // Don't allow public default constructor
        /// <summary>
        /// Gets the process from the stack.  It assumes that the stack frame closest to the root is the process
        /// name and returns an IProcess representing it.  
        /// </summary>
        private IProcessForStackSource GetProcessFromStack(StackSourceCallStackIndex callStack, StackSource stackSource,
            Dictionary<StackSourceCallStackIndex, IProcessForStackSource> rootMostFrameCache, List<IProcess> processes)
        {
            Debug.Assert(callStack != StackSourceCallStackIndex.Invalid);

            IProcessForStackSource ret;
            if (rootMostFrameCache.TryGetValue(callStack, out ret))
            {
                return ret;
            }

            var caller = stackSource.GetCallerIndex(callStack);
            if (caller == StackSourceCallStackIndex.Invalid)
            {
                string topCallStackStr = stackSource.GetFrameName(stackSource.GetFrameIndex(callStack), true);

                if (GetProcessForStackSourceFromTopCallStackFrame(topCallStackStr, out ret))
                {
                    processes.Add(ret);
                }
            }
            else
            {
                ret = GetProcessFromStack(caller, stackSource, rootMostFrameCache, processes);
            }

            rootMostFrameCache.Add(callStack, ret);
            return ret;
        }

        internal virtual string GetProcessIncPat(IProcess process) => $"Process% {process.Name} ({process.ProcessID})";

        /// <summary>
        /// This is and ugly routine that scrapes the data to find the full path (without the .exe extension) of the
        /// exe in the program.   It may fail (return nulls).   
        /// </summary>
        internal virtual string FindExeName(string incPat)
        {
            string procName = null;
            if (!string.IsNullOrWhiteSpace(incPat))
            {
                Match m = Regex.Match(incPat, @"^Process%\s+([^();]+[^(); ])", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    procName = m.Groups[1].Value;
                }
            }
            return procName;
        }

        internal virtual bool GetProcessForStackSourceFromTopCallStackFrame(string topCallStackStr, out IProcessForStackSource result)
        {
            Match m = Regex.Match(topCallStackStr, @"^Process\d*\s+([^()]*?)\s*(\(\s*(\d+)\s*\))?\s*$");
            if (m.Success)
            {
                var processIDStr = m.Groups[3].Value;
                var processName = m.Groups[1].Value;
                if (processName.Length == 0)
                {
                    processName = "(" + processIDStr + ")";
                }

                result = new IProcessForStackSource(processName);
                if (int.TryParse(processIDStr, out int processID))
                {
                    result.ProcessID = processID;
                }

                return true;
            }

            result = null;
            return false;
        }

        protected bool IsMyFormat(string fileName)
        {
            foreach (var extension in FileExtensions)
            {
                if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        protected bool m_opened;
        protected internal DateTime m_utcLastWriteAtOpen;

        // If we have only one stack source put it here
        protected PerfViewStackSource m_singletonStackSource;

        private static Dictionary<string, PerfViewFile> s_internTable = new Dictionary<string, PerfViewFile>();
        #endregion
    }

    // Used for new user defined file formats.  
    internal class PerfViewUserFile : PerfViewFile
    {
        public PerfViewUserFile(string formatName, string[] fileExtensions)
        {
            m_formatName = formatName;
            m_fileExtensions = fileExtensions;
        }

        public override string FormatName { get { return m_formatName; } }
        public override string[] FileExtensions { get { return m_fileExtensions; } }
        protected override Action<Action> OpenImpl(Window parentWindow, StatusBar worker) { return null; }

        #region private
        private string m_formatName;
        private string[] m_fileExtensions;
        #endregion
    }

    public class PerfViewReport : PerfViewTreeItem
    {
        // Used to create a template for all PerfViewFiles
        public PerfViewReport(string name, Action<string, string> onOpen)
        {
            Name = name;
            m_onOpen = onOpen;
        }

        // Used to clone a PerfViewReport and specialize it to a particular data file.  
        internal PerfViewReport(PerfViewReport template, PerfViewFile dataFile)
        {
            Name = template.Name;
            m_onOpen = template.m_onOpen;
            DataFile = dataFile;
        }

        #region overrides
        public virtual string Title { get { return Name + " for " + DataFile.Title; } }
        public PerfViewFile DataFile { get; private set; }
        public override string FilePath { get { return DataFile.FilePath; } }
        public override void Open(Window parentWindow, StatusBar worker, Action doAfter)
        {
            m_onOpen(DataFile.FilePath, Name);
        }
        public override void Close() { }
        public override ImageSource Icon { get { return GuiApp.MainWindow.Resources["HtmlReportBitmapImage"] as ImageSource; } }
        #endregion
        #region private
        private Action<string, string> m_onOpen;
        #endregion
    }

    /// <summary>
    /// Represents a report from an ETL file that can be viewed in a web browsers.  Subclasses need 
    /// to override OpenImpl().  
    /// </summary>
    public abstract class PerfViewHtmlReport : PerfViewTreeItem
    {
        public PerfViewHtmlReport(PerfViewFile dataFile, string name)
        {
            DataFile = dataFile;
            Name = name;
        }
        public virtual string Title { get { return Name + " for " + DataFile.Title; } }
        public PerfViewFile DataFile { get; private set; }
        public WebBrowserWindow Viewer { get; internal set; }
        public override string FilePath { get { return DataFile.FilePath; } }
        protected abstract void WriteHtmlBody(TraceLog dataFile, TextWriter writer, string fileName, TextWriter log);
        /// <summary>
        /// You can make Command:XXXX urls which come here when users click on them.   
        /// Returns an  error message (or null if it succeeds).  
        /// </summary>
        protected virtual string DoCommand(string command, StatusBar worker)
        {
            return "Unimplemented command: " + command;
        }

        protected virtual string DoCommand(string command, StatusBar worker, out Action continuation)
        {
            continuation = null;
            return DoCommand(command, worker);
        }

        protected virtual string DoCommand(Uri commandUri, StatusBar worker, out Action continuation)
        {
            return DoCommand(commandUri.LocalPath, worker, out continuation);
        }

        public override void Open(Window parentWindow, StatusBar worker, Action doAfter)
        {
            if (Viewer == null)
            {
                TraceLog trace = GetTrace(worker);

                worker.StartWork("Opening " + Name, delegate ()
                {
                    string reportFileName = GenerateReportFile(worker, trace);

                    worker.EndWork(delegate ()
                    {
                        Viewer = new WebBrowserWindow(parentWindow);
                        Viewer.WindowState = System.Windows.WindowState.Maximized;
                        Viewer.Closing += delegate (object sender, CancelEventArgs e)
                        {
                            Viewer = null;
                        };
                        Viewer.Browser.Navigating += delegate (object sender, System.Windows.Navigation.NavigatingCancelEventArgs e)
                        {
                            if (e.Uri.Scheme == "command")
                            {
                                e.Cancel = true;
                                Viewer.StatusBar.StartWork("Following Hyperlink", delegate ()
                                {
                                    Action continuation;
                                    var message = DoCommand(e.Uri, Viewer.StatusBar, out continuation);
                                    Viewer.StatusBar.EndWork(delegate ()
                                    {
                                        if (message != null)
                                        {
                                            Viewer.StatusBar.Log(message);
                                        }

                                        continuation?.Invoke();
                                    });
                                });
                            }
                        };

                        Viewer.Width = 1000;
                        Viewer.Height = 600;
                        Viewer.Title = Title;
                        WebBrowserWindow.Navigate(Viewer.Browser, reportFileName);
                        Viewer.Show();

                        doAfter?.Invoke();
                    });
                });
            }
            else
            {
                Viewer.Focus();
                doAfter?.Invoke();
            }
        }

        /// <summary>
        /// Generates an HTML report and opens it using the machine's default handler .html file paths.
        /// </summary>
        /// <param name="worker">The StatusBar that should be updated with progress.</param>
        public void OpenInExternalBrowser(StatusBar worker)
        {
            TraceLog trace = GetTrace(worker);

            worker.StartWork("Opening in external browser " + Name, delegate ()
            {
                string reportFileName = GenerateReportFile(worker, trace);

                worker.EndWork(delegate ()
                {
                    Process.Start(reportFileName);
                });
            });
        }

        public override void Close() { }
        public override ImageSource Icon { get { return GuiApp.MainWindow.Resources["HtmlReportBitmapImage"] as ImageSource; } }

        private TraceLog GetTrace(StatusBar worker)
        {
            var etlDataFile = DataFile as ETLPerfViewData;
            TraceLog trace = null;
            if (etlDataFile != null)
            {
                trace = etlDataFile.GetTraceLog(worker.LogWriter);
            }
            else
            {
                var linuxDataFile = DataFile as LinuxPerfViewData;
                if (linuxDataFile != null)
                {
                    trace = linuxDataFile.GetTraceLog(worker.LogWriter);
                }
                else
                {
                    var eventPipeDataFile = DataFile as EventPipePerfViewData;
                    if (eventPipeDataFile != null)
                    {
                        trace = eventPipeDataFile.GetTraceLog(worker.LogWriter);
                    }
                }
            }

            return trace;
        }

        private string GenerateReportFile(StatusBar worker, TraceLog trace)
        {
            var reportFileName = CacheFiles.FindFile(FilePath, "." + Name + ".html");
            using (var writer = File.CreateText(reportFileName))
            {
                writer.WriteLine("<html>");
                writer.WriteLine("<head>");
                writer.WriteLine("<title>{0}</title>", Title);
                writer.WriteLine("<meta charset=\"UTF-8\"/>");
                writer.WriteLine("<meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\"/>");

                // Add basic styling to the generated HTML
                writer.WriteLine(@"
<style>
body {
    font-family: Segoe UI Light, Helvetica, sans-serif;
}

tr:hover {
    background-color: #eeeeee;
}

th {
    background-color: #eeeeee;
    font-family: Helvetica;
    padding: 4px;
    font-size: small;
    font-weight: normal;
}

td {
    font-family: Consolas, monospace;
    font-size: small;
    padding: 3px;
    padding-bottom: 5px;
}

table {
    border-collapse: collapse;
}
</style>
");

                writer.WriteLine("</head>");
                writer.WriteLine("<body>");
                WriteHtmlBody(trace, writer, reportFileName, worker.LogWriter);
                writer.WriteLine("</body>");
                writer.WriteLine("</html>");


            }

            return reportFileName;
        }
    }

    public class PerfViewTraceInfo : PerfViewHtmlReport
    {
        public PerfViewTraceInfo(PerfViewFile dataFile) : base(dataFile, "TraceInfo") { }
        protected override void WriteHtmlBody(TraceLog dataFile, TextWriter writer, string fileName, TextWriter log)
        {
            writer.WriteLine("<H2>Information on the Trace and Machine</H2>");
            writer.WriteLine("<Table Border=\"1\">");
            writer.WriteLine("<TR><TD>Machine Name</TD><TD Align=\"Center\">{0}</TD></TR>",
                string.IsNullOrEmpty(dataFile.MachineName) ? "&nbsp;" : dataFile.MachineName);
            writer.WriteLine("<TR><TD>Operating System</TD><TD Align=\"Center\">{0}</TD></TR>",
                string.IsNullOrEmpty(dataFile.OSName) ? "&nbsp;" : dataFile.OSName);
            writer.WriteLine("<TR><TD>OS Build Number</TD><TD Align=\"Center\">{0}</TD></TR>",
                string.IsNullOrEmpty(dataFile.OSBuild) ? "&nbsp;" : dataFile.OSBuild);
            writer.WriteLine("<TR><TD Title=\"This is negative if the data was collected in a time zone west of UTC\">UTC offset where data was collected</TD><TD Align=\"Center\">{0}</TD></TR>",
                dataFile.UTCOffsetMinutes.HasValue ? (dataFile.UTCOffsetMinutes.Value / 60.0).ToString("f2") : "Unknown");
            writer.WriteLine("<TR><TD Title=\"This is negative if PerfView is running in a time zone west of UTC\">UTC offset where PerfView is running</TD><TD Align=\"Center\">{0:f2}</TD></TR>",
                TimeZoneInfo.Local.GetUtcOffset(dataFile.SessionStartTime).TotalHours);

            if (dataFile.UTCOffsetMinutes.HasValue)
            {
                writer.WriteLine("<TR><TD Title=\"This is negative if analysis is happening west of collection\">Delta of Local and Collection Time</TD><TD Align=\"Center\">{0:f2}</TD></TR>",
                    TimeZoneInfo.Local.GetUtcOffset(dataFile.SessionStartTime).TotalHours - (dataFile.UTCOffsetMinutes.Value / 60.0));
            }
            writer.WriteLine("<TR><TD>OS Boot Time</TD><TD Align=\"Center\">{0:MM/dd/yyyy HH:mm:ss.fff}</TD></TR>", dataFile.BootTime);
            writer.WriteLine("<TR><TD>Trace Start Time</TD><TD Align=\"Center\">{0:MM/dd/yyyy HH:mm:ss.fff}</TD></TR>", dataFile.SessionStartTime);
            writer.WriteLine("<TR><TD>Trace End Time</TD><TD Align=\"Center\">{0:MM/dd/yyyy HH:mm:ss.fff}</TD></TR>", dataFile.SessionEndTime);
            writer.WriteLine("<TR><TD>Trace Duration (Sec)</TD><TD Align=\"Center\">{0:n1}</TD></TR>", dataFile.SessionDuration.TotalSeconds);
            writer.WriteLine("<TR><TD>CPU Frequency (MHz)</TD><TD Align=\"Center\">{0:n0}</TD></TR>", dataFile.CpuSpeedMHz);
            writer.WriteLine("<TR><TD>Number Of Processors</TD><TD Align=\"Center\">{0}</TD></TR>", dataFile.NumberOfProcessors);
            writer.WriteLine("<TR><TD>Memory Size (MB)</TD><TD Align=\"Center\">{0:n0}</TD></TR>", dataFile.MemorySizeMeg);
            writer.WriteLine("<TR><TD>Pointer Size</TD><TD Align=\"Center\">{0}</TD></TR>", dataFile.PointerSize);
            writer.WriteLine("<TR><TD>Sample Profile Interval (MSec) </TD><TD Align=\"Center\">{0:n2}</TD></TR>", dataFile.SampleProfileInterval.TotalMilliseconds);
            writer.WriteLine("<TR><TD>Total Events</TD><TD Align=\"Center\">{0:n0}</TD></TR>", dataFile.EventCount);
            writer.WriteLine("<TR><TD>Lost Events</TD><TD Align=\"Center\">{0:n0}</TD></TR>", dataFile.EventsLost);

            double len = 0;
            try
            {
                len = new System.IO.FileInfo(DataFile.FilePath).Length / 1000000.0;
            }
            catch (Exception) { }
            if (len > 0)
            {
                writer.WriteLine("<TR><TD>ETL File Size (MB)</TD><TD Align=\"Center\">{0:n1}</TD></TR>", len);
            }

            string logPath = null;

            Debug.Assert(fileName.EndsWith("TraceInfo.html"));
            if (fileName.EndsWith(".TraceInfo.html"))
            {
                logPath = fileName.Substring(0, fileName.Length - 15) + ".LogFile.txt";
            }

            if (logPath != null && File.Exists(logPath))
            {
                writer.WriteLine("<TR><TD colspan=\"2\" Align=\"Center\"> <A HREF=\"command:displayLog:{0}\">View data collection log file</A></TD></TR>", logPath);
            }
            else
            {
                writer.WriteLine("<TR><TD colspan=\"2\" Align=\"Center\"> No data collection log file found</A></TD></TR>");
            }

            writer.WriteLine("</Table>");
        }

        protected override string DoCommand(string command, StatusBar worker)
        {

            if (command.StartsWith("displayLog:"))
            {
                string logFile = command.Substring(command.IndexOf(':') + 1);
                worker.Parent.Dispatcher.BeginInvoke((Action)delegate ()
                {
                    var logTextWindow = new Controls.TextEditorWindow(GuiApp.MainWindow);
                    logTextWindow.TextEditor.OpenText(logFile);
                    logTextWindow.TextEditor.IsReadOnly = true;
                    logTextWindow.Title = "Collection time log";
                    logTextWindow.HideOnClose = true;
                    logTextWindow.Show();
                    logTextWindow.TextEditor.Body.ScrollToEnd();
                });

                return "Displaying Log";
            }
            return "Unknown command " + command;
        }
    }
    /// <summary>
    /// Used to Display Processes Summary 
    /// </summary>
    public class PerfViewProcesses : PerfViewHtmlReport
    {

        public PerfViewProcesses(PerfViewFile dataFile) : base(dataFile, "Processes") { }
        protected override void WriteHtmlBody(TraceLog dataFile, TextWriter writer, string fileName, TextWriter log)
        {
            m_processes = new List<TraceProcess>(dataFile.Processes);
            // Sort by CPU time (largest first), then by start time (latest first)
            m_processes.Sort(delegate (TraceProcess x, TraceProcess y)
            {
                var ret = y.CPUMSec.CompareTo(x.CPUMSec);
                if (ret != 0)
                {
                    return ret;
                }

                return y.StartTimeRelativeMsec.CompareTo(x.StartTimeRelativeMsec);
            });

            var shortProcs = new List<TraceProcess>();
            var longProcs = new List<TraceProcess>();
            foreach (var process in m_processes)
            {
                if (process.ProcessID < 0)
                {
                    continue;
                }

                if (process.StartTimeRelativeMsec == 0 &&
                    process.EndTimeRelativeMsec == dataFile.SessionEndTimeRelativeMSec)
                {
                    longProcs.Add(process);
                }
                else
                {
                    shortProcs.Add(process);
                }
            }

            writer.WriteLine("<H2>Process Summary</H2>");

            writer.WriteLine("<UL>");
            writer.WriteLine("<LI> <A HREF=\"command:processes\">View Process Data in Excel</A></LI>");
            writer.WriteLine("<LI> <A HREF=\"command:module\">View Process Modules in Excel</A></LI>");
            writer.WriteLine("</UL>");

            if (shortProcs.Count > 0)
            {
                writer.WriteLine("<H3>Processes that did <strong>not</strong> live for the entire trace.</H3>");
                WriteProcTable(writer, shortProcs, true);
            }
            if (longProcs.Count > 0)
            {
                writer.WriteLine("<H3>Processes that <strong>did</strong> live for the entire trace.</H3>");
                WriteProcTable(writer, longProcs, false);
            }
        }
        /// <summary>
        /// Takes in either "processes" or "module" which will make a csv of their respective format
        /// </summary>
        protected override string DoCommand(string command, StatusBar worker)
        {
            if (command == "processes")
            {
                var csvFile = CacheFiles.FindFile(FilePath, ".processesSummary.csv");
                if (!File.Exists(csvFile) || File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(FilePath) ||
                    File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(SupportFiles.MainAssemblyPath))
                {
                    MakeProcessesCsv(m_processes, csvFile);
                }
                Command.Run(Command.Quote(csvFile), new CommandOptions().AddStart().AddTimeout(CommandOptions.Infinite));
                System.Threading.Thread.Sleep(500);     // Give it time to start a bit.  
                return "Opening CSV " + csvFile;
            }
            else if (command == "module")
            {
                var csvFile = CacheFiles.FindFile(FilePath, ".processesModule.csv");
                if (!File.Exists(csvFile) || File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(FilePath) ||
                    File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(SupportFiles.MainAssemblyPath))
                {
                    MakeModuleCsv(m_processes, csvFile);
                }
                Command.Run(Command.Quote(csvFile), new CommandOptions().AddStart().AddTimeout(CommandOptions.Infinite));
                System.Threading.Thread.Sleep(500);     // Give it time to start a bit.  
                return "Opening CSV " + csvFile;
            }
            return null;
        }
        #region private
        private void WriteProcTable(TextWriter writer, List<TraceProcess> processes, bool showExit)
        {
            bool showBitness = false;
            if (processes.Count > 0)
            {
                showBitness = (processes[0].Log.PointerSize == 8);
            }

            writer.WriteLine("<Table Border=\"1\">");
            writer.Write("<TR>");
            writer.Write("<TH Align=\"Center\">Name</TH>");
            writer.Write("<TH Align=\"Center\">ID</TH>");
            writer.Write("<TH Align=\"Center\">Parent<BR/>ID</TH>");
            if (showBitness)
            {
                writer.Write("<TH Align=\"Center\">Bitness</TH>");
            }

            writer.Write("<TH Align=\"Center\" Title=\"The amount of CPU time used (on any processor).\" >CPU<BR/>MSec</TH>");
            writer.Write("<TH Align=\"Center\" Title=\"The CPU used divided by the duration.\">Ave Procs<BR/>Used</TH>");
            if (showExit)
            {
                writer.Write("<TH Align=\"Center\">Duration<BR/>MSec</TH>");
                writer.Write("<TH Align=\"Center\" Title=\"The start time in milliseconds from the time the trace started.\">Start<BR/>MSec</TH>");
                writer.Write("<TH Align=\"Center\" Title=\"The integer that the process returned when it exited.\">Exit<BR/>Code</TH>");
            }
            writer.Write("<TH Align=\"Center\">Command Line</TH>");
            writer.WriteLine("</TR>");
            foreach (TraceProcess process in processes)
            {
                writer.Write("<TR>");
                writer.Write("<TD Align=\"Left\">{0}</TD>", process.Name);
                writer.Write("<TD Align=\"Right\">{0}</TD>", process.ProcessID);
                writer.Write("<TD Align=\"Right\">{0}</TD>", process.ParentID);
                if (showBitness)
                {
                    writer.Write("<TD Align=\"Center\">{0}</TD>", process.Is64Bit ? 64 : 32);
                }

                writer.Write("<TD Align=\"Right\">{0:n0}</TD>", process.CPUMSec);
                writer.Write("<TD Align=\"Right\">{0:n3}</TD>", process.CPUMSec / (process.EndTimeRelativeMsec - process.StartTimeRelativeMsec));
                if (showExit)
                {
                    writer.Write("<TD Align=\"Right\">{0:n3}</TD>", process.EndTimeRelativeMsec - process.StartTimeRelativeMsec);
                    writer.Write("<TD Align=\"Right\">{0:n3}</TD>", process.StartTimeRelativeMsec);
                    writer.Write("<TD Align=\"Right\">{0}</TD>", process.ExitStatus.HasValue ? "0x" + process.ExitStatus.Value.ToString("x") : "?");
                }
                writer.Write("<TD Align=\"Left\">{0}</TD>", process.CommandLine);
                writer.WriteLine("</TR>");
            }
            writer.WriteLine("</Table>");
        }

        /// <summary>
        /// Makes a csv file of the contents or processes at the filepath. 
        /// Headers to csv are  Name,ID,Parent_ID,Bitness,CPUMsec,AveProcsUsed,DurationMSec,StartMSec,ExitCode,CommandLine
        /// </summary>
        private void MakeProcessesCsv(List<TraceProcess> processes, string filepath)
        {
            using (var writer = File.CreateText(filepath))
            {
                //add headers 
                string listSeparator = Thread.CurrentThread.CurrentCulture.TextInfo.ListSeparator;
                writer.WriteLine("Name{0}ID{0}Parent_ID{0}Bitness{0}CPUMsec{0}AveProcsUsed{0}DurationMSec{0}StartMSec{0}ExitCode{0}CommandLine", listSeparator);
                foreach (TraceProcess process in processes)
                {
                    writer.Write("{0}{1}", process.Name, listSeparator);
                    writer.Write("{0}{1}", process.ProcessID, listSeparator);
                    writer.Write("{0}{1}", process.ParentID, listSeparator);
                    writer.Write("{0}{1}", process.Is64Bit ? 64 : 32, listSeparator);
                    writer.Write("{0:f0}{1}", process.CPUMSec, listSeparator);
                    writer.Write("{0:f3}{1}", process.CPUMSec / (process.EndTimeRelativeMsec - process.StartTimeRelativeMsec), listSeparator);
                    writer.Write("{0:f3}{1}", process.EndTimeRelativeMsec - process.StartTimeRelativeMsec, listSeparator);
                    writer.Write("{0:f3}{1}", process.StartTimeRelativeMsec, listSeparator);
                    writer.Write("{0}{1}", process.ExitStatus.HasValue ? "0x" + process.ExitStatus.Value.ToString("x") : "?", listSeparator);
                    writer.Write("{0}", PerfViewExtensibility.Events.EscapeForCsv(process.CommandLine, listSeparator));
                    writer.WriteLine("");
                }
            }
        }
        /// <summary>
        /// Makes a Csv at filepath
        /// </summary>
        private void MakeModuleCsv(List<TraceProcess> processes, string filepath)
        {
            using (var writer = File.CreateText(filepath))
            {
                //add headers 
                string listSeparator = Thread.CurrentThread.CurrentCulture.TextInfo.ListSeparator;
                writer.WriteLine("ProcessName{0}ProcessID{0}Name{0}FileVersion{0}Commit{0}BuildTime{0}FilePath{0}", listSeparator);
                foreach (TraceProcess process in processes)  //turn into private function 
                {
                    foreach (TraceLoadedModule module in process.LoadedModules)
                    {
                        writer.Write("{0}{1}", process.Name, listSeparator);
                        writer.Write("{0}{1}", process.ProcessID, listSeparator);
                        writer.Write("{0}{1}", module.ModuleFile.Name, listSeparator);
                        writer.Write("{0}{1}", module.ModuleFile.FileVersion, listSeparator);
                        writer.Write("{0}{1}", module.ModuleFile.GitCommitHash, listSeparator);
                        writer.Write("{0}{1}", PerfViewExtensibility.Events.EscapeForCsv(module.ModuleFile.BuildTime.ToString(), listSeparator), listSeparator);
                        writer.Write("{0}", module.ModuleFile.FilePath);
                        writer.WriteLine();
                    }
                }
            }
        }

        /// <summary>
        /// All the processes in this view.  
        /// </summary>
        private List<TraceProcess> m_processes;
        #endregion
    }

    public class AutomatedAnalysisReport : PerfViewHtmlReport
    {
        public AutomatedAnalysisReport(PerfViewFile dataFile) : base(dataFile, "Automatic CPU Analysis") { }

        protected override void WriteHtmlBody(TraceLog dataFile, TextWriter writer, string fileName, TextWriter log)
        {
            AutomatedAnalysisManager manager = new AutomatedAnalysisManager();
            AutomatedAnalysisTraceLog traceLog = new AutomatedAnalysisTraceLog(dataFile, App.GetSymbolReader(dataFile.FilePath));
            AutomatedAnalysisResult result = manager.ProcessTrace(traceLog, log);
            result.GenerateReport(writer);
        }
    }

    public class PerfViewIisStats : PerfViewHtmlReport
    {
        private Dictionary<Guid, IisRequest> m_Requests = new Dictionary<Guid, IisRequest>();
        private List<ExceptionDetails> allExceptions = new List<ExceptionDetails>();

        public PerfViewIisStats(PerfViewFile dataFile) : base(dataFile, "IIS Stats") { }

        protected override void WriteHtmlBody(TraceLog dataFile, TextWriter writer, string fileName, TextWriter log)
        {
            writer.WriteLine("<H2>IIS Request Statistics</H2>");

            var dispatcher = dataFile.Events.GetSource();

            dispatcher.Dynamic.AddCallbackForProviderEvent("Microsoft-Windows-ASPNET", "Request/Send", delegate (TraceEvent data)
            {
                IisRequest request;
                if (m_Requests.TryGetValue(data.ActivityID, out request))
                {
                    if (data.RelatedActivityID != Guid.Empty)
                    {
                        request.RelatedActivityId = data.RelatedActivityID;
                    }
                }
            });

            Dictionary<Guid, int> childRequests = new Dictionary<Guid, int>();

            var iis = new IisTraceEventParser(dispatcher);

            int startcount = 0;
            int endcount = 0;

            iis.IISGeneralGeneralChildRequestStart += delegate (W3GeneralChildRequestStart traceEvent)
            {
                int childRequestRecurseLevel = 0;
                if (childRequests.ContainsKey(traceEvent.ContextId))
                {
                    if (childRequests.TryGetValue(traceEvent.ContextId, out childRequestRecurseLevel))
                    {
                        childRequests[traceEvent.ContextId] = childRequestRecurseLevel + 1;
                    }
                }
                else
                {
                    childRequests.Add(traceEvent.ContextId, 1);
                }

            };

            iis.IISGeneralGeneralChildRequestEnd += delegate (W3GeneralChildRequestEnd traceEvent)
            {
                int childRequestRecurseLevel = 0;
                if (childRequests.ContainsKey(traceEvent.ContextId))
                {
                    if (childRequests.TryGetValue(traceEvent.ContextId, out childRequestRecurseLevel))
                    {
                        childRequests[traceEvent.ContextId] = childRequestRecurseLevel - 1;
                    }
                }
            };

            iis.IISGeneralGeneralRequestStart += delegate (W3GeneralStartNewRequest request)
            {

                IisRequest req = new IisRequest();
                req.ContextId = request.ContextId;
                req.StartTimeRelativeMSec = request.TimeStampRelativeMSec;
                req.Method = request.RequestVerb;
                req.Path = request.RequestURL;

                // This check is required for requests which have child
                // request events in them. For those, the StartNewRequest 
                // would be called twice for the same request. At this 
                // point, I don't think that is causing any problems to us
                if (!m_Requests.ContainsKey(request.ContextId))
                {
                    m_Requests.Add(request.ContextId, req);
                }

                startcount++;


            };

            iis.IISGeneralGeneralRequestEnd += delegate (W3GeneralEndNewRequest req)
            {
                IisRequest request;
                if (m_Requests.TryGetValue(req.ContextId, out request))
                {
                    request.EndTimeRelativeMSec = req.TimeStampRelativeMSec;
                    request.BytesReceived = req.BytesReceived;
                    request.BytesSent = req.BytesSent;
                    request.StatusCode = req.HttpStatus;
                    request.SubStatusCode = req.HttpSubStatus;

                }

                endcount++;
            };


            iis.IISRequestNotificationPreBeginRequestStart += delegate (IISRequestNotificationPreBeginStart preBeginEvent)
            {
                IisRequest request;
                if (!m_Requests.TryGetValue(preBeginEvent.ContextId, out request))
                {
                    // so this is the case where we dont have a GENERAL_REQUEST_START 
                    // event but we got a MODULE\START Event fired for this request 
                    // so we do our best to create a FAKE start request event
                    // populating as much information as we can as this is one of 
                    // those requests which could have started before the trace was started
                    request = GenerateFakeIISRequest(preBeginEvent.ContextId, preBeginEvent);
                    m_Requests.Add(preBeginEvent.ContextId, request);
                }
                int childRequestRecurseLevel = GetChildEventRecurseLevel(preBeginEvent.ContextId, childRequests);

                var iisPrebeginModuleEvent = new IisPrebeginModuleEvent();
                iisPrebeginModuleEvent.Name = preBeginEvent.ModuleName;
                iisPrebeginModuleEvent.StartTimeRelativeMSec = preBeginEvent.TimeStampRelativeMSec;
                iisPrebeginModuleEvent.ProcessId = preBeginEvent.ProcessID;
                iisPrebeginModuleEvent.StartThreadId = preBeginEvent.ThreadID;
                iisPrebeginModuleEvent.ChildRequestRecurseLevel = childRequestRecurseLevel;
                request.PipelineEvents.Add(iisPrebeginModuleEvent);

            };

            iis.IISRequestNotificationPreBeginRequestEnd += delegate (IISRequestNotificationPreBeginEnd preBeginEvent)
            {
                IisRequest request;
                int childRequestRecurseLevel = GetChildEventRecurseLevel(preBeginEvent.ContextId, childRequests);
                if (m_Requests.TryGetValue(preBeginEvent.ContextId, out request))
                {
                    var module = request.PipelineEvents.FirstOrDefault(m => m.Name == preBeginEvent.ModuleName && m.ChildRequestRecurseLevel == childRequestRecurseLevel);

                    if (module != null)
                    {
                        module.EndTimeRelativeMSec = preBeginEvent.TimeStampRelativeMSec;
                        module.EndThreadId = preBeginEvent.ThreadID;
                    }
                }
                // so this is the case where we dont have a GENERAL_REQUEST_START 
                // event as well as Module Start event for the request but we got 
                // a Module End Event fired for this request. Assuming this happens, 
                // the worst we will miss is delay between this module end event
                // to the next module start event and that should ideally be very
                // less. Hence we don't need the else part for this condition
                //else { }  
            };

            iis.IISRequestNotificationNotifyModuleStart += delegate (IISRequestNotificationEventsStart moduleEvent)
            {
                IisRequest request;
                if (!m_Requests.TryGetValue(moduleEvent.ContextId, out request))
                {
                    // so this is the case where we dont have a GENERAL_REQUEST_START 
                    // event but we got a MODULE\START Event fired for this request 
                    // so we do our best to create a FAKE start request event
                    // populating as much information as we can as this is one of 
                    // those requests which could have started before the trace was started
                    request = GenerateFakeIISRequest(moduleEvent.ContextId, moduleEvent);
                    m_Requests.Add(moduleEvent.ContextId, request);
                }

                int childRequestRecurseLevel = GetChildEventRecurseLevel(moduleEvent.ContextId, childRequests);
                var iisModuleEvent = new IisModuleEvent();
                iisModuleEvent.Name = moduleEvent.ModuleName;
                iisModuleEvent.StartTimeRelativeMSec = moduleEvent.TimeStampRelativeMSec;
                iisModuleEvent.ProcessId = moduleEvent.ProcessID;
                iisModuleEvent.StartThreadId = moduleEvent.ThreadID;
                iisModuleEvent.fIsPostNotification = moduleEvent.fIsPostNotification;
                iisModuleEvent.Notification = (RequestNotification)moduleEvent.Notification;
                iisModuleEvent.ChildRequestRecurseLevel = childRequestRecurseLevel;
                iisModuleEvent.foundEndEvent = false;
                request.PipelineEvents.Add(iisModuleEvent);
            };

            iis.IISRequestNotificationNotifyModuleEnd += delegate (IISRequestNotificationEventsEnd moduleEvent)
            {
                IisRequest request;
                int childRequestRecurseLevel = GetChildEventRecurseLevel(moduleEvent.ContextId, childRequests);
                if (m_Requests.TryGetValue(moduleEvent.ContextId, out request))
                {
                    IEnumerable<IisModuleEvent> iisModuleEvents = request.PipelineEvents.OfType<IisModuleEvent>();
                    var module = iisModuleEvents.FirstOrDefault(m => m.Name == moduleEvent.ModuleName && m.Notification == (RequestNotification)moduleEvent.Notification && m.fIsPostNotification == moduleEvent.fIsPostNotificationEvent && m.ChildRequestRecurseLevel == childRequestRecurseLevel && m.foundEndEvent == false);
                    if (module != null)
                    {
                        module.EndTimeRelativeMSec = moduleEvent.TimeStampRelativeMSec;
                        module.EndThreadId = moduleEvent.ThreadID;
                        module.foundEndEvent = true;
                    }
                }

                // so this is the case where we dont have a GENERAL_REQUEST_START event as well 
                // as Module Start event for the request but we got a Module End Event fired for 
                // this request. Assuming this happens, the worst we will miss is delay between 
                // this module end event to the next module start event and that should ideally be 
                // less. Hence we don't need the else part for this condition

            };

            iis.IISRequestNotificationModuleSetResponseErrorStatus += delegate (IISRequestNotificationEventsResponseErrorStatus responseErrorStatusNotification)
            {
                IisRequest request;
                if (m_Requests.TryGetValue(responseErrorStatusNotification.ContextId, out request))
                {
                    request.FailureDetails = new RequestFailureDetails();
                    request.FailureDetails.HttpReason = responseErrorStatusNotification.HttpReason;
                    request.FailureDetails.HttpStatus = responseErrorStatusNotification.HttpStatus;
                    request.FailureDetails.HttpSubStatus = responseErrorStatusNotification.HttpSubStatus;
                    request.FailureDetails.ModuleName = responseErrorStatusNotification.ModuleName;
                    request.FailureDetails.ErrorCode = responseErrorStatusNotification.ErrorCode;
                    request.FailureDetails.ConfigExceptionInfo = responseErrorStatusNotification.ConfigExceptionInfo;
                    request.FailureDetails.Notification = (RequestNotification)responseErrorStatusNotification.Notification;
                    request.FailureDetails.TimeStampRelativeMSec = responseErrorStatusNotification.TimeStampRelativeMSec;
                }
            };

            iis.IISGeneralGeneralFlushResponseStart += delegate (W3GeneralFlushResponseStart traceEvent)
            {
                AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };

            iis.IISGeneralGeneralFlushResponseEnd += delegate (W3GeneralFlushResponseEnd traceEvent)
            {
                AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };

            iis.IISGeneralGeneralReadEntityStart += delegate (W3GeneralReadEntityStart traceEvent)
            {
                AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };

            iis.IISGeneralGeneralReadEntityEnd += delegate (W3GeneralReadEntityEnd traceEvent)
            {
                AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };

            iis.IISCacheFileCacheAccessStart += delegate (W3CacheFileCacheAccessStart traceEvent)
            {
                AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };

            iis.IISCacheFileCacheAccessEnd += delegate (W3CacheFileCacheAccessEnd traceEvent)
            {
                AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };
            iis.IISCacheUrlCacheAccessStart += delegate (W3CacheURLCacheAccessStart traceEvent)
            {
                AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };

            iis.IISCacheUrlCacheAccessEnd += delegate (W3CacheURLCacheAccessEnd traceEvent)
            {
                AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };

            iis.IISFilterFilterStart += delegate (W3FilterStart traceEvent)
            {
                AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };
            iis.IISFilterFilterEnd += delegate (W3FilterEnd traceEvent)
            {
                AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };
            iis.IISAuthenticationAuthStart += delegate (W3AuthStart traceEvent)
            {
                AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };
            iis.IISAuthenticationAuthEnd += delegate (W3AuthEnd traceEvent)
            {
                AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };
            iis.IISCacheOutputCacheLookupStart += delegate (W3OutputCacheLookupStart traceEvent)
            {
                AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };
            iis.IISCacheOutputCacheLookupEnd += delegate (W3OutputCacheLookupEnd traceEvent)
            {
                AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };
            iis.IISCompressionDynamicCompressionStart += delegate (W3DynamicCompressionStart traceEvent)
            {
                AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };
            iis.IISCompressionDynamicCompressionEnd += delegate (W3DynamicCompressionEnd traceEvent)
            {
                AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };
            iis.IISCompressionStaticCompressionStart += delegate (W3StaticCompressionStart traceEvent)
            {
                AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };
            iis.IISCompressionStaticCompressionEnd += delegate (W3StaticCompressionEnd traceEvent)
            {
                AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };
            iis.IISFilterFilterPreprocHeadersStart += delegate (W3FilterPreprocStart traceEvent)
            {
                AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };
            iis.IISFilterFilterPreprocHeadersEnd += delegate (W3FilterPreprocEnd traceEvent)
            {
                AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, childRequests);
            };

            var aspNet = new AspNetTraceEventParser(dispatcher);

            // The logic used here is that delays between "AspNetTrace/AspNetReq/Start" and "AspNetTrace/AspNetReq/AppDomainEnter"
            // will be due to the delay introduced due to the CLR threadpool code based on how
            // ASP.NET code emits these events.
            aspNet.AspNetReqStart += delegate (AspNetStartTraceData traceEvent)
            {
                IisRequest iisRequest;
                if (m_Requests.TryGetValue(traceEvent.ContextId, out iisRequest))
                {
                    AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, childRequests, "CLRThreadPoolQueue");
                }
            };
            aspNet.AspNetReqAppDomainEnter += delegate (AspNetAppDomainEnterTraceData traceEvent)
            {
                IisRequest iisRequest;
                if (m_Requests.TryGetValue(traceEvent.ContextId, out iisRequest))
                {
                    AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, childRequests, "CLRThreadPoolQueue");
                }
            };

            aspNet.AspNetReqSessionDataBegin += delegate (AspNetAcquireSessionBeginTraceData traceEvent)
            {
                IisRequest iisRequest;
                if (m_Requests.TryGetValue(traceEvent.ContextId, out iisRequest))
                {
                    AddGenericStartEventToRequest(traceEvent.ContextId, traceEvent, childRequests, "AspNetReqSessionData");
                }
            };
            aspNet.AspNetReqSessionDataEnd += delegate (AspNetAcquireSessionEndTraceData traceEvent)
            {
                IisRequest iisRequest;
                if (m_Requests.TryGetValue(traceEvent.ContextId, out iisRequest))
                {
                    AddGenericStopEventToRequest(traceEvent.ContextId, traceEvent, childRequests, "AspNetReqSessionData");
                }
            };

            aspNet.AspNetReqPipelineModuleEnter += delegate (AspNetPipelineModuleEnterTraceData traceEvent)
            {
                IisRequest request;
                if (m_Requests.TryGetValue(traceEvent.ContextId, out request))
                {

                    var aspnetPipelineModuleEvent = new AspNetPipelineModuleEvent()
                    {
                        Name = traceEvent.ModuleName,
                        ModuleName = traceEvent.ModuleName,
                        StartTimeRelativeMSec = traceEvent.TimeStampRelativeMSec,
                        ProcessId = traceEvent.ProcessID,
                        StartThreadId = traceEvent.ThreadID,
                    };
                    request.PipelineEvents.Add(aspnetPipelineModuleEvent);
                }

            };

            aspNet.AspNetReqPipelineModuleLeave += delegate (AspNetPipelineModuleLeaveTraceData traceEvent)
            {
                IisRequest request;
                if (m_Requests.TryGetValue(traceEvent.ContextId, out request))
                {
                    IEnumerable<AspNetPipelineModuleEvent> aspnetPipelineModuleEvents = request.PipelineEvents.OfType<AspNetPipelineModuleEvent>();
                    var module = aspnetPipelineModuleEvents.FirstOrDefault(m => m.ModuleName == traceEvent.ModuleName && m.foundEndEvent == false);
                    if (module != null)
                    {
                        module.EndTimeRelativeMSec = traceEvent.TimeStampRelativeMSec;
                        module.EndThreadId = traceEvent.ThreadID;
                        module.foundEndEvent = true;
                    }
                }
            };
            // Lets look at the rest of Enter/Leave events in AspNetReq now.

            aspNet.AddCallbackForEvents(name => name.EndsWith("Enter"), null, (TraceEvent traceEvent) =>
            {

                // We are using AspNetReqAppDomainEnter to compute for ClrThreadPool so exclude that for now
                if (!traceEvent.OpcodeName.EndsWith("AppDomainEnter") && !traceEvent.OpcodeName.EndsWith("PipelineModuleEnter"))
                {
                    object contextObj = traceEvent.PayloadByName("ContextId");
                    if (contextObj != null && contextObj.GetType() == typeof(Guid))
                    {
                        Guid contextGuid = (Guid)contextObj;

                        IisRequest iisRequest;
                        if (m_Requests.TryGetValue(contextGuid, out iisRequest))
                        {
                            AddGenericStartEventToRequest(contextGuid, traceEvent, childRequests);
                        }

                    }
                }
            });

            aspNet.AddCallbackForEvents(name => name.EndsWith("Leave"), null, (TraceEvent traceEvent) =>
            {
                if (!traceEvent.OpcodeName.EndsWith("PipelineModuleLeave"))
                {
                    object contextObj = traceEvent.PayloadByName("ContextId");
                    if (contextObj != null && contextObj.GetType() == typeof(Guid))
                    {
                        Guid contextGuid = (Guid)contextObj;

                        IisRequest iisRequest;
                        if (m_Requests.TryGetValue(contextGuid, out iisRequest))
                        {
                            AddGenericStopEventToRequest(contextGuid, traceEvent, childRequests);
                        }
                    }
                }
            });

            var clr = new ClrTraceEventParser(dispatcher);

            clr.ExceptionStart += delegate (ExceptionTraceData data)
            {
                ExceptionDetails ex = new ExceptionDetails();
                ex.ExceptionMessage = data.ExceptionMessage;
                ex.ExceptionType = data.ExceptionType;
                ex.ThreadId = data.ThreadID;
                ex.ProcessId = data.ProcessID;
                ex.TimeStampRelativeMSec = data.TimeStampRelativeMSec;
                allExceptions.Add(ex);
            };

            dispatcher.Process();

            // manual fixup for incomplete requests
            foreach (var request in m_Requests.Values.Where(x => x.EndTimeRelativeMSec == 0))
            {
                // so these are all the requests for which we see a GENERAL_REQUEST_START and no GENERAL_REQUEST_END
                // for these it is safe to set the request.EndTimeRelativeMSec to the last timestamp in the trace
                // because that is pretty much the duration that the request is active for.

                request.EndTimeRelativeMSec = dataFile.SessionEndTimeRelativeMSec;

                // Also, for this request, lets first try to find a pipeline start event which does not have a pipeline                
                // stop event next to it. If we find, we just set the EndTimeRelativeMSec to the end of the trace
                var incompletePipeLineEvents = request.PipelineEvents.Where(m => m.EndTimeRelativeMSec == 0);

                if (incompletePipeLineEvents.Count() >= 1)
                {
                    foreach (var incompleteEvent in incompletePipeLineEvents)
                    {
                        incompleteEvent.EndTimeRelativeMSec = dataFile.SessionEndTimeRelativeMSec;
                    }
                    // not setting incompleteEvent.EndThreadId as this is incorrectly adding a hyperlink for requests
                    // requests that are stuck in the session state module
                }

            }

            writer.WriteLine("<UL>");
            var fileInfo = new System.IO.FileInfo(dataFile.FilePath);
            writer.WriteLine("<LI> Total Requests: {0:n} </LI>", m_Requests.Count);
            writer.WriteLine("<LI> Trace Duration (Sec): {0:n1} </LI>", dataFile.SessionDuration.TotalSeconds);
            writer.WriteLine("<LI> RPS (Requests/Sec): {0:n2} </LI>", m_Requests.Count / dataFile.SessionDuration.TotalSeconds);
            writer.WriteLine("<LI> Number of CPUs: {0}</LI>", dataFile.NumberOfProcessors);
            writer.WriteLine("<LI> Successful Requests: {0}</LI>", m_Requests.Values.Count(x => x.StatusCode < 400));
            writer.WriteLine("<LI> Failed Requests: {0}</LI>", m_Requests.Values.Count(x => x.StatusCode >= 400));
            writer.WriteLine("</UL>");

            try
            {
                writer.WriteLine("<H3>HTTP Statistics Per Request URL</H3>");

                writer.WriteLine("<Table Border=\"1\">");
                writer.Write("<TR>");
                writer.Write("<TH Align='Center' Title='This is the Request URL. '>Path</TH>");
                writer.Write("<TH Align='Center' Title='All requests for this URL'>Total</TH>");
                writer.Write("<TH Align='Center' Title='The number of requests that finished with a Success HTTP Status Code'>HTTP Status (200-206)</TH>");
                writer.Write("<TH Align='Center' Title='The number of requests that finished with a Redirect HTTP Status Code'>HTTP Status (301-307)</TH>");
                writer.Write("<TH Align='Center' Title='The number of requests that failed with a HTTP Client error'>HTTP Status (400-412)</TH>");
                writer.Write("<TH Align='Center' Title='The number of requests that failed with a HTTP server error'>HTTP Status (>=500)</TH>");
                writer.WriteLine("</TR>");

                var httpStatisticsPerUrl = m_Requests.Values.GroupBy(n => n.Path.Split('?')[0]).Select(c => new { Path = c.Key, Total = c.Count(), Successful = c.Count(s => s.StatusCode <= 206), Redirect = c.Count(s => s.StatusCode >= 301 && s.StatusCode <= 307), ClientError = c.Count(s => s.StatusCode >= 400 && s.StatusCode <= 412), ServerError = c.Count(s => s.StatusCode >= 500) });

                // sort this list by the the maximum number of requests
                foreach (var item in httpStatisticsPerUrl.OrderByDescending(x => x.Total))
                {
                    writer.WriteLine("<TR>");
                    writer.Write($"<TD>{item.Path}</TD><TD Align='Center'>{item.Total}</TD><TD Align='Center'>{item.Successful}</TD><TD Align='Center'>{item.Redirect}</TD><TD Align='Center'>{item.ClientError}</TD><TD Align='Center'>{item.ServerError}</TD>");
                    writer.Write("</TR>");
                }
                writer.WriteLine("</Table>");
            }
            catch (Exception e)
            {
                log.WriteLine(@"Error while displaying 'HTTP Statistics Per Request URL' " + "\r\n" + e.ToString());
            }


            try
            {
                writer.WriteLine("<H3>HTTP Request Execution Statistics Per Request URL</H3>");

                writer.WriteLine("<Table Border=\"1\">");
                writer.Write("<TR>");
                writer.Write("<TH Align='Center' Title='This is the Request URL.'>Path</TH>");
                writer.Write("<TH Align='Center' Title='All requests for this URL'>Total</TH>");
                writer.Write("<TH Align='Center' Title='The number of requests that finished in less than 1 second'> &lt; 1s </TH>");
                writer.Write("<TH Align='Center' Title='The number of requests that finished in less than 5 seconds'>&lt; 5s</TH>");
                writer.Write("<TH Align='Center' Title='The number of requests that finished in less than 15 seconds'>&lt; 15s</TH>");
                writer.Write("<TH Align='Center' Title='The number of requests that finished in less than 30 seconds'>&lt; 30s</TH>");
                writer.Write("<TH Align='Center' Title='The number of requests that finished in less than 60 seconds'>&lt; 60s</TH>");
                writer.Write("<TH Align='Center' Title='The number of requests that finished in more than 60 seconds'>&gt; 60s</TH>");
                writer.WriteLine("</TR>");

                var httpRequestExecutionPerUrl = m_Requests.Values.GroupBy(n => n.Path.Split('?')[0]).Select(c => new
                {
                    Path = c.Key,
                    Total = c.Count(),
                    OneSec = c.Count(s => (s.EndTimeRelativeMSec - s.StartTimeRelativeMSec) < 1000),
                    FiveSec = c.Count(s => (s.EndTimeRelativeMSec - s.StartTimeRelativeMSec) >= 1000 && (s.EndTimeRelativeMSec - s.StartTimeRelativeMSec) < 5000),
                    FifteenSec = c.Count(s => (s.EndTimeRelativeMSec - s.StartTimeRelativeMSec) >= 5000 && (s.EndTimeRelativeMSec - s.StartTimeRelativeMSec) < 15000),
                    ThirtySec = c.Count(s => (s.EndTimeRelativeMSec - s.StartTimeRelativeMSec) >= 15000 && (s.EndTimeRelativeMSec - s.StartTimeRelativeMSec) < 30000),
                    SixtySec = c.Count(s => (s.EndTimeRelativeMSec - s.StartTimeRelativeMSec) >= 30000 && (s.EndTimeRelativeMSec - s.StartTimeRelativeMSec) < 60000),
                    MoreThanSixtySec = c.Count(s => (s.EndTimeRelativeMSec - s.StartTimeRelativeMSec) > 60000)
                });

                // sort this list by the the maximum number of requests
                foreach (var item in httpRequestExecutionPerUrl.OrderByDescending(x => x.Total))
                {
                    writer.WriteLine("<TR>");
                    writer.Write($"<TD>{item.Path}</TD><TD Align='Center'>{item.Total}</TD><TD Align='Center'>{item.OneSec}</TD><TD Align='Center'>{item.FiveSec}</TD><TD Align='Center'>{item.FifteenSec}</TD><TD Align='Center'>{item.ThirtySec}</TD><TD Align='Center'>{item.SixtySec}</TD><TD Align='Center'>{item.MoreThanSixtySec}</TD>");
                    writer.Write("</TR>");
                }
                writer.WriteLine("</Table>");
            }
            catch (Exception e)
            {
                log.WriteLine(@"Error while displaying 'HTTP Request Execution Statistics Per Request URL' " + "\r\n" + e.ToString());
            }



            writer.WriteLine("<H3>Top 100 Slowest Request Statistics</H3>");
            writer.WriteLine("The below table shows the top 100 slowest requests in this trace. Requests completing within 100 milliseconds are ignored. Hover over column headings for explanation of columns. <BR/><BR/>");

            writer.WriteLine("<Table Border=\"1\">");
            writer.Write("<TR>");
            writer.Write("<TH Align='Center' Title='This column represents the HTTP METHOD used to make the request'>Method</TH>");
            writer.Write("<TH Align='Center' Title='This is the Request URL. Click on the individual requests to see all ETW events for that request.' >Path</TH>");
            writer.Write("<TH Align='Center' Title='cs-bytes represents the total bytes sent by the client for this HTTP Request'>cs-bytes</TH>");
            writer.Write("<TH Align='Center' Title='sc-bytes represents the total bytes that the server sent for the HTTP Response'>sc-bytes</TH>");
            writer.Write("<TH Align='Center' Title='The HTTP Status Code and Substatus code that server sent for this HTTP request'>HttpStatus</TH>");
            writer.Write("<TH Align='Center' Title='The total time it took to execute the request on the server'>Duration(ms)</TH>");
            writer.Write("<TH Align='Center' Title='This is the slowest module in the IIS request processing pipeline. Click on the the slowest module to see user mode stack trace for the thread. Hyperlinks to open thread stack traces are added only if the starting thread and ending thread for the module are the same.'>Slowest Module</TH>");
            writer.Write("<TH Align='Center' Title='This represents the time spent in the slowest module (in milliseconds)'>Time Spent In Slowest Module(ms)</TH>");
            writer.Write("<TH Align='Center' Title='This column gives you a percentage of time spent in the slowest module to the total time spent in request execution'>%Time Spent In Slowest Module</TH>");
            writer.Write("<TH Align='Center' Title='Click on the relevant Views to see the stack traces captured for the request'>Stack Traces</TH>");
            writer.WriteLine("</TR>");



            foreach (var request in m_Requests.Values.Where(x => x.EndTimeRelativeMSec != 0).OrderByDescending(m => m.EndTimeRelativeMSec - m.StartTimeRelativeMSec).Take(100).Where((m => (m.EndTimeRelativeMSec - m.StartTimeRelativeMSec) > 100)))
            {
                writer.WriteLine("<TR>");
                double slowestTime = 0;
                IisPipelineEvent slowestPipelineEvent = GetSlowestEvent(request);
                slowestTime = slowestPipelineEvent.EndTimeRelativeMSec - slowestPipelineEvent.StartTimeRelativeMSec;

                int processId = slowestPipelineEvent.ProcessId;
                int ThreadId = slowestPipelineEvent.StartThreadId;

                double startTimePipelineEvent = slowestPipelineEvent.StartTimeRelativeMSec;
                double endTimePipelineEvent = slowestPipelineEvent.EndTimeRelativeMSec;

                string threadTimeStacksString = $"{processId};{ThreadId};{startTimePipelineEvent};{endTimePipelineEvent}";
                string activityStacksString = $"{processId};{request.RelatedActivityId.ToString()};{startTimePipelineEvent};{endTimePipelineEvent}";

                double totalTimeSpent = request.EndTimeRelativeMSec - request.StartTimeRelativeMSec;

                string requestPath = request.Path;

                // limit display of URL to specific character length only otherwise the table is expanding crazily
                if (requestPath.Length > 85)
                {
                    requestPath = requestPath.Substring(0, 80) + "...";
                }

                string threadTimeStacks = "";
                string activityStacks = "";

                // limit display of even the module names to specific character length only otherwise the table is expanding crazily
                string slowestPipelineEventDisplay = slowestPipelineEvent.ToString();
                if (slowestPipelineEventDisplay != null && slowestPipelineEventDisplay.Length > 55)
                {
                    slowestPipelineEventDisplay = slowestPipelineEventDisplay.Substring(0, 50) + "...";
                }

                if (slowestPipelineEvent.StartThreadId == slowestPipelineEvent.EndThreadId)
                {
                    threadTimeStacks = $"<A HREF =\"command:threadtimestacks:{threadTimeStacksString}\">Thread Time stacks</A>";
                }
                else
                {
                    threadTimeStacks = "";
                }

                activityStacks = $"<A HREF =\"command:activitystacks:{activityStacksString}\">Activity Stacks</A>";

                string detailedRequestCommandString = $"detailedrequestevents:{request.ContextId};{request.StartTimeRelativeMSec};{request.EndTimeRelativeMSec}";

                string csBytes = (request.BytesReceived == 0) ? "-" : request.BytesReceived.ToString();
                string scBytes = (request.BytesSent == 0) ? "-" : request.BytesSent.ToString();
                string statusCode = (request.StatusCode == 0) ? "-" : $"{ request.StatusCode}.{ request.SubStatusCode}";

                writer.WriteLine($"<TD>{request.Method}</TD><TD><A HREF=\"command:{detailedRequestCommandString}\">{requestPath}</A></TD><TD>{csBytes}</TD><TD>{scBytes}</TD><TD>{statusCode}</TD><TD>{totalTimeSpent:0.00}</TD><TD>{slowestPipelineEventDisplay}</TD><TD>{slowestTime:0.00}</TD><TD>{((slowestTime / totalTimeSpent * 100)):0.00}%</TD><TD>{activityStacks} {threadTimeStacks}</TD>");
                writer.Write("</TR>");
            }
            writer.WriteLine("</TABLE>");

            if (m_Requests.Values.Count(x => x.FailureDetails != null) > 0)
            {

                writer.WriteLine("<H2>Failed Requests</H2>");

                writer.WriteLine("<BR/>The below table provides details of all the failed requests (requests with StatusCode >399) in the trace. <BR/>");

                writer.WriteLine("<Table Border=\"1\">");
                writer.Write("<TR>");
                writer.Write("<TH Align='Center' Title='This column represents the HTTP METHOD used to make the request'>Method</TH>");
                writer.Write("<TH Align='Center' Title='This is the Request URL' >Path</TH>");
                writer.Write("<TH Align='Center' Title='cs-bytes represents the total bytes sent by the client for this HTTP Request'>cs-bytes</TH>");
                writer.Write("<TH Align='Center' Title='sc-bytes represents the total bytes that the server sent for the HTTP Response'>sc-bytes</TH>");
                writer.Write("<TH Align='Center' Title='The HTTP Status Code and Substatus code that server sent for this HTTP request'>HttpStatus</TH>");
                writer.Write("<TH Align='Center' Title='A user-friendly description of the error code sent by the server'>Reason</TH>");
                writer.Write("<TH Align='Center' Title='This is the actual HTTP Status which the server sent for this request irrespective of the failure'>Final Status</TH>");
                writer.Write("<TH Align='Center' Title='Additional error code that IIS generated for the failed request'>ErrorCode</TH>");
                writer.Write("<TH Align='Center' Title='The module responsible for setting the failed HTTP Status' >FailingModuleName</TH>");
                writer.Write("<TH Align='Center' Title='The total time it took to execute the request on the server'>Duration(ms)</TH>");
                writer.Write("<TH Align='Center' Title='Any CLR Exceptions that happened on this thread'>Exceptions</TH>");
                writer.WriteLine("</TR>");

                foreach (var request in m_Requests.Values.Where(x => x.FailureDetails != null))
                {
                    writer.WriteLine("<TR>");


                    double totalTimeSpent = request.EndTimeRelativeMSec - request.StartTimeRelativeMSec;

                    string requestPath = request.Path;

                    // limit display of URL to 100 characters only
                    // otherwise the table is expanding crazily
                    if (requestPath.Length > 100)
                    {
                        requestPath = requestPath.Substring(0, 100) + "...";
                    }

                    string detailedRequestCommandString = $"detailedrequestevents:{request.ContextId};{request.StartTimeRelativeMSec};{request.EndTimeRelativeMSec}";

                    string csBytes = (request.BytesReceived == 0) ? "-" : request.BytesReceived.ToString();
                    string scBytes = (request.BytesSent == 0) ? "-" : request.BytesSent.ToString();

                    string exceptionDetails = FindExceptionForThisRequest(request);

                    writer.WriteLine($"<TD>{request.Method}</TD><TD><A HREF=\"command:{detailedRequestCommandString}\">{requestPath}</A></TD><TD>{csBytes}</TD><TD>{scBytes}</TD><TD>{request.FailureDetails.HttpStatus}.{request.FailureDetails.HttpSubStatus}</TD><TD>{request.FailureDetails.HttpReason}</TD><TD>{request.StatusCode}.{request.SubStatusCode}</TD><TD>{request.FailureDetails.ErrorCode}</TD><TD>{request.FailureDetails.ModuleName} ({request.FailureDetails.Notification}) </TD><TD>{totalTimeSpent:0.00}</TD><TD>{exceptionDetails}</TD>");

                    writer.Write("</TR>");
                }

                writer.WriteLine("</TABLE>");
            }
            else
            {
                writer.WriteLine("<BR/><BR/>There are no failed requests (HTTP Requests with StatusCode >=400) in this trace file");
            }

            writer.Flush();
        }

        private int GetChildEventRecurseLevel(Guid contextId, Dictionary<Guid, int> childRequests)
        {
            int childRequestRecurseLevel = 0;
            if (childRequests.ContainsKey(contextId))
            {
                childRequests.TryGetValue(contextId, out childRequestRecurseLevel);
            }
            return childRequestRecurseLevel;
        }

        private string FindExceptionForThisRequest(IisRequest request)
        {
            double startTimeForPipeLineEvent = 0;
            string exceptionMessage = "";
            int processId = 0;
            int threadId = 0;
            foreach (var item in request.PipelineEvents.OfType<IisModuleEvent>().Where(x => x.Name == request.FailureDetails.ModuleName))
            {
                var moduleEvent = item as IisModuleEvent;

                if (moduleEvent.Notification == request.FailureDetails.Notification)
                {
                    startTimeForPipeLineEvent = moduleEvent.StartTimeRelativeMSec;
                    processId = moduleEvent.ProcessId;
                    if (moduleEvent.StartThreadId == moduleEvent.EndThreadId)
                    {
                        threadId = moduleEvent.StartThreadId;
                    }
                }
            }

            Dictionary<string, int> exceptionsList = new Dictionary<string, int>();

            if (startTimeForPipeLineEvent > 0 && processId != 0 && threadId != 0)
            {

                foreach (var ex in allExceptions.Where(x => x.TimeStampRelativeMSec > startTimeForPipeLineEvent && x.TimeStampRelativeMSec <= request.FailureDetails.TimeStampRelativeMSec
                                                        && processId == x.ProcessId
                                                        && threadId == x.ThreadId))
                {
                    exceptionMessage = ex.ExceptionType + ":" + ex.ExceptionMessage;

                    if (exceptionsList.ContainsKey(exceptionMessage))
                    {
                        exceptionsList[exceptionMessage] = exceptionsList[exceptionMessage] + 1;
                    }
                    else
                    {
                        exceptionsList.Add(exceptionMessage, 1);
                    }
                }
            }

            string returnString = "";
            foreach (var item in exceptionsList.OrderByDescending(x => x.Value))
            {
                returnString = $"{item.Value}  exceptions [{item.Key.ToString()}] <br/>";
            }

            return returnString;
        }

        protected override string DoCommand(string command, StatusBar worker)
        {
            if (command.StartsWith("detailedrequestevents:"))
            {
                string detailedrequesteventsString = command.Substring(22);

                var detailedrequesteventsParams = detailedrequesteventsString.Split(';');

                if (detailedrequesteventsParams.Length > 2)
                {
                    string requestId = detailedrequesteventsParams[0];
                    string startTime = detailedrequesteventsParams[1];
                    string endTime = detailedrequesteventsParams[2];

                    var etlFile = new ETLDataFile(DataFile.FilePath);
                    var events = etlFile.Events;

                    // Pick out the desired events. 
                    var desiredEvents = new List<string>();
                    foreach (var eventName in events.EventNames)
                    {
                        if (eventName.Contains("IIS_Trace") || eventName.Contains("AspNet"))
                        {
                            desiredEvents.Add(eventName);
                        }
                    }
                    events.SetEventFilter(desiredEvents);

                    GuiApp.MainWindow.Dispatcher.BeginInvoke((Action)delegate ()
                    {
                        // TODO FIX NOW this is probably a hack?
                        var file = PerfViewFile.Get(events.m_EtlFile.FilePath);
                        var eventSource = new PerfViewEventSource(file);
                        eventSource.m_eventSource = events;

                        eventSource.Viewer = new EventWindow(GuiApp.MainWindow, eventSource);
                        eventSource.Viewer.TextFilterTextBox.Text = requestId;
                        eventSource.Viewer.StartTextBox.Text = startTime;
                        eventSource.Viewer.EndTextBox.Text = endTime;
                        eventSource.Viewer.Loaded += delegate
                        {
                            eventSource.Viewer.EventTypes.SelectAll();
                            eventSource.Viewer.Update();
                        };

                        eventSource.Viewer.Show();
                    });
                }
            }

            else if (command.StartsWith("threadtimestacks:"))
            {
                string threadTimeStacksString = command.Substring(17);

                var threadTimeStacksParams = threadTimeStacksString.Split(';');

                int processID = Convert.ToInt32(threadTimeStacksParams[0]);
                int threadId = Convert.ToInt32(threadTimeStacksParams[1]);
                string startTime = threadTimeStacksParams[2];
                string endTime = threadTimeStacksParams[3];

                using (var etlFile = CommandEnvironment.OpenETLFile(DataFile.FilePath))
                {
                    etlFile.SetFilterProcess(processID);
                    var stacks = etlFile.ThreadTimeStacks();
                    stacks.Filter.StartTimeRelativeMSec = startTime;
                    stacks.Filter.EndTimeRelativeMSec = endTime;

                    //Thread(38008); (11276)
                    stacks.Filter.IncludeRegExs = $"Process% w3wp ({processID.ToString()});Thread ({threadId.ToString()})";

                    CommandEnvironment.OpenStackViewer(stacks);

                }
            }
            else if (command.StartsWith("activitystacks:"))
            {
                string activityStacksString = command.Substring(16);

                var activityStacksParams = activityStacksString.Split(';');

                int processID = Convert.ToInt32(activityStacksParams[0]);
                string relatedActivityId = activityStacksParams[1];
                string startTime = activityStacksParams[2];
                string endTime = activityStacksParams[3];

                using (var etlFile = CommandEnvironment.OpenETLFile(DataFile.FilePath))
                {
                    var startStopSource = new MutableTraceEventStackSource(etlFile.TraceLog);

                    var computer = new ThreadTimeStackComputer(etlFile.TraceLog, App.GetSymbolReader(etlFile.TraceLog.FilePath))
                    {
                        UseTasks = true,
                        GroupByStartStopActivity = true,
                        ExcludeReadyThread = true
                    };
                    computer.GenerateThreadTimeStacks(startStopSource);

                    etlFile.SetFilterProcess(processID);
                    var stacks = new Stacks(startStopSource, "Thread Time (with StartStop Activities)", etlFile, false);

                    stacks.Filter.StartTimeRelativeMSec = startTime;
                    stacks.Filter.EndTimeRelativeMSec = endTime;
                    stacks.Filter.IncludeRegExs = relatedActivityId;
                    stacks.Filter.FoldRegExs = "ntoskrnl!%ServiceCopyEnd;System.Runtime.CompilerServices.Async%MethodBuilder;^STARTING TASK";

                    CommandEnvironment.OpenStackViewer(stacks);
                }
            }
            return base.DoCommand(command, worker);
        }

        #region private
        private IisRequest GenerateFakeIISRequest(Guid contextId, TraceEvent traceEvent, double timeStamp = 0)
        {
            IisRequest request = new IisRequest();
            request.ContextId = contextId;

            if (traceEvent != null)
            {
                request.StartTimeRelativeMSec = traceEvent.TimeStampRelativeMSec;
            }
            else
            {
                request.StartTimeRelativeMSec = timeStamp;
            }
            request.Method = "UNKNOWN";
            request.Path = "Unkwown (GENERAL_REQUEST_START event not captured in trace)";

            return request;
        }

        private void AddGenericStartEventToRequest(Guid contextId, TraceEvent traceEvent, Dictionary<Guid, int> childRequests, string pipelineEventName = "")
        {
            IisRequest request;

            if (!m_Requests.TryGetValue(contextId, out request))
            {
                // so this is the case where we dont have a GENERAL_REQUEST_START 
                // event but we got a Module Event fired for this request 
                // so we do our best to create a FAKE start request event
                // populating as much information as we can.
                request = GenerateFakeIISRequest(contextId, null, traceEvent.TimeStampRelativeMSec);
                m_Requests.Add(contextId, request);
            }


            var iisPipelineEvent = new IisPipelineEvent();
            if (string.IsNullOrEmpty(pipelineEventName))
            {
                if (traceEvent.OpcodeName.ToLower().EndsWith("_start"))
                {
                    iisPipelineEvent.Name = traceEvent.OpcodeName.Substring(0, traceEvent.OpcodeName.Length - 6);
                }
                // For All the AspnetReq events, they start with Enter or Begin
                // Also, we want to append the AspnetReq/ in front of them so we can easily distinguish them
                // as coming from ASP.NET pipeline
                else if (traceEvent.OpcodeName.ToLower().EndsWith("enter") || traceEvent.OpcodeName.ToLower().EndsWith("begin"))
                {
                    iisPipelineEvent.Name = traceEvent.EventName.Substring(0, traceEvent.EventName.Length - 5);
                }
            }
            else
            {
                iisPipelineEvent.Name = pipelineEventName;
            }

            int childRequestRecurseLevel = GetChildEventRecurseLevel(contextId, childRequests);
            iisPipelineEvent.StartTimeRelativeMSec = traceEvent.TimeStampRelativeMSec;
            iisPipelineEvent.StartThreadId = traceEvent.ThreadID;
            iisPipelineEvent.ProcessId = traceEvent.ProcessID;
            iisPipelineEvent.ChildRequestRecurseLevel = childRequestRecurseLevel;
            request.PipelineEvents.Add(iisPipelineEvent);

        }

        private void AddGenericStopEventToRequest(Guid contextId, TraceEvent traceEvent, Dictionary<Guid, int> childRequests, string pipelineEventName = "")
        {
            IisRequest request;
            if (m_Requests.TryGetValue(contextId, out request))
            {
                string eventName = "";

                if (string.IsNullOrEmpty(pipelineEventName))
                {
                    if (traceEvent.OpcodeName.ToLower().EndsWith("_end"))
                    {
                        eventName = traceEvent.OpcodeName.Substring(0, traceEvent.OpcodeName.Length - 4);
                    }

                    // For All the AspnetReq events, they finish with Leave. Also, we want to append the AspnetReq/ 
                    // in front of them so we can easily distinguish them as coming from ASP.NET pipeline
                    else if (traceEvent.OpcodeName.ToLower().EndsWith("leave"))
                    {
                        eventName = traceEvent.EventName.Substring(0, traceEvent.EventName.Length - 5);
                    }
                }
                else
                {
                    eventName = pipelineEventName;
                }

                int childRequestRecurseLevel = GetChildEventRecurseLevel(contextId, childRequests);
                var iisPipelineEvent = request.PipelineEvents.FirstOrDefault(m => (m.Name == eventName) && m.EndTimeRelativeMSec == 0 && m.ChildRequestRecurseLevel == childRequestRecurseLevel);
                if (iisPipelineEvent != null)
                {
                    iisPipelineEvent.EndTimeRelativeMSec = traceEvent.TimeStampRelativeMSec;
                    iisPipelineEvent.EndThreadId = traceEvent.ThreadID;
                }
            }
        }
        private IisPipelineEvent GetSlowestEvent(IisRequest request)
        {
            IisPipelineEvent slowestPipelineEvent = null;
            double slowestTime = 0;

            foreach (var pipeLineEvent in request.PipelineEvents)
            {
                if (pipeLineEvent.StartTimeRelativeMSec != 0 && pipeLineEvent.EndTimeRelativeMSec != 0)
                {
                    var timeinThisEvent = pipeLineEvent.EndTimeRelativeMSec - pipeLineEvent.StartTimeRelativeMSec;
                    if (timeinThisEvent > slowestTime)
                    {
                        slowestTime = timeinThisEvent;
                        slowestPipelineEvent = pipeLineEvent;
                    }
                }
            }

            // Lets check for containment to see if a child event is taking more than 50% 
            // of the time of this pipeline event, then we want to call that out
            foreach (var pipeLineEvent in request.PipelineEvents.Where(x => (x.StartTimeRelativeMSec > slowestPipelineEvent.StartTimeRelativeMSec) && (x.EndTimeRelativeMSec <= slowestPipelineEvent.EndTimeRelativeMSec)))
            {
                var timeinThisEvent = pipeLineEvent.EndTimeRelativeMSec - pipeLineEvent.StartTimeRelativeMSec;

                if (((timeinThisEvent / slowestTime) * 100) > 50)
                {
                    slowestTime = timeinThisEvent;
                    slowestPipelineEvent = pipeLineEvent;
                }

            }

            var timeInSlowestEvent = slowestPipelineEvent == null ? 0D : slowestPipelineEvent.EndTimeRelativeMSec - slowestPipelineEvent.StartTimeRelativeMSec;
            var requestExecutionTime = request.EndTimeRelativeMSec - request.StartTimeRelativeMSec;

            if (timeInSlowestEvent > 0 && requestExecutionTime > 500)
            {
                if (((timeInSlowestEvent / requestExecutionTime) * 100) < 50)
                {
                    // So this is the scenario where the default set of events that we are tracking
                    // do not have any delay. Lets do our best and see if we can at least
                    // populate the StartTime, EndTime                    

                    IisPipelineEvent unKnownPipeLineEvent = CheckForDelayInUnknownEvents(request, timeInSlowestEvent);

                    if (unKnownPipeLineEvent != null)
                    {
                        slowestPipelineEvent = unKnownPipeLineEvent;
                    }
                }
            }

            return slowestPipelineEvent ?? new IisPipelineEvent();
        }

        private IisPipelineEvent CheckForDelayInUnknownEvents(IisRequest request, double timeInSlowestEvent)
        {
            double slowestTimeInThisEvent = 0;
            int position = 0;
            var pipelineEventsArray = request.PipelineEvents.ToArray();
            for (int i = 0; i < pipelineEventsArray.Length - 1; i++)
            {
                if (pipelineEventsArray[i].EndTimeRelativeMSec != 0)
                {
                    var timeDiff = pipelineEventsArray[i + 1].StartTimeRelativeMSec - pipelineEventsArray[i].EndTimeRelativeMSec;
                    if (slowestTimeInThisEvent < timeDiff)
                    {
                        slowestTimeInThisEvent = timeDiff;
                        position = i;
                    }
                }
            }

            IisPipelineEvent unknownEvent = null;

            if ((slowestTimeInThisEvent / timeInSlowestEvent) > 1.5)
            {
                if (position > 0)
                {
                    unknownEvent = new IisPipelineEvent();
                    unknownEvent.Name = "UNKNOWN";
                    unknownEvent.StartThreadId = pipelineEventsArray[position].EndThreadId;
                    unknownEvent.EndThreadId = pipelineEventsArray[position + 1].StartThreadId;
                    unknownEvent.StartTimeRelativeMSec = pipelineEventsArray[position].EndTimeRelativeMSec;
                    unknownEvent.EndTimeRelativeMSec = pipelineEventsArray[position + 1].StartTimeRelativeMSec;
                    unknownEvent.ProcessId = pipelineEventsArray[position + 1].ProcessId;
                }
            }

            return unknownEvent;
        }

        private class ExceptionDetails
        {
            public string ExceptionType;
            public string ExceptionMessage;
            public int ThreadId;
            public int ProcessId;
            public double TimeStampRelativeMSec;
        }

        private class IisRequest
        {
            public string Method;
            public string Path;
            public Guid ContextId;
            public int BytesSent;
            public int BytesReceived;
            public int StatusCode;
            public int SubStatusCode;
            public RequestFailureDetails FailureDetails;
            public double EndTimeRelativeMSec;
            public double StartTimeRelativeMSec;
            public List<IisPipelineEvent> PipelineEvents = new List<IisPipelineEvent>();
            public Guid RelatedActivityId;
        }

        private class IisPipelineEvent
        {
            public string Name;
            public int ProcessId;
            public int StartThreadId;
            public int EndThreadId;
            public double StartTimeRelativeMSec = 0;
            public double EndTimeRelativeMSec = 0;
            public int ChildRequestRecurseLevel = 0;
            public override string ToString()
            {
                return Name;
            }
        }

        private class AspNetPipelineModuleEvent : IisPipelineEvent
        {
            public string ModuleName;
            public bool foundEndEvent = false;

            public override string ToString()
            {
                return ModuleName;
            }
        }

        private class IisModuleEvent : IisPipelineEvent
        {
            public RequestNotification Notification;
            public bool fIsPostNotification;
            public bool foundEndEvent = false;

            public override string ToString()
            {
                return $"{Name} ({Notification.ToString()})";
            }
        }

        private class IisPrebeginModuleEvent : IisPipelineEvent
        {
            public override string ToString()
            {
                return $"{Name} (PreBegin)";
            }
        }

        private class RequestFailureDetails
        {
            public string ModuleName;
            public RequestNotification Notification;
            public string HttpReason;
            public int HttpStatus;
            public int HttpSubStatus;
            public int ErrorCode;
            public string ConfigExceptionInfo;
            public double TimeStampRelativeMSec;
        }

        #endregion 
    }

    public class PerfViewAspNetStats : PerfViewHtmlReport
    {
        public PerfViewAspNetStats(PerfViewFile dataFile) : base(dataFile, "Asp.Net Stats") { }
        protected override void WriteHtmlBody(TraceLog dataFile, TextWriter writer, string fileName, TextWriter log)
        {
            var dispatcher = dataFile.Events.GetSource();
            var aspNet = new AspNetTraceEventParser(dispatcher);

            m_requests = new List<AspNetRequest>();
            var requestByID = new Dictionary<Guid, AspNetRequest>();

            var startIntervalMSec = 0;
            var totalIntervalMSec = dataFile.SessionDuration.TotalMilliseconds;

            var bucketIntervalMSec = 1000;
            var numBuckets = Math.Max(1, (int)(totalIntervalMSec / bucketIntervalMSec));

            var GCType = "Unknown";
            var requestsRecieved = 0;

            var byTimeStats = new ByTimeRequestStats[numBuckets];
            for (int i = 0; i < byTimeStats.Length; i++)
            {
                byTimeStats[i] = new ByTimeRequestStats();
            }

            var requestsProcessing = 0;

            dispatcher.Kernel.PerfInfoSample += delegate (SampledProfileTraceData data)
            {
                if (data.ProcessID == 0)    // Non-idle time.  
                {
                    return;
                }

                int idx = GetBucket(data.TimeStampRelativeMSec, startIntervalMSec, bucketIntervalMSec, byTimeStats.Length);
                if (idx >= 0)
                {
                    byTimeStats[idx].CpuMSec++;
                }
            };

            dispatcher.Clr.RuntimeStart += delegate (RuntimeInformationTraceData data)
            {
                if ((data.StartupFlags & StartupFlags.SERVER_GC) != 0)
                {
                    GCType = "Server";
                }
                else
                {
                    GCType = "Client";
                }
            };

            dispatcher.Clr.ContentionStart += delegate (ContentionStartTraceData data)
            {
                int idx = GetBucket(data.TimeStampRelativeMSec, startIntervalMSec, bucketIntervalMSec, byTimeStats.Length);
                if (idx >= 0)
                {
                    byTimeStats[idx].Contentions++;
                }
            };

            dispatcher.Clr.GCStop += delegate (GCEndTraceData data)
            {
                int idx = GetBucket(data.TimeStampRelativeMSec, startIntervalMSec, bucketIntervalMSec, byTimeStats.Length);
                if (idx >= 0)
                {
                    byTimeStats[idx].NumGcs++;
                    if (data.Depth >= 2)
                    {
                        byTimeStats[idx].NumGen2Gcs++;
                    }
                }
            };

            bool seenBadAllocTick = false;
            dispatcher.Clr.GCAllocationTick += delegate (GCAllocationTickTraceData data)
            {
                int idx = GetBucket(data.TimeStampRelativeMSec, startIntervalMSec, bucketIntervalMSec, byTimeStats.Length);
                if (idx >= 0)
                {
                    var valueMB = data.GetAllocAmount(ref seenBadAllocTick) / 1000000.0f;

                    byTimeStats[idx].GCHeapAllocMB += valueMB;
                }
            };

            dispatcher.Clr.GCHeapStats += delegate (GCHeapStatsTraceData data)
            {
                // TODO should it be summed over processes? 
                int idx = GetBucket(data.TimeStampRelativeMSec, startIntervalMSec, bucketIntervalMSec, byTimeStats.Length);
                if (idx >= 0)
                {
                    var totalSize = data.GenerationSize0 + data.GenerationSize1 + data.GenerationSize2 + data.GenerationSize3 + data.GenerationSize4;
                    byTimeStats[idx].GCHeapSizeMB = Math.Max(byTimeStats[idx].GCHeapSizeMB, totalSize / 1000000.0F);
                }
            };

            dispatcher.Clr.ThreadPoolWorkerThreadAdjustmentAdjustment += delegate (ThreadPoolWorkerThreadAdjustmentTraceData data)
            {
                int idx = GetBucket(data.TimeStampRelativeMSec, startIntervalMSec, bucketIntervalMSec, byTimeStats.Length);
                if (idx >= 0)
                {
                    // TODO compute the average weighted by time.  
                    byTimeStats[idx].ThreadPoolThreadCountSum += data.NewWorkerThreadCount;
                    byTimeStats[idx].ThreadPoolAdjustmentCount++;
                }
            };

            var lastDiskEndMSec = new GrowableArray<double>(4);
            dispatcher.Kernel.AddCallbackForEvents<DiskIOTraceData>(delegate (DiskIOTraceData data)
            {
                // Compute the disk service time.  
                if (data.DiskNumber >= lastDiskEndMSec.Count)
                {
                    lastDiskEndMSec.Count = data.DiskNumber + 1;
                }

                var elapsedMSec = data.ElapsedTimeMSec;
                double serviceTimeMSec = elapsedMSec;
                double durationSinceLastIOMSec = data.TimeStampRelativeMSec - lastDiskEndMSec[data.DiskNumber];
                if (durationSinceLastIOMSec < serviceTimeMSec)
                {
                    serviceTimeMSec = durationSinceLastIOMSec;
                }

                // Add it to the stats.  
                int idx = GetBucket(data.TimeStampRelativeMSec, startIntervalMSec, bucketIntervalMSec, byTimeStats.Length);
                if (idx >= 0)
                {
                    byTimeStats[idx].DiskIOMsec += serviceTimeMSec;
                }
            });

            dispatcher.Kernel.ThreadCSwitch += delegate (CSwitchTraceData data)
            {
                int idx = GetBucket(data.TimeStampRelativeMSec, startIntervalMSec, bucketIntervalMSec, byTimeStats.Length);
                if (idx >= 0)
                {
                    byTimeStats[idx].ContextSwitch++;
                }
            };

            aspNet.AspNetReqStart += delegate (AspNetStartTraceData data)
            {
                var request = new AspNetRequest();
                request.ID = data.ContextId;
                request.Path = data.Path;
                request.Method = data.Method;
                request.QueryString = data.QueryString;
                request.StartTimeRelativeMSec = data.TimeStampRelativeMSec;
                request.StartThreadID = data.ThreadID;
                request.Process = data.Process();

                requestByID[request.ID] = request;
                m_requests.Add(request);

                requestsRecieved++;
                request.RequestsReceived = requestsRecieved;
                request.RequestsProcessing = requestsProcessing;
            };

            aspNet.AspNetReqStop += delegate (AspNetStopTraceData data)
            {
                AspNetRequest request;
                if (requestByID.TryGetValue(data.ContextId, out request))
                {
                    // If we missed the hander end, then complete it.  
                    if (request.HandlerStartTimeRelativeMSec > 0 && request.HandlerStopTimeRelativeMSec == 0)
                    {
                        --requestsProcessing;
                        request.HandlerStopTimeRelativeMSec = data.TimeStampRelativeMSec;
                        Debug.Assert(requestsProcessing >= 0);
                    }

                    Debug.Assert(request.StopTimeRelativeMSec == 0);
                    request.StopTimeRelativeMSec = data.TimeStampRelativeMSec;
                    request.StopThreadID = data.ThreadID;
                    Debug.Assert(request.StopTimeRelativeMSec > request.StartTimeRelativeMSec);

                    --requestsRecieved;
                    Debug.Assert(requestsRecieved >= 0);

                    int idx = GetBucket(data.TimeStampRelativeMSec, startIntervalMSec, bucketIntervalMSec, byTimeStats.Length);
                    if (idx >= 0)
                    {
                        var byTimeState = byTimeStats[idx];
                        byTimeState.NumRequests++;
                        byTimeState.DurationMSecTotal += request.DurationMSec;
                        byTimeState.QueuedDurationMSecTotal += request.QueueDurationMSec;
                        if (request.DurationMSec > byTimeState.RequestsMSecMax)
                        {
                            byTimeState.RequestsThreadOfMax = request.HandlerThreadID;
                            byTimeState.RequestsTimeOfMax = request.StartTimeRelativeMSec;
                            byTimeState.RequestsMSecMax = request.DurationMSec;
                        }
                    }
                }
                else
                {
                    log.WriteLine("WARNING: stop event without a start at {0:n3} Msec.", data.TimeStampRelativeMSec);
                }
            };


            Action<int, double, Guid> handlerStartAction = delegate (int threadID, double timeStampRelativeMSec, Guid contextId)
            {
                AspNetRequest request;
                if (requestByID.TryGetValue(contextId, out request))
                {
                    // allow this routine to be called twice for the same event.  
                    if (request.HandlerStartTimeRelativeMSec != 0)
                    {
                        return;
                    }

                    Debug.Assert(request.StopTimeRelativeMSec == 0);

                    request.HandlerStartTimeRelativeMSec = timeStampRelativeMSec;
                    request.HandlerThreadID = threadID;

                    requestsProcessing++;
                    Debug.Assert(requestsProcessing <= requestsRecieved);
                }
            };

            aspNet.AspNetReqStartHandler += delegate (AspNetStartHandlerTraceData data)
            {
                handlerStartAction(data.ThreadID, data.TimeStampRelativeMSec, data.ContextId);
            };

            // When you don't turn on the most verbose ASP.NET events, you only get a SessionDataBegin event.  Use
            // this as the start of the processing (because it is pretty early) if we have nothing better to use.  
            aspNet.AspNetReqSessionDataBegin += delegate (AspNetAcquireSessionBeginTraceData data)
            {
                handlerStartAction(data.ThreadID, data.TimeStampRelativeMSec, data.ContextId);
            };

            aspNet.AspNetReqEndHandler += delegate (AspNetEndHandlerTraceData data)
            {
                AspNetRequest request;
                if (requestByID.TryGetValue(data.ContextId, out request))
                {
                    if (request.HandlerStartTimeRelativeMSec > 0 && request.HandlerStopTimeRelativeMSec == 0)            // If we saw the start 
                    {
                        --requestsProcessing;
                        request.HandlerStopTimeRelativeMSec = data.TimeStampRelativeMSec;
                    }
                    Debug.Assert(requestsProcessing >= 0);
                }
            };

            dispatcher.Process();
            requestByID = null;         // We are done with the table

            var globalMaxRequestsReceived = 0;
            var globalMaxRequestsQueued = 0;
            var globalMaxRequestsProcessing = 0;

            // It is not uncommon for there to be missing end events, etc, which mess up the running counts of 
            // what is being processed.   Thus look for these messed up events and fix them.  Once the counts
            // are fixed use them to compute the number queued and number being processed over the interval.  
            int recAdjust = 0;
            int procAdjust = 0;
            foreach (var req in m_requests)
            {
                // Compute the fixup for the all subsequent request.  
                Debug.Assert(0 < req.StartTimeRelativeMSec);         // we always set the start time. 

                // Throw out receive counts that don't have a end event
                if (req.StopTimeRelativeMSec == 0)
                {
                    recAdjust++;
                }

                // Throw out process counts that don't have a stop handler or a stop.   
                if (0 < req.HandlerStartTimeRelativeMSec && (req.HandlerStopTimeRelativeMSec == 0 || req.StopTimeRelativeMSec == 0))
                {
                    procAdjust++;
                }

                // Fix up the requests 
                req.RequestsReceived -= recAdjust;
                req.RequestsProcessing -= procAdjust;

                Debug.Assert(0 <= req.RequestsReceived);
                Debug.Assert(0 <= req.RequestsProcessing);
                Debug.Assert(0 <= req.RequestsQueued);
                Debug.Assert(req.RequestsQueued <= req.RequestsReceived);

                // A this point req is accurate.   Calcuate global and byTime stats from that.  
                if (globalMaxRequestsReceived < req.RequestsReceived)
                {
                    globalMaxRequestsReceived = req.RequestsReceived;
                }

                if (globalMaxRequestsProcessing < req.RequestsProcessing)
                {
                    globalMaxRequestsProcessing = req.RequestsProcessing;
                }

                var requestsQueued = req.RequestsQueued;
                if (globalMaxRequestsQueued < requestsQueued)
                {
                    globalMaxRequestsQueued = requestsQueued;
                }

                if (req.StopTimeRelativeMSec > 0)
                {
                    int idx = GetBucket(req.StopTimeRelativeMSec, startIntervalMSec, bucketIntervalMSec, byTimeStats.Length);
                    if (idx >= 0)
                    {
                        byTimeStats[idx].MinRequestsQueued = Math.Min(byTimeStats[idx].MinRequestsQueued, requestsQueued);
                        byTimeStats[idx].MeanRequestsProcessingSum += req.RequestsProcessing;
                        byTimeStats[idx].MeanRequestsProcessingCount++;
                    }
                }
            }
            if (recAdjust != 0)
            {
                log.WriteLine("There were {0} event starts without a matching event end in the trace", recAdjust);
            }

            if (procAdjust != 0)
            {
                log.WriteLine("There were {0} handler starts without a matching handler end in the trace", procAdjust);
            }

            writer.WriteLine("<H2>ASP.Net Statistics</H2>");
            writer.WriteLine("<UL>");
            var fileInfo = new System.IO.FileInfo(dataFile.FilePath);
            writer.WriteLine("<LI> Total Requests: {0:n} </LI>", m_requests.Count);
            writer.WriteLine("<LI> Trace Duration (Sec): {0:n1} </LI>", dataFile.SessionDuration.TotalSeconds);
            writer.WriteLine("<LI> Average Request/Sec: {0:n2} </LI>", m_requests.Count / dataFile.SessionDuration.TotalSeconds);
            writer.WriteLine("<LI> Number of CPUs: {0}</LI>", dataFile.NumberOfProcessors);
            writer.WriteLine("<LI> Maximum Number of requests received but not replied to: {0}</LI>", globalMaxRequestsReceived);
            writer.WriteLine("<LI> Maximum Number of requests queued waiting for processing: {0}</LI>", globalMaxRequestsQueued);
            writer.WriteLine("<LI> Maximum Number of requests concurrently being worked on: {0}</LI>", globalMaxRequestsProcessing);
            writer.WriteLine("<LI> Total Memory (Meg): {0:n0}</LI>", dataFile.MemorySizeMeg);
            writer.WriteLine("<LI> GC Kind: {0} </LI>", GCType);
            writer.WriteLine("<LI> <A HREF=\"#rollupPerTime\">Rollup over time</A></LI>");
            writer.WriteLine("<LI> <A HREF=\"#rollupPerRequestType\">Rollup per request type</A></LI>");
            writer.WriteLine("<LI> <A HREF=\"command:excel/requests\">View ALL individual requests in Excel</A></LI>");
            writer.WriteLine("</UL>");

            writer.Write("<P><A ID=\"rollupPerTime\">Statistics over time.  Hover over column headings for explanation of columns.</A></P>");
            writer.WriteLine("<Table Border=\"1\">");
            writer.Write("<TR>");
            writer.Write("<TH Align=\"Center\">Time Interval MSec</TH>");
            writer.Write("<TH Align=\"Center\">Req/Sec</TH>");
            writer.Write("<TH Align=\"Center\">Max Resp<BR/>MSec</TH>");
            writer.Write("<TH Align=\"Center\" Title=\"The start time of the maximum response (may precede bucket start)\">Start of<BR/>Max</TH>");
            writer.Write("<TH Align=\"Center\">Thread of<BR/>Max</TH>");
            writer.Write("<TH Align=\"Center\" Title=\"The time from when the response is read from the OS until we have written a reply.\">Mean Resp<BR/>MSec</TH>");
            writer.Write("<TH Align=\"Center\" Title=\"The time a request waits before processing begins.\">Mean Queue<BR/>MSec</TH>");
            writer.Write("<TH Align=\"Center\" Title=\"The minium number of requests that have been received but not yet processed.\">Min<BR>Queued</TH>");
            writer.Write("<TH Align=\"Center\" Title=\"The average number of requests that are actively being processed simultaneously.\">Mean<BR>Proc</TH>");
            writer.Write("<TH Align=\"Center\">CPU %</TH>");
            writer.Write("<TH Align=\"Center\" Title=\"The number of context switches per second.\">CSwitch / Sec</TH>");
            writer.Write("<TH Align=\"Center\" Title=\"The total amount of time (MSec) the disk was active (all disks), machine wide.\">Disk<BR>MSec</TH>");
            writer.Write("<TH Align=\"Center\" Title=\"The average number of thread-pool worker over this time period\">Thread<BR>Workers</TH>");
            writer.Write("<TH Align=\"Center\">GC Alloc<BR/>MB/Sec</TH>");
            writer.Write("<TH Align=\"Center\" Title=\"The maximum of the GC heap size (in any process) after any GC\">GC Heap<BR/>MB</TH>");
            writer.Write("<TH Align=\"Center\">GCs</TH>");
            writer.Write("<TH Align=\"Center\">Gen2<BR/>GCs</TH>");
            writer.Write("<TH Align=\"Center\" Title=\"The number of times one thread had to wait for another thread because of a .NET lock\">.NET<BR/>Contention</TH>");
            writer.WriteLine("</TR>");

            // Rollup by time 

            // Only print until CPU goes to 0.  This is because the kernel events stop sooner, and it is confusing 
            // to have one without the other 
            var limit = numBuckets;
            while (0 < limit && byTimeStats[limit - 1].CpuMSec == 0)
            {
                --limit;
            }

            if (limit == 0)             // Something went wrong (e.g no CPU sampling turned on), give up on trimming.
            {
                limit = numBuckets;
            }

            bool wroteARow = false;
            for (int i = 0; i < limit; i++)
            {
                var byTimeStat = byTimeStats[i];
                if (byTimeStat.NumRequests == 0 && !wroteARow)       // Skip initial cases if any. 
                {
                    continue;
                }

                wroteARow = true;
                var startBucketMSec = startIntervalMSec + i * bucketIntervalMSec;
                writer.Write("<TR>");
                writer.Write("<TD Align=\"Center\">{0:n0} - {1:n0}</TD>", startBucketMSec, startBucketMSec + bucketIntervalMSec);
                writer.Write("<TD Align=\"Center\">{0:n1}</TD>", byTimeStat.NumRequests / (bucketIntervalMSec / 1000.0));
                writer.Write("<TD Align=\"Center\">{0:n1}</TD>", byTimeStat.RequestsMSecMax);
                writer.Write("<TD Align=\"Center\">{0:n3}</TD>", byTimeStat.RequestsTimeOfMax);
                writer.Write("<TD Align=\"Center\">{0}</TD>", byTimeStat.RequestsThreadOfMax);
                writer.Write("<TD Align=\"Center\">{0:n1}</TD>", byTimeStat.DurationMSecTotal / byTimeStat.NumRequests);
                writer.Write("<TD Align=\"Center\">{0:n1}</TD>", byTimeStat.QueuedDurationMSecTotal / byTimeStat.NumRequests);
                writer.Write("<TD Align=\"Center\">{0}</TD>", (byTimeStat.MinRequestsQueued == int.MaxValue) ? 0 : byTimeStat.MinRequestsQueued - 1);
                writer.Write("<TD Align=\"Center\">{0:n1}</TD>", byTimeStat.MeanRequestsProcessing);
                writer.Write("<TD Align=\"Center\">{0:n0}</TD>", byTimeStat.CpuMSec * 100.0 / (dataFile.NumberOfProcessors * bucketIntervalMSec));
                writer.Write("<TD Align=\"Center\">{0:n0}</TD>", byTimeStat.ContextSwitch / (bucketIntervalMSec / 1000.0));
                writer.Write("<TD Align=\"Center\">{0:n1}</TD>", byTimeStat.DiskIOMsec);
                writer.Write("<TD Align=\"Center\">{0:n1}</TD>", byTimeStat.MeanThreadPoolThreads);
                writer.Write("<TD Align=\"Center\">{0:n1}</TD>", byTimeStat.GCHeapAllocMB / (bucketIntervalMSec / 1000.0));
                writer.Write("<TD Align=\"Center\">{0}</TD>", byTimeStat.GCHeapSizeMB == 0 ? "No GCs" : byTimeStat.GCHeapSizeMB.ToString("f3"));
                writer.Write("<TD Align=\"Center\">{0}</TD>", byTimeStat.NumGcs);
                writer.Write("<TD Align=\"Center\">{0}</TD>", byTimeStat.NumGen2Gcs);
                writer.Write("<TD Align=\"Center\">{0}</TD>", byTimeStat.Contentions);
                writer.WriteLine("</TR>");
            }
            writer.WriteLine("</Table>");

            var byRequestType = new Dictionary<string, ByRequestStats>();
            foreach (var request in m_requests)
            {
                // Skip requests that did not finish.  
                if (request.StopTimeRelativeMSec == 0)
                {
                    continue;
                }

                var key = request.Method + request.Path + request.QueryString;
                ByRequestStats stats;
                if (!byRequestType.TryGetValue(key, out stats))
                {
                    byRequestType.Add(key, new ByRequestStats(request));
                }
                else
                {
                    stats.AddRequest(request);
                }
            }

            var requestStats = new List<ByRequestStats>(byRequestType.Values);
            requestStats.Sort(delegate (ByRequestStats x, ByRequestStats y)
            {
                return -x.TotalDurationMSec.CompareTo(y.TotalDurationMSec);
            });

            // Rollup by kind of kind of page request
            writer.Write("<P><A ID=\"rollupPerRequestType\">Statistics Per Request URL</A></P>");
            writer.WriteLine("<Table Border=\"1\">");
            writer.Write("<TR>");
            writer.Write("<TH Align=\"Center\">Method</TH>");
            writer.Write("<TH Align=\"Center\">Path</TH>");
            writer.Write("<TH Align=\"Center\">Query String</TH>");
            writer.Write("<TH Align=\"Center\">Num</TH>");
            writer.Write("<TH Align=\"Center\">Num<BR/>&gt; 1s</TH>");
            writer.Write("<TH Align=\"Center\">Num<BR/>&gt; 5s</TH>");
            writer.Write("<TH Align=\"Center\">Num<BR/>&gt; 10s</TH>");
            writer.Write("<TH Align=\"Center\">Total<BR/>MSec</TH>");
            writer.Write("<TH Align=\"Center\">Mean Resp<BR/>MSec</TH>");
            writer.Write("<TH Align=\"Center\">Max Resp<BR/>MSec</TH>");
            writer.Write("<TH Align=\"Center\">Start of<BR/>Max</TH>");
            writer.Write("<TH Align=\"Center\">End of<BR/>Max</TH>");
            writer.Write("<TH Align=\"Center\">Thread of<BR/>Max</TH>");
            writer.WriteLine("</TR>");

            foreach (var requestStat in requestStats)
            {
                writer.Write("<TR>");
                writer.Write("<TD Align=\"Center\">{0}</TD>", requestStat.MaxRequest.Method);
                writer.Write("<TD Align=\"Center\">{0}</TD>", requestStat.MaxRequest.Path);
                var queryString = requestStat.MaxRequest.QueryString;
                if (string.IsNullOrWhiteSpace(queryString))
                {
                    queryString = "&nbsp;";
                }

                writer.Write("<TD Align=\"Center\">{0}</TD>", queryString);
                writer.Write("<TD Align=\"Center\">{0:n0}</TD>", requestStat.NumRequests);
                writer.Write("<TD Align=\"Center\">{0:n0}</TD>", requestStat.NumRequest1Sec);
                writer.Write("<TD Align=\"Center\">{0:n0}</TD>", requestStat.NumRequest5Sec);
                writer.Write("<TD Align=\"Center\">{0:n0}</TD>", requestStat.NumRequest10Sec);
                writer.Write("<TD Align=\"Center\">{0:n0}</TD>", requestStat.TotalDurationMSec);
                writer.Write("<TD Align=\"Center\">{0:n1}</TD>", requestStat.MeanRequestMSec);
                writer.Write("<TD Align=\"Center\">{0:n1}</TD>", requestStat.MaxRequest.DurationMSec);
                writer.Write("<TD Align=\"Center\">{0:n3}</TD>", requestStat.MaxRequest.StartTimeRelativeMSec);
                writer.Write("<TD Align=\"Center\">{0:n3}</TD>", requestStat.MaxRequest.StopTimeRelativeMSec);
                writer.Write("<TD Align=\"Center\">{0}</TD>", requestStat.MaxRequest.HandlerThreadID);
                writer.WriteLine("</TR>");
            }
            writer.WriteLine("</Table>");
            // create some whitespace at the end 
            writer.WriteLine("<p>&nbsp;</p>");
            writer.WriteLine("<p>&nbsp;</p>");
            writer.WriteLine("<p>&nbsp;</p>");
            writer.WriteLine("<p>&nbsp;</p>");
            writer.WriteLine("<p>&nbsp;</p>");
            writer.WriteLine("<p>&nbsp;</p>");
            writer.WriteLine("<p>&nbsp;</p>");
            writer.WriteLine("<p>&nbsp;</p>");
            writer.WriteLine("<p>&nbsp;</p>");
            writer.WriteLine("<p>&nbsp;</p>");
        }

        protected override string DoCommand(string command, StatusBar worker)
        {
            if (command.StartsWith("excel/"))
            {
                var rest = command.Substring(6);
                if (rest == "requests")
                {
                    var csvFile = CacheFiles.FindFile(FilePath, ".aspnet.requests.csv");
                    if (!File.Exists(csvFile) || File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(FilePath) ||
                        File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(SupportFiles.MainAssemblyPath))
                    {
                        CreateCSVFile(m_requests, csvFile);
                    }
                    Command.Run(Command.Quote(csvFile), new CommandOptions().AddStart().AddTimeout(CommandOptions.Infinite));
                    System.Threading.Thread.Sleep(500);     // Give it time to start a bit.  
                    return "Opening CSV file on " + csvFile;
                }
            }
            return "Unknown command " + command;
        }

        #region private
        private class AspNetRequest
        {
            public TraceProcess Process;
            public double DurationMSec { get { return Math.Max(StopTimeRelativeMSec - StartTimeRelativeMSec, 0); } }

            public double QueueDurationMSec
            {
                get
                {
                    // Missing Handler events can cause this.  Typically they are the first events in the system.
                    // TODO is this too misleading?  
                    if (!(HandlerStartTimeRelativeMSec >= StartTimeRelativeMSec))
                    {
                        return 0;
                    }

                    return HandlerStartTimeRelativeMSec - StartTimeRelativeMSec;
                }
            }
            public int StartThreadID;       // TODO remove?  not clear it is interesting. 
            public double StartTimeRelativeMSec;

            public int StopThreadID;        // TODO remove?  not clear it is interesting. 
            public double StopTimeRelativeMSec;

            public int HandlerThreadID;
            public double HandlerStartTimeRelativeMSec;
            public double HandlerStopTimeRelativeMSec;
            public double HandlerDurationMSec { get { return HandlerStopTimeRelativeMSec - HandlerStartTimeRelativeMSec; } }

            public int RequestsReceived;            // just after this request was received, how many have we received but not replied to?
            public int RequestsProcessing;          // just after this request was received, how many total requests are being processed.  
            public int RequestsQueued { get { return RequestsReceived - RequestsProcessing; } }

            public string Method;       // GET or POST
            public string Path;         // url path
            public string QueryString;  // Query 
            public Guid ID;
        }

        private class ByTimeRequestStats
        {
            public ByTimeRequestStats()
            {
                MinRequestsQueued = int.MaxValue;
            }
            public int NumRequests;
            public int CpuMSec;
            public int ContextSwitch;
            public double DiskIOMsec;         // The amount of Disk service time (all disks, machine wide).  

            public double RequestsMSecMax;
            public double RequestsTimeOfMax;
            public int RequestsThreadOfMax;

            public double DurationMSecTotal;
            public double QueuedDurationMSecTotal;

            public int ThreadPoolThreadCountSum;
            public int ThreadPoolAdjustmentCount;
            public double MeanThreadPoolThreads { get { return (double)ThreadPoolThreadCountSum / ThreadPoolAdjustmentCount; } }

            public double GCHeapAllocMB;
            public double GCHeapSizeMB;
            public double NumGcs;
            public double NumGen2Gcs;
            public int Contentions;
            public int MinRequestsQueued;
            public double MeanRequestsProcessing { get { return MeanRequestsProcessingSum / MeanRequestsProcessingCount; } }

            internal double MeanRequestsProcessingSum;
            internal int MeanRequestsProcessingCount;
        };

        private class ByRequestStats
        {
            public ByRequestStats(AspNetRequest request)
            {
                MaxRequest = request;
                AddRequest(request);
            }
            public void AddRequest(AspNetRequest request)
            {
                if (request.DurationMSec > MaxRequest.DurationMSec)
                {
                    MaxRequest = request;
                }

                TotalDurationMSec += request.DurationMSec;
                Debug.Assert(request.DurationMSec >= 0);
                Debug.Assert(TotalDurationMSec >= 0);
                NumRequests++;
                if (request.DurationMSec > 1000)
                {
                    NumRequest1Sec++;
                }

                if (request.DurationMSec > 5000)
                {
                    NumRequest5Sec++;
                }

                if (request.DurationMSec > 10000)
                {
                    NumRequest10Sec++;
                }
            }
            public double MeanRequestMSec { get { return TotalDurationMSec / NumRequests; } }

            public int NumRequest1Sec;
            public int NumRequest5Sec;
            public int NumRequest10Sec;

            public AspNetRequest MaxRequest;
            public double TotalDurationMSec;
            public int NumRequests;
        }

        private static int GetBucket(double timeStampMSec, int startIntervalMSec, int bucketIntervalMSec, int maxBucket)
        {
            if (timeStampMSec < startIntervalMSec)
            {
                return -1;
            }

            int idx = (int)(timeStampMSec / bucketIntervalMSec);
            if (idx >= maxBucket)
            {
                return -1;
            }

            return idx;
        }


        private void CreateCSVFile(List<AspNetRequest> requests, string csvFileName)
        {
            using (var csvFile = File.CreateText(csvFileName))
            {
                string listSeparator = Thread.CurrentThread.CurrentCulture.TextInfo.ListSeparator;
                csvFile.WriteLine("Method{0}Path{0}QueryString{0}StartMSec{0}DurationMSec{0}ProcStartMSec{0}ProcessingMSec{0}ProcessID{0}ProcThread{0}Received{0}Processing{0}Queued", listSeparator);
                foreach (var request in requests)
                {
                    if (request.StopTimeRelativeMSec == 0)       // Skip incomplete entries
                    {
                        continue;
                    }

                    csvFile.WriteLine("{1}{0}{2}{0}{3}{0}{4:f3}{0}{5:f2}{0}{6:f3}{0}{7:f2}{0}{8}{0}{9}{0}{10}{0}{11}{0}{12}", listSeparator,
                        request.Method, EventWindow.EscapeForCsv(request.Path, ","), EventWindow.EscapeForCsv(request.QueryString, ","),
                        request.StartTimeRelativeMSec, request.DurationMSec, request.HandlerStartTimeRelativeMSec, request.HandlerDurationMSec,
                        (request.Process != null) ? request.Process.ProcessID : 0, request.HandlerThreadID, request.RequestsReceived,
                        request.RequestsProcessing, request.RequestsQueued);
                }
            }
        }

        private List<AspNetRequest> m_requests;
        #endregion
    }

    public class PerfViewEventStats : PerfViewHtmlReport
    {
        public PerfViewEventStats(PerfViewFile dataFile) : base(dataFile, "EventStats") { }
        protected override void WriteHtmlBody(TraceLog dataFile, TextWriter writer, string fileName, TextWriter log)
        {
            m_counts = new List<TraceEventCounts>(dataFile.Stats);
            // Sort by count
            m_counts.Sort((x, y) => y.Count - x.Count);
            writer.WriteLine("<H2>Event Statistics</H2>");
            writer.WriteLine("<UL>");
            writer.WriteLine("<LI> <A HREF=\"command:excel\">View Event Statistics in Excel</A></LI>");
            writer.WriteLine("<LI>Total Event Count = {0:n0}</LI>", dataFile.EventCount);
            writer.WriteLine("<LI>Total Lost Events = {0:n0}</LI>", dataFile.EventsLost);
            writer.WriteLine("</UL>");

            writer.WriteLine("<Table Border=\"1\">");
            writer.Write("<TR>");
            writer.Write("<TH Align=\"Center\">Name</TH>");
            writer.Write("<TH Align=\"Center\" Title=\"The number of times this event occurs in the log.\">Count</TH>");
            writer.Write("<TH Align=\"Center\" Title=\"The average size of just the payload of this event.\">Average<BR/>Data Size</TH>");
            writer.Write("<TH Align=\"Center\" Title=\"The number of times this event has a stack trace associated with it.\">Stack<BR/>Count</TH>");
            writer.WriteLine("</TR>");
            foreach (TraceEventCounts count in m_counts)
            {
                writer.Write("<TR>");
                writer.Write("<TD Align=\"Left\">{0}/{1}</TD>", count.ProviderName, count.EventName);
                writer.Write("<TD Align=\"Right\">{0:n0}</TD>", count.Count);
                writer.Write("<TD Align=\"Right\">{0:n0}</TD>", count.AveragePayloadSize);
                writer.Write("<TD Align=\"Right\">{0:n0}</TD>", count.StackCount);
                writer.WriteLine("</TR>");
            }
            writer.WriteLine("</Table>");
        }
        protected override string DoCommand(string command, StatusBar worker)
        {
            if (command == "excel")
            {
                var csvFile = CacheFiles.FindFile(FilePath, ".eventstats.csv");
                if (!File.Exists(csvFile) || File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(FilePath) ||
                    File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(SupportFiles.MainAssemblyPath))
                {
                    //make the csv
                    MakeEventStatCsv(m_counts, csvFile);
                }
                Command.Run(Command.Quote(csvFile), new CommandOptions().AddStart().AddTimeout(CommandOptions.Infinite));
                System.Threading.Thread.Sleep(500);     // Give it time to start a bit.  
                return "Opening CSV " + csvFile;
            }
            return null;
        }
        #region private
        private List<TraceEventCounts> m_counts;
        private void MakeEventStatCsv(List<TraceEventCounts> trace, string filepath)
        {
            string listSeparator = Thread.CurrentThread.CurrentCulture.TextInfo.ListSeparator;
            using (var writer = File.CreateText(filepath))
            {
                writer.WriteLine("Name{0}Count{0}AverageSize{0}StackCount", listSeparator);
                foreach (TraceEventCounts count in trace)
                {
                    writer.Write("{0}/{1}{2}", count.ProviderName, count.EventName, listSeparator);
                    writer.Write("{0}{1}", count.Count, listSeparator);
                    writer.Write("{0:f0}{1}", count.AveragePayloadSize, listSeparator);
                    writer.WriteLine("{0}", count.StackCount);
                }
            }
        }
        #endregion
    }

    public class PerfViewGCStats : PerfViewHtmlReport
    {
        public PerfViewGCStats(PerfViewFile dataFile) : base(dataFile, "GCStats") { }
        protected override string DoCommand(string command, StatusBar worker)
        {
            if (command.StartsWith("excel/"))
            {
                string raw = "";
                var rest = command.Substring(6);
                if (rest.StartsWith("perGeneration/"))
                {
                    raw = ".perGen";
                    rest = rest.Substring(14);
                }
                var processId = int.Parse(rest);
                if (m_gcStats.ContainsKey(processId))
                {
                    var gcProc = m_gcStats[processId];
                    var mang = Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.LoadedDotNetRuntime(gcProc);
                    var csvFile = CacheFiles.FindFile(FilePath, ".gcStats." + processId.ToString() + raw + ".csv");
                    if (!File.Exists(csvFile) || File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(FilePath) ||
                        File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(SupportFiles.MainAssemblyPath))
                    {
                        if (raw.Length != 0)
                        {
                            Stats.GcStats.PerGenerationCsv(csvFile, mang);
                        }
                        else
                        {
                            Stats.GcStats.ToCsv(csvFile, mang);
                        }
                    }
                    Command.Run(Command.Quote(csvFile), new CommandOptions().AddStart().AddTimeout(CommandOptions.Infinite));
                    System.Threading.Thread.Sleep(500);     // Give it time to start a bit.  
                    return "Opening CSV " + csvFile;
                }
            }
            else if (command.StartsWith("excelFinalization/"))
            {
                var processId = int.Parse(command.Substring(18));
                if (m_gcStats.ContainsKey(processId))
                {
                    var gcProc = m_gcStats[processId];
                    var mang = Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.LoadedDotNetRuntime(gcProc);
                    var csvFile = CacheFiles.FindFile(FilePath, ".gcStats.Finalization." + processId.ToString() + ".csv");
                    if (!File.Exists(csvFile) || File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(FilePath) ||
                        File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(SupportFiles.MainAssemblyPath))
                    {
                        Stats.GcStats.ToCsvFinalization(csvFile, mang);
                    }
                    Command.Run(Command.Quote(csvFile), new CommandOptions().AddStart().AddTimeout(CommandOptions.Infinite));
                    System.Threading.Thread.Sleep(500);     // Give it time to start a bit.  
                    return "Opening CSV " + csvFile;
                }
            }
            else if (command.StartsWith("xml/"))
            {
                var processId = int.Parse(command.Substring(4));
                if (m_gcStats.ContainsKey(processId) && Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.LoadedDotNetRuntime(m_gcStats[processId]).GC.Stats().HasDetailedGCInfo)
                {
                    var gcProc = m_gcStats[processId];
                    var mang = Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.LoadedDotNetRuntime(gcProc);
                    var xmlOutputName = CacheFiles.FindFile(FilePath, ".gcStats." + processId.ToString() + ".xml");
                    var csvFile = CacheFiles.FindFile(FilePath, ".gcStats." + processId.ToString() + ".csv");
                    if (!File.Exists(xmlOutputName) || File.GetLastWriteTimeUtc(xmlOutputName) < File.GetLastWriteTimeUtc(FilePath) ||
                        File.GetLastWriteTimeUtc(xmlOutputName) < File.GetLastWriteTimeUtc(SupportFiles.MainAssemblyPath))
                    {
                        using (var writer = File.CreateText(xmlOutputName))
                        {
                            Stats.GcStats.ToXml(writer, gcProc, mang, "");
                        }
                    }

                    // TODO FIX NOW Need a way of viewing it.  
                    var viewer = Command.FindOnPath("xmlView");
                    if (viewer == null)
                    {
                        viewer = "notepad";
                    }

                    Command.Run(viewer + " " + Command.Quote(xmlOutputName),
                        new CommandOptions().AddStart().AddTimeout(CommandOptions.Infinite).AddNoThrow());
                    return viewer + " launched on " + xmlOutputName;
                }
            }
            return "Unknown command " + command;
        }

        protected override void WriteHtmlBody(TraceLog dataFile, TextWriter writer, string fileName, TextWriter log)
        {
            using (var source = dataFile.Events.GetSource())
            {
                m_gcStats = new Dictionary<int, Microsoft.Diagnostics.Tracing.Analysis.TraceProcess>();
                Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.NeedLoadedDotNetRuntimes(source);
                Microsoft.Diagnostics.Tracing.Analysis.TraceProcessesExtensions.AddCallbackOnProcessStart(source, proc =>
                {
                    Microsoft.Diagnostics.Tracing.Analysis.TraceProcessesExtensions.SetSampleIntervalMSec(proc, dataFile.SampleProfileInterval.TotalMilliseconds);
                    proc.Log = dataFile;
                });
                source.Process();
                foreach (var proc in Microsoft.Diagnostics.Tracing.Analysis.TraceProcessesExtensions.Processes(source))
                {
                    if (!m_gcStats.ContainsKey(proc.ProcessID) && Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.LoadedDotNetRuntime(proc) != null)
                    {
                        m_gcStats.Add(proc.ProcessID, proc);
                    }
                }

                Stats.ClrStats.ToHtml(writer, m_gcStats.Values.ToList(), fileName, "GCStats", Stats.ClrStats.ReportType.GC, true);
            }
        }

        private Dictionary<int/*pid*/, Microsoft.Diagnostics.Tracing.Analysis.TraceProcess> m_gcStats;
    }

    public class PerfViewRuntimeLoaderStats : PerfViewHtmlReport
    {
        public PerfViewRuntimeLoaderStats(PerfViewFile dataFile) : base(dataFile, "Runtime Loader") { }
        protected override string DoCommand(Uri commandUri, StatusBar worker, out Action continuation)
        {
            continuation = null;

            string command = commandUri.LocalPath;
            string textStr = "txt/";
            string csvStr = "csv/";
            bool text = command.StartsWith(textStr);
            bool csv = command.StartsWith(csvStr);

            if (text || csv)
            {
                var rest = command.Substring(textStr.Length);


                bool tree = true;
                List<string> filters = null;
                if (!String.IsNullOrEmpty(commandUri.Query))
                {
                    filters = new List<string>();
                    tree = commandUri.Query.Contains("TreeView");
                    if (commandUri.Query.Contains("JIT"))
                        filters.Add("JIT");
                    if (commandUri.Query.Contains("R2R_Found"))
                        filters.Add("R2R_Found");
                    if (commandUri.Query.Contains("R2R_Failed"))
                        filters.Add("R2R_Failed");
                    if (commandUri.Query.Contains("TypeLoad"))
                        filters.Add("TypeLoad");
                    if (commandUri.Query.Contains("AssemblyLoad"))
                        filters.Add("AssemblyLoad");
                }
                string identifier = $"{(tree?"Tree":"Flat")}_";
                if (filters != null)
                {
                    foreach (var filter in filters)
                    {
                        identifier = identifier + "_" + filter;
                    }
                }

                var startMSec = double.Parse(rest.Substring(rest.IndexOf(',') + 1));
                var processId = int.Parse(rest.Substring(0, rest.IndexOf(',')));
                var processData = m_runtimeData.GetProcessDataFromProcessIDAndTimestamp(processId, startMSec);

                var txtFile = CacheFiles.FindFile(FilePath, ".runtimeLoaderstats." + processId.ToString() + "_" + ((long)startMSec).ToString() + "_" + identifier + (csv ? ".csv" : ".txt"));
                if (!File.Exists(txtFile) || File.GetLastWriteTimeUtc(txtFile) < File.GetLastWriteTimeUtc(FilePath) ||
                    File.GetLastWriteTimeUtc(txtFile) < File.GetLastWriteTimeUtc(SupportFiles.MainAssemblyPath))
                {
                    Stats.RuntimeLoaderStats.ToTxt(txtFile, processData, filters.ToArray(), tree);
                }
                Command.Run(Command.Quote(txtFile), new CommandOptions().AddStart().AddTimeout(CommandOptions.Infinite));
                System.Threading.Thread.Sleep(500);     // Give it time to start a bit.  
                return "Opening Txt " + txtFile;
            }
            return "Unknown command " + command;
        }

        protected override void WriteHtmlBody(TraceLog dataFile, TextWriter writer, string fileName, TextWriter log)
        {
            using (var source = dataFile.Events.GetSource())
            {
                Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.NeedLoadedDotNetRuntimes(source);
                CLRRuntimeActivityComputer runtimeLoaderComputer = new CLRRuntimeActivityComputer(source);
                source.Process();
                m_runtimeData = runtimeLoaderComputer.RuntimeLoaderData;
                Stats.ClrStats.ToHtml(writer, Microsoft.Diagnostics.Tracing.Analysis.TraceProcessesExtensions.Processes(source).ToList(), fileName, "Runtime Loader", Stats.ClrStats.ReportType.RuntimeLoader, true, runtimeOpsStats : m_runtimeData);
            }
        }

        private RuntimeLoaderStatsData m_runtimeData;
    }

    public class PerfViewJitStats : PerfViewHtmlReport
    {
        public PerfViewJitStats(PerfViewFile dataFile) : base(dataFile, "JITStats") { }
        protected override string DoCommand(string command, StatusBar worker)
        {
            if (command.StartsWith("excel/"))
            {
                var rest = command.Substring(6);
                var processId = int.Parse(rest);
                if (m_jitStats.ContainsKey(processId))
                {
                    var jitProc = m_jitStats[processId];
                    var mang = Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.LoadedDotNetRuntime(jitProc);
                    var csvFile = CacheFiles.FindFile(FilePath, ".jitStats." + processId.ToString() + ".csv");
                    if (!File.Exists(csvFile) || File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(FilePath) ||
                        File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(SupportFiles.MainAssemblyPath))
                    {
                        Stats.JitStats.ToCsv(csvFile, mang);
                    }

                    Command.Run(Command.Quote(csvFile), new CommandOptions().AddStart().AddTimeout(CommandOptions.Infinite));
                    System.Threading.Thread.Sleep(500);     // Give it time to start a bit.  
                    return "Opening CSV " + csvFile;
                }
            }
            else if (command.StartsWith("excelInlining/"))
            {
                var rest = command.Substring(14);
                var processId = int.Parse(rest);
                if (m_jitStats.ContainsKey(processId))
                {
                    var jitProc = m_jitStats[processId];
                    var mang = Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.LoadedDotNetRuntime(jitProc);
                    var csvFile = CacheFiles.FindFile(FilePath, ".jitInliningStats." + processId.ToString() + ".csv");
                    if (!File.Exists(csvFile) || File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(FilePath) ||
                        File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(SupportFiles.MainAssemblyPath))
                    {
                        Stats.JitStats.ToInliningCsv(csvFile, mang);
                    }

                    Command.Run(Command.Quote(csvFile), new CommandOptions().AddStart().AddTimeout(CommandOptions.Infinite));
                    System.Threading.Thread.Sleep(500);     // Give it time to start a bit.  
                    return "Opening CSV " + csvFile;
                }
            }
            else if (command.StartsWith("excelBackgroundDiag/"))
            {
                var rest = command.Substring(20);
                var processId = int.Parse(rest);
                if (m_jitStats.ContainsKey(processId))
                {
                    var jitProc = m_jitStats[processId];
                    var mang = Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.LoadedDotNetRuntime(jitProc);
                    List<object> events = m_bgJitEvents[processId];
                    var csvFile = CacheFiles.FindFile(FilePath, ".BGjitStats." + processId.ToString() + ".csv");
                    if (!File.Exists(csvFile) || File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(FilePath) ||
                        File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(SupportFiles.MainAssemblyPath))
                    {
                        Stats.JitStats.BackgroundDiagCsv(csvFile, mang, events);
                    }

                    Command.Run(Command.Quote(csvFile), new CommandOptions().AddStart().AddTimeout(CommandOptions.Infinite));
                    System.Threading.Thread.Sleep(500);     // Give it time to start a bit.  
                    return "Opening CSV " + csvFile;
                }
            }

            return "Unknown command " + command;
        }

        protected override void WriteHtmlBody(TraceLog dataFile, TextWriter output, string fileName, TextWriter log)
        {
            var source = dataFile.Events.GetSource();

            m_jitStats = new Dictionary<int, Microsoft.Diagnostics.Tracing.Analysis.TraceProcess>();
            m_bgJitEvents = new Dictionary<int, List<object>>();

            // attach callbacks to grab background JIT events
            var clrPrivate = new ClrPrivateTraceEventParser(source);
            clrPrivate.ClrMulticoreJitCommon += delegate (MulticoreJitPrivateTraceData data)
            {
                if (!m_bgJitEvents.ContainsKey(data.ProcessID))
                {
                    m_bgJitEvents.Add(data.ProcessID, new List<object>());
                }

                m_bgJitEvents[data.ProcessID].Add(data.Clone());
            };
            source.Clr.LoaderModuleLoad += delegate (ModuleLoadUnloadTraceData data)
            {
                if (!m_bgJitEvents.ContainsKey(data.ProcessID))
                {
                    m_bgJitEvents.Add(data.ProcessID, new List<object>());
                }

                m_bgJitEvents[data.ProcessID].Add(data.Clone());
            };

            // process the model
            Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.NeedLoadedDotNetRuntimes(source);
            source.Process();
            foreach (var proc in Microsoft.Diagnostics.Tracing.Analysis.TraceProcessesExtensions.Processes(source))
            {
                if (Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.LoadedDotNetRuntime(proc) != null && !m_jitStats.ContainsKey(proc.ProcessID))
                {
                    m_jitStats.Add(proc.ProcessID, proc);
                }
            }

            Stats.ClrStats.ToHtml(output, m_jitStats.Values.ToList(), fileName, "JITStats", Stats.ClrStats.ReportType.JIT, true);
        }

        private Dictionary<int /*pid*/, Microsoft.Diagnostics.Tracing.Analysis.TraceProcess> m_jitStats;
        private Dictionary<int /*pid*/, List<object>> m_bgJitEvents;
    }

    /// <summary>
    /// Represents all the heap snapshots in the trace
    /// </summary>
    public class PerfViewHeapSnapshots : PerfViewTreeItem
    {
        public PerfViewHeapSnapshots(PerfViewFile file)
        {
            Name = "GC Heap Snapshots";
            DataFile = file;
        }

        public virtual string Title { get { return Name + " for " + DataFile.Title; } }
        public PerfViewFile DataFile { get; private set; }
        public override string FilePath { get { return DataFile.FilePath; } }

        /// <summary>
        /// Open the file (This might be expensive (but maybe not).  This should populate the Children property 
        /// too.  
        /// </summary>
        public override void Open(Window parentWindow, StatusBar worker, Action doAfter)
        {
            if (m_Children == null)
            {
                var newChildren = new List<PerfViewTreeItem>();
                worker.StartWork("Searching for heap dumps in " + Name, delegate ()
                {
                    TraceLog traceLog = null;
                    if (DataFile is ETLPerfViewData)
                    {
                        traceLog = ((ETLPerfViewData)DataFile).GetTraceLog(worker.LogWriter);
                    }
                    else if (DataFile is EventPipePerfViewData)
                    {
                        traceLog = ((EventPipePerfViewData)DataFile).GetTraceLog(worker.LogWriter);
                    }
                    var source = traceLog.Events.GetSource();
                    var jsHeapParser = new JSDumpHeapTraceEventParser(source);


                    // For .NET, we are looking for a Gen 2 GC Start that is induced that has GCBulkNodes after it.   
                    var lastGCStartsRelMSec = new Dictionary<int, double>();

                    source.Clr.GCGenAwareStart += delegate (Microsoft.Diagnostics.Tracing.Parsers.Clr.GenAwareBeginTraceData data)
                    {
                        lastGCStartsRelMSec[data.ProcessID] = data.TimeStampRelativeMSec;
                    };

                    source.Clr.GCStart += delegate (Microsoft.Diagnostics.Tracing.Parsers.Clr.GCStartTraceData data)
                    {
                        // Look for induced GCs.  and remember their when it happened.    
                        if (data.Depth == 2 && data.Reason == GCReason.Induced)
                        {
                            lastGCStartsRelMSec[data.ProcessID] = data.TimeStampRelativeMSec;
                        }
                    };
                    source.Clr.GCBulkNode += delegate (GCBulkNodeTraceData data)
                    {
                        double lastGCStartRelMSec;
                        if (lastGCStartsRelMSec.TryGetValue(data.ProcessID, out lastGCStartRelMSec))
                        {
                            var processName = "";
                            var process = data.Process();
                            if (process != null)
                            {
                                processName = process.Name;
                            }

                            newChildren.Add(new PerfViewHeapSnapshot(DataFile, data.ProcessID, processName, lastGCStartRelMSec, ".NET"));

                            lastGCStartsRelMSec.Remove(data.ProcessID);     // Remove it since so we ignore the rest of the node events.  
                        }
                    };

                    jsHeapParser.JSDumpHeapEnvelopeStart += delegate (SettingsTraceData data)
                    {
                        var processName = "";
                        var process = data.Process();
                        if (process != null)
                        {
                            processName = process.Name;
                        }

                        newChildren.Add(new PerfViewHeapSnapshot(DataFile, data.ProcessID, processName, data.TimeStampRelativeMSec, "JS"));
                    };
                    source.Process();

                    worker.EndWork(delegate ()
                    {
                        m_Children = newChildren;
                        FirePropertyChanged("Children");
                        doAfter?.Invoke();
                    });
                });
            }

            doAfter?.Invoke();
        }
        /// <summary>
        /// Close the file
        /// </summary>
        public override void Close() { }

        public override ImageSource Icon { get { return GuiApp.MainWindow.Resources["FolderOpenBitmapImage"] as ImageSource; } }
    }

    /// <summary>
    /// Represents a single heap snapshot in a ETL file (currently only JScript).  
    /// </summary>
    internal class PerfViewHeapSnapshot : HeapDumpPerfViewFile
    {
        /// <summary>
        /// snapshotKinds should be .NET or JS
        /// </summary>
        public PerfViewHeapSnapshot(PerfViewFile file, int processId, string processName, double timeRelativeMSec, string snapshotKind)
        {
            m_snapshotKind = snapshotKind;
            m_timeRelativeMSec = timeRelativeMSec;
            m_filePath = file.FilePath;
            Kind = snapshotKind;
            m_processId = processId;
            Name = snapshotKind + " Heap Snapshot " + processName + "(" + processId + ") at " + timeRelativeMSec.ToString("n3") + " MSec";
        }
        public override string HelpAnchor { get { return "JSHeapSnapshot"; } }
        public string Kind { get; private set; }

        internal string m_snapshotKind;
        internal double m_timeRelativeMSec;
        internal int m_processId;
    };

    public class PerfViewEventSource : PerfViewTreeItem
    {
        public PerfViewEventSource(PerfViewFile dataFile)
        {
            DataFile = dataFile;
            Name = "Events";
        }

        public PerfViewEventSource(ETWEventSource source)
        {
        }
        public virtual string Title { get { return "Events " + DataFile.Title; } }
        public PerfViewFile DataFile { get; private set; }
        public EventWindow Viewer { get; internal set; }
        public virtual EventSource GetEventSource()
        {
            Debug.Assert(m_eventSource != null, "Open must be called first");
            if (m_needClone)
            {
                return m_eventSource.Clone();
            }

            m_needClone = true;
            return m_eventSource;
        }
        public override string FilePath { get { return DataFile.FilePath; } }
        public override void Open(Window parentWindow, StatusBar worker, Action doAfter)
        {
            if (Viewer == null || !DataFile.IsUpToDate)
            {
                worker.StartWork("Opening " + Name, delegate ()
                {
                    if (m_eventSource == null || !DataFile.IsUpToDate)
                    {
                        m_eventSource = DataFile.OpenEventSourceImpl(worker.LogWriter);
                    }

                    worker.EndWork(delegate ()
                    {
                        if (m_eventSource == null)
                        {
                            throw new ApplicationException("Not a file type that supports the EventView.");
                        }

                        Viewer = new EventWindow(parentWindow, this);
                        Viewer.Show();
                        doAfter?.Invoke();
                    });
                });
            }
            else
            {
                Viewer.Focus();
                doAfter?.Invoke();
            }
        }
        public override void Close() { }
        public override ImageSource Icon { get { return GuiApp.MainWindow.Resources["EventSourceBitmapImage"] as ImageSource; } }

        #region private
        internal EventSource m_eventSource;     // TODO internal is a hack
        private bool m_needClone;           // After giving this out the first time, we need to clone it 
        #endregion
    }

    public class PerfViewStackSource : PerfViewTreeItem
    {
        public PerfViewStackSource(PerfViewFile dataFile, string sourceName)
        {
            DataFile = dataFile;
            SourceName = sourceName;
            if (sourceName.EndsWith(" TaskTree"))   // Special case, call it 'TaskTree' to make it clearer that it is not a call stack
            {
                Name = SourceName;
            }
            else
            {
                Name = SourceName + " Stacks";
            }
        }
        public PerfViewFile DataFile { get; private set; }
        public string SourceName { get; private set; }
        public StackWindow Viewer { get; internal set; }
        public override string HelpAnchor { get { return SourceName.Replace(" ", "") + "Stacks"; } }
        public virtual string Title { get { return SourceName + " Stacks " + DataFile.Title; } }
        public virtual StackSource GetStackSource(TextWriter log, double startRelativeMSec = 0, double endRelativeMSec = double.PositiveInfinity)
        {
            if (m_StackSource != null && DataFile.IsUpToDate && startRelativeMSec == 0 && endRelativeMSec == double.PositiveInfinity)
            {
                return m_StackSource;
            }

            StackSource ret = DataFile.OpenStackSourceImpl(SourceName, log, startRelativeMSec, endRelativeMSec);
            if (ret == null)
            {
                ret = DataFile.OpenStackSourceImpl(log);
            }

            if (ret == null)
            {
                throw new ApplicationException("Not a file type that supports the StackView.");
            }

            if (startRelativeMSec == 0 && endRelativeMSec == double.PositiveInfinity)
            {
                m_StackSource = ret;
            }

            return ret;
        }

        // TODO not clear I want this method 
        protected virtual StackSource OpenStackSource(
            string streamName, TextWriter log, double startRelativeMSec = 0, double endRelativeMSec = double.PositiveInfinity, Predicate<TraceEvent> predicate = null)
        {
            return DataFile.OpenStackSourceImpl(streamName, log, startRelativeMSec, endRelativeMSec, predicate);
        }
        // TODO not clear I want this method (client could do it).  
        protected virtual void SetProcessFilter(string incPat)
        {
            Viewer.IncludeRegExTextBox.Text = incPat;
        }
        protected internal virtual StackSource OpenStackSourceImpl(TextWriter log) { return DataFile.OpenStackSourceImpl(log); }
        public override string FilePath { get { return DataFile.FilePath; } }
        public override void Open(Window parentWindow, StatusBar worker, Action doAfter = null)
        {
            // The OS Heap Alloc stack source has logic to look up type names from PDBs, we only do this
            // lookup when we initially create the stack source.  To allow the user to fetch more PDBs and 
            // try again, we remove the caching of the StackSource so re-opening recomputes the stack source.   
            if (Name.StartsWith("Net OS Heap Alloc"))
                m_StackSource = null;

            if (Viewer == null || !DataFile.IsUpToDate)
            {
                worker.StartWork("Opening " + Name, delegate ()
                {
                    if (m_StackSource == null || !DataFile.IsUpToDate)
                    {
                        // Compute the stack events
                        m_StackSource = OpenStackSource(SourceName, worker.LogWriter);
                        if (m_StackSource == null)
                        {
                            m_StackSource = OpenStackSourceImpl(worker.LogWriter);
                        }

                        if (m_StackSource == null)
                        {
                            throw new ApplicationException("Not a file type that supports the StackView.");
                        }
                    }

                    // Get the process summary if needed. 
                    List<IProcess> processes = null;
                    // TODO Using the source name here is a bit of hack.  Heap Allocations, however are already filtered to a process. 
                    if (DataFile.SupportsProcesses && SourceName != "Net OS Heap Alloc")
                    {
                        worker.Log("[Computing the processes involved in the trace.]");
                        processes = DataFile.GetProcesses(worker.LogWriter);
                    }

                    worker.EndWork(delegate ()
                    {
                        // This is the action that happens either after select process or after the stacks are computed.  
                        Action<List<IProcess>> launchViewer = delegate (List<IProcess> selectedProcesses)
                        {
                            Viewer = new StackWindow(parentWindow, this);
                            ConfigureStackWindow(Viewer);
                            Viewer.Show();

                            List<int> processIDs = null;
                            if (selectedProcesses != null && selectedProcesses.Count != 0)
                            {
                                processIDs = new List<int>();
                                string incPat = "";
                                foreach (var process in selectedProcesses)
                                {
                                    if (incPat.Length != 0)
                                    {
                                        incPat += "|";
                                    }

                                    incPat += DataFile.GetProcessIncPat(process);

                                    if (process.ProcessID != default) // process ID is not always available
                                    {
                                        processIDs.Add(process.ProcessID);
                                    }
                                }
                                SetProcessFilter(incPat);
                            }

                            Viewer.StatusBar.StartWork("Looking up high importance PDBs that are locally cached", delegate
                            {
                                // TODO This is probably a hack that it is here.  
                                var etlDataFile = DataFile as ETLPerfViewData;
                                TraceLog traceLog = null;
                                if (etlDataFile != null)
                                {
                                    var moduleFiles = ETLPerfViewData.GetInterestingModuleFiles(etlDataFile, 5.0, Viewer.StatusBar.LogWriter, processIDs);
                                    traceLog = etlDataFile.GetTraceLog(Viewer.StatusBar.LogWriter);
                                    using (var reader = etlDataFile.GetSymbolReader(Viewer.StatusBar.LogWriter,
                                        SymbolReaderOptions.CacheOnly | SymbolReaderOptions.NoNGenSymbolCreation))
                                    {
                                        foreach (var moduleFile in moduleFiles)
                                        {
                                            // TODO FIX NOW don't throw exceptions, 
                                            Viewer.StatusBar.Log("[Quick lookup of " + moduleFile.Name + "]");
                                            traceLog.CodeAddresses.LookupSymbolsForModule(reader, moduleFile);
                                        }
                                    }
                                }
                                Viewer.StatusBar.EndWork(delegate
                                {
                                    // Catch the error if you don't merge and move to a new machine.  
                                    if (traceLog != null && !traceLog.CurrentMachineIsCollectionMachine() && !traceLog.HasPdbInfo)
                                    {
                                        MessageBox.Show(parentWindow,
                                            "Warning!   This file was not merged and was moved from the collection\r\n" +
                                            "machine.  This means the data is incomplete and symbolic name resolution\r\n" +
                                            "will NOT work.  The recommended fix is use the perfview (not windows OS)\r\n" +
                                            "zip command.  Right click on the file in the main view and select ZIP.\r\n" +
                                            "\r\n" +
                                            "See merging and zipping in the users guide for more information.",
                                            "Data not merged before leaving the machine!");
                                    }

                                    Viewer.SetStackSource(m_StackSource, delegate ()
                                    {
                                        worker.Log("Opening Viewer.");
                                        if (WarnAboutBrokenStacks(Viewer, Viewer.StatusBar.LogWriter))
                                        {
                                            // TODO, WPF leaves blank regions after the dialog box is dismissed.  
                                            // Force a redraw by changing the size.  This should not be needed.   
                                            var width = Viewer.Width;
                                            Viewer.Width = width - 1;
                                            Viewer.Width = width;
                                        }
                                        FirstAction(Viewer);
                                        doAfter?.Invoke();
                                    });
                                });
                            });
                        };

                        if (processes != null && !SkipSelectProcess)
                        {
                            if (DataFile.InitiallyIncludedProcesses == null)
                            {
                                m_SelectProcess = new SelectProcess(parentWindow, processes, new TimeSpan(1, 0, 0), delegate (List<IProcess> selectedProcesses)
                                {
                                    launchViewer(selectedProcesses);
                                }, hasAllProc: true);
                                m_SelectProcess.Show();
                            }
                            else
                            {
                                launchViewer(processes.Where(p => DataFile.InitiallyIncludedProcesses
                                    .Any(iip => string.Equals(p.Name, iip, StringComparison.OrdinalIgnoreCase)))
                                    .ToList());
                            }
                        }
                        else
                        {
                            launchViewer(null);
                        }
                    });
                });
            }
            else
            {
                Viewer.Focus();
                doAfter?.Invoke();
            }
        }

        public override void Close() { }
        protected internal virtual void ConfigureStackWindow(StackWindow stackWindow)
        {
            DataFile.ConfigureStackWindow(SourceName, stackWindow);
        }
        protected internal virtual void FirstAction(StackWindow stackWindow)
        {
            DataFile.FirstAction(stackWindow);
        }
        public override ImageSource Icon { get { return GuiApp.MainWindow.Resources["StackSourceBitmapImage"] as ImageSource; } }

        // If set, we don't show the process selection dialog.  
        public bool SkipSelectProcess;
        #region private
        internal void ViewClosing(StackWindow viewer)
        {
            Viewer = null;
            DataFile.StackSourceClosing(this);
        }

        private bool WarnAboutBrokenStacks(Window parentWindow, TextWriter log)
        {
            if (!m_WarnedAboutBrokenStacks)
            {
                m_WarnedAboutBrokenStacks = true;
                double brokenPercent = Viewer.CallTree.Root.GetBrokenStackCount() * 100 / Viewer.CallTree.Root.InclusiveCount;
                if (brokenPercent > 0)
                {
                    bool is64bit = false;
                    foreach (var child in Viewer.CallTree.Root.Callees)
                    {
                        // if there is any process we can't determine is 64 bit, then we assume it might be.  
                        if (!child.Name.StartsWith("Process32 "))
                        {
                            is64bit = true;
                        }
                    }
                    return WarnAboutBrokenStacks(parentWindow, brokenPercent, is64bit, log);
                }
            }
            return false;
        }
        private static bool WarnAboutBrokenStacks(Window parentWindow, double brokenPercent, bool is64Bit, TextWriter log)
        {
            if (brokenPercent > 1)
            {
                log.WriteLine("Finished aggregating stacks.  (" + brokenPercent.ToString("f1") + "% Broken Stacks)");
            }

            if (brokenPercent > 10)
            {
                if (is64Bit)
                {
                    MessageBox.Show(parentWindow, "Warning: There are " + brokenPercent.ToString("f1") +
                        "% stacks that are broken, analysis is suspect." + "\r\n" +
                        "This is likely due the current inability of the OS stackwalker to walk 64 bit\r\n" +
                        "code that is dynamically (JIT) generated.\r\n\r\n" +
                        "This can be worked around by either by NGENing the EXE,\r\n" +
                        "forcing the EXE to run as a 32 bit app, profiling on Windows 8\r\n" +
                        "or avoiding any top-down analysis.\r\n\r\n" +
                        "Use the troubleshooting link at the top of the view for more information.\r\n",
                        "Broken Stacks");
                }
                else
                {
                    MessageBox.Show(parentWindow, "Warning: There are " + brokenPercent.ToString("f1") + "% stacks that are broken\r\n" +
                        "Top down analysis is suspect, however bottom up approaches are still valid.\r\n\r\n" +
                        "Use the troubleshooting link at the top of the view for more information.\r\n",
                        "Broken Stacks");
                }

                return true;
            }
            return false;
        }

        internal StackSource m_StackSource;
        internal SelectProcess m_SelectProcess;
        private bool m_WarnedAboutBrokenStacks;
        #endregion
    }

    internal class DiffPerfViewData : PerfViewStackSource
    {
        public DiffPerfViewData(PerfViewStackSource data, PerfViewStackSource baseline)
            : base(data.DataFile, data.SourceName)
        {
            m_baseline = baseline;
            m_data = data;
            Name = string.Format("Diff {0} baseline {1}", data.Name, baseline.Name);
        }

        public override string Title
        {
            get
            {
                // TODO do better. 
                return Name;
            }
        }
        public override StackSource GetStackSource(TextWriter log, double startRelativeMSec = 0, double endRelativeMSec = double.PositiveInfinity)
        {
            return InternStackSource.Diff(m_data.GetStackSource(log, startRelativeMSec, endRelativeMSec), m_baseline.GetStackSource(log, startRelativeMSec, endRelativeMSec));
        }

        #region private
        private PerfViewStackSource m_data;
        private PerfViewStackSource m_baseline;
        #endregion
    }

    /// <summary>
    /// These are the data Templates that PerfView understands.  
    /// </summary>
    internal class CSVPerfViewData : PerfViewFile
    {
        public override string FormatName { get { return "XPERF CSV"; } }
        public override string[] FileExtensions { get { return new string[] { ".csvz", ".etl.csv" }; } }

        protected override Action<Action> OpenImpl(Window parentWindow, StatusBar worker)
        {
            m_csvReader = new CSVReader.CSVReader(FilePath);
            m_Children = new List<PerfViewTreeItem>();

            m_Children.Add(new PerfViewEventSource(this));
            foreach (var stackEventName in m_csvReader.StackEventNames)
            {
                m_Children.Add(new PerfViewStackSource(this, stackEventName));
            }

            return null;
        }
        public override void Close()
        {
            if (m_csvReader != null)
            {
                m_csvReader.Dispose();
                m_csvReader = null;
            }
            base.Close();
        }
        protected internal override void ConfigureStackWindow(string stackSourceName, StackWindow stackWindow)
        {
            stackWindow.FoldPercentTextBox.Text = stackWindow.GetDefaultFoldPercentage();
            ConfigureAsEtwStackWindow(stackWindow, stackSourceName == "SampledProfile");
        }
        public override bool SupportsProcesses { get { return true; } }
        protected internal override StackSource OpenStackSourceImpl(
            string streamName, TextWriter log, double startRelativeMSec = 0, double endRelativeMSec = double.PositiveInfinity, Predicate<TraceEvent> predicate = null)
        {
            // TODO: predicate not used
            return m_csvReader.StackSamples(streamName, startRelativeMSec, endRelativeMSec);
        }
        protected internal override EventSource OpenEventSourceImpl(TextWriter log)
        {
            return m_csvReader.GetEventSource();
        }
        public override ImageSource Icon { get { return GuiApp.MainWindow.Resources["FileBitmapImage"] as ImageSource; } }

        #region private
        private CSVReader.CSVReader m_csvReader;
        #endregion
    }

    public partial class ETLPerfViewData : PerfViewFile
    {
        public override string FormatName { get { return "ETW"; } }
        public override string[] FileExtensions { get { return new string[] { ".btl", ".etl", ".etlx", ".etl.zip", ".vspx" }; } }

        protected internal override EventSource OpenEventSourceImpl(TextWriter log)
        {
            var traceLog = GetTraceLog(log);
            return new ETWEventSource(traceLog);
        }
        protected internal override StackSource OpenStackSourceImpl(string streamName, TextWriter log, double startRelativeMSec = 0, double endRelativeMSec = double.PositiveInfinity, Predicate<TraceEvent> predicate = null)
        {
            var eventLog = GetTraceLog(log);
            bool showOptimizationTiers =
                App.CommandLineArgs.ShowOptimizationTiers || streamName.Contains("(with Optimization Tiers)");
            if (streamName.StartsWith("CPU"))
            {
                return eventLog.CPUStacks(null, App.CommandLineArgs, showOptimizationTiers, predicate);
            }

            // var stackSource = new InternTraceEventStackSource(eventLog);
            var stackSource = new MutableTraceEventStackSource(eventLog);

            stackSource.ShowUnknownAddresses = App.CommandLineArgs.ShowUnknownAddresses;
            stackSource.ShowOptimizationTiers = showOptimizationTiers;

            TraceEvents events = eventLog.Events;
            if (!streamName.Contains("TaskTree") && !streamName.Contains("Tasks)"))
            {
                if (predicate != null)
                {
                    events = events.Filter(predicate);
                }
            }
            else
            {
                startRelativeMSec = 0;    // These require activity computers and thus need earlier events.   
            }

            if (startRelativeMSec != 0 || endRelativeMSec != double.PositiveInfinity)
            {
                events = events.FilterByTime(startRelativeMSec, endRelativeMSec);
            }

            var eventSource = events.GetSource();
            var sample = new StackSourceSample(stackSource);

            if (streamName == "Thread Time (with Tasks)")
            {
                return eventLog.ThreadTimeWithTasksStacks();
            }
            else if (streamName == "Thread Time (with ReadyThread)")
            {
                return eventLog.ThreadTimeWithReadyThreadStacks();
            }
            else if (streamName.StartsWith("ASP.NET Thread Time"))
            {
                if (streamName == "ASP.NET Thread Time (with Tasks)")
                {
                    return eventLog.ThreadTimeWithTasksAspNetStacks();
                }
                else
                {
                    return eventLog.ThreadTimeAspNetStacks();
                }
            }
            else if (streamName.StartsWith("Thread Time (with StartStop Activities)"))
            {
                // Handles the normal and (CPU ONLY) case
                var startStopSource = new MutableTraceEventStackSource(eventLog);

                var computer = new ThreadTimeStackComputer(eventLog, App.GetSymbolReader(eventLog.FilePath));
                computer.UseTasks = true;
                computer.GroupByStartStopActivity = true;
                computer.ExcludeReadyThread = true;
                computer.NoAwaitTime = streamName.Contains("(CPU ONLY)");
                computer.GenerateThreadTimeStacks(startStopSource);

                return startStopSource;
            }
            else if (streamName == "Thread Time")
            {
                return eventLog.ThreadTimeStacks();
            }
            else if (streamName == "Processes / Files / Registry")
            {
                return GetProcessFileRegistryStackSource(eventSource, log);
            }
            else if (streamName == "GC Heap Alloc Ignore Free")
            {
                var gcHeapSimulators = new GCHeapSimulators(eventLog, eventSource, stackSource, log);
                gcHeapSimulators.OnNewGCHeapSimulator = delegate (GCHeapSimulator newHeap)
                {
                    newHeap.OnObjectCreate += delegate (Address objAddress, GCHeapSimulatorObject objInfo)
                    {
                        sample.Metric = objInfo.RepresentativeSize;
                        sample.Count = objInfo.RepresentativeSize / objInfo.Size;                                               // We guess a count from the size.  
                        sample.TimeRelativeMSec = objInfo.AllocationTimeRelativeMSec;
                        sample.StackIndex = stackSource.Interner.CallStackIntern(objInfo.ClassFrame, objInfo.AllocStack);        // Add the type as a pseudo frame.  
                        stackSource.AddSample(sample);
                        return true;
                    };
                };
                eventSource.Process();
                stackSource.DoneAddingSamples();
            }
            else if (streamName.StartsWith("GC Heap Net Mem"))
            {
                var gcHeapSimulators = new GCHeapSimulators(eventLog, eventSource, stackSource, log);
                if (streamName == "GC Heap Net Mem (Coarse Sampling)")
                {
                    gcHeapSimulators.UseOnlyAllocTicks = true;
                    m_extraTopStats = "Sampled only 100K bytes";
                }

                gcHeapSimulators.OnNewGCHeapSimulator = delegate (GCHeapSimulator newHeap)
                {
                    newHeap.OnObjectCreate += delegate (Address objAddress, GCHeapSimulatorObject objInfo)
                    {
                        sample.Metric = objInfo.RepresentativeSize;
                        sample.Count = objInfo.RepresentativeSize / objInfo.Size;                                                // We guess a count from the size.  
                        sample.TimeRelativeMSec = objInfo.AllocationTimeRelativeMSec;
                        sample.StackIndex = stackSource.Interner.CallStackIntern(objInfo.ClassFrame, objInfo.AllocStack);        // Add the type as a pseudo frame.  
                        stackSource.AddSample(sample);
                        return true;
                    };
                    newHeap.OnObjectDestroy += delegate (double time, int gen, Address objAddress, GCHeapSimulatorObject objInfo)
                    {
                        sample.Metric = -objInfo.RepresentativeSize;
                        sample.Count = -(objInfo.RepresentativeSize / objInfo.Size);                                            // We guess a count from the size.  
                        sample.TimeRelativeMSec = time;
                        sample.StackIndex = stackSource.Interner.CallStackIntern(objInfo.ClassFrame, objInfo.AllocStack);       // We remove the same stack we added at alloc.  
                        stackSource.AddSample(sample);
                    };

                    newHeap.OnGC += delegate (double time, int gen)
                    {
                        sample.Metric = double.Epsilon;
                        sample.Count = 1;
                        sample.TimeRelativeMSec = time;
                        StackSourceCallStackIndex processStack = stackSource.GetCallStackForProcess(newHeap.Process);
                        StackSourceFrameIndex gcFrame = stackSource.Interner.FrameIntern("GC Occurred Gen(" + gen + ")");
                        sample.StackIndex = stackSource.Interner.CallStackIntern(gcFrame, processStack);
                        stackSource.AddSample(sample);
                    };
                };
                eventSource.Process();
                stackSource.DoneAddingSamples();
            }
            else if (streamName.StartsWith("Gen 2 Object Deaths"))
            {
                var gcHeapSimulators = new GCHeapSimulators(eventLog, eventSource, stackSource, log);

                if (streamName == "Gen 2 Object Deaths (Coarse Sampling)")
                {
                    gcHeapSimulators.UseOnlyAllocTicks = true;
                    m_extraTopStats = "Sampled only 100K bytes";
                }

                gcHeapSimulators.OnNewGCHeapSimulator = delegate (GCHeapSimulator newHeap)
                {
                    newHeap.OnObjectDestroy += delegate (double time, int gen, Address objAddress, GCHeapSimulatorObject objInfo)
                    {
                        if (2 <= gen)
                        {
                            sample.Metric = objInfo.RepresentativeSize;
                            sample.Count = (objInfo.RepresentativeSize / objInfo.Size);                                         // We guess a count from the size.  
                            sample.TimeRelativeMSec = objInfo.AllocationTimeRelativeMSec;
                            sample.StackIndex = stackSource.Interner.CallStackIntern(objInfo.ClassFrame, objInfo.AllocStack);
                            stackSource.AddSample(sample);
                        }
                    };

                    newHeap.OnGC += delegate (double time, int gen)
                    {
                        sample.Metric = double.Epsilon;
                        sample.Count = 1;
                        sample.TimeRelativeMSec = time;
                        StackSourceCallStackIndex processStack = stackSource.GetCallStackForProcess(newHeap.Process);
                        StackSourceFrameIndex gcFrame = stackSource.Interner.FrameIntern("GC Occurred Gen(" + gen + ")");
                        sample.StackIndex = stackSource.Interner.CallStackIntern(gcFrame, processStack);
                        stackSource.AddSample(sample);
                    };
                };

                eventSource.Process();
                stackSource.DoneAddingSamples();
            }
            else if (streamName == "GC Heap Alloc Ignore Free (Coarse Sampling)")
            {
                TypeNameSymbolResolver typeNameSymbolResolver = new TypeNameSymbolResolver(FilePath, log);

                bool seenBadAllocTick = false;

                eventSource.Clr.GCAllocationTick += delegate (GCAllocationTickTraceData data)
                {
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;

                    var stackIndex = stackSource.GetCallStack(data.CallStackIndex(), data);

                    var typeName = data.TypeName;
                    if (string.IsNullOrEmpty(typeName))
                    {
                        // Attempt to resolve the type name.
                        TraceLoadedModule module = data.Process().LoadedModules.GetModuleContainingAddress(data.TypeID, data.TimeStampRelativeMSec);
                        if (module != null)
                        {
                            // Resolve the type name.
                            typeName = typeNameSymbolResolver.ResolveTypeName((int)(data.TypeID - module.ModuleFile.ImageBase), module.ModuleFile, TypeNameSymbolResolver.TypeNameOptions.StripModuleName);
                        }
                    }

                    if (typeName != null && typeName.Length > 0)
                    {
                        var nodeIndex = stackSource.Interner.FrameIntern("Type " + typeName);
                        stackIndex = stackSource.Interner.CallStackIntern(nodeIndex, stackIndex);
                    }

                    sample.Metric = data.GetAllocAmount(ref seenBadAllocTick);

                    if (data.AllocationKind == GCAllocationKind.Large)
                    {

                        var nodeIndex = stackSource.Interner.FrameIntern("LargeObject");
                        stackIndex = stackSource.Interner.CallStackIntern(nodeIndex, stackIndex);
                    }

                    sample.StackIndex = stackIndex;
                    stackSource.AddSample(sample);
                };
                eventSource.Process();
                m_extraTopStats = "Sampled only 100K bytes";
            }
            else if (streamName == "Exceptions")
            {
                eventSource.Clr.ExceptionStart += delegate (ExceptionTraceData data)
                {
                    sample.Metric = 1;
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;

                    // Create a call stack that ends with the 'throw'
                    var nodeName = "Throw(" + data.ExceptionType + ") " + data.ExceptionMessage;
                    var nodeIndex = stackSource.Interner.FrameIntern(nodeName);
                    sample.StackIndex = stackSource.Interner.CallStackIntern(nodeIndex, stackSource.GetCallStack(data.CallStackIndex(), data));
                    stackSource.AddSample(sample);
                };

                eventSource.Kernel.MemoryAccessViolation += delegate (MemoryPageFaultTraceData data)
                {
                    sample.Metric = 1;
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;

                    // Create a call stack that ends with the 'throw'
                    var nodeName = "AccessViolation(ADDR=" + data.VirtualAddress.ToString("x") + ")";
                    var nodeIndex = stackSource.Interner.FrameIntern(nodeName);
                    sample.StackIndex = stackSource.Interner.CallStackIntern(nodeIndex, stackSource.GetCallStack(data.CallStackIndex(), data));
                    stackSource.AddSample(sample);
                };

                eventSource.Process();
            }

            else if (streamName == "Pinning At GC Time")
            {
                // Wire up the GC heap simulations.  
                GCHeapSimulators gcHeapSimulators = new GCHeapSimulators(eventLog, eventSource, stackSource, log);

                // Keep track of the current GC per process 
                var curGCGen = new int[eventLog.Processes.Count];
                var curGCIndex = new int[eventLog.Processes.Count];
                eventSource.Clr.GCStart += delegate (Microsoft.Diagnostics.Tracing.Parsers.Clr.GCStartTraceData data)
                {
                    var process = data.Process();
                    if (process == null)
                    {
                        return;
                    }

                    curGCGen[(int)process.ProcessIndex] = data.Depth;
                    curGCIndex[(int)process.ProcessIndex] = data.Count;
                };

                // Keep track of the live Pinning handles per process.  
                var allLiveHandles = new Dictionary<Address, GCHandleInfo>[eventLog.Processes.Count];
                Action<SetGCHandleTraceData> onSetHandle = delegate (SetGCHandleTraceData data)
                {
                    if (!(data.Kind == GCHandleKind.AsyncPinned || data.Kind == GCHandleKind.Pinned))
                    {
                        return;
                    }

                    var process = data.Process();
                    if (process == null)
                    {
                        return;
                    }

                    var liveHandles = allLiveHandles[(int)process.ProcessIndex];
                    if (liveHandles == null)
                    {
                        allLiveHandles[(int)process.ProcessIndex] = liveHandles = new Dictionary<Address, GCHandleInfo>();
                    }

                    GCHandleInfo info;
                    var handle = data.HandleID;
                    if (!liveHandles.TryGetValue(handle, out info))
                    {
                        liveHandles[handle] = info = new GCHandleInfo();
                        info.PinStartTimeRelativeMSec = data.TimeStampRelativeMSec;
                        info.ObjectAddress = data.ObjectID;
                        info.IsAsync = (data.Kind == GCHandleKind.AsyncPinned || data.Kind == GCHandleKind.DependendAsyncPinned);
                        info.GCGen = (byte)data.Generation;
                        info.PinStack = stackSource.GetCallStack(data.CallStackIndex(), data);

                        // watch this object as it GCs happen  (but frankly it should not move).  
                        gcHeapSimulators[process].TrackObject(info.ObjectAddress);
                    }
                };
                var clrPrivate = new ClrPrivateTraceEventParser(eventSource);
                clrPrivate.GCSetGCHandle += onSetHandle;
                eventSource.Clr.GCSetGCHandle += onSetHandle;

                Action<DestroyGCHandleTraceData> onDestroyHandle = delegate (DestroyGCHandleTraceData data)
                {
                    var process = data.Process();
                    if (process == null)
                    {
                        return;
                    }

                    var liveHandles = allLiveHandles[(int)process.ProcessIndex];
                    if (liveHandles == null)
                    {
                        allLiveHandles[(int)process.ProcessIndex] = liveHandles = new Dictionary<Address, GCHandleInfo>();
                    }

                    GCHandleInfo info;
                    var handle = data.HandleID;
                    if (liveHandles.TryGetValue(handle, out info))
                    {
                        liveHandles.Remove(handle);
                    }
                };
                clrPrivate.GCDestroyGCHandle += onDestroyHandle;
                eventSource.Clr.GCDestoryGCHandle += onDestroyHandle;

#if false 
                var cacheAllocated = new Dictionary<Address, bool>();
                Action<TraceEvent> onPinnableCacheAllocate = delegate(TraceEvent data) 
                {
                    var objectId = (Address) data.PayloadByName("objectId");
                    cacheAllocated[objectId] = true;
                };
                eventSource.Dynamic.AddCallbackForProviderEvent("AllocateBuffer", "Microsoft-DotNETRuntime-PinnableBufferCache", onPinnableCacheAllocate);
                eventSource.Dynamic.AddCallbackForProviderEvent("AllocateBuffer", "Microsoft-DotNETRuntime-PinnableBufferCache-Mscorlib", onPinnableCacheAllocate); 

                Action<PinPlugAtGCTimeTraceData> plugAtGCTime = delegate(PinPlugAtGCTimeTraceData data)
                {
                };
                clrPrivate.GCPinPlugAtGCTime += plugAtGCTime;
                eventSource.Clr.GCPinObjectAtGCTime += plugAtGCTime;
#endif
                // ThreadStacks maps locations in memory of the thread stack to and maps it to a thread.  
                var threadStacks = new Dictionary<Address, TraceThread>[eventLog.Processes.Count];

                // This per-thread information is used solely as a heuristic backstop to try to guess what
                // the Pinned handles are when we don't have other information.   We can remove it. 
                var lastHandleInfoForThreads = new PerThreadGCHandleInfo[eventLog.Threads.Count];

                // The main event, we have pinning that is happening at GC time.  
                Action<PinObjectAtGCTimeTraceData> objectAtGCTime = delegate (PinObjectAtGCTimeTraceData data)
                {
                    var thread = data.Thread();
                    if (thread == null)
                    {
                        return;
                    }

                    var process = thread.Process;
                    var liveHandles = allLiveHandles[(int)process.ProcessIndex];
                    if (liveHandles == null)
                    {
                        allLiveHandles[(int)process.ProcessIndex] = liveHandles = new Dictionary<Address, GCHandleInfo>();
                    }

                    string pinKind = "UnknownPinned";
                    double pinStartTimeRelativeMSec = 0;
                    StackSourceCallStackIndex pinStack = StackSourceCallStackIndex.Invalid;
                    StackSourceCallStackIndex allocStack = StackSourceCallStackIndex.Invalid;
                    int gcGen = curGCGen[(int)process.ProcessIndex];
                    int gcIndex = curGCIndex[(int)process.ProcessIndex];

                    GCHandleInfo info;
                    if (liveHandles.TryGetValue(data.HandleID, out info))
                    {
                        pinStack = info.PinStack;
                        if (pinStack != StackSourceCallStackIndex.Invalid)
                        {
                            pinStartTimeRelativeMSec = info.PinStartTimeRelativeMSec;
                            pinKind = "HandlePinned";
                            gcGen = info.GCGen;
                        }
                        else if (data.ObjectID == info.ObjectAddress)
                        {
                            pinStartTimeRelativeMSec = info.PinStartTimeRelativeMSec;
                        }
                        else
                        {
                            info.PinStartTimeRelativeMSec = data.TimeStampRelativeMSec;     // Restart trying to guess how long this lives
                            info.ObjectAddress = data.ObjectID;
                        }
                    }
                    else
                    {
                        liveHandles[data.HandleID] = info = new GCHandleInfo();
                        info.ObjectAddress = data.ObjectID;
                        info.PinStartTimeRelativeMSec = data.TimeStampRelativeMSec;         // We guess the pinning started at this GC.  
                    }

                    // This is heuristic logic to determine if the pin handles are async or not. 
                    // Basically async handles are themselves pinned and then point at pinned things.  Thus
                    // if you see handles that point near other handles that is likely an async handle. 
                    // TODO I think we can remove this, because we no longer pin the async handle.  
                    if (pinStack == StackSourceCallStackIndex.Invalid)
                    {
                        var lastHandleInfo = lastHandleInfoForThreads[(int)thread.ThreadIndex];
                        if (lastHandleInfo == null)
                        {
                            lastHandleInfoForThreads[(int)thread.ThreadIndex] = lastHandleInfo = new PerThreadGCHandleInfo();
                        }

                        // If we see a handle that 
                        if (data.HandleID - lastHandleInfo.LikelyAsyncHandleTable1 < 128)
                        {
                            pinKind = "LikelyAsyncPinned";
                            lastHandleInfo.LikelyAsyncHandleTable1 = data.HandleID;
                        }
                        else if (data.HandleID - lastHandleInfo.LikelyAsyncHandleTable2 < 128)
                        {
                            // This is here for the async array of buffers case.   
                            pinKind = "LikelyAsyncPinned";
                            lastHandleInfo.LikelyAsyncHandleTable2 = lastHandleInfo.LikelyAsyncHandleTable1;
                            lastHandleInfo.LikelyAsyncHandleTable1 = data.HandleID;
                        }
                        if (data.HandleID - lastHandleInfo.LastObject < 128)
                        {
                            pinKind = "LikelyAsyncDependentPinned";
                            lastHandleInfo.LikelyAsyncHandleTable2 = lastHandleInfo.LikelyAsyncHandleTable1;
                            lastHandleInfo.LikelyAsyncHandleTable1 = lastHandleInfo.LastHandle;
                        }

                        // Remember our values for heuristics we use to determine if it is an async 
                        lastHandleInfo.LastHandle = data.HandleID;
                        lastHandleInfo.LastObject = data.ObjectID;
                    }

                    var objectInfo = gcHeapSimulators[process].GetObjectInfo(data.ObjectID);
                    if (objectInfo != null)
                    {
                        allocStack = objectInfo.AllocStack;
                        if ((allocStack != StackSourceCallStackIndex.Invalid) && (objectInfo.ClassFrame != StackSourceFrameIndex.Invalid))
                        {
                            if (512 <= objectInfo.Size)
                            {
                                var frameName = stackSource.GetFrameName(objectInfo.ClassFrame, false);

                                var size = 1024;
                                while (size < objectInfo.Size)
                                {
                                    size = size * 2;
                                }

                                frameName += " <= " + (size / 1024).ToString() + "K";
                                allocStack = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern(frameName), allocStack);
                            }
                            else
                            {
                                allocStack = stackSource.Interner.CallStackIntern(objectInfo.ClassFrame, allocStack);
                            }
                        }
                    }

                    // If we did not get pinning information, see if it is a stack pin
                    if (pinStack == StackSourceCallStackIndex.Invalid)
                    {
                        const Address allocQuantum = 0x10000 - 1;   // 64K, must be a power of 2.  

                        var threadStack = threadStacks[(int)process.ProcessIndex];
                        if (threadStack == null)
                        {
                            threadStacks[(int)process.ProcessIndex] = threadStack = new Dictionary<Address, TraceThread>();

                            foreach (var procThread in process.Threads)
                            {
                                // Round up to the next 64K boundary
                                var loc = (procThread.UserStackBase + allocQuantum) & ~allocQuantum;
                                // We assume thread stacks are .5 meg (8 * 64K)   Growing down.  
                                for (int i = 0; i < 8; i++)
                                {
                                    threadStack[loc] = procThread;
                                    loc -= (allocQuantum + 1);
                                }
                            }
                        }
                        Address roundUp = (data.HandleID + allocQuantum) & ~allocQuantum;
                        TraceThread stackThread;
                        if (threadStack.TryGetValue(roundUp, out stackThread) && stackThread.StartTimeRelativeMSec <= data.TimeStampRelativeMSec && data.TimeStampRelativeMSec < stackThread.EndTimeRelativeMSec)
                        {
                            pinKind = "StackPinned";
                            pinStack = stackSource.GetCallStackForThread(stackThread);
                        }
                    }

                    /*****  OK we now have all the information we collected, create the sample.  *****/
                    sample.StackIndex = StackSourceCallStackIndex.Invalid;

                    // Choose the stack to use 
                    if (allocStack != StackSourceCallStackIndex.Invalid)
                    {
                        sample.StackIndex = allocStack;
                        sample.StackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("Allocation Location"), sample.StackIndex);
                    }
                    else if (pinStack != StackSourceCallStackIndex.Invalid)
                    {
                        sample.StackIndex = pinStack;
                        sample.StackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("Pinning Location"), sample.StackIndex);
                    }
                    else
                    {
                        var gcThread = data.Thread();
                        if (gcThread == null)
                        {
                            return;             // TODO WARN
                        }

                        sample.StackIndex = stackSource.GetCallStackForThread(gcThread);
                        sample.StackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("GC Location"), sample.StackIndex);
                    }

                    // Add GC Number
                    sample.StackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("GC_NUM " + gcIndex), sample.StackIndex);

                    // Duration of the pin. 
                    var pinDuration = "UNKNOWN";
                    if (pinStartTimeRelativeMSec != 0)
                    {
                        var pinDurationMSec = data.TimeStampRelativeMSec - pinStartTimeRelativeMSec;
                        var roundedDuration = Math.Pow(10.0, Math.Ceiling(Math.Log10(pinDurationMSec)));
                        pinDuration = "<= " + roundedDuration.ToString("n");
                    }
                    var pinDurationInfo = "PINNED_FOR " + pinDuration + " msec";
                    sample.StackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern(pinDurationInfo), sample.StackIndex);

                    // Add the Pin Kind;
                    sample.StackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern(pinKind), sample.StackIndex);

                    // Add the type and size 
                    var typeName = data.TypeName;
                    if (data.ObjectSize > 0)
                    {
                        sample.StackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("Type " + typeName + " Size: 0x" + data.ObjectSize.ToString("x")), sample.StackIndex);
                    }

                    // Add the generation.
                    sample.StackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("Generation " + gcGen), sample.StackIndex);

                    // _sample.StackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("Handle 0x" + data.HandleID.ToString("x") +  " Object 0x" + data.ObjectID.ToString("x")), _sample.StackIndex);

                    // We now have the stack, fill in the rest of the _sample and add it to the stack source.  
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                    sample.Metric = 1;
                    stackSource.AddSample(sample);
                };
                eventSource.Clr.GCPinObjectAtGCTime += objectAtGCTime;
                clrPrivate.GCPinObjectAtGCTime += objectAtGCTime;         // TODO FIX NOW REMOVE AFTER PRIVATE IS GONE

                eventSource.Process();
                stackSource.DoneAddingSamples();
            }
            else if (streamName == "Pinning")
            {
                var clrPrivate = new ClrPrivateTraceEventParser(eventSource);
                var liveHandles = new Dictionary<long, GCHandleInfo>();
                int maxLiveHandles = 0;
                double maxLiveHandleRelativeMSec = 0;

                Action<SetGCHandleTraceData> onSetHandle = delegate (SetGCHandleTraceData data)
                {
                    if (!(data.Kind == GCHandleKind.AsyncPinned || data.Kind == GCHandleKind.Pinned))
                    {
                        return;
                    }

                    GCHandleInfo info;
                    var handle = (long)data.HandleID;
                    if (!liveHandles.TryGetValue(handle, out info))
                    {
                        liveHandles[handle] = info = new GCHandleInfo();
                        if (liveHandles.Count > maxLiveHandles)
                        {
                            maxLiveHandles = liveHandles.Count;
                            maxLiveHandleRelativeMSec = data.TimeStampRelativeMSec;
                        }
                        info.PinStartTimeRelativeMSec = data.TimeStampRelativeMSec;
                        info.ObjectAddress = data.ObjectID;

                        // TODO deal with nulling out. 
                        string nodeName = (data.Kind == GCHandleKind.Pinned) ? "SinglePinned" : "AsyncPinned";
                        StackSourceFrameIndex frameIndex = stackSource.Interner.FrameIntern(nodeName);
                        StackSourceCallStackIndex callStackIndex = stackSource.Interner.CallStackIntern(frameIndex, stackSource.GetCallStack(data.CallStackIndex(), data));

                        // Add the generation.
                        nodeName = "Generation " + data.Generation;
                        frameIndex = stackSource.Interner.FrameIntern(nodeName);
                        info.PinStack = stackSource.Interner.CallStackIntern(frameIndex, callStackIndex);
                    }
                };
                clrPrivate.GCSetGCHandle += onSetHandle;
                eventSource.Clr.GCSetGCHandle += onSetHandle;

                Action<DestroyGCHandleTraceData> onDestroyHandle = delegate (DestroyGCHandleTraceData data)
                {
                    GCHandleInfo info;
                    var handle = (long)data.HandleID;
                    if (liveHandles.TryGetValue(handle, out info))
                    {
                        LogGCHandleLifetime(stackSource, sample, info, data.TimeStampRelativeMSec, log);
                        liveHandles.Remove(handle);
                    }
                };
                clrPrivate.GCDestroyGCHandle += onDestroyHandle;
                eventSource.Clr.GCDestoryGCHandle += onDestroyHandle;

                eventSource.Process();
                // Pick up any handles that were never destroyed.  
                foreach (var info in liveHandles.Values)
                {
                    LogGCHandleLifetime(stackSource, sample, info, eventLog.SessionDuration.TotalMilliseconds, log);
                }

                stackSource.DoneAddingSamples();
                log.WriteLine("The maximum number of live pinning handles is {0} at {1:n3} Msec ", maxLiveHandles, maxLiveHandleRelativeMSec);
            }

            else if (streamName == "Heap Snapshot Pinning")
            {
                GCPinnedObjectAnalyzer pinnedObjectAnalyzer = new GCPinnedObjectAnalyzer(FilePath, eventLog, stackSource, sample, log);
                pinnedObjectAnalyzer.Execute(GCPinnedObjectViewType.PinnedHandles);
            }
            else if (streamName == "Heap Snapshot Pinned Object Allocation")
            {
                GCPinnedObjectAnalyzer pinnedObjectAnalyzer = new GCPinnedObjectAnalyzer(FilePath, eventLog, stackSource, sample, log);
                pinnedObjectAnalyzer.Execute(GCPinnedObjectViewType.PinnedObjectAllocations);
            }
            else if (streamName == "CCW Ref Count")
            {
                // TODO use the callback model.  We seem to have an issue getting the names however. 
                foreach (var data in events.ByEventType<CCWRefCountChangeTraceData>())
                {
                    sample.Metric = 1;
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                    var stackIndex = stackSource.GetCallStack(data.CallStackIndex(), data);

                    var operation = data.Operation;
                    if (operation.StartsWith("Release", StringComparison.OrdinalIgnoreCase))
                    {
                        sample.Metric = -1;
                    }

                    var ccwRefKindName = "CCW " + operation;
                    var ccwRefKindIndex = stackSource.Interner.FrameIntern(ccwRefKindName);
                    stackIndex = stackSource.Interner.CallStackIntern(ccwRefKindIndex, stackIndex);

                    var ccwRefCountName = "CCW NewRefCnt " + data.NewRefCount.ToString();
                    var ccwRefCountIndex = stackSource.Interner.FrameIntern(ccwRefCountName);
                    stackIndex = stackSource.Interner.CallStackIntern(ccwRefCountIndex, stackIndex);

                    var ccwInstanceName = "CCW Instance 0x" + data.COMInterfacePointer.ToString("x");
                    var ccwInstanceIndex = stackSource.Interner.FrameIntern(ccwInstanceName);
                    stackIndex = stackSource.Interner.CallStackIntern(ccwInstanceIndex, stackIndex);

                    var ccwTypeName = "CCW Type " + data.NameSpace + "." + data.ClassName;
                    var ccwTypeIndex = stackSource.Interner.FrameIntern(ccwTypeName);
                    stackIndex = stackSource.Interner.CallStackIntern(ccwTypeIndex, stackIndex);

                    sample.StackIndex = stackIndex;
                    stackSource.AddSample(sample);
                }
                foreach (var data in events.ByEventType<CCWRefCountChangeAnsiTraceData>())
                {
                    sample.Metric = 1;
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                    var stackIndex = stackSource.GetCallStack(data.CallStackIndex(), data);

                    var operation = data.Operation;
                    if (operation.StartsWith("Release", StringComparison.OrdinalIgnoreCase))
                    {
                        sample.Metric = -1;
                    }

                    var ccwRefKindName = "CCW " + operation;
                    var ccwRefKindIndex = stackSource.Interner.FrameIntern(ccwRefKindName);
                    stackIndex = stackSource.Interner.CallStackIntern(ccwRefKindIndex, stackIndex);

                    var ccwRefCountName = "CCW NewRefCnt " + data.NewRefCount.ToString();
                    var ccwRefCountIndex = stackSource.Interner.FrameIntern(ccwRefCountName);
                    stackIndex = stackSource.Interner.CallStackIntern(ccwRefCountIndex, stackIndex);

                    var ccwInstanceName = "CCW Instance 0x" + data.COMInterfacePointer.ToString("x");
                    var ccwInstanceIndex = stackSource.Interner.FrameIntern(ccwInstanceName);
                    stackIndex = stackSource.Interner.CallStackIntern(ccwInstanceIndex, stackIndex);

                    var ccwTypeName = "CCW Type " + data.NameSpace + "." + data.ClassName;
                    var ccwTypeIndex = stackSource.Interner.FrameIntern(ccwTypeName);
                    stackIndex = stackSource.Interner.CallStackIntern(ccwTypeIndex, stackIndex);

                    sample.StackIndex = stackIndex;
                    stackSource.AddSample(sample);
                }
            }
            else if (streamName == ".NET Native CCW Ref Count")
            {
                // TODO FIX NOW, investigate the missing events.  All we know is that incs and dec are not
                // consistent with the RefCount value that is in the events.
                GuiApp.MainWindow.Dispatcher.BeginInvoke((Action)delegate ()
                {
                    MessageBox.Show(GuiApp.MainWindow,
                        "Warning: the Interop CCW events on which this data is based seem to be incomplete.\r\n" +
                        "There seem to be missing instrumentation, which make the referenct counts unreliable\r\n"
                        , "Data May be Incorrect");
                });

                var objectToTypeMap = new Dictionary<long, Address>(1000);
                var typeToNameMap = new Dictionary<Address, string>(100);
                var interopTraceEventParser = new InteropTraceEventParser(eventSource);
                Action<double, long, int, int, StackSourceCallStackIndex> handleCWWInfoArgs = (double timestamp, long objectID, int refCount, int metric, StackSourceCallStackIndex stackIndex) =>
                {
                    sample.Metric = metric;
                    sample.TimeRelativeMSec = timestamp;

                    var ccwRefKindName = $"CCW {(metric >= 0 ? "AddRef" : "Release")}";
                    var ccwRefKindNameIndex = stackSource.Interner.FrameIntern(ccwRefKindName);
                    stackIndex = stackSource.Interner.CallStackIntern(ccwRefKindNameIndex, stackIndex);

                    var objectId = "Object ID 0x" + objectID.ToString("x");
                    var objectIdIndex = stackSource.Interner.FrameIntern(objectId);
                    stackIndex = stackSource.Interner.CallStackIntern(objectIdIndex, stackIndex);

                    Address typeId;
                    if (objectToTypeMap.TryGetValue(objectID, out typeId))
                    {
                        string objectType = "Object Type ";
                        string typeName;
                        if (typeToNameMap.TryGetValue(typeId, out typeName))
                        {
                            objectType += typeName;
                        }
                        else
                        {
                            objectType += "0x" + typeId;
                        }

                        var objectTypeIndex = stackSource.Interner.FrameIntern(objectType);
                        stackIndex = stackSource.Interner.CallStackIntern(objectTypeIndex, stackIndex);
                    }
                    var ccwRefCount = "CCW NewRefCnt " + refCount;
                    var ccwRefCountIndex = stackSource.Interner.FrameIntern(ccwRefCount.ToString());
                    stackIndex = stackSource.Interner.CallStackIntern(ccwRefCountIndex, stackIndex);

                    sample.StackIndex = stackIndex;
                    stackSource.AddSample(sample);
                };

                TypeNameSymbolResolver typeNameSymbolResolver = new TypeNameSymbolResolver(FilePath, log);

                interopTraceEventParser.AddCallbackForEvent<TaskCCWCreationArgs>(null, args =>
                {
                    if (!objectToTypeMap.ContainsKey(args.objectID))
                    {
                        objectToTypeMap.Add(args.objectID, args.targetObjectIDType);
                    }

                    // Attempt to resolve the type name.
                    if (!typeToNameMap.ContainsKey(args.targetObjectIDType))
                    {
                        TraceLoadedModule module = args.Process().LoadedModules.GetModuleContainingAddress(args.targetObjectIDType, args.TimeStampRelativeMSec);
                        if (module != null)
                        {
                            string typeName = typeNameSymbolResolver.ResolveTypeName((int)(args.targetObjectIDType - module.ModuleFile.ImageBase), module.ModuleFile, TypeNameSymbolResolver.TypeNameOptions.StripModuleName);
                            if (typeName != null)
                            {
                                typeToNameMap.Add(args.targetObjectIDType, typeName);
                            }
                        }
                    }
                });
                #region TaskCCWQueryRuntimeClassNameArgs commented for a while. TODO: get type info from pdb
                //interopTraceEventParser.AddCallbackForEvents<TaskCCWQueryRuntimeClassNameArgs>(args =>
                //{
                //    sample.Metric = 0;
                //    sample.TimeRelativeMSec = args.TimeStampRelativeMSec;
                //    var stackIndex = stackSource.GetCallStack(args.CallStackIndex(), args);

                //    var ccwRefKindName = "CCW QueryRuntimeClassName " + args.runtimeClassName;
                //    var ccwRefKindNameIndex = stackSource.Interner.FrameIntern(ccwRefKindName);
                //    stackIndex = stackSource.Interner.CallStackIntern(ccwRefKindNameIndex, stackIndex);

                //    sample.StackIndex = stackIndex;
                //    stackSource.AddSample(sample);
                //});
                #endregion
                interopTraceEventParser.AddCallbackForEvents<TaskCCWRefCountIncArgs>(args =>
                    handleCWWInfoArgs(args.TimeStampRelativeMSec, args.objectID, args.refCount, 1, stackSource.GetCallStack(args.CallStackIndex(), args)));
                interopTraceEventParser.AddCallbackForEvents<TaskCCWRefCountDecArgs>(args =>
                    handleCWWInfoArgs(args.TimeStampRelativeMSec, args.objectID, args.refCount, -1, stackSource.GetCallStack(args.CallStackIndex(), args)));
                eventSource.Process();
            }
            else if (streamName == "Windows Handle Ref Count")
            {
                var allocationsStacks = new Dictionary<long, StackSourceCallStackIndex>(200);

                Action<string, Address, int, int, TraceEvent> onHandleEvent = delegate (string handleTypeName, Address objectInstance, int handleInstance, int handleProcess, TraceEvent data)
                {
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                    sample.StackIndex = StackSourceCallStackIndex.Invalid;

                    sample.Metric = 1;
                    // Closes use the call stack of the allocation site if possible (since that is more helpful)  
                    if (data.Opcode == (TraceEventOpcode)33)       // CloseHandle
                    {
                        sample.Metric = -1;

                        long key = (((long)handleProcess) << 32) + handleInstance;
                        StackSourceCallStackIndex stackIndex;
                        if (allocationsStacks.TryGetValue(key, out stackIndex))
                        {
                            sample.StackIndex = stackIndex;
                        }
                        // TODO should we keep track of the ref count and remove the entry when it drops past zero?  
                    }

                    // If this not a close() (Or if we could not find a stack for the close() make up a call stack from the event.  
                    if (sample.StackIndex == StackSourceCallStackIndex.Invalid)
                    {
                        StackSourceCallStackIndex stackIndex = stackSource.GetCallStack(data.CallStackIndex(), data);

                        // We want all stacks to be int he process where the handle exists.  But this not always the case
                        // If that happened abandon the stack and make up a pseudo stack that lets you know that happened. 
                        if (handleProcess != data.ProcessID)
                        {
                            stackIndex = StackSourceCallStackIndex.Invalid;
                            TraceProcess process = eventLog.Processes.GetProcess(handleProcess, data.TimeStampRelativeMSec);
                            if (process != null)
                            {
                                stackIndex = stackSource.GetCallStackForProcess(process);
                            }

                            var markerIndex = stackSource.Interner.FrameIntern("Handle Allocated Out of Process");
                            stackIndex = stackSource.Interner.CallStackIntern(markerIndex, stackIndex);
                        }

                        var nameIndex = stackSource.Interner.FrameIntern(data.EventName);
                        stackIndex = stackSource.Interner.CallStackIntern(nameIndex, stackIndex);

                        var instanceName = "Handle Instance " + handleInstance.ToString("n0") + " (0x" + handleInstance.ToString("x") + ")";
                        var instanceIndex = stackSource.Interner.FrameIntern(instanceName);
                        stackIndex = stackSource.Interner.CallStackIntern(instanceIndex, stackIndex);

                        var handleTypeIndex = stackSource.Interner.FrameIntern("Handle Type " + handleTypeName);
                        stackIndex = stackSource.Interner.CallStackIntern(handleTypeIndex, stackIndex);

                        //var objectName = "Object Instance 0x" + objectInstance.ToString("x");
                        //var objectIndex = stackSource.Interner.FrameIntern(objectName);
                        //stackIndex = stackSource.Interner.CallStackIntern(objectIndex, stackIndex);

                        sample.StackIndex = stackIndex;

                        long key = (((long)handleProcess) << 32) + handleInstance;
                        allocationsStacks[key] = stackIndex;
                    }

                    stackSource.AddSample(sample);
                };

                eventSource.Kernel.AddCallbackForEvents<ObjectHandleTraceData>(data => onHandleEvent(data.ObjectTypeName, data.Object, data.Handle, data.ProcessID, data));
                eventSource.Kernel.AddCallbackForEvents<ObjectDuplicateHandleTraceData>(data => onHandleEvent(data.ObjectTypeName, data.Object, data.TargetHandle, data.TargetProcessID, data));
                eventSource.Process();
            }
            else if (streamName.StartsWith("Processor"))
            {
                eventSource.Kernel.PerfInfoSample += delegate (SampledProfileTraceData data)
                {
                    StackSourceCallStackIndex stackIndex;
                    var callStackIdx = data.CallStackIndex();
                    if (callStackIdx == CallStackIndex.Invalid)
                    {
                        return;
                    }

                    stackIndex = stackSource.GetCallStack(callStackIdx, data);

                    var processorPriority = "Processor (" + data.ProcessorNumber + ") Priority (" + data.Priority + ")";
                    stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern(processorPriority), stackIndex);

                    sample.StackIndex = stackIndex;
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                    sample.Metric = 1;
                    stackSource.AddSample(sample);
                };
                eventSource.Process();
            }
            else if (streamName.StartsWith("Any"))
            {
                ActivityComputer activityComputer = null;
                StartStopActivityComputer startStopComputer = null;
                bool isAnyTaskTree = (streamName == "Any TaskTree");
                bool isAnyWithTasks = (streamName == "Any Stacks (with Tasks)");
                bool isAnyWithStartStop = (streamName == "Any Stacks (with StartStop Activities)");          // These have the call stacks 
                bool isAnyStartStopTreeNoCallStack = (streamName == "Any StartStopTree");               // These have just the start-stop activities.  
                if (isAnyTaskTree || isAnyWithTasks || isAnyWithStartStop || isAnyStartStopTreeNoCallStack)
                {
                    activityComputer = new ActivityComputer(eventSource, GetSymbolReader(log));

                    // Log a pseudo-event that indicates when the activity dies
                    activityComputer.Stop += delegate (TraceActivity activity, TraceEvent data)
                    {
                        // TODO This is a clone of the logic below, factor it.  
                        TraceThread thread = data.Thread();
                        if (thread != null)
                        {
                            return;
                        }

                        StackSourceCallStackIndex stackIndex;
                        if (isAnyTaskTree)
                        {
                            // Compute the stack where frames using an activity Name as a frame name.
                            stackIndex = activityComputer.GetActivityStack(stackSource, activityComputer.GetCurrentActivity(thread));
                        }
                        else if (isAnyStartStopTreeNoCallStack)
                        {
                            stackIndex = startStopComputer.GetStartStopActivityStack(stackSource, startStopComputer.GetCurrentStartStopActivity(thread, data), thread.Process);
                        }
                        else
                        {
                            Func<TraceThread, StackSourceCallStackIndex> topFrames = null;
                            if (isAnyWithStartStop)
                            {
                                topFrames = delegate (TraceThread topThread) { return startStopComputer.GetCurrentStartStopActivityStack(stackSource, thread, topThread); };
                            }

                            // Use the call stack 
                            stackIndex = activityComputer.GetCallStack(stackSource, data, topFrames);
                        }

                        stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("ActivityStop " + activity.ToString()), stackIndex);
                        sample.StackIndex = stackIndex;
                        sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                        sample.Metric = 1;
                        stackSource.AddSample(sample);
                    };

                    if (isAnyWithStartStop || isAnyStartStopTreeNoCallStack)
                    {
                        startStopComputer = new StartStopActivityComputer(eventSource, activityComputer);
                    }
                }

                StackSourceFrameIndex blockingFrame = stackSource.Interner.FrameIntern("Event Kernel/Thread/BLOCKING CSwitch");
                StackSourceFrameIndex cswitchEventFrame = stackSource.Interner.FrameIntern("Event Windows Kernel/Thread/CSwitch");
                StackSourceFrameIndex readyThreadEventFrame = stackSource.Interner.FrameIntern("Event Windows Kernel/Dispatcher/ReadyThread");
                StackSourceFrameIndex sampledProfileFrame = stackSource.Interner.FrameIntern("Event Windows Kernel/PerfInfo/Sample");

                eventSource.AllEvents += delegate (TraceEvent data)
                {
                    // Get most of the stack (we support getting the normal call stack as well as the task stack.  
                    StackSourceCallStackIndex stackIndex;
                    if (activityComputer != null)
                    {
                        TraceThread thread = data.Thread();
                        if (thread == null)
                        {
                            return;
                        }

                        if (isAnyTaskTree)
                        {
                            // Compute the stack where frames using an activity Name as a frame name.
                            stackIndex = activityComputer.GetActivityStack(stackSource, activityComputer.GetCurrentActivity(thread));
                        }
                        else if (isAnyStartStopTreeNoCallStack)
                        {
                            stackIndex = startStopComputer.GetStartStopActivityStack(stackSource, startStopComputer.GetCurrentStartStopActivity(thread, data), thread.Process);
                        }
                        else
                        {
                            Func<TraceThread, StackSourceCallStackIndex> topFrames = null;
                            if (isAnyWithStartStop)
                            {
                                topFrames = delegate (TraceThread topThread) { return startStopComputer.GetCurrentStartStopActivityStack(stackSource, thread, topThread); };
                            }

                            // Use the call stack 
                            stackIndex = activityComputer.GetCallStack(stackSource, data, topFrames);
                        }
                    }
                    else
                    {
                        // Normal case, get the calls stack of frame names.  
                        var callStackIdx = data.CallStackIndex();
                        if (callStackIdx != CallStackIndex.Invalid)
                        {
                            stackIndex = stackSource.GetCallStack(callStackIdx, data);
                        }
                        else
                        {
                            stackIndex = StackSourceCallStackIndex.Invalid;
                        }
                    }

                    var asCSwitch = data as CSwitchTraceData;
                    if (asCSwitch != null)
                    {
                        if (activityComputer == null)  // Just a plain old any-stacks
                        {
                            var callStackIdx = asCSwitch.BlockingStack();
                            if (callStackIdx != CallStackIndex.Invalid)
                            {
                                StackSourceCallStackIndex blockingStackIndex = stackSource.GetCallStack(callStackIdx, data);
                                // Make an entry for the blocking stacks as well.  
                                blockingStackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("EventData OldThreadState " + asCSwitch.OldThreadState), blockingStackIndex);
                                sample.StackIndex = stackSource.Interner.CallStackIntern(blockingFrame, blockingStackIndex);
                                sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                                sample.Metric = 1;
                                stackSource.AddSample(sample);
                            }
                        }

                        if (stackIndex != StackSourceCallStackIndex.Invalid)
                        {
                            stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("EventData NewProcessName " + asCSwitch.NewProcessName), stackIndex);
                            stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("EventData OldProcessName " + asCSwitch.OldProcessName), stackIndex);

                            stackIndex = stackSource.Interner.CallStackIntern(cswitchEventFrame, stackIndex);
                        }

                        goto ADD_SAMPLE;
                    }

                    if (stackIndex == StackSourceCallStackIndex.Invalid)
                    {
                        return;
                    }

                    var asSampledProfile = data as SampledProfileTraceData;
                    if (asSampledProfile != null)
                    {
                        stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("EventData Priority " + asSampledProfile.Priority), stackIndex);
                        stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("EventData Processor " + asSampledProfile.ProcessorNumber), stackIndex);
                        stackIndex = stackSource.Interner.CallStackIntern(sampledProfileFrame, stackIndex);
                        goto ADD_SAMPLE;
                    }

                    var asReadyThread = data as DispatcherReadyThreadTraceData;
                    if (asReadyThread != null)
                    {
                        var awakenedName = "EventData Readied Thread " + asReadyThread.AwakenedThreadID +
                            " Proc " + asReadyThread.AwakenedProcessID;
                        var awakenedIndex = stackSource.Interner.FrameIntern(awakenedName);
                        stackIndex = stackSource.Interner.CallStackIntern(awakenedIndex, stackIndex);
                        stackIndex = stackSource.Interner.CallStackIntern(readyThreadEventFrame, stackIndex);
                        goto ADD_SAMPLE;
                    }

                    // TODO FIX NOW remove for debugging activity stuff.  
#if false
                    var activityId = data.ActivityID;
                    if (activityId != Guid.Empty && ActivityComputer.IsActivityPath(activityId))
                        stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("ActivityPath " + ActivityComputer.ActivityPathString(activityId)), stackIndex);
#endif
                    var asObjectAllocated = data as ObjectAllocatedArgs;
                    if (asObjectAllocated != null)
                    {
                        var size = "EventData Size 0x" + asObjectAllocated.Size.ToString("x");
                        var sizeIndex = stackSource.Interner.FrameIntern(size);
                        stackIndex = stackSource.Interner.CallStackIntern(sizeIndex, stackIndex);
                        goto ADD_EVENT_FRAME;
                    }

                    var asAllocTick = data as GCAllocationTickTraceData;
                    if (asAllocTick != null)
                    {
                        var frameIdx = stackSource.Interner.FrameIntern("EventData Kind " + asAllocTick.AllocationKind);
                        stackIndex = stackSource.Interner.CallStackIntern(frameIdx, stackIndex);

                        frameIdx = stackSource.Interner.FrameIntern("EventData Size " + asAllocTick.AllocationAmount64);
                        stackIndex = stackSource.Interner.CallStackIntern(frameIdx, stackIndex);

                        var typeName = asAllocTick.TypeName;
                        if (string.IsNullOrEmpty(typeName))
                        {
                            typeName = "TypeId 0x" + asAllocTick.TypeID;
                        }

                        frameIdx = stackSource.Interner.FrameIntern("EventData TypeName " + typeName);
                        stackIndex = stackSource.Interner.CallStackIntern(frameIdx, stackIndex);
                        goto ADD_EVENT_FRAME;
                    }

                    var asSampleObjectAllocated = data as GCSampledObjectAllocationTraceData;
                    if (asSampleObjectAllocated != null)
                    {
                        var size = "EventData Size 0x" + asSampleObjectAllocated.TotalSizeForTypeSample.ToString("x");
                        var sizeIndex = stackSource.Interner.FrameIntern(size);
                        stackIndex = stackSource.Interner.CallStackIntern(sizeIndex, stackIndex);
                        goto ADD_EVENT_FRAME;
                    }

                    var asSetGCHandle = data as SetGCHandleTraceData;
                    if (asSetGCHandle != null)
                    {
                        var handleName = "EventData GCHandleKind " + asSetGCHandle.Kind.ToString();
                        var handleIndex = stackSource.Interner.FrameIntern(handleName);
                        stackIndex = stackSource.Interner.CallStackIntern(handleIndex, stackIndex);
                        goto ADD_EVENT_FRAME;
                    }

                    var asPageAccess = data as MemoryPageAccessTraceData;
                    if (asPageAccess != null)
                    {
                        sample.Metric = 4;      // Convenience since these are 4K pages 

                        // EMit the kind, which may have a file name argument.  
                        var pageKind = asPageAccess.PageKind;
                        string fileName = asPageAccess.FileName;
                        if (fileName == null)
                        {
                            fileName = "";
                        }

                        stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern(pageKind.ToString() + " " + fileName), stackIndex);

                        // If it is the range of a module, log that as well, as well as it bucket.  
                        var address = asPageAccess.VirtualAddress;
                        var process = data.Process();
                        if (process != null)
                        {
                            var module = process.LoadedModules.GetModuleContainingAddress(address, asPageAccess.TimeStampRelativeMSec);
                            if (module != null)
                            {
                                if (module.ModuleFile != null && module.ModuleFile.ImageSize != 0)
                                {
                                    // Create a node that indicates where in the file (in buckets) the access was from 
                                    double normalizeDistance = (address - module.ImageBase) / ((double)module.ModuleFile.ImageSize);
                                    if (0 <= normalizeDistance && normalizeDistance < 1)
                                    {
                                        const int numBuckets = 20;
                                        int bucket = (int)(normalizeDistance * numBuckets);
                                        int bucketSizeInPages = module.ModuleFile.ImageSize / (numBuckets * 4096);
                                        string bucketName = "Image Bucket " + bucket + " Size " + bucketSizeInPages + " Pages";
                                        stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern(bucketName), stackIndex);
                                    }
                                }
                                stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("EventData Image  " + module.ModuleFile.FilePath), stackIndex);
                            }
                        }
                        goto ADD_EVENT_FRAME;
                    }

                    var asPMCCounter = data as PMCCounterProfTraceData;
                    if (asPMCCounter != null)
                    {
                        stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("EventData Processor " + asPMCCounter.ProcessorNumber), stackIndex);
                        var source = "EventData ProfileSourceID " + asPMCCounter.ProfileSource;
                        var sourceIndex = stackSource.Interner.FrameIntern(source);
                        stackIndex = stackSource.Interner.CallStackIntern(sourceIndex, stackIndex);
                        goto ADD_EVENT_FRAME;
                    }

                    var asFileCreate = data as FileIOCreateTraceData;
                    if (asFileCreate != null)
                    {
                        stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("CreateOptions: " + asFileCreate.CreateOptions), stackIndex);
                        stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("FileAttributes: " + asFileCreate.FileAttributes), stackIndex);
                        stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("ShareAccess: " + asFileCreate.ShareAccess), stackIndex);
                        // stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("CreateDispostion: " + asFileCreate.CreateDispostion), stackIndex);
                        stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("FileName: " + asFileCreate.FileName), stackIndex);
                        goto ADD_EVENT_FRAME;
                    }

                    // Tack on additional info about the event. 
                    var fieldNames = data.PayloadNames;
                    for (int i = 0; i < fieldNames.Length; i++)
                    {
                        var fieldName = fieldNames[i];
                        if (0 <= fieldName.IndexOf("Name", StringComparison.OrdinalIgnoreCase) ||
                            fieldName == "OpenPath" || fieldName == "Url" || fieldName == "Uri" || fieldName == "ConnectionId" ||
                            fieldName == "ExceptionType" || 0 <= fieldName.IndexOf("Message", StringComparison.OrdinalIgnoreCase))
                        {
                            var value = data.PayloadString(i);
                            var fieldNodeName = "EventData " + fieldName + " " + value;
                            var fieldNodeIndex = stackSource.Interner.FrameIntern(fieldNodeName);
                            stackIndex = stackSource.Interner.CallStackIntern(fieldNodeIndex, stackIndex);
                        }
                    }

                    ADD_EVENT_FRAME:
                    // Tack on event name 
                    var eventNodeName = "Event " + data.ProviderName + "/" + data.EventName;
                    stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern(eventNodeName), stackIndex);
                    ADD_SAMPLE:
                    sample.StackIndex = stackIndex;
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                    sample.Metric = 1;
                    stackSource.AddSample(sample);
                };
                eventSource.Process();
            }
            else if (streamName == "Managed Load")
            {
                eventSource.Clr.LoaderModuleLoad += delegate (ModuleLoadUnloadTraceData data)
                {
                    sample.Metric = 1;
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;

                    // Create a call stack that ends with 'Disk READ <fileName> (<fileDirectory>)'
                    var nodeName = "Load " + data.ModuleILPath;
                    var nodeIndex = stackSource.Interner.FrameIntern(nodeName);
                    sample.StackIndex = stackSource.Interner.CallStackIntern(nodeIndex, stackSource.GetCallStack(data.CallStackIndex(), data));
                    stackSource.AddSample(sample);
                };
                eventSource.Process();
            }
            else if (streamName == "Disk I/O")
            {
                var diskStartStack = new Dictionary<Address, StackSourceCallStackIndex>(50);
                // On a per-disk basis remember when the last Disk I/O completed.  
                var lastDiskEndMSec = new GrowableArray<double>(4);

                eventSource.Kernel.AddCallbackForEvents<DiskIOInitTraceData>(delegate (DiskIOInitTraceData data)
                                {
                                    diskStartStack[data.Irp] = stackSource.GetCallStack(data.CallStackIndex(), data);
                                });

                eventSource.Kernel.AddCallbackForEvents<DiskIOTraceData>(delegate (DiskIOTraceData data)
                {
                    StackSourceCallStackIndex stackIdx;
                    if (diskStartStack.TryGetValue(data.Irp, out stackIdx))
                    {
                        diskStartStack.Remove(data.Irp);
                    }
                    else
                    {
                        stackIdx = StackSourceCallStackIndex.Invalid;
                    }

                    var diskNumber = data.DiskNumber;
                    if (diskNumber >= lastDiskEndMSec.Count)
                    {
                        lastDiskEndMSec.Count = diskNumber + 1;
                    }

                    // Create a call stack that ends with 'Disk READ <fileName> (<fileDirectory>)'
                    var filePath = data.FileName;
                    if (filePath.Length == 0)
                    {
                        filePath = "UNKNOWN";
                    }

                    var nodeName = "I/O Size 0x" + data.TransferSize.ToString("x");
                    var nodeIndex = stackSource.Interner.FrameIntern(nodeName);
                    stackIdx = stackSource.Interner.CallStackIntern(nodeIndex, stackIdx);

                    nodeName = string.Format("Disk {0} DiskNum({1}) {2} ({3})", data.OpcodeName, diskNumber,
                        GetFileName(filePath), GetDirectoryName(filePath));

                    // The time it took actually using the disk is the smaller of either
                    // The elapsed time (because there were no other entries in the disk queue)
                    // OR the time since the last I/O completed (since that is when this one will start using the disk.
                    var elapsedMSec = data.ElapsedTimeMSec;
                    double serviceTimeMSec = elapsedMSec;
                    double durationSinceLastIOMSec = data.TimeStampRelativeMSec - lastDiskEndMSec[diskNumber];
                    lastDiskEndMSec[diskNumber] = elapsedMSec;
                    if (durationSinceLastIOMSec < serviceTimeMSec)
                    {
                        serviceTimeMSec = durationSinceLastIOMSec;
                        // There is queuing delay, indicate this by adding a sample that represents the queueing delay. 

                        var queueStackIdx = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("Time in Disk Queue " + diskNumber), stackIdx);
                        sample.StackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern(nodeName), queueStackIdx);
                        sample.Metric = elapsedMSec - serviceTimeMSec;
                        sample.TimeRelativeMSec = data.TimeStampRelativeMSec - elapsedMSec;
                        stackSource.AddSample(sample);
                    }

                    stackIdx = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("Service Time Disk " + diskNumber), stackIdx);
                    sample.StackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern(nodeName), stackIdx);
                    sample.Metric = serviceTimeMSec;
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec - serviceTimeMSec;
                    stackSource.AddSample(sample);
                });
                eventSource.Process();
                m_extraTopStats = " Metric is MSec";
            }
            else if (streamName == "Server Request CPU")
            {
                ServerRequestScenarioConfiguration scenarioConfiguration = new ServerRequestScenarioConfiguration(eventLog);
                ComputingResourceStateMachine stateMachine = new ComputingResourceStateMachine(stackSource, scenarioConfiguration, ComputingResourceViewType.CPU);
                stateMachine.Execute();
            }
            else if (streamName == "Server Request Thread Time")
            {
                ServerRequestScenarioConfiguration scenarioConfiguration = new ServerRequestScenarioConfiguration(eventLog);
                ComputingResourceStateMachine stateMachine = new ComputingResourceStateMachine(stackSource, scenarioConfiguration, ComputingResourceViewType.ThreadTime);
                stateMachine.Execute();
            }
            else if (streamName == "Server Request Managed Allocation")
            {
                ServerRequestScenarioConfiguration scenarioConfiguration = new ServerRequestScenarioConfiguration(eventLog);
                ComputingResourceStateMachine stateMachine = new ComputingResourceStateMachine(stackSource, scenarioConfiguration, ComputingResourceViewType.Allocations);
                stateMachine.Execute();
            }
            else if (streamName == "Execution Tracing")
            {
                TraceLogEventSource source = eventLog.Events.GetSource();

                Action<TraceEvent> tracingCallback = delegate (TraceEvent data)
                {
                    string assemblyName = (string)data.PayloadByName("assembly");
                    string typeName = (string)data.PayloadByName("type");
                    string methodName = (string)data.PayloadByName("method");

                    string frameName = string.Format("{0}!{1}.{2}", assemblyName, typeName, methodName);

                    StackSourceCallStackIndex callStackIndex = stackSource.GetCallStack(data.CallStackIndex(), data);
                    StackSourceFrameIndex nodeIndex = stackSource.Interner.FrameIntern(frameName);
                    callStackIndex = stackSource.Interner.CallStackIntern(nodeIndex, callStackIndex);

                    sample.Count = 1;
                    sample.Metric = 1;
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                    sample.StackIndex = callStackIndex;

                    stackSource.AddSample(sample);
                };

                source.Dynamic.AddCallbackForProviderEvent("MethodCallLogger", "MethodEntry", tracingCallback);

                source.Process();
            }
            else if (streamName == "File Queries")
            {
                eventSource.Kernel.AddCallbackForEvents<FileIOInfoTraceData>(delegate (FileIOInfoTraceData data)
                {
                    sample.Metric = 1;
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;

                    StackSourceCallStackIndex stackIdx = stackSource.GetCallStack(data.CallStackIndex(), data);

                    // Create a call stack that ends with 'Disk READ <fileName> (<fileDirectory>)'
                    var filePath = data.FileName;
                    if (filePath.Length == 0)
                    {
                        filePath = "UNKNOWN";
                    }

                    var nodeName = string.Format("File {0}: {1} ({2})", data.OpcodeName,
                        GetFileName(filePath), GetDirectoryName(filePath));
                    var nodeIndex = stackSource.Interner.FrameIntern(nodeName);
                    stackIdx = stackSource.Interner.CallStackIntern(nodeIndex, stackIdx);

                    sample.StackIndex = stackIdx;
                    stackSource.AddSample(sample);
                });
                eventSource.Process();
            }
            else if (streamName == "Directory Enumerations")
            {
                eventSource.Kernel.AddCallbackForEvents<FileIODirEnumTraceData>(delegate (FileIODirEnumTraceData data)
                {
                    sample.Metric = 1;
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;

                    StackSourceCallStackIndex stackIdx = stackSource.GetCallStack(data.CallStackIndex(), data);

                    // Create a call stack that ends with 'Disk READ <fileName> (<fileDirectory>)'
                    var filePath = data.FileName;
                    if (filePath.Length == 0)
                    {
                        filePath = "UNKNOWN";
                    }

                    var nodeName = string.Format("File {0}: {1} ({2})", data.OpcodeName,
                        GetFileName(filePath), GetDirectoryName(filePath));
                    var nodeIndex = stackSource.Interner.FrameIntern(nodeName);
                    stackIdx = stackSource.Interner.CallStackIntern(nodeIndex, stackIdx);

                    sample.StackIndex = stackIdx;
                    stackSource.AddSample(sample);
                });
                eventSource.Process();
            }
            else if (streamName == "File I/O")
            {
                eventSource.Kernel.AddCallbackForEvents<FileIOReadWriteTraceData>(delegate (FileIOReadWriteTraceData data)
                {
                    sample.Metric = data.IoSize;
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;

                    StackSourceCallStackIndex stackIdx = stackSource.GetCallStack(data.CallStackIndex(), data);

                    // Create a call stack that ends with 'Disk READ <fileName> (<fileDirectory>)'
                    var filePath = data.FileName;
                    if (filePath.Length == 0)
                    {
                        filePath = "UNKNOWN";
                    }

                    var nodeName = string.Format("File {0}: {1} ({2})", data.OpcodeName,
                        GetFileName(filePath), GetDirectoryName(filePath));
                    var nodeIndex = stackSource.Interner.FrameIntern(nodeName);
                    stackIdx = stackSource.Interner.CallStackIntern(nodeIndex, stackIdx);

                    sample.StackIndex = stackIdx;
                    stackSource.AddSample(sample);
                });
                eventSource.Process();
            }
            else if (streamName == "Image Load")
            {
                var loadedImages = new Dictionary<Address, StackSourceCallStackIndex>(100);
                Action<ImageLoadTraceData> imageLoadUnload = delegate (ImageLoadTraceData data)
                {
                    // TODO this is not really correct, it assumes process IDs < 64K and images bases don't use lower bits
                    // but it is true 
                    Address imageKey = data.ImageBase + (Address)data.ProcessID;

                    sample.Metric = data.ImageSize;
                    if (data.Opcode == TraceEventOpcode.Stop)
                    {
                        sample.StackIndex = StackSourceCallStackIndex.Invalid;
                        StackSourceCallStackIndex allocIdx;
                        if (loadedImages.TryGetValue(imageKey, out allocIdx))
                        {
                            sample.StackIndex = allocIdx;
                        }

                        sample.Metric = -sample.Metric;
                    }
                    else
                    {
                        // Create a call stack that ends with 'Load <fileName> (<fileDirectory>)'
                        var fileName = data.FileName;
                        var nodeName = "Image Load " + GetFileName(fileName) + " (" + GetDirectoryName(fileName) + ")";
                        var nodeIndex = stackSource.Interner.FrameIntern(nodeName);
                        sample.StackIndex = stackSource.Interner.CallStackIntern(nodeIndex, stackSource.GetCallStack(data.CallStackIndex(), data));
                        loadedImages[imageKey] = sample.StackIndex;
                    }
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                    stackSource.AddSample(sample);
                };
                eventSource.Kernel.ImageLoad += imageLoadUnload;
                eventSource.Kernel.ImageUnload += imageLoadUnload;
                eventSource.Process();
            }
            else if (streamName == "Net Virtual Alloc")
            {
                var droppedEvents = 0;
                var memStates = new MemState[eventLog.Processes.Count];
                eventSource.Kernel.AddCallbackForEvents<VirtualAllocTraceData>(delegate (VirtualAllocTraceData data)
                                {
                                    bool isAlloc = false;
                                    if ((data.Flags & (
                                        VirtualAllocTraceData.VirtualAllocFlags.MEM_COMMIT |
                                        VirtualAllocTraceData.VirtualAllocFlags.MEM_DECOMMIT |
                                        VirtualAllocTraceData.VirtualAllocFlags.MEM_RELEASE)) != 0)
                                    {
                                        // Can't use data.Process() because some of the virtual allocs occur in the process that started the
                                        // process and occur before the process start event, which is what Process() uses to find it. 
                                        // TODO this code assumes that process launch is within 1 second and process IDs are not aggressively reused. 
                                        var processWhereMemoryAllocated = data.Log().Processes.GetProcess(data.ProcessID, data.TimeStampRelativeMSec + 1000);
                                        if (processWhereMemoryAllocated == null)
                                        {
                                            droppedEvents++;
                                            return;
                                        }

                                        var processIndex = processWhereMemoryAllocated.ProcessIndex;
                                        var memState = memStates[(int)processIndex];
                                        if (memState == null)
                                        {
                                            memState = memStates[(int)processIndex] = new MemState();
                                        }

                                        // Commit and decommit not both on together.  
                                        Debug.Assert((data.Flags &
                                                                    (VirtualAllocTraceData.VirtualAllocFlags.MEM_COMMIT | VirtualAllocTraceData.VirtualAllocFlags.MEM_DECOMMIT)) !=
                                                                    (VirtualAllocTraceData.VirtualAllocFlags.MEM_COMMIT | VirtualAllocTraceData.VirtualAllocFlags.MEM_DECOMMIT));

                                        var stackIndex = StackSourceCallStackIndex.Invalid;
                                        if ((data.Flags & VirtualAllocTraceData.VirtualAllocFlags.MEM_COMMIT) != 0)
                                        {
                                            isAlloc = true;
                                            // Some of the early allocations are actually by the process that starts this process.  Don't use their stacks 
                                            // But do count them.  
                                            var processIDAllocatingMemory = processWhereMemoryAllocated.ProcessID;  // This is not right, but it sets the condition properly below 
                                            var thread = data.Thread();
                                            if (thread != null)
                                            {
                                                processIDAllocatingMemory = thread.Process.ProcessID;
                                            }

                                            if (data.TimeStampRelativeMSec >= processWhereMemoryAllocated.StartTimeRelativeMsec && processIDAllocatingMemory == processWhereMemoryAllocated.ProcessID)
                                            {
                                                stackIndex = stackSource.GetCallStack(data.CallStackIndex(), data);
                                            }
                                            else
                                            {
                                                stackIndex = stackSource.GetCallStackForProcess(processWhereMemoryAllocated);
                                                stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("Allocated In Parent Process"), stackIndex);
                                            }
                                        }
                                        memState.Update(data.BaseAddr, data.Length, isAlloc, stackIndex,
                                            delegate (long metric, StackSourceCallStackIndex allocStack)
                                            {
                                                Debug.Assert(allocStack != StackSourceCallStackIndex.Invalid);
                                                Debug.Assert(metric != 0);                                                  // They should trim this already.  
                                                sample.Metric = metric;
                                                sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                                                sample.StackIndex = allocStack;
                                                stackSource.AddSample(sample);
                                                // Debug.WriteLine("Sample Proc {0,12} Time {1,8:f3} Length 0x{2:x} Metric 0x{3:x} Stack {4,8} Cum {5,8}", process.Name, sample.TimeRelativeMSec, data.Length, (int) sample.Metric, (int)sample.StackIndex, memState.TotalMem);
                                            });
                                    }
                                });
                eventSource.Process();
                if (droppedEvents != 0)
                {
                    log.WriteLine("WARNING: {0} events were dropped because their process could not be determined.", droppedEvents);
                }
            }
            else if (streamName == "Net Virtual Reserve")
            {
                // Mapped file (which includes image loads) logic. 
                var mappedImages = new Dictionary<Address, StackSourceCallStackIndex>(100);
                Action<MapFileTraceData> mapUnmapFile = delegate (MapFileTraceData data)
                {
                    sample.Metric = data.ViewSize;
                    // If it is a UnMapFile or MapFileDCStop event
                    if (data.Opcode == (TraceEventOpcode)38)
                    {
                        Debug.Assert(data.OpcodeName == "UnmapFile");
                        sample.StackIndex = StackSourceCallStackIndex.Invalid;
                        StackSourceCallStackIndex allocIdx;
                        if (mappedImages.TryGetValue(data.FileKey, out allocIdx))
                        {
                            sample.StackIndex = allocIdx;
                            mappedImages.Remove(data.FileKey);
                        }
                        sample.Metric = -sample.Metric;
                    }
                    else
                    {
                        Debug.Assert(data.OpcodeName == "MapFile" || data.OpcodeName == "MapFileDCStart");
                        // Create a call stack that ends with 'MapFile <fileName> (<fileDirectory>)'
                        var nodeName = "MapFile";
                        var fileName = data.FileName;
                        if (fileName.Length > 0)
                        {
                            nodeName = nodeName + " " + GetFileName(fileName) + " (" + GetDirectoryName(fileName) + ")";
                        }

                        var nodeIndex = stackSource.Interner.FrameIntern(nodeName);
                        sample.StackIndex = stackSource.Interner.CallStackIntern(nodeIndex, stackSource.GetCallStack(data.CallStackIndex(), data));
                        mappedImages[data.FileKey] = sample.StackIndex;
                    }
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                    stackSource.AddSample(sample);
                };
                eventSource.Kernel.FileIOMapFile += mapUnmapFile;
                eventSource.Kernel.FileIOUnmapFile += mapUnmapFile;
                eventSource.Kernel.FileIOMapFileDCStart += mapUnmapFile;

                // Virtual Alloc logic
                var droppedEvents = 0;
                var memStates = new MemState[eventLog.Processes.Count];
                var virtualReserverFrame = stackSource.Interner.FrameIntern("VirtualReserve");
                eventSource.Kernel.AddCallbackForEvents<VirtualAllocTraceData>(delegate (VirtualAllocTraceData data)
                {
                    bool isAlloc = false;
                    if ((data.Flags & (
                        VirtualAllocTraceData.VirtualAllocFlags.MEM_COMMIT |
                        VirtualAllocTraceData.VirtualAllocFlags.MEM_RESERVE |
                        VirtualAllocTraceData.VirtualAllocFlags.MEM_RELEASE)) != 0)
                    {
                        // Can't use data.Process() because some of the virtual allocs occur in the process that started the
                        // process and occur before the process start event, which is what Process() uses to find it. 
                        // TODO this code assumes that process launch is within 1 second and process IDs are not aggressively reused. 
                        var processWhereMemoryAllocated = data.Log().Processes.GetProcess(data.ProcessID, data.TimeStampRelativeMSec + 1000);
                        if (processWhereMemoryAllocated == null)
                        {
                            droppedEvents++;
                            return;
                        }

                        var processIndex = processWhereMemoryAllocated.ProcessIndex;
                        var memState = memStates[(int)processIndex];
                        if (memState == null)
                        {
                            memState = memStates[(int)processIndex] = new MemState();
                        }

                        // Commit and decommit not both on together.  
                        Debug.Assert((data.Flags &
                                                    (VirtualAllocTraceData.VirtualAllocFlags.MEM_COMMIT | VirtualAllocTraceData.VirtualAllocFlags.MEM_DECOMMIT)) !=
                                                    (VirtualAllocTraceData.VirtualAllocFlags.MEM_COMMIT | VirtualAllocTraceData.VirtualAllocFlags.MEM_DECOMMIT));
                        // Reserve and release not both on together.
                        Debug.Assert((data.Flags &
                                            (VirtualAllocTraceData.VirtualAllocFlags.MEM_RESERVE | VirtualAllocTraceData.VirtualAllocFlags.MEM_RELEASE)) !=
                                            (VirtualAllocTraceData.VirtualAllocFlags.MEM_RESERVE | VirtualAllocTraceData.VirtualAllocFlags.MEM_RELEASE));

                        // You allocate by committing or reserving.  We have already filtered out decommits which have no effect on reservation.  
                        // Thus the only memRelease is the only one that frees.  
                        var stackIndex = StackSourceCallStackIndex.Invalid;
                        if ((data.Flags & (VirtualAllocTraceData.VirtualAllocFlags.MEM_COMMIT | VirtualAllocTraceData.VirtualAllocFlags.MEM_RESERVE)) != 0)
                        {
                            isAlloc = true;
                            // Some of the early allocations are actually by the process that starts this process.  Don't use their stacks 
                            // But do count them.  
                            var processIDAllocatingMemory = processWhereMemoryAllocated.ProcessID;  // This is not right, but it sets the condition properly below 
                            var thread = data.Thread();
                            if (thread != null)
                            {
                                processIDAllocatingMemory = thread.Process.ProcessID;
                            }

                            if (data.TimeStampRelativeMSec >= processWhereMemoryAllocated.StartTimeRelativeMsec && processIDAllocatingMemory == processWhereMemoryAllocated.ProcessID)
                            {
                                stackIndex = stackSource.GetCallStack(data.CallStackIndex(), data);
                            }
                            else
                            {
                                stackIndex = stackSource.GetCallStackForProcess(processWhereMemoryAllocated);
                                stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("Allocated In Parent Process"), stackIndex);
                            }
                            stackIndex = stackSource.Interner.CallStackIntern(virtualReserverFrame, stackIndex);
                        }
                        memState.Update(data.BaseAddr, data.Length, isAlloc, stackIndex,
                            delegate (long metric, StackSourceCallStackIndex allocStack)
                            {
                                Debug.Assert(allocStack != StackSourceCallStackIndex.Invalid);
                                Debug.Assert(metric != 0);                                                  // They should trim this already.  
                                sample.Metric = metric;
                                sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                                sample.StackIndex = allocStack;
                                stackSource.AddSample(sample);
                                // Debug.WriteLine("Sample Proc {0,12} Time {1,8:f3} Length 0x{2:x} Metric 0x{3:x} Stack {4,8} Cum {5,8}", process.Name, sample.TimeRelativeMSec, data.Length, (int) sample.Metric, (int)sample.StackIndex, memState.TotalMem);
                            });
                    }
                });
                eventSource.Process();
                if (droppedEvents != 0)
                {
                    log.WriteLine("WARNING: {0} events were dropped because their process could not be determined.", droppedEvents);
                }
            }
            else if (streamName == "Net OS Heap Alloc")
            {
                // We index by heap address and then within the heap we remember the allocation stack
                var heaps = new Dictionary<Address, Dictionary<Address, StackSourceSample>>();

                var heapParser = new HeapTraceProviderTraceEventParser(eventSource);
                Dictionary<Address, StackSourceSample> lastHeapAllocs = null;

                // These three variables are used in the local function GetAllocationType defined below.
                // and are used to look up type names associated with the native allocations.   
                var loadedModules = new Dictionary<TraceModuleFile, NativeSymbolModule>();
                var allocationTypeNames = new Dictionary<CallStackIndex, string>();
                var symReader = GetSymbolReader(log, SymbolReaderOptions.CacheOnly);

                Address lastHeapHandle = 0;

                double peakMetric = 0;
                StackSourceSample peakSample = null;
                double cumMetric = 0;
                double sumCumMetric = 0;
                int cumCount = 0;

                heapParser.HeapTraceAlloc += delegate (HeapAllocTraceData data)
                {
                    var allocs = lastHeapAllocs;
                    if (data.HeapHandle != lastHeapHandle)
                        allocs = GetHeap(data.HeapHandle, heaps, ref lastHeapAllocs, ref lastHeapHandle);

                    var callStackIndex = data.CallStackIndex();
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                    sample.Metric = data.AllocSize;
                    sample.StackIndex = stackSource.GetCallStack(callStackIndex, data);

                    // Add the 'Alloc < XXX' pseudo node. 
                    var nodeIndex = stackSource.Interner.FrameIntern(GetAllocName((uint)data.AllocSize));
                    sample.StackIndex = stackSource.Interner.CallStackIntern(nodeIndex, sample.StackIndex);

                    // Add the 'Type ALLOCATION_TYPE' if available.  
                    string allocationType = GetAllocationType(callStackIndex);
                    if (allocationType != null)
                    {
                        nodeIndex = stackSource.Interner.FrameIntern("Type " + allocationType);
                        sample.StackIndex = stackSource.Interner.CallStackIntern(nodeIndex, sample.StackIndex);
                    }

                    var addedSample = stackSource.AddSample(sample);
                    allocs[data.AllocAddress] = addedSample;

                    cumMetric += sample.Metric;
                    if (cumMetric > peakMetric)
                    {
                        peakMetric = cumMetric;
                        peakSample = addedSample;
                    }
                    sumCumMetric += cumMetric;
                    cumCount++;

                    /*****************************************************************************/
                    // Performs a stack crawl to match the best typename to this allocation. 
                    // Returns null if no typename was found.
                    // This updates loadedModules and allocationTypeNames. It reads symReader/eventLog.
                    string GetAllocationType(CallStackIndex csi)
                    {
                        if (!allocationTypeNames.TryGetValue(csi, out var typeName))
                        {
                            const int frameLimit = 25; // typically you need about 10 frames to get out of the OS functions 
                                                       // to get to a frame that has type information.   We'll search up this many frames
                                                       // before giving up on getting type information for the allocation.  

                            int frameCount = 0;
                            for (var current = csi; current != CallStackIndex.Invalid && frameCount < frameLimit; current = eventLog.CallStacks.Caller(current), frameCount++)
                            {
                                var module = eventLog.CodeAddresses.ModuleFile(eventLog.CallStacks.CodeAddressIndex(current));
                                if (module == null)
                                    continue;

                                if (!loadedModules.TryGetValue(module, out var symbolModule))
                                {
                                    loadedModules[module] = symbolModule =
                                        (module.PdbSignature != Guid.Empty
                                            ? symReader.FindSymbolFilePath(module.PdbName, module.PdbSignature, module.PdbAge, module.FilePath)
                                            : symReader.FindSymbolFilePathForModule(module.FilePath)) is string pdb
                                        ? symReader.OpenNativeSymbolFile(pdb)
                                        : null;
                                }

                                typeName = symbolModule?.GetTypeForHeapAllocationSite(
                                        (uint)(eventLog.CodeAddresses.Address(eventLog.CallStacks.CodeAddressIndex(current)) - module.ImageBase)
                                    ) ?? typeName;
                            }
                            allocationTypeNames[csi] = typeName;
                        }
                        return typeName;
                    }
                };

                heapParser.HeapTraceFree += delegate (HeapFreeTraceData data)
                {
                    var allocs = lastHeapAllocs;
                    if (data.HeapHandle != lastHeapHandle)
                    {
                        allocs = GetHeap(data.HeapHandle, heaps, ref lastHeapAllocs, ref lastHeapHandle);
                    }

                    StackSourceSample alloc;
                    if (allocs.TryGetValue(data.FreeAddress, out alloc))
                    {
                        cumMetric -= alloc.Metric;
                        sumCumMetric += cumMetric;
                        cumCount++;

                        allocs.Remove(data.FreeAddress);

                        Debug.Assert(alloc.Metric >= 0);
                        sample.Metric = -alloc.Metric;
                        sample.TimeRelativeMSec = data.TimeStampRelativeMSec;

                        sample.StackIndex = alloc.StackIndex;
                        stackSource.AddSample(sample);
                    }
                };

                heapParser.HeapTraceReAlloc += delegate (HeapReallocTraceData data)
                {
                    // Reallocs that actually move stuff will cause a Alloc and delete event
                    // so there is nothing to do for those events.  But when the address is
                    // the same we need to resize 
                    if (data.OldAllocAddress != data.NewAllocAddress)
                    {
                        return;
                    }

                    var allocs = lastHeapAllocs;
                    if (data.HeapHandle != lastHeapHandle)
                    {
                        allocs = GetHeap(data.HeapHandle, heaps, ref lastHeapAllocs, ref lastHeapHandle);
                    }

                    // This is a clone of the Free code 
                    StackSourceSample alloc;
                    if (allocs.TryGetValue(data.OldAllocAddress, out alloc))
                    {
                        cumMetric -= alloc.Metric;
                        sumCumMetric += cumMetric;
                        cumCount++;

                        allocs.Remove(data.OldAllocAddress);

                        Debug.Assert(alloc.Metric == data.OldAllocSize);
                        sample.Metric = -alloc.Metric;
                        sample.TimeRelativeMSec = data.TimeStampRelativeMSec;

                        sample.StackIndex = alloc.StackIndex;
                        stackSource.AddSample(sample);
                    }

                    // This is a clone of the Alloc code (sigh don't clone code)
                    sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                    sample.Metric = data.NewAllocSize;
                    var nodeIndex = stackSource.Interner.FrameIntern(GetAllocName((uint)data.NewAllocSize));
                    sample.StackIndex = stackSource.Interner.CallStackIntern(nodeIndex, stackSource.GetCallStack(data.CallStackIndex(), data));
                    var addedSample = stackSource.AddSample(sample);
                    allocs[data.NewAllocAddress] = addedSample;

                    cumMetric += sample.Metric;
                    if (cumMetric > peakMetric)
                    {
                        peakMetric = cumMetric;
                        peakSample = addedSample;
                    }
                    sumCumMetric += cumMetric;
                    cumCount++;
                };

                heapParser.HeapTraceDestroy += delegate (HeapTraceData data)
                {
                    // Heap is dieing, kill all objects in it.   
                    var allocs = lastHeapAllocs;
                    if (data.HeapHandle != lastHeapHandle)
                    {
                        allocs = GetHeap(data.HeapHandle, heaps, ref lastHeapAllocs, ref lastHeapHandle);
                    }

                    foreach (StackSourceSample alloc in allocs.Values)
                    {
                        // TODO this is a clone of the free code.  
                        cumMetric -= alloc.Metric;
                        sumCumMetric += cumMetric;
                        cumCount++;

                        Debug.Assert(alloc.Metric >= 0);
                        sample.Metric = -alloc.Metric;
                        sample.TimeRelativeMSec = data.TimeStampRelativeMSec;

                        sample.StackIndex = alloc.StackIndex;
                        stackSource.AddSample(sample);
                    }
                };
                eventSource.Process();

                var aveCumMetric = sumCumMetric / cumCount;
                log.WriteLine("Peak Heap Size: {0:n3} MB   Average Heap size: {1:n3} MB", peakMetric / 1000000.0F, aveCumMetric / 1000000.0F);
                if (peakSample != null)
                {
                    log.WriteLine("Peak happens at {0:n3} Msec into the trace.", peakSample.TimeRelativeMSec);
                }

                log.WriteLine("Trimming alloc-free pairs < 3 msec apart: Before we have {0:n1}K events now {1:n1}K events",
                    cumCount / 1000.0, stackSource.SampleIndexLimit / 1000.0);
                return stackSource;
            }
            else if (streamName == "Server GC")
            {
                Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.NeedLoadedDotNetRuntimes(eventSource);
                Microsoft.Diagnostics.Tracing.Analysis.TraceProcessesExtensions.AddCallbackOnProcessStart(eventSource, proc =>
                {
                    Microsoft.Diagnostics.Tracing.Analysis.TraceProcessesExtensions.SetSampleIntervalMSec(proc, eventLog.SampleProfileInterval.TotalMilliseconds);
                    Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.SetMutableTraceEventStackSource(proc, stackSource);
                });
                eventSource.Process();
                return stackSource;
            }
            else if(streamName == "Anti-Malware Real-Time Scan")
            {
                RealtimeAntimalwareComputer computer = new RealtimeAntimalwareComputer(eventSource, stackSource);
                computer.Execute();

                return stackSource;
            }
            else
            {
                throw new Exception("Unknown stream " + streamName);
            }

            log.WriteLine("Produced {0:n3}K events", stackSource.SampleIndexLimit / 1000.0);
            stackSource.DoneAddingSamples();
            return stackSource;
        }

        #region private
        private static StackSource GetProcessFileRegistryStackSource(TraceLogEventSource eventSource, TextWriter log)
        {
            TraceLog traceLog = eventSource.TraceLog;

            // This maps a process Index to the stack that represents that process.  
            StackSourceCallStackIndex[] processStackCache = new StackSourceCallStackIndex[traceLog.Processes.Count];
            for (int i = 0; i < processStackCache.Length; i++)
            {
                processStackCache[i] = StackSourceCallStackIndex.Invalid;
            }

            var stackSource = new MutableTraceEventStackSource(eventSource.TraceLog);

            StackSourceSample sample = new StackSourceSample(stackSource);

            var fileParser = new MicrosoftWindowsKernelFileTraceEventParser(eventSource);

            fileParser.Create += delegate (FileIOCreateTraceData data)
            {
                StackSourceCallStackIndex stackIdx = GetStackForProcess(data.Process(), traceLog, stackSource, processStackCache);
                stackIdx = stackSource.GetCallStack(data.CallStackIndex(), stackIdx);
                string imageFrameString = string.Format("FileOpenOrCreate {0}", data.FileName);
                StackSourceFrameIndex imageFrameIdx = stackSource.Interner.FrameIntern(imageFrameString);
                stackIdx = stackSource.Interner.CallStackIntern(imageFrameIdx, stackIdx);

                sample.Count = 1;
                sample.Metric = 1;
                sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                sample.StackIndex = stackIdx;
                stackSource.AddSample(sample);
            };

            eventSource.Kernel.AddCallbackForEvents(delegate (ImageLoadTraceData data)
            {
                StackSourceCallStackIndex stackIdx = GetStackForProcess(data.Process(), traceLog, stackSource, processStackCache);
                stackIdx = stackSource.GetCallStack(data.CallStackIndex(), stackIdx);
                string fileCreateFrameString = string.Format("ImageLoad Base 0x{0:x} Size 0x{1:x} Name {2}", data.ImageBase, data.ImageSize, data.FileName);
                StackSourceFrameIndex fileCreateFrameIdx = stackSource.Interner.FrameIntern(fileCreateFrameString);
                stackIdx = stackSource.Interner.CallStackIntern(fileCreateFrameIdx, stackIdx);

                sample.Count = 1;
                sample.Metric = 1;
                sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                sample.StackIndex = stackIdx;
                stackSource.AddSample(sample);
            });

            eventSource.Process();
            stackSource.DoneAddingSamples();
            return stackSource;
        }
        private static StackSourceCallStackIndex GetStackForProcess(TraceProcess process, TraceLog traceLog, MutableTraceEventStackSource stackSource, StackSourceCallStackIndex[] processStackCache)
        {
            if (process == null)
            {
                return StackSourceCallStackIndex.Invalid;
            }

            var ret = processStackCache[(int)process.ProcessIndex];
            if (ret == StackSourceCallStackIndex.Invalid)
            {
                StackSourceCallStackIndex parentStack = StackSourceCallStackIndex.Invalid;
                parentStack = GetStackForProcess(process.Parent, traceLog, stackSource, processStackCache);

                string parent = "";
                if (parentStack == StackSourceCallStackIndex.Invalid)
                {
                    parent += ",Parent=" + process.ParentID;
                }

                string command = process.CommandLine;
                if (string.IsNullOrWhiteSpace(command))
                {
                    command = process.ImageFileName;
                }

                string processFrameString = string.Format("Process({0}{1}): {2}", process.ProcessID, parent, command);

                StackSourceFrameIndex processFrameIdx = stackSource.Interner.FrameIntern(processFrameString);
                ret = stackSource.Interner.CallStackIntern(processFrameIdx, parentStack);
            }
            return ret;
        }

        private static string GetDirectoryName(string filePath)
        {
            // We need long (over 260) file name support so we do this by hand.  
            var lastSlash = filePath.LastIndexOf('\\');
            if (lastSlash < 0)
            {
                return "";
            }

            return filePath.Substring(0, lastSlash + 1);
        }

        private static string GetFileName(string filePath)
        {
            // We need long (over 260) file name support so we do this by hand.  
            var lastSlash = filePath.LastIndexOf('\\');
            if (lastSlash < 0)
            {
                return filePath;
            }

            return filePath.Substring(lastSlash + 1);
        }

        /// <summary>
        /// Implements a simple one-element cache for find the heap to look in.  
        /// </summary>
        private static Dictionary<Address, StackSourceSample> GetHeap(Address heapHandle, Dictionary<Address, Dictionary<Address, StackSourceSample>> heaps, ref Dictionary<Address, StackSourceSample> lastHeapAllocs, ref Address lastHeapHandle)
        {
            Dictionary<Address, StackSourceSample> ret;

            if (!heaps.TryGetValue(heapHandle, out ret))
            {
                ret = new Dictionary<Address, StackSourceSample>();
                heaps.Add(heapHandle, ret);
            }
            lastHeapHandle = heapHandle;
            lastHeapAllocs = ret;
            return ret;
        }

        private static void LogGCHandleLifetime(MutableTraceEventStackSource stackSource,
            StackSourceSample sample, GCHandleInfo info, double timeRelativeMSec, TextWriter log)
        {
            sample.Metric = timeRelativeMSec - info.PinStartTimeRelativeMSec;
            if (sample.Metric < 0)
            {
                log.WriteLine("Error got a negative time at {0:n3} started {1:n3}.  Dropping", timeRelativeMSec, info.PinStartTimeRelativeMSec);
                return;
            }

            var stackIndex = info.PinStack;
            var roundToLog = Math.Pow(10.0, Math.Ceiling(Math.Log10(sample.Metric)));
            var nodeName = "LIVE_FOR <= " + roundToLog + " msec";
            var nodeIndex = stackSource.Interner.FrameIntern(nodeName);
            stackIndex = stackSource.Interner.CallStackIntern(nodeIndex, stackIndex);

            nodeName = "OBJECT_INSTANCEID = " + info.ObjectAddress;
            nodeIndex = stackSource.Interner.FrameIntern(nodeName);
            stackIndex = stackSource.Interner.CallStackIntern(nodeIndex, stackIndex);

            sample.TimeRelativeMSec = info.PinStartTimeRelativeMSec;
            sample.StackIndex = stackIndex;
            stackSource.AddSample(sample);
        }

        private class PerThreadGCHandleInfo
        {
            public Address LastHandle;
            public Address LastObject;
            public Address LikelyAsyncHandleTable1;
            public Address LikelyAsyncHandleTable2;
        }

        private class GCHandleInfo
        {
            public double PinStartTimeRelativeMSec;
            public Address ObjectAddress;
            public StackSourceCallStackIndex PinStack = StackSourceCallStackIndex.Invalid;
            public bool IsAsync;
            public byte GCGen;
        }

        public override List<IProcess> GetProcesses(TextWriter log)
        {
            var eventLog = GetTraceLog(log);
            var processes = new List<IProcess>(eventLog.Processes.Count);
            foreach (var process in eventLog.Processes)
            {
                var iprocess = new IProcessForStackSource(process.Name);
                iprocess.StartTime = process.StartTime;
                iprocess.EndTime = process.EndTime;
                iprocess.CPUTimeMSec = process.CPUMSec;
                iprocess.ParentID = process.ParentID;
                iprocess.CommandLine = process.CommandLine;
                iprocess.ProcessID = process.ProcessID;
                processes.Add(iprocess);
            }
            processes.Sort();
            return processes;
        }


        /// <summary>
        /// Class keeps track of the memory state given virtual allocs.  Basically you have to model what memory is allocated 
        /// </summary>
        private class MemState
        {
            public MemState()
            {
                m_searchTable.Add(new Region(0, Region.FreeStackIndex, null));  // Memory starts out completely free.  
                m_numRegions = 1;
            }
            [Conditional("DEBUG")]
            private void ClassInvarient()
            {
                Debug.Assert(0 < m_searchTable.Count);
                var prev = m_searchTable[0];
                Debug.Assert(prev.MemAddr == 0);
                var cur = prev.Next;
                var regionCount = 1;        // Total number of regions in my linked list
                var curIdx = 1;             // Index in my sorted m_searchTable
                while (cur != null)
                {
                    // Update the curIdx.   Note that you can have multiple entries pointing to the same location (this is how we delete regions
                    // without having to shuffle the table.
                    while (curIdx < m_searchTable.Count && m_searchTable[curIdx] == cur)
                    {
                        curIdx++;
                    }

                    Debug.Assert(m_searchTable.Count <= curIdx || cur.MemAddr < m_searchTable[curIdx].MemAddr);

                    Debug.Assert(prev.MemAddr < cur.MemAddr);     // strictly increasing
                    Debug.Assert(!(cur.Next == null && cur.AllocStack != Region.FreeStackIndex && cur.MemAddr != ulong.MaxValue));
                    prev = cur;
                    cur = cur.Next;
                    regionCount++;
                }
                Debug.Assert(regionCount == m_numRegions);          // m_numRegions is accurate.  
                Debug.Assert(curIdx == m_searchTable.Count);        // One entries in the table are in the list.  
            }
#if DEBUG
            private int Count
            {
                get
                {
                    var cur = m_searchTable[0];
                    int cnt = 0;
                    while (cur != null)
                    {
                        cur = cur.Next;
                        cnt++;
                    }
                    return cnt;
                }
            }
#endif
#if DEBUG
            /// <summary>
            /// This routine is only used in asserts.   It represents the total amount of net memory that has been
            /// committed by all the VirtualAllocs/Frees that have occurred so far.  
            /// </summary>
            public long TotalMem
            {
                get
                {
                    long ret = 0;
                    var cur = m_searchTable[0];
                    while (cur != null)
                    {
                        if (!cur.IsFree)
                        {
                            ret += (long)(cur.Next.MemAddr - cur.MemAddr);
                        }

                        cur = cur.Next;
                    }
                    return ret;
                }
            }
#endif

            /// <summary>
            /// updates the memory state of [startAddr, startAddr+length) to be either allocated or free (based on 'isAlloc').  
            /// It returns the amount of memory delta (positive for allocation, negative for free).
            /// 
            /// What makes this a pain is VirtuaAlloc regions can overlap (you can 'commit' the same region multiple times, or
            /// free just a region within an allocation etc).   
            /// 
            /// Thus you have to keep track of exactly what is allocated (we keep a sorted list of regions), and do set operations
            /// on these regions.   This is what makes it non-trivial.  
            /// 
            /// if 'isAlloc' is true, then allocStack should be the stack at that allocation.  
            /// 
            /// 'callback' is called with two parameters (the net memory change (will be negative for frees), as well as the call
            /// stack for the ALLOCATION (even in the case of a free, it is the allocation stack that is logged).   
            /// 
            /// If an allocation overlaps with an existing allocation, only the NET allocation is indicated (the existing allocated
            /// region is subtracted out.   This means is is the 'last' allocation that gets 'charged' for a region.
            /// 
            /// The main point, however is that there is no double-counting and get 'perfect' matching of allocs and frees. 
            /// 
            /// There may be more than one callback issued if the given input region covers several previously allocated regions
            /// and thus need to be 'split up'.  In the case of a free, several callbacks could be issued because different 
            /// allocation call stacks were being freed in a single call.  
            /// </summary>
            internal void Update(Address startAddr, long length, bool isAlloc, StackSourceCallStackIndex allocStack,
                Action<long, StackSourceCallStackIndex> callback)
            {
                Debug.Assert(startAddr != 0);                   // No on can allocate this virtual address.
                if (startAddr == 0)
                {
                    return;
                }

                Address endAddr = startAddr + (Address)length;  // end of range
                if (endAddr == 0)                               // It is possible to wrap around (if you allocate the last region of memory. 
                {
                    endAddr = ulong.MaxValue;                   // Avoid this case by adjust it down a bit.  
                }

                Debug.Assert(endAddr > startAddr);
                if (!isAlloc)
                {
                    allocStack = Region.FreeStackIndex;
                }

                m_totalUpdates++;
#if DEBUG
                long memoryBeforeUpdate = TotalMem;
                long callBackNet = 0;               // How much we said the net allocation was for all the callbacks we make.  
#endif

                Debug.Assert(allocStack != StackSourceCallStackIndex.Invalid);
                // From time to time, update the search table to be 'perfect' if we see that chain length is too high.  
                if (m_totalUpdates > m_searchTable.Count && m_totalChainTraverals > MaxChainLength * m_totalUpdates)
                {
                    Debug.WriteLine(string.Format("Redoing Search table.  Num Regions {0} Table Size {1}  numUpdates {2} Average Chain Leng {3}",
                        m_numRegions, m_searchTable.Count, m_totalUpdates, m_totalChainTraverals / m_totalUpdates));
                    ExpandSearchTable();
                    m_totalUpdates = 0;
                    m_totalChainTraverals = 0;
                }

                int searchTableIdx;             // Points at prev or before.  
                m_searchTable.BinarySearch(startAddr - 1, out searchTableIdx, delegate (Address x, Region y) { return x.CompareTo(y.MemAddr); });
                Debug.Assert(0 <= searchTableIdx);          // Can't get -1 because 0 is the smallest number 
                Region prev = m_searchTable[searchTableIdx];

                Region cur = prev.Next;                         // current node
                Debug.Assert(prev.MemAddr <= startAddr);

                Debug.WriteLine(string.Format("Addr {0:x} idx {1} prev {2:x}", startAddr, searchTableIdx, prev.MemAddr));
                for (int chainLength = 0; ; chainLength++)      // the amount of searching I need to do after binary search 
                {
                    m_totalChainTraverals++;

                    // If we fall off the end, 'clone' split the last region into one that exactly overlaps the new region.  
                    if (cur == null)
                    {
                        prev.Next = cur = new Region(endAddr, prev.AllocStack, null);
                        m_numRegions++;
                        if (chainLength > MaxChainLength)
                        {
                            m_searchTable.Add(cur);
                        }
                    }

                    // Does the new region start after (or at) prev and strictly before than cur? (that is, does the region overlap with prev?)
                    if (startAddr < cur.MemAddr)
                    {
                        var prevAllocStack = prev.AllocStack;       // Remember this since we clobber it.  

                        // Can I reuse the node (it starts at exactly the right place, or it is the same stack 
                        // (which I can coalesce))
                        if (startAddr == prev.MemAddr || prevAllocStack == allocStack)
                        {
                            prev.AllocStack = allocStack;
                        }
                        else
                        {
                            prev.Next = new Region(startAddr, allocStack, cur);
                            m_numRegions++;
                            prev = prev.Next;
                        }

                        // Try to break up long chains in the search table.   
                        if (chainLength > MaxChainLength)
                        {
                            Debug.Assert(searchTableIdx < m_searchTable.Count);
                            if (searchTableIdx + 1 == m_searchTable.Count)
                            {
                                m_searchTable.Add(prev);
                            }
                            else
                            {
                                Debug.Assert(m_searchTable[searchTableIdx].MemAddr <= prev.MemAddr);
                                // Make sure we remain sorted.   Note that we can exceed the next slot in the table because
                                // the region we are inserting 'covers' many table entries.   
                                if (m_searchTable.Count <= searchTableIdx + 2 || prev.MemAddr < m_searchTable[searchTableIdx + 2].MemAddr)
                                {
                                    m_searchTable[searchTableIdx + 1] = prev;
                                }
                            }
                            searchTableIdx++;
                            chainLength = 0;
                        }

                        // net is the amount we are freeing or allocating for JUST THIS FIRST overlapped region (prev to cur)
                        // We start out assuming that the new region is bigger than the current region, so the net is the full current region.  
                        long net = (long)(cur.MemAddr - startAddr);

                        // Does the new region fit completely between prev and cur?  
                        bool overlapEnded = (endAddr <= cur.MemAddr);
                        if (overlapEnded)
                        {
                            net = (long)(endAddr - startAddr);
                            // If it does not end exactly, we need to end our chunk and resume the previous region.  
                            if (endAddr != cur.MemAddr && prevAllocStack != allocStack)
                            {
                                prev.Next = new Region(endAddr, prevAllocStack, cur);
                                m_numRegions++;
                            }
                        }
                        Debug.Assert(net >= 0);

                        // Log the delta to the callback.  
                        StackSourceCallStackIndex stackToLog;
                        if (allocStack != Region.FreeStackIndex)        // Is the update an allocation.  
                        {
                            if (prevAllocStack != Region.FreeStackIndex)
                            {
                                net = 0;                                // committing a committed region, do nothing
                            }

                            stackToLog = allocStack;
                        }
                        else    // The update is a free.  
                        {
                            if (prevAllocStack == Region.FreeStackIndex)
                            {
                                net = 0;                                // freeing a freed region, do nothing  
                            }

                            net = -net;                                 // frees have negative weight. 
                            stackToLog = prevAllocStack;                // We attribute the free to the allocation call stack  
                        }
                        ClassInvarient();

                        if (net != 0)                                   // Make callbacks to user code if there is any change.  
                        {
#if DEBUG
                            callBackNet += net;
#endif
                            callback(net, stackToLog);                  // issue the callback
                        }

                        if (overlapEnded || endAddr == 0)               // Are we done?  (endAddr == 0 is for the case where the region wraps around).  
                        {
#if DEBUG
                            Debug.Assert(memoryBeforeUpdate + callBackNet == TotalMem);
                            Debug.WriteLine(string.Format("EXITING Num Regions {0} Table Size {1}  numUpdates {2} Average Chain Len {3}",
                                m_numRegions, m_searchTable.Count, m_totalUpdates, m_totalChainTraverals * 1.0 / m_totalUpdates));
#endif
                            // Debug.Write("**** Exit State\r\n" + this.ToString());
                            return;
                        }

                        startAddr = cur.MemAddr;       // Modify the region so that it no longer includes the overlap with 'prev'  
                    }

                    // we may be able to coalesce (probably free) nodes.  
                    if (prev.AllocStack == cur.AllocStack)
                    {
                        prev.Next = cur.Next;       // Remove cur (prev does not move)
                        --m_numRegions;

                        // Make sure there are no entries in the search table that point at the entry to be deleted.   
                        var idx = searchTableIdx;
                        do
                        {
                            Debug.Assert(m_searchTable[idx].MemAddr <= cur.MemAddr);
                            if (cur == m_searchTable[idx])
                            {
                                // Assert that we stay sorted.  
                                Debug.Assert(idx == 0 || m_searchTable[idx - 1].MemAddr <= prev.MemAddr);
                                Debug.Assert(idx + 1 == m_searchTable.Count || prev.MemAddr <= m_searchTable[idx + 1].MemAddr);
                                m_searchTable[idx] = prev;
                            }
                            idx++;
                        } while (idx < m_searchTable.Count && m_searchTable[idx].MemAddr <= cur.MemAddr);

                        ClassInvarient();
                    }
                    else
                    {
                        prev = cur;                 // prev advances to cur 
                    }

                    cur = cur.Next;
                }
            }

            /// <summary>
            /// Allocate a new search table that has all the regions in it with not chaining necessary.   
            /// </summary>
            private void ExpandSearchTable()
            {
                Region ptr = m_searchTable[0];
                m_searchTable = new GrowableArray<Region>(m_numRegions + MaxChainLength);   // Add a bit more to grow on the end if necessary.  
                while (ptr != null)
                {
                    m_searchTable.Add(ptr);
                    ptr = ptr.Next;
                }
                Debug.Assert(m_searchTable.Count == m_numRegions);
            }

            private const int MaxChainLength = 8;           // We don't want chain lengths bigger than this.  
                                                            // The state of memory is represented as a (sorted) linked list of addresses (with a stack), 
                                                            // Some of the regions are free (marked by FreeStackIndex)  They only have a start address so by 
                                                            // construction they can't overlap.  

            private class Region
            {
                // The special value that represents a free region.  
                public const StackSourceCallStackIndex FreeStackIndex = (StackSourceCallStackIndex)(-2);
                /// <summary>
                /// Create an allocation region starting at 'startAddr' allocated at 'allocStack'
                /// </summary>
                public Region(Address memAddr, StackSourceCallStackIndex allocStack, Region next) { MemAddr = memAddr; AllocStack = allocStack; Next = next; }
                public bool IsFree { get { return AllocStack == FreeStackIndex; } }

                public Address MemAddr;
                public StackSourceCallStackIndex AllocStack;
                public Region Next;
            };
#if DEBUG
            public override string ToString()
            {
                var sb = new StringBuilder();
                var cur = m_searchTable[0];
                while (cur != null)
                {
                    sb.Append("[").Append(cur.MemAddr.ToString("X")).Append(" stack=").Append(cur.AllocStack).Append("]").AppendLine();
                    cur = cur.Next;
                }
                return sb.ToString();
            }
#endif
            /// <summary>
            /// The basic data structure here is a linked list where each element is ALSO in this GrowableArray of
            /// entry points into that list.   This array of entry points is SORTED, so we can do binary search to 
            /// find a particular entry in log(N) time.   However we want to support fast insertion (and I am too
            /// lazy to implement a self-balancing tree) so when we add entries we add them to the linked list but
            /// not necessarily to this binary search table.   From time to time we will 'fixup' this table to 
            /// be perfect again.   
            /// </summary>
            private GrowableArray<Region> m_searchTable;        // always non-empty, first entry is linked list to all entries.  

            // Keep track of enough to compute the average chain length on lookups.   
            private int m_totalChainTraverals;                  // links we have to traverse from the search table to get to the entry we want.
            private int m_totalUpdates;                         // Number of lookups we did.  (We reset after every table expansion).   
            private int m_numRegions;                           // total number of entries in our linked list (may be larger than the search table) 
        }


        private static string[] AllocNames = InitAllocNames();
        private static string[] InitAllocNames()
        {
            // Cache the names, so we don't create them on every event.  
            string[] ret = new string[16];
            int size = 1;
            for (int i = 0; i < ret.Length; i++)
            {
                ret[i] = "Alloc < " + size;
                size *= 2;
            }
            return ret;
        }
        private static string GetAllocName(uint metric)
        {
            string allocName;
            int log2Metric = 0;
            while (metric > 0)
            {
                metric >>= 1;
                log2Metric++;
            }
            if (log2Metric < AllocNames.Length)
            {
                allocName = AllocNames[log2Metric];
            }
            else
            {
                allocName = "Alloc >= 32768";
            }

            return allocName;
        }
        #endregion

        protected internal override void ConfigureStackWindow(string stackSourceName, StackWindow stackWindow)
        {
            ConfigureAsEtwStackWindow(stackWindow, stackSourceName.StartsWith("CPU"));

            if (stackSourceName == "Processes / Files / Registry")
            {
                var defaultFold = @"^FileOpenOrCreate*:\Windows\Sys;^ImageLoad*:\Windows\Sys;^Process*conhost";
                stackWindow.FoldRegExTextBox.Items.Add(defaultFold);
                stackWindow.FoldRegExTextBox.Text = defaultFold;

                stackWindow.CallTreeTab.IsSelected = true;      // start with the call tree view
                return;
            }

            if (stackSourceName.Contains("(with Tasks)") || stackSourceName.Contains("(with StartStop Activities)"))
            {
                var taskFoldPatBase = "ntoskrnl!%ServiceCopyEnd;System.Runtime.CompilerServices.Async%MethodBuilder";
                var taskFoldPat = taskFoldPatBase + ";^STARTING TASK";
                stackWindow.FoldRegExTextBox.Items.Add(taskFoldPat);
                stackWindow.FoldRegExTextBox.Items.Add(taskFoldPatBase);

                // If the new pattern is a superset of the old, then use it.  
                if (taskFoldPat.StartsWith(stackWindow.FoldRegExTextBox.Text))
                {
                    stackWindow.FoldRegExTextBox.Text = taskFoldPat;
                }

                stackWindow.GroupRegExTextBox.Items.Insert(0, @"[Nuget] System.%!=>OTHER;Microsoft.%!=>OTHER;mscorlib%=>OTHER;v4.0.30319%\%!=>OTHER;system32\*!=>OTHER;syswow64\*!=>OTHER");

                var excludePat = "LAST_BLOCK";
                stackWindow.ExcludeRegExTextBox.Items.Add(excludePat);
                stackWindow.ExcludeRegExTextBox.Items.Add("LAST_BLOCK;Microsoft.Owin.Host.SystemWeb!*IntegratedPipelineContextStage.BeginEvent;Microsoft.Owin.Host.SystemWeb!*IntegratedPipelineContextStage*RunApp");
                stackWindow.ExcludeRegExTextBox.Text = excludePat;
            }

            if (stackSourceName.StartsWith("CPU") || stackSourceName.Contains("Thread Time"))
            {
                if (m_traceLog != null)
                {
                    stackWindow.ExtraTopStats += " TotalProcs " + m_traceLog.NumberOfProcessors;
                }

                stackWindow.ScalingPolicy = ScalingPolicyKind.TimeMetric;
                if (!stackSourceName.Contains("Thread Time"))
                {
                    stackWindow.FoldPercentTextBox.Text = stackWindow.GetDefaultFoldPercentage();
                }
            }

            if (stackSourceName.StartsWith("Processor"))
            {
                stackWindow.GroupRegExTextBox.Items.Insert(0, "Processor ({%}) Priority ({%})->Priority ($2)");
                stackWindow.GroupRegExTextBox.Items.Insert(0, "Processor ({%}) Priority ({%})->Processor ($1)");
            }

            if (stackSourceName == "Net OS Heap Alloc" || stackSourceName == "Image Load" || stackSourceName == "Disk I/O" ||
                stackSourceName == "File I/O" || stackSourceName == "Exceptions" || stackSourceName == "Managed Load" || stackSourceName.StartsWith("Process")
                || stackSourceName.StartsWith("Virtual") || stackSourceName == "Pinning" || stackSourceName.Contains("Thread Time"))
            {
                stackWindow.CallTreeTab.IsSelected = true;      // start with the call tree view
            }

            if (stackSourceName == "Pinning")
            {
                string defaultFoldPattern = "OBJECT_INSTANCEID;LIVE_FOR";
                stackWindow.FoldRegExTextBox.Text = defaultFoldPattern;
                stackWindow.FoldRegExTextBox.Items.Insert(0, defaultFoldPattern);
            }

            if (stackSourceName == "Pinning At GC Time")
            {
                string defaultFoldPattern = "^PINNED_FOR;^GC_NUM";
                stackWindow.FoldRegExTextBox.Text = defaultFoldPattern;
                stackWindow.FoldRegExTextBox.Items.Insert(0, defaultFoldPattern);

                stackWindow.GroupRegExTextBox.Text = "mscorlib.ni!->;system.ni!->;{%}!=>module $1;Generation 0->NonGen2;Generation 1->NonGen2;Type System.Byte[]->Type System.Byte[]";
                stackWindow.ExcludeRegExTextBox.Items.Insert(0, "PinnableBufferCache.CreateNewBuffers");
            }

            if (stackSourceName.Contains("Ref Count"))
            {
                stackWindow.RemoveColumn("IncPercentColumn");
                stackWindow.RemoveColumn("ExcPercentColumn");
            }

            if (stackSourceName.Contains("CCW Ref Count"))
            {
                string defaultFoldPattern = "CCW NewRefCnt;CCW AddRef;CCW Release";
                stackWindow.FoldRegExTextBox.Text = defaultFoldPattern;
                stackWindow.FoldRegExTextBox.Items.Insert(0, defaultFoldPattern);
                stackWindow.FoldRegExTextBox.Items.Insert(1, "CCW NewRefCnt");
            }

            if ((stackSourceName == "Heap Snapshot Pinning") || (stackSourceName == "Heap Snapshot Pinned Object Allocation"))
            {
                string defaultFoldPattern = "OBJECT_INSTANCE";
                stackWindow.FoldRegExTextBox.Text = defaultFoldPattern;
                stackWindow.FoldRegExTextBox.Items.Insert(0, defaultFoldPattern);
            }

            if (stackSourceName == "Net OS Heap Alloc" || stackSourceName.StartsWith("GC Heap Net Mem") ||
                stackSourceName.StartsWith("Virtual") || stackSourceName.StartsWith("GC Heap Alloc Ignore Free"))
            {
                stackWindow.ComputeMaxInTopStats = true;
            }

            if (stackSourceName == "Net OS Heap Alloc")
            {
                stackWindow.FoldRegExTextBox.Items.Insert(0, "^Alloc");
            }

            if (stackSourceName.StartsWith("ASP.NET Thread Time"))
            {
                var prev = stackWindow.FoldRegExTextBox.Text;
                if (0 < prev.Length)
                {
                    prev += ";";
                }

                prev += "^Request URL";
                stackWindow.FoldRegExTextBox.Text = prev;
                stackWindow.FoldRegExTextBox.Items.Insert(0, prev);
            }

            if (m_extraTopStats != null)
            {
                stackWindow.ExtraTopStats = m_extraTopStats;
            }

            // Warn the user about the behavior of type name lookup, but only once per user.  
            if (stackSourceName == "Net OS Heap Alloc")
            {
                if (App.ConfigData["WarnedAboutOsHeapAllocTypes"] == null)
                {
                    MessageBox.Show(stackWindow,
                        "Warning: Allocation type resolution only happens on window launch.\r\n" +
                        "Thus if you manually lookup symbols in this view you will get method\r\n" +
                        "names of allocations sites, but to get the type name associated the \r\n" +
                        "allocation site.\r\n" +
                        "\r\n" +
                        "You must close and reopen this window to get the allocation types.\r\n"
                        , "May need to resolve PDBs and reopen.");
                    App.ConfigData["WarnedAboutOsHeapAllocTypes"] = "true";
                }
            }
        }
        public override bool SupportsProcesses { get { return true; } }

        /// <summary>
        /// Find symbols for the simple module name 'simpleModuleName.  If 'processId' is non-zero then only search for modules loaded in that
        /// process, otherwise look systemWide.
        /// </summary>
        public override void LookupSymbolsForModule(string simpleModuleName, TextWriter log, int processId = 0)
        {
            var symReader = GetSymbolReader(log);

            // If we have a process, look the DLL up just there
            var moduleFiles = new Dictionary<int, TraceModuleFile>();
            if (processId != 0)
            {
                var process = m_traceLog.Processes.LastProcessWithID(processId);
                if (process != null)
                {
                    foreach (var loadedModule in process.LoadedModules)
                    {
                        if (string.Compare(loadedModule.Name, simpleModuleName, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            moduleFiles[(int)loadedModule.ModuleFile.ModuleFileIndex] = loadedModule.ModuleFile;
                        }
                    }
                }
            }

            // We did not find it, try system-wide
            if (moduleFiles.Count == 0)
            {
                foreach (var moduleFile in m_traceLog.ModuleFiles)
                {
                    if (string.Compare(moduleFile.Name, simpleModuleName, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        moduleFiles[(int)moduleFile.ModuleFileIndex] = moduleFile;
                    }
                }
            }

            if (moduleFiles.Count == 0)
            {
                throw new ApplicationException("Could not find module " + simpleModuleName + " in trace.");
            }

            if (moduleFiles.Count > 1)
            {
                log.WriteLine("Found {0} modules with name {1}", moduleFiles.Count, simpleModuleName);
            }

            foreach (var moduleFile in moduleFiles.Values)
            {
                m_traceLog.CodeAddresses.LookupSymbolsForModule(symReader, moduleFile);
            }
        }
        protected override Action<Action> OpenImpl(Window parentWindow, StatusBar worker)
        {
            var tracelog = GetTraceLog(worker.LogWriter, delegate (bool truncated, int numberOfLostEvents, int eventCountAtTrucation)
            {
                if (!m_notifiedAboutLostEvents)
                {
                    HandleLostEvents(parentWindow, truncated, numberOfLostEvents, eventCountAtTrucation, worker);
                    m_notifiedAboutLostEvents = true;
                }
            });

            // Warn about possible Win8 incompatibility.  
            var logVer = tracelog.OSVersion.Major * 10 + tracelog.OSVersion.Minor;
            if (62 <= logVer)
            {
                var ver = Environment.OSVersion.Version.Major * 10 + Environment.OSVersion.Version.Minor;
                if (ver < 62)       // We are decoding on less than windows 8
                {
                    if (!m_notifiedAboutWin8)
                    {
                        m_notifiedAboutWin8 = true;
                        var versionMismatchWarning = "This trace was captured on Window 8 and is being read\r\n" +
                                                     "on and earlier OS.  If you experience any problems please\r\n" +
                                                     "read the trace on an Windows 8 OS.";
                        worker.LogWriter.WriteLine(versionMismatchWarning);
                        parentWindow.Dispatcher.BeginInvoke((Action)delegate ()
                        {
                            MessageBox.Show(parentWindow, versionMismatchWarning, "Log File Version Mismatch", MessageBoxButton.OK);
                        });
                    }
                }
            }

            var advanced = new PerfViewTreeGroup("Advanced Group");
            var memory = new PerfViewTreeGroup("Memory Group");
            var obsolete = new PerfViewTreeGroup("Old Group");
            var experimental = new PerfViewTreeGroup("Experimental Group");
            m_Children = new List<PerfViewTreeItem>();

            bool hasCPUStacks = false;
            bool hasDllStacks = false;
            bool hasCSwitchStacks = false;
            bool hasReadyThreadStacks = false;
            bool hasHeapStacks = false;
            bool hasGCAllocationTicks = false;
            bool hasExceptions = false;
            bool hasManagedLoads = false;
            bool hasAspNet = false;
            bool hasIis = false;
            bool hasDiskStacks = false;
            bool hasAnyStacks = false;
            bool hasVirtAllocStacks = false;
            bool hasFileStacks = false;
            bool hasTpl = false;
            bool hasTplStacks = false;
            bool hasGCHandleStacks = false;
            bool hasMemAllocStacks = false;
            bool hasJSHeapDumps = false;
            bool hasDotNetHeapDumps = false;
            bool hasWCFRequests = false;
            bool hasCCWRefCountStacks = false;
            bool hasNetNativeCCWRefCountStacks = false;
            bool hasWindowsRefCountStacks = false;
            bool hasPinObjectAtGCTime = false;
            bool hasObjectUpdate = false;
            bool hasGCEvents = false;
            bool hasProjectNExecutionTracingEvents = false;
            bool hasDefenderEvents = false;
            bool hasTypeLoad = false;
            bool hasAssemblyLoad = false;
            bool hasJIT = false;

            var stackEvents = new List<TraceEventCounts>();
            foreach (var counts in tracelog.Stats)
            {
                var name = counts.EventName;
                if (!hasCPUStacks && name.StartsWith("PerfInfo"))
                {
                    hasCPUStacks = true;                // Even without true stacks we can display something in the stack viewer.  
                }

                if (!hasAspNet && name.StartsWith("AspNetReq"))
                {
                    hasAspNet = true;
                }

                if (!hasIis && name.StartsWith("IIS"))
                {
                    hasIis = true;
                }

                if (counts.ProviderGuid == ApplicationServerTraceEventParser.ProviderGuid)
                {
                    hasWCFRequests = true;
                }

                if (name.StartsWith("JSDumpHeapEnvelope"))
                {
                    hasJSHeapDumps = true;
                }

                if (name.StartsWith("GC/Start"))
                {
                    hasGCEvents = true;
                }

                if (name.StartsWith("GC/BulkNode"))
                {
                    hasDotNetHeapDumps = true;
                }

                if (name.StartsWith("GC/PinObjectAtGCTime"))
                {
                    hasPinObjectAtGCTime = true;
                }

                if (name.StartsWith("GC/BulkSurvivingObjectRanges") || name.StartsWith("GC/BulkMovedObjectRanges"))
                {
                    hasObjectUpdate = true;
                }

                if (counts.ProviderGuid == TplEtwProviderTraceEventParser.ProviderGuid)
                {
                    hasTpl = true;
                }

                if (counts.ProviderGuid == MicrosoftAntimalwareEngineTraceEventParser.ProviderGuid)
                {
                    hasDefenderEvents = true;
                }

                if (name.StartsWith("Method/JittingStarted"))
                {
                    hasJIT = true;
                }
                if (name.StartsWith("TypeLoad/Start"))
                {
                    hasTypeLoad = true;
                }
                if (name.StartsWith("Loader/AssemblyLoad"))
                {
                    hasAssemblyLoad = true;
                }

                if (counts.StackCount > 0)
                {
                    hasAnyStacks = true;
                    if (counts.ProviderGuid == ETWClrProfilerTraceEventParser.ProviderGuid && name.StartsWith("ObjectAllocated"))
                    {
                        hasMemAllocStacks = true;
                    }

                    if (name.StartsWith("GC/SampledObjectAllocation"))
                    {
                        hasMemAllocStacks = true;
                    }

                    if (name.StartsWith("GC/CCWRefCountChange"))
                    {
                        hasCCWRefCountStacks = true;
                    }

                    if (name.StartsWith("TaskCCWRef"))
                    {
                        hasNetNativeCCWRefCountStacks = true;
                    }

                    if (name.StartsWith("Object/CreateHandle"))
                    {
                        hasWindowsRefCountStacks = true;
                    }

                    if (name.StartsWith("Image"))
                    {
                        hasDllStacks = true;
                    }

                    if (name.StartsWith("HeapTrace"))
                    {
                        hasHeapStacks = true;
                    }

                    if (name.StartsWith("Thread/CSwitch"))
                    {
                        hasCSwitchStacks = true;
                    }

                    if (name.StartsWith("GC/AllocationTick"))
                    {
                        hasGCAllocationTicks = true;
                    }

                    if (name.StartsWith("Exception") || name.StartsWith("PageFault/AccessViolation"))
                    {
                        hasExceptions = true;
                    }

                    if (name.StartsWith("GC/SetGCHandle"))
                    {
                        hasGCHandleStacks = true;
                    }

                    if (name.StartsWith("Loader/ModuleLoad"))
                    {
                        hasManagedLoads = true;
                    }

                    if (name.StartsWith("VirtualMem"))
                    {
                        hasVirtAllocStacks = true;
                    }

                    if (name.StartsWith("Dispatcher/ReadyThread"))
                    {
                        hasReadyThreadStacks = true;
                    }

                    if (counts.ProviderGuid == TplEtwProviderTraceEventParser.ProviderGuid)
                    {
                        hasTplStacks = true;
                    }

                    if (name.StartsWith("DiskIO"))
                    {
                        hasDiskStacks = true;
                    }

                    if (name.StartsWith("FileIO"))
                    {
                        hasFileStacks = true;
                    }

                    if (name.StartsWith("MethodEntry"))
                    {
                        hasProjectNExecutionTracingEvents = true;
                    }
                }
            }

            m_Children.Add(new PerfViewTraceInfo(this));
            m_Children.Add(new PerfViewProcesses(this));

            m_Children.Add(new PerfViewStackSource(this, "Processes / Files / Registry") { SkipSelectProcess = true });

            if (hasCPUStacks)
            {
                m_Children.Add(new PerfViewStackSource(this, "CPU"));
                experimental.Children.Add(new AutomatedAnalysisReport(this));
                if (!App.CommandLineArgs.ShowOptimizationTiers &&
                    tracelog.Events.Any(
                        e => e is MethodLoadUnloadTraceDataBase td && td.OptimizationTier != OptimizationTier.Unknown))
                {
                    advanced.Children.Add(new PerfViewStackSource(this, "CPU (with Optimization Tiers)"));
                }
                advanced.Children.Add(new PerfViewStackSource(this, "Processor"));
            }

            if (hasCSwitchStacks)
            {
                if (hasTplStacks)
                {
                    m_Children.Add(new PerfViewStackSource(this, "Thread Time"));
                    advanced.Children.Add(new PerfViewStackSource(this, "Thread Time (with Tasks)"));
                    advanced.Children.Add(new PerfViewStackSource(this, "Thread Time (with StartStop Activities)"));
                }
                else
                {
                    m_Children.Add(new PerfViewStackSource(this, "Thread Time"));
                }

                if (hasReadyThreadStacks)
                {
                    advanced.Children.Add(new PerfViewStackSource(this, "Thread Time (with ReadyThread)"));
                }
            }
            else if (hasCPUStacks && hasTplStacks)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "Thread Time (with StartStop Activities) (CPU ONLY)"));
            }

            if (hasDiskStacks)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "Disk I/O"));
            }

            if (hasFileStacks)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "File I/O"));
                advanced.Children.Add(new PerfViewStackSource(this, "File Queries"));
                advanced.Children.Add(new PerfViewStackSource(this, "Directory Enumerations"));
            }

            if (hasHeapStacks)
            {
                memory.Children.Add(new PerfViewStackSource(this, "Net OS Heap Alloc"));
            }

            if (hasVirtAllocStacks)
            {
                memory.Children.Add(new PerfViewStackSource(this, "Net Virtual Alloc"));
                memory.Children.Add(new PerfViewStackSource(this, "Net Virtual Reserve"));
            }
            if (hasGCAllocationTicks)
            {
                if (hasObjectUpdate)
                {
                    memory.Children.Add(new PerfViewStackSource(this, "GC Heap Net Mem (Coarse Sampling)"));
                    memory.Children.Add(new PerfViewStackSource(this, "Gen 2 Object Deaths (Coarse Sampling)"));
                }
                memory.Children.Add(new PerfViewStackSource(this, "GC Heap Alloc Ignore Free (Coarse Sampling)"));
            }
            if (hasMemAllocStacks)
            {
                memory.Children.Add(new PerfViewStackSource(this, "GC Heap Net Mem"));
                memory.Children.Add(new PerfViewStackSource(this, "GC Heap Alloc Ignore Free"));
                memory.Children.Add(new PerfViewStackSource(this, "Gen 2 Object Deaths"));
            }

            if (hasDllStacks)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "Image Load"));
            }

            if (hasManagedLoads)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "Managed Load"));
            }

            if (hasExceptions)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "Exceptions"));
            }

            if (hasGCHandleStacks)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "Pinning"));
            }

            if (hasPinObjectAtGCTime)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "Pinning At GC Time"));
            }

            if (hasGCEvents && hasCPUStacks && AppLog.InternalUser)
            {
                memory.Children.Add(new PerfViewStackSource(this, "Server GC"));
            }

            if (hasCCWRefCountStacks)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "CCW Ref Count"));
            }

            if (hasNetNativeCCWRefCountStacks)
            {
                advanced.Children.Add(new PerfViewStackSource(this, ".NET Native CCW Ref Count"));
            }

            if (hasWindowsRefCountStacks)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "Windows Handle Ref Count"));
            }

            if (hasGCHandleStacks && hasMemAllocStacks)
            {
                bool matchingHeapSnapshotExists = GCPinnedObjectAnalyzer.ExistsMatchingHeapSnapshot(FilePath);
                if (matchingHeapSnapshotExists)
                {
                    advanced.Children.Add(new PerfViewStackSource(this, "Heap Snapshot Pinning"));
                    advanced.Children.Add(new PerfViewStackSource(this, "Heap Snapshot Pinned Object Allocation"));
                }
            }

            if ((hasAspNet) || (hasWCFRequests))
            {
                if (hasCPUStacks)
                {
                    obsolete.Children.Add(new PerfViewStackSource(this, "Server Request CPU"));
                }
                if (hasCSwitchStacks)
                {
                    obsolete.Children.Add(new PerfViewStackSource(this, "Server Request Thread Time"));
                }
                if (hasGCAllocationTicks)
                {
                    obsolete.Children.Add(new PerfViewStackSource(this, "Server Request Managed Allocation"));
                }
            }

            if (hasAnyStacks)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "Any"));
                if (hasTpl)
                {
                    if (hasCSwitchStacks)
                    {
                        advanced.Children.Add(new PerfViewStackSource(this, "Any Stacks (with Tasks)"));
                        advanced.Children.Add(new PerfViewStackSource(this, "Any Stacks (with StartStop Activities)"));
                        advanced.Children.Add(new PerfViewStackSource(this, "Any StartStopTree"));
                    }
                    advanced.Children.Add(new PerfViewStackSource(this, "Any TaskTree"));
                }
            }

            if (hasAspNet)
            {
                advanced.Children.Add(new PerfViewAspNetStats(this));
                if (hasCPUStacks)
                {
                    var name = "ASP.NET Thread Time";
                    if (hasCSwitchStacks && hasTplStacks)
                    {
                        obsolete.Children.Add(new PerfViewStackSource(this, "ASP.NET Thread Time (with Tasks)"));
                    }
                    else if (!hasCSwitchStacks)
                    {
                        name += " (CPU ONLY)";
                    }

                    obsolete.Children.Add(new PerfViewStackSource(this, name));
                }
            }

            if (hasIis)
            {
                advanced.Children.Add(new PerfViewIisStats(this));
            }

            if (hasProjectNExecutionTracingEvents && AppLog.InternalUser)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "Execution Tracing"));
            }

            if(hasDefenderEvents)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "Anti-Malware Real-Time Scan"));
            }

            memory.Children.Add(new PerfViewGCStats(this));

            // TODO currently this is experimental enough that we don't show it publicly.  
            if (AppLog.InternalUser)
            {
                memory.Children.Add(new MemoryAnalyzer(this));
            }

            if (hasJSHeapDumps || hasDotNetHeapDumps)
            {
                memory.Children.Add(new PerfViewHeapSnapshots(this));
            }

            advanced.Children.Add(new PerfViewJitStats(this));

            if (hasJIT || hasAssemblyLoad || hasTypeLoad)
            {
                advanced.Children.Add(new PerfViewRuntimeLoaderStats(this));
            }

            advanced.Children.Add(new PerfViewEventStats(this));

            m_Children.Add(new PerfViewEventSource(this));

            if (0 < memory.Children.Count)
            {
                m_Children.Add(memory);
            }

            if (0 < advanced.Children.Count)
            {
                m_Children.Add(advanced);
            }

            if (0 < obsolete.Children.Count)
            {
                m_Children.Add(obsolete);
            }

            if (AppLog.InternalUser && 0 < experimental.Children.Count)
            {
                m_Children.Add(experimental);
            }

            return null;
        }
        // public override string DefaultStackSourceName { get { return "CPU"; } }

        public TraceLog GetTraceLog(TextWriter log, Action<bool, int, int> onLostEvents = null)
        {
            if (m_traceLog != null)
            {
                if (IsUpToDate)
                {
                    return m_traceLog;
                }

                m_traceLog.Dispose();
                m_traceLog = null;
            }
            var dataFileName = FilePath;
            var options = new TraceLogOptions();
            options.ConversionLog = log;
            if (App.CommandLineArgs.KeepAllEvents)
            {
                options.KeepAllEvents = true;
            }

            var traceEventDispatcherOptions = new TraceEventDispatcherOptions();
            traceEventDispatcherOptions.StartTime = App.CommandLineArgs.StartTime;
            traceEventDispatcherOptions.EndTime = App.CommandLineArgs.EndTime;

            options.MaxEventCount = App.CommandLineArgs.MaxEventCount;
            options.ContinueOnError = App.CommandLineArgs.ContinueOnError;
            options.SkipMSec = App.CommandLineArgs.SkipMSec;
            options.OnLostEvents = onLostEvents;
            options.LocalSymbolsOnly = false;
            options.ShouldResolveSymbols = delegate (string moduleFilePath) { return false; };       // Don't resolve any symbols

            // But if there is a directory called EtwManifests exists, look in there instead. 
            var etwManifestDirPath = Path.Combine(Path.GetDirectoryName(dataFileName), "EtwManifests");
            if (Directory.Exists(etwManifestDirPath))
            {
                options.ExplicitManifestDir = etwManifestDirPath;
            }

            CommandProcessor.UnZipIfNecessary(ref dataFileName, log);

            var etlxFile = dataFileName;
            var cachedEtlxFile = false;
            if (dataFileName.EndsWith(".etl", StringComparison.OrdinalIgnoreCase) || dataFileName.EndsWith(".btl", StringComparison.OrdinalIgnoreCase))
            {
                etlxFile = CacheFiles.FindFile(dataFileName, ".etlx");
                if (!File.Exists(etlxFile))
                {
                    log.WriteLine("Creating ETLX file {0} from {1}", etlxFile, dataFileName);
                    TraceLog.CreateFromEventTraceLogFile(dataFileName, etlxFile, options, traceEventDispatcherOptions);

                    var dataFileSize = "Unknown";
                    if (File.Exists(dataFileName))
                    {
                        dataFileSize = ((new System.IO.FileInfo(dataFileName)).Length / 1000000.0).ToString("n3") + " MB";
                    }

                    log.WriteLine("ETL Size {0} ETLX Size {1:n3} MB", dataFileSize, (new System.IO.FileInfo(etlxFile)).Length / 1000000.0);
                }
                else
                {
                    cachedEtlxFile = true;
                    log.WriteLine("Found a corresponding ETLX file {0}", etlxFile);
                }
            }

            try
            {
                m_traceLog = new TraceLog(etlxFile);

                // Add some more parser that we would like.  
                new ETWClrProfilerTraceEventParser(m_traceLog);
                new MicrosoftWindowsNDISPacketCaptureTraceEventParser(m_traceLog);
            }
            catch (Exception)
            {
                if (cachedEtlxFile)
                {
                    // Delete the file and try again.  
                    FileUtilities.ForceDelete(etlxFile);
                    if (!File.Exists(etlxFile))
                    {
                        return GetTraceLog(log, onLostEvents);
                    }
                }
                throw;
            }

            m_utcLastWriteAtOpen = File.GetLastWriteTimeUtc(FilePath);
            if (App.CommandLineArgs.UnsafePDBMatch)
            {
                m_traceLog.CodeAddresses.UnsafePDBMatching = true;
            }

            if (m_traceLog.Truncated)   // Warn about truncation.  
            {
                GuiApp.MainWindow.Dispatcher.BeginInvoke((Action)delegate ()
                {
                    MessageBox.Show("The ETL file was too big to convert and was truncated.\r\nSee log for details", "Log File Truncated", MessageBoxButton.OK);
                });
            }
            return m_traceLog;
        }
        public TraceLog TryGetTraceLog() { return m_traceLog; }

        private void HandleLostEvents(Window parentWindow, bool truncated, int numberOfLostEvents, int eventCountAtTrucation, StatusBar worker)
        {
            string warning;
            if (!truncated)
            {
                // TODO see if we can get the buffer size out of the ETL file to give a good number in the message. 
                warning = "WARNING: There were " + numberOfLostEvents + " lost events in the trace.\r\n" +
                    "Some analysis might be invalid.\r\n" +
                    "Use /InMemoryCircularBuffer or /BufferSize:1024 to avoid this in future traces.";
            }
            else
            {
                warning = "WARNING: The ETLX file was truncated at " + eventCountAtTrucation + " events.\r\n" +
                    "This is to keep the ETLX file size under 4GB, however all rundown events are processed.\r\n" +
                    "Use /SkipMSec:XXX after clearing the cache (File->Clear Temp Files) to see the later parts of the file.\r\n" +
                    "See log for more details.";
            }

            MessageBoxResult result = MessageBoxResult.None;
            parentWindow.Dispatcher.BeginInvoke((Action)delegate ()
            {
                result = MessageBox.Show(parentWindow, warning, "Lost Events", MessageBoxButton.OKCancel);
                worker.LogWriter.WriteLine(warning);
                if (result != MessageBoxResult.OK)
                {
                    worker.AbortWork();
                }
            });
        }
        public override void Close()
        {
            if (m_traceLog != null)
            {
                m_traceLog.Dispose();
                m_traceLog = null;
            }
            base.Close();
        }
        public override ImageSource Icon { get { return GuiApp.MainWindow.Resources["FileBitmapImage"] as ImageSource; } }

        internal static List<TraceModuleFile> GetInterestingModuleFiles(ETLPerfViewData etlFile, double pdbThresholdPercent, TextWriter log, List<int> focusProcessIDs = null)
        {
            // If a DLL is loaded into multiple processes or at different locations we can get repeats, strip them.  
            var ret = new List<TraceModuleFile>();
            var traceLog = etlFile.GetTraceLog(log);

            // There can be several TraceModuleFile for a given path because the module is loaded more than once.
            // Thus we need to accumulate the counts.  This is what moduleCodeAddressCounts does 
            var moduleCodeAddressCounts = new Dictionary<string, int>();
            // Get symbols in cache, generate NGEN images if necessary.  

            IEnumerable<TraceModuleFile> moduleList = traceLog.ModuleFiles;
            int totalCpu = traceLog.CodeAddresses.TotalCodeAddresses;
            if (focusProcessIDs != null)
            {
                var processtotalCpu = 0;
                var processModuleList = new List<TraceModuleFile>();
                foreach (var process in traceLog.Processes)
                {
                    processtotalCpu += (int)process.CPUMSec;
                    if (!focusProcessIDs.Contains(process.ProcessID))
                    {
                        continue;
                    }

                    log.WriteLine("Restricting to process {0} ({1})", process.Name, process.ProcessID);
                    foreach (var mod in process.LoadedModules)
                    {
                        processModuleList.Add(mod.ModuleFile);
                    }
                }
                if (processtotalCpu != 0 && processModuleList.Count > 0)
                {
                    totalCpu = processtotalCpu;
                    moduleList = processModuleList;
                }
                else
                {
                    log.WriteLine("ERROR: could not find any CPU in focus processes, using machine wide total.");
                }
            }
            log.WriteLine("Total CPU = {0} samples", totalCpu);
            int pdbThreshold = (int)((pdbThresholdPercent * totalCpu) / 100.0);
            log.WriteLine("Pdb threshold = {0:f2}% = {1} code address instances", pdbThresholdPercent, pdbThreshold);

            foreach (var moduleFile in moduleList)
            {
                if (moduleFile.CodeAddressesInModule == 0)
                {
                    continue;
                }

                int count = 0;
                if (moduleCodeAddressCounts.TryGetValue(moduleFile.FilePath, out count))
                {
                    // We have already hit the threshold so we don't need to do anything. 
                    if (count >= pdbThreshold)
                    {
                        continue;
                    }
                }

                count += moduleFile.CodeAddressesInModule;
                moduleCodeAddressCounts[moduleFile.FilePath] = count;
                if (count < pdbThreshold)
                {
                    continue;                   // Have not reached threshold
                }

                log.WriteLine("Addr Count = {0} >= {1}, adding: {2}", count, pdbThreshold, moduleFile.FilePath);
                ret.Add(moduleFile);
            }
            return ret;
        }



        #region private
        /// <summary>
        /// See if the log has events from VS providers.  If so we should register the VS providers. 
        /// </summary>
        private bool HasVSEvents(TraceLog traceLog)
        {
            if (!m_checkedForVSEvents)
            {
                var codeMarkerGuid = new Guid(0x143A31DB, 0x0372, 0x40B6, 0xB8, 0xF1, 0xB4, 0xB1, 0x6A, 0xDB, 0x5F, 0x54);
                var measurementBlockGuid = new Guid(0x641D7F6C, 0x481C, 0x42E8, 0xAB, 0x7E, 0xD1, 0x8D, 0xC5, 0xE5, 0xCB, 0x9E);
                foreach (var stats in traceLog.Stats)
                {
                    if (stats.ProviderGuid == codeMarkerGuid || stats.ProviderGuid == measurementBlockGuid)
                    {
                        m_hasVSEvents = true;
                        break;
                    }
                }

                m_checkedForVSEvents = true;
            }
            return m_hasVSEvents;
        }

        private bool m_checkedForVSEvents;
        private bool m_hasVSEvents;
        private TraceLog m_traceLog;
        private bool m_notifiedAboutLostEvents;
        private bool m_notifiedAboutWin8;
        private string m_extraTopStats;
        #endregion
    }

    internal class WTPerfViewFile : PerfViewFile
    {
        public override string FormatName { get { return "CDB WT calls"; } }
        public override string[] FileExtensions { get { return new string[] { ".wt" }; } }

        protected internal override StackSource OpenStackSourceImpl(TextWriter log)
        {
            return new WTStackSource(FilePath);
        }
        protected internal override void ConfigureStackWindow(string stackSourceName, StackWindow stackWindow)
        {
            stackWindow.FoldPercentTextBox.Text = stackWindow.GetDefaultFoldPercentage();
            stackWindow.ScalingPolicy = ScalingPolicyKind.TimeMetric;
        }
    }

    internal class OffProfPerfViewFile : PerfViewFile
    {
        public override string FormatName { get { return "Office Profiler"; } }
        public override string[] FileExtensions { get { return new string[] { ".offtree" }; } }

        protected internal override StackSource OpenStackSourceImpl(TextWriter log)
        {
            return new OffProfStackSource(FilePath);
        }
        protected internal override void ConfigureStackWindow(string stackSourceName, StackWindow stackWindow)
        {
            stackWindow.FoldPercentTextBox.Text = stackWindow.GetDefaultFoldPercentage();
            stackWindow.RemoveColumn("WhenColumn");
            stackWindow.RemoveColumn("FirstColumn");
            stackWindow.RemoveColumn("LastColumn");
        }
    }

    internal class DebuggerStackPerfViewFile : PerfViewFile
    {
        public override string FormatName
        {
            get { return "Windbg kc Call stack"; }
        }

        public override string[] FileExtensions
        {
            get { return new string[] { ".cdbStack", ".windbgStack" }; }
        }

        protected internal override StackSource OpenStackSourceImpl(TextWriter log)
        {
            return new DebuggerStackSource(FilePath);
        }

        protected internal override void ConfigureStackWindow(string stackSourceName, StackWindow stackWindow)
        {
            stackWindow.RemoveColumn("IncCountColumn");
            stackWindow.RemoveColumn("ExcCountColumn");
            stackWindow.RemoveColumn("FoldCountColumn");
        }
    }

    internal class XmlTreeFile : PerfViewFile
    {
        public override string FormatName { get { return "Tree XML FILE"; } }
        public override string[] FileExtensions { get { return new string[] { ".tree.xml" }; } }

        protected internal override StackSource OpenStackSourceImpl(TextWriter log)
        {
            return new XmlTreeStackSource(FilePath);
        }
    }

    internal class XmlPerfViewFile : PerfViewFile
    {
        public override string FormatName { get { return "PerfView XML FILE"; } }
        public override string[] FileExtensions { get { return new string[] { ".perfView.xml", ".perfView.xml.zip", ".perfView.json", ".perfView.json.zip" }; } }

        protected internal override StackSource OpenStackSourceImpl(TextWriter log)
        {
            m_guiState = new StackWindowGuiState();

            return new XmlStackSource(FilePath, delegate (XmlReader reader)
            {
                if (reader.Name == "StackWindowGuiState")
                {
                    m_guiState = m_guiState.ReadFromXml(reader);
                }
                // These are only here for backward compatibility
                else if (reader.Name == "FilterXml")
                {
                    m_guiState.FilterGuiState.ReadFromXml(reader);
                }
                else if (reader.Name == "Log")
                {
                    m_guiState.Log = reader.ReadElementContentAsString().Trim();
                }
                else if (reader.Name == "Notes")
                {
                    m_guiState.Notes = reader.ReadElementContentAsString().Trim();
                }
                else
                {
                    reader.Read();
                }
            });
        }

        protected internal override void ConfigureStackWindow(string stackSourceName, StackWindow stackWindow)
        {
            stackWindow.RestoreWindow(m_guiState, FilePath);
        }
        protected internal override void FirstAction(StackWindow stackWindow)
        {
            if (m_guiState != null)
            {
                stackWindow.GuiState = m_guiState;
            }

            m_guiState = null;
        }

        public override void LookupSymbolsForModule(string simpleModuleName, TextWriter log, int processId = 0)
        {
            throw new ApplicationException("Symbols can not be looked up after a stack view has been saved.\r\n" +
                "You must resolve all symbols you need before saving.\r\n" +
                "Consider the right click -> Lookup Warm Symbols command");
        }

        private StackWindowGuiState m_guiState;
    }

    internal class VmmapPerfViewFile : PerfViewFile
    {
        public override string FormatName { get { return "Vmmap data file"; } }
        public override string[] FileExtensions { get { return new string[] { ".mmp" }; } }


        protected internal override StackSource OpenStackSourceImpl(TextWriter log)
        {
            using (Stream dataStream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                var xmlStream = dataStream;
                XmlReaderSettings settings = new XmlReaderSettings() { IgnoreWhitespace = true, IgnoreComments = true };
                using (XmlReader reader = XmlTextReader.Create(xmlStream, settings))
                {
                    return new VMMapStackSource(reader);
                }
            }
        }

        protected internal override void ConfigureStackWindow(string stackSourceName, StackWindow stackWindow)
        {
            var defaultFold = "^Block;^Shareable;^Image Section;^Heap;^Private data;^Thread Stack";
            stackWindow.FoldRegExTextBox.Items.Insert(0, "^Images in");
            stackWindow.FoldRegExTextBox.Items.Insert(0, defaultFold);

            stackWindow.IncludeRegExTextBox.Items.Insert(0, "^Images in;^MappedFiles in");
            stackWindow.IncludeRegExTextBox.Items.Insert(0, "^Block Private");

            stackWindow.GroupRegExTextBox.Items.Insert(0, "[group files] ^MappedFile{*}->Image$1;^Image{*}->Image$1;Group MappedFile->Group Image;Group Image->Group Image");
        }
        protected internal override void FirstAction(StackWindow stackWindow)
        {
            stackWindow.CallTreeTab.IsSelected = true;
        }

        #region private
        [Flags]
        private enum PageProtection
        {
            PAGE_EXECUTE = 0x10,
            PAGE_EXECUTE_READ = 0x20,
            PAGE_EXECUTE_READWRITE = 0x40,
            PAGE_EXECUTE_WRITECOPY = 0x80,
            PAGE_NOACCESS = 0x01,
            PAGE_READONLY = 0x02,
            PAGE_READWRITE = 0x04,
            PAGE_WRITECOPY = 0x08,
        }

        private enum UseType
        {
            Heap = 0,
            Stack = 1,
            Image = 2,
            MappedFile = 3,
            PrivateData = 4,
            Shareable = 5,
            Free = 6,
            // Unknown1 = 7,
            ManagedHeaps = 8,
            // Unknown2 = 9,
            Unusable = 10,
        }

        private class MemoryNode
        {
            public static MemoryNode Root()
            {
                var ret = new MemoryNode();
                ret.Size = ulong.MaxValue;
                ret.Details = "[ROOT]";
                return ret;
            }
            public MemoryNode Add(XmlReader reader)
            {
                var newNode = new MemoryNode(reader);
                Insert(newNode);
                return newNode;
            }

            public ulong End { get { return Address + Size; } }
            public ulong Address;
            public ulong Blocks;
            public ulong ShareableWS;
            public ulong SharedWS;
            public ulong Size;
            public ulong Commit;
            public ulong PrivateBytes;
            public ulong PrivateWS;
            public ulong Id;
            public PageProtection Protection;
            public ulong Storage;
            public UseType UseType;
            public string Type;
            public string Details;
            public List<MemoryNode> Children;
            public MemoryNode Parent;

            public override string ToString()
            {
                return string.Format("<MemoryNode Name=\"{0}\" Start=\"0x{1:x}\" Length=\"0x{2:x}\"/>", Details, Address, Size);
            }

            #region private

            private void Insert(MemoryNode newNode)
            {
                Debug.Assert(Address <= newNode.Address && newNode.End <= End);
                if (Children == null)
                {
                    Children = new List<MemoryNode>();
                }

                // Search backwards for efficiency.  
                for (int i = Children.Count; 0 < i;)
                {
                    var child = Children[--i];
                    if (child.Address <= newNode.Address && newNode.End <= child.End)
                    {
                        child.Insert(newNode);
                        return;
                    }
                }
                Children.Add(newNode);
                newNode.Parent = this;
            }
            private MemoryNode() { }
            private MemoryNode(XmlReader reader)
            {
                Address = FetchLong(reader, "Address");
                Blocks = FetchLong(reader, "Blocks");
                ShareableWS = FetchLong(reader, "ShareableWS");
                SharedWS = FetchLong(reader, "SharedWS");
                Size = FetchLong(reader, "Size");
                Commit = FetchLong(reader, "Commit");
                PrivateBytes = FetchLong(reader, "PrivateBytes");
                PrivateWS = FetchLong(reader, "PrivateWS");
                Id = FetchLong(reader, "Id");     // This identifies the heap (for Heap type data)
                Protection = (PageProtection)int.Parse(reader.GetAttribute("Protection") ?? "0");
                Storage = FetchLong(reader, "Storage");
                UseType = (UseType)int.Parse(reader.GetAttribute("UseType") ?? "0");
                Type = reader.GetAttribute("Type") ?? "";
                Details = reader.GetAttribute("Details") ?? "";
            }

            private static ulong FetchLong(XmlReader reader, string attributeName)
            {
                ulong ret = 0L;
                var attrValue = reader.GetAttribute(attributeName);
                if (attrValue != null)
                {
                    ulong.TryParse(attrValue, out ret);
                }

                return ret;
            }
            #endregion
        }

        private class VMMapStackSource : InternStackSource
        {
            public VMMapStackSource(XmlReader reader)
            {
                m_sample = new StackSourceSample(this);
                MemoryNode top = MemoryNode.Root();
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        // Start over if we see another snapshot.  THus we read the last one.   
                        // THis is present VMMAP behavior.  TODO We should think about doing better.   
                        if (reader.Name == "Snapshot")
                        {
                            top = MemoryNode.Root();
                        }
                        else if (reader.Name == "Region")
                        {
                            top.Add(reader);
                        }
                    }
                }

                foreach (var child in top.Children)
                {
                    AddToSource(child, StackSourceCallStackIndex.Invalid);
                }

                Interner.DoneInterning();
            }

            /// <summary>
            /// Add all the nodes represented by 'node' to the source.  'parentStack' is the
            /// stack that represents the parent of 'node' (thus the top node is Invalid, 
            /// which represents the empty stack)
            /// </summary>
            private void AddToSource(MemoryNode node, StackSourceCallStackIndex parentStack)
            {
                if (node.Children != null)
                {
                    // At the topmost level we have group (UseType), for the node
                    if (parentStack == StackSourceCallStackIndex.Invalid)
                    {
                        parentStack = AddFrame("Group " + node.UseType.ToString(), parentStack);
                    }

                    if (node.Details.Length != 0)
                    {
                        // Group directories together.  
                        if (node.UseType == UseType.Image || node.UseType == UseType.MappedFile)
                        {
                            parentStack = AddDirPathNodes(node.Details, parentStack, false, node.UseType);
                        }
                        else
                        {
                            parentStack = AddFrame(node.Details, parentStack);
                        }
                    }

                    foreach (var child in node.Children)
                    {
                        AddToSource(child, parentStack);
                    }

                    return;
                }

                var details = node.Details;
                if (node.UseType == UseType.Image && details.Length != 0)
                {
                    details = "Image Section " + details;
                }

                if (details.Length == 0)
                {
                    details = node.Type;
                }

                var frameName = string.Format("{0,-20} address 0x{1:x} size 0x{2:x}", details, node.Address, node.Size);
                StackSourceCallStackIndex nodeStack = AddFrame(frameName, parentStack);

                if (node.PrivateWS != 0)
                {
                    AddSample("Block Private", node.PrivateWS, nodeStack);
                }

                if (node.ShareableWS != 0)
                {
                    AddSample("Block Sharable", node.ShareableWS, nodeStack);
                }
            }
            /// <summary>
            /// Adds nodes for each parent directory that has more than one 'child' (its count is different than it child) 
            /// </summary>
            private StackSourceCallStackIndex AddDirPathNodes(string path, StackSourceCallStackIndex parentStack, bool isDir, UseType useType)
            {
                var lastBackslashIdx = path.LastIndexOf('\\');
                if (lastBackslashIdx >= 0)
                {
                    var dir = path.Substring(0, lastBackslashIdx);
                    parentStack = AddDirPathNodes(dir, parentStack, true, useType);
                }

                var kindName = (useType == UseType.MappedFile) ? "MappedFile" : "Image";
                var prefix = isDir ? kindName + "s in" : kindName;
                return AddFrame(prefix + " " + path, parentStack);
            }
            private void AddSample(string memoryKind, ulong metric, StackSourceCallStackIndex parentStack)
            {
                m_sample.Metric = metric / 1024F;
                var frameName = string.Format("{0,-15} {1}K WS", memoryKind, metric / 1024);
                m_sample.StackIndex = AddFrame(frameName, parentStack);
                AddSample(m_sample);
            }
            private StackSourceCallStackIndex AddFrame(string frameName, StackSourceCallStackIndex parentStack)
            {
                var moduleIdx = Interner.ModuleIntern("");
                var frameIdx = Interner.FrameIntern(frameName, moduleIdx);
                return Interner.CallStackIntern(frameIdx, parentStack);
            }

            private StackSourceSample m_sample;

        }
        #endregion
    }

    internal class PdbScopePerfViewFile : PerfViewFile
    {
        public override string FormatName { get { return ".NET Native Size Graph"; } }
        public override string[] FileExtensions { get { return new string[] { ".imageSize.xml", ".pdb.xml" }; } }   // TODO remove pdb.xml after 1/2015

        protected internal override StackSource OpenStackSourceImpl(TextWriter log)
        {
            Graph graph = new PdbScopeMemoryGraph(FilePath);

            log.WriteLine(
                   "Opened Graph {0} Bytes: {1:f3}M NumObjects: {2:f3}K  NumRefs: {3:f3}K Types: {4:f3}K RepresentationSize: {5:f1}M",
                   FilePath, graph.TotalSize / 1000000.0, (int)graph.NodeIndexLimit / 1000.0,
                   graph.TotalNumberOfReferences / 1000.0, (int)graph.NodeTypeIndexLimit / 1000.0,
                   graph.SizeOfGraphDescription() / 1000000.0);

            log.WriteLine("Type Histogram > 1% of heap size");
            log.Write(graph.HistogramByTypeXml(graph.TotalSize / 100));

#if false // TODO FIX NOW remove
            using (StreamWriter writer = File.CreateText(Path.ChangeExtension(this.FilePath, ".Clrprof.xml")))
            {
                ((MemoryGraph)graph).DumpNormalized(writer);
            }
#endif
            var ret = new MemoryGraphStackSource(graph, log);
            return ret;
        }
        protected internal override void ConfigureStackWindow(string stackSourceName, StackWindow stackWindow)
        {
            stackWindow.ScalingPolicy = ScalingPolicyKind.TimeMetric;
            stackWindow.IsMemoryWindow = true;
            stackWindow.RemoveColumn("WhichColumn");
            stackWindow.GroupRegExTextBox.Items.Add("[group PDBKinds]          #{%}#->type $1");
            stackWindow.GroupRegExTextBox.Items.Add("[raw group modules]       {%}!->module $1");

            stackWindow.FoldRegExTextBox.Items.Add("^RUNTIME_DATA");
            stackWindow.FoldRegExTextBox.Items.Add("^RUNTIME_DATA;^Section;^Image Header");
        }
    }

    internal class ClrProfilerHeapPerfViewFile : PerfViewFile
    {
        public override string FormatName { get { return "CLR Profiler Heap"; } }
        public override string[] FileExtensions { get { return new string[] { ".gcheap", ".clrprofiler" }; } }

        protected internal override StackSource OpenStackSourceImpl(TextWriter log)
        {
            Graph graph = new ClrProfilerMemoryGraph(FilePath);

            // TODO FIX NOW var refGraph = new Experimental.RefGraph(graph);

            log.WriteLine(
                   "Opened Graph {0} Bytes: {1:f3}M NumObjects: {2:f3}K  NumRefs: {3:f3}K Types: {4:f3}K RepresentationSize: {5:f1}M",
                   FilePath, graph.TotalSize / 1000000.0, (int)graph.NodeIndexLimit / 1000.0,
                   graph.TotalNumberOfReferences / 1000.0, (int)graph.NodeTypeIndexLimit / 1000.0,
                   graph.SizeOfGraphDescription() / 1000000.0);

            log.WriteLine("Type Histogram > 1% of heap size");
            log.Write(graph.HistogramByTypeXml(graph.TotalSize / 100));

#if false // TODO FIX NOW remove
            using (StreamWriter writer = File.CreateText(Path.ChangeExtension(this.FilePath, ".Clrprof.xml")))
            {
                ((MemoryGraph)graph).DumpNormalized(writer);
            }
#endif
            var ret = new MemoryGraphStackSource(graph, log);
            return ret;
        }
        protected internal override void ConfigureStackWindow(string stackSourceName, StackWindow stackWindow)
        {
            ConfigureAsMemoryWindow(stackSourceName, stackWindow);
        }
    }

    internal class HeapDumpPerfViewFile : PerfViewFile
    {
        internal const string Gen0WalkableObjectsViewName = "Gen 0 Walkable Objects";
        internal const string Gen1WalkableObjectsViewName = "Gen 1 Walkable Objects";

        public override string FormatName { get { return "CLR Heap Dump"; } }
        public override string[] FileExtensions { get { return new string[] { ".gcDump", ".gcDump.xml" }; } }

        public const string DiagSessionIdentity = "Microsoft.Diagnostics.GcDump";

        public override string DefaultStackSourceName { get { return "Heap"; } }

        public GCHeapDump GCDump { get { return m_gcDump; } }

        protected internal override StackSource OpenStackSourceImpl(string streamName, TextWriter log, double startRelativeMSec, double endRelativeMSec, Predicate<TraceEvent> predicate)
        {
            OpenDump(log);

            Graph graph = m_gcDump.MemoryGraph;
            GCHeapDump gcDump = m_gcDump;

#if false  // TODO FIX NOW remove
            using (StreamWriter writer = File.CreateText(Path.ChangeExtension(this.FilePath, ".heapDump.xml")))
            {
                ((MemoryGraph)graph).DumpNormalized(writer);
            }
#endif
            int gen = -1;
            if (streamName == Gen0WalkableObjectsViewName)
            {
                Debug.Assert(m_gcDump.DotNetHeapInfo != null);
                gen = 0;
            }
            else if (streamName == Gen1WalkableObjectsViewName)
            {
                Debug.Assert(m_gcDump.DotNetHeapInfo != null);
                gen = 1;
            }

            var ret = GenerationAwareMemoryGraphBuilder.CreateStackSource(m_gcDump, log, gen);

#if false // TODO FIX NOW: support post collection filtering?   
            // Set the sampling ratio so that the number of objects does not get too far out of control.  
            if (2000000 <= (int)graph.NodeIndexLimit)
            {
                ret.SamplingRate = ((int)graph.NodeIndexLimit / 1000000);
                log.WriteLine("Setting the sampling rate to {0}.", ret.SamplingRate);
                MessageBox.Show("The graph has more than 2M Objects.  " +
                    "The sampling rate has been set " + ret.SamplingRate.ToString() + " to keep the GUI responsive.");
            }
#endif
            m_extraTopStats = "";

            double unreachableMemory;
            double totalMemory;
            ComputeUnreachableMemory(ret, out unreachableMemory, out totalMemory);

            if (unreachableMemory != 0)
            {
                m_extraTopStats += string.Format(" Unreachable Memory: {0:n3}MB ({1:f1}%)",
                    unreachableMemory / 1000000.0, unreachableMemory * 100.0 / totalMemory);
            }

            if (gcDump.CountMultipliersByType != null)
            {
                m_extraTopStats += string.Format(" Heap Sampled: Mean Count Multiplier {0:f2} Mean Size Multiplier {1:f2}",
                        gcDump.AverageCountMultiplier, gcDump.AverageSizeMultiplier);
            }

            log.WriteLine("Type Histogram > 1% of heap size");
            log.Write(graph.HistogramByTypeXml(graph.TotalSize / 100));
            return ret;
        }

        protected override Action<Action> OpenImpl(Window parentWindow, StatusBar worker)
        {
            OpenDump(worker.LogWriter);

            var advanced = new PerfViewTreeGroup("Advanced Group");

            m_Children = new List<PerfViewTreeItem>(2);

            var defaultSource = new PerfViewStackSource(this, DefaultStackSourceName);
            defaultSource.IsSelected = true;
            m_Children.Add(defaultSource);

            if (m_gcDump.InteropInfo != null)
            {
                // TODO FIX NOW.   This seems to be broken right now  hiding it for now.  
                // advanced.Children.Add(new HeapDumpInteropObjects(this));
            }

            if (m_gcDump.DotNetHeapInfo != null)
            {
                advanced.Children.Add(new PerfViewStackSource(this, Gen0WalkableObjectsViewName));
                advanced.Children.Add(new PerfViewStackSource(this, Gen1WalkableObjectsViewName));
            }

            if (advanced.Children.Count > 0)
            {
                m_Children.Add(advanced);
            }

            return null;
        }

        protected internal override void ConfigureStackWindow(string stackSourceName, StackWindow stackWindow)
        {
            ConfigureAsMemoryWindow(stackSourceName, stackWindow);
            stackWindow.ExtraTopStats = m_extraTopStats;

            if (stackSourceName.Equals(Gen0WalkableObjectsViewName) || stackSourceName.Equals(Gen1WalkableObjectsViewName))
            {
                stackWindow.CallTreeTab.IsSelected = true;      // start with the call tree view
            }
        }

        #region private

        protected internal void OpenDump(TextWriter log)
        {
            if (m_gcDump == null)
            {
                // TODO this is kind of backwards.   The super class should not know about the subclasses.  
                var asSnapshot = this as PerfViewHeapSnapshot;
                if (asSnapshot != null)
                {
                    DotNetHeapInfo dotNetHeapInfo = null;
                    var etlFile = FilePath;
                    CommandProcessor.UnZipIfNecessary(ref etlFile, log);

                    MemoryGraph memoryGraph = null;
                    if (asSnapshot.Kind == "JS")
                    {
                        var dumper = new JavaScriptDumpGraphReader(log);
                        memoryGraph = dumper.Read(etlFile, asSnapshot.m_processId, asSnapshot.m_timeRelativeMSec);
                    }
                    else if (asSnapshot.Kind == ".NET")
                    {
                        var dumper = new DotNetHeapDumpGraphReader(log);
                        dumper.DotNetHeapInfo = dotNetHeapInfo = new DotNetHeapInfo();
                        memoryGraph = dumper.Read(etlFile, asSnapshot.m_processId.ToString(), asSnapshot.m_timeRelativeMSec);
                        var resolver = new TypeNameSymbolResolver(FilePath, log);
                        memoryGraph.ResolveTypeName = resolver.ResolveTypeName;
                    }

                    if (memoryGraph == null)
                    {
                        log.WriteLine("Error Unknown dump kind {0} found, ", asSnapshot.Kind);
                        return;
                    }
                    m_gcDump = new GCHeapDump(memoryGraph);
                    m_gcDump.DotNetHeapInfo = dotNetHeapInfo;
                }
                else
                {

                    if (FilePath.EndsWith(".gcDump.xml", StringComparison.OrdinalIgnoreCase))
                    {
                        m_gcDump = XmlGcHeapDump.ReadGCHeapDumpFromXml(FilePath);
                    }
                    else
                    {
                        m_gcDump = new GCHeapDump(FilePath);

                        // set it up so we resolve any types 
                        var resolver = new TypeNameSymbolResolver(FilePath, log);
                        m_gcDump.MemoryGraph.ResolveTypeName = resolver.ResolveTypeName;
                    }
                }


                if (m_gcDump.TimeCollected.Ticks != 0)
                {
                    log.WriteLine("GCDump collected on {0}", m_gcDump.TimeCollected);
                }
                else
                {
                    log.WriteLine("GCDump collected from a DMP file no time/machine/process info");
                }

                if (m_gcDump.MachineName != null)
                {
                    log.WriteLine("GCDump collected on Machine {0}", m_gcDump.MachineName);
                }

                if (m_gcDump.ProcessName != null)
                {
                    log.WriteLine("GCDump collected on Process {0} ({1})", m_gcDump.MachineName, m_gcDump.ProcessName, m_gcDump.ProcessID);
                }

                if (m_gcDump.TotalProcessCommit != 0)
                {
                    log.WriteLine("Total Process CommitSize {0:n1} MB Working Set {1:n1} MB", m_gcDump.TotalProcessCommit / 1000000.0, m_gcDump.TotalProcessWorkingSet / 1000000.0);
                }

                if (m_gcDump.CollectionLog != null)
                {
                    log.WriteLine("******************** START OF LOG FILE FROM TIME OF COLLECTION **********************");
                    log.Write(m_gcDump.CollectionLog);
                    log.WriteLine("********************  END OF LOG FILE FROM TIME OF COLLECTION  **********************");
                }

#if false // TODO FIX NOW REMOVE
                using (StreamWriter writer = File.CreateText(Path.ChangeExtension(FilePath, ".rawGraph.xml")))
                {
                    m_gcDump.MemoryGraph.WriteXml(writer);
                }
#endif
            }

            MemoryGraph graph = m_gcDump.MemoryGraph;
            log.WriteLine(
                   "Opened Graph {0} Bytes: {1:f3}M NumObjects: {2:f3}K  NumRefs: {3:f3}K Types: {4:f3}K RepresentationSize: {5:f1}M",
                   FilePath, graph.TotalSize / 1000000.0, (int)graph.NodeIndexLimit / 1000.0,
                   graph.TotalNumberOfReferences / 1000.0, (int)graph.NodeTypeIndexLimit / 1000.0,
                   graph.SizeOfGraphDescription() / 1000000.0);
        }

        private static void ComputeUnreachableMemory(MemoryGraphStackSource memoryStackSource, out double unreachableMemoryRet, out double totalMemoryRet)
        {
            double unreachableMemory = 0;
            double totalMemory = 0;

            memoryStackSource.ForEachUnordered(delegate (StackSourceSample sample, bool reachable)
            {
                totalMemory += sample.Metric;
                if (!reachable)
                {
                    unreachableMemory += sample.Metric;
                }
            });

            unreachableMemoryRet = unreachableMemory;
            totalMemoryRet = totalMemory;
        }

        protected internal GCHeapDump m_gcDump;
        private string m_extraTopStats;
        #endregion
    }

    public partial class LinuxPerfViewData : PerfViewFile
    {
        private string[] PerfScriptStreams = new string[]
        {
            "CPU",
            "CPU (with Optimization Tiers)",
            "Thread Time"
        };

        public override string FormatName { get { return "Perf"; } }

        public override string[] FileExtensions { get { return new string[] { ".trace.zip", "perf.data.txt" }; } }

        public override bool SupportsProcesses => true;

        protected internal override EventSource OpenEventSourceImpl(TextWriter log)
        {
            var traceLog = GetTraceLog(log);
            return new ETWEventSource(traceLog);
        }
        protected internal override StackSource OpenStackSourceImpl(string streamName, TextWriter log, double startRelativeMSec = 0, double endRelativeMSec = double.PositiveInfinity, Predicate<TraceEvent> predicate = null)
        {
            if (PerfScriptStreams.Contains(streamName))
            {
                string xmlPath;
                bool doThreadTime = false;

                if (streamName == "Thread Time")
                {
                    xmlPath = CacheFiles.FindFile(FilePath, ".perfscript.threadtime.xml.zip");
                    doThreadTime = true;
                }
                else
                {
                    xmlPath = CacheFiles.FindFile(FilePath, ".perfscript.cpu.xml.zip");
                }

#if !DEBUG
                if (!CacheFiles.UpToDate(xmlPath, FilePath))
#endif
                {
                    XmlStackSourceWriter.WriteStackViewAsZippedXml(
                        new LinuxPerfScriptStackSource(FilePath, doThreadTime), xmlPath);
                }

                bool showOptimizationTiers =
                    App.CommandLineArgs.ShowOptimizationTiers || streamName.Contains("(with Optimization Tiers)");
                return new XmlStackSource(xmlPath, null, showOptimizationTiers);
            }

            return null;
        }

        protected override Action<Action> OpenImpl(Window parentWindow, StatusBar worker)
        {
            // Open the file.
            m_traceLog = GetTraceLog(worker.LogWriter);

            bool hasGC = false;
            bool hasJIT = false;
            bool hasTypeLoad = false;
            bool hasAssemblyLoad = false;
            if (m_traceLog != null)
            {
                foreach (TraceEventCounts eventStats in m_traceLog.Stats)
                {
                    if (eventStats.EventName.StartsWith("GC/Start"))
                    {
                        hasGC = true;
                    }
                    else if (eventStats.EventName.StartsWith("Method/JittingStarted"))
                    {
                        hasJIT = true;
                    }
                    else if (eventStats.EventName.StartsWith("TypeLoad/Start"))
                    {
                        hasTypeLoad = true;
                    }
                    else if (eventStats.EventName.StartsWith("Loader/AssemblyLoad"))
                    {
                        hasAssemblyLoad = true;
                    }
                }
            }

            m_Children = new List<PerfViewTreeItem>();
            var advanced = new PerfViewTreeGroup("Advanced Group");
            var memory = new PerfViewTreeGroup("Memory Group");
            var experimental = new PerfViewTreeGroup("Experimental Group");

            m_Children.Add(new PerfViewStackSource(this, "CPU"));

            if (!App.CommandLineArgs.ShowOptimizationTiers &&
                m_traceLog != null &&
                m_traceLog.Events.Any(
                    e => e is MethodLoadUnloadTraceDataBase td && td.OptimizationTier != OptimizationTier.Unknown))
            {
                advanced.AddChild(new PerfViewStackSource(this, "CPU (with Optimization Tiers)"));
            }

            experimental.AddChild(new PerfViewStackSource(this, "Thread Time"));

            if (m_traceLog != null)
            {
                m_Children.Add(new PerfViewEventSource(this));
                m_Children.Add(new PerfViewEventStats(this));

                if (hasGC)
                {
                    memory.AddChild(new PerfViewGCStats(this));
                }

                if (hasJIT)
                {
                    advanced.AddChild(new PerfViewJitStats(this));
                }

                if (hasJIT || hasTypeLoad || hasAssemblyLoad)
                {
                    advanced.AddChild(new PerfViewRuntimeLoaderStats(this));
                }
            }

            if (memory.Children.Count > 0)
            {
                m_Children.Add(memory);
            }

            if (advanced.Children.Count > 0)
            {
                m_Children.Add(advanced);
            }

            if(AppLog.InternalUser && experimental.Children.Count > 0)
            {
                m_Children.Add(experimental);
            }

            return null;
        }

        public override void Close()
        {
            if (m_traceLog != null)
            {
                m_traceLog.Dispose();
                m_traceLog = null;
            }
            base.Close();
        }

        public override ImageSource Icon { get { return GuiApp.MainWindow.Resources["FileBitmapImage"] as ImageSource; } }

        protected internal override void ConfigureStackWindow(string stackSourceName, StackWindow stackWindow)
        {
            stackWindow.ScalingPolicy = ScalingPolicyKind.TimeMetric;
            stackWindow.GroupRegExTextBox.Text = stackWindow.GetDefaultGroupPat();

            ConfigureGroupRegExTextBox(stackWindow, windows: false);
        }

        public TraceLog GetTraceLog(TextWriter log)
        {
            if (m_traceLog != null)
            {
                if (IsUpToDate)
                {
                    return m_traceLog;
                }

                m_traceLog.Dispose();
                m_traceLog = null;
            }
            else if (m_noTraceLogInfo)
            {
                return null;
            }

            var dataFileName = FilePath;
            var options = new TraceLogOptions();
            options.ConversionLog = log;
            if (App.CommandLineArgs.KeepAllEvents)
            {
                options.KeepAllEvents = true;
            }

            options.MaxEventCount = App.CommandLineArgs.MaxEventCount;
            options.ContinueOnError = App.CommandLineArgs.ContinueOnError;
            options.SkipMSec = App.CommandLineArgs.SkipMSec;
            //options.OnLostEvents = onLostEvents;
            options.LocalSymbolsOnly = false;
            options.ShouldResolveSymbols = delegate (string moduleFilePath) { return false; };       // Don't resolve any symbols

            // Generate the etlx file path / name.
            string etlxFile = CacheFiles.FindFile(dataFileName, ".etlx");
            if (!File.Exists(etlxFile) || File.GetLastWriteTimeUtc(etlxFile) < File.GetLastWriteTimeUtc(dataFileName))
            {
                FileUtilities.ForceDelete(etlxFile);
                log.WriteLine("Creating ETLX file {0} from {1}", etlxFile, dataFileName);
                try
                {
                    TraceLog.CreateFromLttngTextDataFile(dataFileName, etlxFile, options);
                }
                catch (Exception e)        // Throws this if there is no CTF Information
                {
                    if (e is EndOfStreamException)
                    {
                        log.WriteLine("Warning: Trying to open CTF stream failed, no CTF (lttng) information");
                    }
                    else
                    {
                        log.WriteLine("Error: Exception CTF conversion: {0}", e.ToString());
                        log.WriteLine("[Error: exception while opening CTF (lttng) data.]");
                    }

                    Debug.Assert(m_traceLog == null);
                    m_noTraceLogInfo = true;
                    return m_traceLog;
                }
            }

            var dataFileSize = "Unknown";
            if (File.Exists(dataFileName))
            {
                dataFileSize = ((new System.IO.FileInfo(dataFileName)).Length / 1000000.0).ToString("n3") + " MB";
            }

            log.WriteLine("ETL Size {0} ETLX Size {1:n3} MB", dataFileSize, (new System.IO.FileInfo(etlxFile)).Length / 1000000.0);

            // Open the ETLX file.  
            m_traceLog = new TraceLog(etlxFile);
            m_utcLastWriteAtOpen = File.GetLastWriteTimeUtc(FilePath);
            if (App.CommandLineArgs.UnsafePDBMatch)
            {
                m_traceLog.CodeAddresses.UnsafePDBMatching = true;
            }

            if (m_traceLog.Truncated)   // Warn about truncation.  
            {
                GuiApp.MainWindow.Dispatcher.BeginInvoke((Action)delegate ()
                {
                    MessageBox.Show("The ETL file was too big to convert and was truncated.\r\nSee log for details", "Log File Truncated", MessageBoxButton.OK);
                });
            }
            return m_traceLog;
        }

        public TraceLog TryGetTraceLog() { return m_traceLog; }

        #region Private
        private TraceLog m_traceLog;
        private bool m_noTraceLogInfo;
        #endregion
    }

    public partial class EventPipePerfViewData : PerfViewFile
    {
        public override string FormatName => "EventPipe";

        public override string[] FileExtensions => new string[] { ".netperf", ".netperf.zip", ".nettrace" };

        private string m_extraTopStats;

        protected internal override EventSource OpenEventSourceImpl(TextWriter log)
        {
            var traceLog = GetTraceLog(log);
            return new ETWEventSource(traceLog);
        }

        protected override Action<Action> OpenImpl(Window parentWindow, StatusBar worker)
        {
            // Open the file.
            m_traceLog = GetTraceLog(worker.LogWriter, delegate (bool truncated, int numberOfLostEvents, int eventCountAtTrucation)
            {
                if (!m_notifiedAboutLostEvents)
                {
                    HandleLostEvents(parentWindow, truncated, numberOfLostEvents, eventCountAtTrucation, worker);
                    m_notifiedAboutLostEvents = true;
                }
            });

            bool hasGC = false;
            bool hasJIT = false;
            bool hasAnyStacks = false;
            bool hasDotNetHeapDumps = false;
            bool hasGCAllocationTicks = false;
            bool hasObjectUpdate = false;
            bool hasMemAllocStacks = false;
            bool hasTypeLoad = false;
            bool hasAssemblyLoad = false;
            if (m_traceLog != null)
            {
                foreach (TraceEventCounts eventStats in m_traceLog.Stats)
                {
                    if (eventStats.StackCount > 0)
                    {
                        hasAnyStacks = true;
                    }

                    if (eventStats.EventName.StartsWith("GC/Start"))
                    {
                        hasGC = true;
                    }
                    else if (eventStats.EventName.StartsWith("Method/JittingStarted"))
                    {
                        hasJIT = true;
                    }
                    else if (eventStats.EventName.StartsWith("GC/BulkNode"))
                    {
                        hasDotNetHeapDumps = true;
                    }
                    else if (eventStats.EventName.StartsWith("GC/AllocationTick"))
                    {
                        hasGCAllocationTicks = true;
                    }
                    if (eventStats.EventName.StartsWith("GC/BulkSurvivingObjectRanges") || eventStats.EventName.StartsWith("GC/BulkMovedObjectRanges"))
                    {
                        hasObjectUpdate = true;
                    }
                    if (eventStats.EventName.StartsWith("GC/SampledObjectAllocation"))
                    {
                        hasMemAllocStacks = true;
                    }
                    else if (eventStats.EventName.StartsWith("TypeLoad/Start"))
                    {
                        hasTypeLoad = true;
                    }
                    else if (eventStats.EventName.StartsWith("Loader/AssemblyLoad"))
                    {
                        hasAssemblyLoad = true;
                    }
                }
            }

            m_Children = new List<PerfViewTreeItem>();
            var advanced = new PerfViewTreeGroup("Advanced Group");
            var memory = new PerfViewTreeGroup("Memory Group");

            if (m_traceLog != null)
            {
                m_Children.Add(new PerfViewEventSource(this));
                m_Children.Add(new PerfViewEventStats(this));

                if (hasAnyStacks)
                {
                    m_Children.Add(new PerfViewStackSource(this, "Thread Time (with StartStop Activities)"));
                    m_Children.Add(new PerfViewStackSource(this, "Any"));
                }

                if (hasGC)
                {
                    memory.AddChild(new PerfViewGCStats(this));
                }

                if (hasGCAllocationTicks)
                {
                    if (hasObjectUpdate)
                    {
                        memory.Children.Add(new PerfViewStackSource(this, "GC Heap Net Mem (Coarse Sampling)"));
                        memory.Children.Add(new PerfViewStackSource(this, "Gen 2 Object Deaths (Coarse Sampling)"));
                    }
                    memory.Children.Add(new PerfViewStackSource(this, "GC Heap Alloc Ignore Free (Coarse Sampling)"));
                }
                if (hasMemAllocStacks)
                {
                    memory.Children.Add(new PerfViewStackSource(this, "GC Heap Net Mem"));
                    memory.Children.Add(new PerfViewStackSource(this, "GC Heap Alloc Ignore Free"));
                    memory.Children.Add(new PerfViewStackSource(this, "Gen 2 Object Deaths"));
                }

                if (hasDotNetHeapDumps)
                    memory.AddChild(new PerfViewHeapSnapshots(this));

                if (hasJIT)
                {
                    advanced.AddChild(new PerfViewJitStats(this));
                }

                if (hasJIT || hasTypeLoad || hasAssemblyLoad)
                {
                    advanced.AddChild(new PerfViewRuntimeLoaderStats(this));
                }
            }

            if (memory.Children.Count > 0)
            {
                m_Children.Add(memory);
            }

            if (advanced.Children.Count > 0)
            {
                m_Children.Add(advanced);
            }

            return null;
        }

        protected internal override StackSource OpenStackSourceImpl(string streamName, TextWriter log, double startRelativeMSec = 0, double endRelativeMSec = double.PositiveInfinity, Predicate<TraceEvent> predicate = null)
        {
            switch (streamName)
            {
                case "Any":
                    {
                        var eventLog = GetTraceLog(log);

                        var stackSource = new MutableTraceEventStackSource(eventLog);
                        // EventPipe currently only has managed code stacks.
                        stackSource.OnlyManagedCodeStacks = true;

                        stackSource.ShowUnknownAddresses = App.CommandLineArgs.ShowUnknownAddresses;
                        stackSource.ShowOptimizationTiers = App.CommandLineArgs.ShowOptimizationTiers;

                        TraceEvents events = eventLog.Events;

                        if (startRelativeMSec != 0 || endRelativeMSec != double.PositiveInfinity)
                        {
                            events = events.FilterByTime(startRelativeMSec, endRelativeMSec);
                        }

                        var eventSource = events.GetSource();
                        var sample = new StackSourceSample(stackSource);

                        eventSource.AllEvents += (data) =>
                        {
                            var callStackIdx = data.CallStackIndex();
                            if (callStackIdx != CallStackIndex.Invalid)
                            {
                                StackSourceCallStackIndex stackIndex = stackSource.GetCallStack(callStackIdx, data);

                                var asClrThreadSample = data as ClrThreadSampleTraceData;
                                if (asClrThreadSample != null)
                                {
                                    stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("Type: " + asClrThreadSample.Type), stackIndex);
                                    goto ADD_EVENT_FRAME;
                                }

                                var asAllocTick = data as GCAllocationTickTraceData;
                                if (asAllocTick != null)
                                {
                                    var frameIdx = stackSource.Interner.FrameIntern("EventData Kind " + asAllocTick.AllocationKind);
                                    stackIndex = stackSource.Interner.CallStackIntern(frameIdx, stackIndex);

                                    frameIdx = stackSource.Interner.FrameIntern("EventData Size " + asAllocTick.AllocationAmount64);
                                    stackIndex = stackSource.Interner.CallStackIntern(frameIdx, stackIndex);

                                    var typeName = asAllocTick.TypeName;
                                    if (string.IsNullOrEmpty(typeName))
                                    {
                                        typeName = "TypeId 0x" + asAllocTick.TypeID;
                                    }

                                    frameIdx = stackSource.Interner.FrameIntern("EventData TypeName " + typeName);
                                    stackIndex = stackSource.Interner.CallStackIntern(frameIdx, stackIndex);
                                    goto ADD_EVENT_FRAME;
                                }

                                // Tack on event nam
                                ADD_EVENT_FRAME:
                                var eventNodeName = "Event " + data.ProviderName + "/" + data.EventName;
                                stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern(eventNodeName), stackIndex);
                                // Add sample
                                sample.StackIndex = stackIndex;
                                sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                                sample.Metric = 1;
                                stackSource.AddSample(sample);
                            }
                        };
                        eventSource.Process();

                        stackSource.DoneAddingSamples();
                        return stackSource;
                    }
                case "Thread Time (with StartStop Activities)":
                    {
                        var eventLog = GetTraceLog(log);

                        var startStopSource = new MutableTraceEventStackSource(eventLog);
                        // EventPipe currently only has managed code stacks.
                        startStopSource.OnlyManagedCodeStacks = true;

                        var computer = new SampleProfilerThreadTimeComputer(eventLog, App.GetSymbolReader(eventLog.FilePath));
                        computer.GenerateThreadTimeStacks(startStopSource);

                        return startStopSource;
                    }
                case "GC Heap Alloc Ignore Free":
                    {
                        var eventLog = GetTraceLog(log);
                        var eventSource = eventLog.Events.GetSource();
                        var stackSource = new MutableTraceEventStackSource(eventLog);
                        var sample = new StackSourceSample(stackSource);

                        var gcHeapSimulators = new GCHeapSimulators(eventLog, eventSource, stackSource, log);
                        gcHeapSimulators.OnNewGCHeapSimulator = delegate (GCHeapSimulator newHeap)
                        {
                            newHeap.OnObjectCreate += delegate (Address objAddress, GCHeapSimulatorObject objInfo)
                            {
                                sample.Metric = objInfo.RepresentativeSize;
                                sample.Count = objInfo.RepresentativeSize / objInfo.Size;                                               // We guess a count from the size.  
                                sample.TimeRelativeMSec = objInfo.AllocationTimeRelativeMSec;
                                sample.StackIndex = stackSource.Interner.CallStackIntern(objInfo.ClassFrame, objInfo.AllocStack);        // Add the type as a pseudo frame.  
                                stackSource.AddSample(sample);
                                return true;
                            };
                        };
                        eventSource.Process();
                        stackSource.DoneAddingSamples();

                        return stackSource;
                    }
                default:
                    {
                        var eventLog = GetTraceLog(log);
                        var eventSource = eventLog.Events.GetSource();
                        var stackSource = new MutableTraceEventStackSource(eventLog);
                        var sample = new StackSourceSample(stackSource);

                        if (streamName.StartsWith("GC Heap Net Mem"))
                        {
                            var gcHeapSimulators = new GCHeapSimulators(eventLog, eventSource, stackSource, log);
                            if (streamName == "GC Heap Net Mem (Coarse Sampling)")
                            {
                                gcHeapSimulators.UseOnlyAllocTicks = true;
                                m_extraTopStats = "Sampled only 100K bytes";
                            }

                            gcHeapSimulators.OnNewGCHeapSimulator = delegate (GCHeapSimulator newHeap)
                            {
                                newHeap.OnObjectCreate += delegate (Address objAddress, GCHeapSimulatorObject objInfo)
                                {
                                    sample.Metric = objInfo.RepresentativeSize;
                                    sample.Count = objInfo.RepresentativeSize / objInfo.Size;                                                // We guess a count from the size.  
                                    sample.TimeRelativeMSec = objInfo.AllocationTimeRelativeMSec;
                                    sample.StackIndex = stackSource.Interner.CallStackIntern(objInfo.ClassFrame, objInfo.AllocStack);        // Add the type as a pseudo frame.  
                                    stackSource.AddSample(sample);
                                    return true;
                                };
                                newHeap.OnObjectDestroy += delegate (double time, int gen, Address objAddress, GCHeapSimulatorObject objInfo)
                                {
                                    sample.Metric = -objInfo.RepresentativeSize;
                                    sample.Count = -(objInfo.RepresentativeSize / objInfo.Size);                                            // We guess a count from the size.  
                                    sample.TimeRelativeMSec = time;
                                    sample.StackIndex = stackSource.Interner.CallStackIntern(objInfo.ClassFrame, objInfo.AllocStack);       // We remove the same stack we added at alloc.  
                                    stackSource.AddSample(sample);
                                };

                                newHeap.OnGC += delegate (double time, int gen)
                                {
                                    sample.Metric = double.Epsilon;
                                    sample.Count = 1;
                                    sample.TimeRelativeMSec = time;
                                    StackSourceCallStackIndex processStack = stackSource.GetCallStackForProcess(newHeap.Process);
                                    StackSourceFrameIndex gcFrame = stackSource.Interner.FrameIntern("GC Occurred Gen(" + gen + ")");
                                    sample.StackIndex = stackSource.Interner.CallStackIntern(gcFrame, processStack);
                                    stackSource.AddSample(sample);
                                };
                            };
                            eventSource.Process();
                            stackSource.DoneAddingSamples();
                        }
                        else if (streamName.StartsWith("Gen 2 Object Deaths"))
                        {
                            var gcHeapSimulators = new GCHeapSimulators(eventLog, eventSource, stackSource, log);

                            if (streamName == "Gen 2 Object Deaths (Coarse Sampling)")
                            {
                                gcHeapSimulators.UseOnlyAllocTicks = true;
                                m_extraTopStats = "Sampled only 100K bytes";
                            }

                            gcHeapSimulators.OnNewGCHeapSimulator = delegate (GCHeapSimulator newHeap)
                            {
                                newHeap.OnObjectDestroy += delegate (double time, int gen, Address objAddress, GCHeapSimulatorObject objInfo)
                                {
                                    if (2 <= gen)
                                    {
                                        sample.Metric = objInfo.RepresentativeSize;
                                        sample.Count = (objInfo.RepresentativeSize / objInfo.Size);                                         // We guess a count from the size.  
                                        sample.TimeRelativeMSec = objInfo.AllocationTimeRelativeMSec;
                                        sample.StackIndex = stackSource.Interner.CallStackIntern(objInfo.ClassFrame, objInfo.AllocStack);
                                        stackSource.AddSample(sample);
                                    }
                                };

                                newHeap.OnGC += delegate (double time, int gen)
                                {
                                    sample.Metric = double.Epsilon;
                                    sample.Count = 1;
                                    sample.TimeRelativeMSec = time;
                                    StackSourceCallStackIndex processStack = stackSource.GetCallStackForProcess(newHeap.Process);
                                    StackSourceFrameIndex gcFrame = stackSource.Interner.FrameIntern("GC Occurred Gen(" + gen + ")");
                                    sample.StackIndex = stackSource.Interner.CallStackIntern(gcFrame, processStack);
                                    stackSource.AddSample(sample);
                                };
                            };

                            eventSource.Process();
                            stackSource.DoneAddingSamples();
                        }
                        else if (streamName == "GC Heap Alloc Ignore Free (Coarse Sampling)")
                        {
                            TypeNameSymbolResolver typeNameSymbolResolver = new TypeNameSymbolResolver(FilePath, log);

                            bool seenBadAllocTick = false;

                            eventSource.Clr.GCAllocationTick += delegate (GCAllocationTickTraceData data)
                            {
                                sample.TimeRelativeMSec = data.TimeStampRelativeMSec;

                                var stackIndex = stackSource.GetCallStack(data.CallStackIndex(), data);

                                var typeName = data.TypeName;
                                if (string.IsNullOrEmpty(typeName))
                                {
                                    // Attempt to resolve the type name.
                                    TraceLoadedModule module = data.Process().LoadedModules.GetModuleContainingAddress(data.TypeID, data.TimeStampRelativeMSec);
                                    if (module != null)
                                    {
                                        // Resolve the type name.
                                        typeName = typeNameSymbolResolver.ResolveTypeName((int)(data.TypeID - module.ModuleFile.ImageBase), module.ModuleFile, TypeNameSymbolResolver.TypeNameOptions.StripModuleName);
                                    }
                                }

                                if (typeName != null && typeName.Length > 0)
                                {
                                    var nodeIndex = stackSource.Interner.FrameIntern("Type " + typeName);
                                    stackIndex = stackSource.Interner.CallStackIntern(nodeIndex, stackIndex);
                                }

                                sample.Metric = data.GetAllocAmount(ref seenBadAllocTick);

                                if (data.AllocationKind == GCAllocationKind.Large)
                                {

                                    var nodeIndex = stackSource.Interner.FrameIntern("LargeObject");
                                    stackIndex = stackSource.Interner.CallStackIntern(nodeIndex, stackIndex);
                                }

                                sample.StackIndex = stackIndex;
                                stackSource.AddSample(sample);
                            };
                            eventSource.Process();
                            m_extraTopStats = "Sampled only 100K bytes";
                        }
                        else
                        {
                            return null;
                        }

                        return stackSource;
                    }
            }
        }

        protected internal override void ConfigureStackWindow(string stackSourceName, StackWindow stackWindow)
        {
            ConfigureAsEtwStackWindow(stackWindow, false, true, true, false);
            if (stackSourceName.Contains("(with Tasks)") || stackSourceName.Contains("(with StartStop Activities)"))
            {
                var taskFoldPat = "^STARTING TASK";
                stackWindow.FoldRegExTextBox.Items.Add(taskFoldPat);
                stackWindow.FoldRegExTextBox.Text = taskFoldPat;

                var excludePat = "LAST_BLOCK";
                stackWindow.ExcludeRegExTextBox.Items.Add(excludePat);
                stackWindow.ExcludeRegExTextBox.Text = excludePat;
            }

            if (stackSourceName.Contains("Thread Time"))
            {
                stackWindow.ScalingPolicy = ScalingPolicyKind.TimeMetric;
            }

            if (stackSourceName.StartsWith("GC Heap Net Mem") || stackSourceName.StartsWith("GC Heap Alloc Ignore Free"))
            {
                stackWindow.ComputeMaxInTopStats = true;
            }

            if (m_extraTopStats != null)
            {
                stackWindow.ExtraTopStats += " " + m_extraTopStats;
            }
        }

        public override void Close()
        {
            if (m_traceLog != null)
            {
                m_traceLog.Dispose();
                m_traceLog = null;
            }
            base.Close();
        }

        public TraceLog GetTraceLog(TextWriter log, Action<bool, int, int> onLostEvents = null)
        {
            if (m_traceLog != null)
            {
                if (IsUpToDate)
                {
                    return m_traceLog;
                }

                m_traceLog.Dispose();
                m_traceLog = null;
            }
            else if (m_noTraceLogInfo)
            {
                return null;
            }

            var dataFileName = FilePath;
            UnZipIfNecessary(ref dataFileName);

            var options = new TraceLogOptions();
            options.ConversionLog = log;
            if (App.CommandLineArgs.KeepAllEvents)
            {
                options.KeepAllEvents = true;
            }

            options.MaxEventCount = App.CommandLineArgs.MaxEventCount;
            options.ContinueOnError = App.CommandLineArgs.ContinueOnError;
            options.SkipMSec = App.CommandLineArgs.SkipMSec;
            options.OnLostEvents = onLostEvents;
            options.LocalSymbolsOnly = false;
            options.ShouldResolveSymbols = delegate (string moduleFilePath) { return false; };       // Don't resolve any symbols

            // Generate the etlx file path / name.
            string etlxFile = CacheFiles.FindFile(dataFileName, ".etlx");
            bool isCachedEtlx = false;
            if (!File.Exists(etlxFile) || File.GetLastWriteTimeUtc(etlxFile) < File.GetLastWriteTimeUtc(dataFileName))
            {
                FileUtilities.ForceDelete(etlxFile);
                log.WriteLine("Creating ETLX file {0} from {1}", etlxFile, dataFileName);
                try
                {
                    TraceLog.CreateFromEventPipeDataFile(dataFileName, etlxFile, options);
                }
                catch (Exception e)
                {
                    log.WriteLine("Error: Exception EventPipe conversion: {0}", e.ToString());
                    log.WriteLine("[Error: exception while opening EventPipe data.]");

                    Debug.Assert(m_traceLog == null);
                    m_noTraceLogInfo = true;
                    return m_traceLog;
                }
            }
            else
            {
                isCachedEtlx = true;
            }

            var dataFileSize = "Unknown";
            if (File.Exists(dataFileName))
            {
                dataFileSize = ((new System.IO.FileInfo(dataFileName)).Length / 1000000.0).ToString("n3") + " MB";
            }

            log.WriteLine("ETL Size {0} ETLX Size {1:n3} MB", dataFileSize, (new System.IO.FileInfo(etlxFile)).Length / 1000000.0);

            // Open the ETLX file. 
            try
            {
                m_traceLog = new TraceLog(etlxFile);
            }
            catch (Exception)
            {
                if (isCachedEtlx)
                {
                    //  Delete the file and try again.
                    FileUtilities.ForceDelete(etlxFile);
                    if (!File.Exists(etlxFile))
                    {
                        return GetTraceLog(log, onLostEvents);
                    }
                }
                throw;
            }

            m_utcLastWriteAtOpen = File.GetLastWriteTimeUtc(FilePath);
            if (App.CommandLineArgs.UnsafePDBMatch)
            {
                m_traceLog.CodeAddresses.UnsafePDBMatching = true;
            }

            if (m_traceLog.Truncated)   // Warn about truncation.  
            {
                GuiApp.MainWindow.Dispatcher.BeginInvoke((Action)delegate ()
                {
                    MessageBox.Show("The ETL file was too big to convert and was truncated.\r\nSee log for details", "Log File Truncated", MessageBoxButton.OK);
                });
            }
            return m_traceLog;
        }

        private void UnZipIfNecessary(ref string inputFileName)
        {
            string extension = Path.GetExtension(inputFileName);
            string rest = Path.GetFileNameWithoutExtension(inputFileName);
            if (string.Compare(extension, ".zip", StringComparison.OrdinalIgnoreCase) == 0)
            {
                string subExtension = Path.GetExtension(rest);
                if (subExtension.Length > 0)
                {
                    string unzippedFile = CacheFiles.FindFile(inputFileName, subExtension);
                    if (File.Exists(unzippedFile) && File.GetLastWriteTimeUtc(inputFileName) <= File.GetLastWriteTimeUtc(unzippedFile))
                    {
                        inputFileName = unzippedFile;
                        return;
                    }

                    using (var zipArchive = ZipFile.OpenRead(inputFileName))
                    {
                        int count = zipArchive.Entries.Count;
                        foreach (var entry in zipArchive.Entries)
                        {
                            if (zipArchive.Entries.Count == 1 || entry.FullName.EndsWith(subExtension, StringComparison.OrdinalIgnoreCase))
                            {
                                entry.ExtractToFile(unzippedFile, true);
                                File.SetLastWriteTime(unzippedFile, DateTime.Now); // touch the file. 
                                break;
                            }
                        }
                    }
                    inputFileName = unzippedFile;
                }
            }
        }

        public TraceLog TryGetTraceLog() { return m_traceLog; }

        #region Private

        private void HandleLostEvents(Window parentWindow, bool truncated, int numberOfLostEvents, int eventCountAtTrucation, StatusBar worker)
        {
            string warning;
            if (!truncated)
            {
                warning = "WARNING: There were " + numberOfLostEvents + " lost events in the trace.\r\n" +
                    "Some analysis might be invalid.";
            }
            else
            {
                warning = "WARNING: The ETLX file was truncated at " + eventCountAtTrucation + " events.\r\n" +
                    "This is to keep the ETLX file size under 4GB, however all rundown events are processed.\r\n" +
                    "Use /SkipMSec:XXX after clearing the cache (File->Clear Temp Files) to see the later parts of the file.\r\n" +
                    "See log for more details.";
            }

            MessageBoxResult result = MessageBoxResult.None;
            parentWindow.Dispatcher.BeginInvoke((Action)delegate ()
            {
                result = MessageBox.Show(parentWindow, warning, "Lost Events", MessageBoxButton.OKCancel);
                worker.LogWriter.WriteLine(warning);
                if (result != MessageBoxResult.OK)
                {
                    worker.AbortWork();
                }
            });
        }

        private TraceLog m_traceLog;
        private bool m_noTraceLogInfo;
        private bool m_notifiedAboutLostEvents;
        #endregion
    }

    /// <summary>
    /// A simple helper class that looks up symbols for Project N GCDumps 
    /// </summary>
    internal class TypeNameSymbolResolver
    {
        public enum TypeNameOptions
        {
            None,
            StripModuleName,
        }

        /// <summary>
        /// Create a new symbol resolver.  You give it a context file path (PDBS are looked up next to this if non-null) and
        /// a text writer in which to write symbol diagnostic messages.  
        /// </summary>
        public TypeNameSymbolResolver(string contextFilePath, TextWriter log) { m_contextFilePath = contextFilePath; m_log = log; }

        public string ResolveTypeName(int rvaOfType, TraceModuleFile module, TypeNameOptions options = TypeNameOptions.None)
        {
            Module mod = new Module(module.ImageBase);
            mod.BuildTime = module.BuildTime;
            mod.Path = module.FilePath;
            mod.PdbAge = module.PdbAge;
            mod.PdbGuid = module.PdbSignature;
            mod.PdbName = module.PdbName;
            mod.Size = module.ImageSize;

            string typeName = ResolveTypeName(rvaOfType, mod);

            // Trim the module from the type name if requested.
            if (options == TypeNameOptions.StripModuleName && !string.IsNullOrEmpty(typeName))
            {
                // Strip off the module name if present.
                string[] typeNameParts = typeName.Split(new char[] { '!' }, 2);
                if (typeNameParts.Length == 2)
                {
                    typeName = typeNameParts[1];
                }
            }

            return typeName;
        }

        public string ResolveTypeName(int typeID, Graphs.Module module)
        {
            if (module == null || module.Path == null)
            {
                m_log.WriteLine("Error: null module looking up typeID  0x{0:x}", typeID);
                return null;
            }
            if (module.PdbName == null || module.PdbGuid == Guid.Empty)
            {
                m_log.WriteLine("Error: module for typeID 0x{0:x} {1} does not have PDB signature info.", typeID, module.Path);
                return null;
            }
            if (module.PdbGuid == m_badPdb && m_badPdb != Guid.Empty)
            {
                return null;
            }

            if (m_pdbLookupFailures != null && m_pdbLookupFailures.ContainsKey(module.PdbGuid))  // TODO we are assuming unique PDB names (at least for failures). 
            {
                return null;
            }

            if (m_symReader == null)
            {
                m_symReader = App.GetSymbolReader(m_contextFilePath);
            }

            NativeSymbolModule symbolModule = null;
            var pdbPath = m_symReader.FindSymbolFilePath(module.PdbName, module.PdbGuid, module.PdbAge, module.Path);
            if (pdbPath != null)
            {
                symbolModule = m_symReader.OpenNativeSymbolFile(pdbPath);
            }
            else
            {
                if (m_pdbLookupFailures == null)
                {
                    m_pdbLookupFailures = new Dictionary<Guid, bool>();
                }

                m_pdbLookupFailures.Add(module.PdbGuid, true);
            }

            if (symbolModule == null)
            {
                m_numFailures++;
                if (m_numFailures <= 5)
                {
                    if (m_numFailures == 1 && !Path.GetFileName(module.Path).StartsWith("mrt", StringComparison.OrdinalIgnoreCase))
                    {
                        GuiApp.MainWindow.Dispatcher.BeginInvoke((Action)delegate ()
                        {
                            MessageBox.Show(GuiApp.MainWindow,
                                "Warning: Could not find PDB for module " + Path.GetFileName(module.Path) + "\r\n" +
                                "Some types will not have symbolic names.\r\n" +
                                "See log for more details.\r\n" +
                                "Fix by placing PDB on symbol path or in a directory called 'symbols' beside .gcdump file.",
                                "PDB lookup failure");
                        });
                    }
                    m_log.WriteLine("Failed to find PDB for module {0} to look up type 0x{1:x}", module.Path, typeID);
                    if (m_numFailures == 5)
                    {
                        m_log.WriteLine("Discontinuing PDB module lookup messages");
                    }
                }
                return null;
            }

            string typeName;
            try
            {
                typeName = symbolModule.FindNameForRva((uint)typeID);
            }
            catch (OutOfMemoryException)
            {
                // TODO find out why this happens?   I think this is because we try to do a ReadRVA 
                m_log.WriteLine("Error: Caught out of memory exception on file " + symbolModule.SymbolFilePath + ".   Skipping.");
                m_badPdb = module.PdbGuid;
                return null;
            }

            typeName = typeName.Replace(@"::`vftable'", "");
            typeName = typeName.Replace(@"::", ".");
            typeName = typeName.Replace(@"EEType__", "");
            typeName = typeName.Replace(@".Boxed_", ".");

            return typeName;
        }

        #region private 
        private TextWriter m_log;
        private string m_contextFilePath;
        private SymbolReader m_symReader;
        private int m_numFailures;
        private Guid m_badPdb;        // If we hit a bad PDB remember it to avoid logging too much 
        private Dictionary<Guid, bool> m_pdbLookupFailures;
        #endregion
    }

    internal class ClrProfilerCodeSizePerfViewFile : PerfViewFile
    {
        public override string FormatName { get { return "Clr Profiler Code Size"; } }
        public override string[] FileExtensions { get { return new string[] { ".codesize" }; } }

        protected internal override StackSource OpenStackSourceImpl(TextWriter log)
        {
            var codeSizeSource = new ClrProfiler.ClrProfilerMethodSizeStackSource(FilePath);
            log.WriteLine("Info Read:  method called:{0}  totalILSize called:{1}  totalCalls:{2}",
                    codeSizeSource.TotalMethodCount, codeSizeSource.TotalMethodSize, codeSizeSource.TotalCalls);
            return codeSizeSource;
        }
        protected internal override void ConfigureStackWindow(string stackSourceName, StackWindow stackWindow)
        {
            var defaultGroup = "[group framework] !System.=> CLR;!Microsoft.=>CLR";
            stackWindow.GroupRegExTextBox.Text = defaultGroup;
            stackWindow.GroupRegExTextBox.Items.Insert(0, defaultGroup);
        }
    }

    internal class ClrProfilerAllocStacksPerfViewFile : PerfViewFile
    {
        public override string FormatName { get { return "Clr Profiler Alloc"; } }
        public override string[] FileExtensions { get { return new string[] { ".allocStacks" }; } }

        protected internal override StackSource OpenStackSourceImpl(TextWriter log)
        {
            return new ClrProfiler.ClrProfilerAllocStackSource(FilePath);
        }
        protected internal override void ConfigureStackWindow(string stackSourceName, StackWindow stackWindow)
        {
            var defaultGroup = "[group framework] !System.=> CLR;!Microsoft.=>CLR";
            stackWindow.GroupRegExTextBox.Text = defaultGroup;
            stackWindow.GroupRegExTextBox.Items.Insert(0, defaultGroup);
        }
    }

    public class ProcessDumpPerfViewFile : PerfViewFile
    {
        public override string FormatName { get { return "Process Dump"; } }
        public override string[] FileExtensions { get { return new string[] { ".dmp" }; } }

        public override void Open(Window parentWindow, StatusBar worker, Action doAfter = null)
        {
            App.CommandLineArgs.ProcessDumpFile = FilePath;
            GuiApp.MainWindow.TakeHeapShapshot(null);
        }
        public override void Close() { }
        public override ImageSource Icon { get { return GuiApp.MainWindow.Resources["FileBitmapImage"] as ImageSource; } }

        public const string DiagSessionIdentity = "Microsoft.Diagnostics.Minidump";
    }

    public class ScenarioSetPerfViewFile : PerfViewFile
    {
        public override string FormatName { get { return "Scenario Set"; } }
        public override string[] FileExtensions { get { return new string[] { ".scenarioSet.xml" }; } }

        public override string HelpAnchor { get { return "ViewingMultipleScenarios"; } }

        protected internal override StackSource OpenStackSourceImpl(TextWriter log)
        {
            Dictionary<string, string> pathDict = null;
            XmlReaderSettings settings = new XmlReaderSettings() { IgnoreWhitespace = true, IgnoreComments = true };
            using (Stream dataStream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                using (XmlReader reader = XmlTextReader.Create(dataStream, settings))
                {
                    pathDict = DeserializeConfig(reader, log);
                }
            }

            if (pathDict.Count == 0)
            {
                throw new ApplicationException("No scenarios found");
            }

            // Open XmlStackSources on each of our paths.
            var sources = pathDict.Select(
                (pair, idx) =>
                {
                    string name = pair.Key, path = pair.Value;
                    log.WriteLine("[Opening [{0}/{1}] {2} ({3})]", idx + 1, pathDict.Count, name, path);
                    var source = new XmlStackSource(path);
                    return new KeyValuePair<string, StackSource>(name, source);
                }
            ).ToList(); // Copy to list to prevent repeated enumeration from having an effect.

            return new AggregateStackSource(sources);
        }

        protected internal override void ConfigureStackWindow(string stackSourceName, StackWindow stackWindow)
        {
            ConfigureAsEtwStackWindow(stackWindow, false, false);
        }

        #region private

        /// <summary>
        /// Search for scenario data files matching a pattern, and add them to a dictionary.
        /// </summary>
        /// <param name="filePattern">The wildcard file pattern to match. Must not be null.</param>
        /// <param name="namePattern">The pattern by which to name scenarios. If null, defaults to "scenario $1".</param>
        /// <param name="includePattern">If non-null, a pattern which must be matched for the scenario to be added</param>
        /// <param name="excludePattern">If non-null, a pattern which if matched causes the scenario to be excluded</param>
        /// <param name="dict">The dictionary to which to add the scenarios found.</param>
        /// <param name="log">A log file to write log messages.</param>
        /// <param name="baseDir">
        /// The directory used to resolve relative paths.
        /// Defaults to the directory of the XML file represented by this ScenarioSetPerfViewFile.
        /// </param>
        private void AddScenariosToDictionary(
            string filePattern, string namePattern, string includePattern, string excludePattern,
            Dictionary<string, string> dict, TextWriter log,
            string baseDir = null)
        {
            Debug.Assert(filePattern != null);

            if (baseDir == null)
            {
                baseDir = Path.GetDirectoryName(FilePath);
            }

            if (namePattern == null)
            {
                namePattern = "scenario $1";
            }

            string replacePattern = Regex.Escape(filePattern)
                                    .Replace(@"\*", @"([^\\]*)")
                                    .Replace(@"\?", @"[^\\]");

            if (!(filePattern.EndsWith(".perfView.xml", StringComparison.OrdinalIgnoreCase) ||
                  filePattern.EndsWith(".perfView.xml.zip", StringComparison.OrdinalIgnoreCase)))
            {
                throw new ApplicationException("Files must be PerfView XML files");
            }

            string pattern = Path.GetFileName(filePattern);
            string dir = Path.GetDirectoryName(filePattern);

            // Tack on the base directory if we're not already an absolute path.
            if (!Path.IsPathRooted(dir))
            {
                dir = Path.Combine(baseDir, dir);
            }

            var replaceRegex = new Regex(replacePattern, RegexOptions.IgnoreCase);
            var defaultRegex = new Regex(@"(.*)", RegexOptions.IgnoreCase);

            // TODO: Directory.GetFile
            foreach (string file in Directory.GetFiles(dir, pattern, SearchOption.AllDirectories))
            {
                // Filter out those that don't match the include pattern 
                if (includePattern != null && !Regex.IsMatch(file, includePattern))
                {
                    continue;
                }
                // or do match the exclude pattern.  
                if (excludePattern != null && Regex.IsMatch(file, excludePattern))
                {
                    continue;
                }

                string name = null;
                if (namePattern != null)
                {
                    var match = replaceRegex.Match(file);

                    // We won't have a group to match if there were no wildcards in the pattern.
                    if (match.Groups.Count < 1)
                    {
                        match = defaultRegex.Match(GetFileNameWithoutExtension(file));
                    }

                    name = match.Result(namePattern);
                }

                dict[name] = file;

                log.WriteLine("Added '{0}' ({1})", name, file);
            }
        }

        /// <summary>
        /// Deserialize a scenario XML config file.
        /// </summary>
        /// <param name="reader">The XmlReader containing the data to deserialize.</param>
        /// <param name="log">The TextWriter to log output to.</param>
        /// <returns>A Dictionary mapping scenario names to .perfView.xml(.zip) data file paths.</returns>
        /// <remarks>
        /// Scenario XML config files contain a ScenarioSet root element. That element contains
        /// one or more Scenarios elements. A Scenarios element has two attributes: "files" is a required
        /// filename pattern, and namePattern is a pattern by which to name the scenario.
        /// 
        /// files is a required attribute specifying where to find the data files for the scenario(s). Wildcards
        /// are acceptable - any files matched by the wildcard will be added to the scenario set. All paths are
        /// relative to the location of the XML config file.
        /// 
        /// namePattern can contain substitutions as specified in Regex.Replace. Each * in the wildcard
        /// pattern will be converted to an appropriate capturing group. If no wildcards are specified, $1 will be
        /// set to the base name of the data file as specified by <see cref="PerfViewFile.GetFileNameWithoutExtension"/>.
        /// 
        /// files is a required attribute. namePattern is optional, and defaults to "scenario $1".
        /// 
        /// If multiple scenarios have the same name, scenarios later in the file will override scenarios
        /// earlier in the file.
        /// </remarks>
        /// <example>
        /// Example config file:
        /// <ScenarioSet>
        /// <Scenarios files="*.perfView.xml.zip" namePattern="Example scenario [$1]" />
        /// <Scenarios files="foo.perfView.xml.zip" namePattern="Example scenario [baz]" />
        /// </ScenarioSet>
        /// 
        /// Files in the directory:
        /// foo.perfView.xml.zip
        /// bar.perfView.xml.zip
        /// baz.perfView.xml.zip
        /// 
        /// Return value:
        /// "Example scenario [foo]" => "foo.perfView.xml.zip"
        /// "Example scenario [bar]" => "bar.perfView.xml.zip"
        /// "Example scenario [baz]" => "foo.perfView.xml.zip"
        /// </example>
        private Dictionary<string, string> DeserializeConfig(XmlReader reader, TextWriter log)
        {
            var pathDict = new Dictionary<string, string>();

            if (!reader.ReadToDescendant("ScenarioSet"))
            {
                throw new ApplicationException("The file " + FilePath + " does not have a Scenario element");
            }

            if (!reader.ReadToDescendant("Scenarios"))
            {
                throw new ApplicationException("No scenarios specified");
            }

            do
            {
                string filePattern = reader["files"];
                string namePattern = reader["namePattern"];
                string includePattern = reader["includePattern"];
                string excludePattern = reader["excludePattern"];

                if (filePattern == null)
                {
                    throw new ApplicationException("File path is required.");
                }

                AddScenariosToDictionary(filePattern, namePattern, includePattern, excludePattern, pathDict, log);
            }
            while (reader.ReadToNextSibling("Scenarios"));

            return pathDict;
        }

        #endregion
    }

    /// <summary>
    /// Class to represent the Visual Studio .diagsesion file format that is defined
    /// as part of Microsoft.DiagnosticsHub.Packaging
    /// </summary>
    public class DiagSessionPerfViewFile : PerfViewFile
    {
        public override string FormatName { get { return "Diagnostics Session"; } }
        public override string[] FileExtensions { get { return new string[] { ".diagsession" }; } }

        public override IList<PerfViewTreeItem> Children
        {
            get
            {
                if (m_Children == null)
                {
                    m_Children = new List<PerfViewTreeItem>();
                }

                return m_Children;
            }
        }

        public override void Open(Window parentWindow, StatusBar worker, Action doAfter = null)
        {
            if (!m_opened)
            {
                IsExpanded = false;

                worker.StartWork("Opening " + Name, delegate ()
                {
                    OpenImpl(parentWindow, worker);
                    ExecuteOnOpenCommand(worker);

                    worker.EndWork(delegate ()
                    {
                        m_opened = true;

                        FirePropertyChanged("Children");

                        IsExpanded = true;

                        doAfter?.Invoke();
                    });
                });
            }
        }

        protected override Action<Action> OpenImpl(Window parentWindow, StatusBar worker)
        {
            worker.Log("Opening diagnostics session file " + Path.GetFileName(FilePath));

            using (DhPackage dhPackage = DhPackage.Open(FilePath))
            {
                // Get all heap dump resources
                AddResourcesAsChildren(worker, dhPackage, HeapDumpPerfViewFile.DiagSessionIdentity, ".gcdump", (localFilePath) =>
                {
                    return HeapDumpPerfViewFile.Get(localFilePath);
                });

                // Get all process dump resources
                AddResourcesAsChildren(worker, dhPackage, ProcessDumpPerfViewFile.DiagSessionIdentity, ".dmp", (localFilePath) =>
                {
                    return ProcessDumpPerfViewFile.Get(localFilePath);
                });

                // Get all ETL files
                AddEtlResourcesAsChildren(worker, dhPackage);

                // Extract the symbols contained in the package
                ExtractSymbolResources(worker, dhPackage);
            }

            return null;
        }

        public override void Close() { }
        public override ImageSource Icon { get { return GuiApp.MainWindow.Resources["FileBitmapImage"] as ImageSource; } }

        /// <summary>
        /// Gets a new local file path for the given resource, extracting it from the .diagsession if required
        /// </summary>
        /// <param name="packageFilePath">The file path to the package</param>
        /// <param name="package">The diagsession package object (opened from the file path)</param>
        /// <param name="resource">The diagsession resource object</param>
        /// <param name="fileExtension">The final extension to use</param>
        /// <param name="alternateName">Alternate name to use for file</param>
        /// <returns>The full local file path to the resource</returns>
        private static string GetLocalFilePath(string packageFilePath, DhPackage package, ResourceInfo resource, string fileExtension, string alternateName = null)
        {
            string localFileName = alternateName ?? Path.GetFileNameWithoutExtension(resource.Name);
            string localFilePath = CacheFiles.FindFile(packageFilePath, "_" + localFileName + fileExtension);

            if (!File.Exists(localFilePath))
            {
                package.ExtractResourceToPath(ref resource.ResourceId, localFilePath);
            }

            return localFilePath;
        }

        /// <summary>
        /// Gets a new local path for the given resource, extracting it from the .diagsession if required
        /// </summary>
        /// <param name="packageFilePath">The file path to the package</param>
        /// <param name="package">The diagsession package object (opened from the file path)</param>
        /// <param name="resource">The diagsession resource object</param>
        /// <returns>The full local path to the resource</returns>
        private static string GetLocalDirPath(string packageFilePath, DhPackage package, ResourceInfo resource)
        {
            string localDirName = resource.Name;
            string localDirPath = CacheFiles.FindFile(packageFilePath, "_" + localDirName);

            if (!Directory.Exists(localDirPath))
            {
                package.ExtractResourceToPath(ref resource.ResourceId, localDirPath);
            }

            return localDirPath;
        }

        /// <summary>
        /// Adds child files from resources in the DhPackage
        /// </summary>
        private void AddResourcesAsChildren(StatusBar worker, DhPackage dhPackage, string resourceIdentity, string fileExtension, Func<string/*localFileName*/, PerfViewFile> getPerfViewFile)
        {
            ResourceInfo[] resources;
            dhPackage.GetResourceInformationByType(resourceIdentity, out resources);

            foreach (var resource in resources)
            {
                string localFilePath = GetLocalFilePath(FilePath, dhPackage, resource, fileExtension);

                worker.Log("Found '" + resource.ResourceId + "' resource '" + resource.Name + "'. Loading ...");

                PerfViewFile perfViewFile = getPerfViewFile(localFilePath);
                perfViewFile.Name = resource.Name;

                Children.Add(perfViewFile);

                worker.Log("Loaded " + resource.Name + ". Loading ...");
            }
        }

        /// <summary>
        /// Extract symbols from the DhPackage
        /// </summary>
        private void ExtractSymbolResources(StatusBar worker, DhPackage dhPackage)
        {
            string symbolCachePath = new SymbolPath(App.SymbolPath).DefaultSymbolCache();

            try
            {
                ResourceInfo[] resources;
                dhPackage.GetResourceInformationByType("DiagnosticsHub.Resource.SymbolCache", out resources);

                foreach (var resource in resources)
                {
                    string localDirPath = GetLocalDirPath(FilePath, dhPackage, resource);

                    worker.Log("Found '" + resource.ResourceId + "' resource '" + resource.Name + "'. Loading ...");

                    foreach (var subPath in Directory.EnumerateDirectories(localDirPath))
                    {
                        // The directories contained in the symbol cache resource _are_ in the symbol cache format
                        // which means directories are in the form of /<file.ext>/<hash>/file.ext so in this case
                        // we use the GetFileName API since it will consider the dictory name a file name.
                        var targetDir = Path.Combine(symbolCachePath, Path.GetFileName(subPath));

                        if (!Directory.Exists(targetDir))
                        {
                            Directory.Move(subPath, targetDir);
                        }
                        else
                        {
                            // The directory exists, so we must merge the two cache directories
                            foreach (var symbolVersionDir in Directory.EnumerateDirectories(subPath))
                            {
                                var targetVersionDir = Path.Combine(targetDir, Path.GetFileName(symbolVersionDir));
                                if (!Directory.Exists(targetVersionDir))
                                {
                                    Directory.Move(symbolVersionDir, targetVersionDir);
                                }
                            }
                        }
                    }

                    // Clean up the extracted symbol cache
                    Directory.Delete(localDirPath, true);
                }
            }
            catch (Exception e)
            {
                worker.Log($"Failed to extract symbols from {Path.GetFileName(FilePath)} ... (Exception: {e.Message})");
            }
        }

        /// <summary>
        /// Adds child files from ETL resources in the DhPackage
        /// </summary>
        private void AddEtlResourcesAsChildren(StatusBar worker, DhPackage dhPackage)
        {
            ResourceInfo[] resources;
            dhPackage.GetResourceInformationByType("DiagnosticsHub.Resource.EtlFile", out resources);

            foreach (var resource in CreateEtlResources(dhPackage, resources))
            {
                worker.Log("Found  resource '" + resource.Name + "'. Loading ...");

                Children.Add(resource);

                worker.Log("Loaded '" + resource.Name + "'. Loading ...");
            }
        }

        /// <summary>
        /// Organize DiagSession ETL resources for the UI
        /// </summary>
        private IEnumerable<PerfViewFile> CreateEtlResources(
            DhPackage dhPackage,
            IEnumerable<ResourceInfo> resources)
        {
            string auxStandardCollectorEtlFile = null;
            var newResources = new List<PerfViewFile>();
            foreach (var resource in resources)
            {
                // If the standard collector auxiliary file is present, the standard collection (Diagnostics Hub)
                // created this DiagSession which means we should process the ETL files in bulk.
                if (resource.Name.Equals("sc.user_aux.etl", StringComparison.OrdinalIgnoreCase))
                {
                    auxStandardCollectorEtlFile = GetLocalFilePath(FilePath, dhPackage, resource, ".etl", "sc");
                }
                else
                {
                    var etlFile = GetLocalFilePath(FilePath, dhPackage, resource, ".etl");

                    // The "sc." prefix is considered reserved when loading an ETL from a DiagSession, but we
                    // still want to extract the file, even though the UI will not show it.
                    if (!resource.Name.StartsWith("sc.", StringComparison.OrdinalIgnoreCase))
                    {
                        var file = ETLPerfViewData.Get(etlFile);
                        file.Name = resource.Name;
                        newResources.Add(file);
                    }
                }
            }

            if (auxStandardCollectorEtlFile != null)
            {
                Debug.Assert(File.Exists(auxStandardCollectorEtlFile), "Standard Collector auxiliary file must exist to properly handle bulk processing");
                var mergedEtlFilename = Path.GetFileNameWithoutExtension(FilePath);

                var file = ETLPerfViewData.Get(auxStandardCollectorEtlFile);
                file.Name = $"{mergedEtlFilename}.etl (Merged)";

                // Insert the "merged" file at the top of the list
                newResources.Insert(0, file);
            }

            return newResources;
        }
    }
}
