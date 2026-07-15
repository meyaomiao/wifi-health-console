import AppKit
import CoreLocation
import CoreWLAN
import Foundation

enum WiFiServiceError: LocalizedError {
    case noInterface
    case scanUnavailable

    var errorDescription: String? {
        switch self {
        case .noInterface: "未找到可用的 Wi-Fi 接口"
        case .scanUnavailable: "附近网络扫描不可用"
        }
    }
}

enum LocationPermissionNextAction: Equatable {
    case requestAuthorization
    case openSystemSettings
    case refresh

    static func resolve(for status: CLAuthorizationStatus) -> Self {
        switch status {
        case .notDetermined:
            .requestAuthorization
        case .denied, .restricted:
            .openSystemSettings
        case .authorizedAlways, .authorizedWhenInUse:
            .refresh
        @unknown default:
            .openSystemSettings
        }
    }

    var buttonTitle: String {
        switch self {
        case .requestAuthorization: "授权"
        case .openSystemSettings: "打开系统设置"
        case .refresh: "刷新"
        }
    }
}

final class LocationPermissionService: NSObject, CLLocationManagerDelegate {
    private let manager = CLLocationManager()
    var onChange: ((CLAuthorizationStatus) -> Void)?

    override init() {
        super.init()
        manager.delegate = self
    }

    var status: CLAuthorizationStatus { manager.authorizationStatus }

    func performNextAction() {
        switch LocationPermissionNextAction.resolve(for: status) {
        case .requestAuthorization:
            manager.requestWhenInUseAuthorization()
        case .openSystemSettings:
            openLocationSettings()
        case .refresh:
            onChange?(status)
        }
    }

    func locationManagerDidChangeAuthorization(_ manager: CLLocationManager) {
        onChange?(manager.authorizationStatus)
    }

    private func openLocationSettings() {
        let destinations = [
            "x-apple.systempreferences:com.apple.settings.PrivacySecurity.extension?Privacy_LocationServices",
            "x-apple.systempreferences:com.apple.preference.security?Privacy_LocationServices"
        ]

        for destination in destinations {
            if let url = URL(string: destination), NSWorkspace.shared.open(url) {
                return
            }
        }

        NSWorkspace.shared.open(URL(fileURLWithPath: "/System/Applications/System Settings.app"))
    }
}

struct WiFiService {
    func currentSnapshot() -> WiFiSnapshot {
        guard let interface = CWWiFiClient.shared().interface() else {
            return .unavailable
        }

        let channel = interface.wlanChannel()
        let rssi = interface.rssiValue()
        let noise = interface.noiseMeasurement()
        return WiFiSnapshot(
            interfaceName: interface.interfaceName,
            ssid: interface.ssid(),
            bssid: interface.bssid(),
            band: band(from: channel?.channelBand),
            channel: channel.map { Int($0.channelNumber) },
            channelWidthMHz: channel.map { width(from: $0.channelWidth) },
            rssi: rssi == 0 ? nil : rssi,
            noise: noise == 0 ? nil : noise,
            transmitRateMbps: interface.transmitRate() == 0 ? nil : interface.transmitRate(),
            ccaPercent: nil
        )
    }

    func scanNearby() async throws -> [NearbyNetwork] {
        try await Task.detached(priority: .userInitiated) {
            guard let interface = CWWiFiClient.shared().interface() else {
                throw WiFiServiceError.noInterface
            }
            let scanned = try interface.scanForNetworks(withSSID: nil)
            return scanned.enumerated().map { index, network in
                let channel = network.wlanChannel
                let bssid = network.bssid
                let ssid = network.ssid ?? "隐藏网络"
                return NearbyNetwork(
                    id: bssid ?? "\(ssid)-\(channel?.channelNumber ?? 0)-\(network.rssiValue)-\(network.noiseMeasurement)-\(index)",
                    ssid: ssid,
                    bssid: bssid,
                    band: Self.band(from: channel?.channelBand),
                    channel: channel.map { Int($0.channelNumber) } ?? 0,
                    channelWidthMHz: channel.map { Self.width(from: $0.channelWidth) } ?? 20,
                    rssi: network.rssiValue,
                    noise: network.noiseMeasurement
                )
            }
            .filter { $0.channel > 0 }
            .sorted {
                if $0.band.rawValue == $1.band.rawValue { return $0.rssi > $1.rssi }
                return $0.band.rawValue < $1.band.rawValue
            }
        }.value
    }

    private func band(from value: CWChannelBand?) -> WiFiBand {
        Self.band(from: value)
    }

    private static func band(from value: CWChannelBand?) -> WiFiBand {
        switch value {
        case .band2GHz: .band2
        case .band5GHz: .band5
        case .band6GHz: .band6
        default: .unknown
        }
    }

    private func width(from value: CWChannelWidth) -> Int {
        Self.width(from: value)
    }

    private static func width(from value: CWChannelWidth) -> Int {
        switch value {
        case .width20MHz: 20
        case .width40MHz: 40
        case .width80MHz: 80
        case .width160MHz: 160
        default: 0
        }
    }
}
