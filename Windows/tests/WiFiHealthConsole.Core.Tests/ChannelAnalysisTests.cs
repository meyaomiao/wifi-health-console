using WiFiHealthConsole.Core;

namespace WiFiHealthConsole.Core.Tests;

public sealed class ChannelAnalysisTests
{
    [Fact]
    public void DomainsAndFootprintsCoverTwoPointFourFiveAndSixGHz()
    {
        Assert.Equal((1d, 14d), ChannelAnalysis.Domain(WiFiBand.Band2_4GHz));
        Assert.Equal((32d, 177d), ChannelAnalysis.Domain(WiFiBand.Band5GHz));
        Assert.Equal((1d, 233d), ChannelAnalysis.Domain(WiFiBand.Band6GHz));

        var two = ChannelAnalysis.EstimatedRange(6, 20, WiFiBand.Band2_4GHz);
        var five = ChannelAnalysis.EstimatedRange(149, 80, WiFiBand.Band5GHz);
        var six = ChannelAnalysis.EstimatedRange(229, 160, WiFiBand.Band6GHz);

        Assert.True(two.WidthKnown);
        Assert.Equal(20, two.EffectiveWidthMHz);
        Assert.InRange(five.LowerBound, 32, 177);
        Assert.InRange(five.UpperBound, 32, 177);
        Assert.InRange(six.LowerBound, 1, 233);
        Assert.InRange(six.UpperBound, 1, 233);
    }

    [Fact]
    public void UnknownWidthIsExplicitAndUsesOnlyMinimumVisualizationFootprint()
    {
        var footprint = ChannelAnalysis.EstimatedRange(44, null, WiFiBand.Band5GHz);

        Assert.False(footprint.WidthKnown);
        Assert.Equal(20, footprint.EffectiveWidthMHz);

        var current = Snapshot(WiFiBand.Band5GHz, 44, width: null);
        var suggestion = ChannelAnalysis.Suggestions(current, [Network("far", WiFiBand.Band5GHz, 149, null, -50)])
            .First(item => item.Category == ChannelSuggestionCategory.Channel);

        Assert.Equal(HealthGrade.Unavailable, suggestion.Grade);
        Assert.Equal("参考", suggestion.StatusLabel);
        Assert.Contains("频宽未知", suggestion.Detail);
    }

    [Fact]
    public void OverlapOnlyCountsSameBandAndExcludesCurrentBssid()
    {
        var current = Snapshot(WiFiBand.Band5GHz, 40, 80, bssid: "current");
        var networks = new[]
        {
            Network("current", WiFiBand.Band5GHz, 40, 80, -40),
            Network("near", WiFiBand.Band5GHz, 44, 80, -52),
            Network("far", WiFiBand.Band5GHz, 149, 80, -52),
            Network("other-band", WiFiBand.Band2_4GHz, 6, 20, -52),
        };

        Assert.Equal(1, ChannelAnalysis.OverlapCount(current, networks));
        Assert.True(ChannelAnalysis.OverlapScore(current, networks) > 0);
    }

    [Fact]
    public void StrongerOverlapContributesMoreToScore()
    {
        var current = Snapshot(WiFiBand.Band5GHz, 40, 80);
        var strong = ChannelAnalysis.OverlapScore(current, [Network("strong", WiFiBand.Band5GHz, 44, 80, -45)]);
        var weak = ChannelAnalysis.OverlapScore(current, [Network("weak", WiFiBand.Band5GHz, 44, 80, -85)]);

        Assert.True(strong > weak);
    }

    [Fact]
    public void TwoPointFourFortyMHzRecommendsTwentyAsWarning()
    {
        var current = Snapshot(WiFiBand.Band2_4GHz, 6, 40);

        Assert.Equal(20, ChannelAnalysis.RecommendedWidthMHz(current, []));
        var suggestion = BandwidthSuggestion(current, []);
        Assert.Equal(HealthGrade.Warning, suggestion.Grade);
        Assert.Equal("注意", suggestion.StatusLabel);
        Assert.Equal("建议频宽：20 MHz", suggestion.Title);
    }

    [Fact]
    public void FiveGHzOneHundredSixtyRecommendsEightyInOrdinaryEnvironment()
    {
        var current = Snapshot(WiFiBand.Band5GHz, 40, 160);
        var networks = new[] { Network("distant", WiFiBand.Band5GHz, 149, 80, -55) };

        Assert.Equal(80, ChannelAnalysis.RecommendedWidthMHz(current, networks));
        var suggestion = BandwidthSuggestion(current, networks);
        Assert.Equal(HealthGrade.Warning, suggestion.Grade);
        Assert.Equal("注意", suggestion.StatusLabel);
        Assert.Equal("建议频宽：80 MHz", suggestion.Title);
    }

    [Fact]
    public void FourStrongOverlapsAtEightyRecommendFortyMHz()
    {
        var current = Snapshot(WiFiBand.Band5GHz, 40, 80);
        var networks = new[]
        {
            Network("a", WiFiBand.Band5GHz, 36, 20, -60),
            Network("b", WiFiBand.Band5GHz, 40, 20, -61),
            Network("c", WiFiBand.Band5GHz, 44, 20, -62),
            Network("d", WiFiBand.Band5GHz, 48, 20, -63),
        };

        Assert.Equal(4, ChannelAnalysis.OverlapCount(current, networks));
        Assert.Equal(40, ChannelAnalysis.RecommendedWidthMHz(current, networks));
        Assert.Equal("建议频宽：40 MHz", BandwidthSuggestion(current, networks).Title);
    }

