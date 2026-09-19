import type { ReactNode } from "react";
import { Reveal } from "@/components/reveal";

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
    <section id={id} className="scroll-mt-20 py-12 first:pt-0 md:py-16">
      <Reveal>
        <header className="mb-6">
          {eyebrow ? <p className="label mb-3">{eyebrow}</p> : null}
          <h2 className="font-mono text-2xl leading-tight font-medium tracking-tight md:text-3xl">
            <a href={`#${id}`} className="hover:underline underline-offset-4">
              {title}
            </a>
          </h2>
          {lede ? (
            <div className="text-dim mt-3 max-w-[68ch] text-base leading-relaxed md:text-[1.0625rem]">
              {lede}
            </div>
          ) : null}
        </header>
      </Reveal>
      <div className="space-y-6">{children}</div>
    </section>
  );
}

/** A sub-heading inside a section. Not an anchor: the sidebar stops at sections. */
export function Sub({ children }: { children: ReactNode }) {
  return (
    <h3 className="font-mono text-base font-medium tracking-tight md:text-lg">
      {children}
    </h3>
  );
}

/** Body copy. Kept to ~68 characters so long documentation stays readable. */
export function P({ children }: { children: ReactNode }) {
  return (
    <p className="max-w-[68ch] text-[0.9375rem] leading-relaxed md:text-base">
      {children}
    </p>
  );
}

/** A quiet aside: a caveat, a rejected alternative, something not yet verified. */
export function Note({
  label = "Note",
  children,
}: {
  label?: string;
  children: ReactNode;
}) {
  return (
    <aside className="border-line bg-faint max-w-[72ch] border-l-2 px-4 py-3">
      <p className="label mb-1.5">{label}</p>
      <div className="text-dim text-[0.9375rem] leading-relaxed">{children}</div>
    </aside>
  );
}

/** Inline code. */
export function C({ children }: { children: ReactNode }) {
  return (
    <code className="bg-faint border-line text-foreground border px-1 py-0.5 font-mono text-[0.85em]">
      {children}
    </code>
  );
}
