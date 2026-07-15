import SwiftUI

struct ChannelSpectrumChart: View {
    let band: WiFiBand
    let networks: [NearbyNetwork]
    let current: WiFiSnapshot

    private var sortedNetworks: [NearbyNetwork] {
        networks.sorted {
            if $0.rssi == $1.rssi { return $0.channel < $1.channel }
            return $0.rssi > $1.rssi
        }
    }

    private var activeChannelCount: Int {
        Set(networks.map(\.channel)).count
    }

    private var overlapCount: Int {
        guard current.band == band else { return 0 }
        return ChannelAnalysis.overlapCount(current: current, networks: networks)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 16) {
                SpectrumStat(title: "可见网络", value: "\(networks.count)", systemImage: "wifi")
                SpectrumStat(title: "活跃主信道", value: "\(activeChannelCount)", systemImage: "number")
                SpectrumStat(
                    title: "当前重叠",
                    value: current.band == band ? "\(overlapCount)" : "--",
                    systemImage: "square.3.layers.3d"
                )
                Spacer()
                if current.band == band, let width = current.channelWidthMHz {
                    Text("当前：Ch \(current.channel.map(String.init) ?? "--") / \(width) MHz")
                        .font(.caption.monospacedDigit())
                        .foregroundStyle(.secondary)
                }
            }

            SpectrumCanvas(band: band, networks: sortedNetworks, current: current)
                .frame(height: 360)
                .background(
                    .background.opacity(0.72),
                    in: RoundedRectangle(cornerRadius: AppLayout.cardCornerRadius, style: .continuous)
                )
                .overlay {
                    RoundedRectangle(cornerRadius: AppLayout.cardCornerRadius, style: .continuous)
                        .stroke(.separator.opacity(0.45), lineWidth: 1)
                }

            HStack(spacing: 16) {
                Label("曲线高度 = RSSI", systemImage: "arrow.up.and.down")
                Label("曲线宽度 = 20/40/80/160 MHz", systemImage: "arrow.left.and.right")
                Label("蓝色粗线 = 当前网络", systemImage: "scope")
            }
            .font(.caption)
            .foregroundStyle(.secondary)

            VStack(alignment: .leading, spacing: 8) {
                Text("网络图例")
                    .font(.callout.weight(.semibold))

                LazyVGrid(
                    columns: [GridItem(.adaptive(minimum: AppLayout.spectrumLegendMinimumWidth), spacing: 8)],
                    spacing: 8
                ) {
                    ForEach(sortedNetworks) { network in
                        NetworkLegendItem(
                            network: network,
                            color: SpectrumPalette.color(for: network, current: current),
                            isCurrent: SpectrumPalette.isCurrent(network, current: current)
                        )
                    }
                }
            }
        }
        .padding(AppLayout.cardPadding)
        .appCardStyle()
    }
}

private struct SpectrumStat: View {
    let title: String
    let value: String
    let systemImage: String

    var body: some View {
        HStack(spacing: 7) {
            Image(systemName: systemImage)
                .foregroundStyle(.secondary)
            VStack(alignment: .leading, spacing: 1) {
                Text(value)
                    .font(.callout.monospacedDigit().weight(.semibold))
                Text(title)
                    .font(.caption2)
                    .foregroundStyle(.secondary)
            }
        }
    }
}

private struct NetworkLegendItem: View {
    let network: NearbyNetwork
    let color: Color
    let isCurrent: Bool

    var body: some View {
        HStack(spacing: 8) {
            RoundedRectangle(cornerRadius: 2)
                .fill(color)
                .frame(width: isCurrent ? 18 : 12, height: isCurrent ? 5 : 3)

            VStack(alignment: .leading, spacing: 2) {
                HStack(spacing: 5) {
                    Text(network.ssid)
                        .font(.caption.weight(isCurrent ? .semibold : .regular))
                        .lineLimit(1)
                    if isCurrent {
                        Text("当前")
                            .font(.caption2.weight(.semibold))
                            .foregroundStyle(.blue)
                    }
                }
                Text("Ch \(network.channel) · \(network.channelWidthMHz) MHz · \(network.rssi) dBm")
                    .font(.caption2.monospacedDigit())
                    .foregroundStyle(.secondary)
            }
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 9)
        .padding(.vertical, 7)
        .background(color.opacity(isCurrent ? 0.12 : 0.055), in: RoundedRectangle(cornerRadius: 5))
    }
}

