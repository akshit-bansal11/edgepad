import { CodeBlock } from "@/components/code-block";
import { C, ExternalLink, Note, P, Section, Sub } from "@/components/section";
import { Badge } from "@/components/ui/badge";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { LATEST_RELEASE } from "@/lib/nav";

const SURFACE: { where: string; touch: string; laptop: string }[] = [
  {
    where: "A corner ruler",
    touch: "slide",
    laptop: "Turns that dial. Clockwise raises.",
  },
  {
    where: "A corner ruler",
    touch: "tap",
    laptop: "The dial's action: mute, play/pause, mic mute, task view, reset zoom.",
  },
  {
    where: "Anywhere else, one finger",
    touch: "move / tap / tap then hold-and-move",
    laptop: "Pointer / left click / drag.",
  },
  { where: "Two fingers", touch: "drag", laptop: "Scroll, both axes." },
  { where: "Two fingers", touch: "pinch", laptop: "Zoom (Ctrl+wheel)." },
  { where: "Two fingers", touch: "tap", laptop: "Right click." },
  {
    where: "Three or four fingers",
    touch: "tap, swipe up, down, left, right",
    laptop: "Whatever Settings assigns. Ten slots in all.",
  },
  {
    where: "Anywhere else, one finger",
    touch: "press and hold until it ticks, then draw",
    laptop:
      "The action or macro bound to the shape drawn. An unrecognised stroke does nothing.",
  },
  {
    where: "Top centre",
    touch: "tap one of the four buttons",
    laptop:
      "Settings, Controls (keyboard, gamepad, capture, annotate), the lock, the macros, left to right. The lock stays on the phone.",
  },
  { where: "Back", touch: "", laptop: "Leaves the surface. The link stays up." },
];

const DEFAULT_GESTURES: { fingers: string; gesture: string; does: string }[] = [
  {
    fingers: "Three",
    gesture: "left / right",
    does: "Previous / next virtual desktop",
  },
  { fingers: "Three", gesture: "up", does: "Task view" },
  { fingers: "Three", gesture: "down", does: "Show the desktop" },
  { fingers: "Three", gesture: "tap", does: "Search" },
  {
    fingers: "Four",
    gesture: "left / right",
    does: "Walk the app switcher. Alt stays held while the fingers are down",
  },
  { fingers: "Four", gesture: "tap", does: "Notifications" },
];

const CAPTURE: { os: string; region: string; screen: string; record: string }[] = [
  {
    os: "Windows",
    region: "Win+Shift+S",
    screen: "PrtScn",
    record: "Win+Shift+R (needs a recent Snipping Tool; unverified)",
  },
  {
    os: "Mac",
    region: "Cmd+Shift+4",
    screen: "Cmd+Shift+3",
    record: "Cmd+Shift+5",
  },
  {
    os: "Linux",
    region: "Print",
    screen: "Print",
    record: "Ctrl+Shift+Alt+R on GNOME, Meta+Alt+R on KDE (unverified)",
  },
];

