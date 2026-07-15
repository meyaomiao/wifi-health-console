using System.Diagnostics;
using WiFiHealthConsole.App.Interop;
using WiFiHealthConsole.Core;

namespace WiFiHealthConsole.App.Services;

public sealed class WindowsWifiTelemetryProvider : IWifiTelemetryProvider
{
    private const int ScanSettleDelayMilliseconds = 1_800;
    private readonly MockWifiTelemetryProvider _nonWindowsFallback = new();

    public bool IsSupported => OperatingSystem.IsWindows();

    public string ProviderName => "Windows Native WLAN API";

    public Task<WifiSnapshot> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return _nonWindowsFallback.GetCurrentAsync(cancellationToken);
        }

        return Task.Run(GetCurrentCore, cancellationToken);
    }

    public async Task<IReadOnlyList<NearbyNetwork>> ScanAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return await _nonWindowsFallback.ScanAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            return await ScanWindowsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WifiTelemetryException)
        {
            throw;
        }
        catch (WlanNativeException error) when (error.IsAccessDenied)
        {
            throw new WifiLocationPermissionException("扫描附近 Wi-Fi", error);
        }
        catch (WlanNativeException error)
        {
            throw new WifiTelemetryException("Windows Native WLAN 扫描失败。", error);
        }
        catch (Exception error)
        {
            throw new WifiTelemetryException("无法读取 Windows Wi-Fi 扫描结果。", error);
        }
    }

    private static async Task<IReadOnlyList<NearbyNetwork>> ScanWindowsAsync(
        CancellationToken cancellationToken)
    {

        cancellationToken.ThrowIfCancellationRequested();
        using var client = new WlanClient();
        var interfaces = client.GetInterfaces();
        if (interfaces.Count == 0)
        {
            return [];
        }

        WlanNativeException? firstScanError = null;
        var scanRequested = false;
        foreach (var wifiInterface in interfaces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                client.RequestScan(wifiInterface);
                scanRequested = true;
            }
            catch (WlanNativeException error)
            {
                firstScanError ??= error;
            }
        }

        if (!scanRequested && firstScanError?.IsAccessDenied == true)
        {
            throw new WifiLocationPermissionException("扫描附近 Wi-Fi", firstScanError);
        }

        // WlanScan 只发起异步扫描。等待系统更新 BSS 缓存后再读取，
        // 不用 netsh 的本地化文本作为数据源。
        if (scanRequested)
        {
            await Task.Delay(ScanSettleDelayMilliseconds, cancellationToken).ConfigureAwait(false);
        }

        var nativeNetworks = new List<NativeBssNetwork>();
        var bssCallSucceeded = false;
        WlanNativeException? firstBssError = null;
        foreach (var wifiInterface in interfaces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                nativeNetworks.AddRange(client.GetBssNetworks(wifiInterface));
                bssCallSucceeded = true;
            }
            catch (WlanNativeException error)
            {
                firstBssError ??= error;
            }
        }

        if (!bssCallSucceeded && firstBssError?.IsAccessDenied == true)
        {
            throw new WifiLocationPermissionException("读取附近 Wi-Fi", firstBssError);
        }

        if (!bssCallSucceeded && firstBssError is not null)
        {
            throw new WifiTelemetryException("无法读取 Windows Wi-Fi 扫描结果。", firstBssError);
        }

        var seenAt = DateTimeOffset.Now;
        return nativeNetworks
            .Where(network => network.PrimaryChannel is > 0)
            .GroupBy(
                network => $"{network.Bssid}|{network.CenterFrequencyMHz}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(network => network.RssiDbm)
                .First())
            .Select((network, index) => ToNearbyNetwork(network, seenAt, index))
            .OrderBy(network => network.Band)
            .ThenBy(network => network.PrimaryChannel)
            .ThenByDescending(network => network.RssiDbm)
            .ToArray();
    }

    public Task<bool> OpenLocationPrivacySettingsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(false);
        }

        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = WifiLocationPermissionException.LocationSettingsUri,
                UseShellExecute = true,
            });
            process?.Dispose();
            return Task.FromResult(process is not null);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    private static WifiSnapshot GetCurrentCore()
    {
        try
        {
            using var client = new WlanClient();
            var interfaces = client.GetInterfaces();
            if (interfaces.Count == 0)
            {
                return CreateUnavailableSnapshot(null, "Windows 未找到 Wi-Fi 接口。");
            }

            var wifiInterface = interfaces.FirstOrDefault(item => item.State == WlanInterfaceState.Connected);
            if (wifiInterface is null)
            {
                return CreateUnavailableSnapshot(interfaces[0], "Wi-Fi 接口当前未连接。");
            }

            var connection = client.GetCurrentConnection(wifiInterface);
            if (connection is null)
            {
                return CreateUnavailableSnapshot(wifiInterface, "Wi-Fi 接口当前未连接。");
            }

            int? queriedChannel = null;
            try
            {
                queriedChannel = client.GetCurrentChannel(wifiInterface);
            }
            catch (WlanNativeException)
            {
                // BSS 记录仍可以提供主信道；不因单个回退调用失败丢弃整体采样。
            }

            NativeBssNetwork? currentBss = null;
            WlanNativeException? bssError = null;
            try
            {
                currentBss = client.GetBssNetworks(wifiInterface)
                    .Where(network => string.Equals(
                        network.Bssid,
                        connection.Bssid,
                        StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(network => network.RssiDbm)
                    .FirstOrDefault();
            }
            catch (WlanNativeException error)
            {
                bssError = error;
            }

            var radioUnavailableReason = bssError?.IsAccessDenied == true
                ? "Windows 位置权限未允许，无法读取 BSS 无线细节。"
                : currentBss is null
                    ? "BSS 列表中未匹配到当前连接的 BSSID。"
                    : null;
            var radioAvailability = bssError?.IsAccessDenied == true
                ? MetricAvailability.PermissionDenied
                : MetricAvailability.Unavailable;
            var primaryChannel = currentBss?.PrimaryChannel ?? queriedChannel;
            var band = MapBand(WifiChannelMath.BandFromFrequencyMHz(currentBss?.CenterFrequencyMHz));

            return new WifiSnapshot
            {
                Timestamp = DateTimeOffset.Now,
                InterfaceName = wifiInterface.Name,
                InterfaceId = wifiInterface.Id,
                Ssid = AvailableOrUnavailable(
                    connection.Ssid,
                    "当前连接未返回 SSID。"),
                Bssid = AvailableOrUnavailable(
                    connection.Bssid,
                    "当前连接未返回 BSSID。"),
                Band = band == WiFiBand.Unknown
                    ? Observed<WiFiBand>.Unavailable(
                        radioAvailability,
                        EvidenceSource.NativeWlanApi,
                        radioUnavailableReason ?? "未能从 BSS 中心频率识别频段。")
                    : Observed<WiFiBand>.Available(
                        band,
                        EvidenceSource.NativeWlanApi,
                        "由 Native WLAN BSS 中心频率识别。"),
                PrimaryChannel = primaryChannel is { } channel
                    ? Observed<int>.Available(
                        channel,
                        EvidenceSource.NativeWlanApi,
                        currentBss?.PrimaryChannel is not null
                            ? "从 AP 广播的操作 IE 读取主信道。"
                            : "由 WlanQueryInterface 读取当前信道。")
                    : Observed<int>.Unavailable(
                        radioAvailability,
                        EvidenceSource.NativeWlanApi,
                        radioUnavailableReason ?? "未取得当前主信道。"),
                ChannelWidthMHz = currentBss?.ChannelWidthMHz is { } width
                    ? Observed<int>.Available(
                        width,
                        EvidenceSource.NativeWlanApi,
                        "从 AP 广播的 HT/VHT/HE Operation IE 解析；未把网卡‘支持的最大频宽’冒充当前频宽。")
                    : Observed<int>.Unavailable(
                        radioAvailability,
                        EvidenceSource.NativeWlanApi,
                        radioUnavailableReason ?? "操作 IE 未提供可安全确定的 20/40/80/160 MHz 频宽。"),
                RssiDbm = currentBss is not null
                    ? Observed<int>.Available(
                        currentBss.RssiDbm,
                        EvidenceSource.NativeWlanApi,
                        "来自 WlanGetNetworkBssList 的真实 RSSI，不是由信号质量百分比换算。")
                    : Observed<int>.Unavailable(
                        radioAvailability,
                        EvidenceSource.NativeWlanApi,
                        radioUnavailableReason ?? "未取得 RSSI。"),
                SignalQualityPercent = Observed<uint>.Available(
                    connection.SignalQualityPercent,
                    EvidenceSource.NativeWlanApi,
                    "Windows WLAN 链路质量百分比；与 dBm RSSI 是不同指标。"),
                ReceiveRateMbps = PositiveRate(
                    connection.ReceiveRateMbps,
                    "Windows WLAN 当前接收协商速率。"),
                TransmitRateMbps = PositiveRate(
                    connection.TransmitRateMbps,
                    "Windows WLAN 当前发送协商速率。"),
                NoiseDbm = NotSupported<int>("Windows 公共 WLAN API 不提供噪声值，未进行估算。"),
                SnrDb = NotSupported<int>("Windows 公共 WLAN API 不提供 SNR，未由信号质量反推。"),
                CcaPercent = NotSupported<double>("Windows 公共 WLAN API 不提供 CCA，需以路由器无线统计为准。"),
                IsConnected = true,
            };
        }
        catch (WlanNativeException error) when (error.IsAccessDenied)
        {
            return CreatePermissionDeniedSnapshot(error);
        }
        catch (Exception error)
        {
            return CreateFailedSnapshot(error);
        }
    }

    private static NearbyNetwork ToNearbyNetwork(
        NativeBssNetwork network,
        DateTimeOffset seenAt,
        int fallbackIndex)
    {
        var band = MapBand(WifiChannelMath.BandFromFrequencyMHz(network.CenterFrequencyMHz));
        var ssid = string.IsNullOrWhiteSpace(network.Ssid) ? null : network.Ssid;
        var bssid = string.IsNullOrWhiteSpace(network.Bssid) ? null : network.Bssid;
        return new NearbyNetwork
        {
            Id = bssid is not null
                ? $"{bssid}-{network.CenterFrequencyMHz?.ToString() ?? "unknown"}"
                : $"hidden-{network.PrimaryChannel}-{network.RssiDbm}-{fallbackIndex}",
            Ssid = ssid,
            Bssid = bssid,
            Band = band,
            PrimaryChannel = network.PrimaryChannel!.Value,
            CenterFrequencyMHz = network.CenterFrequencyMHz,
            ChannelWidthMHz = network.ChannelWidthMHz,
            // HT/VHT/HE Operation IE 描述 AP 当前操作频宽，不是按 PHY 能力猜测。
            WidthEstimated = false,
            RssiDbm = network.RssiDbm,
            SignalQualityPercent = network.SignalQualityPercent,
            Authentication = network.Authentication,
            Cipher = network.Cipher,
            SeenAt = seenAt,
        };
    }

    private static WifiSnapshot CreateUnavailableSnapshot(
        NativeWifiInterface? wifiInterface,
        string detail)
    {
        return new WifiSnapshot
        {
            Timestamp = DateTimeOffset.Now,
            InterfaceName = wifiInterface?.Name,
            InterfaceId = wifiInterface?.Id,
            Ssid = Unavailable<string>(MetricAvailability.Unavailable, detail),
            Bssid = Unavailable<string>(MetricAvailability.Unavailable, detail),
            Band = Unavailable<WiFiBand>(MetricAvailability.Unavailable, detail),
            PrimaryChannel = Unavailable<int>(MetricAvailability.Unavailable, detail),
            ChannelWidthMHz = Unavailable<int>(MetricAvailability.Unavailable, detail),
            RssiDbm = Unavailable<int>(MetricAvailability.Unavailable, detail),
            SignalQualityPercent = Unavailable<uint>(MetricAvailability.Unavailable, detail),
            ReceiveRateMbps = Unavailable<double>(MetricAvailability.Unavailable, detail),
            TransmitRateMbps = Unavailable<double>(MetricAvailability.Unavailable, detail),
            NoiseDbm = NotSupported<int>("Windows 公共 WLAN API 不提供噪声值。"),
            SnrDb = NotSupported<int>("Windows 公共 WLAN API 不提供 SNR。"),
            CcaPercent = NotSupported<double>("Windows 公共 WLAN API 不提供 CCA。"),
            IsConnected = false,
        };
    }

    private static WifiSnapshot CreatePermissionDeniedSnapshot(WlanNativeException error)
    {
        var detail = $"{error.Operation} 被 Windows 位置权限拒绝。请打开位置设置，允许桌面应用访问后重试。";
        return new WifiSnapshot
        {
            Timestamp = DateTimeOffset.Now,
            Ssid = Unavailable<string>(MetricAvailability.PermissionDenied, detail),
            Bssid = Unavailable<string>(MetricAvailability.PermissionDenied, detail),
            Band = Unavailable<WiFiBand>(MetricAvailability.PermissionDenied, detail),
            PrimaryChannel = Unavailable<int>(MetricAvailability.PermissionDenied, detail),
            ChannelWidthMHz = Unavailable<int>(MetricAvailability.PermissionDenied, detail),
            RssiDbm = Unavailable<int>(MetricAvailability.PermissionDenied, detail),
            SignalQualityPercent = Unavailable<uint>(MetricAvailability.PermissionDenied, detail),
            ReceiveRateMbps = Unavailable<double>(MetricAvailability.PermissionDenied, detail),
            TransmitRateMbps = Unavailable<double>(MetricAvailability.PermissionDenied, detail),
            NoiseDbm = NotSupported<int>("Windows 公共 WLAN API 不提供噪声值。"),
            SnrDb = NotSupported<int>("Windows 公共 WLAN API 不提供 SNR。"),
            CcaPercent = NotSupported<double>("Windows 公共 WLAN API 不提供 CCA。"),
            IsConnected = false,
        };
    }

    private static WifiSnapshot CreateFailedSnapshot(Exception error)
    {
        var detail = $"Windows Native WLAN 采集失败：{error.Message}";
        return new WifiSnapshot
        {
            Timestamp = DateTimeOffset.Now,
            Ssid = Unavailable<string>(MetricAvailability.Failed, detail),
            Bssid = Unavailable<string>(MetricAvailability.Failed, detail),
            Band = Unavailable<WiFiBand>(MetricAvailability.Failed, detail),
            PrimaryChannel = Unavailable<int>(MetricAvailability.Failed, detail),
            ChannelWidthMHz = Unavailable<int>(MetricAvailability.Failed, detail),
            RssiDbm = Unavailable<int>(MetricAvailability.Failed, detail),
            SignalQualityPercent = Unavailable<uint>(MetricAvailability.Failed, detail),
            ReceiveRateMbps = Unavailable<double>(MetricAvailability.Failed, detail),
            TransmitRateMbps = Unavailable<double>(MetricAvailability.Failed, detail),
            NoiseDbm = NotSupported<int>("Windows 公共 WLAN API 不提供噪声值。"),
            SnrDb = NotSupported<int>("Windows 公共 WLAN API 不提供 SNR。"),
            CcaPercent = NotSupported<double>("Windows 公共 WLAN API 不提供 CCA。"),
            IsConnected = false,
        };
    }

    private static Observed<string> AvailableOrUnavailable(string value, string unavailableDetail) =>
        string.IsNullOrWhiteSpace(value)
            ? Unavailable<string>(MetricAvailability.Unavailable, unavailableDetail)
            : Observed<string>.Available(value, EvidenceSource.NativeWlanApi);

    private static Observed<double> PositiveRate(double value, string detail) =>
        value > 0
            ? Observed<double>.Available(value, EvidenceSource.NativeWlanApi, detail)
            : Unavailable<double>(MetricAvailability.Unavailable, "Windows 未返回有效协商速率。");

    private static Observed<T> Unavailable<T>(MetricAvailability availability, string detail) =>
        Observed<T>.Unavailable(availability, EvidenceSource.NativeWlanApi, detail);

    private static Observed<T> NotSupported<T>(string detail) =>
        Observed<T>.Unavailable(MetricAvailability.NotSupported, EvidenceSource.NativeWlanApi, detail);

    private static WiFiBand MapBand(NativeWifiBand band) => band switch
    {
        NativeWifiBand.Band2_4GHz => WiFiBand.Band2_4GHz,
        NativeWifiBand.Band5GHz => WiFiBand.Band5GHz,
        NativeWifiBand.Band6GHz => WiFiBand.Band6GHz,
        _ => WiFiBand.Unknown,
    };
}
