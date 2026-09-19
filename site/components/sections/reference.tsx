import { C, Note, P, Section, Sub } from "@/components/section";
import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from "@/components/ui/accordion";
import { Badge } from "@/components/ui/badge";
import { REPO } from "@/lib/nav";

type Decision = { id: string; question: string; answer: string; rejected?: string };

const DECISIONS: Decision[] = [
  {
    id: "transport",
    question: "Why Bluetooth Classic RFCOMM?",
    answer:
      "It gives an ordered, encrypted byte stream on both ends between two already-paired devices, with no server, no discovery and no network. Bluetooth was also the requirement.",
    rejected:
      "BLE GATT — Windows as a GATT server is unreliable across adapters, and GATT is datagram-shaped where an ordered stream is wanted. Wi-Fi or LAN — needs a shared network and a discovery step.",
  },
  {
    id: "csharp",
    question: "Why C# on .NET for the laptop half?",
    answer:
      "The Windows Bluetooth (WinRT), SendInput, Core Audio and WMI APIs are all first-class from .NET, and a self-contained single-file exe needs nothing installed on the laptop.",
    rejected: "Python, which was left open as a choice.",
  },
  {
    id: "tray",
    question: "Why a tray app rather than a Windows service?",
    answer:
      "Services run in session 0 and can neither inject input into the desktop nor reach the user's audio session. Edgepad is a per-user tray app that starts at login through the HKCU Run key.",
  },
  {
    id: "binary",
    question: "Why binary fixed-size frames rather than JSON lines?",
    answer:
      "Latency became the priority, so each frame is a 1-byte type plus a payload whose length is fixed by that type. An unknown type closes the connection.",
  },
  {
    id: "semantic",
    question: "Why does the phone send semantic frames instead of keystrokes?",
    answer:
      "The phone recognises gestures; the laptop owns the table of what each action id does. That keeps the two halves independently versionable and makes an unknown id something to drop rather than something to misexecute.",
  },
  {
    id: "fixtures",
    question: "Why are the protocol tables plain text files?",
    answer:
      "Both test suites read protocol/frames.txt and protocol/actions.txt at run time, so one file drives assertions in two codebases and a one-sided codec change fails a test. Plain text rather than JSON because org.json is a stub that throws in JVM unit tests.",
  },
  {
    id: "views",
    question: "Why platform views on Android rather than Compose?",
    answer:
      "AGP 9 compiles Kotlin itself and its migration guide says nothing about the Compose compiler plugin under it. The control surface has to be a custom View either way, for raw MotionEvents and unbuffered dispatch. The result is that JUnit is the app's only dependency.",
  },
  {
    id: "trust",
    question: "Why trust on first use rather than a pairing code?",
    answer:
      "Bluetooth pairing already happened in Windows Settings, and re-asking for a code would be a second, weaker version of a step the OS did properly. The first phone to complete the handshake is remembered by address; the tray menu has Forget.",
  },
  {
    id: "drop",
    question: "Why are unknown ids dropped rather than refused?",
    answer:
      "A malformed frame — bad type, length, magic or flag byte — closes the connection. A well-formed frame naming something this side does not know is dropped and counted. That lets a newer phone talk to an older laptop, and is why new ids do not force a protocol bump.",
  },
  {
    id: "brightness",
    question: "Why does brightness run on its own thread?",
    answer:
      "A WMI call is far slower than a frame. Queueing brightness writes would make a dial drag lag further behind the finger the longer it moved, so the worker keeps only the latest value.",
  },
  {
    id: "release",
    question: "Why does the injector release everything when a session ends?",
    answer:
      "Alt is genuinely held across frames during an app switch. A link dropped mid-gesture would otherwise leave Alt — or a mouse button — stuck down on the laptop.",
  },
  {
    id: "lock",
    question: "Why does Lock call LockWorkStation instead of sending Win+L?",
    answer: "Windows ignores an injected Win+L.",
  },
  {
    id: "placement",
    question: "Why are the dials fixed to corners?",
    answer:
      "Dials were freely placeable along the edge for several releases. They are back in the four corners because seven kinds competing for four named places is a choice a settings screen can present, where an arbitrary position along an edge is a fiddle. The corner arc follows the display's own rounding.",
  },
  {
    id: "twofinger",
    question: "Why are two-finger gestures no longer assignable?",
    answer:
      "Drag to scroll, pinch to zoom, tap to right-click are what a hand already expects from a trackpad. A phone that answers them differently reads as broken rather than as configured. Three and four fingers stay assignable.",
  },
  {
    id: "monogram",
    question: "Why is the playing app a monogram rather than its icon?",
    answer:
      "The laptop sends the app's name as TEXT kind 1 and the phone draws its first letter in a ring. Sending the icon bitmap means much larger frames over RFCOMM, and Win32 and packaged apps keep their icons in different places. Player logos supplied as SVGs are drawn exactly as given.",
  },
  {
    id: "theme",
    question: "Why does the theme use the system's per-app night mode?",
    answer:
      "UiModeManager.setApplicationNightMode is remembered by the system, so Edgepad stores no preference of its own for it. The surface is black on white or white on black; there is no accent colour in the product.",
  },
  {
    id: "singleinstance",
    question: "Why does a new laptop copy ask the old one to quit?",
    answer:
      "A named mutex plus a quit event: the new copy sets the event and waits up to ten seconds for the lock, and explains itself if it does not get it. Killing the other process was rejected as heavy-handed, and would also kill a copy you meant to keep.",
  },
  {
    id: "stablenames",
    question: "Why do the release assets have stable names?",
    answer:
      "So releases/latest/download/Edgepad.apk and .../Edgepad.exe always resolve to the newest build. Versioned file names break that permanent link, and CI artifacts were rejected as a distribution channel because they expire and need a GitHub login.",
  },
];

