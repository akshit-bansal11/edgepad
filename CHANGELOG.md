# Changelog

All notable changes to Edgepad. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Until 1.0 a minor version may change the protocol; both apps must be installed from the same release.

## [Unreleased]

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

[Unreleased]: https://github.com/akshit-bansal11/edgepad/compare/v0.6.2...HEAD
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
