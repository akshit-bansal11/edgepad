# Changelog

All notable changes to Edgepad. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html). From 1.0 the wire protocol only changes in a major version. Both apps must still be installed from the same release: they refuse each other at the handshake when their protocol versions differ, and say so.

## [Unreleased]

## [3.0.0] - 2026-09-24

The gamepad stops pretending. It drives a real Xbox controller on the laptop, its sticks and triggers
are analog, and which controls exist and what each one drives are the user's to choose and bind, with a
named layout kept per game. Carrying a whole controller needs a frame version 3 had no room for, so
**the wire protocol moves to version 4** -- its first move since 0.6.0, and the rule has always been
that this number moves only in a major release. **Install both halves from this release.** A 3.0.0 half
and a 2.x half refuse each other at the handshake and say so. One thing is lost on update, once: a
gamepad layout saved by 2.x is refused rather than migrated, which is under Changed.

### Added
- **The gamepad drives a real controller.** Every control on it used to send a key, and the stick threw
  its analog value away: the smooth travel under the thumb was quantised to eight compass sectors and
  pressed WASD, so a half push and a full push were the same key, and a game that reads controllers
  rather than keys saw nothing at all. The laptop now plugs a virtual Xbox pad into Windows, and the
  phone sends the whole controller in one frame -- the buttons, both analog triggers and four analog
  axes -- laid out byte for byte like Windows' own `XINPUT_GAMEPAD`, so the laptop copies each field
  onto the pad's report rather than translating it. Games that only ever accepted a controller can be
  played from the phone.

  A stick scales its magnitude rather than each axis on its own, which keeps a diagonal pointing where
  the thumb does instead of squaring the circle the thumb moves in. The dead zone comes out of the
  travel and what is left is stretched back over the range, so the first step past it is a nudge and
  the rim is exactly full deflection. That arithmetic is the part most likely to be wrong, so it lives
  in a file with no Android type in it and is tested on the JVM -- until now nothing covered touch to
  output at all, because it sat inside a view's touch handler where no test could reach it.
- **A keyboard fallback that says why it is a keyboard.** The virtual pad comes from **ViGEmBus**, a
  separate third-party driver the laptop may simply not have. Without it nothing goes quietly dead: the
  gamepad falls back to pressing keys and writes across the middle of the pad which of three states it
  is in -- waiting for the laptop to answer, no controller driver on the laptop, or the laptop could not
  plug the controller in. Which state it is in is the laptop's answer and not a setting: the phone asks
  for a controller when the gamepad opens and hands it back when it closes, and the laptop replies with
  a status token. A laptop too old to know the question never answers, which reads as waiting.

  Five layouts ship with the app rather than four. The new **Xbox** is every control bound to a real
  controller input, and does nothing at all in keyboard mode; **Xbox keys**, **Platformer**, **Racing**
  and **Shooter** are 2.x's keyboard layouts unchanged, they are the only kind that works on a laptop
  with no driver, and they stay one tap away in the same popup. Worth knowing before installing the
  driver: ViGEmBus was archived by its author in November 2023 and has had no release since, though
  that last release is signed and works. Edgepad neither bundles nor installs it.
- **The gamepad editor chooses the controls, binds them, and keeps one layout per game.** The editor
  could move a control and resize it and nothing else; which controls existed and what each one pressed
  were fixed in the presets. Long-press a control for its menu -- binding, size, label, delete -- and
  add one from the options popup. A binding is offered from exactly the set that fits that kind of
  control, built from one function the picker and the store share, so the list can never offer a
  pairing the store would refuse. The last option is always a keyboard key, because keyboard mode is
  where a laptop without the driver ends up and a control with no key does nothing there.

  Layouts are a library rather than a single saved arrangement, which is what per-game needs: save,
  save as, rename, delete, switch. The built-in presets are never stored -- a saved layout of the same
  name shadows one instead -- so Edgepad's own Xbox layout can always be got back and can never be
  renamed away. A name that collides costs a number and never a layout: a second **Elden Ring** becomes
  **Elden Ring 2**, because overwriting would throw away an arrangement that cannot be got back.
  Deleting the layout being played falls back to the built-in underneath it, or to the first there is;
  the set is never empty, because the presets are always in it.
