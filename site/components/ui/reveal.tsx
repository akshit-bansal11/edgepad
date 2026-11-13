"use client";

import { type MotionProps, motion, useInView } from "motion/react";
import { useEffect, useRef, useState } from "react";
import { seconds } from "@/lib/motion";
import { cn } from "@/lib/utils";

interface RevealProps extends MotionProps {
  children: React.ReactNode;
  className?: string;
}

/**
 * Brings a whole block in once, when it scrolls into view: opacity and a short lift,
 * over the base duration. Used on sections, not on every card inside one.
 */
export function Reveal({ children, className, ...props }: RevealProps) {
  const ref = useRef<HTMLDivElement>(null);
  const isInView = useInView(ref, { once: true, margin: "-50px" });

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

  return (
    <motion.div
      ref={ref}
      initial={false}
      animate={armed && !isInView ? { opacity: 0, y: 8 } : { opacity: 1, y: 0 }}
      transition={{ duration: seconds("base"), ease: "easeOut" }}
      // Under prefers-reduced-motion nothing moves: these override the inline styles
      // motion writes, so the content is simply there. Done in CSS, not with a hook,
      // because the server cannot know the preference.
      className={cn(className, "motion-reduce:opacity-100! motion-reduce:transform-none!")}
      {...props}
    >
      {children}
    </motion.div>
  );
}