type Limit = {
  title: string;
  body: string;
  kind: "not built" | "unverified" | "accepted";
};

const LIMITS: Limit[] = [
  {
    title: "No latency figure has ever been measured",
    body: "The app carries a live round-trip readout built from PING/PONG, and no number from real hardware has been reported. Nothing in this project writes a latency figure down until one has been.",
    kind: "unverified",
  },
  {
    title: "The feel constants are first guesses",
    body: "Pointer gain, scroll units per dp, zoom and switch step sizes in TrackpadRecognizer; base units per dp, units per step and the haptic notch size in Dial; the ruler sizes and hit depths in ControlSurface. They are tuned by feel rather than measured, so expect to adjust the sensitivity settings to taste.",
    kind: "unverified",
  },
  {
    title: "Overlay mode is not built",
    body: "A mode that draws only the dials over whatever is on screen, kept alive by a foreground service, is planned and does not exist. In overlay mode nothing can be claimed back from Android's own gestures, so corner drags may be stolen by the assistant gesture or the notification shade.",
    kind: "not built",
  },
  {
    title: "No keyboard-backlight dial",
    body: "It was asked for. Windows has no general API for a laptop keyboard backlight: it goes through the maker's own driver and utility. Windows Dynamic Lighting covers only compliant RGB devices, and whether any given laptop's keyboard is one has not been checked. Left open rather than guessed at.",
    kind: "not built",
  },
  {
    title: "Android's back gesture wins some of the edge",
    body: "Back-gesture exclusion is requested for every dial, but Android honours at most 200dp per vertical edge and the bottom cannot be claimed at all. With some layouts parts of a ruler will lose to the back gesture under gesture navigation. Immersive mode may soften it; untested.",
    kind: "accepted",
  },
  {
    title: "Seeking depends on the player",
    body: "Media position is read from Windows' system media transport controls and seeking uses TryChangePlaybackPositionAsync, which is dropped where the player does not allow it. Browsers vary by site.",
    kind: "accepted",
  },
  {
    title: "A background browser tab keeps the browser's name",
    body: "Only the front tab titles the window, so a stream playing in a background tab is reported as the browser. A packaged app is matched by its own id instead.",
    kind: "accepted",
  },
  {
    title: "SendInput cannot reach elevated windows",
    body: "An elevated window, a UAC prompt or the secure desktop silently refuses injected input. That is Windows protecting them. The refusals are counted and logged once per session.",
    kind: "accepted",
  },
  {
    title: "Edgepad.exe is not code-signed",
    body: "SmartScreen warns on every download. Acceptable for a small audience; it is the largest drop-off point if traffic is ever driven at it.",
    kind: "accepted",
  },
  {
    title: "Artifact sizes",
    body: "Edgepad.exe is about 58.5 MB as a compressed single file, down from roughly 143 MB uncompressed. Edgepad.apk is about 2.3 MB with R8 off, mostly the Kotlin standard library. Follow-ups, not defects.",
    kind: "accepted",
  },
];

const HISTORY: { version: string; date: string; summary: string }[] = [
  {
    version: "2.0.0",
    date: "2026-09-17",
    summary:
      "Macro buttons — the phone can launch things on the laptop, by slot number. A refresh-rate dial. An on-screen readout on the laptop when a level is set from the phone. Pointer and scroll speed become settings. Per-dial-kind sensitivity. Two-finger gestures become fixed rather than assignable. Three security claims corrected. The wire protocol stayed at version 3.",
  },
  {
    version: "1.0.0",
    date: "2026-09-16",
    summary:
      "The first stable release. Behaviour unchanged from 0.10.0; what changed is the promise around it. The wire protocol settles at version 3 and from here only moves in a major release, and the repository is arranged for someone other than its author to work in. Several duplicated pieces — both layout editors' canvas, the gamepad's shape rules, the surface's three buttons, Perimeter's four corners, the extended-key table — were reduced to one each.",
  },
  {
    version: "0.10.0",
    date: "2026-09-15",
    summary:
      "The phone app is redesigned. A five-page guide, shown one page at a time or all on one scrolling page. JetBrains Mono bundled and used for every piece of text. Settings pages of their own for Corners, Dial feel and Background & pattern. Layout editors that fill the screen so what is laid out has the surface's proportions.",
  },
  {
    version: "0.6.0",
    date: "earlier",
    summary:
      "Protocol version 3: TEXT kind 3 carries text to type, which is what the phone's keyboard screen sends.",
  },
  {
    version: "0.3.0",
    date: "earlier",
    summary:
      "Protocol version 2: TEXT kind 2 carries the timeline, and a version mismatch is refused with the laptop's own version so the phone can name which side is old. The rulers become slide-along rather than turned round a corner point.",
  },
  {
    version: "0.1.0",
    date: "earlier",
    summary: "Protocol version 1: HELLO through STATE. The first release.",
  },
];

