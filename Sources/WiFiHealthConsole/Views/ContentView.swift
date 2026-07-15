import SwiftUI

struct ContentView: View {
    @ObservedObject var store: AppStore
    @SceneStorage("selectedSection") private var selectedSectionRaw = AppSection.overview.rawValue

    private var selection: Binding<AppSection> {
        Binding(
            get: { AppSection(rawValue: selectedSectionRaw) ?? .overview },
            set: { selectedSectionRaw = $0.rawValue }
        )
    }

    var body: some View {
        NavigationSplitView {
            List(AppSection.allCases, selection: selection) { section in
                Label(section.rawValue, systemImage: section.systemImage)
                    .tag(section)
            }
            .listStyle(.sidebar)
            .navigationTitle("Wi-Fi 体检台")
            .navigationSplitViewColumnWidth(min: 170, ideal: 190)
        } detail: {
            detailView(for: selection.wrappedValue)
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
                .toolbar {
                    ToolbarItemGroup {
                        Button {
                            store.refreshCurrentConnection()
                            Task { await store.refreshNetworkContext() }
                        } label: {
                            Label("刷新连接", systemImage: "arrow.clockwise")
                        }
                        .help("刷新连接")

                        Button {
                            selectedSectionRaw = AppSection.diagnosis.rawValue
                            Task { await store.runDiagnosis() }
                        } label: {
                            Label("开始体检", systemImage: "stethoscope")
                        }
                        .help("开始 60 秒体检")
                        .disabled(store.isRunningDiagnosis)

                        Button {
                            selectedSectionRaw = AppSection.speedTest.rawValue
                            Task { await store.runSpeedTest() }
                        } label: {
                            Label("网速测速", systemImage: "speedometer")
                        }
                        .help("测试实际下载和上传速度")
                        .disabled(store.isRunningSpeedTest)
                    }
                }
        }
        .navigationSplitViewStyle(.balanced)
        .alert("采集失败", isPresented: Binding(
            get: { store.errorMessage != nil },
            set: { if !$0 { store.errorMessage = nil } }
        )) {
            Button("好") { store.errorMessage = nil }
        } message: {
            Text(store.errorMessage ?? "未知错误")
        }
        .task {
            while !Task.isCancelled {
                try? await Task.sleep(for: .seconds(5))
                store.refreshCurrentConnection()
            }
        }
    }

    @ViewBuilder
    private func detailView(for section: AppSection) -> some View {
        switch section {
        case .overview: OverviewView(store: store)
        case .diagnosis: DiagnosisView(store: store)
        case .speedTest: SpeedTestView(store: store)
        case .radar: ChannelRadarView(store: store)
        case .history: HistoryView(store: store)
        case .router: RouterView(store: store)
        }
    }
}
