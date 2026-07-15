namespace WiFiHealthConsole.Core;

public enum ChannelSuggestionCategory
{
    Band,
    Bandwidth,
    Channel,
}

public static class ChannelSuggestionCategoryExtensions
{
    public static string DisplayName(this ChannelSuggestionCategory category) => category switch
    {
        ChannelSuggestionCategory.Band => "频段建议",
        ChannelSuggestionCategory.Bandwidth => "频宽建议",
        ChannelSuggestionCategory.Channel => "信道建议",
        _ => "建议",
    };
}

public sealed record ChannelSuggestion
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public ChannelSuggestionCategory Category { get; init; }

    public HealthGrade Grade { get; init; }

    public string StatusLabel { get; init; }

    public string Title { get; init; }

    public string Detail { get; init; }

    public ChannelSuggestion(
        ChannelSuggestionCategory category,
        HealthGrade grade,
        string statusLabel,
        string title,
        string detail)
    {
        if (!HealthStandards.IsStatusLabelCompatible(statusLabel, grade))
        {
            throw new ArgumentException("Wi-Fi 建议状态文字与健康等级不一致。", nameof(statusLabel));
        }

        Category = category;
        Grade = grade;
        StatusLabel = statusLabel;
        Title = title;
        Detail = detail;
    }
}

/// <summary>
/// Approximate occupied channel-number range used by the radar. When WidthKnown is false,
/// EffectiveWidthMHz is a minimum 20 MHz visualization footprint, not a claimed measurement.
/// </summary>
public sealed record ChannelFootprint(
    double LowerBound,
    double UpperBound,
    int EffectiveWidthMHz,
    bool WidthKnown);

public sealed record NetworkOverlap(
    NearbyNetwork Network,
    double OverlapRatio,
    double StrengthWeight,
    double Score,
    bool WidthKnown);

public sealed record ChannelCandidateScore(
    int PrimaryChannel,
    double Score,
    int OverlappingNetworkCount,
    bool HasUnknownWidthEvidence);

public static class ChannelAnalysis
{
    private static readonly IReadOnlyDictionary<WiFiBand, int[]> CandidateChannels =
        new Dictionary<WiFiBand, int[]>
        {
            [WiFiBand.Band2_4GHz] = [1, 6, 11],
            [WiFiBand.Band5GHz] = [36, 52, 100, 149],
            [WiFiBand.Band6GHz] = [5, 37, 69, 101, 133, 165, 197, 229],
        };

    public static ChannelFootprint EstimatedRange(int channel, int? widthMHz, WiFiBand band)
    {
        var widthKnown = widthMHz is > 0;
        var effectiveWidth = widthKnown ? Math.Max(20, widthMHz!.Value) : 20;
        var halfSpan = Math.Max(2d, effectiveWidth / 10d);
        var (domainLower, domainUpper) = Domain(band);
        var lower = Math.Max(domainLower, channel - halfSpan);
        var upper = Math.Min(domainUpper, channel + halfSpan);
        return new ChannelFootprint(lower, upper, effectiveWidth, widthKnown);
    }

    public static (double LowerBound, double UpperBound) Domain(WiFiBand band) => band switch
    {
        WiFiBand.Band2_4GHz => (1, 14),
        WiFiBand.Band5GHz => (32, 177),
        WiFiBand.Band6GHz => (1, 233),
        _ => (1, 233),
    };

    public static IReadOnlyList<NetworkOverlap> AnalyzeOverlaps(
        WifiSnapshot current,
        IEnumerable<NearbyNetwork> networks,
        int? currentWidthOverrideMHz = null)
    {
        var band = current.BandValue;
        var channel = current.PrimaryChannelValue;
        if (band == WiFiBand.Unknown || channel is null)
        {
            return [];
        }

        var currentWidth = currentWidthOverrideMHz ?? current.ChannelWidthValue;
        var currentRange = EstimatedRange(channel.Value, currentWidth, band);
        var currentBssid = current.Bssid.TryGetValue(out var bssid) ? bssid : null;

        return networks
            .Where(network => network.Band == band)
            .Where(network => !SameBssid(network.Bssid, currentBssid))
            .Select(network => CreateOverlap(currentRange, network))
            .Where(overlap => overlap is not null)
            .Cast<NetworkOverlap>()
            .OrderByDescending(overlap => overlap.Score)
            .ThenBy(overlap => overlap.Network.PrimaryChannel)
            .ToArray();
    }

