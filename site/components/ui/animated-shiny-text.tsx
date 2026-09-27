import type { ComponentPropsWithoutRef, CSSProperties, FC } from "react";

import { cn } from "@/lib/utils";

export interface AnimatedShinyTextProps extends ComponentPropsWithoutRef<"span"> {
  shimmerWidth?: number;
}

export const AnimatedShinyText: FC<AnimatedShinyTextProps> = ({
  children,
  className,
  shimmerWidth = 100,
  ...props
}) => {
  return (
    <span
      style={
        {
          "--shiny-width": `${shimmerWidth}px`,
        } as CSSProperties
      }
      className={cn(
        // Ink at 75% on the pill clears 4.5:1 in both themes; the shine brings it to full ink.
        "text-foreground/75",

        // Shine effect
        "animate-shiny-text motion-reduce:animate-none motion-reduce:bg-none bg-size-[var(--shiny-width)_100%] bg-clip-text bg-position-[0_0] bg-no-repeat [transition:background-position_1s_cubic-bezier(.6,.6,0,1)_infinite]",

        // Shine gradient
        "via-foreground bg-linear-to-r from-transparent via-50% to-transparent",

        className,
      )}
      {...props}
    >
      {children}
    </span>
  );
};
