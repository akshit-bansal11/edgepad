# Changelog

All notable changes to Edgepad. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html). From 1.0 the wire protocol only changes in a major version. Both apps must still be installed from the same release: they refuse each other at the handshake when their protocol versions differ, and say so.

## [Unreleased]

### Fixed
- **A service name matched inside an ordinary word named the wrong thing.** The laptop reads
  the service out of the browser's window title, and it matched with a plain substring test.
  It also searches *every* visible browser window, not just the one playing, and returns on
  the first title that matches anything — so a tab titled "Twitches (2005)" made the phone say
  **Twitch** while the tab actually playing said YouTube right there in its own title. Each
  name now has to stand as its own word. The trailing lookahead is deliberately not a second
  word boundary: "Paramount+" and "Disney+" end in a non-word character, which has no boundary
  after it, so the obvious spelling would have matched neither. All nineteen names and the
  false positives are covered by tests.

## [2.1.0] - 2026-09-19

Both apps now carry a link to the documentation, and the documentation now exists. Nothing
about the wire protocol moves: it stays at version 3, so a 2.1.0 half and a 2.0.0 half
still speak to each other. Update both anyway, so both ends know where the manual is.

### Added
- **A link to the documentation in both apps.** The phone gets a **Documentation** row under
  Settings › Help, beneath the Guide; the laptop gets a **Documentation** item in its tray
  menu. Both open <https://edgepad-docs.vercel.app>. One page, linked from both halves,
  rather than each half explaining itself. Neither touches the wire protocol.

### Documentation
- **The README's tray-menu listing was stale.** It had never mentioned **Macros…**, added in
  2.0.0, and now also names **Documentation**.
- **A documentation site, in `site/`.** One page: what Edgepad is, how to install and use
  it, the architecture of both halves, the wire protocol, the trust model, and the
  developer guide. Its frame, action and control tables are generated from
  `protocol/frames.txt` and `protocol/actions.txt` at build time — the same two fixtures
  both test suites read — so the page cannot drift from the apps the way a hand-typed
  copy would. A fixture it cannot parse fails the build rather than rendering an empty
  table. Next.js, checked by its own gate (Biome, ESLint, tsc) in its own workflow, which
  runs only when `site/**` or `protocol/**` changes.
- **`SECURITY.md` claimed the phone cannot send a key code.** It can, and always could:
  `KEY` carries a raw Windows virtual-key code and `TEXT` kind 3 carries arbitrary text,
  because that is what the phone's keyboard screen is. 2.0.0 corrected this claim in
  three places and missed this one, which is the file a reader checks first. The action
  and control table keeps the two halves independently versionable; it is not a
  containment boundary. The trust boundary is the Bluetooth pairing plus
  trust-on-first-use.

## [2.0.0] - 2026-09-17

Macro buttons: the phone can now launch things on the laptop. That is the headline, and it is why this is a
major release rather than a minor one — **the wire protocol is unchanged at version 3**. The refresh-rate
dial and the macro buttons are new ids in tables that already existed, and an unknown id is dropped and
counted rather than treated as an error, so a 2.0.0 half and a 1.0.0 half still speak to each other. Update
both anyway: only the pair knows about the new controls.

### Added
- **Macro buttons.** The laptop's tray menu gains a Macros editor: name an app, a document, a folder or a
  URL, and it appears as a button on the phone — a 5x3 grid, fifteen of them. Fifteen is what one frame can
  name, so a full grid always arrives labelled rather than trailing off into blank buttons. The phone sends
  a slot number, never what the slot opens: a button cannot be repointed from the phone, and the laptop's
  own list is the only thing that decides what runs.
- **A refresh-rate dial.** Steps the laptop's display through the rates it actually offers, filtered to the
  resolution and colour depth already in use so a rate can never drag the desktop to another size. The
  switch is for this session only; a dial should not decide what the desktop boots at.
- **An on-screen readout on the laptop.** Sliding volume, microphone or brightness from the phone now shows
  a small panel on the laptop. Windows draws one for its own volume keys but not for a level set through
  Core Audio or WMI, so until now brightness changed with no feedback at all.
- **Pointer speed and scroll speed** are settings rather than constants, on the renamed Trackpad screen.
- **A sensitivity of its own for each dial kind.** Volume runs 0-100 under a thumb and wants a slow ruler
  where the app switcher wants a fast one. A dial left on SHARED still follows the one slider.

