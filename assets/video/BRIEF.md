---
workflow: general-video
flow: automation
storyboard: yes (STORYBOARD.md)
message: "Your phone becomes the trackpad, keyboard, gamepad and remote for your laptop. Your laptop, from anywhere in the room."
destination: website, README, YouTube (demo5); Instagram and LinkedIn (reel5)
cuts: demo5 1920x1080 (~2.5 min), reel5 at 1080x1920 (instagram5) and 1080x1440 (linkedin5), ~95 s
language: en
angle: premium product film, keynote style, dark
voice: en-US-AndrewMultilingualNeural (edge-tts), one take per script
---

# Edgepad 5 film: brief

## Intent

Edgepad 5.1's launch film and its social cut. The 4.0 films (scripts/full.json, scripts/short.json, the compositions
index.html and cuts/linkedin.html, cuts/instagram.html) were a careful walkthrough. This one is a product film: fewer
words on screen, bigger moves, more of the laptop's own screen, and every 5.x feature worth a second shown working.
It keeps the 4.0 machinery (voice.py, music.py, ui/film.js as the director, ui/beats.js as the per-line acts, the
ported canvas renderers) and the 4.0 dark look, and adds three things the 4.0 film never had:

1. A laptop with a live screen, drawn in the room, so the phone's input lands somewhere visible: an original racing
   game (GAME.md), slides with the laser dot, a file arriving, a pull request opening, a shape firing a macro.
2. The animated Edgepad mark from assets/brand/edgepad/motion/ as the ident: the dial animation opens the meet line and
   closes the film. Its ruler hands off into the phone's real corner dial.
3. Sound design: earshot effects on every tap, slide, landing and whoosh, plus the racing game's engine (SOUND.md).

## Audience

People with a laptop on a TV, a projector, or a desk they would rather not sit at; gamers who play on a laptop;
people who give talks; developers. They have not heard of Edgepad. They should leave knowing it is free, open source,
for Windows, Mac and Linux, and on GitHub.

## Files

- scripts/demo5.json, scripts/reel5.json: the narration, used as written. "Wi-Fi" is written normally; voice.py says
  it as "Why-Fye" (SPOKEN map) so captions keep the written form.
- STORYBOARD.md: one frame per line, both cuts. DESIGN.md: tokens and layout. MOTION.md: every move. GAME.md: the
  racing game. SOUND.md: the cue sheet. FACTS.md: the proof for every claim. PORT.md: what to draw and from where.

## Rules

- The phone's UI is the app's drawing code ported to canvas. Never screenshots, never invented UI.
- Nothing renders locally. Every voice, music, sound and render step runs in the `video` job of .github/workflows/ci.yml.
- No latency, speed or performance figure anywhere, and no ping on any Stats screen. The game's speedometer has no digits.
- No real game, studio, car brand, track or HUD. No real person's name, song, playlist, macro or file.
- Edgepad 6 is named for screen mirroring and remote access with no date and no further description.
