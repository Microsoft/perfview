using Microsoft.Diagnostics.Tracing.Analysis.GC;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace PerfView
{
    internal class GeometryBuilder
    {
        private StreamGeometry m_geometry;
        private StreamGeometryContext m_context;

        internal void AddRect(double x, double y, double w, double h)
        {
            if (m_geometry == null)
            {
                m_geometry = new StreamGeometry();
                m_context = m_geometry.Open();
            }

            m_context.BeginFigure(new Point(x, y), true, true);
            m_context.LineTo(new Point(x + w, y), false, true);
            m_context.LineTo(new Point(x + w, y + h), false, true);
            m_context.LineTo(new Point(x, y + h), false, true);
        }

        internal StreamGeometry Close()
        {
            if (m_context != null)
            {
                m_context.Close();
                m_geometry.Freeze();
            }

            return m_geometry;
        }
    }

    /// <summary>
    /// Class for combining multiple time intervals on the same horizontal bar
    /// </summary>
    internal class BarInterval
    {
        private double m_start;
        private double m_end;
        private Renderer m_render;
        private Brush m_brush;

        internal BarInterval(Renderer render, Brush brush)
        {
            m_start = -1;
            m_end = -1;
            m_render = render;
            m_brush = brush;
        }

        internal void Start(double start)
        {
            if (m_start < 0) // new interval
            {
                m_start = start;
            }
            else if (start > m_end) // disconnected with old one, draw old, start new
            {
                Draw();

                m_start = start;
                m_end = -1;   // may not close
            } // merge with old
        }

        internal void End(double end)
        {
            if (m_start > 0) // if opened
            {
                m_end = end;
            }
        }

        internal void Draw()
        {
            if (m_end > 0) // if closed
            {
                m_render.DrawBar(m_start, m_end, m_brush);
            }
        }
    }

    /// <summary>
    /// Renderer: WPF Visual generation
    /// </summary>
    internal class Renderer
    {
        private const double minWidth = 0.1;   // min GC bar width
        private const double margin = 5;
        private int m_width;
        private int m_height;
        private int m_xAxis, m_yAxis;
        private DrawingVisual m_visual;
        private DrawingContext m_context;
        private double m_x0;
        private double m_x1;
        private double m_gcHeight;
        private Typeface m_arial;

        public void SetTimeScale(double start, double end, out double x0, out double x1, bool drawTick)
        {
            double duration = end - start;

            start -= duration / 50; // Add 2% before first event
            end += duration / 50; // Add 2% after  last  event

            m_x0 = -start;
            m_x1 = (m_width - m_xAxis - 5) / (end - start);

            // screen = (t + m_x0) * m_x1 + m_xAxis;
            x0 = m_x0 * m_x1 + m_xAxis;
            x1 = m_x1;

            end /= 100; // in 100 ms

            m_arial = new Typeface("arial");

            if (!drawTick)
            {
                return;
            }

            double oneSecond = MapXDelta(1000);

            int size10 = 9;
            int size1 = 0;
            int size01 = 0;

            if (oneSecond >= 100)
            {
                size10 = 13;
                size1 = 11;
                size01 = 9;
            }
            else if (oneSecond >= 20)
            {
                size10 = 11;
                size1 = 9;
            }

            // second marks
            for (int t = (int)(start / 100); t < end; t++)
            {
                double x = MapX(t * 100);

                if (x >= m_xAxis)
                {
                    if ((t % 100) == 0) // 10 seconds
                    {
                        m_context.DrawRectangle(Brushes.Black, null, new Rect(x - 1, m_yAxis, 2, 8));

                        DrawText(x - 5, m_yAxis + 10, (t / 10).ToString(), size10);
                    }
                    else if ((t % 10) == 0) // 1 second
                    {
                        m_context.DrawRectangle(Brushes.Black, null, new Rect(x - 0.5, m_yAxis, 1, 6));

                        if (size1 != 0)
                        {
                            DrawText(x - 5, m_yAxis + 8, (t / 10).ToString(), size1);
                        }
                    }
                    else if (size01 != 0) // 100 ms
                    {
                        m_context.DrawRectangle(Brushes.Black, null, new Rect(x - 0.5, m_yAxis, 1, 4));
                    }
                }
            }
        }

        private Pen m_blackPen;
        private Brush m_black;
        private Brush m_red;
        private Brush m_yellow;
        private Brush m_blue25;
        private ColorScheme m_g0Palette;
        private ColorScheme m_g1Palette;
        private ColorScheme m_g2Palette;
        private ColorScheme m_gLPalette;
        private ColorScheme m_gTPalette;
        private ColorScheme[] m_Palettes;

        public void Open(int width, int height, bool allocTick)
        {
            m_black = Brushes.Black;
            m_red = Brushes.Red;
            m_yellow = Brushes.Yellow;
            m_blue25 = new SolidColorBrush(Color.FromArgb(63, 0, 0, 255));

            m_g0Palette = new ColorScheme("Gen0", 60, 238, 111);    // RGB(118,235,1)
            m_g1Palette = new ColorScheme("Gen1", 40, 238, 111);    // RGB(235,235,1)
            m_g2Palette = new ColorScheme("Gen2", 20, 238, 111);    // RGB(235,118,1)
            m_gLPalette = new ColorScheme("Loh", 0, 238, 111);    // RGB(235,  1,1)
            m_gTPalette = new ColorScheme("Total", Colors.Black, Colors.Black, Colors.Black);

            m_Palettes = new ColorScheme[] { m_g0Palette, m_g1Palette, m_g2Palette, m_gLPalette, m_gTPalette };

            m_width = width;
            m_height = height;
            m_visual = new DrawingVisual();

            m_blackPen = new Pen(Brushes.Black, 1);

            m_xAxis = 48;
            m_yAxis = height - 20;

            if (allocTick)
            {
                m_yAxis -= 200;
            }

            m_gcHeight = m_yAxis - margin;

            m_context = m_visual.RenderOpen();

            DrawRect(Brushes.WhiteSmoke, 0, 0, width, height);
        }

        private double MapX(double t)
        {
            return (t + m_x0) * m_x1 + m_xAxis;
        }

        private double MapXDelta(double t)
        {
            return t * m_x1;
        }

        private void DrawRect(Brush b, double x, double y, double width, double height)
        {
            if ((width > 0) && (height > 0))
            {
                m_context.DrawRectangle(b, null, new Rect(x, y, width, height));
            }
        }

        // G0/G3 budget
        // GC start/end eventd
        public void DrawGCEvent(double start, double end, int gen, double lastEnd, double g0Budget, double g3Budget, double gcCpu, bool induced, GeometryBuilder[] gcBarList)
        {
            start = MapX(start);
            end = MapX(end);

            if (lastEnd > 0)
            {
                double x = MapX(lastEnd);

                if (start >= (x + 1)) // Width at least 1 pixel in 96 dpi
                {
                    double y;

                    if (!Double.IsNaN(g0Budget) && (g0Budget > m_yPixel))
                    {
                        y = MapY(g0Budget);
                        DrawRect(m_g0Palette.budgetBrush, x, y, start - x, m_yAxis - y);
                    }

                    if (!Double.IsNaN(g3Budget) && (g3Budget > m_yPixel))
                    {
                        y = MapY(g3Budget);
                        DrawRect(m_gLPalette.budgetBrush, x, y, start - x, m_yAxis - y);
                    }
                }
            }

            if (induced)
            {
                gen += 3;
            }

            if (gcBarList[gen] == null)
            {
                gcBarList[gen] = new GeometryBuilder();
            }

            gcBarList[gen].AddRect(start, m_yAxis - m_gcHeight, Math.Max(minWidth, end - start), m_gcHeight);

            //    DrawRect(
            //      induced ? m_Palettes[gen].inducedGcBrush : m_Palettes[gen].gcBrush, 
            //      start, m_yAxis - m_gcHeight, Math.Max(minWidth, end - start), m_gcHeight);

            if (gcCpu > 0)
            {
                //DrawRect(Brushes.Black, start, m_yAxis - 100, MapXDelta(gcCpu), 20);
            }
        }

        internal void DrawGeometry(StreamGeometry geo, int gen)
        {
            m_context.DrawGeometry(gen >= 3 ? m_Palettes[gen - 3].inducedGcBrush : m_Palettes[gen].gcBrush, null, geo);
        }

        private double m_maxHeap;
        private Pen m_thinBlack;
        private double m_y0, m_y1, m_yPixel;

        public void SetMemoryScale(double maxHeap)
        {
            m_maxHeap = maxHeap;
            m_y0 = m_yAxis;
            m_y1 = -(m_yAxis - margin) / m_maxHeap;
            m_yPixel = Math.Abs(1 / m_y1);

            if (m_thinBlack == null)
            {
                m_thinBlack = new Pen(Brushes.Black, 0.1);
            }

            int unit = 1;

            if (m_maxHeap >= 2500)
            {
                unit = 500;
            }
            else if (m_maxHeap >= 500)
            {
                unit = 100;
            }
            else if (m_maxHeap >= 250)
            {
                unit = 50;
            }
            else if (m_maxHeap >= 50)
            {
                unit = 10;
            }
            else if (m_maxHeap >= 25)
            {
                unit = 5;
            }

            for (int y = unit; y < m_maxHeap; y += unit)
            {
                m_context.DrawRectangle(Brushes.Black, null, new Rect(m_xAxis - 8, MapY(y) - 0.5, 8, 1));

                m_context.DrawLine(
                    (y % (unit * 5)) == 0 ? m_blackPen : m_thinBlack,
                    new Point(m_xAxis, MapY(y)), new Point(m_width - margin, MapY(y)));

                string lable = y.ToString();

                DrawText(m_xAxis - 20 - lable.Length * 5, MapY(y) - 5, lable);
            }
        }

        private void DrawText(double x, double y, string text, double size = 9)
        {
            m_context.DrawText(new FormattedText(text, Thread.CurrentThread.CurrentCulture, FlowDirection.LeftToRight, m_arial, size, Brushes.Black), new Point(x, y));
        }

        private double MapY(double y)
        {
            return m_y0 + y * m_y1;
        }

        private static double Diff(Point p0, Point p1)
        {
            return Math.Abs(p0.X - p1.X) + Math.Abs(p0.Y - p1.Y);
        }

        // x y0 y1 y2 ...
        public void DrawLines(List<double> line, int yCount, string[] curveLabels = null)
        {
            int stride = yCount + 1;

            List<Tuple<Point, string, Pen>> labels = new List<Tuple<Point, string, Pen>>(yCount);

            // Draw the lines one by one
            for (int j = 0; j < yCount; j++)
            {
                StreamGeometry geometry = new StreamGeometry();

                StreamGeometryContext ctx = geometry.Open();

                Point p = new Point();
                Point p0 = new Point();
                double alloc = 0;

                for (int i = 0; i < line.Count; i += stride)
                {
                    p = new Point(MapX(line[i]), MapY(alloc = line[i + 1 + j]));

                    if (i == 0)
                    {
                        ctx.BeginFigure(p, false, false);
                        p0 = p;
                    }
                    else
                    {
                        if (Diff(p, p0) >= 0.1) // 1 pixel in 960 dpi
                        {
                            ctx.LineTo(p, true, true);
                            p0 = p;
                        }
                    }
                }

                ctx.Close();

                ColorScheme pal = m_Palettes[j];

                geometry.Freeze();

                m_context.DrawGeometry(null, curveLabels == null ? pal.memoryPen2 : pal.memoryPen1, geometry);

                labels.Add(new Tuple<Point, string, Pen>(
                    p,
                    String.Format("{0} {1:N3} mb", curveLabels == null ? pal.label : curveLabels[j], alloc),
                    pal.memoryPen2));
            }

            double y = m_yAxis - 8;

            foreach (var v in labels.OrderByDescending(v => v.Item1.Y))
            {
                if (v.Item1.Y < y)
                {
                    y = v.Item1.Y;
                }

                m_context.DrawLine(v.Item3, new Point(v.Item1.X + 3, v.Item1.Y), new Point(v.Item1.X + 8, y));

                DrawText(v.Item1.X + 10, y - 5, v.Item2, 10);

                y -= 10;
            }
        }

        public void DrawAlloc(int y, double alloc, double ratio, string key, string method)
        {
            int yy = m_yAxis + 20 + y * 9;

            DrawRect(m_Palettes[y % 4].budgetBrush, m_xAxis, yy, MapY(0) - MapY(alloc), 10);

            DrawText(m_xAxis + 5, yy, ratio.ToString("N1") + "% " + alloc.ToString("N3") + " mb");
            DrawText(m_xAxis + 200, yy, key + " " + method);
        }

        public void DrawMarkers(int y, List<HeapEventData> events)
        {
            int count = events.Count;

            Brush brush1 = Brushes.Blue;
            Brush brush2 = Brushes.Brown;

            for (int i = 0; i < count; i++)
            {
                HeapEventData data = events[i];

                double t = data.m_time;

                if (t < m_barT0)
                {
                    continue;
                }

                if (t > m_barT1)
                {
                    break;
                }

                if ((data.m_event >= HeapEvents.GCMarkerFirst) && (data.m_event <= HeapEvents.GCMarkerLast))
                {
                    DrawRect(brush1, MapX(t), y - 1, 1, 10);
                }
                else if ((data.m_event >= HeapEvents.BGCMarkerFirst) && (data.m_event <= HeapEvents.BGCMarkerLast))
                {
                    DrawRect(brush2, MapX(t), y - 1, 1, 10);
                }
            }
        }

        private double m_barY;
        private double m_barH;
        private double m_barT0;
        private double m_barT1;

        /// <summary>
        /// Set horizontal bar region
        /// </summary>
        /// <param name="h">height</param>
        /// <param name="t0">minimum time stamp</param>
        /// <param name="t1">maximum time stamp</param>
        public void SetBarRegion(double h, double t0, double t1)
        {
            m_barY = 0;
            m_barH = h;
            m_barT0 = t0;
            m_barT1 = t1;
        }

        public void DrawBar(double start, double end, Brush brush)
        {
            if ((start < m_barT1) && (end > m_barT0))
            {
                DrawRect(brush, MapX(start), m_barY, MapXDelta(end - start), m_barH);
            }
        }

        public void DrawThread(int y, ThreadMemoryInfo thread, bool drawLegend, bool drawMarker)
        {
            List<HeapEventData> events = thread.m_events;

            int count = events.Count;

            m_barY = y;

            if (count > 1)
            {
                double start = events[0].m_time;
                double end = events[count - 1].m_time;

                if (drawLegend)
                {
                    string name = thread.Name;

                    if (!String.IsNullOrEmpty(name))
                    {
                        if (name.StartsWith(".Net ", StringComparison.OrdinalIgnoreCase))
                        {
                            name = name.Substring(5);
                        }
                    }

                    DrawText(5, y + 8, String.Format("{0}, {1}, {2} ms", thread.ThreadID, name, thread.CpuSample), 9);
                    return;
                }

                if ((start > m_barT1) || (end < m_barT0))
                {
                    return;
                }

                if (end > m_barT1)
                {
                    end = m_barT1;
                }

                DrawText(MapX(end) + 5, y,
                    String.Format("Thread {0}, {1}, {2} ms", thread.ThreadID, thread.Name, thread.CpuSample), 8);

                DrawRect(m_blue25, MapX(start), y, MapXDelta(end - start), m_barH);

                BarInterval cpuSample = new BarInterval(this, Brushes.Black);
                BarInterval contention = new BarInterval(this, Brushes.Red);
                BarInterval waitBgc = new BarInterval(this, Brushes.Yellow);

                double half = thread.SampleInterval / 2;

                for (int i = 0; i < count; i++)
                {
                    HeapEventData evt = events[i];

                    double tick = evt.m_time;

                    switch (evt.m_event)
                    {
                        case HeapEvents.CPUSample:
                            cpuSample.Start(tick - half);
                            cpuSample.End(tick + half);
                            break;

                        case HeapEvents.ContentionStart:
                            contention.Start(tick);
                            break;

                        case HeapEvents.ContentionStop:
                            contention.End(tick);
                            break;

                        case HeapEvents.BGCAllocWaitStart:
                            waitBgc.Start(tick);
                            break;

                        case HeapEvents.BGCAllocWaitStop:
                            waitBgc.End(tick);
                            break;

                        default:
                            break;
                    }
                }

                cpuSample.Draw();
                contention.Draw();
                waitBgc.Draw();

                if (drawMarker)
                {
                    DrawMarkers(y, events);
                }
            }
        }

        public Visual CloseDiagram(bool drawAxis)
        {
            if (m_context != null)
            {
                if (drawAxis)
                {
                    m_context.DrawLine(m_blackPen, new Point(m_xAxis, m_yAxis), new Point(m_xAxis, margin));
                    m_context.DrawLine(m_blackPen, new Point(m_xAxis, m_yAxis), new Point(m_width - margin, m_yAxis));
                }

                m_context.Close();

                m_context = null;
            }

            return m_visual;
        }
    }

    /// <summary>
    /// Diagram generation
    /// </summary>
    internal class HeapDiagramGenerator
    {
        private static double CheckMax(ref double max, double value)
        {
            if (value > max)
            {
                max = value;
            }

            return value;
        }

        private DiagramData m_data;
        private double m_t0;
        private double m_t1;

        public void RenderDiagram(int width, int height, DiagramData data)
        {
            m_data = data;

            Renderer render = new Renderer();

            render.Open(width, height, m_data.drawAllocTicks);

            m_t0 = m_data.startTime;
            m_t1 = m_data.endTime;

            render.SetTimeScale(m_t0, m_t1, out data.x0, out data.x1, data.drawGCEvents);

            if (data.drawGCEvents)
            {
                SetYScale(render);

                // GC events as vertical bars + heap size curves
                DrawGCEvents(render);
            }

            if (m_data.drawAllocTicks)
            {
                DrawAllocation(render);
            }

            if (m_data.drawThreadCount != 0)
            {
                DrawThreads(render);
            }

            data.visual = render.CloseDiagram(data.drawGCEvents);
        }

        private void SetYScale(Renderer render)
        {
            double maxHeap = m_data.vmMaxVM;

            foreach (TraceGC gc in m_data.events)
            {
                double g0 = gc.GenSizeBeforeMB[(int)Gens.Gen0];
                double g1 = gc.GenSizeBeforeMB[(int)Gens.Gen1];
                double g2 = gc.GenSizeBeforeMB[(int)Gens.Gen2];
                double g3 = gc.GenSizeBeforeMB[(int)Gens.GenLargeObj];

                CheckMax(ref maxHeap, g0 + g1 + g2 + g3);
            }

            render.SetMemoryScale(maxHeap);
        }

        // GC event marks + 
        // Heap size curves
        private void DrawGCEvents(Renderer render)
        {
            List<double> sizeCurves = new List<double>((m_data.events.Count * 2 + 1) * 6);

            // Process start, no managed heap
            sizeCurves.Add(0);
            sizeCurves.Add(0);
            sizeCurves.Add(0);
            sizeCurves.Add(0);
            sizeCurves.Add(0);
            sizeCurves.Add(0);

            double lastEnd = 0;

            GeometryBuilder[] gcBarList = new GeometryBuilder[6];

            foreach (TraceGC gc in m_data.events)
            {
                double start = gc.PauseStartRelativeMSec;
                double end = gc.PauseStartRelativeMSec + gc.PauseDurationMSec;

                if (end < m_t0)
                {
                    continue;
                }

                if (start > m_t1)
                {
                    break;
                }

                int gen = gc.Generation;

                double gcTime = gc.GetTotalGCTime();

                render.DrawGCEvent(
                    start, end, gen, lastEnd,
                    gc.GenBudgetMB(Gens.Gen0), gc.GenBudgetMB(Gens.GenLargeObj),
                    gcTime,
                    gc.IsInduced(), gcBarList);

                // double smallAloc  = gc.AllocedSinceLastGCBasedOnAllocTickMB[0];
                // double largeAlloc = gc.AllocedSinceLastGCBasedOnAllocTickMB[1];

                double g0 = gc.GenSizeBeforeMB[(int)Gens.Gen0];
                double g1 = gc.GenSizeBeforeMB[(int)Gens.Gen1];
                double g2 = gc.GenSizeBeforeMB[(int)Gens.Gen2];
                double g3 = gc.GenSizeBeforeMB[(int)Gens.GenLargeObj];

                sizeCurves.Add(start);
                sizeCurves.Add(g0);
                sizeCurves.Add(g1);
                sizeCurves.Add(g2);
                sizeCurves.Add(g3);
                sizeCurves.Add(g0 + g1 + g2 + g3);

                if (gc.HeapStats != null) // May not be complete, no HeapStats, assuming the same
                {
                    g0 = gc.GenSizeAfterMB(Gens.Gen0);
                    g1 = gc.GenSizeAfterMB(Gens.Gen1);
                    g2 = gc.GenSizeAfterMB(Gens.Gen2);
                    g3 = gc.GenSizeAfterMB(Gens.GenLargeObj);
                }

                sizeCurves.Add(end);
                sizeCurves.Add(g0);
                sizeCurves.Add(g1);
                sizeCurves.Add(g2);
                sizeCurves.Add(g3);
                sizeCurves.Add(g0 + g1 + g2 + g3);

                lastEnd = end;
            }

            for (int g = 0; g < 6; g++)
            {
                if (gcBarList[g] != null)
                {
                    StreamGeometry geo = gcBarList[g].Close();

                    render.DrawGeometry(geo, g);
                }
            }

            render.DrawLines(sizeCurves, 5);

            if (m_data.vmCurve != null)
            {
                string[] labels = new string[] {
                    "CLR VMC",
                    "+ Graphics VMC",
                    m_data.wwaHost? "+ Jscript VMC" : "+ Xaml VMC",
                    "Total VMC" };

                render.DrawLines(m_data.vmCurve, 4, labels);
            }
        }

        private void DrawThreads(Renderer render)
        {
            double totalSample = 0;

            foreach (ThreadMemoryInfo thread in m_data.threads.Values)
            {
                totalSample += thread.CpuSample;
            }

            double cutoff = totalSample / 1000; // 0.1 percent

            int y = 0;

            render.SetBarRegion(8, m_t0, m_t1);

            foreach (ThreadMemoryInfo thread in m_data.threads.Values.OrderBy(e => e, new ThreadMemoryInfoComparer()))
            {
                if (thread.CpuSample >= cutoff)
                {
                    render.DrawThread(y * 10 + 5, thread, m_data.drawLegend, m_data.drawMarker);
                    y++;

                    if (y >= m_data.drawThreadCount)
                    {
                        break;
                    }
                }
            }
        }

        public void DrawAllocation(Renderer render)
        {
            double total = 0;

            for (int i = 0; i < m_data.allocsites.Count; i++)
            {
                total += m_data.allocsites[i].Alloc;
            }

            double onePercent = total / 100;

            double other = 0;

            int y = 0;

            for (int i = 0; i < m_data.allocsites.Count; i++)
            {
                double alloc = m_data.allocsites[i].Alloc;

                if (alloc >= onePercent)
                {
                    AllocTick key = m_data.allocsites[i];

                    string method = m_data.dataFile.GetMethodName(key.m_caller1);

                    render.DrawAlloc(y, alloc, alloc * 100 / total, key.m_type, method);
                    y++;
                }
                else
                {
                    other += alloc;
                }
            }

            render.DrawAlloc(y, other, other * 100 / total, "Other", String.Empty);
        }
    }

    /// <summary>
    /// Metric for Metrics DataGrid
    /// </summary>
    public class Metric
    {
        private object m_value;
        private string m_format;

        public string Name { get; set; }

        public object Value
        {
            get
            {
                if ((m_value != null) && (m_value is double))
                {
                    return String.Format(m_format, m_value);
                }

                return m_value;
            }
        }

        public Metric(string name, object val, string format = null)
        {
            Name = name;
            m_value = val;
            m_format = format;
        }
    }

    /// <summary>
    /// HeapDiagram Panel
    /// </summary>
    public class HeapDiagram
    {
        private int TopPanelHeight = 31;
        private int LeftPanelWidth = 240;
        private int LegendWidth = 88;
        private PerfViewFile m_dataFile;
        private Window m_parent;
        private ProcessMemoryInfo m_heapInfo;
        private StatusBar m_statusBar;
        private List<Metric> m_metrics;

        public HeapDiagram(PerfViewFile dataFile, StatusBar status, Window parent)
        {
            m_dataFile = dataFile;
            m_statusBar = status;
            m_parent = parent;
        }

        private DockPanel m_topPanel;
        private DockPanel m_leftPanel;
        private CheckBox m_timeline;
        private CheckBox m_drawMarker;
        private ScrollViewer m_scrollViewer;
        private Label m_zoomLabel, m_posLabel;
        private VisualHolder m_diagramHolder;
        private Slider m_zoomSlider;

        //    Button m_testButton;

        private Button m_cropButton;
        private Button m_undoButton;
        private int m_graphWidth = 80 * 11;
        private int m_graphHeight = 80 * 5;
        private double m_widthZoom = 1;
        private RubberBandAdorner m_rubberBand;
        private double m_diagramT0;
        private double m_diagramT1;

        private void RedrawDiagram()
        {
            int zoomWidth = (int)(m_graphWidth * m_widthZoom);

            Stopwatch watch = new Stopwatch();

            watch.Start();

            int threadCount = 0;

            if (m_timeline.IsChecked == true)
            {
                threadCount = 30;
            }

            DiagramData data = m_heapInfo.RenderDiagram(zoomWidth, m_graphHeight, m_diagramT0, m_diagramT1,
                true, threadCount, m_drawMarker.IsChecked == true, false);

            if (m_rubberBand != null)
            {
                m_rubberBand.Detach();
            }

            m_diagramHolder = new VisualHolder();
            m_rubberBand = new RubberBandAdorner(m_diagramHolder, m_diagramHolder.AddMessage, CreateContextMenu);

            m_diagramHolder.SetVisual(zoomWidth, m_graphHeight, data.visual, m_widthZoom, m_zoomSlider.Value, data.x0, data.x1);

            m_scrollViewer.Content = m_diagramHolder;
            m_scrollViewer.MouseMove += OnMouseMove;

            {
                DiagramData legend = m_heapInfo.RenderLegend(LegendWidth, m_graphHeight, threadCount);

                VisualHolder legendHolder = new VisualHolder();
                legendHolder.SetVisual(LegendWidth, m_graphHeight, legend.visual, 1, 1, legend.x0, legend.x1);
                m_leftLegend.Children.Clear();
                m_leftLegend.Children.Add(legendHolder);
            }

            watch.Stop();

            m_statusBar.Log(String.Format("RadrawDiagram({0:N3} {1:N3}, {2}x{3} {4:N3} ms", m_diagramT0, m_diagramT1, zoomWidth, m_graphHeight, watch.Elapsed.TotalMilliseconds));
        }

        private void SetZoom(double zoom)
        {
            m_zoomLabel.Content = String.Format("x {0:N3}", zoom);
            m_widthZoom = zoom;

            RedrawDiagram();
        }

        private void ZoomValueChanged(object sender, RoutedPropertyChangedEventArgs<double> value)
        {
            SetZoom(value.NewValue);
        }

        private void ToggleTimeline(object sender, RoutedEventArgs e)
        {
            RedrawDiagram();

            m_drawMarker.IsEnabled = m_timeline.IsChecked == true;
        }

        private void ToggleDrawMarker(object sender, RoutedEventArgs e)
        {
            RedrawDiagram();
        }

        private int FindEvent(double tick, int start)
        {
            for (int i = start; i < m_heapInfo.GcEvents.Count; i++)
            {
                TraceGC gc = m_heapInfo.GcEvents[i];

                if (tick >= gc.PauseStartRelativeMSec)
                {
                    if (tick < gc.PauseStartRelativeMSec + gc.PauseDurationMSec)
                    {
                        return i;
                    }
                }
                else
                {
                    break;
                }
            }

            return -1;
        }

        private int m_lastEvent;

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!m_scrollViewer.IsMouseCaptured)
            {
                Point p = e.GetPosition(m_diagramHolder);

                double t = m_diagramHolder.GetValueX(p.X);

                int g1 = FindEvent(t, 0);

                if (g1 >= 0)
                {
                    int g2 = FindEvent(t, g1 + 1); // Over have overlapping GCs

                    if ((g1 + g2) != m_lastEvent)
                    {
                        string message = m_heapInfo.GcEvents[g1].GetTip();

                        if (g2 >= 0)
                        {
                            message = message + "\r\n" + m_heapInfo.GcEvents[g2].GetTip();
                        }

                        m_tip.Content = message;

                        m_tip.PlacementTarget = m_scrollViewer;
                        m_tip.Placement = PlacementMode.Relative;
                    }

                    p = e.GetPosition(m_scrollViewer);

                    m_tip.IsOpen = true;
                    m_tip.HorizontalOffset = p.X + 11;
                    m_tip.VerticalOffset = p.Y + 18;

                    m_lastEvent = g1 + g2;
                }
                else
                {
                    m_tip.IsOpen = false;
                }

                string tip = String.Format("{0:N3} ms", t);

                m_posLabel.Content = tip;
            }
        }

        private bool m_allowResize;

        private void OnSizeChange(object sender, SizeChangedEventArgs e)
        {
            if (m_allowResize)
            {
                // m_testButton.Content = e.NewSize; // String.Format("{0} x {1}  ", width, height);

                int width = (int)Math.Round(e.NewSize.Width) - 32;
                int height = (int)Math.Round(e.NewSize.Height) - 32;

                if ((width != m_graphWidth) || (height != m_graphHeight))
                {
                    if ((width > 0) && (height > 0))
                    {
                        m_graphWidth = width;
                        m_graphHeight = height;

                        RedrawDiagram();
                    }
                }
            }
        }

        private ToolTip m_tip;
        private DataGrid m_metricGrid;
        private StackPanel m_leftLegend;
        private const double MaxZoom = 100;

        internal Panel CreateHeapDiagramPanel(TextBox helpBox)
        {
            // Top: controls
            m_topPanel = new DockPanel();
            {
                m_topPanel.Height = TopPanelHeight;
                m_topPanel.Background = Brushes.LightGray;

                m_topPanel.DockLeft(m_timeline = Toolbox.CreateCheckBox(false, "Timeline", 10, 5, ToggleTimeline));
                m_timeline.ToolTip = "Overlay thread time line.";

                m_topPanel.DockLeft(m_drawMarker = Toolbox.CreateCheckBox(false, "Marker", 10, 5, ToggleDrawMarker));
                m_drawMarker.ToolTip = "Overlay GC marking events on thread time line.";
                m_drawMarker.IsEnabled = false;

                m_zoomSlider = new Slider();
                m_zoomSlider.Margin = new Thickness(10, 2, 10, 2);
                m_zoomSlider.Minimum = 1;
                m_zoomSlider.Maximum = MaxZoom;
                m_zoomSlider.Value = 1;
                m_zoomSlider.Width = 200;
                m_zoomSlider.Ticks = new DoubleCollection(new double[] { 1, 2, 4, 8, 10, 16, 32, 50, 64, MaxZoom });
                m_zoomSlider.TickPlacement = TickPlacement.BottomRight;
                m_zoomSlider.ValueChanged += ZoomValueChanged;
                m_zoomSlider.ToolTip = "Change time axis zoom ratio.";
                m_topPanel.DockLeft(m_zoomSlider);

                m_zoomLabel = new Label();
                m_zoomLabel.Content = "x 1";
                m_zoomLabel.Margin = new Thickness(10, 5, 10, 5);
                m_topPanel.DockLeft(m_zoomLabel);

                m_posLabel = new Label();
                m_topPanel.DockLeft(m_posLabel);

                // m_testButton = Toolbox.CreateButton("Test", 50, OnTest, 5, 5);
                // m_topPanel.DockLeft(m_testButton);

                m_cropButton = Toolbox.CreateButton("Crop", 38, OnCropDiagram, 5, 5);
                m_cropButton.ToolTip = "Crop diagram to current displayed time range.";

                m_undoButton = Toolbox.CreateButton("Undo", 38, OnUndoCrop, 5, 5);
                m_undoButton.ToolTip = "Restore last time range.";
                m_undoButton.IsEnabled = false;

                m_topPanel.DockRight(m_undoButton);
                m_topPanel.DockRight(m_cropButton);
            }

            // Left: DataGrid with Metrics, helpBox
            m_leftPanel = new DockPanel();
            {
                m_leftPanel.Width = LeftPanelWidth;
                m_leftPanel.Background = Brushes.LightGray;

                m_metricGrid = new DataGrid();
                m_metricGrid.Background = Brushes.LightGray;
                m_metricGrid.AutoGenerateColumns = false;
                m_metricGrid.IsReadOnly = true;

                m_metricGrid.AddColumn("Metric", "Name", false);
                m_metricGrid.AddColumn("Value", "Value", true);

                m_leftLegend = new StackPanel();
                m_leftLegend.Width = LegendWidth;

                m_leftPanel.DockRight(m_leftLegend);
                m_leftPanel.DockTop(m_metricGrid);
                m_leftPanel.DockTop(helpBox);
            }

            // Bottom-right: ScrollViewer with diagram
            m_scrollViewer = new ScrollViewer();
            {
                m_scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                m_scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                m_scrollViewer.SizeChanged += OnSizeChange;

                m_tip = new ToolTip();
                m_scrollViewer.ToolTip = m_tip;

                ToolTipService.SetInitialShowDelay(m_scrollViewer, 500);
                ToolTipService.SetShowDuration(m_scrollViewer, 1000);
            }

            DockPanel heapDiagram = Toolbox.DockTopLeft(m_topPanel, m_leftPanel, m_scrollViewer);

            return heapDiagram;
        }

        internal void SetData(ProcessMemoryInfo heapInfo)
        {
            m_heapInfo = heapInfo;

            m_diagramT0 = m_heapInfo.FirstEventTime;
            m_diagramT1 = m_heapInfo.LastEventTime;

            RedrawDiagram();

            m_metrics = m_heapInfo.GetMetrics();

            m_metricGrid.ItemsSource = m_metrics;

            m_allowResize = true;
        }

        private MenuItem m_zoomTo;
        private double m_rangeT0;
        private double m_rangeT1;

        /// <summary>
        /// Zoom to time range
        /// </summary>
        private void OnZoomTo(object sender, RoutedEventArgs e)
        {
            double range = Math.Abs(m_rangeT0 - m_rangeT1);

            if (range >= 0.1) // 0.1 ms
            {
                double t0 = m_diagramHolder.GetValueX(0);                      // Real start time displayed
                double t1 = m_diagramHolder.GetValueX(m_diagramHolder.Width);  // Real end time displayed

                double whole = t1 - t0;                                        // whole time range
                double zoom = whole / range;                                   // Ratio

                if (zoom >= MaxZoom)                                           // Limit at MaxZoom
                {
                    zoom = MaxZoom;
                }

                range = whole / zoom;                                          // Limited range
                double T0 = (m_rangeT0 + m_rangeT1) / 2 - range / 2;           // Adjusted t0 to display

                m_zoomSlider.Value = zoom;                                     // Update zoom

                double offset = (T0 - t0) / whole * zoom * m_graphWidth;       // Scroll offset

                m_statusBar.Status = String.Format("ZoomTo({0:N3} .. {1:N3})", m_rangeT0, m_rangeT1);

                m_scrollViewer.ScrollToHorizontalOffset(offset);
            }
        }

        private StackWindowHook m_cpuStack;
        private StackWindowHook m_allocStack;
        private StackWindowHook m_vmallocStack;

        /// <summary>
        /// Create/update ContextMenu for RubbeerBand adorner
        /// </summary>
        private bool CreateContextMenu(ContextMenu cm, Point start, Point end)
        {
            m_rangeT0 = m_diagramHolder.GetValueX(start.X);
            m_rangeT1 = m_diagramHolder.GetValueX(end.X);

            if (cm.Items.Count == 0)
            {
                m_zoomTo = new MenuItem();
                m_zoomTo.Click += OnZoomTo;

                cm.Items.Add(m_zoomTo);
            }

            m_zoomTo.Header = String.Format("Zoom to [{0:N3} ms .. {1:N3} ms]", m_rangeT0, m_rangeT1);

            if (m_cpuStack == null)
            {
                m_cpuStack = new StackWindowHook(m_dataFile, m_heapInfo.ProcessID, m_statusBar, "CPU", m_parent);
                m_allocStack = new StackWindowHook(m_dataFile, m_heapInfo.ProcessID, m_statusBar, "GC Heap Alloc Ignore Free (Coarse Sampling)", m_parent);

                if (m_heapInfo.HasVmAlloc)
                {
                    m_vmallocStack = new StackWindowHook(m_dataFile, m_heapInfo.ProcessID, m_statusBar, "Net Virtual Alloc", m_parent);
                }
            }

            m_cpuStack.AttachMenu(cm, m_rangeT0, m_rangeT1);
            m_allocStack.AttachMenu(cm, m_rangeT0, m_rangeT1);

            if (m_heapInfo.HasVmAlloc)
            {
                m_vmallocStack.AttachMenu(cm, m_rangeT0, m_rangeT1);
            }

            return true;
        }

        private class DiagramPara
        {
            internal double scrollOffset;
            internal double t0;
            internal double t1;
            internal double zoom;
        }

        private Stack<DiagramPara> m_cropList = new Stack<DiagramPara>();

        /// <summary>
        /// Crop diagram to displayed time range, for performance, bigger zoom range
        /// </summary>
        private void OnCropDiagram(object sender, RoutedEventArgs e)
        {
            DiagramPara para = new DiagramPara();

            para.scrollOffset = m_scrollViewer.HorizontalOffset;
            para.zoom = m_zoomSlider.Value;
            para.t0 = m_diagramT0;
            para.t1 = m_diagramT1;

            m_cropList.Push(para);

            if (m_cropList.Count == 1)
            {
                m_undoButton.IsEnabled = true;
            }

            m_diagramT0 = m_diagramHolder.GetValueX(para.scrollOffset);                                      // Real start time displayed
            m_diagramT1 = m_diagramHolder.GetValueX(para.scrollOffset + m_diagramHolder.Width / para.zoom);  // Real end time displayed

            m_zoomSlider.Value = 1;                                          // Update zoom to 1

            m_scrollViewer.ScrollToHorizontalOffset(0);

            m_statusBar.Status = String.Format("CropTo({0:N3} .. {1:N3})", m_diagramT0, m_diagramT1);
        }

        /// <summary>
        /// Undo Crop
        /// </summary>
        private void OnUndoCrop(object sender, RoutedEventArgs e)
        {
            if (m_cropList.Count != 0)
            {
                DiagramPara para = m_cropList.Pop();

                m_diagramT0 = para.t0;
                m_diagramT1 = para.t1;
                m_zoomSlider.Value = para.zoom;
                m_scrollViewer.ScrollToHorizontalOffset(para.scrollOffset);
            }

            if (m_cropList.Count == 0)
            {
                m_undoButton.IsEnabled = false;
            }
        }

        private void OnTest(object sender, RoutedEventArgs e)
        {
        }


    }

}
