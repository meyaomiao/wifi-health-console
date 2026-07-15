import Foundation

struct MetricAssessment: Equatable {
    let grade: HealthGrade
    let statusLabel: String
    let interpretation: String
    let standard: String

    var badgeSystemImage: String {
        switch statusLabel {
        case "参考": "info.circle"
        case "部分完成": "exclamationmark.circle"
        case "未检测": "minus.circle"
        default: grade.systemImage
        }
    }
}

enum HealthStandards {
    static let rssiStandard = "> -55 dBm 优秀；-55～-67 dBm 正常；-68～-75 dBm 注意；< -75 dBm 严重。"
    static let snrStandard = "≥ 40 dB 优秀；30～39 dB 正常；20～29 dB 注意；< 20 dB 严重。"
    static let ccaStandard = "≤ 50% 正常；> 50%～80% 注意；> 80% 严重。需从路由器侧读取。"
    static let gatewayLatencyStandard = "≤ 10 ms 优秀；> 10～30 ms 正常；> 30～100 ms 注意；> 100 ms 严重。"
    static let gatewayJitterStandard = "≤ 10 ms 正常；> 10～30 ms 注意；> 30 ms 严重。"
    static let gatewayLossStandard = "≤ 1% 正常；> 1%～5% 注意；> 5% 严重。"
    static let internetLatencyStandard = "≤ 80 ms 正常；> 80～150 ms 注意；> 150 ms 严重。"
    static let dnsStandard = "≤ 100 ms 正常；> 100～300 ms 注意；> 300 ms 或解析失败为严重。"
    static let httpsStandard = "≤ 800 ms 正常；> 800～2000 ms 注意；> 2000 ms 或请求失败为严重。"
    static let publicICMPLossStandard = "≤ 10% 为正常参考；> 10% 需要注意。公网可能降低 ICMP 优先级，因此不单独判为严重。"
    static let downloadStandard = "≥ 100 Mbps 优秀；25～99.9 Mbps 正常；10～24.9 Mbps 注意；< 10 Mbps 严重。"
    static let uploadStandard = "≥ 20 Mbps 优秀；10～19.9 Mbps 正常；5～9.9 Mbps 注意；< 5 Mbps 严重。"
    static let idleLatencyStandard = "≤ 40 ms 正常；> 40～100 ms 注意；> 100 ms 严重。"
    static let responsivenessStandard = "≥ 600 RPM 优秀；200～599 RPM 注意；< 200 RPM 严重。RPM 越高越好。"
    static let channelWidthStandard = "2.4 GHz 通常优先 20 MHz；5/6 GHz 的 80 MHz 较均衡；160 MHz 仅在频谱干净时更合适。频宽本身不单独判故障。"

    static func rssi(_ value: Int?) -> MetricAssessment {
        guard let value else {
            return make(.unavailable, "未检测", "未取得 RSSI。", rssiStandard)
        }
        if value > -55 {
            return make(.good, "优秀", "当前 \(value) dBm，信号优秀，覆盖不是首要问题。", rssiStandard)
        }
        if value >= -67 {
            return make(.good, "正常", "当前 \(value) dBm，处于正常使用范围。", rssiStandard)
        }
        if value >= -75 {
            return make(.warning, "注意", "当前 \(value) dBm，信号偏弱，速率和稳定性余量已经下降。", rssiStandard)
        }
        return make(.critical, "严重", "当前 \(value) dBm，信号很弱，容易降速、重传或断续。", rssiStandard)
    }

    static func snr(_ value: Int?) -> MetricAssessment {
        guard let value else {
            return make(.unavailable, "未检测", "未取得 SNR。", snrStandard)
        }
        if value >= 40 {
            return make(.good, "优秀", "当前 \(value) dB，信号明显高于噪声，抗干扰余量充足。", snrStandard)
        }
        if value >= 30 {
            return make(.good, "正常", "当前 \(value) dB，信噪比处于正常范围。", snrStandard)
        }
        if value >= 20 {
            return make(.warning, "注意", "当前 \(value) dB，抗干扰余量不足，繁忙时更容易降速或重传。", snrStandard)
        }
        return make(.critical, "严重", "当前 \(value) dB，信号与噪声过于接近，连接容易不稳定。", snrStandard)
    }

    static func cca(_ value: Double?) -> MetricAssessment {
        guard let value else {
            return make(.unavailable, "未检测", "macOS 公共 API 不提供 CCA，本次未用估算值参与判定。", ccaStandard)
        }
        if value > 80 {
            return make(.critical, "严重", String(format: "当前 %.1f%%，空口绝大多数时间繁忙，属于严重拥塞。", value), ccaStandard)
        }
        if value > 50 {
            return make(.warning, "注意", String(format: "当前 %.1f%%，空口竞争明显，可能增加等待和重传。", value), ccaStandard)
        }
        return make(.good, "正常", String(format: "当前 %.1f%%，空口占用在正常范围。", value), ccaStandard)
    }

