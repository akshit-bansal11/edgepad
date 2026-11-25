/**
 * Reads the repository's golden protocol fixtures at build time.
 *
 * `protocol/frames.txt` and `protocol/actions.txt` are the authoritative tables:
 * both the Kotlin and the C# test suites parse the same two files at run time, so
 * a codec that drifts fails a test rather than shipping. This site parses them too
 * rather than keeping a third hand-typed copy that nothing checks.
 *
 * Prose (what a frame means, what an action does) cannot come from the fixtures and
 * is written out below from docs/PROTOCOL.md. The merge is left-joined from the
 * fixture, never from the prose: a row present in the fixture with no description is
 * rendered and marked `undocumented`, never dropped.
 */
import { readFileSync } from "node:fs";
import path from "node:path";

const PROTOCOL_DIR = path.join(process.cwd(), "..", "protocol");

/** Which way a frame travels. */
export type Direction = "phone-to-laptop" | "laptop-to-phone" | "both";

export type FrameExample = {
  /** The fields as the fixture writes them, e.g. "5 -3". */
  fields: string;
  /** The exact bytes both codecs must produce, e.g. "10 05 00 fd ff". */
  bytes: string;
};

/**
 * One frame of the protocol table: its fixture examples, joined with the prose this
 * file holds for it. `documented` is false when the fixture has a frame this file has
 * no prose for yet.
 */
export type FrameRow = {
  name: string;
  /** First byte of the fixture's own bytes, so the type can never be mistyped here. */
  type: string;
  payload: string | null;
  direction: Direction | null;
  meaning: string | null;
  examples: FrameExample[];
  documented: boolean;
};

/**
 * One row of an id table (actions, controls, TEXT kinds and the rest), as
 * `protocol/actions.txt` lists it.
 */
export type IdRow = {
  /** As the fixture writes it: a number, or for PAD_STATUS the token on the wire. */
  id: string;
  name: string;
  description: string | null;
  documented: boolean;
};

/** Everything the protocol pages show, read from the two fixture files. */
export type ProtocolTables = {
  version: number;
  serviceId: string;
  frames: FrameRow[];
  actions: IdRow[];
  controls: IdRow[];
  textKinds: IdRow[];
  padButtons: IdRow[];
  padStatuses: IdRow[];
  features: IdRow[];
  signStatuses: IdRow[];
  /** The annotation colours; each description is the fixture's hex. */
  palette: IdRow[];
  /** Frames described here that no fixture line covers. Drift, surfaced on the page. */
  undescribedInFixture: string[];
};

/** Prose for each frame, from docs/PROTOCOL.md. Keyed by the fixture's own names. */
const FRAME_DOCS: Record<
  string,
  { payload: string; direction: Direction; meaning: string }
