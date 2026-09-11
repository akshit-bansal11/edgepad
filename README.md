# Edgepad

Your phone's screen as a control surface for your Windows laptop, over Bluetooth.

Two apps talk to each other: an Android app you touch, and a small tray app on the laptop that carries out what the phone asks for.

- **Edge dials**, drawn as ruler ticks. Press, hold and drag around one to turn it, like a camera's zoom dial. Six kinds: volume, brightness, media seek, zoom, app switcher, microphone. Settings puts any of them anywhere along any edge, corners included, or a little way in from a rounded corner.
- **Trackpad** in the middle, with the Windows one-, two-, three- and four-finger gestures.
- **Media bar** along the top: what is playing and in which app, previous, play/pause, next, and a progress line.
- **Live state**: the dials show the laptop's real volume, mute, brightness and playback position, and follow changes made on the laptop itself.
- Black on white or white on black, portrait or landscape.

## Try it

Download both files from the [latest release](https://github.com/akshit-bansal11/edgepad/releases/latest): `Edgepad.exe` for the laptop, `Edgepad.apk` for the phone. Update both together: the protocol between them grows with each release.

1. Pair the phone with the laptop once, in Windows Bluetooth settings.
2. On the laptop, run `Edgepad.exe`. It is not code-signed, so Windows SmartScreen asks first: choose **More info**, then **Run anyway**. It lives in the system tray; its menu shows whether a phone is connected.
3. On the phone, install `Edgepad.apk` (Android 12 or later; allow installing unknown apps) and allow it the Nearby devices permission.
4. Open Edgepad on the phone and tap your laptop. It remembers the laptop and reconnects when it opens.

The laptop app keeps a log at `%LOCALAPPDATA%\Edgepad\edgepad.log`, reachable from the tray menu.

## What the fingers do

| Fingers | Gesture | Laptop does |
| --- | --- | --- |
| 1 | move / tap / tap then hold-and-move | pointer / left click / drag |
| 2 | drag / pinch / tap | scroll both axes / zoom / right click |
| 3 | left, right / up / down / tap | switch desktop / task view / show desktop / search |
| 4 | left, right / up / down / tap | app switcher (Alt held while the fingers are down) / task view / show desktop / notifications |

A dial: press and hold to arm, drag to turn; sweep up on the sides or right along the top and bottom to raise it. A tap without a hold runs the dial's action: mute, play/pause, mic mute, task view, reset zoom. Back leaves the controls.

## Publishing a release

Push a version tag. `.github/workflows/release.yml` runs both quality gates, builds a signed APK and a self-contained exe, and attaches them to a GitHub Release:

```
git tag v0.2.0
git push origin v0.2.0
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
