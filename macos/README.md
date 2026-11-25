# Edgepad for Mac

The Mac menu-bar app: Swift and AppKit, IOBluetooth for RFCOMM, CGEvent for input and Core Audio for volume, with no third-party dependencies. It is a Swift Package Manager package with no Xcode project. The Mac cannot reference the .NET core the Windows and Linux apps share, so it keeps its own Swift port of the same logic, held to the same `protocol/` fixtures. The design is under "The Mac app" in [docs/ARCHITECTURE.md](../docs/ARCHITECTURE.md#the-mac-app).

| Path | What is in it |
| --- | --- |
| `Sources/EdgepadCore/` | The port of [shared/Edgepad.Core](../shared/Edgepad.Core/README.md): frames and ids, the handshake and the network link, trust and the PIN, the dispatcher, macros, strokes, transfers, the dashboard and updates. No AppKit, so it runs as unit tests on a machine with no Bluetooth. `Seams.swift` holds the protocols the app fills in. |
| `Sources/Edgepad/` | The app: `main.swift` and `AppDelegate` (the menu bar), the RFCOMM and network servers, `SessionHost`, input, audio, displays, now playing, and every window and panel. |
| `Tests/EdgepadCoreTests/` | The core's tests, including the shared fixtures. |
| `Localization/` | `MacStrings.*.resx`, the Mac's UI words in every language; `scripts/resx-to-lproj.ps1` turns them into `.lproj` strings. |
| `Resources/`, `bundle.sh` | The app icon, and the script that builds `Edgepad.app`. |

On a Mac, check with `pwsh scripts/check.ps1 -Only macos` (`swift format lint --strict`, `swift build` with warnings as errors, `swift test`). Run it unbundled with `swift run` from `macos/`, or build the app with `bash macos/bundle.sh 0.0.0-dev out`. More in [docs/DEVELOPING.md](../docs/DEVELOPING.md).
