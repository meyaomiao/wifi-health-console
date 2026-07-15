import XCTest
@testable import WiFiHealthConsole

final class HealthStandardsTests: XCTestCase {
    func testRSSIBoundaries() {
        assertAssessment(HealthStandards.rssi(nil), grade: .unavailable, label: "未检测")
        assertAssessment(HealthStandards.rssi(-54), grade: .good, label: "优秀")
        assertAssessment(HealthStandards.rssi(-55), grade: .good, label: "正常")
        assertAssessment(HealthStandards.rssi(-67), grade: .good, label: "正常")
        assertAssessment(HealthStandards.rssi(-68), grade: .warning, label: "注意")
        assertAssessment(HealthStandards.rssi(-75), grade: .warning, label: "注意")
        assertAssessment(HealthStandards.rssi(-76), grade: .critical, label: "严重")
    }

    func testSNRBoundaries() {
        assertAssessment(HealthStandards.snr(nil), grade: .unavailable, label: "未检测")
        assertAssessment(HealthStandards.snr(40), grade: .good, label: "优秀")
        assertAssessment(HealthStandards.snr(39), grade: .good, label: "正常")
        assertAssessment(HealthStandards.snr(30), grade: .good, label: "正常")
        assertAssessment(HealthStandards.snr(29), grade: .warning, label: "注意")
        assertAssessment(HealthStandards.snr(20), grade: .warning, label: "注意")
        assertAssessment(HealthStandards.snr(19), grade: .critical, label: "严重")
    }

    func testGatewayLatencyBoundaries() {
        assertAssessment(HealthStandards.gatewayLatency(nil), grade: .unavailable, label: "未检测")
        assertAssessment(HealthStandards.gatewayLatency(10), grade: .good, label: "优秀")
        assertAssessment(HealthStandards.gatewayLatency(10.1), grade: .good, label: "正常")
        assertAssessment(HealthStandards.gatewayLatency(30), grade: .good, label: "正常")
        assertAssessment(HealthStandards.gatewayLatency(30.1), grade: .warning, label: "注意")
        assertAssessment(HealthStandards.gatewayLatency(100), grade: .warning, label: "注意")
        assertAssessment(HealthStandards.gatewayLatency(100.1), grade: .critical, label: "严重")
    }

    func testGatewayJitterBoundaries() {
        assertAssessment(HealthStandards.gatewayJitter(nil), grade: .unavailable, label: "未检测")
        assertAssessment(HealthStandards.gatewayJitter(10), grade: .good, label: "正常")
        assertAssessment(HealthStandards.gatewayJitter(10.1), grade: .warning, label: "注意")
        assertAssessment(HealthStandards.gatewayJitter(30), grade: .warning, label: "注意")
        assertAssessment(HealthStandards.gatewayJitter(30.1), grade: .critical, label: "严重")
    }

    func testGatewayLossBoundaries() {
        assertAssessment(HealthStandards.gatewayLoss(nil), grade: .unavailable, label: "未检测")
        assertAssessment(HealthStandards.gatewayLoss(1), grade: .good, label: "正常")
        assertAssessment(HealthStandards.gatewayLoss(1.1), grade: .warning, label: "注意")
        assertAssessment(HealthStandards.gatewayLoss(5), grade: .warning, label: "注意")
        assertAssessment(HealthStandards.gatewayLoss(5.1), grade: .critical, label: "严重")
    }

    func testCCABoundaries() {
        assertAssessment(HealthStandards.cca(nil), grade: .unavailable, label: "未检测")
        assertAssessment(HealthStandards.cca(50), grade: .good, label: "正常")
        assertAssessment(HealthStandards.cca(50.1), grade: .warning, label: "注意")
        assertAssessment(HealthStandards.cca(80), grade: .warning, label: "注意")
        assertAssessment(HealthStandards.cca(80.1), grade: .critical, label: "严重")
    }

    func testInternetLatencyBoundaries() {
        assertAssessment(HealthStandards.internetLatency(nil), grade: .unavailable, label: "未检测")
        assertAssessment(HealthStandards.internetLatency(80), grade: .good, label: "正常")
        assertAssessment(HealthStandards.internetLatency(80.1), grade: .warning, label: "注意")
        assertAssessment(HealthStandards.internetLatency(150), grade: .warning, label: "注意")
        assertAssessment(HealthStandards.internetLatency(150.1), grade: .critical, label: "严重")
    }

