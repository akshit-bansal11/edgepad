"use client";

import { useRef } from "react";

/** Chapter starts in seconds, from the narration timings the demo was rendered with. */
const CHAPTERS: { at: number; title: string }[] = [
  { at: 0, title: "What it is" },
  { at: 28, title: "Setup" },
  { at: 83, title: "The surface" },
  { at: 97, title: "The trackpad" },
  { at: 146, title: "Corner dials" },
  { at: 205, title: "Media" },
  { at: 223, title: "The top row and locking" },
  { at: 254, title: "The keyboard" },
  { at: 275, title: "Macros" },
  { at: 310, title: "Shapes" },
  { at: 330, title: "Make it yours" },
  { at: 358, title: "The gamepad (in development)" },
  { at: 387, title: "Under the hood" },
  { at: 432, title: "Open source" },
];

const clock = (s: number) => `${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")}`;

/**
 * The full 7:28 walkthrough. The picture has captions burned in; the text track is
 * the same narration for screen readers and caption styling, off until chosen.
 * `preload="none"` keeps the 25 MB file off the wire until someone presses play.
 */
export function DemoVideo() {
  const video = useRef<HTMLVideoElement>(null);

  const seek = (at: number) => {
    const el = video.current;
    if (!el) return;
    el.currentTime = at;
    void el.play().catch(() => {});
    el.scrollIntoView({ behavior: "smooth", block: "center" });
  };

  return (
    <div className="grid gap-8 lg:grid-cols-[minmax(0,1fr)_16rem]">
      <video
        ref={video}
        className="border-line aspect-video w-full border bg-black"
        src="/edgepad-demo.mp4"
        poster="/edgepad-demo-poster.jpg"
        controls
        preload="none"
        playsInline
      >
        <track
          kind="captions"
          src="/edgepad-demo.en.vtt"
          srcLang="en"
          label="English"
        />
        <a href="/edgepad-demo.mp4">Download the demo video</a>
      </video>

      <nav aria-label="Demo chapters">
        <p className="label mb-3">Chapters</p>
        <ol className="divide-line border-line divide-y border-t border-b">
          {CHAPTERS.map((chapter) => (
            <li key={chapter.at}>
              <button
                type="button"
                onClick={() => seek(chapter.at)}
                className="text-dim hover:text-foreground flex min-h-11 w-full items-center gap-3 py-2 text-left text-sm"
              >
                <span className="font-mono text-xs tabular-nums">
                  {clock(chapter.at)}
                </span>
                {chapter.title}
              </button>
            </li>
          ))}
        </ol>
      </nav>
    </div>
  );
}
