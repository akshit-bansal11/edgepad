# Edgepad 5 film: facts

Every claim the two scripts make, with what proves it in v5.1.0.

Path prefixes: K = android/app/src/main/java/me/akshitbansal/edgepad/, V = android/app/src/main/res/values/,
UG = docs/USER-GUIDE.md, CL = CHANGELOG.md.

**OWNER CHECK** marks a line the code supports but the docs call untested on a device, or where sources disagree.
Confirm or cut it before the build.

| # | Claim (script words) | Evidence |
|---|---|---|
| 1 | "free, open-source Android app" | LICENSE; README; Android 12 or later (UG:59) |
| 2 | "trackpad, keyboard, gamepad and remote for your laptop. Windows, Mac or Linux." | README; UG:9, 19, 30 |
| 3 | "multi-touch trackpad" | K surface/ControlSurface.kt; UG:77–93 (one to four fingers) |
| 4 | "The corners are dials" / "volume, brightness, zoom, scrub" | Default corners Volume, Brightness, Zoom, Media scrub: K surface/DialKind.kt:17–33; V strings.xml:363–386 |
| 5 | "Split the corners, and four dials become eight" | "Gives each corner two dials, one on each edge. Up to eight in all." V strings.xml:164–171; CL:299 |
| 6 | "Change the volume on the laptop, and the dial moves with it" | Windows listens for endpoint changes: windows/src/Edgepad/Controls/AudioEndpoint.cs:41–42. Mac polls twice a second: UG:212. Linux: not confirmed, so the film shows it on Windows only |
| 7 | "Tap Controls" | K screens/ControlsPopup.kt: the "Controls" heading, pencil and tiles |
| 8 | "A full keyboard, in six sizes, from a full board with a number pad down to a compact sixty percent" | 100, 96, 80, 75, 65 and 60%, default 75: K screens/KeyboardLayouts.kt:43–52, 138–289; K Settings.kt:640 |
| 9 | "two sticks and analog triggers" | Xbox preset L/R sticks, LT/RT: K gamepad/GamepadLayout.kt:344–363. Trigger 0..255: GamepadLayout.kt:57–58 |
| 10 | "On Windows and Linux, your laptop sees a real Xbox controller" | Windows ViGEmBus virtual Xbox 360 pad: windows/src/Edgepad/Gamepad/VirtualPad.cs:6, 78. Linux uinput "Edgepad virtual Xbox 360 controller": docs/LINUX.md:59–61 |
| 10a | "so your games just work" | True only with ViGEmBus installed by the user on Windows (UG:17); without it the pad sends keys. The line is fair as marketing, but keep "On Windows and Linux" attached. OWNER CHECK: keep or soften to "so your games see a controller" (the 4.0 wording) |
| 11 | "Drag every control where your thumbs want it" | Gamepad layout editor, "Drag to move. Long-press a control for its menu.": K screens/GamepadLayoutScreen.kt; V strings_gamepad.xml:9 |
| 12 | "start from a racing, platformer or shooter layout" | Presets Xbox, Xbox keys, Platformer, Racing, Shooter: K gamepad/GamepadLayout.kt:203–207, 319 |
| 13 | "Presenter steps through your slides" | "Next slide" / "Previous slide": V strings_shortcuts.xml:22–23; K screens/PresenterScreen.kt |
| 14 | "a laser dot follows your thumb" | Laser sizes and a trail tapering over 250 ms: K LaserSizes.kt:8–14; V strings_shortcuts.xml:27–32; CL:46 |
| 15 | "Capture a region, the full screen or a recording" | "Region", "Full screen", "Record": V strings.xml:444–447. OWNER CHECK: UG:123 and UG:127–129 call capture and Record unverified on real laptops |
| 16 | "send it straight to your phone" | The "To phone" chip: K screens/CaptureScreen.kt:211. Not a "Save to: Laptop / Phone" control |
| 17 | "Clipboard sync works both ways" | "Copy on one, paste on the other: text and images.": V strings.xml:154–155 |
| 18 | "and keeps a history" | Clipboard sheet, "From laptop", "From this phone", "Just now", "Nothing copied yet": V strings_shortcuts.xml:10–20; K screens/ClipboardSheet.kt |
| 19 | "Send files either way, up to two gigabytes each" | 2 GB per file: K link/xfer/XferLimits.kt:36; CL 5.0.0 "send a file" |
| 20 | "Pick a few, watch them go, pause any time" | Up to 100 files per batch: K MainActivity.kt:3517. Card with Pause/Resume, Stop, "Sent N files": K screens/XferCardView.kt:130–134; V strings.xml:593–619 |
| 21 | "Macros open apps, folders and websites" | A macro opens a program, file, folder or web address: shared/Edgepad.Core/Macros/MacroStore.cs:576; README:57. Not scripts, not key sequences |
| 22 | "Draw a shape on the trackpad, and one runs" | Shapes run "Macro · X": V strings.xml:285, 301–303 |
| 23 | "Turn on developer mode" | The switch in Settings › Developer: K screens/SettingsScreen.kt:135; V strings_dashboard.xml:7–9 |
| 24 | "Your Claude Code and Codex limits, your spend and every open session" | Usage for Claude Code (5 h, weekly, model weekly, credits) and Codex (5 h, weekly). Spend for today, 7 and 30 days. Sessions with context: V strings_ai.xml:4–10; strings_dashboard.xml:38–62; CL:63. Needs the tool connected in the laptop's Integrations window |
| 25 | "Pull requests and issues from GitHub, GitLab and more. Tap one, and it opens on the laptop." | Hosts GitHub, GitLab, Gitea, Forgejo, Codeberg, Bitbucket, Gitee. Mine, Review and Assigned tabs: V strings_dashboard.xml:64–101. Opens in the laptop's browser (round two, Notion 2026-11-22) |
| 26 | "Air mouse: tilt to aim, hold a button to drag" | "To drag, hold Left or Right and tilt. Lift your thumb to drop.": V strings_air.xml:4–7; CL 5.1.0 |
| 27 | "Edgepad puts the laptop to sleep when it ends" | "Sleep the laptop", 60 s countdown: V strings.xml:571–577; UG:153. Windows and Linux. OWNER CHECK on the Mac: CL:41 and UG:153 disagree. The film shows Windows |
| 28 | "No account. No server." | No sign-in and no Edgepad-run service. The phone only contacts the update check and an update you choose (CL 5.0.0 About note). Weather (Open-Meteo), the speed test (Cloudflare) and Git hosts are opt-in third parties, so the line means Edgepad runs no server of its own, not that the phone never goes online (the owner shortened "no Edgepad server" to "no server" on 2026-11-25) |
| 29 | "over Bluetooth, Wi-Fi or USB" | UG:73–75. Bluetooth is the default. The Mac has no USB (UG:26), so the line names transports, not "on every laptop" |
| 30 | "Windows is tested on real hardware. Mac and Linux are newer" | CL 5.0.0 intro: Mac and Linux passed CI only; README:41 "still in testing" |
| 31 | "Coming in Edgepad six: screen mirroring, and remote access" | Owner decision, 6.0.0 plan (2026-11-22). No date, no further detail, per the brief |
| 32 | "Free and open source, on GitHub" | https://github.com/akshit-bansal11/edgepad |

## Not claimed, on purpose

- A drag-anywhere keyboard canvas. The keyboard has six fixed sizes only: Size, Function keys and Keyboard text size
  (K screens/KeyboardSettingsScreen.kt:17–53). The user guide said otherwise until PR #89 corrected it.
- Script or keystroke macros.
- AI limits for Gemini CLI or OpenCode (sessions only).
- A virtual controller on the Mac (keyboard mode only, UG:217).
- Any latency, ping, speed-test or transfer-speed figure.
