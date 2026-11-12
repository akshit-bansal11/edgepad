import Link from "next/link";
import { SiteFooter } from "@/components/site-footer";
import { SiteHeader } from "@/components/site-header";
import { Button } from "@/components/ui/button";

export default function NotFound() {
  return (
    <div className="min-h-dvh">
      <SiteHeader />
      <main id="main" className="mx-auto max-w-[80rem] px-4 py-20 md:px-6">
        <p className="text-primary mb-2 text-[0.9375rem] font-bold">404</p>
        <h1 className="max-w-[20ch] text-[2.5rem] leading-[1.04] font-black tracking-[-0.025em] md:text-6xl">
          That page is not here.
        </h1>
        <p className="text-dim mt-5 max-w-[56ch] text-base leading-relaxed md:text-lg">
          The address may be mistyped or out of date. These are the places that exist.
        </p>
        <div className="mt-8 flex flex-wrap items-center gap-3">
          <Button asChild>
            <Link href="/">Home</Link>
          </Button>
          <Button asChild variant="outline">
            <Link href="/docs">Documentation</Link>
          </Button>
          <Button asChild variant="outline">
            <Link href="/#get">Download</Link>
          </Button>
        </div>
      </main>
      <SiteFooter />
    </div>
  );
}
