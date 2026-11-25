# Edgepad for Android

The phone app: Kotlin on Android platform views, with no UI framework. The control surface is one custom `View` that draws everything and reads raw `MotionEvent`s, so a touch becomes a frame with nothing buffering it. AndroidSVG draws the player logos; JUnit 4 is the only test dependency. The phone recognises and the laptop executes: every gesture leaves here as a semantic frame ([docs/ARCHITECTURE.md](../docs/ARCHITECTURE.md#the-split)).

| Package under `app/src/main/java/me/akshitbansal/edgepad` | What is in it |
| --- | --- |
| (root) | `MainActivity`, which owns the screens and the link, `ShareActivity` for Share > Edgepad, `Settings`, `Palette` and the logo lookup. |
| `protocol/` | Frames, the codec and the id enums, checked against `protocol/frames.txt` and `actions.txt`. |
| `link/` | Finding laptops, saved laptops, the connection to each (Bluetooth and TCP) and `LaptopState`, the laptop's last report. |
| `link/net/` | The encrypted network link: key exchange, the pairing code and the secure channel. |
| `link/blob/`, `link/xfer/` | Blobs (clipboard, icons, Git, approvals) and file transfers over the second socket. |
| `link/notify/` | Laptop notifications shown on the phone, and agent approvals. |
| `surface/` | The control surface: the trackpad recogniser, dials, shapes, media, widgets, batteries and the air mouse. |
| `screens/` | Every other screen and sheet: keyboard, gamepad, presenter, annotate, capture, settings pages, layout editors and the hub pages. |
| `gamepad/` | Gamepad layouts, their geometry and where they are saved. |
| `overlay/` | The floating corner dials drawn over other apps. |
| `update/` | The in-app updater. |

`app/src/debug/.../gallery/` is a debug-only activity that renders one screen with sample state, so CI can screenshot every screen. Unit tests are in `app/src/test`.

Check with `pwsh scripts/check.ps1 -Only android` (ktlint, Android lint with warnings as errors, unit tests), or install a debug build with `cd android && ./gradlew installDebug`. More in [docs/DEVELOPING.md](../docs/DEVELOPING.md).
