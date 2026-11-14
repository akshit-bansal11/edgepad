# Developing Edgepad

How to build, check and test Edgepad. The short version is in the [README](../README.md); how to send a change back (branches, commits, the changelog) is in [CONTRIBUTING.md](../CONTRIBUTING.md).

The code is MIT-licensed, so you can build it, change it and run your own copy. To send a change back, fork the repository on GitHub, then clone your fork:

```powershell
git clone https://github.com/<you>/edgepad.git
cd edgepad
git remote add upstream https://github.com/akshit-bansal11/edgepad.git   # to pull in later changes
```

The repository is a monorepo:

| Path | What |
| --- | --- |
| `android/` | The phone app. Kotlin, Android platform views, no UI framework; AndroidSVG draws the mark and the player logos, and JUnit 4 is the only test dependency. |
| `windows/` | The laptop tray app. C# on .NET 10, WinForms for the tray, WinRT for Bluetooth and media, NAudio for volume, WMI for brightness, ViGEmBus for the virtual controller. |
| `shared/` | The core every .NET laptop app shares: the protocol, trust, the handshake and logging, with its own tests. |
| `linux/` | The Linux tray app. C# on .NET, Avalonia for the tray and windows, BlueZ over D-Bus for Bluetooth, uinput for input. |
| `macos/` | The Mac menu-bar app. Swift, AppKit, IOBluetooth for RFCOMM, CGEvent for input, Core Audio for volume. No third-party dependencies. Swift Package Manager, no Xcode project. |
| `protocol/` | The wire format and the id tables as plain text fixtures. Both test suites run them, so the two apps cannot drift apart. |
| `scripts/` | The quality gate, and the maintainer's release-signing script. |
| `docs/` | Architecture and protocol. |
| `site/` | The website: a landing page at the root, the documentation at `/docs`. Next.js; its own gate, not part of `check.ps1`. |

### Prerequisites

Either half can be worked on alone, so install only what the part you are changing needs.

- **Android:** JDK 17 and the Android SDK with platform 37 and build tools 37.0.0. The Gradle wrapper fetches Gradle itself.
- **Windows:** the .NET 10 SDK (`global.json` pins 10.0.401 and rolls forward to the latest feature band). Windows 10 version 2004 or later, because the app builds against the Windows SDK projection for Bluetooth.
- **Linux:** the .NET SDK, as for Windows. The Linux app and the shared core build and test on Linux.
- **Mac:** a Mac with Xcode (Swift 5.9 or later). The gate for this half runs only on a Mac.
- **Site:** Node 24 or later.
- **The gate:** PowerShell 7 (`pwsh`), which runs `scripts/check.ps1`.

### Quality gate

One script checks all three apps. It formats, lints, builds and tests; CI runs the same script in its non-mutating mode, so the local gate and CI cannot disagree.

```powershell
pwsh scripts/check.ps1                  # formats in place, then checks everything
pwsh scripts/check.ps1 -Only android    # ktlint, Android lint (warnings are errors), unit tests
pwsh scripts/check.ps1 -Only core       # the shared core on its own (CI runs it on Linux): format, build, tests
pwsh scripts/check.ps1 -Only linux      # the Linux app: format, build, tests (CI runs it on Linux)
pwsh scripts/check.ps1 -Only windows    # dotnet format, build (warnings are errors), tests
pwsh scripts/check.ps1 -Only macos      # swift format lint --strict, swift build (warnings are errors), swift test; Mac only
pwsh scripts/check.ps1 -Ci              # what CI runs: fails on unformatted code instead of fixing it
```

A plain `check.ps1` on Windows or Linux skips the Mac half. The site has its own gate, run from `site/`: `npm install` once, then `npm run check` (Biome, ESLint, tsc) and `npm run build`. [site/README.md](../site/README.md) covers the rest.

### Run locally

- **Laptop:** `dotnet run --project windows/src/Edgepad` starts the tray app. A self-contained single-file build, the same shape as a release, is `dotnet publish windows/src/Edgepad -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true`.
- **Mac:** `swift run` from `macos/` starts the app unbundled; `bash macos/bundle.sh 0.0.0-dev out` builds `Edgepad.app` into `out`.
- **Phone:** `cd android && ./gradlew installDebug` installs a debug build on a connected device. Debug builds are versioned `0.0.0-dev` and signed with the debug key, so they do not install over a release build; uninstall the release first.
- **Site:** `cd site && npm run dev` serves it at `http://localhost:3000`.
- **Tests only:** `cd android && ./gradlew testDebugUnitTest` and `cd windows && dotnet test --solution Edgepad.slnx`, and `swift test` from `macos/` on a Mac.

Running the apps against each other needs a Windows laptop or a Mac and an Android phone already paired over Bluetooth. [CONTRIBUTING.md](../CONTRIBUTING.md) says what can still be tested without them.

### Tests

All three suites read the same fixtures. `protocol/frames.txt` holds every frame type as golden bytes; each codec must encode the fields to exactly those bytes and decode the bytes to exactly those fields. `protocol/actions.txt` holds the action and control ids; each enum must match it exactly. On top of that, the Android suite covers the gesture recogniser (the whole finger table, assignable actions, natural scrolling), the shape recogniser and the pad's mode table, the stick's dead zone and scaling, the gamepad layout library (name collisions, deletion, reset), the dials (arming, slop, snapping, steppers, haptic notches), the edge geometry, coalescing and the laptop-state model; the Windows suite covers the dispatcher's drop paths, input batches, trust on first use and its prompt, the media-session name mapping, and what a laptop with no controller driver answers; the Mac suite (`macos/Tests/EdgepadCoreTests`) covers the stream reassembler, the key map, the dispatcher's drop paths and action table, the trust store, the handshake and its refusals, and the update-version parsing. Nothing in the Mac app that touches Bluetooth, CGEvent, Core Audio or the display is tested, and none of it has run on a real Mac. No test sends real input, plugs a controller into the machine running it, or touches a device.
