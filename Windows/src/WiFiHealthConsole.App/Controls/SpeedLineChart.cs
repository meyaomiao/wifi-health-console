using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using WiFiHealthConsole.App.ViewModels;

namespace WiFiHealthConsole.App.Controls;

public sealed class SpeedLineChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<SpeedSampleViewModel>?> SamplesProperty =
        AvaloniaProperty.Register<SpeedLineChart, IReadOnlyList<SpeedSampleViewModel>?>(nameof(Samples));

    public static readonly StyledProperty<IBrush> LineBrushProperty =
        AvaloniaProperty.Register<SpeedLineChart, IBrush>(nameof(LineBrush), UiPalette.Accent);

    public static readonly StyledProperty<int> RevisionProperty =
        AvaloniaProperty.Register<SpeedLineChart, int>(nameof(Revision));

    public static readonly StyledProperty<double> MinimumMaximumProperty =
        AvaloniaProperty.Register<SpeedLineChart, double>(nameof(MinimumMaximum), 10d);

    public static readonly StyledProperty<double> MaximumSecondsProperty =
        AvaloniaProperty.Register<SpeedLineChart, double>(nameof(MaximumSeconds), 20d);

    static SpeedLineChart()
    {
        AffectsRender<SpeedLineChart>(SamplesProperty, LineBrushProperty, RevisionProperty, MinimumMaximumProperty, MaximumSecondsProperty);
    }

    public IReadOnlyList<SpeedSampleViewModel>? Samples
    {
        get => GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public IBrush LineBrush
    {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public int Revision
    {
        get => GetValue(RevisionProperty);
        set => SetValue(RevisionProperty, value);
    }

    public double MinimumMaximum
    {
        get => GetValue(MinimumMaximumProperty);
        set => SetValue(MinimumMaximumProperty, value);
    }

    public double MaximumSeconds
    {
        get => GetValue(MaximumSecondsProperty);
        set => SetValue(MaximumSecondsProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var plot = new Rect(45, 12, Math.Max(0, Bounds.Width - 58), Math.Max(0, Bounds.Height - 38));
        if (plot.Width <= 0 || plot.Height <= 0) return;

        var gridPen = new Pen(new SolidColorBrush(Color.Parse("#344047")), 1);
        var labelBrush = new SolidColorBrush(Color.Parse("#829098"));
        var samples = Samples;
        var observedMaximum = samples is { Count: > 0 }
            ? samples.Max(sample => sample.Mbps) * 1.18
            : MinimumMaximum;
        var maxMbps = NiceMaximum(Math.Max(MinimumMaximum, observedMaximum));
        var maxSeconds = Math.Max(1, Math.Max(MaximumSeconds, samples is { Count: > 0 } ? samples.Max(sample => sample.Seconds) : 0));

        for (var index = 0; index <= 4; index++)
        {
            var y = plot.Top + plot.Height * index / 4d;
            context.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var value = maxMbps * (4 - index) / 4d;
            DrawRightAlignedText(context, $"{value:0}", plot.Left - 7, y - 7, labelBrush, 9.5);
        }

        for (var index = 0; index <= 4; index++)
        {
            var x = plot.Left + plot.Width * index / 4d;
            context.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            DrawCenteredText(context, $"{maxSeconds * index / 4d:0}s", x, plot.Bottom + 6, labelBrush, 9.5);
        }

        if (samples is null || samples.Count < 2) return;

        var points = samples
            .Select(sample => new Point(
                plot.Left + sample.Seconds / maxSeconds * plot.Width,
                plot.Bottom - sample.Mbps / maxMbps * plot.Height))
            .ToArray();

        var fillColor = LineBrush is SolidColorBrush solid
            ? Color.FromArgb(36, solid.Color.R, solid.Color.G, solid.Color.B)
            : Color.FromArgb(26, 41, 151, 255);

        var fill = new StreamGeometry();
        using (var geometry = fill.Open())
        {
            geometry.BeginFigure(new Point(points[0].X, plot.Bottom), true);
            geometry.LineTo(points[0]);
            AppendSmoothCurve(geometry, points);
            geometry.LineTo(new Point(points[^1].X, plot.Bottom));
            geometry.EndFigure(true);
        }
        context.DrawGeometry(new SolidColorBrush(fillColor), null, fill);

        var line = new StreamGeometry();
        using (var geometry = line.Open())
        {
            geometry.BeginFigure(points[0], false);
            AppendSmoothCurve(geometry, points);
            geometry.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(LineBrush, 2.8, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), line);

        context.DrawEllipse(LineBrush, null, points[^1], 3.2, 3.2);
    }

    private static double NiceMaximum(double value)
    {
        if (value <= 0) return 10;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        var normalized = value / magnitude;
        var nice = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
        return nice * magnitude;
    }

    private static void DrawRightAlignedText(
        DrawingContext context,
        string value,
        double right,
        double top,
        IBrush brush,
        double fontSize)
    {
        var text = CreateText(value, brush, fontSize);
        context.DrawText(text, new Point(right - text.Width, top));
    }

    private static void DrawCenteredText(
        DrawingContext context,
        string value,
        double centerX,
        double top,
        IBrush brush,
        double fontSize)
    {
        var text = CreateText(value, brush, fontSize);
        context.DrawText(text, new Point(centerX - text.Width / 2, top));
    }

    private static FormattedText CreateText(string value, IBrush brush, double fontSize) =>
        new(
            value,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Normal, FontStretch.Normal),
            fontSize,
            brush);

    private static void AppendSmoothCurve(StreamGeometryContext geometry, IReadOnlyList<Point> points)
    {
        for (var index = 1; index < points.Count; index++)
        {
            var previous = points[index - 1];
            var current = points[index];
            var distance = (current.X - previous.X) / 3d;
            geometry.CubicBezierTo(
                new Point(previous.X + distance, previous.Y),
                new Point(current.X - distance, current.Y),
                current);
        }
    }
}