    static func gatewayLatency(_ value: Double?) -> MetricAssessment {
        latency(
            value,
            excellentUpper: 10,
            normalUpper: 30,
            warningUpper: 100,
            subject: "Mac 到路由器",
            standard: gatewayLatencyStandard
        )
    }

    static func gatewayJitter(_ value: Double?) -> MetricAssessment {
        guard let value else {
            return make(.unavailable, "未检测", "未取得网关抖动。", gatewayJitterStandard)
        }
        if value <= 10 {
            return make(.good, "正常", String(format: "当前 %.1f ms，局域网延迟波动正常。", value), gatewayJitterStandard)
        }
        if value <= 30 {
            return make(.warning, "注意", String(format: "当前 %.1f ms，波动偏大，实时业务可能受影响。", value), gatewayJitterStandard)
        }
        return make(.critical, "严重", String(format: "当前 %.1f ms，波动严重，通话、游戏和远程控制容易卡顿。", value), gatewayJitterStandard)
    }

    static func gatewayLoss(_ value: Double?) -> MetricAssessment {
        guard let value else {
            return make(.unavailable, "未检测", "未取得网关丢包率。", gatewayLossStandard)
        }
        if value <= 1 {
            return make(.good, "正常", String(format: "当前 %.1f%%，家庭内部链路处于正常范围。", value), gatewayLossStandard)
        }
        if value <= 5 {
            return make(.warning, "注意", String(format: "当前 %.1f%%，家庭内部链路已出现异常丢包。", value), gatewayLossStandard)
        }
        return make(.critical, "严重", String(format: "当前 %.1f%%，家庭内部链路丢包严重。", value), gatewayLossStandard)
    }

    static func internetLatency(_ value: Double?) -> MetricAssessment {
        latency(
            value,
            excellentUpper: nil,
            normalUpper: 80,
            warningUpper: 150,
            subject: "公网响应",
            standard: internetLatencyStandard
        )
    }

    static func publicICMPLoss(_ value: Double?) -> MetricAssessment {
        guard let value else {
            return make(.unavailable, "未检测", "未取得公网 ICMP 丢包率。", publicICMPLossStandard)
        }
        if value <= 10 {
            return make(.good, "正常", String(format: "当前 %.1f%%，处于参考范围；仍需与 DNS、HTTPS 一起判断。", value), publicICMPLossStandard)
        }
        return make(.warning, "注意", String(format: "当前 %.1f%%，需要关注，但公网目标可能降低 ICMP 优先级，不能据此单独判定断网。", value), publicICMPLossStandard)
    }

    static func dns(_ timing: EndpointTiming) -> MetricAssessment {
        guard timing.succeeded else {
            return make(.critical, "严重", "DNS 解析失败：\(timing.detail)", dnsStandard)
        }
        return latency(
            timing.milliseconds,
            excellentUpper: nil,
            normalUpper: 100,
            warningUpper: 300,
            subject: "DNS 解析",
            standard: dnsStandard
        )
    }

    static func https(_ timing: EndpointTiming) -> MetricAssessment {
        guard timing.succeeded else {
            return make(.critical, "严重", "HTTPS 请求失败：\(timing.detail)", httpsStandard)
        }
        return latency(
            timing.milliseconds,
            excellentUpper: nil,
            normalUpper: 800,
            warningUpper: 2_000,
            subject: "HTTPS 建连与响应",
            standard: httpsStandard
        )
    }

    static func download(_ mbps: Double) -> MetricAssessment {
        if mbps >= 100 {
            return make(.good, "优秀", "当前下载速度适合多设备 4K、云盘和大型游戏下载。", downloadStandard)
        }
        if mbps >= 25 {
            return make(.good, "正常", "当前下载速度可满足网页、高清视频和单路 4K。", downloadStandard)
        }
        if mbps >= 10 {
            return make(.warning, "注意", "当前下载速度偏低，大型下载和高码率场景会明显等待。", downloadStandard)
        }
        return make(.critical, "严重", "当前下载速度很低，网页、高清视频和大文件下载都可能明显缓慢。", downloadStandard)
    }

    static func upload(_ mbps: Double) -> MetricAssessment {
        if mbps >= 20 {
            return make(.good, "优秀", "当前上传速度适合视频会议、直播和云盘同步。", uploadStandard)
        }
        if mbps >= 10 {
            return make(.good, "正常", "当前上传速度可满足视频会议和日常云同步。", uploadStandard)
        }
        if mbps >= 5 {
            return make(.warning, "注意", "当前上传速度偏低，视频会议余量有限，大文件上传会较慢。", uploadStandard)
        }
        return make(.critical, "严重", "当前上传速度很低，视频会议、云同步和发送大文件容易受影响。", uploadStandard)
    }

    static func idleLatency(_ value: Double?) -> MetricAssessment {
        latency(
            value,
            excellentUpper: nil,
            normalUpper: 40,
            warningUpper: 100,
            subject: "空闲延迟",
            standard: idleLatencyStandard
        )
    }

