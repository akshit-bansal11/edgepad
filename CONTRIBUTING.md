# Contributing

Thanks for looking. Edgepad is small on purpose; the bar for a change is that it makes the product better for someone using it, and that it passes the same gate CI runs.

## Before you start

- Read [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). The split between the phone and the laptop apps, what lives in the shared core and what in each app, and what the phone is and is not allowed to send, are deliberate.
- Open an issue for anything bigger than a fix, so the design can be talked through before code exists.

## Setting up

Fork the repository on GitHub and clone your fork. Your work lives on your fork, and reaches this repository as a pull request.

```powershell
git clone https://github.com/<you>/edgepad.git
cd edgepad
git remote add upstream https://github.com/akshit-bansal11/edgepad.git
```

You need JDK 17 and the Android SDK (platform 37, build tools 37.0.0; the Gradle wrapper fetches Gradle itself) for the phone app, and the .NET SDK version pinned in the root `global.json` (10.0.401, roll-forward to the latest feature band) on Windows 10 version 2004 or later for the Windows laptop app. The Linux app and the shared core need only that same .NET SDK and build and test on Windows or Linux; nothing in their gates reaches BlueZ, uinput or an X server, and running the app takes a Linux laptop with BlueZ, systemd-logind, `wpctl` or `pactl`, `xdg-utils` and, for typing text that is not ASCII, `wl-clipboard` or `xclip` (see [docs/LINUX.md](docs/LINUX.md)). The Mac app needs a Mac with Xcode (Swift 5.9 or later) and has no other dependency; its gate runs only there. Any half can be worked on alone: `pwsh scripts/check.ps1 -Only android`, `-Only core`, `-Only windows`, `-Only linux` or `-Only macos` runs just that gate. `core` is the shared library and its tests, which is what to run for a change that touches only `shared/`.

Build each half directly, without the gate, while iterating:

```powershell
cd android && ./gradlew assembleDebug      # phone: builds a debug APK
cd windows && dotnet build Edgepad.slnx -c Release  # Windows laptop: builds the tray app
cd linux && dotnet build Edgepad.Linux.slnx -c Release  # Linux laptop: builds the tray app
cd shared && dotnet test --solution Edgepad.Core.slnx   # the shared core's tests
cd macos && swift build                    # Mac: builds the menu-bar app (swift run starts it)
```

### Running the two apps against each other

This needs a Windows, Mac or Linux laptop and an Android phone, already paired over Bluetooth in the laptop's settings, and it is the one part of the repo you cannot verify from a PR alone:

1. `dotnet run --project windows/src/Edgepad` starts the tray app on a Windows laptop (on a Mac, `swift run` from `macos/`; on Linux, `dotnet run --project linux/src/Edgepad.Linux`, after the one-time uinput setup in [docs/LINUX.md](docs/LINUX.md)). It must already be running before the phone tries to connect — there is no discovery or retry on the laptop side, only the phone reconnects.
2. `cd android && ./gradlew installDebug` installs a debug build on the paired phone (uninstall any release build first: debug builds are versioned `0.0.0-dev` and signed with the debug key, so they will not install over a release build).
3. Open Edgepad on the phone and tap the laptop.

Without both devices, you cannot exercise the Bluetooth handshake, trust-on-first-use, input injection, brightness/volume control, or media session reading — see "Where to start without a device" below for what you can still test.

### Where to start without a device

Most of the logic that matters is plain Kotlin, C# or Swift with no platform dependency, and is exercised by unit tests that run on a CI runner with no phone or laptop attached:

- **Android** — `android/app/src/main/java/me/akshitbansal/edgepad/surface` (gesture recognition, the dials, the corner geometry) and `.../protocol` (frame and id coding) are pure and unit-tested; `.../link` (coalescing, the laptop-state model) too. `.../gamepad` and `.../screens` hold the layout editors and settings UI, which mostly need a device or emulator to see rendered but can still be read and reasoned about.
- **Windows** — `windows/src/Edgepad/Protocol` and `windows/src/Edgepad/Dispatch` are the pure, testable side. `windows/src/Edgepad/Bluetooth`, `Injection`, `Trust` and the audio/brightness/media pieces under `Controls` need real hardware or a live session to verify and are correspondingly harder to test from a PR.
- **Shared core** — `shared/Edgepad.Core` (codec, handshake, trust and PIN, dispatcher, macros, strokes) is pure .NET and is the best place to start on the laptop side; its tests run on Linux in CI.
- **Linux** — `linux/src/Edgepad.Linux/Platform`, `Input` and `Bluetooth/Admission.cs` have unit tests for their parsing, tables and hand-off rules. BlueZ, uinput, the tray and the annotate layer need a real Linux laptop to verify, and [docs/LINUX.md](docs/LINUX.md) lists what nobody has yet.
- **Mac** — `macos/Sources/EdgepadCore` is pure and tested (codec, key map, dispatcher, session and trust). `macos/Sources/Edgepad` needs a real Mac to verify: Bluetooth, input injection, audio and the display.

Start a first change in the protocol codecs, the gesture recognizer, the dial geometry, or the dispatcher's drop paths — see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for how those pieces fit together.

