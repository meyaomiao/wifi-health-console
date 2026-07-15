using WiFiHealthConsole.App.Services.History;
using WiFiHealthConsole.App.ViewModels;
using WiFiHealthConsole.Core;

namespace WiFiHealthConsole.App.Tests;

public sealed class HistoryPageViewModelTests
{
    [Fact]
    public async Task BeforeAndAfterRssiUseCoreThresholdsAndDeclineIsNotGreen()
    {
        var before = Sample(DateTimeOffset.Parse("2026-07-15T10:00:00+08:00"), HistoryMarker.Before, -60, ssid: "Demo_5G");
        var after = Sample(DateTimeOffset.Parse("2026-07-15T10:10:00+08:00"), HistoryMarker.After, -70, ssid: "Demo_5G");
        var viewModel = new HistoryPageViewModel(new MemoryHistoryStore([before, after]));

        await viewModel.InitializeAsync();

        AssertAssessment(HealthStandards.Rssi(-60), viewModel.BeforeRssiMetric);
        AssertAssessment(HealthStandards.Rssi(-70), viewModel.AfterRssiMetric);
        Assert.Equal("下降 10 dB", viewModel.RssiChange);
        Assert.Same(UiPalette.Warning, viewModel.RssiChangeForeground);
        Assert.NotSame(UiPalette.Good, viewModel.RssiChangeForeground);
    }

    [Fact]
    public async Task MissingRssiStaysUnavailableInsteadOfNormal()
    {
        var before = Sample(DateTimeOffset.Parse("2026-07-15T10:00:00+08:00"), HistoryMarker.Before, null, ssid: "Demo_5G");
        var after = Sample(DateTimeOffset.Parse("2026-07-15T10:10:00+08:00"), HistoryMarker.After, null, ssid: "Demo_5G");
        var viewModel = new HistoryPageViewModel(new MemoryHistoryStore([before, after]));

        await viewModel.InitializeAsync();

        Assert.Equal("--", viewModel.BeforeRssiMetric.Value);
        Assert.Equal("--", viewModel.AfterRssiMetric.Value);
        AssertAssessment(HealthStandards.Rssi((int?)null), viewModel.BeforeRssiMetric);
        AssertAssessment(HealthStandards.Rssi((int?)null), viewModel.AfterRssiMetric);
        Assert.Equal("等待前后标记", viewModel.RssiChange);
        Assert.Same(UiPalette.Reference, viewModel.RssiChangeForeground);
    }

    [Fact]
    public async Task RowsAndComparisonExposeTheSameDetailedNetworkEvidence()
    {
        var before = Sample(
            DateTimeOffset.Parse("2026-07-15T10:00:00+08:00"),
            HistoryMarker.Before,
            -61,
            ssid: "Demo_5G",
            channel: 40,
            channelWidth: 160,
            gatewayAverageMs: 18.4);
        var after = Sample(
            DateTimeOffset.Parse("2026-07-15T10:15:00+08:00"),
            HistoryMarker.After,
            -54,
            ssid: "Demo_5G",
            channel: 149,
            channelWidth: 80,
            gatewayAverageMs: 3.2);
        var viewModel = new HistoryPageViewModel(new MemoryHistoryStore([before, after]));

        await viewModel.InitializeAsync();

        Assert.True(viewModel.HasHistory);
        Assert.False(viewModel.ShowEmptyState);
        Assert.Equal("Demo_5G", viewModel.BeforeComparison.Ssid);
        Assert.Equal("Ch 40", viewModel.BeforeComparison.Channel);
        Assert.Equal("160 MHz", viewModel.BeforeComparison.ChannelWidth);
        Assert.Equal("18.4 ms", viewModel.BeforeComparison.GatewayLatency);
        Assert.Equal("Ch 149", viewModel.AfterComparison.Channel);
        Assert.Equal("80 MHz", viewModel.AfterComparison.ChannelWidth);
        Assert.Equal("Demo_5G", viewModel.Rows[0].Ssid);
        Assert.Equal("Ch 149", viewModel.Rows[0].Channel);
        Assert.Equal("80 MHz", viewModel.Rows[0].ChannelWidth);
        Assert.Equal("3.2 ms", viewModel.Rows[0].GatewayLatency);
        Assert.Equal(3, viewModel.Rows[0].MarkerOptions.Count);
        Assert.Equal(HistoryMarker.After, viewModel.Rows[0].SelectedMarker.Value);
    }

