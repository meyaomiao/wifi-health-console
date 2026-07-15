using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentIcons.Common;
using WiFiHealthConsole.App.Services.Speed;
using WiFiHealthConsole.Core;

namespace WiFiHealthConsole.App.ViewModels;

public partial class SpeedTestPageViewModel : ViewModelBase
{
    private readonly ISpeedTestProvider _provider;
    private SpeedTestPhase? _currentSpeedPhase;
    private WifiSnapshot _wifiSnapshot = WifiSnapshot.Unavailable;
    private double? _downloadResponsivenessRpm;
    private double? _uploadResponsivenessRpm;
    private double _measurementChartSeconds = SpeedTestDurationPreset.Standard.PhaseRuntimeSeconds();

    [ObservableProperty]
    private bool isRunning;

    [ObservableProperty]
    private bool hasResult;

    [ObservableProperty]
    private string activePhase = "尚未测速";

    [ObservableProperty]
    private string selectedRoute = SpeedTestRoute.CurrentPath.DisplayName();

    [ObservableProperty]
    private string selectedDuration = SpeedTestDurationPreset.Standard.SegmentTitle();

    [ObservableProperty]
    private bool isCurrentRouteSelected = true;

    [ObservableProperty]
    private bool isDirectRouteSelected;

    [ObservableProperty]
    private bool isStandardDurationSelected = true;

    [ObservableProperty]
    private bool isStableDurationSelected;

    [ObservableProperty]
    private double? downloadMbps;

    [ObservableProperty]
    private double? uploadMbps;

    [ObservableProperty]
    private double? idleLatencyMs;

    [ObservableProperty]
    private MetricCardViewModel downloadMetric = CreateDownloadMetric(null);

    [ObservableProperty]
    private MetricCardViewModel uploadMetric = CreateUploadMetric(null);

    [ObservableProperty]
    private MetricCardViewModel idleLatencyMetric = CreateIdleLatencyMetric(null);

    [ObservableProperty]
    private MetricCardViewModel downloadResponsivenessMetric = CreateResponsivenessMetric(
        "下载时响应",
        null);

    [ObservableProperty]
    private MetricCardViewModel uploadResponsivenessMetric = CreateResponsivenessMetric(
        "上传时响应",
        null);

    [ObservableProperty]
    private string endpoint = "尚未选择测速节点";

    [ObservableProperty]
    private string transferredTraffic = "--";

    [ObservableProperty]
    private string trendInterfaceDisplay = "未识别";

    [ObservableProperty]
    private string resultConclusion = "测速结果未生成";

    [ObservableProperty]
    private string resultContext = "--";

    [ObservableProperty]
    private string resultStatus = HealthStatusLabels.Unavailable;

    [ObservableProperty]
    private Icon resultIcon = Icon.Info;

    [ObservableProperty]
    private IBrush resultStatusForeground = UiPalette.Reference;

    [ObservableProperty]
    private IBrush resultStatusBackground = UiPalette.ReferenceSoft;

    [ObservableProperty]
    private IBrush resultSummaryBackground = UiPalette.ReferenceSoft;

    [ObservableProperty]
    private IBrush resultSummaryBorder = UiPalette.Reference;

    [ObservableProperty]
    private bool showInterfaceMismatch;

    [ObservableProperty]
    private string interfaceMismatchMessage = string.Empty;

    [ObservableProperty]
    private string? errorMessage;

    public SpeedTestPageViewModel(ISpeedTestProvider? provider = null)
    {
        var usesPreviewProvider = provider is null && !OperatingSystem.IsWindows();
        _provider = provider ?? new CloudflareSpeedTestService(
            OperatingSystem.IsWindows()
                ? null
                : new CloudflareSpeedTestOptions
                {
                    PhaseDurationOverride = TimeSpan.FromSeconds(4),
                    WarmupDuration = TimeSpan.FromSeconds(1),
                    ParallelConnections = 2,
                    DownloadChunkBytes = 5_000_000,
                    UploadChunkBytes = 2_000_000
                });

        if (usesPreviewProvider)
        {
            SeedPreviewResult();
        }
    }

