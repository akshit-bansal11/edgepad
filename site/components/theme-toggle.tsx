"use client";

import { Monitor, Moon, Sun } from "lucide-react";
import { useTheme } from "next-themes";
import { useSyncExternalStore } from "react";
import { cn } from "@/lib/utils";

const OPTIONS = [
  { value: "light", label: "Light", Icon: Sun },
  { value: "dark", label: "Dark", Icon: Moon },
  { value: "system", label: "System", Icon: Monitor },
] as const;

/** Never notifies: the value flips once, when hydration swaps the snapshot. */
const subscribe = () => () => {};

/**
 * Real radio inputs inside a fieldset, rather than buttons wearing `role="radio"`.
 * Three states where exactly one is chosen is what a radio group is, and doing it
 * natively brings arrow-key navigation and the correct announcement for free.
 *
 * Each input is a transparent overlay filling its label, NOT `sr-only`. `sr-only`
 * positions the element absolutely and clips it to a 1px box; clicking the label then
 * focuses that stray box and the browser scrolls it into view, which with
 * `scroll-behavior: smooth` reads as the whole page sliding away when you change
 * theme. Sized in place, focus cannot move the page.
 */
export function ThemeToggle() {
  const { theme, setTheme } = useTheme();

  // The server cannot know the chosen theme, so the active state is only painted
  // after hydration. useSyncExternalStore is the hydration-safe way to ask "am I on
  // the client yet": it returns the server snapshot during SSR and the client one
  // after, with no effect and no extra render scheduled by setState.
  const mounted = useSyncExternalStore(
    subscribe,
    () => true,
    () => false,
  );

  return (
    <fieldset className="border-line flex border">
      <legend className="sr-only">Colour theme</legend>
      {OPTIONS.map(({ value, label, Icon }) => {
        const active = mounted && theme === value;
        return (
          <label
            key={value}
            title={label}
            className={cn(
              "border-line text-dim relative grid size-9 cursor-pointer place-items-center border-r transition-colors last:border-r-0",
              "hover:text-foreground",
              "has-[:focus-visible]:outline-foreground has-[:focus-visible]:outline-2 has-[:focus-visible]:-outline-offset-2",
              active && "bg-foreground text-background",
            )}
          >
            <input
              type="radio"
              name="theme"
              value={value}
              checked={active}
              onChange={() => setTheme(value)}
              aria-label={label}
              className="absolute inset-0 cursor-pointer appearance-none opacity-0"
            />
            <Icon aria-hidden className="size-4" />
          </label>
        );
      })}
    </fieldset>
  );
}
