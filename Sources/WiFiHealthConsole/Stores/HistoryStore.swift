import Foundation

final class HistoryStore {
    private let fileURL: URL
    private let encoder: JSONEncoder
    private let decoder: JSONDecoder

    init(fileURL: URL? = nil) {
        let supportDirectory = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!
        self.fileURL = fileURL ?? supportDirectory
            .appendingPathComponent("WiFiHealthConsole", isDirectory: true)
            .appendingPathComponent("history.json")
        encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        encoder.dateEncodingStrategy = .iso8601
        decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
    }

    func load() -> [HistorySample] {
        guard let data = try? Data(contentsOf: fileURL),
              let samples = try? decoder.decode([HistorySample].self, from: data) else {
            return []
        }
        return samples.sorted { $0.timestamp < $1.timestamp }
    }

    func save(_ samples: [HistorySample]) throws {
        try FileManager.default.createDirectory(
            at: fileURL.deletingLastPathComponent(),
            withIntermediateDirectories: true
        )
        let retained = Array(samples.sorted { $0.timestamp < $1.timestamp }.suffix(2_000))
        try encoder.encode(retained).write(to: fileURL, options: .atomic)
    }
}
