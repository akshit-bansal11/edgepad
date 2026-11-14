import {
  Activity,
  ArrowRight,
  Camera,
  ChevronDown,
  ClipboardCopy,
  Gamepad2,
  Gauge,
  Keyboard,
  Laptop,
  LayoutGrid,
  Link2,
  type LucideIcon,
  MousePointer2,
  Music,
  PenLine,
  ShieldCheck,
  SlidersHorizontal,
  Smartphone,
  Spline,
  Star,
} from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { DemoVideo } from "@/components/demo-video";
import { GithubMark } from "@/components/icons/github";
import { LegacyHashRedirect } from "@/components/legacy-hash-redirect";
import { LinkBeam } from "@/components/link-beam";
import { ExternalLink } from "@/components/section";
import { SiteFooter } from "@/components/site-footer";
import { SiteHeader } from "@/components/site-header";
import { SurfaceFigure } from "@/components/surface-figure";
import { Badge } from "@/components/ui/badge";
import { BorderBeam } from "@/components/ui/border-beam";
import { Button, buttonVariants } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { GridPattern } from "@/components/ui/grid-pattern";
import { Reveal } from "@/components/ui/reveal";
import { LATEST_RELEASE, REPO } from "@/lib/nav";
import { loadProtocol } from "@/lib/protocol";
import { DOWNLOADS, RELEASE_VERSION } from "@/lib/release";
import { cn } from "@/lib/utils";

export const metadata: Metadata = {
  alternates: { canonical: "/" },
};

const FEATURES: { title: string; Icon: LucideIcon; body: string }[] = [
  {
    title: "Corner dials",
    Icon: Gauge,
    body: "Slide along a ruler in any corner to change volume, brightness, zoom and more. Clockwise raises.",
  },
  {
    title: "Trackpad",
    Icon: MousePointer2,
    body: "Everything between the corners moves the pointer. One finger clicks, two scroll and right-click, three and four do what you assign.",
  },
  {
    title: "Media",
    Icon: Music,
    body: "See what is playing and where it is. Previous, play/pause and next sit where you put them.",
  },
  {
    title: "Keyboard",
    Icon: Keyboard,
    body: "A full on-screen keyboard. Hold or tap the modifier keys. It opens sideways.",
  },
  {
    title: "Gamepad",
    Icon: Gamepad2,
    body: "Play games from across the room. With ViGEmBus on Windows it is a real Xbox controller; without it, it sends keyboard keys and says so.",
  },
  {
    title: "Your pad, your bindings",
    Icon: SlidersHorizontal,
    body: "Place, size, label and bind every control. Layouts are kept one per game.",
  },
  {
    title: "Shapes",
    Icon: Spline,
    body: "Press and hold, then draw a shape to run an action or a macro. A stroke that matches nothing does nothing.",
  },
  {
    title: "Macros",
    Icon: LayoutGrid,
    body: "Buttons that open an app, a document, a folder or a web address. You choose them on the laptop; the phone only presses them.",
  },
  {
    title: "Annotate and sign",
    Icon: PenLine,
    body: "Draw on the laptop screen with the phone as a pen, or sign and save a transparent PNG.",
  },
  {
    title: "Capture",
    Icon: Camera,
    body: "Take a screenshot or start a recording with the laptop's own shortcut, and drag out the region from the phone.",
  },
  {
    title: "Share clipboard",
    Icon: ClipboardCopy,
    body: "Copy on one, paste on the other: text and images. Off until you choose it on first run.",
  },
  {
    title: "Several laptops",
    Icon: Laptop,
    body: "Stay connected to more than one laptop and switch with the Switch laptop dial.",
  },
  {
    title: "Stay connected",
    Icon: Link2,
    body: "A notification keeps the link while Edgepad is minimized. Off until you choose it on first run.",
  },
  {
    title: "Security levels and a PIN",
    Icon: ShieldCheck,
    body: "Trust a phone once, or also ask for a PIN: once per phone, or on every connection.",
  },
  {
    title: "Live state",
    Icon: Activity,
    body: "The dials show the laptop's real volume, mute and brightness, and follow changes made on the laptop.",
  },
];

const CORE_TITLES = new Set([
  "Corner dials",
  "Trackpad",
  "Media",
  "Keyboard",
  "Gamepad",
  "Macros",
]);
const CORE_FEATURES = FEATURES.filter((feature) => CORE_TITLES.has(feature.title));
const MORE_FEATURES = FEATURES.filter((feature) => !CORE_TITLES.has(feature.title));

