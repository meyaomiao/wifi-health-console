using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentIcons.Common;
using WiFiHealthConsole.App.Services;
using WiFiHealthConsole.Core;

namespace WiFiHealthConsole.App.ViewModels;

public partial class OverviewPageViewModel : ViewModelBase
{
    private readonly IWifiTelemetryProvider _wifiProvider;
    private readonly Func<CancellationToken, Task>? _runDiagnosis;

    [ObservableProperty]
    private string title = "当前 Wi-Fi";

    [ObservableProperty]
    private string subtitle = "正在读取 Windows WLAN 接口";

    [ObservableProperty]
    private string overallStatus = "未检测";

    [ObservableProperty]
    private string conclusion = "无线状态未检测";

    [ObservableProperty]
    private string conclusionDetail = "连接 Wi-Fi 后刷新；SSID / BSSID 与附近网络可能需要 Windows 定位权限。";

    [ObservableProperty]
    private Icon conclusionIcon = Icon.Info;

    [ObservableProperty]
    private bool showPermissionBanner;

    [ObservableProperty]
    private string permissionTitle = "附近网络扫描需要 Windows 定位权限";

    [ObservableProperty]
    private string permissionDetail = "只用于读取 SSID / BSSID 和附近信道，不读取或保存经纬度。";

    [ObservableProperty]
    private IBrush overallForeground = UiPalette.Reference;

    [ObservableProperty]
    private IBrush overallBackground = UiPalette.ReferenceSoft;

    [ObservableProperty]
    private IBrush conclusionBackground = UiPalette.ReferenceSoft;

    [ObservableProperty]
    private IBrush conclusionBorder = new SolidColorBrush(Color.Parse("#4A5258"));

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool hasError;

    public OverviewPageViewModel(
        IWifiTelemetryProvider? wifiProvider = null,
        Func<CancellationToken, Task>? runDiagnosis = null)
    {
        _wifiProvider = wifiProvider ?? WifiTelemetryProviderFactory.CreateDefault();
        _runDiagnosis = runDiagnosis;
        BuildUnavailableMetrics();
    }

    public ObservableCollection<MetricCardViewModel> Metrics { get; } = [];

    public ObservableCollection<EvidenceItemViewModel> Evidence { get; } =
    [
        new(Icon.Ruler, "RSSI：> -55 dBm 优秀；-55～-67 dBm 正常；-68～-75 dBm 注意；< -75 dBm 严重。"),
        new(Icon.Info, "Windows 无法稳定读取噪声、SNR 和路由器侧 CCA，未取得时不会伪造数值。"),
        new(Icon.Status, "统一状态：绿色为优秀/正常，橙色为注意，红色为严重，灰色为参考/未检测。"),
        new(Icon.ArrowRight, "运行 60 秒体检，确认网关和宽带出口是否为瓶颈。")
    ];

    public WifiSnapshot CurrentSnapshot { get; private set; } = WifiSnapshot.Unavailable;
    public HealthGrade CurrentGrade { get; private set; } = HealthGrade.Unavailable;

    partial void OnErrorMessageChanged(string? value) =>
        HasError = !string.IsNullOrWhiteSpace(value);

