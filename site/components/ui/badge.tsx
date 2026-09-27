import { Slot } from "@radix-ui/react-slot";
import { cva, type VariantProps } from "class-variance-authority";
import type { ComponentProps } from "react";
import { cn } from "@/lib/utils";

const badgeVariants = cva(
  "inline-flex w-fit shrink-0 items-center gap-1 rounded-full px-2.5 py-1 text-[0.8125rem] leading-none font-bold whitespace-nowrap",
  {
    variants: {
      // A pill, sentence case. Text on a tinted fill is ink or accent, never dim: dim on
      // faint is 4.35:1 in light and does not pass.
      variant: {
        default: "bg-primary text-primary-foreground",
        outline: "bg-faint text-foreground",
        solid: "bg-accent-soft text-primary",
      },
    },
    defaultVariants: { variant: "outline" },
  },
);

export function Badge({
  className,
  variant,
  asChild = false,
  ...props
}: ComponentProps<"span"> &
  VariantProps<typeof badgeVariants> & { asChild?: boolean }) {
  const Comp = asChild ? Slot : "span";
  return (
    <Comp
      data-slot="badge"
      className={cn(badgeVariants({ variant }), className)}
      {...props}
    />
  );
}

export { badgeVariants };
