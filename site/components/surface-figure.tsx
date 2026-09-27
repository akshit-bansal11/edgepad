import {
  Gamepad2,
  Keyboard,
  LayoutGrid,
  Lock,
  Play,
  Settings,
  SkipBack,
  SkipForward,
} from "lucide-react";

/**
 * The phone's control surface, drawn rather than photographed, in the 2.0 look: four
 * corner dials, the five top buttons, the media pieces, and the trackpad that is
 * everything else. The same anatomy the docs page describes in words.
 *
 * The dials are drawn by the app's own ruler geometry, ported from the design
 * reference (`EP.ruler`, itself a port of Perimeter.kt and RulerPainter.kt): minor
 * notches are dots, majors are short rounded pills, the indicator is an accent pill
 * with a soft glow, and on a level dial the notches between the indicator and the
 * ruler's end take the accent, so the dial reads as a bent progress bar. The path's
 * corners are widened (`bend`) so no mark can reach the bend's centre and cross
 * another. Computed once at build time; nothing here runs in the browser.
 *
 * Colours are theme tokens, so the phone follows light and dark with the page.
 * Units are dp of a 411-wide phone.
 */

const W = 411;
const H = 860;
const DISPLAY_RADIUS = 44;

const HALF_PI = Math.PI / 2;
const NOTCH = 11;
const MAJOR_EVERY = 5;
const INSET = 5;
const MAJOR_LEN = 20;
const IND_LEN = 32;
const ARMED = 1.2;
const CLEAR = 10;
const FEATHER = 36;

/** A point on the perimeter and its inward normal. */
type Point = { x: number; y: number; nx: number; ny: number };

type Piece =
  | {
      kind: "line";
      start: number;
      len: number;
      x: number;
      y: number;
      ux: number;
      uy: number;
    }
  | {
      kind: "arc";
      start: number;
      len: number;
      cx: number;
      cy: number;
      r: number;
      from: number;
    };

/** A rounded rectangle walked clockwise from the top edge, as a length. */
function perimeter(width: number, height: number, radius: number) {
  const r = Math.max(1, Math.min(radius, Math.min(width, height) / 2));
  const top = width - 2 * r;
  const side = height - 2 * r;
  const arc = HALF_PI * r;
  const length = 2 * top + 2 * side + 4 * arc;
  const mids = [
    -arc / 2,
    top + arc / 2,
    top + arc + side + arc / 2,
    2 * top + 2 * arc + side + arc / 2,
  ];

  const pieces: Piece[] = [];
  let start = 0;
  const line = (x: number, y: number, ux: number, uy: number, len: number) => {
    pieces.push({ kind: "line", start, len, x, y, ux, uy });
    start += len;
  };
  const corner = (cx: number, cy: number, from: number) => {
    pieces.push({ kind: "arc", start, len: arc, cx, cy, r, from });
    start += arc;
  };
  line(r, 0, 1, 0, top);
  corner(width - r, r, -HALF_PI);
  line(width, r, 0, 1, side);
  corner(width - r, height - r, 0);
  line(width - r, height, -1, 0, top);
  corner(r, height - r, HALF_PI);
  line(0, height - r, 0, -1, side);
  corner(r, r, Math.PI);

  const wrap = (s: number) => ((s % length) + length) % length;

  const point = (s: number): Point => {
    const d = wrap(s);
    const piece = pieces.find((q) => d < q.start + q.len) ?? pieces[pieces.length - 1];
    if (!piece) return { x: 0, y: 0, nx: 0, ny: 0 };
    const off = Math.min(d - piece.start, piece.len);
    if (piece.kind === "line") {
      return {
        x: piece.x + piece.ux * off,
        y: piece.y + piece.uy * off,
        nx: -piece.uy,
        ny: piece.ux,
      };
    }
    const a = piece.from + off / piece.r;
    const c = Math.cos(a);
    const sn = Math.sin(a);
    return { x: piece.cx + piece.r * c, y: piece.cy + piece.r * sn, nx: -c, ny: -sn };
  };

  return { point, mid: (corner: number) => wrap(mids[corner] ?? 0) };
}

type Role = "tick" | "fill" | "glow" | "ind";
type Mark = {
  x1: number;
  y1: number;
  x2: number;
  y2: number;
  w: number;
  o: number;
  role: Role;
};

const reach = (h: number) => INSET + IND_LEN * h * ARMED;
const smooth = (x: number) => {
  const t = Math.max(0, Math.min(1, x));
  return t * t * (3 - 2 * t);
};

/**
 * One corner's dial. `offset` is where the ruler sits under the indicator; `before`
 * and `after` are the room on each side; `length` makes it a level dial whose lit
 * part is `offset` long.
 */