    public string Title => "网速测速";
    public string Subtitle => "直接显示 Mbps、MB/s、文件耗时和实际体验";
    public string RunButtonText => IsRunning ? "测速中" : "开始测速";
    public string DownloadSpeedDisplay => FormatMbps(DownloadMbps);
    public string UploadSpeedDisplay => FormatMbps(UploadMbps);
    public string DownloadMegabytesPerSecond => FormatMegabytesPerSecond(DownloadMbps);
    public string UploadMegabytesPerSecond => FormatMegabytesPerSecond(UploadMbps);
    public string DownloadPhaseStatus => PhaseStatus(SpeedTestPhase.Download);
    public string UploadPhaseStatus => PhaseStatus(SpeedTestPhase.Upload);
    public IBrush DownloadPhaseStatusForeground => PhaseColors(SpeedTestPhase.Download).foreground;
    public IBrush DownloadPhaseStatusBackground => PhaseColors(SpeedTestPhase.Download).background;
    public IBrush UploadPhaseStatusForeground => PhaseColors(SpeedTestPhase.Upload).foreground;
    public IBrush UploadPhaseStatusBackground => PhaseColors(SpeedTestPhase.Upload).background;
    public string RouteDetail => CurrentRoute.Detail();
    public string DurationDetail => CurrentDuration.Detail();
    public string TrafficWarning => CurrentDuration.TrafficWarning();
    public string MaximumDurationHint =>
        $"吞吐阶段约 {CurrentDuration.ThroughputStageSeconds()} 秒 · 延迟预检另计";
    public string EmptyStateDescription =>
        $"{CurrentDuration.DisplayName()}下载、上传每个方向各测约 {CurrentDuration.PhaseRuntimeSeconds()} 秒；" +
        "开始前的延迟预检和连接建立另计，网络异常时总耗时会更长。";
    public string DirectWifiInterfaceDetail => _wifiSnapshot.InterfaceName is { Length: > 0 } interfaceName
        ? $"当前 Wi-Fi 接口：{interfaceName}"
        : "概览页未取得 Wi-Fi 接口；测速时由 Windows 再确认活动接口。";
    public bool ShowDirectWifiInterface => IsDirectRouteSelected;
    public string TrendHeading => IsRunning ? "分阶段实时趋势" : "本次分阶段趋势";
    public string NegotiatedRateDisplay =>
        _wifiSnapshot.TransmitRateMbps.TryGetValue(out var value) && double.IsFinite(value) && value > 0
            ? $"{value:0} Mbps"
            : "--";
    public string NegotiatedRateDetail =>
        _wifiSnapshot.TransmitRateMbps.TryGetValue(out var value) && double.IsFinite(value) && value > 0
            ? "Windows WLAN 接口报告的当前发送协商速率"
            : _wifiSnapshot.TransmitRateMbps.Detail ?? "本次未取得协商速率，未用信号质量反推。";
    public double ChartMaximumSeconds => IsRunning || HasResult
        ? _measurementChartSeconds
        : CurrentDuration.PhaseRuntimeSeconds();
    public bool ShowResultCards => HasResult && !IsRunning;
    public bool ShowCharts => IsRunning || HasResult;
    public bool ShowEmptyState => !IsRunning && !HasResult && string.IsNullOrWhiteSpace(ErrorMessage);
    public bool ShowErrorState => !IsRunning && !HasResult && !string.IsNullOrWhiteSpace(ErrorMessage);

    public ObservableCollection<SpeedSampleViewModel> DownloadSamples { get; } = [];
    public ObservableCollection<SpeedSampleViewModel> UploadSamples { get; } = [];
    public ObservableCollection<SpeedMetricCardViewModel> ThroughputMetrics { get; } = [];
    public ObservableCollection<SpeedMetricCardViewModel> QualityMetrics { get; } = [];
    public ObservableCollection<SpeedTransferEstimateViewModel> TransferEstimates { get; } = [];
    public ObservableCollection<SpeedMeasurementDetailViewModel> MeasurementDetails { get; } = [];

