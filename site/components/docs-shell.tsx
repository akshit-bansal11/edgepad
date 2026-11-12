import type { ReactNode } from "react";
import { SiteFooter } from "@/components/site-footer";
import { SiteHeader } from "@/components/site-header";
import { Toc } from "@/components/toc";
import type { NavGroup } from "@/lib/nav";

/** The frame both documentation routes share: header, title block, sidebar, footer. */
export function DocsShell({
  groups,
  eyebrow,
  title,
  intro,
  children,
}: {
  groups: NavGroup[];
  eyebrow: string;
  title: string;
  intro: ReactNode;
  children: ReactNode;
}) {
  return (
    <div id="top" className="min-h-dvh">
      <SiteHeader variant="docs" groups={groups} />

      <main id="main" className="mx-auto scroll-mt-16 max-w-[90rem] px-4 md:px-6">
        {/* No entrance on the title block: it is the first paint, and a fade would
            hide it until the JavaScript arrives. */}
        <div className="py-10 md:py-16">
          <p className="text-primary mb-2 text-[0.9375rem] font-bold">{eyebrow}</p>
          <h1 className="max-w-[20ch] text-[2.5rem] leading-[1.04] font-black tracking-[-0.025em] md:text-6xl">
            {title}
          </h1>
          <p className="text-dim mt-5 max-w-[64ch] text-base leading-relaxed md:text-lg">
            {intro}
          </p>
        </div>

        {/* Sidebar + body */}
        <div className="lg:grid lg:grid-cols-[17rem_minmax(0,1fr)] lg:gap-12">
          <aside className="hidden lg:block">
            <div className="bg-card shadow-card sticky top-20 max-h-[calc(100dvh-6rem)] overflow-y-auto rounded-[var(--radius-panel)] p-3 pt-4">
              <Toc groups={groups} />
            </div>
          </aside>

          <div className="divide-line min-w-0 divide-y">{children}</div>
        </div>
      </main>

      <SiteFooter />
    </div>
  );
}
