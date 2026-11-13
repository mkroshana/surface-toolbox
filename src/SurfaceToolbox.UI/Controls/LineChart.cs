using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using FlowDirection = System.Windows.FlowDirection;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace SurfaceToolbox.UI.Controls
{
    /// <summary>
    /// A small line chart: one series, three labelled grid lines (lowest, middle, highest), and
    /// nothing else. The vertical range follows the data, but never gets narrower than
    /// <see cref="MinimumRange"/>, so sensor noise on a steady value is not blown up into drama.
    /// </summary>
    public class LineChart : FrameworkElement
    {
        private const double LabelWidth = 46;
        private const double VerticalPadding = 8;

        public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
            nameof(Values), typeof(IReadOnlyList<double>), typeof(LineChart),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
            nameof(Stroke), typeof(Brush), typeof(LineChart),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
            nameof(GridBrush), typeof(Brush), typeof(LineChart),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty LabelBrushProperty = DependencyProperty.Register(
            nameof(LabelBrush), typeof(Brush), typeof(LineChart),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty MinimumRangeProperty = DependencyProperty.Register(
            nameof(MinimumRange), typeof(double), typeof(LineChart),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ValueFormatProperty = DependencyProperty.Register(
            nameof(ValueFormat), typeof(string), typeof(LineChart),
            new FrameworkPropertyMetadata("0", FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>The series to draw, oldest first.</summary>
        public IReadOnlyList<double>? Values
        {
            get => (IReadOnlyList<double>?)GetValue(ValuesProperty);
            set => SetValue(ValuesProperty, value);
        }

        public Brush Stroke
        {
            get => (Brush)GetValue(StrokeProperty);
            set => SetValue(StrokeProperty, value);
        }

        public Brush GridBrush
        {
            get => (Brush)GetValue(GridBrushProperty);
            set => SetValue(GridBrushProperty, value);
        }

        public Brush LabelBrush
        {
            get => (Brush)GetValue(LabelBrushProperty);
            set => SetValue(LabelBrushProperty, value);
        }

        /// <summary>The smallest vertical span the chart will show, in the series' own units.</summary>
        public double MinimumRange
        {
            get => (double)GetValue(MinimumRangeProperty);
            set => SetValue(MinimumRangeProperty, value);
        }

        /// <summary>.NET numeric format for the axis labels, for example "0" or "0.0".</summary>
        public string ValueFormat
        {
            get => (string)GetValue(ValueFormatProperty);
            set => SetValue(ValueFormatProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double width = ActualWidth;
            double height = ActualHeight;
            if (width <= LabelWidth + 4 || height <= VerticalPadding * 2 + 4)
                return;

            double left = LabelWidth;
            double right = width - 2;
            double top = VerticalPadding;
            double bottom = height - VerticalPadding;
            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var typeface = new Typeface("Segoe UI");

            IReadOnlyList<double>? values = Values;
            if (values == null || values.Count == 0)
            {
                var empty = new FormattedText("No readings yet", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, 11, LabelBrush, pixelsPerDip);
                dc.DrawText(empty, new Point(left + (right - left - empty.Width) / 2, top + (bottom - top - empty.Height) / 2));
                return;
            }

            double min = values.Min();
            double max = values.Max();
            double minimumRange = MinimumRange > 0 ? MinimumRange : 1.0;
            if (max - min < minimumRange)
            {
                bool neverNegative = min >= 0;
                double middle = (max + min) / 2;
                min = middle - minimumRange / 2;
                max = middle + minimumRange / 2;

                // A series that is never negative (fan speed, load) should not get a negative axis.
                if (neverNegative && min < 0)
                {
                    max -= min;
                    min = 0;
                }
            }

            // Three grid lines with their values: highest at the top, lowest at the bottom.
            var gridPen = new Pen(GridBrush, 1);
            gridPen.Freeze();
            for (int line = 0; line < 3; line++)
            {
                double fraction = line / 2.0;
                double y = top + fraction * (bottom - top);
                dc.DrawLine(gridPen, new Point(left, y), new Point(right, y));

                double labelValue = max - fraction * (max - min);
                var label = new FormattedText(labelValue.ToString(ValueFormat, CultureInfo.CurrentCulture), CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight, typeface, 10, LabelBrush, pixelsPerDip);
                dc.DrawText(label, new Point(left - 6 - label.Width, y - label.Height / 2));
            }

            double ToY(double value) => bottom - (value - min) / (max - min) * (bottom - top);

            if (values.Count == 1)
            {
                dc.DrawEllipse(Stroke, null, new Point(right, ToY(values[0])), 2, 2);
                return;
            }

            var geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                double step = (right - left) / (values.Count - 1);
                context.BeginFigure(new Point(left, ToY(values[0])), false, false);
                for (int i = 1; i < values.Count; i++)
                    context.LineTo(new Point(left + i * step, ToY(values[i])), true, true);
            }
            geometry.Freeze();

            var linePen = new Pen(Stroke, 1.6) { LineJoin = PenLineJoin.Round };
            linePen.Freeze();
            dc.DrawGeometry(null, linePen, geometry);
        }
    }
}
