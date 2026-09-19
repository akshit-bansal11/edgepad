import { CodeBlock } from "@/components/code-block";
import { FlowDiagram } from "@/components/flow-diagram";
import { C, Note, P, Section, Sub } from "@/components/section";
import { Badge } from "@/components/ui/badge";
import {
  Table,
  TableBody,
  TableCaption,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import type { Direction, IdRow, ProtocolTables } from "@/lib/protocol";

const DIRECTION_LABEL: Record<Direction, string> = {
  "phone-to-laptop": "phone → laptop",
  "laptop-to-phone": "laptop → phone",
  both: "both ways",
};

/** Renders a generated id table, marking any row the fixture has and this page does not. */
function IdTable({
  rows,
  idHeading,
  caption,
}: {
  rows: IdRow[];
  idHeading: string;
  caption: string;
}) {
  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead className="w-16">{idHeading}</TableHead>
          <TableHead>Name</TableHead>
          <TableHead>What it means</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {rows.map((row) => (
          <TableRow key={row.name}>
            <TableCell className="font-mono text-[0.8125rem]">{row.id}</TableCell>
            <TableCell className="font-mono text-[0.8125rem] whitespace-nowrap">
              {row.name}
            </TableCell>
            <TableCell className="text-[0.875rem]">
              {row.description ?? (
                <span className="text-dim">
                  In the fixture, not yet described on this page.
                </span>
              )}
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
      <TableCaption>{caption}</TableCaption>
    </Table>
  );
}

export function InternalsSections({ protocol }: { protocol: ProtocolTables }) {
  return (
    <>
      <Section
        id="split"
        eyebrow="How it works"
        title="The split"
        lede="The phone recognises; the laptop executes. That single sentence decides most of the rest of the design."
      >
        <P>
          Every touch is turned into a semantic frame on the phone — pointer moved,
          button pressed, scroll, zoom, run action 3, set control 0 to 55, type
          &ldquo;hi&rdquo; — and the laptop carries it out with the Windows input,
          audio, display and media APIs. The laptop owns the table of what each action
          id does. The phone can name an action; it cannot invent one.
        </P>
        <P>
          The laptop reports back. After the handshake it sends a snapshot of volume,
          microphone and brightness, then what is playing and where, then the
          display&apos;s available refresh rates and the macro names, and thereafter
          every change as it happens. The phone keeps the last report in{" "}
          <C>LaptopState</C>, which outlives the control surface, so a surface rebuilt
          after a rotation or a theme change starts from real values and never shows 0
          for a level it has not heard. A level never reported shows an ellipsis, not a
          zero.
        </P>

        <Sub>Why Windows gestures are emulated by their results</Sub>
        <P>
          Windows only runs its native multi-finger gestures for certified Precision
          Touchpad hardware. Two ways round that were rejected: a virtual PTP driver,
          which needs a kernel driver and test-signing mode and so weakens the
          laptop&apos;s security, and making the phone a Bluetooth HID touchpad, which
          needs a PTP certification blob and depends on Android OEM support for{" "}
          <C>BluetoothHidDevice</C>. So Edgepad produces the result instead:
          Win+Ctrl+Left rather than a finger-following desktop slide. The cost is the
          animation. Alt+Tab stays interactive because Alt is genuinely held while the
          fingers are down.
        </P>
      </Section>

      <Section
        id="transport"
        eyebrow="How it works"
        title="Transport"
        lede="Bluetooth Classic RFCOMM: an ordered, encrypted byte stream between two already-paired devices, with no server, no discovery and no network anywhere in the product."
      >
        <div className="flex flex-wrap gap-2">
          <Badge variant="solid">RFCOMM</Badge>
          <Badge variant="outline">BluetoothEncryptionWithAuthentication</Badge>
          <Badge variant="outline">Protocol v{protocol.version}</Badge>
        </div>
        <CodeBlock
          title="Service UUID, from protocol/actions.txt"
          code={protocol.serviceId}
          caption="Both sides know this one id. There is no discovery step and no service record to look up."
        />
        <P>
          The socket is bound at <C>BluetoothEncryptionWithAuthentication</C>. That
          means only a device already paired with the laptop can connect at all, and the
          stream is encrypted by the link layer rather than by anything Edgepad wrote.
        </P>

        <Sub>What was rejected</Sub>
        <div className="grid gap-px sm:grid-cols-3">
          {[
            {
              title: "BLE GATT",
              body: "Windows as a GATT server is unreliable across adapters, and GATT is datagram-shaped where an ordered stream is wanted.",
            },
            {
              title: "Wi-Fi / LAN",
              body: "Needs a shared network and a discovery step. Bluetooth was also the requirement.",
            },
            {
              title: "Phone as a Bluetooth HID touchpad",
              body: "Windows only runs its multi-finger gestures for certified Precision Touchpad hardware, so the gestures would have to be emulated anyway.",
            },
          ].map((item) => (
            <div key={item.title} className="border-line border p-4">
              <p className="font-mono text-sm font-medium">{item.title}</p>
              <p className="text-dim mt-2 text-[0.9375rem] leading-relaxed">
                {item.body}
              </p>
            </div>
          ))}
        </div>
      </Section>

      <Section
        id="architecture"
        eyebrow="How it works"
        title="Architecture"
        lede="One product in two programs, in one repository. This is the map of both and of the single seam between them."
      >
        <FlowDiagram />

        <Sub>The phone&apos;s surface</Sub>
        <P>
          <C>ControlSurface</C> is a single custom <C>View</C> that draws everything and
          receives every touch. There is no view hierarchy between the finger and the
          recogniser. Geometry is computed once in <C>onSizeChanged</C>; nothing is
          allocated while drawing.
        </P>
        <P>
          <C>Perimeter</C> models the screen&apos;s edge as one clockwise path — a
          rounded rectangle whose corner radius is the display&apos;s own, from{" "}
          <C>WindowInsets.getRoundedCorner</C> — so a ruler bends round a corner instead
          of being clipped by it. A dial&apos;s finger position is projected onto that
          path and its movement measured along it. <C>Perimeter</C> is the single place
          that says a screen has four corners; three files each used to carry their own
          copy.
        </P>
        <P>
          <C>TrackpadRecognizer</C> and <C>Dial</C> are pure Kotlin with no Android
          types. That is what lets the whole gesture table run as plain JVM unit tests
          with no emulator.
        </P>

        <Sub>Why the laptop half is a tray app, not a service</Sub>
        <P>
          Windows services run in session 0 and can neither inject input into the
          desktop nor reach the user&apos;s audio session. So Edgepad is a per-user tray
          app that starts at login through the <C>HKCU</C> Run key. It is
          single-instance, held by a named mutex, and a newer copy signals the running
          one to quit and waits for the lock rather than killing the process.
        </P>
      </Section>

      <Section
        id="threads"
        eyebrow="How it works"
        title="Threads and latency"
        lede="Latency is the design priority after correctness. Every choice below exists to keep the path from a finger to SendInput short, and to stop a slow link from turning into a growing backlog."
      >
        <Sub>On the phone</Sub>
        <P>
          <C>LaptopLink.open</C> blocks for the life of the connection on its own thread
          and reads there. A second thread drains the outbox and writes. <C>send</C>{" "}
          never blocks, so touch handling never waits on the link. Every callback into
          the activity is posted to the main thread. The link is kept across
          configuration changes through <C>onRetainNonConfigurationInstance</C> with its
          listener swapped to the new activity, and closed when the app leaves the
          foreground.
        </P>

        <Sub>On the laptop</Sub>
        <P>
          <C>RfcommServer</C> accepts on WinRT&apos;s thread and hands each socket to a{" "}
          <C>Session</C> on a dedicated above-normal-priority thread, where the read
          loop turns frames straight into input. There is no queue between the socket
          and <C>SendInput</C>.
        </P>
        <P>
          A new connection replaces the old one rather than being refused, because after
          a dropped link the phone reconnects before the laptop&apos;s old socket has
          noticed it is dead. Audio-change notifications arrive on COM threads and
          brightness events on a WMI thread, so <C>Session.Send</C> is locked and they
          interleave safely with the read loop&apos;s PONGs.
        </P>

        <Sub>The four things that make it feel immediate</Sub>
        <ol className="text-dim max-w-[70ch] list-decimal space-y-3 pl-5 text-[0.9375rem] leading-relaxed">
          <li>
            <strong className="text-foreground">Unbuffered touch.</strong>{" "}
            <C>View.requestUnbufferedDispatch</C>, with every historical sample read
            from each <C>MotionEvent</C>, so events arrive as they happen rather than
            batched to vsync.
          </li>
          <li>
            <strong className="text-foreground">One write per batch.</strong> The writer
            thread sends everything already queued in a single write.
          </li>
          <li>
            <strong className="text-foreground">Coalescing.</strong> A backlog of{" "}
            <C>MOVE</C>, <C>SCROLL</C> and <C>ZOOM</C> frames still waiting in the
            outbox is summed into one before it goes out, and only the last <C>SET</C>{" "}
            per control survives. A slow link catches up in a single packet instead of
            replaying every sample it missed.
          </li>
          <li>
            <strong className="text-foreground">Brightness off the hot path.</strong> A
            WMI call is far slower than a frame, so brightness writes go to their own
            thread with latest-value-wins. Queueing them would make a dial drag lag
            further behind the finger the longer it moved.
          </li>
        </ol>

        <Note label="Held input">
          The app switcher works by holding Alt across frames. <C>InputInjector</C>{" "}
          remembers everything it holds down and releases all of it when a session ends,
          so a link dropped mid-gesture can never leave Alt or a mouse button stuck on
          the laptop.
        </Note>

        <Note label="1 ms was asked for and is not reachable">
          The touchscreen samples at a fixed rate and Bluetooth Classic schedules
          traffic in 625 µs slots, before any processing at all. The answer was the list
          above plus a live readout, so the real number on real hardware is measured
          rather than quoted. It has not been measured yet.
        </Note>
      </Section>

      <Section
        id="protocol"
        eyebrow="How it works"
        title={`Wire protocol, version ${protocol.version}`}
        lede={
          <>
            A frame is one type byte followed by a payload whose length is fixed by the
            type, except <C>TEXT</C>, whose second header byte gives the length of the
            text that follows. All multi-byte integers are little-endian. Frames are 2
            to 258 bytes.
          </>
        }
      >
        <Note label="These tables are generated">
          Every row below is parsed from <C>protocol/frames.txt</C> and{" "}
          <C>protocol/actions.txt</C> in this repository when the site is built — the
          same two files the Kotlin and the C# test suites read at run time. The type
          bytes and ids are not retyped here, so this page cannot drift from the two
          apps the way a hand-written copy would.
        </Note>

        <Sub>Errors, and what is merely ignored</Sub>
        <P>
          An unknown type byte, a wrong length, a bad magic or a flag byte other than 0
          or 1 is a protocol error and closes the connection. A well-formed frame naming
          an action, a control or a text kind this side does not know is{" "}
          <strong>dropped and counted</strong>, never treated as an error. That is what
          lets a newer app talk to an older one until the version check says otherwise —
          and why new ids do not need a version bump.
        </P>

        <Sub>Frames</Sub>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="w-20">Type</TableHead>
              <TableHead>Name</TableHead>
              <TableHead>Payload</TableHead>
              <TableHead>Direction</TableHead>
              <TableHead>Meaning</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {protocol.frames.map((frame) => (
              <TableRow key={frame.name}>
                <TableCell className="font-mono text-[0.8125rem]">
                  {frame.type}
                </TableCell>
                <TableCell className="font-mono text-[0.8125rem] whitespace-nowrap">
                  {frame.name}
                </TableCell>
                <TableCell className="font-mono text-[0.8125rem]">
                  {frame.payload ?? "—"}
                </TableCell>
                <TableCell className="text-dim text-[0.8125rem] whitespace-nowrap">
                  {frame.direction ? DIRECTION_LABEL[frame.direction] : "—"}
                </TableCell>
                <TableCell className="text-[0.875rem]">
                  {frame.meaning ?? (
                    <span className="text-dim">
                      In the fixture, not yet described on this page.
                    </span>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
          <TableCaption>
            {protocol.frames.length} frame types, read from protocol/frames.txt.
          </TableCaption>
        </Table>

        {protocol.undescribedInFixture.length > 0 ? (
          <Note label="Drift">
            This page describes {protocol.undescribedInFixture.join(", ")}, which no
            line of <C>protocol/frames.txt</C> covers. Either the fixture is missing a
            golden line or this page is describing a frame that no longer exists.
          </Note>
        ) : null}

        <Sub>Golden bytes</Sub>
        <P>
          Each line of the fixture is a frame&apos;s fields and its exact bytes. Both
          test suites encode the fields and must get exactly those bytes, then decode
          the bytes and must get exactly those fields. Because one file drives
          assertions in two codebases, changing one side&apos;s codec without the other
          fails that side&apos;s test immediately. The fixture is what stops the two
          apps from drifting apart — not code review.
        </P>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Frame</TableHead>
              <TableHead>Fields</TableHead>
              <TableHead>Bytes</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {protocol.frames.flatMap((frame) =>
              frame.examples.map((example) => (
                <TableRow key={example.bytes}>
                  <TableCell className="font-mono text-[0.8125rem] whitespace-nowrap">
                    {frame.name}
                  </TableCell>
                  <TableCell className="font-mono text-[0.8125rem]">
                    {example.fields || "—"}
                  </TableCell>
                  <TableCell className="font-mono text-[0.8125rem]">
                    {example.bytes}
                  </TableCell>
                </TableRow>
              )),
            )}
          </TableBody>
          <TableCaption>Every line of protocol/frames.txt, verbatim.</TableCaption>
        </Table>

        <Sub>TEXT kinds</Sub>
        <IdTable
          rows={protocol.textKinds}
          idHeading="Kind"
          caption="Read from protocol/actions.txt. TEXT carries at most 255 bytes and is never split inside a character."
        />

        <Sub>What the laptop sends, and when</Sub>
        <CodeBlock
          title="After HELLO_ACK"
          code={`STATE  volume, microphone, brightness
TEXT   0 now playing, 1 the app, 2 the timeline
STATE  media position
TEXT   4 the display's refresh rates
STATE  the current refresh rate
TEXT   5 the laptop's macro names

then every change as it happens.
Media position is refreshed once a second while playing.`}
        />

        <Sub>Versioning</Sub>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="w-20">Version</TableHead>
              <TableHead className="w-24">Release</TableHead>
              <TableHead>Change</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow>
              <TableCell className="font-mono text-[0.8125rem]">1</TableCell>
              <TableCell className="font-mono text-[0.8125rem]">0.1.0</TableCell>
              <TableCell className="text-[0.875rem]">
                HELLO through STATE. TEXT kinds 0 and 1 arrived in 0.2.0 without a bump.
              </TableCell>
            </TableRow>
            <TableRow>
              <TableCell className="font-mono text-[0.8125rem]">2</TableCell>
              <TableCell className="font-mono text-[0.8125rem]">0.3.0</TableCell>
              <TableCell className="text-[0.875rem]">
                TEXT kind 2. A mismatch is refused with the laptop&apos;s version.
              </TableCell>
            </TableRow>
            <TableRow>
              <TableCell className="font-mono text-[0.8125rem]">3</TableCell>
              <TableCell className="font-mono text-[0.8125rem]">0.6.0</TableCell>
              <TableCell className="text-[0.875rem]">
                TEXT kind 3. Actions 31 to 34 arrived in 0.5.0, and CONTROL 4, TEXT 4
                and 5 and the macro block in 1.1.0 — all without a bump.
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
        <P>
          Only a new frame <strong>type</strong> forces a version, because an unknown
          type closes the connection. The refresh-rate dial and the macro buttons both
          arrived without a bump, which is the rule working rather than being broken.
          From 1.0 this number moves only in a major release, and any change to what a
          frame means bumps it on both sides in the same commit and adds a fixture line.
        </P>
      </Section>

      <Section
        id="actions"
        eyebrow="How it works"
        title="Actions and controls"
        lede="The laptop owns both tables. The phone names an id; the laptop decides what the id does."
      >
        <Sub>Actions</Sub>
        <IdTable
          rows={protocol.actions}
          idHeading="Id"
          caption={
            protocol.actions.length +
            " action ids, read from protocol/actions.txt. Sent as the payload of the ACTION frame."
          }
        />

        <Sub>Controls</Sub>
        <IdTable
          rows={protocol.controls}
          idHeading="Id"
          caption={
            protocol.controls.length +
            " control ids, read from protocol/actions.txt. Carried by SET (phone to laptop) and STATE (laptop to phone)."
          }
        />

        <Note label="Why the refresh rate is an index, not a rate">
          <C>SET</C> carries <C>value u8</C> and the laptop drops anything above 100, so
          120 or 144 could not cross the wire at all. An index into the <C>TEXT 4</C>{" "}
          list also makes a rate the laptop does not have unrepresentable rather than
          merely rejected. A laptop that names no rates has none to offer, and the dial
          has nothing to show. The switch lasts for the session only; a dial should not
          decide what the desktop boots at, and the list is filtered to the resolution
          and colour depth already in use so a rate can never drag the desktop to
          another size.
        </Note>

        <Note label="Why macros send an index">
          Actions 64 to 95 are a reserved block of 32 slots; the phone runs slot n as{" "}
          <C>MACRO_BASE + n</C>. It sends the index and never what the index opens. The
          laptop&apos;s own list, edited from its tray menu, decides that, so a slot
          cannot be repointed from the phone. A blank name keeps its slot: dropping one
          would slide every later name onto another slot&apos;s index, and the index is
          what the phone sends.
        </Note>
      </Section>

      <Section
        id="security"
        eyebrow="How it works"
        title="Security and trust"
        lede="Edgepad lets a phone drive a laptop, so its boundaries matter. There are two layers, and one common misreading of them."
      >
        <Sub>The two layers</Sub>
        <div className="grid gap-px sm:grid-cols-2">
          {[
            {
              title: "Pairing",
              body: "Enforced by the socket's protection level. The link is bound at BluetoothEncryptionWithAuthentication, so a device not already bonded with the laptop through Windows cannot connect at all, and the stream is encrypted by the link layer.",
            },
            {
              title: "Trust on first use",
              body: "Enforced by TrustStore. The first phone to complete the handshake has its Bluetooth address written to %APPDATA%\\Edgepad\\trusted-phone.txt, and every other paired phone is refused before HELLO_ACK. The tray menu's Forget trusted phone clears it.",
            },
          ].map((item) => (
            <div key={item.title} className="border-line border p-4">
              <p className="font-mono text-sm font-medium">{item.title}</p>
              <p className="text-dim mt-2 text-[0.9375rem] leading-relaxed">
                {item.body}
              </p>
            </div>
          ))}
        </div>
        <P>
          The handshake also carries the protocol version. A mismatch is refused with
          the laptop&apos;s version in the reply, so the phone can say which side needs
          updating instead of misreading frames. A refusal before <C>HELLO_ACK</C> names
          both possible causes: a laptop app older than the phone&apos;s, or a laptop
          that trusts a different phone.
        </P>

        <Sub>What the action table is, and is not</Sub>
        <Note label="Corrected in 2.0.0">
          <p className="mb-3">
            The semantic action table is often described as a containment boundary
            against a compromised phone. It is not one, and three places in the product
            used to claim it was.
          </p>
          <p className="mb-3">
            <C>KEY</C> carries a raw Windows virtual-key code and <C>TEXT</C> kind 3
            carries arbitrary characters, both of which the laptop injects directly —
            that is what the phone&apos;s keyboard screen is, and Win+R with a typed
            line is already arbitrary execution. The macro index is still worth having,
            because a slot cannot be repointed from the phone, but it contains nothing
            the keyboard does not already allow.
          </p>
          <p>
            A paired phone is a trusted input device. The trust boundary is the
            Bluetooth pairing plus trust-on-first-use, not the macro table.
          </p>
        </Note>
        <Note label="Stale file">
          <C>SECURITY.md</C> in the repository still carries the pre-2.0.0 wording
          (&ldquo;There is no frame for a key code, a scan code or a command&rdquo;).{" "}
          <C>docs/PROTOCOL.md</C> and the 2.0.0 changelog entry are the corrected
          account, and are what this page follows.
        </Note>

        <Sub>The line Windows draws</Sub>
        <P>
          <C>SendInput</C> is silently refused when an elevated window, a UAC prompt or
          the secure desktop has focus. This is Windows protecting them and the app
          cannot cross it. The laptop counts those refusals and logs them once per
          session, so a gesture that appears to do nothing has a recorded reason.
        </P>

        <Sub>Known limits</Sub>
        <ul className="text-dim max-w-[70ch] list-disc space-y-2 pl-5 text-[0.9375rem] leading-relaxed">
          <li>
            The trusted phone&apos;s Bluetooth address is stored in plain text under{" "}
            <C>%APPDATA%\Edgepad</C>. Anyone who can write that file can change which
            phone is trusted — and that person can already run programs as you.
          </li>
          <li>
            <C>Edgepad.exe</C> is not code-signed, so SmartScreen warns on first run.
            Verify the download came from the project&apos;s own Releases page.
          </li>
          <li>
            Text typed from the phone goes wherever the laptop&apos;s focus is, exactly
            as a keyboard would.
          </li>
          <li>
            Nothing leaves the two devices: no network, no server, no account, no
            telemetry. The laptop writes a local log of connections and errors and
            nothing else.
          </li>
          <li>
            Release builds are signed, and every release is signed with the same key, so
            a phone will refuse an update that was not built by this project.
          </li>
        </ul>
      </Section>
    </>
  );
}
