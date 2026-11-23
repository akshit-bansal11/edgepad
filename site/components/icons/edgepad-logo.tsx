import { readFileSync } from "node:fs";
import path from "node:path";

/**
 * The Edgepad logo: the rounded tile and the mark, read at build time out of
 * `public/edgepad-logo.svg`, which scripts/logo.ps1 writes from the same geometry as every
 * other copy of the logo. Replace the file and the component follows.
 *
 * Inlined rather than served through an <img> so it takes the theme's own colours: the
 * tile is the card colour, the ticks the ink, the needle and arc the blue.
 *
 * Server-only: it reads from disk at module load.
 */
type Part = { id: string; d: string; role: "bg" | "ink" | "accent" };

const PATH_RE = /<path id="([^"]+)" (fill|stroke)="([^"]+)" d="([^"]+)"\/>/g;

const LOGO = (() => {
  const file = path.join(process.cwd(), "public", "edgepad-logo.svg");
  const raw = readFileSync(file, "utf8");
  const strokeWidth = /stroke-width="([\d.]+)"/.exec(raw)?.[1];
  const parts: Part[] = [];
  for (const [, id, kind, colour, d] of raw.matchAll(PATH_RE)) {
    if (!id || !d) continue;
    const role =
      kind === "fill"
        ? "bg"
        : colour?.toUpperCase() === "#0068D6"
          ? "accent"
          : "ink";
    parts.push({ id, d, role });
  }
  if (!strokeWidth || parts.length !== 7) {
    throw new Error(
      `${file} should hold the tile and six strokes; found ${parts.length} paths. Regenerate it with scripts/logo.ps1.`,
    );
  }
  return { strokeWidth, parts };
})();

const COLOUR = {
  bg: "var(--card)",
  ink: "var(--ink)",
  accent: "var(--blue)",
} as const;

export function EdgepadLogo({ className }: { className?: string }) {
  return (
    <svg
      viewBox="0 0 1024 1024"
      aria-hidden="true"
      focusable="false"
      className={className}
    >
      {LOGO.parts.map((part) =>
        part.role === "bg" ? (
          <path key={part.id} d={part.d} fill={COLOUR.bg} />
        ) : (
          <path
            key={part.id}
            d={part.d}
            fill="none"
            stroke={COLOUR[part.role]}
            strokeWidth={LOGO.strokeWidth}
            strokeLinecap="round"
          />
        ),
      )}
    </svg>
  );
}
