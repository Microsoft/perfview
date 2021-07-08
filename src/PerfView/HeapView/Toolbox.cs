using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Analysis.GC;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Stacks;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Packaging;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Xps;
using System.Windows.Xps.Packaging;

namespace PerfView
{
    /// <summary>
    /// Another IProcess implementation with more information (CLR version for the moment)
    /// </summary>
    internal class ProcInfo : IProcess
    {
        internal ProcInfo(string name, string clr)
        {
            Name = name;
            StartTime = DateTime.MaxValue;
            CommandLine = "";
            Clr = clr;
        }

        public string Clr { get; private set; }
        public string Name { get; private set; }
        public DateTime StartTime { get; internal set; }
        public DateTime EndTime { get; internal set; }
        public string CommandLine { get; internal set; }

        public string Duration
        {
            get
            {
                double duration = (EndTime - StartTime).TotalSeconds;
                return duration.ToString("f2") + " sec";
            }
        }

        public int ProcessID { get; internal set; }

        public int ParentID { get; internal set; }

        public double CPUTimeMSec { get; internal set; }

        public int CompareTo(IProcess other)
        {
            ProcInfo p = other as ProcInfo;

            int ret = 0;

            if (p != null)
            {
                ret = -Clr.CompareTo(p.Clr);

                if (ret != 0)
                {
                    return ret;
                }
            }

            // Choose largest CPU time first.  
            ret = -CPUTimeMSec.CompareTo(other.CPUTimeMSec);

            if (ret != 0)
            {
                return ret;
            }

            // Otherwise go by date (reversed)
            return -StartTime.CompareTo(other.StartTime);
        }

    }


    /// <summary>
    /// Wrapper around Visual as FrameworkElement
    /// </summary>
    public class VisualHolder : FrameworkElement
    {
        private Visual m_visual;
        private double m_displayZoom;
        private double m_widthZoom;
        private int m_width;
        private int m_height;
        private double m_x0, m_x1;

        public VisualHolder()
        {
            m_displayZoom = 1;
            m_widthZoom = 1;
        }

        private static Typeface m_arial;

        internal void AddMessage(DrawingContext dc, Point startPoint, Point endPoint)
        {
            double duration = Math.Abs(GetValueX(startPoint.X) - GetValueX(endPoint.X));

            if (m_arial == null)
            {
                m_arial = new Typeface("arial");
            }

            string message;

            if (duration < 1000)
            {
                message = String.Format("{0:N1} ms", duration);
            }
            else
            {
                message = String.Format("{0:N3} s", duration / 1000);
            }

            dc.DrawText(new FormattedText(message, Thread.CurrentThread.CurrentCulture, FlowDirection.LeftToRight, m_arial, 10, Brushes.Black),
                new Point(startPoint.X, startPoint.Y - 10));
        }

        public double GetValueX(double screenX)
        {
            // screenX = x * m_x1 + m_x0
            return (screenX - m_x0) / m_x1;
        }

        public void SetVisual(int width, int height, Visual visual, double widthZoom, double zoom, double x0, double x1)
        {
            m_widthZoom = widthZoom;
            m_width = width;
            m_height = height;
            m_visual = visual;
            m_x0 = x0;
            m_x1 = x1;

            AddVisualChild(visual);
            SetZoom(zoom);

            UpdateLayout();
        }

        public void SetZoom(double zoom)
        {
            m_displayZoom = zoom / m_widthZoom;
            Width = m_width * m_displayZoom;
            Height = m_height;

            RenderTransform = new ScaleTransform(m_displayZoom, 1, 0, 0);
        }

        protected override int VisualChildrenCount
        {
            get
            {
                return 1;
            }
        }

        protected override Visual GetVisualChild(int index)
        {
            return m_visual;
        }
    }


    /// <summary>
    /// HTML generation
    /// </summary>
    internal class HtmlWriter : StreamWriter
    {
        public HtmlWriter(string fileName)
            : base(fileName)
        {
        }

        public void StartUl()
        {
            WriteLine("<ul>");
        }

        public void StartTable()
        {
            WriteLine("<table cellspacing=\"0\" border=\"1\">");
        }
    }


    /// <summary>
    /// Wrapper Visual for XPS generation with specific page size
    /// </summary>
    public class VisualPaginator : DocumentPaginator
    {
        private Size m_size;
        private Visual m_visual;

        public VisualPaginator(Visual visual, double width, double height)
        {
            m_visual = visual;
            m_size = new Size(width, height);
        }

        public override IDocumentPaginatorSource Source
        {
            get
            {
                return null;
            }
        }

        public override DocumentPage GetPage(int pageNumber)
        {
            Rect box = new Rect(0, 0, m_size.Width, m_size.Height);

            return new DocumentPage(m_visual, m_size, box, box);
        }

        public override bool IsPageCountValid
        {
            get
            {
                return true;
            }
        }

        public override int PageCount
        {
            get
            {
                return 1;
            }
        }

        public override System.Windows.Size PageSize
        {
            get
            {
                return m_size;
            }
            set
            {
                m_size = value;
            }
        }

    }

    /// <summary>
    /// Reusable helper methods
    /// </summary>
    internal static class Toolbox
    {
        public const string PercentageFormat = "{0:N2} %";
        public const string TimeFormat = "{0:N3} ms";
        public const string TimeFormatN0 = "{0:N0} ms";
        public const string MemoryFormatN0 = "{0:N0} mb";
        public const string MemoryFormatN3 = "{0:N3} mb";
        public const string CountFormatN0 = "{0:N0}";

