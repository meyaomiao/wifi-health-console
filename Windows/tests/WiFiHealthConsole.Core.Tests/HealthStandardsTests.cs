using WiFiHealthConsole.Core;

namespace WiFiHealthConsole.Core.Tests;

public sealed class HealthStandardsTests
{
    [Theory]
    [InlineData(null, HealthGrade.Unavailable, "未检测")]
    [InlineData(-54, HealthGrade.Good, "优秀")]
    [InlineData(-55, HealthGrade.Good, "正常")]
    [InlineData(-67, HealthGrade.Good, "正常")]
    [InlineData(-68, HealthGrade.Warning, "注意")]
    [InlineData(-75, HealthGrade.Warning, "注意")]
    [InlineData(-76, HealthGrade.Critical, "严重")]
    public void RssiBoundariesMatchMacEdition(int? value, HealthGrade grade, string label) =>
        AssertAssessment(HealthStandards.Rssi(value), grade, label);

    [Theory]
    [InlineData(null, HealthGrade.Unavailable, "未检测")]
    [InlineData(40, HealthGrade.Good, "优秀")]
    [InlineData(39, HealthGrade.Good, "正常")]
    [InlineData(30, HealthGrade.Good, "正常")]
    [InlineData(29, HealthGrade.Warning, "注意")]
    [InlineData(20, HealthGrade.Warning, "注意")]
    [InlineData(19, HealthGrade.Critical, "严重")]
    public void SnrBoundariesMatchMacEdition(int? value, HealthGrade grade, string label) =>
        AssertAssessment(HealthStandards.Snr(value), grade, label);

    [Fact]
    public void NotSupportedObservationIsNotReportedAsMissing()
    {
        var observation = Observed<int>.Unavailable(
            MetricAvailability.NotSupported,
            EvidenceSource.NativeWlanApi,
            "Windows 公共 WLAN API 不提供 SNR。");

        AssertAssessment(HealthStandards.Snr(observation), HealthGrade.Unavailable, "不支持");
    }

    [Theory]
    [InlineData(null, HealthGrade.Unavailable, "未检测")]
    [InlineData(50.0, HealthGrade.Good, "正常")]
    [InlineData(50.1, HealthGrade.Warning, "注意")]
    [InlineData(80.0, HealthGrade.Warning, "注意")]
    [InlineData(80.1, HealthGrade.Critical, "严重")]
    public void CcaBoundariesMatchMacEdition(double? value, HealthGrade grade, string label) =>
        AssertAssessment(HealthStandards.Cca(value), grade, label);

    [Theory]
    [InlineData(null, HealthGrade.Unavailable, "未检测")]
    [InlineData(10.0, HealthGrade.Good, "优秀")]
    [InlineData(10.1, HealthGrade.Good, "正常")]
    [InlineData(30.0, HealthGrade.Good, "正常")]
    [InlineData(30.1, HealthGrade.Warning, "注意")]
    [InlineData(100.0, HealthGrade.Warning, "注意")]
    [InlineData(100.1, HealthGrade.Critical, "严重")]
    public void GatewayLatencyBoundaries(double? value, HealthGrade grade, string label) =>
        AssertAssessment(HealthStandards.GatewayLatency(value), grade, label);

    [Theory]
    [InlineData(null, HealthGrade.Unavailable, "未检测")]
    [InlineData(10.0, HealthGrade.Good, "正常")]
    [InlineData(10.1, HealthGrade.Warning, "注意")]
    [InlineData(30.0, HealthGrade.Warning, "注意")]
    [InlineData(30.1, HealthGrade.Critical, "严重")]
    public void GatewayJitterBoundaries(double? value, HealthGrade grade, string label) =>
        AssertAssessment(HealthStandards.GatewayJitter(value), grade, label);

    [Theory]
    [InlineData(null, HealthGrade.Unavailable, "未检测")]
    [InlineData(1.0, HealthGrade.Good, "正常")]
    [InlineData(1.1, HealthGrade.Warning, "注意")]
    [InlineData(5.0, HealthGrade.Warning, "注意")]
    [InlineData(5.1, HealthGrade.Critical, "严重")]
    public void GatewayLossBoundaries(double? value, HealthGrade grade, string label) =>
        AssertAssessment(HealthStandards.GatewayLoss(value), grade, label);

    [Theory]
    [InlineData(null, HealthGrade.Unavailable, "未检测")]
    [InlineData(80.0, HealthGrade.Good, "正常")]
    [InlineData(80.1, HealthGrade.Warning, "注意")]
    [InlineData(150.0, HealthGrade.Warning, "注意")]
    [InlineData(150.1, HealthGrade.Critical, "严重")]
    public void InternetLatencyBoundaries(double? value, HealthGrade grade, string label) =>
        AssertAssessment(HealthStandards.InternetLatency(value), grade, label);