    func testDNSBoundariesAndFailure() {
        assertAssessment(
            HealthStandards.dns(endpoint(succeeded: true, milliseconds: nil)),
            grade: .unavailable,
            label: "未检测"
        )
        assertAssessment(HealthStandards.dns(endpoint(milliseconds: 100)), grade: .good, label: "正常")
        assertAssessment(HealthStandards.dns(endpoint(milliseconds: 100.1)), grade: .warning, label: "注意")
        assertAssessment(HealthStandards.dns(endpoint(milliseconds: 300)), grade: .warning, label: "注意")
        assertAssessment(HealthStandards.dns(endpoint(milliseconds: 300.1)), grade: .critical, label: "严重")
        assertAssessment(
            HealthStandards.dns(endpoint(succeeded: false, milliseconds: nil)),
            grade: .critical,
            label: "严重"
        )
    }

    func testHTTPSBoundariesAndFailure() {
        assertAssessment(
            HealthStandards.https(endpoint(succeeded: true, milliseconds: nil)),
            grade: .unavailable,
            label: "未检测"
        )
        assertAssessment(HealthStandards.https(endpoint(milliseconds: 800)), grade: .good, label: "正常")
        assertAssessment(HealthStandards.https(endpoint(milliseconds: 800.1)), grade: .warning, label: "注意")
        assertAssessment(HealthStandards.https(endpoint(milliseconds: 2_000)), grade: .warning, label: "注意")
        assertAssessment(HealthStandards.https(endpoint(milliseconds: 2_000.1)), grade: .critical, label: "严重")
        assertAssessment(
            HealthStandards.https(endpoint(succeeded: false, milliseconds: nil)),
            grade: .critical,
            label: "严重"
        )
    }

    func testDownloadBoundaries() {
        assertAssessment(HealthStandards.download(100), grade: .good, label: "优秀")
        assertAssessment(HealthStandards.download(99.9), grade: .good, label: "正常")
        assertAssessment(HealthStandards.download(25), grade: .good, label: "正常")
        assertAssessment(HealthStandards.download(24.9), grade: .warning, label: "注意")
        assertAssessment(HealthStandards.download(10), grade: .warning, label: "注意")
        assertAssessment(HealthStandards.download(9.9), grade: .critical, label: "严重")
    }

    func testUploadBoundaries() {
        assertAssessment(HealthStandards.upload(20), grade: .good, label: "优秀")
        assertAssessment(HealthStandards.upload(19.9), grade: .good, label: "正常")
        assertAssessment(HealthStandards.upload(10), grade: .good, label: "正常")
        assertAssessment(HealthStandards.upload(9.9), grade: .warning, label: "注意")
        assertAssessment(HealthStandards.upload(5), grade: .warning, label: "注意")
        assertAssessment(HealthStandards.upload(4.9), grade: .critical, label: "严重")
    }

    func testIdleLatencyBoundaries() {
        assertAssessment(HealthStandards.idleLatency(nil), grade: .unavailable, label: "未检测")
        assertAssessment(HealthStandards.idleLatency(40), grade: .good, label: "正常")
        assertAssessment(HealthStandards.idleLatency(40.1), grade: .warning, label: "注意")
        assertAssessment(HealthStandards.idleLatency(100), grade: .warning, label: "注意")
        assertAssessment(HealthStandards.idleLatency(100.1), grade: .critical, label: "严重")
    }

    func testResponsivenessBoundaries() {
        assertAssessment(
            HealthStandards.responsiveness(nil, subject: "下载时响应"),
            grade: .unavailable,
            label: "未检测"
        )
        assertAssessment(
            HealthStandards.responsiveness(600, subject: "下载时响应"),
            grade: .good,
            label: "优秀"
        )
        assertAssessment(
            HealthStandards.responsiveness(599, subject: "下载时响应"),
            grade: .warning,
            label: "注意"
        )
        assertAssessment(
            HealthStandards.responsiveness(200, subject: "下载时响应"),
            grade: .warning,
            label: "注意"
        )
        assertAssessment(
            HealthStandards.responsiveness(199, subject: "下载时响应"),
            grade: .critical,
            label: "严重"
        )
    }

    func testPublicICMPLossNeverBecomesCriticalByItself() {
        assertAssessment(HealthStandards.publicICMPLoss(nil), grade: .unavailable, label: "未检测")
        assertAssessment(HealthStandards.publicICMPLoss(10), grade: .good, label: "正常")

        for loss in [10.1, 50, 51, 100, 1_000] {
            let assessment = HealthStandards.publicICMPLoss(loss)
            assertAssessment(assessment, grade: .warning, label: "注意")
            XCTAssertNotEqual(assessment.grade, .critical, "公网 ICMP 丢包不能单独判为严重")
        }
    }

