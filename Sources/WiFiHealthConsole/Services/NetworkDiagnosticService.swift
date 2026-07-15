import Foundation

struct NetworkContext {
    var gateway: String?
    var interfaceName: String?
    var gatewaySource: String
    var proxyEnabled: Bool
    var proxyDescription: String
    var vpnLikelyActive: Bool
    var vpnDescription: String
}

struct NetworkDiagnosticService {
    private let wifi = WiFiService()

    func discoverContext() async -> NetworkContext {
        let wifiInterface = wifi.currentSnapshot().interfaceName
        async let routeResult = ProcessRunner.run("/sbin/route", ["-n", "get", "default"])
        async let proxyResult = ProcessRunner.run("/usr/sbin/scutil", ["--proxy"])
        async let vpnResult = ProcessRunner.run("/usr/sbin/scutil", ["--nc", "list"])
        let (route, proxy, vpn) = await (routeResult, proxyResult, vpnResult)

        let defaultGateway = value(after: "gateway:", in: route.output)
        let defaultInterface = value(after: "interface:", in: route.output)
        let interfaceName = wifiInterface ?? defaultInterface
        var gateway = defaultGateway
        var gatewaySource = "系统默认路由"

        if let interfaceName {
            let dhcp = await ProcessRunner.run("/usr/sbin/ipconfig", ["getoption", interfaceName, "router"])
            let dhcpGateway = dhcp.output.trimmingCharacters(in: .whitespacesAndNewlines)
            if dhcp.status == 0, !dhcpGateway.isEmpty {
                gateway = dhcpGateway.split(whereSeparator: \.isWhitespace).first.map(String.init)
                gatewaySource = "Wi-Fi DHCP"
            } else {
                let scopedRoute = await ProcessRunner.run(
                    "/sbin/route",
                    ["-n", "get", "-ifscope", interfaceName, "default"]
                )
                if let scopedGateway = value(after: "gateway:", in: scopedRoute.output) {
                    gateway = scopedGateway
                    gatewaySource = "Wi-Fi 接口路由"
                }
            }
        }

        let enabledKeys = ["HTTPEnable : 1", "HTTPSEnable : 1", "SOCKSEnable : 1", "ProxyAutoConfigEnable : 1"]
        let proxyEnabled = enabledKeys.contains(where: proxy.output.contains)
        let connectedVPNs = vpn.output.split(separator: "\n").filter { $0.contains("(Connected)") }
        let defaultIsTunnel = defaultInterface?.hasPrefix("utun") == true
        let vpnActive = defaultIsTunnel || !connectedVPNs.isEmpty

        return NetworkContext(
            gateway: gateway,
            interfaceName: interfaceName,
            gatewaySource: gatewaySource,
            proxyEnabled: proxyEnabled,
            proxyDescription: proxyEnabled ? "检测到系统代理配置；体检 HTTPS 已显式绕过系统代理" : "未检测到已启用的系统代理",
            vpnLikelyActive: vpnActive,
            vpnDescription: vpnActive ? "检测到已连接 VPN 或默认隧道接口" : "未检测到已连接的系统 VPN"
        )
    }

