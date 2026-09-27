import { Slot } from "@radix-ui/react-slot";
import { cva, type VariantProps } from "class-variance-authority";
import type { ComponentProps } from "react";
import { cn } from "@/lib/utils";

const buttonVariants = cva(
  "inline-flex shrink-0 items-center justify-center gap-2 rounded-[var(--radius-card)] font-bold whitespace-nowrap transition-[background-color,color,opacity,filter] disabled:pointer-events-none disabled:opacity-35 [&_svg]:pointer-events-none [&_svg]:shrink-0",
  {
    variants: {
      // The design system's three buttons: filled for the one main action, tinted for
      // a strong second, plain for a way out. Names kept from shadcn so call sites
      // read the same.
      variant: {
        default: "bg-primary text-primary-foreground hover:brightness-110",
        outline: "bg-secondary text-secondary-foreground hover:bg-secondary/70",
        ghost: "text-foreground hover:bg-faint",
      },
      size: {
        // 44px and 48px: both clear the 24x24 minimum and the 44px comfortable target.
        default: "h-12 px-5 text-base",
        sm: "h-11 px-4 text-[0.9375rem]",
        icon: "size-11",
      },
    },
    defaultVariants: { variant: "default", size: "default" },
  },
);

export function Button({
  className,
  variant,
  size,
  asChild = false,
  ...props
}: ComponentProps<"button"> &
  VariantProps<typeof buttonVariants> & { asChild?: boolean }) {
  const Comp = asChild ? Slot : "button";
  return (
    <Comp
      data-slot="button"
      className={cn(buttonVariants({ variant, size }), className)}
      {...props}
    />
  );
}

export { buttonVariants };
