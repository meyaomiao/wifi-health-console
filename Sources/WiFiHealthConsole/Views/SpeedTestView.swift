import SwiftUI

struct SpeedTestView: View {
    @ObservedObject var store: AppStore
    @State private var selectedRoute: SpeedTestRoute = .currentPath
    @State private var selectedDurationPreset: SpeedTestDurationPreset = .standard

    var body: some View {
        PageContainer {
            PageHeader(
                title: "网速测速",
                subtitle: "直接显示 Mbps、MB/s、文件耗时和实际体验",
                trailing: AnyView(startButton)
            )

            SpeedTestOptionsView(
                selectedRoute: $selectedRoute,
                selectedDurationPreset: $selectedDurationPreset,
                wifiInterface: store.currentSnapshot.interfaceName,
                isRunning: store.isRunningSpeedTest
            )

            if store.isRunningSpeedTest {
                runningPanel
            } else if let error = store.speedTestError {
                errorPanel(error)
            }

            if store.isRunningSpeedTest || !store.speedTestSamples.isEmpty {
                SpeedLiveChart(
                    samples: store.speedTestSamples,
                    currentPhase: store.speedTestPhase,
                    downloadMbps: store.liveDownloadMbps,
                    uploadMbps: store.liveUploadMbps,
                    interfaceName: store.speedTestSampledInterface ?? store.currentSnapshot.interfaceName,
                    isRunning: store.isRunningSpeedTest,
                    hasFinalResult: store.latestSpeedTest != nil
                )
            }

            if let report = store.latestSpeedTest {
                resultView(report)
            } else if !store.isRunningSpeedTest, store.speedTestError == nil {
                ContentUnavailableView(
                    "尚未测速",
                    systemImage: "speedometer",
                    description: Text(emptyStateDescription)
                )
                .frame(maxWidth: .infinity, minHeight: 300)
            }
        }
    }

    private var startButton: some View {
        Button {
            Task {
                await store.runSpeedTest(
                    route: selectedRoute,
                    durationPreset: selectedDurationPreset
                )
            }
        } label: {
            Label(store.isRunningSpeedTest ? "测速中" : "开始测速", systemImage: "play.fill")
        }
        .buttonStyle(.borderedProminent)
        .disabled(store.isRunningSpeedTest || (selectedRoute == .directWiFi && store.currentSnapshot.interfaceName == nil))
    }

