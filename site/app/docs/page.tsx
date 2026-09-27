import type { Metadata } from "next";
import { DevelopingSections } from "@/components/sections/developing";
import { InternalsSections } from "@/components/sections/internals";
import { ProductSections } from "@/components/sections/product";
import { ReferenceSections } from "@/components/sections/reference";
import { SiteFooter } from "@/components/site-footer";
import { SiteHeader } from "@/components/site-header";
import { Toc } from "@/components/toc";
import { loadProtocol } from "@/lib/protocol";

export const metadata: Metadata = {
  title: "Edgepad — documentation",
  description:
    "Full Edgepad documentation: install, the control surface, both apps' architecture, the wire protocol, and the developer guide, on one page.",
};

export default function DocsPage() {
  // Read at build time from ../protocol/*.txt — the same two fixture files both test
  // suites read. If they cannot be parsed, this throws and the build fails, rather
  // than rendering a protocol page with no protocol on it. The path is resolved from
  // the working directory, not from the route, so it is unaffected by living at /docs.
  const protocol = loadProtocol();

  return (
    <div id="top" className="min-h-dvh">
      <SiteHeader variant="docs" />

      <main className="mx-auto max-w-[90rem] px-4 md:px-6">
        {/* No entrance on the title block: it is the first paint, and a fade would
            hide it until the JavaScript arrives. */}
        <div className="py-10 md:py-16">
          <p className="text-primary mb-2 text-[0.9375rem] font-bold">Documentation</p>
          <h1 className="max-w-[20ch] text-[2.5rem] leading-[1.04] font-black tracking-[-0.025em] md:text-6xl">
            Everything Edgepad does, and how.
          </h1>
          <p className="text-dim mt-5 max-w-[64ch] text-base leading-relaxed md:text-lg">
            Install, the control surface, both apps&apos; architecture, the wire
            protocol and the developer guide. The protocol tables below are generated
            from the same fixtures both test suites read, so this page cannot drift from
            the two apps.
          </p>
        </div>

        {/* Sidebar + body */}
        <div className="lg:grid lg:grid-cols-[17rem_minmax(0,1fr)] lg:gap-12">
          <aside className="hidden lg:block">
            <div className="bg-card shadow-card sticky top-20 max-h-[calc(100dvh-6rem)] overflow-y-auto rounded-[var(--radius-panel)] p-3 pt-4">
              <Toc />
            </div>
          </aside>

          <div className="divide-line min-w-0 divide-y">
            <ProductSections />
            <InternalsSections protocol={protocol} />
            <DevelopingSections protocolVersion={protocol.version} />
            <ReferenceSections />
          </div>
        </div>
      </main>

      <SiteFooter />
    </div>
  );
}
