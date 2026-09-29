import { Menu } from "lucide-react";
import Link from "next/link";
import { GithubMark } from "@/components/icons/github";
import { ThemeToggle } from "@/components/theme-toggle";
import { Button } from "@/components/ui/button";
import { LATEST_RELEASE, NAV, REPO } from "@/lib/nav";

/** The landing page's own anchors. The docs page uses NAV instead. */
const LANDING_LINKS: { href: string; label: string }[] = [
  { href: "#features", label: "What it does" },
  { href: "#how", label: "How it works" },
  { href: "#get", label: "Get it" },
];

const MENU_LINK =
  "text-foreground hover:bg-faint flex min-h-11 items-center rounded-xl px-3 text-[0.9375rem]";

function MenuLink({
  href,
  external = false,
  children,
}: {
  href: string;
  external?: boolean;
  children: React.ReactNode;
}) {
  return (
    <li>
      {external ? (
        <a href={href} target="_blank" rel="noreferrer noopener" className={MENU_LINK}>
          {children}
        </a>
      ) : (
        <a href={href} className={MENU_LINK}>
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
 * landing page, plus whichever buttons the width has dropped. Below `sm` its summary
 * is an icon alone, so the header fits a 360px screen with the theme toggle in it.
 */
export function SiteHeader({ variant = "landing" }: { variant?: "landing" | "docs" }) {
  const docs = variant === "docs";

  return (
    <>
      {/* The first thing a keyboard reaches: past the header and its menus, to the page's
          own <main id="main">. Hidden until focused. */}
      <a
        href="#main"
        className="bg-card text-foreground shadow-lift sr-only z-50 rounded-xl px-4 font-bold focus:not-sr-only focus:fixed focus:top-2 focus:left-2 focus:flex focus:min-h-11 focus:items-center"
      >
        Skip to content
      </a>
      <header className="border-line bg-background/80 sticky top-0 z-40 border-b backdrop-blur-xl">
        <div className="mx-auto flex h-16 max-w-[90rem] items-center gap-2 px-4 md:gap-3 md:px-6">
          <Link
            href="/"
            className="flex min-h-11 items-center gap-2 rounded-lg text-xl font-black tracking-[-0.01em]"
          >
            Edgepad
            {docs ? (
              <span className="text-dim hidden text-[0.9375rem] font-bold sm:inline">
                Docs
              </span>
            ) : null}
          </Link>

          <div className="flex-1" />

          {docs ? null : (
            <nav aria-label="Sections" className="hidden items-center gap-1 lg:flex">
              {LANDING_LINKS.map((link) => (
                <a
                  key={link.href}
                  href={link.href}
                  className="text-dim hover:text-foreground flex min-h-11 items-center rounded-full px-3 text-[0.9375rem] font-bold transition-colors"
                >
                  {link.label}
                </a>
              ))}
            </nav>
          )}

          <Button asChild size="sm" variant="ghost" className="hidden sm:inline-flex">
            <Link href={docs ? "/" : "/docs"}>{docs ? "Home" : "Docs"}</Link>
          </Button>

          <Button asChild size="sm" variant="ghost" className="hidden md:inline-flex">
            <a href={REPO} target="_blank" rel="noreferrer noopener">
              <GithubMark className="size-4" />
              GitHub
            </a>
          </Button>

          <ThemeToggle />

          <Button asChild size="sm" className="hidden lg:inline-flex">
            <a href={LATEST_RELEASE} target="_blank" rel="noreferrer noopener">
              Download
            </a>
          </Button>

          <details className="relative lg:hidden">
            <summary className="bg-faint text-foreground grid h-11 min-w-11 cursor-pointer list-none place-items-center rounded-full px-3 text-[0.9375rem] font-bold sm:px-4 [&::-webkit-details-marker]:hidden">
              <span className="flex items-center gap-1.5">
                <Menu aria-hidden className="size-4" />
                <span className="sr-only sm:not-sr-only">
                  {docs ? "Contents" : "Menu"}
                </span>
              </span>
            </summary>
            <div className="bg-card shadow-lift absolute right-0 mt-2 max-h-[75vh] w-[min(20rem,calc(100vw-2rem))] overflow-y-auto rounded-2xl p-2">
              {docs ? (
                NAV.map((group) => (
                  <div key={group.label} className="mb-2">
                    <p className="label px-3 pt-2 pb-1">{group.label}</p>
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
                <div className="mb-2">
                  <p className="label px-3 pt-2 pb-1">This page</p>
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
                <p className="label px-3 pt-2 pb-1">Elsewhere</p>
                <ul>
                  <li>
                    <Link href={docs ? "/" : "/docs"} className={MENU_LINK}>
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
    </>
  );
}
