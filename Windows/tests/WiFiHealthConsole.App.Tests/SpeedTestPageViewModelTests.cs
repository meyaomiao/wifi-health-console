using WiFiHealthConsole.App.Services.Speed;
using WiFiHealthConsole.App.ViewModels;
using WiFiHealthConsole.Core;

namespace WiFiHealthConsole.App.Tests;

public sealed class SpeedTestPageViewModelTests
{
    [Fact]
    public void InitialStateKeepsUnavailableMeasurementsOutOfNormalResults()
    {
        var viewModel = new SpeedTestPageViewModel(new ImmediateSpeedTestProvider(new SpeedTestReport()));

        Assert.False(viewModel.HasResult);
        Assert.True(viewModel.ShowEmptyState);
        Assert.False(viewModel.ShowResultCards);
        Assert.False(viewModel.ShowCharts);
        Assert.Equal("--", viewModel.DownloadMetric.Value);
        Assert.Equal("--", viewModel.UploadMetric.Value);
        Assert.Equal("--", viewModel.IdleLatencyMetric.Value);
        AssertAssessment(HealthStandards.Download(null), viewModel.DownloadMetric);
        AssertAssessment(HealthStandards.Upload(null), viewModel.UploadMetric);
        AssertAssessment(HealthStandards.IdleLatency(null), viewModel.IdleLatencyMetric);
    }

    [Fact]
    public void DurationSelectionKeepsBothChartsOnTheConfiguredPhaseWindow()
    {
        var viewModel = new SpeedTestPageViewModel(new ImmediateSpeedTestProvider(new SpeedTestReport()));

        Assert.Equal(20, viewModel.ChartMaximumSeconds);

        viewModel.SelectDurationCommand.Execute("stable");

        Assert.Equal(30, viewModel.ChartMaximumSeconds);
    }

    [Fact]
    public async Task RunningStateShowsLiveAreaWithoutShowingFinalAssessmentOrEmptyState()
    {
        var provider = new BlockingSpeedTestProvider();
        var viewModel = new SpeedTestPageViewModel(provider);

        var run = viewModel.RunSpeedTestCommand.ExecuteAsync(null);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(viewModel.IsRunning);
        Assert.True(viewModel.ShowCharts);
        Assert.False(viewModel.ShowResultCards);
        Assert.False(viewModel.ShowEmptyState);
        Assert.False(viewModel.ShowErrorState);
        Assert.Equal(HealthStatusLabels.Unavailable, viewModel.DownloadMetric.Status);
        Assert.Equal(HealthStatusLabels.Unavailable, viewModel.UploadMetric.Status);
        Assert.Equal(HealthStatusLabels.Unavailable, viewModel.IdleLatencyMetric.Status);
        Assert.Equal("等待", viewModel.DownloadPhaseStatus);
        Assert.Equal("等待", viewModel.UploadPhaseStatus);

        provider.Complete(Report(downloadMbps: 12, uploadMbps: 3, idleLatencyMs: null));
        await run;

        Assert.False(viewModel.IsRunning);
        Assert.True(viewModel.HasResult);
        Assert.True(viewModel.ShowResultCards);
        Assert.False(viewModel.ShowEmptyState);
        Assert.Equal("已采样", viewModel.DownloadPhaseStatus);
        Assert.Equal("已采样", viewModel.UploadPhaseStatus);
        Assert.Same(UiPalette.Accent, viewModel.DownloadPhaseStatusForeground);
        Assert.Same(UiPalette.Accent, viewModel.UploadPhaseStatusForeground);
        Assert.Equal("--", viewModel.IdleLatencyMetric.Value);
        AssertAssessment(HealthStandards.IdleLatency(null), viewModel.IdleLatencyMetric);
    }

    [Fact]
    public async Task CompletedResultUsesCoreAssessmentTextAndThresholds()
    {
        var report = Report(downloadMbps: 12, uploadMbps: 3, idleLatencyMs: 55);
        var viewModel = new SpeedTestPageViewModel(new ImmediateSpeedTestProvider(report));

        await viewModel.RunSpeedTestCommand.ExecuteAsync(null);

        Assert.Equal("12.0 Mbps", viewModel.DownloadMetric.Value);
        Assert.Equal("3.0 Mbps", viewModel.UploadMetric.Value);
        Assert.Equal("55.0 ms", viewModel.IdleLatencyMetric.Value);
        AssertAssessment(HealthStandards.Download(12), viewModel.DownloadMetric);
        AssertAssessment(HealthStandards.Upload(3), viewModel.UploadMetric);
        AssertAssessment(HealthStandards.IdleLatency(55), viewModel.IdleLatencyMetric);
    }

