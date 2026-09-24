"use client";

import { useEffect } from "react";
import { ALL_NAV_IDS } from "@/lib/nav";

/**
 * Every documentation anchor — #protocol, #install, #architecture and the rest — used
 * to live at `/`. They live at `/docs` now, and a static export has no server to
 * redirect them, so the landing page forwards them itself.
 *
 * The allowlist is `ALL_NAV_IDS`, the same list the table of contents is built from,
 * so a link only leaves this page when it names a section that really is on the other
 * one. The landing page's own anchors are deliberately named nothing in that list.
 *
 * `location.replace` rather than the router: it leaves no history entry to bounce
 * back to, and the browser scrolls to the hash itself on the new document.
 */
export function LegacyHashRedirect() {
  useEffect(() => {
    const id = window.location.hash.slice(1);
    if (id && ALL_NAV_IDS.includes(id)) {
      window.location.replace(`/docs#${id}`);
    }
  }, []);

  return null;
}
