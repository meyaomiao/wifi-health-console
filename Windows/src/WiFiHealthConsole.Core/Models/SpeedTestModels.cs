namespace WiFiHealthConsole.Core;

public enum SpeedTestRoute
{
    CurrentPath,
    DirectWiFiBaseline,
}

public static class SpeedTestRouteExtensions
{
    public static string DisplayName(this SpeedTestRoute route) => route switch
    {
        SpeedTestRoute.CurrentPath => "当前实际链路",
        SpeedTestRoute.DirectWiFiBaseline => "直连 Wi-Fi 基线",
        _ => "当前实际链路",
    };

    public static string Detail(this SpeedTestRoute route) => route switch
    {
        SpeedTestRoute.CurrentPath => "按系统当前路径测速，包含正在使用的 VPN 或代理，最接近日常应用的实际体验。",
        SpeedTestRoute.DirectWiFiBaseline => "绑定当前 Wi-Fi 接口作为对比基线；VPN 驱动仍可能接管流量，因此结果会明确记录实际接口。",
        _ => string.Empty,
    };
}

public enum SpeedTestPhase
{
    Download,
    Upload,
}

public enum SpeedTestDurationPreset
{
    Standard,
    Stable,
}

public static class SpeedTestDurationPresetExtensions
{
    public static string DisplayName(this SpeedTestDurationPreset preset) => preset switch
    {
        SpeedTestDurationPreset.Standard => "标准模式",
        SpeedTestDurationPreset.Stable => "稳定模式",
        _ => "标准模式",
    };

    public static int PhaseRuntimeSeconds(this SpeedTestDurationPreset preset) => preset switch
    {
        SpeedTestDurationPreset.Standard => 20,
        SpeedTestDurationPreset.Stable => 30,
        _ => 20,
    };

    public static int ThroughputStageSeconds(this SpeedTestDurationPreset preset) =>
        preset.PhaseRuntimeSeconds() * 2;

    public static string SegmentTitle(this SpeedTestDurationPreset preset) => preset switch
    {
        SpeedTestDurationPreset.Standard => "标准 · 吞吐约 40 秒",
        SpeedTestDurationPreset.Stable => "稳定 · 吞吐约 60 秒",
        _ => "标准 · 吞吐约 40 秒",
    };

    public static string Detail(this SpeedTestDurationPreset preset) => preset switch
    {
        SpeedTestDurationPreset.Standard =>
            "下载、上传每个方向各测约 20 秒；开始前还会进行延迟预检和连接建立，网络异常时总耗时会更长。",
        SpeedTestDurationPreset.Stable =>
            "下载、上传每个方向各测约 30 秒；开始前还会进行延迟预检和连接建立，适合高波动链路回测。",
        _ => string.Empty,
    };

    public static string TrafficWarning(this SpeedTestDurationPreset preset) => preset switch
    {
        SpeedTestDurationPreset.Standard =>
            "测速会持续占用真实带宽；高速连接可能消耗数百 MB，千兆链路极端情况下可达数 GB。",
        SpeedTestDurationPreset.Stable =>
            "稳定模式会占用更长时间和更多真实流量；使用计费手机热点时请谨慎。",
        _ => string.Empty,
    };
}

public sealed record SpeedTestReport
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.Now;

    public SpeedTestRoute Route { get; init; }

    public SpeedTestDurationPreset DurationPreset { get; init; }

    public string? RequestedInterface { get; init; }

    public string? SampledInterface { get; init; }

    public string? SampledInterfaceId { get; init; }

    public string? MeasuredInterface { get; init; }

    public string? MeasuredInterfaceId { get; init; }

    public string? PathDescription { get; init; }

    public string? Endpoint { get; init; }

    public double DownloadBitsPerSecond { get; init; }

    public double UploadBitsPerSecond { get; init; }

    public double? IdleLatencyMs { get; init; }

    public double? DownloadResponsivenessRpm { get; init; }

    public double? UploadResponsivenessRpm { get; init; }

    public long? DownloadedBytes { get; init; }

    public long? UploadedBytes { get; init; }

    public double? DurationSeconds { get; init; }

    public bool WasProxied { get; init; }

    public double DownloadMbps => DownloadBitsPerSecond / 1_000_000d;

    public double UploadMbps => UploadBitsPerSecond / 1_000_000d;

    public double DownloadMegabytesPerSecond => DownloadBitsPerSecond / 8_000_000d;

    public double UploadMegabytesPerSecond => UploadBitsPerSecond / 8_000_000d;

    public double TransferredMegabytes => ((DownloadedBytes ?? 0) + (UploadedBytes ?? 0)) / 1_000_000d;

    public double? DownloadSecondsForGigabytes(double gigabytes) =>
        TransferSeconds(gigabytes, DownloadBitsPerSecond);

    public double? UploadSecondsForGigabytes(double gigabytes) =>
        TransferSeconds(gigabytes, UploadBitsPerSecond);

    private static double? TransferSeconds(double gigabytes, double bitsPerSecond) =>
        gigabytes >= 0 && bitsPerSecond > 0
            ? gigabytes * 8_000_000_000d / bitsPerSecond
            : null;
}

public sealed record SpeedTestSample
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public SpeedTestPhase Phase { get; init; }

    public double ElapsedSeconds { get; init; }

    public double Mbps { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
}
