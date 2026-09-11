# Architecture

Edgepad is one product in two programs. This page is the map of both and of the seams between them: what runs where, on which thread, what trusts what, and why.

```
Android phone                                   Windows laptop
--------------------------------------          ----------------------------------------
MainActivity                                    Edgepad.exe (WinForms tray, one instance)
  screens: onboarding, pairing, settings,         TrayContext: menu, status, run at login
           gestures, media layout,                RfcommServer: advertises the service
           connection lost                          Session (one per phone, own thread)
  ControlSurface (one View, all drawn)                FrameCodec -> Dispatcher
    TrackpadRecognizer -> frames                          InputInjector (SendInput)
    Dial x4 -> SET / ACTION frames                        AudioEndpoint (Core Audio)
    keyboard -> TEXT frames                               BrightnessControl (WMI)
  LaptopLink: RFCOMM socket                               MediaSessions (system media controls)
    reader thread, writer thread,   <-- RFCOMM -->      STATE / TEXT / PONG back to the phone
    coalescing outbox
  LaptopState: the laptop's last report
```

## The split

The phone recognises; the laptop executes. Every touch is turned into a semantic frame on the phone (pointer moved, button pressed, scroll, zoom, run action 3, set control 0 to 55, type "hi") and the laptop carries it out with the Windows APIs. The laptop owns the table of what each action id does. Nothing the phone sends can name a key, a scan code or a command line, so a compromised or buggy phone app can do no more than the table allows.

The laptop also reports: a snapshot of volume, mute, microphone and brightness after the handshake, then every change as it happens, plus what is playing. The phone keeps the last report in `LaptopState`, which outlives the control surface, so a surface rebuilt after a rotation or a theme change starts from real values and never shows 0 for a level it has not heard.

## Transport

Bluetooth Classic RFCOMM, one service UUID both sides know, with the socket bound at `BluetoothEncryptionWithAuthentication`. That means only a device already paired with the laptop can connect, the stream is encrypted by the link layer, and there is no discovery, server or network anywhere in the product.

Rejected on the way here: BLE GATT (Windows as a GATT server is unreliable across adapters, and GATT is datagram-shaped where an ordered stream is wanted), Wi-Fi (needs a shared network and a discovery step), and making the phone a Bluetooth HID touchpad (Windows only runs its multi-finger gestures for certified Precision Touchpad hardware, so the gestures would have to be emulated anyway).

## Frames

`docs/PROTOCOL.md` has the byte layout. The shape that matters here: one type byte, then a payload whose length is fixed by the type, except TEXT, which carries its own length. Frames are 2 to 258 bytes. Both codecs are held to the same golden fixture file by their test suites, so the two apps cannot drift apart without a test failing on one side.

Latency is the design priority after correctness. The phone dispatches touch unbuffered and feeds every historical sample through; the writer thread sends everything queued in one write; and a backlog of MOVE, SCROLL and ZOOM frames still waiting in the outbox is summed into one before it goes out, so a slow link catches up in a single packet instead of replaying every sample it missed. The laptop injects input on the reading thread itself: there is no queue between the socket and `SendInput`.

## Threads

**Phone.** `LaptopLink.open` blocks for the life of the connection on its own thread and reads there. A second thread drains the outbox and writes. `send` never blocks, so touch handling never waits on the link. Every callback into the activity is posted to the main thread. The link is kept across configuration changes through `onRetainNonConfigurationInstance` and its listener swapped to the new activity; it is closed when the app leaves the foreground.

**Laptop.** `RfcommServer` accepts on WinRT's thread and hands each socket to a `Session` running on a dedicated above-normal-priority thread, where the read loop turns frames straight into input. A new connection replaces the old one rather than being refused, because after a dropped link the phone reconnects before the laptop's old socket has noticed it is dead. Audio-change notifications arrive on COM threads and brightness events on a WMI thread; `Session.Send` is locked so they interleave safely with the read loop's PONGs. Brightness writes go to their own thread with latest-value-wins, because a WMI call is far slower than a frame and queueing would make a dial drag lag further behind the finger the longer it moved. Media state is read from Windows' system media transport controls and extrapolated once a second while playing.

**Held input.** The app switcher works by holding Alt across frames. `InputInjector` remembers everything it holds down and releases it all when a session ends, so a link dropped mid-gesture can never leave Alt or a mouse button stuck on the laptop.

## Trust

Two layers. Pairing, enforced by the socket's protection level: unpaired devices cannot connect at all. Trust on first use, enforced by `TrustStore`: the first phone to complete the handshake has its Bluetooth address written to `%APPDATA%\Edgepad\trusted-phone.txt`, and every other phone is refused before HELLO_ACK. The tray menu's **Forget trusted phone** clears it.

The handshake also carries a protocol version. A mismatch is refused with the laptop's version in the reply, so the phone can say which side needs updating instead of misreading frames.

Windows itself adds a boundary the app cannot cross: `SendInput` is silently refused when an elevated window or the secure desktop has focus. The laptop counts those refusals and logs them once per session.

## The phone's surface

`ControlSurface` is a single custom `View` that draws everything and receives every touch. There is no view hierarchy between the finger and the recogniser. A touch is classified where it starts: inside a corner's zone it belongs to that dial for its whole life, on a media piece or the gear or the keyboard button it is a button press, anywhere else it goes to `TrackpadRecognizer`. Geometry is computed once in `onSizeChanged`; nothing is allocated while drawing.

`Perimeter` models the screen's edge as one clockwise path, a rounded rectangle whose corner radius is the display's own (`WindowInsets.getRoundedCorner`), so a ruler bends round a corner instead of being clipped by it. A dial's finger position is projected onto that path and its movement measured along it.

`TrackpadRecognizer` and `Dial` are pure Kotlin with no Android types, which is what lets the whole gesture table run as JVM unit tests.

## Settings and state

Settings live in one `SharedPreferences` file; the theme is the system's own per-app night mode, so no preference of Edgepad's is needed for it. Dial positions and the gesture map are stored by enum name, so a renumbering cannot silently remap them. Media-piece positions are fractions of the surface's width and height, so a layout made in portrait keeps its shape in landscape.

## Build and release

One script, `scripts/check.ps1`, is the quality gate for both halves: format, lint (warnings are errors on both sides), build, test. CI runs it in its non-mutating mode on every push and pull request, then produces a debug APK and a self-contained exe as artifacts. A version tag runs the same gates and publishes a GitHub Release with a signed APK and the exe under stable names. The APK's signing key exists only in the repository's Actions secrets and on the machine that created it; `scripts/new-signing-key.ps1` makes it once and never prints it.

The laptop app is a per-user tray app rather than a Windows service on purpose: services run in session 0 and can neither inject input into the desktop nor reach the user's audio session. It is single-instance, and a newer copy asks the running one to quit and waits for the lock, so an update takes over cleanly.
