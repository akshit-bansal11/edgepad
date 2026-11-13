"use client";

import { MotionConfig } from "motion/react";

/** One place that makes every Motion transform animation follow prefers-reduced-motion. */
export function MotionProvider({ children }: { children: React.ReactNode }) {
  return <MotionConfig reducedMotion="user">{children}</MotionConfig>;
}