    func run(
        durationSeconds: Int = 60,
        progress: @escaping (Double, String) -> Void
    ) async -> DiagnosticReport {
        let startedAt = Date()
        progress(0.02, "识别默认网关与代理路径")
        let context = await discoverContext()

        async let gatewayPing = ping(
            host: context.gateway,
            count: max(4, durationSeconds / 2),
            interval: durationSeconds >= 30 ? 2 : 1
        )
        async let externalPing = ping(host: "1.1.1.1", count: 8, interval: 1)
        async let dns = measureDNS()
        async let https = measureHTTPSWithoutProxy()

        var samples: [WiFiSnapshot] = []
        let ticks = max(1, durationSeconds)
        for second in 0..<ticks {
            samples.append(wifi.currentSnapshot())
            progress(0.08 + (Double(second + 1) / Double(ticks)) * 0.78, "持续采样无线信号与网关稳定性")
            if second < ticks - 1 {
                try? await Task.sleep(for: .seconds(1))
            }
        }

        progress(0.9, "汇总四层证据")
        let gatewayStats = await gatewayPing
        let externalStats = await externalPing
        let dnsTiming = await dns
        let httpsTiming = await https
        let layers = makeLayers(
            samples: samples,
            gateway: gatewayStats,
            external: externalStats,
            dns: dnsTiming,
            https: httpsTiming,
            context: context
        )
        progress(1, "体检完成")

        return DiagnosticReport(
            startedAt: startedAt,
            completedAt: Date(),
            gateway: context.gateway,
            interfaceName: context.interfaceName,
            baselineDescription: "基线：Mac 当前 Wi-Fi；DNS 直连 1.1.1.1，HTTPS 禁用系统代理，ICMP 不经过 HTTP 代理。",
            wirelessSamples: samples,
            gatewayPing: gatewayStats,
            externalPing: externalStats,
            dns: dnsTiming,
            https: httpsTiming,
            layers: layers
        )
    }

    private func ping(host: String?, count: Int, interval: Int) async -> PingStatistics? {
        guard let host, !host.isEmpty else { return nil }
        let result = await ProcessRunner.run("/sbin/ping", [
            "-n", "-q", "-c", "\(count)", "-i", "\(interval)", "-W", "1000", host
        ])
        return parsePing(result.output + "\n" + result.error, host: host)
    }

    private func parsePing(_ output: String, host: String) -> PingStatistics? {
        let packetPattern = #"(\d+) packets transmitted, (\d+) packets received, ([\d.]+)% packet loss"#
        guard let packetMatch = output.firstMatch(pattern: packetPattern), packetMatch.count == 4 else { return nil }
        let sent = Int(packetMatch[1]) ?? 0
        let received = Int(packetMatch[2]) ?? 0
        let loss = Double(packetMatch[3]) ?? 100

        let timingPattern = #"(?:round-trip|rtt) min/avg/max/(?:stddev|mdev) = ([\d.]+)/([\d.]+)/([\d.]+)/([\d.]+) ms"#
        let timing = output.firstMatch(pattern: timingPattern)
        return PingStatistics(
            host: host,
            sent: sent,
            received: received,
            packetLossPercent: loss,
            minimumMs: timing.flatMap { Double($0[1]) },
            averageMs: timing.flatMap { Double($0[2]) },
            maximumMs: timing.flatMap { Double($0[3]) },
            jitterMs: timing.flatMap { Double($0[4]) }
        )
    }

    private func measureDNS() async -> EndpointTiming {
        var timings: [Double] = []
        var lastError = "未收到 DNS 响应"
        for _ in 0..<3 {
            let result = await ProcessRunner.run("/usr/bin/dig", ["+stats", "+time=2", "+tries=1", "@1.1.1.1", "www.apple.com", "A"])
            if let match = result.output.firstMatch(pattern: #"Query time: (\d+) msec"#), let milliseconds = Double(match[1]) {
                timings.append(milliseconds)
            } else if !result.error.isEmpty {
                lastError = result.error.trimmingCharacters(in: .whitespacesAndNewlines)
            }
        }
        guard !timings.isEmpty else { return EndpointTiming(succeeded: false, milliseconds: nil, detail: lastError) }
        return EndpointTiming(
            succeeded: true,
            milliseconds: timings.reduce(0, +) / Double(timings.count),
            detail: "直连 1.1.1.1，完成 \(timings.count)/3 次解析"
        )
    }