### Changed
- **Two-finger gestures are fixed and no longer assignable.** Drag to scroll, pinch to zoom, tap to
  right-click. They are what a hand already expects from a trackpad, and a phone that answers them
  differently reads as broken rather than as configured. Three and four fingers stay assignable.
- A dial still goes in one of the four corners and nowhere else. Seven kinds now compete for those four
  places, which is the corners screen's job to settle.
- **What the app says about its own security is now true.** Three places — including a label in the Macros
  window — claimed the phone "can never name a program of its own". It never could not: the phone's
  keyboard sends a raw key code and arbitrary text straight to the laptop, because that is what a keyboard
  screen is, and Win+R with a typed line is already arbitrary execution. The macro index is still worth
  having, but it contains nothing the keyboard does not already allow. A paired phone is a trusted input
  device, and the trust boundary is the Bluetooth pairing, not the macro list.

### Fixed
- **Two-finger scroll no longer starts late.** Every sample advanced the last-seen position, including the
  ones spent below the slop deciding scroll from pinch, so the travel spent deciding was dropped and every
  stroke began 8 dp behind the finger. Pinch never had the bug, because it only advances its reference once
  a mode is settled.
- **A macro added while the phone is connected appears at once.** The names went out once at the handshake
  and never again, so a new button needed the app closed and opened.
- **Reconnecting during a refresh-rate change could kill the tray app.** The mode list was read twice while
  a reconnect was emptying and refilling it; between the two reads it could be empty, and an unhandled
  error on a background thread ends the process rather than the dial.
- **A phone flipping between two refresh rates could blank the screen for ever.** A switch to the rate
  already in force is now free.
- **OK in the Macros window could freeze the laptop app.** It wrote to the Bluetooth socket on the thread
  drawing the interface, so a phone that had stopped reading took the tray, the editor and the menu with it.
- **A fast dial drag could bog the laptop down.** The on-screen readout queued a repaint per frame from a
  higher-priority thread; it now keeps one in flight and draws the newest value.
- The laptop's log stopped recording display failures after the first one, which was usually written at
  startup — so the errors it exists for were the ones it silenced.

### Documentation
- PROTOCOL.md records why new ids did not move the version, and what would.

## [1.0.0] - 2026-09-16

The first stable release. How the app behaves is unchanged from 0.10.0; what changes is the promise around
it. The wire protocol is settled at version 3 and now only moves in a major release, and the repository is
arranged for someone other than its author to work in.

### Changed
- Both layout editors draw on one shared canvas: the dot grid, the border, the centre lines and the
  snapping existed twice, in two files that had drifted apart in small ways.
- The gamepad's shape rules — a shoulder button's proportions, the d-pad's grid and which of its cells are
  arms — are in one place instead of being declared separately by the editor and the live gamepad.
- The control surface's gear, keyboard and gamepad buttons are one list, rather than three of every field
  and a branch each in six methods.
- A slider over a range of settings carries its own step arithmetic; each screen used to write the same
  three formulas by hand, and one screen wrote one of them out three times.
- `Perimeter` is the single place that says a screen has four corners. Three files each had their own copy.
- On the laptop, one table decides whether a key is an extended key. There were two, which could disagree.

### Fixed
- The gamepad no longer allocates while drawing (a rectangle per d-pad cell, every frame) or while being
  touched (a direction-bit array on every change). Android lint's `DrawAllocation` only inspects a method
  literally named `onDraw`, so neither was ever reported.
- The changelog had two different `0.9.5` sections. The release workflow reads the first match, so the
  second could never have been published.

### Documentation
- The README says what Edgepad is, who it is for and what it replaces, before any badge.
- CONTRIBUTING covers building each half, running the two against each other, what cannot be checked
  without a phone and a laptop to hand, and where to start without them.
- ARCHITECTURE explains the golden protocol fixtures: `protocol/frames.txt` and `protocol/actions.txt` are
  read by both test suites, so changing one side's codec without the other fails a test rather than
  shipping two apps that no longer understand each other.

## [0.10.0] - 2026-09-15

The phone app is redesigned after the owner's 2026-09-15 design. The protocol and the laptop app are unchanged; install both from the same release as always.

### Added
- A guide in five pages (why Edgepad, its anatomy, the surface, the keyboard, the gamepad), shown one page at a time with a drawing or all on one scrolling page. It opens on the first run, ending at FIND MY LAPTOP, and again from Settings › Help › Guide.
- The Devices screen shows the connected laptop's live round trip beside it, a ring round the dot while a connection is being made, and a Bluetooth settings button in its title row.
- Settings pages of their own for Corners, Dial feel and Background & pattern, each summarised on the Settings screen, and the app and protocol versions at its foot.
- The layout editors and the gamepad have an options button whose popup holds the media size, the presets, RESET and DONE. The gamepad's popup also opens the layout editor.

