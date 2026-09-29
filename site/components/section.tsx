import type { ReactNode } from "react";
import { BlurFade } from "@/components/ui/blur-fade";

/**
 * One documented section. The `id` is the anchor the sidebar and the scroll-spy use,
 * so it must match an entry in lib/nav.ts.
 */
export function Section({
  id,
  eyebrow,
  title,
  lede,
  children,
}: {
  id: string;
  eyebrow?: string;
  title: string;
  lede?: ReactNode;
  children: ReactNode;
}) {
  return (
    <section id={id} className="scroll-mt-20 py-12 first:pt-2 md:py-16">
      <BlurFade>
        <header className="mb-6">
          {eyebrow ? (
            <p className="text-primary mb-2 text-sm font-bold">{eyebrow}</p>
          ) : null}
          <h2 className="text-[1.75rem] leading-tight font-black tracking-[-0.01em] md:text-[2rem]">
            <a
              href={`#${id}`}
              className="decoration-primary rounded-sm underline-offset-4 hover:underline"
            >
              {title}
            </a>
          </h2>
          {lede ? (
            <div className="text-dim mt-3 max-w-[68ch] text-base leading-relaxed md:text-[1.0625rem]">
              {lede}
            </div>
          ) : null}
        </header>
      </BlurFade>
      <div className="space-y-6">{children}</div>
    </section>
  );
}

/** A sub-heading inside a section. Not an anchor: the sidebar stops at sections. */
export function Sub({ children }: { children: ReactNode }) {
  return <h3 className="pt-2 text-lg font-bold md:text-xl">{children}</h3>;
}

/** Body copy. Kept to ~68 characters so long documentation stays readable. */
export function P({ children }: { children: ReactNode }) {
  return <p className="max-w-[68ch] text-base leading-relaxed">{children}</p>;
}

/**
 * A quiet aside: a caveat, a rejected alternative, something not yet verified. A card
 * with an accent rule, so it reads as set apart without a second colour. Its text is
 * dim on the card (5.1:1), never on a tint.
 */
export function Note({
  label = "Note",
  children,
}: {
  label?: string;
  children: ReactNode;
}) {
  return (
    <aside className="bg-card shadow-card relative max-w-[72ch] overflow-hidden rounded-[var(--radius-card)] py-4 pr-5 pl-6">
      <span
        aria-hidden
        className="bg-primary absolute inset-y-3 left-2.5 w-1 rounded-full"
      />
      <p className="mb-1.5 text-[0.9375rem] font-bold">{label}</p>
      <div className="text-dim text-[0.9375rem] leading-relaxed">{children}</div>
    </aside>
  );
}

/** A link out of the site, in running text. Opens a new tab, and tells the new page nothing. */
export function ExternalLink({
  href,
  children,
}: {
  href: string;
  children: ReactNode;
}) {
  return (
    <a
      href={href}
      target="_blank"
      rel="noreferrer noopener"
      className="text-primary decoration-primary/40 hover:decoration-primary rounded-sm font-bold underline underline-offset-4"
    >
      {children}
    </a>
  );
}

/** Inline code. */
export function C({ children }: { children: ReactNode }) {
  return (
    <code className="bg-faint text-foreground rounded-md px-1.5 py-0.5 font-mono text-[0.85em] [overflow-wrap:anywhere]">
      {children}
    </code>
  );
}
