using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace WiFiHealthConsole.App.Services;

public sealed record NetworkContextSnapshot
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    public string? WifiInterfaceName { get; init; }

    public string? WifiInterfaceId { get; init; }

    public IPAddress? WifiDefaultGateway { get; init; }

    public bool ProxyConfigured { get; init; }

    public string ProxySummary { get; init; } = "未检测到系统或环境代理。";

    public bool VpnLikelyActive { get; init; }

    public IReadOnlyList<string> VpnInterfaces { get; init; } = [];

    public string RouteSummary { get; init; } = "未找到活动 Wi-Fi 网关。";
}

public sealed class NetworkContextService
{
    private static readonly string[] VpnKeywords =
    [
        "vpn", "tunnel", "tun", "tap", "wintun", "wireguard", "tailscale", "zerotier",
        "openvpn", "anyconnect", "fortinet", "forticlient", "globalprotect", "pulse secure",
        "clash", "sing-box", "v2ray", "shadowsocks", "surfshark", "nordlynx", "mullvad",
    ];

    private static readonly string[] ProxyEnvironmentVariables =
    [
        "HTTPS_PROXY", "HTTP_PROXY", "ALL_PROXY", "https_proxy", "http_proxy", "all_proxy",
    ];

    public Task<NetworkContextSnapshot> GetCurrentAsync(CancellationToken cancellationToken = default) =>
        Task.Run(GetCurrent, cancellationToken);

    public async Task<bool> OpenGatewayAsync(
        bool useHttps = false,
        CancellationToken cancellationToken = default)
    {
        var context = await GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        var gatewayUri = BuildGatewayUri(context.WifiDefaultGateway, useHttps);
        if (gatewayUri is null)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = gatewayUri.AbsoluteUri,
                UseShellExecute = true,
            });
            process?.Dispose();
            return process is not null;
        }
        catch
        {
            return false;
        }
    }

    public static Uri? BuildGatewayUri(IPAddress? gateway, bool useHttps)
    {
        if (gateway is null || IPAddress.Any.Equals(gateway) || IPAddress.IPv6Any.Equals(gateway))
        {
            return null;
        }

        var scheme = useHttps ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
        var host = gateway.AddressFamily == AddressFamily.InterNetworkV6
            ? $"[{gateway}]"
            : gateway.ToString();
        return Uri.TryCreate($"{scheme}://{host}/", UriKind.Absolute, out var uri) ? uri : null;
    }

    private static NetworkContextSnapshot GetCurrent()
    {
        var activeInterfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(networkInterface =>
                networkInterface.OperationalStatus == OperationalStatus.Up
                && networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .ToArray();

        var wifiCandidates = activeInterfaces
            .Where(networkInterface => networkInterface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            .Select(networkInterface => new
            {
                Interface = networkInterface,
                Gateway = SelectGateway(networkInterface),
            })
            .OrderByDescending(candidate => candidate.Gateway?.AddressFamily == AddressFamily.InterNetwork)
            .ThenByDescending(candidate => candidate.Interface.Speed)
            .ToArray();
        var wifi = wifiCandidates.FirstOrDefault(candidate => candidate.Gateway is not null)
            ?? wifiCandidates.FirstOrDefault();

        var vpnInterfaces = activeInterfaces
            .Where(IsLikelyVpnInterface)
            .Select(networkInterface => string.IsNullOrWhiteSpace(networkInterface.Description)
                || string.Equals(networkInterface.Name, networkInterface.Description, StringComparison.OrdinalIgnoreCase)
                    ? networkInterface.Name
                    : $"{networkInterface.Name} ({networkInterface.Description})")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var proxy = DetectProxy();
        return new NetworkContextSnapshot
        {
            Timestamp = DateTimeOffset.Now,
            WifiInterfaceName = wifi?.Interface.Name,
            WifiInterfaceId = wifi?.Interface.Id,
            WifiDefaultGateway = wifi?.Gateway,
            ProxyConfigured = proxy.IsConfigured,
            ProxySummary = proxy.Summary,
            VpnLikelyActive = vpnInterfaces.Length > 0,
            VpnInterfaces = vpnInterfaces,
            RouteSummary = wifi?.Gateway is { } gateway
                ? $"Wi-Fi 默认网关由网卡‘{wifi.Interface.Name}’自动检测为 {gateway}。"
                : wifi is not null
                    ? $"已找到 Wi-Fi 网卡‘{wifi.Interface.Name}’，但系统未报告默认网关。"
                    : "未找到已启用的 Wi-Fi 网卡，不会使用固定路由器 IP。",
        };
    }

    private static IPAddress? SelectGateway(NetworkInterface networkInterface)
    {
        try
        {
            return networkInterface.GetIPProperties().GatewayAddresses
                .Select(address => address.Address)
                .Where(address =>
                    address is not null
                    && !IPAddress.Any.Equals(address)
                    && !IPAddress.IPv6Any.Equals(address)
                    && !IPAddress.Loopback.Equals(address)
                    && !address.IsIPv6LinkLocal)
                .OrderByDescending(address => address.AddressFamily == AddressFamily.InterNetwork)
                .FirstOrDefault();
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    private static bool IsLikelyVpnInterface(NetworkInterface networkInterface)
    {
        if (networkInterface.NetworkInterfaceType is NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp)
        {
            return true;
        }

        var identity = $"{networkInterface.Name} {networkInterface.Description}";
        return VpnKeywords.Any(keyword => identity.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static ProxyDetection DetectProxy()
    {
        var configuredEnvironmentVariables = ProxyEnvironmentVariables
            .Select(name => (Name: name, Value: Environment.GetEnvironmentVariable(name)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .Select(item => item.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (configuredEnvironmentVariables.Length > 0)
        {
            return new ProxyDetection(
                true,
                $"检测到代理环境变量：{string.Join(" / ", configuredEnvironmentVariables)}。");
        }

        try
        {
            var probe = new Uri("https://www.msftconnecttest.com/connecttest.txt");
            var proxy = HttpClient.DefaultProxy;
            if (!proxy.IsBypassed(probe))
            {
                var proxyUri = proxy.GetProxy(probe);
                if (proxyUri is not null && proxyUri != probe)
                {
                    return new ProxyDetection(true, $"系统当前为 HTTPS 请求选择代理 {proxyUri.Host}:{proxyUri.Port}。");
                }
            }
        }
        catch
        {
            // 代理实现可以抛出平台异常；这是辅助证据，不应阻断网关采集。
        }

        return new ProxyDetection(false, "未检测到系统或环境代理。");
    }

    private readonly record struct ProxyDetection(bool IsConfigured, string Summary);
}
