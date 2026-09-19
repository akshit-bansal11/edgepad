<p align="center"><picture><source media="(prefers-color-scheme: dark)" srcset="assets/edgepad-dark.svg"><img src="assets/edgepad-light.svg" width="96" alt="Edgepad"></picture></p>

# Edgepad

Edgepad turns an Android phone into a trackpad, a media remote and a control panel for a Windows laptop, over a direct Bluetooth link with nothing in between.

<p align="center"><img src="assets/demo.gif" width="880" alt="A finger crossing the phone's trackpad, then sliding the corner ruler to change the laptop's volume"></p>

<p align="center"><sub>The phone's screen. The finger crosses the trackpad, then slides the corner ruler and the volume follows.</sub></p>

It is for the times the laptop is across the room rather than under your hands: plugged into a television, docked on a desk you are not sitting at, parked somewhere a mouse dongle will not reach. The alternatives are a remote app that only sends media keys, or a remote-desktop app that streams the whole screen and wants an account and a network round trip to change the volume. Edgepad is neither. The phone and the laptop pair once, the way a headset does, and after that they talk directly: no account, no Wi-Fi, no service in between that can be slow or down. The cost is Bluetooth's own — both ends need it, and it does not reach another room or the internet.

[![CI](https://github.com/akshit-bansal11/edgepad/actions/workflows/ci.yml/badge.svg)](https://github.com/akshit-bansal11/edgepad/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/akshit-bansal11/edgepad)](https://github.com/akshit-bansal11/edgepad/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Documentation](https://img.shields.io/badge/docs-edgepad--docs.vercel.app-black.svg)](https://edgepad-docs.vercel.app)

## What it does

- **Corner rulers.** Each corner of the phone holds a dial drawn as a ruler that wraps the bend. Slide along it, clockwise to raise. Volume, brightness, media scrub, zoom, app switcher or microphone level; Settings picks what each corner does, how long the rulers are and how tall.
- **Trackpad.** Everything between the corners moves the laptop's pointer. One finger moves and clicks, two scroll and pinch, three and four fingers do whatever you assign them: desktops, task view, media, volume, app switcher, and more.
- **Media.** The track, the app playing it and where it is, with previous, play/pause and next. Drag the three pieces anywhere on the surface.
- **Keyboard and gamepad.** A full on-screen keyboard whose modifiers work held or tapped, and a gamepad with editable layouts and presets. Both open sideways.
- **Live state.** The dials show the laptop's real volume, mute, brightness and playback position, and follow changes made on the laptop itself.
- **Guide.** Five pages on the first run, and again from Settings, one at a time or on one page.
- Black on white or white on black, held upright or sideways: a toggle in Settings, no free rotation.

## Install

Both apps come from the [latest release](https://github.com/akshit-bansal11/edgepad/releases/latest). Always install both from the same release: the two refuse each other at the handshake when their protocol versions differ, and say so.

### Laptop (Windows 10 version 2004 or later, 64-bit)

1. Download `Edgepad.exe`. It is a single self-contained file; nothing else needs installing.
2. Run it. It is not code-signed, so SmartScreen asks first: choose **More info**, then **Run anyway**.
3. It lives in the system tray. The menu shows the version, whether a phone is connected, **Start with Windows**, **Forget trusted phone**, **Open log** and **Quit**.

Running a newer `Edgepad.exe` asks the running copy to quit and takes its place.

### Phone (Android 12 or later)

1. Download `Edgepad.apk` and open it. Allow installing from this source if asked.
2. On first run, allow the **Nearby devices** permission. Edgepad uses it to see the laptops paired with the phone; it never scans for new ones.
3. Every later release installs over the previous one; settings are kept.

## Use

1. Pair the phone with the laptop once, in Windows **Settings > Bluetooth & devices**.
2. Keep Edgepad running in the laptop's tray.
3. Open Edgepad on the phone and tap the laptop. The first phone to connect becomes the laptop's trusted phone; any other paired phone is refused until you choose **Forget trusted phone** in the tray menu.
4. The phone remembers the laptop and reconnects when the app opens. If the link drops, the phone retries ten times, two seconds apart, and says so.

### The control surface

| Where | Touch | Laptop does |
| --- | --- | --- |
| A corner ruler | slide | turns that dial: clockwise raises |
| A corner ruler | tap | the dial's action: mute, play/pause, mic mute, task view, reset zoom |
| Anywhere else, one finger | move / tap / tap then hold-and-move | pointer / left click / drag |
| Two fingers | drag | scroll both axes |
| Two fingers | pinch / tap | zoom / right click, or what Settings assigns |
| Three or four fingers | tap, swipe up, down, left, right | what Settings assigns |
| Top centre | tap the gear, the keyboard or the gamepad | Settings, the keyboard, the gamepad |
| Back | | leaves the surface; the link stays up |

Out of the box, three fingers left and right switch desktops, three up opens task view, three down shows the desktop, four fingers left and right walk the app switcher (Alt stays held while the fingers are down), and tapping searches or opens notifications. Every one of those is changed under **Settings > Gestures**.

### Settings

- **Connection:** the remembered laptop with its round trip, Forget, and whether to reconnect automatically.
- **Surface:** Corners (which dial each corner holds, or none), Gestures (the gesture map and on-screen hints), Dial feel (slide sensitivity, dial length and height, haptic ticks, snapping to round numbers, with a live preview), and natural scrolling.
- **Layouts:** the media layout and the gamepad layout, each a full-screen canvas where pieces are dragged anywhere; the options button holds the size, the presets and reset.
- **Appearance:** dark or light, upright or sideways, and Background & pattern (control colour, a colour, gradient or image behind the surface, and a grid, dots or checker over it).
- **Help:** the guide.

## How it works

The phone recognises gestures and sends small semantic frames: move the pointer by so much, press a button, scroll, run action 3, set control 0 to 55. The laptop owns the table of what each action does and executes it with the Windows input, audio, display and media APIs. The phone can name an action; it can never send a key or a command line. The laptop sends its own state back (volume, mute, brightness, what is playing) so the dials show real values.

The transport is Bluetooth Classic RFCOMM: an ordered, encrypted byte stream between two already-paired devices, with no server, no discovery and no network. Frames are 2 to 258 bytes and go out in a single write per batch; a backlog of pointer moves collapses into one before it is sent, so a slow link catches up instead of lagging.

The full documentation is at [edgepad-docs.vercel.app](https://edgepad-docs.vercel.app) — install, the control surface, both apps' architecture, the wire protocol and the developer guide on one page. In the repository: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) describes both apps, their threads and their trust model. [docs/PROTOCOL.md](docs/PROTOCOL.md) is the wire format. [CHANGELOG.md](CHANGELOG.md) lists what each release changed.

## Build it yourself

The repository is a monorepo:

| Path | What |
| --- | --- |
| `android/` | The phone app. Kotlin, Android platform views, no UI libraries; JUnit 4 is the only dependency. |
| `windows/` | The laptop tray app. C# on .NET 10, WinForms for the tray, WinRT for Bluetooth and media, NAudio for volume, WMI for brightness. |
| `protocol/` | The wire format and the id tables as plain text fixtures. Both test suites run them, so the two apps cannot drift apart. |
| `scripts/` | The quality gate and the release-key script. |
| `docs/` | Architecture and protocol. |
| `site/` | The documentation site. Next.js; its own gate, not part of `check.ps1`. |

### Prerequisites

- **Android:** JDK 17 and the Android SDK with platform 37 and build tools 37.0.0. The Gradle wrapper fetches Gradle itself.
- **Windows:** the .NET 10 SDK (`windows/global.json` pins the feature band). Windows 10 version 2004 or later, because the app builds against the Windows SDK projection for Bluetooth.

### Quality gate

One script checks both halves. It formats, lints, builds and tests; CI runs the same script in its non-mutating mode, so the local gate and CI cannot disagree.

```powershell
pwsh scripts/check.ps1                  # formats in place, then checks everything
pwsh scripts/check.ps1 -Only android    # ktlint, Android lint (warnings are errors), unit tests
pwsh scripts/check.ps1 -Only windows    # dotnet format, build (warnings are errors), tests
pwsh scripts/check.ps1 -Ci              # what CI runs: fails on unformatted code instead of fixing it
```

### Run locally

- **Laptop:** `dotnet run --project windows/src/Edgepad` starts the tray app. A self-contained single-file build, the same as a release, is `dotnet publish windows/src/Edgepad -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true`.
- **Phone:** `cd android && ./gradlew installDebug` installs a debug build on a connected device. Debug builds are versioned `0.0.0-dev` and signed with the debug key, so they do not install over a release build; uninstall the release first.
- **Tests only:** `cd android && ./gradlew testDebugUnitTest` and `cd windows && dotnet test --solution Edgepad.slnx`.

The laptop app logs to `%LOCALAPPDATA%\Edgepad\edgepad.log` (also in the tray menu): connections, refusals, dropped frames, and input batches Windows refused because an elevated window had focus.

### Tests

Both suites read the same fixtures. `protocol/frames.txt` holds every frame type as golden bytes; each codec must encode the fields to exactly those bytes and decode the bytes to exactly those fields. `protocol/actions.txt` holds the action and control ids; each enum must match it exactly. On top of that, the Android suite covers the gesture recogniser (the whole finger table, assignable actions, natural scrolling), the dials (arming, slop, snapping, steppers, haptic notches), the edge geometry, coalescing and the laptop-state model; the Windows suite covers the dispatcher's drop paths, input batches, trust on first use and the media-session name mapping. No test sends real input or touches a device.

## Releases

Push a version tag and `.github/workflows/release.yml` does the rest: both quality gates, a signed release APK, a self-contained exe, and a GitHub Release with both attached under stable names, so `releases/latest/download/Edgepad.apk` and `.../Edgepad.exe` always point at the newest build.

```
git tag v0.6.2
git push origin v0.6.2
```

The APK's signing key lives only in the repository's Actions secrets and on the machine that made it. `pwsh scripts/new-signing-key.ps1` creates it once and sets the secrets without printing them. Every release must be signed with the same key, or the installed app refuses the update.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Bug reports and feature requests use the issue templates; pull requests run the same gate as CI.

## License

[MIT](LICENSE).

## Credits

Icons are [Lucide](https://lucide.dev), ISC licence. The typeface is [JetBrains Mono](https://github.com/JetBrains/JetBrainsMono), SIL Open Font License 1.1; its licence ships in the APK under `assets/licenses`. Player logos belong to their owners and are drawn as supplied.
