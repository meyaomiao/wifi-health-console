using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using WiFiHealthConsole.Core;

namespace WiFiHealthConsole.App.Services.Diagnostics;

public sealed class NetworkDiagnosticService : INetworkDiagnosticService
{
    private static readonly byte[] PingPayload = Encoding.ASCII.GetBytes("WiFiHealthConsole");

    private readonly IWifiTelemetryProvider _wifiTelemetryProvider;
    private readonly NetworkContextService _networkContextService;

    public NetworkDiagnosticService()
        : this(WifiTelemetryProviderFactory.CreateDefault(), new NetworkContextService())
    {
    }

    public NetworkDiagnosticService(
        IWifiTelemetryProvider wifiTelemetryProvider,
        NetworkContextService networkContextService)
    {
        _wifiTelemetryProvider = wifiTelemetryProvider
            ?? throw new ArgumentNullException(nameof(wifiTelemetryProvider));
        _networkContextService = networkContextService
            ?? throw new ArgumentNullException(nameof(networkContextService));
    }

    public async Task<DiagnosticReport> RunAsync(
        NetworkDiagnosticOptions? options = null,
        IProgress<NetworkDiagnosticProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new NetworkDiagnosticOptions();
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        var startedAt = DateTimeOffset.Now;
        progress?.Report(new NetworkDiagnosticProgress
        {
            Stage = "正在识别 Wi-Fi 网关、代理与 VPN 路径",
            FractionCompleted = 0.01,
        });

        var context = await _networkContextService
            .GetCurrentAsync(cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new NetworkDiagnosticProgress
        {
            Stage = "正在采样无线信号与网关链路",
            FractionCompleted = 0.03,
        });

        var gatewayTask = SampleWirelessAndGatewayAsync(
            context.WifiDefaultGateway,
            options,
            progress,
            cancellationToken);
        var externalTask = PingFixedCountAsync(
            options.ExternalPingAddress,
            options.ExternalPingCount,
            options.ExternalPingInterval,
            options.PingTimeout,
            cancellationToken);
        var dnsTask = ProbeDnsAsync(options, cancellationToken);
        var httpsTask = ProbeHttpsAsync(options, cancellationToken);

        await Task.WhenAll(gatewayTask, externalTask, dnsTask, httpsTask).ConfigureAwait(false);

        var gatewayResult = await gatewayTask.ConfigureAwait(false);
        var externalPing = await externalTask.ConfigureAwait(false);
        var dns = await dnsTask.ConfigureAwait(false);
        var https = await httpsTask.ConfigureAwait(false);

        progress?.Report(new NetworkDiagnosticProgress
        {
            Stage = "正在生成分层结论",
            FractionCompleted = 0.96,
            GatewaySamplesCompleted = gatewayResult.Ping?.Sent ?? 0,
            WirelessSamplesCompleted = gatewayResult.WirelessSamples.Count,
        });

        var layers = new[]
        {
            BuildWirelessLayer(gatewayResult.WirelessSamples),
            BuildLocalNetworkLayer(gatewayResult.Ping, context),
            BuildInternetLayer(externalPing, dns, https),
            BuildProxyVpnLayer(context),
        };

        var report = new DiagnosticReport
        {
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.Now,
            Gateway = context.WifiDefaultGateway?.ToString(),
            InterfaceName = context.WifiInterfaceName,
            BaselineDescription =
                "DNS 使用 UDP 直连 1.1.1.1；HTTPS 使用 UseProxy=false 访问 Apple success 页面。"
                + "该基线不经过系统 HTTP 代理，但 VPN/网络扩展仍可能在更低层接管流量。",
            WirelessSamples = gatewayResult.WirelessSamples,
            GatewayPing = gatewayResult.Ping,
            ExternalPing = externalPing,
            Dns = dns,
            Https = https,
            Layers = layers,
        };

        progress?.Report(new NetworkDiagnosticProgress
        {
            Stage = "体检完成",
            FractionCompleted = 1,
            GatewaySamplesCompleted = gatewayResult.Ping?.Sent ?? 0,
            WirelessSamplesCompleted = gatewayResult.WirelessSamples.Count,
        });
        return report;
    }

