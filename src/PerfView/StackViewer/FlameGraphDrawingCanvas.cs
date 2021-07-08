using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using static PerfView.FlameGraph;

namespace PerfView
{
    public class FlameGraphDrawingCanvas : Canvas
    {
        private static readonly Typeface Typeface = new Typeface("Consolas");

        private static readonly Brush[][] Brushes = GenerateBrushes(new Random(12345));

        public event EventHandler<string> CurrentFlameBoxChanged;

        private List<Visual> visuals = new List<Visual>();
        private FlameBoxesMap flameBoxesMap = new FlameBoxesMap();
        private ToolTip tooltip = new ToolTip() { FontSize = 20.0 };
        private ScaleTransform scaleTransform = new ScaleTransform(1.0f, 1.0f, 0.0f, 0.0f);
        private Cursor cursor;

        public FlameGraphDrawingCanvas()
        {
            MouseMove += OnMouseMove;
            MouseLeave += OnMouseLeave;
            PreviewMouseWheel += OnPreviewMouseWheel;
            MouseLeftButtonDown += OnMouseLeftButtonDown;
            MouseLeftButtonUp += OnMouseLeftButtonUp;
            PreviewKeyDown += OnPreviewKeyDown;
            Focusable = true;
        }

        public bool IsEmpty => visuals.Count == 0;

        protected override int VisualChildrenCount => visuals.Count;

        protected override Visual GetVisualChild(int index) => visuals[index];

        private bool IsZoomed => scaleTransform.ScaleX != 1.0;

        public void Draw(IEnumerable<FlameBox> boxes)
        {
            Clear();

            var visual = new DrawingVisual { Transform = scaleTransform }; // we have only one visual to provide best possible perf

            using (DrawingContext drawingContext = visual.RenderOpen())
            {
                int index = 0;
                System.Drawing.Font forSize = null;

                foreach (var box in boxes)
                {
                    var brush = Brushes[box.Node.InclusiveMetric < 0 ? 1 : 0][index++ % Brushes.Length]; // use second brush set (aqua theme) for negative metrics

                    drawingContext.DrawRectangle(
                        brush,
                        null,  // no Pen is crucial for performance
                        new Rect(box.X, box.Y, box.Width, box.Height));

                    if (box.Width * scaleTransform.ScaleX > 50 && box.Height * scaleTransform.ScaleY >= 6) // we draw the text only if humans can see something
                    {
                        if (forSize == null)
                        {
                            forSize = new System.Drawing.Font("Consolas", (float)box.Height, System.Drawing.GraphicsUnit.Pixel);
                        }

                        var text = new FormattedText(
                                box.Node.DisplayName,
                                CultureInfo.InvariantCulture,
                                FlowDirection.LeftToRight,
                                Typeface,
                                Math.Min(forSize.SizeInPoints, 20),
                                System.Windows.Media.Brushes.Black);

                        text.MaxTextWidth = box.Width;
                        text.MaxTextHeight = box.Height;

                        drawingContext.DrawText(text, new Point(box.X, box.Y));
                    }

                    flameBoxesMap.Add(box);
                }

                AddVisual(visual);

                flameBoxesMap.Sort();
            }
        }

        /// <summary>
        /// DrawingVisual provides no tooltip support, so I had to implement it myself.. I feel bad for it.
        /// </summary>
        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!IsEmpty && e.LeftButton == MouseButtonState.Released)
            {
                var position = scaleTransform.Inverse.Transform(Mouse.GetPosition(this));
                var tooltipText = flameBoxesMap.Find(position);
                if (tooltipText != null)
                {
                    ShowTooltip(tooltipText);
                    CurrentFlameBoxChanged(this, tooltipText);
                    return;
                }
            }
            else if (!IsEmpty && e.LeftButton == MouseButtonState.Pressed && IsZoomed)
            {
                var relativeMousePosition = scaleTransform.Inverse.Transform(Mouse.GetPosition(this));
                MoveZoomingCenterPoint(relativeMousePosition.X, relativeMousePosition.Y);
            }

