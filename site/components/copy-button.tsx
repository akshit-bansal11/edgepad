"use client";

import { Check, CircleAlert, Copy } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useEffect, useState } from "react";
import { seconds } from "@/lib/motion";

type CopyState = "idle" | "copied" | "failed";

const MESSAGE: Record<CopyState, string> = {
  idle: "",
  copied: "Copied",
  failed: "Copy failed. Select the text instead.",
};

export function CopyButton({ text, label }: { text: string; label: string }) {
  const [state, setState] = useState<CopyState>("idle");

  useEffect(() => {
    if (state === "idle") return;
    const timer = window.setTimeout(
      () => setState("idle"),
      state === "failed" ? 4000 : 1500,
    );
    return () => window.clearTimeout(timer);
  }, [state]);

  async function copy() {
    // Clipboard access throws on an insecure origin and where the user has denied it.
    // That is shown, not swallowed: an error icon and a spoken message.
    try {
      await navigator.clipboard.writeText(text);
      setState("copied");
    } catch {
      setState("failed");
    }
  }

  return (
    <>
      <button
        type="button"
        onClick={copy}
        aria-label={`Copy ${label}`}
        title={state === "failed" ? MESSAGE.failed : undefined}
        className="press text-dim hover:text-foreground hover:bg-faint grid size-11 place-items-center rounded-xl transition-colors"
      >
        <AnimatePresence mode="wait" initial={false}>
          <motion.span
            key={state}
            initial={{ opacity: 0, scale: 0.8 }}
            animate={{ opacity: 1, scale: 1 }}
            exit={{ opacity: 0, scale: 0.8 }}
            transition={{ duration: seconds("fast") }}
            className="grid place-items-center"
          >
            {state === "copied" ? (
              <Check aria-hidden className="text-primary size-4" />
            ) : state === "failed" ? (
              <CircleAlert aria-hidden className="text-destructive size-4" />
            ) : (
              <Copy aria-hidden className="size-4" />
            )}
          </motion.span>
        </AnimatePresence>
      </button>
      {/* The result for assistive tech: the icon swap alone says nothing. */}
      <span role="status" className="sr-only">
        {MESSAGE[state]}
      </span>
    </>
  );
}
