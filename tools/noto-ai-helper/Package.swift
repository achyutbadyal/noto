// swift-tools-version: 6.0
import PackageDescription

// The on-device Apple Intelligence helper for Noto's AI field suggestions.
//
// Noto (a .NET app) cannot call Apple's FoundationModels framework directly — it is Swift-only. So this
// tiny binary is the bridge: the C# side starts it, writes {"system", "user"} to stdin and reads the
// model's reply from stdout (see src/Noto.Providers/Ai/AppleOnDeviceProvider.cs).
//
// Deliberately NOT part of Noto.sln: it is a Swift executable the app launches, not a .NET project.
// Build and install it with `mise run ai:helper`.
let package = Package(
    name: "noto-ai-helper",
    // FoundationModels ships with macOS 26. Building for it here means the binary simply won't load on
    // older systems, which is why the C# side also refuses the mode below macOS 26.
    platforms: [.macOS("26.0")],
    targets: [
        .executableTarget(name: "noto-ai-helper", path: "Sources/noto-ai-helper")
    ]
)
