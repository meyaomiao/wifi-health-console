using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using WiFiHealthConsole.App.ViewModels;

namespace WiFiHealthConsole.App.Controls;

public sealed class HistoryLineChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<HistoryPointViewModel>?> PointsProperty =
        AvaloniaProperty.Register<HistoryLineChart, IReadOnlyList<HistoryPointViewModel>?>(nameof(Points));

    public static readonly StyledProperty<int> RevisionProperty =
        AvaloniaProperty.Register<HistoryLineChart, int>(nameof(Revision));

    static HistoryLineChart()
    {
        AffectsRender<HistoryLineChart>(PointsProperty, RevisionProperty);
    }

    public IReadOnlyList<HistoryPointViewModel>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public int Revision
    {
        get => GetValue(RevisionProperty);
        set => SetValue(RevisionProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var plot = new Rect(46, 18, Math.Max(0, Bounds.Width - 60), Math.Max(0, Bounds.Height - 48));
        if (plot.Width <= 0 || plot.Height <= 0) return;

        var gridPen = new Pen(new SolidColorBrush(Color.Parse("#344047")), 1);
        var labelBrush = new SolidColorBrush(Color.Parse("#829098"));
        var goodPen = new Pen(new SolidColorBrush(Color.Parse("#28613F")), 1, dashStyle: new DashStyle([5, 5], 0));
        var warningPen = new Pen(new SolidColorBrush(Color.Parse("#7A5730")), 1, dashStyle: new DashStyle([5, 5], 0));
        var criticalPen = new Pen(new SolidColorBrush(Color.Parse("#704047")), 1, dashStyle: new DashStyle([5, 5], 0));
        foreach (var rssi in new[] { -30, -55, -67, -75, -90 })
        {
            var y = MapRssi(rssi, plot);
            var pen = rssi switch
            {
                -55 => goodPen,
                -67 => warningPen,
                -75 => criticalPen,
                _ => gridPen,
            };
            context.DrawLine(pen, new Point(plot.Left, y), new Point(plot.Right, y));
            DrawRightAlignedText(context, rssi.ToString(CultureInfo.InvariantCulture), plot.Left - 7, y - 7, labelBrush, 9.5);
        }

        var points = Points;
        if (points is null || points.Count < 2) return;
        var first = points[0].Timestamp;
        var duration = Math.Max(1, (points[^1].Timestamp - first).TotalSeconds);
        var mapped = points.Select(point => new Point(
            plot.Left + (point.Timestamp - first).TotalSeconds / duration * plot.Width,
            MapRssi(point.Rssi, plot))).ToArray();

        for (var index = 0; index <= 4; index++)
        {
            var x = plot.Left + plot.Width * index / 4d;
            context.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            var timestamp = first + TimeSpan.FromSeconds(duration * index / 4d);
            DrawCenteredText(context, timestamp.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentUICulture), x, plot.Bottom + 7, labelBrush, 9.5);
        }

        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(mapped[0], false);
            for (var index = 1; index < mapped.Length; index++)
            {
                var previous = mapped[index - 1];
                var current = mapped[index];
                var dx = (current.X - previous.X) / 3d;
                path.CubicBezierTo(new Point(previous.X + dx, previous.Y), new Point(current.X - dx, current.Y), current);
            }
            path.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(UiPalette.Accent, 2.2, lineCap: PenLineCap.Round), geometry);

        for (var index = 0; index < points.Count; index++)
        {
            if (string.IsNullOrEmpty(points[index].Marker) || points[index].Marker == "未标记") continue;
            var color = points[index].Marker == "变更后" ? UiPalette.Good : UiPalette.Warning;
            context.DrawEllipse(color, null, mapped[index], 4.5, 4.5);
            context.DrawLine(new Pen(color, 1, dashStyle: new DashStyle([4, 4], 0)), new Point(mapped[index].X, plot.Top), new Point(mapped[index].X, plot.Bottom));
        }
    }

    private static double MapRssi(double rssi, Rect plot)
    {
        const double top = -30;
        const double bottom = -90;
        var normalized = Math.Clamp((rssi - bottom) / (top - bottom), 0, 1);
        return plot.Bottom - normalized * plot.Height;
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
}
