import { ArrowRight } from "lucide-react";
import Link from "next/link";
import { GithubMark } from "@/components/icons/github";
import { LegacyHashRedirect } from "@/components/legacy-hash-redirect";
import { Reveal } from "@/components/reveal";
import { SiteFooter } from "@/components/site-footer";
import { SiteHeader } from "@/components/site-header";
import { SurfaceFigure } from "@/components/surface-figure";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { LATEST_RELEASE, REPO } from "@/lib/nav";
import { loadProtocol } from "@/lib/protocol";

const FEATURES: { title: string; body: string }[] = [
  {
    title: "Corner rulers",
    body: "Each corner holds a dial drawn as a ruler that wraps the bend. Slide along it, clockwise to raise. Volume, brightness, media scrub, zoom, app switcher, microphone level or refresh rate — Settings picks what each corner does.",
  },
  {
    title: "Trackpad",
    body: "Everything between the corners moves the laptop's pointer. One finger moves and clicks, two scroll and pinch and right-click, three and four fingers do whatever you assign them.",
  },
  {
    title: "Media",
    body: "The track, the app playing it and where it is, with previous, play/pause and next. Drag the three pieces anywhere on the surface.",
  },
  {
    title: "Keyboard",
    body: "A full on-screen keyboard whose modifiers work held or tapped. It opens sideways, and its text size is a setting.",
  },
  {
    title: "Gamepad",
    body: "Install ViGEmBus on the laptop and the phone drives a real virtual Xbox controller, sticks and triggers analog, so games that only accept a controller can be played from across the room. Without the driver the same pad sends keyboard keys, and says which of the two it is in.",
  },
  {
    title: "Your pad, your bindings",
    body: "Add a control, bind it to any controller input or key that fits it, size it, label it, move it. Layouts are a library kept one per game, and the ones Edgepad ships with can never be renamed away.",
  },
  {
    title: "Shapes",
    body: "Press and hold on the trackpad until it ticks, then draw. The stroke runs the action or macro you bound to it. A stroke that matches nothing does nothing.",
  },
  {
    title: "Macro buttons",
    body: "Fifteen slots the laptop's tray menu names with an app, a document, a folder or a URL each; the phone sends the slot number and never what it opens. The grid sizes its buttons to the longest name and fits as many across as the screen allows.",
  },
  {
    title: "Live state",
    body: "The dials show the laptop's real volume, mute, brightness and playback position, and follow changes made on the laptop itself.",
  },
];

const STEPS: { step: string; title: string; body: string }[] = [
  {
    step: "01",
    title: "Pair once, like a headset",
    body: "The phone and the laptop pair in Windows Settings > Bluetooth & devices. Edgepad has no pairing step of its own, no account to make and no network to join. The first phone to connect becomes the laptop's trusted phone; every other paired phone is refused until you forget it from the tray menu.",
  },
  {
    step: "02",
    title: "The phone recognises, the laptop executes",
    body: "The phone turns a touch into a small semantic frame: move the pointer by so much, press a button, scroll, run action 3, set control 0 to 55, here is the whole state of a controller. The laptop owns the table of what each action does, so a phone can name an action but never invent one. It is not a containment boundary and is not offered as one: a paired phone is a trusted input device, and its keyboard screen sends raw key codes the way any keyboard does.",
  },
  {
    step: "03",
    title: "The laptop reports back",
    body: "Volume, mute, brightness and what is playing come back the other way, so the dials show the laptop's real values and follow changes you make on the laptop itself rather than drifting out of step with it.",
  },
];

const LIMITS: string[] = [
  "It is Bluetooth, so both ends need it, and it does not reach another room or the internet.",
  "The laptop half needs Windows 10 version 2004 or later, 64-bit. The phone half needs Android 12 or later.",
  "Edgepad.exe is not code-signed, so SmartScreen warns the first time you run it.",
  "The gamepad's virtual controller needs ViGEmBus, a third-party signed kernel driver you install yourself. It was archived by its author in November 2023 and receives no updates, though it remains signed and working. Without it the gamepad sends keyboard keys instead, and the laptop tells the phone which of the two it is in.",
  "Task Manager, and anything running as administrator, ignores the phone unless Edgepad runs as administrator too. The lock screen, UAC prompts and Ctrl+Alt+Del cannot be reached at all.",
];

const INSTALL: {
  half: string;
  requirement: string;
  file: string;
  steps: string[];
}[] = [
  {
    half: "Laptop",
    // Badges do not wrap, so these stay short enough to fit a narrow card. The full
    // requirement is spelled out in the steps and in the limits above.
    requirement: "Windows 10 2004+",
    file: "Edgepad.exe",
    steps: [
      "Download it. One self-contained 64-bit file; the laptop does not need .NET.",
      "Run it. SmartScreen asks first: More info, then Run anyway.",
      "It lives in the system tray, with macros, the log and Start with Windows in its menu.",
      "Optional: install ViGEmBus if you want the gamepad to drive a real controller. Everything else works without it.",
    ],
  },
  {
    half: "Phone",
    requirement: "Android 12+",
    file: "Edgepad.apk",
    steps: [
      "Download it and open it. Allow installing from this source if asked.",
      "Allow Nearby devices on first run. It reads the laptops already paired with the phone and never scans for new ones.",
      "Open it, tap the laptop, and the surface is there.",
    ],
  },
];

