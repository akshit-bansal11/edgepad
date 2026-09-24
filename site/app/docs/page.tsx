import type { Metadata } from "next";
import { Reveal } from "@/components/reveal";
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
        <Reveal>
          <div className="border-line border-b py-10 md:py-14">
            <p className="label mb-3">Documentation</p>
            <h1 className="max-w-[24ch] font-mono text-3xl leading-[1.1] font-medium tracking-tight md:text-5xl">
              Everything Edgepad does, and how.
            </h1>
            <p className="text-dim mt-5 max-w-[68ch] text-base leading-relaxed md:text-lg">
              Install, the control surface, both apps&apos; architecture, the wire
              protocol and the developer guide. The protocol tables below are generated
              from the same fixtures both test suites read, so this page cannot drift
              from the two apps.
            </p>
          </div>
        </Reveal>

        {/* Sidebar + body */}
        <div className="lg:grid lg:grid-cols-[16rem_minmax(0,1fr)] lg:gap-12">
          <aside className="hidden lg:block">
            <div className="sticky top-20 max-h-[calc(100dvh-6rem)] overflow-y-auto pt-4 pr-4 pb-12">
              <Toc />
            </div>
          </aside>

          <div className="divide-line min-w-0 divide-y pt-4">
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
