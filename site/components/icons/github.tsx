import { readFileSync } from "node:fs";
import path from "node:path";

/**
 * GitHub's mark, pulled from svgl and stored at `public/github.svg`.
 *
 * Lucide 1.x removed every brand icon, so this is the one glyph on the site that is
 * not Lucide. The path is read out of that file at build time rather than pasted here,
 * so the SVG in `public/` stays the single source: replace the file and the component
 * follows.
 *
 * Inlined rather than served through an <img> because an <img> cannot inherit
 * `currentColor`, and this site flips between pure black and pure white.
 *
 * Server-only: it reads from disk at module load, so it must not be imported into a
 * client component.
 */
const MARK_PATH = (() => {
  const file = path.join(process.cwd(), "public", "github.svg");
  const raw = readFileSync(file, "utf8");
  const match = /\sd="([^"]+)"/.exec(raw);
  if (!match?.[1]) {
    throw new Error(
      `No path data found in ${file}. The GitHub mark is read from that file at build time; re-download it from svgl if it is missing or malformed.`,
    );
  }
  return match[1];
})();

export function GithubMark({ className }: { className?: string }) {
  return (
    <svg
      viewBox="0 0 1024 1024"
      fill="currentColor"
      aria-hidden="true"
      focusable="false"
      className={className}
    >
      <path fillRule="evenodd" clipRule="evenodd" d={MARK_PATH} />
    </svg>
  );
}
