import Foundation

enum HealthGrade: String, Codable, CaseIterable {
    case good
    case warning
    case critical
    case unavailable

    var label: String {
        switch self {
        case .good: "正常"
        case .warning: "注意"
        case .critical: "严重"
        case .unavailable: "未检测"
        }
    }

    var systemImage: String {
        switch self {
        case .good: "checkmark.circle.fill"
        case .warning: "exclamationmark.triangle.fill"
        case .critical: "xmark.octagon.fill"
        case .unavailable: "questionmark.circle"
        }
    }
}

enum WiFiBand: String, Codable, CaseIterable, Identifiable {
    case band2 = "2.4 GHz"
    case band5 = "5 GHz"
    case band6 = "6 GHz"
    case unknown = "未知"

    var id: String { rawValue }
}

struct WiFiSnapshot: Codable, Identifiable, Equatable {
    var id = UUID()
    var timestamp = Date()
    var interfaceName: String?
    var ssid: String?
    var bssid: String?
    var band: WiFiBand = .unknown
    var channel: Int?
    var channelWidthMHz: Int?
    var rssi: Int?
    var noise: Int?
    var transmitRateMbps: Double?
    var ccaPercent: Double?

    var snr: Int? {
        guard let rssi, let noise, rssi != 0, noise != 0 else { return nil }
        return rssi - noise
    }

    var isConnected: Bool {
        channel != nil && rssi != nil
    }

    static let unavailable = WiFiSnapshot()
}

struct NearbyNetwork: Codable, Identifiable, Equatable {
    var id: String
    var ssid: String
    var bssid: String?
    var band: WiFiBand
    var channel: Int
    var channelWidthMHz: Int
    var rssi: Int
    var noise: Int

    var snr: Int { rssi - noise }
}

enum DiagnosticLayer: String, Codable, CaseIterable, Identifiable {
    case wireless = "无线空口"
    case localNetwork = "局域网"
    case internet = "宽带出口"
    case proxyVPN = "VPN / 代理"

    var id: String { rawValue }

    var systemImage: String {
        switch self {
        case .wireless: "wifi"
        case .localNetwork: "network"
        case .internet: "globe.asia.australia"
        case .proxyVPN: "shield.lefthalf.filled"
        }
    }
}

struct LayerResult: Codable, Identifiable, Equatable {
    var id: DiagnosticLayer { layer }
    var layer: DiagnosticLayer
    var grade: HealthGrade
    var conclusion: String
    var evidence: [String]
    var metrics: [DiagnosticMetric]
    var action: String
}

struct DiagnosticMetric: Codable, Identifiable, Equatable {
    var id: String
    var title: String
    var value: String
    var grade: HealthGrade
    var statusLabel: String
    var interpretation: String
    var impact: String
    var standard: String

    init(
        id: String,
        title: String,
        value: String,
        assessment: MetricAssessment,
        impact: String
    ) {
        self.id = id
        self.title = title
        self.value = value
        self.grade = assessment.grade
        self.statusLabel = assessment.statusLabel
        self.interpretation = assessment.interpretation
        self.impact = impact
        self.standard = assessment.standard
    }

    var assessment: MetricAssessment {
        MetricAssessment(
            grade: grade,
            statusLabel: statusLabel,
            interpretation: interpretation,
            standard: standard
        )
    }
}

struct PingStatistics: Codable, Equatable {
    var host: String
    var sent: Int
    var received: Int
    var packetLossPercent: Double
    var minimumMs: Double?
    var averageMs: Double?
    var maximumMs: Double?
    var jitterMs: Double?
}

struct EndpointTiming: Codable, Equatable {
    var succeeded: Bool
    var milliseconds: Double?
    var detail: String
}

struct DiagnosticReport: Codable, Identifiable, Equatable {
    var id = UUID()
    var startedAt: Date
    var completedAt: Date
    var gateway: String?
    var interfaceName: String?
    var baselineDescription: String
    var wirelessSamples: [WiFiSnapshot]
    var gatewayPing: PingStatistics?
    var externalPing: PingStatistics?
    var dns: EndpointTiming
    var https: EndpointTiming
    var layers: [LayerResult]

    var overallGrade: HealthGrade {
        if layers.contains(where: { $0.grade == .critical }) { return .critical }
        if layers.contains(where: { $0.grade == .warning }) { return .warning }
        if layers.contains(where: { $0.grade == .unavailable }) { return .unavailable }
        return .good
    }

    var overallStatusLabel: String {
        if overallGrade == .unavailable, layers.contains(where: { $0.grade == .good }) {
            return "部分完成"
        }
        return overallGrade.label
    }

    var hasUnavailableLayer: Bool {
        layers.contains(where: { $0.grade == .unavailable })
    }
}

enum HistoryMarker: String, Codable, CaseIterable, Identifiable {
    case none = "未标记"
    case before = "变更前"
    case after = "变更后"

    var id: String { rawValue }
}

enum HistoryGradeScope: String, Codable, Equatable {
    case wireless = "无线采样"
    case fullDiagnosis = "四层体检"
}

