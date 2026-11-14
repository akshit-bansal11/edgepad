"use client";

import { Monitor, Moon, Sun } from "lucide-react";
import { useTheme } from "next-themes";
import { useSyncExternalStore } from "react";

/** The cycle order: follow the device, then light, then dark, then round again. */
const CYCLE = [
  { value: "system", label: "System", Icon: Monitor },
  { value: "light", label: "Light", Icon: Sun },
  { value: "dark", label: "Dark", Icon: Moon },
] as const;

/** Never notifies: the value flips once, when hydration swaps the snapshot. */
const subscribe = () => () => {};

/**
 * One button that steps through the three themes. Its name states the current theme
 * and what a press does, so a screen reader hears both without a group to explore.
 */
export function ThemeToggle() {
  const { theme, setTheme } = useTheme();

  // The server cannot know the chosen theme, so the real one is only shown after
  // hydration. useSyncExternalStore is the hydration-safe way to ask "am I on the
  // client yet": the server snapshot during SSR, the client one after.
  const mounted = useSyncExternalStore(
    subscribe,
    () => true,
    () => false,
  );

  const index = Math.max(
    0,
    CYCLE.findIndex((option) => mounted && option.value === theme),
  );
  const current = CYCLE[index] ?? CYCLE[0];
  const next = CYCLE[(index + 1) % CYCLE.length] ?? CYCLE[0];
  const Icon = current.Icon;

  return (
    <button
      type="button"
      onClick={() => setTheme(next.value)}
      aria-label={`Colour theme: ${current.label}. Switch to ${next.label}.`}
      title={`Colour theme: ${current.label}`}
      className="press bg-faint text-foreground hover:text-primary grid size-11 place-items-center rounded-full transition-colors"
    >
      <Icon aria-hidden className="size-4" />
    </button>
  );
}
