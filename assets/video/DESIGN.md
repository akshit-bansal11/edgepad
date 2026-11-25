# Edgepad 5 film: design

The 4.0 dark look (film.js 13–17), kept and extended. Every value below is a token. Define each once in film.js (or a
new ui/tokens.js) and never repeat a literal.

## Colour

| Token | Value | Use | Contrast |
|---|---|---|---|
| GROUND | #1E2026 | page ground | — |
| INK | #F2F3F5 | headline, sub-lines | 14.7:1 on GROUND, 12.4:1 on PILL |
| GREY | #9A9CA3 | secondary text, card labels | 5.9:1 on GROUND, 5.0:1 on PILL |
| BLUE | #4DA3FF | the one keyword per headline, glows, the ident's accent | 6.2:1 on GROUND, 5.2:1 on PILL |
| PILL | #2B2D33 | caption pill, dark laptop cards | — |
| PHONE | #0E0F12 | the app's own dark theme, drawn by the renderers | — |
| ROOM_DEEP | #121317 | the room's far wall (new; one step darker than GROUND) | — |
| LAPTOP_BODY | #2B2D33 | the laptop's lid and deck (new), with a 1.5 px rgba(255,255,255,0.12) rim | — |
| LASER | #FF3B30 core, rgba(255,59,48,0.35) glow | the laser dot, as AnnotationOverlay.cs draws it | — |
| GAME_* | see GAME.md | the racing game, inside the laptop screen only | — |

The mark is the white tile (assets/brand/edgepad/edgepad-logo-light.svg) on the dark ground, because the dark tile
vanishes on it. The ident uses the motion files' maths (MOTION.md M-IDENT) in the light-tile colours: tile #FFFFFF,
ink #16171A, accent #0068D6.

## Type

Lato only (brand/Lato-*.ttf), the app's own face.

| Role | 16:9 | 9:16 | 3:4 | Weight |
|---|---|---|---|---|
| Headline | 110 px, max width 800 | 96 px, max 920 | 84 px, max 936 | Black |
| Headline, top layout (sideways phone) | 96 px, max 1300 | 96 | 84 | Black |
| Sub-line | 56 px, gap 52 | 50, gap 36 | 42, gap 28 | Bold |
| Caption pill | 44 px | 44 | 40 | Bold |
| Laptop card label | 22 px | 22 × 0.8 | 22 × 0.72 | Bold, GREY |
| End card close line | 56 px | 54 | 50 | Black |

These are film.js's F table (19–49). Keep them; only the new laptop presets are added.

## Layout

- **16:9 (demo5):** the phone stands right (CAMS 169 `right`/`hero`), with the headline and sub-lines left from x 150.
  - The new laptop sits back left in the room, its screen 640 × 400 at rest.
  - `raceSplit`: the laptop screen grows to 1040 × 650, centred left, and the sideways phone sits lower right at 1.0×.
  - The caption pill is centred at y 990.
- **9:16 (instagram5):** the laptop screen sits in the top band (centre y 520, 880 × 550), the headline band below it,
  and the phone in the bottom half.
  - Safe areas for Instagram: keep text out of the top 250 px and the bottom 340 px.
  - The caption pill sits at y 1664, above the bottom safe area's UI.
- **3:4 (linkedin5):** the same stack, tighter.
  - The laptop screen is centred at y 330, 800 × 500.
  - The headline is at y 84 when the laptop is off, and drops below the laptop when it is on.
  - The pill is at y 1372.
- **Margins:** at least 72 px on every edge in every cut, and at least 32 px between any two text blocks.
- **Laptop shape (new, EP.laptop):**
  - A generic thin-bezel laptop: a screen with a 14 px bezel and a 2% corner radius, and the deck as a trapezoid below
    it at 18% of the screen height.
  - No logo, no brand shape, no notch.
  - The screen is lit: a 60 px blur glow in the screen's average colour, at 25%, on the wall behind.
- **Phone shape:** the 4.0 drawPhone (film.js 348) with its thin rim light. In the hook it is held by a soft hand
  silhouette: one flat shape in #15161A with a 1 px rim, and no fingers drawn over the screen.

## Rules

- One keyword in BLUE per headline, never two.
- Every UI string on the phone comes from res/values/*.xml through the port, never retyped by hand into the film. The
  sample data are the only exception: neutral file names, repo names, slide titles, macro names (Simple Icons apps only)
  and ring values.
- No OS logos, no app logos other than the Simple Icons set already in icons/ (CC0, see icons/NOTICE) and the Edgepad mark.
- No digits on the game's speedometer, and no speed figure on the transfer card. Show the percent only.
