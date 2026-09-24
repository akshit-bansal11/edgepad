# Architecture

Edgepad is one product in two programs. This page is the map of both and of the seams between them: what runs where, on which thread, what trusts what, and why.

```
Android phone                                   Windows laptop
--------------------------------------          ----------------------------------------
MainActivity                                    Edgepad.exe (WinForms tray, one instance)
  screens: guide, devices, settings pages,      TrayContext: menu, status, run at login
           layouts, keyboard, gamepad,          RfcommServer: advertises the service
           connection lost                        Session (one per phone, own thread)
  ControlSurface (one View, all drawn)              FrameCodec -> Dispatcher
    TrackpadRecognizer -> frames                      InputInjector (SendInput)
    Shapes -> action, macro or pad mode               VirtualPad (ViGEmBus)
    Dial x4 -> SET / ACTION frames                    AudioEndpoint (Core Audio)
    keyboard -> TEXT frames                           BrightnessControl (WMI)
    gamepad -> PAD_STATE frames                       MediaSessions (system media controls)
  LaptopLink: RFCOMM socket                           STATE / TEXT / PONG back to the phone
    reader thread, writer thread,   <-- RFCOMM -->
    coalescing outbox
  LaptopState: the laptop's last report
```

## The split

The phone recognises; the laptop executes. Every touch is turned into a semantic frame on the phone (pointer moved, button pressed, scroll, zoom, run action 3, set control 0 to 55, type "hi", here is the whole controller) and the laptop carries it out with the Windows APIs. The laptop owns the table of what each action id does, which is what lets the two halves be versioned apart and an id one of them has never heard of be dropped and counted rather than misexecuted.

That table is a versioning seam and not a containment boundary, and should not be read as one: KEY carries a raw Windows virtual-key code and TEXT kind 3 carries arbitrary characters, both of which the laptop injects directly, because that is exactly what the phone's keyboard screen is. A paired phone is a trusted input device, and the trust boundary is the pairing plus trust on first use described under **Trust** below. [SECURITY.md](../SECURITY.md) says the same at more length.

The laptop also reports: a snapshot of volume, mute, microphone and brightness after the handshake, then every change as it happens, plus what is playing and whether it can offer a virtual controller. The phone keeps the last report in `LaptopState`, which outlives the control surface, so a surface rebuilt after a rotation or a theme change starts from real values and never shows 0 for a level it has not heard. The pad's status is kept there for the same reason and one more: the laptop volunteers it once after the handshake and otherwise only answers a request, so a rotation that rebuilt the gamepad screen would lose the only answer it had been given.

## Transport

Bluetooth Classic RFCOMM, one service UUID both sides know, with the socket bound at `BluetoothEncryptionWithAuthentication`. That means only a device already paired with the laptop can connect, the stream is encrypted by the link layer, and there is no discovery, server or network anywhere in the product.

Rejected on the way here: BLE GATT (Windows as a GATT server is unreliable across adapters, and GATT is datagram-shaped where an ordered stream is wanted), Wi-Fi (needs a shared network and a discovery step), and making the phone a Bluetooth HID touchpad (Windows only runs its multi-finger gestures for certified Precision Touchpad hardware, so the gestures would have to be emulated anyway).

## Frames

`docs/PROTOCOL.md` has the byte layout. The shape that matters here: one type byte, then a payload whose length is fixed by the type, except TEXT, which carries its own length. Frames are 2 to 258 bytes. Both codecs are held to the same golden fixture file by their test suites, so the two apps cannot drift apart without a test failing on one side.

The version is 4, moved from 3 by 3.0.0 to make room for PAD_STATE. It carries the whole controller — buttons, both triggers, both sticks — in one payload laid out byte for byte like Windows' own `XINPUT_GAMEPAD`, so the laptop copies the fields onto a report rather than translating them, and there is no range or button order of ours to hold against Microsoft's. Only a new frame type forces a version, and the version moves only in a major release, which is why one frame's arrival is the whole of the difference between 3 and 4. The handshake refuses a mismatch, so both halves of a release are installed together or neither connects.

Latency is the design priority after correctness. The phone dispatches touch unbuffered and feeds every historical sample through; the writer thread sends everything queued in one write; and a backlog of MOVE, SCROLL and ZOOM frames still waiting in the outbox is summed into one before it goes out, so a slow link catches up in a single packet instead of replaying every sample it missed. A backlog of SET or PAD_STATE frames collapses to the newest instead of summing, because each of those is a snapshot rather than a change and the ones before it describe a moment that has passed. KEY and POINTER_BUTTON are never collapsed at all: they are edges, and dropping a release would leave the key or the button held down on the laptop with nothing left to lift it. The laptop injects input on the reading thread itself: there is no queue between the socket and `SendInput`.

## Threads

