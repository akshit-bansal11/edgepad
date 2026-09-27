"use client";

import {
  AnimatePresence,
  type MotionProps,
  motion,
  type UseInViewOptions,
  useInView,
  type Variants,
} from "motion/react";
import { useRef } from "react";
import { cn } from "@/lib/utils";

type MarginType = UseInViewOptions["margin"];

interface BlurFadeProps extends MotionProps {
  children: React.ReactNode;
  className?: string;
  variant?: {
    hidden: { y: number };
    visible: { y: number };
  };
  duration?: number;
  delay?: number;
  offset?: number;
  direction?: "up" | "down" | "left" | "right";
  inView?: boolean;
  inViewMargin?: MarginType;
  blur?: string;
}

const getFilter = (v: Variants[string]) =>
  typeof v === "function" ? undefined : v.filter;

export function BlurFade({
  children,
  className,
  variant,
  duration = 0.4,
  delay = 0,
  // Edgepad's defaults: a short lift into place, once, when the block scrolls into
  // view. This replaced the site's own Reveal, which did the same without the blur.
  offset = 8,
  direction = "up",
  inView = true,
  inViewMargin = "-50px",
  blur = "6px",
  ...props
}: BlurFadeProps) {
  const ref = useRef(null);
  const inViewResult = useInView(ref, { once: true, margin: inViewMargin });
  const isInView = !inView || inViewResult;
  const defaultVariants: Variants = {
    hidden: {
      [direction === "left" || direction === "right" ? "x" : "y"]:
        direction === "right" || direction === "down" ? -offset : offset,
      opacity: 0,
      filter: `blur(${blur})`,
    },
    visible: {
      [direction === "left" || direction === "right" ? "x" : "y"]: 0,
      opacity: 1,
      filter: `blur(0px)`,
    },
  };
  const combinedVariants = variant ?? defaultVariants;

  const hiddenFilter = getFilter(combinedVariants.hidden);
  const visibleFilter = getFilter(combinedVariants.visible);

  const shouldTransitionFilter =
    hiddenFilter != null && visibleFilter != null && hiddenFilter !== visibleFilter;

  return (
    <AnimatePresence>
      <motion.div
        ref={ref}
        initial="hidden"
        animate={isInView ? "visible" : "hidden"}
        exit="hidden"
        variants={combinedVariants}
        transition={{
          delay: 0.04 + delay,
          duration,
          ease: "easeOut",
          ...(shouldTransitionFilter ? { filter: { duration } } : {}),
        }}
        // Under prefers-reduced-motion nothing moves or blurs: these override the
        // inline styles motion writes, so the content is simply there. Done in CSS,
        // not with useReducedMotion, because the server cannot know the preference:
        // a hook would render different markup on the client, and React does not
        // repair a mismatched style on hydration, so the content stayed invisible.
        className={cn(
          className,
          "motion-reduce:opacity-100! motion-reduce:filter-none! motion-reduce:transform-none!",
        )}
        {...props}
      >
        {children}
      </motion.div>
    </AnimatePresence>
  );
}