    public static int OverlapCount(WifiSnapshot current, IEnumerable<NearbyNetwork> networks) =>
        AnalyzeOverlaps(current, networks).Count;

    public static double OverlapScore(WifiSnapshot current, IEnumerable<NearbyNetwork> networks) =>
        AnalyzeOverlaps(current, networks).Sum(overlap => overlap.Score);

    public static int RecommendedWidthMHz(WifiSnapshot current, IEnumerable<NearbyNetwork> networks)
    {
        switch (current.BandValue)
        {
            case WiFiBand.Band2_4GHz:
                return 20;
            case WiFiBand.Band5GHz:
            case WiFiBand.Band6GHz:
                var strongOverlapsAt80 = AnalyzeOverlaps(current, networks, currentWidthOverrideMHz: 80)
                    .Count(overlap => overlap.Network.RssiDbm >= -75);
                return strongOverlapsAt80 >= 4 ? 40 : 80;
            default:
                return current.ChannelWidthValue ?? 20;
        }
    }

    public static IReadOnlyList<ChannelCandidateScore> ScoreCandidateChannels(
        WifiSnapshot current,
        IEnumerable<NearbyNetwork> networks)
    {
        var band = current.BandValue;
        if (!CandidateChannels.TryGetValue(band, out var candidates))
        {
            return [];
        }

        var width = RecommendedWidthMHz(current, networks);
        var currentBssid = current.Bssid.TryGetValue(out var bssid) ? bssid : null;
        var sameBand = networks
            .Where(network => network.Band == band)
            .Where(network => !SameBssid(network.Bssid, currentBssid))
            .ToArray();

        return candidates
            .Select(candidate =>
            {
                var candidateRange = EstimatedRange(candidate, width, band);
                var overlaps = sameBand
                    .Select(network => CreateOverlap(candidateRange, network))
                    .Where(overlap => overlap is not null)
                    .Cast<NetworkOverlap>()
                    .ToArray();
                return new ChannelCandidateScore(
                    candidate,
                    overlaps.Sum(overlap => overlap.Score),
                    overlaps.Length,
                    overlaps.Any(overlap => !overlap.WidthKnown));
            })
            .OrderBy(candidate => candidate.Score)
            .ThenBy(candidate => Array.IndexOf(candidates, candidate.PrimaryChannel))
            .ToArray();
    }

    public static IReadOnlyList<ChannelSuggestion> Suggestions(
        WifiSnapshot current,
        IEnumerable<NearbyNetwork> networks)
    {
        var networkList = networks.ToArray();
        if (!HasConnectionEvidence(current))
        {
            return
            [
                UnavailableSuggestion(ChannelSuggestionCategory.Band, "等待频段数据"),
                UnavailableSuggestion(ChannelSuggestionCategory.Bandwidth, "等待频宽数据"),
                UnavailableSuggestion(ChannelSuggestionCategory.Channel, "等待信道数据"),
            ];
        }

        var result = new List<ChannelSuggestion>
        {
            BandSuggestion(current, networkList),
            BandwidthSuggestion(current, networkList),
        };

        if (networkList.Length == 0)
        {
            result.Add(new ChannelSuggestion(
                ChannelSuggestionCategory.Channel,
                HealthGrade.Unavailable,
                HealthStatusLabels.Unavailable,
                "尚无信道扫描证据",
                "完成附近网络扫描后，再评估当前信道重叠与候选主信道。"));
            return result;
        }

        result.Add(ChannelOverlapSuggestion(current, networkList));
        var candidate = CandidateChannelSuggestion(current, networkList);
        if (candidate is not null)
        {
            result.Add(candidate);
        }

        return result;
    }

