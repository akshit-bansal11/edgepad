import Link from "next/link";
import { REPO } from "@/lib/nav";

/** Shared by the landing page and the documentation page. */
export function SiteFooter() {
  return (
    <footer className="border-line mt-12 border-t">
      <div className="text-dim mx-auto flex max-w-[90rem] flex-col gap-3 px-4 py-8 text-sm sm:flex-row sm:items-center sm:justify-between md:px-6">
        <p>
          Edgepad is MIT licensed. This site is generated from the repository it
          documents.
        </p>
        <div className="flex flex-wrap gap-4">
          <Link href="/docs" className="hover:text-foreground">
            Documentation
          </Link>
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
  );
}
