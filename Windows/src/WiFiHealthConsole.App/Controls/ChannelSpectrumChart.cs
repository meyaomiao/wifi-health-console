using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using WiFiHealthConsole.App.ViewModels;

namespace WiFiHealthConsole.App.Controls;

public sealed class ChannelSpectrumChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<ChannelNetworkViewModel>?> NetworksProperty =
        AvaloniaProperty.Register<ChannelSpectrumChart, IReadOnlyList<ChannelNetworkViewModel>?>(nameof(Networks));

    public static readonly StyledProperty<string> BandProperty =
        AvaloniaProperty.Register<ChannelSpectrumChart, string>(nameof(Band), "5 GHz");

    public static readonly StyledProperty<int> RevisionProperty =
        AvaloniaProperty.Register<ChannelSpectrumChart, int>(nameof(Revision));

    static ChannelSpectrumChart()
    {
        AffectsRender<ChannelSpectrumChart>(NetworksProperty, BandProperty, RevisionProperty);
    }

    public IReadOnlyList<ChannelNetworkViewModel>? Networks
    {
        get => GetValue(NetworksProperty);
        set => SetValue(NetworksProperty, value);
    }

    public string Band
    {
        get => GetValue(BandProperty);
        set => SetValue(BandProperty, value);
    }

    public int Revision
    {
        get => GetValue(RevisionProperty);
        set => SetValue(RevisionProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var networks = Networks ?? [];
        if (Band == "总览")
        {
            RenderOverview(context, networks);
            return;
        }

        var plot = new Rect(52, 26, Math.Max(0, Bounds.Width - 70), Math.Max(0, Bounds.Height - 66));
        RenderBand(context, Band, plot, networks, maximumLabels: 10, showAxisTitles: true);
    }

    private void RenderOverview(
        DrawingContext context,
        IReadOnlyList<ChannelNetworkViewModel> networks)
    {
        const double left = 68;
        const double right = 18;
        const double top = 12;
        const double bottom = 10;
        const double gap = 12;
        var availableHeight = Math.Max(0, Bounds.Height - top - bottom - gap * 2);
        var rowHeight = availableHeight / 3;
        if (Bounds.Width - left - right <= 0 || rowHeight <= 42) return;

        var labelBrush = new SolidColorBrush(Color.Parse("#AAB2B7"));
        var bands = new[] { "2.4 GHz", "5 GHz", "6 GHz" };
        for (var index = 0; index < bands.Length; index++)
        {
            var band = bands[index];
            var rowTop = top + index * (rowHeight + gap);
            DrawText(context, band, new Point(8, rowTop + 4), labelBrush, 11);
            var plot = new Rect(
                left,
                rowTop + 4,
                Math.Max(0, Bounds.Width - left - right),
                Math.Max(0, rowHeight - 26));
            RenderBand(context, band, plot, networks, maximumLabels: 6, showAxisTitles: false);
        }
    }

    private static void RenderBand(
        DrawingContext context,
        string band,
        Rect plot,
        IReadOnlyList<ChannelNetworkViewModel> networks,
        int maximumLabels,
        bool showAxisTitles)
    {
        if (plot.Width <= 0 || plot.Height <= 0) return;

        var gridPen = new Pen(new SolidColorBrush(Color.Parse("#344047")), 1);
        var warningPen = new Pen(new SolidColorBrush(Color.Parse("#7A5730")), 1, dashStyle: new DashStyle([5, 5], 0));
        var goodPen = new Pen(new SolidColorBrush(Color.Parse("#28613F")), 1, dashStyle: new DashStyle([5, 5], 0));
        var labelBrush = new SolidColorBrush(Color.Parse("#8E979D"));

        foreach (var rssi in new[] { -30, -55, -67, -80, -95 })
        {
            var y = MapRssi(rssi, plot);
            var pen = rssi switch
            {
                -55 => goodPen,
                -67 => warningPen,
                _ => gridPen,
            };
            context.DrawLine(pen, new Point(plot.Left, y), new Point(plot.Right, y));
            DrawText(
                context,
                rssi.ToString(CultureInfo.InvariantCulture),
                new Point(Math.Max(4, plot.Left - 40), y - 7),
                labelBrush,
                showAxisTitles ? 10 : 8.5);
        }

        var (minimumChannel, maximumChannel) = BandRange(band);
        foreach (var channel in ChannelTicks(band))
        {
            var x = MapChannel(channel, minimumChannel, maximumChannel, plot);
            context.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            DrawCenteredText(
                context,
                channel.ToString(CultureInfo.InvariantCulture),
                x,
                plot.Bottom + 5,
                labelBrush,
                showAxisTitles ? 10 : 8.5);
        }

        if (showAxisTitles)
        {
            DrawText(context, "RSSI dBm", new Point(8, 5), labelBrush, 10.5);
            DrawText(context, "信道", new Point(plot.Right - 22, plot.Bottom + 25), labelBrush, 10.5);
        }

        var visible = networks
            .Where(network => network.Band == band)
            .OrderBy(network => network.IsCurrent ? 1 : 0)
            .ToArray();
        foreach (var network in visible)
        {
            DrawNetwork(context, plot, band, network);
        }

        var occupiedLabels = new List<Rect>();
        foreach (var network in visible
                     .OrderByDescending(network => network.IsCurrent)
                     .ThenByDescending(network => network.Rssi)
                     .Take(maximumLabels))
        {
            DrawNetworkLabel(context, plot, band, network, occupiedLabels, showAxisTitles);
        }
    }

    private static void DrawNetwork(
        DrawingContext context,
        Rect plot,
        string band,
        ChannelNetworkViewModel network)
    {
        var (minimumChannel, maximumChannel) = BandRange(band);

        var width = network.WidthMHz ?? 0;
        var halfSpan = width <= 0 ? 0.8 : band == "2.4 GHz" ? Math.Max(1, width / 10d) : Math.Max(2, width / 10d);
        var center = MapChannel(network.Channel, minimumChannel, maximumChannel, plot);
        var left = MapChannel(network.Channel - halfSpan, minimumChannel, maximumChannel, plot);
        var right = MapChannel(network.Channel + halfSpan, minimumChannel, maximumChannel, plot);
        var peak = MapRssi(network.Rssi, plot);
        var baseline = plot.Bottom;
        var color = Color.Parse(network.Color);
        var stroke = new SolidColorBrush(color);
        var fill = new SolidColorBrush(Color.FromArgb(network.IsCurrent ? (byte)54 : (byte)34, color.R, color.G, color.B));

        if (network.WidthMHz is null)
        {
            context.DrawLine(new Pen(stroke, network.IsCurrent ? 3 : 2), new Point(center, peak), new Point(center, baseline));
            return;
        }

        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(new Point(left, baseline), true);
            path.CubicBezierTo(
                new Point(left + (center - left) * .56, baseline),
                new Point(center - (center - left) * .38, peak),
                new Point(center, peak));
            path.CubicBezierTo(
                new Point(center + (right - center) * .38, peak),
                new Point(right - (right - center) * .56, baseline),
                new Point(right, baseline));
            path.EndFigure(true);
        }
        context.DrawGeometry(fill, new Pen(stroke, network.IsCurrent ? 2.8 : 1.8, lineCap: PenLineCap.Round), geometry);
    }

    private static void DrawNetworkLabel(
        DrawingContext context,
        Rect plot,
        string band,
        ChannelNetworkViewModel network,
        ICollection<Rect> occupiedLabels,
        bool fullSize)
    {
        var (minimumChannel, maximumChannel) = BandRange(band);
        var center = MapChannel(network.Channel, minimumChannel, maximumChannel, plot);
        var peak = MapRssi(network.Rssi, plot);
        var maximumSsidLength = fullSize ? 13 : 10;
        var shortSsid = network.Ssid.Length > maximumSsidLength
            ? $"{network.Ssid[..(maximumSsidLength - 1)]}…"
            : network.Ssid;
        var label = network.IsCurrent
            ? $"{shortSsid} · 当前"
            : $"{shortSsid} · Ch {network.Channel}";
        var brush = new SolidColorBrush(Color.Parse(network.Color));
        var fontSize = fullSize
            ? network.IsCurrent ? 11 : 10.5
            : network.IsCurrent ? 9.5 : 9;
        var text = CreateText(label, brush, fontSize);
        var origin = FindLabelOrigin(plot, center, peak, text.Width, text.Height, occupiedLabels);
        if (origin is null && !network.IsCurrent)
        {
            return;
        }

        var fallback = new Point(
            Math.Clamp(center - text.Width / 2, plot.Left + 2, Math.Max(plot.Left + 2, plot.Right - text.Width - 2)),
            Math.Clamp(peak - text.Height - 5, plot.Top + 2, Math.Max(plot.Top + 2, plot.Bottom - text.Height - 2)));
        var resolved = origin ?? fallback;
        occupiedLabels.Add(new Rect(resolved, new Size(text.Width, text.Height)));
        context.DrawText(text, resolved);
    }

    private static Point? FindLabelOrigin(
        Rect plot,
        double center,
        double peak,
        double width,
        double height,
        IEnumerable<Rect> occupiedLabels)
    {
        var baseX = Math.Clamp(
            center - width / 2,
            plot.Left + 2,
            Math.Max(plot.Left + 2, plot.Right - width - 2));
        var verticalStep = height + 3;
        for (var level = 0; level < 4; level++)
        {
            var candidatesY = new[]
            {
                peak - height - 5 - level * verticalStep,
                peak + 5 + level * verticalStep,
            };
            foreach (var candidateY in candidatesY)
            {
                if (candidateY < plot.Top + 2 || candidateY + height > plot.Bottom - 2)
                {
                    continue;
                }

                foreach (var horizontalOffset in new[] { 0d, -width * .58, width * .58 })
                {
                    var candidateX = Math.Clamp(
                        baseX + horizontalOffset,
                        plot.Left + 2,
                        Math.Max(plot.Left + 2, plot.Right - width - 2));
                    var candidate = new Rect(candidateX, candidateY, width, height);
                    if (occupiedLabels.All(existing => !Overlaps(existing, candidate)))
                    {
                        return candidate.Position;
                    }
                }
            }
        }

        return null;
    }

    private static bool Overlaps(Rect first, Rect second) =>
        first.Left < second.Right
        && first.Right > second.Left
        && first.Top < second.Bottom
        && first.Bottom > second.Top;

    private static (double minimum, double maximum) BandRange(string band) => band switch
    {
        "2.4 GHz" => (1d, 14d),
        "6 GHz" => (1d, 233d),
        _ => (32d, 177d),
    };

    private static IReadOnlyList<int> ChannelTicks(string band) => band switch
    {
        "2.4 GHz" => [1, 3, 6, 9, 11, 14],
        "6 GHz" => [1, 33, 65, 97, 129, 161, 193, 225],
        _ => [36, 44, 52, 64, 100, 116, 132, 149, 165, 177],
    };

    private static void DrawText(
        DrawingContext context,
        string value,
        Point origin,
        IBrush brush,
        double fontSize) =>
        context.DrawText(CreateText(value, brush, fontSize), origin);

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

    private static double MapChannel(double channel, double minimum, double maximum, Rect plot)
        => plot.Left + Math.Clamp((channel - minimum) / (maximum - minimum), 0, 1) * plot.Width;

    private static double MapRssi(double rssi, Rect plot)
    {
        const double top = -30;
        const double bottom = -95;
        var normalized = Math.Clamp((rssi - bottom) / (top - bottom), 0, 1);
        return plot.Bottom - normalized * plot.Height;
    }
}
