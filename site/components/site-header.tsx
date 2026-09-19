import { ChevronDown } from "lucide-react";
import { GithubMark } from "@/components/icons/github";
import { ThemeToggle } from "@/components/theme-toggle";
import { Button } from "@/components/ui/button";
import { LATEST_RELEASE, NAV, REPO } from "@/lib/nav";

/**
 * Sticky header. The narrow-screen menu is a plain <details>, which needs no state,
 * no portal and no JavaScript, and closes itself when a link inside it is followed.
 */
export function SiteHeader() {
  return (
    <header className="border-line bg-background/90 sticky top-0 z-40 border-b backdrop-blur">
      <div className="mx-auto flex h-16 max-w-[90rem] items-center gap-4 px-4 md:px-6">
        <a href="#top" className="flex items-baseline gap-2">
          <span className="font-mono text-base font-medium tracking-tight">
            Edgepad
          </span>
          <span className="label hidden sm:inline">Documentation</span>
        </a>

        <div className="flex-1" />

        <ThemeToggle />

        <Button asChild size="sm" variant="outline" className="hidden sm:inline-flex">
          <a href={REPO} target="_blank" rel="noreferrer noopener">
            <GithubMark className="size-4" />
            GitHub
          </a>
        </Button>

        <Button asChild size="sm" className="hidden md:inline-flex">
          <a href={LATEST_RELEASE} target="_blank" rel="noreferrer noopener">
            Download
          </a>
        </Button>

        <details className="relative lg:hidden">
          <summary className="border-line text-dim hover:text-foreground grid h-9 cursor-pointer list-none items-center border px-3 font-mono text-xs tracking-[0.12em] uppercase [&::-webkit-details-marker]:hidden">
            <span className="flex items-center gap-1.5">
              Contents
              <ChevronDown aria-hidden className="size-3.5" />
            </span>
          </summary>
          <div className="border-line bg-background absolute right-0 mt-2 max-h-[70vh] w-[min(20rem,calc(100vw-2rem))] overflow-y-auto border p-4">
            {NAV.map((group) => (
              <div key={group.label} className="mb-4 last:mb-0">
                <p className="label mb-1.5">{group.label}</p>
                <ul>
                  {group.items.map((item) => (
                    <li key={item.id}>
                      <a
                        href={`#${item.id}`}
                        className="text-dim hover:text-foreground block py-1 text-sm"
                      >
                        {item.label}
                      </a>
                    </li>
                  ))}
                </ul>
              </div>
            ))}
          </div>
        </details>
      </div>
    </header>
  );
}
