import Foundation

enum SpeedTestError: LocalizedError, Equatable {
    case unavailable(String)
    case invalidOutput
    case failed(domain: String, code: Int)
    case incomplete

    var errorDescription: String? {
        switch self {
        case .unavailable(let detail):
            return "系统测速工具不可用：\(detail)"
        case .invalidOutput:
            return "系统测速工具返回了无法识别的结果。"
        case .failed(let domain, let code):
            if domain == "NSURLErrorDomain", code == -1009 {
                return "当前测速路径无法连接互联网。若使用“直连 Wi-Fi 基线”，可能是 VPN/代理接管了网络，或该网络不允许直接访问。"
            }
            return "测速失败（\(domain) \(code)）。"
        case .incomplete:
            return "测速未取得完整的下载和上传结果，请稍后重试。"
        }
    }
}

struct SpeedTestService {
    func run(
        route: SpeedTestRoute,
        wifiInterface: String?,
        durationPreset: SpeedTestDurationPreset = .standard,
        onPhaseChange: @escaping (SpeedTestPhase) -> Void = { _ in },
        onSamplingInterface: @escaping (String?) -> Void = { _ in },
        onSample: @escaping (SpeedTestSample) -> Void = { _ in }
    ) async throws -> SpeedTestReport {
        let sampledInterface = await Self.samplingInterface(
            for: route,
            wifiInterface: wifiInterface
        )
        onSamplingInterface(sampledInterface)

        onPhaseChange(.download)
        let download = try await runPhase(
            .download,
            route: route,
            wifiInterface: wifiInterface,
            durationPreset: durationPreset,
            sampledInterface: sampledInterface,
            onSample: onSample
        )

        onPhaseChange(.upload)
        let upload = try await runPhase(
            .upload,
            route: route,
            wifiInterface: wifiInterface,
            durationPreset: durationPreset,
            sampledInterface: sampledInterface,
            onSample: onSample
        )

        return try Self.combine(
            download: download,
            upload: upload,
            route: route,
            durationPreset: durationPreset,
            requestedInterface: route == .directWiFi ? wifiInterface : nil,
            sampledInterface: sampledInterface
        )
    }

    private func runPhase(
        _ phase: SpeedTestPhase,
        route: SpeedTestRoute,
        wifiInterface: String?,
        durationPreset: SpeedTestDurationPreset,
        sampledInterface: String?,
        onSample: @escaping (SpeedTestSample) -> Void
    ) async throws -> NetworkQualityPhaseResult {
        let arguments = try Self.arguments(
            for: phase,
            route: route,
            wifiInterface: wifiInterface,
            durationPreset: durationPreset
        )
        let result = await runWithTrafficSampling(
            arguments: arguments,
            interfaceName: sampledInterface,
            phase: phase,
            onSample: onSample
        )
        guard result.status != -1 else {
            throw SpeedTestError.unavailable(result.error)
        }
        return try Self.parsePhase(
            output: result.output + "\n" + result.error,
            phase: phase
        )
    }

    static func arguments(
        for phase: SpeedTestPhase,
        route: SpeedTestRoute,
        wifiInterface: String?,
        durationPreset: SpeedTestDurationPreset
    ) throws -> [String] {
        var arguments = [
            "-c",
            "-M", String(durationPreset.phaseRuntimeSeconds),
            phase == .download ? "-u" : "-d"
        ]
        if route == .directWiFi {
            guard let wifiInterface, !wifiInterface.isEmpty else {
                throw SpeedTestError.unavailable("未找到当前 Wi-Fi 接口")
            }
            arguments.append(contentsOf: ["-I", wifiInterface])
        }
        return arguments
    }

    static func samplingInterface(
        for route: SpeedTestRoute,
        wifiInterface: String?
    ) async -> String? {
        if route == .directWiFi { return wifiInterface }
        let result = await ProcessRunner.run("/sbin/route", ["-n", "get", "default"])
        return DefaultRouteInterface.parse(output: result.output) ?? wifiInterface
    }

    private func runWithTrafficSampling(
        arguments: [String],
        interfaceName: String?,
        phase: SpeedTestPhase,
        onSample: @escaping (SpeedTestSample) -> Void
    ) async -> ProcessResult {
        await withTaskGroup(of: SpeedTestWorkerOutput.self, returning: ProcessResult.self) { group in
            group.addTask {
                .process(await ProcessRunner.run("/usr/bin/networkQuality", arguments))
            }

            if let interfaceName, !interfaceName.isEmpty {
                group.addTask {
                    await sampleTraffic(
                        interfaceName: interfaceName,
                        phase: phase,
                        onSample: onSample
                    )
                    return .samplerFinished
                }
            }

            while let output = await group.next() {
                if case .process(let result) = output {
                    group.cancelAll()
                    return result
                }
            }

            return ProcessResult(status: -1, output: "", error: "测速进程未返回结果")
        }
    }

