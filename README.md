# Edgepad

Your phone's screen as a control surface for your Windows laptop, over Bluetooth.

Two apps talk to each other: an Android app you touch, and a small tray app on the laptop that carries out what the phone asks for.

- **Edge rulers.** Slide along a ruler to turn it, clockwise to raise. At a corner the ruler wraps the bend as one L, following the screen's own rounded corner; along an edge it runs straight. Six kinds: volume, brightness, media scrub, zoom, app switcher, microphone. Settings drags any of them anywhere along the edge.
- **Trackpad** in the middle, with one-, two-, three- and four-finger gestures.
- **Now playing** at the top: the track, the app playing it, and where it is. Previous, play/pause and next at the bottom.
- **Live state.** The rulers show the laptop's real volume, mute, brightness and position, and follow changes made on the laptop itself.
- Black on white or white on black, portrait or landscape.

## Try it

Download both files from the [latest release](https://github.com/akshit-bansal11/edgepad/releases/latest): `Edgepad.exe` for the laptop, `Edgepad.apk` for the phone. Always install both from the same release: a phone and a laptop from different releases refuse each other at the handshake and say so.

1. Pair the phone with the laptop once, in Windows Bluetooth settings.
2. On the laptop, run `Edgepad.exe`. It is not code-signed, so Windows SmartScreen asks first: choose **More info**, then **Run anyway**. It lives in the system tray; its menu shows its version and whether a phone is connected.
3. On the phone, install `Edgepad.apk` (Android 12 or later; allow installing unknown apps) and allow it the Nearby devices permission.
4. Open Edgepad on the phone and pick your laptop. It remembers the laptop and reconnects when it opens.

The laptop app keeps a log at `%LOCALAPPDATA%\Edgepad\edgepad.log`, reachable from the tray menu.

### Updating the laptop app

From 0.3.0, running a newer `Edgepad.exe` asks the running one to quit and takes over. Versions 0.1.0 and 0.2.0 do not listen for that, so quit them from the tray icon first; the new one tells you if an old one is still in the way.

## What the fingers do

| Fingers | Gesture | Laptop does |
| --- | --- | --- |
| 1 | move / tap / tap then hold-and-move | pointer / left click / drag |
| 2 | drag / pinch / tap | scroll both axes / zoom / right click |
| 3 | left, right / up / down / tap | switch desktop / task view / show desktop / search |
| 4 | left, right / up / down / tap | app switcher (Alt held while the fingers are down) / task view / show desktop / notifications |

A ruler: slide to turn it, clockwise to raise. A tap without a slide runs its action: mute, play/pause, mic mute, task view, reset zoom. Back leaves the controls.

## Publishing a release

Push a version tag. `.github/workflows/release.yml` runs both quality gates, builds a signed APK and a self-contained exe, and attaches them to a GitHub Release:

```
git tag v0.3.0
git push origin v0.3.0
```

The APK is signed with a key that lives only in the repository's Actions secrets and on the machine that made it. Create it once with `pwsh scripts/new-signing-key.ps1` and keep the copy it leaves in `~/.edgepad-signing`: every later release must be signed with the same key, or the installed app refuses the update.

## Layout

| Path | What |
| --- | --- |
| `android/` | The phone app. Kotlin, no UI libraries. |
| `windows/` | The laptop tray app. C#, .NET 10, WinRT Bluetooth and media sessions. |
| `protocol/frames.txt` | The wire format as golden bytes. Both test suites run it, so the two apps cannot drift apart. |
| `protocol/actions.txt` | The action and control ids. Both suites hold their enums to it. |
| `scripts/check.ps1` | The quality gate for both halves. `-Ci` is the non-mutating version CI runs. |

## Building locally

- Android: JDK 17 and the Android SDK, then `pwsh scripts/check.ps1 -Only android`.
- Windows: the .NET 10 SDK, then `pwsh scripts/check.ps1 -Only windows`.

## License

MIT
