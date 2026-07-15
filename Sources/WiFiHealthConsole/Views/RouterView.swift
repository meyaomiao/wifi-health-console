import AppKit
import SwiftUI

struct RouterView: View {
    @ObservedObject var store: AppStore

    private var gateway: String? { store.networkContext?.gateway }
    private var managementURL: URL? { GatewayAddress.managementURL(for: gateway) }

    var body: some View {
        PageContainer {
            PageHeader(
                title: "路由管理",
                subtitle: gateway.map { "当前 Wi-Fi 网关：\($0)" } ?? "正在识别当前 Wi-Fi 网关",
                trailing: AnyView(headerActions)
            )

            gatewayPanel

            ChannelSuggestionsView(
                title: "当前频段、频宽与信道建议",
                suggestions: ChannelAnalysis.suggestions(
                    current: store.currentSnapshot,
                    networks: store.nearbyNetworks
                )
            )

            Divider()

            Button {
                Task { await store.runDiagnosis() }
            } label: {
                Label(store.isRunningDiagnosis ? "回测中" : "运行变更后回测", systemImage: "arrow.triangle.2.circlepath")
            }
            .buttonStyle(.borderedProminent)
            .disabled(store.isRunningDiagnosis)
        }
        .task {
            await store.refreshNetworkContext()
            if store.nearbyNetworks.isEmpty, !store.isScanning {
                await store.scanNearby()
            }
        }
    }

    private var gatewayPanel: some View {
        HStack(alignment: .top, spacing: 14) {
            Image(systemName: "wifi.router")
                .font(.title2)
                .foregroundStyle(gateway == nil ? Color.secondary : Color.accentColor)
                .frame(width: 30)

            VStack(alignment: .leading, spacing: 7) {
                HStack(spacing: 8) {
                    Text(gatewayPanelTitle)
                        .font(.headline)
                    if store.isDetectingGateway {
                        ProgressView()
                            .controlSize(.small)
                    }
                }

                if let gateway {
                    Text(gateway)
                        .font(.title3.monospacedDigit().weight(.semibold))

                    Text("来源：\(store.networkContext?.gatewaySource ?? "自动检测") · 接口：\(store.networkContext?.interfaceName ?? "未知")")
                        .font(.caption)
                        .foregroundStyle(.secondary)

                    Text(gatewayExplanation(gateway))
                        .font(.callout)
                        .foregroundStyle(.secondary)
                } else if !store.isDetectingGateway {
                    Text("未找到当前 Wi-Fi 接口的 DHCP 网关或默认路由。请确认已连接网络后重新检测。")
                        .font(.callout)
                        .foregroundStyle(.secondary)
                }

                Label("工具只负责识别和打开候选地址，不会自动修改任何路由器设置。", systemImage: "lock.shield")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            Spacer()
        }
        .padding(AppLayout.cardPadding)
        .appCardStyle()
    }

    private var headerActions: some View {
        HStack(spacing: 8) {
            Button {
                Task { await store.refreshNetworkContext() }
            } label: {
                Label("重新检测", systemImage: "arrow.clockwise")
            }
            .disabled(store.isDetectingGateway)

            Button {
                if let managementURL {
                    NSWorkspace.shared.open(managementURL)
                }
            } label: {
                Label("打开管理页", systemImage: "safari")
            }
            .buttonStyle(.borderedProminent)
            .disabled(managementURL == nil)
        }
    }

    private var gatewayPanelTitle: String {
        if store.isDetectingGateway, gateway == nil { return "正在自动检测" }
        guard let gateway else { return "未检测到网关" }
        return GatewayAddress.isPrivateOrLocal(gateway) ? "已检测到本地网关" : "已检测到上游网关"
    }

    private func gatewayExplanation(_ gateway: String) -> String {
        if GatewayAddress.isPrivateOrLocal(gateway) {
            return "多数家用路由器会在默认网关提供管理页面。地址由当前网络自动检测，不依赖小米、华为、TP-Link 等厂商预设。"
        }
        return "这个地址不是常见的本地私有地址，可能属于公共、企业或运营商网络，因此不保证存在可访问的路由管理页面。"
    }
}