            HideTooltip();
        }

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            HideTooltip();
            ResetCursor(); // leaving the control while still zooming and OnMouseLeftButtonUp won't fire
        }

        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            double modifier = e.Delta > 0 ? 1.1 : 0.9;

            var relativeMousePosition = scaleTransform.Inverse.Transform(Mouse.GetPosition(this));

            scaleTransform.ScaleX = Math.Max(1.0, scaleTransform.ScaleX * modifier);
            scaleTransform.ScaleY = Math.Max(1.0, scaleTransform.ScaleY * modifier);
            scaleTransform.CenterX = relativeMousePosition.X;
            scaleTransform.CenterY = relativeMousePosition.Y;

            Keyboard.Focus(this); // make it possible to handle Arrow keys and move CenterX & Y scaling points
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsZoomed)
            {
                cursor = Mouse.OverrideCursor;
                Mouse.OverrideCursor = Cursors.Hand; // emulate drag&drop cursor style
            }
        }

        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (IsZoomed)
            {
                ResetCursor();
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!IsZoomed)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.Left:
                    MoveZoomingCenterPoint(scaleTransform.CenterX * 0.9, scaleTransform.CenterY);
                    e.Handled = true;
                    break;
                case Key.Right:
                    MoveZoomingCenterPoint(scaleTransform.CenterX * 1.1, scaleTransform.CenterY);
                    e.Handled = true;
                    break;
                case Key.Up:
                    MoveZoomingCenterPoint(scaleTransform.CenterX, scaleTransform.CenterY * 0.9);
                    e.Handled = true;
                    break;
                case Key.Down:
                    MoveZoomingCenterPoint(scaleTransform.CenterX, scaleTransform.CenterY * 1.1);
                    e.Handled = true;
                    break;
                default:
                    break;
            }
        }

        private void ShowTooltip(string text)
        {
            if (object.ReferenceEquals(tooltip.Content, text) && tooltip.IsOpen)
            {
                return;
            }

            tooltip.IsOpen = false; // by closing and opening it again we restart it's position to the current mouse position..
            tooltip.Content = text;
            tooltip.Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse;
            tooltip.IsOpen = true;
            tooltip.PlacementTarget = this;
        }

        private void HideTooltip() => tooltip.IsOpen = false;

        private void Clear()
        {
            for (int i = visuals.Count - 1; i >= 0; i--)
            {
                DeleteVisual(visuals[i]);
                visuals.RemoveAt(i);
            }

            flameBoxesMap.Clear();
        }

        private void AddVisual(Visual visual)
        {
            visuals.Add(visual);

            base.AddVisualChild(visual);
            base.AddLogicalChild(visual);
        }

        private void DeleteVisual(Visual visual)
        {
            base.RemoveVisualChild(visual);
            base.RemoveLogicalChild(visual);
        }

        private void MoveZoomingCenterPoint(double x, double y)
        {
            if (IsZoomed)
            {
                scaleTransform.CenterX = Math.Min(x, ActualWidth);
                scaleTransform.CenterY = Math.Min(y, ActualHeight);
            }
        }

        private void ResetCursor() => Mouse.OverrideCursor = cursor;

        private static Brush[][] GenerateBrushes(Random random)
        {
            var brushes = new Brush[][]
            {
                Enumerable.Range(0, 100)
                    .Select(_ => (Brush)new SolidColorBrush(
                        Color.FromRgb(
                            (byte)(205.0 + 50.0 * random.NextDouble()),
                            (byte)(230.0 * random.NextDouble()),
                            (byte)(55.0 * random.NextDouble()))))
                    .ToArray(),
                Enumerable.Range(0, 100)
                    .Select(_ => (Brush)new SolidColorBrush(
                        Color.FromRgb(
                            (byte)(50 + 60.0 * random.NextDouble()),
                            (byte)(165 + 55.0 * random.NextDouble()),
                            (byte)(165.0 + 55.0 * random.NextDouble()))))
                    .ToArray()
            };

            foreach (var brushArray in brushes)
            {
                foreach (var brush in brushArray)
                {
                    brush.Freeze(); // this is crucial for performance
                }
            }

            return brushes;
        }

        private class FlameBoxesMap
        {
            private SortedDictionary<Range, List<FlameBox>> boxesMap = new SortedDictionary<Range, List<FlameBox>>();

            internal void Clear() => boxesMap.Clear();

            internal void Add(FlameBox flameBox)
            {
                var row = new Range(flameBox.Y, flameBox.Y + flameBox.Height);

                if (!boxesMap.TryGetValue(row, out var list))
                {
                    boxesMap.Add(row, list = new List<FlameBox>());
                }

                list.Add(flameBox);
            }

            internal void Sort()
            {
                foreach (var row in boxesMap.Values)
                {
                    row.Sort(CompareByX); // sort the boxes from left to the right
                }
            }

            internal string Find(Point point)
            {
                foreach (var rowData in boxesMap)
                {
                    if (rowData.Key.Contains(point.Y))
                    {
                        int low = 0, high = rowData.Value.Count - 1, mid = 0;

                        while (low <= high)
                        {
                            mid = (low + high) / 2;

                            if (rowData.Value[mid].X > point.X)
                            {
                                high = mid - 1;
                            }
                            else if ((rowData.Value[mid].X + rowData.Value[mid].Width) < point.X)
                            {
                                low = mid + 1;
                            }
                            else
                            {
                                break;
                            }
                        }

                        if (rowData.Value[mid].X <= point.X && point.X <= (rowData.Value[mid].X + rowData.Value[mid].Width))
                        {
                            return rowData.Value[mid].TooltipText;
                        }

                        return null;
                    }
                }

                return null;
            }

            private static int CompareByX(FlameBox left, FlameBox right) => left.X.CompareTo(right.X);

            private struct Range : IEquatable<Range>, IComparable<Range>
            {
                private readonly double Start, End;

                internal Range(double start, double end)
                {
                    Start = start;
                    End = end;
                }

                internal bool Contains(double y) => Start <= y && y <= End;

                public override bool Equals(object obj) => throw new InvalidOperationException("No boxing");

                public bool Equals(Range other) => other.Start == Start && other.End == End;

                public int CompareTo(Range other) => other.Start.CompareTo(Start);

                public override int GetHashCode() => (Start * End).GetHashCode();
            }
        }
    }
}