- **Shapes drawn on the trackpad run an action or a macro.** Press one finger, hold it still until it
  ticks, then draw without lifting. On release the stroke is matched against the shapes drawn under
  Settings › Shapes, and the one it matches runs: a laptop action, a macro slot, or the pad's own
  focus or lock. A stroke that matches nothing does nothing, which is the right answer -- the
  alternative is the nearest binding firing on a scrawl. A second finger clears it, so scroll, pinch
  and the three- and four-finger swipes are untouched, and a drag refuses to arm it, since a drag's
  second press is also a hold. A phone with no shapes drawn behaves exactly as it did.

  Matching is a $1-style unistroke -- resample, centre, scale, compare point by point -- with two
  deliberate departures: the scale is uniform, because $1's per-axis box fit makes a tall I and a round
  O the same blob, and there is no rotation normalisation, because a C turned around is not a C. How
  close is close enough, and by how much the best match must beat the runner-up, are two constants at
  the top of one file. Both are guesses; no real stroke has been scored against them yet.
- **A lock button on the control surface.** A fifth button joins the row at the top, on the centre:
  settings, keyboard, lock, gamepad, macros. One tap hides the dials and the media and leaves the
  trackpad; a second tap inside the double-tap window locks the trackpad instead and puts the dials and
  media back, so the phone can sit in a pocket or under a palm and answer only its rulers. Any later
  tap returns the whole surface. A locked pad refuses the first touch, not only the ones after it, and
  every mode change cancels the touch in flight, so going to a locked pad mid-drag cannot leave the
  left button held with no release to come. The mode lives in the surface and is neither stored nor
  offered as a setting: a phone that came back up silently locked would read as broken.

### Changed
- **A gamepad layout saved by 2.x is refused rather than migrated, and the first preset takes its
  place.** This is the breaking change in the release, and it costs the positions of any controls
  dragged in 2.x, once. Migrating would have been a lie: a 2.x layout records where each control sits
  and nothing about what it drives, because in 2.x every control drove a key chosen by the preset, so
  there are no controller bindings in it to carry forward. Inventing them is the only other option, and
  a pad whose stick silently became something else is worse than a pad that starts from a preset.
- **Settings are grouped by the thing they configure.** The hub grouped by the kind of editor a row
  opened, which is why the keyboard's text size and the macro buttons' appearance both sat on a page
  titled "Background & pattern" -- a title that described neither. **CONTROLS** now holds the keyboard,
  the gamepad, the macro buttons and the media layout, each on its own page, and **Appearance** is left
  holding only the background and the pattern its title names. Natural scrolling moves to the Trackpad
  page beside the scroll speed, where it is a scrolling setting rather than the hub's one lone switch
  among links.
- **Orientation holds the control surface and the media-layout editor, and nothing else.** It was a
  single flag that forced every screen upright or sideways. The media-layout editor is pinned with the
  surface on purpose: a media layout has been stored per orientation since 2.3.0, so a phone turned
  mid-edit would quietly begin changing the other one. The keyboard and the gamepad stay sideways as
  before, and every other screen follows the phone.
- **The macro grid sizes its buttons and wraps to fit.** It was three across upright and five across
  sideways whatever the names were, under a heading that said "Macros" on a screen reached by tapping a
  macro button, above a line that counted the slots. The heading and the count are gone -- most of a row
  and a half handed back to the buttons -- and the back chevron stays. Every cell is as wide as the
  widest label in the grid, so no button is a different size from its neighbour, and a row takes as
  many as the screen has room for rather than a fixed number. Fifteen slots is unchanged, and is not a
  layout decision: fifteen names at sixteen bytes with fourteen separators is 254 of the 255 bytes one
  `TEXT` frame carries.

