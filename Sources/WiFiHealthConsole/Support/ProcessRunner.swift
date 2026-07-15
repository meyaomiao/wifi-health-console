import Foundation

struct ProcessResult {
    var status: Int32
    var output: String
    var error: String
}

enum ProcessRunner {
    static func run(_ executable: String, _ arguments: [String]) async -> ProcessResult {
        await withCheckedContinuation { continuation in
            DispatchQueue.global(qos: .utility).async {
                let process = Process()
                let stdout = Pipe()
                let stderr = Pipe()
                process.executableURL = URL(fileURLWithPath: executable)
                process.arguments = arguments
                process.standardOutput = stdout
                process.standardError = stderr

                do {
                    try process.run()
                    process.waitUntilExit()
                    let outputData = stdout.fileHandleForReading.readDataToEndOfFile()
                    let errorData = stderr.fileHandleForReading.readDataToEndOfFile()
                    continuation.resume(returning: ProcessResult(
                        status: process.terminationStatus,
                        output: String(decoding: outputData, as: UTF8.self),
                        error: String(decoding: errorData, as: UTF8.self)
                    ))
                } catch {
                    continuation.resume(returning: ProcessResult(
                        status: -1,
                        output: "",
                        error: error.localizedDescription
                    ))
                }
            }
        }
    }
}
