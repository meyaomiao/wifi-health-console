import Charts
import SwiftUI

struct SpeedLiveChart: View {
    let samples: [SpeedTestSample]
    let currentPhase: SpeedTestPhase?
    let downloadMbps: Double
    let uploadMbps: Double
    let interfaceName: String?
    let isRunning: Bool
    let hasFinalResult: Bool

    init(
        samples: [SpeedTestSample],
        currentPhase: SpeedTestPhase?,
        downloadMbps: Double,
        uploadMbps: Double,
        interfaceName: String?,
        isRunning: Bool,
        hasFinalResult: Bool = false
    ) {
        self.samples = samples
        self.currentPhase = currentPhase
        self.downloadMbps = downloadMbps
        self.uploadMbps = uploadMbps
        self.interfaceName = interfaceName
        self.isRunning = isRunning
        self.hasFinalResult = hasFinalResult
    }

    private var downloadSamples: [SpeedTestSample] {
        samples.filter { $0.phase == .download }
    }

    private var uploadSamples: [SpeedTestSample] {
        samples.filter { $0.phase == .upload }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            VStack(alignment: .leading, spacing: 3) {
                Text(isRunning ? "分阶段实时趋势" : "本次分阶段趋势")
                    .font(.headline)
                Text("采样接口：\(interfaceName ?? "未识别")")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }

            DirectionSpeedChart(
                phase: .download,
                samples: downloadSamples,
                currentPhase: currentPhase,
                liveMbps: downloadMbps,
                isRunning: isRunning,
                hasFinalResult: hasFinalResult
            )

            DirectionSpeedChart(
                phase: .upload,
                samples: uploadSamples,
                currentPhase: currentPhase,
                liveMbps: uploadMbps,
                isRunning: isRunning,
                hasFinalResult: hasFinalResult
            )

            Divider()

            VStack(alignment: .leading, spacing: 5) {
                Label("曲线口径", systemImage: "waveform.path.ecg")
                    .font(.caption.weight(.semibold))
                Text("两张曲线分别表示测速阶段内，采样接口的总接收或总发送流量趋势；其中可能包含 TCP 确认、系统后台任务和其他 App 流量。")
                Text("页面下方的最终下载与上传结果来自 macOS networkQuality 的专属测试流量统计，应以最终结果作为网速结论。")
            }
            .font(.caption)
            .foregroundStyle(.secondary)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .padding(AppLayout.cardPadding)
        .appCardStyle()
    }
}

private struct DirectionSpeedChart: View {
    let phase: SpeedTestPhase
    let samples: [SpeedTestSample]
    let currentPhase: SpeedTestPhase?
    let liveMbps: Double
    let isRunning: Bool
    let hasFinalResult: Bool

    private var isActive: Bool {
        isRunning && currentPhase == phase
    }

    private var color: Color {
        switch phase {
        case .download: .blue
        case .upload: .mint
        }
    }

    private var title: String {
        switch phase {
        case .download: "下载阶段"
        case .upload: "上传阶段"
        }
    }

    private var subtitle: String {
        switch phase {
        case .download: "接口接收总流量"
        case .upload: "接口发送总流量"
        }
    }

    private var systemImage: String {
        switch phase {
        case .download: "arrow.down.circle.fill"
        case .upload: "arrow.up.circle.fill"
        }
    }

    private var yMaximum: Double {
        let peak = samples.map(\.mbps).max() ?? 0
        return max(10, peak * 1.18)
    }

