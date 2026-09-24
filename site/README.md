# Edgepad website

Two routes, built from the repository they document: a landing page at `/`, and the
whole documentation on one page at `/docs`.

**Live at [edgepad-docs.vercel.app](https://edgepad-docs.vercel.app).** Deployed to Vercel from the repository root (not from `site/`), because the build reads `../protocol/*.txt` — see *Hosting* below.

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

**Stylelint is not used.** With Tailwind 4 nearly all styling lives in `className` attributes; the site has one CSS file of about 100 lines, so Stylelint would guard almost nothing. Biome's CSS formatter and linter are also disabled, because Tailwind 4's `@theme`, `@custom-variant` and `@layer` at-rules are not something an untested CSS parser should be let loose on. `app/globals.css` is formatted by hand.

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
  ui/                 shadcn primitives, hand-placed (see below)
  section.tsx         Section, Sub, P, Note, C
  toc.tsx             sidebar with an IntersectionObserver scroll-spy
  reveal.tsx          the site's only entrance animation
  flow-diagram.tsx    the two-column architecture map
  surface-figure.tsx  the phone's control surface, drawn in SVG rather than photographed
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

**Palette.** Edgepad's own: pure `#000` on `#FFF` or `#FFF` on `#000`, with the greys flattened to opaque values rather than alphas so overlapping strokes do not darken. Light dim `#737373` is 4.74:1 on white; dark dim `#8C8C8C` is 6.5:1 on black. There is no accent colour in the product and none here.

**Type.** The app sets every piece of text in JetBrains Mono. The site keeps that for everything structural — headings, labels, tables, code, navigation — and falls back to Inter for running prose only, because several thousand words of mono is harder to read than it is characterful.

**Motion.** One entrance animation: an 8px lift and a fade, once, on first view. It is skipped entirely under `prefers-reduced-motion`. No parallax and nothing that holds a compositor layer alive after it has played.

**Theme toggle.** `next-themes` with `attribute="class"`, three states (light / dark / system). The dark variant follows the class rather than the media query, so the toggle wins over the OS in both directions.

## Two things done by hand

**shadcn components are written into `components/ui/` directly** rather than added with `npx shadcn add`, because this project was scaffolded without a network install step. `components.json` is present and correct, so `npx shadcn@latest add <component>` works normally from here.

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

Live at [edgepad-docs.vercel.app](https://edgepad-docs.vercel.app), on Vercel.

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