        /// <summary>
        /// Create StackPanel with two children
        /// </summary>
        internal static StackPanel Stack(Orientation or, UIElement elm1, UIElement elm2, UIElement elm3 = null)
        {
            StackPanel panel = new StackPanel();

            panel.HorizontalAlignment = HorizontalAlignment.Left;
            panel.VerticalAlignment = VerticalAlignment.Top;
            panel.Orientation = or;

            panel.Children.Add(elm1);
            panel.Children.Add(elm2);

            if (elm3 != null)
            {
                panel.Children.Add(elm3);
            }

            return panel;
        }

        /// <summary>
        /// Create DockPanel with three children
        /// </summary>
        internal static DockPanel DockTopLeft(UIElement top, UIElement left, UIElement right)
        {
            DockPanel dock = new DockPanel();

            if (top != null)
            {
                DockPanel.SetDock(top, Dock.Top);

                dock.Children.Add(top);
            }

            if (left != null)
            {
                DockPanel.SetDock(left, Dock.Left);
                dock.Children.Add(left);
            }

            dock.Children.Add(right);

            return dock;
        }

        /// <summary>
        /// Dock top
        /// </summary>
        internal static DockPanel DockTop(this DockPanel panel, UIElement elm)
        {
            DockPanel.SetDock(elm, Dock.Top);

            panel.Children.Add(elm);

            return panel;
        }

        /// <summary>
        /// Dock top
        /// </summary>
        internal static DockPanel DockBottom(this DockPanel panel, UIElement elm)
        {
            DockPanel.SetDock(elm, Dock.Bottom);

            panel.Children.Add(elm);

            return panel;
        }

        /// <summary>
        /// Dock left
        /// </summary>
        internal static DockPanel DockLeft(this DockPanel panel, UIElement elm)
        {
            DockPanel.SetDock(elm, Dock.Left);

            panel.Children.Add(elm);

            return panel;
        }

        /// <summary>
        /// Dock right
        /// </summary>
        internal static DockPanel DockRight(this DockPanel panel, FrameworkElement elm)
        {
            DockPanel.SetDock(elm, Dock.Right);

            elm.HorizontalAlignment = HorizontalAlignment.Right;
            panel.Children.Add(elm);

            return panel;
        }

        /// <summary>
        /// Walk Visual tree to find first child of certain type
        /// </summary>
        private static object FindChild(Visual visual, Type typ)
        {
            if (visual == null)
            {
                return null;
            }

            if (visual.GetType() == typ)
            {
                return visual;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
            {
                Object obj = FindChild(VisualTreeHelper.GetChild(visual, i) as Visual, typ);

                if (obj != null)
                {
                    return obj;
                }
            }

            return null;
        }

        /// <summary>
        /// Walk Visual tree for investigation
        /// </summary>
        private static void WalkTree(StringBuilder sb, Visual visual)
        {
            if (visual == null)
            {
                return;
            }

            string v = visual.GetType().ToString();

            int p = v.LastIndexOf('.');

            if (p >= 0)
            {
                v = v.Substring(p + 1);
            }

            sb.Append(v);

            int child = VisualTreeHelper.GetChildrenCount(visual);

            if (child != 0)
            {
                sb.Append('[');

                for (int i = 0; i < child; i++)
                {
                    if (i != 0)
                    {
                        sb.Append(", ");
                    }

                    WalkTree(sb, VisualTreeHelper.GetChild(visual, i) as Visual);
                }

                sb.Append(']');
            }
        }

        /// <summary>
        /// Create right justify style
        /// </summary>
        internal static Style RightJustifyStyle(Style baseStyle)
        {
            Style s = new Style();

            s.BasedOn = baseStyle;
            s.TargetType = typeof(DataGridCell);
            s.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));

            return s;
        }

        /// <summary>
        /// Add Button column to DataGrid
        /// </summary>
        internal static DataGridTemplateColumn AddButtonColumn(this DataGrid grid, Type dataType, object header, string binding, RoutedEventHandler OnClick = null)
        {
            DataGridTemplateColumn col = new DataGridTemplateColumn();

            col.Header = header;

            DataTemplate template = new DataTemplate();

            FrameworkElementFactory but = new FrameworkElementFactory(typeof(Button));
            but.SetBinding(Button.ContentProperty, new Binding(binding));
            but.SetBinding(Button.VisibilityProperty, new Binding("Visible"));
            but.SetBinding(Button.TagProperty, new Binding("."));

            if (OnClick != null)
            {
                but.AddHandler(Button.ClickEvent, OnClick);
            }

            template.VisualTree = but;
            template.DataType = dataType;

            col.CellTemplate = template;

            grid.Columns.Add(col);

            return col;
        }

        /// <summary>
        /// Add column to Datagrid
        /// </summary>
        internal static DataGridTextColumn AddColumn(this DataGrid grid, object header, string binding, bool right = false, string format = null)
        {
            DataGridTextColumn col = new DataGridTextColumn();

            col.Header = header;

            Binding b = new Binding(binding);

            if (format != null)
            {
                b.StringFormat = format;
            }

            if (right)
            {
                col.CellStyle = RightJustifyStyle(col.CellStyle);
            }

            col.Binding = b;

            grid.Columns.Add(col);

            return col;
        }