    [Fact]
    public void DnsBoundariesAndFailureAreConsistent()
    {
        AssertAssessment(HealthStandards.Dns(Endpoint(true, null)), HealthGrade.Unavailable, "未检测");
        AssertAssessment(HealthStandards.Dns(Endpoint(true, 100)), HealthGrade.Good, "正常");
        AssertAssessment(HealthStandards.Dns(Endpoint(true, 100.1)), HealthGrade.Warning, "注意");
        AssertAssessment(HealthStandards.Dns(Endpoint(true, 300)), HealthGrade.Warning, "注意");
        AssertAssessment(HealthStandards.Dns(Endpoint(true, 300.1)), HealthGrade.Critical, "严重");
        AssertAssessment(HealthStandards.Dns(Endpoint(false, null)), HealthGrade.Critical, "严重");
    }

    [Fact]
    public void HttpsBoundariesAndFailureAreConsistent()
    {
        AssertAssessment(HealthStandards.Https(Endpoint(true, null)), HealthGrade.Unavailable, "未检测");
        AssertAssessment(HealthStandards.Https(Endpoint(true, 800)), HealthGrade.Good, "正常");
        AssertAssessment(HealthStandards.Https(Endpoint(true, 800.1)), HealthGrade.Warning, "注意");
        AssertAssessment(HealthStandards.Https(Endpoint(true, 2_000)), HealthGrade.Warning, "注意");
        AssertAssessment(HealthStandards.Https(Endpoint(true, 2_000.1)), HealthGrade.Critical, "严重");
        AssertAssessment(HealthStandards.Https(Endpoint(false, null)), HealthGrade.Critical, "严重");
    }

    [Theory]
    [InlineData(null, HealthGrade.Unavailable, "未检测")]
    [InlineData(100.0, HealthGrade.Good, "优秀")]
    [InlineData(99.9, HealthGrade.Good, "正常")]
    [InlineData(25.0, HealthGrade.Good, "正常")]
    [InlineData(24.9, HealthGrade.Warning, "注意")]
    [InlineData(10.0, HealthGrade.Warning, "注意")]
    [InlineData(9.9, HealthGrade.Critical, "严重")]
    public void DownloadBoundaries(double? value, HealthGrade grade, string label) =>
        AssertAssessment(HealthStandards.Download(value), grade, label);

    [Theory]
    [InlineData(null, HealthGrade.Unavailable, "未检测")]
    [InlineData(20.0, HealthGrade.Good, "优秀")]
    [InlineData(19.9, HealthGrade.Good, "正常")]
    [InlineData(10.0, HealthGrade.Good, "正常")]
    [InlineData(9.9, HealthGrade.Warning, "注意")]
    [InlineData(5.0, HealthGrade.Warning, "注意")]
    [InlineData(4.9, HealthGrade.Critical, "严重")]
    public void UploadBoundaries(double? value, HealthGrade grade, string label) =>
        AssertAssessment(HealthStandards.Upload(value), grade, label);

    [Theory]
    [InlineData(null, HealthGrade.Unavailable, "未检测")]
    [InlineData(40.0, HealthGrade.Good, "正常")]
    [InlineData(40.1, HealthGrade.Warning, "注意")]
    [InlineData(100.0, HealthGrade.Warning, "注意")]
    [InlineData(100.1, HealthGrade.Critical, "严重")]
    public void IdleLatencyBoundaries(double? value, HealthGrade grade, string label) =>
        AssertAssessment(HealthStandards.IdleLatency(value), grade, label);

    [Theory]
    [InlineData(null, HealthGrade.Unavailable, "未检测")]
    [InlineData(600.0, HealthGrade.Good, "优秀")]
    [InlineData(599.0, HealthGrade.Warning, "注意")]
    [InlineData(200.0, HealthGrade.Warning, "注意")]
    [InlineData(199.0, HealthGrade.Critical, "严重")]
    public void ResponsivenessBoundaries(double? value, HealthGrade grade, string label) =>
        AssertAssessment(HealthStandards.Responsiveness(value, "下载时响应"), grade, label);

    [Fact]
    public void PublicIcmpLossNeverBecomesCriticalByItself()
    {
        AssertAssessment(HealthStandards.PublicIcmpLoss(null), HealthGrade.Unavailable, "未检测");
        AssertAssessment(HealthStandards.PublicIcmpLoss(10), HealthGrade.Good, "正常");

        foreach (var loss in new[] { 10.1, 50, 51, 100, 1_000 })
        {
            var assessment = HealthStandards.PublicIcmpLoss(loss);
            AssertAssessment(assessment, HealthGrade.Warning, "注意");
            Assert.NotEqual(HealthGrade.Critical, assessment.Grade);
        }
    }