function FeatureGrid({ features }: { features: typeof FEATURES }) {
  return (
    <ul className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
      {features.map(({ title, Icon, body }) => (
        <li key={title}>
          <Card className="h-full gap-0 p-6">
            <span className="bg-accent-soft text-primary grid size-11 place-items-center rounded-xl">
              <Icon aria-hidden className="size-5" />
            </span>
            <h3 className="mt-4 text-lg font-bold">{title}</h3>
            <p className="text-dim mt-2 text-[0.9375rem] leading-relaxed">{body}</p>
          </Card>
        </li>
      ))}
    </ul>
  );
}

const STEPS: { step: string; title: string; body: string }[] = [
  {
    step: "01",
    title: "Pair once, like a headset",
    body: "Pair the phone in the laptop's own Bluetooth settings and confirm the code on both sides. There is no account. Wi-Fi and USB are optional, off until you turn them on in the laptop's menu, and use a one-time pairing code. The laptop asks Trust NAME? once; you can forget a phone later in Security….",
  },
  {
    step: "02",
    title: "The phone sends touches, the laptop acts",
    body: "The phone turns a touch into a small message such as move, click or scroll. The laptop decides what each message does, so a phone can ask for an action but never invent one. A paired phone is a trusted input device: only trust your own.",
  },
  {
    step: "03",
    title: "The laptop reports back",
    body: "Volume, brightness and what is playing come back, so the dials stay in step with the laptop. The protocol details are in the docs.",
  },
];

const LIMITS: string[] = [
  "The default link is Bluetooth, so both ends need it, and it does not reach another room or the internet. Wi-Fi and USB connections are optional, and the phone's half of them has not been tried on real devices yet.",
  "Mac and Linux are still in testing. Please use them at your own risk. The Windows app is the one that has been run on a real laptop; the Linux app has not been seen working on a Linux laptop yet.",
  "The laptop half needs Windows 10 version 2004 or later (64-bit), macOS 13 or later, or a 64-bit Linux laptop with BlueZ. The phone half needs Android 12 or later.",
  "Edgepad.exe is not code-signed, so SmartScreen warns the first time you run it.",
  "The gamepad's virtual controller needs ViGEmBus, a third-party signed kernel driver you install yourself. It was archived by its author in November 2023 and receives no updates, though it remains signed and working. Without it the gamepad sends keyboard keys instead, and the laptop tells the phone which of the two it is in.",
  "Task Manager, and anything running as administrator, ignores the phone unless Edgepad runs as administrator too. The lock screen, UAC prompts and Ctrl+Alt+Del cannot be reached at all.",
];

