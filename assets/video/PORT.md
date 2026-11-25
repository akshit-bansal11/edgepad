# Edgepad 5 film: what to draw, and from where

The renderers in ui/ are a canvas port of the Android drawing code at 1 unit = 1 dp. Each block names the Kotlin it
follows; keep doing that. K = android/app/src/main/java/me/akshitbansal/edgepad/.

## Already ported (reuse as they are)

| Renderer | Lines | Follows |
|---|---|---|
| screens.js helpers (wrapLines, bar, section, card, toggle, chip, segmented, button, largeTitle) | 39–188 | Page.kt |
| S.devices | 191–256 | PickerScreen.kt |
| Deck primitives and S.macros | 257–386 | Deck.kt, DeckPage.kt, DeckLayout.kt, MacroScreen.kt |
| S.keyboard | 540–601 | KeyboardScreen.kt, KeyboardLayouts.kt |
| S.annotate | 602–818 | AnnotateScreen.kt, AnnotateCanvas.kt |
| S.gamepad | 852–932 | GamepadLayout.kt, GamepadScreen.kt |
| S.pin | 933–971 | PinScreen.kt |
| S.corners | 972–1024 | CornersScreen.kt |
| S.gestures | 1025–1083 | GestureScreen.kt |
| S.mediaLayout | 1084–1114 | MediaLayoutScreen.kt |
| surface.js ruler, fingers, backdrop | 6–252 | RulerPainter.kt, DialSpan.kt, Backdrop.kt |
| beats.js laptop cards: level, winVolume, trust, security, pairing, screen, saved, clip | 44–362 | LevelOverlay.cs etc. |

## Update for 5.x

| ID | What | Where | Change (source) |
|---|---|---|---|
| U1 | The surface's top row | surface.js 311–333 | Four buttons, Controls −1.5, Type −0.5, Lock 0.5, Settings 1.5 (K surface/ControlSurface.kt:288–297) |
| U2 | Controls popup | screens.js 514–538 | The "Controls" heading and pencil, 128×112 tiles in the order Keyboard, Gamepad, Macros, Presenter, Capture, Annotate, Clipboard, Stats, Send a file, Developer (developer mode only), then the Edit chip. The switch chips were removed (K screens/ControlsPopup.kt:66–72, 345) |
| U3 | Stencil icons | screens.js 387–511 | Add Presenter, Clipboard, Stats (System), Developer and the Send paper plane (K screens/StencilIcon.kt) |
| U4 | Capture | screens.js 819–851 | Region is a plain drag, the hold ring fills on the pad, and saving is the "To phone" chip (K screens/CaptureScreen.kt:211) |
| U5 | Keyboard sizes | screens.js 540–601 | Draw all six sizes from KeyboardLayouts.kt:138–289 and add the key-reflow morph (MOTION.md) |
| U6 | The mark | kit.js 154–174 | The 5.0.1 rebuilt logo paths from assets/brand/edgepad/edgepad-logo-light.svg |
| U7 | Status and nav bars | screens.js 1209–1215 | Hidden in 5.x: draw none |
| U8 | Stay-connected notice | screens.js 1166–1208 | Add the play/pause and volume buttons (only if a frame shows it; the storyboard does not) |
| U9 | Laptop tray menu | beats.js 180–199 | 5.x submenus (only if shown; the storyboard does not) |
| U10 | Gesture hints | surface.js 335–346 | Off by default: draw no hint text |
| U11 | Battery strip | — | Not drawn: the docs and the code disagree on its look. Leave it out |

## New to port

| ID | Screen | Kotlin source | Notes |
|---|---|---|---|
| N1 | Presenter | K screens/PresenterScreen.kt | The laser pad fills the stage. Previous and Next are a row below when upright, tall side columns when sideways |
| N2 | Laser on the laptop | windows/src/Edgepad/Annotation/AnnotationOverlay.cs:160–225 | A red dot with a white core, a ring and a glow, and the 250 ms tapered trail |
| N3 | Transfer card | K screens/XferCard.kt, XferCardView.kt | The title, file name, "2 of 3", a bar with percent (no speed figure), Pause/Resume, Stop, then "Sent N files" |
| N4 | Windows transfer toast | windows/src/Edgepad/Ui/TransferToast.cs:134, 253 | Near the tray, mirroring N3 |
| N5 | Clipboard sheet | K screens/ClipboardSheet.kt | Cards with a trimmed preview, source and age; tap resends |
| N6 | AI screen | K screens/AiScreen.kt, AiLayout.kt, RingGauge.kt | One card per tool, usage rings (green, yellow from 50%, orange from 75%, red from 90%), spend, sessions with context bars |
| N7 | Git screen | K screens/GitScreen.kt | Repos column, then PRs and issues under Mine / Review / Assigned, with host chips. Neutral repos only |
| N8 | Developer hub pager | K screens/DeveloperHub.kt | AI and Git pages with dots |
| N9 | Gamepad layout editor | K screens/GamepadLayoutScreen.kt | Sideways, a snapping grid, centre lines lighting, the layout options list with the five presets |
| N10 | Air mouse surface | K surface/ControlSurface.kt:144–212, 631, 917–931 | Only the pad, the "Air mouse · Off" pill, and Left and Right (a bottom row upright) |
| N11 | Settings › Developer switch | K screens/SettingsScreen.kt:135 | One section with the Developer mode switch |
| N12 | Keyboard settings › Size | K screens/KeyboardSettingsScreen.kt:17–53 | The Size segmented control driving U5 |
| N13 | Sleep countdown on the laptop | windows/src/Edgepad/Dispatch/ShutdownCountdown.cs | The 60 s countdown, "Sleep the laptop", Cancel |
| N14 | The laptop and room | new: EP.laptop, EP.laptopScreen | DESIGN.md "Laptop shape". Screen kinds: race, desktop, notes, slides, video, editor, browser-pr, terminal, credits |
| N15 | The racing game | new: ui/game.js | GAME.md in full |
| N16 | The dial ident | new: ui/ident.js | MOTION.md M-IDENT, ported from scripts/logo-motion.ps1 |

## Pipeline changes

| ID | Change |
|---|---|
| V1 | voice.py: read an optional per-line `hold` (seconds of extra silence after the line) and an optional script-level `poster_line`. The scripts stay plain strings, so put holds in a parallel `holds` map in the script JSON: `"holds": {"9": 2.0}` |
| V2 | New compositions: `demo5.html` (16:9, CUT id demo5, script demo5), `cuts/instagram5.html` (1080x1920, script reel5), `cuts/linkedin5.html` (1080x1440, script reel5), plus covers `cuts/thumb5-landscape.html` and `cuts/thumb5-portrait.html` |
| V3 | beats.js: new `BEATS.demo5` and `BEATS.reel5`, one entry per line, in STORYBOARD.md's order |
| V4 | sfx.py (SOUND.md) and an `<audio id="sfx">` track 2 in each composition |
| V5 | The `video` job (.github/workflows/ci.yml 384–530): install earshot into the venv, then run voice.py, music.py and sfx.py for demo5 and reel5. Stamp data-duration from out/demo5 and out/reel5. Render demo5, instagram5 and linkedin5 at 30 fps with `--video-bitrate 20M`, plus the covers. Make contact sheets for all three. Upload the `edgepad-video` and `edgepad-video-sheets` artifacts |
| V6 | Keep the 4.0 compositions and scripts until the PR that ships the new film. That PR removes them from the job's render list, so the job renders only the 5.x cuts |
