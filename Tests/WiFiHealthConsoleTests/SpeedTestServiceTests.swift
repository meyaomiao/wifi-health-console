import XCTest
@testable import WiFiHealthConsole

final class SpeedTestServiceTests: XCTestCase {
    func testLiveCurrentPathWhenExplicitlyEnabled() async throws {
        guard ProcessInfo.processInfo.environment["RUN_LIVE_SPEED_TEST"] == "1" else {
            throw XCTSkip("Live speed test is opt-in because it uses real network data.")
        }

        let collector = SpeedSampleCollector()
        let report = try await SpeedTestService().run(
            route: .currentPath,
            wifiInterface: "en0",
            onSample: { sample in
                Task { await collector.append(sample) }
            }
        )
        try? await Task.sleep(for: .milliseconds(100))
        let samples = await collector.values
        XCTAssertGreaterThan(report.downloadBitsPerSecond, 0)
        XCTAssertGreaterThan(report.uploadBitsPerSecond, 0)
        XCTAssertGreaterThan(samples.count, 5)
        XCTAssertTrue(samples.contains { $0.phase == .download })
        XCTAssertTrue(samples.contains { $0.phase == .upload })
        print(
            String(
                format: "LIVE_SPEED download=%.2fMbps download=%.2fMB/s upload=%.2fMbps latency=%.1fms interface=%@ proxied=%@ live_samples=%d peak_down=%.2f peak_up=%.2f",
                report.downloadMbps,
                report.downloadMegabytesPerSecond,
                report.uploadMbps,
                report.idleLatencyMs ?? -1,
                report.measuredInterface ?? "unknown",
                report.wasProxied ? "yes" : "no",
                samples.count,
                samples.filter { $0.phase == .download }.map(\.mbps).max() ?? 0,
                samples.filter { $0.phase == .upload }.map(\.mbps).max() ?? 0
            )
        )
    }

    func testBuildsSeparateOneWayArgumentsAndBindsOnlyDirectWiFi() throws {
        let download = try SpeedTestService.arguments(
            for: .download,
            route: .currentPath,
            wifiInterface: "en0",
            durationPreset: .standard
        )
        let upload = try SpeedTestService.arguments(
            for: .upload,
            route: .directWiFi,
            wifiInterface: "en0",
            durationPreset: .stable
        )

        XCTAssertTrue(download.contains("-u"))
        XCTAssertFalse(download.contains("-d"))
        XCTAssertFalse(download.contains("-I"))
        XCTAssertTrue(upload.contains("-d"))
        XCTAssertFalse(upload.contains("-u"))
        XCTAssertEqual(Array(upload.suffix(2)), ["-I", "en0"])
        XCTAssertEqual(argument(after: "-M", in: download), "20")
        XCTAssertEqual(argument(after: "-M", in: upload), "30")
        XCTAssertFalse(download.contains("-s"))
        XCTAssertFalse(upload.contains("-s"))
    }

    func testDurationPresetMetadataUsesLongerDefaultAndStableLimits() {
        XCTAssertEqual(SpeedTestDurationPreset.standard.phaseRuntimeSeconds, 20)
        XCTAssertEqual(SpeedTestDurationPreset.standard.maximumTotalSeconds, 40)
        XCTAssertEqual(SpeedTestDurationPreset.stable.phaseRuntimeSeconds, 30)
        XCTAssertEqual(SpeedTestDurationPreset.stable.maximumTotalSeconds, 60)
        XCTAssertTrue(SpeedTestDurationPreset.standard.detail.contains("提前结束"))
        XCTAssertTrue(SpeedTestDurationPreset.stable.trafficWarning.contains("手机热点"))
    }

    func testDirectWiFiArgumentsRequireAnInterface() {
        XCTAssertThrowsError(
            try SpeedTestService.arguments(
                for: .download,
                route: .directWiFi,
                wifiInterface: nil,
                durationPreset: .standard
            )
        )
    }