    private func sampleTraffic(
        interfaceName: String,
        phase: SpeedTestPhase,
        onSample: @escaping (SpeedTestSample) -> Void
    ) async {
        guard var previous = await InterfaceTrafficSampler.read(interfaceName: interfaceName) else { return }
        let startedAt = Date()
        var previousDate = startedAt
        var smoother = TrafficRateSmoother(timeConstantSeconds: 1.1)

        while !Task.isCancelled {
            do {
                try await Task.sleep(for: .milliseconds(500))
            } catch {
                return
            }
            guard !Task.isCancelled,
                  let current = await InterfaceTrafficSampler.read(interfaceName: interfaceName) else {
                continue
            }

            let now = Date()
            let interval = now.timeIntervalSince(previousDate)
            guard interval > 0,
                  current.receivedBytes >= previous.receivedBytes,
                  current.sentBytes >= previous.sentBytes else {
                previous = current
                previousDate = now
                continue
            }

            let byteDelta: UInt64
            switch phase {
            case .download:
                byteDelta = current.receivedBytes - previous.receivedBytes
            case .upload:
                byteDelta = current.sentBytes - previous.sentBytes
            }
            let rate = Double(byteDelta) * 8 / interval / 1_000_000
            let smoothed = smoother.update(mbps: rate, intervalSeconds: interval)

            onSample(SpeedTestSample(
                phase: phase,
                elapsedSeconds: now.timeIntervalSince(startedAt),
                mbps: smoothed
            ))

            previous = current
            previousDate = now
        }
    }

    static func parse(
        output: String,
        route: SpeedTestRoute,
        durationPreset: SpeedTestDurationPreset = .standard,
        requestedInterface: String?
    ) throws -> SpeedTestReport {
        let decoded = try decode(output: output)
        let download = try phaseResult(from: decoded, phase: .download)
        let upload = try phaseResult(from: decoded, phase: .upload)
        return try combine(
            download: download,
            upload: upload,
            route: route,
            durationPreset: durationPreset,
            requestedInterface: requestedInterface
        )
    }

    static func parsePhase(
        output: String,
        phase: SpeedTestPhase
    ) throws -> NetworkQualityPhaseResult {
        try phaseResult(from: decode(output: output), phase: phase)
    }

    static func combine(
        download: NetworkQualityPhaseResult,
        upload: NetworkQualityPhaseResult,
        route: SpeedTestRoute,
        durationPreset: SpeedTestDurationPreset = .standard,
        requestedInterface: String?,
        sampledInterface: String? = nil
    ) throws -> SpeedTestReport {
        guard download.phase == .download, upload.phase == .upload else {
            throw SpeedTestError.incomplete
        }

        let latencyValues = [download.baseRTT, upload.baseRTT].compactMap { $0 }
        let idleLatency = latencyValues.isEmpty
            ? nil
            : latencyValues.reduce(0, +) / Double(latencyValues.count)
        let durationValues = [download.durationSeconds, upload.durationSeconds].compactMap { $0 }
        let totalDuration = durationValues.isEmpty ? nil : durationValues.reduce(0, +)

        return SpeedTestReport(
            route: route,
            durationPreset: durationPreset,
            requestedInterface: requestedInterface,
            sampledInterface: sampledInterface,
            measuredInterface: download.interfaceName ?? upload.interfaceName,
            endpoint: download.endpoint ?? upload.endpoint,
            downloadBitsPerSecond: download.throughputBitsPerSecond,
            uploadBitsPerSecond: upload.throughputBitsPerSecond,
            idleLatencyMs: idleLatency,
            downloadResponsivenessRPM: download.responsivenessRPM,
            uploadResponsivenessRPM: upload.responsivenessRPM,
            downloadedBytes: download.transferredBytes,
            uploadedBytes: upload.transferredBytes,
            durationSeconds: totalDuration,
            wasProxied: download.wasProxied || upload.wasProxied
        )
    }

    private static func decode(output: String) throws -> NetworkQualityOutput {
        guard let jsonStart = output.firstIndex(of: "{") else { throw SpeedTestError.invalidOutput }
        guard let jsonEnd = output.lastIndex(of: "}"), jsonEnd >= jsonStart else {
            throw SpeedTestError.invalidOutput
        }
        let json = String(output[jsonStart...jsonEnd])
        guard let data = json.data(using: .utf8) else { throw SpeedTestError.invalidOutput }
        let decoded: NetworkQualityOutput
        do {
            decoded = try JSONDecoder().decode(NetworkQualityOutput.self, from: data)
        } catch {
            throw SpeedTestError.invalidOutput
        }

        if let errorCode = decoded.errorCode {
            throw SpeedTestError.failed(domain: decoded.errorDomain ?? "networkQuality", code: errorCode)
        }
        return decoded
    }

