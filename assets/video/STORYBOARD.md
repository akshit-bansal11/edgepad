---
format: demo5 1920x1080; reel5 1080x1920 (instagram5) and 1080x1440 (linkedin5)
duration: demo5 ~150 s; reel5 ~90-95 s (with the holds below)
message: "Your laptop, from anywhere in the room."
arc: Hook → Meet (ident) → Surface → Type and play (race) → Present → Move things → Developers → Small things → Straight talk → Next (6) → Get it
mode: autonomous, timed by voice.py's word timings
---

# Storyboard

How to read a frame:

- **h** is the headline, written in the beats.js form: one keyword between `*stars*` (drawn in #4DA3FF), `|` breaks
  a line. `h: keep` leaves the last headline up.
- **s** lists the sub-lines, each `text@word`. A sub-line appears when the voice says that word.
- **cam** names a camera preset (MOTION.md). `→` is a move, timed to a word: `hero → tl @volume`.
- **phone** is what the phone's screen shows (PORT.md names the renderer). **laptop** is what the laptop's screen shows.
- **sfx** names cues from SOUND.md. **hold** is extra silence after the line: voice.py needs a per-line `hold` key added
  (PORT.md, task V1).
- **→ next** says how this frame hands off. Nothing cuts. Every change is a move, a morph or a cross-dissolve of at
  most 400 ms over moving picture.

The room is the 4.0 hook room (film.js 423–493), extended: a dark room, a TV stand, and a laptop on it (new, DESIGN.md).
The laptop's screen is a canvas region drawn by `EP.laptopScreen(ctx, t, kind)`. In 16:9 the laptop sits back left
and the phone stands right. In the tall cuts the laptop sits above and the phone below.

## demo5 (16:9, 24 lines)

### Chapter: Hook

**1.** "Your laptop's on the TV. Or the projector. Or across the room, running the show."
- h: none for 2.4 s, then `Across the *room.*`
- cam: `roomWide`, starting at frame 0 with motion already under way: a slow dolly in. At 0.0 s the laptop screen is
  already lit with the racing game in attract mode (GAME.md, "attract"), so the first frame is a moving picture.
  - On "TV" the screen is mirrored onto a large TV shape behind the stand.
  - On "projector" a projector beam fades in across the wall.
  - On "across the room" the camera settles on the stand at the far end.
- phone: off screen.
- laptop: the race in attract mode, the car looping one corner.
- sfx: `room-tone` from 0, `engine-far` at -24 LUFS under it.
- → next: the camera keeps dollying. Nothing resets.

**2.** "You're over here. That's fine."
- h: `You're *over here.*`
- cam: `roomWide → hero @here`. A 1.4 s pull back and turn, so the phone rises into the foreground, held in a hand
  shape (a soft silhouette, no skin detail, no brand). The laptop stays visible small in the back (16:9: back left).
- phone: the control surface (surface.js drawSurface), the 5.x top row of Controls, Type, Lock and Settings, 4 dials,
  the media block idle.
- sfx: `whoosh-soft @here`.
- hold: 0.4 s.
- → next: the phone stays. The ident grows out of it.

**3.** "This is Edgepad five. A free, open-source Android app that turns your phone into the trackpad, keyboard, gamepad and remote for your laptop. Windows, Mac or Linux."
- h: `This is | *Edgepad 5.*` on "Edgepad"
- s: `Free and *open source*@free`, `Trackpad · keyboard · *gamepad* · remote@trackpad`, `Windows · Mac · *Linux*@windows`
- ident: on "This" the long dial animation (MOTION.md, M-IDENT) plays at 260 px on the left above the headline, in the
  white-tile dark colours. It pops (0–760 ms), then its ruler scrolls (900–2650 ms), and it ends on the mark.
  - On "trackpad" the phone's screen shows a finger glide.
  - On "keyboard" it shows a flash of the keyboard (100 ms morph each way).
  - On "gamepad" it shows a flash of the gamepad.
  - On "remote" it is back on the surface.
  - These three flashes are the only fast cuts in the film, and each is a 120 ms morph, not a hard cut.
- cam: `hero`, with a slow 2% push.
- laptop: three OS chips rise under "Windows · Mac · Linux". Each is a dark card with the OS name in text. No OS logos.
- sfx: `logo-pop @This` (earshot `logo/bloom`), `tick-key` ×3 on the flashes, `title-thump @windows`.
- → next: the mark shrinks into the top-left corner dial of the phone (M-HANDOFF), becoming the volume ruler.

### Chapter: The surface

**4.** "The middle is a multi-touch trackpad. The corners are dials."
- h: `The middle is a *trackpad.*` then `The corners are *dials.*@corners`
- cam: `hero → tl @corners`
- phone: one-finger glide with its tail, a two-finger scroll, then on "corners" all four rulers light up in turn
  (clockwise, 90 ms apart).
- laptop: the pointer moves on the laptop's desktop (a neutral dark desktop with three plain windows).
- sfx: `glide` (a fingertip slide), `tick-key` ×4 on the ruler lights.
- → next: the camera is already at the top-left corner.

**5.** "Slide for volume. Brightness. Zoom. Scrub through a video. Split the corners, and four dials become eight."
- h: `Slide to *set.*`
- s: `*Volume*@volume`, `*Brightness*@brightness`, `*Zoom*@zoom`, `*Scrub*@scrub`, `Four become *eight*@split`
- cam: `tl @volume → tr @brightness → br @zoom → bl @scrub → hero @split`. Each move is 520 ms, eased, landing on the word.
- phone: each dial is dragged along its edge with an Android "show taps" dot (the app draws no finger on a dial). The
  ruler numbers roll.
  - On "split", Settings › Corners slides up as a sheet (CornersScreen port), Split corners switches on, and the sheet
    slides away. The surface now shows eight halves.
- laptop: the level overlay card (beats.js C.level) for Volume, then Brightness. On zoom the laptop desktop zooms 1.0
  to 1.6. On scrub a video player on the laptop scrubs (a neutral landscape clip drawn as gradients, no real footage).
- sfx: `ruler-tick` at each 5-unit step (rate-limited to 12/s), `switch-on @split`.
- → next: on the volume dial again.

**6.** "Change the volume on the laptop, and the dial moves with it."
- h: `Volume *syncs* both ways.`
- cam: `split169`: the laptop's screen and the phone's top-left corner side by side, each at 1.3x.
- laptop: the Windows volume flyout (beats.js C.winVolume) is dragged by a pointer from 30 to 70.
- phone: the volume ruler follows, with a 120 ms ease.
- sfx: `ruler-tick` rolling, soft.
- → next: the camera rises to the top row.

### Chapter: Type and play

**7.** "Tap Controls, and the phone becomes whatever you need next."
- h: `Tap *Controls.*`
- cam: `top`
- phone: a tap dot on Controls. The Controls popup (5.x: the "Controls" heading with its pencil, then the tiles
  Keyboard, Gamepad, Macros, Presenter, Capture, Annotate, Clipboard, Stats, Send a file and Developer, then the Edit chip)
  opens from the button within 300 ms (UX "reveal" tier).
- sfx: `tap`, `pop-open`.
- → next: the Keyboard tile is pressed (`tap`) and the phone turns sideways.

**8.** "A full keyboard, in six sizes, from a full board with a number pad down to a compact sixty percent."
- h: `A full *keyboard.*` (lay: top)
- s: `*Six* sizes@six`, `Full, with a *number pad*@number`, `Down to *60%*@compact`
- cam: `hero → land` (the 4.0 turn, 600 ms).
- phone: the keyboard at 75%. Ctrl is latched and Shift held for a beat. On "full board" Keyboard settings › Size
  shows: the board morphs to 100% with the numpad. On "compact sixty percent" it morphs down through 80 and 65 to 60%.
  Each size change is a 280 ms key-reflow morph (keys slide to their new places).
- laptop: text types into a neutral note window: "Meeting notes" then "- dials on every corner". Neutral, no names.
- sfx: `key-type` ×N on each keypress, `morph-slide` on each resize.
- → next: back upright through the Controls popup to the Gamepad tile. Or, simpler: stay sideways and swap the
  keyboard for the gamepad with a 400 ms cross-slide.

**9.** "A gamepad with two sticks and analog triggers. On Windows and Linux, your laptop sees a real Xbox controller, so your games just work."
- h: `A real *gamepad.*` (lay: top)
- s: `Two sticks · *analog* triggers@analog`, `Seen as an *Xbox controller*@xbox`, `Windows and *Linux*@windows`
- cam: `land → raceSplit @two`: the phone sideways in the foreground, the laptop screen big behind it, about 55% of the frame.
- phone: the gamepad, Xbox preset (GamepadScreen port). The inputs follow GAME.md's input track exactly: left stick
  steers, RT (right trigger) pulls in analog, LT brakes, A boosts.
- laptop: the race, live (GAME.md, "race" segment). It opens on the green light.
- sfx: `engine` (follows throttle), `tyre-squeal` in the hairpin, `boost` on A, `whoosh-pass` as a lamp post passes.
- hold: 2.0 s with the race and its sound only. The headline dims to 40%. This is the film's showpiece; let it breathe.
- → next: the race keeps running in the back.

**10.** "Drag every control where your thumbs want it, or start from a racing, platformer or shooter layout."
- h: `Your *layout.*`
- s: `*Drag* anything@drag`, `Racing · Platformer · *Shooter*@racing`
- cam: `land`, pushed in on the phone, with the race still running small on the laptop behind.
- phone: the gamepad layout editor (GamepadLayoutScreen port, new). RT is dragged to a new spot on a snapping grid, and
  the centre lines light up. On "racing" the layout options list shows Xbox, Xbox keys, Platformer, Racing and Shooter.
  Racing, Platformer and Shooter highlight in turn and the controls rearrange for each (280 ms reflow).
- sfx: `pick-up`, `drop`, `tick-key` per highlight.
- → next: the phone turns back upright (600 ms) as the race fades into a slide deck on the laptop.

### Chapter: Present and capture

**11.** "Giving a talk? Presenter steps through your slides, and a laser dot follows your thumb."
- h: `Present from *anywhere.*`
- s: `*Next* and previous@presenter`, `A *laser* that follows you@laser`
- cam: `land → raceSplit` mirrored: the phone upright on the right, the laptop screen big on the left.
- phone: the Presenter screen (PresenterScreen port, new), upright: the laser pad, with Previous and Next as a row below.
  Next is tapped on "steps".
- laptop: a neutral slide deck. Slide 1 is "Q3 roadmap" with three plain bars; it changes to slide 2 with a 300 ms
  slide transition on "steps". A thumb on the pad then draws the red laser dot with its tapered 250 ms trail across the
  slide's bars (Show trail on, size Medium).
- sfx: `next-page` on Next, `laser-hum` (very quiet, while the dot moves).
- → next: the dot fades. The capture region starts from the same corner.

**12.** "Capture a region, the full screen or a recording, and send it straight to your phone."
- h: `*Capture* it.`
- s: `A *region*@region`, `The *full screen*@full`, `A *recording*@recording`, `Straight to your *phone*@phone`
- phone: Capture (CaptureScreen port, updated to 5.x): Region, Full screen and Record, with the "To phone" chip. On
  "region": tap Region, then a plain drag on the pad to the far corner. A ring fills while the finger holds still.
- laptop: a region rectangle grows over the slide, matching the drag. On "full screen" a white flash (80 ms, 30%). On
  "recording" a red dot pulses at the top right. On "phone" a thumbnail flies from the laptop's screen into the phone
  (a 400 ms arc, MOTION.md M-FLY).
- sfx: `shutter @region`, `shutter @full`, `rec-blip @recording`, `whoosh-arc @phone`, `land @arrive`.
- → next: the thumbnail settles into the phone. The camera widens.

### Chapter: Move things

**13.** "Copy on one, paste on the other. Clipboard sync works both ways, and keeps a history."
- h: `Copy here. *Paste* there.`
- s: `Both *ways*@both`, `With a *history*@history`
- cam: `wide`: phone and laptop both in frame.
- laptop: a line of text is selected and copied in the note window. A small chip lifts off the laptop and arcs to the
  phone (M-FLY).
- phone: the text appears, and goes back the other way. On "history" the Clipboard sheet (ClipboardSheet port, new)
  shows four rows marked "From laptop", "From this phone" and "Just now".
- sfx: `copy-tick`, `whoosh-arc`, `land`.
- → next: the sheet closes into the Send a file tile.

**14.** "Send files either way, up to two gigabytes each. Pick a few, watch them go, pause any time."
- h: `Send *files.*`
- s: `Either *way*@either`, `Up to *2 GB* each@two`, `*Pause* any time@pause`
- phone: Controls › Send a file. Three neutral files are picked (`trip-photos.zip`, `slides.pdf`, `demo.mp4`). The
  transfer card (XferCard port, new) shows "Sending slides.pdf (2 of 3) · 41%" with a bar. On "pause", Pause is pressed
  and the bar holds. Resume is pressed and the bar finishes. The card ends on "Sent 3 files".
- laptop: the Windows transfer toast near the tray (TransferToast port, new) mirrors it.
- The speed field shows no figure. Blank it or show only the percent.
- sfx: `tap`, `whoosh-stream` (a soft loop while sending), `pause-click`, `success`.
- → next: the card slides down. The macro deck rises.

**15.** "Macros open apps, folders and websites. Draw a shape on the trackpad, and one runs."
- h: `*Macros.*`
- s: `Apps · folders · *websites*@macros`, `Draw a *shape*@draw`, `And it *runs*@runs`
- phone: the macro deck (screens.js S.macros) with neutral macros only (Simple Icons set: Blender, Figma, OBS, VLC,
  Steam, Firefox, Notion, Obsidian, Terminal). It slides to page two. Back on the surface, a lightning-bolt shape is
  drawn on the pad (two fingers hold until the line turns solid, then the stroke).
- laptop: on "runs" a terminal-style window opens (neutral, the Gnome Terminal icon from icons/).
- sfx: `page-slide`, `draw-stroke`, `success @runs`.
- → next: the camera tilts down as the screen turns to code.

### Chapter: For developers

**16.** "Writing code? Turn on developer mode. Your Claude Code and Codex limits, your spend and every open session, right on your phone."
- h: `For *developers.*`
- s: `*Limits* and spend@limits`, `Every open *session*@session`
- phone: Settings › Developer: the "Developer mode" switch turns on. Then the AI screen (AiScreen port, new), upright
  and stacked, with Claude Code and Codex cards. The usage rings (5 h, week) fill to neutral values (34% and 58%: green,
  then yellow per the ring colours). Spend shows today, 7 days and 30 days in tokens and estimated dollars. Use
  plausible round placeholders and label them in the storyboard sheet as sample data. Sessions show "Context 42% full",
  Working, then "Waiting for you".
- laptop: a code editor in dark colours with neutral code, and a terminal pane with a generic prompt.
- sfx: `switch-on`, `ring-fill` (a soft rising tick).
- → next: a swipe left (the DeveloperHub pager) to Git.

**17.** "Pull requests and issues from GitHub, GitLab and more. Tap one, and it opens on the laptop."
- h: `Your *pull requests.*`
- s: `GitHub · GitLab · *and more*@more`, `Tap, and it *opens*@opens`
- phone: the Git screen (GitScreen port, new) with the Mine, Review and Assigned switch, host chips, and neutral repos
  `acme/website`, `acme/api`. One PR reads "Fix the login redirect" with a green check.
- laptop: on "opens" a browser window opens on a neutral PR page: a dark generic layout, no GitHub chrome or logo.
- sfx: `swipe`, `tap`, `pop-open`.
- → next: the camera drifts out to the room.

### Chapter: Small things

**18.** "Air mouse: tilt to aim, hold a button to drag."
- h: `*Air* mouse.`
- s: `*Tilt* to aim@tilt`, `*Hold* to drag@hold`
- cam: `hero`, with the phone tilting ±10° in 3D (a perspective skew, MOTION.md M-TILT).
- phone: the Air mouse surface: only the pad, the "Air mouse · Off" pill at the top, and Left and Right along the bottom.
- laptop: the pointer follows the tilt. On "drag" Left is held, a file icon drags to a folder, and lifts.
- sfx: `air-hum` (very quiet), `pick-up`, `drop`.
- → next: the room's lights dim.

**19.** "Falling asleep to a film? Edgepad puts the laptop to sleep when it ends."
- h: `Sleep when it *ends.*`
- cam: `roomWide`. The room lamp dims 40%.
- laptop: a film ends (credits roll as plain text lines). The 60-second countdown card (ShutdownCountdown port) shows
  "Sleep the laptop" and "Cancel", and the screen fades to black.
- phone: the "Sleep when this ends" chip, on.
- sfx: `room-tone` drops, `soft-chime` on the countdown.
- → next: black, then the straight-talk cards fade up.

### Chapter: Straight talk

**20.** "No account. No server. Your phone talks straight to your laptop, over Bluetooth, Wi-Fi or USB."
- h: `No account. | No *server.*`
- s: `*Straight* to your laptop@straight`, `Bluetooth · Wi-Fi · *USB*@bluetooth`
- cam: `talk` (the 4.0 plain-cards layout, beats.js C.talk). The phone steps aside.
- Picture: a single line draws from phone to laptop with a travelling pulse, no cloud icon.
- sfx: `line-draw`.

**21.** "Windows is tested on real hardware. Mac and Linux are newer, so tell us on GitHub when something's off."
- h: keep
- s: `Windows: tested on *real hardware*@tested`, `Mac and Linux: *newer*@newer`, `Tell us on *GitHub*@github`
- sfx: none, just the music bed.
- → next: the cards slide off left. The phone returns.

### Chapter: Next

**22.** "Coming in Edgepad six: screen mirroring, and remote access."
- h: `Coming in *Edgepad 6.*`
- s: `Screen *mirroring*@mirroring`, `Remote *access*@remote`
- Picture: phone and laptop face each other. A faint, blurred, unresolved shape glints on both screens: a promise, not
  a feature shown. No mock UI of version 6.
- sfx: `riser` into `shine`.
- hold: 1.0 s.

### Chapter: Get it

**23.** "Edgepad. Free and open source, on GitHub."
- End card (4.0's end card, film.js): the long dial ident plays again at 360 px, then "Edgepad", "Free & open source."
  and the GitHub button.
- sfx: `logo-pop`, `title-thump`.

**24.** "Your laptop, from anywhere in the room."
- The close line lands last. The mark's ruler keeps scrolling slowly (the dial ident's scroll phase looped).
- sfx: music resolves. voice.py's TAIL is 3 s.

## reel5 (9:16 and 3:4, 14 lines)

Same picture language, stacked: the laptop in the top half, the phone in the bottom half, with the headline in the band
between them (DESIGN.md, tall layout). Each line reuses a demo5 act, shortened.

1. "Your laptop's across the room. Your phone is in your hand."
   - Demo 1 + 2 in one: the race in attract mode on the laptop at 0.0 s, then the phone rises on "hand".
   - h: `Across the *room.*`
2. "This is Edgepad five. Free, open source, and it turns your phone into the trackpad, keyboard, gamepad and remote for your laptop."
   - Demo 3: the ident and the three flashes.
   - h: `This is *Edgepad 5.*`
3. "A multi-touch trackpad, with dials on every corner. Volume, brightness, zoom, scrubbing."
   - Demo 4 + 5 compressed: four corner visits of 380 ms each.
   - h: `Dials on every *corner.*`
4. "Change the volume on the laptop, and the dial follows."
   - Demo 6.
   - h: `*Syncs* both ways.`
5. "A keyboard in six sizes. A gamepad your games see as a real Xbox controller, on Windows and Linux."
   - Demo 8, then 9: the keyboard morphing 75 → 100 → 60, then the gamepad and the race.
   - h: `Keyboard. *Gamepad.*`
   - hold 3.0 s on the race.
6. "A presenter with a laser. Screenshots and recordings, sent to your phone."
   - Demo 11 + 12.
   - h: `*Present.* Capture.`
7. "Clipboard sync and file sending, both ways. Macros that run from a shape you draw."
   - Demo 13, 14 and 15, with each act at half length.
   - h: `Clipboard. Files. *Macros.*`
8. "Air mouse: tilt to aim, hold a button to drag."
   - Demo 18.
   - h: `*Air* mouse.`
9. "And for developers: your AI limits, sessions and pull requests, at a glance."
   - Demo 16 + 17.
   - h: `For *developers.*`
10. "Windows, Mac and Linux. Bluetooth, Wi-Fi or USB. No account, no server."
    - Demo 20.
    - h: `No account. No *server.*`
11. "Mac and Linux are newer, so tell us on GitHub when something's off."
    - Demo 21.
    - h: keep.
12. "Screen mirroring and remote access are coming in Edgepad six."
    - Demo 22.
    - hold 1.5 s.
13. "Edgepad. Free and open source, on GitHub."
    - Demo 23, the end card.
14. "Your laptop, from anywhere in the room."
    - Demo 24.

Holds in reel5: lines 1 +1.5 s, 5 +3.0 s, 9 +1.0 s, 12 +1.5 s. With gap 0.9 and voice.py's chapter gap, the cut lands
near 90 s. If the measured total (timing.json duration) is under 88 s, raise line 5's hold to 5 s. If it is over 98 s,
lower the gap to 0.7.

## Poster

voice.py picks the first line containing "dial". In demo5 that is line 4. Override it to the middle of line 9, the
race with the gamepad in the foreground: add `"poster_line": 9` to the script and read it in voice.py (PORT.md V1).
