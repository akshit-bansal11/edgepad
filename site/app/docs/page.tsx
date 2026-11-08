import type { Metadata } from "next";
import { DocsShell } from "@/components/docs-shell";
import { LegacyHashRedirect } from "@/components/legacy-hash-redirect";
import { ProductSections } from "@/components/sections/product";
import { ReferenceSections } from "@/components/sections/reference";
import { DOCS_NAV, SOCIAL_IMAGE } from "@/lib/nav";

const TITLE = "Edgepad — documentation";
const DESCRIPTION =
  "The Edgepad user guide: install on Windows, Mac or Linux, pair your phone, use the control surface, settings, troubleshooting and known limits.";

// A page's openGraph replaces the layout's whole object rather than merging into it,
// hence type, site name and image again here.
export const metadata: Metadata = {
  title: TITLE,
  description: DESCRIPTION,
  alternates: { canonical: "/docs" },
  openGraph: {
    title: TITLE,
    description: DESCRIPTION,
    type: "website",
    url: "/docs",
    siteName: "Edgepad",
    images: [SOCIAL_IMAGE],
  },
};

export default function DocsPage() {
  return (
    <DocsShell
      groups={DOCS_NAV}
      eyebrow="Documentation"
      title="Everything Edgepad does, and how."
      intro={
        <>
          Install, pair, use the control surface and fix what goes wrong. Looking for the
          wire protocol, the architecture or the quality gate? That is the{" "}
          <a
            href="/docs/developers"
            className="text-primary decoration-primary/40 hover:decoration-primary rounded-sm font-bold underline underline-offset-4"
          >
            developer documentation
          </a>
          .
        </>
      }
    >
      <LegacyHashRedirect from="docs" />
      <ProductSections />
      <ReferenceSections />
    </DocsShell>
  );
}
