import AppKit
import SwiftUI
import XCTest
@testable import WiFiHealthConsole

final class ChannelSuggestionsRenderingTests: XCTestCase {
    @MainActor
    func testRenderThreeSuggestionCategoriesWhenExplicitlyEnabled() throws {
        guard let outputPath = ProcessInfo.processInfo.environment["RENDER_CHANNEL_SUGGESTIONS_PATH"] else {
            throw XCTSkip("Suggestion rendering is opt-in because it writes a local PNG artifact.")
        }

        let current = WiFiSnapshot(
            ssid: "Home",
            bssid: "current",
            band: .band5,
            channel: 40,
            channelWidthMHz: 160,
            rssi: -58,
            noise: -92
        )
        let networks = [
            NearbyNetwork(id: "home-6", ssid: "Home", bssid: "home-6", band: .band6, channel: 37, channelWidthMHz: 80, rssi: -57, noise: -92),
            NearbyNetwork(id: "near-1", ssid: "Neighbor 1", bssid: "near-1", band: .band5, channel: 36, channelWidthMHz: 80, rssi: -60, noise: -92),
            NearbyNetwork(id: "near-2", ssid: "Neighbor 2", bssid: "near-2", band: .band5, channel: 44, channelWidthMHz: 80, rssi: -66, noise: -92)
        ]

        let rootView = ChannelSuggestionsView(
            title: "频段、频宽与信道建议",
            suggestions: ChannelAnalysis.suggestions(current: current, networks: networks)
        )
        .frame(width: 1_140)
        .padding(20)

        let hostingView = NSHostingView(rootView: rootView)
        hostingView.frame = NSRect(x: 0, y: 0, width: 1_180, height: 520)
        hostingView.layoutSubtreeIfNeeded()

        let representation = try XCTUnwrap(hostingView.bitmapImageRepForCachingDisplay(in: hostingView.bounds))
        hostingView.cacheDisplay(in: hostingView.bounds, to: representation)
        let png = try XCTUnwrap(representation.representation(using: .png, properties: [:]))
        try png.write(to: URL(fileURLWithPath: outputPath), options: .atomic)
        XCTAssertGreaterThan(png.count, 10_000)
    }
}
