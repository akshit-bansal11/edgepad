"use client";

import { useEffect, useState } from "react";
import { ALL_NAV_IDS, NAV } from "@/lib/nav";
import { cn } from "@/lib/utils";

/**
 * Sidebar with scroll-spy. One IntersectionObserver over every section, with a top
 * margin that makes a section "current" once it reaches the upper third of the
 * viewport, so the highlight moves with reading rather than with the section's top
 * edge crossing zero.
 */
export function Toc() {
  const [active, setActive] = useState<string>(ALL_NAV_IDS[0] ?? "");

  useEffect(() => {
    const sections = ALL_NAV_IDS.map((id) => document.getElementById(id)).filter(
      (element): element is HTMLElement => element !== null,
    );
    if (sections.length === 0) return;

    const observer = new IntersectionObserver(
      (entries) => {
        const visible = entries
          .filter((entry) => entry.isIntersecting)
          .sort((a, b) => a.boundingClientRect.top - b.boundingClientRect.top);
        const first = visible[0];
        if (first) setActive(first.target.id);
      },
      { rootMargin: "-88px 0px -66% 0px", threshold: 0 },
    );

    for (const section of sections) observer.observe(section);
    return () => observer.disconnect();
  }, []);

  return (
    <nav aria-label="On this page" className="space-y-6">
      {NAV.map((group) => (
        <div key={group.label}>
          <p className="label mb-2">{group.label}</p>
          <ul className="border-line space-y-px border-l">
            {group.items.map((item) => {
              const current = active === item.id;
              return (
                <li key={item.id}>
                  <a
                    href={`#${item.id}`}
                    aria-current={current ? "location" : undefined}
                    className={cn(
                      "-ml-px block border-l py-1 pl-3 text-[0.8125rem] transition-colors",
                      current
                        ? "border-foreground text-foreground font-medium"
                        : "text-dim hover:text-foreground border-transparent",
                    )}
                  >
                    {item.label}
                  </a>
                </li>
              );
            })}
          </ul>
        </div>
      ))}
    </nav>
  );
}
