"use client";

import { Search } from "lucide-react";
import Link from "next/link";
import { useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { type Chunk, find, loadIndex } from "@/lib/search";

type Load = "idle" | "loading" | "ready" | "unavailable";

/**
 * Docs search. The button opens a native modal <dialog> (focus trap, Escape and the
 * inert page behind it come from the platform). The index loads on the first open and
 * the query never leaves the browser. It grows from the button and shrinks back into
 * it by CSS transition (reveal, UX-32); under reduced motion it is an opacity fade.
 */
export function DocsSearch() {
  const dialog = useRef<HTMLDialogElement>(null);
  const opener = useRef<HTMLButtonElement>(null);
  const [load, setLoad] = useState<Load>("idle");
  const [index, setIndex] = useState<Chunk[]>([]);
  const [query, setQuery] = useState("");

  const open = () => {
    const el = dialog.current;
    const from = opener.current?.getBoundingClientRect();
    if (!el || el.open) return;
    el.showModal();
    if (from) {
      const box = el.getBoundingClientRect();
      const x = from.left + from.width / 2 - box.left;
      const y = from.top + from.height / 2 - box.top;
      el.style.transformOrigin = `${x}px ${y}px`;
    }
    if (load !== "idle") return;
    setLoad("loading");
    loadIndex().then(
      (chunks) => {
        setIndex(chunks);
        setLoad("ready");
      },
      (error: unknown) => {
        console.warn(error);
        setLoad("unavailable");
      },
    );
  };

  const close = () => {
    dialog.current?.close();
    opener.current?.focus();
  };

  const results = load === "ready" ? find(index, query) : [];
  const typed = query.trim();
  let status = "Type to search the docs";
  if (load === "loading") status = "Loading";
  else if (load === "unavailable") status = "Search is not available";
  else if (typed && results.length === 0) status = `No results for “${typed}”`;
  else if (typed) status = `Results: ${results.length}`;

  return (
    <>
      <Button
        ref={opener}
        size="sm"
        variant="ghost"
        aria-haspopup="dialog"
        onClick={open}
        className="min-w-11 px-3"
      >
        <Search aria-hidden className="size-4" />
        <span className="sr-only sm:not-sr-only">Search</span>
      </Button>

      <dialog
        ref={dialog}
        aria-label="Search the docs"
        onClose={() => opener.current?.focus()}
        className="bg-card text-foreground shadow-lift mx-auto mt-[10dvh] max-h-[80dvh] w-[min(36rem,calc(100vw-2rem))] scale-96 overflow-y-auto rounded-2xl p-4 opacity-0 transition-[opacity,scale,overlay,display] transition-discrete duration-(--duration-reveal) ease-(--ease-move) backdrop:bg-black/40 open:scale-100 open:opacity-100 starting:open:scale-96 starting:open:opacity-0 motion-reduce:scale-100 motion-reduce:duration-(--duration-fade) motion-reduce:open:scale-100 motion-reduce:starting:open:scale-100"
      >
        <label htmlFor="docs-search-input" className="label mb-2 block">
          Search the docs
        </label>
        <input
          id="docs-search-input"
          type="search"
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          placeholder={"Bluetooth pairing, gamepad, quality gate…"}
          autoComplete="off"
          spellCheck={false}
          className="border-line bg-background text-foreground focus-visible:outline-primary min-h-11 w-full rounded-xl border px-3 text-base"
        />
        <p role="status" className="text-dim mt-3 text-sm">
          {status}
        </p>
        {results.length > 0 ? (
          <ul className="mt-2">
            {results.map((hit) => (
              <li key={hit.u}>
                <Link
                  href={hit.u}
                  onClick={close}
                  className="hover:bg-faint block min-h-11 rounded-xl px-3 py-2"
                >
                  <span className="block font-bold">{hit.t}</span>
                  <span className="text-dim block text-sm">{hit.g}</span>
                </Link>
              </li>
            ))}
          </ul>
        ) : null}
        <Button size="sm" variant="ghost" onClick={close} className="mt-3">
          Close
        </Button>
      </dialog>
    </>
  );
}
