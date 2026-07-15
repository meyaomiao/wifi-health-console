import XCTest
@testable import WiFiHealthConsole

final class LiveDiagnosticServiceTests: XCTestCase {
    func testLiveDiagnosticWhenExplicitlyEnabled() async throws {
        guard ProcessInfo.processInfo.environment["RUN_LIVE_DIAGNOSIS_TEST"] == "1" else {
            throw XCTSkip("Live diagnosis is opt-in because it sends real network probes.")
        }

        let report = await NetworkDiagnosticService().run(durationSeconds: 3) { _, _ in }

        XCTAssertEqual(report.layers.count, DiagnosticLayer.allCases.count)
        XCTAssertFalse(report.baselineDescription.isEmpty)
        XCTAssertTrue(report.dns.succeeded || report.https.succeeded, "DNS 与 HTTPS 至少应有一条路径返回")

        let layerSummary = report.layers
            .map { "\($0.layer.rawValue)=\($0.grade.label)" }
            .joined(separator: ",")
        print(
            "LIVE_DIAG interface=\(report.interfaceName ?? "--") " +
            "gateway=\(report.gateway ?? "--") dns=\(report.dns.milliseconds ?? -1)ms " +
            "https=\(report.https.milliseconds ?? -1)ms layers=\(layerSummary)"
        )
    }
}
