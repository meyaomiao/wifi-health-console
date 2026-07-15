import SwiftUI

struct DiagnosisView: View {
    @ObservedObject var store: AppStore

    var body: some View {
        PageContainer {
            PageHeader(
                title: "60 秒体检",
                subtitle: "无线空口、局域网、宽带出口、VPN / 代理分层判定",
                trailing: AnyView(startButton)
            )

            if store.isRunningDiagnosis {
                progressPanel
            }

            if let report = store.latestReport {
                reportSummary(report)
                VStack(spacing: 12) {
                    ForEach(report.layers) { result in
                        LayerResultView(result: result)
                    }
                }
            } else if !store.isRunningDiagnosis {
                ContentUnavailableView(
                    "尚无体检报告",
                    systemImage: "stethoscope",
                    description: Text("完整采样窗口为 60 秒。")
                )
                .frame(maxWidth: .infinity, minHeight: 320)
            }
        }
    }

    private var startButton: some View {
        Button {
            Task { await store.runDiagnosis() }
        } label: {
            Label(store.isRunningDiagnosis ? "体检中" : "开始体检", systemImage: "play.fill")
        }
        .buttonStyle(.borderedProminent)
        .disabled(store.isRunningDiagnosis)
    }

    private var progressPanel: some View {
        VStack(alignment: .leading, spacing: 9) {
            HStack {
                Text(store.diagnosisStage)
                    .font(.callout.weight(.medium))
                Spacer()
                Text(store.diagnosisProgress, format: .percent.precision(.fractionLength(0)))
                    .monospacedDigit()
                    .foregroundStyle(.secondary)
            }
            ProgressView(value: store.diagnosisProgress)
        }
        .padding(AppLayout.cardPadding)
        .appCardStyle()
    }

    private func reportSummary(_ report: DiagnosticReport) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Text("结论")
                    .font(.headline)
                StatusBadge(
                    grade: report.overallGrade,
                    label: report.overallStatusLabel,
                    systemImage: report.overallStatusLabel == "部分完成" ? "exclamationmark.circle" : nil
                )
                Spacer()
                Text(DisplayFormat.dateTime.string(from: report.completedAt))
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            Text(primaryConclusion(report))
                .font(.title3.weight(.semibold))
            Text(report.baselineDescription)
                .font(.callout)
                .foregroundStyle(.secondary)
            if report.hasUnavailableLayer {
                Text("未检测：\(report.layers.filter { $0.grade == .unavailable }.map { $0.layer.rawValue }.joined(separator: "、"))")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
        .padding(16)
        .appCardStyle(
            tint: report.overallGrade.color,
            borderColor: report.overallGrade.color.opacity(0.2)
        )
    }

    private func primaryConclusion(_ report: DiagnosticReport) -> String {
        switch report.overallGrade {
        case .critical:
            guard let result = report.layers.first(where: { $0.grade == .critical }) else { return "体检发现严重异常" }
            return "严重问题：\(result.layer.rawValue) · \(result.conclusion)"
        case .warning:
            guard let result = report.layers.first(where: { $0.grade == .warning }) else { return "体检结果需要注意" }
            return "需要注意：\(result.layer.rawValue) · \(result.conclusion)"
        case .good:
            return "四层基线均处于正常范围"
        case .unavailable:
            return report.overallStatusLabel == "部分完成"
                ? "体检部分完成；已完成项目未见异常"
                : "体检未取得足够证据"
        }
    }
}
