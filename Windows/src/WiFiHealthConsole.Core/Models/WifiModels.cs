namespace WiFiHealthConsole.Core;

public enum WiFiBand
{
    Unknown = 0,
    Band2_4GHz,
    Band5GHz,
    Band6GHz,
}

public static class WiFiBandExtensions
{
    public static string DisplayName(this WiFiBand band) => band switch
    {
        WiFiBand.Band2_4GHz => "2.4 GHz",
        WiFiBand.Band5GHz => "5 GHz",
        WiFiBand.Band6GHz => "6 GHz",
        _ => "未知",
    };
}

/// <summary>
/// Current Windows Wi-Fi connection telemetry. Noise, SNR and CCA intentionally have no
/// calculated properties: Windows public WLAN APIs generally do not expose them, so an
/// unavailable value must remain unavailable unless real router/driver telemetry is supplied.
/// </summary>
public sealed record WifiSnapshot
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    public string? InterfaceName { get; init; }

    public Guid? InterfaceId { get; init; }

    public Observed<string> Ssid { get; init; } = NotObserved<string>("未取得 SSID。");

    public Observed<string> Bssid { get; init; } = NotObserved<string>("未取得 BSSID。");

    public Observed<WiFiBand> Band { get; init; } = NotObserved<WiFiBand>("未识别当前频段。");

    public Observed<int> PrimaryChannel { get; init; } = NotObserved<int>("未取得主信道。");

    public Observed<int> ChannelWidthMHz { get; init; } = NotObserved<int>("未取得信道频宽。");

    public Observed<int> RssiDbm { get; init; } = NotObserved<int>("未取得 RSSI。");

    public Observed<uint> SignalQualityPercent { get; init; } = NotObserved<uint>("未取得信号质量百分比。");

    public Observed<int> NoiseDbm { get; init; } = Observed<int>.Unavailable(
        MetricAvailability.NotSupported,
        EvidenceSource.NativeWlanApi,
        "Windows 公共 WLAN API 不提供噪声值，未进行估算。");

    public Observed<int> SnrDb { get; init; } = Observed<int>.Unavailable(
        MetricAvailability.NotSupported,
        EvidenceSource.NativeWlanApi,
        "Windows 公共 WLAN API 不提供 SNR，未通过 RSSI 或质量百分比反推。");

    public Observed<double> ReceiveRateMbps { get; init; } = NotObserved<double>("未取得接收协商速率。");

    public Observed<double> TransmitRateMbps { get; init; } = NotObserved<double>("未取得发送协商速率。");

    public Observed<double> CcaPercent { get; init; } = Observed<double>.Unavailable(
        MetricAvailability.NotSupported,
        EvidenceSource.NativeWlanApi,
        "Windows 公共 WLAN API 不提供 CCA，未进行估算。");

    public bool IsConnected { get; init; }

    // Read-only aliases keep platform collectors and presentation code concise.
    public Observed<string> SSID => Ssid;

    public Observed<string> BSSID => Bssid;

    public Observed<int> Channel => PrimaryChannel;

    public Observed<int> ChannelWidth => ChannelWidthMHz;

    public Observed<int> RSSI => RssiDbm;

    public Observed<int> Noise => NoiseDbm;

    public Observed<int> SNR => SnrDb;

    public Observed<double> RxRateMbps => ReceiveRateMbps;

    public Observed<double> TxRateMbps => TransmitRateMbps;

    public WiFiBand BandValue => Band.TryGetValue(out var value) ? value : WiFiBand.Unknown;

    public int? PrimaryChannelValue => PrimaryChannel.TryGetValue(out var value) ? value : null;

    public int? ChannelWidthValue => ChannelWidthMHz.TryGetValue(out var value) && value > 0 ? value : null;

    public int? RssiValue => RssiDbm.TryGetValue(out var value) ? value : null;

    public static WifiSnapshot Unavailable => new();

    private static Observed<T> NotObserved<T>(string detail) =>
        Observed<T>.Unavailable(MetricAvailability.Unavailable, EvidenceSource.None, detail);
}

public sealed record NearbyNetwork
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string? Ssid { get; init; }

    public string? Bssid { get; init; }

    public WiFiBand Band { get; init; } = WiFiBand.Unknown;

    public int PrimaryChannel { get; init; }

    public int? CenterFrequencyMHz { get; init; }

    /// <summary>
    /// Null means the scan did not expose a trustworthy width. Channel analysis will retain
    /// that uncertainty rather than claiming a measured 20/40/80/160 MHz value.
    /// </summary>
    public int? ChannelWidthMHz { get; init; }

    public bool WidthEstimated { get; init; }

    public int RssiDbm { get; init; }

    public uint SignalQualityPercent { get; init; }

    public string? Authentication { get; init; }

    public string? Cipher { get; init; }

    public DateTimeOffset SeenAt { get; init; } = DateTimeOffset.Now;

    public string? SSID => Ssid;

    public string? BSSID => Bssid;

    public int Channel => PrimaryChannel;

    public int RSSI => RssiDbm;

    public bool HasKnownWidth => ChannelWidthMHz is > 0 && !WidthEstimated;
}