    [Fact]
    public async Task AnyVisibleRowCanBeMarkedAndTheChoiceIsPersisted()
    {
        var older = Sample(DateTimeOffset.Parse("2026-07-15T10:00:00+08:00"), HistoryMarker.None, -60);
        var newer = Sample(DateTimeOffset.Parse("2026-07-15T10:10:00+08:00"), HistoryMarker.None, -58);
        var store = new MemoryHistoryStore([older, newer]);
        var viewModel = new HistoryPageViewModel(store);
        await viewModel.InitializeAsync();

        await viewModel.SetMarkerAsync(older.Id, HistoryMarker.Before);
        await viewModel.SetMarkerAsync(newer.Id, HistoryMarker.After);

        Assert.Equal(2, store.SaveCount);
        Assert.Equal(HistoryMarker.Before, store.Samples.Single(sample => sample.Id == older.Id).Marker);
        Assert.Equal(HistoryMarker.After, store.Samples.Single(sample => sample.Id == newer.Id).Marker);
        Assert.Equal(older.Timestamp.ToLocalTime().ToString("MM-dd HH:mm") + " · 无线采样", viewModel.BeforeComparison.Meta);
        Assert.Equal(newer.Timestamp.ToLocalTime().ToString("MM-dd HH:mm") + " · 无线采样", viewModel.AfterComparison.Meta);
    }

    [Fact]
    public async Task ReversedBeforeAndAfterMarkersAreNotCompared()
    {
        var newerBefore = Sample(
            DateTimeOffset.Parse("2026-07-15T10:10:00+08:00"),
            HistoryMarker.Before,
            -60,
            ssid: "Demo_5G");
        var olderAfter = Sample(
            DateTimeOffset.Parse("2026-07-15T10:00:00+08:00"),
            HistoryMarker.After,
            -50,
            ssid: "Demo_5G");
        var viewModel = new HistoryPageViewModel(new MemoryHistoryStore([olderAfter, newerBefore]));

        await viewModel.InitializeAsync();

        Assert.Equal("暂不可比较", viewModel.RssiChange);
        Assert.Contains("必须晚于", viewModel.RssiChangeExplanation);
        Assert.Equal("--", viewModel.GatewayChange);
        Assert.Same(UiPalette.Reference, viewModel.RssiChangeForeground);
    }

    [Fact]
    public async Task DifferentSsidsAreNotComparedAsOneRouterChange()
    {
        var before = Sample(
            DateTimeOffset.Parse("2026-07-15T10:00:00+08:00"),
            HistoryMarker.Before,
            -70,
            ssid: "Demo_5G",
            gatewayAverageMs: 12);
        var after = Sample(
            DateTimeOffset.Parse("2026-07-15T10:10:00+08:00"),
            HistoryMarker.After,
            -50,
            ssid: "Phone-Hotspot",
            gatewayAverageMs: 3);
        var viewModel = new HistoryPageViewModel(new MemoryHistoryStore([before, after]));

        await viewModel.InitializeAsync();

        Assert.Equal("暂不可比较", viewModel.RssiChange);
        Assert.Contains("不同 Wi-Fi", viewModel.RssiChangeExplanation);
        Assert.Equal("--", viewModel.GatewayChange);
    }

    [Fact]
    public async Task MissingSsidEvidenceDoesNotProduceAChangeClaim()
    {
        var before = Sample(
            DateTimeOffset.Parse("2026-07-15T10:00:00+08:00"),
            HistoryMarker.Before,
            -70);
        var after = Sample(
            DateTimeOffset.Parse("2026-07-15T10:10:00+08:00"),
            HistoryMarker.After,
            -50);
        var viewModel = new HistoryPageViewModel(new MemoryHistoryStore([before, after]));

        await viewModel.InitializeAsync();

        Assert.Equal("暂不可比较", viewModel.RssiChange);
        Assert.Contains("SSID 证据不完整", viewModel.RssiChangeExplanation);
    }

    [Fact]
    public async Task ClearHistoryRequiresConfirmationAndReturnsToEmptyState()
    {
        var store = new MemoryHistoryStore(
            [Sample(DateTimeOffset.Parse("2026-07-15T10:00:00+08:00"), HistoryMarker.None, -60)]);
        var viewModel = new HistoryPageViewModel(store);
        await viewModel.InitializeAsync();

        viewModel.RequestClearCommand.Execute(null);
        Assert.True(viewModel.ShowClearConfirmation);

        await viewModel.ClearHistoryCommand.ExecuteAsync(null);

        Assert.Empty(store.Samples);
        Assert.Empty(viewModel.Rows);
        Assert.Empty(viewModel.Points);
        Assert.False(viewModel.HasHistory);
        Assert.True(viewModel.ShowEmptyState);
        Assert.False(viewModel.ShowClearConfirmation);
    }

