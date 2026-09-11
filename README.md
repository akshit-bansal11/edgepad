# Edgepad

Your phone's screen as a control surface for your Windows laptop, over Bluetooth.

Two apps talk to each other: an Android app you touch, and a small tray app on the laptop that carries out what the phone asks for.

**Status: milestone 1 of 6.** The Bluetooth link works end to end and the phone shows the measured round-trip time. The controls below are the plan, not yet built:

- **Corner dials** drawn as ruler ticks. Press, hold and drag around a corner to turn volume, brightness and more, like a camera's zoom dial.
- **Full-screen mode**: the centre of the screen is a trackpad with the Windows two-, three- and four-finger gestures.
- **Overlay mode**: just the corner dials, floating over whatever else is on the phone.

## Try it

1. Pair the phone with the laptop once, in Windows Bluetooth settings.
2. On the laptop, run `Edgepad.exe`. It lives in the system tray; its menu shows whether a phone is connected.
3. On the phone, install the APK (Android 12 or later) and allow it the Nearby devices permission.
4. Open Edgepad on the phone and tap your laptop.

Both builds come from CI: open the latest run under **Actions** and download `edgepad-android` and `edgepad-windows`.

The laptop app keeps a log at `%LOCALAPPDATA%\Edgepad\edgepad.log`, reachable from the tray menu.

## Layout

| Path | What |
| --- | --- |
| `android/` | The phone app. Kotlin, no UI libraries. |
| `windows/` | The laptop tray app. C#, .NET 10, WinRT Bluetooth. |
| `protocol/frames.txt` | The wire format as golden bytes. Both test suites run it, so the two apps cannot drift apart. |
| `scripts/check.ps1` | The quality gate for both halves. `-Ci` is the non-mutating version CI runs. |

## Building locally

- Android: JDK 17 and the Android SDK, then `pwsh scripts/check.ps1 -Only android`.
- Windows: the .NET 10 SDK, then `pwsh scripts/check.ps1 -Only windows`.

## License

MIT
