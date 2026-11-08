import type { Metadata } from "next";
import { DocsShell } from "@/components/docs-shell";
import { DevelopingSections } from "@/components/sections/developing";
import { InternalsSections } from "@/components/sections/internals";
import { DEV_NAV, SOCIAL_IMAGE } from "@/lib/nav";
import { loadProtocol } from "@/lib/protocol";

const TITLE = "Edgepad — developer documentation";
const DESCRIPTION =
  "Edgepad for developers: the architecture of all four apps, the wire protocol, the quality gate, running it locally and releases.";

export const metadata: Metadata = {
  title: TITLE,
  description: DESCRIPTION,
  alternates: { canonical: "/docs/developers" },
  openGraph: {
    title: TITLE,
    description: DESCRIPTION,
    type: "website",
    url: "/docs/developers",
    siteName: "Edgepad",
    images: [SOCIAL_IMAGE],
  },
};

export default function DevelopersPage() {
  // Read at build time from ../protocol/*.txt — the same two fixture files both test
  // suites read. If they cannot be parsed, this throws and the build fails, rather
  // than rendering a protocol page with no protocol on it.
  const protocol = loadProtocol();

  return (
    <DocsShell
      groups={DEV_NAV}
      eyebrow="Developer documentation"
      title="How Edgepad works, and how to change it."
      intro={
        <>
          The architecture, the wire protocol and the developer guide. The protocol
          tables below are generated from the same fixtures every test suite reads, so
          this page cannot drift from the apps. Just want to use Edgepad? See the{" "}
          <a
            href="/docs"
            className="text-primary decoration-primary/40 hover:decoration-primary rounded-sm font-bold underline underline-offset-4"
          >
            user guide
          </a>
          .
        </>
      }
    >
      <InternalsSections protocol={protocol} />
      <DevelopingSections protocolVersion={protocol.version} />
    </DocsShell>
  );
}