**Phone.** `LaptopLink.open` blocks for the life of the connection on its own thread and reads there. A second thread drains the outbox and writes. `send` never blocks, so touch handling never waits on the link. Every callback into the activity is posted to the main thread. The link is kept across configuration changes through `onRetainNonConfigurationInstance` and its listener swapped to the new activity; it is closed when the app leaves the foreground.

**Laptop.** `RfcommServer` accepts on WinRT's thread and hands each socket to a `Session` running on a dedicated above-normal-priority thread, where the read loop turns frames straight into input. A new connection replaces the old one rather than being refused, because after a dropped link the phone reconnects before the laptop's old socket has noticed it is dead. Audio-change notifications arrive on COM threads and brightness events on a WMI thread; `Session.Send` is locked so they interleave safely with the read loop's PONGs. Brightness writes go to their own thread with latest-value-wins, because a WMI call is far slower than a frame and queueing would make a dial drag lag further behind the finger the longer it moved. Media state is read from Windows' system media transport controls and extrapolated once a second while playing.

`VirtualPad` belongs to the session thread too, and is written the way `InputInjector` is: no exception leaves it. ViGEmBus is a separate install this laptop may not have, may have removed, or may have at a version the client cannot speak to, and all three are ordinary states rather than faults — every entry point answers with a `PAD_STATUS` token instead, which is what that token exists for. A tray app has no dialog to show a crash in, and an escape from the session thread would take the link with it and everything the phone was holding down. Failures are caught by namespace rather than by a list of types, because the library's exceptions derive straight from `Exception` with no common base and the one a list missed would be the crash the class exists to prevent. The driver is looked for once per connection and the answer kept, so a phone may press attach as often as it likes; the cost is that a ViGEmBus installed mid-session is not noticed until the phone reconnects.

**Held input.** The app switcher works by holding Alt across frames. `InputInjector` remembers everything it holds down — including the raw virtual-key codes the on-screen keyboard and the gamepad send, which it did not until 3.0.0 — and releases it all when a session ends, so a link dropped mid-gesture can never leave Alt, a mouse button or a key stuck on the laptop. The virtual controller is unplugged on the same path in the session's `finally`, and for the same reason: a phone that disappears mid-game must not leave a pad plugged into Windows with a stick still pushed forward and nothing alive to centre it.

## Trust

Two layers. Pairing, enforced by the socket's protection level: unpaired devices cannot connect at all. Trust on first use, enforced by `TrustStore`: the first phone to complete the handshake has its Bluetooth address written to `%APPDATA%\Edgepad\trusted-phone.txt`, and every other phone is refused before HELLO_ACK. The tray menu's **Forget trusted phone** clears it.

The handshake also carries a protocol version. A mismatch is refused with the laptop's version in the reply, so the phone can say which side needs updating instead of misreading frames.

Windows itself adds a boundary the app cannot cross: `SendInput` is silently refused when an elevated window or the secure desktop has focus. The laptop counts those refusals and logs them once per session.

## The phone's surface

`ControlSurface` is a single custom `View` that draws everything and receives every touch. There is no view hierarchy between the finger and the recogniser. A touch is classified where it starts: inside a corner's zone it belongs to that dial for its whole life, on a media piece or one of the five buttons along the top it is a button press, anywhere else it goes to `TrackpadRecognizer`. Geometry is computed once in `onSizeChanged`; nothing is allocated while drawing.

Four of those buttons open a screen — settings, the keyboard, the gamepad, the macro grid. The fifth, the lock in the middle, walks the surface through `PadMode`: one tap hides the dials and the media and leaves the trackpad, a second inside the double-tap window locks the trackpad instead and puts them back, and any later tap returns the whole surface. The mode is gated where the surface already decides those things rather than beside them — `mediaShown()` and `dialAt()` both answer for it, and both are what `onDraw` and the hit test route through, so nothing is ever drawn without answering or answering without being drawn. Every mode change cancels the touch in flight, because arriving at a locked pad mid-drag would otherwise leave the left button held with no release to come. It is neither stored nor offered as a setting: a phone that came back up silently locked would read as broken.

A stroke drawn on the trackpad is the one gesture that is not in the finger table. Press one finger, hold it still until it ticks, then draw without lifting; on lift the stroke goes to `Shapes`, a $1-style unistroke recogniser, and whatever it matched runs — a laptop action, a macro slot, or the surface's own focus or lock. That seam was free: a bare press and hold with no tap before it emitted nothing at all on this pad. The recogniser takes the shape sink as null when nothing is bound, so a phone whose owner has drawn no shapes keeps exactly the old behaviour rather than swallowing a third of a second of pointer movement on every press.

`Perimeter` models the screen's edge as one clockwise path, a rounded rectangle whose corner radius is the display's own (`WindowInsets.getRoundedCorner`), so a ruler bends round a corner instead of being clipped by it. A dial's finger position is projected onto that path and its movement measured along it.