private struct SpectrumCanvas: View {
    let band: WiFiBand
    let networks: [NearbyNetwork]
    let current: WiFiSnapshot

    private let minimumRSSI = -95.0
    private let maximumRSSI = -30.0

    var body: some View {
        Canvas { context, size in
            let plot = CGRect(x: 48, y: 18, width: max(1, size.width - 64), height: max(1, size.height - 48))
            drawGrid(context: &context, plot: plot)
            drawNetworks(context: &context, plot: plot)
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("\(band.rawValue) 信道频谱总览")
        .accessibilityValue("显示 \(networks.count) 个网络的信道、频宽和 RSSI")
    }

    private func drawGrid(context: inout GraphicsContext, plot: CGRect) {
        let yTicks = [-30.0, -55.0, -67.0, -80.0, -95.0]
        for tick in yTicks {
            let yPosition = y(tick, plot: plot)
            var line = Path()
            line.move(to: CGPoint(x: plot.minX, y: yPosition))
            line.addLine(to: CGPoint(x: plot.maxX, y: yPosition))
            let thresholdColor: Color = tick == -55 ? .green : tick == -67 ? .orange : .secondary
            context.stroke(
                line,
                with: .color(thresholdColor.opacity(tick == -55 || tick == -67 ? 0.28 : 0.13)),
                style: StrokeStyle(lineWidth: 1, dash: tick == -55 || tick == -67 ? [4, 4] : [])
            )
            context.draw(
                Text("\(Int(tick))")
                    .font(.caption2.monospacedDigit())
                    .foregroundStyle(.secondary),
                at: CGPoint(x: plot.minX - 7, y: yPosition),
                anchor: .trailing
            )
        }

        for tick in axisTicks {
            let xPosition = x(tick, plot: plot)
            var line = Path()
            line.move(to: CGPoint(x: xPosition, y: plot.minY))
            line.addLine(to: CGPoint(x: xPosition, y: plot.maxY))
            context.stroke(line, with: .color(Color.secondary.opacity(0.09)), lineWidth: 1)
            context.draw(
                Text("\(Int(tick))")
                    .font(.caption2.monospacedDigit())
                    .foregroundStyle(.secondary),
                at: CGPoint(x: xPosition, y: plot.maxY + 14),
                anchor: .center
            )
        }

        context.draw(
            Text("RSSI dBm")
                .font(.caption2)
                .foregroundStyle(.secondary),
            at: CGPoint(x: plot.minX, y: 8),
            anchor: .leading
        )
        context.draw(
            Text("信道")
                .font(.caption2)
                .foregroundStyle(.secondary),
            at: CGPoint(x: plot.maxX, y: plot.maxY + 14),
            anchor: .trailing
        )
    }

    private func drawNetworks(context: inout GraphicsContext, plot: CGRect) {
        let weakToStrong = networks.sorted { $0.rssi < $1.rssi }
        for network in weakToStrong {
            let range = ChannelAnalysis.estimatedRange(
                channel: network.channel,
                widthMHz: network.channelWidthMHz,
                band: band
            )
            let lowerX = x(range.lowerBound, plot: plot)
            let upperX = x(range.upperBound, plot: plot)
            let peakX = (lowerX + upperX) / 2
            let baseY = y(minimumRSSI, plot: plot)
            let peakY = y(Double(network.rssi), plot: plot)
            let halfWidth = max(2, (upperX - lowerX) / 2)
            let color = SpectrumPalette.color(for: network, current: current)
            let isCurrent = SpectrumPalette.isCurrent(network, current: current)

            var shape = Path()
            shape.move(to: CGPoint(x: lowerX, y: baseY))
            shape.addCurve(
                to: CGPoint(x: peakX, y: peakY),
                control1: CGPoint(x: lowerX + halfWidth * 0.42, y: baseY),
                control2: CGPoint(x: peakX - halfWidth * 0.28, y: peakY)
            )
            shape.addCurve(
                to: CGPoint(x: upperX, y: baseY),
                control1: CGPoint(x: peakX + halfWidth * 0.28, y: peakY),
                control2: CGPoint(x: upperX - halfWidth * 0.42, y: baseY)
            )
            shape.closeSubpath()

            context.fill(shape, with: .color(color.opacity(isCurrent ? 0.17 : 0.07)))
            context.stroke(shape, with: .color(color.opacity(isCurrent ? 1 : 0.72)), lineWidth: isCurrent ? 3 : 1.35)
        }

        var occupiedLabelRects: [CGRect] = []
        let labelCandidates = networks
            .sorted {
                let firstCurrent = SpectrumPalette.isCurrent($0, current: current)
                let secondCurrent = SpectrumPalette.isCurrent($1, current: current)
                if firstCurrent != secondCurrent { return firstCurrent }
                return $0.rssi > $1.rssi
            }
            .prefix(14)

        for network in labelCandidates {
            let range = ChannelAnalysis.estimatedRange(channel: network.channel, widthMHz: network.channelWidthMHz, band: band)
            let labelX = (x(range.lowerBound, plot: plot) + x(range.upperBound, plot: plot)) / 2
            let labelY = max(plot.minY + 7, y(Double(network.rssi), plot: plot) - 9)
            let label = network.ssid == "隐藏网络" ? "隐藏 · Ch \(network.channel)" : network.ssid
            let estimatedWidth = min(132, max(42, CGFloat(label.count) * 7))
            let labelRect = CGRect(x: labelX - estimatedWidth / 2, y: labelY - 8, width: estimatedWidth, height: 16)
            guard !occupiedLabelRects.contains(where: { $0.insetBy(dx: -5, dy: -3).intersects(labelRect) }) else { continue }
            occupiedLabelRects.append(labelRect)

            context.draw(
                Text(label)
                    .font(.caption2.weight(SpectrumPalette.isCurrent(network, current: current) ? .bold : .medium))
                    .foregroundStyle(SpectrumPalette.color(for: network, current: current)),
                at: CGPoint(x: labelX, y: labelY),
                anchor: .center
            )
        }
    }

    private var axisTicks: [Double] {
        switch band {
        case .band2: [1, 3, 6, 9, 11, 14]
        case .band5: [36, 48, 64, 100, 116, 132, 149, 165, 177]
        case .band6: [1, 37, 69, 101, 133, 165, 197, 229]
        case .unknown: [1, 50, 100, 150, 200, 233]
        }
    }

    private func x(_ channel: Double, plot: CGRect) -> CGFloat {
        let domain = ChannelAnalysis.domain(for: band)
        let fraction = (channel - domain.lowerBound) / (domain.upperBound - domain.lowerBound)
        return plot.minX + CGFloat(max(0, min(1, fraction))) * plot.width
    }

    private func y(_ rssi: Double, plot: CGRect) -> CGFloat {
        let clamped = max(minimumRSSI, min(maximumRSSI, rssi))
        let fraction = (maximumRSSI - clamped) / (maximumRSSI - minimumRSSI)
        return plot.minY + CGFloat(fraction) * plot.height
    }
}

private enum SpectrumPalette {
    private static let colors: [Color] = [
        .purple, .pink, .orange, .teal, .indigo, .mint, .cyan, .brown
    ]

    static func isCurrent(_ network: NearbyNetwork, current: WiFiSnapshot) -> Bool {
        if let currentBSSID = current.bssid, let networkBSSID = network.bssid {
            return currentBSSID.caseInsensitiveCompare(networkBSSID) == .orderedSame
        }
        if let currentSSID = current.ssid {
            return network.ssid == currentSSID && network.channel == current.channel
        }
        return false
    }

    static func color(for network: NearbyNetwork, current: WiFiSnapshot) -> Color {
        if isCurrent(network, current: current) { return .blue }
        let key = network.bssid ?? network.id
        let index = stableIndex(key, count: colors.count)
        return colors[index]
    }

    private static func stableIndex(_ value: String, count: Int) -> Int {
        var hash: UInt64 = 14_695_981_039_346_656_037
        for byte in value.utf8 {
            hash ^= UInt64(byte)
            hash &*= 1_099_511_628_211
        }
        return Int(hash % UInt64(count))
    }
}
