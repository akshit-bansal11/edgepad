# Edgepad website

Two routes, built from the repository they document: a landing page at `/`, and the
whole documentation on one page at `/docs`.

**Live at [edgepad.vercel.app](https://edgepad.vercel.app).** Deployed to Vercel from the repository root (not from `site/`), because the build reads `../protocol/*.txt` — see *Hosting* below.

It is **not** part of either app's quality gate and CI does not build it. `scripts/check.ps1` still checks only `android/` and `windows/`.

## Running it

```bash
cd site
npm install
npm run dev        # http://localhost:3000
```

## The quality gate

One command, three tools, in this order. It exits non-zero on any finding.

```bash
npm run check      # Biome (format + import order + lint) -> ESLint -> tsc
npm run check:ci   # the same, non-mutating: fails on unformatted code
```

`check` writes, because fixing your own formatting is useful. `check:ci` must not write to a branch, so it fails instead of quietly reformatting a pull request. That is the same split `scripts/check.ps1` uses for the two apps.

| Tool | Owns | Command |
| --- | --- | --- |
| **Biome** | Formatting, import order, general JS/TS lint | `biome check --write .` |
| **ESLint** | React hooks, JSX, accessibility, Next.js rules | `eslint .` |
| **tsc** | Type checking | `tsc --noEmit` |

Individually: `npm run format`, `npm run lint`, `npm run typecheck`.

**The split is deliberate and the overlap is switched off.** Biome and ESLint can both lint JavaScript, and left alone they fight over the same lines. Biome formats and organises imports; ESLint ships no stylistic rules at all (they left ESLint core), so no `eslint-config-prettier` layer is needed. Biome's two React-hook rules (`useExhaustiveDependencies`, `useHookAtTopLevel`) are turned **off** in `biome.json` because `eslint-config-next` already carries `eslint-plugin-react-hooks`.

Two rules in `biome.json` encode project rules rather than defaults: `noExplicitAny` is an error, and `noConsole` is an error that allows `warn` and `error` only.

**Accessibility is checked twice, on purpose.** Biome has a11y rules and `eslint-config-next` brings `jsx-a11y`. They overlap, so some findings are reported by both. That is noisier than switching one off, and switching one off is not a trade worth making for accessibility.

**Stylelint is not used.** With Tailwind 4 nearly all styling lives in `className` attributes; the site has one CSS file of about 170 lines, so Stylelint would guard almost nothing. Biome's CSS formatter and linter are also disabled, because Tailwind 4's `@theme`, `@custom-variant` and `@layer` at-rules are not something an untested CSS parser should be let loose on. `app/globals.css` is formatted by hand.

Node 24 or later (Active LTS; Node 20 and 18 are past end of security support). Verified on Node 24.19.0.

### About `.editorconfig`

`site/.editorconfig` adds to the repository root one and does not replace it. The root sets `indent_size = 4` for `[*]`, which is right for Kotlin and C# and wrong here. Biome reads editorconfig, so without that local override it would reformat the entire site to 4-space indents on its first run.

```bash
npm run build      # next build, kept out of the gate
```

## It must be built from inside the repository

`lib/protocol.ts` reads `../protocol/frames.txt` and `../protocol/actions.txt` at build time — the same two golden fixtures both the Kotlin and the C# test suites read at run time. The frame table, the byte table, and the action, control and TEXT-kind tables on the page are all generated from them.

That is deliberate. A hand-typed copy of those tables would be a third place the protocol is written down, and nothing would check it. As it stands the site cannot drift from the two apps: if a fixture gains a frame, the page grows a row.

The merge is left-joined **from the fixture**. Prose (what a frame means, what an action does) lives in `lib/protocol.ts` because it cannot come from a fixture, but a row that exists in the fixture with no prose is still rendered, marked *"In the fixture, not yet described on this page"* — never silently dropped. Prose describing a frame no fixture covers is surfaced on the page as a drift warning.

If the fixtures cannot be parsed at all, `assertParsed` throws and the build fails. A protocol page that renders an empty protocol table would be worse than no page.

## Structure

```
app/
  layout.tsx          fonts, metadata, theme provider
  page.tsx            the landing page: hero, features, how it works, limits, install
  docs/page.tsx       the documentation: sidebar layout, composes the four section files
  globals.css         the palette and the two font variables
lib/
  protocol.ts         reads and parses ../protocol/*.txt  (the only non-trivial logic)
  nav.ts              the table of contents; every id must exist as a section anchor
  utils.ts            cn()
components/
  sections/           the documentation itself, in four files
  ui/                 shadcn primitives and Magic UI components (see below)
  section.tsx         Section, Sub, P, Note, C
  toc.tsx             sidebar with an IntersectionObserver scroll-spy
  flow-diagram.tsx    the two-column architecture map
  link-beam.tsx       phone -> RFCOMM -> laptop, drawn with Magic UI's AnimatedBeam
  surface-figure.tsx  the phone's control surface in the 2.0 corner-dial style, drawn in SVG
  site-header.tsx     shared; `variant` picks the landing menu or the table of contents
  site-footer.tsx     shared
  legacy-hash-redirect.tsx   see below
```

### Anchors that used to live at `/`

Every documentation anchor — `#protocol`, `#install`, `#architecture` and the rest —
was on `/` before the landing page existed. A static export has no server to redirect
them with, so `components/legacy-hash-redirect.tsx` does it on the client: on the
landing page, a hash that appears in `ALL_NAV_IDS` is forwarded to `/docs#<id>` with
`location.replace`, which leaves no history entry to bounce back to and lets the
browser scroll to the anchor itself on the new document.

The allowlist is the table of contents, so a link only leaves the landing page when it
names a section that really is on the other one. The landing page's own three anchors
(`#features`, `#how`, `#get`) are deliberately named nothing in that list — reusing
`#install` on both routes would make the redirect ambiguous.

## Design notes

The site follows the Edgepad 2.0 design system: calm, rounded, one blue.

**Palette.** The app's own 2.0 palette, mapped onto shadcn's CSS variables in `app/globals.css` so components installed with the CLI pick it up. Light is a soft grey ground (`#F2F3F7`) with white grouped cards; dark is a soft near-black (`#0E0F12`) with off-white ink, not `#000` and `#FFF`. One accent, a blue (`#0068D6` light, `#4DA3FF` dark), kept for primary actions, links, selection and live things. Every text pair used was computed at 4.5:1 or better; the two that fail (dim on `faint`, dim on `accent-soft`, both in light) are named in the stylesheet so nobody uses them.

**Type.** Lato in 400, 700 and 900, sentence case everywhere, no letter-spaced capitals. JetBrains Mono only for what really is code: commands, byte tables, protocol ids.

**Motion.** Magic UI's BlurFade for section entrances (a short lift and unblur, once), a BorderBeam around the hero phone, an AnimatedBeam on the "How it works" figure and a shine on the hero pill. Under `prefers-reduced-motion` all of them stop, **in CSS** (`motion-reduce:` utilities), never with `useReducedMotion`: the server cannot know the preference, so a hook renders different markup on the client, and React does not repair a mismatched `style` on hydration, which left every docs heading invisible. The hero headline and the docs title have no entrance at all, so the first paint never waits for JavaScript.

**Theme toggle.** `next-themes` with `attribute="class"`, three states (light / dark / system). The dark variant follows the class rather than the media query, so the toggle wins over the OS in both directions.

## Two things done by hand

**The older shadcn primitives were written into `components/ui/` by hand** (button, badge, tabs, accordion, table), because this project was first scaffolded without a network install step, and they were restyled for 2.0 in place. Everything added since came from the CLI:

```bash
npx shadcn@latest add card                       # shadcn
npx shadcn@latest add @magicui/blur-fade @magicui/grid-pattern @magicui/animated-shiny-text   @magicui/animated-beam @magicui/border-beam @magicui/magic-card   # Magic UI
```

`components.json` registers the `@magicui` namespace, so `npx shadcn@latest add @magicui/<name>` works from here. Installed components are then owned code: each was trimmed or fixed after install, and the reason is in its comments (MagicCard lost its orb mode and its theme sniffing; the beams and BlurFade handle reduced motion in CSS; `card.tsx` came out of the CLI importing `cn` from an npm package called `cn`, which was removed and the import pointed at `@/lib/utils`).

**Versions in `package.json` are pinned exactly**, read from the npm registry rather than from memory. Two pins are deliberately behind `latest`, and both are load-bearing:

- **`eslint` is pinned to `9.39.5`, not `10.x`.** `eslint-config-next@16.3.5` declares a peer range of `>=9.0.0`, but the `eslint-plugin-react@7.37.5` it bundles calls a context API that ESLint 10 removed. On ESLint 10 every lint run dies with `contextOrFilename.getFilename is not a function` before reporting anything. Do not bump ESLint until `eslint-config-next` ships a plugin set that supports 10.
- **`typescript` is pinned to `5.9.3`, not `7.0.2`.** TypeScript 7 is the new native port. 5.9.3 is what this project was verified against; 7 may well work, but nothing here has run under it.

Both are safe to revisit — just run `npm run check` after.

## Content sources

Everything on both pages traces to something in this repository:

| Section | Source |
| --- | --- |
| What Edgepad is, install, using it | `README.md` |
| The split, transport, architecture, threads | `docs/ARCHITECTURE.md` |
| Wire protocol, actions and controls | `protocol/*.txt` (generated) and `docs/PROTOCOL.md` |
| Security and trust | `SECURITY.md`, `docs/PROTOCOL.md`, `CHANGELOG.md` |
| Repository layout, prerequisites, gate, tests, contributing | `CONTRIBUTING.md`, `scripts/check.ps1`, `.github/workflows/` |
| Version history | `CHANGELOG.md` |
| The landing page, end to end | `README.md`, and the four section files it summarises |

Two deliberate omissions, both of which are project rules rather than oversights:

- **No latency figure appears anywhere.** None has ever been measured on real hardware. The app carries a live round-trip readout for exactly this reason. Nothing goes on either page until a real number is reported.
- **Nothing is invented to fill a section.** Where something is unverified or not built, the site says so — see the *Known limits* section, which labels each entry `not built`, `unverified` or `accepted`.

## One known contradiction in the repository

`SECURITY.md` still carries pre-2.0.0 wording: *"There is no frame for a key code, a scan code or a command."* There is — `0x22 KEY` carries a raw Windows virtual-key code, and `TEXT` kind 3 carries arbitrary text. The 2.0.0 changelog explicitly retracted that claim and `docs/PROTOCOL.md` documents the corrected position.

The site follows `docs/PROTOCOL.md` and the changelog, and says plainly that `SECURITY.md` is stale. Fixing `SECURITY.md` itself is a separate change to the repository's own docs and was left alone.

## Hosting

Live at [edgepad.vercel.app](https://edgepad.vercel.app), on Vercel. The Vercel project
is still named `edgepad-docs`, and its old domain `edgepad-docs.vercel.app` 308s to the
new one from `vercel.json`, because Edgepad.exe through 3.0.1 has the old address in its
tray menu. `cleanUrls` there is what makes `/docs` serve `docs.html` from the export.

The site is a **static export** (`output: "export"` in `next.config.ts`): every page is
prerendered and nothing is read at request time, so it ships as plain files with no
Next.js runtime and no serverless functions. It would serve just as well from GitHub
Pages or any static host.

It deploys from the **repository root**, not from `site/`. That is deliberate:
`lib/protocol.ts` reads `../protocol/*.txt` at build time, so a deploy rooted at `site/`
cannot see the fixtures and the build fails — loudly, by design. The root `vercel.json`
therefore runs the build inside `site/` and points Vercel at `site/out`, and
`.vercelignore` keeps the two apps and the demo video (about 180 MB together) out of the
upload.

```bash
vercel deploy --prod     # from the repository root
npm run preview          # or serve the export locally from site/
```
