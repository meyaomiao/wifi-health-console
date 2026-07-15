using WiFiHealthConsole.App.Services;
using WiFiHealthConsole.App.ViewModels;
using WiFiHealthConsole.Core;

namespace WiFiHealthConsole.App.Tests;

public sealed class OverviewPageViewModelTests
{
    [Fact]
    public async Task GoodRssiWithUnavailableAirMetricsIsPartialInsteadOfGreen()
    {
        var snapshot = ConnectedSnapshot(-58);
        var viewModel = new OverviewPageViewModel(new SnapshotProvider(snapshot));

        await viewModel.InitializeAsync();

        Assert.Equal(HealthGrade.Unavailable, viewModel.CurrentGrade);
        Assert.Equal(HealthStatusLabels.Partial, viewModel.OverallStatus);
        Assert.Equal("RSSI 正常，空口证据不完整", viewModel.Conclusion);
        Assert.Same(UiPalette.Reference, viewModel.OverallForeground);
    }

    [Fact]
    public async Task WarningRssiRemainsWarningWhenOtherAirMetricsAreUnavailable()
    {
        var snapshot = ConnectedSnapshot(-70);
        var viewModel = new OverviewPageViewModel(new SnapshotProvider(snapshot));

        await viewModel.InitializeAsync();

        Assert.Equal(HealthGrade.Warning, viewModel.CurrentGrade);
        Assert.Equal(HealthStatusLabels.Warning, viewModel.OverallStatus);
        Assert.Equal("无线状态需要注意", viewModel.Conclusion);
        Assert.Same(UiPalette.Warning, viewModel.OverallForeground);
    }

    private static WifiSnapshot ConnectedSnapshot(int rssi) => new()
    {
        IsConnected = true,
        InterfaceName = "Wi-Fi",
        Ssid = Observed<string>.Available("Demo_5G", EvidenceSource.Mock),
        Band = Observed<WiFiBand>.Available(WiFiBand.Band5GHz, EvidenceSource.Mock),
        PrimaryChannel = Observed<int>.Available(44, EvidenceSource.Mock),
        ChannelWidthMHz = Observed<int>.Available(80, EvidenceSource.Mock),
        RssiDbm = Observed<int>.Available(rssi, EvidenceSource.Mock),
    };

    private sealed class SnapshotProvider(WifiSnapshot snapshot) : IWifiTelemetryProvider
    {
        public bool IsSupported => true;
        public string ProviderName => "test";

        public Task<WifiSnapshot> GetCurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);

        public Task<IReadOnlyList<NearbyNetwork>> ScanAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NearbyNetwork>>([]);

        public Task<bool> OpenLocationPrivacySettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