### Removed
- **The decorative rulers down the edges of the guide and the device list.** Tick marks that drew
  nothing and said nothing. The device list's wrapper went with them, since it existed only to stack
  the ruler over the page.

### Fixed
- **A key the phone was holding stayed pressed on the laptop when the link dropped.** The laptop
  remembers what it holds so a dead connection cannot leave anything down, and lifts the lot when the
  session ends -- but the call behind every key the on-screen keyboard and the gamepad send pressed the
  code straight through and recorded nothing. A thumb on the gamepad's stick is the case that makes it
  obvious: the stick holds its key down for as long as the thumb stays forward, so the window in which
  a drop stranded a key was the whole time the user was walking.

### Documentation
- **A landing page at the root, and the documentation at `/docs`.** The site was one long documentation
  page with a hero on top, which is the right page for someone who has already decided and the wrong
  one for someone who has not. The documentation moves to `/docs` whole -- its sidebar, its four
  sections and the tables generated from the protocol fixtures -- and the root becomes a page that
  explains what Edgepad is to somebody who has never heard of it. No figure for speed appears on either
  page, because none has been measured, and no testimonial, rating, download count or logo wall
  appears, because there are none. The limits are a section rather than a footnote: Bluetooth reaches
  one room and not the internet, both ends need it, the exe is unsigned so SmartScreen warns, and the
  secure desktop cannot be reached at all. A deep link into an old anchor is forwarded on the client,
  allowlisted against the documentation's own table of contents, because a static export cannot
  redirect.
- **The protocol page describes version 4's new rows**: the controller frame, the two actions that ask
  for and hand back the pad, and the status the laptop answers with. The page builds its tables from
  `protocol/frames.txt` and `protocol/actions.txt`, so the rows appeared the moment the fixtures moved,
  marked as not yet described until the prose caught up.

## [2.3.0] - 2026-09-22

Macro icons, a keyboard text size, separate media layouts for each orientation, and a round of
fixes from reviewing the last two pull requests. The wire protocol does not move: it stays at
version 3, so a 2.3.0 half and a 2.2.0 half still speak to each other.

### Added
- **Macro buttons show the program's icon.** Add a macro in the laptop's tray menu and the phone
  draws whatever that macro opens: a program's own icon, a document's file-type icon, or a picture
  you point the new optional **Icon** field at. Nothing to configure for the common case -- the
  icon comes from the target. A slot whose icon cannot be read keeps its name, which is also what
  a URL or a folder gets, and what the whole grid falls back to against an older laptop.
  Appearance gains **ICON + LABEL** or **ICON ONLY** for how the grid draws them.

  The wire protocol does **not** move: it stays at version 3. An icon does not fit the 255 bytes a
  TEXT payload holds, so it travels as new TEXT kinds -- 6 carries one piece of one icon, 7 is the
  phone asking for them -- and an unknown kind is dropped and counted on both sides, where an
  unknown frame type would have closed the connection and cost a major release. Icons are answered
  rather than pushed, so they cross the link when the macro grid opens and never while a finger is
  on the trackpad.
- **Plex is recognised in browser titles.** A Plex tab now shows as Plex on the phone rather
  than as the browser's name. It is safe to add only because of the word-boundary fix in 2.2.0:
  a bare substring test would have read it out of "complexity".
- **Keyboard text size.** Appearance gains a slider for how large the on-screen keyboard draws its
  key labels. A label always stays inside its key: when one would not fit, every label is drawn
  smaller together, so the keyboard never mixes sizes.

### Fixed
- **Portrait and landscape shared one media layout.** Moving or resizing the media pieces in one
  orientation moved them in the other too. Each orientation now keeps its own layout. Nothing
  moves on update: landscape starts as a copy of the layout you already had, and from then on
  editing one leaves the other alone.
