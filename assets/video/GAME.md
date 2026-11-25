# The racing game: "Night Lap"

An original pseudo-3D racing scene drawn on the laptop's screen for the film. It is not a recreation of any real game.
It shows no studio, car brand, licence plate, real track or real HUD layout. It is a canvas painter,
`EP.laptopScreen(ctx, t, 'race')`, a pure function of time like the rest of the film.

## Art direction

- **Setting:** a coastal night circuit. The road is a dark asphalt ribbon (#26282E) with white kerbs alternating
  #F2F3F5 and #3A3D45, a dashed centre line in #9A9CA3, and the sea as a flat band (#101826) with a soft moonlit
  horizon line in #4DA3FF at 20%.
- **Lights:** street lamps every 40 road segments. Each is a thin post with a warm head (#FFD9A0) and a cone of light on
  the road at 12%. Passing a lamp flares the car's paint for 80 ms.
- **Car:** an original wedge coupé seen from behind and slightly above. The body is low and wide, in matte charcoal
  (#3A3D45). A blue light bar (#4DA3FF) runs across the tail, the film's accent. Two round tail lights (#FF3B30) glow
  brighter under braking. There is no badge, no number, no sponsor and no plate.
- **Sky:** a graded #0B0C10 → #1A1D27, with a few fixed-seed stars.
- **Speed feel:** road-segment stripes stream past, lamps whip by, a light vignette deepens with speed, and at full
  boost the field of view widens by 8% with blue speed lines.
- **HUD (original):** an Edgepad-style ruler arc at the bottom centre: the same tick spacing and long-blue-tick rhythm
  as the corner dials and the ident, filling with BLUE as throttle rises.
  - A boost pip sits beside it.
  - No digits, no gear number, no lap timer, no minimap and no position readout. This rules out any speed figure and
    keeps the HUD unlike any real game's.

## Engine (pseudo-3D road)

- **Technique:** a road of 600 segments, each 200 units long, with a curve and a hill value per segment, projected
  with a fixed camera height and depth. Draw back to front.
- **Track:** one lap, built as a table: a straight (80 segments), a long right-hander (curve +2, 120), a short straight
  (40), a hairpin left (curve −5, 60), a crest (hill +40 over 60), and back to the start.
- **Physics, a pure function of t:**
  - speed(t) is integrated in closed form from the input track below:
    - `accel = 0.9·RT`
    - `brake = 1.6·LT`
    - `drag = 0.02·speed`
    - a boost adds 0.6 for its 1.2 s.
  - position(t) is the integral of speed(t). The car's lateral offset is x(t) = smoothed(LX(t)) − centrifugal(curve,
    speed), clamped to the road.
  - Precompute the whole timeline at 240 Hz once at load into arrays (a deterministic table), then read by
    interpolation at any t. This keeps seeking exact.
- **Car pose:** yaw is 8° × LX, body roll is 3° × LX, pitch is −1.5° under throttle and +2° under brake. Pose changes
  ease over 100 ms.

## Input track (the phone's gamepad and the game share this table)

Time is from the start of the race segment (demo5 line 9, starting on "two"). The gamepad renderer reads the same
table, so the phone's stick and triggers move exactly as the car does.

| t (s) | LX (left stick x, −1 to 1) | RT (0–1) | LT (0–1) | A | What happens |
|---|---|---|---|---|---|
| 0.0 | 0 | 0 | 0 | — | Green light: three small blue pips go out on the HUD |
| 0.3 | 0 | 1.0 | 0 | — | Launch, full throttle |
| 2.0 | +0.45 | 0.85 | 0 | — | Into the long right-hander |
| 3.6 | +0.2 | 1.0 | 0 | — | Exit, straightening |
| 4.4 | 0 | 1.0 | 0 | press 4.4–5.6 | Boost on the short straight: blue speed lines |
| 5.8 | 0 | 0 | 0.9 | — | Hard braking into the hairpin, tail lights flare |
| 6.4 | −0.95 | 0.4 | 0.2 | — | Hairpin left, tyre squeal |
| 7.6 | −0.3 | 1.0 | 0 | — | Exit |
| 8.6 | 0 | 1.0 | 0 | — | Over the crest |
| 10.0 | 0 | 0.7 | 0 | — | Cruise. This is where line 10 starts; the race continues small behind the editor |

Between rows, values interpolate with in-out easing over 200 ms. For reel5, use rows 0.0–8.6 compressed to 0.85×
time, and end on the crest.

## Attract mode (demo5 line 1, reel5 line 1)

The car laps the long right-hander at a steady 70% throttle and LX +0.35. The HUD is hidden. The camera sits 30% higher
for a cinematic view, and the scene loops seamlessly on a 6-second period.

## Sound

The engine pitch follows speed(t), and its loudness follows RT and the boost. The tyre squeal plays when |LX| > 0.7 and
speed > 60% of max. The boost has its own sound. All three are earshot cues (SOUND.md).
