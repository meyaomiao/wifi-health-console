import AppKit
import SwiftUI

struct MenuBarContentView: View {
    @ObservedObject var store: AppStore
    @Environment(\.openWindow) private var openWindow

    var body: some View {
        Text(store.currentSnapshot.ssid ?? "Wi-Fi 未识别")
        Text(store.currentConclusion)
            .foregroundStyle(.secondary)
        Divider()
        Button("打开体检台") {
            openWindow(id: "main")
            NSApp.activate(ignoringOtherApps: true)
        }
        Button(store.isRunningDiagnosis ? "体检进行中" : "开始 60 秒体检") {
            Task { await store.runDiagnosis() }
        }
        .disabled(store.isRunningDiagnosis)
        Button(store.isRunningSpeedTest ? "测速进行中" : "开始网速测速") {
            Task { await store.runSpeedTest() }
        }
        .disabled(store.isRunningSpeedTest)
        Button("扫描附近网络") {
            Task { await store.scanNearby() }
        }
        .disabled(store.isScanning)
        Divider()
        Button("退出") {
            NSApplication.shared.terminate(nil)
        }
    }
}
