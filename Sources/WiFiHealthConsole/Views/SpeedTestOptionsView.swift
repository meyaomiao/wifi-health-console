import SwiftUI

struct SpeedTestOptionsView: View {
    @Binding var selectedRoute: SpeedTestRoute
    @Binding var selectedDurationPreset: SpeedTestDurationPreset
    let wifiInterface: String?
    let isRunning: Bool

    private let labelColumnWidth: CGFloat = 72
    private let controlColumnWidth: CGFloat = 520

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Grid(alignment: .leading, horizontalSpacing: 12, verticalSpacing: 12) {
                GridRow(alignment: .top) {
                    optionLabel("测速链路")

                    VStack(alignment: .leading, spacing: 5) {
                        EqualWidthSegmentedPicker(
                            options: SpeedTestRoute.allCases,
                            selection: $selectedRoute,
                            title: \.rawValue,
                            isEnabled: !isRunning,
                            accessibilityLabel: "测速链路"
                        )
                        .frame(width: controlColumnWidth, height: 28, alignment: .leading)

                        Text(selectedRoute.detail)
                            .font(.caption)
                            .foregroundStyle(.secondary)
                            .fixedSize(horizontal: false, vertical: true)

                        if selectedRoute == .directWiFi {
                            Label(
                                wifiInterface.map { "当前 Wi-Fi 接口：\($0)" } ?? "未找到当前 Wi-Fi 接口",
                                systemImage: "network"
                            )
                            .font(.caption.monospacedDigit())
                            .foregroundStyle(wifiInterface == nil ? .orange : .secondary)
                        }
                    }
                    .frame(width: controlColumnWidth, alignment: .leading)
                }

                GridRow(alignment: .top) {
                    optionLabel("测速时长")

                    VStack(alignment: .leading, spacing: 5) {
                        EqualWidthSegmentedPicker(
                            options: SpeedTestDurationPreset.allCases,
                            selection: $selectedDurationPreset,
                            title: \.segmentTitle,
                            isEnabled: !isRunning,
                            accessibilityLabel: "测速时长"
                        )
                        .frame(width: controlColumnWidth, height: 28, alignment: .leading)

                        Text(selectedDurationPreset.detail)
                            .font(.caption)
                            .foregroundStyle(.secondary)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                    .frame(width: controlColumnWidth, alignment: .leading)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)

            Label(selectedDurationPreset.trafficWarning, systemImage: "arrow.up.arrow.down.circle")
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)

            Divider()

            HStack(alignment: .top, spacing: 10) {
                Image(systemName: "iphone.gen3.radiowaves.left.and.right")
                    .foregroundStyle(.secondary)
                    .frame(width: 18)
                VStack(alignment: .leading, spacing: 3) {
                    Text("手机热点对照口径")
                        .font(.caption.weight(.semibold))
                    Text("手机状态栏或热点页常显示瞬时总流量，单位也可能是 MB/s；本工具最终值是 Mac 到测速节点的阶段平均有效速率，单位为 Mbps（例如 10 MB/s ≈ 80 Mbps）。严格对照需使用同一测速节点、关闭其他流量并连续测 3 次看中位数；热点还多一段 Wi-Fi、NAT 与蜂窝链路，因此两端数值不必完全相同。")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(AppLayout.cardPadding)
        .appCardStyle()
    }

    private func optionLabel(_ text: String) -> some View {
        Text(text)
            .font(.callout.weight(.medium))
            .foregroundStyle(.secondary)
            .frame(width: labelColumnWidth, alignment: .leading)
            .padding(.top, 5)
    }
}