    private var runningPanel: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                ProgressView()
                    .controlSize(.small)
                Text(runningStageDescription)
                    .font(.callout.weight(.medium))
                Spacer()
                Text("最长约 \(activeDurationPreset.maximumTotalSeconds) 秒")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            ProgressView()
        }
        .padding(AppLayout.cardPadding)
        .appCardStyle(tint: .blue)
    }

    private var runningStageDescription: String {
        switch store.speedTestPhase {
        case .download:
            "正在测量下载阶段，并采样接口接收总流量"
        case .upload:
            "下载阶段已完成，正在测量上传阶段和接口发送总流量"
        case nil:
            "正在准备分阶段下载与上传测速"
        }
    }

    private var activeDurationPreset: SpeedTestDurationPreset {
        store.activeSpeedTestDurationPreset ?? selectedDurationPreset
    }

    private var emptyStateDescription: String {
        "\(selectedDurationPreset.displayName)下载、上传每个方向最多 \(selectedDurationPreset.phaseRuntimeSeconds) 秒，总计最长约 \(selectedDurationPreset.maximumTotalSeconds) 秒，并会产生实际网络流量。"
    }

    private func errorPanel(_ message: String) -> some View {
        HStack(alignment: .top, spacing: 12) {
            Image(systemName: "exclamationmark.triangle.fill")
                .foregroundStyle(.orange)
            VStack(alignment: .leading, spacing: 4) {
                Text("本次测速未完成")
                    .font(.callout.weight(.semibold))
                Text(message)
                    .font(.callout)
                    .foregroundStyle(.secondary)
            }
            Spacer()
            Button("重试") {
                Task {
                    await store.runSpeedTest(
                        route: selectedRoute,
                        durationPreset: selectedDurationPreset
                    )
                }
            }
        }
        .padding(AppLayout.cardPadding)
        .appCardStyle(tint: .orange, tintOpacity: 0.08)
    }

    @ViewBuilder
    private func resultView(_ report: SpeedTestReport) -> some View {
        let downloadAssessment = HealthStandards.download(report.downloadMbps)
        let uploadAssessment = HealthStandards.upload(report.uploadMbps)

        resultSummary(report)

        LazyVGrid(
            columns: [GridItem(.adaptive(minimum: AppLayout.speedMetricMinimumWidth), spacing: 12, alignment: .top)],
            spacing: 12
        ) {
            SpeedValueCard(
                title: "下载速度",
                systemImage: "arrow.down.circle.fill",
                color: .blue,
                mbps: report.downloadMbps,
                megabytesPerSecond: report.downloadMegabytesPerSecond,
                assessment: downloadAssessment,
                impact: "下载速度影响网页与视频加载、大文件下载、系统更新和多设备同时使用。"
            )
            SpeedValueCard(
                title: "上传速度",
                systemImage: "arrow.up.circle.fill",
                color: .mint,
                mbps: report.uploadMbps,
                megabytesPerSecond: report.uploadMegabytesPerSecond,
                assessment: uploadAssessment,
                impact: "上传速度影响视频会议、直播、云盘同步、照片备份和发送大文件。"
            )
        }

        conversionExplanation
        transferTimes(report)
        qualityDetails(report)
        linkComparison(report)
        measurementDetails(report)
    }

    private func resultSummary(_ report: SpeedTestReport) -> some View {
        let namedAssessments = speedAssessments(report)
        let assessments = namedAssessments.map { $0.assessment }
        let grade = HealthStandards.worst(assessments)
        let statusLabel = HealthStandards.summaryStatusLabel(for: assessments)
        return HStack(alignment: .top, spacing: 12) {
            Image(systemName: grade.systemImage)
                .font(.title2)
                .foregroundStyle(grade.color)
            VStack(alignment: .leading, spacing: 4) {
                Text(speedConclusion(
                    grade: grade,
                    statusLabel: statusLabel,
                    namedAssessments: namedAssessments
                ))
                    .font(.headline)
                Text("\(report.route.rawValue) · \(report.durationPreset.displayName) · \(DisplayFormat.dateTime.string(from: report.completedAt))")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            Spacer()
            StatusBadge(grade: grade, label: statusLabel)
        }
        .padding(16)
        .appCardStyle(tint: grade.color, borderColor: grade.color.opacity(0.18))
    }

    private var conversionExplanation: some View {
        HStack(alignment: .top, spacing: 12) {
            Image(systemName: "divide.circle")
                .foregroundStyle(.blue)
            VStack(alignment: .leading, spacing: 4) {
                Text("不用再手动除以 8")
                    .font(.callout.weight(.semibold))
                Text("运营商通常用 Mbps（兆比特/秒），浏览器和下载器常用 MB/s（兆字节/秒）。1 Byte = 8 bit，本工具已直接换算并同时显示。")
                    .font(.callout)
                    .foregroundStyle(.secondary)
            }
        }
        .padding(AppLayout.cardPadding)
        .appCardStyle(tint: .blue, tintOpacity: 0.06)
    }

    private func transferTimes(_ report: SpeedTestReport) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("实际文件大约要多久")
                .font(.headline)
            LazyVGrid(
                columns: [GridItem(.adaptive(minimum: AppLayout.speedSupportMinimumWidth), spacing: 10, alignment: .top)],
                spacing: 10
            ) {
                TransferTimeCard(title: "下载 1 GB", value: durationText(report.downloadSeconds(forGigabytes: 1)), systemImage: "arrow.down.doc")
                TransferTimeCard(title: "下载 10 GB", value: durationText(report.downloadSeconds(forGigabytes: 10)), systemImage: "externaldrive")
                TransferTimeCard(title: "上传 1 GB", value: durationText(report.uploadSeconds(forGigabytes: 1)), systemImage: "arrow.up.doc")
            }
            Text("按全程维持当前平均速度估算；服务器限速、Wi-Fi 波动和磁盘速度会让真实时间变化。")
                .font(.caption)
                .foregroundStyle(.secondary)
            Text("这里的 GB 使用十进制定义（1 GB = 1,000 MB）；部分系统显示的 GiB 会比同名 GB 大约 7.4%。")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
    }

    private func qualityDetails(_ report: SpeedTestReport) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("连接质量")
                .font(.headline)
            LazyVGrid(
                columns: [GridItem(.adaptive(minimum: AppLayout.speedSupportMinimumWidth), spacing: 10, alignment: .top)],
                spacing: 10
            ) {
                QualityTile(
                    title: "空闲延迟",
                    value: DisplayFormat.decimal(report.idleLatencyMs, suffix: "ms"),
                    assessment: HealthStandards.idleLatency(report.idleLatencyMs),
                    impact: "影响网页首响应、远程控制、游戏和通话的即时感受；越低越好。"
                )
                QualityTile(
                    title: "下载时响应",
                    value: DisplayFormat.decimal(report.downloadResponsivenessRPM, suffix: "RPM", digits: 0),
                    assessment: HealthStandards.responsiveness(report.downloadResponsivenessRPM, subject: "下载时响应"),
                    impact: "反映下载占满带宽时，网页、通话和交互请求是否仍能及时排队处理。"
                )
                QualityTile(
                    title: "上传时响应",
                    value: DisplayFormat.decimal(report.uploadResponsivenessRPM, suffix: "RPM", digits: 0),
                    assessment: HealthStandards.responsiveness(report.uploadResponsivenessRPM, subject: "上传时响应"),
                    impact: "反映上传占满带宽时是否出现缓冲膨胀，影响会议、游戏和日常浏览。"
                )
            }
        }
    }

    private func linkComparison(_ report: SpeedTestReport) -> some View {
        VStack(alignment: .leading, spacing: 9) {
            Text("不要和 Wi-Fi 协商速率混淆")
                .font(.headline)
            HStack(spacing: 24) {
                VStack(alignment: .leading, spacing: 2) {
                    Text("Wi-Fi PHY 协商速率")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                    Text(DisplayFormat.decimal(store.currentSnapshot.transmitRateMbps, suffix: "Mbps", digits: 0))
                        .font(.title3.monospacedDigit().weight(.semibold))
                }
                Image(systemName: "arrow.right")
                    .foregroundStyle(.secondary)
                VStack(alignment: .leading, spacing: 2) {
                    Text("互联网实际下载")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                    Text(String(format: "%.1f Mbps", report.downloadMbps))
                        .font(.title3.monospacedDigit().weight(.semibold))
                }
            }
            Text("协商速率只是无线链路的理论上限；互联网实测还受宽带套餐、出口、VPN/代理和测试服务器影响，不能直接把差值全部算成 Wi-Fi 损耗。")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
        .padding(AppLayout.cardPadding)
        .appCardStyle()
    }

    private func measurementDetails(_ report: SpeedTestReport) -> some View {
        VStack(alignment: .leading, spacing: 7) {
            Text("本次测量")
                .font(.headline)
            detailRow("测速链路", report.route.rawValue)
            detailRow("测速时长", "\(report.durationPreset.displayName) · 每方向最多 \(report.durationPreset.phaseRuntimeSeconds) 秒")
            detailRow("曲线接口", report.sampledInterface ?? "未取得接口采样")
            detailRow("实际接口", report.measuredInterface ?? report.requestedInterface ?? "系统自动选择")
            detailRow("测试节点", report.endpoint ?? "系统自动选择")
            detailRow("代理状态", report.wasProxied ? "检测到代理路径" : "未检测到代理路径")
            detailRow("结果口径", "Mac 到测速节点的阶段平均有效吞吐")
            detailRow("传输流量", report.transferredMegabytes > 0 ? String(format: "约 %.1f MB", report.transferredMegabytes) : "未报告")
            detailRow("有效测试时长", report.durationSeconds.map { String(format: "约 %.1f 秒", $0) } ?? "未报告")

            if let sampled = report.sampledInterface,
               let measured = report.measuredInterface,
               sampled != measured {
                Label(
                    "曲线采样接口为 \(sampled)，系统最终结果报告接口为 \(measured)。两者路径不同，因此曲线只用于观察趋势，最终速度以系统结果为准。",
                    systemImage: "exclamationmark.triangle.fill"
                )
                .font(.caption)
                .foregroundStyle(.orange)
                .padding(.top, 4)
            }
        }
        .padding(AppLayout.cardPadding)
        .appCardStyle()
    }

    private func detailRow(_ title: String, _ value: String) -> some View {
        HStack(alignment: .firstTextBaseline) {
            Text(title)
                .foregroundStyle(.secondary)
                .frame(width: 90, alignment: .leading)
            Text(value)
                .textSelection(.enabled)
            Spacer()
        }
        .font(.callout)
    }

    private func speedAssessments(_ report: SpeedTestReport) -> [(title: String, assessment: MetricAssessment)] {
        [
            ("下载速度", HealthStandards.download(report.downloadMbps)),
            ("上传速度", HealthStandards.upload(report.uploadMbps)),
            ("空闲延迟", HealthStandards.idleLatency(report.idleLatencyMs)),
            ("下载时响应", HealthStandards.responsiveness(report.downloadResponsivenessRPM, subject: "下载时响应")),
            ("上传时响应", HealthStandards.responsiveness(report.uploadResponsivenessRPM, subject: "上传时响应"))
        ]
    }

    private func speedConclusion(
        grade: HealthGrade,
        statusLabel: String,
        namedAssessments: [(title: String, assessment: MetricAssessment)]
    ) -> String {
        let issueTitles = namedAssessments
            .filter { $0.assessment.grade == grade && ($0.assessment.grade == .warning || $0.assessment.grade == .critical) }
            .map { $0.title }
            .joined(separator: "、")

        switch grade {
        case .good:
            return statusLabel == "优秀"
                ? "测速结果优秀，已取得的吞吐与响应指标都很理想"
                : "测速结果正常，已取得的吞吐与响应指标处于正常范围"
        case .warning:
            return "测速结果需要注意：\(issueTitles.isEmpty ? "已测指标" : issueTitles)"
        case .critical:
            return "测速结果存在严重异常：\(issueTitles.isEmpty ? "已测指标" : issueTitles)"
        case .unavailable:
            return "测速结果未取得足够证据"
        }
    }

    private func durationText(_ seconds: Double?) -> String {
        guard let seconds, seconds.isFinite else { return "--" }
        if seconds < 60 { return "约 \(Int(seconds.rounded())) 秒" }
        if seconds < 3_600 {
            let minutes = Int(seconds) / 60
            let remainder = Int(seconds) % 60
            return remainder == 0 ? "约 \(minutes) 分钟" : "约 \(minutes) 分 \(remainder) 秒"
        }
        let hours = Int(seconds) / 3_600
        let minutes = (Int(seconds) % 3_600) / 60
        return minutes == 0 ? "约 \(hours) 小时" : "约 \(hours) 小时 \(minutes) 分"
    }
}