    [Fact]
    public async Task SuccessfulAppendClearsAnEarlierTransientStoreError()
    {
        var store = new FlakyAppendHistoryStore();
        var viewModel = new HistoryPageViewModel(store);
        await viewModel.InitializeAsync();
        var firstTimestamp = DateTimeOffset.Parse("2026-07-15T10:00:00+08:00");

        await viewModel.AppendWirelessAsync(
            ConnectedSnapshot(firstTimestamp, -60),
            HealthGrade.Good,
            HealthStatusLabels.Normal);

        Assert.True(viewModel.HasError);
        Assert.Equal("模拟写入失败", viewModel.ErrorMessage);

        await viewModel.AppendWirelessAsync(
            ConnectedSnapshot(firstTimestamp.AddMinutes(1), -59),
            HealthGrade.Good,
            HealthStatusLabels.Normal);

        Assert.False(viewModel.HasError);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Single(viewModel.Rows);
    }

    private static HistorySample Sample(
        DateTimeOffset timestamp,
        HistoryMarker marker,
        int? rssi,
        string? ssid = null,
        int? channel = null,
        int? channelWidth = null,
        double? gatewayAverageMs = null) => new()
        {
            Timestamp = timestamp,
            Marker = marker,
            Snapshot = new WifiSnapshot
            {
                Timestamp = timestamp,
                IsConnected = rssi is not null,
                Ssid = ssid is { } networkName
                    ? Observed<string>.Available(networkName, EvidenceSource.Mock)
                    : Observed<string>.Unavailable(MetricAvailability.Unavailable, EvidenceSource.Mock),
                PrimaryChannel = channel is { } primaryChannel
                    ? Observed<int>.Available(primaryChannel, EvidenceSource.Mock)
                    : Observed<int>.Unavailable(MetricAvailability.Unavailable, EvidenceSource.Mock),
                ChannelWidthMHz = channelWidth is { } width
                    ? Observed<int>.Available(width, EvidenceSource.Mock)
                    : Observed<int>.Unavailable(MetricAvailability.Unavailable, EvidenceSource.Mock),
                RssiDbm = rssi is { } value
                    ? Observed<int>.Available(value, EvidenceSource.Mock)
                    : Observed<int>.Unavailable(MetricAvailability.Unavailable, EvidenceSource.Mock)
            },
            GatewayAverageMs = gatewayAverageMs,
            OverallGrade = rssi is null ? HealthGrade.Unavailable : HealthStandards.Rssi(rssi).Grade,
            OverallStatusLabel = HealthStandards.Rssi(rssi).StatusLabel,
            GradeScope = HistoryGradeScope.Wireless
        };

    private static WifiSnapshot ConnectedSnapshot(DateTimeOffset timestamp, int rssi) => new()
    {
        Timestamp = timestamp,
        IsConnected = true,
        Ssid = Observed<string>.Available("Demo_5G", EvidenceSource.Mock),
        RssiDbm = Observed<int>.Available(rssi, EvidenceSource.Mock),
    };

    private static void AssertAssessment(MetricAssessment expected, MetricCardViewModel actual)
    {
        Assert.Equal(expected.StatusLabel, actual.Status);
        Assert.Equal(expected.Interpretation, actual.Explanation);
        Assert.Equal(expected.Standard, actual.Standard);
    }

    private sealed class MemoryHistoryStore(IReadOnlyList<HistorySample> samples) : IHistoryStore
    {
        private readonly List<HistorySample> _samples = [.. samples];

        public string FilePath => "memory://history";
        public IReadOnlyList<HistorySample> Samples => _samples;
        public int SaveCount { get; private set; }

        public Task<IReadOnlyList<HistorySample>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<HistorySample>>(_samples);

        public Task AppendAsync(HistorySample sample, CancellationToken cancellationToken = default)
        {
            _samples.Add(sample);
            return Task.CompletedTask;
        }

        public Task SaveAsync(
            IEnumerable<HistorySample> samplesToSave,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            _samples.Clear();
            _samples.AddRange(samplesToSave);
            return Task.CompletedTask;
        }
    }

    private sealed class FlakyAppendHistoryStore : IHistoryStore
    {
        private readonly List<HistorySample> _samples = [];
        private bool _failNextAppend = true;

        public string FilePath => "memory://flaky-history";

        public Task<IReadOnlyList<HistorySample>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<HistorySample>>(_samples);

        public Task AppendAsync(HistorySample sample, CancellationToken cancellationToken = default)
        {
            if (_failNextAppend)
            {
                _failNextAppend = false;
                throw new HistoryStoreException("模拟写入失败");
            }

            _samples.Add(sample);
            return Task.CompletedTask;
        }

        public Task SaveAsync(
            IEnumerable<HistorySample> samples,
            CancellationToken cancellationToken = default)
        {
            _samples.Clear();
            _samples.AddRange(samples);
            return Task.CompletedTask;
        }
    }
}
