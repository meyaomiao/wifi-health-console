import SwiftUI

extension HealthGrade {
    var color: Color {
        switch self {
        case .good: .green
        case .warning: .orange
        case .critical: .red
        case .unavailable: .secondary
        }
    }
}

struct StatusBadge: View {
    let grade: HealthGrade
    let label: String
    let systemImage: String

    init(grade: HealthGrade, label: String? = nil, systemImage: String? = nil) {
        let requestedLabel = label ?? grade.label
        self.grade = grade
        self.label = HealthStandards.isStatusLabelCompatible(requestedLabel, with: grade)
            ? requestedLabel
            : grade.label
        self.systemImage = systemImage ?? grade.systemImage
    }

    init(assessment: MetricAssessment) {
        self.grade = assessment.grade
        self.label = assessment.statusLabel
        self.systemImage = assessment.badgeSystemImage
    }

    var body: some View {
        Label(label, systemImage: systemImage)
            .font(.caption.weight(.semibold))
            .foregroundStyle(grade.color)
            .lineLimit(1)
            .fixedSize(horizontal: true, vertical: false)
            .padding(.horizontal, 9)
            .padding(.vertical, 5)
            .background(grade.color.opacity(0.1), in: Capsule())
    }
}

struct PageHeader: View {
    let title: String
    let subtitle: String
    var trailing: AnyView? = nil

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 16) {
            VStack(alignment: .leading, spacing: 4) {
                Text(title)
                    .font(.title2.weight(.semibold))
                Text(subtitle)
                    .font(.callout)
                    .foregroundStyle(.secondary)
            }
            .frame(maxWidth: .infinity, alignment: .leading)

            if let trailing {
                trailing
                    .fixedSize(horizontal: true, vertical: false)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

struct MetricTile: View {
    let title: String
    let value: String
    let systemImage: String
    let assessment: MetricAssessment

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(alignment: .center) {
                Label(title, systemImage: systemImage)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                Spacer()
                StatusBadge(assessment: assessment)
            }
            Text(value)
                .font(.title3.weight(.semibold))
                .lineLimit(1)
                .minimumScaleFactor(0.75)
            Text(assessment.interpretation)
                .font(.caption)
                .foregroundStyle(.secondary)
                .lineLimit(3)
                .frame(maxWidth: .infinity, alignment: .leading)
        }
        .padding(12)
        .frame(minWidth: 150, minHeight: 124, alignment: .topLeading)
        .appCardStyle(
            tint: assessment.grade.color,
            tintOpacity: 0.025,
            borderColor: assessment.grade.color.opacity(0.14)
        )
    }
}

struct LayerResultView: View {
    let result: LayerResult

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                Label(result.layer.rawValue, systemImage: result.layer.systemImage)
                    .font(.headline)
                Spacer()
                StatusBadge(grade: result.grade)
            }
            Text(result.conclusion)
                .font(.body.weight(.medium))
            if result.metrics.isEmpty {
                VStack(alignment: .leading, spacing: 5) {
                    ForEach(result.evidence, id: \.self) { evidence in
                        Label(evidence, systemImage: "doc.text.magnifyingglass")
                            .foregroundStyle(.secondary)
                            .font(.callout)
                    }
                }
            } else {
                LazyVGrid(
                    columns: [GridItem(.adaptive(minimum: AppLayout.diagnosticMetricMinimumWidth), spacing: 10, alignment: .top)],
                    spacing: 10
                ) {
                    ForEach(result.metrics) { metric in
                        DiagnosticMetricView(metric: metric)
                    }
                }
            }
            Divider()
            VStack(alignment: .leading, spacing: 4) {
                Text("下一步")
                    .font(.caption.weight(.semibold))
                    .foregroundStyle(.secondary)
                Label(result.action, systemImage: "arrow.right.circle")
                    .font(.callout)
            }
        }
        .padding(AppLayout.cardPadding)
        .appCardStyle(
            tint: result.grade.color,
            tintOpacity: 0.02,
            borderColor: result.grade.color.opacity(0.16)
        )
    }
}

private struct DiagnosticMetricView: View {
    let metric: DiagnosticMetric

    var body: some View {
        VStack(alignment: .leading, spacing: 9) {
            HStack(alignment: .firstTextBaseline, spacing: 10) {
                Text(metric.title)
                    .font(.callout.weight(.semibold))
                Spacer()
                Text(metric.value)
                    .font(.callout.monospacedDigit().weight(.medium))
                StatusBadge(assessment: metric.assessment)
            }

            Text(metric.interpretation)
                .font(.callout)

            Divider()

            Grid(alignment: .leading, horizontalSpacing: 8, verticalSpacing: 7) {
                GridRow(alignment: .top) {
                    Label("影响", systemImage: "arrow.triangle.branch")
                        .foregroundStyle(.secondary)
                        .frame(width: 58, alignment: .leading)
                    Text(metric.impact)
                }
                GridRow(alignment: .top) {
                    Label("标准", systemImage: "ruler")
                        .foregroundStyle(.secondary)
                        .frame(width: 58, alignment: .leading)
                    Text(metric.standard)
                }
            }
            .font(.caption)
            .foregroundStyle(.secondary)
        }
        .padding(12)
        .frame(maxWidth: .infinity, minHeight: 152, alignment: .topLeading)
        .appCardStyle(
            tint: metric.grade.color,
            tintOpacity: 0.025,
            borderColor: metric.grade.color.opacity(0.16)
        )
    }
}

struct PermissionBanner: View {
    let statusText: String
    var actionTitle = "授权"
    let action: () -> Void

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: "location.slash")
                .foregroundStyle(.orange)
            VStack(alignment: .leading, spacing: 2) {
                Text("SSID 与附近网络需要定位授权")
                    .font(.callout.weight(.semibold))
                Text(statusText)
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            Spacer()
            Button(actionTitle, action: action)
                .buttonStyle(.bordered)
        }
        .padding(12)
        .appCardStyle(
            tint: .orange,
            tintOpacity: 0.08,
            borderColor: .orange.opacity(0.18)
        )
    }
}