private struct SpeedValueCard: View {
    let title: String
    let systemImage: String
    let color: Color
    let mbps: Double
    let megabytesPerSecond: Double
    let assessment: MetricAssessment
    let impact: String

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Label(title, systemImage: systemImage)
                    .font(.headline)
                    .foregroundStyle(color)
                Spacer()
                StatusBadge(assessment: assessment)
            }
            Text(String(format: "%.1f Mbps", mbps))
                .font(.system(size: 30, weight: .semibold, design: .rounded))
                .monospacedDigit()
            Text(String(format: "≈ %.2f MB/s", megabytesPerSecond))
                .font(.title3.monospacedDigit().weight(.medium))
            Text("已换算为常见文件速率单位")
                .font(.caption)
                .foregroundStyle(.secondary)
            Divider()
            Text(assessment.interpretation)
                .font(.callout)

            Grid(alignment: .leading, horizontalSpacing: 8, verticalSpacing: 7) {
                GridRow(alignment: .top) {
                    Label("影响", systemImage: "arrow.triangle.branch")
                        .frame(width: 58, alignment: .leading)
                    Text(impact)
                }
                GridRow(alignment: .top) {
                    Label("标准", systemImage: "ruler")
                        .frame(width: 58, alignment: .leading)
                    Text(assessment.standard)
                }
            }
            .font(.caption)
            .foregroundStyle(.secondary)
        }
        .padding(16)
        .frame(maxWidth: .infinity, minHeight: 250, alignment: .topLeading)
        .appCardStyle(
            tint: assessment.grade.color,
            tintOpacity: 0.025,
            borderColor: assessment.grade.color.opacity(0.18)
        )
    }
}

