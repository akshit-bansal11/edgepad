import { ArrowDown } from "lucide-react";
import { GithubMark } from "@/components/icons/github";
import { Reveal } from "@/components/reveal";
import { DevelopingSections } from "@/components/sections/developing";
import { InternalsSections } from "@/components/sections/internals";
import { ProductSections } from "@/components/sections/product";
import { ReferenceSections } from "@/components/sections/reference";
import { SiteHeader } from "@/components/site-header";
import { Toc } from "@/components/toc";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { LATEST_RELEASE, REPO } from "@/lib/nav";
import { loadProtocol } from "@/lib/protocol";

export default function Page() {
  // Read at build time from ../protocol/*.txt — the same two fixture files both test
  // suites read. If they cannot be parsed, this throws and the build fails, rather
  // than rendering a protocol page with no protocol on it.
  const protocol = loadProtocol();

  return (
    <div id="top" className="min-h-dvh">
      <SiteHeader />

      <main className="mx-auto max-w-[90rem] px-4 md:px-6">
        {/* Hero */}
        <Reveal>
          <section className="border-line border-b py-16 md:py-24">
            <p className="label mb-4">Documentation</p>
            <h1 className="max-w-[20ch] font-mono text-4xl leading-[1.05] font-medium tracking-tight md:text-6xl">
              Your laptop, from across the room.
            </h1>
            <p className="text-dim mt-6 max-w-[62ch] text-lg leading-relaxed">
              Edgepad turns an Android phone into a trackpad, a media remote and a
              control panel for a Windows laptop, over a direct Bluetooth link with
              nothing in between. No account, no Wi-Fi, no server.
            </p>

            <div className="mt-8 flex flex-wrap items-center gap-3">
              <Button asChild>
                <a href={LATEST_RELEASE} target="_blank" rel="noreferrer noopener">
                  Download both halves
                </a>
              </Button>
              <Button asChild variant="outline">
                <a href="#split">
                  <ArrowDown aria-hidden className="size-4" />
                  How it works
                </a>
              </Button>
              <Button asChild variant="ghost">
                <a href={REPO} target="_blank" rel="noreferrer noopener">
                  <GithubMark className="size-4" />
                  Source
                </a>
              </Button>
            </div>

            <dl className="border-line mt-12 grid gap-px border-t pt-6 sm:grid-cols-4">
              {[
                { term: "Transport", value: "Bluetooth Classic RFCOMM" },
                { term: "Protocol", value: `Version ${protocol.version}` },
                { term: "Phone", value: "Kotlin, Android 12+" },
                { term: "Laptop", value: "C# on .NET 10, Windows 10 2004+" },
              ].map((item) => (
                <div key={item.term}>
                  <dt className="label">{item.term}</dt>
                  <dd className="mt-1 font-mono text-sm">{item.value}</dd>
                </div>
              ))}
            </dl>

            <div className="mt-8 flex flex-wrap gap-2">
              <Badge variant="outline">No account</Badge>
              <Badge variant="outline">No network</Badge>
              <Badge variant="outline">No telemetry</Badge>
              <Badge variant="outline">MIT</Badge>
            </div>
          </section>
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

      <footer className="border-line mt-12 border-t">
        <div className="text-dim mx-auto flex max-w-[90rem] flex-col gap-3 px-4 py-8 text-sm sm:flex-row sm:items-center sm:justify-between md:px-6">
          <p>
            Edgepad is MIT licensed. This page is generated from the repository it
            documents.
          </p>
          <div className="flex gap-4">
            <a
              href={REPO}
              target="_blank"
              rel="noreferrer noopener"
              className="hover:text-foreground"
            >
              GitHub
            </a>
            <a
              href={`${REPO}/blob/main/CHANGELOG.md`}
              target="_blank"
              rel="noreferrer noopener"
              className="hover:text-foreground"
            >
              Changelog
            </a>
            <a
              href={`${REPO}/blob/main/docs/PROTOCOL.md`}
              target="_blank"
              rel="noreferrer noopener"
              className="hover:text-foreground"
            >
              Protocol
            </a>
            <a href="#top" className="hover:text-foreground">
              Back to top
            </a>
          </div>
        </div>
      </footer>
    </div>
  );
}