    private static func phaseResult(
        from decoded: NetworkQualityOutput,
        phase: SpeedTestPhase
    ) throws -> NetworkQualityPhaseResult {
        let throughput: Double?
        let responsiveness: Double?
        let bytes: Int64?
        let duration: Double?
        switch phase {
        case .download:
            throughput = decoded.downloadThroughput
            responsiveness = decoded.downloadResponsiveness
            bytes = decoded.downloadedBytes
            duration = decoded.downloadPhaseDuration
        case .upload:
            throughput = decoded.uploadThroughput
            responsiveness = decoded.uploadResponsiveness
            bytes = decoded.uploadedBytes
            duration = decoded.uploadPhaseDuration
        }
        guard let throughput, throughput > 0 else { throw SpeedTestError.incomplete }

        return NetworkQualityPhaseResult(
            phase: phase,
            throughputBitsPerSecond: throughput,
            baseRTT: decoded.baseRTT,
            responsivenessRPM: responsiveness.flatMap { $0 > 0 ? $0 : nil },
            transferredBytes: bytes,
            durationSeconds: duration,
            interfaceName: decoded.interfaceName,
            endpoint: decoded.testEndpoint,
            wasProxied: (decoded.other?.proxyState?["proxied"] ?? 0) > 0
        )
    }
}

private enum SpeedTestWorkerOutput {
    case process(ProcessResult)
    case samplerFinished
}

struct NetworkQualityPhaseResult: Equatable {
    var phase: SpeedTestPhase
    var throughputBitsPerSecond: Double
    var baseRTT: Double?
    var responsivenessRPM: Double?
    var transferredBytes: Int64?
    var durationSeconds: Double?
    var interfaceName: String?
    var endpoint: String?
    var wasProxied: Bool
}

struct TrafficRateSmoother {
    private let timeConstantSeconds: Double
    private var previousMbps: Double?

    init(timeConstantSeconds: Double = 1.1) {
        self.timeConstantSeconds = max(0.1, timeConstantSeconds)
    }

    mutating func update(
        mbps: Double,
        intervalSeconds: Double
    ) -> Double {
        let interval = min(max(intervalSeconds, 0.05), 2)
        let alpha = 1 - exp(-interval / timeConstantSeconds)
        let value = blend(previous: previousMbps, current: mbps, alpha: alpha)
        previousMbps = value
        return value
    }

    private func blend(previous: Double?, current: Double, alpha: Double) -> Double {
        let sanitized = current.isFinite ? max(0, current) : 0
        guard let previous else { return sanitized }
        return previous + alpha * (sanitized - previous)
    }
}

struct InterfaceByteCounters: Equatable {
    var receivedBytes: UInt64
    var sentBytes: UInt64
}

enum InterfaceTrafficSampler {
    static func read(interfaceName: String) async -> InterfaceByteCounters? {
        let result = await ProcessRunner.run("/usr/sbin/netstat", ["-bI", interfaceName])
        return parse(output: result.output, interfaceName: interfaceName)
    }

    static func parse(output: String, interfaceName: String) -> InterfaceByteCounters? {
        for line in output.split(separator: "\n") {
            let fields = line.split(whereSeparator: \.isWhitespace)
            guard fields.count >= 10,
                  fields[0] == Substring(interfaceName),
                  fields[2].hasPrefix("<Link#"),
                  let received = UInt64(fields[6]),
                  let sent = UInt64(fields[9]) else {
                continue
            }
            return InterfaceByteCounters(receivedBytes: received, sentBytes: sent)
        }
        return nil
    }
}

enum DefaultRouteInterface {
    static func parse(output: String) -> String? {
        output.split(separator: "\n")
            .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
            .first(where: { $0.hasPrefix("interface:") })?
            .split(separator: ":", maxSplits: 1)
            .last
            .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
            .flatMap { $0.isEmpty ? nil : $0 }
    }
}

private struct NetworkQualityOutput: Decodable {
    var baseRTT: Double?
    var downloadThroughput: Double?
    var uploadThroughput: Double?
    var downloadResponsiveness: Double?
    var uploadResponsiveness: Double?
    var downloadedBytes: Int64?
    var uploadedBytes: Int64?
    var downloadPhaseDuration: Double?
    var uploadPhaseDuration: Double?
    var interfaceName: String?
    var testEndpoint: String?
    var errorCode: Int?
    var errorDomain: String?
    var other: NetworkQualityOther?

    enum CodingKeys: String, CodingKey {
        case baseRTT = "base_rtt"
        case downloadThroughput = "dl_throughput"
        case uploadThroughput = "ul_throughput"
        case downloadResponsiveness = "dl_responsiveness"
        case uploadResponsiveness = "ul_responsiveness"
        case downloadedBytes = "dl_bytes_transferred"
        case uploadedBytes = "ul_bytes_transferred"
        case downloadPhaseDuration = "dl_phase_duration"
        case uploadPhaseDuration = "ul_phase_duration"
        case interfaceName = "interface_name"
        case testEndpoint = "test_endpoint"
        case errorCode = "error_code"
        case errorDomain = "error_domain"
        case other
    }
}

private struct NetworkQualityOther: Decodable {
    var proxyState: [String: Int]?

    enum CodingKeys: String, CodingKey {
        case proxyState = "proxy_state"
    }
}
