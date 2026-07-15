using WiFiHealthConsole.App.Services;
using WiFiHealthConsole.App.ViewModels;
using WiFiHealthConsole.Core;

namespace WiFiHealthConsole.App.Tests;

public sealed class ChannelRadarPageViewModelTests
{
    [Fact]
    public async Task InitialScanShowsAllBandsInOneOverview()
    {
        var current = new WifiSnapshot
        {
            IsConnected = true,
            Ssid = Observed<string>.Available("Demo_5G", EvidenceSource.Mock),
            Bssid = Observed<string>.Available("aa:bb:cc:dd:ee:44", EvidenceSource.Mock),
            Band = Observed<WiFiBand>.Available(WiFiBand.Band5GHz, EvidenceSource.Mock),
            PrimaryChannel = Observed<int>.Available(44, EvidenceSource.Mock),
            ChannelWidthMHz = Observed<int>.Available(80, EvidenceSource.Mock),
            RssiDbm = Observed<int>.Available(-58, EvidenceSource.Mock),
        };
        NearbyNetwork[] networks =
        [
            Network("home-24", "Home-24", "aa:bb:cc:dd:ee:24", WiFiBand.Band2_4GHz, 6, 20, -66),
            Network("current", "Demo_5G", "aa:bb:cc:dd:ee:44", WiFiBand.Band5GHz, 44, 80, -58),
            Network("home-6", "Home-6", "aa:bb:cc:dd:ee:66", WiFiBand.Band6GHz, 97, 80, -70),
        ];
        var viewModel = new ChannelRadarPageViewModel(new RadarProvider(current, networks));

        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsOverviewSelected);
        Assert.Equal("总览", viewModel.SelectedBand);
        Assert.Equal(520, viewModel.ChartHeight);
        Assert.Equal("3", viewModel.NetworkCount);
        Assert.Equal("3", viewModel.ActiveChannelCount);
        Assert.Equal(3, viewModel.VisibleNetworks.Count);
        Assert.Contains("2.4 GHz 1 个", viewModel.ChartAccessibilitySummary);
        Assert.Contains("5 GHz 1 个", viewModel.ChartAccessibilitySummary);
        Assert.Contains("6 GHz 1 个", viewModel.ChartAccessibilitySummary);
        Assert.Contains("Demo_5G", viewModel.ChartAccessibilitySummary);
    }

    [Fact]
    public async Task BandDetailFiltersLegendWithoutDiscardingOverviewData()
    {
        var current = new WifiSnapshot
        {
            IsConnected = true,
            Bssid = Observed<string>.Available("current", EvidenceSource.Mock),
            Band = Observed<WiFiBand>.Available(WiFiBand.Band5GHz, EvidenceSource.Mock),
        };
        NearbyNetwork[] networks =
        [
            Network("24", "Home-24", "24", WiFiBand.Band2_4GHz, 1, 20, -65),
            Network("5", "Home-5", "current", WiFiBand.Band5GHz, 149, 80, -60),
        ];
        var viewModel = new ChannelRadarPageViewModel(new RadarProvider(current, networks));
        await viewModel.InitializeAsync();

        viewModel.SelectBandCommand.Execute("5 GHz");

        Assert.False(viewModel.IsOverviewSelected);
        Assert.True(viewModel.IsBand5Selected);
        Assert.Equal(360, viewModel.ChartHeight);
        Assert.Single(viewModel.VisibleNetworks);
        Assert.Equal("Home-5", viewModel.VisibleNetworks[0].Ssid);
        Assert.Equal(2, viewModel.Networks.Count);
    }

    private static NearbyNetwork Network(
        string id,
        string ssid,
        string bssid,
        WiFiBand band,
        int channel,
        int width,
        int rssi) => new()
        {
            Id = id,
            Ssid = ssid,
            Bssid = bssid,
            Band = band,
            PrimaryChannel = channel,
            ChannelWidthMHz = width,
            RssiDbm = rssi,
        };

    private sealed class RadarProvider(
        WifiSnapshot current,
        IReadOnlyList<NearbyNetwork> networks) : IWifiTelemetryProvider
    {
        public bool IsSupported => true;
        public string ProviderName => "test";

        public Task<WifiSnapshot> GetCurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(current);

        public Task<IReadOnlyList<NearbyNetwork>> ScanAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(networks);

        public Task<bool> OpenLocationPrivacySettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
