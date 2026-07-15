using WiFiHealthConsole.Core;

namespace WiFiHealthConsole.App.Services;

/// <summary>
/// Deterministic preview data for macOS/Linux development and UI tests. Every observed value
/// is marked as Mock so it cannot be mistaken for telemetry from the user's machine.
/// </summary>
public sealed class MockWifiTelemetryProvider : IWifiTelemetryProvider
{
    private static readonly Guid PreviewInterfaceId = new("33D0F276-51A6-4F99-A920-0D12B14A2538");

    public bool IsSupported => true;

    public string ProviderName => "Mock Wi-Fi preview data";

    public Task<WifiSnapshot> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string detail = "用于非 Windows 开发预览的 Mock 数据，不是本机测量。";
        var snapshot = new WifiSnapshot
        {
            Timestamp = DateTimeOffset.Now,
            InterfaceName = "Wi-Fi (preview)",
            InterfaceId = PreviewInterfaceId,
            Ssid = Observed<string>.Available("Demo_5G", EvidenceSource.Mock, detail),
            Bssid = Observed<string>.Available("02:00:00:00:05:01", EvidenceSource.Mock, detail),
            Band = Observed<WiFiBand>.Available(WiFiBand.Band5GHz, EvidenceSource.Mock, detail),
            PrimaryChannel = Observed<int>.Available(44, EvidenceSource.Mock, detail),
            ChannelWidthMHz = Observed<int>.Available(80, EvidenceSource.Mock, detail),
            RssiDbm = Observed<int>.Available(-58, EvidenceSource.Mock, detail),
            SignalQualityPercent = Observed<uint>.Available(84, EvidenceSource.Mock, detail),
            ReceiveRateMbps = Observed<double>.Available(1_201, EvidenceSource.Mock, detail),
            TransmitRateMbps = Observed<double>.Available(1_201, EvidenceSource.Mock, detail),
            NoiseDbm = Observed<int>.Unavailable(
                MetricAvailability.NotSupported,
                EvidenceSource.Mock,
                "Windows 公共 WLAN API 不提供噪声，Mock 也不伪造。"),
            SnrDb = Observed<int>.Unavailable(
                MetricAvailability.NotSupported,
                EvidenceSource.Mock,
                "Windows 公共 WLAN API 不提供 SNR，Mock 也不反推。"),
            CcaPercent = Observed<double>.Unavailable(
                MetricAvailability.NotSupported,
                EvidenceSource.Mock,
                "CCA 需要路由器或驱动真实统计，Mock 不伪造。"),
            IsConnected = true,
        };

        return Task.FromResult(snapshot);
    }

    public Task<IReadOnlyList<NearbyNetwork>> ScanAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var seenAt = DateTimeOffset.Now;
        IReadOnlyList<NearbyNetwork> networks =
        [
            Network("02:00:00:00:05:01", "Demo_5G", WiFiBand.Band5GHz, 44, 80, -58, 84, 5_220, "WPA3-SAE", "CCMP/AES", seenAt),
            Network("02:00:00:00:05:02", "Neighbor-A", WiFiBand.Band5GHz, 36, 80, -64, 72, 5_180, "WPA2-Personal", "CCMP/AES", seenAt),
            Network("02:00:00:00:05:03", "Neighbor-B", WiFiBand.Band5GHz, 149, 80, -72, 54, 5_745, "WPA2-Personal", "CCMP/AES", seenAt),
            Network("02:00:00:00:02:01", "Demo_2.4G", WiFiBand.Band2_4GHz, 6, 20, -47, 92, 2_437, "WPA2-Personal", "CCMP/AES", seenAt),
            Network("02:00:00:00:02:02", "Neighbor-C", WiFiBand.Band2_4GHz, 1, 20, -69, 62, 2_412, "WPA2-Personal", "CCMP/AES", seenAt),
            Network("02:00:00:00:06:01", "Demo_6G", WiFiBand.Band6GHz, 37, 80, -78, 42, 6_135, "WPA3-SAE", "CCMP/AES", seenAt),
        ];

        return Task.FromResult(networks);
    }

    public Task<bool> OpenLocationPrivacySettingsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }

    private static NearbyNetwork Network(
        string bssid,
        string ssid,
        WiFiBand band,
        int channel,
        int width,
        int rssi,
        uint quality,
        int centerFrequency,
        string authentication,
        string cipher,
        DateTimeOffset seenAt) =>
        new()
        {
            Id = $"preview-{bssid}",
            Ssid = ssid,
            Bssid = bssid,
            Band = band,
            PrimaryChannel = channel,
            CenterFrequencyMHz = centerFrequency,
            ChannelWidthMHz = width,
            WidthEstimated = false,
            RssiDbm = rssi,
            SignalQualityPercent = quality,
            Authentication = authentication,
            Cipher = cipher,
            SeenAt = seenAt,
        };
}
