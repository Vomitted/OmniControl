using System.Windows;
using System.Windows.Media;
using UserControl = System.Windows.Controls.UserControl;
using Brushes = System.Windows.Media.Brushes;

namespace OmniHub.App.Wpf.Controls;

/// <summary>Rolling line chart: a fixed-size sample buffer redrawn on each new point.</summary>
public partial class WaveformChart : UserControl
{
    private const int MaxSamples = 60;
    private const int GridDivisions = 4;

    private readonly Queue<double> _samples = new();

    private Brush _lineBrush = Brushes.White;
    public Brush LineBrush
    {
        get => _lineBrush;
        set
        {
            _lineBrush = value;
            ApplyBrushes();
        }
    }

    public double MinValue { get; set; } = 0;
    public double MaxValue { get; set; } = 100;

    public WaveformChart()
    {
        InitializeComponent();
        ApplyBrushes();
    }

    /// <summary>
    /// Rebuilds the brushes derived from LineBrush. Done on assignment rather than on every
    /// redraw: the colour changes approximately never, the geometry changes every two seconds.
    /// </summary>
    private void ApplyBrushes()
    {
        if (StrokePath is null) return;

        StrokePath.Stroke = _lineBrush;
        TipDot.Fill = _lineBrush;

        var color = (_lineBrush as SolidColorBrush)?.Color ?? Colors.White;
        StrokeGlow.Color = color;

        var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        fill.GradientStops.Add(new GradientStop(Color.FromArgb(0x55, color.R, color.G, color.B), 0));
        fill.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, color.R, color.G, color.B), 1));
        fill.Freeze();
        FillPath.Fill = fill;
    }

    public void Push(double value)
    {
        _samples.Enqueue(value);
        while (_samples.Count > MaxSamples) _samples.Dequeue();
        Redraw();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        BuildGrid();
        Redraw();
    }

    /// <summary>
    /// Horizontal reference lines. Without them the trace is a shape with no scale, and the
    /// eye cannot tell a 5-degree wobble from a 40-degree climb.
    /// </summary>
    private void BuildGrid()
    {
        double w = Root.ActualWidth, h = Root.ActualHeight;
        if (w <= 0 || h <= 0) return;

        var geometry = new GeometryGroup();
        for (int i = 1; i < GridDivisions; i++)
        {
            double y = h * i / GridDivisions;
            geometry.Children.Add(new LineGeometry(new Point(0, y), new Point(w, y)));
        }
        geometry.Freeze();
        GridPath.Data = geometry;
    }

    private void Redraw()
    {
        double w = Root.ActualWidth, h = Root.ActualHeight;
        if (w <= 0 || h <= 0 || _samples.Count < 2)
        {
            StrokePath.Data = null;
            FillPath.Data = null;
            TipDot.Visibility = Visibility.Collapsed;
            return;
        }

        var arr = _samples.ToArray();
        double step = w / (MaxSamples - 1);
        double startX = w - (arr.Length - 1) * step;

        double Y(double v)
        {
            double range = Math.Max(1, MaxValue - MinValue);
            double t = Math.Clamp((v - MinValue) / range, 0, 1);
            return h - t * h;
        }

        var points = new Point[arr.Length];
        for (int i = 0; i < arr.Length; i++)
            points[i] = new Point(startX + i * step, Y(arr[i]));

        var figure = new PathFigure { StartPoint = points[0] };
        AppendSmoothed(figure, points);

        var stroke = new PathGeometry();
        stroke.Figures.Add(figure);
        StrokePath.Data = stroke;

        // Same curve, closed down to the baseline for the area fill. Cloned before the stroke
        // geometry is frozen, since the fill needs two extra segments on the end.
        var fillFigure = figure.Clone();
        fillFigure.Segments.Add(new LineSegment(new Point(points[^1].X, h), true));
        fillFigure.Segments.Add(new LineSegment(new Point(points[0].X, h), true));
        fillFigure.IsClosed = true;

        var fillGeometry = new PathGeometry();
        fillGeometry.Figures.Add(fillFigure);
        fillGeometry.Freeze();
        FillPath.Data = fillGeometry;

        stroke.Freeze();

        TipDot.Visibility = Visibility.Visible;
        TipDot.Margin = new Thickness(points[^1].X - 3.5, points[^1].Y - 3.5, 0, 0);
    }

    /// <summary>
    /// Draws the trace as a smooth curve rather than straight segments.
    ///
    /// A Catmull-Rom spline converted to cubic Beziers, with control points pulled in to a
    /// sixth of the span. That tension matters: a looser spline overshoots on sharp changes,
    /// which on a temperature chart would draw peaks the machine never actually reached. The
    /// smoothing is cosmetic, so it must not invent data.
    /// </summary>
    private static void AppendSmoothed(PathFigure figure, Point[] p)
    {
        for (int i = 0; i < p.Length - 1; i++)
        {
            Point p0 = i == 0 ? p[0] : p[i - 1];
            Point p1 = p[i];
            Point p2 = p[i + 1];
            Point p3 = i + 2 < p.Length ? p[i + 2] : p2;

            var c1 = new Point(p1.X + (p2.X - p0.X) / 6.0, p1.Y + (p2.Y - p0.Y) / 6.0);
            var c2 = new Point(p2.X - (p3.X - p1.X) / 6.0, p2.Y - (p3.Y - p1.Y) / 6.0);

            figure.Segments.Add(new BezierSegment(c1, c2, p2, true));
        }
    }
}