> = {
  HELLO: {
    payload: "magic EDGP, version u8",
    direction: "phone-to-laptop",
    meaning: "Opens a session.",
  },
  HELLO_ACK: {
    payload: "version u8",
    direction: "laptop-to-phone",
    meaning:
      "Accepts it. On a version mismatch the laptop sends its own version, then closes, so the phone can say which side needs updating.",
  },
  MOVE: {
    payload: "dx i16, dy i16",
    direction: "phone-to-laptop",
    meaning:
      "Move the pointer, in laptop pixels, before Windows' own pointer acceleration.",
  },
  BUTTON: {
    payload: "button u8, down u8",
    direction: "phone-to-laptop",
    meaning: "0 left, 1 right, 2 middle. down is 1 or 0.",
  },
  SCROLL: {
    payload: "dx i16, dy i16",
    direction: "phone-to-laptop",
    meaning: "Wheel units. 120 is one notch; positive dy is wheel forward.",
  },
  ZOOM: {
    payload: "delta i16",
    direction: "phone-to-laptop",
    meaning: "Ctrl+wheel units.",
  },
  ACTION: {
    payload: "id u8",
    direction: "phone-to-laptop",
    meaning: "Run one action from the laptop’s table.",
  },
  SET: {
    payload: "control u8, value u8",
    direction: "phone-to-laptop",
    meaning: "Set a control to 0-100. The laptop drops anything above 100.",
  },
  KEY: {
    payload: "code u16, down u8",
    direction: "phone-to-laptop",
    meaning:
      "Press or release one key, by Windows virtual-key code. This is what the phone’s keyboard screen sends.",
  },
  PING: {
    payload: "t i64",
    direction: "phone-to-laptop",
    meaning: "The phone’s clock, in nanoseconds.",
  },
  PONG: {
    payload: "t i64",
    direction: "laptop-to-phone",
    meaning:
      "The same value echoed back, which is what the round-trip readout measures.",
  },
  STATE: {
    payload: "control u8, value u8, flags u8",
    direction: "laptop-to-phone",
    meaning:
      "A control’s current value. Flags bit 0 is muted for audio controls, playing for media position.",
  },
  TEXT: {
    payload: "kind u8, length u8, UTF-8 bytes",
    direction: "both",
    meaning:
      "The only variable-length frame: up to 255 bytes, never split inside a character.",
  },
  PIN_REQUEST: {
    payload: "attemptsLeft u8, lockedSeconds u16",
    direction: "laptop-to-phone",
    meaning:
      "Asks for the PIN, or says the phone is locked out and for how long. Only between the trust question and HELLO_ACK.",
  },
  PIN_ANSWER: {
    payload: "six bytes, each 0 to 9",
    direction: "phone-to-laptop",
    meaning:
      "The PIN, one digit value per byte rather than ASCII. Only as the answer to PIN_REQUEST; a byte above 9 closes the link.",
  },
  PAD_STATE: {
    payload: "buttons u16, lt u8, rt u8, lx i16, ly i16, rx i16, ry i16",
    direction: "phone-to-laptop",
    meaning:
      "The whole virtual controller in one frame, laid out like Windows' own XINPUT_GAMEPAD so the laptop copies the fields rather than translating them. Triggers are 0-255, sticks -32768 to 32767, positive up and right. A snapshot and not a change, so a backlog of them collapses to the newest.",
  },
  PEN: {
    payload: "x u16, y u16, state u8",
    direction: "phone-to-laptop",
    meaning:
      "One point of a pen stroke: state 0 is up, 1 down, 2 move. x and y span the whole u16 range, 0 the left or top edge of the laptop’s screen and 65535 the right or bottom. A state above 2 decodes and is dropped and counted.",
  },
  FILE_BEGIN: {
    payload: "fileType u8, length u8, size u32, UTF-8 name",
    direction: "laptop-to-phone",
    meaning:
      "Starts sending a capture to the phone, when the phone asked for captures to be saved there. TEXT’s header shape, so the length counts the size and the name. The type is an id from the FILE_TYPE table, never a MIME string.",
  },
  FILE_CHUNK: {
    payload: "seq u8, length u8, bytes",
    direction: "laptop-to-phone",
    meaning:
      "Up to 255 bytes of the file. seq is the chunk’s index mod 256, so a lost chunk shows. Paced, and slower while the phone is sending input, so the trackpad stays responsive.",
  },
  FILE_END: {
    payload: "sha256, 32 bytes",
    direction: "laptop-to-phone",
    meaning:
      "The SHA-256 of the whole file. The phone keeps the file only if it matches.",
  },
  FILE_RESULT: {
    payload: "status u8",
    direction: "phone-to-laptop",
    meaning:
      "The phone’s answer: 0 saved, 1 bad hash, 2 rejected, 3 write failed. Only 0 lets the laptop delete its copy.",
  },
  BULK_HELLO: {
    payload: "magic EDGP, version u8",
    direction: "phone-to-laptop",
    meaning:
      "HELLO’s shape under its own type, the first frame of the second socket a phone opens for file transfers. It can only attach to a phone session that is live right now over the same link; it never creates a session and asks no trust question or PIN.",
  },
  XFER_BEGIN: {
    payload: "id u8, length u8, size u64, UTF-8 name",
    direction: "both",
    meaning:
      "Starts a file transfer in either direction. The id is 1 to 255 and chosen by the sender; the length counts the size and the name.",
  },
  XFER_CHUNK: {
    payload: "id u8, length u8, offset u64, 1 to 247 bytes",
    direction: "both",
    meaning:
      "One piece of the file. The offset is the file offset of its first byte, so a restart after an interruption is exact.",
  },
  XFER_END: {
    payload: "id u8, sha256, 32 bytes",
    direction: "both",
    meaning:
      "The SHA-256 of the whole file. The receiver keeps the file only if it matches.",
  },
  XFER_STATUS: {
    payload: "id u8, status u8, offset u64",
    direction: "both",
    meaning:
      "How the transfer stands. The receiver sends READY, PROGRESS and the final verdict; the sender may send CANCELLED. READY’s offset is where the sender starts.",
  },
};

