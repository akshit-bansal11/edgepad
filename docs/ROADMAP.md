# Edgepad roadmap: 4.2.1 to 5.0.0

One list for every planned fix, feature and change after v4.2.0 (`07e83d1`). It replaces `edgepad-4.x.y-fixes.md`, `edgepad-4.x-features.md` and `edgepad-5-features.md`. Written 2026-11-16.

## Status (2026-11-23)

The 4.2.x fixes shipped in 4.2.1. Every item from 4.3.0 to 5.0.0 below shipped together in v5.0.0 on 2026-11-23, on protocol 6. It was tested on the owner's Android phone and Windows laptop; the Mac and Linux apps have passed CI only, and Linux arm64 has never run on a device. Nothing here carries a speed, latency or battery figure.

What did not ship, and why:

- **Windows laptop notifications (4.9.0).** The 4.9.0 spike, a probe of Windows' `UserNotificationListener` (`--probe-notifications`, W7), has to be run by the owner on a Windows machine. The Windows forwarder and its tray row (W8) wait for its result. Linux forwards notifications; macOS has no public API.
- **Wayland macro windows via KWin (X1c, 4.6.0).** The spike stopped before anything was built. KDE's documentation does not confirm the scripting calls it needs, and it needs a Plasma 6 Wayland session. Macro running state, raise and close stay X11 only on Linux. **Deferred (2026-11-17):** built only together with someone on Plasma 6 Wayland who can test it, so nothing untested ships; GNOME would need a Shell extension of its own. On Wayland the macro tiles still open their apps.
- **Browser site icon (4.3.0).** Option C was built: the media's own artwork, not an Edgepad browser extension (option B).
- **ANDU-10** (one macro page planned for 10 tiles) is dropped as designed: the page keeps a stable tile size on purpose.
- **Update signing key (5.0.0).** Created by the owner with `scripts/new-update-key.ps1` on 2026-11-16 and embedded (`protocol/update-key.txt`, `e9076b9`). The laptop apps install a verified release in place; the first end-to-end update is 5.0.0 to the next release, so that path has not yet been used on a real release.

## Version rule (owner, 2026-11-16)

| Kind | Goes in | Example |
| --- | --- | --- |
| A fix to the released version | a patch | 4.2.1, 4.2.2, … |
| A new feature, or a change to how something looks or works, on the same protocol | a minor | 4.3.0, 4.4.0, … |
| Anything that needs a wire protocol change | the major | 5.0.0 |

Protocol 5 (since 4.0.0) allows new TEXT kinds, SET controls, ACTION ids and BLOB kinds without a bump: an older half drops them. Only a new frame type forces protocol 6. Checked 2026-11-16: Windows, Linux and macOS drop these and count them. Android drops them without counting, which is fixed in 4.2.5.

## Standing rules for every item

- **Phone stays light.** No item may raise phone memory or CPU use; low-tier phones must keep working. Push work to the laptop, send data once per change (never on a timer), keep caches small and bounded.
- **The laptop owns the action table.** The phone never sends command lines (2026-09-11).
- **Changing any English string** needs all 14 translations in the same change; lint and the Windows test enforce it.
- **Visual changes** need the owner's go-ahead per item.

## How to read this

- **Hours** are focused development time with one CI round, estimated and not measured. "+N tune" is device tuning on top.
- **ID** prefixes: F = reported on the devices; AND, WIN, LIN, MAC, CORE, X = the 2026-11-14 state-machines deep dive (`https://app.notion.com/p/418c6445b19f8181ac23ca00f40a1a97`); ANDC, ANDU, WINA, CORA, MACA, LINA = the 2026-11-16 audit. "HW" = only real hardware can confirm it.
- The audit was read-only: nothing was built or run, and nothing was seen on a device.

## Already done in 4.2.0?

None of the items planned on 2026-11-15 shipped in 4.2.0. AGP 9.5.0 did ship (`android/build.gradle.kts:3`).