    private async Task<GatewaySamplingResult> SampleWirelessAndGatewayAsync(
        IPAddress? gateway,
        NetworkDiagnosticOptions options,
        IProgress<NetworkDiagnosticProgress>? progress,
        CancellationToken cancellationToken)
    {
        var samples = new List<WifiSnapshot>();
        var roundTrips = new List<double>();
        var sent = 0;
        var stopwatch = Stopwatch.StartNew();
        var nextSampleAt = TimeSpan.Zero;

        using var ping = new Ping();
        while (samples.Count == 0 || nextSampleAt < options.Duration)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var delay = nextSampleAt - stopwatch.Elapsed;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            if (samples.Count > 0 && stopwatch.Elapsed >= options.Duration)
            {
                break;
            }

            var wifiTask = CaptureWirelessSampleAsync(cancellationToken);
            if (gateway is not null)
            {
                sent++;
                var roundTrip = await PingOnceAsync(
                    ping,
                    gateway,
                    options.PingTimeout,
                    cancellationToken).ConfigureAwait(false);
                if (roundTrip is not null)
                {
                    roundTrips.Add(roundTrip.Value);
                }
            }

            samples.Add(await wifiTask.ConfigureAwait(false));
            nextSampleAt += options.GatewayPingInterval;

            var fraction = Math.Clamp(stopwatch.Elapsed.TotalSeconds / options.Duration.TotalSeconds, 0, 1);
            progress?.Report(new NetworkDiagnosticProgress
            {
                Stage = "正在采样无线信号与网关链路",
                FractionCompleted = 0.03 + (fraction * 0.9),
                GatewaySamplesCompleted = sent,
                WirelessSamplesCompleted = samples.Count,
            });
        }

