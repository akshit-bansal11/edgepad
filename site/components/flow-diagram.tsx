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
      name: "InputInjector · AudioEndpoint · BrightnessControl · MediaSessions",
      detail: "SendInput, Core Audio, WMI, system media transport controls",
    },
  ],
};

function Col({ column }: { column: Column }) {
  return (
    <div className="border-line border">
      <div className="border-line border-b px-4 py-3">
        <p className="font-mono text-sm font-medium">{column.title}</p>
        <p className="text-dim mt-0.5 text-xs">{column.subtitle}</p>
      </div>
      <ul>
        {column.rows.map((row) => (
          <li key={row.name} className="border-line border-b px-4 py-3 last:border-b-0">
            <p className="font-mono text-[0.8125rem] leading-snug">{row.name}</p>
            <p className="text-dim mt-1 text-xs leading-relaxed">{row.detail}</p>
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
      <div className="grid gap-4 lg:grid-cols-[1fr_auto_1fr] lg:items-center">
        <Col column={PHONE} />
        <div className="text-dim flex items-center justify-center gap-2 py-2 lg:flex-col lg:py-0">
          <ArrowLeftRight aria-hidden className="size-4 lg:rotate-0" />
          <span className="label lg:[writing-mode:vertical-rl]">RFCOMM</span>
        </div>
        <Col column={LAPTOP} />
      </div>
      <figcaption className="text-dim mt-3 text-sm">
        The phone recognises; the laptop executes. Frames go right: pointer, button,
        scroll, zoom, action id, control value, key, text. State comes back left:
        volume, mute, microphone, brightness, refresh rate, what is playing, macro
        names, and the PONG the round-trip readout is measured from.
      </figcaption>
    </figure>
  );
}