### Changed
- JetBrains Mono (OFL) for every piece of text, bundled with the app; pure black on white or white on black, with the design's greys.
- A 52 dp title row on every page, square segmented controls, 48 dp buttons, and two columns on every settings page when the phone is sideways.
- Corners and gestures are picked from a list on the page, with a dot on the current choice, instead of a dialog.
- The layout editors fill the screen with the system bars hidden, as the surface does, so what is laid out has the surface's proportions. Pieces at rest are dashed; the one being dragged lifts on a shadow. RESET in the media editor also resets the size.
- The now-playing logo is 40 dp and the play button 52 dp.

### Removed
- The old onboarding screen, replaced by the guide.

## [0.9.5] - 2026-09-12

### Fixed
- The Windows exe and tray icon are the supplied `assets/edgepad-light.svg`, rendered from the file itself. 0.9.3 had redrawn the mark by hand: the arc came out the wrong radius, the mark sat off centre, and the tile was the wrong grey.
- The Android launcher icon takes the brand's colours (#CDCDCD on #1A1A1A). Its drawing is unchanged: the launcher masks its outer edge, so the mark stays inside the safe zone rather than filling the tile as the app icon does.

## [0.9.4] - 2026-09-12

### Added
- The Edgepad mark on the onboarding, finder and Settings screens, from the owner's two SVGs: a dark tile on the light theme, a light tile on the dark one. The README shows whichever matches the reader's theme.

## [0.9.3] - 2026-09-12

### Added
- The Windows exe and its tray entry carry the Edgepad mark, the same corner dial as the Android launcher icon. The mark is in the repository as `assets/edgepad.svg`.

## [0.9.2] - 2026-09-12

### Fixed
- Prime Video, Apple TV, Apple TV+ and Peacock showed their light logo on the dark theme and their dark logo on the light theme; the files are named for the theme they belong to, and are now picked that way.

## [0.9.1] - 2026-09-12

