using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentIcons.Common;
using WiFiHealthConsole.App.Services.History;
using WiFiHealthConsole.Core;

namespace WiFiHealthConsole.App.ViewModels;

public partial class HistoryPageViewModel : ViewModelBase
{
    private static readonly IReadOnlyList<HistoryMarkerOptionViewModel> MarkerOptions =
    [
        new(HistoryMarker.None, HistoryMarker.None.DisplayName()),
        new(HistoryMarker.Before, HistoryMarker.Before.DisplayName()),
        new(HistoryMarker.After, HistoryMarker.After.DisplayName()),
    ];

    private readonly IHistoryStore _store;
    private List<HistorySample> _samples = [];

    [ObservableProperty]
    private MetricCardViewModel beforeRssiMetric = CreateRssiMetric("变更前 RSSI", null);

    [ObservableProperty]
    private MetricCardViewModel afterRssiMetric = CreateRssiMetric("变更后 RSSI", null);

    [ObservableProperty]
    private HistoryComparisonViewModel beforeComparison = CreateComparison("变更前", null);

    [ObservableProperty]
    private HistoryComparisonViewModel afterComparison = CreateComparison("变更后", null);

    [ObservableProperty]
    private string rssiChange = "等待前后标记";

    [ObservableProperty]
    private string rssiChangeExplanation = "分别标记变更前和变更后的记录，才能判断信号变化。";

    [ObservableProperty]
    private IBrush rssiChangeForeground = UiPalette.Reference;

    [ObservableProperty]
    private IBrush rssiChangeBackground = UiPalette.ReferenceSoft;

    [ObservableProperty]
    private string gatewayChange = "--";

    [ObservableProperty]
    private string historyPath = string.Empty;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool showClearConfirmation;

    public HistoryPageViewModel(IHistoryStore? store = null)
    {
        _store = store ?? new HistoryStore();
        HistoryPath = _store.FilePath;
    }