## Making a change

1. Bring your fork's `main` up to date with `upstream/main`, then branch from it on your fork.
2. Keep the apps in step. A protocol change edits `protocol/frames.txt` or `protocol/actions.txt`, all three codecs or enums (Kotlin, the shared C# core's and Swift), `docs/PROTOCOL.md` and the site's tables, and bumps the protocol version on every side if it adds a frame type or changes what a frame means, all in one commit.
3. **OS-independent code goes in `shared/Edgepad.Core`; code that touches an OS stays in that OS's app.** If it would be written the same on Windows and Linux (protocol, handshake, trust, the PIN, routing, the macro list, the stroke model) it belongs in the core, once; if it calls WinRT, COM, WMI, D-Bus, uinput or AppKit, it is the app's and should not be forced behind an interface just to share a shape. Everything in the core is `internal`: do not make a type public, and when a new project needs the core's types, add it to the `InternalsVisibleTo` list in `shared/Edgepad.Core/Edgepad.Core.csproj` (today `Edgepad`, `Edgepad.Tests`, `Edgepad.Core.Tests`, `Edgepad.Linux` and `Edgepad.Linux.Tests`). Moving code into the core is a refactor with its own commit and the tests that moved with it. The Mac app is Swift and cannot share it, so a change to the shared logic needs the same change in `macos/Sources/EdgepadCore`, and the fixtures are what check them against each other.
4. Add or change a test when you add logic, a branch or fix a bug. The recogniser, the dials and the codecs are pure and cheap to test; prefer testing there over anything that needs a device. See "What the tests cover" in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for how the three suites and the protocol fixtures fit together.
5. Run `pwsh scripts/check.ps1` until it is clean. It formats in place, then lints, builds and tests every half it can run with warnings as errors (the shared core, Windows and Linux gates run on any OS with the .NET SDK; the Mac half is skipped off a Mac) — it is the same script CI runs, in its non-mutating `-Ci` mode. It writes locally because fixing your formatting is useful; CI must not write to your branch, so it fails instead of quietly reformatting the pull request. Three things it rejects that are not obvious from the error: Kotlin compiles with `allWarningsAsErrors`, Android lint runs with `warningsAsErrors` (an unused string resource fails the build, not just a warning), and the C# build passes `-warnaserror`. Do not suppress a lint finding to get green.
6. Commit with a [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/) subject (`feat:`, `fix:`, `docs:`, `ci:`, with an optional scope such as `feat(android):`) and a body that says what changed and why, including what you rejected.
7. Add a line under **Unreleased** in [CHANGELOG.md](CHANGELOG.md).
8. Push the branch to your fork and open a pull request against `main` in this repository. CI runs the non-mutating gate on every half and builds the artifacts; the site's gate runs too when `site/` or `protocol/` changed. The maintainer reviews and merges.

## Lock files

Every .NET project pins its packages, transitive ones included, in a committed `packages.lock.json` (`RestorePackagesWithLockFile` in `Directory.Build.props`). CI restores in locked mode, so adding, removing or updating a package without regenerating the lock file fails the build. The projects are `shared/Edgepad.Core`, `shared/Edgepad.Core.Tests`, `windows/src/Edgepad`, `windows/tests/Edgepad.Tests`, `linux/src/Edgepad.Linux` and `linux/tests/Edgepad.Linux.Tests`.

With a .NET SDK, `dotnet restore <project>` rewrites a lock file; commit it with the change that caused it. Without one, the `lockfiles` job in `.github/workflows/ci.yml` does it for you on every push and pull request:

1. Push the change; the CI run's `lockfiles` job runs `dotnet restore --force-evaluate` on the six projects with locked mode switched off.
2. Download its artifact: `gh run download <run-id> -n edgepad-lockfiles`.
3. Copy the `packages.lock.json` files it holds to the same paths in your branch and commit them, in the same pull request as the package change.
4. Push again. The other jobs now restore in locked mode against files that match.

If the first run goes red on the restore before `lockfiles` finishes, that is expected; the job runs even when the others fail. A project that names `RuntimeIdentifiers` (the core lists `win-x64;linux-x64`, the Windows app `win-x64`, the Linux app `linux-x64`) gets those runtimes' packages in its lock file, because `dotnet publish -r <rid>` restores them: add a runtime to the property before regenerating, or the publish step fails in locked mode. Dependabot updates the lock files with each package bump.

## Style

- No UI libraries on the phone, no third-party packages on the Windows laptop beyond NAudio, System.Management and Nefarius.ViGEm.Client, none on the Linux laptop beyond Avalonia and Tmds.DBus.Protocol, none in the shared core, and none at all on the Mac. A new dependency needs a reason in the pull request.
- Constants are named. A number that appears twice is a constant.
- Doc comments say why, not what the next line already says.
- Nothing runs on the input path that could block: no logging, no allocation while drawing, no I/O on the session thread beyond the socket.

## Releases

The maintainer builds and publishes releases. A pull request does not bump the app version, tag, or build a release; a merged change ships in the next one.