export default function Page() {
  // The protocol version is read at build time from ../protocol/actions.txt, the same
  // fixture both test suites read, so the number on this page cannot drift from the
  // number the two apps refuse each other over.
  const protocol = loadProtocol();

  const facts: { term: string; value: string }[] = [
    { term: "Transport", value: "Bluetooth Classic RFCOMM" },
    { term: "Protocol", value: `Version ${protocol.version}` },
    { term: "Phone", value: "Kotlin, Android 12+" },
    { term: "Laptop", value: "C# on .NET 10, Windows 10 2004+" },
  ];

  return (
    <div id="top" className="min-h-dvh">
      <LegacyHashRedirect />
      <SiteHeader />

      <main className="mx-auto max-w-[90rem] px-4 md:px-6">
        {/* Hero */}
        <section className="border-line grid gap-12 border-b py-16 md:py-24 lg:grid-cols-[minmax(0,1fr)_20rem] lg:items-center lg:gap-16">
          <Reveal>
            <p className="label mb-4">Phone · Bluetooth · Windows laptop</p>
            <h1 className="max-w-[18ch] font-mono text-4xl leading-[1.05] font-medium tracking-tight md:text-6xl">
              Your laptop, from across the room.
            </h1>
            <p className="text-dim mt-6 max-w-[62ch] text-lg leading-relaxed">
              Edgepad turns an Android phone into a trackpad, a media remote and a
              control panel for a Windows laptop, over a direct Bluetooth link with
              nothing in between. The two pair once, the way a headset does, and after
              that they talk straight to each other.
            </p>

            <div className="mt-8 flex flex-wrap items-center gap-3">
              <Button asChild>
                <a href={LATEST_RELEASE} target="_blank" rel="noreferrer noopener">
                  Download both halves
                </a>
              </Button>
              <Button asChild variant="outline">
                <Link href="/docs">
                  Read the docs
                  <ArrowRight aria-hidden className="size-4" />
                </Link>
              </Button>
              <Button asChild variant="ghost">
                <a href={REPO} target="_blank" rel="noreferrer noopener">
                  <GithubMark className="size-4" />
                  Source
                </a>
              </Button>
            </div>

            <div className="mt-8 flex flex-wrap gap-2">
              <Badge variant="outline">No account</Badge>
              <Badge variant="outline">No network</Badge>
              <Badge variant="outline">No telemetry</Badge>
              <Badge variant="outline">MIT</Badge>
            </div>
          </Reveal>

          <Reveal delay={0.1} className="order-first lg:order-none">
            <SurfaceFigure className="text-foreground mx-auto h-auto w-full max-w-[15rem] lg:max-w-none" />
          </Reveal>
        </section>

        {/* The facts, stated rather than claimed */}
        <Reveal>
          <dl className="border-line grid gap-6 border-b py-8 sm:grid-cols-2 lg:grid-cols-4">
            {facts.map((fact) => (
              <div key={fact.term}>
                <dt className="label">{fact.term}</dt>
                <dd className="mt-1 font-mono text-sm">{fact.value}</dd>
              </div>
            ))}
          </dl>
        </Reveal>

        {/* What it does */}
        <section
          id="features"
          className="scroll-mt-20 border-line border-b py-16 md:py-24"
        >
          <Reveal>
            <p className="label mb-3">What it does</p>
            <h2 className="max-w-[24ch] font-mono text-2xl leading-tight font-medium tracking-tight md:text-4xl">
              The whole phone screen is the control surface.
            </h2>
            <p className="text-dim mt-4 max-w-[68ch] text-base leading-relaxed md:text-lg">
              A touch is classified where it starts: inside a corner&apos;s zone it
              belongs to that dial for its whole life, on a media piece or a top-centre
              button it is a button press, anywhere else it is the trackpad.
            </p>
          </Reveal>

          <div className="mt-10 grid gap-px sm:grid-cols-2 lg:grid-cols-3">
            {FEATURES.map((feature, index) => (
              <Reveal key={feature.title} delay={index * 0.04}>
                <div className="border-line h-full border p-5">
                  <p className="font-mono text-sm font-medium">{feature.title}</p>
                  <p className="text-dim mt-2 text-[0.9375rem] leading-relaxed">
                    {feature.body}
                  </p>
                </div>
              </Reveal>
            ))}
          </div>
        </section>

        {/* How it works */}
        <section id="how" className="scroll-mt-20 border-line border-b py-16 md:py-24">
          <Reveal>
            <p className="label mb-3">How it works</p>
            <h2 className="max-w-[24ch] font-mono text-2xl leading-tight font-medium tracking-tight md:text-4xl">
              Two programs, one seam, nothing in the middle.
            </h2>
            <p className="text-dim mt-4 max-w-[68ch] text-base leading-relaxed md:text-lg">
              The alternatives are a remote app that only sends media keys, or a
              remote-desktop app that streams the whole screen and wants an account and
              a network round trip to change the volume. Edgepad is neither.
            </p>
          </Reveal>

          <ol className="mt-10 grid gap-px md:grid-cols-3">
            {STEPS.map((item, index) => (
              <li key={item.step} className="border-line border p-5">
                <Reveal delay={index * 0.04}>
                  <p className="label">{item.step}</p>
                  <p className="mt-3 font-mono text-base font-medium">{item.title}</p>
                  <p className="text-dim mt-2 text-[0.9375rem] leading-relaxed">
                    {item.body}
                  </p>
                </Reveal>
              </li>
            ))}
          </ol>

          <Reveal>
            <div className="border-line bg-faint mt-8 border-l-2 px-5 py-4">
              <p className="label mb-2">The transport</p>
              <p className="text-dim max-w-[72ch] text-[0.9375rem] leading-relaxed">
                Bluetooth Classic RFCOMM: an ordered, encrypted byte stream between two
                already-paired devices, with no server, no discovery and no network
                anywhere in the product. Frames are 2 to 258 bytes and go out in a
                single write per batch; a backlog of pointer moves collapses into one
                before it is sent, so a slow link catches up instead of lagging.
              </p>
              <p className="mt-4">
                <Link
                  href="/docs#protocol"
                  className="text-foreground font-mono text-xs tracking-[0.12em] uppercase underline underline-offset-4"
                >
                  The wire protocol, byte by byte
                </Link>
              </p>
            </div>
          </Reveal>
        </section>

        {/* What it will not do */}
        <section className="border-line border-b py-16 md:py-24">
          <div className="grid gap-10 lg:grid-cols-[minmax(0,22rem)_minmax(0,1fr)] lg:gap-16">
            <Reveal>
              <p className="label mb-3">The cost</p>
              <h2 className="max-w-[20ch] font-mono text-2xl leading-tight font-medium tracking-tight md:text-3xl">
                What it will not do.
              </h2>
              <p className="text-dim mt-4 text-[0.9375rem] leading-relaxed">
                Bluetooth&apos;s own limits are the price of having no server in the
                middle. Nothing here is hidden because it is inconvenient.
              </p>
            </Reveal>

            <Reveal delay={0.05}>
              <ul className="divide-line border-line divide-y border-t border-b">
                {LIMITS.map((limit) => (
                  <li
                    key={limit}
                    className="text-dim max-w-[72ch] py-4 text-[0.9375rem] leading-relaxed"
                  >
                    {limit}
                  </li>
                ))}
              </ul>
              <p className="mt-6">
                <Link
                  href="/docs#limits"
                  className="text-foreground font-mono text-xs tracking-[0.12em] uppercase underline underline-offset-4"
                >
                  Every known limit, labelled
                </Link>
              </p>
            </Reveal>
          </div>
        </section>

        {/* Get it */}
        <section id="get" className="scroll-mt-20 py-16 md:py-24">
          <Reveal>
            <p className="label mb-3">Get it</p>
            <h2 className="max-w-[24ch] font-mono text-2xl leading-tight font-medium tracking-tight md:text-4xl">
              Both halves, from the same release.
            </h2>
            <p className="text-dim mt-4 max-w-[68ch] text-base leading-relaxed md:text-lg">
              Always install both from the same one: the two refuse each other at the
              handshake when their protocol versions differ, and say so. Protocol
              version {protocol.version} is what this release speaks.
            </p>
          </Reveal>

          <div className="mt-10 grid gap-px md:grid-cols-2">
            {INSTALL.map((half, index) => (
              <Reveal key={half.half} delay={index * 0.04}>
                <div className="border-line h-full border p-5">
                  <p className="font-mono text-base font-medium">{half.half}</p>
                  <div className="mt-3 flex flex-wrap gap-2">
                    <Badge variant="solid">{half.requirement}</Badge>
                    <Badge variant="outline">{half.file}</Badge>
                  </div>
                  <ol className="text-dim mt-4 list-decimal space-y-2 pl-5 text-[0.9375rem] leading-relaxed">
                    {half.steps.map((step) => (
                      <li key={step}>{step}</li>
                    ))}
                  </ol>
                </div>
              </Reveal>
            ))}
          </div>

          <Reveal>
            <div className="mt-10 flex flex-wrap items-center gap-3">
              <Button asChild>
                <a href={LATEST_RELEASE} target="_blank" rel="noreferrer noopener">
                  Download both halves
                </a>
              </Button>
              <Button asChild variant="outline">
                <Link href="/docs#install">
                  Full install guide
                  <ArrowRight aria-hidden className="size-4" />
                </Link>
              </Button>
            </div>
            <p className="text-dim mt-4 max-w-[68ch] text-[0.9375rem] leading-relaxed">
              Free and MIT licensed. There is no account to make, no server to reach and
              nothing reported back: both halves are in the repository, and the release
              is built from it by GitHub Actions.
            </p>
          </Reveal>
        </section>
      </main>

      <SiteFooter />
    </div>
  );
}
