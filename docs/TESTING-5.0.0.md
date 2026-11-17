# Device test list: 5.0.0 (and the untested 4.2.1 fixes)

Nothing in 4.2.1 or 5.0.0 has run on a device. Every item below needs real hardware. Install **both halves from the
same draft**: 5.0.0 speaks protocol 6, so a 5.0 phone and a 4.x laptop refuse each other (and say so).

Tick items as you go. When something fails, note the step, what you saw, and the log lines from that moment
(phone: Settings › About › Share log; laptop: tray › Open log).

Legend: **[W]** Windows · **[M]** Mac · **[L]** Linux · **[A]** Android phone · **HIGH** = most likely to break.

## 0. Install and first start
- [ ] [A] The draft APK installs over 4.2.1 and keeps paired laptops, PIN-free trust and settings.
- [ ] [W] **HIGH** First start of the new exe keeps the PIN and trusted phones (4.2.1 moved data to `%LOCALAPPDATA%\Edgepad`).
- [ ] [W] Windows Firewall asks once for the new exe path (expected); after Allow, Wi-Fi pairing works.
- [ ] [M] The Mac zip opens; Gatekeeper and Accessibility prompts as before.
- [ ] [L] x64 archive installs with `install.sh`; on an arm64 laptop (if you have one) the arm64 archive runs.
- [ ] [A]+laptop A 4.2.1 laptop and a 5.0 phone show a clear "update the other side" message, not a hang.

## 1. 4.2.1 fixes (never tested)
- [ ] [A]+[W] **HIGH** Shapes: hold two fingers still, lift one, draw with the other. Both-finger drawing still works.
- [ ] [A]+[W] **HIGH** Double-tap-and-drag on a Windows title bar moves the window (does not maximise); hold-still drag in Snipping Tool selects a region. If it feels off, note it: `DOUBLE_CLICK_EXIT_PX` (8) and `TAP_DRAG_MS` (300) are the tuning knobs.
- [ ] [A] Two-finger tap still works after the first lift.
- [ ] [A] Switching a laptop from Wi-Fi to Bluetooth on its row selects the new connection (no false "Reconnecting").
- [ ] [A] A laptop that loses its network shows Reconnecting within about 20 s.
- [ ] [A] Android 12 or 13: leave and re-enter Devices quickly; every laptop is still listed.
- [ ] [A] Stay connected keeps working after the system kills the app in the background.
- [ ] [A] Tapping an unpaired Bluetooth row with Bluetooth off says so.
- [ ] [A] Arabic: the PIN keypad is not mirrored; switches and sliders are readable (darker off track).
- [ ] [M] **HIGH** Mac Bluetooth connect: no stall, no lost HELLO.
- [ ] [L] Type text on a non-US keyboard layout types the right characters, and the clipboard is kept.
- [ ] [L] Tray icon shows on KDE; on GNOME with the AppIndicator extension; without it, the "Edgepad is running" window appears.
- [ ] [W]/[M]/[L] Pairing: cancel or ignore a pairing prompt; the same phone cannot raise it again for a minute.

## 2. Update in place (5.0.0)
To test properly you need two drafts: install the older one, then publish/point at a newer one. Easiest after 5.0.0
is tagged (5.0.0 → 5.0.1). Until then, check the parts you can:
- [ ] [W]/[M]/[L] With a newer release available: a notice appears (tray row with a dot, a system notification once per version).
- [ ] [W]/[M]/[L] **Later** hides it until the next daily check; **Skip this version** never offers it again; a newer one is offered.
- [ ] [W] **HIGH** **Install**: the exe downloads, is checked, the new copy starts and the old one closes; no "second copy" message. SmartScreen may warn on the new exe (expected for an unsigned exe).
- [ ] [W] Updating to a new major version asks Yes/No; No cancels.
- [ ] [M] **HIGH** Install replaces the .app and relaunches; refused (opens the release page) when running from Downloads (App Translocation).
- [ ] [L] A user-writable install is replaced and restarts; a root install (`/usr/local/bin`) shows the `sudo ./install.sh` command instead.
- [ ] [A] Settings › Updates shows **Later** and **Skip this version**; when the app is closed and the daily check finds one, one notification appears (Android 13+: only if notifications are allowed).

## 3. File transfer (protocol 6)
- [ ] [A]→laptop **HIGH** Controls › **Send a file**: pick a photo and a large video (≥ 500 MB). Both arrive in `Downloads/Edgepad` on the laptop, with a progress notification on the phone.
- [ ] [A]→laptop Share › Edgepad from another app (Files, Gallery) for any file type; the confirm names the laptop.
- [ ] laptop→[A] [W] Explorer right-click › Send to › **Edgepad**; [W]/[L]/[M] tray › **Send a file to the phone…**. File lands in the phone's Downloads/Edgepad.
- [ ] Over **Wi-Fi** the transfer uses the second socket (log line about the bulk link); over **Bluetooth** it still works (slower); the trackpad stays smooth while a file moves.
- [ ] **HIGH** Resume: start a big file, turn Wi-Fi off mid-way, turn it back on, send the same file again: it continues, not from zero.
- [ ] Cancel from the phone's notification stops it on both sides.
- [ ] A file over 2 GB is refused on the phone with its own message.
- [ ] A name like `report.pdf` sent twice becomes `report (1).pdf`, never overwritten.
- [ ] [W] The received file has the "downloaded from the internet" mark (Properties › Unblock shows); [M] the quarantine prompt appears on first open.
- [ ] A laptop whose Downloads is on a network drive refuses transfers with a message (expected).