const ACTION_DOCS: Record<string, string> = {
  MUTE_TOGGLE: "Mute key.",
  PLAY_PAUSE: "Media play/pause key.",
  NEXT_TRACK: "Media next key.",
  PREVIOUS_TRACK: "Media previous key.",
  MIC_MUTE_TOGGLE: "Toggles the default microphone’s mute.",
  LOCK: "LockWorkStation. Win+L cannot be injected.",
  TASK_VIEW: "Win+Tab.",
  SHOW_DESKTOP: "Win+D.",
  SEARCH: "Win+S.",
  NOTIFICATIONS: "Win+N.",
  DESKTOP_LEFT: "Win+Ctrl+Left.",
  DESKTOP_RIGHT: "Win+Ctrl+Right.",
  APP_SWITCH_BEGIN:
    "Holds Alt and presses Tab. Alt stays down until APP_SWITCH_END or the session ends.",
  APP_SWITCH_NEXT: "Tab, only while Alt is held.",
  APP_SWITCH_PREVIOUS: "Shift+Tab, only while Alt is held.",
  APP_SWITCH_END: "Releases Alt.",
  ZOOM_RESET: "Ctrl+0.",
  VOLUME_UP: "Volume-up key.",
  VOLUME_DOWN: "Volume-down key.",
  BRIGHTNESS_UP: "The panel’s brightness, plus 10.",
  BRIGHTNESS_DOWN: "The panel’s brightness, minus 10.",
  PAD_ATTACH:
    "Asks the laptop to plug in its virtual Xbox controller. The laptop answers with TEXT 8, so the phone learns whether it may send PAD_STATE or must fall back to the keyboard.",
  PAD_DETACH:
    "Asks the laptop to unplug the virtual controller. Sent when the gamepad screen closes; the laptop also unplugs it by itself when the session ends.",
  CAPTURE_REGION:
    "Starts a region screenshot with the laptop’s own tool: Win+Shift+S on Windows, Cmd+Shift+4 on a Mac, Print on Linux. The drag happens on the laptop’s screen.",
  CAPTURE_SCREEN:
    "Takes a screenshot of the whole screen: PrtScn on Windows, Cmd+Shift+3 on a Mac, Print on Linux.",
  CAPTURE_RECORD:
    "Starts screen recording: Win+Shift+R on Windows, which needs a recent Snipping Tool and is unverified, Cmd+Shift+5 on a Mac, which opens the capture toolbar, and on Linux the desktop’s own shortcut, unverified (Ctrl+Shift+Alt+R on GNOME, Meta+Alt+R on KDE).",
  CAPTURE_CANCEL:
    "Sent when the phone leaves its capture screen. Presses Escape on the laptop, only while a region or recording overlay it opened may still be open (8 seconds).",
  ANNOTATE_BEGIN:
    "Shows the annotation overlay on the laptop’s primary display. PEN and the tool actions are dropped until it is shown or a signing surface is open.",
  ANNOTATE_END:
    "Removes the overlay and discards its strokes. A session ending does the same.",
  ANNOTATE_UNDO: "Removes the last stroke.",
  ANNOTATE_CLEAR: "Removes every stroke.",
  TOOL_PEN: "The next stroke is a pen line, 0.4% of the screen height wide.",
  TOOL_HIGHLIGHTER:
    "The next stroke is a highlighter, 2% of the screen height wide at 35% opacity, stroked once per path so a loop does not darken where it crosses itself.",
  TOOL_LINE:
    "The next stroke is a straight line from the down point to the latest move.",
  TOOL_ARROW: "As the line, with an arrow head four times the pen width at the end.",
  TOOL_RECTANGLE:
    "The next stroke is a rectangle from the down point to the latest move.",
  TOOL_ELLIPSE: "The next stroke is an ellipse from the down point to the latest move.",
  SIGN_BEGIN:
    "Opens a signing surface that shows nothing on the laptop and accepts only the pen tool.",
  SIGN_SAVE:
    "Exports the strokes as a transparent PNG cropped to the ink, to the clipboard and to an Edgepad folder in Pictures, never over an earlier file, then answers with TEXT 11.",
  TOOL_LASER:
    "The next stroke is a laser dot that follows the finger and leaves nothing behind. Never stored. The phone offers it only for a laptop that names annotate.",
  TOOL_LASER_TRAIL:
    "The next stroke is a laser dot with a short red trail behind it. Never stored. Both lasers take their size from PEN_WIDTH: the dot's radius is 0.06% of the screen's height per step, so 20 is 1.2%.",
  END_SLEEP:
    "Arms the laptop to sleep when the current media ends. The laptop reports what is armed as TEXT 25.",
  END_SHUTDOWN:
    "Arms the laptop to shut down when the current media ends. The laptop reports what is armed as TEXT 25.",
  END_CANCEL: "Disarms the end-of-media action.",
  MACRO_BASE:
    "The first of 32 reserved macro slots (ids 64-95). Slot n runs as MACRO_BASE + n. The phone sends the index; the laptop’s own list decides what it opens.",
};