### Changed
- Every icon in the app is now a [Lucide](https://lucide.dev) icon (ISC): the refresh arrow and gear on the finder, chevrons, the gear, keyboard and gamepad buttons on the surface, skip, play and pause. A player without a logo of its own shows a music note instead of its initial. The player logos are unchanged.
- The dial preview in Settings scales down when the ruler is longer than its box, so a length past 150 dp is seen growing instead of running off the edge.
- Slider steps sit a little further below the track, and every slider has room under it.
- The corner dials grab a shallower band of the trackpad at the bottom corners, and at every corner when the phone is sideways; upright, the top corners keep the deep band.

## [0.9.0] - 2026-09-12

### Added
- The finder has a refresh button that re-reads the paired list, and a gear into Settings, in its title row.
- A Landscape toggle under Appearance: the app is held upright or sideways, never rotating freely. The keyboard, the gamepad and the gamepad layout editor are always sideways.
- Laptop: a browser playing Netflix, YouTube, Prime Video and other services is named after the service, read from the browser window's title, so the phone draws the service's logo instead of the browser's initial.

### Changed
- Corner rulers are trimmed at their far ends so two never overlap and none runs under the buttons at the top; the gear, keyboard and gamepad buttons are spaced 64 dp apart.
- The now-playing block and transport are hidden while the laptop has no player open; that room is trackpad.
- Keyboard modifiers: Shift, Ctrl, Alt and Menu are down while held, and a tap alone presses them at once and arms them for the next key. Win is an ordinary key, so a tap opens Start immediately instead of after the next key.
- Settings: slider steps are dots under the track; the theme, control colour, background and pattern choices are lower pills; colours are picked from a strip of swatches instead of hue, saturation and value sliders.

### Removed
- The SETTINGS link at the bottom of the finder, and the hue, saturation and value sliders.

## [0.8.1] - 2026-09-12

### Fixed
- The control surface crashed on a portrait phone since 0.7.1: the now-playing box's smallest width was larger than the room between the corner dials, and the clamp threw. The box now keeps its minimum and overlaps the dials' reach instead. The crash hit on every launch that auto-connected, and on Back from Settings.

## [0.8.0] - 2026-09-12

### Added
- A full on-screen keyboard: six rows with function keys, numbers, modifiers that stay held until the next key, arrows, and a back button to the controls. Several keys can be held at once.
- A gamepad screen: buttons, shoulders, a d-pad and a stick, each mapped to keyboard keys on the laptop, with multi-touch so a thumb on the stick and a button press work together. Four presets (Xbox, Platformer, Racing, Shooter).
- A gamepad layout editor with the same snap grid and centre lines as the media layout: drag any control, long-press to resize, pick a preset, reset. Reached from the gamepad's Edit button or from Settings.
- Protocol: a KEY frame (0x22) presses or releases one key by Windows virtual-key code; the laptop sends scan codes too, so games see the keys.

### Changed
- The keyboard button at the top of the surface opens the keyboard screen instead of the phone's own keyboard; a gamepad button sits beside it.

## [0.7.2] - 2026-09-12

### Added
- Every finger on the trackpad is drawn; one finger leaves a fading, tapering tail, and two fingers show the line between them and a ring on their span while pinching.

### Fixed
- Two-finger taps and other multi-finger gestures: a second finger landing moved the gesture's centroid and counted as a slide, so no multi-finger tap ever fired.

## [0.7.1] - 2026-09-12

### Added
- A size slider on the Media layout page scales every media piece.
- The now-playing box fits its title, centred on its position, and cuts long titles with an ellipsis before reaching the corner dials.

### Changed
- Dial length and height default to their smallest values (100 dp, x0.6), and the sliders reach further down (60 dp, x0.4).
- Every slider in Settings is a plain line with a tick at each step and a round thumb.
- Theme, control colour, background and pattern choices are rounded pills.
- The square grid at 35 dp and 15% is the default pattern.
- The play/pause button is smaller.
- The media layout grid snaps every 12 dp.
- The laptop's name and round trip no longer sit on the surface; Settings shows them.

### Fixed
- Logos with transparent gradient stops (Netflix) did not render; logos are drawn to bitmaps.
- The Forget button lost its side padding to its border.

## [0.7.0] - 2026-09-12

### Added
- Player logos from the bundled SVGs, in their own colours; dark and light variants follow the theme.
- Backgrounds for the control surface: a colour, a gradient with an angle, or an imported image, with an optional grid, dot or checker pattern with its own size, colour and opacity.
- One control colour for every dial, button and label, or automatic contrast against the background.
- A live preview of a corner dial under the dial length and height sliders.
- The media layout grid snaps pieces to 24 dp points and to the centre lines, which light up when a piece sits on them; a Reset button.

### Changed
- The keyboard types on the laptop as each character is entered, rather than when a suggestion is chosen.
- The keyboard button sits at the top beside the Settings gear.
- The media controls default to the bottom: the player and the track, then previous, play and next under them. The progress line under the track appears only when no corner holds the Media dial, and can be slid to seek.

### Fixed
- The play/pause button showed a plain disc; the play and pause glyphs are drawn again.

## [0.6.2] - 2026-09-11

### Added
- Documentation: architecture, wire protocol, contributing, security and this changelog; issue and pull-request templates; Dependabot.

### Changed
- Release notes come from the changelog.
- Housekeeping from a review of both apps; no behaviour change intended.

## [0.6.1] - 2026-09-11

### Added
- Dial length and dial height in Settings.

### Changed
- The Settings gear is drawn as a cog.
- Settings pages hide the scroll bar; the Forget button has room round its label.

### Removed
- The keyboard-backlight row, which had nothing behind it.

## [0.6.0] - 2026-09-11

### Added
- A keyboard button: the phone's keyboard types on the laptop (protocol 3, TEXT kind 3).

### Changed
- The gear sits at the top-edge centre in both orientations.
- The plain screens use the system sans for labels, with larger sizes and taller rows.

## [0.5.0] - 2026-09-11

### Added
- Every two-, three- and four-finger gesture can be assigned an action in Settings, including continuous ones that step with the swipe (volume, brightness, zoom, app switcher). Actions 31 to 34 on the laptop.
- A Media layout screen: drag the now-playing block, the play button and the skip pair anywhere.
- Drawn marks for the popular players.

### Changed
- Dials live only in the four corners; Settings picks each corner's dial.
- The trackpad has no box or lines, hints are off by default, and the Apps ruler shows no number.

### Removed
- Edge dials and the drag-along-the-edge placement editor.

## [0.4.0] - 2026-09-11

### Added
- The device list shows only paired computers; tapping one connects.

### Changed
- Now playing, progress and transport sit together; the trackpad box is gone.
- In landscape, corner dials stay and edge dials turn onto the long edges.

### Fixed
- Quitting the laptop app threw inside Bluetooth's StopAdvertising, which left a stuck process holding the single-instance lock so no update could take over and the phone could not reconnect.
- The laptop's media reader failed while a player started or closed.

## [0.3.2] - 2026-09-11

Superseded by 0.4.0 before it was tagged; its fixes are listed there.

## [0.3.1] - 2026-09-11

### Fixed
- The controls crashed on opening: a placeholder edge path clamped its radius into an empty range.

## [0.3.0] - 2026-09-11

### Added
- The owner's design: edge rulers you slide along, bending round the display's own rounded corners; monochrome screens for onboarding, finding the laptop, settings and connection lost; a landscape layout; sensitivity, haptics, snapping, natural scrolling, hints and a dark/light switch.
- The laptop follows brightness changed on the laptop itself, and sends the media timeline (protocol 2).
- A newer laptop app asks the running one to quit and takes over; the tray menu shows the version.

### Fixed
- Values reset to 0 when the controls were reopened or the phone rotated; the laptop's last report is now kept by the activity.

## [0.2.0] - 2026-09-11

### Added
- The phone's controls: corner dials for volume, brightness, media and more, placeable along the edges; a trackpad with one- to four-finger gestures; a media bar; a picker that remembers the laptop; onboarding; a reconnecting screen; a dark/light toggle.
- Laptop state back to the phone: volume, mute, microphone and brightness, with audio changes watched live; media sessions with now playing, app name, position and seek; TEXT frames and the media-position control.
- The exe is a compressed single file.

## [0.1.0] - 2026-09-11

### Added
- The Bluetooth link: an RFCOMM service on the laptop, a phone that connects to it, and a live round-trip readout.
- The laptop's action layer: a dispatcher for every frame, input injection that releases held keys when a session ends, Core Audio volume and microphone, WMI brightness, trust on first use, start with Windows.
- CI for both apps and a tag-triggered release with a signed APK and a self-contained exe.

[Unreleased]: https://github.com/akshit-bansal11/edgepad/compare/v2.1.0...HEAD
[2.1.0]: https://github.com/akshit-bansal11/edgepad/compare/v2.0.0...v2.1.0
[2.0.0]: https://github.com/akshit-bansal11/edgepad/compare/v1.0.0...v2.0.0
[1.0.0]: https://github.com/akshit-bansal11/edgepad/compare/v0.10.0...v1.0.0
[0.10.0]: https://github.com/akshit-bansal11/edgepad/compare/v0.9.5...v0.10.0
[0.9.5]: https://github.com/akshit-bansal11/edgepad/compare/v0.9.4...v0.9.5
[0.9.4]: https://github.com/akshit-bansal11/edgepad/compare/v0.9.3...v0.9.4
[0.9.3]: https://github.com/akshit-bansal11/edgepad/compare/v0.9.2...v0.9.3
[0.9.2]: https://github.com/akshit-bansal11/edgepad/compare/v0.9.1...v0.9.2
[0.9.1]: https://github.com/akshit-bansal11/edgepad/compare/v0.9.0...v0.9.1
[0.9.0]: https://github.com/akshit-bansal11/edgepad/compare/v0.8.1...v0.9.0
[0.8.1]: https://github.com/akshit-bansal11/edgepad/compare/v0.8.0...v0.8.1
[0.8.0]: https://github.com/akshit-bansal11/edgepad/compare/v0.7.2...v0.8.0
[0.7.2]: https://github.com/akshit-bansal11/edgepad/compare/v0.7.1...v0.7.2
[0.7.1]: https://github.com/akshit-bansal11/edgepad/compare/v0.7.0...v0.7.1
[0.7.0]: https://github.com/akshit-bansal11/edgepad/compare/v0.6.2...v0.7.0
[0.6.2]: https://github.com/akshit-bansal11/edgepad/compare/v0.6.1...v0.6.2
[0.6.1]: https://github.com/akshit-bansal11/edgepad/compare/v0.6.0...v0.6.1
[0.6.0]: https://github.com/akshit-bansal11/edgepad/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/akshit-bansal11/edgepad/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/akshit-bansal11/edgepad/compare/v0.3.1...v0.4.0
[0.3.2]: https://github.com/akshit-bansal11/edgepad/compare/v0.3.1...v0.4.0
[0.3.1]: https://github.com/akshit-bansal11/edgepad/compare/v0.3.0...v0.3.1
[0.3.0]: https://github.com/akshit-bansal11/edgepad/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/akshit-bansal11/edgepad/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/akshit-bansal11/edgepad/releases/tag/v0.1.0