`TrackpadRecognizer`, `Dial`, `Shapes`, `PadMode`, `PadAxis` and `GamepadLibrary` are pure Kotlin with no Android types, which is what lets the gesture table, the shape maths, the pad's transitions, the stick's arithmetic and the layout library's rules all run as JVM unit tests. `PadAxis` is there deliberately: until 3.0.0 nothing covered touch to controller output at all, because it sat unreachable inside a `View`'s touch handler. Inside the layout, `Binding.fits` is the single answer to which bindings suit a kind of control: the picker builds its list from it and the store validates against it, so the editor can never offer a pairing the store would refuse. `InputNames` holds what each of them is called on screen, in Kotlin rather than in `strings.xml`, because a legend moulded into a keycap is the same characters whatever language the phone is set to.

## Settings and state

Settings live in one `SharedPreferences` file; the theme is the system's own per-app night mode, so no preference of Edgepad's is needed for it. Dial positions and the gesture map are stored by enum name, so a renumbering cannot silently remap them. Media-piece positions are fractions of the surface's width and height, so a layout made in portrait keeps its shape in landscape. The drawn shapes are text in the same file, and a set that fails to decode reads as no shapes at all rather than as the ones that happened to parse — a hub row claiming four shapes over a trackpad that recognises none would send the owner looking in the wrong place.

The gamepad's layouts are a library rather than one saved arrangement, which is what per-game needs. `GamepadStore` is a thin wrapper over `GamepadLibrary`, and the library is two strings: the saved layouts, and the name of the one in play. The built-in presets are never stored — a saved layout of the same name shadows one instead — so the layout Edgepad ships can always be got back, can never be renamed away, and the list is never empty. A name that collides costs a number and never a layout: overwriting would throw away an arrangement nothing can recover, and refusing needs somewhere to print an error that a popup over a full-screen canvas does not have. A layout saved by 2.x is refused rather than migrated, and the first preset takes its place, because a 2.x layout has no controller bindings in it to migrate and pretending otherwise would be a lie.

The Orientation setting holds the control surface and the media-layout editor, and nothing else. The editor is pinned with the surface on purpose: a media layout has been stored per orientation since 2.3.0, so a phone turned mid-edit would quietly begin changing the other one. The keyboard and the gamepad are always sideways, and every other screen follows the phone.

## What the tests cover

`protocol/frames.txt` and `protocol/actions.txt` are golden fixtures, plain text, read by both test suites at run time rather than copied into either. `frames.txt` lists every frame type as literal hex bytes next to the fields they mean; `FrameFixtureTest` on the phone and `FrameFixtureTests` on the laptop each parse the same file, encode the fields and assert on the exact bytes, then decode the bytes and assert on the exact fields. `actions.txt` does the same for the action and control ids, checked by `IdsFixtureTest` / `IdsFixtureTests`. Because one file drives assertions in two codebases, a change to one side's codec without the matching change on the other fails that side's test immediately — the fixture is what stops the two apps from drifting apart, not code review. A protocol change therefore always touches a fixture file and both codecs in the same commit (see CONTRIBUTING.md).

Above that fixture layer, the Android suite (`android/app/src/test`) covers the gesture recogniser and the whole finger table, natural scrolling, the shape recogniser and the text a set of shapes is stored as, the pad's mode transitions, the stick's dead zone and magnitude scaling, the gamepad layout's encoding and the library's rules (name collisions, deletion, reset, a 2.x layout refused), the dials (arming, slop, snapping, steppers, haptic notches), the corner/perimeter geometry, coalescing, and the laptop-state model — all pure Kotlin, no Android platform types, so it runs as a plain JVM unit test with no emulator. The Windows suite (`windows/tests/Edgepad.Tests`) covers the dispatcher's drop paths, input batching, trust-on-first-use, the media-session title mapping, and what a laptop with no controller driver answers — a `VirtualPad` handed a client that throws what an absent ViGEmBus throws, so the assertion holds on a build machine and on a desk that does have the driver, and neither ends up with a controller plugged into it. The paths that need the driver, a pad that actually plugs in and the attach-failed answer, have no test: a fake client cannot produce a real Xbox360 target. Neither suite sends real input, opens a real Bluetooth socket, or touches a device; that is only exercised by running both apps against each other by hand (see CONTRIBUTING.md).

## Build and release

One script, `scripts/check.ps1`, is the quality gate for both halves: format, lint (warnings are errors on both sides), build, test. CI runs it in its non-mutating mode on every push and pull request, then produces a debug APK and a self-contained exe as artifacts. A version tag runs the same gates and publishes a GitHub Release with a signed APK and the exe under stable names. The APK's signing key exists only in the repository's Actions secrets and on the machine that created it; `scripts/new-signing-key.ps1` makes it once and never prints it.

The laptop app is a per-user tray app rather than a Windows service on purpose: services run in session 0 and can neither inject input into the desktop nor reach the user's audio session. It is single-instance, and a newer copy asks the running one to quit and waits for the lock, so an update takes over cleanly.