function ruler(
  per: ReturnType<typeof perimeter>,
  corner: number,
  o: {
    offset: number;
    before: number;
    after: number;
    length?: number;
    height?: number;
  },
) {
  const h = o.height ?? 1;
  const centre = per.mid(corner);
  const level = o.length !== undefined;
  let first = Math.ceil((-o.before - o.offset) / NOTCH);
  let last = Math.floor((o.after - o.offset) / NOTCH);
  if (o.length !== undefined) {
    first = Math.max(first, -Math.floor(o.length / NOTCH));
    last = Math.min(last, 0);
  }

  const marks: Mark[] = [];
  const mark = (p: Point, d0: number, d1: number, w: number, op: number, role: Role) =>
    marks.push({
      x1: p.x + p.nx * d0,
      y1: p.y + p.ny * d0,
      x2: p.x + p.nx * d1,
      y2: p.y + p.ny * d1,
      w,
      o: op,
      role,
    });

  for (let n = first; n <= last; n++) {
    const s = o.offset + n * NOTCH;
    const room = s >= 0 ? o.after - s : o.before + s;
    // Feathered at both ends, and ducked as it slides under the indicator so the two
    // never read as one doubled stroke.
    const fade = smooth(room / FEATHER) * (0.15 + 0.85 * smooth((Math.abs(s) - 3) / 9));
    if (fade <= 0.02) continue;
    const lit = level && s > 0.5;
    const p = per.point(centre + s);
    if (n % MAJOR_EVERY === 0) {
      mark(
        p,
        INSET,
        INSET + MAJOR_LEN * h,
        3.2,
        (lit ? 1 : 0.85) * fade,
        lit ? "fill" : "tick",
      );
    } else {
      const dot = 3 + 0.8 * h;
      mark(
        p,
        INSET + dot / 2,
        INSET + dot / 2 + 0.01,
        dot,
        (lit ? 0.95 : 0.42) * fade,
        lit ? "fill" : "tick",
      );
    }
  }

  const p = per.point(centre);
  mark(p, INSET - 1, INSET + IND_LEN * h, 12, 0.18, "glow");
  mark(p, INSET - 1, INSET + IND_LEN * h, 5, 1, "ind");

  const depth = reach(h) + 40;
  return { marks, lx: p.x + p.nx * depth, ly: p.y + p.ny * depth };
}

const ROLE_CLASS: Record<Role, string> = {
  tick: "stroke-foreground",
  fill: "stroke-primary",
  glow: "stroke-primary",
  ind: "stroke-primary",
};

// The ruler's path widens its corners past the display's own radius, so the tallest
// mark stays clear of the bend's centre.
const PER = perimeter(W, H, Math.max(DISPLAY_RADIUS, reach(1) + CLEAR));
const RULER_LEN = 176;

/** Corners clockwise from top left: 0 TL, 1 TR, 2 BR, 3 BL. */
const DIALS: {
  corner: number;
  label: string;
  value?: string;
  dial: ReturnType<typeof ruler>;
}[] = [
  {
    corner: 0,
    label: "VOL",
    value: "23",
    dial: ruler(PER, 0, {
      offset: 0.23 * RULER_LEN,
      before: 160,
      after: 80,
      length: RULER_LEN,
    }),
  },
  {
    corner: 1,
    label: "BRI",
    value: "100",
    dial: ruler(PER, 1, {
      offset: RULER_LEN,
      before: 60,
      after: 220,
      length: RULER_LEN,
    }),
  },
  {
    corner: 2,
    label: "SCRUB",
    value: "2:04",
    dial: ruler(PER, 2, { offset: 4, before: 150, after: 150 }),
  },
  {
    corner: 3,
    label: "ZOOM",
    dial: ruler(PER, 3, {
      offset: 0.5 * RULER_LEN,
      before: 150,
      after: 150,
      length: RULER_LEN,
    }),
  },
];

const TOP_BUTTONS = [
  { name: "settings", Icon: Settings },
  { name: "keyboard", Icon: Keyboard },
  { name: "lock", Icon: Lock },
  { name: "gamepad", Icon: Gamepad2 },
  { name: "macros", Icon: LayoutGrid },
];
const GRID = 35;