- **Four paths could end the tray app.** Writing the log while it was open in an editor past its
  rotation size, a WMI brightness error other than the one caught, and the tray menu's Forget and
  Start with Windows could each throw where nothing catches it.
- **A bonded but untrusted device could drop the connected phone** just by connecting: the new
  session replaced the old one before it checked trust. It is now refused first, and so is any
  device while the trust file cannot be read.
- **A trusted phone that could not be saved left the laptop open.** If writing the trust file
  failed, the phone was admitted but not remembered, so every bonded device after it was treated
  as the first. It is now remembered until the app quits. An unreadable folder is also no longer
  mistaken for a missing file.
- **Opening the tray menu rewrote the Start with Windows entry** every time, and deleted it if the
  registry read failed. It now writes only when you click it.
- **A macro saved while icons were loading could put an old icon on the wrong button.** The laptop
  now stops sending the old list's icons the moment the list changes.
- **An icon claiming an enormous size could crash the phone** while decoding. Anything larger
  than 256px is ignored.
- **Starting Edgepad normally while a copy ran as administrator crashed it.** It now says an
  administrator copy is running and how to quit it.

### Security
- **Every GitHub Action is pinned to a commit SHA,** including the four in the release job that
  decrypts the signing key, and checkouts no longer keep their credentials.

## [2.2.0] - 2026-09-19

Three fixes and the first outside contribution. Nothing about the wire protocol moves:
it stays at version 3, so a 2.2.0 half and a 2.1.0 half still speak to each other.
Update both anyway -- the browser-title fix is on the laptop and what it corrects shows
up on the phone.

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

### Changed
- **The trackpad stopped allocating on the touch path.** `ControlSurface` built a fresh pair of
  `FloatArray`s for every touch sample handed to the gesture recogniser, and for every
  historical sample inside each event. The surface asks for unbuffered dispatch, so samples
  arrive as fast as the digitiser makes them: one finger dragging produced a steady stream of
  short-lived garbage on the one path in the app written to be fast — the same defect fixed in
  the gamepad in 1.0.0, which Android lint cannot see because `DrawAllocation` only inspects a
  method literally named `onDraw`. It now fills one pair of buffers sized once at
  `MAX_POINTERS` and passes an explicit finger count. No gesture, threshold or feel constant
  moves, and the recogniser's tests are unchanged — `count` defaults to the array's own size,
  so every existing caller reads as it did.

### Tests
- **The shared protocol fixture now carries non-ASCII titles.** Every `TEXT` line in
  `protocol/frames.txt` was pure ASCII, so nothing proved the two codecs agreed on a
  single multi-byte character -- and `TEXT` is the frame that carries track titles and
  artist names, which routinely are not. Three lines added, two, four and three bytes of
  UTF-8, each with a UTF-8 byte count that differs from its UTF-16 length: if either side
  ever counted UTF-16 units, the length header would not match and both suites would fail
  on the same line. Contributed by [@wized2](https://github.com/wized2) in
  [#7](https://github.com/akshit-bansal11/edgepad/pull/7) -- the first outside change to
  Edgepad. Their three lines landed under `TEXT` kinds 4, 5 and 6, which are already
  `REFRESH_RATES` and `MACROS` and, for 6, nothing at all; the codecs do not validate the
  kind byte, so both suites passed and the documentation site would have published the
  contradiction. Corrected to kind 0, `NOW_PLAYING`, keeping the characters exactly.

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

[Unreleased]: https://github.com/akshit-bansal11/edgepad/compare/v3.0.0...HEAD
[3.0.0]: https://github.com/akshit-bansal11/edgepad/compare/v2.3.0...v3.0.0
[2.3.0]: https://github.com/akshit-bansal11/edgepad/compare/v2.2.0...v2.3.0
[2.2.0]: https://github.com/akshit-bansal11/edgepad/compare/v2.1.0...v2.2.0
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
