import Charts
import SwiftUI

struct HistoryView: View {
    @ObservedObject var store: AppStore
    @State private var showClearConfirmation = false

    private var recentHistory: [HistorySample] { Array(store.history.suffix(100)) }
    private var before: HistorySample? { store.history.last(where: { $0.marker == .before }) }
    private var after: HistorySample? { store.history.last(where: { $0.marker == .after }) }

    var body: some View {
        PageContainer {
            PageHeader(
                title: "历史趋势",
                subtitle: "自动采样与改信道前后对比",
                trailing: AnyView(clearButton)
            )

            if store.history.isEmpty {
                ContentUnavailableView(
                    "尚无历史采样",
                    systemImage: "chart.xyaxis.line"
                )
                .frame(maxWidth: .infinity, minHeight: 320)
            } else {
                comparison
                trendChart
                historyTable
            }
        }
        .confirmationDialog("清除全部历史记录？", isPresented: $showClearConfirmation) {
            Button("清除", role: .destructive) { store.clearHistory() }
        }
    }

    private var comparison: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("变更前后")
                .font(.headline)
            HStack(spacing: 12) {
                ComparisonColumn(title: "变更前", sample: before)
                Image(systemName: "arrow.right")
                    .foregroundStyle(.secondary)
                ComparisonColumn(title: "变更后", sample: after)
            }
        }
    }

    private var trendChart: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("RSSI 趋势")
                .font(.headline)
            Chart {
                ForEach(recentHistory) { sample in
                    if let rssi = sample.snapshot.rssi {
                        LineMark(
                            x: .value("时间", sample.timestamp),
                            y: .value("RSSI", rssi)
                        )
                        .foregroundStyle(.blue)
                        PointMark(
                            x: .value("时间", sample.timestamp),
                            y: .value("RSSI", rssi)
                        )
                        .foregroundStyle(sample.marker == .none ? .blue.opacity(0.45) : .orange)
                    }
                }
                RuleMark(y: .value("注意阈值", -67))
                    .foregroundStyle(.orange.opacity(0.6))
                    .lineStyle(StrokeStyle(dash: [4]))
                RuleMark(y: .value("严重阈值", -75))
                    .foregroundStyle(.red.opacity(0.55))
                    .lineStyle(StrokeStyle(dash: [4]))
                RuleMark(y: .value("优秀阈值", -55))
                    .foregroundStyle(.green.opacity(0.5))
                    .lineStyle(StrokeStyle(dash: [4]))
            }
            .chartYScale(domain: -90 ... -35)
            .frame(height: 220)
        }
        .padding(AppLayout.cardPadding)
        .appCardStyle()
    }

    private var historyTable: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("最近采样")
                .font(.headline)
            ForEach(recentHistory.reversed()) { sample in
                HStack(spacing: 12) {
                    Text(DisplayFormat.dateTime.string(from: sample.timestamp))
                        .font(.caption.monospacedDigit())
                        .frame(width: 105, alignment: .leading)
                    Text(sample.snapshot.ssid ?? "SSID 未授权")
                        .lineLimit(1)
                        .frame(minWidth: 120, maxWidth: .infinity, alignment: .leading)
                    Text("Ch \(sample.snapshot.channel.map(String.init) ?? "--") / \(sample.snapshot.channelWidthMHz.map { "\($0) MHz" } ?? "--")")
                        .foregroundStyle(.secondary)
                        .frame(width: 125, alignment: .leading)
                    Text(DisplayFormat.integer(sample.snapshot.rssi, suffix: "dBm"))
                        .monospacedDigit()
                        .frame(width: 80, alignment: .trailing)
                    Picker("标记", selection: Binding(
                        get: { sample.marker },
                        set: { store.setMarker($0, for: sample.id) }
                    )) {
                        ForEach(HistoryMarker.allCases) { marker in
                            Text(marker.rawValue).tag(marker)
                        }
                    }
                    .labelsHidden()
                    .frame(width: 100)
                }
                .font(.callout)
                Divider()
            }
        }
    }

    private var clearButton: some View {
        Button(role: .destructive) {
            showClearConfirmation = true
        } label: {
            Label("清除", systemImage: "trash")
        }
        .disabled(store.history.isEmpty)
    }
}

private struct ComparisonColumn: View {
    let title: String
    let sample: HistorySample?

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text(title)
                    .font(.callout.weight(.semibold))
                Spacer()
                if let sample {
                    StatusBadge(
                        grade: sample.overallGrade,
                        label: sample.overallStatusLabel ?? sample.overallGrade.label,
                        systemImage: sample.overallStatusLabel == "部分完成" ? "exclamationmark.circle" : nil
                    )
                }
            }
            Text(sample.map {
                "\(DisplayFormat.dateTime.string(from: $0.timestamp)) · \($0.gradeScope?.rawValue ?? "历史记录")"
            } ?? "未标记")
                .font(.caption)
                .foregroundStyle(.secondary)
            HStack {
                Text("RSSI \(DisplayFormat.integer(sample?.snapshot.rssi, suffix: "dBm"))")
                Spacer()
                Text("网关 \(DisplayFormat.decimal(sample?.gatewayAverageMs, suffix: "ms"))")
            }
            .font(.callout.monospacedDigit())
        }
        .padding(12)
        .frame(maxWidth: .infinity, minHeight: 90, alignment: .topLeading)
        .appCardStyle()
    }
}