    private func measureHTTPSWithoutProxy() async -> EndpointTiming {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.connectionProxyDictionary = [:]
        configuration.timeoutIntervalForRequest = 8
        configuration.timeoutIntervalForResource = 10
        let session = URLSession(configuration: configuration)
        guard let url = URL(string: "https://www.apple.com/library/test/success.html") else {
            return EndpointTiming(succeeded: false, milliseconds: nil, detail: "测试 URL 无效")
        }

        let started = ContinuousClock.now
        do {
            let (_, response) = try await session.data(from: url)
            let elapsed = started.duration(to: .now).milliseconds
            let status = (response as? HTTPURLResponse)?.statusCode ?? 0
            return EndpointTiming(
                succeeded: (200..<400).contains(status),
                milliseconds: elapsed,
                detail: "无系统代理 HTTPS，HTTP \(status)"
            )
        } catch {
            return EndpointTiming(succeeded: false, milliseconds: nil, detail: error.localizedDescription)
        }
    }

    private func makeLayers(
        samples: [WiFiSnapshot],
        gateway: PingStatistics?,
        external: PingStatistics?,
        dns: EndpointTiming,
        https: EndpointTiming,
        context: NetworkContext
    ) -> [LayerResult] {
        let validRSSI = samples.compactMap(\.rssi)
        let validSNR = samples.compactMap(\.snr)
        let averageRSSI = validRSSI.isEmpty ? nil : validRSSI.reduce(0, +) / validRSSI.count
        let averageSNR = validSNR.isEmpty ? nil : validSNR.reduce(0, +) / validSNR.count
        let lastSnapshot = samples.last

        let rssiAssessment = HealthStandards.rssi(averageRSSI)
        let snrAssessment = HealthStandards.snr(averageSNR)
        let widthAssessment = HealthStandards.channelWidth(
            lastSnapshot?.channelWidthMHz,
            band: lastSnapshot?.band ?? .unknown
        )
        let ccaAssessment = HealthStandards.cca(lastSnapshot?.ccaPercent)
        let channelAssessment = HealthStandards.reference(
            available: lastSnapshot?.channel != nil,
            interpretation: lastSnapshot?.channel.map {
                "当前工作在信道 \($0)；信道本身没有绝对好坏，需要结合附近网络重叠、CCA 和重传判断。"
            } ?? "未取得主信道。",
            standard: "没有固定的最佳信道；以可见重叠少、路由器 CCA 低、重传少为好。"
        )

        let wirelessMetrics = [
            DiagnosticMetric(
                id: "wireless-rssi",
                title: "平均 RSSI",
                value: DisplayFormat.integer(averageRSSI, suffix: "dBm"),
                assessment: rssiAssessment,
                impact: "代表 Mac 收到的无线信号强度。信号过弱会降低调制速率，并增加重传、卡顿和漫游不稳定。"
            ),
            DiagnosticMetric(
                id: "wireless-snr",
                title: "平均 SNR",
                value: DisplayFormat.integer(averageSNR, suffix: "dB"),
                assessment: snrAssessment,
                impact: "表示有效信号比背景噪声高出多少。SNR 越高，设备越容易维持高阶调制和稳定传输。"
            ),
            DiagnosticMetric(
                id: "wireless-channel",
                title: "主信道",
                value: lastSnapshot?.channel.map { "信道 \($0)" } ?? "--",
                assessment: channelAssessment,
                impact: "决定无线传输所在的频谱位置；同信道和相邻信道网络过多时，会竞争空口时间。"
            ),
            DiagnosticMetric(
                id: "wireless-width",
                title: "信道频宽",
                value: DisplayFormat.integer(lastSnapshot?.channelWidthMHz, suffix: "MHz"),
                assessment: widthAssessment,
                impact: "频宽越大，理论峰值越高，但占用频谱也越宽；拥塞环境中可能遇到更多竞争和重传。"
            ),
            DiagnosticMetric(
                id: "wireless-cca",
                title: "CCA 空口占用",
                value: DisplayFormat.decimal(lastSnapshot?.ccaPercent, suffix: "%", digits: 1),
                assessment: ccaAssessment,
                impact: "表示无线电认为信道正忙的时间比例。CCA 高时，即使信号很强，也要频繁等待空口。"
            )
        ]

        var wirelessDecisive = [rssiAssessment, snrAssessment, ccaAssessment]
        if widthAssessment.grade == .warning || widthAssessment.grade == .critical {
            wirelessDecisive.append(widthAssessment)
        }
        let wirelessGrade = HealthStandards.worst(wirelessDecisive)
        let wirelessEvidence = [
            averageRSSI.map { "平均 RSSI \($0) dBm" },
            averageSNR.map { "平均 SNR \($0) dB" },
            lastSnapshot?.channel.map {
                "信道 \($0) / \(lastSnapshot?.channelWidthMHz.map { "\($0) MHz" } ?? "频宽未知")"
            },
            lastSnapshot?.ccaPercent.map {
                "CCA \(DisplayFormat.decimal($0, suffix: "%", digits: 1))"
            } ?? "CCA：macOS 公共 API 不提供，未参与判定"
        ].compactMap { $0 }

        let wirelessResult = makeLayerResult(
            layer: .wireless,
            grade: wirelessGrade,
            metrics: wirelessMetrics,
            evidence: wirelessEvidence,
            normalConclusion: "已采集的无线空口指标正常，暂未看到明显覆盖问题",
            unavailableConclusion: "无线空口未检测到足够证据",
            goodAction: "若仍感觉慢，继续查看局域网、宽带出口和测速结果。",
            warningAction: "按橙色指标处理：改善覆盖、检查 2.4 GHz 频宽，或结合信道雷达与路由器 CCA 回测。",
            criticalAction: "先处理红色指标，再在相同位置重新体检；严重信号或 CCA 问题会直接限制稳定性。",
            unavailableAction: "确认 Wi-Fi 已连接；SSID 显示和附近网络扫描还需要定位权限。"
        )

        let gatewayLatencyAssessment = HealthStandards.gatewayLatency(gateway?.averageMs)
        let gatewayJitterAssessment = HealthStandards.gatewayJitter(gateway?.jitterMs)
        let gatewayLossAssessment = HealthStandards.gatewayLoss(gateway?.packetLossPercent)
        let gatewayMetrics = [
            DiagnosticMetric(
                id: "gateway-latency",
                title: "网关延迟",
                value: DisplayFormat.decimal(gateway?.averageMs, suffix: "ms"),
                assessment: gatewayLatencyAssessment,
                impact: "只测 Mac 到家庭路由器，不经过宽带。这里高，通常说明无线竞争、覆盖、节点回程或局域网负载有问题。"
            ),
            DiagnosticMetric(
                id: "gateway-jitter",
                title: "网关抖动",
                value: DisplayFormat.decimal(gateway?.jitterMs, suffix: "ms"),
                assessment: gatewayJitterAssessment,
                impact: "表示每次延迟的波动。抖动高会让语音、视频会议和游戏出现忽快忽慢。"
            ),
            DiagnosticMetric(
                id: "gateway-loss",
                title: "网关丢包",
                value: DisplayFormat.decimal(gateway?.packetLossPercent, suffix: "%"),
                assessment: gatewayLossAssessment,
                impact: "网关丢包发生在家庭内部链路，会触发重传并直接造成网页停顿、视频降码率或通话断续。"
            )
        ]
        let gatewayGrade = HealthStandards.worst([
            gatewayLatencyAssessment,
            gatewayJitterAssessment,
            gatewayLossAssessment
        ])
        let gatewayEvidence = [
            gateway.map { "网关 \($0.host)，平均 \(DisplayFormat.decimal($0.averageMs, suffix: "ms"))" },
            gateway.map {
                "丢包 \(DisplayFormat.decimal($0.packetLossPercent, suffix: "%"))，抖动 \(DisplayFormat.decimal($0.jitterMs, suffix: "ms"))"
            }
        ].compactMap { $0 }

        let localResult = makeLayerResult(
            layer: .localNetwork,
            grade: gatewayGrade,
            metrics: gatewayMetrics,
            evidence: gatewayEvidence,
            normalConclusion: "Mac 到路由器的已测指标正常",
            unavailableConclusion: "局域网未检测到默认网关或有效响应",
            goodAction: "局域网不是当前首要瓶颈。",
            warningAction: "优先排查无线覆盖、同频竞争、Mesh 节点回程和局域网高负载。",
            criticalAction: "先解决网关高延迟、严重抖动或丢包；在局域网恢复前，公网测速没有定位意义。",
            unavailableAction: "确认当前网络存在可达的默认网关后重新体检。"
        )

        let internetLatencyAssessment = HealthStandards.internetLatency(external?.averageMs)
        let publicLossAssessment = HealthStandards.publicICMPLoss(external?.packetLossPercent)
        let dnsAssessment = HealthStandards.dns(dns)
        let httpsAssessment = HealthStandards.https(https)
        let internetMetrics = [
            DiagnosticMetric(
                id: "internet-latency",
                title: "外网延迟",
                value: DisplayFormat.decimal(external?.averageMs, suffix: "ms"),
                assessment: internetLatencyAssessment,
                impact: "反映从家庭网络到公网目标的往返时间，会影响网页首响应、远程桌面、游戏和通话。"
            ),
            DiagnosticMetric(
                id: "internet-loss",
                title: "公网 ICMP 丢包",
                value: DisplayFormat.decimal(external?.packetLossPercent, suffix: "%"),
                assessment: publicLossAssessment,
                impact: "可能反映宽带或上游拥塞，但部分公网节点会降低 ICMP 优先级，因此必须与 DNS、HTTPS 一起看。"
            ),
            DiagnosticMetric(
                id: "internet-dns",
                title: "DNS 解析",
                value: DisplayFormat.decimal(dns.milliseconds, suffix: "ms"),
                assessment: dnsAssessment,
                impact: "DNS 把域名转换为 IP。DNS 慢会让网页和 App 在真正连接服务器前就长时间等待。"
            ),
            DiagnosticMetric(
                id: "internet-https",
                title: "无代理 HTTPS",
                value: DisplayFormat.decimal(https.milliseconds, suffix: "ms"),
                assessment: httpsAssessment,
                impact: "包含真实的网络连接、TLS 和 HTTP 响应，比单纯 ping 更接近网页与 App 的使用体验。"
            )
        ]
        let internetGrade = HealthStandards.worst([
            internetLatencyAssessment,
            publicLossAssessment,
            dnsAssessment,
            httpsAssessment
        ])
        let internetResult = makeLayerResult(
            layer: .internet,
            grade: internetGrade,
            metrics: internetMetrics,
            evidence: [
                "外网 1.1.1.1：\(DisplayFormat.decimal(external?.averageMs, suffix: "ms"))，丢包 \(DisplayFormat.decimal(external?.packetLossPercent, suffix: "%"))",
                "DNS：\(DisplayFormat.decimal(dns.milliseconds, suffix: "ms"))（\(dns.detail)）",
                "HTTPS：\(DisplayFormat.decimal(https.milliseconds, suffix: "ms"))（\(https.detail)）"
            ],
            normalConclusion: "已完成的宽带出口检查正常",
            unavailableConclusion: "宽带出口未检测到足够证据",
            goodAction: "若只有特定应用慢，再单独检查应用服务器、地区线路或账号策略。",
            warningAction: "结合橙色指标复测；公网 ICMP 只作参考，DNS 与无代理 HTTPS 的证据优先级更高。",
            criticalAction: "先确认 DNS 和无代理 HTTPS 是否失败，再用网线或另一设备复测以区分宽带与本机问题。",
            unavailableAction: "确认互联网可用后重新体检。"
        )

        let proxyStandard = "诊断基线以无代理链路为准；启用代理不等于故障，但应与关闭状态对比。"
        let vpnStandard = "以关闭 VPN 的同位置测试作为 Wi-Fi/宽带基线，再与开启状态比较。"
        let proxyAssessment = HealthStandards.pathState(
            active: context.proxyEnabled,
            detail: context.proxyDescription,
            standard: proxyStandard
        )
        let vpnAssessment = HealthStandards.pathState(
            active: context.vpnLikelyActive,
            detail: context.vpnDescription,
            standard: vpnStandard
        )
        let proxyMetrics = [
            DiagnosticMetric(
                id: "path-proxy",
                title: "系统代理",
                value: context.proxyEnabled ? "已启用" : "未启用",
                assessment: proxyAssessment,
                impact: "代理会改变访问路径、DNS 行为和出口位置，可能只影响浏览器或遵循系统代理的 App。"
            ),
            DiagnosticMetric(
                id: "path-vpn",
                title: "系统 VPN",
                value: context.vpnLikelyActive ? "疑似已连接" : "未检测到",
                assessment: vpnAssessment,
                impact: "VPN 可能接管默认路由并增加绕行延迟，也可能造成只有特定应用或域名变慢。"
            )
        ]
        let proxyGrade = HealthStandards.worst([proxyAssessment, vpnAssessment])
        let proxyResult = makeLayerResult(
            layer: .proxyVPN,
            grade: proxyGrade,
            metrics: proxyMetrics,
            evidence: [context.proxyDescription, context.vpnDescription],
            normalConclusion: "未发现会改变诊断基线的系统代理或 VPN",
            unavailableConclusion: "未能确认 VPN / 代理状态",
            goodAction: "无需处理。",
            warningAction: "对比关闭 VPN/代理后的同一测试；本报告的 HTTPS 已显式绕过系统代理。",
            criticalAction: "先恢复直连基线，再重新体检。",
            unavailableAction: "检查系统网络设置后重新体检。"
        )

        return [wirelessResult, localResult, internetResult, proxyResult]
    }

