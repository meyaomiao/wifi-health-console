using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using WiFiHealthConsole.Core;

[assembly: InternalsVisibleTo("WiFiHealthConsole.App.Tests")]

namespace WiFiHealthConsole.App.Services.Speed;

public sealed class CloudflareSpeedTestService : ISpeedTestProvider
{
    private readonly CloudflareSpeedTestOptions _options;
    private readonly Func<SpeedTestRoute, InterfaceBinding?, HttpMessageHandler>? _handlerFactory;

    public CloudflareSpeedTestService(CloudflareSpeedTestOptions? options = null)
        : this(options ?? new CloudflareSpeedTestOptions(), null)
    {
    }

    internal CloudflareSpeedTestService(
        CloudflareSpeedTestOptions options,
        Func<SpeedTestRoute, InterfaceBinding?, HttpMessageHandler>? handlerFactory)
    {
        _options = options;
        _handlerFactory = handlerFactory;
    }

    public string ProviderName => "Cloudflare Speed Test";

    public async Task<SpeedTestReport> RunAsync(
        SpeedTestRequest request,
        IProgress<SpeedTestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var phaseDuration = _options.PhaseDurationOverride
            ?? TimeSpan.FromSeconds(request.DurationPreset.PhaseRuntimeSeconds());
        _options.Validate(phaseDuration);

        var sampledWifi = ResolveWifiBinding(request.RequestedInterface, requireRequestedMatch: false);
        InterfaceBinding? measuredBinding = null;
        if (request.Route == SpeedTestRoute.DirectWiFiBaseline)
        {
            measuredBinding = ResolveWifiBinding(request.RequestedInterface, requireRequestedMatch: true)
                ?? throw new SpeedTestUnavailableException(
                    request.RequestedInterface is null
                        ? "没有找到可绑定的活动 Wi-Fi 地址，无法把本次结果标为直连 Wi-Fi 基线。"
                        : $"没有找到可绑定的 Wi-Fi 接口‘{request.RequestedInterface}’。");
        }

        using var handler = CreateHandler(request.Route, measuredBinding);
        using var client = new HttpClient(handler)
        {
            BaseAddress = _options.BaseUri,
            Timeout = Timeout.InfiniteTimeSpan,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WiFiHealthConsole/Windows");
        client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue
        {
            NoCache = true,
            NoStore = true,
        };

        var node = new NodeTracker();
        var idleLatency = await MeasureIdleLatencyAsync(client, node, cancellationToken)
            .ConfigureAwait(false);

        // These awaits are intentionally sequential. Upload workers do not exist until every
        // download worker has stopped, so the two curves can never represent concurrent traffic.
        var download = await RunPhaseAsync(
                SpeedTestPhase.Download,
                phaseDuration,
                client,
                node,
                progress,
                cancellationToken)
            .ConfigureAwait(false);
        var upload = await RunPhaseAsync(
                SpeedTestPhase.Upload,
                phaseDuration,
                client,
                node,
                progress,
                cancellationToken)
            .ConfigureAwait(false);

        var nodeName = node.Value;
        return new SpeedTestReport
        {
            CompletedAt = DateTimeOffset.Now,
            Route = request.Route,
            DurationPreset = request.DurationPreset,
            RequestedInterface = request.RequestedInterface,
            SampledInterface = sampledWifi?.Name,
            SampledInterfaceId = sampledWifi?.Id,
            MeasuredInterface = measuredBinding?.Name,
            MeasuredInterfaceId = measuredBinding?.Id,
            PathDescription = request.Route == SpeedTestRoute.CurrentPath
                ? "跟随 Windows 系统当前默认路由"
                : $"已绑定 Wi-Fi 接口 {measuredBinding!.Name}",
            Endpoint = nodeName is null
                ? _options.BaseUri.Host
                : $"{_options.BaseUri.Host} · {nodeName}",
            DownloadBitsPerSecond = download.BitsPerSecond,
            UploadBitsPerSecond = upload.BitsPerSecond,
            IdleLatencyMs = idleLatency,
            DownloadResponsivenessRpm = download.ResponsivenessRpm,
            UploadResponsivenessRpm = upload.ResponsivenessRpm,
            DownloadedBytes = download.TransferredBytes,
            UploadedBytes = upload.TransferredBytes,
            DurationSeconds = download.Elapsed.TotalSeconds + upload.Elapsed.TotalSeconds,
            WasProxied = request.Route == SpeedTestRoute.CurrentPath
                && IsSystemProxySelected(_options.BaseUri),
        };
    }

    private HttpMessageHandler CreateHandler(
        SpeedTestRoute route,
        InterfaceBinding? binding)
    {
        if (_handlerFactory is not null)
        {
            return _handlerFactory(route, binding);
        }

        var handler = new SocketsHttpHandler
        {
            UseProxy = route == SpeedTestRoute.CurrentPath,
            Proxy = null,
            AutomaticDecompression = DecompressionMethods.None,
            AllowAutoRedirect = false,
            ConnectTimeout = _options.RequestTimeout,
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = _options.ParallelConnections + 2,
        };

        if (binding is not null)
        {
            handler.ConnectCallback = (context, cancellationToken) =>
                ConnectBoundAsync(context, binding.LocalAddress, cancellationToken);
        }

        return handler;
    }

    private async Task<double?> MeasureIdleLatencyAsync(
        HttpClient client,
        NodeTracker node,
        CancellationToken cancellationToken)
    {
        var measurements = new List<double>(3);
        for (var index = 0; index < 3; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(_options.RequestTimeout);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    BuildDownloadUri(0, index));
                request.Headers.AcceptEncoding.ParseAdd("identity");
                using var response = await client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        requestTimeout.Token)
                    .ConfigureAwait(false);
                node.Observe(response);
                response.EnsureSuccessStatusCode();
                measurements.Add(stopwatch.Elapsed.TotalMilliseconds);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error) when (IsRecoverableNetworkError(error))
            {
                // Throughput phases provide the decisive evidence; idle latency can remain absent.
            }
        }

        if (measurements.Count == 0)
        {
            return null;
        }

        measurements.Sort();
        return measurements[measurements.Count / 2];
    }

    private async Task<PhaseMeasurement> RunPhaseAsync(
        SpeedTestPhase phase,
        TimeSpan duration,
        HttpClient client,
        NodeTracker node,
        IProgress<SpeedTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        var counter = new ByteCounter();
        var errors = new ConcurrentQueue<Exception>();
        using var phaseCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var workers = Enumerable.Range(0, _options.ParallelConnections)
            .Select(workerId => phase == SpeedTestPhase.Download
                ? DownloadWorkerAsync(client, workerId, counter, node, errors, phaseCancellation.Token)
                : UploadWorkerAsync(client, workerId, counter, node, errors, phaseCancellation.Token))
            .ToArray();
        var responsiveness = MeasureLoadedResponsivenessAsync(
            client,
            node,
            phaseCancellation.Token);

        var stopwatch = Stopwatch.StartNew();
        var checkpoints = new List<ThroughputCheckpoint>
        {
            new(TimeSpan.Zero, 0),
        };
        long previousBytes = 0;
        var previousElapsed = TimeSpan.Zero;

        try
        {
            while (stopwatch.Elapsed < duration)
            {
                var remaining = duration - stopwatch.Elapsed;
                var delay = remaining < _options.SampleInterval
                    ? remaining
                    : _options.SampleInterval;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }

                var elapsed = stopwatch.Elapsed < duration ? stopwatch.Elapsed : duration;
                var bytes = counter.Value;
                var intervalSeconds = (elapsed - previousElapsed).TotalSeconds;
                var intervalBytes = Math.Max(0, bytes - previousBytes);
                var mbps = intervalSeconds > 0
                    ? intervalBytes * 8d / intervalSeconds / 1_000_000d
                    : 0;
                checkpoints.Add(new ThroughputCheckpoint(elapsed, bytes));

                progress?.Report(new SpeedTestProgress
                {
                    Phase = phase,
                    Sample = new SpeedTestSample
                    {
                        Phase = phase,
                        ElapsedSeconds = elapsed.TotalSeconds,
                        Mbps = mbps,
                        Timestamp = DateTimeOffset.Now,
                    },
                    PhaseFractionCompleted = Math.Clamp(
                        elapsed.TotalSeconds / duration.TotalSeconds,
                        0,
                        1),
                    TransferredBytes = bytes,
                    Node = node.Value,
                });

                previousBytes = bytes;
                previousElapsed = elapsed;
            }
        }
        finally
        {
            phaseCancellation.Cancel();
            await ObserveWorkerCompletionAsync(workers).ConfigureAwait(false);
        }

        var responsivenessRpm = await responsiveness.ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        var totalBytes = counter.Value;
        var bytesAtMeasurementEnd = checkpoints[^1].TotalBytes;
        if (bytesAtMeasurementEnd <= 0)
        {
            errors.TryPeek(out var error);
            throw new SpeedTestUnavailableException(
                phase == SpeedTestPhase.Download
                    ? "Cloudflare 下载测速没有收到数据。"
                    : "Cloudflare 上传测速没有发送数据。",
                error);
        }

        var finalElapsed = checkpoints[^1].Elapsed;
        var warmupCheckpoint = checkpoints
            .FirstOrDefault(checkpoint => checkpoint.Elapsed >= _options.WarmupDuration)
            ?? checkpoints[0];
        var measuredSeconds = (finalElapsed - warmupCheckpoint.Elapsed).TotalSeconds;
        var measuredBytes = Math.Max(0, bytesAtMeasurementEnd - warmupCheckpoint.TotalBytes);
        if (measuredSeconds <= 0 || measuredBytes <= 0)
        {
            throw new SpeedTestUnavailableException(
                $"{phase} 测速在排除 {_options.WarmupDuration.TotalSeconds:0.0} 秒预热后没有足够样本。");
        }

        return new PhaseMeasurement(
            measuredBytes * 8d / measuredSeconds,
            totalBytes,
            finalElapsed,
            responsivenessRpm);
    }

    private async Task<double?> MeasureLoadedResponsivenessAsync(
        HttpClient client,
        NodeTracker node,
        CancellationToken phaseCancellation)
    {
        var measurements = new List<double>();
        var requestIndex = 0;

        while (!phaseCancellation.IsCancellationRequested)
        {
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(phaseCancellation);
            requestTimeout.CancelAfter(_options.RequestTimeout);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    BuildDownloadUri(0, 800_000_000 + requestIndex++));
                request.Headers.AcceptEncoding.ParseAdd("identity");
                using var response = await client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        requestTimeout.Token)
                    .ConfigureAwait(false);
                node.Observe(response);
                response.EnsureSuccessStatusCode();
                measurements.Add(stopwatch.Elapsed.TotalMilliseconds);
            }
            catch (OperationCanceledException) when (phaseCancellation.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error) when (IsRecoverableNetworkError(error))
            {
                // An incomplete probe is not converted into a synthetic latency or RPM value.
            }

            try
            {
                await Task.Delay(_options.ResponsivenessProbeInterval, phaseCancellation)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (phaseCancellation.IsCancellationRequested)
            {
                break;
            }
        }

        return CalculateResponsivenessRpm(measurements, _options.MinimumResponsivenessSamples);
    }

    internal static double? CalculateResponsivenessRpm(
        IEnumerable<double> roundTripMilliseconds,
        int minimumSamples)
    {
        ArgumentNullException.ThrowIfNull(roundTripMilliseconds);
        if (minimumSamples < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumSamples));
        }

        var samples = roundTripMilliseconds
            .Where(value => double.IsFinite(value) && value > 0)
            .Order()
            .ToArray();
        if (samples.Length < minimumSamples)
        {
            return null;
        }

        var middle = samples.Length / 2;
        var median = samples.Length % 2 == 0
            ? (samples[middle - 1] + samples[middle]) / 2d
            : samples[middle];
        var rpm = 60_000d / median;
        return double.IsFinite(rpm) && rpm > 0 ? rpm : null;
    }

    private async Task DownloadWorkerAsync(
        HttpClient client,
        int workerId,
        ByteCounter counter,
        NodeTracker node,
        ConcurrentQueue<Exception> errors,
        CancellationToken phaseCancellation)
    {
        var requestIndex = 0;
        var buffer = new byte[128 * 1024];
        while (!phaseCancellation.IsCancellationRequested)
        {
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(phaseCancellation);
            requestTimeout.CancelAfter(_options.RequestTimeout);

            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    BuildDownloadUri(_options.DownloadChunkBytes, (workerId * 1_000_000) + requestIndex++));
                request.Headers.AcceptEncoding.ParseAdd("identity");
                using var response = await client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        requestTimeout.Token)
                    .ConfigureAwait(false);
                node.Observe(response);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content
                    .ReadAsStreamAsync(requestTimeout.Token)
                    .ConfigureAwait(false);
                while (!requestTimeout.IsCancellationRequested)
                {
                    var read = await stream
                        .ReadAsync(buffer.AsMemory(), requestTimeout.Token)
                        .ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    counter.Add(read);
                }
            }
            catch (OperationCanceledException) when (phaseCancellation.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error) when (IsRecoverableNetworkError(error))
            {
                errors.Enqueue(error);
                await DelayBeforeRetryAsync(phaseCancellation).ConfigureAwait(false);
            }
        }
    }

    private async Task UploadWorkerAsync(
        HttpClient client,
        int workerId,
        ByteCounter counter,
        NodeTracker node,
        ConcurrentQueue<Exception> errors,
        CancellationToken phaseCancellation)
    {
        var requestIndex = 0;
        while (!phaseCancellation.IsCancellationRequested)
        {
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(phaseCancellation);
            requestTimeout.CancelAfter(_options.RequestTimeout);

            try
            {
                var uri = new Uri(
                    _options.BaseUri,
                    $"__up?cacheBust={workerId}-{requestIndex++}-{Guid.NewGuid():N}");
                using var request = new HttpRequestMessage(HttpMethod.Post, uri)
                {
                    Content = new CountingUploadContent(_options.UploadChunkBytes, counter.Add),
                };
                request.Headers.ExpectContinue = false;
                using var response = await client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        requestTimeout.Token)
                    .ConfigureAwait(false);
                node.Observe(response);
                response.EnsureSuccessStatusCode();
            }
            catch (OperationCanceledException) when (phaseCancellation.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error) when (IsRecoverableNetworkError(error))
            {
                errors.Enqueue(error);
                await DelayBeforeRetryAsync(phaseCancellation).ConfigureAwait(false);
            }
        }
    }

    private Uri BuildDownloadUri(int bytes, int requestIndex) => new(
        _options.BaseUri,
        $"__down?bytes={bytes}&cacheBust={requestIndex}-{Guid.NewGuid():N}");

    private static async Task DelayBeforeRetryAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task ObserveWorkerCompletionAsync(IEnumerable<Task> workers)
    {
        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Phase cancellation is how a fixed-duration measurement stops in-flight I/O.
        }
    }

    private static bool IsRecoverableNetworkError(Exception error) =>
        error is HttpRequestException or IOException or SocketException or OperationCanceledException;

    private static InterfaceBinding? ResolveWifiBinding(
        string? requestedInterface,
        bool requireRequestedMatch)
    {
        try
        {
            var candidates = NetworkInterface.GetAllNetworkInterfaces()
                .Where(item =>
                    item.OperationalStatus == OperationalStatus.Up
                    && item.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                .SelectMany(item => item.GetIPProperties().UnicastAddresses
                    .Select(address => new
                    {
                        Interface = item,
                        Address = address.Address,
                        HasGateway = item.GetIPProperties().GatewayAddresses.Any(gateway =>
                            gateway.Address is not null
                            && !IPAddress.Any.Equals(gateway.Address)
                            && !IPAddress.IPv6Any.Equals(gateway.Address)),
                    }))
                .Where(candidate =>
                    !IPAddress.IsLoopback(candidate.Address)
                    && !candidate.Address.IsIPv6LinkLocal
                    && candidate.Address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                .ToArray();

            if (!string.IsNullOrWhiteSpace(requestedInterface))
            {
                var requested = candidates
                    .Where(candidate =>
                        string.Equals(candidate.Interface.Name, requestedInterface, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(candidate.Interface.Id, requestedInterface, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(candidate.Interface.Description, requestedInterface, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(candidate => candidate.Address.AddressFamily == AddressFamily.InterNetwork)
                    .FirstOrDefault();
                if (requested is not null)
                {
                    return new InterfaceBinding(
                        requested.Interface.Name,
                        requested.Interface.Id,
                        requested.Address);
                }

                if (requireRequestedMatch)
                {
                    return null;
                }
            }

            var selected = candidates
                .OrderByDescending(candidate => candidate.HasGateway)
                .ThenByDescending(candidate => candidate.Address.AddressFamily == AddressFamily.InterNetwork)
                .FirstOrDefault();
            return selected is null
                ? null
                : new InterfaceBinding(selected.Interface.Name, selected.Interface.Id, selected.Address);
        }
        catch (NetworkInformationException)
        {
            return null;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    private static async ValueTask<Stream> ConnectBoundAsync(
        SocketsHttpConnectionContext context,
        IPAddress localAddress,
        CancellationToken cancellationToken)
    {
        var addresses = await Dns
            .GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken)
            .ConfigureAwait(false);
        Exception? lastError = null;

        foreach (var address in addresses.Where(address => address.AddressFamily == localAddress.AddressFamily))
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
            };
            try
            {
                socket.Bind(new IPEndPoint(localAddress, 0));
                await socket
                    .ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken)
                    .ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception error) when (error is SocketException or IOException)
            {
                lastError = error;
                socket.Dispose();
            }
        }

        throw new HttpRequestException(
            $"无法从 Wi-Fi 本地地址 {localAddress} 连接 {context.DnsEndPoint}。",
            lastError);
    }

    private static bool IsSystemProxySelected(Uri endpoint)
    {
        try
        {
            var proxy = HttpClient.DefaultProxy;
            if (proxy.IsBypassed(endpoint))
            {
                return false;
            }

            var proxyUri = proxy.GetProxy(endpoint);
            return proxyUri is not null && proxyUri != endpoint;
        }
        catch
        {
            return false;
        }
    }

    private sealed class ByteCounter
    {
        private long _value;

        public long Value => Interlocked.Read(ref _value);

        public void Add(int value) => Interlocked.Add(ref _value, value);
    }

    private sealed class NodeTracker
    {
        private readonly object _gate = new();
        private string? _value;

        public string? Value
        {
            get
            {
                lock (_gate)
                {
                    return _value;
                }
            }
        }

        public void Observe(HttpResponseMessage response)
        {
            if (!response.Headers.TryGetValues("CF-Ray", out var values))
            {
                return;
            }

            var ray = values.FirstOrDefault();
            var separator = ray?.LastIndexOf('-') ?? -1;
            if (separator < 0 || separator == ray!.Length - 1)
            {
                return;
            }

            var candidate = ray[(separator + 1)..].Trim().ToUpperInvariant();
            if (candidate.Length is < 3 or > 8)
            {
                return;
            }

            lock (_gate)
            {
                _value ??= candidate;
            }
        }
    }

    private sealed class CountingUploadContent : HttpContent
    {
        private static readonly byte[] Payload = CreatePayload();

        private readonly int _length;
        private readonly Action<int> _countBytes;

        public CountingUploadContent(int length, Action<int> countBytes)
        {
            _length = length;
            _countBytes = countBytes;
            Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            Headers.ContentLength = length;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            WriteToStreamAsync(stream, CancellationToken.None);

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken) =>
            WriteToStreamAsync(stream, cancellationToken);

        protected override bool TryComputeLength(out long length)
        {
            length = _length;
            return true;
        }

        private async Task WriteToStreamAsync(Stream stream, CancellationToken cancellationToken)
        {
            var remaining = _length;
            while (remaining > 0)
            {
                var count = Math.Min(remaining, Payload.Length);
                await stream
                    .WriteAsync(Payload.AsMemory(0, count), cancellationToken)
                    .ConfigureAwait(false);
                _countBytes(count);
                remaining -= count;
            }
        }

        private static byte[] CreatePayload()
        {
            var payload = new byte[128 * 1024];
            RandomNumberGenerator.Fill(payload);
            return payload;
        }
    }

    private sealed record ThroughputCheckpoint(TimeSpan Elapsed, long TotalBytes);

    private sealed record PhaseMeasurement(
        double BitsPerSecond,
        long TransferredBytes,
        TimeSpan Elapsed,
        double? ResponsivenessRpm);
}