    static func responsiveness(_ value: Double?, subject: String) -> MetricAssessment {
        guard let value else {
            return make(.unavailable, "未检测", "未取得\(subject)响应能力。", responsivenessStandard)
        }
        if value >= 600 {
            return make(.good, "优秀", String(format: "%@ %.0f RPM，负载下仍能保持较快响应。", subject, value), responsivenessStandard)
        }
        if value >= 200 {
            return make(.warning, "注意", String(format: "%@ %.0f RPM，负载下响应一般，可能出现排队延迟。", subject, value), responsivenessStandard)
        }
        return make(.critical, "严重", String(format: "%@ %.0f RPM，负载下响应很差，容易出现明显卡顿。", subject, value), responsivenessStandard)
    }

    static func channelWidth(_ width: Int?, band: WiFiBand) -> MetricAssessment {
        guard let width, width > 0 else {
            return make(.unavailable, "未检测", "未取得信道频宽。", channelWidthStandard)
        }
        if band == .band2, width > 20 {
            return make(.warning, "注意", "当前 \(width) MHz；2.4 GHz 上超过 20 MHz 会覆盖更多相邻信道，通常更容易产生重叠。", channelWidthStandard)
        }
        if width >= 160 {
            return make(.unavailable, "参考", "当前 \(width) MHz，峰值高但占用范围很宽；是否需要降到 80 MHz 要结合信道重叠、CCA 和重传。", channelWidthStandard)
        }
        if width == 80, band == .band5 || band == .band6 {
            return make(.unavailable, "参考", "当前 80 MHz，在 5/6 GHz 上通常兼顾速度与频谱占用。", channelWidthStandard)
        }
        return make(.unavailable, "参考", "当前 \(width) MHz；频宽需要结合频段、附近网络和实际吞吐判断。", channelWidthStandard)
    }

    static func reference(available: Bool, interpretation: String, standard: String) -> MetricAssessment {
        guard available else {
            return make(.unavailable, "未检测", interpretation, standard)
        }
        return make(.unavailable, "参考", interpretation, standard)
    }

    static func pathState(active: Bool, detail: String, standard: String) -> MetricAssessment {
        active
            ? make(.warning, "注意", detail, standard)
            : make(.good, "正常", detail, standard)
    }

    static func worst(_ grades: HealthGrade...) -> HealthGrade {
        worst(grades)
    }

    static func worst(_ grades: [HealthGrade]) -> HealthGrade {
        if grades.contains(.critical) { return .critical }
        if grades.contains(.warning) { return .warning }
        if grades.contains(.good) { return .good }
        return .unavailable
    }

    static func worst(_ assessments: [MetricAssessment]) -> HealthGrade {
        worst(assessments.map(\.grade))
    }

    static func summaryStatusLabel(for assessments: [MetricAssessment]) -> String {
        let decisive = assessments.filter { $0.statusLabel != "参考" && $0.grade != .unavailable }
        switch worst(decisive) {
        case .good:
            return decisive.isEmpty || decisive.contains(where: { $0.statusLabel != "优秀" }) ? "正常" : "优秀"
        case .warning:
            return "注意"
        case .critical:
            return "严重"
        case .unavailable:
            return "未检测"
        }
    }

    static func isStatusLabelCompatible(_ label: String, with grade: HealthGrade) -> Bool {
        switch grade {
        case .good: ["优秀", "正常"].contains(label)
        case .warning: label == "注意"
        case .critical: label == "严重"
        case .unavailable: ["未检测", "参考", "部分完成"].contains(label)
        }
    }

    private static func latency(
        _ value: Double?,
        excellentUpper: Double?,
        normalUpper: Double,
        warningUpper: Double,
        subject: String,
        standard: String
    ) -> MetricAssessment {
        guard let value else {
            return make(.unavailable, "未检测", "未取得\(subject)时间。", standard)
        }
        if let excellentUpper, value <= excellentUpper {
            return make(.good, "优秀", String(format: "%@ %.1f ms，响应优秀。", subject, value), standard)
        }
        if value <= normalUpper {
            return make(.good, "正常", String(format: "%@ %.1f ms，处于正常范围。", subject, value), standard)
        }
        if value <= warningUpper {
            return make(.warning, "注意", String(format: "%@ %.1f ms，已经偏高。", subject, value), standard)
        }
        return make(.critical, "严重", String(format: "%@ %.1f ms，明显过高。", subject, value), standard)
    }

    private static func make(
        _ grade: HealthGrade,
        _ statusLabel: String,
        _ interpretation: String,
        _ standard: String
    ) -> MetricAssessment {
        precondition(isStatusLabelCompatible(statusLabel, with: grade), "状态文字与健康等级不一致")
        return MetricAssessment(
            grade: grade,
            statusLabel: statusLabel,
            interpretation: interpretation,
            standard: standard
        )
    }
}