    private func makeLayerResult(
        layer: DiagnosticLayer,
        grade: HealthGrade,
        metrics: [DiagnosticMetric],
        evidence: [String],
        normalConclusion: String,
        unavailableConclusion: String,
        goodAction: String,
        warningAction: String,
        criticalAction: String,
        unavailableAction: String
    ) -> LayerResult {
        let issueTitles = metrics
            .filter { $0.grade == grade && ($0.grade == .warning || $0.grade == .critical) }
            .map(\.title)
            .joined(separator: "、")

        let conclusion: String
        let action: String
        switch grade {
        case .good:
            conclusion = normalConclusion
            action = goodAction
        case .warning:
            conclusion = "\(layer.rawValue)需要注意：\(issueTitles.isEmpty ? "已测指标" : issueTitles)"
            action = warningAction
        case .critical:
            conclusion = "\(layer.rawValue)存在严重异常：\(issueTitles.isEmpty ? "已测指标" : issueTitles)"
            action = criticalAction
        case .unavailable:
            conclusion = unavailableConclusion
            action = unavailableAction
        }

        return LayerResult(
            layer: layer,
            grade: grade,
            conclusion: conclusion,
            evidence: evidence,
            metrics: metrics,
            action: action
        )
    }

    private func value(after key: String, in output: String) -> String? {
        output.split(separator: "\n")
            .map(String.init)
            .first(where: { $0.trimmingCharacters(in: .whitespaces).hasPrefix(key) })?
            .split(separator: ":", maxSplits: 1)
            .last
            .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
    }
}

private extension String {
    func firstMatch(pattern: String) -> [String]? {
        guard let regex = try? NSRegularExpression(pattern: pattern),
              let match = regex.firstMatch(in: self, range: NSRange(startIndex..., in: self)) else {
            return nil
        }
        return (0..<match.numberOfRanges).compactMap { index in
            guard let range = Range(match.range(at: index), in: self) else { return nil }
            return String(self[range])
        }
    }
}

private extension Duration {
    var milliseconds: Double {
        let components = self.components
        return Double(components.seconds) * 1_000 + Double(components.attoseconds) / 1e15
    }
}