    private SpeedTestRoute CurrentRoute => IsCurrentRouteSelected
        ? SpeedTestRoute.CurrentPath
        : SpeedTestRoute.DirectWiFiBaseline;

    private SpeedTestDurationPreset CurrentDuration => IsStandardDurationSelected
        ? SpeedTestDurationPreset.Standard
        : SpeedTestDurationPreset.Stable;

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(RunButtonText));
        OnPropertyChanged(nameof(ChartMaximumSeconds));
        NotifyPresentationStateChanged();
    }

    partial void OnHasResultChanged(bool value)
    {
        OnPropertyChanged(nameof(ChartMaximumSeconds));
        NotifyPresentationStateChanged();
    }

    partial void OnErrorMessageChanged(string? value) => NotifyPresentationStateChanged();

    partial void OnDownloadMbpsChanged(double? value)
    {
        OnPropertyChanged(nameof(DownloadSpeedDisplay));
        OnPropertyChanged(nameof(DownloadMegabytesPerSecond));
        NotifyPhasePresentationChanged();
    }

    partial void OnUploadMbpsChanged(double? value)
    {
        OnPropertyChanged(nameof(UploadSpeedDisplay));
        OnPropertyChanged(nameof(UploadMegabytesPerSecond));
        NotifyPhasePresentationChanged();
    }

    public void UpdateWifiContext(WifiSnapshot snapshot)
    {
        _wifiSnapshot = snapshot ?? WifiSnapshot.Unavailable;
        OnPropertyChanged(nameof(DirectWifiInterfaceDetail));
        OnPropertyChanged(nameof(NegotiatedRateDisplay));
        OnPropertyChanged(nameof(NegotiatedRateDetail));
    }

    [RelayCommand]
    private void SelectRoute(string route)
    {
        IsCurrentRouteSelected = route == "current";
        IsDirectRouteSelected = !IsCurrentRouteSelected;
        SelectedRoute = CurrentRoute.DisplayName();
        OnPropertyChanged(nameof(RouteDetail));
        OnPropertyChanged(nameof(ShowDirectWifiInterface));
    }

    [RelayCommand]
    private void SelectDuration(string duration)
    {
        IsStandardDurationSelected = duration == "standard";
        IsStableDurationSelected = !IsStandardDurationSelected;
        SelectedDuration = CurrentDuration.SegmentTitle();
        OnPropertyChanged(nameof(DurationDetail));
        OnPropertyChanged(nameof(TrafficWarning));
        OnPropertyChanged(nameof(MaximumDurationHint));
        OnPropertyChanged(nameof(EmptyStateDescription));
        OnPropertyChanged(nameof(ChartMaximumSeconds));
    }

    [RelayCommand]
    private async Task RunSpeedTestAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning) return;

        var route = CurrentRoute;
        var duration = CurrentDuration;
        _measurementChartSeconds = duration.PhaseRuntimeSeconds();
        IsRunning = true;
        HasResult = false;
        ErrorMessage = null;
        DownloadMbps = null;
        UploadMbps = null;
        IdleLatencyMs = null;
        ResetResultPresentation();
        ActivePhase = "正在准备分阶段下载与上传测速";
        Endpoint = "正在选择测速节点";
        TransferredTraffic = "正在建立测速连接";
        TrendInterfaceDisplay = _wifiSnapshot.InterfaceName ?? "由测速引擎自动识别";
        DownloadSamples.Clear();
        UploadSamples.Clear();
        _currentSpeedPhase = null;
        NotifyPhasePresentationChanged();

        var progress = new Progress<SpeedTestProgress>(update =>
        {
            if (update.Phase == SpeedTestPhase.Download)
            {
                ActivePhase = "正在测量下载阶段，上传尚未开始";
                SetCurrentSpeedPhase(SpeedTestPhase.Download);
                AddLiveSample(DownloadSamples, update.Sample, value => DownloadMbps = value);
            }
            else
            {
                ActivePhase = "下载阶段已完成，正在测量上传阶段";
                SetCurrentSpeedPhase(SpeedTestPhase.Upload);
                AddLiveSample(UploadSamples, update.Sample, value => UploadMbps = value);
            }

            NotifyPhasePresentationChanged();

            if (!string.IsNullOrWhiteSpace(update.Node)) Endpoint = update.Node!;
            TransferredTraffic = $"当前阶段已传输 {update.TransferredBytes / 1_000_000d:0.0} MB";
        });

        try
        {
            var report = await _provider.RunAsync(
                new SpeedTestRequest
                {
                    Route = route,
                    DurationPreset = duration,
                    RequestedInterface = _wifiSnapshot.InterfaceName
                },
                progress,
                cancellationToken);

            ApplyReport(report);
            ActivePhase = "测速完成";
            HasResult = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ErrorMessage = "测速已取消，未生成最终结果。";
            ActivePhase = "测速已取消";
        }
        catch (Exception error)
        {
            ErrorMessage = error.Message;
            ActivePhase = "测速未完成";
        }
        finally
        {
            _currentSpeedPhase = null;
            NotifyPhasePresentationChanged();
            IsRunning = false;
        }
    }

    private void SeedPreviewResult()
    {
        UpdateWifiContext(new WifiSnapshot
        {
            InterfaceName = "Wi-Fi",
            TransmitRateMbps = Observed<double>.Available(1_201, EvidenceSource.Mock, "开发预览数据")
        });

        var report = new SpeedTestReport
        {
            Route = SpeedTestRoute.CurrentPath,
            DurationPreset = SpeedTestDurationPreset.Standard,
            RequestedInterface = "Wi-Fi",
            SampledInterface = "Wi-Fi",
            SampledInterfaceId = "preview-wifi",
            PathDescription = "跟随 Windows 系统当前默认路由",
            Endpoint = "Cloudflare Speed Test · 开发预览",
            DownloadBitsPerSecond = 286_400_000,
            UploadBitsPerSecond = 48_700_000,
            IdleLatencyMs = 28,
            DownloadResponsivenessRpm = 742,
            UploadResponsivenessRpm = 468,
            DownloadedBytes = 612_000_000,
            UploadedBytes = 108_000_000,
            DurationSeconds = 40,
            WasProxied = false
        };

        ApplyReport(report);
        ActivePhase = "开发预览";
        HasResult = true;

        for (var index = 0; index <= 40; index++)
        {
            var t = index / 2d;
            var download = 305 * (1 - Math.Exp(-t / 2.5)) *
                (0.91 + Math.Sin(t * 1.05) * 0.06 + Math.Sin(t * .29) * .025);
            var upload = 51 * (1 - Math.Exp(-t / 2.2)) *
                (0.93 + Math.Sin(t * .92 + 1.5) * .06);
            DownloadSamples.Add(new SpeedSampleViewModel(t, Math.Max(0, download)));
            UploadSamples.Add(new SpeedSampleViewModel(t, Math.Max(0, upload)));
        }
    }

    private void ApplyReport(SpeedTestReport report)
    {
        DownloadMbps = NormalizeMeasurement(report.DownloadMbps);
        UploadMbps = NormalizeMeasurement(report.UploadMbps);
        IdleLatencyMs = NormalizeMeasurement(report.IdleLatencyMs);
        _measurementChartSeconds = report.DurationPreset.PhaseRuntimeSeconds();
        OnPropertyChanged(nameof(ChartMaximumSeconds));
        DownloadMetric = CreateDownloadMetric(DownloadMbps);
        UploadMetric = CreateUploadMetric(UploadMbps);
        IdleLatencyMetric = CreateIdleLatencyMetric(IdleLatencyMs);
        _downloadResponsivenessRpm = NormalizeMeasurement(report.DownloadResponsivenessRpm);
        _uploadResponsivenessRpm = NormalizeMeasurement(report.UploadResponsivenessRpm);
        DownloadResponsivenessMetric = CreateResponsivenessMetric("下载时响应", _downloadResponsivenessRpm);
        UploadResponsivenessMetric = CreateResponsivenessMetric("上传时响应", _uploadResponsivenessRpm);
        Endpoint = report.Endpoint ?? _provider.ProviderName;
        TransferredTraffic = FormatTransferSummary(report);
        TrendInterfaceDisplay = report.SampledInterface
            ?? report.MeasuredInterface
            ?? report.RequestedInterface
            ?? "未识别";

        BuildSummary(report);
        BuildThroughputMetrics();
        BuildQualityMetrics();
        BuildTransferEstimates(report);
        BuildMeasurementDetails(report);

        ShowInterfaceMismatch = report.SampledInterfaceId is { Length: > 0 } sampledId
            && report.MeasuredInterfaceId is { Length: > 0 } measuredId
            && !string.Equals(sampledId, measuredId, StringComparison.OrdinalIgnoreCase);
        InterfaceMismatchMessage = ShowInterfaceMismatch
            ? $"Wi-Fi 上下文接口为 {report.SampledInterface ?? report.SampledInterfaceId}，" +
              $"实际绑定接口为 {report.MeasuredInterface ?? report.MeasuredInterfaceId}。" +
              "两者不同时，Wi-Fi 上下文不代表实际绑定路径，最终速度以分阶段流量结果为准。"
            : string.Empty;
    }

    private void BuildSummary(SpeedTestReport report)
    {
        var namedAssessments = new (string Title, MetricAssessment Assessment)[]
        {
            ("下载速度", HealthStandards.Download(DownloadMbps)),
            ("上传速度", HealthStandards.Upload(UploadMbps)),
            ("空闲延迟", HealthStandards.IdleLatency(IdleLatencyMs)),
            ("下载时响应", ResponsivenessAssessment(_downloadResponsivenessRpm, "下载时响应")),
            ("上传时响应", ResponsivenessAssessment(_uploadResponsivenessRpm, "上传时响应"))
        };

        var assessments = namedAssessments.Select(item => item.Assessment).ToArray();
        var measuredGrade = HealthStandards.Worst(assessments);
        var hasMissingEvidence = assessments.Any(assessment =>
            assessment.Grade == HealthGrade.Unavailable
            && assessment.StatusLabel == HealthStatusLabels.Unavailable);
        var isPartial = measuredGrade == HealthGrade.Good && hasMissingEvidence;
        var grade = isPartial ? HealthGrade.Unavailable : measuredGrade;
        ResultStatus = isPartial
            ? HealthStatusLabels.Partial
            : HealthStandards.SummaryStatusLabel(assessments);
        ResultConclusion = isPartial
            ? "已取得的测速指标正常，但仍有指标未测得"
            : SpeedConclusion(grade, ResultStatus, namedAssessments);
        ResultContext =
            $"{report.Route.DisplayName()} · {report.DurationPreset.DisplayName()} · " +
            $"{report.CompletedAt.LocalDateTime:yyyy/M/d HH:mm:ss}";

        var tone = ToneFor(grade);
        var colors = UiPalette.For(tone);
        ResultStatusForeground = colors.foreground;
        ResultStatusBackground = colors.background;
        ResultSummaryBackground = colors.background;
        ResultSummaryBorder = colors.foreground;
        ResultIcon = grade switch
        {
            HealthGrade.Good => Icon.Checkmark,
            HealthGrade.Warning => Icon.Warning,
            HealthGrade.Critical => Icon.ErrorCircle,
            _ => Icon.Info
        };
    }

    private void BuildThroughputMetrics()
    {
        ThroughputMetrics.Clear();
        ThroughputMetrics.Add(new SpeedMetricCardViewModel(
            "下载速度",
            Icon.ArrowDown,
            FormatMbps(DownloadMbps),
            FormatMegabytesPerSecond(DownloadMbps, approximation: true),
            HealthStandards.Download(DownloadMbps),
            "影响网页与视频加载、大文件下载、系统更新和多设备同时使用。",
            ToneFor(HealthStandards.Download(DownloadMbps).Grade),
            UiPalette.Accent));
        ThroughputMetrics.Add(new SpeedMetricCardViewModel(
            "上传速度",
            Icon.ArrowUp,
            FormatMbps(UploadMbps),
            FormatMegabytesPerSecond(UploadMbps, approximation: true),
            HealthStandards.Upload(UploadMbps),
            "影响视频会议、直播、云盘同步、照片备份和发送大文件。",
            ToneFor(HealthStandards.Upload(UploadMbps).Grade),
            UiPalette.Good));
    }

    private void BuildQualityMetrics()
    {
        QualityMetrics.Clear();
        QualityMetrics.Add(CreateSpeedMetric(
            "空闲延迟",
            Icon.Clock,
            FormatMilliseconds(IdleLatencyMs),
            HealthStandards.IdleLatency(IdleLatencyMs),
            "影响网页首响应、远程控制、游戏和通话的即时感受；越低越好。"));
        QualityMetrics.Add(CreateSpeedMetric(
            "下载时响应",
            Icon.ArrowDown,
            DownloadResponsivenessMetric.Value,
            ResponsivenessAssessment(_downloadResponsivenessRpm, "下载时响应"),
            "反映下载占满带宽时，网页、通话和交互请求是否仍能及时处理。"));
        QualityMetrics.Add(CreateSpeedMetric(
            "上传时响应",
            Icon.ArrowUp,
            UploadResponsivenessMetric.Value,
            ResponsivenessAssessment(_uploadResponsivenessRpm, "上传时响应"),
            "反映上传占满带宽时是否出现缓冲膨胀，影响会议、游戏和日常浏览。"));
    }

    private void BuildTransferEstimates(SpeedTestReport report)
    {
        TransferEstimates.Clear();
        TransferEstimates.Add(new(
            Icon.ArrowDown,
            "下载 1 GB",
            DurationText(report.DownloadSecondsForGigabytes(1))));
        TransferEstimates.Add(new(
            Icon.ArrowDown,
            "下载 10 GB",
            DurationText(report.DownloadSecondsForGigabytes(10))));
        TransferEstimates.Add(new(
            Icon.ArrowUp,
            "上传 1 GB",
            DurationText(report.UploadSecondsForGigabytes(1))));
    }

    private void BuildMeasurementDetails(SpeedTestReport report)
    {
        MeasurementDetails.Clear();
        MeasurementDetails.Add(new("测速链路", report.Route.DisplayName()));
        MeasurementDetails.Add(new(
            "测速时长",
            $"{report.DurationPreset.DisplayName()} · 吞吐阶段每方向约 {report.DurationPreset.PhaseRuntimeSeconds()} 秒"));
        MeasurementDetails.Add(new("网络路径", report.PathDescription ?? report.Route.Detail()));
        MeasurementDetails.Add(new("Wi-Fi 上下文接口", report.SampledInterface ?? "未取得接口信息"));
        MeasurementDetails.Add(new(
            "绑定接口",
            report.MeasuredInterface
                ?? (report.Route == SpeedTestRoute.CurrentPath
                    ? "未固定，跟随系统默认路由"
                    : report.RequestedInterface ?? "未取得接口信息")));
        MeasurementDetails.Add(new("测试节点", report.Endpoint ?? "系统自动选择"));
        MeasurementDetails.Add(new(
            "代理状态",
            report.WasProxied ? "当前实际链路检测到系统代理" : "未检测到系统代理路径"));
        MeasurementDetails.Add(new("结果口径", "Windows 电脑到测速节点的分阶段平均有效吞吐"));
        MeasurementDetails.Add(new(
            "传输流量",
            report.TransferredMegabytes > 0 ? $"约 {report.TransferredMegabytes:0.0} MB" : "未报告"));
        MeasurementDetails.Add(new(
            "有效测试时长",
            NormalizeMeasurement(report.DurationSeconds) is { } seconds ? $"约 {seconds:0.0} 秒" : "未报告"));
    }

    private void ResetResultPresentation()
    {
        DownloadMetric = CreateDownloadMetric(null);
        UploadMetric = CreateUploadMetric(null);
        IdleLatencyMetric = CreateIdleLatencyMetric(null);
        DownloadResponsivenessMetric = CreateResponsivenessMetric("下载时响应", null);
        UploadResponsivenessMetric = CreateResponsivenessMetric("上传时响应", null);
        _downloadResponsivenessRpm = null;
        _uploadResponsivenessRpm = null;
        ThroughputMetrics.Clear();
        QualityMetrics.Clear();
        TransferEstimates.Clear();
        MeasurementDetails.Clear();
        ResultConclusion = "测速结果未生成";
        ResultContext = "--";
        ResultStatus = HealthStatusLabels.Unavailable;
        ResultIcon = Icon.Info;
        ResultStatusForeground = UiPalette.Reference;
        ResultStatusBackground = UiPalette.ReferenceSoft;
        ResultSummaryBackground = UiPalette.ReferenceSoft;
        ResultSummaryBorder = UiPalette.Reference;
        ShowInterfaceMismatch = false;
        InterfaceMismatchMessage = string.Empty;
    }

    private void NotifyPresentationStateChanged()
    {
        OnPropertyChanged(nameof(ShowResultCards));
        OnPropertyChanged(nameof(ShowCharts));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowErrorState));
        OnPropertyChanged(nameof(TrendHeading));
        NotifyPhasePresentationChanged();
    }

    private void SetCurrentSpeedPhase(SpeedTestPhase phase)
    {
        if (_currentSpeedPhase == phase) return;
        _currentSpeedPhase = phase;
        NotifyPhasePresentationChanged();
    }

    private void NotifyPhasePresentationChanged()
    {
        OnPropertyChanged(nameof(DownloadPhaseStatus));
        OnPropertyChanged(nameof(UploadPhaseStatus));
        OnPropertyChanged(nameof(DownloadPhaseStatusForeground));
        OnPropertyChanged(nameof(DownloadPhaseStatusBackground));
        OnPropertyChanged(nameof(UploadPhaseStatusForeground));
        OnPropertyChanged(nameof(UploadPhaseStatusBackground));
    }

    private string PhaseStatus(SpeedTestPhase phase)
    {
        if (IsRunning && _currentSpeedPhase == phase) return "测试中";
        return HasPhaseMeasurement(phase) ? "已采样" : IsRunning ? "等待" : "无数据";
    }

    private (IBrush foreground, IBrush background) PhaseColors(SpeedTestPhase phase)
    {
        if (IsRunning && _currentSpeedPhase == phase) return UiPalette.For(StatusTone.Accent);
        return HasPhaseMeasurement(phase)
            ? UiPalette.For(StatusTone.Accent)
            : UiPalette.For(StatusTone.Reference);
    }

    private bool HasPhaseMeasurement(SpeedTestPhase phase) => phase switch
    {
        SpeedTestPhase.Download => DownloadSamples.Count > 0 || HasResult && DownloadMbps is not null,
        SpeedTestPhase.Upload => UploadSamples.Count > 0 || HasResult && UploadMbps is not null,
        _ => false
    };

    private static void AddLiveSample(
        ObservableCollection<SpeedSampleViewModel> samples,
        SpeedTestSample sample,
        Action<double?> updateValue)
    {
        var value = NormalizeMeasurement(sample.Mbps);
        if (value is null) return;

        samples.Add(new SpeedSampleViewModel(sample.ElapsedSeconds, value.Value));
        updateValue(value);
    }

    private static SpeedMetricCardViewModel CreateSpeedMetric(
        string title,
        Icon icon,
        string value,
        MetricAssessment assessment,
        string impact) =>
        new(
            title,
            icon,
            value,
            null,
            assessment,
            impact,
            ToneFor(assessment.Grade));

    private static MetricCardViewModel CreateDownloadMetric(double? value) =>
        CreateMetric("下载速度", Icon.ArrowDown, FormatMbps(value), HealthStandards.Download(value));

    private static MetricCardViewModel CreateUploadMetric(double? value) =>
        CreateMetric("上传速度", Icon.ArrowUp, FormatMbps(value), HealthStandards.Upload(value));

    private static MetricCardViewModel CreateIdleLatencyMetric(double? value) =>
        CreateMetric("空闲延迟", Icon.Clock, FormatMilliseconds(value), HealthStandards.IdleLatency(value));

    private static MetricCardViewModel CreateResponsivenessMetric(string subject, double? value) =>
        CreateMetric(subject, Icon.Gauge, FormatRpm(value), ResponsivenessAssessment(value, subject));

    private static MetricAssessment ResponsivenessAssessment(double? value, string subject) =>
        value is null
            ? new MetricAssessment(
                HealthGrade.Unavailable,
                HealthStatusLabels.Unavailable,
                $"本次未测得{subject}；负载期间成功探针不足，未估算 RPM。",
                HealthStandards.ResponsivenessStandard)
            : HealthStandards.Responsiveness(value, subject);

    private static MetricCardViewModel CreateMetric(
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

    private static StatusTone ToneFor(HealthGrade grade) => grade switch
    {
        HealthGrade.Good => StatusTone.Good,
        HealthGrade.Warning => StatusTone.Warning,
        HealthGrade.Critical => StatusTone.Critical,
        _ => StatusTone.Reference
    };

    private static string SpeedConclusion(
        HealthGrade grade,
        string statusLabel,
        IEnumerable<(string Title, MetricAssessment Assessment)> namedAssessments)
    {
        var issueTitles = namedAssessments
            .Where(item => item.Assessment.Grade == grade && grade is HealthGrade.Warning or HealthGrade.Critical)
            .Select(item => item.Title)
            .ToArray();
        var issues = issueTitles.Length == 0 ? "已测指标" : string.Join("、", issueTitles);

        return grade switch
        {
            HealthGrade.Good when statusLabel == HealthStatusLabels.Excellent =>
                "测速结果优秀，已取得的吞吐与响应指标都很理想",
            HealthGrade.Good => "测速结果正常，已取得的吞吐与响应指标处于正常范围",
            HealthGrade.Warning => $"测速结果需要注意：{issues}",
            HealthGrade.Critical => $"测速结果存在严重异常：{issues}",
            _ => "测速结果未取得足够证据"
        };
    }

    private static double? NormalizeMeasurement(double? value) =>
        value is >= 0 && double.IsFinite(value.Value) ? value : null;

    private static string FormatMbps(double? value) =>
        value is { } measured ? $"{measured:0.0} Mbps" : "--";

    private static string FormatMegabytesPerSecond(double? value, bool approximation = false) =>
        value is { } measured
            ? $"{(approximation ? "≈ " : string.Empty)}{measured / 8:0.00} MB/s"
            : "--";

    private static string FormatMilliseconds(double? value) =>
        value is { } measured ? $"{measured:0.0} ms" : "--";

    private static string FormatRpm(double? value) =>
        value is { } measured ? $"{measured:0} RPM" : "--";

    private static string DurationText(double? seconds)
    {
        if (NormalizeMeasurement(seconds) is not { } measured) return "--";
        var roundedSeconds = (int)Math.Round(measured);
        if (roundedSeconds < 60) return $"约 {roundedSeconds} 秒";
        if (roundedSeconds < 3_600)
        {
            var minutes = roundedSeconds / 60;
            var remainder = roundedSeconds % 60;
            return remainder == 0 ? $"约 {minutes} 分钟" : $"约 {minutes} 分 {remainder} 秒";
        }

        var hours = roundedSeconds / 3_600;
        var remainingMinutes = roundedSeconds % 3_600 / 60;
        return remainingMinutes == 0
            ? $"约 {hours} 小时"
            : $"约 {hours} 小时 {remainingMinutes} 分";
    }

    private static string FormatTransferSummary(SpeedTestReport report)
    {
        var hasTraffic = report.DownloadedBytes is not null || report.UploadedBytes is not null;
        var traffic = hasTraffic ? $"总流量约 {report.TransferredMegabytes:0.0} MB" : "流量未记录";
        var duration = NormalizeMeasurement(report.DurationSeconds) is { } seconds
            ? $"{seconds:0.0} 秒"
            : "时长未记录";
        return $"{traffic} · {duration}";
    }
}