const INSTALL: {
  half: string;
  Icon: LucideIcon;
  requirement: string;
  file: string;
  href: string;
  steps: string[];
}[] = [
  {
    half: "Laptop",
    Icon: Laptop,
    // Badges do not wrap, so these stay short enough to fit a narrow card. The full
    // requirement is spelled out in the steps and in the limits above.
    requirement: "Windows 10 2004+",
    file: DOWNLOADS.windows.file,
    href: DOWNLOADS.windows.href,
    steps: [
      "Download it. One self-contained 64-bit file; the laptop does not need .NET.",
      "Run it. SmartScreen asks first: More info, then Run anyway.",
      "It lives in the system tray. Turn on Wi-Fi and USB connections there if you want them: Wi-Fi and USB steps are in the install guide.",
      "Optional: install ViGEmBus if you want the gamepad to drive a real controller. Everything else works without it.",
    ],
  },
  {
    half: "Phone",
    Icon: Smartphone,
    requirement: "Android 12+",
    file: DOWNLOADS.phone.file,
    href: DOWNLOADS.phone.href,
    steps: [
      "Download it and open it. If the browser or Android warns, allow it: Download anyway, then Settings to allow installing, then Install anyway if Play Protect asks.",
      "Allow Nearby devices on first run. It shows the laptops already paired over Bluetooth and, if you turn Wi-Fi and USB on, finds laptops on your network.",
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
    <Reveal>
      <p className="text-primary text-[0.9375rem] font-bold">{eyebrow}</p>
      <h2 className="mt-2 max-w-[20ch] text-[2rem] leading-[1.08] font-black tracking-[-0.02em] md:text-5xl">
        {title}
      </h2>
      {children ? (
        <p className="text-dim mt-4 max-w-[64ch] text-base leading-relaxed md:text-lg">
          {children}
        </p>
      ) : null}
    </Reveal>
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
    {
      term: "Transport",
      value: "Bluetooth; Wi-Fi and USB are new, not tried on real phones",
    },
    { term: "Protocol", value: `Version ${protocol.version}` },
    { term: "Phone", value: "Kotlin, Android 12+" },
    { term: "Laptop", value: "Windows; Mac and Linux in testing" },
  ];

  return (
    <div id="top" className="min-h-dvh">
      <LegacyHashRedirect from="landing" />
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
                Phone · Bluetooth, Wi-Fi, USB · Windows; Mac and Linux in testing
              </p>
              <h1 className="mt-6 max-w-[13ch] text-[2.75rem] leading-[1.02] font-black tracking-[-0.025em] sm:text-6xl lg:text-7xl">
                Your laptop, from across the room.
              </h1>
              <p className="text-dim mt-6 max-w-[56ch] text-lg leading-relaxed md:text-xl">
                Edgepad turns an Android phone into a trackpad, a media remote and a
                control panel for a Windows, Mac or Linux laptop, over a direct
                Bluetooth link with nothing in between; Wi-Fi and USB are optional. The
                two pair once, the way a headset does, and after that they talk straight
                to each other.
              </p>

              <div className="mt-8 flex flex-wrap items-center gap-3">
                <Button asChild>
                  <a href="#get">Download</a>
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
                {["No account", "No server", "No telemetry", "MIT licensed"].map(
                  (item) => (
                    <li key={item}>
                      <Badge variant="outline">{item}</Badge>
                    </li>
                  ),
                )}
              </ul>
            </div>

            <Reveal>
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
            </Reveal>
          </div>
        </section>

        {/* The facts, stated rather than claimed */}
        <div className="mx-auto max-w-[80rem] px-4 md:px-6">
          <Reveal>
            <dl className="bg-line shadow-card grid gap-px overflow-hidden rounded-[var(--radius-panel)] sm:grid-cols-2 lg:grid-cols-4">
              {facts.map((fact) => (
                <div key={fact.term} className="bg-card px-5 py-4">
                  <dt className="label">{fact.term}</dt>
                  <dd className="mt-1 text-base font-bold">{fact.value}</dd>
                </div>
              ))}
            </dl>
          </Reveal>
        </div>

        {/* The demo */}
        <section id="demo" className={SECTION}>
          <Heading
            eyebrow="Demo, 2:40"
            title="Every feature, in under three minutes."
          />
          <Reveal className="mt-10">
            <DemoVideo />
          </Reveal>
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

          <Reveal className="mt-10">
            <FeatureGrid features={CORE_FEATURES} />

            {/* A native disclosure: keyboard and screen-reader operable with no script. */}
            <details className="group mt-4">
              <summary
                className={cn(
                  buttonVariants({ variant: "outline" }),
                  "cursor-pointer list-none [&::-webkit-details-marker]:hidden",
                )}
              >
                More features
                <ChevronDown
                  aria-hidden
                  className="size-4 transition-transform group-open:rotate-180"
                />
              </summary>
              <div className="mt-4">
                <FeatureGrid features={MORE_FEATURES} />
              </div>
            </details>
          </Reveal>
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

          <Reveal className="mt-10">
            <LinkBeam />
          </Reveal>

          <ol className="mt-4 grid gap-4 md:grid-cols-3">
            {STEPS.map((item, index) => (
              <li key={item.step}>
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
              </li>
            ))}
          </ol>

          <Reveal>
            <Card className="bg-accent-soft mt-4 px-6 py-6 shadow-none">
              <div>
                <h3 className="text-lg font-bold">The transport</h3>
                {/* Ink on the tint, not dim: dim on accent-soft is 4.4:1 in light. */}
                <p className="mt-2 max-w-[76ch] text-[0.9375rem] leading-relaxed">
                  Bluetooth Classic RFCOMM by default: an ordered, encrypted byte stream
                  between two already-paired devices, with no server and no discovery.
                  The optional Wi-Fi and USB connections listen on port 47321 and pair
                  once with a six-digit code. Frames are 2 to 258 bytes and go out in a
                  single write per batch; a backlog of pointer moves collapses into one
                  before it is sent, so a slow link catches up instead of lagging.
                </p>
                <p className="mt-2">
                  <MoreLink href="/docs/developers#protocol">
                    The wire protocol, byte by byte
                  </MoreLink>
                </p>
              </div>
            </Card>
          </Reveal>
        </section>

        {/* What it will not do */}
        <section className={SECTION}>
          <div className="grid gap-10 lg:grid-cols-[minmax(0,24rem)_minmax(0,1fr)] lg:gap-16">
            <Heading eyebrow="The cost" title="What it will not do.">
              The default Bluetooth link&apos;s own limits are the price of having no
              server in the middle. Nothing here is hidden because it is inconvenient.
            </Heading>

            <Reveal>
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
            </Reveal>
          </div>
        </section>

        {/* One real review, quoted verbatim, given with permission (issue 63) */}
        <section aria-label="From a user" className={SECTION}>
          <Reveal>
            <Card className="mx-auto max-w-[48rem] gap-0 p-6 md:p-8">
              <figure className="m-0">
                <p className="flex items-center gap-2">
                  <span aria-hidden className="text-primary flex gap-0.5">
                    {[1, 2, 3, 4, 5].map((star) => (
                      <Star key={star} className="size-5 fill-current" />
                    ))}
                  </span>
                  <span className="sr-only">5 out of 5</span>
                </p>
                <blockquote className="mt-4 text-lg leading-relaxed md:text-xl">
                  <p>
                    For years I looked for a way to sync the clipboard between my phone
                    and my laptop over Bluetooth, and never found one I was happy with —
                    until Edgepad. It treats the Bluetooth link as a first-class part of
                    the product, and the features built on top of it are inventive,
                    genuinely useful, and put together in a way that feels coherent and
                    looks good. It went far beyond what I expected.
                  </p>
                </blockquote>
                <figcaption className="text-dim mt-4 text-[0.9375rem]">
                  <ExternalLink href="https://github.com/HaujetZhao">
                    @HaujetZhao
                  </ExternalLink>
                  , on GitHub, shared with permission.{" "}
                  <ExternalLink href="https://github.com/akshit-bansal11/edgepad/issues/63">
                    Source
                  </ExternalLink>
                </figcaption>
              </figure>
            </Card>
          </Reveal>
        </section>

        {/* Get it */}
        <section id="get" className={SECTION}>
          <Heading
            eyebrow="Get it"
            title={`Both halves, from release ${RELEASE_VERSION}.`}
          >
            Always install both from the same one: the two refuse each other at the
            handshake when their protocol versions differ, and say so. Protocol version{" "}
            {protocol.version} is what this release speaks.
          </Heading>

          <div className="mt-10 grid gap-4 md:grid-cols-2">
            {INSTALL.map(({ half, Icon, requirement, file, href, steps }) => (
              <Card key={half} className="h-full">
                <CardHeader className="flex items-center gap-4 px-6">
                  <span className="bg-accent-soft text-primary grid size-12 shrink-0 place-items-center rounded-2xl">
                    <Icon aria-hidden className="size-6" />
                  </span>
                  <div className="min-w-0">
                    <CardTitle className="text-xl font-black">{half}</CardTitle>
                    <div className="mt-1.5 flex flex-wrap gap-2">
                      <Badge variant="solid">{requirement}</Badge>
                      <Badge variant="outline" asChild>
                        <a
                          href={href}
                          className="relative after:absolute after:inset-x-0 after:-inset-y-1.5"
                        >
                          {file}
                        </a>
                      </Badge>
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
            ))}
          </div>

          <Reveal>
            <Card className="bg-accent-soft mt-4 px-6 py-6 shadow-none">
              <div>
                <h3 className="text-lg font-bold">
                  Mac and Linux are still in testing. Please use them at your own risk.
                </h3>
                <p className="mt-2 max-w-[76ch] text-[0.9375rem] leading-relaxed">
                  The Windows app is the one that has been run on a real laptop. The Mac
                  app has been compiled and unit-tested in CI, and the Linux app has not
                  been seen working on a Linux laptop yet. Expect rough edges, and
                  report what you find. On a Mac the laptop half is{" "}
                  <a
                    className="text-primary font-bold underline"
                    href={DOWNLOADS.mac.href}
                  >
                    {DOWNLOADS.mac.file}
                  </a>
                  ; macOS refuses it at first, so choose Open Anyway under System
                  Settings &gt; Privacy &amp; Security. On Linux it is{" "}
                  <a
                    className="text-primary font-bold underline"
                    href={DOWNLOADS.linux.href}
                  >
                    {DOWNLOADS.linux.file}
                  </a>
                  , installed with sudo ./install.sh.
                </p>
                <p className="mt-2">
                  <MoreLink href="/docs#install">Mac and Linux install steps</MoreLink>
                </p>
              </div>
            </Card>
          </Reveal>

          <Reveal>
            <div className="mt-10 flex flex-wrap items-center gap-3">
              <Button asChild>
                <a href={DOWNLOADS.windows.href}>Download for Windows</a>
              </Button>
              <Button asChild>
                <a href={DOWNLOADS.phone.href}>Download for Android</a>
              </Button>
              <Button asChild variant="outline">
                <a href={LATEST_RELEASE}>All downloads</a>
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