    private static ChannelSuggestion BandSuggestion(WifiSnapshot current, NearbyNetwork[] networks)
    {
        if (!current.Ssid.TryGetValue(out var ssid) || string.IsNullOrWhiteSpace(ssid))
        {
            return new ChannelSuggestion(
                ChannelSuggestionCategory.Band,
                HealthGrade.Unavailable,
                HealthStatusLabels.Reference,
                $"当前频段：{current.BandValue.DisplayName()}",
                "SSID 未授权或未取得，无法确认同一 Wi-Fi 是否还提供 2.4/5/6 GHz；频段建议暂以当前连接为参考。");
        }

        var sameSsid = networks
            .Where(network => string.Equals(network.Ssid, ssid, StringComparison.Ordinal))
            .Where(network => network.Band != WiFiBand.Unknown)
            .ToArray();

        NearbyNetwork? Strongest(WiFiBand band) => sameSsid
            .Where(network => network.Band == band)
            .OrderByDescending(network => network.RssiDbm)
            .FirstOrDefault();

        var currentRssi = current.RssiValue ?? -100;
        switch (current.BandValue)
        {
            case WiFiBand.Band2_4GHz:
                {
                    var band6 = Strongest(WiFiBand.Band6GHz);
                    if (band6?.RssiDbm >= -60)
                    {
                        return SwitchBandSuggestion(
                            WiFiBand.Band6GHz,
                            $"扫描到同 SSID 的 6 GHz 信号 {band6.RssiDbm} dBm，近距离下频谱通常更干净、速度上限更高；离开路由器后衰减也更快。");
                    }

                    var band5 = Strongest(WiFiBand.Band5GHz);
                    if (band5?.RssiDbm >= -67)
                    {
                        return SwitchBandSuggestion(
                            WiFiBand.Band5GHz,
                            $"扫描到同 SSID 的 5 GHz 信号 {band5.RssiDbm} dBm，处于正常范围；日常高速连接优先 5 GHz，远距离再回到 2.4 GHz。");
                    }

                    return new ChannelSuggestion(
                        ChannelSuggestionCategory.Band,
                        HealthGrade.Good,
                        HealthStatusLabels.Normal,
                        "保持频段：2.4 GHz",
                        "未发现信号足够好的同 SSID 5/6 GHz；当前位置继续使用 2.4 GHz 更有利于覆盖稳定。");
                }

            case WiFiBand.Band5GHz:
                {
                    var band2 = Strongest(WiFiBand.Band2_4GHz);
                    if (currentRssi < -67 && band2 is not null &&
                        (band2.RssiDbm >= -67 || band2.RssiDbm >= currentRssi + 5))
                    {
                        return new ChannelSuggestion(
                            ChannelSuggestionCategory.Band,
                            HealthGrade.Warning,
                            HealthStatusLabels.Warning,
                            "建议频段：2.4 GHz",
                            $"当前 5 GHz 为 {currentRssi} dBm，偏弱；同 SSID 的 2.4 GHz 为 {band2.RssiDbm} dBm。远距离优先稳定性时可切换，靠近节点后再用 5 GHz。");
                    }

                    if (currentRssi < -67)
                    {
                        return new ChannelSuggestion(
                            ChannelSuggestionCategory.Band,
                            HealthGrade.Warning,
                            HealthStatusLabels.Warning,
                            "暂留 5 GHz，先改善覆盖",
                            $"当前 5 GHz 为 {currentRssi} dBm，已经偏弱，且未发现更合适的同 SSID 频段；先靠近节点或改善回程。");
                    }

                    var band6 = Strongest(WiFiBand.Band6GHz);
                    if (band6?.RssiDbm >= -60)
                    {
                        return SwitchBandSuggestion(
                            WiFiBand.Band6GHz,
                            $"扫描到同 SSID 的 6 GHz 信号 {band6.RssiDbm} dBm；近距离追求更干净频谱时可优先 6 GHz，覆盖变化大时继续使用 5 GHz。");
                    }

                    return new ChannelSuggestion(
                        ChannelSuggestionCategory.Band,
                        HealthGrade.Good,
                        HealthStatusLabels.Normal,
                        "保持频段：5 GHz",
                        "当前信号处于正常范围，5 GHz 通常是速度、兼容性与覆盖之间最稳妥的选择。");
                }

            case WiFiBand.Band6GHz:
                {
                    var band5 = Strongest(WiFiBand.Band5GHz);
                    if (currentRssi < -67 && band5 is not null &&
                        (band5.RssiDbm >= -67 || band5.RssiDbm >= currentRssi + 3))
                    {
                        return new ChannelSuggestion(
                            ChannelSuggestionCategory.Band,
                            HealthGrade.Warning,
                            HealthStatusLabels.Warning,
                            "建议频段：5 GHz",
                            $"当前 6 GHz 为 {currentRssi} dBm，偏弱；同 SSID 的 5 GHz 为 {band5.RssiDbm} dBm，通常能以较小速度损失换取更稳定覆盖。");
                    }

                    var band2 = Strongest(WiFiBand.Band2_4GHz);
                    if (currentRssi < -75 && band2 is not null)
                    {
                        return new ChannelSuggestion(
                            ChannelSuggestionCategory.Band,
                            HealthGrade.Warning,
                            HealthStatusLabels.Warning,
                            "建议频段：2.4 GHz",
                            $"当前 6 GHz 信号很弱；同 SSID 的 2.4 GHz 为 {band2.RssiDbm} dBm。远距离场景优先使用覆盖更强的频段。");
                    }

                    var weak = currentRssi < -67;
                    return new ChannelSuggestion(
                        ChannelSuggestionCategory.Band,
                        weak ? HealthGrade.Warning : HealthGrade.Good,
                        weak ? HealthStatusLabels.Warning : HealthStatusLabels.Normal,
                        weak ? "暂留 6 GHz，先改善覆盖" : "保持频段：6 GHz",
                        weak
                            ? "当前 6 GHz 信号偏弱，且未发现更合适的同 SSID 频段；先靠近节点再回测。"
                            : "当前 6 GHz 信号处于正常范围，适合近距离高速连接和较干净频谱。");
                }

            default:
                return UnavailableSuggestion(ChannelSuggestionCategory.Band, "未识别当前频段");
        }
    }