struct HistorySample: Codable, Identifiable, Equatable {
    var id = UUID()
    var timestamp: Date
    var marker: HistoryMarker = .none
    var snapshot: WiFiSnapshot
    var gatewayAverageMs: Double?
    var gatewayJitterMs: Double?
    var gatewayLossPercent: Double?
    var internetAverageMs: Double?
    var overallGrade: HealthGrade
    var overallStatusLabel: String?
    var gradeScope: HistoryGradeScope?
}

enum SpeedTestRoute: String, Codable, CaseIterable, Identifiable {
    case currentPath = "当前实际链路"
    case directWiFi = "直连 Wi-Fi 基线"

    var id: String { rawValue }

    var detail: String {
        switch self {
        case .currentPath:
            "按系统当前路径测速，包含正在使用的 VPN 或代理，最接近日常 App 的实际体验。"
        case .directWiFi:
            "绑定当前 Wi-Fi 接口测速，用于对比 VPN/代理影响；部分网络可能不允许绕开当前路径。"
        }
    }
}

enum SpeedTestPhase: String, Codable, CaseIterable, Identifiable {
    case download = "下载"
    case upload = "上传"

    var id: String { rawValue }

    var systemImage: String {
        switch self {
        case .download: "arrow.down.circle.fill"
        case .upload: "arrow.up.circle.fill"
        }
    }
}

enum SpeedTestDurationPreset: String, Codable, CaseIterable, Identifiable {
    case standard
    case stable

    var id: String { rawValue }

    var displayName: String {
        switch self {
        case .standard: "标准模式"
        case .stable: "稳定模式"
        }
    }

    var segmentTitle: String {
        switch self {
        case .standard: "标准 · 最多 40 秒"
        case .stable: "稳定 · 最多 60 秒"
        }
    }

    var phaseRuntimeSeconds: Int {
        switch self {
        case .standard: 20
        case .stable: 30
        }
    }

    var maximumTotalSeconds: Int { phaseRuntimeSeconds * SpeedTestPhase.allCases.count }

    var detail: String {
        switch self {
        case .standard:
            "下载、上传每个方向最多 20 秒，给热点、VPN 和慢爬升链路更多收敛时间；系统取得稳定估计后可能提前结束。"
        case .stable:
            "下载、上传每个方向最多 30 秒，适合高波动链路或变更前后严谨回测；系统取得稳定估计后可能提前结束。"
        }
    }

    var trafficWarning: String {
        switch self {
        case .standard:
            "测速会持续占用真实带宽；高速连接可能消耗数百 MB，千兆链路极端情况下可达到数 GB。"
        case .stable:
            "稳定模式会占用更长时间和更多真实流量；使用计费手机热点时请谨慎，千兆链路极端情况下可达到数 GB。"
        }
    }
}

struct SpeedTestReport: Codable, Identifiable, Equatable {
    var id = UUID()
    var completedAt = Date()
    var route: SpeedTestRoute
    var durationPreset: SpeedTestDurationPreset
    var requestedInterface: String?
    var sampledInterface: String?
    var measuredInterface: String?
    var endpoint: String?
    var downloadBitsPerSecond: Double
    var uploadBitsPerSecond: Double
    var idleLatencyMs: Double?
    var downloadResponsivenessRPM: Double?
    var uploadResponsivenessRPM: Double?
    var downloadedBytes: Int64?
    var uploadedBytes: Int64?
    var durationSeconds: Double?
    var wasProxied: Bool

    var downloadMbps: Double { downloadBitsPerSecond / 1_000_000 }
    var uploadMbps: Double { uploadBitsPerSecond / 1_000_000 }
    var downloadMegabytesPerSecond: Double { downloadBitsPerSecond / 8_000_000 }
    var uploadMegabytesPerSecond: Double { uploadBitsPerSecond / 8_000_000 }
    var transferredMegabytes: Double {
        Double((downloadedBytes ?? 0) + (uploadedBytes ?? 0)) / 1_000_000
    }

    func downloadSeconds(forGigabytes gigabytes: Double) -> Double? {
        transferSeconds(gigabytes: gigabytes, bitsPerSecond: downloadBitsPerSecond)
    }

    func uploadSeconds(forGigabytes gigabytes: Double) -> Double? {
        transferSeconds(gigabytes: gigabytes, bitsPerSecond: uploadBitsPerSecond)
    }

    private func transferSeconds(gigabytes: Double, bitsPerSecond: Double) -> Double? {
        guard bitsPerSecond > 0 else { return nil }
        return gigabytes * 8_000_000_000 / bitsPerSecond
    }
}

struct SpeedTestSample: Identifiable, Equatable {
    var id = UUID()
    var phase: SpeedTestPhase
    var elapsedSeconds: Double
    var mbps: Double
}

enum AppSection: String, CaseIterable, Identifiable {
    case overview = "概览"
    case diagnosis = "60 秒体检"
    case speedTest = "网速测速"
    case radar = "信道雷达"
    case history = "历史趋势"
    case router = "路由管理"

    var id: String { rawValue }

    var systemImage: String {
        switch self {
        case .overview: "gauge.with.dots.needle.50percent"
        case .diagnosis: "stethoscope"
        case .speedTest: "speedometer"
        case .radar: "dot.radiowaves.left.and.right"
        case .history: "chart.xyaxis.line"
        case .router: "wifi.router"
        }
    }
}
