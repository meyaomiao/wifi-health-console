import CoreLocation
import SwiftUI

struct ChannelRadarView: View {
    @ObservedObject var store: AppStore
    @State private var selectedBand: WiFiBand = .band5

    private var filteredNetworks: [NearbyNetwork] {
        store.nearbyNetworks.filter { $0.band == selectedBand }
    }

    var body: some View {
        PageContainer {
            PageHeader(
                title: "信道雷达",
                subtitle: "查看可见网络频谱，并获得频段、频宽和信道建议",
                trailing: AnyView(scanButton)
            )

            if needsLocationPermission {
                PermissionBanner(
                    statusText: permissionText,
                    actionTitle: store.locationPermissionActionTitle
                ) {
                    store.requestLocationAccess()
                }
            }

            Picker("频段", selection: $selectedBand) {
                ForEach([WiFiBand.band2, .band5, .band6]) { band in
                    Text(band.rawValue).tag(band)
                }
            }
            .pickerStyle(.segmented)
            .frame(maxWidth: 420, alignment: .leading)

            if store.isScanning, store.nearbyNetworks.isEmpty {
                ProgressView("正在扫描附近网络")
                    .frame(maxWidth: .infinity, minHeight: 260)
            } else if filteredNetworks.isEmpty {
                ContentUnavailableView(
                    "此频段暂无扫描结果",
                    systemImage: "dot.radiowaves.left.and.right",
                    description: Text(needsLocationPermission ? "允许定位权限后重新扫描。" : "附近可能没有此频段网络，或扫描结果尚未返回。")
                )
                .frame(maxWidth: .infinity, minHeight: 280)
            } else {
                ChannelSpectrumChart(
                    band: selectedBand,
                    networks: filteredNetworks,
                    current: store.currentSnapshot
                )

                Text("图中所有曲线均来自本次 CoreWLAN 扫描。曲线宽度依据主信道与频宽近似计算；macOS 不提供中心信道、隐藏网络身份和实际 CCA，因此这些部分不会伪造精度。")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }

            ChannelSuggestionsView(
                title: "频段、频宽与信道建议",
                suggestions: ChannelAnalysis.suggestions(
                    current: store.currentSnapshot,
                    networks: store.nearbyNetworks
                )
            )
        }
        .task {
            if store.currentSnapshot.band != .unknown {
                selectedBand = store.currentSnapshot.band
            }
            if store.nearbyNetworks.isEmpty, !store.isScanning {
                await store.scanNearby()
            }
        }
    }

    private var scanButton: some View {
        Button {
            Task { await store.scanNearby() }
        } label: {
            Label(store.isScanning ? "扫描中" : "重新扫描", systemImage: "dot.radiowaves.left.and.right")
        }
        .buttonStyle(.borderedProminent)
        .disabled(store.isScanning)
    }

    private var needsLocationPermission: Bool {
        switch store.locationStatus {
        case .authorizedAlways, .authorizedWhenInUse: false
        default: true
        }
    }

    private var permissionText: String {
        switch store.locationStatus {
        case .denied:
            "此前已拒绝定位权限；请打开系统设置允许后重新扫描。"
        case .restricted:
            "定位权限受到系统限制；请打开系统设置检查后重新扫描。"
        default:
            "授权后才能识别 SSID/BSSID，并在图中准确标出当前网络。"
        }
    }
}
