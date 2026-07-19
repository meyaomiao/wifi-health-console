using WiFiHealthConsole.App.Services.Diagnostics;
using WiFiHealthConsole.Core;

namespace WiFiHealthConsole.App.Tests;

public sealed class NetworkDiagnosticServiceTests
{
    [Fact]
    public void WirelessLayerPreservesUnsupportedMetricsAndReportsChannel()
    {
        var snapshot = new WifiSnapshot
        {
            IsConnected = true,
            Ssid = Observed<string>.Available("Demo_5G", EvidenceSource.Mock),
            Band = Observed<WiFiBand>.Available(WiFiBand.Band5GHz, EvidenceSource.Mock),
            PrimaryChannel = Observed<int>.Available(36, EvidenceSource.Mock),
            ChannelWidthMHz = Observed<int>.Available(80, EvidenceSource.Mock),
            RssiDbm = Observed<int>.Available(-48, EvidenceSource.Mock),
            SnrDb = Observed<int>.Unavailable(
                MetricAvailability.NotSupported,
                EvidenceSource.NativeWlanApi,
                "Windows 公共 WLAN API 不提供 SNR。"),
            CcaPercent = Observed<double>.Unavailable(
                MetricAvailability.NotSupported,
                EvidenceSource.NativeWlanApi,
                "Windows 公共 WLAN API 不提供 CCA。"),
        };

        var layer = NetworkDiagnosticService.BuildWirelessLayer([snapshot]);

        Assert.Equal("信道 36", Assert.Single(layer.Metrics, metric => metric.Id == "wireless-channel").Value);
        Assert.Equal("系统不支持", Assert.Single(layer.Metrics, metric => metric.Id == "wireless-snr").Value);
        Assert.Equal("不支持", Assert.Single(layer.Metrics, metric => metric.Id == "wireless-snr").StatusLabel);
        Assert.Equal("系统不支持", Assert.Single(layer.Metrics, metric => metric.Id == "wireless-cca").Value);
        Assert.Equal("不支持", Assert.Single(layer.Metrics, metric => metric.Id == "wireless-cca").StatusLabel);
    }
}
