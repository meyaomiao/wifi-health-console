import Foundation

enum ChannelSuggestionCategory: String, CaseIterable, Identifiable {
    case band = "频段建议"
    case bandwidth = "频宽建议"
    case channel = "信道建议"

    var id: String { rawValue }

    var systemImage: String {
        switch self {
        case .band: "antenna.radiowaves.left.and.right"
        case .bandwidth: "arrow.left.and.right"
        case .channel: "number"
        }
    }
}

struct ChannelSuggestion: Identifiable, Equatable {
    let id = UUID()
    var category: ChannelSuggestionCategory
    var grade: HealthGrade
    var statusLabel: String
    var title: String
    var detail: String

    var badgeSystemImage: String {
        switch statusLabel {
        case "参考": "info.circle"
        case "未检测": "minus.circle"
        default: grade.systemImage
        }
    }

    init(
        category: ChannelSuggestionCategory,
        grade: HealthGrade,
        statusLabel: String,
        title: String,
        detail: String
    ) {
        precondition(
            HealthStandards.isStatusLabelCompatible(statusLabel, with: grade),
            "Wi-Fi 建议状态文字与健康等级不一致"
        )
        self.category = category
        self.grade = grade
        self.statusLabel = statusLabel
        self.title = title
        self.detail = detail
    }
}

enum ChannelAnalysis {
    static func estimatedRange(channel: Int, widthMHz: Int, band: WiFiBand) -> ClosedRange<Double> {
        let halfSpan = max(2, Double(max(widthMHz, 20)) / 10)
        let domain = domain(for: band)
        let lower = max(domain.lowerBound, Double(channel) - halfSpan)
        let upper = min(domain.upperBound, Double(channel) + halfSpan)
        return lower...upper
    }

    static func domain(for band: WiFiBand) -> ClosedRange<Double> {
        switch band {
        case .band2: 1...14
        case .band5: 32...177
        case .band6: 1...233
        case .unknown: 1...233
        }
    }

    static func overlapCount(current: WiFiSnapshot, networks: [NearbyNetwork]) -> Int {
        guard let width = current.channelWidthMHz else { return 0 }
        return overlappingNetworks(current: current, networks: networks, widthMHz: width).count
    }

    static func recommendedWidthMHz(current: WiFiSnapshot, networks: [NearbyNetwork]) -> Int {
        switch current.band {
        case .band2:
            return 20
        case .band5, .band6:
            let strongOverlapsAt80 = overlappingNetworks(current: current, networks: networks, widthMHz: 80)
                .filter { $0.rssi >= -75 }
                .count
            return strongOverlapsAt80 >= 4 ? 40 : 80
        case .unknown:
            return current.channelWidthMHz ?? 20
        }
    }

    static func suggestions(current: WiFiSnapshot, networks: [NearbyNetwork]) -> [ChannelSuggestion] {
        guard current.isConnected else {
            return [
                unavailableSuggestion(.band, title: "等待频段数据"),
                unavailableSuggestion(.bandwidth, title: "等待频宽数据"),
                unavailableSuggestion(.channel, title: "等待信道数据")
            ]
        }

        var result = [
            bandSuggestion(current: current, networks: networks),
            bandwidthSuggestion(current: current, networks: networks)
        ]

        guard !networks.isEmpty else {
            result.append(ChannelSuggestion(
                category: .channel,
                grade: .unavailable,
                statusLabel: "未检测",
                title: "尚无信道扫描证据",
                detail: "完成附近网络扫描后，再评估当前信道重叠与候选主信道。"
            ))
            return result
        }

        result.append(channelOverlapSuggestion(current: current, networks: networks))
        if let candidate = candidateChannelSuggestion(current: current, networks: networks) {
            result.append(candidate)
        }
        return result
    }

