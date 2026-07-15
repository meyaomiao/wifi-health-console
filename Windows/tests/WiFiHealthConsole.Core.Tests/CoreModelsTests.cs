using WiFiHealthConsole.Core;

namespace WiFiHealthConsole.Core.Tests;

public sealed class CoreModelsTests
{
    [Fact]
    public void ObservedRequiresCallersToRespectAvailability()
    {
        var measured = Observed<int>.Available(-55, EvidenceSource.NativeWlanApi);
        var unavailable = Observed<int>.Unavailable(
            MetricAvailability.NotSupported,
            EvidenceSource.NativeWlanApi,
            "API 不提供");

        Assert.True(measured.TryGetValue(out var measuredValue));
        Assert.Equal(-55, measuredValue);
        Assert.False(unavailable.TryGetValue(out _));
        Assert.False(unavailable.HasValue);
        Assert.Throws<ArgumentException>(() => Observed<int>.Unavailable(MetricAvailability.Available));
    }

    [Fact]
    public void WifiSnapshotDoesNotDeriveSnrFromRssiAndNoise()
    {
        var snapshot = new WifiSnapshot
        {
            RssiDbm = Observed<int>.Available(-50, EvidenceSource.Mock),
            NoiseDbm = Observed<int>.Available(-90, EvidenceSource.RouterTelemetry),
        };

        Assert.False(snapshot.SnrDb.TryGetValue(out _));
        Assert.Equal(MetricAvailability.NotSupported, snapshot.SnrDb.Availability);
    }

    [Fact]
    public void DiagnosticReportUsesPartialCompletionInsteadOfClaimingAllNormal()
    {
        var report = new DiagnosticReport
        {
            StartedAt = DateTimeOffset.Now.AddMinutes(-1),
            CompletedAt = DateTimeOffset.Now,
            BaselineDescription = "测试",
            Layers =
            [
                Layer(DiagnosticLayer.Wireless, HealthGrade.Good, "正常"),
                Layer(DiagnosticLayer.LocalNetwork, HealthGrade.Unavailable, "未检测"),
                Layer(DiagnosticLayer.Internet, HealthGrade.Good, "正常"),
                Layer(DiagnosticLayer.ProxyVpn, HealthGrade.Good, "正常"),
            ],
        };

        Assert.Equal(HealthGrade.Unavailable, report.OverallGrade);
        Assert.Equal("部分完成", report.OverallStatusLabel);
        Assert.True(report.HasUnavailableLayer);
    }

    [Fact]
    public void EmptyDiagnosticReportIsUnavailable()
    {
        var report = new DiagnosticReport
        {
            BaselineDescription = "尚未开始",
        };

        Assert.Equal(HealthGrade.Unavailable, report.OverallGrade);
        Assert.Equal("未检测", report.OverallStatusLabel);
    }

    [Fact]
    public void DiagnosticMetricCopiesSingleAssessmentAtomically()
    {
        var assessment = HealthStandards.Rssi(-68);
        var metric = DiagnosticMetric.FromAssessment(
            "rssi",
            "RSSI",
            "-68 dBm",
            assessment,
            "影响覆盖和稳定性");

        Assert.Equal(assessment, metric.Assessment);
        Assert.Equal("注意", metric.StatusLabel);
        Assert.Equal(HealthGrade.Warning, metric.Grade);
    }

    [Fact]
    public void SpeedTestReportShowsMbpsAndUserVisibleMegabytesPerSecond()
    {
        var report = new SpeedTestReport
        {
            DownloadBitsPerSecond = 800_000_000,
            UploadBitsPerSecond = 80_000_000,
            DownloadedBytes = 1_000_000_000,
            UploadedBytes = 100_000_000,
        };

        Assert.Equal(800, report.DownloadMbps);
        Assert.Equal(100, report.DownloadMegabytesPerSecond);
        Assert.Equal(80, report.UploadMbps);
        Assert.Equal(10, report.UploadMegabytesPerSecond);
        Assert.Equal(1_100, report.TransferredMegabytes);
        Assert.Equal(10, report.DownloadSecondsForGigabytes(1));
    }

    [Theory]
    [InlineData(SpeedTestDurationPreset.Standard, 20, 40, "标准 · 吞吐约 40 秒")]
    [InlineData(SpeedTestDurationPreset.Stable, 30, 60, "稳定 · 吞吐约 60 秒")]
    public void SpeedTestDurationDescribesOnlyTheThroughputStages(
        SpeedTestDurationPreset preset,
        int phaseSeconds,
        int throughputSeconds,
        string segmentTitle)
    {
        Assert.Equal(phaseSeconds, preset.PhaseRuntimeSeconds());
        Assert.Equal(throughputSeconds, preset.ThroughputStageSeconds());
        Assert.Equal(segmentTitle, preset.SegmentTitle());
        Assert.Contains("延迟预检", preset.Detail());
        Assert.DoesNotContain("最长", preset.Detail());
    }

    private static LayerResult Layer(DiagnosticLayer layer, HealthGrade grade, string label) => new(
        layer,
        grade,
        label,
        label,
        [],
        [],
        "测试");
}