    public string Title => "历史趋势";
    public string Subtitle => "保存本机采样，比较路由设置变更前后的结果";
    public bool HasHistory => _samples.Count > 0;
    public bool ShowEmptyState => !HasHistory;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public ObservableCollection<HistoryPointViewModel> Points { get; } = [];
    public ObservableCollection<HistoryRowViewModel> Rows { get; } = [];

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _samples = (await _store.LoadAsync(cancellationToken)).OrderBy(sample => sample.Timestamp).ToList();
            ErrorMessage = null;
            RefreshPresentation();
        }
        catch (HistoryStoreException error)
        {
            ErrorMessage = error.Message;
        }
    }

    public async Task AppendWirelessAsync(
        WifiSnapshot snapshot,
        HealthGrade grade,
        string statusLabel,
        CancellationToken cancellationToken = default)
    {
        if (!snapshot.IsConnected) return;
        if (_samples.LastOrDefault() is { } latest && snapshot.Timestamp - latest.Timestamp < TimeSpan.FromSeconds(30)) return;

        var sample = new HistorySample
        {
            Timestamp = snapshot.Timestamp,
            Snapshot = snapshot,
            OverallGrade = grade,
            OverallStatusLabel = statusLabel,
            GradeScope = HistoryGradeScope.Wireless,
            Marker = HistoryMarker.None
        };
        try
        {
            await _store.AppendAsync(sample, cancellationToken);
            _samples.Add(sample);
            ErrorMessage = null;
            RefreshPresentation();
        }
        catch (HistoryStoreException error)
        {
            ErrorMessage = error.Message;
        }
    }

    public async Task AppendDiagnosticAsync(
        DiagnosticReport report,
        CancellationToken cancellationToken = default)
    {
        var sample = new HistorySample
        {
            Timestamp = report.CompletedAt,
            Snapshot = report.WirelessSamples.LastOrDefault() ?? WifiSnapshot.Unavailable,
            GatewayAverageMs = report.GatewayPing?.AverageMs,
            GatewayJitterMs = report.GatewayPing?.JitterMs,
            GatewayLossPercent = report.GatewayPing?.PacketLossPercent,
            InternetAverageMs = report.ExternalPing?.AverageMs,
            OverallGrade = report.OverallGrade,
            OverallStatusLabel = report.OverallStatusLabel,
            GradeScope = HistoryGradeScope.FullDiagnosis,
            Marker = HistoryMarker.None
        };
        try
        {
            await _store.AppendAsync(sample, cancellationToken);
            _samples.Add(sample);
            ErrorMessage = null;
            RefreshPresentation();
        }
        catch (HistoryStoreException error)
        {
            ErrorMessage = error.Message;
        }
    }

    [RelayCommand]
    private void RequestClear()
    {
        if (HasHistory)
        {
            ShowClearConfirmation = true;
        }
    }

    [RelayCommand]
    private void CancelClear() => ShowClearConfirmation = false;

    [RelayCommand]
    private async Task ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        if (!HasHistory)
        {
            ShowClearConfirmation = false;
            return;
        }

        try
        {
            await _store.SaveAsync([], cancellationToken);
            _samples.Clear();
            ShowClearConfirmation = false;
            ErrorMessage = null;
            RefreshPresentation();
        }
        catch (HistoryStoreException error)
        {
            ErrorMessage = error.Message;
        }
    }

    public async Task SetMarkerAsync(
        Guid sampleId,
        HistoryMarker marker,
        CancellationToken cancellationToken = default)
    {
        var index = _samples.FindIndex(sample => sample.Id == sampleId);
        if (index < 0 || _samples[index].Marker == marker)
        {
            return;
        }

        var original = _samples[index];
        _samples[index] = original with { Marker = marker };
        try
        {
            await _store.SaveAsync(_samples, cancellationToken);
            ErrorMessage = null;
        }
        catch (HistoryStoreException error)
        {
            _samples[index] = original;
            ErrorMessage = error.Message;
        }
        finally
        {
            RefreshPresentation();
        }
    }

    private void RefreshPresentation()
    {
        Points.Clear();
        foreach (var sample in _samples.TakeLast(100))
        {
            if (sample.Snapshot.RssiValue is { } rssi)
            {
                Points.Add(new HistoryPointViewModel(sample.Timestamp, rssi, sample.Marker.DisplayName()));
            }
        }

        Rows.Clear();
        foreach (var sample in _samples.TakeLast(100).Reverse())
        {
            var color = sample.OverallGrade switch
            {
                HealthGrade.Good => UiPalette.Good,
                HealthGrade.Warning => UiPalette.Warning,
                HealthGrade.Critical => UiPalette.Critical,
                _ => UiPalette.Reference
            };
            Rows.Add(new HistoryRowViewModel(
                sample.Id,
                sample.Timestamp.ToLocalTime().ToString("MM-dd HH:mm"),
                DisplaySsid(sample.Snapshot),
                sample.Snapshot.PrimaryChannelValue is { } channel ? $"Ch {channel}" : "--",
                sample.Snapshot.ChannelWidthValue is { } width ? $"{width} MHz" : "--",
                sample.Snapshot.RssiValue is { } rssi ? $"{rssi} dBm" : "--",
                sample.GatewayAverageMs is { } gateway ? $"{gateway:0.0} ms" : "--",
                sample.OverallStatusLabel ?? sample.OverallGrade.Label(),
                MarkerOptions,
                MarkerOptions.First(option => option.Value == sample.Marker),
                color));
        }

        var before = _samples.LastOrDefault(sample => sample.Marker == HistoryMarker.Before);
        var after = _samples.LastOrDefault(sample => sample.Marker == HistoryMarker.After);
        var beforeRssi = before?.Snapshot.RssiValue;
        var afterRssi = after?.Snapshot.RssiValue;
        BeforeRssiMetric = CreateRssiMetric("变更前 RSSI", beforeRssi);
        AfterRssiMetric = CreateRssiMetric("变更后 RSSI", afterRssi);
        BeforeComparison = CreateComparison("变更前", before);
        AfterComparison = CreateComparison("变更后", after);
        var comparisonIssue = ComparisonIssue(before, after);
        UpdateRssiChange(beforeRssi, afterRssi, comparisonIssue);
        GatewayChange = comparisonIssue is null
            && before?.GatewayAverageMs is { } beforeGateway
            && after?.GatewayAverageMs is { } afterGateway
            ? $"{beforeGateway:0.0} → {afterGateway:0.0} ms"
            : "--";
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    private void UpdateRssiChange(int? before, int? after, string? comparisonIssue)
    {
        if (comparisonIssue is not null)
        {
            RssiChange = "暂不可比较";
            RssiChangeExplanation = comparisonIssue;
            ApplyRssiChangeTone(StatusTone.Reference);
            return;
        }

        if (before is null || after is null)
        {
            RssiChange = "等待前后标记";
            RssiChangeExplanation = "分别标记变更前和变更后的有效 RSSI 记录，才能判断信号变化。";
            ApplyRssiChangeTone(StatusTone.Reference);
            return;
        }

        var delta = after.Value - before.Value;
        var afterAssessment = HealthStandards.Rssi(after);
        if (delta > 0)
        {
            RssiChange = $"提升 {delta} dB";
            RssiChangeExplanation = afterAssessment.Grade == HealthGrade.Good
                ? "变更后信号增强，并已处于正常阈值内。"
                : $"信号有所增强，但变更后仍为“{afterAssessment.StatusLabel}”，还不能视为问题已经解决。";
            ApplyRssiChangeTone(ToneFor(afterAssessment.Grade));
            return;
        }

        if (delta < 0)
        {
            RssiChange = $"下降 {Math.Abs(delta)} dB";
            RssiChangeExplanation = $"变更后信号变弱，当前判定为“{afterAssessment.StatusLabel}”；建议在同一位置复测并检查频段、信道和频宽。";
            ApplyRssiChangeTone(afterAssessment.Grade == HealthGrade.Critical
                ? StatusTone.Critical
                : StatusTone.Warning);
            return;
        }

        RssiChange = "无明显变化";
        RssiChangeExplanation = afterAssessment.Grade == HealthGrade.Good
            ? "前后 RSSI 相同，当前信号仍处于正常阈值内。"
            : $"前后 RSSI 相同，且当前仍为“{afterAssessment.StatusLabel}”。";
        ApplyRssiChangeTone(ToneFor(afterAssessment.Grade));
    }

    private static string? ComparisonIssue(HistorySample? before, HistorySample? after)
    {
        if (before is null || after is null)
        {
            return null;
        }

        if (after.Timestamp <= before.Timestamp)
        {
            return "“变更后”记录必须晚于“变更前”记录；请重新选择时间顺序正确的两次采样。";
        }

        var beforeSsid = ComparableSsid(before.Snapshot);
        var afterSsid = ComparableSsid(after.Snapshot);
        if (beforeSsid is null || afterSsid is null)
        {
            return "前后记录的 SSID 证据不完整，无法确认它们来自同一个 Wi-Fi；取得 SSID 后再重新标记并回测。";
        }

        if (!string.Equals(beforeSsid, afterSsid, StringComparison.Ordinal))
        {
            return $"前后记录属于不同 Wi-Fi（{beforeSsid} / {afterSsid}），不能把差值解释为同一路由设置的变化。";
        }

        if (before.Snapshot.InterfaceId is { } beforeInterface
            && after.Snapshot.InterfaceId is { } afterInterface
            && beforeInterface != afterInterface)
        {
            return "前后记录来自不同的 Wi-Fi 网卡，无法直接解释为同一链路的设置变化。";
        }

        return null;
    }

    private static string? ComparableSsid(WifiSnapshot snapshot) =>
        snapshot.Ssid.TryGetValue(out var ssid) && !string.IsNullOrWhiteSpace(ssid)
            ? ssid.Trim()
            : null;

    private void ApplyRssiChangeTone(StatusTone tone)
    {
        var colors = UiPalette.For(tone);
        RssiChangeForeground = colors.foreground;
        RssiChangeBackground = colors.background;
    }

    private static HistoryComparisonViewModel CreateComparison(
        string title,
        HistorySample? sample)
    {
        if (sample is null)
        {
            var unavailable = UiPalette.For(StatusTone.Reference);
            return new HistoryComparisonViewModel(
                title,
                "未标记",
                $"在下方记录中选择“{title}”",
                "--",
                "--",
                "--",
                "--",
                "--",
                unavailable.foreground,
                unavailable.background);
        }

        var colors = UiPalette.For(ToneFor(sample.OverallGrade));
        var scope = sample.GradeScope switch
        {
            HistoryGradeScope.Wireless => "无线采样",
            HistoryGradeScope.FullDiagnosis => "四层体检",
            _ => "历史记录",
        };
        return new HistoryComparisonViewModel(
            title,
            sample.OverallStatusLabel ?? sample.OverallGrade.Label(),
            $"{sample.Timestamp.ToLocalTime():MM-dd HH:mm} · {scope}",
            DisplaySsid(sample.Snapshot),
            sample.Snapshot.PrimaryChannelValue is { } channel ? $"Ch {channel}" : "--",
            sample.Snapshot.ChannelWidthValue is { } width ? $"{width} MHz" : "--",
            sample.Snapshot.RssiValue is { } rssi ? $"{rssi} dBm" : "--",
            sample.GatewayAverageMs is { } gateway ? $"{gateway:0.0} ms" : "--",
            colors.foreground,
            colors.background);
    }

    private static string DisplaySsid(WifiSnapshot snapshot) =>
        snapshot.Ssid.TryGetValue(out var ssid) && !string.IsNullOrWhiteSpace(ssid)
            ? ssid
            : "SSID 未取得";

    private static MetricCardViewModel CreateRssiMetric(string title, int? value)
    {
        var assessment = HealthStandards.Rssi(value);
        return new MetricCardViewModel(
            title,
            Icon.WiFi4,
            value is { } measured ? $"{measured} dBm" : "--",
            assessment.StatusLabel,
            assessment.Interpretation,
            assessment.Standard,
            ToneFor(assessment.Grade));
    }

    private static StatusTone ToneFor(HealthGrade grade) => grade switch
    {
        HealthGrade.Good => StatusTone.Good,
        HealthGrade.Warning => StatusTone.Warning,
        HealthGrade.Critical => StatusTone.Critical,
        _ => StatusTone.Reference
    };
}