const CONTROL_DOCS: Record<string, string> = {
  VOLUME:
    "SET sets the default speakers' level; STATE reports the level and a muted flag.",
  BRIGHTNESS:
    "SET sets the built-in panel’s brightness through WMI; STATE reports the level.",
  MIC_LEVEL:
    "SET sets the default microphone’s level; STATE reports the level and a muted flag.",
  MEDIA_POSITION:
    "SET seeks the current track to that percent, where the player allows it; STATE reports the percent and a playing flag.",
  REFRESH_RATE:
    "SET switches the display to the rate at that index of the TEXT 4 list; STATE reports the current rate’s index. Never a rate in hertz.",
  PEN_COLOR:
    "SET picks the stroke colour by index into the eight-colour palette in protocol/actions.txt. An index of 8 or more is dropped and counted. A colour outside the palette is TEXT 17.",
  CAPTURE_TARGET:
    "Where a capture ends up: 0 the laptop keeps it, 1 the laptop sends it to the phone and deletes its copy only once the phone says it saved it. Sent on connect and whenever the setting changes.",
  PEN_WIDTH:
    "SET sets the width of new pen-tool strokes, 1 to 100, as 0.02% of the screen’s height per step: 5 is 0.1%, 20 is the default 0.4% and 100 is 2.0%. The highlighter is always 5 times the pen’s width. 0 or more than 100 is dropped and counted. Forgotten when the session ends.",
  NOTIFY:
    "SET 1 forwards the laptop’s notifications to this link, 0 stops it. Forgotten when the session ends; any other value is dropped and counted. The phone offers it only for a laptop that names notify.",
  FOREGROUND_WATCH:
    "SET 1 lets this link hear which app is in front (TEXT 21), 0 stops it. Forgotten when the session ends; any other value is dropped and counted. The phone offers it only for a laptop that names foreground.",
  PEN_OPACITY:
    "SET sets the opacity of new strokes in percent, 10 to 100. The highlighter multiplies its own 35% by it. Anything else is dropped and counted. Forgotten when the session ends.",
  DASH_WATCH:
    "SET is a bitmask of what the phone draws right now: 1 widgets that need system numbers, 2 the System screen, 4 the weather widget, 8 an AI widget or screen, 16 the GitHub screen. 0 to 31; more is dropped and counted. Forgotten when the session ends. It is the only thing that makes the laptop sample, read usage files or poll.",
};

