// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "Edgepad",
    defaultLocalization: "en",
    platforms: [.macOS(.v13)],
    targets: [
        .target(name: "EdgepadCore", resources: [.process("Resources")]),
        .executableTarget(
            name: "Edgepad", dependencies: ["EdgepadCore"], resources: [.process("Resources")]),
        .testTarget(name: "EdgepadCoreTests", dependencies: ["EdgepadCore"]),
    ]
)
