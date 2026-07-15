import CoreLocation
import SwiftUI

struct OverviewView: View {
    @ObservedObject var store: AppStore

    private var snapshot: WiFiSnapshot { store.currentSnapshot }

    var body: some View {
        PageContainer {
            PageHeader(
                title: snapshot.ssid ?? "当前 Wi-Fi",
                subtitle: connectionSubtitle,
                trailing: AnyView(StatusBadge(
                    grade: store.currentGrade,
                    label: store.currentStatusLabel
                ))
            )

            if needsLocationPermission {
                PermissionBanner(
                    statusText: permissionText,
                    actionTitle: store.locationPermissionActionTitle
                ) {
                    store.requestLocationAccess()
                }
            }

            conclusionPanel
            metricGrid
            evidenceAndAction
        }
    }

    private var conclusionPanel: some View {
        HStack(alignment: .top, spacing: 14) {
            Image(systemName: store.currentGrade.systemImage)
                .font(.title2)
                .foregroundStyle(store.currentGrade.color)
                .frame(width: 32)
            VStack(alignment: .leading, spacing: 4) {
                Text(store.currentConclusion)
                    .font(.headline)
                Text(conclusionDetail)
                    .font(.callout)
                    .foregroundStyle(.secondary)
            }
            Spacer()
            Button("60 秒体检") {
                Task { await store.runDiagnosis() }
            }
            .buttonStyle(.borderedProminent)
            .disabled(store.isRunningDiagnosis)
        }
        .padding(16)
        .appCardStyle(
            tint: store.currentGrade.color,
            borderColor: store.currentGrade.color.opacity(0.2)
        )
    }

    private var metricGrid: some View {
        LazyVGrid(
            columns: [GridItem(.adaptive(minimum: AppLayout.metricTileMinimumWidth), spacing: 10, alignment: .top)],
            spacing: 10
        ) {
            MetricTile(
                title: "频段",
                value: snapshot.band.rawValue,
                systemImage: "antenna.radiowaves.left.and.right",
                assessment: bandAssessment
            )
            MetricTile(
                title: "信道",
                value: snapshot.channel.map(String.init) ?? "--",
                systemImage: "number",
                assessment: channelAssessment
            )
            MetricTile(
                title: "频宽",
                value: DisplayFormat.integer(snapshot.channelWidthMHz, suffix: "MHz"),
                systemImage: "arrow.left.and.right",
                assessment: HealthStandards.channelWidth(snapshot.channelWidthMHz, band: snapshot.band)
            )
            MetricTile(
                title: "RSSI",
                value: DisplayFormat.integer(snapshot.rssi, suffix: "dBm"),
                systemImage: "wifi",
                assessment: HealthStandards.rssi(snapshot.rssi)
            )
            MetricTile(
                title: "噪声",
                value: DisplayFormat.integer(snapshot.noise, suffix: "dBm"),
                systemImage: "waveform",
                assessment: noiseAssessment
            )
            MetricTile(
                title: "SNR",
                value: DisplayFormat.integer(snapshot.snr, suffix: "dB"),
                systemImage: "waveform.path.ecg",
                assessment: HealthStandards.snr(snapshot.snr)
            )
            MetricTile(
                title: "协商速率",
                value: DisplayFormat.decimal(snapshot.transmitRateMbps, suffix: "Mbps", digits: 0),
                systemImage: "speedometer",
                assessment: transmitRateAssessment
            )
            MetricTile(
                title: "CCA",
                value: DisplayFormat.decimal(snapshot.ccaPercent, suffix: "%", digits: 1),
                systemImage: "chart.bar.xaxis",
                assessment: HealthStandards.cca(snapshot.ccaPercent)
            )
        }
    }

    private var evidenceAndAction: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("证据与下一步")
                .font(.headline)
            Label("RSSI：\(HealthStandards.rssiStandard)", systemImage: "ruler")
            Label("SNR：\(HealthStandards.snrStandard)", systemImage: "checklist")
            Label("统一状态：绿色为优秀/正常，橙色为注意，红色为严重，灰色为参考/未检测。", systemImage: "circle.grid.2x2")
            Label(nextAction, systemImage: "arrow.right.circle")
        }
        .font(.callout)
    }

    private var connectionSubtitle: String {
        let interface = snapshot.interfaceName ?? "接口未知"
        return "\(interface) · \(snapshot.band.rawValue) · \(DisplayFormat.dateTime.string(from: snapshot.timestamp))"
    }

    private var conclusionDetail: String {
        guard snapshot.isConnected else { return "确认 Mac 已连接 Wi-Fi；SSID 显示还需要定位授权。" }
        return "此处仅判断 Mac 当前无线信号；局域网、宽带出口和 VPN/代理需运行完整体检。"
    }

    private var bandAssessment: MetricAssessment {
        HealthStandards.reference(
            available: snapshot.band != .unknown,
            interpretation: snapshot.band == .unknown
                ? "未取得当前频段。"
                : "当前连接在 \(snapshot.band.rawValue)；频段本身不单独代表好坏。",
            standard: "2.4 GHz 覆盖更远；5/6 GHz 通常速度更高但穿墙衰减更明显。"
        )
    }

    private var channelAssessment: MetricAssessment {
        HealthStandards.reference(
            available: snapshot.channel != nil,
            interpretation: snapshot.channel.map {
                "当前主信道为 \($0)；需结合附近网络重叠、CCA 和重传判断。"
            } ?? "未取得主信道。",
            standard: "没有固定的最佳信道；以重叠少、CCA 低、重传少为好。"
        )
    }

    private var noiseAssessment: MetricAssessment {
        HealthStandards.reference(
            available: snapshot.noise != nil,
            interpretation: snapshot.noise.map {
                "当前 \($0) dBm；噪声需与 RSSI 一起计算 SNR，单独数值不作健康判定。"
            } ?? "未取得噪声底。",
            standard: "噪声越负通常越安静，但应优先看 SNR，而不是单独看噪声。"
        )
    }

    private var transmitRateAssessment: MetricAssessment {
        HealthStandards.reference(
            available: snapshot.transmitRateMbps != nil,
            interpretation: snapshot.transmitRateMbps.map {
                "当前 PHY 协商速率约 \(Int($0.rounded())) Mbps，不等于互联网实测吞吐。"
            } ?? "未取得协商速率。",
            standard: "协商速率是无线链路参考上限；实际网速还受协议开销、宽带、出口和服务器影响。"
        )
    }

    private var nextAction: String {
        switch store.currentGrade {
        case .good: "运行 60 秒体检，确认网关和宽带出口是否为瓶颈。"
        case .warning: "在相同位置完成体检，并扫描信道重叠后再决定是否改频宽。"
        case .critical: "先靠近路由节点回测；若改善明显，优先处理覆盖或节点回程。"
        case .unavailable: "连接 Wi-Fi 并刷新采样。"
        }
    }

    private var needsLocationPermission: Bool {
        locationStatusNeedsAction(store.locationStatus)
    }

    private var permissionText: String {
        switch store.locationStatus {
        case .denied:
            "此前已拒绝定位权限；macOS 不会再次弹窗，请打开系统设置后允许。"
        case .restricted:
            "定位权限受到系统限制，请打开系统设置检查限制来源。"
        default:
            "授权后可显示 SSID、BSSID 并扫描附近信道。"
        }
    }

    private func locationStatusNeedsAction(_ status: CLAuthorizationStatus) -> Bool {
        switch status {
        case .authorizedAlways, .authorizedWhenInUse: false
        default: true
        }
    }
}
