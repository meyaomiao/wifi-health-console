import Foundation

enum DisplayFormat {
    static let dateTime: DateFormatter = {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "zh_CN")
        formatter.dateFormat = "MM-dd HH:mm:ss"
        return formatter
    }()

    static func integer(_ value: Int?, suffix: String) -> String {
        guard let value else { return "--" }
        return "\(value) \(suffix)"
    }

    static func decimal(_ value: Double?, suffix: String, digits: Int = 1) -> String {
        guard let value else { return "--" }
        return String(format: "%.*f %@", digits, value, suffix)
    }
}