    [Fact]
    public async Task CompletedResultBuildsTheFullMacParityEvidenceSections()
    {
        var report = Report(downloadMbps: 80, uploadMbps: 20, idleLatencyMs: 30) with
        {
            Route = SpeedTestRoute.DirectWiFiBaseline,
            DurationPreset = SpeedTestDurationPreset.Stable,
            RequestedInterface = "Wi-Fi 2",
            SampledInterface = "Wi-Fi 2",
            SampledInterfaceId = "wifi-adapter-2",
            MeasuredInterface = "Wi-Fi 2",
            MeasuredInterfaceId = "wifi-adapter-2",
            PathDescription = "已绑定 Wi-Fi 接口 Wi-Fi 2",
            DownloadResponsivenessRpm = 742,
            UploadResponsivenessRpm = 468,
            DownloadedBytes = 600_000_000,
            UploadedBytes = 100_000_000,
            WasProxied = false
        };
        var viewModel = new SpeedTestPageViewModel(new ImmediateSpeedTestProvider(report));

        await viewModel.RunSpeedTestCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.ThroughputMetrics.Count);
        Assert.Equal(3, viewModel.QualityMetrics.Count);
        Assert.Equal(3, viewModel.TransferEstimates.Count);
        Assert.Equal(10, viewModel.MeasurementDetails.Count);
        Assert.Equal("≈ 10.00 MB/s", viewModel.ThroughputMetrics[0].SecondaryValue);
        Assert.Equal("约 1 分 40 秒", viewModel.TransferEstimates[0].Value);
        Assert.Contains("直连 Wi-Fi 基线", viewModel.ResultContext);
        Assert.Contains("稳定模式", viewModel.ResultContext);
        Assert.False(viewModel.ShowInterfaceMismatch);
        Assert.Contains(
            viewModel.MeasurementDetails,
            detail => detail.Title == "网络路径" && detail.Value.Contains("已绑定 Wi-Fi 接口"));
        Assert.Contains(
            viewModel.MeasurementDetails,
            detail => detail.Title == "绑定接口" && detail.Value == "Wi-Fi 2");
    }

    [Fact]
    public async Task CurrentDefaultRouteDescriptionIsNotComparedWithARealWifiIdentity()
    {
        var report = Report(downloadMbps: 80, uploadMbps: 20, idleLatencyMs: 30) with
        {
            Route = SpeedTestRoute.CurrentPath,
            SampledInterface = "Wi-Fi",
            SampledInterfaceId = "wifi-adapter",
            MeasuredInterface = null,
            MeasuredInterfaceId = null,
            PathDescription = "跟随 Windows 系统当前默认路由"
        };
        var viewModel = new SpeedTestPageViewModel(new ImmediateSpeedTestProvider(report));

        await viewModel.RunSpeedTestCommand.ExecuteAsync(null);

        Assert.False(viewModel.ShowInterfaceMismatch);
        Assert.Empty(viewModel.InterfaceMismatchMessage);
        Assert.Contains(
            viewModel.MeasurementDetails,
            detail => detail.Title == "网络路径" && detail.Value.Contains("默认路由"));
        Assert.Contains(
            viewModel.MeasurementDetails,
            detail => detail.Title == "绑定接口" && detail.Value.Contains("未固定"));
    }

    [Fact]
    public async Task MissingResponsivenessSamplesAreExplicitlyUnmeasured()
    {
        var report = Report(downloadMbps: 80, uploadMbps: 20, idleLatencyMs: 30);
        var viewModel = new SpeedTestPageViewModel(new ImmediateSpeedTestProvider(report));

        await viewModel.RunSpeedTestCommand.ExecuteAsync(null);

        Assert.Equal("--", viewModel.QualityMetrics[1].Value);
        Assert.Equal(HealthStatusLabels.Unavailable, viewModel.QualityMetrics[1].Status);
        Assert.Contains("本次未测得下载时响应", viewModel.QualityMetrics[1].Interpretation);
        Assert.Contains("未估算 RPM", viewModel.QualityMetrics[1].Interpretation);
        Assert.Equal(HealthStatusLabels.Partial, viewModel.ResultStatus);
        Assert.Contains("仍有指标未测得", viewModel.ResultConclusion);
        Assert.Same(UiPalette.Reference, viewModel.ResultStatusForeground);
    }

    [Fact]
    public async Task DifferentRealInterfaceIdentitiesProduceAMismatchWarning()
    {
        var report = Report(downloadMbps: 80, uploadMbps: 20, idleLatencyMs: 30) with
        {
            Route = SpeedTestRoute.DirectWiFiBaseline,
            SampledInterface = "Wi-Fi",
            SampledInterfaceId = "wifi-adapter-a",
            MeasuredInterface = "Wi-Fi 2",
            MeasuredInterfaceId = "wifi-adapter-b",
            PathDescription = "已绑定 Wi-Fi 接口 Wi-Fi 2"
        };
        var viewModel = new SpeedTestPageViewModel(new ImmediateSpeedTestProvider(report));

        await viewModel.RunSpeedTestCommand.ExecuteAsync(null);

        Assert.True(viewModel.ShowInterfaceMismatch);
        Assert.Contains("Wi-Fi 上下文接口为 Wi-Fi", viewModel.InterfaceMismatchMessage);
        Assert.Contains("实际绑定接口为 Wi-Fi 2", viewModel.InterfaceMismatchMessage);
    }

    [Fact]
    public async Task CompletedChartKeepsTheMeasuredPresetWhenNextPresetChanges()
    {
        var report = Report(downloadMbps: 80, uploadMbps: 20, idleLatencyMs: 30) with
        {
            DurationPreset = SpeedTestDurationPreset.Stable
        };
        var viewModel = new SpeedTestPageViewModel(new ImmediateSpeedTestProvider(report));
        viewModel.SelectDurationCommand.Execute("stable");

        await viewModel.RunSpeedTestCommand.ExecuteAsync(null);
        Assert.Equal(30, viewModel.ChartMaximumSeconds);

        viewModel.SelectDurationCommand.Execute("standard");

        Assert.True(viewModel.IsStandardDurationSelected);
        Assert.Equal(30, viewModel.ChartMaximumSeconds);
    }

    [Fact]
    public void RouteDurationAndWifiContextDriveVisibleExplanations()
    {
        var viewModel = new SpeedTestPageViewModel(new ImmediateSpeedTestProvider(new SpeedTestReport()));
        viewModel.UpdateWifiContext(new WifiSnapshot
        {
            InterfaceName = "Wi-Fi 2",
            TransmitRateMbps = Observed<double>.Available(1_201, EvidenceSource.Mock, "测试数据")
        });

        viewModel.SelectRouteCommand.Execute("direct");
        viewModel.SelectDurationCommand.Execute("stable");

        Assert.True(viewModel.ShowDirectWifiInterface);
        Assert.Contains("Wi-Fi 2", viewModel.DirectWifiInterfaceDetail);
        Assert.Contains("对比基线", viewModel.RouteDetail);
        Assert.Contains("每个方向各测约 30 秒", viewModel.DurationDetail);
        Assert.Contains("延迟预检", viewModel.DurationDetail);
        Assert.Contains("吞吐阶段约 60 秒", viewModel.MaximumDurationHint);
        Assert.DoesNotContain("最长", viewModel.MaximumDurationHint);
        Assert.Contains("手机热点", viewModel.TrafficWarning);
        Assert.Equal("1201 Mbps", viewModel.NegotiatedRateDisplay);
        Assert.Equal(30, viewModel.ChartMaximumSeconds);
    }

    [Fact]
    public async Task FailedRunShowsOnlyErrorStateAndNoFinalCards()
    {
        var viewModel = new SpeedTestPageViewModel(new ThrowingSpeedTestProvider());

        await viewModel.RunSpeedTestCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsRunning);
        Assert.False(viewModel.HasResult);
        Assert.False(viewModel.ShowEmptyState);
        Assert.True(viewModel.ShowErrorState);
        Assert.False(viewModel.ShowResultCards);
        Assert.False(viewModel.ShowCharts);
        Assert.Equal("模拟测速失败", viewModel.ErrorMessage);
    }

    private static SpeedTestReport Report(double downloadMbps, double uploadMbps, double? idleLatencyMs) => new()
    {
        DownloadBitsPerSecond = downloadMbps * 1_000_000,
        UploadBitsPerSecond = uploadMbps * 1_000_000,
        IdleLatencyMs = idleLatencyMs,
        DownloadedBytes = 10_000_000,
        UploadedBytes = 2_000_000,
        DurationSeconds = 40,
        Endpoint = "测试节点"
    };

    private static void AssertAssessment(MetricAssessment expected, MetricCardViewModel actual)
    {
        Assert.Equal(expected.StatusLabel, actual.Status);
        Assert.Equal(expected.Interpretation, actual.Explanation);
        Assert.Equal(expected.Standard, actual.Standard);
    }

    private sealed class ImmediateSpeedTestProvider(SpeedTestReport report) : ISpeedTestProvider
    {
        public string ProviderName => "测试测速服务";

        public Task<SpeedTestReport> RunAsync(
            SpeedTestRequest request,
            IProgress<SpeedTestProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(report);
    }

    private sealed class BlockingSpeedTestProvider : ISpeedTestProvider
    {
        private readonly TaskCompletionSource<SpeedTestReport> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string ProviderName => "阻塞测试测速服务";

        public Task<SpeedTestReport> RunAsync(
            SpeedTestRequest request,
            IProgress<SpeedTestProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return _completion.Task;
        }

        public void Complete(SpeedTestReport report) => _completion.TrySetResult(report);
    }

    private sealed class ThrowingSpeedTestProvider : ISpeedTestProvider
    {
        public string ProviderName => "失败测试测速服务";

        public Task<SpeedTestReport> RunAsync(
            SpeedTestRequest request,
            IProgress<SpeedTestProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<SpeedTestReport>(new SpeedTestUnavailableException("模拟测速失败"));
    }
}
