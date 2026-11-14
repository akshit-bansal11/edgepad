"use client";

import { useRef, useState } from "react";

/** Chapter starts in seconds, from the narration timings the demo was rendered with. */
const CHAPTERS: { at: number; title: string }[] = [
  { at: 0, title: "What it is" },
  { at: 19, title: "Setup" },
  { at: 31, title: "The trackpad" },
  { at: 42, title: "Corner dials" },
  { at: 62, title: "Controls" },
  { at: 90, title: "Macros and clipboard" },
  { at: 108, title: "Connections and trust" },
  { at: 128, title: "Make it yours" },
  { at: 137, title: "Straight talk" },
  { at: 149, title: "Get it" },
];

const clock = (s: number) => `${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")}`;

/**
 * The full 2:40 walkthrough. The picture has captions burned in; the text track is
 * the same narration for screen readers and caption styling, off until chosen.
 * `preload="none"` keeps the 13 MB file off the wire until someone presses play.
 */
export function DemoVideo() {
  const video = useRef<HTMLVideoElement>(null);
  const [current, setCurrent] = useState(0);

  // The last chapter that has started: CHAPTERS is in ascending order of `at`.
  const track = (time: number) =>
    setCurrent(CHAPTERS.filter((chapter) => chapter.at <= time).length - 1);

  const seek = (index: number) => {
    const el = video.current;
    if (!el) return;
    // The chapter lights up on the tap, not on the first timeupdate: the file is
    // fetched on demand and that update can be seconds away on a slow link.
    setCurrent(index);
    el.currentTime = CHAPTERS[index]?.at ?? 0;
    void el.play().catch(() => {});
    // Only move the page when the video is not fully on screen already.
    const box = el.getBoundingClientRect();
    if (box.top >= 0 && box.bottom <= window.innerHeight) return;
    const still = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    el.scrollIntoView({ behavior: still ? "auto" : "smooth", block: "center" });
  };

  return (
    <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_18rem] lg:gap-5">
      <div className="shadow-lift overflow-hidden rounded-[var(--radius-panel)] bg-black">
        <video
          ref={video}
          className="block aspect-video w-full"
          src="/edgepad-demo.mp4"
          poster="/edgepad-demo-poster.jpg"
          controls
          preload="none"
          playsInline
          onTimeUpdate={(event) => track(event.currentTarget.currentTime)}
        >
          <track
            kind="captions"
            src="/edgepad-demo.en.vtt"
            srcLang="en"
            label="English (same as the captions in the picture)"
          />
          <a href="/edgepad-demo.mp4">Download the demo video</a>
        </video>
      </div>

      {/* The list is as tall as the video beside it and scrolls, rather than stretching
          the row: h-0 + min-h-full lets the grid row be sized by the video alone. */}
      <nav
        aria-label="Demo chapters"
        className="bg-card shadow-card flex flex-col overflow-hidden rounded-[var(--radius-panel)] lg:h-0 lg:min-h-full"
      >
        <p className="label px-4 pt-4 pb-2">Chapters</p>
        <ol className="divide-line overflow-y-auto divide-y px-2 pb-2">
          {CHAPTERS.map((chapter, index) => (
            <li key={chapter.at}>
              <button
                type="button"
                onClick={() => seek(index)}
                aria-current={index === current ? "true" : undefined}
                className="press hover:bg-faint aria-[current=true]:bg-accent-soft flex min-h-11 w-full items-center gap-3 rounded-xl px-2 text-left text-[0.9375rem] transition-colors"
              >
                <span className="text-primary w-9 shrink-0 text-sm font-bold">
                  {clock(chapter.at)}
                </span>
                {chapter.title}
              </button>
            </li>
          ))}
        </ol>
        <a
          href="/edgepad-demo.mp4"
          download
          className="text-primary px-4 pt-2 pb-4 text-[0.9375rem] font-bold underline underline-offset-4"
        >
          Download the demo (MP4, 13 MB)
        </a>
      </nav>
    </div>
  );
}