    func testWorstGradeAndSummaryStatusLabel() {
        XCTAssertEqual(HealthStandards.worst([.unavailable]), .unavailable)
        XCTAssertEqual(HealthStandards.worst([.unavailable, .good]), .good)
        XCTAssertEqual(HealthStandards.worst([.good, .warning]), .warning)
        XCTAssertEqual(HealthStandards.worst([.warning, .critical]), .critical)

        XCTAssertEqual(
            HealthStandards.summaryStatusLabel(for: [HealthStandards.download(100), HealthStandards.upload(20)]),
            "优秀"
        )
        XCTAssertEqual(
            HealthStandards.summaryStatusLabel(for: [HealthStandards.download(100), HealthStandards.upload(10)]),
            "正常"
        )
        XCTAssertEqual(
            HealthStandards.summaryStatusLabel(for: [HealthStandards.download(10), HealthStandards.upload(20)]),
            "注意"
        )
        XCTAssertEqual(
            HealthStandards.summaryStatusLabel(for: [HealthStandards.download(9.9), HealthStandards.upload(20)]),
            "严重"
        )
        XCTAssertEqual(
            HealthStandards.summaryStatusLabel(for: [HealthStandards.reference(available: true, interpretation: "参考", standard: "参考")]),
            "未检测"
        )
    }

    func testEveryReturnedStatusLabelIsCompatibleWithItsGrade() {
        for assessment in allAssessmentBranches {
            XCTAssertTrue(
                HealthStandards.isStatusLabelCompatible(assessment.statusLabel, with: assessment.grade),
                "不兼容状态：\(assessment.statusLabel) / \(assessment.grade)"
            )
        }
    }

    func testCompatibilityMatrixRejectsCrossGradeLabels() {
        let compatible: [(HealthGrade, [String])] = [
            (.good, ["优秀", "正常"]),
            (.warning, ["注意"]),
            (.critical, ["严重"]),
            (.unavailable, ["未检测", "参考", "部分完成"])
        ]

        for (grade, labels) in compatible {
            for label in labels {
                XCTAssertTrue(HealthStandards.isStatusLabelCompatible(label, with: grade))
            }
        }

        for grade in HealthGrade.allCases {
            for label in ["优秀", "正常", "注意", "严重", "未检测", "参考", "部分完成"]
            where !compatible.first(where: { $0.0 == grade })!.1.contains(label) {
                XCTAssertFalse(
                    HealthStandards.isStatusLabelCompatible(label, with: grade),
                    "跨等级状态不应兼容：\(label) / \(grade)"
                )
            }
        }
    }

    func testDiagnosticMetricCopiesOneAssessmentWithoutIndependentStatusText() {
        let assessment = HealthStandards.rssi(-68)
        let metric = DiagnosticMetric(
            id: "rssi",
            title: "RSSI",
            value: "-68 dBm",
            assessment: assessment,
            impact: "影响稳定性"
        )

        XCTAssertEqual(metric.grade, assessment.grade)
        XCTAssertEqual(metric.statusLabel, assessment.statusLabel)
        XCTAssertEqual(metric.interpretation, assessment.interpretation)
        XCTAssertEqual(metric.standard, assessment.standard)
    }

    func testReportUsesPartialCompletionInsteadOfClaimingEveryLayerIsNormal() {
        let report = DiagnosticReport(
            startedAt: Date(),
            completedAt: Date(),
            gateway: nil,
            interfaceName: nil,
            baselineDescription: "测试",
            wirelessSamples: [],
            gatewayPing: nil,
            externalPing: nil,
            dns: endpoint(milliseconds: 20),
            https: endpoint(milliseconds: 200),
            layers: [
                layer(.wireless, grade: .good),
                layer(.localNetwork, grade: .unavailable),
                layer(.internet, grade: .good),
                layer(.proxyVPN, grade: .good)
            ]
        )

        XCTAssertEqual(report.overallGrade, .unavailable)
        XCTAssertEqual(report.overallStatusLabel, "部分完成")
        XCTAssertTrue(report.hasUnavailableLayer)
    }

