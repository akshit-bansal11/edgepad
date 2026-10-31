---
workflow: general-video
flow: automation
storyboard: no
message: "Your phone is your laptop's remote: a trackpad in the middle, a dial on every corner, everything else one tap away."
destination: youtube, site, reels
aspect: 1920x1080 (full, minute), 1080x1920 (reel)
language: en
length: full ~4.5 min, minute ~60 s, reel ~30 s
angle: product demo and tutorial
voice: en-US-AndrewMultilingualNeural
---

## Intent

Edgepad 4.0.0's demo, its one-minute cut and its reel, rebuilt to studio quality to match a reference video the owner
loves: an off-white ground with a soft white glow, a huge Lato Black headline revealed word by word with one blue
keyword, a Lato Bold sub-line, a white caption pill that follows the voice word by word, and a large phone the camera
pushes into, turns sideways for the keyboard and gamepad, and holds still while you read.

## Assets

- scripts/full.json, scripts/minute.json, scripts/reel.json — the narration, used as written.
- brand/edgepad-light.svg, brand/edgepad-dark.svg — the 4.0 mark (copies of assets/edgepad-*.svg), used as-is.
- brand/logo_youtube.svg, brand/logo_spotify.svg — the app's own now-playing logos (android res/raw).
- icons/*.svg — Simple Icons 16.35.0 (CC0) for the macro deck.

## Customizations

- The phone's UI is the app's drawing code ported to canvas (ui/kit.js, surface.js, screens.js), never screenshots.
- 4.0 features in the reference's style: the new mark, Split corners (4 dials to 8), the Controls tiles, Annotate and
  Sign, Capture, the macro deck (no fill, hold to drag, grid picker, short last row centred), PIN entry and the
  laptop's trust question, the gamepad as shipped (Windows and Linux), sideways, dark and light, a custom background.
- Laptop-side cards in the laptop's own look: the level overlay, the Windows volume flyout, the trust question, the
  security levels, the tray menu, the screen under Annotate and Snipping Tool.
- Covers: thumb-landscape (1280 x 720) and thumb-portrait (1080 x 1920).

## Notes

- Nothing renders locally: every HyperFrames command runs in CI (the laptop crashes under load).
- No music: media-use resolves a music bed only through a HeyGen account this project does not have.
- No latency figure may appear anywhere.

## 2026-10-31 — dark look, hook, music, social cuts

- Cuts: `full` (1920x1080) and two cuts of the `short` script, `linkedin` (1080x1440) and `instagram` (1080x1920). The one-minute and reel cuts are gone; the `minute` and `reel` scripts went with them.
- Look: a dark ground (`#1E2026`) with ink `#F2F3F5`, grey `#9A9CA3` and blue `#4DA3FF`; the caption pill is `#2B2D33`; the mark is the white tile. The white glow is replaced by a slow blue light behind the phone that swells at each chapter start, and the phone has a thin rim light. Contrast on the ground: ink 14.7:1, grey 5.9:1, blue 6.2:1; on the pill ink 12.4:1, blue 5.2:1, grey 5.0:1.
- Hook: the first line of each script opens on a dark room, a laptop glowing on a TV stand far across it, and one continuous move in to the phone rising into frame. No cut.
- Beats: one per script line, written from the new scripts. A feature is on screen when the voice names it.
- End card: the mark (still moving), "Edgepad", "Free & open source.", the GitHub button, then "Your laptop, from anywhere in the room." landing last.
- Music: an original bed per script (`music.py`), mixed under the voice at `data-volume="0.16"`.
- Still true: no latency figure anywhere; nothing renders locally.