private struct TransferTimeCard: View {
    let title: String
    let value: String
    let systemImage: String

    var body: some View {
        VStack(alignment: .leading, spacing: 7) {
            Label(title, systemImage: systemImage)
                .font(.caption)
                .foregroundStyle(.secondary)
            Text(value)
                .font(.title3.monospacedDigit().weight(.semibold))
        }
        .padding(12)
        .frame(maxWidth: .infinity, minHeight: 78, alignment: .leading)
        .appCardStyle()
    }
}

private struct QualityTile: View {
    let title: String
    let value: String
    let assessment: MetricAssessment
    let impact: String

    var body: some View {
        VStack(alignment: .leading, spacing: 7) {
            HStack {
                Text(title)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                Spacer()
                StatusBadge(assessment: assessment)
            }
            Text(value)
                .font(.title3.monospacedDigit().weight(.semibold))
            Text(assessment.interpretation)
                .font(.caption)
            Divider()
            Text("影响：\(impact)")
                .font(.caption)
                .foregroundStyle(.secondary)
            Text("标准：\(assessment.standard)")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
        .padding(12)
        .frame(maxWidth: .infinity, minHeight: 180, alignment: .topLeading)
        .appCardStyle(
            tint: assessment.grade.color,
            tintOpacity: 0.025,
            borderColor: assessment.grade.color.opacity(0.16)
        )
    }
}
