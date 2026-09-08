// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "TokendialCore",
    platforms: [.macOS(.v14)],
    products: [
        .library(name: "TokendialCore", targets: ["TokendialCore"]),
        .executable(name: "tokendial-probe", targets: ["tokendial-probe"])
    ],
    targets: [
        .target(name: "TokendialCore", path: "Sources/TokendialCore"),
        .executableTarget(name: "tokendial-probe", dependencies: ["TokendialCore"], path: "Sources/tokendial-probe"),
        .testTarget(name: "TokendialCoreTests", dependencies: ["TokendialCore"], path: "Tests/TokendialCoreTests")
    ]
)
