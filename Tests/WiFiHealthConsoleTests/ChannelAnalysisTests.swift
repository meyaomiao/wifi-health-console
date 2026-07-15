import XCTest
@testable import WiFiHealthConsole

final class ChannelAnalysisTests: XCTestCase {
    func testOverlapCountsNetworksInSameBand() {
        let current = currentSnapshot(band: .band5, channel: 40, width: 80)
        let networks = [
            network(id: "near", band: .band5, channel: 44, width: 80, rssi: -52),
            network(id: "far", band: .band5, channel: 149, width: 80, rssi: -52),
            network(id: "other-band", band: .band2, channel: 6, width: 20, rssi: -52)
        ]

        XCTAssertEqual(ChannelAnalysis.overlapCount(current: current, networks: networks), 1)
    }

    func testEverySuggestionSetContainsBandBandwidthAndChannelCategories() {
        let scenarios: [(WiFiSnapshot, [NearbyNetwork])] = [
            (.unavailable, []),
            (currentSnapshot(band: .band2, channel: 6, width: 40), []),
            (
                currentSnapshot(band: .band5, channel: 40, width: 80),
                [network(id: "neighbor", band: .band5, channel: 44, width: 80, rssi: -60)]
            )
        ]

        for (current, networks) in scenarios {
            let suggestions = ChannelAnalysis.suggestions(current: current, networks: networks)
            XCTAssertEqual(
                Set(suggestions.map(\.category)),
                Set(ChannelSuggestionCategory.allCases),
                "每组建议都必须覆盖频段、频宽和信道"
            )
        }
    }

    func testTwoPointFourGHzFortyMHzRecommendsTwentyMHzAsWarning() throws {
        let current = currentSnapshot(band: .band2, channel: 6, width: 40)

        XCTAssertEqual(ChannelAnalysis.recommendedWidthMHz(current: current, networks: []), 20)

        let suggestion = try bandwidthSuggestion(current: current, networks: [])
        XCTAssertEqual(suggestion.grade, .warning)
        XCTAssertEqual(suggestion.statusLabel, "注意")
        XCTAssertEqual(suggestion.title, "建议频宽：20 MHz")
    }

    func testStrongSameSSIDFiveGHzNetworkIsRecommendedFromTwoPointFourGHz() throws {
        let current = currentSnapshot(ssid: "Home", band: .band2, channel: 6, width: 20)
        let networks = [
            network(
                id: "home-5g",
                ssid: "Home",
                band: .band5,
                channel: 36,
                width: 80,
                rssi: -55
            )
        ]

        let suggestion = try bandSuggestion(current: current, networks: networks)
        XCTAssertEqual(suggestion.title, "推荐频段：5 GHz")
        XCTAssertEqual(suggestion.grade, .unavailable)
        XCTAssertEqual(suggestion.statusLabel, "参考")
    }

    func testFiveGHzOneHundredSixtyMHzRecommendsEightyMHzInOrdinaryEnvironment() throws {
        let current = currentSnapshot(band: .band5, channel: 40, width: 160)
        let networks = [
            network(id: "distant-neighbor", band: .band5, channel: 149, width: 80, rssi: -55)
        ]

        XCTAssertEqual(ChannelAnalysis.recommendedWidthMHz(current: current, networks: networks), 80)

        let suggestion = try bandwidthSuggestion(current: current, networks: networks)
        XCTAssertEqual(suggestion.grade, .warning)
        XCTAssertEqual(suggestion.statusLabel, "注意")
        XCTAssertEqual(suggestion.title, "建议频宽：80 MHz")
    }

    func testFourStrongOverlapsInsideEightyMHzRangeRecommendFortyMHz() throws {
        let current = currentSnapshot(band: .band5, channel: 40, width: 80)
        let networks = [36, 40, 44, 48].enumerated().map { index, channel in
            network(
                id: "strong-overlap-\(index)",
                band: .band5,
                channel: channel,
                width: 20,
                rssi: -60 - index
            )
        }

        XCTAssertEqual(ChannelAnalysis.overlapCount(current: current, networks: networks), 4)
        XCTAssertEqual(ChannelAnalysis.recommendedWidthMHz(current: current, networks: networks), 40)

        let suggestion = try bandwidthSuggestion(current: current, networks: networks)
        XCTAssertEqual(suggestion.grade, .warning)
        XCTAssertEqual(suggestion.statusLabel, "注意")
        XCTAssertEqual(suggestion.title, "建议频宽：40 MHz")
    }

