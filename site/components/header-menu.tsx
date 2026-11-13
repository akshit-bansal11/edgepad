"use client";

import { Menu } from "lucide-react";
import { type ReactNode, useEffect, useRef } from "react";

/**
 * The narrow-screen menu: a native <details>, so it still opens with no JavaScript.
 * The script only adds the ways out a menu needs: Escape (focus returns to the
 * button), a press outside, and following any link inside it. A full page load closes
 * it by itself; an in-page anchor or a soft navigation does not, hence the click.
 */
export function HeaderMenu({ label, children }: { label: string; children: ReactNode }) {
  const menu = useRef<HTMLDetailsElement>(null);

  useEffect(() => {
    const el = menu.current;
    if (!el) return;
    const onKey = (event: KeyboardEvent) => {
      if (event.key !== "Escape" || !el.open) return;
      el.open = false;
      el.querySelector("summary")?.focus();
    };
    const onPointer = (event: PointerEvent) => {
      if (event.target instanceof Node && !el.contains(event.target)) el.open = false;
    };
    const onClick = (event: MouseEvent) => {
      if (event.target instanceof Element && event.target.closest("a")) el.open = false;
    };
    document.addEventListener("keydown", onKey);
    document.addEventListener("pointerdown", onPointer);
    el.addEventListener("click", onClick);
    return () => {
      document.removeEventListener("keydown", onKey);
      document.removeEventListener("pointerdown", onPointer);
      el.removeEventListener("click", onClick);
    };
  }, []);

  return (
    <details ref={menu} className="relative lg:hidden">
      <summary className="press bg-faint text-foreground grid h-11 min-w-11 cursor-pointer list-none place-items-center rounded-full px-3 text-[0.9375rem] font-bold sm:px-4 [&::-webkit-details-marker]:hidden">
        <span className="flex items-center gap-1.5">
          <Menu aria-hidden className="size-4" />
          <span className="sr-only sm:not-sr-only">{label}</span>
        </span>
      </summary>
      <div className="bg-card shadow-lift absolute right-0 mt-2 max-h-[75vh] w-[min(20rem,calc(100vw-2rem))] overflow-y-auto rounded-2xl p-2">
        {children}
      </div>
    </details>
  );
}
