/**
 * The phone's control surface, drawn rather than photographed: there are no images in
 * this deployment and none is needed. Four corner rulers, the trackpad between them,
 * the three top-centre buttons and the media strip — the same anatomy the docs page
 * describes in words.
 *
 * Everything is `currentColor` so it inverts with the theme along with everything
 * else, and nothing here is decorative-only: it is the fastest way to say what the
 * phone looks like in use.
 */

const W = 300;
const H = 520;
const INSET = 10;
const TICKS = 8;
const STEP = 11;

/** One corner's ruler: a run of ticks along each edge, meeting at the bend. */
function CornerRuler() {
  return (
    <g stroke="currentColor" strokeWidth={1.5} strokeLinecap="square">
      {Array.from({ length: TICKS }, (_, i) => {
        const offset = 18 + i * STEP;
        const length = i % 2 === 0 ? 11 : 6;
        return (
          <g key={offset}>
            <line x1={offset} y1={0} x2={offset} y2={length} />
            <line x1={0} y1={offset} x2={length} y2={offset} />
          </g>
        );
      })}
    </g>
  );
}

const CORNERS: string[] = [
  `translate(${INSET} ${INSET})`,
  `translate(${W - INSET} ${INSET}) scale(-1 1)`,
  `translate(${INSET} ${H - INSET}) scale(1 -1)`,
  `translate(${W - INSET} ${H - INSET}) scale(-1 -1)`,
];

export function SurfaceFigure({ className }: { className?: string }) {
  return (
    <svg
      viewBox={`0 0 ${W} ${H}`}
      className={className}
      role="img"
      aria-label="The phone's control surface: a ruler wrapping each of the four corners, the trackpad filling the middle, three buttons at the top centre and a media strip below them."
      fill="none"
    >
      <title>The phone&apos;s control surface</title>

      {/* The phone */}
      <rect
        x={0.75}
        y={0.75}
        width={W - 1.5}
        height={H - 1.5}
        stroke="currentColor"
        strokeWidth={1.5}
      />

      {CORNERS.map((transform) => (
        <g key={transform} transform={transform}>
          <CornerRuler />
        </g>
      ))}

      {/* Top-centre: settings, keyboard, gamepad */}
      <g stroke="currentColor" strokeWidth={1.5} opacity={0.55}>
        {[0, 1, 2].map((i) => (
          <rect key={i} x={W / 2 - 40 + i * 28} y={34} width={20} height={20} />
        ))}
      </g>

      {/* Media: the track, the app playing it, and where it is */}
      <g opacity={0.55}>
        <rect
          x={44}
          y={92}
          width={W - 88}
          height={54}
          stroke="currentColor"
          strokeWidth={1.5}
        />
        <line
          x1={56}
          y1={112}
          x2={160}
          y2={112}
          stroke="currentColor"
          strokeWidth={1.5}
        />
        <line
          x1={56}
          y1={126}
          x2={120}
          y2={126}
          stroke="currentColor"
          strokeWidth={1.5}
        />
        <line
          x1={56}
          y1={138}
          x2={W - 56}
          y2={138}
          stroke="currentColor"
          strokeWidth={1.5}
          strokeDasharray="3 4"
        />
      </g>

      {/* The trackpad: everything between the corners */}
      <text
        x={W / 2}
        y={H / 2 + 40}
        textAnchor="middle"
        className="font-mono"
        fill="currentColor"
        fontSize={11}
        letterSpacing={3}
        opacity={0.55}
      >
        TRACKPAD
      </text>

      {/* A finger's path across it, ending on a corner ruler */}
      <path
        d={`M 72 ${H / 2 + 96} C 130 ${H / 2 + 60}, 190 ${H / 2 + 130}, ${W - INSET - 30} ${H - INSET - 34}`}
        stroke="currentColor"
        strokeWidth={1.5}
        strokeDasharray="5 5"
        opacity={0.55}
      />
      <circle cx={72} cy={H / 2 + 96} r={4} fill="currentColor" opacity={0.55} />
    </svg>
  );
}
