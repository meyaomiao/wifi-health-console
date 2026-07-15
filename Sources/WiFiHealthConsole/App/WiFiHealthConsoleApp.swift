import AppKit
import SwiftUI

final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.regular)
        NSApp.activate(ignoringOtherApps: true)
    }
}

@main
struct WiFiHealthConsoleApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
    @StateObject private var store = AppStore()

    var body: some Scene {
        WindowGroup("Wi-Fi 体检台", id: "main") {
            ContentView(store: store)
        }
        .defaultSize(width: 1_080, height: 720)
        .commands {
            CommandMenu("体检") {
                Button("刷新当前连接") {
                    store.refreshCurrentConnection()
                }
                .keyboardShortcut("r", modifiers: .command)

                Button("开始 60 秒体检") {
                    Task { await store.runDiagnosis() }
                }
                .keyboardShortcut("d", modifiers: [.command, .shift])
                .disabled(store.isRunningDiagnosis)

                Button("开始网速测速") {
                    Task { await store.runSpeedTest() }
                }
                .keyboardShortcut("t", modifiers: [.command, .shift])
                .disabled(store.isRunningSpeedTest)

                Button("扫描附近网络") {
                    Task { await store.scanNearby() }
                }
                .keyboardShortcut("s", modifiers: [.command, .shift])
                .disabled(store.isScanning)
            }
        }

        MenuBarExtra("Wi-Fi 体检台", systemImage: "wifi") {
            MenuBarContentView(store: store)
        }
    }
}