        var statistics = gateway is null
            ? null
            : BuildPingStatistics(gateway.ToString(), sent, roundTrips);
        return new GatewaySamplingResult(samples, statistics);
    }

    private async Task<WifiSnapshot> CaptureWirelessSampleAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _wifiTelemetryProvider
                .GetCurrentAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // A failed WLAN sample must not erase the independently measured IP evidence.
            return WifiSnapshot.Unavailable;
        }
    }

    private static async Task<PingStatistics> PingFixedCountAsync(
        IPAddress target,
        int count,
        TimeSpan interval,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var roundTrips = new List<double>(count);
        var stopwatch = Stopwatch.StartNew();
        using var ping = new Ping();

        for (var index = 0; index < count; index++)
        {
            var dueAt = TimeSpan.FromTicks(interval.Ticks * index);
            var delay = dueAt - stopwatch.Elapsed;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            var roundTrip = await PingOnceAsync(ping, target, timeout, cancellationToken)
                .ConfigureAwait(false);
            if (roundTrip is not null)
            {
                roundTrips.Add(roundTrip.Value);
            }
        }

        return BuildPingStatistics(target.ToString(), count, roundTrips);
    }

    private static async Task<double?> PingOnceAsync(
        Ping ping,
        IPAddress target,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            var pingOptions = target.AddressFamily == AddressFamily.InterNetwork
                ? new PingOptions(64, true)
                : null;
            var reply = await ping.SendPingAsync(
                    target,
                    timeout,
                    PingPayload,
                    pingOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PingException)
        {
            return null;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    private static PingStatistics BuildPingStatistics(
        string host,
        int sent,
        IReadOnlyList<double> roundTrips)
    {
        var received = roundTrips.Count;
        var average = received > 0 ? roundTrips.Average() : (double?)null;
        double? jitter = null;
        if (average is not null)
        {
            // Product definition: jitter is the population standard deviation of successful RTTs.
            var variance = roundTrips.Sum(value => Math.Pow(value - average.Value, 2)) / received;
            jitter = Math.Sqrt(variance);
        }

        return new PingStatistics
        {
            Host = host,
            Sent = sent,
            Received = received,
            PacketLossPercent = sent == 0 ? 0 : (sent - received) * 100d / sent,
            MinimumMs = received > 0 ? roundTrips.Min() : null,
            AverageMs = average,
            MaximumMs = received > 0 ? roundTrips.Max() : null,
            JitterMs = jitter,
        };
    }

    private static async Task<EndpointTiming> ProbeDnsAsync(
        NetworkDiagnosticOptions options,
        CancellationToken cancellationToken)
    {
        var overallStopwatch = Stopwatch.StartNew();
        var lastFailure = "未收到有效响应。";

        for (var attempt = 1; attempt <= options.DnsMaximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transactionId = (ushort)RandomNumberGenerator.GetInt32(0, ushort.MaxValue + 1);
            var query = BuildDnsQuery(transactionId, options.DnsHostName);

            using var udp = new UdpClient(options.DnsServer.AddressFamily);
            udp.Connect(options.DnsServer);
            using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCancellation.CancelAfter(options.DnsAttemptTimeout);

            try
            {
                await udp.SendAsync(query, attemptCancellation.Token).ConfigureAwait(false);
                var response = await udp.ReceiveAsync(attemptCancellation.Token).ConfigureAwait(false);
                if (TryValidateDnsResponse(response.Buffer, transactionId, out var answers, out var detail))
                {
                    return new EndpointTiming
                    {
                        Succeeded = true,
                        Milliseconds = overallStopwatch.Elapsed.TotalMilliseconds,
                        Detail =
                            $"UDP 直连 {options.DnsServer.Address}:{options.DnsServer.Port} "
                            + $"在第 {attempt} 次返回 {answers} 条回答。",
                        Source = EvidenceSource.DnsProbe,
                    };
                }

                lastFailure = detail;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastFailure = $"第 {attempt} 次请求在 {options.DnsAttemptTimeout.TotalMilliseconds:0} ms 后超时。";
            }
            catch (SocketException error)
            {
                lastFailure = $"第 {attempt} 次 UDP 请求失败：{error.SocketErrorCode}。";
            }
        }

        return new EndpointTiming
        {
            Succeeded = false,
            Milliseconds = overallStopwatch.Elapsed.TotalMilliseconds,
            Detail =
                $"UDP 直连 {options.DnsServer.Address}:{options.DnsServer.Port} "
                + $"解析 {options.DnsHostName} 失败；{lastFailure}",
            Source = EvidenceSource.DnsProbe,
        };
    }

    private static byte[] BuildDnsQuery(ushort transactionId, string hostName)
    {
        var labels = hostName.Trim().TrimEnd('.').Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length == 0)
        {
            throw new ArgumentException("DNS 测试域名无效。", nameof(hostName));
        }

        var query = new List<byte>(64);
        AddUInt16(query, transactionId);
        AddUInt16(query, 0x0100); // Recursion desired.
        AddUInt16(query, 1); // QDCOUNT
        AddUInt16(query, 0);
        AddUInt16(query, 0);
        AddUInt16(query, 0);

        foreach (var label in labels)
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            if (bytes.Length is 0 or > 63)
            {
                throw new ArgumentException("DNS 标签长度无效。", nameof(hostName));
            }

            query.Add((byte)bytes.Length);
            query.AddRange(bytes);
        }

        query.Add(0);
        AddUInt16(query, 1); // A
        AddUInt16(query, 1); // IN
        return query.ToArray();
    }

    private static bool TryValidateDnsResponse(
        ReadOnlySpan<byte> response,
        ushort transactionId,
        out int answerCount,
        out string detail)
    {
        answerCount = 0;
        if (response.Length < 12)
        {
            detail = "DNS 响应短于 12 字节头部。";
            return false;
        }

        if (BinaryPrimitives.ReadUInt16BigEndian(response) != transactionId)
        {
            detail = "DNS 响应事务 ID 不匹配。";
            return false;
        }

        var flags = BinaryPrimitives.ReadUInt16BigEndian(response[2..]);
        if ((flags & 0x8000) == 0)
        {
            detail = "收到的数据包不是 DNS 响应。";
            return false;
        }

        if ((flags & 0x0200) != 0)
        {
            detail = "DNS UDP 响应被截断。";
            return false;
        }

        var responseCode = flags & 0x000F;
        if (responseCode != 0)
        {
            detail = $"DNS 服务器返回 RCODE {responseCode}。";
            return false;
        }

        answerCount = BinaryPrimitives.ReadUInt16BigEndian(response[6..]);
        if (answerCount == 0)
        {
            detail = "DNS 响应成功但没有回答记录。";
            return false;
        }

        detail = "DNS 响应有效。";
        return true;
    }

    private static void AddUInt16(ICollection<byte> destination, ushort value)
    {
        destination.Add((byte)(value >> 8));
        destination.Add((byte)value);
    }

    private static async Task<EndpointTiming> ProbeHttpsAsync(
        NetworkDiagnosticOptions options,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            Proxy = null,
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = options.HttpsTimeout,
        };
        using var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.HttpsTimeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, options.HttpsEndpoint);
            request.Headers.UserAgent.ParseAdd("WiFiHealthConsole/Windows");
            request.Headers.CacheControl = new CacheControlHeaderValue
            {
                NoCache = true,
                NoStore = true,
            };

            using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseContentRead,
                    timeout.Token)
                .ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            var hasSuccessMarker = body.Contains("Success", StringComparison.OrdinalIgnoreCase);
            var succeeded = response.IsSuccessStatusCode && hasSuccessMarker;
            return new EndpointTiming
            {
                Succeeded = succeeded,
                Milliseconds = stopwatch.Elapsed.TotalMilliseconds,
                Detail = succeeded
                    ? $"UseProxy=false 访问 Apple success 页面，HTTP {(int)response.StatusCode} 且内容校验通过。"
                    : $"UseProxy=false 请求返回 HTTP {(int)response.StatusCode}，"
                        + (hasSuccessMarker ? "状态码异常。" : "页面缺少 Success 标记，可能被门户页改写。"),
                Source = EvidenceSource.HttpsProbe,
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new EndpointTiming
            {
                Succeeded = false,
                Milliseconds = stopwatch.Elapsed.TotalMilliseconds,
                Detail = $"UseProxy=false 请求在 {options.HttpsTimeout.TotalMilliseconds:0} ms 后超时。",
                Source = EvidenceSource.HttpsProbe,
            };
        }
        catch (HttpRequestException error)
        {
            return new EndpointTiming
            {
                Succeeded = false,
                Milliseconds = stopwatch.Elapsed.TotalMilliseconds,
                Detail = $"UseProxy=false 请求失败：{error.Message}",
                Source = EvidenceSource.HttpsProbe,
            };
        }
    }

    private static LayerResult BuildWirelessLayer(IReadOnlyList<WifiSnapshot> samples)
    {
        var connected = samples.Where(sample => sample.IsConnected).ToArray();
        var source = connected.Length > 0 ? connected : samples.ToArray();
        var representative = source.LastOrDefault();
        var rssiValues = AvailableValues(source, sample => sample.RssiDbm);
        var snrValues = AvailableValues(source, sample => sample.SnrDb);
        var ccaValues = AvailableValues(source, sample => sample.CcaPercent);
        var rssi = rssiValues.Count > 0 ? (int?)Math.Round(rssiValues.Average()) : null;
        var snr = snrValues.Count > 0 ? (int?)Math.Round(snrValues.Average()) : null;
        var cca = ccaValues.Count > 0 ? (double?)ccaValues.Average() : null;
        var channelWidth = LastAvailable(source, sample => sample.ChannelWidthMHz);
        var band = LastAvailable(source, sample => sample.Band) ?? WiFiBand.Unknown;

        var rssiAssessment = HealthStandards.Rssi(rssi);
        var snrAssessment = HealthStandards.Snr(snr);
        var ccaAssessment = HealthStandards.Cca(cca);
        var widthAssessment = HealthStandards.ChannelWidth(channelWidth, band);
        var assessments = new[] { rssiAssessment, snrAssessment, ccaAssessment, widthAssessment };
        var metrics = new[]
        {
            DiagnosticMetric.FromAssessment(
                "wireless-rssi",
                "RSSI",
                rssi is null ? "未检测" : $"{rssi} dBm",
                rssiAssessment,
                "决定覆盖余量；过弱会降速、增加重传并导致断续。"),
            DiagnosticMetric.FromAssessment(
                "wireless-snr",
                "SNR",
                snr is null ? "未检测" : $"{snr} dB",
                snrAssessment,
                "反映信号高出噪声的余量；越低越容易受干扰。"),
            DiagnosticMetric.FromAssessment(
                "wireless-cca",
                "CCA",
                cca is null ? "未检测" : $"{cca:0.0}%",
                ccaAssessment,
                "反映无线信道繁忙程度；Windows 公共 API 不提供时不会估算。"),
            DiagnosticMetric.FromAssessment(
                "wireless-width",
                "信道频宽",
                channelWidth is null ? "未检测" : $"{channelWidth} MHz",
                widthAssessment,
                "频宽越大峰值越高，但覆盖的频谱更宽，也更容易与邻居重叠。"),
        };

        var grade = HealthStandards.Worst(assessments);
        var ssid = representative is not null && representative.Ssid.TryGetValue(out var ssidValue)
            ? ssidValue
            : "未取得 SSID";
        var evidence = new List<string>
        {
            $"共采集 {samples.Count} 个无线样本，其中 {connected.Length} 个标记为已连接。",
            $"当前网络：{ssid}；频段：{band.DisplayName()}；频宽：{(channelWidth is null ? "未检测" : $"{channelWidth} MHz")}。",
        };
        if (rssiValues.Count > 0)
        {
            evidence.Add($"RSSI 样本范围 {rssiValues.Min()}～{rssiValues.Max()} dBm，平均约 {rssi} dBm。");
        }

        return new LayerResult(
            DiagnosticLayer.Wireless,
            grade,
            HealthStandards.SummaryStatusLabel(assessments),
            grade switch
            {
                HealthGrade.Good => "当前无线信号样本处于正常范围。",
                HealthGrade.Warning => "无线信号或频宽配置需要关注。",
                HealthGrade.Critical => "无线空口指标已达到严重范围。",
                _ => "无线证据不足，未使用估算值下结论。",
            },
            evidence,
            metrics,
            grade switch
            {
                HealthGrade.Good => "继续检查局域网和宽带出口，确认瓶颈是否在无线之外。",
                HealthGrade.Warning => "靠近路由器复测，并结合信道雷达检查频段、频宽和重叠。",
                HealthGrade.Critical => "先改善覆盖或无线配置，再运行一次完整体检。",
                _ => "确认 Windows 位置权限与 Wi-Fi 连接状态后重试。",
            });
    }

    private static LayerResult BuildLocalNetworkLayer(
        PingStatistics? gatewayPing,
        NetworkContextSnapshot context)
    {
        var latency = HealthStandards.GatewayLatency(gatewayPing?.AverageMs);
        var jitter = HealthStandards.GatewayJitter(gatewayPing?.JitterMs);
        var loss = HealthStandards.GatewayLoss(gatewayPing?.PacketLossPercent);
        var assessments = new[] { latency, jitter, loss };
        var metrics = new[]
        {
            DiagnosticMetric.FromAssessment(
                "gateway-latency",
                "网关延迟",
                Milliseconds(gatewayPing?.AverageMs),
                latency,
                "反映电脑到路由器的响应速度；异常通常发生在家庭内部链路。"),
            DiagnosticMetric.FromAssessment(
                "gateway-jitter",
                "网关抖动",
                Milliseconds(gatewayPing?.JitterMs),
                jitter,
                "本工具以成功 RTT 的标准差计算；波动大会影响通话、游戏和远程控制。"),
            DiagnosticMetric.FromAssessment(
                "gateway-loss",
                "网关丢包",
                Percent(gatewayPing?.PacketLossPercent),
                loss,
                "家庭内部丢包会直接造成重传、卡顿和吞吐下降。"),
        };
        var grade = HealthStandards.Worst(assessments);
        var evidence = gatewayPing is null
            ? new[] { context.RouteSummary }
            : new[]
            {
                $"自动检测网关 {gatewayPing.Host}；发送 {gatewayPing.Sent}，收到 {gatewayPing.Received}。",
                $"RTT 最低 {Milliseconds(gatewayPing.MinimumMs)}，平均 {Milliseconds(gatewayPing.AverageMs)}，最高 {Milliseconds(gatewayPing.MaximumMs)}。",
            };

        return new LayerResult(
            DiagnosticLayer.LocalNetwork,
            grade,
            HealthStandards.SummaryStatusLabel(assessments),
            grade switch
            {
                HealthGrade.Good => "电脑到路由器的局域网链路稳定。",
                HealthGrade.Warning => "局域网延迟、抖动或丢包已经偏高。",
                HealthGrade.Critical => "家庭内部链路存在严重异常。",
                _ => "未找到可检测的 Wi-Fi 默认网关。",
            },
            evidence,
            metrics,
            grade switch
            {
                HealthGrade.Good => "局域网不是当前首要瓶颈，继续查看宽带出口。",
                HealthGrade.Warning => "靠近路由器复测，并暂停大流量局域网任务后比较。",
                HealthGrade.Critical => "先排查 Wi-Fi 覆盖、路由器负载和网线/节点回程，再测宽带。",
                _ => "确认电脑通过 Wi-Fi 联网并能取得默认网关后重试。",
            });
    }

    private static LayerResult BuildInternetLayer(
        PingStatistics externalPing,
        EndpointTiming dns,
        EndpointTiming https)
    {
        var latency = HealthStandards.InternetLatency(externalPing.AverageMs);
        var loss = HealthStandards.PublicIcmpLoss(externalPing.PacketLossPercent);
        var dnsAssessment = HealthStandards.Dns(dns);
        var httpsAssessment = HealthStandards.Https(https);
        var assessments = new[] { latency, loss, dnsAssessment, httpsAssessment };
        var metrics = new[]
        {
            DiagnosticMetric.FromAssessment(
                "internet-latency",
                "外网延迟",
                Milliseconds(externalPing.AverageMs),
                latency,
                "反映离开家庭网络后的基础响应；距离、运营商路由和拥塞都会影响。"),
            DiagnosticMetric.FromAssessment(
                "internet-loss",
                "公网 ICMP 丢包",
                Percent(externalPing.PacketLossPercent),
                loss,
                "公网可能降低 ICMP 优先级，因此只作为与 DNS、HTTPS 交叉验证的证据。"),
            DiagnosticMetric.FromAssessment(
                "dns-direct",
                "DNS 直连",
                Milliseconds(dns.Milliseconds),
                dnsAssessment,
                "域名解析慢会让网页和应用在开始连接前长时间等待。"),
            DiagnosticMetric.FromAssessment(
                "https-direct",
                "HTTPS 直连",
                Milliseconds(https.Milliseconds),
                httpsAssessment,
                "验证无系统代理时的 TLS 建连、HTTP 响应及门户页改写。"),
        };
        var grade = HealthStandards.Worst(assessments);
        var evidence = new[]
        {
            $"向 {externalPing.Host} 发送 {externalPing.Sent} 次 ICMP，收到 {externalPing.Received} 次。",
            dns.Detail,
            https.Detail,
        };

        return new LayerResult(
            DiagnosticLayer.Internet,
            grade,
            HealthStandards.SummaryStatusLabel(assessments),
            grade switch
            {
                HealthGrade.Good => "公网响应、DNS 与 HTTPS 基线正常。",
                HealthGrade.Warning => "宽带出口存在偏高延迟或参考性丢包。",
                HealthGrade.Critical => "DNS 或 HTTPS 基线失败，宽带出口存在严重异常。",
                _ => "宽带出口证据不足。",
            },
            evidence,
            metrics,
            grade switch
            {
                HealthGrade.Good => "如仍感觉慢，运行分阶段网速测速确认实际吞吐。",
                HealthGrade.Warning => "避开高峰复测，并比较无代理基线与当前实际链路。",
                HealthGrade.Critical => "先确认是否欠费、断网或被门户页拦截，再联系运营商。",
                _ => "确认网络可访问公网后重试。",
            });
    }

    private static LayerResult BuildProxyVpnLayer(NetworkContextSnapshot context)
    {
        const string standard =
            "检测系统/环境代理及常见 VPN 隧道接口；DNS 与 HTTPS 诊断基线主动绕过系统 HTTP 代理。";
        var proxy = HealthStandards.PathState(context.ProxyConfigured, context.ProxySummary, standard);
        var vpn = HealthStandards.PathState(
            context.VpnLikelyActive,
            context.VpnLikelyActive
                ? $"检测到可能的 VPN/隧道接口：{string.Join("、", context.VpnInterfaces)}。"
                : "未检测到常见 VPN/隧道接口。",
            standard);
        var assessments = new[] { proxy, vpn };
        var grade = HealthStandards.Worst(assessments);
        var metrics = new[]
        {
            DiagnosticMetric.FromAssessment(
                "path-proxy",
                "系统 / 环境代理",
                context.ProxyConfigured ? "已检测到" : "未检测到",
                proxy,
                "代理可能改变出口、DNS、延迟和可用带宽。"),
            DiagnosticMetric.FromAssessment(
                "path-vpn",
                "VPN / 隧道接口",
                context.VpnLikelyActive ? "已检测到" : "未检测到",
                vpn,
                "VPN 可能在系统代理之外接管底层流量，因此仍需做链路对比。"),
        };

        return new LayerResult(
            DiagnosticLayer.ProxyVpn,
            grade,
            HealthStandards.SummaryStatusLabel(assessments),
            grade == HealthGrade.Warning
                ? "检测到可能改变当前实际链路的代理或 VPN。"
                : "未检测到常见代理或 VPN 路径。",
            new[]
            {
                context.ProxySummary,
                context.VpnLikelyActive
                    ? $"VPN/隧道接口：{string.Join("、", context.VpnInterfaces)}。"
                    : "未发现常见 VPN/隧道接口。",
                "本次 DNS 和 HTTPS 基线没有使用系统 HTTP 代理。",
            },
            metrics,
            grade == HealthGrade.Warning
                ? "分别运行‘当前实际链路’与‘直连 Wi-Fi 基线’测速，再比较差异。"
                : "无需调整；若使用网络扩展类代理，仍应手动关闭后对比一次。");
    }

    private static List<T> AvailableValues<T>(
        IEnumerable<WifiSnapshot> samples,
        Func<WifiSnapshot, Observed<T>> selector)
    {
        var values = new List<T>();
        foreach (var sample in samples)
        {
            if (selector(sample).TryGetValue(out var value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static T? LastAvailable<T>(
        IEnumerable<WifiSnapshot> samples,
        Func<WifiSnapshot, Observed<T>> selector)
        where T : struct
    {
        foreach (var sample in samples.Reverse())
        {
            if (selector(sample).TryGetValue(out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static string Milliseconds(double? value) => value is null
        ? "未检测"
        : $"{value.Value.ToString("0.0", CultureInfo.InvariantCulture)} ms";

    private static string Percent(double? value) => value is null
        ? "未检测"
        : $"{value.Value.ToString("0.0", CultureInfo.InvariantCulture)}%";

    private sealed record GatewaySamplingResult(
        IReadOnlyList<WifiSnapshot> WirelessSamples,
        PingStatistics? Ping);
}
