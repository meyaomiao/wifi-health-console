import CoreLocation
import Foundation

@MainActor
final class AppStore: ObservableObject {
    @Published var currentSnapshot: WiFiSnapshot = .unavailable
    @Published var nearbyNetworks: [NearbyNetwork] = []
    @Published var history: [HistorySample] = []
    @Published var latestReport: DiagnosticReport?
    @Published var latestSpeedTest: SpeedTestReport?
    @Published var speedTestSamples: [SpeedTestSample] = []
    @Published var liveDownloadMbps = 0.0
    @Published var liveUploadMbps = 0.0
    @Published var speedTestPhase: SpeedTestPhase?
    @Published var speedTestSampledInterface: String?
    @Published var activeSpeedTestDurationPreset: SpeedTestDurationPreset?
    @Published var isRunningDiagnosis = false
    @Published var isRunningSpeedTest = false
    @Published var diagnosisProgress = 0.0
    @Published var diagnosisStage = "准备体检"
    @Published var isScanning = false
    @Published var isDetectingGateway = false
    @Published var errorMessage: String?
    @Published var speedTestError: String?
    @Published var locationStatus: CLAuthorizationStatus
    @Published var networkContext: NetworkContext?

    private let wifi = WiFiService()
    private let diagnostics = NetworkDiagnosticService()
    private let speedTestService = SpeedTestService()
    private let historyStore: HistoryStore
    private let locationPermission = LocationPermissionService()
    private var lastStoredSnapshotAt: Date?

    init(historyStore: HistoryStore = HistoryStore()) {
        self.historyStore = historyStore
        self.history = historyStore.load()
        self.locationStatus = locationPermission.status
        locationPermission.onChange = { [weak self] status in
            Task { @MainActor in
                self?.locationStatus = status
                self?.refreshCurrentConnection(forceStore: false)
            }
        }
        refreshCurrentConnection(forceStore: false)
        Task { [weak self] in
            await self?.refreshNetworkContext()
        }
    }

    var currentGrade: HealthGrade {
        guard currentSnapshot.isConnected else { return .unavailable }
        return HealthStandards.worst(currentWirelessAssessments.map { $0.assessment })
    }

    var currentStatusLabel: String {
        guard currentSnapshot.isConnected else { return "未检测" }
        return HealthStandards.summaryStatusLabel(for: currentWirelessAssessments.map { $0.assessment })
    }

    var locationPermissionActionTitle: String {
        LocationPermissionNextAction.resolve(for: locationStatus).buttonTitle
    }

    var currentConclusion: String {
        switch currentGrade {
        case .good:
            currentStatusLabel == "优秀" ? "无线状态优秀" : "无线状态正常"
        case .warning:
            "无线状态需要注意：\(currentIssueTitles)"
        case .critical:
            "无线状态存在严重异常：\(currentIssueTitles)"
        case .unavailable:
            "无线状态未检测"
        }
    }

    private var currentWirelessAssessments: [(title: String, assessment: MetricAssessment)] {
        [
            ("RSSI", HealthStandards.rssi(currentSnapshot.rssi)),
            ("SNR", HealthStandards.snr(currentSnapshot.snr)),
            ("频宽", HealthStandards.channelWidth(currentSnapshot.channelWidthMHz, band: currentSnapshot.band)),
            ("CCA", HealthStandards.cca(currentSnapshot.ccaPercent))
        ]
    }

    private var currentIssueTitles: String {
        let titles = currentWirelessAssessments
            .filter { $0.assessment.grade == currentGrade }
            .map { $0.title }
        return titles.isEmpty ? "证据不足" : titles.joined(separator: "、")
    }

    func requestLocationAccess() {
        locationPermission.performNextAction()
    }

    func refreshCurrentConnection(forceStore: Bool = true) {
        currentSnapshot = wifi.currentSnapshot()
        guard forceStore, currentSnapshot.isConnected else { return }
        if lastStoredSnapshotAt.map({ Date().timeIntervalSince($0) >= 30 }) ?? true {
            appendHistory(HistorySample(
                timestamp: currentSnapshot.timestamp,
                snapshot: currentSnapshot,
                overallGrade: currentGrade,
                overallStatusLabel: currentStatusLabel,
                gradeScope: .wireless
            ))
            lastStoredSnapshotAt = Date()
        }
    }

