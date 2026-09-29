import type { MetadataRoute } from "next";
import { SITE } from "@/lib/nav";

// A static export has no server to answer at request time, so the file is written at build.
export const dynamic = "force-static";

export default function robots(): MetadataRoute.Robots {
  return {
    rules: { userAgent: "*", allow: "/" },
    sitemap: `${SITE}/sitemap.xml`,
  };
}