    private static func bandSuggestion(
        current: WiFiSnapshot,
        networks: [NearbyNetwork]
    ) -> ChannelSuggestion {
        guard let ssid = current.ssid, !ssid.isEmpty else {
            return ChannelSuggestion(
                category: .band,
                grade: .unavailable,
                statusLabel: "参考",
                title: "当前频段：\(current.band.rawValue)",
                detail: "SSID 未授权，无法确认同一 Wi-Fi 是否还提供 2.4/5/6 GHz；频段建议暂以当前连接为参考。"
            )
        }

        let sameSSID = networks.filter { $0.ssid == ssid && $0.band != .unknown }
        let strongest: (WiFiBand) -> NearbyNetwork? = { band in
            sameSSID.filter { $0.band == band }.max(by: { $0.rssi < $1.rssi })
        }
        let currentRSSI = current.rssi ?? -100

        switch current.band {
        case .band2:
            if let candidate = strongest(.band6), candidate.rssi >= -60 {
                return switchBandSuggestion(
                    to: .band6,
                    detail: "扫描到同 SSID 的 6 GHz 信号 \(candidate.rssi) dBm，近距离下频谱通常更干净、速度上限更高；离开路由器后衰减也更快。"
                )
            }
            if let candidate = strongest(.band5), candidate.rssi >= -67 {
                return switchBandSuggestion(
                    to: .band5,
                    detail: "扫描到同 SSID 的 5 GHz 信号 \(candidate.rssi) dBm，处于正常范围；日常高速连接优先 5 GHz，远距离再回到 2.4 GHz。"
                )
            }
            return ChannelSuggestion(
                category: .band,
                grade: .good,
                statusLabel: "正常",
                title: "保持频段：2.4 GHz",
                detail: "未发现信号足够好的同 SSID 5/6 GHz；当前位置继续使用 2.4 GHz 更有利于覆盖稳定。"
            )

        case .band5:
            if currentRSSI < -67,
               let candidate = strongest(.band2),
               candidate.rssi >= -67 || candidate.rssi >= currentRSSI + 5 {
                return ChannelSuggestion(
                    category: .band,
                    grade: .warning,
                    statusLabel: "注意",
                    title: "建议频段：2.4 GHz",
                    detail: "当前 5 GHz 为 \(currentRSSI) dBm，偏弱；同 SSID 的 2.4 GHz 为 \(candidate.rssi) dBm。远距离优先稳定性时可切换，靠近节点后再用 5 GHz。"
                )
            }
            if currentRSSI < -67 {
                return ChannelSuggestion(
                    category: .band,
                    grade: .warning,
                    statusLabel: "注意",
                    title: "暂留 5 GHz，先改善覆盖",
                    detail: "当前 5 GHz 为 \(currentRSSI) dBm，已经偏弱，且未发现更合适的同 SSID 频段；先靠近节点或改善回程。"
                )
            }
            if let candidate = strongest(.band6), candidate.rssi >= -60 {
                return switchBandSuggestion(
                    to: .band6,
                    detail: "扫描到同 SSID 的 6 GHz 信号 \(candidate.rssi) dBm；近距离追求更干净频谱时可优先 6 GHz，覆盖变化大时继续使用 5 GHz。"
                )
            }
            return ChannelSuggestion(
                category: .band,
                grade: .good,
                statusLabel: "正常",
                title: "保持频段：5 GHz",
                detail: "当前信号处于正常范围，5 GHz 通常是速度、兼容性与覆盖之间最稳妥的选择。"
            )

        case .band6:
            if currentRSSI < -67,
               let candidate = strongest(.band5),
               candidate.rssi >= -67 || candidate.rssi >= currentRSSI + 3 {
                return ChannelSuggestion(
                    category: .band,
                    grade: .warning,
                    statusLabel: "注意",
                    title: "建议频段：5 GHz",
                    detail: "当前 6 GHz 为 \(currentRSSI) dBm，偏弱；同 SSID 的 5 GHz 为 \(candidate.rssi) dBm，通常能以较小速度损失换取更稳定覆盖。"
                )
            }
            if currentRSSI < -75, let candidate = strongest(.band2) {
                return ChannelSuggestion(
                    category: .band,
                    grade: .warning,
                    statusLabel: "注意",
                    title: "建议频段：2.4 GHz",
                    detail: "当前 6 GHz 信号很弱；同 SSID 的 2.4 GHz 为 \(candidate.rssi) dBm。远距离场景优先使用覆盖更强的频段。"
                )
            }
            return ChannelSuggestion(
                category: .band,
                grade: currentRSSI < -67 ? .warning : .good,
                statusLabel: currentRSSI < -67 ? "注意" : "正常",
                title: currentRSSI < -67 ? "暂留 6 GHz，先改善覆盖" : "保持频段：6 GHz",
                detail: currentRSSI < -67
                    ? "当前 6 GHz 信号偏弱，且未发现更合适的同 SSID 频段；先靠近节点再回测。"
                    : "当前 6 GHz 信号处于正常范围，适合近距离高速连接和较干净频谱。"
            )

        case .unknown:
            return unavailableSuggestion(.band, title: "未识别当前频段")
        }
    }