    func testParsesAndCombinesOneWayPhaseOutputs() throws {
        let downloadJSON = #"""
        {
          "base_rtt": 18,
          "dl_bytes_transferred": 50000000,
          "dl_phase_duration": 8,
          "dl_responsiveness": 650,
          "dl_throughput": 400000000,
          "interface_name": "en0",
          "other": { "proxy_state": { "proxied": 0 } },
          "test_endpoint": "download.example.test"
        }
        """#
        let uploadJSON = #"""
        {
          "base_rtt": 22,
          "interface_name": "en0",
          "other": { "proxy_state": { "proxied": 2 } },
          "test_endpoint": "upload.example.test",
          "ul_bytes_transferred": 10000000,
          "ul_phase_duration": 7,
          "ul_responsiveness": 350,
          "ul_throughput": 80000000
        }
        """#

        let download = try SpeedTestService.parsePhase(output: downloadJSON, phase: .download)
        let upload = try SpeedTestService.parsePhase(output: uploadJSON, phase: .upload)
        let report = try SpeedTestService.combine(
            download: download,
            upload: upload,
            route: .directWiFi,
            durationPreset: .stable,
            requestedInterface: "en0",
            sampledInterface: "en0"
        )

        XCTAssertEqual(download.throughputBitsPerSecond, 400_000_000)
        XCTAssertFalse(download.wasProxied)
        XCTAssertEqual(upload.throughputBitsPerSecond, 80_000_000)
        XCTAssertTrue(upload.wasProxied)
        XCTAssertEqual(report.downloadMbps, 400, accuracy: 0.001)
        XCTAssertEqual(report.uploadMbps, 80, accuracy: 0.001)
        XCTAssertEqual(report.idleLatencyMs, 20)
        XCTAssertEqual(report.durationSeconds, 15)
        XCTAssertEqual(report.transferredMegabytes, 60, accuracy: 0.001)
        XCTAssertEqual(report.sampledInterface, "en0")
        XCTAssertEqual(report.durationPreset, .stable)
        XCTAssertTrue(report.wasProxied)
    }

    func testParsesDefaultRouteInterfaceForCurrentPathSampling() {
        let output = """
           route to: default
        destination: default
              mask: default
           gateway: 192.168.1.1
         interface: utun4
        """

        XCTAssertEqual(DefaultRouteInterface.parse(output: output), "utun4")
        XCTAssertNil(DefaultRouteInterface.parse(output: "gateway: 192.168.1.1"))
    }

    func testOneWayParserRejectsTheMissingRequestedPhase() throws {
        let downloadOnly = #"{ "dl_throughput": 100000000 }"#
        XCTAssertThrowsError(
            try SpeedTestService.parsePhase(output: downloadOnly, phase: .upload)
        ) { error in
            XCTAssertEqual(error as? SpeedTestError, .incomplete)
        }
    }

    func testParsesNetworkQualityOutputAndConvertsUnits() throws {
        let json = #"""
        {
          "base_rtt": 22.5,
          "dl_bytes_transferred": 50000000,
          "dl_phase_duration": 8,
          "dl_responsiveness": 650,
          "dl_throughput": 400000000,
          "interface_name": "en0",
          "other": { "proxy_state": { "proxied": 4 } },
          "test_endpoint": "example.test",
          "ul_bytes_transferred": 10000000,
          "ul_phase_duration": 7,
          "ul_responsiveness": 350,
          "ul_throughput": 80000000
        }
        """#

        let report = try SpeedTestService.parse(
            output: json,
            route: .currentPath,
            requestedInterface: nil
        )

        XCTAssertEqual(report.downloadMbps, 400, accuracy: 0.001)
        XCTAssertEqual(report.downloadMegabytesPerSecond, 50, accuracy: 0.001)
        XCTAssertEqual(report.uploadMbps, 80, accuracy: 0.001)
        XCTAssertEqual(try XCTUnwrap(report.downloadSeconds(forGigabytes: 1)), 20, accuracy: 0.001)
        XCTAssertEqual(report.transferredMegabytes, 60, accuracy: 0.001)
        XCTAssertEqual(report.durationSeconds, 15)
        XCTAssertTrue(report.wasProxied)
    }

    func testParsesNetworkQualityError() {
        let json = #"{ "error_code": -1009, "error_domain": "NSURLErrorDomain" }"#
        XCTAssertThrowsError(
            try SpeedTestService.parse(output: json, route: .directWiFi, requestedInterface: "en0")
        ) { error in
            XCTAssertEqual((error as? SpeedTestError)?.localizedDescription.contains("无法连接互联网"), true)
        }
    }

    func testParsesInterfaceByteCounters() throws {
        let output = """
        Name       Mtu   Network       Address            Ipkts Ierrs     Ibytes    Opkts Oerrs     Obytes  Coll
        en0        1500  <Link#12>   aa:bb:cc:dd:ee:ff   1000     0   12345678      900     0    8765432     0
        en0        1500  192.168/24    192.168.1.2        1000     -   12345678      900     -    8765432     -
        """

        let counters = try XCTUnwrap(InterfaceTrafficSampler.parse(output: output, interfaceName: "en0"))
        XCTAssertEqual(counters.receivedBytes, 12_345_678)
        XCTAssertEqual(counters.sentBytes, 8_765_432)
    }

    func testTrafficRateSmootherReducesAbruptJumpsWithoutInventingValues() {
        var smoother = TrafficRateSmoother(timeConstantSeconds: 1)
        let idle = smoother.update(mbps: 0, intervalSeconds: 0.5)
        let rising = smoother.update(mbps: 100, intervalSeconds: 0.5)
        let falling = smoother.update(mbps: 0, intervalSeconds: 0.5)

        XCTAssertEqual(idle, 0)
        XCTAssertGreaterThan(rising, 0)
        XCTAssertLessThan(rising, 100)
        XCTAssertGreaterThan(falling, 0)
        XCTAssertLessThan(falling, rising)
    }

    func testNewPhaseSmootherDoesNotInheritThePreviousPhaseTail() {
        var downloadSmoother = TrafficRateSmoother(timeConstantSeconds: 1)
        _ = downloadSmoother.update(mbps: 100, intervalSeconds: 0.5)
        _ = downloadSmoother.update(mbps: 20, intervalSeconds: 0.5)

        var uploadSmoother = TrafficRateSmoother(timeConstantSeconds: 1)
        let firstUpload = uploadSmoother.update(mbps: 5, intervalSeconds: 0.5)

        XCTAssertEqual(firstUpload, 5, accuracy: 0.001)
    }

    func testChartDataFiltersSamplesByTheirMeasuredPhase() {
        let samples = [
            SpeedTestSample(phase: .download, elapsedSeconds: 0.5, mbps: 20),
            SpeedTestSample(phase: .download, elapsedSeconds: 1.0, mbps: 30),
            SpeedTestSample(phase: .upload, elapsedSeconds: 0.5, mbps: 4)
        ]
        let downloads = SpeedChartData.points(from: samples, series: .download)
        let uploads = SpeedChartData.points(from: samples, series: .upload)

        XCTAssertEqual(downloads.map(\.series), [.download, .download])
        XCTAssertEqual(uploads.map(\.series), [.upload])
        XCTAssertEqual(downloads.map(\.mbps), [20, 30])
        XCTAssertEqual(uploads.map(\.mbps), [4])
        XCTAssertEqual(downloads.map(\.elapsedSeconds), [0.5, 1.0])
        XCTAssertEqual(uploads.map(\.elapsedSeconds), [0.5])
        XCTAssertTrue(Set(downloads.map(\.id)).isDisjoint(with: Set(uploads.map(\.id))))
    }
}

private actor SpeedSampleCollector {
    private(set) var values: [SpeedTestSample] = []

    func append(_ sample: SpeedTestSample) {
        values.append(sample)
    }
}

private func argument(after flag: String, in arguments: [String]) -> String? {
    guard let index = arguments.firstIndex(of: flag), arguments.indices.contains(index + 1) else {
        return nil
    }
    return arguments[index + 1]
}