    func refreshNetworkContext() async {
        guard !isDetectingGateway else { return }
        isDetectingGateway = true
        let context = await diagnostics.discoverContext()
        networkContext = context
        isDetectingGateway = false
    }

    func scanNearby() async {
        guard !isScanning else { return }
        isScanning = true
        errorMessage = nil
        defer { isScanning = false }
        do {
            nearbyNetworks = try await wifi.scanNearby()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func runDiagnosis(durationSeconds: Int = 60) async {
        guard !isRunningDiagnosis else { return }
        isRunningDiagnosis = true
        diagnosisProgress = 0
        errorMessage = nil
        let report = await diagnostics.run(durationSeconds: durationSeconds) { [weak self] progress, stage in
            Task { @MainActor in
                self?.diagnosisProgress = progress
                self?.diagnosisStage = stage
            }
        }
        latestReport = report
        if let snapshot = report.wirelessSamples.last {
            currentSnapshot = snapshot
            appendHistory(HistorySample(
                timestamp: report.completedAt,
                snapshot: snapshot,
                gatewayAverageMs: report.gatewayPing?.averageMs,
                gatewayJitterMs: report.gatewayPing?.jitterMs,
                gatewayLossPercent: report.gatewayPing?.packetLossPercent,
                internetAverageMs: report.externalPing?.averageMs,
                overallGrade: report.overallGrade,
                overallStatusLabel: report.overallStatusLabel,
                gradeScope: .fullDiagnosis
            ))
        }
        isRunningDiagnosis = false
    }

    func runSpeedTest(
        route: SpeedTestRoute = .currentPath,
        durationPreset: SpeedTestDurationPreset = .standard
    ) async {
        guard !isRunningSpeedTest else { return }
        isRunningSpeedTest = true
        speedTestError = nil
        latestSpeedTest = nil
        speedTestSamples = []
        liveDownloadMbps = 0
        liveUploadMbps = 0
        speedTestPhase = nil
        speedTestSampledInterface = nil
        activeSpeedTestDurationPreset = durationPreset
        defer {
            isRunningSpeedTest = false
            speedTestPhase = nil
            activeSpeedTestDurationPreset = nil
        }

        do {
            let report = try await speedTestService.run(
                route: route,
                wifiInterface: currentSnapshot.interfaceName,
                durationPreset: durationPreset,
                onPhaseChange: { [weak self] phase in
                    Task { @MainActor in
                        self?.speedTestPhase = phase
                    }
                },
                onSamplingInterface: { [weak self] interfaceName in
                    Task { @MainActor in
                        self?.speedTestSampledInterface = interfaceName
                    }
                }
            ) { [weak self] sample in
                Task { @MainActor in
                    guard let self else { return }
                    self.speedTestSamples.append(sample)
                    self.speedTestSamples = Array(self.speedTestSamples.suffix(240))
                    switch sample.phase {
                    case .download:
                        self.liveDownloadMbps = sample.mbps
                    case .upload:
                        self.liveUploadMbps = sample.mbps
                    }
                }
            }
            latestSpeedTest = report
            liveDownloadMbps = report.downloadMbps
            liveUploadMbps = report.uploadMbps
        } catch {
            speedTestError = error.localizedDescription
        }
    }

    func setMarker(_ marker: HistoryMarker, for id: UUID) {
        guard let index = history.firstIndex(where: { $0.id == id }) else { return }
        history[index].marker = marker
        persistHistory()
    }

    func clearHistory() {
        history = []
        persistHistory()
    }

    private func appendHistory(_ sample: HistorySample) {
        history.append(sample)
        history = Array(history.suffix(2_000))
        persistHistory()
    }

    private func persistHistory() {
        do {
            try historyStore.save(history)
        } catch {
            errorMessage = "历史记录保存失败：\(error.localizedDescription)"
        }
    }
}