export function ProductSections() {
  return (
    <>
      <Section
        id="overview"
        eyebrow="Start here"
        title="What Edgepad is"
        lede={
          <>
            Edgepad turns an Android phone into a trackpad, a media remote and a control
            panel for a Windows, Mac or Linux laptop, over a direct Bluetooth link with
            nothing in between.
          </>
        }
      >
        <P>
          It is for the times the laptop is across the room rather than under your
          hands: plugged into a television, docked on a desk you are not sitting at,
          parked somewhere a mouse dongle will not reach.
        </P>
        <P>
          The alternatives are a remote app that only sends media keys, or a
          remote-desktop app that streams the whole screen and wants an account and a
          network round trip to change the volume. Edgepad is neither. The phone and the
          laptop pair once, the way a headset does, and after that they talk directly:
          no account, no Wi-Fi, no service in between that can be slow or down.
        </P>
        <P>
          The cost is Bluetooth&apos;s own. Both ends need it, and it does not reach
          another room or the internet.
        </P>

        <Sub>What it does</Sub>
        <div className="grid gap-3 sm:grid-cols-2">
          {[
            {
              title: "Corner rulers",
              body: "Each corner holds a dial drawn as a ruler that wraps the bend. Slide along it, clockwise to raise. Volume, brightness, media scrub, zoom, app switcher, microphone level or refresh rate; Settings picks what each corner does, how long the rulers are and how tall. Split corners gives each corner two dials, eight in all.",
            },
            {
              title: "Trackpad",
              body: "Everything between the corners moves the laptop's pointer. One finger moves and clicks, two scroll and pinch, three and four fingers do whatever you assign them.",
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
              body: "A real virtual Xbox controller on the laptop, with analog sticks and triggers, where the ViGEmBus driver is installed; the same pad falls back to sending keys where it is not. Every control is yours to place, bind, size and label, and layouts are kept one per game.",
            },
            {
              title: "Shapes",
              body: "Press and hold on the trackpad until it ticks, then draw without lifting. The stroke is matched against the shapes drawn in Settings, and the one it matches runs a laptop action, a macro, or the pad's own focus or lock.",
            },
            {
              title: "Macro buttons",
              body: "Fifteen slots the laptop names with an app, a document, a folder or a URL each, in its tray or menu-bar menu (a text file on Linux); the phone sends the slot number and never what it opens. The grid sizes every button to the longest name and fits as many across as the screen has room for, and Edit layout drags the buttons into the order you want.",
            },
            {
              title: "Controls",
              body: "One button on the surface opens a grid of the keyboard, the gamepad, capture and annotate. A laptop shows only what it can do, and the grid can be rearranged like the macro deck.",
            },
            {
              title: "Annotate and sign",
              body: "Controls > Annotate turns the phone into an absolute pen for the laptop screen: a pen, a highlighter, a line, an arrow, a rectangle and an ellipse in eight colours. Sign is the same pad as a signature, saved as a transparent PNG.",
            },
            {
              title: "Capture",
              body: "Buttons for a region, the whole screen and a recording press the laptop's own screenshot shortcut, with a trackpad under them to drag out the region.",
            },
            {
              title: "Security levels and a PIN",
              body: "Level 2 also asks each phone for a six-digit PIN the first time, level 3 on every connection, on the phone's own keypad. It is set on the laptop, and five wrong answers lock a phone out for 30 seconds, then longer.",
            },
            {
              title: "Live state",
              body: "The dials show the laptop's real volume, mute, brightness and playback position, and follow changes made on the laptop itself.",
            },
          ].map((feature) => (
            <div
              key={feature.title}
              className="bg-card shadow-card rounded-[var(--radius-card)] p-5"
            >
              <p className="text-base font-bold">{feature.title}</p>
              <p className="text-dim mt-2 text-[0.9375rem] leading-relaxed">
                {feature.body}
              </p>
            </div>
          ))}
        </div>

        <Note label="On speed">
          Latency is the design priority after correctness, and the app carries a live
          round-trip readout built from <C>PING</C>/<C>PONG</C> so the real number can
          be measured on real hardware. No such measurement has been taken yet, so this
          page quotes no latency figure. It will say one when one exists.
        </Note>
      </Section>

      <Section
        id="install"
        eyebrow="Start here"
        title="Download and install"
        lede={
          <>
            Both halves come from the same{" "}
            <ExternalLink href={LATEST_RELEASE}>GitHub release</ExternalLink>. Always
            install both from the same one: the two refuse each other at the handshake
            when their protocol versions differ, and say so.
          </>
        }
      >
        <Note label="In testing">
          <p className="mb-3">
            <strong className="text-foreground">
              Mac and Linux are still in testing. Please use them at your own risk.
            </strong>
          </p>
          <p>
            The Windows app is the one that has been run on a real laptop. The Mac app
            has been compiled and unit-tested in CI, and the Linux app has not been seen
            working on a Linux laptop yet. Expect rough edges, and report what you find.
          </p>
        </Note>

        <Tabs defaultValue="windows">
          <TabsList>
            <TabsTrigger value="windows">Laptop</TabsTrigger>
            <TabsTrigger value="android">Phone</TabsTrigger>
          </TabsList>

          <TabsContent value="windows" className="space-y-4">
            <div className="flex flex-wrap items-center gap-2">
              <Badge variant="solid">Windows 10 version 2004 or later</Badge>
              <Badge variant="outline">64-bit</Badge>
              <Badge variant="outline">Edgepad-x.y.z.exe</Badge>
            </div>
            <ol className="text-dim max-w-[68ch] list-decimal space-y-3 pl-5 text-[0.9375rem] leading-relaxed">
              <li>
                Download <C>Edgepad-x.y.z.exe</C>. It is a single self-contained file;
                nothing else needs installing, and the laptop does not need .NET.
              </li>
              <li>
                Run it. It is not code-signed, so SmartScreen asks first: choose{" "}
                <strong className="text-foreground">More info</strong>, then{" "}
                <strong className="text-foreground">Run anyway</strong>.
              </li>
              <li>
                It lives in the system tray. The menu shows whether a phone is
                connected, then{" "}
                <strong className="text-foreground">Start with Windows</strong>,{" "}
                <strong className="text-foreground">Macros…</strong>,{" "}
                <strong className="text-foreground">Open log</strong>,{" "}
                <strong className="text-foreground">Documentation</strong>,{" "}
                <strong className="text-foreground">Forget trusted phones</strong> and{" "}
                <strong className="text-foreground">Quit Edgepad</strong>, with the
                version beside it.
              </li>
            </ol>
            <P>
              Running a newer <C>Edgepad-x.y.z.exe</C> asks the running copy to quit and
              takes its place, so an update takes over cleanly rather than failing on
              the single-instance lock.
            </P>
            <Note label="Optional: ViGEmBus, for the gamepad">
              <p className="mb-3">
                The gamepad works without it. Every control can be bound to a keyboard
                key, and that is what the pad sends when the laptop has no controller
                driver — which is the state most laptops are in.
              </p>
              <p className="mb-3">
                Install{" "}
                <ExternalLink href="https://github.com/nefarius/ViGEmBus/releases/latest">
                  ViGEmBus
                </ExternalLink>{" "}
                and Edgepad plugs a real virtual Xbox controller into Windows instead.
                Games that only ever accepted a controller can then be played from the
                phone, and a stick is a stick rather than four keys: the sticks and
                triggers carry their full analog range. It is a signed kernel driver
                from a third party, installed by you and not by Edgepad.
              </p>
              <p>
                You never have to guess which of the two you are in. The laptop answers
                every request for the controller with a <C>PAD_STATUS</C> token, and the
                gamepad screen says <em>keyboard mode</em> and which of the three
                reasons it was, rather than being quietly dead. A ViGEmBus installed
                while Edgepad is running is noticed on the next reconnect.
              </p>
            </Note>
            <Note label="On a Mac">
              <p className="mb-3">
                macOS 13 or later, one download for Apple Silicon and Intel:{" "}
                <C>Edgepad-x.y.z-mac.zip</C>, which holds <C>Edgepad.app</C>. Move it to
                Applications and open it. It is not notarised, so macOS refuses it at
                first: choose <strong className="text-foreground">Open Anyway</strong>{" "}
                under System Settings &gt; Privacy &amp; Security.
              </p>
              <p className="mb-3">
                It lives in the menu bar and asks for Bluetooth and for input control
                (Accessibility, named Device Control and Data Access from macOS 27),
                without which macOS silently discards every pointer move and key press
                the app sends. If input still does nothing after you grant it, quit
                Edgepad from the menu bar and open it again. The signature is ad hoc, so
                macOS treats each new version as a different app and the permission has
                to be granted again after an update.
              </p>
              <p>
                The Mac app has been compiled and unit-tested in CI but not yet run on a
                real Mac, and it has no now playing, level readout or virtual controller
                yet. The README lists what differs.
              </p>
            </Note>
            <Note label="On Linux">
              <p className="mb-3">
                Download <C>Edgepad-x.y.z-linux-x64.tar.gz</C> (64-bit) and check it
                against <C>SHA256SUMS.txt</C>, then extract it and run the installer as
                root. Log out and back in once so your session gets access to{" "}
                <C>/dev/uinput</C>, then start Edgepad from the launcher or run{" "}
                <C>edgepad</C>.
              </p>
              <p className="mb-3">
                <C>install.sh</C> only copies files and loads a module; it downloads
                nothing. It puts <C>edgepad</C> in <C>/usr/local/bin</C>, the launcher
                entry and the icon under <C>/usr/local/share</C>, a udev rule in{" "}
                <C>/etc/udev/rules.d</C> and <C>uinput</C> in{" "}
                <C>/etc/modules-load.d</C>, then runs <C>modprobe uinput</C>. Another
                prefix goes after the command, and <C>--uninstall</C> removes it all and
                leaves your settings in <C>~/.config/edgepad</C>.
              </p>
              <p className="mb-3">
                The app is a self-contained single file, so .NET need not be installed.
                It needs BlueZ for Bluetooth, <C>wpctl</C> (PipeWire) or <C>pactl</C>{" "}
                for the volume and microphone dials, systemd-logind for Lock and
                brightness, and <C>xdg-open</C> for macros that open a file, folder or
                web address and for Open log and Documentation. <C>wl-copy</C> on
                Wayland or <C>xclip</C> on X11 is optional, for typing text that is not
                plain ASCII. GNOME needs the AppIndicator extension to show the tray
                icon.
              </p>
              <p>
                On Wayland, annotate runs under XWayland and the compositor may ignore
                its always-on-top request, so it is best effort: the phone shows an
                Annotate tile only when it can work. The shortcuts for task view, show
                desktop, search, notifications and switching desktops differ per
                desktop; the GNOME and KDE tables were written from memory and are
                unverified, and on any other desktop those actions do nothing. Macros
                are a text file you edit by hand, <C>~/.config/edgepad/macros.txt</C>,
                with no editor on Linux yet. <C>docs/LINUX.md</C> in the repository
                lists everything that has not been seen working.
              </p>
            </Note>
            <CodeBlock
              title="Install on Linux"
              code={`tar xzf Edgepad-x.y.z-linux-x64.tar.gz
cd edgepad-x.y.z
sudo ./install.sh`}
              caption="Then log out and back in once. sudo ./install.sh /opt/edgepad installs under another prefix, and sudo ./install.sh --uninstall removes it."
            />
          </TabsContent>

          <TabsContent value="android" className="space-y-4">
            <div className="flex flex-wrap items-center gap-2">
              <Badge variant="solid">Android 12 or later</Badge>
              <Badge variant="outline">minSdk 31</Badge>
              <Badge variant="outline">Edgepad-x.y.z.apk</Badge>
            </div>
            <ol className="text-dim max-w-[68ch] list-decimal space-y-3 pl-5 text-[0.9375rem] leading-relaxed">
              <li>
                Download <C>Edgepad-x.y.z.apk</C> and open it. Allow installing from
                this source if asked.
              </li>
              <li>
                On first run, allow the{" "}
                <strong className="text-foreground">Nearby devices</strong> permission.
                Edgepad uses it to see the laptops already paired with the phone; it
                never scans for new ones.
              </li>
              <li>
                Every later release installs over the previous one and settings are
                kept, because <C>versionCode</C> comes from the workflow run number and
                only ever increases.
              </li>
            </ol>
            <Note label="Debug builds">
              A build made locally is versioned <C>0.0.0-dev</C> and signed with the
              debug key, so it will not install over a release build. Uninstall the
              release first.
            </Note>
          </TabsContent>
        </Tabs>

        <Sub>Pairing and first connection</Sub>
        <ol className="text-dim max-w-[68ch] list-decimal space-y-3 pl-5 text-[0.9375rem] leading-relaxed">
          <li>
            Pair the phone with the laptop once, in Windows{" "}
            <strong className="text-foreground">
              Settings &gt; Bluetooth &amp; devices
            </strong>
            or macOS{" "}
            <strong className="text-foreground">System Settings &gt; Bluetooth</strong>.
            Edgepad has no pairing step of its own; on Linux, use your desktop&apos;s
            Bluetooth settings.
          </li>
          <li>
            Keep Edgepad running in the laptop&apos;s tray, or on a Mac in the menu bar.
            It must already be running before the phone tries to connect: there is no
            discovery or retry on the laptop side.
          </li>
          <li>
            Open Edgepad on the phone and tap the laptop. The first time, the laptop
            asks <strong className="text-foreground">Trust this phone?</strong>; choose
            Trust there. Every paired phone is asked about once, and each one you trust
            is remembered; choose{" "}
            <strong className="text-foreground">Forget trusted phones</strong> in the
            tray or menu-bar menu to forget them all. One phone drives the laptop at a
            time: a trusted phone that connects takes over from the connected one, and
            the phone it replaced is refused for 30 seconds before it can reconnect with
            Retry.
          </li>
          <li>
            The phone remembers the laptop and reconnects when the app opens. If the
            link drops after a completed handshake, the phone retries ten times, two
            seconds apart, and says so. Disconnecting on purpose does not retry.
          </li>
        </ol>
      </Section>

      <Section
        id="using"
        eyebrow="Start here"
        title="Using it"
        lede="The whole phone screen is the control surface. A touch is classified where it starts: inside a corner's zone it belongs to that dial for its whole life, on a media piece or a top-centre button it is a button press, anywhere else it is the trackpad."
      >
        <Sub>The control surface</Sub>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Where</TableHead>
              <TableHead>Touch</TableHead>
              <TableHead>The laptop does</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {SURFACE.map((row) => (
              <TableRow key={`${row.where}:${row.touch}`}>
                <TableCell className="font-mono text-[0.8125rem] whitespace-nowrap">
                  {row.where}
                </TableCell>
                <TableCell className="text-dim text-[0.8125rem]">
                  {row.touch || "—"}
                </TableCell>
                <TableCell className="text-[0.875rem]">{row.laptop}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>

        <Note label="Two fingers are fixed">
          Drag to scroll, pinch to zoom, tap to right-click. As of 2.0.0 these are no
          longer assignable: they are what a hand already expects from a trackpad, and a
          phone that answers them differently reads as broken rather than as configured.
          Three and four fingers stay assignable.
        </Note>

        <Sub>Default three- and four-finger gestures</Sub>
        <P>
          Swapped from Windows&apos; own defaults on purpose. Every one of these is
          changed under <strong>Settings &gt; Gestures</strong>.
        </P>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Fingers</TableHead>
              <TableHead>Gesture</TableHead>
              <TableHead>The laptop does</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {DEFAULT_GESTURES.map((row) => (
              <TableRow key={`${row.fingers}:${row.gesture}`}>
                <TableCell className="font-mono text-[0.8125rem]">
                  {row.fingers}
                </TableCell>
                <TableCell className="text-dim text-[0.8125rem]">
                  {row.gesture}
                </TableCell>
                <TableCell className="text-[0.875rem]">{row.does}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>

        <Sub>The corner dials</Sub>
        <P>
          A dial is a ruler lying along the screen&apos;s edge, bending round the corner
          along the display&apos;s own rounding. The finger slides the ruler under a
          fixed indicator; clockwise raises the value everywhere, like turning a knob.
          Sliding past a small slop arms it — there is no hold — the ticks lengthen
          while armed, and one haptic tick fires per notch that passes. A tap without a
          slide runs the dial&apos;s action instead.
        </P>
        <P>
          Seven kinds compete for four places: volume, brightness, media scrub, zoom,
          app switcher, microphone level and refresh rate. A corner may also hold
          nothing. With <strong>Split corners</strong> on, in{" "}
          <strong>Settings &gt; Corners</strong>, each corner takes a dial on each of
          the two edges it joins, eight places in all. The dial a corner already held
          stays on its side edge, any place can take any dial, and turning it off brings
          the four single dials back as they were. A dial shows the laptop&apos;s real
          value from <C>STATE</C> frames, and dragging sends absolute <C>SET</C> frames.
        </P>

        <Sub>Focusing and locking the pad</Sub>
        <P>
          The third of the four top buttons is the lock. One tap hides the dials and the
          media and leaves the trackpad. A second tap inside the double-tap window locks
          the trackpad instead and puts the dials and the media back, so the phone can
          sit in a pocket or under a palm and answer only its rulers. Any later tap
          returns the whole surface. A locked pad keeps the lock button drawn in the
          full ink, because a surface that silently swallows every touch reads as a
          crash rather than as a mode.
        </P>
        <P>
          The mode lives in the view: it is neither stored nor offered as a setting,
          since a phone that came back up silently locked would read as broken.
        </P>

        <Sub>Shapes</Sub>
        <P>
          Press one finger on the trackpad, hold it still until it ticks, then draw
          without lifting. On lift the stroke is matched against the shapes drawn under{" "}
          <strong>Settings &gt; Shapes</strong>, and the one it matches runs: a laptop
          action, a macro slot, or the pad&apos;s own focus or lock. A stroke that
          matches nothing does nothing, which is the right answer — the alternative is
          the nearest binding firing on a scrawl.
        </P>
        <P>
          Shape mode will not arm while dragging, and stops the moment a second finger
          lands, so scroll, pinch and the three- and four-finger swipes are untouched. A
          phone with no shapes drawn behaves exactly as it did before there were any.
        </P>

        <Sub>Macro buttons</Sub>
        <P>
          Fifteen slots, named from the laptop&apos;s tray or menu-bar menu, or on Linux
          from a text file. Fifteen is what one frame can name — fifteen names at
          sixteen bytes with fourteen separators is 254 of the 255 bytes a <C>TEXT</C>{" "}
          frame carries — so a full grid always arrives labelled rather than trailing
          off into blank buttons. A macro saved in the Windows or Mac editor while the
          phone is connected appears at once.
        </P>
        <P>
          The grid is no longer a fixed 5x3. Every cell is as wide as the widest label
          in it, so no button is a different size from its neighbour, and a row holds as
          many as the screen actually has room for. The count is capped by what fits
          rather than by how many slots are filled, so two macros are two ordinary
          buttons at the left of a full-width row and not two half-screen slabs.
        </P>

        <P>
          <strong>Edit layout</strong> in the Macros bar lets you drag the buttons into
          the order you want, or move each one earlier or later with a screen
          reader&apos;s actions, and pick the grid, such as 3 × 2, from a list that
          leaves out any grid whose buttons would be too small to hit. The order and the
          grid are kept on the phone, and a button still runs the same macro wherever it
          is placed. The buttons sit straight on the background with the press and focus
          feedback kept, and a last row with fewer buttons than the rest sits under the
          middle of them.
        </P>

        <Sub>Security levels and the PIN</Sub>
        <P>
          Set from the laptop&apos;s tray or menu-bar menu under{" "}
          <strong>Security…</strong>, never from the phone. Level 1, the default, is
          the Trust this phone? question and nothing else. Level 2 also asks each phone
          for a six-digit PIN the first time it connects, and level 3 asks on every
          connection.
        </P>
        <P>
          The phone asks on its own keypad, so no keyboard app sees the digits. It never
          stores the PIN, and the screen cannot be captured. A wrong answer shows the
          attempts left; after five wrong answers the laptop refuses that phone for 30
          seconds, doubling with each further five, up to an hour. The counters are kept
          in memory, so quitting the laptop app resets them, and changing the PIN or the
          level makes every phone enter the PIN again.
        </P>
        <P>
          A forgotten PIN can only be reset on the laptop, with Reset PIN and forget
          phones…, which removes the PIN, goes back to level 1 and forgets every trusted
          phone, so each one is asked Trust this phone? again. A six-digit PIN is only a
          million values, so what stops guessing is the lock-out. It does not protect
          against someone who can copy the laptop&apos;s settings file, and the laptop
          stores only a salted hash of the PIN.
        </P>

        <Sub>Controls, annotate, sign and capture</Sub>
        <P>
          <strong>Controls</strong> on the surface opens a grid of Keyboard, Gamepad,
          Capture and Annotate. A laptop shows only what it can do, so a phone shows no
          Annotate tile when the laptop does not offer it, and no Capture tile when it
          cannot capture. Back from the keyboard, the gamepad and Capture returns to
          Controls.
        </P>
        <P>
          Annotate draws on the laptop&apos;s primary display, with the phone as an
          absolute pen: a touch lands at the same place on the laptop. It has a pen, a
          highlighter, a line, an arrow, a rectangle and an ellipse in eight colours,
          plus Undo and Clear. The Sign switch turns the same pad into a signature:
          nothing shows on the laptop, only the pen is offered, and Save signature
          copies a transparent PNG to the clipboard and saves it as{" "}
          <C>signature-yyyyMMdd-HHmmss.png</C> in an <C>Edgepad</C> folder in Pictures,
          never over an earlier one. Annotate works on Windows, on the Mac and on Linux;
          on Linux it is best effort and in testing.
        </P>
        <P>
          Capture presses the laptop&apos;s own screenshot or recording shortcut, and
          has a trackpad under its buttons to drag out the region.
        </P>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Laptop</TableHead>
              <TableHead>Region</TableHead>
              <TableHead>Whole screen</TableHead>
              <TableHead>Recording</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {CAPTURE.map((row) => (
              <TableRow key={row.os}>
                <TableCell className="font-mono text-[0.8125rem] whitespace-nowrap">
                  {row.os}
                </TableCell>
                <TableCell className="text-[0.875rem]">{row.region}</TableCell>
                <TableCell className="text-[0.875rem]">{row.screen}</TableCell>
                <TableCell className="text-[0.875rem]">{row.record}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Section>

      <Section
        id="settings"
        eyebrow="Start here"
        title="Settings"
        lede="One SharedPreferences file holds all of it. Dial positions and the gesture map are stored by enum name, so renumbering an action can never silently remap a corner."
      >
        <P>
          The hub groups its rows by the thing each one configures, not by the kind of
          editor the row opens. Until 3.0.0 it did the opposite, which is how the
          keyboard&apos;s text size and the macro buttons&apos; appearance both ended up
          on a page titled <strong>Background &amp; pattern</strong> — a title that
          described neither.
        </P>
        <div className="grid gap-3 sm:grid-cols-2">
          {[
            {
              title: "Connection",
              body: "The remembered laptop with its round trip, Forget, and whether to reconnect automatically.",
            },
            {
              title: "Surface",
              body: "Corners (which dial each corner holds, or none), Trackpad (pointer and scroll speed, natural scrolling, the three- and four-finger map and the on-screen hints), Shapes (draw one, bind it, delete it), and Dial feel (slide sensitivity — shared or per dial kind — dial length and height, haptic ticks, snapping to round numbers, with a live preview).",
            },
            {
              title: "Controls",
              body: "One page each for the Keyboard, the Gamepad layout, the Macro buttons and the Media layout. A page holds everything about its own control: the keyboard's text size, the gamepad's canvas and layout library, whether a macro button shows its icon alone or its icon and label, and where the three media pieces sit.",
            },
            {
              title: "Appearance",
              body: "Dark or light, upright or sideways, and Background & pattern: a control colour, a colour, gradient or image behind the surface, and a grid, dots or checker over it.",
            },
            {
              title: "Help",
              body: "The five-page guide, shown one page at a time with a drawing or all on one scrolling page. It opens on the first run and again from here. Below it, Documentation opens this page in a browser.",
            },
          ].map((item) => (
            <div
              key={item.title}
              className="bg-card shadow-card rounded-[var(--radius-card)] p-5"
            >
              <p className="text-base font-bold">{item.title}</p>
              <p className="text-dim mt-2 text-[0.9375rem] leading-relaxed">
                {item.body}
              </p>
            </div>
          ))}
        </div>

        <P>
          The theme is the system&apos;s own per-app night mode, set through{" "}
          <C>UiModeManager.setApplicationNightMode</C>, so Edgepad stores no preference
          of its own for it. Media-piece positions are stored as fractions of the
          surface&apos;s width and height, so a layout made in portrait keeps its shape
          in landscape.
        </P>
        <P>
          Orientation is a toggle — upright or sideways — not free rotation. Since 4.0.0
          it applies to every screen except the keyboard, the gamepad and the
          gamepad&apos;s editor, which are sideways whatever it says. From 3.0.0 until
          then it spoke only for the control surface and the two canvases that stand in
          for it, the media layout editor and the shape canvas; every other screen
          followed the phone.
        </P>

        <Sub>Where the laptop keeps things</Sub>
        <CodeBlock
          title="Laptop files"
          code={`%APPDATA%\\Edgepad\\trusted-phone.txt   the trusted phones' Bluetooth addresses, one per line
%LOCALAPPDATA%\\Edgepad\\edgepad.log    connections, refusals, dropped frames,
                                      and input batches Windows refused
~/Library/Application Support/Edgepad/trusted-phone.txt   the same, on a Mac
~/Library/Logs/Edgepad/edgepad.log                        the log, on a Mac
~/.config/edgepad/trusted-phone.txt                       the same, on Linux
~/.local/share/Edgepad/edgepad.log                        the log, on Linux`}
          caption="The log is also in the tray menu, under Open log."
        />
      </Section>
    </>
  );
}
