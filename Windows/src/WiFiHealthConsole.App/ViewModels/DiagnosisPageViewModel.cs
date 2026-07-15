using System.Collections.ObjectModel;
using System.Net;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentIcons.Common;
using WiFiHealthConsole.App.Services.Diagnostics;
using WiFiHealthConsole.Core;

namespace WiFiHealthConsole.App.ViewModels;

public partial class DiagnosisPageViewModel : ViewModelBase
{
    private readonly INetworkDiagnosticService _service;
    private readonly HistoryPageViewModel? _history;

    [ObservableProperty]
    private bool isRunning;

    [ObservableProperty]
    private bool hasReport;

    [ObservableProperty]
    private double progress;

    [ObservableProperty]
    private string stage = "尚无体检报告";

    [ObservableProperty]
    private string overallStatus = HealthStatusLabels.Unavailable;

    [ObservableProperty]
    private string overallConclusion = "尚无体检报告";

    [ObservableProperty]
    private string baselineDescription = "完成体检后查看无代理基线说明。";

    [ObservableProperty]
    private string completedAt = "--";

    [ObservableProperty]
    private Icon overallIcon = Icon.Info;

    [ObservableProperty]
    private IBrush overallForeground = UiPalette.Reference;

    [ObservableProperty]
    private IBrush overallBackground = UiPalette.ReferenceSoft;

    [ObservableProperty]
    private bool showUnavailableLayers;

    [ObservableProperty]
    private string unavailableLayers = string.Empty;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool hasError;

    public DiagnosisPageViewModel(
        INetworkDiagnosticService? service = null,
        HistoryPageViewModel? history = null)
    {
        _service = service ?? new NetworkDiagnosticService();
        _history = history;
    }

    public string Title => "60 秒体检";
    public string Subtitle => "无线空口、局域网、宽带出口、VPN / 代理分层判定";
    public string DurationHint => OperatingSystem.IsWindows()
        ? "完整采样窗口为 60 秒。"
        : "当前为 Mac 开发预览，使用加速采样验证界面。";

    public ObservableCollection<LayerCardViewModel> Layers { get; } = [];

    partial void OnErrorMessageChanged(string? value) =>
        HasError = !string.IsNullOrWhiteSpace(value);

    [RelayCommand]
    public async Task RunDiagnosisAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning) return;
        IsRunning = true;
        HasReport = false;
        ErrorMessage = null;
        Progress = 0;
        Layers.Clear();

        var progress = new Progress<NetworkDiagnosticProgress>(update =>
        {
            Stage = update.Stage;
            Progress = Math.Clamp(update.FractionCompleted, 0, 1);
        });

        try
        {
            var options = OperatingSystem.IsWindows()
                ? new NetworkDiagnosticOptions()
                : new NetworkDiagnosticOptions
                {
                    Duration = TimeSpan.FromSeconds(2.4),
                    GatewayPingInterval = TimeSpan.FromMilliseconds(240),
                    PingTimeout = TimeSpan.FromMilliseconds(800),
                    ExternalPingAddress = IPAddress.Loopback,
                    ExternalPingCount = 2,
                    ExternalPingInterval = TimeSpan.FromMilliseconds(80),
                    DnsMaximumAttempts = 1,
                    DnsAttemptTimeout = TimeSpan.FromSeconds(1),
                    HttpsTimeout = TimeSpan.FromSeconds(3)
                };

            var report = await _service.RunAsync(options, progress, cancellationToken);
            foreach (var layer in report.Layers)
            {
                Layers.Add(MapLayer(layer));
            }
            ApplyReportSummary(report);
            Stage = report.OverallStatusLabel == HealthStatusLabels.Partial
                ? "体检部分完成"
                : $"体检完成 · {report.OverallStatusLabel}";
            Progress = 1;
            HasReport = true;
            if (_history is not null)
            {
                await _history.AppendDiagnosticAsync(report, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Stage = "体检已取消";
        }
        catch (Exception error)
        {
            ErrorMessage = error.Message;
            Stage = "体检未完成";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private static LayerCardViewModel MapLayer(LayerResult layer)
    {
        var tone = ToneFor(layer.Grade);
        var colors = UiPalette.For(tone);
        var glyph = layer.Layer switch
        {
            DiagnosticLayer.Wireless => Icon.WiFi4,
            DiagnosticLayer.LocalNetwork => Icon.Home,
            DiagnosticLayer.Internet => Icon.Globe,
            DiagnosticLayer.ProxyVpn => Icon.Shield,
            _ => Icon.Info
        };
        return new LayerCardViewModel(
            layer.Layer.DisplayName(),
            glyph,
            layer.StatusLabel,
            layer.Conclusion,
            layer.Evidence.Count == 0 ? "本层未取得足够证据。" : string.Join("；", layer.Evidence),
            layer.Metrics.Select(MapMetric).ToArray(),
            layer.Action,
            colors.foreground,
            colors.background);
    }

    private static DiagnosticMetricCardViewModel MapMetric(DiagnosticMetric metric)
    {
        var colors = UiPalette.For(ToneFor(metric.Grade));
        return new DiagnosticMetricCardViewModel(
            metric.Title,
            metric.Value,
            metric.StatusLabel,
            metric.Interpretation,
            metric.Impact,
            metric.Standard,
            colors.foreground,
            colors.background);
    }

    private void ApplyReportSummary(DiagnosticReport report)
    {
        OverallStatus = report.OverallStatusLabel;
        BaselineDescription = report.BaselineDescription;
        CompletedAt = report.CompletedAt.ToLocalTime().ToString("MM-dd HH:mm:ss");

        var focusLayer = report.Layers.FirstOrDefault(layer => layer.Grade == HealthGrade.Critical)
            ?? report.Layers.FirstOrDefault(layer => layer.Grade == HealthGrade.Warning);
        OverallConclusion = focusLayer is not null
            ? $"{focusLayer.Layer.DisplayName()}：{focusLayer.Conclusion}"
            : report.OverallGrade switch
            {
                HealthGrade.Good => "四层基线均处于正常范围",
                HealthGrade.Unavailable when report.OverallStatusLabel == HealthStatusLabels.Partial =>
                    "体检部分完成；已完成项目未见异常",
                _ => "体检未取得足够证据"
            };

        OverallIcon = report.OverallGrade switch
        {
            HealthGrade.Good => Icon.Checkmark,
            HealthGrade.Warning => Icon.Warning,
            HealthGrade.Critical => Icon.ErrorCircle,
            _ => Icon.Info
        };
        var colors = UiPalette.For(ToneFor(report.OverallGrade));
        OverallForeground = colors.foreground;
        OverallBackground = colors.background;

        var unavailable = report.Layers
            .Where(layer => layer.Grade == HealthGrade.Unavailable)
            .Select(layer => layer.Layer.DisplayName())
            .ToArray();
        ShowUnavailableLayers = unavailable.Length > 0;
        UnavailableLayers = unavailable.Length == 0
            ? string.Empty
            : $"未检测：{string.Join("、", unavailable)}";
    }

    private static StatusTone ToneFor(HealthGrade grade) => grade switch
    {
        HealthGrade.Good => StatusTone.Good,
        HealthGrade.Warning => StatusTone.Warning,
        HealthGrade.Critical => StatusTone.Critical,
        _ => StatusTone.Reference
    };
}
