# Edgepad 5 film: sound

Three layers, mixed in the `video` job: the voice (track 0), the music bed (track 1) and the sound effects (track 2).
None of it is made on the laptop.

## Voice

- edge-tts, voice `en-US-AndrewMultilingualNeural`, `RATE = "+4%"`, one take per script (voice.py as it is).
- "Wi-Fi" is spoken as "Why-Fye" through voice.py's SPOKEN map; the script and the captions keep "Wi-Fi".
- Pauses are widened only in silence (voice.py's cut-and-lay-out). Per-line `hold` seconds are added by the new key
  (PORT.md V1).
- Target -16 LUFS integrated for the voice stem.

## Music

- music.py's original bed per script (no licence or credit applies), with the chapter energies from scripts/demo5.json
  and scripts/reel5.json.
- Mixed about 15 LU under the voice (the 4.0 standard; the job's ebur128 reading checks it). Keep the composition's
  `data-volume` near 0.2 and adjust only if the loudness reading is off.
- Under demo5 line 9's 2 s race hold, the bed ducks a further 4 LU so the engine leads.

## Sound effects (earshot)

Tool: https://github.com/Leonxlnx/earshot (MIT). Base install only. Do not install the listening extra, which downloads
a model.

In the job: `pip install "earshot @ git+https://github.com/Leonxlnx/earshot"` into the existing venv (PyPI's `earshot`
is a different project). Pin it to the commit current on the day of the build, and write the commit SHA in this file.

Pinned commit: `7a7f7c5c70d4a763a1eca4b3666af7c77e313f97` (also `PIN` in sfx.py).

A new script, `assets/video/sfx.py`, runs each request below with `--seed 0` and writes `out/sfx/<id>.wav`. It then
lays every cue at its time from timing.json (cue times are keyed to words, like beats.js: `@word`) into
`out/<script>/sfx.mp3`. Each composition carries it as `<audio id="sfx">` on track 2.

Read earshot's first output line for every cue. If it says `makes no …` or `not followed`, try the first `also`
alternative with `--sound ID`. If none fits, build the sound with `earshot build` from recorded parts, and write what
was used here.

| id | request (earshot make "…") | options | used on |
|---|---|---|---|
| room-tone | a quiet room at night, no loop | --dur 12 --lufs -34 | demo5 1–2, 19 |
| whoosh-soft | a soft short whoosh | --lufs -24 | camera arrivals |
| logo-pop | a logo bloom | --sound logo/bloom --lufs -20 | the ident (demo5 3, 23; reel5 2, 13) |
| title-thump | a heavy bag dropped under a title | --sound title/bag --lufs -22 | headline landings on chapter starts only |
| tick-key | the top of a mechanical key for a tick | --sound tick/key --lufs -26 | ruler lights, flashes, highlights |
| tap | a soft UI tap | --lufs -24 | every phone tap |
| ruler-tick | a tiny dry click | --lufs -30 --events 1 | dial steps, max 12 per second |
| switch-on | a toggle switching on | --sound toggle/key_down --lufs -24 | switches |
| pop-open | a drawer opening softly | --lufs -24 | popups and sheets |
| key-type | typing on a mechanical keyboard, single keystroke | --n 6 --lufs -28 | keyboard presses, alternating takes |
| morph-slide | a page sliding | --sound slide/paper --lufs -26 | keyboard resizes, gamepad reflows |
| engine | a car engine revving, steady, no loop | --dur 12 | the race. Render 3 takes at low, mid and high pitch; sfx.py cross-fades by speed(t) and gains by RT |
| tyre-squeal | tyres squealing on asphalt | --lufs -22 | hairpin, when abs(LX) > 0.7 |
| boost | a fast rising whoosh | --sound whoosh/fast --lufs -20 | A press |
| whoosh-pass | a car passing by, short | --lufs -28 | lamp posts in boost |
| engine-far | a distant car engine, no loop | --dur 6 --lufs -36 | attract mode |
| pick-up | a small object picked up | --lufs -26 | drags start |
| drop | a soft thump when a card settles | --sound thump/door --lufs -26 | drags end |
| next-page | turning a page | --sound next/page --lufs -24 | Presenter Next |
| laser-hum | a very soft electric hum, no loop | --dur 3 --lufs -36 | while the laser moves |
| shutter | a camera shutter | --sound camera/shutter --lufs -22 | capture region and full |
| rec-blip | a short soft beep | --lufs -26 | record start |
| whoosh-arc | a light whoosh through the air | --sound whoosh/sweep --lufs -24 | M-FLY |
| land | a soft thump when a card settles, small | --lufs -28 | M-FLY end |
| copy-tick | a keyboard shortcut click | --lufs -28 | copy |
| whoosh-stream | a soft airy rush, no loop | --dur 4 --lufs -32 | file transfer running |
| pause-click | a light switch click | --sound switch/lamp --lufs -26 | Pause and Resume |
| success | a soft success sound | --sound success/shutter --lufs -22 | transfer done, macro runs |
| page-slide | a page sliding | --sound slide/paper --lufs -28 | deck page change |
| draw-stroke | a marker drawn on a whiteboard, short | --lufs -30 | shape drawn |
| ring-fill | a soft rising tick | --lufs -30 | AI rings filling |
| swipe | a fingertip swipe on glass | --lufs -28 | pager swipe |
| air-hum | a gentle breath of air, no loop | --sound rise/breath --lufs -34 | air mouse tilt |
| soft-chime | a soft notification | --lufs -26 | the sleep countdown |
| line-draw | a soft rising shimmer | --lufs -30 | straight-talk line |
| riser | a breath of air rising | --sound rise/breath --lufs -24 | into Edgepad 6 |
| shine | a sword drawn for a glint | --sound shine/blade --lufs -26 | Edgepad 6 glint |

Mixing rules:
- No effect peaks above -6 dBFS on the stem.
- Effects never land within 80 ms of a stressed word's start, so the voice stays clear; shift them later.
- Duck all effects by 6 dB while the voice speaks, except taps and keys.
- The finished mix must not clip: check the job's loudness reading for the full mix, and keep the true peak at or
  below -1 dBTP.

## As built (sfx.py)

- Cues live in `scripts/<id>.cues.json`; the format is documented at the top of `sfx.py`. The storyboard's `glide` is the
  `swipe` cue. `engine-far` is laid at +6 dB over its table level, under `room-tone`.
- `--n 6` takes are alternated in order. The engine is one 12 s render resampled by 0.8 / 1.0 / 1.25 (earshot's `--pitch`
  is not used). earshot's wavs are decoded and resampled to 44.1 kHz stereo by ffmpeg, not numpy.
- Race bed: a Python mirror of GAME.md's input table (ROWS in `sfx.py` mirrors `ui/game.js` EP.game.ROWS). Rows ease over
  200 ms starting at each row's time. Engine trimmed 8 dB under the other effects. Boost at 4.4 s, four lamp whooshes
  evenly over 4.5 to 5.5 s. After the hold the bed falls 1.5 s to -18 dB and runs to the end of line 10 (demo5), or stops
  with the hold (reel5).
- A cue keyed to a word is moved 80 ms after that word's start (every word counts as stressed), except taps and keys.
- `room-tone` on demo5 line 19 is laid at -6 dB under the line ("drops" read as lowered, not stopped).