const LIMIT_VARIANT: Record<Limit["kind"], "default" | "outline" | "solid"> = {
  "not built": "default",
  unverified: "solid",
  accepted: "outline",
};

export function ReferenceSections() {
  return (
    <>
      <Section
        id="decisions"
        eyebrow="Reference"
        title="Design decisions"
        lede="Each of these had a real alternative that was considered and rejected. The reason is kept with the rule, because a rule stripped of its reasoning gets re-litigated by the next person who reads it."
      >
        <Accordion type="multiple" className="border-line border-t">
          {DECISIONS.map((decision) => (
            <AccordionItem key={decision.id} value={decision.id}>
              <AccordionTrigger>{decision.question}</AccordionTrigger>
              <AccordionContent>
                <p>{decision.answer}</p>
                {decision.rejected ? (
                  <p className="mt-3">
                    <span className="label">Rejected</span>{" "}
                    <span className="ml-1">{decision.rejected}</span>
                  </p>
                ) : null}
              </AccordionContent>
            </AccordionItem>
          ))}
        </Accordion>
      </Section>

      <Section
        id="limits"
        eyebrow="Reference"
        title="Known limits"
        lede="What is not built, what is not verified, and what is a deliberate trade. Nothing here is hidden because it is inconvenient."
      >
        <div className="grid gap-px">
          {LIMITS.map((limit) => (
            <div key={limit.title} className="border-line border p-4">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <p className="font-mono text-sm font-medium">{limit.title}</p>
                <Badge variant={LIMIT_VARIANT[limit.kind]}>{limit.kind}</Badge>
              </div>
              <p className="text-dim mt-2 max-w-[70ch] text-[0.9375rem] leading-relaxed">
                {limit.body}
              </p>
            </div>
          ))}
        </div>

        <Sub>Not goals</Sub>
        <P>
          No account, no server, no telemetry, and no web deployment: Edgepad ships as
          two native binaries from GitHub Releases and there is nothing to host. The
          link does not reach another room or the internet, and it is not meant to.
        </P>
      </Section>

      <Section
        id="history"
        eyebrow="Reference"
        title="Version history"
        lede={
          <>
            The full record is{" "}
            <a
              href={`${REPO}/blob/main/CHANGELOG.md`}
              target="_blank"
              rel="noreferrer noopener"
              className="text-foreground underline underline-offset-4"
            >
              CHANGELOG.md
            </a>
            , which follows Keep a Changelog. Versions follow Semantic Versioning.
          </>
        }
      >
        <div className="grid gap-px">
          {HISTORY.map((entry) => (
            <div key={entry.version} className="border-line border p-4">
              <div className="flex flex-wrap items-baseline gap-3">
                <p className="font-mono text-base font-medium">{entry.version}</p>
                <span className="label">{entry.date}</span>
              </div>
              <p className="text-dim mt-2 max-w-[70ch] text-[0.9375rem] leading-relaxed">
                {entry.summary}
              </p>
            </div>
          ))}
        </div>

        <Note label="Both halves, same release">
          The two apps refuse each other at the handshake when their protocol versions
          differ, and say so. Even where they would interoperate — as 2.0.0 and 1.0.0 do
          — only the matching pair knows about the newer controls.
        </Note>
      </Section>

      <Section id="credits" eyebrow="Reference" title="Licence and credits" lede="MIT.">
        <ul className="text-dim max-w-[70ch] list-disc space-y-2 pl-5 text-[0.9375rem] leading-relaxed">
          <li>
            Icons are{" "}
            <a
              href="https://lucide.dev"
              target="_blank"
              rel="noreferrer noopener"
              className="text-foreground underline underline-offset-4"
            >
              Lucide
            </a>
            , ISC licence.
          </li>
          <li>
            The typeface is{" "}
            <a
              href="https://github.com/JetBrains/JetBrainsMono"
              target="_blank"
              rel="noreferrer noopener"
              className="text-foreground underline underline-offset-4"
            >
              JetBrains Mono
            </a>
            , SIL Open Font License 1.1. Its licence ships inside the APK under{" "}
            <C>assets/licenses</C>.
          </li>
          <li>Player logos belong to their owners and are drawn as supplied.</li>
          <li>
            The laptop app depends on NAudio (for volume) and <C>System.Management</C>{" "}
            (for WMI brightness), and nothing else. The phone app depends on JUnit, and
            nothing else.
          </li>
        </ul>
      </Section>
    </>
  );
}