        /// <summary>
        /// Add column to Datagrid
        /// </summary>
        internal static DataGridTextColumn AddColumn(this DataGrid grid, object header, string binding, IValueConverter converter, object para = null, bool right = false)
        {
            DataGridTextColumn col = new DataGridTextColumn();

            col.Header = header;

            Binding b = new Binding(binding);

            b.ConverterParameter = para;
            b.Converter = converter;

            if (right)
            {
                col.CellStyle = RightJustifyStyle(col.CellStyle);
            }

            col.Binding = b;
            col.MaxWidth = 500;
            grid.Columns.Add(col);

            return col;
        }

        /// <summary>
        /// Create CheckBox
        /// </summary>
        internal static CheckBox CreateCheckBox(bool state, string label, int left = 0, int top = 0, RoutedEventHandler checkHandler = null)
        {
            CheckBox check = new CheckBox();
            check.Content = label;
            check.Margin = new Thickness(left, top, 0, 0);
            check.IsChecked = state;

            if (checkHandler != null)
            {
                check.Checked += checkHandler;
                check.Unchecked += checkHandler;
            }

            return check;
        }

        /// <summary>
        /// Add CheckBox
        /// </summary>
        internal static CheckBox AddCheckBox(this Panel panel, bool state, string label, int left = 0, int top = 0, RoutedEventHandler checkHandler = null)
        {
            CheckBox check = CreateCheckBox(state, label, left, top, checkHandler);

            panel.Children.Add(check);

            return check;
        }

        /// <summary>
        /// Create Button
        /// </summary>
        internal static Button CreateButton(string label, int width, RoutedEventHandler onClick, int left = 0, int top = 0)
        {
            Button button = new Button();

            button.Content = label;
            button.Width = width;
            button.Margin = new Thickness(left, top, 1, 1);

            if (onClick != null)
            {
                button.Click += onClick;
            }

            return button;
        }

        /// <summary>
        /// Add Button
        /// </summary>
        internal static Button AddButton(this Panel panel, string label, int width, RoutedEventHandler onClick, int left = 0, int top = 0)
        {
            Button button = CreateButton(label, width, onClick, left, top);

            panel.Children.Add(button);

            return button;
        }

        /// <summary>
        /// Create TextBlock with Hyperlink
        /// </summary>
        internal static TextBlock CreateTextBlock(string label, string param, RoutedEventHandler binding, int left = 0, int top = 0)
        {
            TextBlock tb = new TextBlock();

            tb.Margin = new Thickness(left, top, 0, 0);
            tb.Inlines.Add(label);

            if (binding != null)
            {
                Hyperlink link = new Hyperlink();
                link.Inlines.Add("?");
                link.Click += binding;
                link.CommandParameter = param;
                tb.Inlines.Add(link);
            }

            return tb;
        }

        /// <summary>
        /// Add TextBlock with Hyperlink
        /// </summary>
        internal static TextBlock AddTextBlock(this Panel panel, string label, string param, RoutedEventHandler binding, int left = 0, int top = 0)
        {
            TextBlock tb = CreateTextBlock(label, param, binding, left, top);

            panel.Children.Add(tb);

            return tb;
        }

