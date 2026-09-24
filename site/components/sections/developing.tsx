import { CodeBlock } from "@/components/code-block";
import { C, Note, P, Section, Sub } from "@/components/section";
import { Badge } from "@/components/ui/badge";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { REPO } from "@/lib/nav";

const LAYOUT: { path: string; what: string }[] = [
  {
    path: "android/",
    what: "The phone app. Kotlin, Android platform views, no UI libraries. JUnit 4 is the only dependency.",
  },
  {
    path: "windows/",
    what: "The laptop tray app. C# on .NET 10, WinForms for the tray, WinRT for Bluetooth and media, NAudio for volume, WMI for brightness, ViGEmBus for the virtual controller.",
  },
  {
    path: "protocol/",
    what: "The wire format and the id tables as plain-text fixtures. Both test suites read them, so the two apps cannot drift apart.",
  },
  { path: "scripts/", what: "The quality gate and the release-key script." },
  { path: "docs/", what: "Architecture and protocol." },
  { path: "site/", what: "This documentation site. Next.js; not part of either gate." },
];

const ANDROID_PACKAGES: { path: string; what: string; pure: boolean }[] = [
  {
    path: "surface/",
    what: "Gesture recognition, the dials, the corner geometry, the ruler painter, the shape matcher, the pad's focus and lock table.",
    pure: true,
  },
  { path: "protocol/", what: "Frame and id coding.", pure: true },
  {
    path: "link/",
    what: "The RFCOMM socket, coalescing, the laptop-state model, round-trip stats.",
    pure: true,
  },
  {
    path: "gamepad/",
    what: "Gamepad layout, control geometry, which bindings fit which kind of control, the stick and trigger arithmetic, the layout library and its store.",
    pure: true,
  },
  {
    path: "screens/",
    what: "Every screen: guide, devices, settings and its per-control pages, layout editors, shapes, keyboard, gamepad, macros.",
    pure: false,
  },
];

const WINDOWS_FOLDERS: { path: string; what: string; pure: boolean }[] = [
  { path: "Protocol/", what: "Frame and id coding.", pure: true },
  {
    path: "Dispatch/",
    what: "The dispatcher: what each action id does, and every drop path.",
    pure: true,
  },
  {
    path: "Bluetooth/",
    what: "RfcommServer and Session. Needs a live socket to verify.",
    pure: false,
  },
  {
    path: "Injection/",
    what: "InputBuilder, InputInjector and the SendInput interop.",
    pure: false,
  },
  {
    path: "Controls/",
    what: "Audio, brightness, display modes, media sessions, the on-screen level overlay.",
    pure: false,
  },
  {
    path: "Gamepad/",
    what: "VirtualPad: the ViGEmBus controller, and the PAD_STATUS answer when there is no driver.",
    pure: false,
  },
  { path: "Trust/", what: "TrustStore: trust on first use.", pure: false },
  { path: "Macros/", what: "The macro list and its tray-menu editor.", pure: false },
];

