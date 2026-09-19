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

export type Direction = "phone-to-laptop" | "laptop-to-phone" | "both";

export type FrameExample = {
  /** The fields as the fixture writes them, e.g. "5 -3". */
  fields: string;
  /** The exact bytes both codecs must produce, e.g. "10 05 00 fd ff". */
  bytes: string;
};

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

export type IdRow = {
  id: number;
  name: string;
  description: string | null;
  documented: boolean;
};

export type ProtocolTables = {
  version: number;
  serviceId: string;
  frames: FrameRow[];
  actions: IdRow[];
  controls: IdRow[];
  textKinds: IdRow[];
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
    meaning: "Run one action from the laptop's table.",
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
      "Press or release one key, by Windows virtual-key code. This is what the phone's keyboard screen sends.",
  },
  PING: {
    payload: "t i64",
    direction: "phone-to-laptop",
    meaning: "The phone's clock, in nanoseconds.",
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
      "A control's current value. Flags bit 0 is muted for audio controls, playing for media position.",
  },
  TEXT: {
    payload: "kind u8, length u8, UTF-8 bytes",
    direction: "both",
    meaning:
      "The only variable-length frame: up to 255 bytes, never split inside a character.",
  },
};

const ACTION_DOCS: Record<string, string> = {
  MUTE_TOGGLE: "Mute key.",
  PLAY_PAUSE: "Media play/pause key.",
  NEXT_TRACK: "Media next key.",
  PREVIOUS_TRACK: "Media previous key.",
  MIC_MUTE_TOGGLE: "Toggles the default microphone's mute.",
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
  BRIGHTNESS_UP: "The panel's brightness, plus 10.",
  BRIGHTNESS_DOWN: "The panel's brightness, minus 10.",
  MACRO_BASE:
    "The first of 32 reserved macro slots (ids 64-95). Slot n runs as MACRO_BASE + n. The phone sends the index; the laptop's own list decides what it opens.",
};

const CONTROL_DOCS: Record<string, string> = {
  VOLUME:
    "SET sets the default speakers' level; STATE reports the level and a muted flag.",
  BRIGHTNESS:
    "SET sets the built-in panel's brightness through WMI; STATE reports the level.",
  MIC_LEVEL:
    "SET sets the default microphone's level; STATE reports the level and a muted flag.",
  MEDIA_POSITION:
    "SET seeks the current track to that percent, where the player allows it; STATE reports the percent and a playing flag.",
  REFRESH_RATE:
    "SET switches the display to the rate at that index of the TEXT 4 list; STATE reports the current rate's index. Never a rate in hertz.",
};

const TEXT_DOCS: Record<string, string> = {
  NOW_PLAYING: "What is playing. Laptop to phone.",
  APP: "The app playing it. Laptop to phone.",
  TIMELINE: "The timeline as seconds/length, such as 84/227. Laptop to phone.",
  TYPE: "Text to type, where \\b is backspace and \\n is enter. Phone to laptop.",
  REFRESH_RATES:
    "The display's available refresh rates as 60/120/144, in the order CONTROL 4 indexes them. Laptop to phone.",
  MACROS:
    "The laptop's macro names as Chrome/Spotify/Notes, in the order MACRO_BASE indexes them. A blank name keeps its slot. Laptop to phone.",
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

type ParsedIds = {
  ACTION: IdRow[];
  CONTROL: IdRow[];
  TEXT: IdRow[];
  HANDSHAKE: Map<string, string>;
};

function parseIds(): ParsedIds {
  const out: ParsedIds = {
    ACTION: [],
    CONTROL: [],
    TEXT: [],
    HANDSHAKE: new Map(),
  };

  const docsFor: Record<string, Record<string, string>> = {
    ACTION: ACTION_DOCS,
    CONTROL: CONTROL_DOCS,
    TEXT: TEXT_DOCS,
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
    if (kind !== "ACTION" && kind !== "CONTROL" && kind !== "TEXT") continue;

    const description = docsFor[kind]?.[name];
    out[kind].push({
      id: Number(id),
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
