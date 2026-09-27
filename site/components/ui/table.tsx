import type { ComponentProps } from "react";
import { cn } from "@/lib/utils";

/**
 * A table is the one thing allowed to be wider than the page, so each one carries its
 * own horizontal scroll container rather than letting the body scroll sideways.
 */
export function Table({ className, ...props }: ComponentProps<"table">) {
  return (
    <div className="bg-card shadow-card w-full overflow-x-auto rounded-[var(--radius-card)]">
      <table
        data-slot="table"
        className={cn("w-full caption-bottom text-sm", className)}
        {...props}
      />
    </div>
  );
}

export function TableHeader({ className, ...props }: ComponentProps<"thead">) {
  return <thead className={cn("[&_tr]:border-b", className)} {...props} />;
}

export function TableBody({ className, ...props }: ComponentProps<"tbody">) {
  return <tbody className={cn("[&_tr:last-child]:border-0", className)} {...props} />;
}

export function TableRow({ className, ...props }: ComponentProps<"tr">) {
  return <tr className={cn("border-line border-b", className)} {...props} />;
}

export function TableHead({ className, ...props }: ComponentProps<"th">) {
  return (
    <th
      className={cn(
        "text-dim h-11 px-4 text-left align-middle text-[0.8125rem] font-bold whitespace-nowrap",
        className,
      )}
      {...props}
    />
  );
}

export function TableCell({ className, ...props }: ComponentProps<"td">) {
  return <td className={cn("px-4 py-3 align-top", className)} {...props} />;
}

export function TableCaption({ className, ...props }: ComponentProps<"caption">) {
  return (
    <caption className={cn("text-dim px-4 pt-1 pb-4 text-sm", className)} {...props} />
  );
}
