"use client";

import { motion, useMotionTemplate, useMotionValue } from "motion/react";
import type { PointerEvent, ReactNode } from "react";
import { cn } from "@/lib/utils";

/**
 * Magic UI's MagicCard, installed with `npx shadcn add @magicui/magic-card` and cut
 * down to the one mode this site uses: a soft spotlight that follows the pointer, and
 * a border that lights up under it.
 *
 * Removed from the registry version: the "orb" mode, its theme detection (a
 * `setMounted` effect that re-rendered every card after hydration) and three
 * window-level listeners that existed only to reset the orb. Leaving the card is
 * enough to reset the gradient.
 *
 * It is pointer-driven, not animated: nothing moves unless the pointer does, so it is
 * unaffected by prefers-reduced-motion. On touch there is no hover and it is a plain
 * card.
 */
export function MagicCard({
  children,
  className,
  gradientSize = 220,
  gradientColor = "var(--blue-soft)",
  gradientFrom = "var(--blue)",
  gradientTo = "var(--blue-soft)",
}: {
  children?: ReactNode;
  className?: string;
  gradientSize?: number;
  gradientColor?: string;
  gradientFrom?: string;
  gradientTo?: string;
}) {
  const mouseX = useMotionValue(-gradientSize);
  const mouseY = useMotionValue(-gradientSize);

  const border = useMotionTemplate`
    linear-gradient(var(--card) 0 0) padding-box,
    radial-gradient(${gradientSize}px circle at ${mouseX}px ${mouseY}px,
      ${gradientFrom}, ${gradientTo}, var(--line) 100%) border-box`;
  const spotlight = useMotionTemplate`
    radial-gradient(${gradientSize}px circle at ${mouseX}px ${mouseY}px,
      ${gradientColor}, transparent 100%)`;

  const move = (event: PointerEvent<HTMLDivElement>) => {
    const rect = event.currentTarget.getBoundingClientRect();
    mouseX.set(event.clientX - rect.left);
    mouseY.set(event.clientY - rect.top);
  };

  const reset = () => {
    mouseX.set(-gradientSize);
    mouseY.set(-gradientSize);
  };

  return (
    <motion.div
      className={cn(
        "group relative isolate overflow-hidden border border-transparent",
        className,
      )}
      onPointerMove={move}
      onPointerLeave={reset}
      style={{ background: border }}
    >
      <motion.div
        aria-hidden
        className="pointer-events-none absolute inset-px z-0 rounded-[inherit] opacity-0 transition-opacity duration-300 group-hover:opacity-60"
        style={{ background: spotlight }}
      />
      <div className="relative z-10 h-full">{children}</div>
    </motion.div>
  );
}