const TEXT_DOCS: Record<string, string> = {
  NOW_PLAYING: "What is playing. Laptop to phone.",
  APP: "The app playing it. Laptop to phone.",
  TIMELINE: "The timeline as seconds/length, such as 84/227. Laptop to phone.",
  TYPE: "Text to type, where \\b is backspace and \\n is enter. Phone to laptop.",
  REFRESH_RATES:
    "The display’s available refresh rates as 60/120/144, in the order CONTROL 4 indexes them. Laptop to phone.",
  MACROS:
    "The laptop’s macro names as Chrome/Spotify/Notes, in the order MACRO_BASE indexes them. A blank name keeps its slot. Laptop to phone.",
  MACRO_ICON:
    "One piece of one macro’s icon as slot/chunk/chunks/base64, the base64 being part of a PNG. An icon does not fit the 255 bytes a payload holds, so it arrives in pieces. Laptop to phone.",
  WANT_ICONS:
    "Asks for the macro icons, with an empty payload. Sent when the phone opens the macro grid; nothing is pushed unasked. Phone to laptop.",
  PAD_STATUS:
    "Whether a virtual controller can be offered, as one of ready, no-driver or attach-failed. Sent after the handshake and in answer to PAD_ATTACH and PAD_DETACH. An unknown token reads as no-driver, so the phone falls back to the keyboard rather than sending input nothing receives. Laptop to phone.",
  FEATURES:
    "The optional features this laptop has, as tokens joined by a slash such as annotate/capture. Sent once after the handshake. A feature that is not named is off, and a token the phone does not know is ignored. Laptop to phone.",
  SCREEN_SIZE:
    "The primary display in pixels as 1920/1080. Sent after the handshake and whenever the display changes, so the phone can shape its drawing surface to the same ratio. Laptop to phone.",
  SIGN_STATUS:
    "How a SIGN_SAVE ended, as one of saved, empty or failed. Laptop to phone.",
  CAPTURE_STATUS:
    "Why a capture the phone asked for stayed on the laptop, as one of too-large, not-found or failed. Laptop to phone.",
  FOREGROUND_APP:
    "The app in front as token/name, split on the first slash. Sent only after CONTROL 11 is 1, on change, and never logged. Empty means nothing is known. Laptop to phone.",
  MACRO_CAP:
    "How many macro slots the laptop keeps, in decimal. Empty or never sent means the phone’s own default. Laptop to phone.",
  CAPTURE_STATE:
    "Whether the laptop is recording the screen, as idle or recording. Best effort: a laptop that cannot tell sends nothing. Laptop to phone.",
  UNAVAILABLE:
    "The features the laptop cannot offer right now and why, as feature:reason entries joined by a slash. A reason is one of unsupported, off, no-tool or no-permission; an unknown one reads as unsupported. Laptop to phone.",
  END_ACTION:
    "What the end-of-media action is armed to do, as none, sleep or shutdown. Sent on change. Laptop to phone.",
  NOW_PLAYING_FULL:
    "The title line of what is playing before it is cleaned for TEXT 0, sent only when it differs from TEXT 0. Laptop to phone.",
  PEN_RGB:
    "A stroke colour as exactly six hex digits, RRGGBB, either case. Anything else, a leading # included, is dropped and counted. It sets the colour until the next PEN_COLOR or PEN_RGB. Phone to laptop.",
  STATS:
    "System numbers as cpu/ram/gpu/vram/down/up/ping/loss/ctemp/gtemp/gpower: percents, KiB/s, milliseconds, degrees C and watts, -1 for unknown. Once a second while the phone watches them; not re-sent when identical. Laptop to phone.",
  STATS_INFO:
    "Names for the numbers as cpuName|ramMiB|gpuName|vramMiB|netName. Sent when a watch starts and when it changes. Laptop to phone.",
  DISKS:
    "Up to six disks as label:usedPct:totalGiB:freeGiB, joined by a bar. Sent while the System screen is open, when changed. Laptop to phone.",
  SESSIONS:
    "Up to eight AI sessions as agent:state:ctx:title joined by a bar, newest first; the agent is claude, codex, gemini or opencode, the state busy, wait or idle, and ctx the session's context fill 0 to 100 or -1 unknown. Empty means none. Laptop to phone.",
  PERM_ANSWER:
    "The answer to an approval as id/a (allow) or id/d (deny), the id being 12 lowercase hex digits. Anything else, or an unknown or already answered id, is dropped and counted. Phone to laptop.",
  PERM_GONE:
    "An approval was answered elsewhere, timed out or died, so the phone dismisses its sheet. The payload is the id. Laptop to phone.",
  LIMITS:
    "Usage limits as agent:window:pct:resetAtMin:measuredAtMin joined by a bar. The window is minutes (300 is five hours, 10080 a week), ctx for the context window, or a tilde and a name for a limit with no duration. Times are UTC minutes since the Unix epoch, -1 unknown. Laptop to phone.",
  SPEND:
    "Estimated spend at API list prices as agent/measuredAtMin/tokToday/centsToday/tok7d/cents7d/tok30d/cents30d/unpriced joined by a bar. Today, the last 7 days and the last 30 days. Tokens are uncached input plus output. Laptop to phone.",
  WEATHER:
    "The weather as tempTenthsC/wmoCode/city. Empty means none. Laptop to phone.",
};