    private static ChannelSuggestion BandwidthSuggestion(WifiSnapshot current, NearbyNetwork[] networks)
    {
        if (current.BandValue == WiFiBand.Unknown)
        {
            return UnavailableSuggestion(ChannelSuggestionCategory.Bandwidth, "未识别当前频宽环境");
        }

        var target = RecommendedWidthMHz(current, networks);
        var currentWidth = current.ChannelWidthValue;
        var strongOverlaps = AnalyzeOverlaps(current, networks, target)
            .Count(overlap => overlap.Network.RssiDbm >= -75);

        string detail;
        switch (current.BandValue)
        {
            case WiFiBand.Band2_4GHz:
                detail = "2.4 GHz 信道资源有限，20 MHz 可减少相邻信道重叠；40 MHz 通常只增加竞争范围。";
                break;
            case WiFiBand.Band5GHz:
            case WiFiBand.Band6GHz:
                if (target == 40)
                {
                    detail = $"按 {target} MHz 估算范围仍可见 {strongOverlaps} 个较强重叠网络；高密度环境优先 40 MHz 的稳定性，再用实测吞吐确认。";
                }
                else
                {
                    var clean160 = !AnalyzeOverlaps(current, networks, 160)
                        .Any(overlap => overlap.Network.RssiDbm >= -75);
                    detail = "80 MHz 通常兼顾速度与抗拥塞。" +
                             (clean160
                                 ? "160 MHz 范围暂未见较强重叠，但仍应在路由器 CCA ≤ 50% 时才考虑。"
                                 : "160 MHz 会覆盖更多邻居，不建议仅为峰值速率开启。");
                }

                break;
            default:
                detail = "未取得足够数据。";
                break;
        }

        if (currentWidth is null)
        {
            return new ChannelSuggestion(
                ChannelSuggestionCategory.Bandwidth,
                HealthGrade.Unavailable,
                HealthStatusLabels.Reference,
                $"建议频宽：{target} MHz",
                $"当前频宽未检测，建议基于可见网络的最低范围分析，不能把它当作已测频宽。{detail}");
        }

        if (currentWidth == target)
        {
            return new ChannelSuggestion(
                ChannelSuggestionCategory.Bandwidth,
                HealthGrade.Good,
                HealthStatusLabels.Normal,
                $"保持频宽：{target} MHz",
                detail);
        }

        var narrowing = currentWidth > target;
        return new ChannelSuggestion(
            ChannelSuggestionCategory.Bandwidth,
            narrowing ? HealthGrade.Warning : HealthGrade.Unavailable,
            narrowing ? HealthStatusLabels.Warning : HealthStatusLabels.Reference,
            $"建议频宽：{target} MHz",
            $"当前为 {currentWidth} MHz。{detail}");
    }

