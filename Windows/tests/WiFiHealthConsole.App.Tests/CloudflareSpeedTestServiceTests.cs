using System.Net;
using System.Net.Http.Headers;
using WiFiHealthConsole.App.Services.Speed;
using WiFiHealthConsole.Core;

namespace WiFiHealthConsole.App.Tests;

public sealed class CloudflareSpeedTestServiceTests
{
    [Fact]
    public void ResponsivenessUsesTheMedianOfEnoughRealRoundTrips()
    {
        var rpm = CloudflareSpeedTestService.CalculateResponsivenessRpm(
            [80, 100, 120, double.NaN, -1],
            minimumSamples: 3);

        Assert.Equal(600, rpm);
        Assert.Null(CloudflareSpeedTestService.CalculateResponsivenessRpm([80, 100], 3));
    }

    [Fact]
    public async Task ProductionPipelineSamplesResponsivenessDuringBothThroughputPhases()
    {
        var options = new CloudflareSpeedTestOptions
        {
            PhaseDurationOverride = TimeSpan.FromMilliseconds(350),
            SampleInterval = TimeSpan.FromMilliseconds(35),
            WarmupDuration = TimeSpan.FromMilliseconds(70),
            ResponsivenessProbeInterval = TimeSpan.FromMilliseconds(20),
            MinimumResponsivenessSamples = 3,
            RequestTimeout = TimeSpan.FromMilliseconds(200),
            ParallelConnections = 1,
            DownloadChunkBytes = 64 * 1024,
            UploadChunkBytes = 64 * 1024
        };
        using var handler = new DeterministicSpeedHandler();
        var service = new CloudflareSpeedTestService(options, (_, _) => handler);

        var report = await service.RunAsync(new SpeedTestRequest
        {
            Route = SpeedTestRoute.CurrentPath,
            DurationPreset = SpeedTestDurationPreset.Standard
        });

        Assert.True(report.DownloadBitsPerSecond > 0);
        Assert.True(report.UploadBitsPerSecond > 0);
        Assert.NotNull(report.DownloadResponsivenessRpm);
        Assert.NotNull(report.UploadResponsivenessRpm);
        Assert.True(report.DownloadResponsivenessRpm > 0);
        Assert.True(report.UploadResponsivenessRpm > 0);
        Assert.Null(report.MeasuredInterface);
        Assert.Null(report.MeasuredInterfaceId);
        Assert.Equal("跟随 Windows 系统当前默认路由", report.PathDescription);
    }

    [Fact]
    public async Task FailedLoadedProbesStayUnavailableInsteadOfCreatingSyntheticRpm()
    {
        var options = new CloudflareSpeedTestOptions
        {
            PhaseDurationOverride = TimeSpan.FromMilliseconds(220),
            SampleInterval = TimeSpan.FromMilliseconds(25),
            WarmupDuration = TimeSpan.FromMilliseconds(50),
            ResponsivenessProbeInterval = TimeSpan.FromMilliseconds(20),
            MinimumResponsivenessSamples = 3,
            RequestTimeout = TimeSpan.FromMilliseconds(100),
            ParallelConnections = 1,
            DownloadChunkBytes = 64 * 1024,
            UploadChunkBytes = 64 * 1024
        };
        using var handler = new DeterministicSpeedHandler(failResponsivenessProbes: true);
        var service = new CloudflareSpeedTestService(options, (_, _) => handler);

        var report = await service.RunAsync(new SpeedTestRequest());

        Assert.True(report.DownloadBitsPerSecond > 0);
        Assert.True(report.UploadBitsPerSecond > 0);
        Assert.Null(report.DownloadResponsivenessRpm);
        Assert.Null(report.UploadResponsivenessRpm);
    }

    private sealed class DeterministicSpeedHandler : HttpMessageHandler
    {
        private static readonly byte[] DownloadPayload = new byte[64 * 1024];
        private readonly bool _failResponsivenessProbes;

        public DeterministicSpeedHandler(bool failResponsivenessProbes = false)
        {
            _failResponsivenessProbes = failResponsivenessProbes;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath;
            if (path == "/__up" && request.Content is not null)
            {
                await request.Content.CopyToAsync(Stream.Null, cancellationToken);
            }

            var isResponsivenessProbe = path == "/__down"
                && request.RequestUri?.Query.Contains("bytes=0", StringComparison.Ordinal) == true;
            if (isResponsivenessProbe && _failResponsivenessProbes)
            {
                throw new HttpRequestException("模拟负载响应探针失败");
            }

            await Task.Delay(
                isResponsivenessProbe ? TimeSpan.FromMilliseconds(8) : TimeSpan.FromMilliseconds(2),
                cancellationToken);

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = path == "/__down" && !isResponsivenessProbe
                    ? new ByteArrayContent(DownloadPayload)
                    : new ByteArrayContent([])
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            response.Headers.TryAddWithoutValidation("CF-Ray", "test-ray-SJC");
            return response;
        }
    }
}
