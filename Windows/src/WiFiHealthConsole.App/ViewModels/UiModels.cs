using Avalonia.Media;
using FluentIcons.Common;
using WiFiHealthConsole.Core;

namespace WiFiHealthConsole.App.ViewModels;

public enum AppSection
{
    Overview,
    Diagnosis,
    SpeedTest,
    ChannelRadar,
    History,
    Router
}

public enum StatusTone
{
    Good,
    Warning,
    Critical,
    Reference,
    Accent
}

public static class UiPalette
{
    private static readonly Color GoodColor = Color.Parse("#28D760");
    private static readonly Color WarningColor = Color.Parse("#F59A36");
    private static readonly Color CriticalColor = Color.Parse("#FF5D62");
    private static readonly Color ReferenceColor = Color.Parse("#A7ADB2");
    private static readonly Color AccentColor = Color.Parse("#2997FF");

    public static readonly IBrush Good = new SolidColorBrush(GoodColor);
    public static readonly IBrush GoodSoft = new SolidColorBrush(Color.Parse("#173C2A"));
    public static readonly IBrush Warning = new SolidColorBrush(WarningColor);
    public static readonly IBrush WarningSoft = new SolidColorBrush(Color.Parse("#46331F"));
    public static readonly IBrush Critical = new SolidColorBrush(CriticalColor);
    public static readonly IBrush CriticalSoft = new SolidColorBrush(Color.Parse("#47262A"));
    public static readonly IBrush Reference = new SolidColorBrush(ReferenceColor);
    public static readonly IBrush ReferenceSoft = new SolidColorBrush(Color.Parse("#373D41"));
    public static readonly IBrush Accent = new SolidColorBrush(AccentColor);
    public static readonly IBrush AccentSoft = new SolidColorBrush(Color.Parse("#173B5B"));

    public static (IBrush foreground, IBrush background) For(StatusTone tone) => tone switch
    {
        StatusTone.Good => (Good, GoodSoft),
        StatusTone.Warning => (Warning, WarningSoft),
        StatusTone.Critical => (Critical, CriticalSoft),
        StatusTone.Accent => (Accent, AccentSoft),
        _ => (Reference, ReferenceSoft)
    };

    public static IBrush WithAlpha(StatusTone tone, byte alpha)
    {
        var color = tone switch
        {
            StatusTone.Good => GoodColor,
            StatusTone.Warning => WarningColor,
            StatusTone.Critical => CriticalColor,
            StatusTone.Accent => AccentColor,
            _ => ReferenceColor,
        };
        return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }
}

public sealed record NavigationItemViewModel(
    AppSection Section,
    string Title,
    Icon Icon,
    ViewModelBase Page);

public sealed class MetricCardViewModel
{
    public MetricCardViewModel(
        string title,
        Icon icon,
        string value,
        string status,
        string explanation,
        string standard,
        StatusTone tone = StatusTone.Reference)
    {
        Title = title;
        Icon = icon;
        Value = value;
        Status = status;
        Explanation = explanation;
        Standard = standard;
        Tone = tone;
        var colors = UiPalette.For(tone);
        StatusForeground = colors.foreground;
        StatusBackground = colors.background;
        AccentBrush = colors.foreground;
    }

    public string Title { get; }
    public Icon Icon { get; }
    public string Value { get; }
    public string Status { get; }
    public string Explanation { get; }
    public string Standard { get; }
    public StatusTone Tone { get; }
    public IBrush StatusForeground { get; }
    public IBrush StatusBackground { get; }
    public IBrush AccentBrush { get; }
}

public sealed record EvidenceItemViewModel(Icon Icon, string Text);

public sealed record DiagnosticMetricCardViewModel(
    string Title,
    string Value,
    string Status,
    string Interpretation,
    string Impact,
    string Standard,
    IBrush StatusForeground,
    IBrush StatusBackground);

public sealed record LayerCardViewModel(
    string Title,
    Icon Icon,
    string Status,
    string Conclusion,
    string Evidence,
    IReadOnlyList<DiagnosticMetricCardViewModel> Metrics,
    string Action,
    IBrush StatusForeground,
    IBrush StatusBackground)
{
    public bool ShowEvidence => Metrics.Count == 0 && !string.IsNullOrWhiteSpace(Evidence);
}

public sealed record SpeedSampleViewModel(double Seconds, double Mbps);

public sealed class SpeedMetricCardViewModel
{
    public SpeedMetricCardViewModel(
        string title,
        Icon icon,
        string value,
        string? secondaryValue,
        MetricAssessment assessment,
        string impact,
        StatusTone tone,
        IBrush? accentBrush = null)
    {
        Title = title;
        Icon = icon;
        Value = value;
        SecondaryValue = secondaryValue;
        HasSecondaryValue = !string.IsNullOrWhiteSpace(secondaryValue);
        Status = assessment.StatusLabel;
        Interpretation = assessment.Interpretation;
        Impact = impact;
        Standard = assessment.Standard;
        Tone = tone;

        var colors = UiPalette.For(tone);
        StatusForeground = colors.foreground;
        StatusBackground = colors.background;
        AccentBrush = accentBrush ?? colors.foreground;
    }

    public string Title { get; }
    public Icon Icon { get; }
    public string Value { get; }
    public string? SecondaryValue { get; }
    public bool HasSecondaryValue { get; }
    public string Status { get; }
    public string Interpretation { get; }
    public string Impact { get; }
    public string Standard { get; }
    public StatusTone Tone { get; }
    public IBrush StatusForeground { get; }
    public IBrush StatusBackground { get; }
    public IBrush AccentBrush { get; }
}

public sealed record SpeedTransferEstimateViewModel(Icon Icon, string Title, string Value);

public sealed record SpeedMeasurementDetailViewModel(string Title, string Value);

public sealed record ChannelNetworkViewModel(
    string Id,
    string Ssid,
    string Band,
    int Channel,
    int? WidthMHz,
    int Rssi,
    string Color,
    bool IsCurrent = false);

public sealed record HistoryPointViewModel(DateTimeOffset Timestamp, double Rssi, string Marker);

public sealed record HistoryMarkerOptionViewModel(
    HistoryMarker Value,
    string Label);

public sealed record HistoryComparisonViewModel(
    string Title,
    string Status,
    string Meta,
    string Ssid,
    string Channel,
    string ChannelWidth,
    string Rssi,
    string GatewayLatency,
    IBrush StatusForeground,
    IBrush StatusBackground);

public sealed record HistoryRowViewModel(
    Guid Id,
    string Time,
    string Ssid,
    string Channel,
    string ChannelWidth,
    string Rssi,
    string GatewayLatency,
    string Status,
    IReadOnlyList<HistoryMarkerOptionViewModel> MarkerOptions,
    HistoryMarkerOptionViewModel SelectedMarker,
    IBrush StatusForeground);

public sealed record SuggestionCardViewModel(
    string Title,
    Icon Icon,
    string Headline,
    string Detail,
    string Status,
    IBrush StatusForeground,
    IBrush StatusBackground);
