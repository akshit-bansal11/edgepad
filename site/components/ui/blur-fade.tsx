"use client";

import {
  AnimatePresence,
  type MotionProps,
  motion,
  type UseInViewOptions,
  useInView,
  type Variants,
} from "motion/react";
import { useEffect, useRef, useState } from "react";
import { seconds } from "@/lib/motion";
import { cn } from "@/lib/utils";

type MarginType = UseInViewOptions["margin"];

interface BlurFadeProps extends MotionProps {
  children: React.ReactNode;
  className?: string;
  variant?: {
    hidden: { y: number };
    visible: { y: number };
  };
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
  const ref = useRef<HTMLDivElement>(null);
  const inViewResult = useInView(ref, { once: true, margin: inViewMargin });
  const isInView = !inView || inViewResult;

  // Progressive enhancement: the server HTML, and the first client render, show the
  // content. Only once the page has hydrated is a block that sits below the fold hidden,
  // ready to be revealed when it scrolls in. Without JS nothing is ever invisible, and
  // a block already on screen never flashes. The measuring waits a frame so it is not a
  // synchronous state update inside the effect.
  const [armed, setArmed] = useState(false);
  useEffect(() => {
    const frame = requestAnimationFrame(() => {
      const top = ref.current?.getBoundingClientRect().top ?? 0;
      setArmed(top >= window.innerHeight);
    });
    return () => cancelAnimationFrame(frame);
  }, []);

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

  // The reveal, delay included, never runs past the slow token (300 ms): a stagger
  // can use at most a third of it.
  const total = seconds("slow");
  const wait = Math.min(delay, total / 3);
  const duration = total - wait;

  return (
    <AnimatePresence>
      <motion.div
        ref={ref}
        initial={false}
        animate={armed && !isInView ? "hidden" : "visible"}
        exit="hidden"
        variants={combinedVariants}
        transition={{
          delay: wait,
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
