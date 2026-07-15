import Foundation

enum GatewayAddress {
    static func managementURL(for address: String?) -> URL? {
        guard let address, !address.isEmpty else { return nil }
        var components = URLComponents()
        components.scheme = "http"
        components.host = address
        return components.url
    }

    static func isPrivateOrLocal(_ address: String) -> Bool {
        let lowercased = address.lowercased()
        if lowercased == "localhost" || lowercased.hasPrefix("fe80:") || lowercased.hasPrefix("fc") || lowercased.hasPrefix("fd") {
            return true
        }

        let parts = address.split(separator: ".").compactMap { Int($0) }
        guard parts.count == 4, parts.allSatisfy({ (0...255).contains($0) }) else { return false }

        if parts[0] == 10 || parts[0] == 127 { return true }
        if parts[0] == 172, (16...31).contains(parts[1]) { return true }
        if parts[0] == 192, parts[1] == 168 { return true }
        if parts[0] == 169, parts[1] == 254 { return true }
        if parts[0] == 100, (64...127).contains(parts[1]) { return true }
        return false
    }
}
