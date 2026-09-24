import { ChevronDown } from "lucide-react";
import Link from "next/link";
import { GithubMark } from "@/components/icons/github";
import { ThemeToggle } from "@/components/theme-toggle";
import { Button } from "@/components/ui/button";
import { LATEST_RELEASE, NAV, REPO } from "@/lib/nav";

/** The landing page's own three anchors. The docs page uses NAV instead. */
const LANDING_LINKS: { href: string; label: string }[] = [
  { href: "#features", label: "What it does" },
  { href: "#how", label: "How it works" },
  { href: "#get", label: "Get it" },
];

function MenuLink({
  href,
  external = false,
  children,
}: {
  href: string;
  external?: boolean;
  children: React.ReactNode;
}) {
  const className = "text-dim hover:text-foreground block py-1 text-sm";
  return (
    <li>
      {external ? (
        <a href={href} target="_blank" rel="noreferrer noopener" className={className}>
          {children}
        </a>
      ) : (
        <a href={href} className={className}>
          {children}
        </a>
      )}
    </li>
  );
}

/**
 * Sticky header, shared by both routes.
 *
 * The narrow-screen menu stays a plain <details>: no state, no portal, no JavaScript,
 * and it closes itself when a link inside it is followed. It carries the table of
 * contents on the documentation page and the landing page's own anchors on the
 * landing page, plus whichever buttons the width has dropped.
 */
export function SiteHeader({ variant = "landing" }: { variant?: "landing" | "docs" }) {
  const docs = variant === "docs";

  return (
    <header className="border-line bg-background/90 sticky top-0 z-40 border-b backdrop-blur">
      <div className="mx-auto flex h-16 max-w-[90rem] items-center gap-3 px-4 md:gap-4 md:px-6">
        <Link href="/" className="flex items-baseline gap-2">
          <span className="font-mono text-base font-medium tracking-tight">
            Edgepad
          </span>
          {docs ? <span className="label hidden sm:inline">Documentation</span> : null}
        </Link>

        <div className="flex-1" />

        {docs ? null : (
          <nav aria-label="Sections" className="hidden items-center gap-5 lg:flex">
            {LANDING_LINKS.map((link) => (
              <a
                key={link.href}
                href={link.href}
                className="text-dim hover:text-foreground font-mono text-xs tracking-[0.12em] uppercase"
              >
                {link.label}
              </a>
            ))}
          </nav>
        )}

        <Button
          asChild
          size="sm"
          variant={docs ? "ghost" : "outline"}
          className="hidden sm:inline-flex"
        >
          <Link href={docs ? "/" : "/docs"}>{docs ? "Home" : "Docs"}</Link>
        </Button>

        <ThemeToggle />

        <Button asChild size="sm" variant="outline" className="hidden md:inline-flex">
          <a href={REPO} target="_blank" rel="noreferrer noopener">
            <GithubMark className="size-4" />
            GitHub
          </a>
        </Button>

        <Button asChild size="sm" className="hidden lg:inline-flex">
          <a href={LATEST_RELEASE} target="_blank" rel="noreferrer noopener">
            Download
          </a>
        </Button>

        <details className="relative lg:hidden">
          <summary className="border-line text-dim hover:text-foreground grid h-9 cursor-pointer list-none items-center border px-3 font-mono text-xs tracking-[0.12em] uppercase [&::-webkit-details-marker]:hidden">
            <span className="flex items-center gap-1.5">
              {docs ? "Contents" : "Menu"}
              <ChevronDown aria-hidden className="size-3.5" />
            </span>
          </summary>
          <div className="border-line bg-background absolute right-0 mt-2 max-h-[70vh] w-[min(20rem,calc(100vw-2rem))] overflow-y-auto border p-4">
            {docs ? (
              NAV.map((group) => (
                <div key={group.label} className="mb-4 last:mb-0">
                  <p className="label mb-1.5">{group.label}</p>
                  <ul>
                    {group.items.map((item) => (
                      <MenuLink key={item.id} href={`#${item.id}`}>
                        {item.label}
                      </MenuLink>
                    ))}
                  </ul>
                </div>
              ))
            ) : (
              <div className="mb-4">
                <p className="label mb-1.5">This page</p>
                <ul>
                  {LANDING_LINKS.map((link) => (
                    <MenuLink key={link.href} href={link.href}>
                      {link.label}
                    </MenuLink>
                  ))}
                </ul>
              </div>
            )}

            <div>
              <p className="label mb-1.5">Elsewhere</p>
              <ul>
                <li>
                  <Link
                    href={docs ? "/" : "/docs"}
                    className="text-dim hover:text-foreground block py-1 text-sm"
                  >
                    {docs ? "Home" : "Documentation"}
                  </Link>
                </li>
                <MenuLink href={REPO} external>
                  GitHub
                </MenuLink>
                <MenuLink href={LATEST_RELEASE} external>
                  Download
                </MenuLink>
              </ul>
            </div>
          </div>
        </details>
      </div>
    </header>
  );
}
