import {
  Activity,
  ArrowRight,
  Gamepad2,
  Gauge,
  Keyboard,
  Laptop,
  LayoutGrid,
  type LucideIcon,
  MousePointer2,
  Music,
  SlidersHorizontal,
  Smartphone,
  Spline,
} from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { DemoVideo } from "@/components/demo-video";
import { GithubMark } from "@/components/icons/github";
import { LegacyHashRedirect } from "@/components/legacy-hash-redirect";
import { LinkBeam } from "@/components/link-beam";
import { SiteFooter } from "@/components/site-footer";
import { SiteHeader } from "@/components/site-header";
import { SurfaceFigure } from "@/components/surface-figure";
import { AnimatedShinyText } from "@/components/ui/animated-shiny-text";
import { Badge } from "@/components/ui/badge";
import { BlurFade } from "@/components/ui/blur-fade";
import { BorderBeam } from "@/components/ui/border-beam";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { GridPattern } from "@/components/ui/grid-pattern";
import { MagicCard } from "@/components/ui/magic-card";
import { LATEST_RELEASE, REPO } from "@/lib/nav";
import { loadProtocol } from "@/lib/protocol";

export const metadata: Metadata = {
  alternates: { canonical: "/" },
};

const FEATURES: { title: string; Icon: LucideIcon; body: string }[] = [
  {
    title: "Corner dials",
    Icon: Gauge,
    body: "Each corner holds a dial drawn as a ruler that wraps the bend. Slide along it, clockwise to raise. Volume, brightness, media scrub, zoom, app switcher, microphone level or refresh rate — Settings picks what each corner does.",
  },
  {
    title: "Trackpad",
    Icon: MousePointer2,
    body: "Everything between the corners moves the laptop's pointer. One finger moves and clicks, two scroll and pinch and right-click, three and four fingers do whatever you assign them.",
  },
  {
    title: "Media",
    Icon: Music,
    body: "The track, the app playing it and where it is, with previous, play/pause and next. Drag the three pieces anywhere on the surface.",
  },
  {
    title: "Keyboard",
    Icon: Keyboard,
    body: "A full on-screen keyboard whose modifiers work held or tapped. It opens sideways, and its text size is a setting.",
  },
  {
    title: "Gamepad",
    Icon: Gamepad2,
    body: "Install ViGEmBus on the laptop and the phone drives a real virtual Xbox controller, sticks and triggers analog, so games that only accept a controller can be played from across the room. Without the driver the same pad sends keyboard keys, and says which of the two it is in.",
  },
  {
    title: "Your pad, your bindings",
    Icon: SlidersHorizontal,
    body: "Add a control, bind it to any controller input or key that fits it, size it, label it, move it. Layouts are a library kept one per game, and the ones Edgepad ships with can never be renamed away.",
  },
  {
    title: "Shapes",
    Icon: Spline,
    body: "Press and hold on the trackpad until it ticks, then draw. The stroke runs the action or macro you bound to it. A stroke that matches nothing does nothing.",
  },
  {
    title: "Macro buttons",
    Icon: LayoutGrid,
    body: "Fifteen slots the laptop's tray menu names with an app, a document, a folder or a URL each; the phone sends the slot number and never what it opens. The grid sizes its buttons to the longest name and fits as many across as the screen allows.",
  },
  {
    title: "Live state",
    Icon: Activity,
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
  Icon: LucideIcon;
  requirement: string;
  file: string;
  steps: string[];
}[] = [
  {
    half: "Laptop",
    Icon: Laptop,
    // Badges do not wrap, so these stay short enough to fit a narrow card. The full
    // requirement is spelled out in the steps and in the limits above.
    requirement: "Windows 10 2004+",
    file: "Edgepad-x.y.z.exe",
    steps: [
      "Download it. One self-contained 64-bit file; the laptop does not need .NET.",
      "Run it. SmartScreen asks first: More info, then Run anyway.",
      "It lives in the system tray, with macros, the log and Start with Windows in its menu.",
      "Optional: install ViGEmBus if you want the gamepad to drive a real controller. Everything else works without it.",
    ],
  },
  {
    half: "Phone",
    Icon: Smartphone,
    requirement: "Android 12+",
    file: "Edgepad-x.y.z.apk",
    steps: [
      "Download it and open it. Allow installing from this source if asked.",
      "Allow Nearby devices on first run. It reads the laptops already paired with the phone and never scans for new ones.",
      "Open it, tap the laptop, and the surface is there.",
    ],
  },
];

/** A section's heading block: a short accent eyebrow, a 900 title, a dim lede. */
function Heading({
  eyebrow,
  title,
  children,
}: {
  eyebrow: string;
  title: string;
  children?: React.ReactNode;
}) {
  return (
    <BlurFade>
      <p className="text-primary text-[0.9375rem] font-bold">{eyebrow}</p>
      <h2 className="mt-2 max-w-[20ch] text-[2rem] leading-[1.08] font-black tracking-[-0.02em] md:text-5xl">
        {title}
      </h2>
      {children ? (
        <p className="text-dim mt-4 max-w-[64ch] text-base leading-relaxed md:text-lg">
          {children}
        </p>
      ) : null}
    </BlurFade>
  );
}

/** An inline "go deeper" link: accent, bold, with an arrow. */
function MoreLink({ href, children }: { href: string; children: React.ReactNode }) {
  return (
    <Link
      href={href}
      className="text-primary inline-flex min-h-11 items-center gap-1.5 rounded-lg text-[0.9375rem] font-bold hover:underline hover:underline-offset-4"
    >
      {children}
      <ArrowRight aria-hidden className="size-4" />
    </Link>
  );
}

const SECTION = "mx-auto max-w-[80rem] scroll-mt-20 px-4 py-14 md:px-6 md:py-20";

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

      <main id="main" className="scroll-mt-16">
        {/* Hero, on the app's own surface grid, faded out towards the edges */}
        <section className="relative overflow-hidden">
          <GridPattern
            width={35}
            height={35}
            className="fill-transparent stroke-line [mask-image:radial-gradient(ellipse_70%_60%_at_60%_30%,black,transparent)]"
          />
          <div className="relative mx-auto grid max-w-[80rem] gap-14 px-4 pt-12 pb-16 md:px-6 md:pt-20 md:pb-24 lg:grid-cols-[minmax(0,1fr)_auto] lg:items-center lg:gap-20">
            {/* No entrance on the headline: it is the largest paint on the page, and a
                fade would leave it invisible until the JavaScript arrives. */}
            <div>
              <p className="bg-card shadow-card inline-flex rounded-full px-4 py-1.5 text-sm font-bold">
                <AnimatedShinyText>
                  Phone · Bluetooth · Windows laptop
                </AnimatedShinyText>
              </p>
              <h1 className="mt-6 max-w-[13ch] text-[2.75rem] leading-[1.02] font-black tracking-[-0.025em] sm:text-6xl lg:text-7xl">
                Your laptop, from across the room.
              </h1>
              <p className="text-dim mt-6 max-w-[56ch] text-lg leading-relaxed md:text-xl">
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

              <ul
                aria-label="What it does not need"
                className="mt-8 flex flex-wrap gap-2"
              >
                {["No account", "No network", "No telemetry", "MIT licensed"].map(
                  (item) => (
                    <li key={item}>
                      <Badge variant="outline">{item}</Badge>
                    </li>
                  ),
                )}
              </ul>
            </div>

            <BlurFade delay={0.1}>
              {/* The radius is the figure's own 44/411 of its width, so the beam runs
                  exactly along the phone's edge at both sizes. */}
              <div className="shadow-lift relative mx-auto w-64 rounded-[27.4px] lg:w-[21rem] lg:rounded-[36px]">
                <SurfaceFigure className="block h-auto w-full" />
                <BorderBeam
                  size={140}
                  duration={9}
                  borderWidth={2}
                  colorFrom="var(--blue)"
                  colorTo="var(--blue-soft)"
                />
              </div>
            </BlurFade>
          </div>
        </section>

        {/* The facts, stated rather than claimed */}
        <div className="mx-auto max-w-[80rem] px-4 md:px-6">
          <BlurFade>
            <dl className="bg-line shadow-card grid gap-px overflow-hidden rounded-[var(--radius-panel)] sm:grid-cols-2 lg:grid-cols-4">
              {facts.map((fact) => (
                <div key={fact.term} className="bg-card px-5 py-4">
                  <dt className="label">{fact.term}</dt>
                  <dd className="mt-1 text-base font-bold">{fact.value}</dd>
                </div>
              ))}
            </dl>
          </BlurFade>
        </div>

        {/* The demo */}
        <section id="demo" className={SECTION}>
          <Heading
            eyebrow="Demo, 7:28"
            title="Every feature, from setup to how it works."
          />
          <BlurFade delay={0.05} className="mt-10">
            <DemoVideo />
          </BlurFade>
        </section>

        {/* What it does */}
        <section id="features" className={SECTION}>
          <Heading
            eyebrow="What it does"
            title="The whole phone screen is the control surface."
          >
            A touch is classified where it starts: inside a corner&apos;s zone it
            belongs to that dial for its whole life, on a media piece or a top button it
            is a button press, anywhere else it is the trackpad.
          </Heading>

          <ul className="mt-10 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {FEATURES.map(({ title, Icon, body }, index) => (
              <li key={title}>
                <BlurFade delay={(index % 3) * 0.05} className="h-full">
                  <MagicCard className="shadow-card h-full rounded-[var(--radius-panel)]">
                    <div className="p-6">
                      <span className="bg-accent-soft text-primary grid size-11 place-items-center rounded-xl">
                        <Icon aria-hidden className="size-5" />
                      </span>
                      <h3 className="mt-4 text-lg font-bold">{title}</h3>
                      <p className="text-dim mt-2 text-[0.9375rem] leading-relaxed">
                        {body}
                      </p>
                    </div>
                  </MagicCard>
                </BlurFade>
              </li>
            ))}
          </ul>
        </section>

        {/* How it works */}
        <section id="how" className={SECTION}>
          <Heading
            eyebrow="How it works"
            title="Two programs, one seam, nothing in the middle."
          >
            The alternatives are a remote app that only sends media keys, or a
            remote-desktop app that streams the whole screen and wants an account and a
            network round trip to change the volume. Edgepad is neither.
          </Heading>

          <BlurFade className="mt-10">
            <LinkBeam />
          </BlurFade>

          <ol className="mt-4 grid gap-4 md:grid-cols-3">
            {STEPS.map((item, index) => (
              <li key={item.step}>
                <BlurFade delay={index * 0.05} className="h-full">
                  <Card className="h-full px-6 py-6">
                    <span className="bg-primary text-primary-foreground grid size-9 place-items-center rounded-full text-[0.9375rem] font-black">
                      {index + 1}
                    </span>
                    <div>
                      <h3 className="text-lg leading-snug font-bold">{item.title}</h3>
                      <p className="text-dim mt-2 text-[0.9375rem] leading-relaxed">
                        {item.body}
                      </p>
                    </div>
                  </Card>
                </BlurFade>
              </li>
            ))}
          </ol>

          <BlurFade>
            <Card className="bg-accent-soft mt-4 px-6 py-6 shadow-none">
              <div>
                <h3 className="text-lg font-bold">The transport</h3>
                {/* Ink on the tint, not dim: dim on accent-soft is 4.4:1 in light. */}
                <p className="mt-2 max-w-[76ch] text-[0.9375rem] leading-relaxed">
                  Bluetooth Classic RFCOMM: an ordered, encrypted byte stream between
                  two already-paired devices, with no server, no discovery and no
                  network anywhere in the product. Frames are 2 to 258 bytes and go out
                  in a single write per batch; a backlog of pointer moves collapses into
                  one before it is sent, so a slow link catches up instead of lagging.
                </p>
                <p className="mt-2">
                  <MoreLink href="/docs#protocol">
                    The wire protocol, byte by byte
                  </MoreLink>
                </p>
              </div>
            </Card>
          </BlurFade>
        </section>

        {/* What it will not do */}
        <section className={SECTION}>
          <div className="grid gap-10 lg:grid-cols-[minmax(0,24rem)_minmax(0,1fr)] lg:gap-16">
            <Heading eyebrow="The cost" title="What it will not do.">
              Bluetooth&apos;s own limits are the price of having no server in the
              middle. Nothing here is hidden because it is inconvenient.
            </Heading>

            <BlurFade delay={0.05}>
              <ul className="bg-card shadow-card divide-line divide-y rounded-[var(--radius-panel)] px-5">
                {LIMITS.map((limit) => (
                  <li key={limit} className="py-4 text-[0.9375rem] leading-relaxed">
                    {limit}
                  </li>
                ))}
              </ul>
              <p className="mt-3 px-1">
                <MoreLink href="/docs#limits">Every known limit, labelled</MoreLink>
              </p>
            </BlurFade>
          </div>
        </section>

        {/* Get it */}
        <section id="get" className={SECTION}>
          <Heading eyebrow="Get it" title="Both halves, from the same release.">
            Always install both from the same one: the two refuse each other at the
            handshake when their protocol versions differ, and say so. Protocol version{" "}
            {protocol.version} is what this release speaks.
          </Heading>

          <div className="mt-10 grid gap-4 md:grid-cols-2">
            {INSTALL.map(({ half, Icon, requirement, file, steps }, index) => (
              <BlurFade key={half} delay={index * 0.05} className="h-full">
                <Card className="h-full">
                  <CardHeader className="flex items-center gap-4 px-6">
                    <span className="bg-accent-soft text-primary grid size-12 shrink-0 place-items-center rounded-2xl">
                      <Icon aria-hidden className="size-6" />
                    </span>
                    <div className="min-w-0">
                      <CardTitle className="text-xl font-black">{half}</CardTitle>
                      <div className="mt-1.5 flex flex-wrap gap-2">
                        <Badge variant="solid">{requirement}</Badge>
                        <Badge variant="outline">{file}</Badge>
                      </div>
                    </div>
                  </CardHeader>
                  <CardContent className="px-6">
                    <ol className="divide-line divide-y">
                      {steps.map((step, stepIndex) => (
                        <li key={step} className="flex gap-3 py-3 first:pt-0 last:pb-0">
                          <span className="text-primary w-4 shrink-0 text-[0.9375rem] font-black">
                            {stepIndex + 1}
                          </span>
                          <span className="text-dim text-[0.9375rem] leading-relaxed">
                            {step}
                          </span>
                        </li>
                      ))}
                    </ol>
                  </CardContent>
                </Card>
              </BlurFade>
            ))}
          </div>

          <BlurFade>
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
          </BlurFade>
        </section>
      </main>

      <SiteFooter />
    </div>
  );
}
