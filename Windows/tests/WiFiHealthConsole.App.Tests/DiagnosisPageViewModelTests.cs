using WiFiHealthConsole.App.Services.Diagnostics;
using WiFiHealthConsole.App.ViewModels;
using WiFiHealthConsole.Core;

namespace WiFiHealthConsole.App.Tests;

public sealed class DiagnosisPageViewModelTests
{
    [Fact]
    public async Task CompletedReportPreservesCoreSummaryAndMetricEvidence()
    {
        var assessment = HealthStandards.GatewayJitter(12);
        var metric = DiagnosticMetric.FromAssessment(
            "gateway-jitter",
            "网关抖动",
            "12.0 ms",
            assessment,
            "影响实时通话和远程控制。");
        var layer = new LayerResult(
            DiagnosticLayer.LocalNetwork,
            assessment.Grade,
            assessment.StatusLabel,
            "局域网抖动需要关注。",
            ["已自动检测默认网关。"],
            [metric],
            "靠近路由器后回测。");
        var report = new DiagnosticReport
        {
            StartedAt = DateTimeOffset.Now.AddMinutes(-1),
            CompletedAt = DateTimeOffset.Now,
            BaselineDescription = "DNS 与 HTTPS 使用无系统代理基线。",
            Layers = [layer]
        };
        var viewModel = new DiagnosisPageViewModel(new ImmediateDiagnosticService(report));

        await viewModel.RunDiagnosisAsync();

        Assert.True(viewModel.HasReport);
        Assert.Equal(report.OverallStatusLabel, viewModel.OverallStatus);
        Assert.Equal(report.BaselineDescription, viewModel.BaselineDescription);
        var mappedLayer = Assert.Single(viewModel.Layers);
        Assert.Equal(layer.StatusLabel, mappedLayer.Status);
        var mappedMetric = Assert.Single(mappedLayer.Metrics);
        Assert.Equal(metric.StatusLabel, mappedMetric.Status);
        Assert.Equal(metric.Interpretation, mappedMetric.Interpretation);
        Assert.Equal(metric.Impact, mappedMetric.Impact);
        Assert.Equal(metric.Standard, mappedMetric.Standard);
    }

    [Fact]
    public async Task FailedReportExposesTheSharedErrorBannerState()
    {
        var viewModel = new DiagnosisPageViewModel(new FailingDiagnosticService());

        await viewModel.RunDiagnosisAsync();

        Assert.False(viewModel.HasReport);
        Assert.True(viewModel.HasError);
        Assert.Equal("测试体检失败", viewModel.ErrorMessage);
    }

    private sealed class ImmediateDiagnosticService(DiagnosticReport report) : INetworkDiagnosticService
    {
        public Task<DiagnosticReport> RunAsync(
            NetworkDiagnosticOptions? options = null,
            IProgress<NetworkDiagnosticProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(report);
    }

    private sealed class FailingDiagnosticService : INetworkDiagnosticService
    {
        public Task<DiagnosticReport> RunAsync(
            NetworkDiagnosticOptions? options = null,
            IProgress<NetworkDiagnosticProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<DiagnosticReport>(new InvalidOperationException("测试体检失败"));
    }
}