    [Fact]
    public void WindowsUnavailableRadioMetricsAreNotEstimated()
    {
        var snapshot = new WifiSnapshot
        {
            IsConnected = true,
            RssiDbm = Observed<int>.Available(-52, EvidenceSource.NativeWlanApi),
            NoiseDbm = Observed<int>.Unavailable(
                MetricAvailability.NotSupported,
                EvidenceSource.NativeWlanApi,
                "驱动未提供"),
            SnrDb = Observed<int>.Unavailable(
                MetricAvailability.NotSupported,
                EvidenceSource.NativeWlanApi,
                "没有真实 SNR"),
            CcaPercent = Observed<double>.Unavailable(
                MetricAvailability.NotSupported,
                EvidenceSource.NativeWlanApi,
                "没有真实 CCA"),
        };

        AssertAssessment(HealthStandards.Noise(snapshot.NoiseDbm), HealthGrade.Unavailable, "不支持");
        AssertAssessment(HealthStandards.Snr(snapshot.SnrDb), HealthGrade.Unavailable, "不支持");
        AssertAssessment(HealthStandards.Cca(snapshot.CcaPercent), HealthGrade.Unavailable, "不支持");
        Assert.False(snapshot.SnrDb.TryGetValue(out _));
        Assert.False(snapshot.CcaPercent.TryGetValue(out _));
    }

    [Fact]
    public void StatusLabelsCannotContradictGrades()
    {
        Assert.True(HealthStandards.IsStatusLabelCompatible("优秀", HealthGrade.Good));
        Assert.True(HealthStandards.IsStatusLabelCompatible("正常", HealthGrade.Good));
        Assert.True(HealthStandards.IsStatusLabelCompatible("注意", HealthGrade.Warning));
        Assert.True(HealthStandards.IsStatusLabelCompatible("严重", HealthGrade.Critical));
        Assert.True(HealthStandards.IsStatusLabelCompatible("参考", HealthGrade.Unavailable));
        Assert.True(HealthStandards.IsStatusLabelCompatible("未检测", HealthGrade.Unavailable));

        Assert.False(HealthStandards.IsStatusLabelCompatible("正常", HealthGrade.Warning));
        Assert.False(HealthStandards.IsStatusLabelCompatible("注意", HealthGrade.Good));
        Assert.Throws<ArgumentException>(() => new MetricAssessment(
            HealthGrade.Warning,
            "正常",
            "矛盾状态",
            "测试"));
    }

    [Fact]
    public void WorstAndSummaryLabelsMatchProductSemantics()
    {
        Assert.Equal(HealthGrade.Unavailable, HealthStandards.Worst(HealthGrade.Unavailable));
        Assert.Equal(HealthGrade.Good, HealthStandards.Worst(HealthGrade.Unavailable, HealthGrade.Good));
        Assert.Equal(HealthGrade.Warning, HealthStandards.Worst(HealthGrade.Good, HealthGrade.Warning));
        Assert.Equal(HealthGrade.Critical, HealthStandards.Worst(HealthGrade.Warning, HealthGrade.Critical));

        Assert.Equal("优秀", HealthStandards.SummaryStatusLabel(
            [HealthStandards.Download(100), HealthStandards.Upload(20)]));
        Assert.Equal("正常", HealthStandards.SummaryStatusLabel(
            [HealthStandards.Download(100), HealthStandards.Upload(10)]));
        Assert.Equal("注意", HealthStandards.SummaryStatusLabel(
            [HealthStandards.Download(10), HealthStandards.Upload(20)]));
        Assert.Equal("严重", HealthStandards.SummaryStatusLabel(
            [HealthStandards.Download(9.9), HealthStandards.Upload(20)]));
        Assert.Equal("未检测", HealthStandards.SummaryStatusLabel(
            [HealthStandards.Reference(true, "参考", "参考")]));
    }

    [Fact]
    public void ChannelWidthKeepsReferenceSeparateFromFaults()
    {
        AssertAssessment(HealthStandards.ChannelWidth((int?)null, WiFiBand.Band5GHz), HealthGrade.Unavailable, "未检测");
        AssertAssessment(HealthStandards.ChannelWidth(40, WiFiBand.Band2_4GHz), HealthGrade.Warning, "注意");
        AssertAssessment(HealthStandards.ChannelWidth(160, WiFiBand.Band5GHz), HealthGrade.Unavailable, "参考");
        AssertAssessment(HealthStandards.ChannelWidth(80, WiFiBand.Band5GHz), HealthGrade.Unavailable, "参考");
    }

    private static EndpointTiming Endpoint(bool succeeded, double? milliseconds) => new()
    {
        Succeeded = succeeded,
        Milliseconds = milliseconds,
        Detail = succeeded ? "完成" : "测试失败",
    };

    private static void AssertAssessment(MetricAssessment assessment, HealthGrade grade, string label)
    {
        Assert.Equal(grade, assessment.Grade);
        Assert.Equal(label, assessment.StatusLabel);
        Assert.True(HealthStandards.IsStatusLabelCompatible(assessment.StatusLabel, assessment.Grade));
    }
}
