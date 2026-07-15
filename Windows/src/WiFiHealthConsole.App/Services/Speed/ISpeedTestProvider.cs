using System.Net;
using WiFiHealthConsole.Core;

namespace WiFiHealthConsole.App.Services.Speed;

public interface ISpeedTestProvider
{
    string ProviderName { get; }

    Task<SpeedTestReport> RunAsync(
        SpeedTestRequest request,
        IProgress<SpeedTestProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record SpeedTestRequest
{
    public SpeedTestRoute Route { get; init; } = SpeedTestRoute.CurrentPath;

    public SpeedTestDurationPreset DurationPreset { get; init; } = SpeedTestDurationPreset.Standard;

    public string? RequestedInterface { get; init; }
}

public sealed record SpeedTestProgress
{
    public SpeedTestPhase Phase { get; init; }

    public required SpeedTestSample Sample { get; init; }

    public double PhaseFractionCompleted { get; init; }

    public long TransferredBytes { get; init; }

    public string? Node { get; init; }

    public double MegabytesPerSecond => Sample.Mbps / 8d;
}

public sealed record CloudflareSpeedTestOptions
{
    public Uri BaseUri { get; init; } = new("https://speed.cloudflare.com/");

    public TimeSpan SampleInterval { get; init; } = TimeSpan.FromMilliseconds(500);

    public TimeSpan WarmupDuration { get; init; } = TimeSpan.FromSeconds(2);

    public int ParallelConnections { get; init; } = 4;

    public int DownloadChunkBytes { get; init; } = 25_000_000;

    public int UploadChunkBytes { get; init; } = 10_000_000;

    public TimeSpan? PhaseDurationOverride { get; init; }

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan ResponsivenessProbeInterval { get; init; } = TimeSpan.FromSeconds(1);

    public int MinimumResponsivenessSamples { get; init; } = 3;

    internal void Validate(TimeSpan phaseDuration)
    {
        if (BaseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Cloudflare 测速端点必须使用 https。", nameof(BaseUri));
        }

        if (SampleInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(SampleInterval));
        }

        if (phaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(PhaseDurationOverride));
        }

        if (WarmupDuration < TimeSpan.Zero || WarmupDuration >= phaseDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(WarmupDuration),
                "预热时间必须小于每个方向的测速时间。");
        }

        if (ParallelConnections is < 1 or > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(ParallelConnections));
        }

        if (DownloadChunkBytes is < 64 * 1024 or > 1_000_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(DownloadChunkBytes));
        }

        if (UploadChunkBytes is < 64 * 1024 or > 100_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(UploadChunkBytes));
        }

        if (RequestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(RequestTimeout));
        }

        if (ResponsivenessProbeInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ResponsivenessProbeInterval));
        }

        if (MinimumResponsivenessSamples is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumResponsivenessSamples));
        }
    }
}

public sealed class SpeedTestUnavailableException : Exception
{
    public SpeedTestUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

internal sealed record InterfaceBinding(string Name, string Id, IPAddress LocalAddress);
