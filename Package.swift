// swift-tools-version: 6.2
import PackageDescription

let package = Package(
    name: "WiFiHealthConsole",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "WiFiHealthConsole", targets: ["WiFiHealthConsole"])
    ],
    targets: [
        .executableTarget(
            name: "WiFiHealthConsole",
            path: "Sources/WiFiHealthConsole"
        ),
        .testTarget(
            name: "WiFiHealthConsoleTests",
            dependencies: ["WiFiHealthConsole"],
            path: "Tests/WiFiHealthConsoleTests"
        )
    ],
    swiftLanguageModes: [.v5]
)