    private static ChannelSuggestion ChannelOverlapSuggestion(WifiSnapshot current, NearbyNetwork[] networks)
    {
        var overlaps = AnalyzeOverlaps(current, networks);
        var unknownWidth = current.ChannelWidthValue is null || overlaps.Any(overlap => !overlap.WidthKnown);
        if (overlaps.Count == 0)
        {
            if (unknownWidth)
            {
                return new ChannelSuggestion(
                    ChannelSuggestionCategory.Channel,
                    HealthGrade.Unavailable,
                    HealthStatusLabels.Reference,
                    "最小可见范围未见重叠",
                    "当前或附近网络频宽未知，只能按最低 20 MHz 足迹参考；更宽的实际频宽仍可能发生重叠。");
            }

            return new ChannelSuggestion(
                ChannelSuggestionCategory.Channel,
                HealthGrade.Good,
                HealthStatusLabels.Normal,
                "当前信道未见明显重叠",
                "基于主信道与当前频宽的近似占用范围，未发现其他可见网络交叠。");
        }

        return new ChannelSuggestion(
            ChannelSuggestionCategory.Channel,
            HealthGrade.Warning,
            HealthStatusLabels.Warning,
            $"当前范围约有 {overlaps.Count} 个重叠网络",
            $"加权重叠评分 {overlaps.Sum(overlap => overlap.Score):0.0}。重叠会增加竞争风险，但扫描图不能单独判为严重；{(unknownWidth ? "部分频宽未知，结果为最低范围参考；" : string.Empty)}需结合路由器 CCA、重传和实际延迟确认。");
    }

    private static ChannelSuggestion? CandidateChannelSuggestion(WifiSnapshot current, NearbyNetwork[] networks)
    {
        var ranked = ScoreCandidateChannels(current, networks);
        var best = ranked.FirstOrDefault();
        if (best is null)
        {
            return null;
        }

        var width = RecommendedWidthMHz(current, networks);
        return new ChannelSuggestion(
            ChannelSuggestionCategory.Channel,
            HealthGrade.Unavailable,
            HealthStatusLabels.Reference,
            $"候选主信道：{best.PrimaryChannel}",
            $"按建议频宽 {width} MHz 对可见邻居的重叠比例与信号强度加权后，这是低占用候选；{(best.HasUnknownWidthEvidence ? "部分邻居频宽未知；" : string.Empty)}DFS、隐藏网络和实际 CCA 仍以路由器数据为准。");
    }

    private static NetworkOverlap? CreateOverlap(ChannelFootprint subject, NearbyNetwork network)
    {
        var neighborWidth = network.ChannelWidthMHz is > 0 ? network.ChannelWidthMHz : null;
        var neighborRange = EstimatedRange(network.PrimaryChannel, neighborWidth, network.Band);
        if (!RangesOverlap(subject, neighborRange))
        {
            return null;
        }

        var intersection = Math.Max(
            0,
            Math.Min(subject.UpperBound, neighborRange.UpperBound) -
            Math.Max(subject.LowerBound, neighborRange.LowerBound));
        var smallerSpan = Math.Max(
            0.001,
            Math.Min(
                subject.UpperBound - subject.LowerBound,
                neighborRange.UpperBound - neighborRange.LowerBound));
        // Closed ranges touching at the spectral edge still carry a small adjacency score,
        // matching the conservative radar behavior used by the macOS edition.
        var overlapRatio = intersection == 0 ? 0.05 : Math.Clamp(intersection / smallerSpan, 0, 1);
        var strengthWeight = Math.Max(1, 100 + network.RssiDbm);
        var widthKnown = subject.WidthKnown && neighborRange.WidthKnown && !network.WidthEstimated;
        return new NetworkOverlap(
            network,
            overlapRatio,
            strengthWeight,
            overlapRatio * strengthWeight,
            widthKnown);
    }

    private static bool RangesOverlap(ChannelFootprint left, ChannelFootprint right) =>
        left.LowerBound <= right.UpperBound && right.LowerBound <= left.UpperBound;

    private static bool HasConnectionEvidence(WifiSnapshot current) =>
        current.IsConnected ||
        (current.BandValue != WiFiBand.Unknown && current.PrimaryChannelValue is not null && current.RssiValue is not null);

    private static bool SameBssid(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static ChannelSuggestion SwitchBandSuggestion(WiFiBand band, string detail) => new(
        ChannelSuggestionCategory.Band,
        HealthGrade.Unavailable,
        HealthStatusLabels.Reference,
        $"推荐频段：{band.DisplayName()}",
        detail);

    private static ChannelSuggestion UnavailableSuggestion(
        ChannelSuggestionCategory category,
        string title) => new(
        category,
        HealthGrade.Unavailable,
        HealthStatusLabels.Unavailable,
        title,
        "连接 Wi-Fi 并完成附近网络扫描后生成建议。");
}
