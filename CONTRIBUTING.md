# Contributing

Thanks for looking. Edgepad is small on purpose; the bar for a change is that it makes the product better for someone using it, and that it passes the same gate CI runs.

## Before you start

- Read [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). The split between the two apps, and what the phone is and is not allowed to send, are deliberate.
- Open an issue for anything bigger than a fix, so the design can be talked through before code exists.

## Setting up

You need JDK 17 and the Android SDK (platform 37, build tools 37.0.0; the Gradle wrapper fetches Gradle itself) for the phone app, and the .NET SDK version pinned in `windows/global.json` (10.0.401, roll-forward to the latest feature band) on Windows 10 version 2004 or later for the laptop app. Either half can be worked on alone: `pwsh scripts/check.ps1 -Only android` or `-Only windows` runs just that gate.

Build each half directly, without the gate, while iterating:

```powershell
cd android && ./gradlew assembleDebug      # phone: builds a debug APK
cd windows && dotnet build Edgepad.slnx -c Release  # laptop: builds the tray app
```

### Running the two apps against each other

This needs a Windows machine and an Android phone, already paired over Bluetooth in Windows Settings, and it is the one part of the repo you cannot verify from a PR alone:

1. `dotnet run --project windows/src/Edgepad` starts the tray app on the laptop. It must already be running before the phone tries to connect — there is no discovery or retry on the laptop side, only the phone reconnects.
2. `cd android && ./gradlew installDebug` installs a debug build on the paired phone (uninstall any release build first: debug builds are versioned `0.0.0-dev` and signed with the debug key, so they will not install over a release build).
3. Open Edgepad on the phone and tap the laptop.

Without both devices, you cannot exercise the Bluetooth handshake, trust-on-first-use, input injection, brightness/volume control, or media session reading — see "Where to start without a device" below for what you can still test.

### Where to start without a device

Most of the logic that matters is plain Kotlin/C# with no platform dependency, and is exercised by unit tests that run on a CI runner with no phone or laptop attached:

- **Android** — `android/app/src/main/java/me/akshitbansal/edgepad/surface` (gesture recognition, the dials, the corner geometry) and `.../protocol` (frame and id coding) are pure and unit-tested; `.../link` (coalescing, the laptop-state model) too. `.../gamepad` and `.../screens` hold the layout editors and settings UI, which mostly need a device or emulator to see rendered but can still be read and reasoned about.
- **Windows** — `windows/src/Edgepad/Protocol` and `windows/src/Edgepad/Dispatch` are the pure, testable side. `windows/src/Edgepad/Bluetooth`, `Injection`, `Trust` and the audio/brightness/media pieces under `Controls` need real hardware or a live session to verify and are correspondingly harder to test from a PR.

Start a first change in the protocol codecs, the gesture recognizer, the dial geometry, or the dispatcher's drop paths — see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for how those pieces fit together.

## Making a change

1. Branch from `main`.
2. Keep the two apps in step. A protocol change edits `protocol/frames.txt` or `protocol/actions.txt`, both codecs or both enums, and bumps the protocol version on both sides, all in one commit.
3. Add or change a test when you add logic, a branch or fix a bug. The recogniser, the dials and the codecs are pure and cheap to test; prefer testing there over anything that needs a device. See "What the tests cover" in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for how the two suites and the protocol fixtures fit together.
4. Run `pwsh scripts/check.ps1` until it is clean. It formats in place, then lints, builds and tests both halves with warnings as errors — it is the same script CI runs, in its non-mutating `-Ci` mode. It writes locally because fixing your formatting is useful; CI must not write to your branch, so it fails instead of quietly reformatting the pull request. Three things it rejects that are not obvious from the error: Kotlin compiles with `allWarningsAsErrors`, Android lint runs with `warningsAsErrors` (an unused string resource fails the build, not just a warning), and the C# build passes `-warnaserror`. Do not suppress a lint finding to get green.
5. Commit with a [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/) subject (`feat:`, `fix:`, `docs:`, `ci:`, with an optional scope such as `feat(android):`) and a body that says what changed and why, including what you rejected.
6. Add a line under **Unreleased** in [CHANGELOG.md](CHANGELOG.md).
7. Open a pull request. CI runs the non-mutating gate on both halves and builds both artifacts.

## Style

- No UI libraries on the phone, no third-party packages on the laptop beyond NAudio and System.Management. A new dependency needs a reason in the pull request.
- Constants are named. A number that appears twice is a constant.
- Doc comments say why, not what the next line already says.
- Nothing runs on the input path that could block: no logging, no allocation while drawing, no I/O on the session thread beyond the socket.

## Releasing

Maintainers only. Update the version table in `docs/PROTOCOL.md` if the protocol changed, move **Unreleased** in the changelog under the new version, then tag and push; the Release workflow does the rest. The signing key is not in the repository and must not be regenerated: a new key would stop installed phones from updating.
