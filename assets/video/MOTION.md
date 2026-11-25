# Edgepad 5 film: motion

Every frame is a pure function of the timeline's time (film.js's rule), so HyperFrames can seek anywhere. There are no
timers, no random numbers without a fixed seed, and no state carried between frames. The game (GAME.md) follows the
same rule.

## Tiers (the app's own, UX-27 / UX-32)

| Tier | Ceiling | Easing | Used for |
|---|---|---|---|
| Press | 160 ms | out (cubic 0.2, 0.8, 0.2, 1) | tap dots, key presses, button states |
| Reveal | 300 ms | out | popups and sheets opening from where they were opened, cards rising 16 px as they fade in |
| Scene | 400 ms | in-out (cubic 0.65, 0, 0.35, 1) | a screen change carried across (slide, morph), cross-dissolves |
| Camera | 520–1400 ms | in-out | camera moves only, always landing on the word that names the target |

Nothing in the film cuts. Every change is a camera move, a morph, or a cross-dissolve of 400 ms or less over moving
picture.

## Camera presets (add to film.js CAMS for '169', '916' and '34')

Keep the existing presets (`off`, `right`, `hero`, `tl`, `tr`, `br`, `bl`, `top`, `media`, `settings`, `land`,
`landRight`, `end`, `wide`) and add:

- `roomWide`: the whole room, the laptop on its stand far back, and the phone off. In 16:9 the laptop screen is 360 ×
  225 at x 0.42, y 0.47. Dolly in 6% over the first line.
- `raceSplit`: the laptop screen big (16:9: 1040 × 650 at the left), and the sideways phone at 1.0× lower right. In
  the tall cuts the laptop is in the top band and the phone below.
- `split169`: the laptop screen at 1.3× on the left and the phone's top-left corner at 1.3× on the right (tall cuts:
  stacked).
- `talk`: the existing straight-talk card position (F.talk).

A move between presets interpolates x, y, s and r with in-out easing and a 3% overshoot only on scale.

## Named moves

- **M-IDENT (the dial ident):** port scripts/logo-motion.ps1's maths to JS. The logo-motion.ps1 1024 canvas has the
  pivot at 881.8, stroke 88 with round caps, the arc r 288 (180°→270°), the side ticks r 424→656, and the needle r
  424→720, at angles 180, 202.5, 225, 247.5 and 270.
  - **Pop:** the parts arc, t1, t2, needle, t3 and t4, each delayed i·60 ms and lasting 420 ms.
    - k = 0.55 + 0.45·spring(x), where spring(x) = x ≥ 1 ? 1 : 1 − e^(−6x)·cos(9x).
    - opacity = clamp(x / 0.25).
    - The pop totals 760 ms.
  - **Dial:**
    - From 900 ms a ruler shows a tick every 22.5°. Every third tick (k mod 3 == 2) is the long blue one.
    - The ruler is visible between 168.75° and 281.25°, with fades at the ends.
    - The drag follows the waypoints [[900,0],[1750,150],[2150,122],[2450,142]] with in-out easing.
    - On release it snaps to a multiple of 67.5° over 180 ms with out easing.
    - The pressed stroke width is 88 + 8·held.
  - The total is 2650 ms. Clip every stroke to the tile, and hide any zero-length stroke (round caps draw a dot for it).
  - Check the JS port against a frame of assets/brand/edgepad/motion/edgepad-logo-dial-light.svg at 0.5, 1.2, 2.0
    and 2.65 s.
- **M-HANDOFF:** after the ident on demo5 line 3 and reel5 line 2, the mark scales down and travels (Scene tier,
  400 ms) to the phone's top-left corner. Its blue ruler cross-fades into the phone's volume ruler at the same angle.
- **M-FLY (thumbnail, text chip, file):** an arc from the source to the target, 400 ms in-out. The path is a quadratic
  curve whose control point sits 18% of the distance above the midpoint. Scale goes 1 → 0.6 → 1 and the shadow
  deepens at the apex. A 120 ms "land" squash (scale 1.04 then 1) plays at the end.
- **M-TILT (Air mouse):** the phone rotates about its vertical axis ±10°, drawn as a horizontal skew plus a 4% width
  change. The laptop pointer follows with a 120 ms lag.
- **Key reflow (keyboard sizes, gamepad presets):** each key or control interpolates from its old rectangle to its
  new one over 280 ms, in-out, staggered 6 ms by row. Keys that vanish fade out over 160 ms. New keys fade in over the
  last 160 ms.
- **Ruler roll:** a dial's number rolls as a 120 ms vertical slot per step (the app's own ruler draw).
- **Flashes (demo5 line 3):** three 120 ms morphs (surface → keyboard → gamepad → surface). They are the only fast
  changes in the film.
- **Headline reveal:** the 4.0 film.js reveal, word by word, 60 ms apart, each word rising 12 px and fading in over
  300 ms. The keyword's blue fades in 80 ms after the word.

## Easing reference

The values match film.js's EP.ease: `out = cubic-bezier(0.2, 0.8, 0.2, 1)`, `inOut = cubic-bezier(0.65, 0, 0.35, 1)`.
prefers-reduced-motion does not apply to a rendered film.
