import AppKit
import SwiftUI
import XCTest
@testable import WiFiHealthConsole

final class SpeedLiveChartRenderingTests: XCTestCase {
    @MainActor
    func testRenderSeparatedDirectionChartsWhenExplicitlyEnabled() throws {
        guard let outputPath = ProcessInfo.processInfo.environment["RENDER_SPEED_CHART_PATH"] else {
            throw XCTSkip("Chart rendering is opt-in because it writes a local PNG artifact.")
        }

        let downloadSamples = (0..<24).map { index in
            let elapsed = Double(index) * 0.5
            let speed = 12 + sin(Double(index) * 0.32) * 7 + Double(index) * 1.8
            return SpeedTestSample(
                phase: .download,
                elapsedSeconds: elapsed,
                mbps: max(0, speed)
            )
        }
        let uploadSamples = (0..<20).map { index in
            let elapsed = Double(index) * 0.5
            let speed = 8 + sin(Double(index) * 0.28) * 5 + Double(index) * 1.9
            return SpeedTestSample(
                phase: .upload,
                elapsedSeconds: elapsed,
                mbps: max(0, speed)
            )
        }

        let rootView = SpeedLiveChart(
            samples: downloadSamples + uploadSamples,
            currentPhase: nil,
            downloadMbps: 42.8,
            uploadMbps: 36.4,
            interfaceName: "en0",
            isRunning: false
        )
        .frame(width: 1_220, height: 940)
        .padding(20)

        let hostingView = NSHostingView(rootView: rootView)
        hostingView.frame = NSRect(x: 0, y: 0, width: 1_260, height: 980)
        hostingView.layoutSubtreeIfNeeded()

        let representation = try XCTUnwrap(hostingView.bitmapImageRepForCachingDisplay(in: hostingView.bounds))
        hostingView.cacheDisplay(in: hostingView.bounds, to: representation)
        let png = try XCTUnwrap(representation.representation(using: .png, properties: [:]))
        try png.write(to: URL(fileURLWithPath: outputPath), options: .atomic)

        XCTAssertGreaterThan(png.count, 10_000)
    }
}
