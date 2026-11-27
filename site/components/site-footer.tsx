import Link from "next/link";
import { REPO } from "@/lib/nav";

const LINK =
  "press text-dim hover:text-foreground flex min-h-11 items-center rounded-lg transition-colors";

const FEATURED = [
  { name: "Aura++", href: "https://auraplusplus.com/projects/edgepad" },
  {
    name: "ScrollLaunch",
    href: "https://www.scrolllaunch.com/products/edgepad?ref=badge",
  },
];

/** Shared by the landing page and the documentation page. */
export function SiteFooter() {
  return (
    <footer className="border-line mt-16 border-t">
      <div className="mx-auto flex max-w-[90rem] flex-col gap-4 px-4 py-8 text-[0.9375rem] md:flex-row md:items-center md:justify-between md:px-6">
        <div>
          <p className="text-lg font-black">Edgepad</p>
          <p className="text-dim mt-1">
            MIT licensed. This site is generated from the repository it documents.
          </p>
        </div>
        <div className="flex flex-wrap gap-x-5">
          <Link href="/docs" className={LINK}>
            Documentation
          </Link>
          <a href={REPO} target="_blank" rel="noreferrer noopener" className={LINK}>
            GitHub
          </a>
          <a
            href={`${REPO}/blob/main/CHANGELOG.md`}
            target="_blank"
            rel="noreferrer noopener"
            className={LINK}
          >
            Changelog
          </a>
          <a
            href={`${REPO}/blob/main/docs/PROTOCOL.md`}
            target="_blank"
            rel="noreferrer noopener"
            className={LINK}
          >
            Protocol
          </a>
          <a href="#top" className={LINK}>
            Back to top
          </a>
        </div>
      </div>
      {/* Launch directories look for a link back in this page's HTML. */}
      <div className="text-dim mx-auto flex max-w-[90rem] flex-wrap items-center gap-x-5 px-4 pb-8 text-[0.9375rem] md:px-6">
        <span>Featured on</span>
        {FEATURED.map(({ name, href }) => (
          <a
            key={name}
            href={href}
            target="_blank"
            rel="noreferrer noopener"
            className={LINK}
          >
            {name}
          </a>
        ))}
      </div>
    </footer>
  );
}
