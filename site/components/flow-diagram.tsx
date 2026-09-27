import { ArrowLeftRight } from "lucide-react";

type Column = {
  title: string;
  subtitle: string;
  rows: { name: string; detail: string }[];
};

const PHONE: Column = {
  title: "Android phone",
  subtitle: "Kotlin, platform views, no UI libraries",
  rows: [
    {
      name: "MainActivity",
      detail: "Guide, devices, settings, layout editors, keyboard, gamepad, macros",
    },
    {
      name: "ControlSurface",
      detail: "One custom View that draws everything and receives every touch",
    },
    {
      name: "TrackpadRecognizer · Dial x4",
      detail: "Pure Kotlin, no Android types. Turns touch into semantic frames",
    },
    {
      name: "LaptopLink",
      detail: "RFCOMM socket: a reader thread, a writer thread, a coalescing outbox",
    },
    {
      name: "LaptopState",
      detail: "The laptop's last report, kept across rotation and theme changes",
    },
  ],
};

const LAPTOP: Column = {
  title: "Windows laptop",
  subtitle: "C# on .NET 10, WinForms tray, single instance",
  rows: [
    {
      name: "TrayContext",
      detail: "Menu, status, run at login, macros editor, Forget",
    },
    {
      name: "RfcommServer",
      detail: "Advertises the service, accepts on WinRT's thread",
    },
    {
      name: "Session",
      detail: "One per phone, on its own above-normal-priority thread",
    },
    {
      name: "FrameCodec -> Dispatcher",
      detail: "Decodes, then executes from the laptop-owned action table",
    },
    {
      name: "InputInjector · AudioEndpoint · BrightnessControl · MediaSessions · VirtualPad",
      detail:
        "SendInput, Core Audio, WMI, system media transport controls, and a virtual Xbox controller where ViGEmBus is installed",
    },
  ],
};

function Col({ column }: { column: Column }) {
  return (
    <div className="bg-card shadow-card overflow-hidden rounded-[var(--radius-panel)]">
      <div className="border-line border-b px-5 py-4">
        <p className="text-lg font-bold">{column.title}</p>
        <p className="text-dim mt-0.5 text-sm">{column.subtitle}</p>
      </div>
      <ul className="divide-line divide-y px-5">
        {column.rows.map((row) => (
          <li key={row.name} className="py-3">
            <p className="font-mono text-[0.8125rem] leading-snug [overflow-wrap:anywhere]">
              {row.name}
            </p>
            <p className="text-dim mt-1 text-sm leading-relaxed">{row.detail}</p>
          </li>
        ))}
      </ul>
    </div>
  );
}

/** The map of both programs and the one seam between them. */
export function FlowDiagram() {
  return (
    <figure>
      <div className="grid gap-4 lg:grid-cols-[1fr_auto_1fr] lg:items-stretch">
        <Col column={PHONE} />
        <div className="flex items-center justify-center gap-2 lg:flex-col lg:self-center">
          <span className="bg-accent-soft text-primary grid size-10 place-items-center rounded-full">
            <ArrowLeftRight aria-hidden className="size-4 rotate-90 lg:rotate-0" />
          </span>
          <span className="text-primary font-mono text-[0.8125rem] font-bold">
            RFCOMM
          </span>
        </div>
        <Col column={LAPTOP} />
      </div>
      <figcaption className="text-dim mt-4 max-w-[72ch] text-sm leading-relaxed">
        The phone recognises; the laptop executes. Frames go right: pointer, button,
        scroll, zoom, action id, control value, key, text. State comes back left:
        volume, mute, microphone, brightness, refresh rate, what is playing, macro
        names, and the PONG the round-trip readout is measured from.
      </figcaption>
    </figure>
  );
}
