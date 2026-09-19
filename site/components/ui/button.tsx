import { Slot } from "@radix-ui/react-slot";
import { cva, type VariantProps } from "class-variance-authority";
import type { ComponentProps } from "react";
import { cn } from "@/lib/utils";

const buttonVariants = cva(
  "inline-flex shrink-0 items-center justify-center gap-2 border font-mono text-xs tracking-[0.12em] uppercase transition-colors outline-none disabled:pointer-events-none disabled:opacity-50 [&_svg]:pointer-events-none [&_svg]:shrink-0",
  {
    variants: {
      variant: {
        default:
          "border-foreground bg-foreground text-background hover:bg-background hover:text-foreground",
        outline: "border-line bg-transparent text-foreground hover:border-foreground",
        ghost: "border-transparent bg-transparent text-dim hover:text-foreground",
      },
      size: {
        // 44px and 48px: both clear the 24x24 minimum and the 44px comfortable target.
        default: "h-12 px-5",
        sm: "h-11 px-4",
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