    private var allAssessmentBranches: [MetricAssessment] {
        [
            HealthStandards.rssi(nil), HealthStandards.rssi(-54), HealthStandards.rssi(-55), HealthStandards.rssi(-68), HealthStandards.rssi(-76),
            HealthStandards.snr(nil), HealthStandards.snr(40), HealthStandards.snr(30), HealthStandards.snr(20), HealthStandards.snr(19),
            HealthStandards.cca(nil), HealthStandards.cca(50), HealthStandards.cca(60), HealthStandards.cca(81),
            HealthStandards.gatewayLatency(nil), HealthStandards.gatewayLatency(10), HealthStandards.gatewayLatency(20), HealthStandards.gatewayLatency(50), HealthStandards.gatewayLatency(101),
            HealthStandards.gatewayJitter(nil), HealthStandards.gatewayJitter(10), HealthStandards.gatewayJitter(20), HealthStandards.gatewayJitter(31),
            HealthStandards.gatewayLoss(nil), HealthStandards.gatewayLoss(1), HealthStandards.gatewayLoss(2), HealthStandards.gatewayLoss(6),
            HealthStandards.internetLatency(nil), HealthStandards.internetLatency(80), HealthStandards.internetLatency(100), HealthStandards.internetLatency(151),
            HealthStandards.publicICMPLoss(nil), HealthStandards.publicICMPLoss(10), HealthStandards.publicICMPLoss(100),
            HealthStandards.dns(endpoint(succeeded: true, milliseconds: nil)), HealthStandards.dns(endpoint(milliseconds: 100)), HealthStandards.dns(endpoint(milliseconds: 200)), HealthStandards.dns(endpoint(milliseconds: 301)), HealthStandards.dns(endpoint(succeeded: false, milliseconds: nil)),
            HealthStandards.https(endpoint(succeeded: true, milliseconds: nil)), HealthStandards.https(endpoint(milliseconds: 800)), HealthStandards.https(endpoint(milliseconds: 1_000)), HealthStandards.https(endpoint(milliseconds: 2_001)), HealthStandards.https(endpoint(succeeded: false, milliseconds: nil)),
            HealthStandards.download(100), HealthStandards.download(25), HealthStandards.download(10), HealthStandards.download(9),
            HealthStandards.upload(20), HealthStandards.upload(10), HealthStandards.upload(5), HealthStandards.upload(4),
            HealthStandards.idleLatency(nil), HealthStandards.idleLatency(40), HealthStandards.idleLatency(80), HealthStandards.idleLatency(101),
            HealthStandards.responsiveness(nil, subject: "响应"), HealthStandards.responsiveness(600, subject: "响应"), HealthStandards.responsiveness(200, subject: "响应"), HealthStandards.responsiveness(199, subject: "响应"),
            HealthStandards.channelWidth(nil, band: .band5), HealthStandards.channelWidth(40, band: .band2), HealthStandards.channelWidth(160, band: .band5), HealthStandards.channelWidth(80, band: .band5), HealthStandards.channelWidth(40, band: .band5),
            HealthStandards.reference(available: false, interpretation: "未取得", standard: "参考"), HealthStandards.reference(available: true, interpretation: "已取得", standard: "参考"),
            HealthStandards.pathState(active: false, detail: "未启用", standard: "路径"), HealthStandards.pathState(active: true, detail: "已启用", standard: "路径")
        ]
    }

    private func endpoint(succeeded: Bool = true, milliseconds: Double?) -> EndpointTiming {
        EndpointTiming(
            succeeded: succeeded,
            milliseconds: milliseconds,
            detail: succeeded ? "完成" : "测试失败"
        )
    }

    private func layer(_ layer: DiagnosticLayer, grade: HealthGrade) -> LayerResult {
        LayerResult(
            layer: layer,
            grade: grade,
            conclusion: grade.label,
            evidence: [],
            metrics: [],
            action: "测试"
        )
    }

    private func assertAssessment(
        _ assessment: MetricAssessment,
        grade: HealthGrade,
        label: String,
        file: StaticString = #filePath,
        line: UInt = #line
    ) {
        XCTAssertEqual(assessment.grade, grade, file: file, line: line)
        XCTAssertEqual(assessment.statusLabel, label, file: file, line: line)
        XCTAssertTrue(
            HealthStandards.isStatusLabelCompatible(assessment.statusLabel, with: assessment.grade),
            "状态文字 \(assessment.statusLabel) 与等级 \(assessment.grade) 不兼容",
            file: file,
            line: line
        )
    }
}
