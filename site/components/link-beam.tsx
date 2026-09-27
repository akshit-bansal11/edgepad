"use client";

import { Bluetooth, Laptop, type LucideIcon, Smartphone } from "lucide-react";
import { type RefObject, useRef } from "react";
import { AnimatedBeam } from "@/components/ui/animated-beam";

function Node({
  ref,
  Icon,
  title,
  detail,
  large = false,
}: {
  ref: RefObject<HTMLDivElement | null>;
  Icon: LucideIcon;
  title: string;
  detail: string;
  large?: boolean;
}) {
  return (
    <div className="flex w-24 flex-col items-center text-center sm:w-36">
      {/* Every node sits in a box of the same height, so all three centres share a
          line and the straight beams are level. */}
      <div className="flex h-16 items-center sm:h-20">
        <div
          ref={ref}
          className={
            large
              ? "bg-faint relative z-10 grid size-16 place-items-center rounded-2xl sm:size-20"
              : "bg-accent-soft text-primary relative z-10 grid size-12 place-items-center rounded-full sm:size-14"
          }
        >
          <Icon
            aria-hidden
            className={large ? "size-7 sm:size-8" : "size-5 sm:size-6"}
          />
        </div>
      </div>
      <p className="mt-3 text-[0.9375rem] font-bold">{title}</p>
      <p className="text-dim mt-0.5 text-sm leading-snug">{detail}</p>
    </div>
  );
}

/**
 * The one seam, drawn: frames travel from the phone over the Bluetooth link to the
 * laptop, and the laptop's state comes back the other way over the top. The light that
 * travels along each path is Magic UI's AnimatedBeam; under prefers-reduced-motion
 * only the resting paths are drawn.
 */
export function LinkBeam() {
  const container = useRef<HTMLDivElement>(null);
  const phone = useRef<HTMLDivElement>(null);
  const link = useRef<HTMLDivElement>(null);
  const laptop = useRef<HTMLDivElement>(null);

  const beam = {
    containerRef: container,
    pathColor: "var(--dim)",
    pathOpacity: 0.35,
    pathWidth: 3,
    gradientStartColor: "var(--blue)",
    gradientStopColor: "var(--blue)",
    duration: 3.5,
  };

  return (
    <figure className="bg-card shadow-card rounded-[var(--radius-panel)] pb-6">
      <div
        ref={container}
        className="relative flex items-start justify-between px-2 pt-16 pb-6 sm:px-10 sm:pt-20"
      >
        <Node
          ref={phone}
          Icon={Smartphone}
          title="Phone"
          detail="Recognises the touch"
          large
        />
        <Node
          ref={link}
          Icon={Bluetooth}
          title="RFCOMM"
          detail="Paired once, no network"
        />
        <Node
          ref={laptop}
          Icon={Laptop}
          title="Laptop"
          detail="Runs the action"
          large
        />

        <AnimatedBeam {...beam} fromRef={phone} toRef={link} />
        <AnimatedBeam {...beam} fromRef={link} toRef={laptop} delay={0.6} />
        <AnimatedBeam
          {...beam}
          fromRef={laptop}
          toRef={phone}
          curvature={100}
          reverse
          delay={1.4}
        />
      </div>
      <figcaption className="text-dim px-2 text-center text-sm leading-relaxed">
        Frames go to the laptop. Volume, mute, brightness and what is playing come back.
      </figcaption>
    </figure>
  );
}