## 4. Media and end of media (4.3.0)
- [ ] [A]+[W] **HIGH** The playing app's real icon shows on the surface for VLC, Windows Media Player, Spotify, a browser (browser shows the media artwork). Note which players show nothing at all (some never report to Windows).
- [ ] [A]+[L] Same with MPRIS players (VLC, Spotify, Firefox).
- [ ] [A]+[M] **HIGH** Now playing for Music and Spotify on the Mac (first use asks Automation permission); position and seek work.
- [ ] Clean titles: "Song (Official Video) [4K] ft. X" shows as "Song"; "Stand with Me" stays whole; TalkBack reads the original full title.
- [ ] Controls › **Sleep when this ends**: Sleep and Shut down. Sleep happens at the end of the track/video; Shut down shows a 60 s countdown on the laptop with Cancel focused. With no timeline (a live stream), it refuses with a message.
- [ ] [L] Shut down without a desktop (no countdown window possible) is refused, never instant.
- [ ] Battery strip: underline style, percent always shown, red when low, bolt when charging; readable over a light background photo.

## 5. Laptop control and reporting (4.3.0)
- [ ] [W] Tray › **Restart as administrator**: UAC prompt, then the trackpad works in Task Manager.
- [ ] Macro list respects the laptop's cap (shows when full).
- [ ] Controls show why a feature is missing ("turned off on the laptop", "missing a program", "not allowed yet") instead of "needs a newer Edgepad".
- [ ] Capture shows "Recording on the laptop" while recording (best effort).

## 6. Phone shortcuts (4.4.0)
- [ ] [A] Quick Settings tile: shows the connected laptop; tapping it opens/acts as designed; with nothing connected it opens the app.
- [ ] [A] The Stay connected notification has volume and play/pause buttons that work with the screen off.
- [ ] [A] **Clipboard history**: the last few laptop copies are listed; tapping one sends it back; Clear empties it.
- [ ] [A] **Presenter**: Next/Previous move slides in PowerPoint, Google Slides (browser) and a PDF viewer; volume keys step slides only while Presenter is open; **Laser** draws a red dot on the laptop that leaves nothing behind.

## 7. Mac and Linux in every language (4.5.0)
- [ ] [M] Switch macOS to German, Arabic, Japanese: tray menu, Security, Pairing, Macro editor and alerts are translated; Arabic reads right to left.
- [ ] [L] Same on Linux (`LANG=de_DE.UTF-8 edgepad`, Arabic, Japanese): shell, tray, first run, fallback window, Security, Pairing, Macro editor.

## 8. Mac and Linux catch up (4.6.0)
- [ ] [L] Refresh-rate dial on X11 (xrandr) and on GNOME Wayland (Mutter): lists the rates and switches.
- [ ] [L] Capture with **Flameshot** as the screenshot tool (issue #75): does the capture reach the phone (clipboard fallback)? Note exactly what happens.
- [ ] [L]/[M] Capture clipboard fallback when the screenshot tool only copies to the clipboard.
- [ ] [M] Fast user switch or screen lock holds the phone off; unlocking resumes.
- [ ] [L] Wayland: macro tiles open apps; no running dot (expected, deferred).

## 9. Per-app profiles (4.7.0)
- [ ] [A] Settings › Surface › **App profiles** on, add the app in front (e.g. Chrome), change a corner dial; switching to Chrome on the laptop swaps the dials on the phone within a moment; switching away restores them.
- [ ] [W]/[M]/[L X11] Works on each; Linux Wayland shows it as unavailable (expected).
- [ ] The app name never appears in the laptop log.

## 10. Air mouse and floating dials (4.8.0)
- [ ] [A] **HIGH** Controls › **Air mouse**: rest a finger on the trackpad and tilt; the pointer moves; sensitivity and dead-zone sliders change the feel; landscape direction is right (the landscape signs are a guess).
- [ ] [A] Phones without a gyroscope say so.
- [ ] [A] **HIGH** Controls › **Floating dials**: asks for "Display over other apps" permission from the tap; dials appear over other apps; touches outside the dials pass through; rotation rebuilds them; switching laptop moves them to the new one.
- [ ] [A] Samsung: edge panel and back gesture still work with floating dials on.

## 11. Laptop notifications on the phone (4.9.0)
- [ ] [W] Run the probe: `Edgepad-<draft>.exe --probe-notifications`, send the "Notification probe" log lines (decides whether Windows gets this feature).
- [ ] [L] Tray › **Forward notifications to phone** on, phone Settings › **Show laptop notifications** on (Android 13+ asks permission): a desktop notification (e.g. `notify-send Hello World`) appears on the phone. Content never in the log.
- [ ] Both switches default off; turning either off stops it.

## 12. Motion that carries
- [ ] [A] Opening a screen from a card grows out of that card; Back shrinks it to the same spot; popups grow from their button. Nothing feels slow (≤ 0.4 s). The trackpad, dials and keys stay instant.
- [ ] [A] With "Remove animations" on (Accessibility), screens cross-fade or switch instantly.
- [ ] [A] The PIN and pairing-code screens switch instantly and cannot be screenshotted.
- [ ] [A] TalkBack announces each new screen's title.
- [ ] [W]/[M]/[L] Windows and menus appear quickly from their control; tray menus stay instant.
- [ ] Site (edgepad.vercel.app after merge): page and menu transitions, docs search (Search button, type, results, Escape closes).

## 13. Site and downloads (after the release is published)
- [ ] The site's download buttons point at the 5.0.0 files, including Linux arm64.
- [ ] `releases/latest` shows 5.0.0 with `SHA256SUMS.txt.sig`.