    private var xMaximum: Double {
        let last = samples.map(\.elapsedSeconds).max() ?? 0
        return max(1, last * 1.04)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            ViewThatFits(in: .horizontal) {
                HStack(alignment: .firstTextBaseline, spacing: 12) {
                    phaseIdentity
                    PhaseStatePill(text: stateText, color: stateColor)
                    Spacer()
                    LiveSpeedValue(
                        title: valueTitle,
                        value: samples.isEmpty ? nil : liveMbps,
                        color: color,
                        alignTrailing: true
                    )
                }

                VStack(alignment: .leading, spacing: 8) {
                    HStack(alignment: .firstTextBaseline, spacing: 10) {
                        phaseIdentity
                        Spacer()
                        PhaseStatePill(text: stateText, color: stateColor)
                    }
                    LiveSpeedValue(
                        title: valueTitle,
                        value: samples.isEmpty ? nil : liveMbps,
                        color: color,
                        alignTrailing: false
                    )
                }
            }

            if samples.isEmpty {
                emptyState
            } else {
                Chart(samples) { sample in
                    LineMark(
                        x: .value("阶段时间", sample.elapsedSeconds),
                        y: .value("速度", sample.mbps)
                    )
                    .foregroundStyle(color)
                    .lineStyle(StrokeStyle(lineWidth: 2.8, lineCap: .round, lineJoin: .round))
                    .interpolationMethod(.monotone)
                }
                .chartXScale(domain: 0...xMaximum)
                .chartYScale(domain: 0...yMaximum)
                .chartXAxis {
                    AxisMarks(position: .bottom) { value in
                        AxisGridLine().foregroundStyle(.secondary.opacity(0.12))
                        AxisTick().foregroundStyle(.secondary.opacity(0.5))
                        AxisValueLabel {
                            if let seconds = value.as(Double.self) {
                                Text("\(Int(seconds))s")
                            }
                        }
                    }
                }
                .chartYAxis {
                    AxisMarks(position: .leading) { value in
                        AxisGridLine().foregroundStyle(.secondary.opacity(0.14))
                        AxisTick().foregroundStyle(.secondary.opacity(0.5))
                        AxisValueLabel {
                            if let mbps = value.as(Double.self) {
                                Text(mbps, format: .number.precision(.fractionLength(0)))
                            }
                        }
                    }
                }
                .chartPlotStyle { plot in
                    plot
                        .background(.background.opacity(0.5))
                        .clipShape(RoundedRectangle(cornerRadius: 5))
                }
                .frame(height: 190)
                .animation(.linear(duration: 0.2), value: samples.count)

                HStack {
                    Label(subtitle, systemImage: "circle.fill")
                        .foregroundStyle(color)
                    Spacer()
                    Text("纵轴：Mbps · 横轴：本阶段秒数")
                }
                .font(.caption)
                .foregroundStyle(.secondary)
            }
        }
        .padding(12)
        .background(
            .background.opacity(0.55),
            in: RoundedRectangle(cornerRadius: AppLayout.cardCornerRadius, style: .continuous)
        )
        .overlay {
            RoundedRectangle(cornerRadius: AppLayout.cardCornerRadius, style: .continuous)
                .stroke(color.opacity(0.14), lineWidth: 1)
        }
    }

    private var phaseIdentity: some View {
        VStack(alignment: .leading, spacing: 2) {
            Label(title, systemImage: systemImage)
                .font(.callout.weight(.semibold))
                .foregroundStyle(color)
            Text(subtitle)
                .font(.caption)
                .foregroundStyle(.secondary)
        }
    }

    private var emptyState: some View {
        VStack(spacing: 7) {
            Image(systemName: isRunning ? "clock" : "waveform.slash")
                .font(.title2)
                .foregroundStyle(.secondary)
            Text(emptyTitle)
                .font(.callout.weight(.medium))
            Text(emptyDetail)
                .font(.caption)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
        }
        .frame(maxWidth: .infinity, minHeight: 190)
    }

    private var stateText: String {
        if isActive { return "测试中" }
        if !samples.isEmpty { return "已采样" }
        return isRunning ? "等待" : "无数据"
    }

    private var stateColor: Color {
        if isActive { return color }
        return samples.isEmpty ? .secondary : .green
    }

    private var valueTitle: String {
        if isActive { return "接口实时" }
        if isRunning { return "阶段末值" }
        return hasFinalResult ? "系统阶段平均" : "接口末值"
    }

    private var emptyTitle: String {
        switch phase {
        case .download:
            return isRunning ? "等待下载阶段样本" : "未取得下载阶段曲线"
        case .upload:
            return isRunning ? "等待上传阶段开始" : "未取得上传阶段曲线"
        }
    }

    private var emptyDetail: String {
        switch phase {
        case .download:
            return isRunning ? "系统正在准备下载测试。" : "本次测试没有返回可绘制的接口接收样本。"
        case .upload:
            return isRunning ? "下载阶段完成后，上传测试会在独立时间轴中开始绘制。" : "本次测试没有返回可绘制的接口发送样本。"
        }
    }
}

private struct PhaseStatePill: View {
    let text: String
    let color: Color

    var body: some View {
        Text(text)
            .font(.caption.weight(.semibold))
            .foregroundStyle(color)
            .lineLimit(1)
            .fixedSize(horizontal: true, vertical: false)
            .padding(.horizontal, 8)
            .padding(.vertical, 4)
            .background(color.opacity(0.1), in: Capsule())
    }
}

private struct LiveSpeedValue: View {
    let title: String
    let value: Double?
    let color: Color
    let alignTrailing: Bool

    var body: some View {
        VStack(alignment: alignTrailing ? .trailing : .leading, spacing: 2) {
            Text(title)
                .font(.caption)
                .foregroundStyle(.secondary)
            Text(value.map { String(format: "%.1f Mbps", $0) } ?? "--")
                .font(.title3.monospacedDigit().weight(.semibold))
                .foregroundStyle(value == nil ? Color.secondary : color)
                .frame(minWidth: 110, alignment: alignTrailing ? .trailing : .leading)
            Text(value.map { String(format: "%.2f MB/s", $0 / 8) } ?? "等待阶段开始")
                .font(.caption.monospacedDigit())
                .foregroundStyle(.secondary)
        }
    }
}
