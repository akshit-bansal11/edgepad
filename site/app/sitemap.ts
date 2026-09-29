import type { MetadataRoute } from "next";
import { SITE } from "@/lib/nav";

// A static export has no server to answer at request time, so the file is written at build.
export const dynamic = "force-static";

/** The two routes there are. No lastModified: a build date would claim a change on every deploy. */
export default function sitemap(): MetadataRoute.Sitemap {
  return [{ url: SITE }, { url: `${SITE}/docs` }];
}