        /// <summary>
        /// Check if a ToggleButton is not checked
        /// </summary>
        internal static bool UnChecked(this ToggleButton button)
        {
            bool? check = button.IsChecked;

            if (check.HasValue && !check.Value)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Add MenuItem
        /// </summary>
        internal static MenuItem AddItem(this ItemsControl menu, string header, bool checkable = false, bool chked = false)
        {
            MenuItem item = new MenuItem();

            item.Header = header;
            item.IsCheckable = checkable;
            item.IsChecked = chked;

            menu.Items.Add(item);

            return item;
        }

        /// <summary>
        /// Hookup MenuItem Checked/Unchecked events to UIElement visibility
        /// </summary>
        internal static void HookupVisibility(this MenuItem item, UIElement target)
        {
            item.CommandParameter = target;

            item.Checked += MakeVisible;
            item.Unchecked += MakeCollapsed;
        }

        /// <summary>
        /// Collapse UIElement
        /// </summary>
        internal static void MakeCollapsed(object sender, RoutedEventArgs e)
        {
            MenuItem item = sender as MenuItem;

            if (item != null)
            {
                UIElement elm = item.CommandParameter as UIElement;

                if (elm != null)
                {
                    elm.Visibility = Visibility.Collapsed;
                }
            }
        }

        /// <summary>
        /// Make UIElement visible
        /// </summary>
        internal static void MakeVisible(object sender, RoutedEventArgs e)
        {
            MenuItem item = sender as MenuItem;

            if (item != null)
            {
                UIElement elm = item.CommandParameter as UIElement;

                if (elm != null)
                {
                    elm.Visibility = Visibility.Visible;
                }
            }
        }

        /// <summary>
        /// Check for CLR modules
        /// </summary>
        internal static bool IsClr(this string name)
        {
            return name.Equals("clr", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("coreclr", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("mscorwks", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("mrt100", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Check for BCL modules
        /// </summary>
        internal static bool IsMscorlib(this string name)
        {
            return name.Equals("mscorlib", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("mscorlib.ni", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("corefx", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Get processes with CLR loaded
        /// </summary>
        internal static List<IProcess> GetClrProcesses(this TraceLog traceLog, out bool hasClr)
        {
            List<IProcess> result = new List<IProcess>();

            hasClr = false;

            foreach (TraceProcess process in traceLog.Processes)
            {
                if (process.ProcessID <= 0)
                {
                    continue;
                }

                bool keep = true; // keep process with no module info

                TraceLoadedModules mods = process.LoadedModules;

                string clrVersion = String.Empty;

                if (mods != null)
                {
                    foreach (TraceLoadedModule m in mods)
                    {
                        keep = false;

                        string name = m.Name;

                        if (name.IsClr() || name.IsMscorlib() && String.IsNullOrEmpty(clrVersion))
                        {
                            clrVersion = m.ModuleFile.FileVersion;

                            if (String.IsNullOrEmpty(clrVersion))
                            {
                                clrVersion = m.ModuleFile.FilePath;

                                int pos = clrVersion.LastIndexOf('\\');

                                if (pos > 0)
                                {
                                    int p = clrVersion.LastIndexOf('\\', pos - 1);

                                    if (p > 0)
                                    {
                                        pos = p;
                                    }

                                    clrVersion = clrVersion.Substring(pos + 1);
                                }
                            }

                            hasClr = true;
                            keep = true; // or with CLR loaded

                            if (name.IsClr())
                            {
                                break;
                            }
                        }
                    }
                }

                if (keep)
                {
                    ProcInfo proc = new ProcInfo(process.Name, clrVersion);

                    proc.StartTime = process.StartTime;
                    proc.EndTime = process.EndTime;
                    proc.CPUTimeMSec = process.CPUMSec;
                    proc.ParentID = process.ParentID;
                    proc.CommandLine = process.CommandLine;
                    proc.ProcessID = process.ProcessID;

                    result.Add(proc);
                }
            }

            result.Sort();

            return result;
        }

        /// <summary>
        /// Process selection dialog box with CLR processes only, extra column for CLR version
        /// </summary>
        internal static void SelectClrProcess(this TraceLog traceLog, Action<List<IProcess>> action)
        {
            bool hasClr = false;

            // Filter to processes with CLR loaded
            List<IProcess> clrProcesses = traceLog.GetClrProcesses(out hasClr);

            SelectProcess selectProcess = new SelectProcess(GuiApp.MainWindow, clrProcesses, new TimeSpan(1, 0, 0), action, false);

            // Add a column for CLR version
            if (hasClr)
            {
                DataGrid grid = selectProcess.Grid;

                Toolbox.AddColumn(grid, "CLR", ".", new IProcessConverter());
            }

            selectProcess.Show();
        }

        /// <summary>
        /// Save Visual as PNG file
        /// </summary>
        internal static string SaveAsPng(Visual visual, int width, int height, string fileName)
        {
            RenderTargetBitmap image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);

            image.Render(visual);

            string pngFile = fileName;

            using (FileStream stream = new FileStream(pngFile, FileMode.Create))
            {
                PngBitmapEncoder encoder = new PngBitmapEncoder();

                encoder.Frames.Add(BitmapFrame.Create(image));
                encoder.Save(stream);
            }

            return pngFile;
        }

        /// <summary>
        /// Save Visual as XPS file, may be slow
        /// </summary>
        internal static string SaveAsXps(Visual visual, int width, int height, string fileName)
        {
            string xpsFile = fileName;

            Package container = null;

            // XpsFile may be held by XpsViewer
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    container = Package.Open(xpsFile, FileMode.Create);
                }
                catch (System.IO.IOException)
                {
                }

                if (container != null)
                {
                    break;
                }

                xpsFile = fileName + "_" + (DateTime.Now.Ticks % 100) + ".xps";
            }

            if (container != null)
            {
                using (XpsDocument xpsDoc = new XpsDocument(container, CompressionOption.Maximum))
                {
                    XpsDocumentWriter writer = XpsDocument.CreateXpsDocumentWriter(xpsDoc);

                    VisualPaginator page = new VisualPaginator(visual, width, height);

                    writer.Write(page);
                }

                container.Close();
            }

            return xpsFile;
        }

        /// <summary>
        /// Wrap around Panel with top-left border, with a label as Tooltip
        /// </summary>
        internal static Panel Wrap(this Panel panel, string help)
        {
            StackPanel left = new StackPanel();
            left.Width = 8;
            left.Background = new LinearGradientBrush(Color.FromRgb(3, 8, 111), Colors.LightGray, 90);
            left.ToolTip = help;

            TextBlock text = new TextBlock();
            text.Text = help;
            text.Foreground = Brushes.Yellow;
            text.FontSize = 8;
            text.LayoutTransform = new RotateTransform(90);
            left.Children.Add(text);

            StackPanel top = new StackPanel();
            top.Height = 3;
            top.Background = new LinearGradientBrush(Color.FromRgb(3, 8, 111), Colors.LightGray, 0);

            return DockTopLeft(top, left, panel);
        }

        /// <summary>
        /// FileSaveAs dialog box
        /// </summary>
        internal static string GetSaveFileName(string fileName, string ext, string description)
        {
            SaveFileDialog dlg = new SaveFileDialog();

            dlg.FileName = fileName;
            dlg.DefaultExt = ext;
            dlg.Filter = String.Format("{0} Files ({1})|*{1}", description, ext);
            dlg.AddExtension = true;
            dlg.CheckFileExists = false;
            dlg.OverwritePrompt = true;

            Nullable<bool> result = dlg.ShowDialog();

            // Process save file dialog box results
            if (result == true)
            {
                return dlg.FileName;
            }

            return null;
        }

        /// <summary>
        /// Get method name from CodeAddressIndex
        /// </summary>
        internal static string GetMethodName(this TraceLog log, CodeAddressIndex index)
        {
            TraceCodeAddress addr = log.CodeAddresses[index];

            TraceModuleFile mod = addr.ModuleFile;

            string method;

            if (mod != null)
            {
                method = addr.FullMethodName;

                if (String.IsNullOrEmpty(method))
                {
                    method = String.Format("{0}!0x{1:x}", mod.Name, (ulong)addr.Address - (ulong)mod.ImageBase);
                }
                else
                {
                    method = mod.Name + "!" + method;
                }
            }
            else
            {
                method = String.Format("0x{0:x}", addr.Address);
            }

            return method;
        }

        /// <summary>
        /// Create StackSource from supported named 'stream's, with filtering
        /// Check ETLPerfViewData.OpenStackSourceImpl for real implementation
        /// </summary>
        internal static StackSource CreateStackSource(this PerfViewFile dataFile, string stream, int procID, TextWriter log, bool largeAlloc = false)
        {
            EventFilter filter = new EventFilter(procID, largeAlloc);

            return dataFile.OpenStackSourceImpl(stream, log, 0, double.PositiveInfinity, filter.Filter);
        }

        /// <summary>
        /// Open/Reopen StackWindow for a process and sets/resets its time range
        /// </summary>
        internal static void StackWindowTo(this PerfViewFile dataFile, Window parent, ref StackWindow stackWindow, StackSource source, string stream, double t0 = 0, double t1 = 0)
        {
            if (stackWindow == null)
            {
                if (source != null)
                {
                    PerfViewStackSource perfViewStackSource = new PerfViewStackSource(dataFile, stream);

                    stackWindow = new StackWindow(parent, perfViewStackSource);

                    if (t1 > 0)
                    {
                        stackWindow.StartTextBox.Text = t0.ToString();
                        stackWindow.EndTextBox.Text = t1.ToString();
                    }

                    stackWindow.SetStackSource(source);
                }
                else
                {
                    throw new NotSupportedException("Only ETL/ETLX supported");
                }
            }
            else if (t1 > 0)
            {
                stackWindow.StartTextBox.Text = t0.ToString();
                stackWindow.EndTextBox.Text = t1.ToString();

                stackWindow.Update();
            }

            stackWindow.Show();
        }

        internal static bool IsInduced(this TraceGC evt)
        {
            GCReason reason = evt.Reason;

            return (reason == GCReason.Induced) ||
                   (reason == GCReason.InducedLowMemory) ||
                   (reason == GCReason.InducedNotForced);
        }

        internal static bool IsAllocLarge(this TraceGC evt)
        {
            GCReason reason = evt.Reason;

            return (reason == GCReason.AllocLarge) || (reason == GCReason.OutOfSpaceLOH);
        }

        internal static string GetTip(this TraceGC gc)
        {
            return String.Format("GC# {0}, Gen {1}, pause {2:N3} ms", gc.Number, gc.Generation, gc.PauseDurationMSec);
        }
    }


    /// <summary>
    /// DataBinding for ProcInfo in DataGrid
    /// </summary>
    public class IProcessConverter : IValueConverter
    {
        object IValueConverter.Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            ProcInfo proc = value as ProcInfo;

            if (proc != null)
            {
                return proc.Clr;
            }
            else
            {
                return value.ToString();
            }
        }

        object IValueConverter.ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }

    /// <summary>
    /// Add extra drawing to RubberBand adorner
    /// </summary>
    public delegate void AddExtra(DrawingContext dc, Point start, Point end);

    public delegate bool CreateContextMenu(ContextMenu cm, Point start, Point end);

    /// <summary>
    /// Rubber Band implemented as Adorner
    /// </summary>
    internal class RubberBandAdorner : Adorner
    {
        private Point m_startPoint;
        private Point m_endPoint;
        private FrameworkElement m_canvas;
        private AdornerLayer m_layer;
        private bool m_bandVisible;
        private bool m_selectionGood;
        private AddExtra m_addExtra;
        private CreateContextMenu m_createMenu;

        public RubberBandAdorner(FrameworkElement canvas, AddExtra message = null, CreateContextMenu createMenu = null)
            : base(canvas)
        {
            m_canvas = canvas;
            m_addExtra = message;
            m_createMenu = createMenu;

            m_canvas.PreviewMouseLeftButtonDown += OnMouseLeftButtonDown;
            m_canvas.MouseLeftButtonUp += OnMouseLeftButtonUp;
            m_canvas.MouseMove += OnCanvasMouseMove;

            if (createMenu != null)
            {
                m_canvas.MouseRightButtonUp += OnMouseRightButtonUp;
            }
        }

        public void Detach()
        {
            Clear();

            m_canvas.PreviewMouseLeftButtonDown -= OnMouseLeftButtonDown;
            m_canvas.MouseLeftButtonUp -= OnMouseLeftButtonUp;
            m_canvas.MouseMove -= OnCanvasMouseMove;

            if (m_createMenu != null)
            {
                m_canvas.MouseRightButtonUp -= OnMouseRightButtonUp;
            }
        }

        /// <summary>
        /// Left butto down: start rubber banding
        /// </summary>
        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!m_canvas.IsMouseCaptured)
            {
                m_startPoint = e.GetPosition(m_canvas);
                m_endPoint = m_startPoint;
                m_selectionGood = false;

                m_layer = AdornerLayer.GetAdornerLayer(m_canvas);

                if (!m_bandVisible)
                {
                    m_bandVisible = true;
                    m_layer.Add(this);
                }

                Mouse.Capture(m_canvas);
            }
        }

        internal bool GetSelection(out Point start, out Point end)
        {
            start = m_startPoint;
            end = m_endPoint;

            return m_selectionGood;
        }

        internal void Clear()
        {
            if (m_bandVisible)
            {
                m_bandVisible = false;
                m_layer.Remove(this);
            }
        }

        /// <summary>
        /// Left button up: end rubber banding
        /// </summary>
        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (m_canvas.IsMouseCaptured)
            {
                m_selectionGood = true;
                m_canvas.ReleaseMouseCapture();
            }
        }

        /// <summary>
        /// Mouse move, update rubber banding if started
        /// </summary>
        private void OnCanvasMouseMove(object sender, MouseEventArgs e)
        {
            if (m_canvas.IsMouseCaptured)
            {
                m_endPoint = e.GetPosition(m_canvas);
                m_layer.Update();
            }
        }

        /// <summary>
        /// Generate Visual for rubber band, with extra drawing through delegate
        /// </summary>
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            Rect rect = new Rect(m_startPoint, m_endPoint);

            dc.DrawGeometry(null, new Pen(Brushes.Brown, 1), new RectangleGeometry(rect));

            m_addExtra?.Invoke(dc, m_startPoint, m_endPoint);
        }

        private ContextMenu m_selectMenu;

        private void OnMouseRightButtonUp(object sender, RoutedEventArgs e)
        {
            if (m_selectionGood)
            {
                if (m_selectMenu == null)
                {
                    m_selectMenu = new ContextMenu();
                }

                if (m_createMenu(m_selectMenu, m_startPoint, m_endPoint))
                {
                    m_canvas.ContextMenu = m_selectMenu;
                }
                else
                {
                    m_canvas.ContextMenu = null;
                }
            }
        }
    }

    /// <summary>
    /// Filter for TraceEvent
    /// </summary>
    internal class EventFilter
    {
        private int m_procID;
        private bool m_largeAlloc;

        internal EventFilter(int procID, bool largeAlloc)
        {
            m_procID = procID;
            m_largeAlloc = largeAlloc;
        }

        internal bool Filter(TraceEvent anEvent)
        {
            int pid = anEvent.ProcessID;

            // FIX Virtual allocs
            bool result = (pid == m_procID) || (pid == -1);

            if (result && m_largeAlloc)
            {
                GCAllocationTickTraceData data = anEvent as GCAllocationTickTraceData;

                if (data != null)
                {
                    if (data.AllocationKind != GCAllocationKind.Large)
                    {
                        return false;
                    }
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Hook up StackWindow
    /// </summary>
    internal class StackWindowHook
    {
        private PerfViewFile m_file;
        private int m_procID;
        private StatusBar m_status;
        private Window m_parent;
        private StackWindow m_stackWindow;
        private string m_stream;
        private double m_t0, m_t1;

        internal StackWindowHook(PerfViewFile file, int procID, StatusBar status, string stream, Window parent)
        {
            m_file = file;
            m_procID = procID;
            m_status = status;
            m_stream = stream;
            m_parent = parent;
        }

        private void OnCloseStack(object sender, EventArgs e)
        {
            m_stackWindow = null;
        }

        private MenuItem m_stackTo;

        internal void AttachMenu(ContextMenu m, double t0, double t1)
        {
            if (!m.Items.Contains(m_stackTo))
            {
                m_stackTo = new MenuItem();
                m_stackTo.Click += OnStackTo;

                m.Items.Add(m_stackTo);
            }

            m_t0 = t0;
            m_t1 = t1;

            m_stackTo.Header = String.Format("Open {0} StackWindow to [{1:N3} ms .. {2:N3} ms]", m_stream, t0, t1);
        }

        private void OnStackTo(object sender, RoutedEventArgs e)
        {
            StackSource source = null;

            if (m_stackWindow == null)
            {
                source = m_file.CreateStackSource(m_stream, m_procID, m_status.LogWriter);
            }

            m_file.StackWindowTo(m_parent, ref m_stackWindow, source, m_stream, m_t0, m_t1);

            if (m_stackWindow != null)
            {
                m_stackWindow.Closed += OnCloseStack;
            }

            m_status.Status = String.Format("{0} StackWindow To({1:N3} .. {2:N3})", m_stream, m_t0, m_t1);
        }

    }

    /// <summary>
    /// A panel as a row in a Grid panel, with splitter and visibility control
    /// </summary>
    internal class GridRow
    {
        private Panel m_panel;
        private RowDefinition m_rowDef;
        private int m_row;
        private GridSplitter m_splitter;
        private Grid m_grid;

        public GridRow(Grid grid, Panel panel, bool visible, bool split, int row, int height)
        {
            m_grid = grid;
            m_panel = panel;
            m_row = row;
            m_rowDef = new RowDefinition();
            m_rowDef.Tag = row;

            if (split)
            {
                m_rowDef.Height = new GridLength(height, GridUnitType.Pixel);
            }
            else
            {
                m_rowDef.Height = new GridLength(height, GridUnitType.Star);
            }

            Grid.SetRow(panel, row);
            grid.Children.Add(panel);

            if (split)
            {
                m_splitter = new GridSplitter();
                Grid.SetRow(m_splitter, row);
                m_splitter.HorizontalAlignment = HorizontalAlignment.Stretch;
                m_splitter.VerticalAlignment = VerticalAlignment.Bottom;
                m_splitter.Height = 5;

                grid.Children.Add(m_splitter);
            }

            if (visible)
            {
                MakeVisible(null, null);
            }
            else
            {
                MakeCollapsed(null, null);
            }
        }

        /// <summary>
        /// Hookup MenuItem Checked/Unchecked events to UIElement visibility
        /// </summary>
        internal void HookupVisibility(MenuItem item)
        {
            item.Checked += MakeVisible;
            item.Unchecked += MakeCollapsed;
        }

        /// <summary>
        /// Collapse UIElement
        /// </summary>
        internal void MakeCollapsed(object sender, RoutedEventArgs e)
        {
            m_panel.Visibility = Visibility.Collapsed;

            if (m_grid.RowDefinitions.Contains(m_rowDef))
            {
                m_grid.RowDefinitions.Remove(m_rowDef);
            }

            if (m_splitter != null)
            {
                m_splitter.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Make UIElement visible
        /// </summary>
        internal void MakeVisible(object sender, RoutedEventArgs e)
        {
            m_panel.Visibility = Visibility.Visible;

            RowDefinitionCollection col = m_grid.RowDefinitions;

            if (!col.Contains(m_rowDef)) // Insert in order
            {
                int i = 0;

                while (i < col.Count)
                {
                    if (m_row < (int)col[i].Tag)
                    {
                        break;
                    }

                    i++;
                }

                col.Insert(i, m_rowDef);
            }

            if (m_splitter != null)
            {
                m_splitter.Visibility = Visibility.Visible;
            }
        }
    }

    /// <summary>
    /// StackSource building by adding samples
    /// </summary>
    internal class StackSourceBuilder
    {
        private MutableTraceEventStackSource m_stackSource;
        private StackSourceSample m_sample;
        private TraceLog m_trace;

        internal StackSourceBuilder(TraceLog trace)
        {
            m_trace = trace;
            m_stackSource = new MutableTraceEventStackSource(trace);
            m_sample = new StackSourceSample(m_stackSource);

            m_stackSource.ShowUnknownAddresses = App.CommandLineArgs.ShowUnknownAddresses;
            m_stackSource.ShowOptimizationTiers = App.CommandLineArgs.ShowOptimizationTiers;
        }

        internal StackSource Stacks
        {
            get
            {
                return m_stackSource;
            }
        }

        internal void AddSample(TraceThread thread, double cost, double timeStamp, string nodeName, EventIndex index)
        {
            CallStackIndex stack = m_trace.GetEvent(index).CallStackIndex();

            if (stack != CallStackIndex.Invalid)
            {
                m_sample.Metric = cost;
                m_sample.Count = 1;
                m_sample.TimeRelativeMSec = timeStamp;

                StackSourceFrameIndex nodeIndex = m_stackSource.Interner.FrameIntern(nodeName);

                m_sample.StackIndex = m_stackSource.Interner.CallStackIntern(nodeIndex, m_stackSource.GetCallStackThread(stack, thread));

                m_stackSource.AddSample(m_sample);
            }
        }
    }

    /// <summary>
    /// Set of brushes, pens, for showing one generation of GC
    /// </summary>
    internal class ColorScheme
    {
        private Color gcColor;
        private Color budgetColor;
        private Brush strokeBrush;

        internal Brush gcBrush;
        internal Brush inducedGcBrush;
        internal Brush budgetBrush;
        internal Pen memoryPen2;
        internal Pen memoryPen1;
        internal string label;

        private static Pen CreateRoundPen(Brush brush, double thickness)
        {
            Pen p = new Pen(brush, thickness);

            p.LineJoin = PenLineJoin.Round;

            return p;
        }

        private const int maxRGB = 255;
        private const int maxHSL = 240;

        public static Color ToRgbColor(int hue, int saturation, int luminosity)
        {
            hue = Math.Min(maxHSL, Math.Max(0, hue));
            saturation = Math.Min(maxHSL, Math.Max(0, saturation));
            luminosity = Math.Min(maxHSL, Math.Max(0, luminosity));

            int red = 0, green = 0, blue = 0;

            if (luminosity != 0)
            {
                if (saturation == 0)
                {
                    // if there's no saturation, then color is based on luminosity.
                    int temp = luminosity * maxRGB / maxHSL;

                    red = temp;
                    green = temp;
                    blue = temp;
                }
                else
                {
                    int magic2;

                    // calculate the lum/sat mixture values based on specified luminosity.
                    if (luminosity <= maxHSL / 2)
                    {
                        magic2 = (luminosity * (maxHSL + saturation) + (maxHSL / 2)) / maxHSL;
                    }
                    else
                    {
                        magic2 = luminosity + saturation - ((luminosity * saturation) + (maxHSL / 2)) / maxHSL;
                    }

                    int magic1 = 2 * luminosity - magic2;

                    // calculate the red, green, blue values based on the lum/sat mixture and
                    // factors of the hue for each color.
                    red = HueToRgb(magic1, magic2, hue + (maxHSL / 3));
                    green = HueToRgb(magic1, magic2, hue);
                    blue = HueToRgb(magic1, magic2, hue - (maxHSL / 3));
                }
            }

            // now create a Color instance to return for the calculated RGB values.
            return Color.FromRgb((byte)red, (byte)green, (byte)blue);
        }

        private static int HueToRgb(int m1, int m2, int hue)
        {
            int result = 0;

            // range check
            if (hue < 0)
            {
                // check for overflow before attempting to add
                if (hue + maxHSL >= hue)
                {
                    hue += maxHSL;
                }
            }

            if (hue > maxHSL)
            {
                // check for underflow before attempting to subtract
                if (hue - maxHSL <= hue)
                {
                    hue -= maxHSL;
                }
            }

            // return r,g, or b value from this tridrant.
            if (6 * hue < maxHSL)
            {
                result = m1 + (((m2 - m1) * hue + (maxHSL / 12)) / (maxHSL / 6));
            }
            else if (2 * hue < maxHSL)
            {
                result = m2;
            }
            else if (3 * hue < 2 * maxHSL)
            {
                result = m1 + (((m2 - m1) * (((maxHSL * 2) / 3) - hue) + (maxHSL / 12)) / (maxHSL / 6));
            }
            else
            {
                result = m1;
            }

            // based on the value range of the hue, calculate the final RGB color value.
            return (result * maxRGB + (maxHSL / 2)) / maxHSL;
        }

        internal ColorScheme(string _label, Color fill, Color darkFill, Color stroke)
        {
            label = _label;

            gcColor = Color.FromArgb(230, fill.R, fill.G, fill.B);  // opacity = 0.9
            budgetColor = Color.FromArgb(63, fill.R, fill.G, fill.B);  // opacity = 0.25

            gcBrush = new SolidColorBrush(gcColor);
            inducedGcBrush = new LinearGradientBrush(darkFill, gcColor, 90);
            budgetBrush = new SolidColorBrush(budgetColor);

            strokeBrush = new SolidColorBrush(stroke);
            memoryPen1 = CreateRoundPen(strokeBrush, 1);
            memoryPen2 = CreateRoundPen(strokeBrush, 2);
        }

        internal ColorScheme(string _label, int h, int s, int l) :
            this(
                _label,
                ToRgbColor(h, s, l * 12 / 10),  // + 20% luminosity
                ToRgbColor(h, s, l * 6 / 10),  // - 40% luminosity
                ToRgbColor(h, s, l)
                )
        {
        }
    }

    internal enum ModuleClass : byte
    {
        Free,
        Unknown,

        DriverKernel,
        OSKernel,
        OSUser,
        OSGraphics,

        JScript,
        Win8Store,

        NativeApp,

        Clr,
        Mscorlib,
        ManagedNgen,
        ManagedIL,

        Max
    };

    // Faster call stack query
    internal class StackDecoder
    {
        private TraceLog m_trace;
        private TraceCodeAddresses m_addresses;
        private TraceModuleFiles m_modules;
        private TraceCallStacks m_stacks;
        private ModuleClass[] m_modList;
        private bool m_wwaHost;

        internal bool WwaHost
        {
            get
            {
                return m_wwaHost;
            }
        }

        private ModuleClass GetClass(TraceModuleFile mod)
        {
            string name = mod.Name.ToLowerInvariant();

            ModuleClass klass = ModuleClass.Unknown;

            switch (name)
            {
                case "ntoskrnl":
                    klass = ModuleClass.OSKernel;
                    break;

                case "ntdll":
                case "kernelbase":
                case "kernel32":
                case "msvcrt":
                case "gdi32":
                case "user32":
                case "combase":
                    klass = ModuleClass.OSUser;
                    break;

                case "clr":
                case "coreclr":
                case "mscorwks":
                case "mrt100":
                    klass = ModuleClass.Clr;
                    break;

                case "mscorlib":
                case "mscorlib.ni":
                case "corefx":
                case "corelib":
                    klass = ModuleClass.Mscorlib;
                    break;

                case "windows.ui.xaml":
                    klass = ModuleClass.Win8Store;
                    break;

                case "jscript9":
                case "chakra":
                    klass = ModuleClass.JScript;
                    break;

                case "d3d10warp":
                case "dwrite":
                case "d2d1":
                case "nvwgf2um":
                case "d3d11":
                case "dcomp":
                    klass = ModuleClass.OSGraphics;
                    break;

                case "wwahost":
                    m_wwaHost = true;
                    break;

                default:
                    break;
            }

            return klass;
        }

        public StackDecoder(TraceLog trace)
        {
            m_trace = trace;
            m_addresses = m_trace.CodeAddresses;
            m_modules = m_trace.ModuleFiles;
            m_stacks = m_trace.CallStacks;

            m_modList = new ModuleClass[m_modules.Count];

            foreach (TraceModuleFile mod in m_modules)
            {
                m_modList[(int)mod.ModuleFileIndex] = GetClass(mod);
            }
        }

        public CallStackIndex FirstFrame(EventIndex evnt)
        {
            // Implemented by binary search on eventsToStacks
            return m_trace.GetEvent(evnt).CallStackIndex();
        }

        public CallStackIndex GetCaller(CallStackIndex frame)
        {
            // Implemented by querying private GrowableArray<CallStackInfo>
            return m_stacks.Caller(frame);
        }

        public CodeAddressIndex GetFrameAddr(CallStackIndex frame)
        {
            // Implemented by querying private GrowableArray<CallStackInfo>
            return m_stacks.CodeAddressIndex(frame);
        }

        public ModuleFileIndex GetModuleIndex(CallStackIndex frame)
        {
            return m_addresses.ModuleFileIndex(m_stacks.CodeAddressIndex(frame)); ;
        }

        public ModuleClass GetModuleClass(CallStackIndex frame)
        {
            ModuleFileIndex mod = GetModuleIndex(frame);

            if (mod != ModuleFileIndex.Invalid)
            {
                return m_modList[(int)mod];
            }
            else
            {
                return ModuleClass.Unknown;
            }
        }

        public TraceModuleFile GetModule(CallStackIndex frame)
        {
            ModuleFileIndex mod = GetModuleIndex(frame);

            if (mod != ModuleFileIndex.Invalid)
            {
                return m_modules[mod];
            }
            else
            {
                return null;
            }
        }
    }


}