    [Fact]
    public void SameSsidStrongFiveGHzCanBeRecommendedFromTwoPointFour()
    {
        var current = Snapshot(WiFiBand.Band2_4GHz, 6, 20, ssid: "Home");
        var networks = new[]
        {
            Network("home-5", WiFiBand.Band5GHz, 36, 80, -55, ssid: "Home"),
        };

        var suggestion = BandSuggestion(current, networks);
        Assert.Equal("推荐频段：5 GHz", suggestion.Title);
        Assert.Equal(HealthGrade.Unavailable, suggestion.Grade);
        Assert.Equal("参考", suggestion.StatusLabel);
    }

    [Fact]
    public void DifferentSsidNeverTriggersBandSwitch()
    {
        var current = Snapshot(WiFiBand.Band2_4GHz, 6, 20, ssid: "Home");
        var networks = new[]
        {
            Network("guest-5", WiFiBand.Band5GHz, 36, 80, -35, ssid: "Guest"),
            Network("office-6", WiFiBand.Band6GHz, 37, 80, -30, ssid: "Office"),
        };

        var suggestion = BandSuggestion(current, networks);
        Assert.Equal("保持频段：2.4 GHz", suggestion.Title);
        Assert.Equal(HealthGrade.Good, suggestion.Grade);
        Assert.Equal("正常", suggestion.StatusLabel);
    }

    [Fact]
    public void EverySuggestionSetContainsBandBandwidthAndChannel()
    {
        var scenarios = new (WifiSnapshot Current, NearbyNetwork[] Networks)[]
        {
            (WifiSnapshot.Unavailable, []),
            (Snapshot(WiFiBand.Band2_4GHz, 6, 40), []),
            (Snapshot(WiFiBand.Band5GHz, 40, 80), [Network("neighbor", WiFiBand.Band5GHz, 44, 80, -60)]),
            (Snapshot(WiFiBand.Band6GHz, 37, null), [Network("six", WiFiBand.Band6GHz, 69, null, -65)]),
        };

        foreach (var scenario in scenarios)
        {
            var categories = ChannelAnalysis.Suggestions(scenario.Current, scenario.Networks)
                .Select(suggestion => suggestion.Category)
                .ToHashSet();
            Assert.Equal(
                new HashSet<ChannelSuggestionCategory>(Enum.GetValues<ChannelSuggestionCategory>()),
                categories);
        }
    }

    [Fact]
    public void ScanOverlapNeverClaimsCriticalWithoutCcaOrRetransmissionEvidence()
    {
        var current = Snapshot(WiFiBand.Band5GHz, 40, 160);
        var networks = Enumerable.Range(1, 6)
            .Select(index => Network($"n{index}", WiFiBand.Band5GHz, 36 + index * 4, 80, -45 - index))
            .ToArray();

        var suggestions = ChannelAnalysis.Suggestions(current, networks);
        Assert.NotEmpty(suggestions);
        Assert.DoesNotContain(suggestions, suggestion => suggestion.Grade == HealthGrade.Critical);
        Assert.All(suggestions, suggestion =>
            Assert.True(HealthStandards.IsStatusLabelCompatible(suggestion.StatusLabel, suggestion.Grade)));
    }

    [Fact]
    public void CandidateRankingSupportsSixGHzAndUnknownNeighborWidth()
    {
        var current = Snapshot(WiFiBand.Band6GHz, 37, 80);
        var candidates = ChannelAnalysis.ScoreCandidateChannels(
            current,
            [Network("unknown-width", WiFiBand.Band6GHz, 37, null, -50)]);

        Assert.Equal(8, candidates.Count);
        Assert.Contains(candidates, candidate => candidate.PrimaryChannel == 229);
        Assert.Contains(candidates, candidate => candidate.HasUnknownWidthEvidence);
        Assert.Equal(candidates.Min(candidate => candidate.Score), candidates[0].Score);
    }

    private static ChannelSuggestion BandSuggestion(WifiSnapshot current, IEnumerable<NearbyNetwork> networks) =>
        ChannelAnalysis.Suggestions(current, networks)
            .First(suggestion => suggestion.Category == ChannelSuggestionCategory.Band);

    private static ChannelSuggestion BandwidthSuggestion(WifiSnapshot current, IEnumerable<NearbyNetwork> networks) =>
        ChannelAnalysis.Suggestions(current, networks)
            .First(suggestion => suggestion.Category == ChannelSuggestionCategory.Bandwidth);

    private static WifiSnapshot Snapshot(
        WiFiBand band,
        int channel,
        int? width,
        int rssi = -50,
        string ssid = "Home",
        string bssid = "current") => new()
        {
            IsConnected = true,
            Ssid = Observed<string>.Available(ssid, EvidenceSource.Mock),
            Bssid = Observed<string>.Available(bssid, EvidenceSource.Mock),
            Band = Observed<WiFiBand>.Available(band, EvidenceSource.Mock),
            PrimaryChannel = Observed<int>.Available(channel, EvidenceSource.Mock),
            ChannelWidthMHz = width is > 0
            ? Observed<int>.Available(width.Value, EvidenceSource.Mock)
            : Observed<int>.Unavailable(MetricAvailability.Unavailable, EvidenceSource.Mock, "频宽未知"),
            RssiDbm = Observed<int>.Available(rssi, EvidenceSource.Mock),
        };

    private static NearbyNetwork Network(
        string id,
        WiFiBand band,
        int channel,
        int? width,
        int rssi,
        string? ssid = null) => new()
        {
            Id = id,
            Ssid = ssid ?? id,
            Bssid = id,
            Band = band,
            PrimaryChannel = channel,
            ChannelWidthMHz = width,
            WidthEstimated = false,
            RssiDbm = rssi,
            SignalQualityPercent = 70,
        };
}