const PAD_BUTTON_DOCS: Record<string, string> = {
  NONE: "Nothing held. What PAD_STATE carries the moment the last button comes up.",
  DPAD_UP: "D-pad up.",
  DPAD_DOWN: "D-pad down.",
  DPAD_LEFT: "D-pad left.",
  DPAD_RIGHT: "D-pad right.",
  START: "Start, the menu button.",
  BACK: "Back, the view button.",
  LEFT_THUMB: "Left stick pressed in.",
  RIGHT_THUMB: "Right stick pressed in.",
  LEFT_SHOULDER: "Left bumper.",
  RIGHT_SHOULDER: "Right bumper.",
  GUIDE: "The Xbox button.",
  A: "A.",
  B: "B.",
  X: "X.",
  Y: "Y.",
};

const PAD_STATUS_DOCS: Record<string, string> = {
  READY:
    "The virtual controller is plugged in and PAD_STATE will be acted on. The only token that leaves the pad usable.",
  NO_DRIVER:
    "The virtual-controller driver, ViGEmBus, is not installed on the laptop. The pad falls back to keys.",
  ATTACH_FAILED:
    "The driver is there but plugging the pad in did not work. The pad falls back to keys.",
};

const FEATURE_DOCS: Record<string, string> = {
  END_ACTION:
    "The laptop answers ACTION 60 to 62, sleep or shut down when the media ends.",
  NOTIFY: "The laptop can forward its notifications once CONTROL 10 is 1.",
  FOREGROUND:
    "The laptop can say which app is in front, in TEXT 21, once CONTROL 11 is 1. A Linux Wayland session cannot.",
  STATS:
    "The laptop sends system numbers (TEXT 27 to 29). Named only while its stats switch is on and usable.",
  WEATHER:
    "The laptop sends the weather (TEXT 35). Named only while its weather switch is on.",
  CLAUDE:
    "The laptop’s Claude Code integration is on and usable: sessions, limits, spend and approvals.",
  CODEX:
    "The laptop’s Codex integration is on and usable: sessions, limits, spend and approvals.",
  GITHUB:
    "The laptop is signed in to GitHub and sends pull requests and notifications (BLOB 7).",
  ANNOTATE:
    "The laptop can show the annotation overlay and a signing surface. The phone shows an Annotate tile only for a laptop that names this.",
  CAPTURE:
    "The laptop can start its own screenshot and screen-recording tools. The phone shows a Capture tile only for a laptop that names this.",
};

