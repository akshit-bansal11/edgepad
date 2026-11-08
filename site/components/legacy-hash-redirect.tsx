"use client";

import { useEffect } from "react";
import { DEV_NAV_IDS, DOCS_NAV_IDS } from "@/lib/nav";

/**
 * Every documentation anchor — #protocol, #install, #architecture and the rest — used
 * to live at `/`, then all at `/docs`. They now live at `/docs` (the user guide) or
 * `/docs/developers`, and a static export has no server to redirect them, so the pages
 * forward them themselves.
 *
 * The allowlists are the same lists the tables of contents are built from, so a link
 * only leaves a page when it names a section that really is on the other one. The
 * landing page's own anchors are deliberately named nothing in those lists.
 *
 * `location.replace` rather than the router: it leaves no history entry to bounce
 * back to, and the browser scrolls to the hash itself on the new document.
 */
export function LegacyHashRedirect({ from }: { from: "landing" | "docs" }) {
  useEffect(() => {
    const id = window.location.hash.slice(1);
    if (!id) return;
    if (DEV_NAV_IDS.includes(id)) {
      window.location.replace(`/docs/developers#${id}`);
    } else if (from === "landing" && DOCS_NAV_IDS.includes(id)) {
      window.location.replace(`/docs#${id}`);
    }
  }, [from]);

  return null;
}
