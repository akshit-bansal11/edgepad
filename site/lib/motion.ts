/**
 * Motion durations, in seconds, read from the CSS tokens in app/globals.css
 * (`--duration-fast|base|slow`). The tokens are the only copy: under reduced motion
 * they are 0ms, so every Motion tween here collapses to nothing as well. On the server
 * there is no document and nothing animates, so 0 is the right answer there too.
 */
export type DurationToken = "fast" | "base" | "slow";

export function seconds(token: DurationToken): number {
  if (typeof document === "undefined") return 0;
  const raw = getComputedStyle(document.documentElement).getPropertyValue(
    `--duration-${token}`,
  );
  const ms = Number.parseFloat(raw);
  return Number.isFinite(ms) ? ms / 1000 : 0;
}