    func testStrongHigherBandNetworksWithDifferentSSIDDoNotTriggerBandSwitch() throws {
        let current = currentSnapshot(ssid: "Home", band: .band2, channel: 6, width: 20)
        let networks = [
            network(id: "guest-5g", ssid: "Guest", band: .band5, channel: 36, width: 80, rssi: -35),
            network(id: "guest-6g", ssid: "Office", band: .band6, channel: 37, width: 80, rssi: -30)
        ]

        let suggestion = try bandSuggestion(current: current, networks: networks)
        XCTAssertEqual(suggestion.title, "保持频段：2.4 GHz")
        XCTAssertEqual(suggestion.grade, .good)
        XCTAssertEqual(suggestion.statusLabel, "正常")
    }

    func testAllSuggestionStatusLabelsRemainCompatibleWithGrades() {
        let crowdedNetworks = [36, 40, 44, 48].enumerated().map { index, channel in
            network(
                id: "crowded-\(index)",
                band: .band5,
                channel: channel,
                width: 20,
                rssi: -60 - index
            )
        }
        let scenarios: [(WiFiSnapshot, [NearbyNetwork])] = [
            (.unavailable, []),
            (currentSnapshot(band: .band2, channel: 6, width: 40), []),
            (
                currentSnapshot(ssid: "Home", band: .band2, channel: 6, width: 20),
                [network(id: "home-5g", ssid: "Home", band: .band5, channel: 36, width: 80, rssi: -55)]
            ),
            (currentSnapshot(band: .band5, channel: 40, width: 160), []),
            (currentSnapshot(band: .band5, channel: 40, width: 80), crowdedNetworks),
            (
                currentSnapshot(ssid: "Home", band: .band2, channel: 6, width: 20),
                [network(id: "other-5g", ssid: "Other", band: .band5, channel: 36, width: 80, rssi: -30)]
            )
        ]

        for (current, networks) in scenarios {
            for suggestion in ChannelAnalysis.suggestions(current: current, networks: networks) {
                XCTAssertTrue(
                    HealthStandards.isStatusLabelCompatible(suggestion.statusLabel, with: suggestion.grade),
                    "状态文字 \(suggestion.statusLabel) 与等级 \(suggestion.grade) 不兼容：\(suggestion.title)"
                )
            }
        }
    }

    func testOverlapSuggestionsNeverBecomeCriticalWithoutCCAOrRetransmissionEvidence() {
        let current = currentSnapshot(band: .band5, channel: 40, width: 160)
        let networks = (1...6).map { index in
            network(
                id: "neighbor-\(index)",
                band: .band5,
                channel: 36 + index * 4,
                width: 80,
                rssi: -45 - index
            )
        }

        let suggestions = ChannelAnalysis.suggestions(current: current, networks: networks)
        XCTAssertFalse(suggestions.isEmpty)
        XCTAssertFalse(
            suggestions.contains(where: { $0.grade == .critical }),
            "扫描重叠不能脱离 CCA/重传单独判严重"
        )
    }

    private func bandSuggestion(
        current: WiFiSnapshot,
        networks: [NearbyNetwork],
        file: StaticString = #filePath,
        line: UInt = #line
    ) throws -> ChannelSuggestion {
        try XCTUnwrap(
            ChannelAnalysis.suggestions(current: current, networks: networks)
                .first(where: { $0.category == .band }),
            "缺少频段建议",
            file: file,
            line: line
        )
    }

    private func bandwidthSuggestion(
        current: WiFiSnapshot,
        networks: [NearbyNetwork],
        file: StaticString = #filePath,
        line: UInt = #line
    ) throws -> ChannelSuggestion {
        try XCTUnwrap(
            ChannelAnalysis.suggestions(current: current, networks: networks)
                .first(where: { $0.category == .bandwidth }),
            "缺少频宽建议",
            file: file,
            line: line
        )
    }

    private func currentSnapshot(
        ssid: String = "Home",
        band: WiFiBand,
        channel: Int,
        width: Int,
        rssi: Int = -50
    ) -> WiFiSnapshot {
        WiFiSnapshot(
            ssid: ssid,
            bssid: "current",
            band: band,
            channel: channel,
            channelWidthMHz: width,
            rssi: rssi,
            noise: -90
        )
    }

    private func network(
        id: String,
        ssid: String? = nil,
        band: WiFiBand,
        channel: Int,
        width: Int,
        rssi: Int
    ) -> NearbyNetwork {
        NearbyNetwork(
            id: id,
            ssid: ssid ?? id,
            bssid: id,
            band: band,
            channel: channel,
            channelWidthMHz: width,
            rssi: rssi,
            noise: -90
        )
    }
}