These old Notion threads are already fixed: AppPicker epochs (#7), `CaptureInbox.permit` (#8), Mac scan off the main thread (#10), Devices dial dispatch (#11), `ClipboardSync` threads and the `DeviceSwitch.fit` surrogate (#12), stale frames from a closed link, PAD_BUTTON rows on the site, the Backdrop decode under AGP 9.5, Windows Bluetooth retry at sign-in, the `release` environment's reviewers and the signing-certificate pin.

From the deep dive, these are fixed: AND-1, WIN-1, LIN-1, LIN-2, MAC-1 and MAC-2. These are partly fixed: X-1, X-3, MAC-21 and MAC-22.

## Summary

| Version | Kind | Theme | Hours |
| --- | --- | --- | --- |
| 4.2.1 | Fixes | Device reports, high-impact bugs, urgent security | 50.5 (+5 tune) |
| 4.2.2 | Fixes | Laptop security and reliability | 55.5 |
| 4.2.3 | Fixes | Android UI, accessibility, docs | 19.5 |
| 4.2.4 | Fixes | Laptop polish | 50 |
| 4.2.5 | Fixes | Android polish | 24.5 |
| 4.3.0 | Features + change | Laptop control, now-playing icons, clean titles, new battery strip | 41.5 |
| 4.4.0 | Features | Phone shortcuts | 39 |
| 4.5.0 | Features | Mac and Linux in every language | 48 |
| 4.6.0 | Features | Mac and Linux catch up with Windows | 58.5 |
| 4.7.0 | Feature | Per-app profiles | 25 |
| 4.8.0 | Features | Air mouse and floating dials | 37 (+4 tune) |
| 4.9.0 | Feature | Laptop notifications on the phone | 38 |
| 5.0.0 | Feature, protocol 6 | File transfer, rebuilt | 55 |
| none | Site, CI, settings | Ships on merge | 9 |
| **Total** | | | **551 (+9 tune)** |

## Decisions still open (owner)

| Decision | Options | Affects |
| --- | --- | --- |
| Shapes (F2) | Fix: hold two, lift one, draw with the other (8 h, +2 tune). Remove: Shapes and its editor (3 h). | 4.2.1 |
| Site icon for media in a browser | Decided: C, the media's artwork (built). B, an extension, was not built. | 4.3.0 |
| Switch and slider contrast (ANDU-9) | A darker off colour for the tracks, a visual change. | 4.2.5 |
| Order of the minors | 4.4.0 to 4.9.0 stand alone and can be reordered. | 4.4.0 to 4.9.0 |

---

## 4.2.1: device reports, high-impact bugs, urgent security (50.5 h, +5 tune)

### Fixes

| ID | Problem | Where | Fix | Hours |
| --- | --- | --- | --- | --- |
| F1 | Switching transport (Wi-Fi to Bluetooth) leaves the phone on the Reconnecting screen. The two transports are two records, and the new one is selected only when `tapped && !connected`. | `MainActivity.kt:2014`, `:2075`, `:2290`; `LaptopConnections.kt:147` | When the row holds the selected laptop, select the new record and end the old record's link once the new one connects. Also fixes AND-19. | 3 |
| F2 | Shapes cannot be drawn since 4.2.0. | `TrackpadRecognizer.kt:190-194`, `:517` | **Owner decides:** fix (8, +2 tune) or remove (3). | 8 or 3 |
| F3 | Bring back double-tap-and-drag next to hold-still drag, without Windows reading it as a double-click. It restores 4.1.0 behaviour, so it counts as a fix. | `TrackpadRecognizer.kt:15`; `ControlSurface.kt:77`, `CaptureScreen.kt:70` | Send nothing on the second touch until the pointer has travelled past the 4 × 4 px double-click box, then press. The Capture screen keeps hold-still only. The Mac and Linux double-click distances are unknown (HW). | 7 (+3 tune) |
| ANDC-1 + WINA-2 | A Wi-Fi/USB laptop that vanishes is noticed only after about 15 min: the phone's 5 s PING keeps TCP keep-alive from probing, and no PONG is checked. The laptop never ends a silent session, so keys stay held. | `LaptopLink.kt:263-276`, `TcpTransport.kt:156`; `Session.cs:84-101`, `TcpLinkListener.cs:208-209`, `RfcommServer.cs:271-275` | Phone: close as LOST after about 15 s with no frame, plus `TCP_USER_TIMEOUT` 20 s. Laptops: end a session after 30 s with no frame, 30 s send timeout, documented in `PROTOCOL.md`. | 6 |
| ANDC-3 | A two-, three- or four-finger tap is often lost (no right-click): `up()` does not rebase the start point. | `TrackpadRecognizer.kt:396-410` vs `:163-173` | Rebase `startX/startY` in `up()` when fingers remain. Test DOWN, DOWN, UP, MOVE, UP. | 1.5 |
| Reconnect cluster | The phone retries refusals, mismatches and lock-outs up to 10 times and at every return to the app (ANDC-2, AND-6); Bluetooth off marks a laptop Not paired forever (AND-2); New code after Pair again fails and turns USB into Wi-Fi (AND-5); any early IOException reads as "owner said no" (AND-13); both records of a merged row auto-connect (AND-19). | `LaptopConnections.kt:340-352`; `MainActivity.kt:1827-1837`, `:2053-2060`, `:1033-1038`; `LaptopLink.kt:449-457`; `TcpTransport.kt:74-76` | Retry only LOST, UNREACHABLE and NO_ANSWER; check the adapter first; `Target.Network.again()` keeping the kind; classify timeouts as LOST; auto-connect off on the other record. | 8 |
| ANDC-4 | Possible crash at cold start with an image background: `resize(0, 0)` rounds NaN. | `Backdrop.kt:75-78`, `:107-110` | Return early at width or height 0. | 0.5 |
| AND-3 | Leaving the gamepad screen while holding a key-bound control never sends the key up. | `GamepadScreen.kt:366-369` | Release every held pointer on detach. | 1.5 |
| CORA-1 | Windows and Linux: any device on the same network can open pairing prompts in a loop, and five timed-out prompts lock real pairing for 5 min, rising to 1 h. | `PairingLimiter.cs:30-41`, `:84-85`; `LinkResponder.cs:223`, `:284-297` | Refuse a host for 60 s (doubling) after a cancel or timeout, cap prompts per hour, count only a wrong code or bad commit as Failed. | 4 |
| MACA-3 + MAC-6 | Mac: a connection flood from the LAN does file I/O on the main queue with no per-host cap, so the pointer stalls. | `NetworkServer.swift:155-171`; `Log.swift:32-54` | Cap 2 per host; log the first refusal per host per minute; queue log I/O. | 3 |
| LINA-2 | Linux: Type text on a non-US layout freezes the pointer for up to 2 s and overwrites the user's clipboard. | `LinuxInput.cs:149-169`, `:306-323`; `Typing.cs:166-200` | Post typing to the Worker in order; save and restore the clipboard. | 5 |
| MACA-1 + MAC-9 | Mac (HW): the RFCOMM delegate is set only after a synchronous `requestAuthentication()`, so HELLO is probably dropped and each Bluetooth connect stalls about 10 s. | `RfcommServer.swift:125-141` | Set the delegate first and buffer; authenticate off the main thread with a timeout. | 3 |

## 4.2.2: laptop security and reliability (55.5 h)

### Fixes

| ID | Problem | Fix | Hours |
| --- | --- | --- | --- |
| WINA-1 | The Bluetooth device name goes uncleaned into the trust question and log (`BluetoothLink.cs:18`). | `PhoneLabel.Shown(name)`. | 1 |
| WINA-6 | Each update leaves another inbound firewall rule for the old exe (`Firewall.cs:30`, `:38-41`). | Delete by rule name before adding. | 1 |
| WINA-7 | Resume keys, the PIN hash and the link id live in Roaming AppData. | Move to LocalApplicationData with a migration. | 4 |
| MAC-5 + CORE-1 | A failed Forget leaves the phone admitted (Mac); re-pairing after a failed Forget stays refused (core). | A denied set on the Mac; `denied.Remove` after Add. | 2 |
| CORE-10 | `Grant` returns true when the trust file cannot be read. | Return false. | 0.5 |
| Gradle metadata | No dependency verification metadata committed. | Run `gradle-verification.yml`, review, commit. | 2 |
| X-2 | `PowerReporter` holds its lock across a blocking send, so a replacing phone hangs and Linux keeps keys down. | Send outside the lock; reorder teardown. | 3 |
| X-4 | No UI-thread exception handler on Windows or Linux. | `Application.ThreadException`; `Dispatcher.UIThread.UnhandledException`. | 1.5 |
| TEXT 18 | Windows never tells the phone that input is blocked (UIPI); Linux and Mac do. | Send `blocked` on the first refusal and `ok` on the next success, debounced. | 3.5 |
| WINA-3 | Browser title lookup enumerates every process once a second, even while paused. | Check playback first; cache the service name. | 2.5 |
| X-3 | A Bluetooth-off status replaces the header while a Wi-Fi phone is connected (Windows, Mac). | Post only when no phone is live. | 1 |
| WIN-2 | A second device's status overwrites the connected phone's header. | Post candidate statuses only when no phone is current. | 1.5 |
| WIN-3 | A ViGEm error leaves a ghost controller. | `Unplug()`. | 0.5 |
| WIN-4 | Two BlobSenders: a laptop copy aborts the phone's icon transfer. | One shared sender. | 1.5 |
| WIN-5 | The refresh-rate dial replays a stale resolution. | Refresh on `DisplaySettingsChanged`. | 2 |
| LINA-1 | Linux never loads translated CoreStrings (`InvariantGlobalization=true`). | `PredefinedCulturesOnly=false`, culture from `LANG`, a test. | 6 |
| LINA-3 | Linux forks `wpctl` four times a second all session. | Back off while idle, or subscribe to events. | 4 |
| LIN-3 to LIN-7 | Dark tray icon missing; adapter off at launch shows Waiting; pairing status sticks; tray text race; Security window closes mid-save. | As on the deep-dive page. | 6 |
| MACA-2 | Mac: a copied screenshot is re-encoded on the main thread. | Utility queue. | 3 |
| MAC-3, 4, 7, 8 | Trust timer aborts Security; toggling Bluetooth drops Wi-Fi phones; Capture needs English file names; a new copy starts on a random port. | As on the deep-dive page. | 9 |

## 4.2.3: Android UI, accessibility, docs (19.5 h)

### Fixes

| ID | Problem | Fix | Hours |
| --- | --- | --- | --- |
| ANDU-1 | The nav title is squeezed to about 14 dp under "Surface settings", and to nothing in German (`Page.kt:879-892`). | Cap each end at about 40 %; ellipsize the back label. | 2.5 |
| ANDU-2 | Segmented controls overflow at large font (`Page.kt:480-560`). | Stack under the label when they do not fit. | 3 |
| ANDU-3 | Capture forgets a running recording on reopen (`CaptureScreen.kt:47`). | Keep the flag per laptop at app scope. | 2 |
| ANDU-4 | A dimmed Annotate/Capture tile always says "needs a newer Edgepad", even on a current Linux build (`ControlsPopup.kt:212`). | New wording in 15 languages. | 2 |
| ANDU-5 | Annotate rebuilds every stroke on every touch sample (`AnnotateCanvas.kt:66-91`). | Cache a Path per finished stroke. | 3 |
| ANDU-6 | The layout editors are one accessibility node, so TalkBack cannot edit layouts. | `ControlNodes` plus move-by-step actions. | 4 |
| Guide presets | `strings_guide.xml:47` says "four presets" and `:130` omits Xbox keys; there are five. | English plus 14 translations. | 1.5 |
| Doc drift | `PROTOCOL.md:40`, `:229`, `:411` and `ARCHITECTURE.md:74` disagree with the code. | Edit the docs. | 1.5 |

## 4.2.4: laptop polish (50 h)

### Fixes

| ID | Problem | Hours |
| --- | --- | --- |
| WINA-4 | The default output and Bluetooth devices are re-read every 3 s. | 4 |
| WINA-5 | The update check fires before the first-run dialog is answered. | 1 |
| WINA-8 | The pairing balloon has no click action (keyboard and screen readers). | 2 |
| CORA-2 | The blob budget never refills in a long session. | 1 |
| WIN-6 to WIN-15 | Ten lows from the deep dive. | 6 |
| X-5, X-6, CORE-2 to CORE-9 | Prompt not withdrawn on hang-up, teardown finally, recordings cut by Cancel, and six more. | 8 |
| LINA-4 | The clipboard poll fetches up to 4 MB only to hash it. | 4 |
| LINA-5 | logind is polled twice a second. | 2 |
| LIN-8 to LIN-21 | Fourteen lows from the deep dive. | 9 |
| MACA-5 | The Desktop is listed on the main thread twice a second while Capture is armed. | 3 |
| MAC-10 to MAC-25 | Sixteen lows from the deep dive. | 10 |

## 4.2.5: Android polish (24.5 h)

### Fixes

| ID | Problem | Hours |
| --- | --- | --- |
| ANDU-7 to ANDU-14 | Capture status not announced; Arabic PIN keypad mirrored; switch and slider contrast 1.26:1 (owner decides); one macro page planned for 10 tiles; hold-to-close undiscoverable; expired code reads "0 seconds"; Type text draft lost on rotation; stale gamepad description. | 8 |
| AND-4, 7, 9 to 12, 14 to 17, 20 | Second update download after rotation; wrong pointer released; no writer-thread net; stale update error; resume screen lost; early red PIN line; three unguarded starts; double resolve; transport built on the main thread. | 8 |
| ANDC-5, ANDC-6 | A system-destroyed activity closes Stay-connected links; the linger cuts a Bluetooth image share. | 5.5 |
| AND-18 + counter | `DeviceState.kt` is dead code; unknown TEXT kinds are not counted. | 1.5 |
| Translation lengths | Italian `Macro_ReorderHint`, Polish `control_color`, Russian `piece_transport`: owner looks on a device, then shorten. | 1.5 |

---

## 4.3.0: laptop control, now-playing icons, clean titles, new battery strip (41.5 h)

### Features

| Feature | Detail | Hours |
| --- | --- | --- |
| Restart as administrator | A tray row that relaunches Edgepad elevated, so input reaches elevated windows such as Task Manager. The lock screen and UAC stay unreachable by design. Releases the `Local\Edgepad` mutex first; one new string in 15 languages. | 3.5 |
| Sleep when this ends | New ACTION ids: the laptop sleeps or shuts down when the current video or track ends. Shutdown shows a cancellable countdown first. Uses the existing TEXT 2 timeline; ACTION ids 60-63 and 128+ are free. | 11 |
| Laptop reports its macro cap | A new TEXT kind; the phone assumes 15 today (`AppListScreen.kt:78`, `:93`). | 2 |
| Laptop reports recording state and why a feature is missing | New TEXT kinds; follow-up to ANDU-3 and ANDU-4. | 6 |
| The playing app's real icon | Owner report: VLC and Windows Media Player show a music note, because the phone knows only 19 bundled service logos (`AppLogo.kt:80-169`). The laptop finds the icon of whatever is playing and sends it once per app change as a 64 px PNG over a new BLOB kind 4 `MEDIA_ICON`. Windows: the app's media ID through the shell's app folder, falling back to the running exe through `MacroIcons.cs`. Linux: MPRIS `DesktopEntry` through `DesktopIcons.cs`. Mac: with now-playing in 4.6.0. Phone: title-matched service logo first, then the laptop's icon, then the note; at most 8 icons kept (about 128 KB). A player that never reports to the OS media controls has no now-playing at all: check VLC and Media Player on a device. | 12 |
| Site icon for media in a browser | **Owner decides** B (extension, 18 h) or C (artwork, 3 h). The OS media controls name only the browser, never the site. Not in the total. | 18 or 3 |
| Clean media titles | The laptop strips everything that is not the title: (1) every `(…)`, `[…]`, `{…}`; (2) from `feat.`, `feat`, `ft.`, `ft`, `featuring`, `prod.` to the end; (3) from `with`, `and`, `&`, `x`, `,` to the end, only when what follows matches the reported artist list, so `Stand with Me` stays whole; (4) a leading `Artist - ` that repeats the artist (ignoring ` - Topic` and `VEVO`); (5) leftover separators and spaces. An empty result keeps the original, and TalkBack reads the original. One function in `shared/Edgepad.Core` replaces `MediaSessions.cs:211` and `Mpris.cs:67`; a shared fixture `protocol/titles.txt` (about 30 cases) keeps the Mac port in 4.6.0 identical. No phone cost. | 4 |

### Changes

| Change | Detail | Hours |
| --- | --- | --- |
| Battery strip: underline design | Owner picked direction 3 on the design canvas (`https://claude.ai/artifact/6n5xi4LoYuLKcwkzVhN8zL`), 2026-11-16. The translucent pills go. Each device is its icon, the percent and a bolt when charging, with a 2 dp progress line under them filled to the level: ink normally, accent when charging, danger when low. The percent stays, so low never relies on colour alone. "+N" is the number in dim, red when one of the others is low. When a background image is set, one soft scrim sits behind the whole strip so text stays readable on a light photo. The strip stays inside `WIDTH_DP` (144 dp), so the dials' keep-out does not move; the tap target and TalkBack summary are unchanged. Draw cost: one rounded rect for the track and one for the fill per device, no clip, less than today. Files: `BatteryStrip.kt` (draw and place); the design reference under `assets/design/` trails. | 3 |

## 4.4.0: phone shortcuts (39 h)

### Features

| Feature | Detail | Hours |
| --- | --- | --- |
| Quick Settings tile and a richer notification | Volume and play/pause from the shade, on the Stay connected service (`LinkService.kt:150-161` has one Disconnect action today). | 12 |
| Clipboard history | The last N laptop copies; tap one to send it again. Since Android 10, only laptop-to-phone copies can be recorded while Edgepad is not in front. | 8 |
| Presenter mode | Slides on the volume keys, only while Edgepad is in front (5); a laser dot on Annotate's overlay, one tool ACTION id at most (14). | 19 |

## 4.5.0: Mac and Linux in every language (48 h)

### Features

| Feature | Detail | Hours |
| --- | --- | --- |
| macOS localisation, 14 languages | No `NSLocalizedString` today. A string catalog generated from the existing resx files. | 24 |
| Linux UI localisation, 14 languages | Shell, tray and dialog text is hard-coded English. Needs LINA-1 (4.2.2) first. | 24 |

## 4.6.0: Mac and Linux catch up with Windows (58.5 h)

### Features

| Feature | Platform | Detail | Hours |
| --- | --- | --- | --- |
| Now playing and media position | macOS | AppleScript for Music and Spotify; MediaRemote is private and restricted since macOS 15.4. Brings the app icon (+2 h) and title cleaning port (+1.5 h), included. | 13.5 |
| Macro running state, raise and close on Wayland | Linux | X11 only today (`LinuxMacroWindows.cs:52`); a GNOME Shell extension or a KWin script. | 16 |
| Refresh-rate dial | Linux | Mutter DisplayConfig or xrandr (`LinuxPlatform.cs:186`). | 10 |
| arm64 build | Linux | linux-x64 only today. | 4 |
| Hold the phone off on fast user switch or screen lock | macOS | Linux has `SessionWatch`. | 3 |
| Capture clipboard fallback | macOS, Linux | Windows has it. 6 each. | 12 |

A Mac gamepad is left out: a virtual controller needs a DriverKit entitlement and a Developer ID.

## 4.7.0: per-app profiles (25 h)

### Feature

A new TEXT kind (21) for the foreground app. Dials and gestures switch per app, and profiles live on the phone. The app name is personal data: it is sent only while connected and the feature is on. Foreground detection exists for macro apps only (`WindowsMacroWindows.cs:113`, `X11Windows.cs:93`, `WorkspaceMacroWindows.swift:47`). Wayland has no active-window API, so on Linux this is X11/XWayland only.

## 4.8.0: air mouse and floating dials (37 h, +4 tune)

### Features

| Feature | Detail | Hours |
| --- | --- | --- |
| Air mouse | The gyroscope drives the existing MOVE frames; recentre, dead zone, sensitivity. Phone only. The sensor is read only while the mode is on. | 7 (+4 tune) |
| Floating dials over other apps | "Draw over other apps" mode, reusing `Dial.kt` and `ControlSurface`. Overlay service, permission flow, touch pass-through. Samsung gesture conflicts are untested. | 30 |

## 4.9.0: laptop notifications on the phone (38 h)

### Feature

Sent as a BLOB kind, because TEXT is capped at 255 bytes. Windows (`UserNotificationListener`) and Linux (D-Bus monitor) only; macOS has no public API. **Spike first (4 h, inside the 38):** `UserNotificationListener` may need package identity, which the unpackaged exe lacks. If it does, Windows drops out or needs MSIX, and this release is re-planned.

---

## 5.0.0: protocol 6 (55 h, range 45 to 65)

A 5.0 phone and a 4.x laptop refuse each other at the handshake, so both halves must be installed together.

### Feature: file transfer, rebuilt

Today `FILE_BEGIN` / `FILE_CHUNK` / `FILE_END` (0x50-0x52) carry Capture's files laptop to phone only, 255 bytes per chunk with a sequence number mod 256, on the stream shared with pointer input. More threads will not speed up Bluetooth: the radio is the limit.

| Part | Detail | Hours |
| --- | --- | --- |
| New chunk frame | A full byte offset per chunk instead of seq mod 256: the reason for the bump. SHA-256 at the end (already there). Fixtures and the version on all four apps. | 8 |
| Bytes over Wi-Fi, control over Bluetooth | A second TCP socket on the Wi-Fi/USB path (port 47321, EDGL1); bulk chunks at low priority behind pointer frames, so input stays smooth. Needs the phone paired over Wi-Fi; otherwise Bluetooth. | 15 |
| Resumable | Resume from the last verified offset after a dropped link. | 8 |
| Phone to laptop, any file | Share › Edgepad for any file; `ShareActivity.kt` takes text and images only today. | 8 |
| Laptop to phone | From the tray or Explorer's Send to › Edgepad, on all three laptops. | 15 |

No speed figure may be written down until one is measured on the devices.

A smaller option without a bump: a new BLOB kind for arbitrary files (about 12 h) would cover phone-to-laptop sharing in a 4.x minor, with no resume and no Wi-Fi fast path.

---

## Not tied to an app release (9 h)

The site deploys from `main`; settings are the owner's.

| ID | Problem | Fix | Hours |
| --- | --- | --- | --- |
| WEB-17 | The docs site has no search. | A JSON index at build time, filtered client-side, no new dependency. | 7 |
| Download links | `site/lib/release.ts:15-17` reads the newest CHANGELOG heading, so links 404 before the tag publishes. | Read the newest published release. | 1 |
| Site deps | `next` 16.4.1 (latest 16.4.6); `eslint-config-next` 16.4.3 does not match. | One patch bump. | 0.5 |
| CI nits | No `concurrency` in `release.yml`; mismatched upload-artifact pin at `ci.yml:400`; HSTS unchecked. | Add, align, `curl -I` once. | 0.5 |
| SHA pinning | "Require actions to be pinned to a full-length commit SHA" is off. | Owner: `https://github.com/akshit-bansal11/edgepad/settings/actions`, tick it, Save. | owner |