export function DevelopingSections({ protocolVersion }: { protocolVersion: number }) {
  return (
    <>
      <Section
        id="repo"
        eyebrow="Developing"
        title="Repository layout"
        lede="A monorepo. One slug, one repository, two programs that must ship together."
      >
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="w-32">Path</TableHead>
              <TableHead>What</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {LAYOUT.map((row) => (
              <TableRow key={row.path}>
                <TableCell className="font-mono text-[0.8125rem] whitespace-nowrap">
                  {row.path}
                </TableCell>
                <TableCell className="text-[0.875rem]">{row.what}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>

        <Sub>Inside the phone app</Sub>
        <P>
          Under <C>android/app/src/main/java/me/akshitbansal/edgepad</C>. The packages
          marked pure have no Android platform types and run as plain JVM unit tests.
        </P>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="w-28">Package</TableHead>
              <TableHead>What</TableHead>
              <TableHead className="w-28">Testable</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {ANDROID_PACKAGES.map((row) => (
              <TableRow key={row.path}>
                <TableCell className="font-mono text-[0.8125rem] whitespace-nowrap">
                  {row.path}
                </TableCell>
                <TableCell className="text-[0.875rem]">{row.what}</TableCell>
                <TableCell>
                  <Badge variant={row.pure ? "solid" : "outline"}>
                    {row.pure ? "Pure" : "Needs a device"}
                  </Badge>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>

        <Sub>Inside the laptop app</Sub>
        <P>
          Under <C>windows/src/Edgepad</C>.
        </P>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="w-28">Folder</TableHead>
              <TableHead>What</TableHead>
              <TableHead className="w-28">Testable</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {WINDOWS_FOLDERS.map((row) => (
              <TableRow key={row.path}>
                <TableCell className="font-mono text-[0.8125rem] whitespace-nowrap">
                  {row.path}
                </TableCell>
                <TableCell className="text-[0.875rem]">{row.what}</TableCell>
                <TableCell>
                  <Badge variant={row.pure ? "solid" : "outline"}>
                    {row.pure ? "Pure" : "Needs hardware"}
                  </Badge>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Section>

      <Section
        id="prerequisites"
        eyebrow="Developing"
        title="Prerequisites"
        lede="Either half can be worked on alone. You do not need both toolchains to contribute to one app."
      >
        <div className="grid gap-px md:grid-cols-2">
          <div className="border-line border p-4">
            <p className="font-mono text-sm font-medium">Phone app</p>
            <ul className="text-dim mt-3 space-y-2 text-[0.9375rem] leading-relaxed">
              <li>JDK 17.</li>
              <li>The Android SDK with platform 37 and build tools 37.0.0.</li>
              <li>
                The Gradle wrapper fetches Gradle itself — nothing to install for that.
              </li>
            </ul>
          </div>
          <div className="border-line border p-4">
            <p className="font-mono text-sm font-medium">Laptop app</p>
            <ul className="text-dim mt-3 space-y-2 text-[0.9375rem] leading-relaxed">
              <li>
                The .NET SDK version pinned in <C>windows/global.json</C>: 10.0.401,
                with <C>rollForward: latestFeature</C>.
              </li>
              <li>
                Windows 10 version 2004 or later, because the app builds against the
                Windows SDK projection for Bluetooth.
              </li>
            </ul>
          </div>
        </div>

        <Note label="Running the two against each other">
          This needs a Windows machine and an Android phone already paired in Windows
          Settings, and it is the one part of the repository that cannot be verified
          from a pull request alone. Without both devices you cannot exercise the
          Bluetooth handshake, trust-on-first-use, input injection, brightness or volume
          control, or media-session reading.
        </Note>
      </Section>

      <Section
        id="gate"
        eyebrow="Developing"
        title="The quality gate"
        lede="One script checks both halves: format, lint, build, test. CI runs the same script in its non-mutating mode, so the local gate and CI cannot disagree."
      >
        <CodeBlock
          title="The gate"
          code={`pwsh scripts/check.ps1                  # formats in place, then checks everything
pwsh scripts/check.ps1 -Only android    # ktlint, Android lint, unit tests
pwsh scripts/check.ps1 -Only windows    # dotnet format, build, tests
pwsh scripts/check.ps1 -Ci              # what CI runs: fails on unformatted code`}
          caption="It writes locally because fixing your formatting is useful. CI must not write to your branch, so it fails instead of quietly reformatting the pull request."
        />

        <Sub>What it actually runs</Sub>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="w-24">Half</TableHead>
              <TableHead>Step</TableHead>
              <TableHead>Local / CI</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow>
              <TableCell className="font-mono text-[0.8125rem]">android</TableCell>
              <TableCell className="text-[0.875rem]">Kotlin formatting</TableCell>
              <TableCell className="font-mono text-[0.8125rem]">
                ktlintFormat / ktlintCheck
              </TableCell>
            </TableRow>
            <TableRow>
              <TableCell className="font-mono text-[0.8125rem]">android</TableCell>
              <TableCell className="text-[0.875rem]">
                Android lint, then the unit tests
              </TableCell>
              <TableCell className="font-mono text-[0.8125rem]">same</TableCell>
            </TableRow>
            <TableRow>
              <TableCell className="font-mono text-[0.8125rem]">windows</TableCell>
              <TableCell className="text-[0.875rem]">C# formatting</TableCell>
              <TableCell className="font-mono text-[0.8125rem]">
                dotnet format / --verify-no-changes
              </TableCell>
            </TableRow>
            <TableRow>
              <TableCell className="font-mono text-[0.8125rem]">windows</TableCell>
              <TableCell className="text-[0.875rem]">Build, then test</TableCell>
              <TableCell className="font-mono text-[0.8125rem]">
                dotnet build -warnaserror; dotnet test
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>

        <Note label="Three rejections that are not obvious from the error">
          Kotlin compiles with <C>allWarningsAsErrors</C>. Android lint runs with{" "}
          <C>warningsAsErrors</C>, so an unused string resource fails the build rather
          than warning. The C# build passes <C>-warnaserror</C>, and{" "}
          <C>Directory.Build.props</C> sets <C>TreatWarningsAsErrors</C>,{" "}
          <C>AnalysisLevel: latest-recommended</C> and <C>EnforceCodeStyleInBuild</C>.
          Do not suppress a lint finding to get green.
        </Note>

        <Sub>What CI does on top</Sub>
        <P>
          <C>.github/workflows/ci.yml</C> runs on every push to <C>main</C> and every
          pull request, in two jobs: Android on <C>ubuntu-latest</C> with Temurin 17 and
          Gradle wrapper validation, Windows on <C>windows-latest</C> with the SDK from{" "}
          <C>global.json</C>. Each runs the gate in <C>-Ci</C> mode and then uploads an
          artifact — a debug APK and the self-contained exe.
        </P>
      </Section>

      <Section
        id="run-locally"
        eyebrow="Developing"
        title="Running it locally"
        lede="Build each half directly, without the gate, while iterating."
      >
        <Sub>Laptop</Sub>
        <CodeBlock
          title="Run the tray app"
          code={`dotnet run --project windows/src/Edgepad`}
          caption="It must already be running before the phone tries to connect: there is no discovery or retry on the laptop side, only the phone reconnects."
        />
        <CodeBlock
          title="Build a release-shaped exe"
          code={`dotnet publish windows/src/Edgepad -c Release -r win-x64 --self-contained \\
  -p:PublishSingleFile=true \\
  -p:IncludeNativeLibrariesForSelfExtract=true \\
  -p:EnableCompressionInSingleFile=true`}
          caption="A self-contained single file, the same shape a release ships. Compression takes it from roughly 143 MB to 58.5 MB."
        />
        <CodeBlock
          title="Build only"
          code={`cd windows && dotnet build Edgepad.slnx -c Release`}
        />

        <Sub>Phone</Sub>
        <CodeBlock
          title="Install a debug build on a connected device"
          code={`cd android && ./gradlew installDebug`}
          caption="Uninstall any release build first: debug builds are versioned 0.0.0-dev and signed with the debug key, so they will not install over a release."
        />
        <CodeBlock title="Build only" code={`cd android && ./gradlew assembleDebug`} />

        <Sub>Together</Sub>
        <ol className="text-dim max-w-[68ch] list-decimal space-y-2 pl-5 text-[0.9375rem] leading-relaxed">
          <li>Pair the phone and the laptop in Windows Settings.</li>
          <li>
            Start the tray app with <C>dotnet run</C>.
          </li>
          <li>Install and open the phone app, then tap the laptop.</li>
          <li>
            Read <C>%LOCALAPPDATA%\Edgepad\edgepad.log</C> for dropped frames or refused
            input batches.
          </li>
        </ol>

        <Sub>This site</Sub>
        <CodeBlock
          title="Documentation site"
          code={`cd site
npm install
npm run dev        # http://localhost:3000
npm run check      # Biome -> ESLint -> tsc, the site's own gate
npm run check:ci   # the same, non-mutating`}
          caption="Biome owns formatting and import order, ESLint owns React hooks and the Next.js rules, tsc owns types. It reads ../protocol/*.txt at build time, so it must be built from inside the repository. It is not part of either app's quality gate and CI does not run it."
        />
      </Section>

      <Section
        id="tests"
        eyebrow="Developing"
        title="Tests"
        lede="Both suites read the same fixtures. Neither sends real input, opens a real Bluetooth socket, or touches a device."
      >
        <CodeBlock
          title="Run them"
          code={`cd android && ./gradlew testDebugUnitTest
cd windows && dotnet test --solution Edgepad.slnx`}
        />

        <Sub>The fixture layer</Sub>
        <P>
          <C>protocol/frames.txt</C> holds every frame type as golden bytes, and{" "}
          <C>protocol/actions.txt</C> the action, control and text-kind ids.{" "}
          <C>FrameFixtureTest</C> and <C>IdsFixtureTest</C> on the phone, and{" "}
          <C>FrameFixtureTests</C> and <C>IdsFixtureTests</C> on the laptop, parse those
          same files at run time rather than copying them into either codebase.
        </P>
        <P>
          A protocol change therefore always touches a fixture file and both codecs in
          the same commit. Change one side&apos;s codec without the other and that
          side&apos;s test fails immediately.
        </P>

        <Sub>Above the fixtures</Sub>
        <div className="grid gap-px md:grid-cols-2">
          <div className="border-line border p-4">
            <p className="font-mono text-sm font-medium">Android</p>
            <p className="text-dim mt-2 text-[0.9375rem] leading-relaxed">
              The gesture recogniser and the whole finger table, assignable actions,
              natural scrolling, the dials (arming, slop, snapping, steppers, haptic
              notches), the corner and perimeter geometry, coalescing, round-trip stats,
              the gamepad layout, the modifier latch and the laptop-state model. 3.0.0
              adds <C>ShapesTest</C> for the unistroke matcher, <C>PadAxisTest</C> for
              the dead zone and the stick scaling, <C>PadModeTest</C> for the lock
              button&apos;s transitions, and <C>GamepadStoreTest</C> for what a name
              collision costs and what is left playing after a deletion. All pure
              Kotlin, so it runs with no emulator.
            </p>
          </div>
          <div className="border-line border p-4">
            <p className="font-mono text-sm font-medium">Windows</p>
            <p className="text-dim mt-2 text-[0.9375rem] leading-relaxed">
              The dispatcher&apos;s drop paths, input batching, trust on first use, the
              macro store, display modes, the level overlay, the media-session title
              mapping, and <C>VirtualPadTests</C>.
            </p>
          </div>
        </div>

        <Note label="How a test covers a driver it must not install">
          <C>VirtualPad</C> takes the function that opens its ViGEmBus handle, so a test
          hands in one that throws what an absent driver throws. Nothing else pins the
          no-driver answer: whether the machine running the suite has ViGEmBus is not
          ours to choose, and a test that opened the real client on a machine that does
          would plug a controller into whoever ran it.
        </Note>

        <Note label="Where to start without a device">
          The protocol codecs, the gesture recogniser, the dial geometry and the
          dispatcher&apos;s drop paths are all pure and cheap to test. That is where a
          first change belongs.
        </Note>
      </Section>

      <Section
        id="contributing"
        eyebrow="Developing"
        title="Contributing"
        lede="Edgepad is small on purpose. The bar for a change is that it makes the product better for someone using it, and that it passes the same gate CI runs."
      >
        <ol className="text-dim max-w-[70ch] list-decimal space-y-3 pl-5 text-[0.9375rem] leading-relaxed">
          <li>
            Branch from <C>main</C>.
          </li>
          <li>
            <strong className="text-foreground">Keep the two apps in step.</strong> A
            protocol change edits <C>protocol/frames.txt</C> or{" "}
            <C>protocol/actions.txt</C>, both codecs or both enums, and bumps the
            protocol version on both sides — all in one commit.
          </li>
          <li>
            Add or change a test when you add logic or a branch, or fix a bug. Prefer
            the pure layers over anything that needs a device.
          </li>
          <li>
            Run <C>pwsh scripts/check.ps1</C> until it is clean.
          </li>
          <li>
            Commit with a{" "}
            <a
              href="https://www.conventionalcommits.org/en/v1.0.0/"
              target="_blank"
              rel="noreferrer noopener"
              className="text-foreground underline underline-offset-4"
            >
              Conventional Commits
            </a>{" "}
            subject and a body saying what changed and why, including what you rejected.
          </li>
          <li>
            Add a line under <strong className="text-foreground">Unreleased</strong> in{" "}
            <C>CHANGELOG.md</C>.
          </li>
          <li>Open a pull request.</li>
        </ol>

        <Sub>Style</Sub>
        <ul className="text-dim max-w-[70ch] list-disc space-y-2 pl-5 text-[0.9375rem] leading-relaxed">
          <li>
            No UI libraries on the phone. No third-party packages on the laptop beyond
            NAudio, <C>System.Management</C> and <C>Nefarius.ViGEm.Client</C>. A new
            dependency needs a reason in the pull request.
          </li>
          <li>Constants are named. A number that appears twice is a constant.</li>
          <li>Doc comments say why, not what the next line already says.</li>
          <li>
            Nothing runs on the input path that could block: no logging, no allocation
            while drawing, no I/O on the session thread beyond the socket.
          </li>
        </ul>

        <Note label="A lint rule worth knowing">
          Android lint&apos;s <C>DrawAllocation</C> only inspects a method literally
          named <C>onDraw</C>. The gamepad allocated a rectangle per d-pad cell every
          frame for several releases and was never reported, because the allocation was
          one call deeper. Read the draw path, do not rely on the linter for it.
        </Note>

        <P>
          Bug reports and feature requests use the issue templates in{" "}
          <C>.github/ISSUE_TEMPLATE</C>. Open an issue for anything bigger than a fix,
          so the design can be talked through before code exists. Report anything
          exploitable through a{" "}
          <a
            href={`${REPO}/security/advisories/new`}
            target="_blank"
            rel="noreferrer noopener"
            className="text-foreground underline underline-offset-4"
          >
            security advisory
          </a>
          , not a public issue.
        </P>
      </Section>

      <Section
        id="releases"
        eyebrow="Developing"
        title="Releases"
        lede="Releases are built by CI, not by hand. Pushing a version tag is the whole process."
      >
        <CodeBlock
          title="Cut a release"
          code={`# 1. Update the version table in docs/PROTOCOL.md if the protocol changed.
# 2. Move Unreleased in CHANGELOG.md under the new version.
git tag v3.0.0
git push origin v3.0.0`}
        />
        <P>
          <C>.github/workflows/release.yml</C> runs both quality gates, builds a signed
          release APK and a compressed self-contained exe, and attaches them to a GitHub
          Release under the stable names <C>Edgepad.apk</C> and <C>Edgepad.exe</C>, so{" "}
          <C>releases/latest/download/&lt;file&gt;</C> always resolves to the newest
          build.
        </P>
        <P>
          Versioned file names were rejected because they break that permanent link, and
          CI artifacts were rejected as a distribution channel because they expire and
          need a GitHub login. <C>versionName</C> comes from the tag; <C>versionCode</C>{" "}
          comes from the workflow run number, which only increases, so an update always
          installs over the previous one.
        </P>

        <Note label="Release builds are signed">
          The APK published on a release is signed by CI, and every release is signed
          with the same key. That is what lets your phone accept an update as genuinely
          the next version of the app it already has, and refuse one that is not.
        </Note>

        <Note label="Protocol version and release version are different numbers">
          <p className="mb-3">
            The wire protocol sits at version {protocolVersion} and, from 1.0, only
            moves in a major release. A major release does not oblige it to move. 2.0.0
            was one that did <em>not</em>: macro buttons and the refresh-rate dial were
            new ids in tables that already existed, and an unknown id is dropped rather
            than erroring, so a 2.0.0 half and a 1.0.0 half still speak to each other.
          </p>
          <p>
            3.0.0 did move it, to {protocolVersion}, because <C>PAD_STATE</C> is a new
            frame type and an unknown type byte closes the connection. Only a new frame{" "}
            <strong>type</strong> forces a bump, and a 3.0.0 half genuinely refuses a
            2.x one at the handshake.
          </p>
        </Note>
      </Section>
    </>
  );
}