    public async Task InitializeAsync(CancellationToken cancellationToken = default) =>
        await RefreshAsync(cancellationToken);

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        ErrorMessage = null;
        try
        {
            CurrentSnapshot = await _wifiProvider.GetCurrentAsync(cancellationToken);
            ApplySnapshot(CurrentSnapshot);
        }
        catch (WifiLocationPermissionException error)
        {
            ShowPermissionBanner = true;
            PermissionTitle = "Windows 已阻止 Wi-Fi 连接详情";
            PermissionDetail = error.Message;
            ErrorMessage = error.Message;
            ApplySnapshot(WifiSnapshot.Unavailable);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            ErrorMessage = error.Message;
            ApplySnapshot(WifiSnapshot.Unavailable);
        }
    }

    [RelayCommand]
    private async Task OpenLocationSettingsAsync(CancellationToken cancellationToken = default)
    {
        await _wifiProvider.OpenLocationPrivacySettingsAsync(cancellationToken);
    }

    [RelayCommand]
    private Task RunDiagnosisAsync(CancellationToken cancellationToken = default) =>
        _runDiagnosis?.Invoke(cancellationToken) ?? Task.CompletedTask;

    private void ApplySnapshot(WifiSnapshot snapshot)
    {
        var permissionDenied = new[]
        {
            snapshot.Ssid.Availability,
            snapshot.Bssid.Availability,
            snapshot.Band.Availability,
            snapshot.PrimaryChannel.Availability
        }.Contains(MetricAvailability.PermissionDenied);
        ShowPermissionBanner = permissionDenied;
        if (permissionDenied)
        {
            PermissionTitle = "Windows 定位权限尚未允许";
            PermissionDetail = "允许桌面应用访问位置后，才能读取 SSID / BSSID 并扫描附近网络；应用不读取经纬度。";
        }

        Title = snapshot.Ssid.TryGetValue(out var ssid) && !string.IsNullOrWhiteSpace(ssid)
            ? ssid
            : "当前 Wi-Fi";
        var band = snapshot.BandValue.DisplayName();
        var channel = snapshot.PrimaryChannelValue?.ToString() ?? "--";
        var width = snapshot.ChannelWidthValue is { } widthValue ? $"{widthValue} MHz" : "频宽未检测";
        Subtitle = snapshot.IsConnected
            ? $"{snapshot.InterfaceName ?? "Wi-Fi"} · {band} · 信道 {channel} · {width}"
            : "未检测到已连接的 Wi-Fi";

        var rssi = HealthStandards.Rssi(snapshot.RssiDbm);
        var noise = HealthStandards.Noise(snapshot.NoiseDbm);
        var snr = HealthStandards.Snr(snapshot.SnrDb);
        var channelWidth = HealthStandards.ChannelWidth(snapshot.ChannelWidthMHz, snapshot.BandValue);
        var cca = HealthStandards.Cca(snapshot.CcaPercent);

        Metrics.Clear();
        Metrics.Add(Metric(
            "频段",
            Icon.WiFiSettings,
            snapshot.Band.TryGetValue(out var currentBand) ? currentBand.DisplayName() : "--",
            HealthStandards.Reference(
                snapshot.Band.HasValue,
                snapshot.Band.HasValue ? $"当前连接在 {snapshot.BandValue.DisplayName()}；频段本身不单独代表好坏。" : snapshot.Band.Detail ?? "未取得频段。",
                "结合覆盖范围与附近网络选择频段。")));
        Metrics.Add(Metric(
            "信道",
            Icon.Channel,
            Format(snapshot.PrimaryChannel, value => value.ToString()),
            HealthStandards.Reference(
                snapshot.PrimaryChannel.HasValue,
                snapshot.PrimaryChannel.HasValue ? $"当前主信道为 {snapshot.PrimaryChannelValue}；需结合附近网络重叠与路由器侧数据判断。" : snapshot.PrimaryChannel.Detail ?? "未取得信道。",
                "信道本身不单独判故障。")));
        Metrics.Add(Metric("频宽", Icon.ArrowAutofitWidth, Format(snapshot.ChannelWidthMHz, value => $"{value} MHz"), channelWidth));
        Metrics.Add(Metric("RSSI", Icon.WiFi4, Format(snapshot.RssiDbm, value => $"{value} dBm"), rssi));
        Metrics.Add(Metric("噪声", Icon.Pulse, Format(snapshot.NoiseDbm, value => $"{value} dBm"), noise));
        Metrics.Add(Metric("SNR", Icon.HeartPulse, Format(snapshot.SnrDb, value => $"{value} dB"), snr));
        Metrics.Add(Metric(
            "协商速率",
            Icon.Gauge,
            Format(snapshot.TransmitRateMbps, value => $"{value:0} Mbps"),
            HealthStandards.Reference(
                snapshot.TransmitRateMbps.HasValue,
                snapshot.TransmitRateMbps.HasValue
                    ? $"当前发送协商速率约 {snapshot.TransmitRateMbps.Value:0} Mbps，不等于互联网实测吞吐。"
                    : snapshot.TransmitRateMbps.Detail ?? "未取得协商速率。",
                "需要结合网速测速与稳定性判断。")));
        Metrics.Add(Metric("CCA", Icon.DataBarVertical, Format(snapshot.CcaPercent, value => $"{value:0.0}%"), cca));

        var decisive = new[] { rssi, snr, cca }
            .Concat(channelWidth.Grade is HealthGrade.Warning or HealthGrade.Critical
                ? [channelWidth]
                : [])
            .ToArray();
        var hasIncompleteAirEvidence = decisive.Any(assessment =>
            assessment.Grade == HealthGrade.Unavailable
            && assessment.StatusLabel is HealthStatusLabels.Unavailable or HealthStatusLabels.NotSupported);
        var measuredGrade = HealthStandards.Worst(decisive);
        var grade = snapshot.IsConnected
            ? measuredGrade == HealthGrade.Good && hasIncompleteAirEvidence
                ? HealthGrade.Unavailable
                : measuredGrade
            : HealthGrade.Unavailable;
        CurrentGrade = grade;
        OverallStatus = snapshot.IsConnected
            ? measuredGrade == HealthGrade.Good && hasIncompleteAirEvidence
                ? HealthStatusLabels.Partial
                : HealthStandards.SummaryStatusLabel(decisive)
            : HealthStatusLabels.Unavailable;
        Conclusion = grade switch
        {
            HealthGrade.Good when OverallStatus == HealthStatusLabels.Excellent => "无线状态优秀",
            HealthGrade.Good => "无线状态正常",
            HealthGrade.Warning => "无线状态需要注意",
            HealthGrade.Critical => "无线状态存在严重异常",
            _ when snapshot.IsConnected && measuredGrade == HealthGrade.Good => "RSSI 正常，空口证据不完整",
            _ => "无线状态未检测"
        };
        ConclusionIcon = grade switch
        {
            HealthGrade.Good => Icon.Checkmark,
            HealthGrade.Warning => Icon.Warning,
            HealthGrade.Critical => Icon.ErrorCircle,
            _ => Icon.Info
        };
        ConclusionDetail = snapshot.IsConnected && measuredGrade == HealthGrade.Good && hasIncompleteAirEvidence
            ? "已取得的无线指标处于正常范围，但 Windows 未提供完整的 SNR / CCA 等空口证据，不能把整体无线状态判为正常。"
            : snapshot.IsConnected
                ? "此处判断当前 Windows 电脑的无线连接；局域网、宽带出口和 VPN / 代理需要运行完整体检。"
            : "确认 Windows 电脑已连接 Wi-Fi；部分连接详情和附近扫描需要定位权限。";

        var tone = ToneFor(grade);
        var colors = UiPalette.For(tone);
        OverallForeground = colors.foreground;
        OverallBackground = colors.background;
        ConclusionBackground = colors.background;
        ConclusionBorder = UiPalette.WithAlpha(tone, 78);
    }

    private void BuildUnavailableMetrics() => ApplySnapshot(WifiSnapshot.Unavailable);

    private static MetricCardViewModel Metric(
        string title,
        Icon icon,
        string value,
        MetricAssessment assessment) =>
        new(
            title,
            icon,
            value,
            assessment.StatusLabel,
            assessment.Interpretation,
            assessment.Standard,
            ToneFor(assessment.Grade));

    private static string Format<T>(Observed<T> observed, Func<T, string> formatter) =>
        observed.TryGetValue(out var value) ? formatter(value) : "--";

    private static StatusTone ToneFor(HealthGrade grade) => grade switch
    {
        HealthGrade.Good => StatusTone.Good,
        HealthGrade.Warning => StatusTone.Warning,
        HealthGrade.Critical => StatusTone.Critical,
        _ => StatusTone.Reference
    };

}
