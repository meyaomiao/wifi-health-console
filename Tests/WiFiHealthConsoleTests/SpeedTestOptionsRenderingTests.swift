import AppKit
import SwiftUI
import XCTest
@testable import WiFiHealthConsole

final class SpeedTestOptionsRenderingTests: XCTestCase {
    @MainActor
    func testRenderAlignedSpeedTestOptionsWhenExplicitlyEnabled() throws {
        guard let outputPath = ProcessInfo.processInfo.environment["RENDER_SPEED_OPTIONS_PATH"] else {
            throw XCTSkip("Options rendering is opt-in because it writes a local PNG artifact.")
        }

        let rootView = SpeedTestOptionsView(
            selectedRoute: .constant(.directWiFi),
            selectedDurationPreset: .constant(.stable),
            wifiInterface: "en0",
            isRunning: false
        )
        .frame(width: 760)
        .padding(20)

        let hostingView = NSHostingView(rootView: rootView)
        hostingView.frame = NSRect(x: 0, y: 0, width: 800, height: 620)
        hostingView.layoutSubtreeIfNeeded()

        let representation = try XCTUnwrap(hostingView.bitmapImageRepForCachingDisplay(in: hostingView.bounds))
        hostingView.cacheDisplay(in: hostingView.bounds, to: representation)
        let png = try XCTUnwrap(representation.representation(using: .png, properties: [:]))
        try png.write(to: URL(fileURLWithPath: outputPath), options: .atomic)

        XCTAssertGreaterThan(png.count, 10_000)
    }
}