const SIGN_STATUS_DOCS: Record<string, string> = {
  SAVED: "The signature was exported to a PNG and put on the clipboard.",
  EMPTY: "Nothing had been drawn, so nothing was saved.",
  FAILED: "The file could not be saved, or every clipboard route failed.",
};

function read(file: string): string[] {
  const raw = readFileSync(path.join(PROTOCOL_DIR, file), "utf8");
  return raw
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0 && !line.startsWith("#"));
}

function parseFrames(): FrameRow[] {
  const byName = new Map<string, FrameRow>();

  for (const line of read("frames.txt")) {
    const halves = line.split("=").map((half) => half.trim());
    const left = halves[0];
    const right = halves[1];
    if (left === undefined || right === undefined) continue;

    const tokens = left.split(/\s+/);
    const name = tokens[0];
    if (name === undefined) continue;

    const first = right.split(/\s+/)[0];
    if (first === undefined) continue;

    const example: FrameExample = {
      fields: tokens.slice(1).join(" "),
      bytes: right,
    };

    const existing = byName.get(name);
    if (existing) {
      existing.examples.push(example);
      continue;
    }

    const docs = FRAME_DOCS[name];
    byName.set(name, {
      name,
      type: `0x${first.toLowerCase()}`,
      payload: docs?.payload ?? null,
      direction: docs?.direction ?? null,
      meaning: docs?.meaning ?? null,
      examples: [example],
      documented: docs !== undefined,
    });
  }

  return [...byName.values()].sort((a, b) => a.type.localeCompare(b.type));
}

const ID_KINDS = [
  "ACTION",
  "CONTROL",
  "TEXT",
  "PAD_BUTTON",
  "PAD_STATUS",
  "FEATURE",
  "SIGN_STATUS",
  "PALETTE",
] as const;
type IdKind = (typeof ID_KINDS)[number];

type ParsedIds = Record<IdKind, IdRow[]> & { HANDSHAKE: Map<string, string> };

const isIdKind = (kind: string): kind is IdKind =>
  (ID_KINDS as readonly string[]).includes(kind);

function parseIds(): ParsedIds {
  const out: ParsedIds = {
    ACTION: [],
    CONTROL: [],
    TEXT: [],
    PAD_BUTTON: [],
    PAD_STATUS: [],
    FEATURE: [],
    SIGN_STATUS: [],
    PALETTE: [],
    HANDSHAKE: new Map(),
  };

  const docsFor: Record<IdKind, Record<string, string>> = {
    ACTION: ACTION_DOCS,
    CONTROL: CONTROL_DOCS,
    TEXT: TEXT_DOCS,
    PAD_BUTTON: PAD_BUTTON_DOCS,
    PAD_STATUS: PAD_STATUS_DOCS,
    FEATURE: FEATURE_DOCS,
    SIGN_STATUS: SIGN_STATUS_DOCS,
    PALETTE: {},
  };

  for (const line of read("actions.txt")) {
    const parts = line.split(/\s+/);
    const kind = parts[0];
    const id = parts[1];
    const name = parts[2];
    if (kind === undefined || id === undefined || name === undefined) continue;

    if (kind === "HANDSHAKE") {
      out.HANDSHAKE.set(name, id);
      continue;
    }
    // An unknown kind is skipped, not guessed at; assertParsed catches a kind that
    // stops parsing altogether.
    if (!isIdKind(kind)) continue;

    // Only a palette row has a fourth field: its colour, which is its description.
    const hex = kind === "PALETTE" ? parts[3] : undefined;
    const description = hex ? `#${hex}` : docsFor[kind][name];
    out[kind].push({
      id,
      name,
      description: description ?? null,
      documented: description !== undefined,
    });
  }

  return out;
}

