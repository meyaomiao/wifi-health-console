using System.Net;
using WiFiHealthConsole.Core;

namespace WiFiHealthConsole.App.Services.Diagnostics;

public interface INetworkDiagnosticService
{
    Task<DiagnosticReport> RunAsync(
        NetworkDiagnosticOptions? options = null,
        IProgress<NetworkDiagnosticProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record NetworkDiagnosticOptions
{
    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(60);

    public TimeSpan GatewayPingInterval { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan PingTimeout { get; init; } = TimeSpan.FromMilliseconds(1_500);

    public IPAddress ExternalPingAddress { get; init; } = IPAddress.Parse("1.1.1.1");

    public int ExternalPingCount { get; init; } = 8;

    public TimeSpan ExternalPingInterval { get; init; } = TimeSpan.FromMilliseconds(750);

    public IPEndPoint DnsServer { get; init; } = new(IPAddress.Parse("1.1.1.1"), 53);

    public string DnsHostName { get; init; } = "www.apple.com";

    public int DnsMaximumAttempts { get; init; } = 3;

    public TimeSpan DnsAttemptTimeout { get; init; } = TimeSpan.FromSeconds(3);

    public Uri HttpsEndpoint { get; init; } = new("https://www.apple.com/library/test/success.html");

    public TimeSpan HttpsTimeout { get; init; } = TimeSpan.FromSeconds(10);

    internal void Validate()
    {
        if (Duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(Duration), "体检时长必须大于零。");
        }

        if (GatewayPingInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(GatewayPingInterval));
        }

        if (PingTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(PingTimeout));
        }

        if (ExternalPingCount is < 1 or > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(ExternalPingCount));
        }

        if (ExternalPingInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ExternalPingInterval));
        }

        if (DnsMaximumAttempts is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DnsMaximumAttempts),
                "DNS 直连检测最多重试三次。");
        }

        if (DnsAttemptTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(DnsAttemptTimeout));
        }

        if (string.IsNullOrWhiteSpace(DnsHostName))
        {
            throw new ArgumentException("DNS 测试域名不能为空。", nameof(DnsHostName));
        }

        if (HttpsEndpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("HTTPS 检测端点必须使用 https。", nameof(HttpsEndpoint));
        }

        if (HttpsTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(HttpsTimeout));
        }
    }
}

public sealed record NetworkDiagnosticProgress
{
    public required string Stage { get; init; }

    public double FractionCompleted { get; init; }

    public int GatewaySamplesCompleted { get; init; }

    public int WirelessSamplesCompleted { get; init; }
}
