"use client";

import { Menu } from "lucide-react";
import { type ReactNode, useEffect, useRef } from "react";

import { seconds } from "@/lib/motion";

/**
 * The narrow-screen menu: a native <details>, so it still opens with no JavaScript.
 * The script only adds the ways out a menu needs: Escape (focus returns to the
 * button), a press outside, and following any link inside it. A full page load closes
 * it by itself; an in-page anchor or a soft navigation does not, hence the click.
 */
export function HeaderMenu({
  label,
  children,
}: {
  label: string;
  children: ReactNode;
}) {
  const menu = useRef<HTMLDetailsElement>(null);

  useEffect(() => {
    const el = menu.current;
    if (!el) return;
    let timer: number | undefined;
    // Shrinks back into the button, then closes; instant under reduced motion.
    const close = () => {
      if (!el.open || el.dataset.closing !== undefined) return;
      const wait = seconds("reveal") * 1000;
      if (wait === 0) {
        el.open = false;
        return;
      }
      el.dataset.closing = "";
      timer = window.setTimeout(() => {
        delete el.dataset.closing;
        el.open = false;
      }, wait);
    };
    const onKey = (event: KeyboardEvent) => {
      if (event.key !== "Escape" || !el.open) return;
      close();
      el.querySelector("summary")?.focus();
    };
    const onPointer = (event: PointerEvent) => {
      if (event.target instanceof Node && !el.contains(event.target)) close();
    };
    const onClick = (event: MouseEvent) => {
      if (event.target instanceof Element && event.target.closest("a")) close();
    };
    document.addEventListener("keydown", onKey);
    document.addEventListener("pointerdown", onPointer);
    el.addEventListener("click", onClick);
    return () => {
      window.clearTimeout(timer);
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
      <div className="menu-panel bg-card shadow-lift absolute right-0 mt-2 max-h-[75vh] w-[min(20rem,calc(100vw-2rem))] overflow-y-auto rounded-2xl p-2">
        {children}
      </div>
    </details>
  );
}