/**
 * Self-check, run on every build. Generating the tables only helps if a fixture the
 * site can no longer parse fails the build loudly, instead of rendering an empty
 * table that reads as "the protocol has no frames".
 */
function assertParsed(tables: Omit<ProtocolTables, "undescribedInFixture">): void {
  const problems: string[] = [];
  if (tables.frames.length === 0) {
    problems.push("no frames parsed from protocol/frames.txt");
  }
  if (tables.actions.length === 0) {
    problems.push("no actions parsed from protocol/actions.txt");
  }
  if (tables.controls.length === 0) {
    problems.push("no controls parsed from protocol/actions.txt");
  }
  if (tables.textKinds.length === 0) {
    problems.push("no TEXT kinds parsed from protocol/actions.txt");
  }
  if (tables.padButtons.length === 0) {
    problems.push("no PAD_BUTTON masks parsed from protocol/actions.txt");
  }
  if (tables.padStatuses.length === 0) {
    problems.push("no PAD_STATUS tokens parsed from protocol/actions.txt");
  }
  if (tables.features.length === 0) {
    problems.push("no FEATURE tokens parsed from protocol/actions.txt");
  }
  if (tables.signStatuses.length === 0) {
    problems.push("no SIGN_STATUS tokens parsed from protocol/actions.txt");
  }
  if (tables.palette.length === 0) {
    problems.push("no PALETTE rows parsed from protocol/actions.txt");
  }
  if (!Number.isInteger(tables.version) || tables.version < 1) {
    problems.push("HANDSHAKE VERSION is not a positive integer");
  }
  const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  if (!uuid.test(tables.serviceId)) {
    problems.push("HANDSHAKE SERVICE_ID is not a UUID");
  }
  if (problems.length > 0) {
    throw new Error(
      "Edgepad protocol fixtures could not be read from " +
        PROTOCOL_DIR +
        ":\n  - " +
        problems.join("\n  - ") +
        "\nThis site is built from the same two files both test suites read. " +
        "It must be built from inside the edgepad repository.",
    );
  }
}

/**
 * Reads `protocol/frames.txt` and `protocol/actions.txt` at build time. Throws, failing
 * the build, when either cannot be parsed, so the site never shows a protocol the apps
 * do not speak.
 */
export function loadProtocol(): ProtocolTables {
  const frames = parseFrames();
  const ids = parseIds();

  const tables = {
    version: Number(ids.HANDSHAKE.get("VERSION")),
    serviceId: ids.HANDSHAKE.get("SERVICE_ID") ?? "",
    frames,
    actions: ids.ACTION,
    controls: ids.CONTROL,
    textKinds: ids.TEXT,
    padButtons: ids.PAD_BUTTON,
    padStatuses: ids.PAD_STATUS,
    features: ids.FEATURE,
    signStatuses: ids.SIGN_STATUS,
    palette: ids.PALETTE,
  };
  assertParsed(tables);

  const inFixture = new Set(frames.map((frame) => frame.name));
  return {
    ...tables,
    undescribedInFixture: Object.keys(FRAME_DOCS).filter(
      (name) => !inFixture.has(name),
    ),
  };
}
