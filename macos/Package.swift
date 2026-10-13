// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "Edgepad",
    platforms: [.macOS(.v13)],
    targets: [
        .target(name: "EdgepadCore"),
        .executableTarget(name: "Edgepad", dependencies: ["EdgepadCore"]),
        .testTarget(name: "EdgepadCoreTests", dependencies: ["EdgepadCore"]),
    ]
)