    private static func bandwidthSuggestion(
        current: WiFiSnapshot,
        networks: [NearbyNetwork]
    ) -> ChannelSuggestion {
        guard current.band != .unknown else {
            return unavailableSuggestion(.bandwidth, title: "未识别当前频宽环境")
        }

        let target = recommendedWidthMHz(current: current, networks: networks)
        let currentWidth = current.channelWidthMHz
        let strongOverlaps = overlappingNetworks(current: current, networks: networks, widthMHz: target)
            .filter { $0.rssi >= -75 }
            .count

        let detail: String
        switch current.band {
        case .band2:
            detail = "2.4 GHz 信道资源有限，20 MHz 可减少相邻信道重叠；40 MHz 通常只增加竞争范围。"
        case .band5 where target == 40, .band6 where target == 40:
            detail = "按 \(target) MHz 估算范围仍可见 \(strongOverlaps) 个较强重叠网络；高密度环境优先 40 MHz 的稳定性，再用实测吞吐确认。"
        case .band5, .band6:
            let clean160 = overlappingNetworks(current: current, networks: networks, widthMHz: 160)
                .filter { $0.rssi >= -75 }
                .isEmpty
            detail = "80 MHz 通常兼顾速度与抗拥塞。\(clean160 ? "160 MHz 范围暂未见较强重叠，但仍应在路由器 CCA ≤ 50% 时才考虑。" : "160 MHz 会覆盖更多邻居，不建议仅为峰值速率开启。")"
        case .unknown:
            detail = "未取得足够数据。"
        }

        guard let currentWidth, currentWidth > 0 else {
            return ChannelSuggestion(
                category: .bandwidth,
                grade: .unavailable,
                statusLabel: "参考",
                title: "建议频宽：\(target) MHz",
                detail: detail
            )
        }

        if currentWidth == target {
            return ChannelSuggestion(
                category: .bandwidth,
                grade: .good,
                statusLabel: "正常",
                title: "保持频宽：\(target) MHz",
                detail: detail
            )
        }

        let narrowing = currentWidth > target
        return ChannelSuggestion(
            category: .bandwidth,
            grade: narrowing ? .warning : .unavailable,
            statusLabel: narrowing ? "注意" : "参考",
            title: "建议频宽：\(target) MHz",
            detail: "当前为 \(currentWidth) MHz。\(detail)"
        )
    }

    private static func channelOverlapSuggestion(
        current: WiFiSnapshot,
        networks: [NearbyNetwork]
    ) -> ChannelSuggestion {
        let overlaps = overlapCount(current: current, networks: networks)
        if overlaps == 0 {
            return ChannelSuggestion(
                category: .channel,
                grade: .good,
                statusLabel: "正常",
                title: "当前信道未见明显重叠",
                detail: "基于主信道与当前频宽的近似占用范围，未发现其他可见网络交叠。"
            )
        }
        return ChannelSuggestion(
            category: .channel,
            grade: .warning,
            statusLabel: "注意",
            title: "当前范围约有 \(overlaps) 个重叠网络",
            detail: "重叠会增加竞争风险，但扫描图不能单独判为严重；需结合路由器 CCA、重传和实际延迟确认。"
        )
    }

    private static func candidateChannelSuggestion(
        current: WiFiSnapshot,
        networks: [NearbyNetwork]
    ) -> ChannelSuggestion? {
        let candidates: [Int]
        switch current.band {
        case .band2: candidates = [1, 6, 11]
        case .band5: candidates = [36, 52, 100, 149]
        case .band6: candidates = [5, 37, 69, 101, 133, 165, 197, 229]
        case .unknown: candidates = []
        }
        guard !candidates.isEmpty else { return nil }

        let width = recommendedWidthMHz(current: current, networks: networks)
        let ranked = candidates.map { candidate in
            let candidateRange = estimatedRange(channel: candidate, widthMHz: width, band: current.band)
            let load = networks
                .filter {
                    $0.band == current.band &&
                    $0.bssid != current.bssid &&
                    candidateRange.overlaps(estimatedRange(channel: $0.channel, widthMHz: $0.channelWidthMHz, band: $0.band))
                }
                .reduce(0.0) { partial, network in
                    partial + max(1, 100 + Double(network.rssi))
                }
            return (candidate, load)
        }.sorted { $0.1 < $1.1 }

        guard let best = ranked.first else { return nil }
        return ChannelSuggestion(
            category: .channel,
            grade: .unavailable,
            statusLabel: "参考",
            title: "候选主信道：\(best.0)",
            detail: "按建议频宽 \(width) MHz 对可见邻居加权后，这是低占用候选；DFS、隐藏网络和实际 CCA 仍以路由器数据为准。"
        )
    }

    private static func overlappingNetworks(
        current: WiFiSnapshot,
        networks: [NearbyNetwork],
        widthMHz: Int
    ) -> [NearbyNetwork] {
        guard let channel = current.channel else { return [] }
        let currentRange = estimatedRange(channel: channel, widthMHz: widthMHz, band: current.band)
        return networks.filter { network in
            guard network.band == current.band else { return false }
            if let currentBSSID = current.bssid, network.bssid == currentBSSID { return false }
            return currentRange.overlaps(estimatedRange(
                channel: network.channel,
                widthMHz: network.channelWidthMHz,
                band: network.band
            ))
        }
    }

    private static func switchBandSuggestion(
        to band: WiFiBand,
        detail: String
    ) -> ChannelSuggestion {
        ChannelSuggestion(
            category: .band,
            grade: .unavailable,
            statusLabel: "参考",
            title: "推荐频段：\(band.rawValue)",
            detail: detail
        )
    }

    private static func unavailableSuggestion(
        _ category: ChannelSuggestionCategory,
        title: String
    ) -> ChannelSuggestion {
        ChannelSuggestion(
            category: category,
            grade: .unavailable,
            statusLabel: "未检测",
            title: title,
            detail: "连接 Wi-Fi 并完成附近网络扫描后生成建议。"
        )
    }
}