export function SurfaceFigure({ className }: { className?: string }) {
  return (
    <svg
      viewBox={`0 0 ${W} ${H}`}
      className={className}
      role="img"
      aria-label="The phone's control surface: a dial wrapping each of the four corners, five buttons along the top, the media pieces in the middle, and the trackpad filling everything else."
      fill="none"
    >
      <title>The phone&apos;s control surface</title>

      <defs>
        <clipPath id="surface-screen">
          <rect width={W} height={H} rx={DISPLAY_RADIUS} />
        </clipPath>
      </defs>

      {/* The phone, and the surface's grid pattern clipped to its screen */}
      <rect
        x={0.75}
        y={0.75}
        width={W - 1.5}
        height={H - 1.5}
        rx={DISPLAY_RADIUS}
        className="fill-card stroke-line"
        strokeWidth={1.5}
      />
      <g
        clipPath="url(#surface-screen)"
        className="stroke-line"
        strokeWidth={1}
        opacity={0.7}
      >
        {Array.from({ length: Math.floor(W / GRID) }, (_, i) => (i + 1) * GRID).map(
          (x) => (
            <line key={`v${x}`} x1={x} y1={0} x2={x} y2={H} />
          ),
        )}
        {Array.from({ length: Math.floor(H / GRID) }, (_, i) => (i + 1) * GRID).map(
          (y) => (
            <line key={`h${y}`} x1={0} y1={y} x2={W} y2={y} />
          ),
        )}
      </g>

      {/* The four corner dials */}
      {DIALS.map(({ corner, label, value, dial }) => (
        <g key={corner}>
          <g strokeLinecap="round">
            {dial.marks.map((m) => (
              <line
                key={`${m.role}-${m.x1.toFixed(2)}-${m.y1.toFixed(2)}`}
                x1={m.x1}
                y1={m.y1}
                x2={m.x2}
                y2={m.y2}
                strokeWidth={m.w}
                opacity={m.o}
                className={ROLE_CLASS[m.role]}
              />
            ))}
          </g>
          <text
            x={dial.lx}
            y={value ? dial.ly - 12 : dial.ly + 4}
            textAnchor="middle"
            className="fill-dim"
            fontSize={11}
            fontWeight={700}
            letterSpacing="0.06em"
          >
            {label}
          </text>
          {value ? (
            <text
              x={dial.lx}
              y={dial.ly + 16}
              textAnchor="middle"
              className="fill-foreground"
              fontSize={26}
              fontWeight={900}
            >
              {value}
            </text>
          ) : null}
        </g>
      ))}

      {/* The five top buttons: settings, keyboard, lock, gamepad, macros */}
      {TOP_BUTTONS.map(({ name, Icon }, i) => (
        <Icon
          key={name}
          x={W / 2 - 2 * 48 + i * 48 - 12}
          y={34}
          width={24}
          height={24}
          strokeWidth={2}
          className="text-dim"
        />
      ))}

      {/* Media: the app playing, the track, where it is, and the three buttons */}
      <g>
        <circle cx={W / 2} cy={318} r={22} className="fill-faint" />
        <rect
          x={W / 2 - 70}
          y={356}
          width={140}
          height={10}
          rx={5}
          className="fill-foreground"
          opacity={0.8}
        />
        <rect
          x={W / 2 - 46}
          y={374}
          width={92}
          height={8}
          rx={4}
          className="fill-dim"
          opacity={0.6}
        />
        <rect
          x={W / 2 - 110}
          y={404}
          width={220}
          height={4}
          rx={2}
          className="fill-faint"
        />
        <rect
          x={W / 2 - 110}
          y={404}
          width={88}
          height={4}
          rx={2}
          className="fill-primary"
        />
        <SkipBack
          x={W / 2 - 84}
          y={436}
          width={26}
          height={26}
          className="text-foreground"
        />
        <circle cx={W / 2} cy={449} r={26} className="fill-primary" />
        <Play
          x={W / 2 - 10}
          y={439}
          width={22}
          height={20}
          className="fill-primary-foreground text-primary-foreground"
        />
        <SkipForward
          x={W / 2 + 58}
          y={436}
          width={26}
          height={26}
          className="text-foreground"
        />
      </g>

      {/* The trackpad, and a finger's path across it ending on a corner dial */}
      <text
        x={W / 2}
        y={590}
        textAnchor="middle"
        className="fill-dim"
        fontSize={15}
        fontWeight={700}
      >
        Trackpad
      </text>
      <path
        d={`M 116 ${H - 190} C 180 ${H - 250}, 240 ${H - 110}, ${W - 150} ${H - 40}`}
        className="stroke-dim"
        strokeWidth={2}
        strokeDasharray="2 8"
        strokeLinecap="round"
      />
      <circle cx={116} cy={H - 190} r={18} className="fill-primary" opacity={0.18} />
      <circle cx={116} cy={H - 190} r={7} className="fill-primary" />
    </svg>
  );
}
