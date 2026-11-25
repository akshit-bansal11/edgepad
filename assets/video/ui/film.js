// The director: one cut (window.CUT) from its narration timing (window.TIMING, written by voice.py) and its beats
// (window.BEATS, ui/beats.js). It builds the page's text as DOM revealed by one paused GSAP timeline, and draws the
// room, the phone, the laptop cards and the ground on one canvas from a proxy tween's onUpdate. Every frame is a pure
// function of the timeline's time, so HyperFrames can seek to any frame in any order.
//
// CUT.id is the composition's id (and its timeline's key); CUT.script is the narration it is cut from (default: the
// same). The two social cuts share the `short` script and differ only in their layout.
(function () {
  const { lerp, clamp, ease, seg, rgba, fillRR, rr } = EP;
  const CUT = window.CUT;
  const W = CUT.w, H = CUT.h, TALL = H > W;
  const BASE = CUT.base || '';
  const SID = CUT.script || CUT.id;
  // The dark ground and the inks on it (EP.TOK, ui/kit.js). Contrast against the ground (relative luminance 0.0145): INK
  // 14.7:1, BLUE 6.2:1. On the caption pill #2B2D33 (0.0263): INK 12.4:1, BLUE 5.2:1. The app's own dark theme (#0E0F12) is
  // darker than the ground, so the phone reads as a dark object on a lighter room.
  const { INK, BLUE, GROUND } = EP.TOK;
  const BLUE_RGB = '77,163,255'; // BLUE as the channels a gradient stop needs
  const GLINT_RGB = '150,190,255'; // the Edgepad 6 promise: a faint, soft, unresolved shape on both screens

  // ---- layout per format: where the headline, the sub-line, the caption pill and the laptop cards sit ----
  // 169 the phone stands on the right; 916 headline above, phone below; 34 (1080 x 1440) the same, tighter, so headline and phone fit.
  // 916 keeps all text out of the top 250 px (the headline starts at 262).
  const LAY = W > H ? '169' : H / W > 1.6 ? '916' : '34';
  const HEAD916 = { x: 80, y: 262, size: 96, max: 920 };
  const HEAD34 = { x: 72, y: 84, size: 84, max: 936 };
  // lap: where a headline starts on a beat flagged `lap` (the laptop band is shown): 169 moves up above the laptop, the
  // tall cuts drop below it. end.mark is the ident mark's size on the end card.
  const F = {
    '169': { head: { x: 150, y: 250, size: 110, max: 800 }, top: { x: 360, y: 42, size: 96, max: 1300 }, sub: { size: 56, gap: 52, min: 488 }, topSub: 44, cap: { cx: 550, cy: 990, size: 44 }, card: { x: 150, y: 640, s: 1 }, end: { y: 80, close: 56, w: 1000, mark: 300 }, lap: { y: 130 }, band: null, scrim: null },
    '916': { head: HEAD916, top: HEAD916, sub: { size: 50, gap: 36, min: 0 }, topSub: 50, cap: { cx: 540, cy: 1664, size: 44 }, card: { x: 90, y: 1130, s: 0.8 }, end: { y: 230, close: 54, w: 920, mark: 300 }, lap: { y: 950 }, tall: true, band: { x: 80, y: 512, s: 1 }, scrim: { solid: 520, to: 800 } },
    '34': { head: HEAD34, top: HEAD34, sub: { size: 42, gap: 28, min: 0 }, topSub: 42, cap: { cx: 540, cy: 1372, size: 40 }, card: { x: 60, y: 960, s: 0.72 }, end: { y: 70, close: 50, w: 936, mark: 220 }, lap: { y: 700 }, tall: true, band: { x: 72, y: 372, s: 1 }, scrim: { solid: 400, to: 670 } },
  }[LAY];
  F.talk = { '169': { x: 1000, y: 165, s: 1.4 }, '916': { x: 190, y: 585, s: 1.4 }, '34': { x: 190, y: 345, s: 1.4 } }[LAY];
  EP.F = F;

  // Camera presets: the phone's centre in page pixels, its scale in pixels per dp, and its turn in degrees.
  // The lap* presets are for beats with the laptop band up: in the tall cuts the phone (lapPhone upright, lapLand sideways) and
  // its corners (lapTl .. lapBl), its foot above the caption pill (lapFoot) and its head (lapHead) sit below the headline, where the band's scrim leaves them clear; in 16:9 they are the plain ones.
  // roomWide and talk keep the phone off the page (the room, or the plain cards, are the picture). raceSplit is the sideways
  // phone (set D.turn to 1) lower right (169) or below the laptop band (tall). split169 shows the phone's top-left corner.
  const CAMS = {
    '169': {
      off: { x: 1300, y: 1700, s: 1.08, r: 0 }, right: { x: 1300, y: 548, s: 1.08, r: 0 }, hero: { x: 1310, y: 520, s: 1.22, r: 0 },
      tl: { x: 1515, y: 1047, s: 1.79, r: 0 }, tr: { x: 1160, y: 1047, s: 1.79, r: 0 }, br: { x: 1200, y: 40, s: 1.79, r: 0 }, bl: { x: 1515, y: 40, s: 1.79, r: 0 },
      top: { x: 1483, y: 942, s: 1.68, r: 0 }, media: { x: 1320, y: 410, s: 1.45, r: 0 }, settings: { x: 1300, y: 700, s: 1.55, r: 0 },
      land: { x: 1040, y: 615, s: 1.55, r: -90 }, landRight: { x: 1330, y: 560, s: 1.0, r: -90 }, end: { x: 1350, y: 545, s: 0.845, r: 0 }, wide: { x: 1300, y: 548, s: 1.0, r: 0 },
      roomWide: { x: 1300, y: 1700, s: 1.08, r: 0 }, raceSplit: { x: 1440, y: 820, s: 0.95, r: -90 }, split169: { x: 1449, y: 864, s: 1.3, r: 0 }, talk: { x: 1300, y: 1700, s: 1.08, r: 0 },
      lapPhone: { x: 1300, y: 548, s: 1.08, r: 0 }, lapLand: { x: 1440, y: 820, s: 0.95, r: -90 }, lapTl: { x: 1515, y: 1047, s: 1.79, r: 0 }, lapTr: { x: 1160, y: 1047, s: 1.79, r: 0 }, lapBr: { x: 1200, y: 40, s: 1.79, r: 0 }, lapBl: { x: 1515, y: 40, s: 1.79, r: 0 }, lapFoot: { x: 1300, y: 548, s: 1.08, r: 0 }, lapHead: { x: 1300, y: 548, s: 1.08, r: 0 },
    },
    '916': {
      off: { x: 540, y: 2500, s: 1.15, r: 0 }, right: { x: 540, y: 1085, s: 1.1, r: 0 }, hero: { x: 540, y: 1085, s: 1.1, r: 0 },
      tl: { x: 820, y: 1430, s: 2.0, r: 0 }, tr: { x: 260, y: 1430, s: 2.0, r: 0 }, br: { x: 300, y: 740, s: 2.0, r: 0 }, bl: { x: 780, y: 740, s: 2.0, r: 0 },
      top: { x: 540, y: 1500, s: 1.7, r: 0 }, media: { x: 540, y: 1000, s: 1.6, r: 0 }, settings: { x: 540, y: 1085, s: 1.1, r: 0 },
      land: { x: 540, y: 1150, s: 1.18, r: -90 }, landRight: { x: 540, y: 1200, s: 1.1, r: -90 }, end: { x: 540, y: 1330, s: 0.62, r: 0 }, wide: { x: 540, y: 1085, s: 1.1, r: 0 },
      roomWide: { x: 540, y: 2500, s: 1.15, r: 0 }, raceSplit: { x: 540, y: 1450, s: 0.8, r: -90 }, split169: { x: 379, y: 1564, s: 1.3, r: 0 }, talk: { x: 540, y: 2500, s: 1.15, r: 0 },
      lapPhone: { x: 540, y: 1544, s: 0.85, r: 0 }, lapLand: { x: 540, y: 1400, s: 1.0, r: -90 }, lapTl: { x: 640, y: 1889, s: 1.6, r: 0 }, lapTr: { x: 440, y: 1889, s: 1.6, r: 0 }, lapBr: { x: 440, y: 896, s: 1.6, r: 0 }, lapBl: { x: 640, y: 896, s: 1.6, r: 0 }, lapFoot: { x: 540, y: 1047, s: 1.25, r: 0 }, lapHead: { x: 540, y: 1732, s: 1.25, r: 0 },
    },
    '34': {
      off: { x: 540, y: 1900, s: 0.9, r: 0 }, right: { x: 540, y: 880, s: 0.9, r: 0 }, hero: { x: 540, y: 885, s: 0.92, r: 0 },
      tl: { x: 511, y: 1081, s: 1.5, r: 0 }, tr: { x: 570, y: 1081, s: 1.5, r: 0 }, br: { x: 570, y: 479, s: 1.5, r: 0 }, bl: { x: 511, y: 479, s: 1.5, r: 0 },
      top: { x: 540, y: 1031, s: 1.5, r: 0 }, media: { x: 540, y: 573, s: 1.3, r: 0 }, settings: { x: 540, y: 885, s: 0.92, r: 0 },
      land: { x: 540, y: 900, s: 0.98, r: -90 }, landRight: { x: 540, y: 900, s: 0.9, r: -90 }, end: { x: 540, y: 1075, s: 0.58, r: 0 }, wide: { x: 540, y: 885, s: 0.92, r: 0 },
      roomWide: { x: 540, y: 1900, s: 0.9, r: 0 }, raceSplit: { x: 540, y: 1150, s: 0.62, r: -90 }, split169: { x: 328, y: 1257, s: 1.1, r: 0 }, talk: { x: 540, y: 1900, s: 0.9, r: 0 },
      lapPhone: { x: 540, y: 1168, s: 0.64, r: 0 }, lapLand: { x: 540, y: 1100, s: 0.95, r: -90 }, lapTl: { x: 640, y: 1468, s: 1.3, r: 0 }, lapTr: { x: 440, y: 1468, s: 1.3, r: 0 }, lapBr: { x: 440, y: 756, s: 1.3, r: 0 }, lapBl: { x: 640, y: 756, s: 1.3, r: 0 }, lapFoot: { x: 540, y: 886, s: 1.0, r: 0 }, lapHead: { x: 540, y: 1338, s: 1.0, r: 0 },
    },
  };
  const CAM = CAMS[LAY];
  EP.CAM = CAM;

  // The laptop's screen rect per preset: centre x, y, width w (the height is w / 1.6) and alpha a. room is the pose on the
  // TV stand (360 px wide at x 0.42 W, y 0.47 H in 16:9); rest, raceSplit (race), split169 (split) and wide are the
  // layout's own, from DESIGN.md "Layout" (16:9 left, the tall cuts in the top band); off is the room pose, unseen.
  const lap = (r) => ({ a: 1, ...r });
  const LAP_SETS = {
    '169': { room: { x: W * 0.42, y: H * 0.47, w: 360 }, rest: { x: 600, y: 700, w: 640 }, raceSplit: { x: 592, y: 440, w: 1040 }, split169: { x: 640, y: 560, w: 832 }, wide: { x: 960, y: 480, w: 1280 } },
    '916': { room: { x: W * 0.5, y: H * 0.47, w: 440 }, rest: { x: 540, y: 520, w: 880 }, raceSplit: { x: 540, y: 520, w: 880 }, split169: { x: 540, y: 530, w: 936 }, wide: { x: 540, y: 520, w: 936 } },
    '34': { room: { x: W * 0.5, y: H * 0.47, w: 440 }, rest: { x: 540, y: 330, w: 800 }, raceSplit: { x: 540, y: 330, w: 800 }, split169: { x: 540, y: 350, w: 880 }, wide: { x: 540, y: 360, w: 900 } },
  }[LAY];
  const LAPS = {
    off: { ...LAP_SETS.room, a: 0 }, room: lap(LAP_SETS.room), roomWide: lap(LAP_SETS.room), rest: lap(LAP_SETS.rest), race: lap(LAP_SETS.raceSplit), raceSplit: lap(LAP_SETS.raceSplit),
    split: lap(LAP_SETS.split169), split169: lap(LAP_SETS.split169), wide: lap(LAP_SETS.wide), talk: lap(LAP_SETS.rest),
  };
  EP.LAPS = LAPS;

  // D.tilt's Track: a move's value is degrees (yaw) or {yaw, pitch}; at() always returns {yaw, pitch}.
  class TiltTrack extends EP.Track {
    to(t0, t1, v) {
      return super.to(t0, t1, typeof v === 'number' ? { yaw: v, pitch: 0 } : { yaw: 0, pitch: 0, ...v });
    }
  }
  // The camera's Track: a move eases x, y and r in-out and the scale in-out with a 3% overshoot of its span (MOTION.md
  // "Camera presets"), settling by the move's end. A pure function of t, like Track.at.
  const OVERSHOOT = 0.03;
  class CamTrack extends EP.Track {
    at(t) {
      let v = this.v0;
      for (const [t0, t1, to] of this.moves) {
        if (t < t0) break;
        const p = clamp((t - t0) / (t1 - t0));
        const k = EP.easeInOut(p);
        const s = Math.sin(Math.PI * seg(p, 0.7, 1));
        v = { x: lerp(v.x, to.x, k), y: lerp(v.y, to.y, k), s: lerp(v.s, to.s, k * (1 + OVERSHOOT * s * s)), r: lerp(v.r, to.r, k) };
      }
      return v;
    }
  }

  // ---- timing: voice.py's, or a stand-in from the script so a still renders on a machine that has never run it ----
  const norm = (s) => s.toLowerCase().replace(/[^a-z0-9]/g, '');
  function standIn() {
    const x = new XMLHttpRequest();
    x.open('GET', `${BASE}scripts/${SID}.json`, false);
    x.send();
    const script = JSON.parse(x.responseText);
    let at = 0.5;
    const lines = [], chapters = [];
    script.chapters.forEach((ch, ci) => {
      chapters.push({ title: ch.title, start: Math.max(0, at - 0.5) });
      ch.lines.forEach((t) => {
        const words = t.split(/\s+/).map((tok) => {
          const d = 0.12 + norm(tok).length * 0.052 + (/[.,?!]$/.test(tok) ? 0.18 : 0);
          const w = [tok, at, at + d - 0.04];
          at += d;
          return w;
        });
        lines.push({ chapter: ci, text: t, start: words[0][1], end: words[words.length - 1][2], words });
        at += 0.47;
      });
      at += 0.5;
    });
    return { id: SID, duration: at + 2.5, chapters, lines, standIn: true };
  }
  const T = CUT.still ? { id: SID, duration: 1, lines: [], chapters: [] } : window.TIMING || standIn();
  const LINES = T.lines;
  const DUR = CUT.still ? 1 : T.duration;
  window.FILM = { duration: DUR, timing: T };

  // L(i): one line's clock. w('word', n) is when its nth spoken word starting with those letters begins.
  function L(i) {
    const line = LINES[i];
    const find = (word, n = 1) => {
      const k = norm(word);
      let seen = 0;
      for (const w of line.words) if (norm(w[0]).startsWith(k) && ++seen === n) return w;
      return null;
    };
    return {
      i,
      t0: line.start,
      t1: line.end,
      prev: i > 0 ? LINES[i - 1].end : 0,
      next: i + 1 < LINES.length ? LINES[i + 1].start : DUR,
      w: (word, n) => (find(word, n) || [0, line.start])[1],
      we: (word, n) => (find(word, n) || [0, 0, line.end])[2],
      words: line.words,
    };
  }

  // Screen anchors [[t, lt], ...] must run forward in film time, or the warp would double back: nudge a tie or a miss.
  const forward = (anchors) => {
    const out = [];
    for (const [t, lt] of anchors) out.push([out.length && t <= out[out.length - 1][0] ? out[out.length - 1][0] + 0.02 : t, lt]);
    return out;
  };

  // ---- the director's tracks: everything on the canvas is read from these at time t ----
  const Track = EP.Track;
  const CORNER = { VOL: 0, BRI: 1, ZOOM: 2, SCRUB: 3 };
  const resolve =(o, t) => (typeof o === 'function' ? o(t) : o);
  const D = {
    // ---- The 5.x additions (4.0 calls keep working). t is film time in seconds; every `o` below may be a plain object or a
    // function (t) => object. Beats read this block; nothing else describes them. ----
    // D.screen(t, key, o, anchors, land, fade = 0.35): as before, `o` may be a function of film time and `fade` is the
    //   cross-fade in seconds (0.12 for the three flashes).
    // D.lap(t, preset | {x, y, w, a}, dur = 0.9): the laptop's screen rect eases to a preset (centre x, y, width w, alpha a,
    //   height w / 1.6) or to the given fields (missing ones stay as they are). Presets (EP.LAPS): off (unseen), room (on its
    //   stand), roomWide, rest, race = raceSplit, split = split169, wide, talk.
    // D.lapScreen(t, kind, o): from t the laptop shows `kind` ('race', 'desktop', 'notes', 'slides', 'video', 'editor',
    //   'browser-pr', 'credits'; EP.laptopScreen) with o, cross-dissolved over 0.4 s from the one before (none for a first
    //   call at t < 0.05). o.tr defaults to D.raceAt(t); o.glint is always D.glint. The laptop is also the TV in the hook.
    // D.room (0 far .. 1 gone, as before) with D.roomFx.tv / .beam (0..1) and .lamp (1 normal .. 0.6 dimmed) Tracks; the
    //   laptop is drawn on the stand while D.room < 1 and eases into the D.lap pose as D.room runs 0.5 .. 1 (alpha too).
    //   D.dolly Track 0..1: the room and laptop scale 1 .. 1.06 about the page centre (roomWide's dolly-in).
    // D.hand Track 0..1: the soft hand behind the phone. D.tilt Track: M-TILT, a true 3D tilt of the whole phone with perspective.
    //   D.tilt.to(t0, t1, v) takes degrees (yaw) or {yaw, pitch}; yaw turns about the phone's vertical axis (positive: right
    //   edge toward the viewer), pitch about its horizontal axis (positive: top edge toward the viewer). At 0 the phone is
    //   drawn flat as usual; tilted, the hand fades out. D.tilt.at(t) is {yaw, pitch}. D.glint Track 0..1: a faint blurred shape on the laptop's screen and the phone's.
    //   D.sleep Track 0..1: the "Sleep when this ends" chip on the phone's media block.
    // D.ident(t0, {x, y, size}, o): EP.ident drawn from t0, centred on (x, y) (default the phone's centre at t0, size
    //   F.end.mark), with EP.ident's o (loop, a) and o.until (film time it fades out over 0.3 s; it holds otherwise).
    // D.handoff(t, corner = 'VOL'): M-HANDOFF for the latest D.ident: from t, 400 ms in-out, the mark shrinks and travels to
    //   the phone's top-left dial (corner 'VOL' | 'BRI' | 'ZOOM' | 'SCRUB' or 0..3), re-read from the camera each frame, and
    //   fades over the phone's own ruler in its last third. The phone must be upright.
    // D.fly(t0, from, to, draw, dur = 0.4): M-FLY. from / to are 'laptop' | 'phone' | {x, y} (page px, read at each frame);
    //   draw(ctx, k) paints the chip centred on the origin, k the eased 0..1 progress. A quadratic arc whose control point is
    //   18% of the distance above the midpoint, scale 1 -> 0.6 -> 1, a shadow deepening at the apex, then a 120 ms land
    //   squash (1.04 -> 1); gone after it.
    // D.line(t0, t1, from = 'phone', to = 'laptop'): a straight line draws on over 0.5 s from t0 between the phone and the
    //   laptop (or {x, y} points), a pulse travelling along it until t1, then it fades over 0.3 s.
    // D.dim(t0, t1, to = 0.4): the headline on screen at t0 fades to `to` over 0.3 s from t0 and back from t1.
    // D.race(t0, scale = 1): race time zero (the green light) at film time t0; D.raceAt(t) = (t - t0) / scale, negative on the
    //   grid. Later calls win from their t0 on; with none, D.raceAt(t) = t.
    // Beat fields: h may be an array ['text', 'text@word', ...]: each later entry replaces the one before at that word of the
    //   line, and the first appears at line start, or on its word when it is 'text@word'; '@word' alone shows nothing until
    //   the next entry. lap: true places the headline for the laptop band (below it in the tall cuts, above it in 16:9).
    cam: new CamTrack(CUT.startCam ? CAM[CUT.startCam] : CAM.off),
    turn: new Track(0),
    theme: new Track(0),
    bg: new Track(0),
    vol: new Track(31),
    bri: new Track(55),
    zoom: new Track(0),
    split: new Track(0),
    extra: new Track(0),
    room: new Track(CUT.still ? 1 : 0), // the hook's dark room: 0 far across it, 1 gone
    roomFx: { tv: new Track(0), beam: new Track(0), lamp: new Track(1) },
    dolly: new Track(0),
    lapT: new Track({ ...LAPS.off }),
    lapScreens: [{ t: -1, kind: 'desktop', o: {} }],
    hand: new Track(0),
    tilt: new TiltTrack({ yaw: 0, pitch: 0 }),
    glint: new Track(0),
    sleep: new Track(0),
    idents: [],
    flies: [],
    lines: [],
    dims: [],
    races: [],
    mediaA: new Track(1),
    mediaY: new Track(0.78),
    screens: [{ t: -1, key: 'surface', land: false, o: {}, anchors: null, fade: 0.35 }],
    armed: [],
    modes: [[-1, 'normal']],
    extraKinds: [[-1, undefined]], // the kinds in the four halves a split adds: [DEVICES, TABS, SIDE, DESK] ...
    scrubOn: [[-1, true]], // whether a scrub dial is on the pad (now playing then shows no time)
    presses: [],
    fingers: [],
    cards: [],
    media: [[-1, { app: 'Video', title: 'Mountain Lake Timelapse', dur: 612, pos: 298, playing: true }]],
    shaping: [],
    scrubOff: new Track(0),
    steps: {}, // stepper dials (ZOOM, DEVICES, ...): kind -> Track of steps taken
    // Seconds added to the playing position by a finger on the scrub dial, between t0 and t1.
    scrub(t0, t1, delta) {
      const m = mediaAt(t0);
      this.scrubOff.to(t0, t1, this.scrubOff.at(t0) + delta);
      this.armed.push([t0 - 0.12, t1 + 0.18, 'SCRUB']);
      this.fingers.push({ dial: 'SCRUB', t0, t1, from: 0, to: (delta / m.dur) * 100, trail: true });
    },
    // One more anchor on the latest screen: film time t shows that screen's own time lt.
    anchor(t, lt) {
      const s = this.screens[this.screens.length - 1];
      s.anchors = forward([...s.anchors, [t, lt]].sort((a, b) => a[0] - b[0]));
    },
    // A camera move to a preset (or a {x,y,s,r}) over dur seconds from t.
    move(t, preset, dur = 0.9) {
      this.cam.to(t, t + dur, typeof preset === 'string' ? CAM[preset] : preset);
    },
    // The phone shows `key` from t. anchors map film time to the screen's own clock, [[t, lt], ...].
    screen(t, key, o = {}, anchors = null, land = false, fade = 0.35) {
      this.screens.push({ t, key, o, anchors: forward(anchors || [[t, 0]]), land, fade });
      this.screens.sort((a, b) => a.t - b.t);
    },
    lap(t, preset, dur = 0.9) {
      this.lapT.to(t, t + dur, typeof preset === 'string' ? LAPS[preset] : { ...this.lapT.at(t), ...preset });
    },
    lapScreen(t, kind, o = {}) {
      this.lapScreens.push({ t, kind, o });
      this.lapScreens.sort((a, b) => a.t - b.t);
    },
    ident(t0, pos = {}, o = {}) {
      const cam = this.cam.at(t0);
      this.idents.push({ t0, x: pos.x ?? cam.x, y: pos.y ?? cam.y, size: pos.size ?? F.end.mark, o, hand: null });
    },
    handoff(t, corner = 'VOL') {
      this.idents[this.idents.length - 1].hand = { t, corner: CORNER[corner] ?? corner };
    },
    fly(t0, from, to, draw, dur = 0.4) {
      this.flies.push({ t0, from, to, draw, dur });
    },
    line(t0, t1, from = 'phone', to = 'laptop') {
      this.lines.push({ t0, t1, from, to });
    },
    dim(t0, t1, to = 0.4) {
      this.dims.push({ t0, t1, to });
    },
    race(t0, scale = 1) {
      this.races.push({ t0, scale });
      this.races.sort((a, b) => a.t0 - b.t0);
    },
    raceAt(t) {
      if (!this.races.length) return t;
      let r = this.races[0];
      for (const e of this.races) if (e.t0 <= t) r = e;
      return (t - r.t0) / r.scale;
    },
    mode(t, m) {
      this.modes.push([t, m]);
      this.modes.sort((a, b) => a[0] - b[0]);
    },
    kinds(t, list) {
      this.extraKinds.push([t, list]);
      this.extraKinds.sort((a, b) => a[0] - b[0]);
    },
    press(t, name) {
      this.presses.push([t, name]);
    },
    // A finger on the surface: keys [[t, x, y], ...] in the surface's dp, down from the first key to the last.
    touch(keys, trail = true) {
      this.fingers.push({ keys, trail });
    },
    tap(t, x, y, hold = 0.14) {
      this.fingers.push({ keys: [[t, x, y], [t + hold, x, y]], trail: false });
    },
    // A dial slid by a finger: the level eases from its value to `to` between t0 and t1 while it is armed.
    dial(kind, t0, t1, to, finger = true) {
      const tr = { VOL: this.vol, BRI: this.bri, ZOOM: this.zoom }[kind];
      const from = tr.at(t0);
      tr.to(t0, t1, to);
      this.armed.push([t0 - 0.12, t1 + 0.18, kind]);
      if (finger) this.fingers.push({ dial: kind, t0, t1, from, to, trail: true });
    },
    // A stepper dial (the half in `slot`) nudged by `delta` steps between t0 and t1, a finger riding its edge.
    step(kind, slot, t0, t1, delta) {
      const tr = (this.steps[kind] = this.steps[kind] || new Track(0));
      const from = tr.at(t0);
      tr.to(t0, t1, from + delta);
      this.armed.push([t0 - 0.12, t1 + 0.18, kind]);
      this.fingers.push({ dial: kind, slot, step: true, t0, t1, from, to: from + delta, trail: true });
    },
    // pos: where it sits (default F.card; EP.F.talk is the open stretch the phone leaves when it slides away).
    card(t0, t1, draw, pos) {
      this.cards.push({ t0, t1, draw, pos });
    },
    play(t, change) {
      const prev = mediaAt(t);
      this.media.push([t, { ...prev, ...change, base: this.scrubOff.at(t) }]);
      this.media.sort((a, b) => a[0] - b[0]);
    },
  };
  window.DIRECTOR = D;

  // Media: the track playing at t, its position running on while it plays.
  function mediaAt(t) {
    let ev = D.media[0];
    for (const e of D.media) if (e[0] <= t) ev = e;
    const m = ev[1];
    const pos = clamp(m.pos + (m.playing ? t - Math.max(ev[0], 0) : 0) + D.scrubOff.at(t) - (m.base || 0), 0, m.dur);
    return { ...m, pos };
  }
  const fmt = (s) => `${Math.floor(s / 60)}:${String(Math.floor(s % 60)).padStart(2, '0')}`;
  const discrete = (list, t) => {
    let v = list[0][1];
    for (const [at, x] of list) if (at <= t) v = x;
    return v;
  };

  // ---- run the beats: each line's act writes into the tracks ----
  const BEATS = (window.BEATS && window.BEATS[SID]) || [];
  if (!CUT.still && BEATS.length !== LINES.length) console.error(`beats: ${BEATS.length} for ${LINES.length} lines in ${SID}`);
  BEATS.forEach((b, i) => {
    if (b && b.act && LINES[i]) b.act(L(i), D);
  });
  if (CUT.setup) CUT.setup(D);

  // ---- the surface at t ----
  const PER = { p: new EP.Perimeter(390, 844, 44), l: new EP.Perimeter(844, 390, 44) };
  const P4 = [0, 0, 0, 0];
  const LEVEL_SLOT = { VOL: 0, BRI: 3, ZOOM: 4, SCRUB: 7 };
  const LAPTOPS = ['Work laptop', 'Home PC'];
  const stepText = (kind, v) => {
    const n = Math.round(v);
    if (kind === 'DEVICES') return LAPTOPS[((n % 2) + 2) % 2].slice(0, 10);
    return n > 0 ? `+${n}` : '0';
  };
  function onEdge(per, s, inset = 26) {
    per.point(s, P4);
    return [P4[0] + P4[2] * inset, P4[1] + P4[3] * inset];
  }
  function surfaceState(t, pal, land) {
    const per = land ? PER.l : PER.p;
    const S = { pal, radius: 44, dials: {}, fingers: [], trail: [] };
    const split = D.split.at(t);
    S.split = split;
    S.extra = D.extra.at(t);
    S.sleepChip = D.sleep.at(t);
    S.mode = discrete(D.modes, t);
    S.extraKinds = discrete(D.extraKinds, t);
    S.scrubOnADial = discrete(D.scrubOn, t);
    const b = D.bg.at(t);
    S.bg = b > 0 ? { k: b, from: '#0F2A4A', to: '#3A1C5C', angle: 60 } : null;
    const armed = (k) => D.armed.some(([a, z, kind]) => kind === k && t >= a && t <= z);
    const vol = D.vol.at(t), bri = D.bri.at(t), zoom = D.zoom.at(t);
    S.dials.VOL = { ruler: EP.levelToRuler(vol), text: String(Math.round(vol)), armed: armed('VOL') };
    S.dials.BRI = { ruler: EP.levelToRuler(bri), text: String(Math.round(bri)), armed: armed('BRI') };
    S.dials.ZOOM = { ruler: zoom * 22, text: zoom > 0.5 ? `+${Math.round(zoom)}` : '0', armed: armed('ZOOM') };
    S.dials.RATE = { ruler: 0, text: '120 Hz', armed: false };
    for (const kind of ['ZOOM2', 'APPS', 'SCROLL', 'SIDE', 'DESK', 'TABS']) S.dials[kind] = { ruler: 0, text: '', armed: false };
    S.dials.DEVICES = { ruler: 0, text: stepText('DEVICES', 0), armed: false };
    for (const [kind, tr] of Object.entries(D.steps)) {
      const v = tr.at(t);
      S.dials[kind] = { ruler: v * 22, text: stepText(kind, v), armed: armed(kind) };
    }
    const m = mediaAt(t);
    S.media = { a: D.mediaA.at(t), playing: m.playing, app: m.app, title: m.title, sub: `${m.app} · ${fmt(m.pos)} of ${fmt(m.dur)}`, at: [0.5, D.mediaY.at(t)], transport: [0.5, 0.9], press: 0 };
    for (const [pt] of D.presses.filter((p) => p[1] === 'play')) S.media.press = Math.max(S.media.press, tri(t, pt, 0.3));
    S.dials.SCRUB = { ruler: EP.levelToRuler((m.pos / m.dur) * 100), text: fmt(m.pos), armed: armed('SCRUB') };
    S.press = null;
    for (const [pt, name] of D.presses) {
      const k = tri(t, pt, 0.32);
      if (k > 0 && name !== 'play') S.press = { name, k };
    }
    // Fingers: plain paths, and fingers riding a dial along the edge as its level moves.
    let trailFn = null;
    for (const f of D.fingers) {
      if (f.dial) {
        if (t < f.t0 - 0.1 || t > f.t1 + 0.12) continue;
        const kind = f.dial;
        const c = f.step ? per.slotAt(f.slot, 50) : lerp(per.cornerMid[CORNER[kind]], per.slotAt(LEVEL_SLOT[kind], 50), D.split.at(f.t0));
        const dl = f.step || kind === 'ZOOM' ? 22 : 1 / (0.25 * 1.4);
        const fn = (tt) => {
          const k = EP.easeInOut((tt - f.t0) / (f.t1 - f.t0));
          const [x, y] = onEdge(per, c + (lerp(f.from, f.to, k) - f.from) * dl);
          const a = ease((tt - (f.t0 - 0.1)) / 0.08) * (1 - ease((tt - f.t1) / 0.1));
          return a > 0 ? { x, y, a } : null;
        };
        const p = fn(t);
        if (p) {
          S.fingers.push(p);
          trailFn = fn;
        }
      } else {
        const p = EP.path(t, f.keys, true);
        if (p) {
          S.fingers.push(p);
          if (f.trail) trailFn = (tt) => EP.path(tt, f.keys, true);
        }
      }
    }
    if (S.fingers.length === 1 && trailFn) S.trail = EP.trailOf((tt) => trailFn(tt) || S.fingers[0], t);
    S.shaping = D.shaping.some(([a, z]) => t >= a && t <= z);
    return S;
  }
  const tri = (t, t0, d) => {
    const x = (t - t0) / d;
    return x < 0 || x > 1 ? 0 : Math.sin(x * Math.PI);
  };

  // ---- the phone: 390 x 844 dp of screen in a 12 dp bezel, its screens cross-faded ----
  function layers(t, land) {
    const fits = D.screens.filter((s) => s.land === land);
    let c = -1;
    fits.forEach((s, i) => {
      if (s.t <= t) c = i;
    });
    if (c < 0) return land ? [] : [{ s: D.screens[0], a: 1 }];
    const cur = fits[c];
    const a = ease(seg(t, cur.t, cur.t + cur.fade));
    return a < 1 && c > 0 ? [{ s: fits[c - 1], a: 1 }, { s: cur, a }] : [{ s: cur, a: 1 }];
  }
  function drawScreen(ctx, w, h, t, pal, l, land) {
    ctx.save();
    ctx.globalAlpha *= l.a;
    const s = l.s;
    if (s.key === 'surface') EP.drawSurface(ctx, w, h, surfaceState(t, pal, land));
    else {
      ctx.fillStyle = rgba(pal.bg);
      ctx.fillRect(0, 0, w, h);
      let o = resolve(s.o, t);
      if (s.key === 'controls' || s.key === 'xfer' || s.key === 'clipboard') {
        // A popup, card or dialog is drawn over the surface as it stands; a Controls button press is the popup's, not the film's.
        const under = surfaceState(t, pal, land);
        delete under.press;
        o = { ...o, under };
      }
      EP.screens[s.key](ctx, w, h, pal, EP.warp(t, s.anchors), o);
    }
    ctx.restore();
  }
  function drawPhone(ctx, t) {
    const cam = D.cam.at(t);
    const tilt = D.tilt.at(t);
    const mag = Math.max(Math.abs(tilt.yaw), Math.abs(tilt.pitch));
    // The hand does not tilt with the phone: it fades out over the first 3 degrees.
    const hand = D.hand.at(t) * (1 - seg(mag, 0, 3));
    if (hand > 0.003) EP.hand(ctx, { ...cam, a: hand });
    if (mag > 0.01) return drawTilted(ctx, t, cam, tilt);
    ctx.save();
    ctx.translate(cam.x, cam.y);
    ctx.scale(cam.s, cam.s);
    ctx.rotate((cam.r * Math.PI) / 180);
    ctx.translate(-207, -434);
    phoneBody(ctx, t, cam.s);
    ctx.restore();
  }
  // M-TILT: the phone drawn flat on a cached offscreen canvas (its shadow included), then painted as a perspective mesh of
  // 10 x 20 cells, two affine triangles each. Yaw turns it about its vertical axis (positive: right edge toward the viewer),
  // pitch about its horizontal axis (positive: top edge toward the viewer), then the camera distance is 2.2 phone heights.
  const MESH = [10, 20];
  let tiltCv = null;
  function drawTilted(ctx, t, cam, tilt) {
    const pw = 414 * cam.s, ph = 868 * cam.s, m = Math.ceil(130 * cam.s); // m holds the shadow: blur 70, offset 30
    const cw = Math.ceil(pw + 2 * m), ch = Math.ceil(ph + 2 * m);
    if (!tiltCv || tiltCv.width < cw || tiltCv.height < ch) tiltCv = Object.assign(document.createElement('canvas'), { width: cw, height: ch });
    const oc = tiltCv.getContext('2d');
    oc.setTransform(1, 0, 0, 1, 0, 0);
    oc.clearRect(0, 0, tiltCv.width, tiltCv.height);
    oc.translate(m, m);
    oc.scale(cam.s, cam.s);
    phoneBody(oc, t, cam.s);
    const yaw = (tilt.yaw * Math.PI) / 180, pitch = (tilt.pitch * Math.PI) / 180;
    const cy = Math.cos(yaw), sy = Math.sin(yaw), cp = Math.cos(pitch), sp = Math.sin(pitch);
    const dist = 2.2 * ph;
    const [nx, ny] = MESH;
    const src = [], dst = [];
    for (let j = 0; j <= ny; j++) {
      for (let i = 0; i <= nx; i++) {
        const u = (i / nx) * cw, v = (j / ny) * ch;
        const X = u - m - pw / 2, Y = v - m - ph / 2;
        const z1 = -X * sy, x1 = X * cy;
        const y2 = Y * cp - z1 * sp, z2 = Y * sp + z1 * cp;
        const f = dist / Math.max(dist + z2, 1e-3);
        src.push([u, v]);
        dst.push([x1 * f, y2 * f]);
      }
    }
    ctx.save();
    ctx.translate(cam.x, cam.y);
    ctx.rotate((cam.r * Math.PI) / 180);
    const at = (i, j) => j * (nx + 1) + i;
    const tri = (a, b, c) => {
      const [s0, s1, s2] = [src[a], src[b], src[c]], [d0, d1, d2] = [dst[a], dst[b], dst[c]];
      const u1x = s1[0] - s0[0], u1y = s1[1] - s0[1], u2x = s2[0] - s0[0], u2y = s2[1] - s0[1];
      const det = u1x * u2y - u2x * u1y;
      if (Math.abs(det) < 1e-9) return;
      const w1x = d1[0] - d0[0], w1y = d1[1] - d0[1], w2x = d2[0] - d0[0], w2y = d2[1] - d0[1];
      if (Math.abs(w1x * w2y - w2x * w1y) < 1e-6) return;
      const A = (w1x * u2y - w2x * u1y) / det, C = (w2x * u1x - w1x * u2x) / det;
      const B = (w1y * u2y - w2y * u1y) / det, Dd = (w2y * u1x - w1y * u2x) / det;
      const gx = (d0[0] + d1[0] + d2[0]) / 3, gy = (d0[1] + d1[1] + d2[1]) / 3;
      ctx.save();
      ctx.beginPath();
      [d0, d1, d2].forEach(([x, y], k) => {
        const l = Math.hypot(x - gx, y - gy) || 1;
        const px = x + (0.5 * (x - gx)) / l, py = y + (0.5 * (y - gy)) / l;
        if (k === 0) ctx.moveTo(px, py);
        else ctx.lineTo(px, py);
      });
      ctx.closePath();
      ctx.clip();
      ctx.transform(A, B, C, Dd, d0[0] - A * s0[0] - C * s0[1], d0[1] - B * s0[0] - Dd * s0[1]);
      const x0 = Math.max(0, Math.min(s0[0], s1[0], s2[0]) - 2), y0 = Math.max(0, Math.min(s0[1], s1[1], s2[1]) - 2);
      const x1 = Math.min(cw, Math.max(s0[0], s1[0], s2[0]) + 2), y1 = Math.min(ch, Math.max(s0[1], s1[1], s2[1]) + 2);
      ctx.drawImage(tiltCv, x0, y0, x1 - x0, y1 - y0, x0, y0, x1 - x0, y1 - y0);
      ctx.restore();
    };
    for (let j = 0; j < ny; j++) {
      for (let i = 0; i < nx; i++) {
        tri(at(i, j), at(i + 1, j), at(i, j + 1));
        tri(at(i + 1, j), at(i + 1, j + 1), at(i, j + 1));
      }
    }
    ctx.restore();
  }
  // The phone's own drawing in dp, origin at the top-left of its 414 x 868 body; `s` is the page scale (shadow sizes are in page px).
  function phoneBody(ctx, t, s) {
    const turn = D.turn.at(t);
    const pal = EP.palette(D.theme.at(t));
    // Shadow under the body: black, deep and long, for a ground this dark.
    ctx.save();
    ctx.shadowColor = 'rgba(0,0,0,0.6)';
    ctx.shadowBlur = 70 * s;
    ctx.shadowOffsetY = 30 * s;
    fillRR(ctx, 0, 0, 414, 868, 58, '#0B0B0E');
    ctx.restore();
    // Side keys on the right edge, then the frame, with a thin rim light on the edge that faces the glow.
    fillRR(ctx, 412, 196, 4, 74, 2, '#2E3036');
    fillRR(ctx, 412, 300, 4, 116, 2, '#2E3036');
    fillRR(ctx, 0, 0, 414, 868, 58, '#0B0B0E');
    const lit = ctx.createLinearGradient(0, 0, 414, 868);
    lit.addColorStop(0, 'rgba(200,218,255,0.62)');
    lit.addColorStop(0.45, 'rgba(120,150,210,0.2)');
    lit.addColorStop(1, 'rgba(200,218,255,0.38)');
    ctx.lineWidth = 1.6;
    ctx.strokeStyle = lit;
    rr(ctx, 0.8, 0.8, 412.4, 866.4, 57.2);
    ctx.stroke();
    ctx.save();
    rr(ctx, 12, 12, 390, 844, 46);
    ctx.clip();
    const sideways = ease(seg(turn, 0.35, 0.65));
    ctx.save();
    ctx.translate(12, 12);
    if (sideways < 1) for (const l of layers(t, false)) drawScreen(ctx, 390, 844, t, pal, { ...l, a: l.a * (1 - sideways) }, false);
    ctx.restore();
    if (sideways > 0) {
      ctx.save();
      ctx.translate(12 + 390, 12);
      ctx.rotate(Math.PI / 2);
      for (const l of layers(t, true)) drawScreen(ctx, 844, 390, t, pal, { ...l, a: l.a * sideways }, true);
      ctx.restore();
    }
    const glint = D.glint.at(t);
    if (glint > 0.003) {
      // A faint, soft, unresolved shape: two overlapping blurred blobs, never a UI.
      ctx.save();
      ctx.translate(12, 12);
      for (const [x, y, rx, ry, a] of [[200, 400, 110, 60, 0.2], [250, 450, 70, 90, 0.14]]) {
        const g = ctx.createRadialGradient(0, 0, 0, 0, 0, 1);
        g.addColorStop(0, `rgba(${GLINT_RGB},${a * glint})`);
        g.addColorStop(1, `rgba(${GLINT_RGB},0)`);
        ctx.save();
        ctx.translate(x, y);
        ctx.scale(rx, ry);
        ctx.fillStyle = g;
        ctx.fillRect(-1, -1, 2, 2);
        ctx.restore();
      }
      ctx.restore();
    }
    ctx.restore();
    // The punch-hole camera.
    ctx.fillStyle = '#121318';
    ctx.beginPath();
    ctx.arc(207, 30, 6.5, 0, Math.PI * 2);
    ctx.fill();
    ctx.fillStyle = 'rgba(60,80,140,0.5)';
    ctx.beginPath();
    ctx.arc(205.5, 28.5, 2, 0, Math.PI * 2);
    ctx.fill();
  }

  // ---- the light behind the phone: slow, soft and blue, swelling at each chapter start. A pure function of t. ----
  const STARTS = (T.chapters || []).map((c) => c.start);
  function glow(ctx, t) {
    let swell = 0;
    for (const s of STARTS) {
      const d = t - s;
      if (d > -0.2 && d < 6) swell += 0.1 * Math.min(1, (d + 0.2) / 0.5) * Math.exp(-Math.max(0, d) / 1.6);
    }
    const a = 0.7 * (0.14 + 0.025 * Math.sin(t * 0.45) + Math.min(swell, 0.16));
    const cam = D.cam.at(t);
    const x = cam.x, y = clamp(cam.y, H * 0.25, H * 0.8), r = TALL ? W * 1.0 : W * 0.44;
    const g = ctx.createRadialGradient(x, y, 0, x, y, r);
    g.addColorStop(0, `rgba(70,130,235,${a})`);
    g.addColorStop(0.5, `rgba(60,110,210,${a * 0.38})`);
    g.addColorStop(1, 'rgba(60,110,210,0)');
    ctx.fillStyle = g;
    ctx.fillRect(0, 0, W, H);
  }

  // ---- the laptop: on its stand in the hook's room (EP.room), then where D.lap puts it; in the room it is also the TV ----
  // The screen's content at t: the current kind, cross-dissolved over 0.4 s from the one before it (not at the very start).
  function lapContent(c, w, h, t) {
    const list = D.lapScreens;
    let i = 0;
    list.forEach((e, j) => {
      if (e.t <= t) i = j;
    });
    const paint = (e, a) => {
      c.save();
      c.globalAlpha *= a;
      EP.laptopScreen(c, w, h, t, e.kind, { tr: D.raceAt(t), ...resolve(e.o, t), glint: D.glint.at(t) });
      c.restore();
    };
    const k = list[i].t < 0.05 ? 1 : EP.easeInOut(seg(t, list[i].t, list[i].t + 0.4));
    if (i > 0 && k < 1) paint(list[i - 1], 1);
    paint(list[i], k);
  }
  // D.dolly: the room and the laptop close in by up to 6% about the page centre.
  function dollyOn(c, t) {
    const z = 1 + 0.06 * D.dolly.at(t);
    c.translate(W / 2, H / 2);
    c.scale(z, z);
    c.translate(-W / 2, -H / 2);
  }
  // The room behind everything while D.room < 1. Returns the laptop's screen rect (top-left x, y, w, h) and alpha at t: on
  // the stand at first, then easing into the D.lap pose as D.room runs from 0.5 to 1, so there is no cut.
  function roomBack(c, t) {
    const k = D.room.at(t), p = D.lapT.at(t);
    const pose = { x: p.x - p.w / 2, y: p.y - p.w / 3.2, w: p.w, h: p.w / 1.6, a: p.a };
    if (k >= 1) return pose;
    c.save();
    dollyOn(c, t);
    const r = EP.room(c, W, H, { k, tv: D.roomFx.tv.at(t), beam: D.roomFx.beam.at(t), lamp: D.roomFx.lamp.at(t), screen: (cc, w, h) => lapContent(cc, w, h, t) });
    c.restore();
    const b = ease(seg(k, 0.5, 1));
    return { x: lerp(r.x, pose.x, b), y: lerp(r.y, pose.y, b), w: lerp(r.w, pose.w, b), h: lerp(r.h, pose.h, b), a: lerp(1, pose.a, b) };
  }
  function laptopFront(c, t, st) {
    if (st.a <= 0.003) return;
    c.save();
    dollyOn(c, t);
    EP.laptop(c, st.x, st.y, st.w, st.h, (cc, w, h) => lapContent(cc, w, h, t), { a: st.a });
    c.restore();
  }

  // ---- the marks that travel over the page: the ident and its hand-off, flying chips, the straight-talk line ----
  const PHONE_HALF = [207, 434]; // the body's half size in dp
  // Where the top-left dial (or another corner's) sits on the upright phone: 28 dp in from the screen edge, in page px.
  function dialPoint(cam, corner) {
    const p = [0, 0, 0, 0];
    PER.p.point(PER.p.cornerMid[corner], p);
    const dx = (p[0] + p[2] * 28 + 12 - PHONE_HALF[0]) * cam.s, dy = (p[1] + p[3] * 28 + 12 - PHONE_HALF[1]) * cam.s;
    const r = (cam.r * Math.PI) / 180;
    return { x: cam.x + dx * Math.cos(r) - dy * Math.sin(r), y: cam.y + dx * Math.sin(r) + dy * Math.cos(r) };
  }
  const spot = (spec, t) => {
    if (spec === 'phone') return D.cam.at(t);
    if (spec === 'laptop') return D.lapT.at(t);
    return spec;
  };
  // M-IDENT / M-HANDOFF: the mark from its start; on its hand-off it shrinks to 48 dp and glides (400 ms, in-out) to the
  // dial, fading over the phone's own ruler in the last third.
  function drawIdents(c, t) {
    for (const e of D.idents) {
      const ms = (t - e.t0) * 1000;
      if (ms < 0) continue;
      const { until, ...io } = e.o;
      let { x, y, size } = e;
      let a = until === undefined ? 1 : 1 - ease(seg(t, until, until + 0.3));
      if (e.hand) {
        const p = EP.easeInOut(seg(t, e.hand.t, e.hand.t + 0.4));
        if (t >= e.hand.t + 0.4) continue;
        const cam = D.cam.at(t), d = dialPoint(cam, e.hand.corner);
        x = lerp(x, d.x, p);
        y = lerp(y, d.y, p);
        size = lerp(size, 48 * cam.s, p);
        a *= 1 - ease(seg(p, 0.65, 1));
      }
      if (a > 0.003) EP.ident(c, x - size / 2, y - size / 2, size, ms, { ...io, a: (io.a ?? 1) * a });
    }
  }
  // M-FLY: a quadratic arc, its control point 18% of the distance above the midpoint; scale 1 -> 0.6 -> 1, the shadow
  // deepening at the apex, then a 120 ms squash 1.04 -> 1 on landing.
  const LAND = 0.12;
  function drawFlies(c, t) {
    for (const e of D.flies) {
      if (t < e.t0 || t > e.t0 + e.dur + LAND) continue;
      const p = EP.easeInOut(seg(t, e.t0, e.t0 + e.dur));
      const A = spot(e.from, t), B = spot(e.to, t);
      const d = Math.hypot(B.x - A.x, B.y - A.y);
      const mx = (A.x + B.x) / 2, my = (A.y + B.y) / 2 - 0.18 * d;
      const x = (1 - p) * (1 - p) * A.x + 2 * (1 - p) * p * mx + p * p * B.x;
      const y = (1 - p) * (1 - p) * A.y + 2 * (1 - p) * p * my + p * p * B.y;
      const apex = Math.sin(Math.PI * p);
      const land = seg(t, e.t0 + e.dur, e.t0 + e.dur + LAND);
      const sc = t > e.t0 + e.dur ? 1 + 0.04 * (1 - EP.easeOut(land)) : 1 - 0.4 * apex;
      c.save();
      c.translate(x, y);
      c.scale(sc, sc);
      c.shadowColor = 'rgba(0,0,0,0.5)';
      c.shadowBlur = 10 + 40 * apex;
      c.shadowOffsetY = 4 + 26 * apex;
      e.draw(c, p);
      c.restore();
    }
  }
  // The straight-talk line: drawn on over 0.5 s from the phone's edge to the laptop's, a pulse riding it until t1.
  function drawLines(c, t) {
    for (const e of D.lines) {
      if (t < e.t0 || t > e.t1 + 0.3) continue;
      const A = spot(e.from, t), B = spot(e.to, t);
      const dx = B.x - A.x, dy = B.y - A.y, d = Math.hypot(dx, dy) || 1, ux = dx / d, uy = dy / d;
      const cam = D.cam.at(t), lp = D.lapT.at(t);
      const trim = (spec, hw, hh) => (spec === 'phone' || spec === 'laptop' ? Math.min(hw / Math.max(Math.abs(ux), 1e-3), hh / Math.max(Math.abs(uy), 1e-3)) : 0);
      const a0 = trim(e.from, e.from === 'phone' ? PHONE_HALF[0] * cam.s : lp.w / 2, e.from === 'phone' ? PHONE_HALF[1] * cam.s : lp.w / 3.2);
      const b0 = trim(e.to, e.to === 'phone' ? PHONE_HALF[0] * cam.s : lp.w / 2, e.to === 'phone' ? PHONE_HALF[1] * cam.s : lp.w / 3.2);
      const len = Math.max(0, d - a0 - b0);
      const grow = ease(seg(t, e.t0, e.t0 + 0.5));
      const out = 1 - ease(seg(t, e.t1, e.t1 + 0.3));
      const sx = A.x + ux * a0, sy = A.y + uy * a0;
      c.save();
      c.globalAlpha *= out;
      c.lineCap = 'round';
      c.lineWidth = 4;
      c.strokeStyle = `rgba(${BLUE_RGB},0.7)`;
      c.beginPath();
      c.moveTo(sx, sy);
      c.lineTo(sx + ux * len * grow, sy + uy * len * grow);
      c.stroke();
      if (t > e.t0 + 0.5) {
        const u = (((t - e.t0 - 0.5) / 1.4) % 1) * len;
        const px = sx + ux * u, py = sy + uy * u;
        const g = c.createRadialGradient(px, py, 0, px, py, 26);
        g.addColorStop(0, `rgba(${BLUE_RGB},0.9)`);
        g.addColorStop(1, `rgba(${BLUE_RGB},0)`);
        c.fillStyle = g;
        c.beginPath();
        c.arc(px, py, 26, 0, Math.PI * 2);
        c.fill();
        c.fillStyle = INK;
        c.beginPath();
        c.arc(px, py, 5, 0, Math.PI * 2);
        c.fill();
      }
      c.restore();
    }
  }

  // ---- the frame ----
  const canvas = document.getElementById('film-canvas');
  canvas.width = W;
  canvas.height = H;
  const ctx = canvas.getContext('2d');
  let endMark = null; // the end card's canvas mark { ctx, size, t0 }, set where the end card is built
  function drawFrame(t) {
    ctx.save();
    ctx.fillStyle = GROUND;
    ctx.fillRect(0, 0, W, H);
    const lapNow = roomBack(ctx, t);
    glow(ctx, t);
    // The tall cuts stack the laptop, the headline and the phone: while the laptop band is up, the phone is drawn first and
    // the ground covers it down to the foot of the headline, so a pushed-in phone never runs under the text or the laptop.
    const band = F.tall ? lapNow.a * ease(seg(D.room.at(t), 0.6, 1)) : 0;
    if (band > 0.003) {
      // A beat whose text runs deeper than two headline lines (a sub-line under them) pushes the phone and the scrim down by that much.
      const deep = lapDeep(t);
      ctx.save();
      ctx.translate(0, deep * band);
      drawPhone(ctx, t);
      ctx.restore();
      const foot = LAP_FOOT + deep;
      ctx.save();
      ctx.globalAlpha = band;
      const g = ctx.createLinearGradient(0, foot, 0, foot + 40);
      g.addColorStop(0, GROUND);
      g.addColorStop(1, 'rgba(30,32,38,0)');
      ctx.fillStyle = GROUND;
      ctx.fillRect(0, 0, W, foot);
      ctx.fillStyle = g;
      ctx.fillRect(0, foot, W, 40);
      ctx.restore();
    }
    laptopFront(ctx, t, lapNow);
    if (band <= 0.003) drawPhone(ctx, t);
    // A scrim of the ground under the headline, only while the camera is pushed in and no laptop band is shown; at rest there is none.
    const push = F.scrim ? ease(seg(D.cam.at(t).s, CAM.hero.s * 1.1, CAM.hero.s * 1.4)) * (1 - lapNow.a) : 0;
    if (push > 0.003) {
      ctx.save();
      ctx.globalAlpha = push;
      const g = ctx.createLinearGradient(0, 0, 0, F.scrim.to);
      g.addColorStop(0, GROUND);
      g.addColorStop(F.scrim.solid / F.scrim.to, GROUND);
      g.addColorStop(1, 'rgba(30,32,38,0)');
      ctx.fillStyle = g;
      ctx.fillRect(0, 0, W, F.scrim.to);
      ctx.restore();
    }
    drawLines(ctx, t);
    drawIdents(ctx, t);
    drawFlies(ctx, t);
    for (const c of D.cards) {
      const a = ease(seg(t, c.t0, c.t0 + 0.4)) * (1 - ease(seg(t, c.t1 - 0.35, c.t1)));
      if (a > 0) c.draw(ctx, t, a, c.pos || F.card);
    }
    ctx.restore();
    // The end card's mark lives on its own small canvas, redrawn here from the same clock as everything else.
    if (endMark) {
      endMark.ctx.clearRect(0, 0, endMark.size, endMark.size);
      EP.ident(endMark.ctx, 0, 0, endMark.size, (t - endMark.t0) * 1000, { loop: true });
    }
  }
  window.drawFrame = drawFrame;

  // ---- the DOM text ----
  const layer = document.getElementById('film-text');
  const mctx = document.createElement('canvas').getContext('2d');
  const widthOf = (s, weight, size, track = 0) => {
    mctx.font = `${weight} ${size}px Lato`;
    mctx.letterSpacing = `${track * size}px`;
    return mctx.measureText(s).width;
  };
  // "Every corner is a *dial.*" -> words with a keyword flag; the stars may wrap one word or several.
  function parse(s) {
    let key = false;
    return s.split(/\s+/).filter(Boolean).map((tok) => {
      let k = key;
      if (tok.startsWith('*')) {
        tok = tok.slice(1);
        k = key = true;
      }
      if (tok.endsWith('*')) {
        tok = tok.slice(0, -1);
        key = false;
      }
      return { w: tok, k };
    });
  }
  // Greedy wrap at maxWidth, measured in the face it is set in.
  function wrap(words, weight, size, max, track) {
    const lines = [[]];
    for (const wd of words) {
      // A lone | in the text is a line break the writer asked for.
      if (wd.w === '|') {
        lines.push([]);
        continue;
      }
      const cur = lines[lines.length - 1];
      const next = [...cur, wd].map((x) => x.w).join(' ');
      if (cur.length && widthOf(next, weight, size, track) > max) lines.push([wd]);
      else cur.push(wd);
    }
    return lines;
  }
  // When each word appears: on its spoken word in this line where there is one, else just after the one before.
  function times(words, line, from) {
    let p = 0;
    const out = words.map((wd) => {
      const n = norm(wd.w);
      for (let j = p; j < line.words.length; j++) {
        if (n && norm(line.words[j][0]) === n) {
          p = j + 1;
          return line.words[j][1];
        }
      }
      return null;
    });
    let last = from - 0.1;
    for (let i = 0; i < out.length; i++) {
      if (out[i] === null || out[i] < last) out[i] = last + 0.1;
      last = out[i];
    }
    return out;
  }
  function block(cls, x, y) {
    const el = document.createElement('div');
    el.className = cls;
    el.style.left = `${x}px`;
    el.style.top = `${y}px`;
    layer.appendChild(el);
    return el;
  }
  function words(el, lines) {
    const spans = [];
    lines.forEach((ln) => {
      const row = document.createElement('div');
      row.className = 'ln';
      ln.forEach((wd, i) => {
        const s = document.createElement('span');
        s.className = wd.k ? 'w k' : 'w';
        s.textContent = wd.w;
        row.appendChild(s);
        if (i < ln.length - 1) row.appendChild(document.createTextNode(' '));
        spans.push(s);
      });
      el.appendChild(row);
    });
    return spans;
  }
  const reveal = (tl, spans, ts) =>
    spans.forEach((s, i) => tl.fromTo(s, { opacity: 0, y: 26, filter: 'blur(14px)' }, { opacity: 1, y: 0, filter: 'blur(0px)', duration: 0.55, ease: 'power3.out' }, Math.max(0, ts[i] - 0.06)));
  const leave = (tl, el, t) => tl.to(el, { opacity: 0, y: -14, filter: 'blur(8px)', duration: 0.32, ease: 'power2.in' }, Math.max(0, t - 0.3));

  // The tall cuts' text depth under a laptop beat: [t, px beyond LAP_FOOT], eased from one beat's to the next over 0.4 s.
  const LAP_FOOT = F.lap.y + F.head.size * 2.1 + 20;
  const lapDeeps = [[-1, 0]];
  function lapDeep(t) {
    let v = 0;
    for (const [at, px] of lapDeeps) v = lerp(v, px, ease(seg(t, at, at + 0.4)));
    return v;
  }
  function buildText(tl) {
    let head = null; // { n, lay, size }: the layout of the headline on screen (its tallest entry)
    let cur = null; // { el, from, to }: the element on screen, kept so D.dim can find it
    const heads = [];
    let subEnd = []; // subs to retire at the next line
    let endEl = null;
    const retire = (t) => {
      if (!cur) return;
      leave(tl, cur.el, t);
      cur.to = t;
      cur = null;
    };
    BEATS.forEach((b, i) => {
      const line = LINES[i];
      if (!b || !line) return;
      const base = b.lay === 'top' ? F.top : F.head;
      const lay = b.lap && b.lay !== 'top' ? { ...base, y: F.lap.y } : base;
      const subSize = b.lay === 'top' ? F.topSub : F.sub.size;
      if (b.end) {
        retire(line.start - 0.05);
        for (const s of subEnd) leave(tl, s, line.start - 0.02);
        head = null;
        subEnd = [];
        endEl = buildEnd(tl, i);
        return;
      }
      if (b.close) return buildClose(tl, b, i, endEl);
      if (b.h !== undefined && b.h !== null) {
        retire(line.start - 0.05);
        head = null;
        // h is a string, or ['text', 'text@word', ...]: each entry replaces the one before at its word.
        const entries = (Array.isArray(b.h) ? b.h : [b.h]).map((raw) => {
          const [txt, anchor] = raw.split('@');
          return { txt: txt.trim(), late: Boolean(anchor), at: anchor ? L(i).w(anchor) : line.start };
        });
        entries.forEach((en, k) => {
          if (k > 0) retire(en.at - 0.05);
          if (!en.txt) return;
          const ws = parse(en.txt);
          const lines = wrap(ws, 900, lay.size, lay.max, -0.01);
          const el = block('hl', lay.x, lay.y);
          el.style.fontSize = `${lay.size}px`;
          const spoken = ws.filter((x) => x.w !== '|');
          reveal(tl, words(el, lines), en.late || k > 0 ? spoken.map((_, j) => en.at + j * 0.09) : times(spoken, line, line.start));
          cur = { el, from: en.at, to: DUR };
          heads.push(cur);
          head = { n: Math.max(head ? head.n : 0, lines.length), lay, size: lay.size };
        });
      }
      for (const s of subEnd) leave(tl, s, line.start - 0.02);
      subEnd = [];
      const subs = b.s === undefined || b.s === null ? [] : Array.isArray(b.s) ? b.s : [b.s];
      const top = head ? head.lay.y + head.n * head.size * 1.0 + (b.lay === 'top' ? 18 : F.sub.gap) : lay.y;
      const y = b.lay === 'top' ? top : Math.max(b.lap ? 0 : F.sub.min, top);
      let subLines = 0;
      subs.forEach((raw, k) => {
        const [s, anchor] = raw.split('@');
        const ws = parse(s).filter((x) => x.w !== '|');
        const el = block('sub', lay.x, y);
        el.style.fontSize = `${subSize}px`;
        const from = anchor ? L(i).w(anchor) : line.start;
        const wrapped = wrap(ws, 700, subSize, lay.max, 0);
        subLines = Math.max(subLines, wrapped.length);
        reveal(tl, words(el, wrapped), anchor ? ws.map((_, j) => from + j * 0.09) : times(ws, line, from));
        if (k + 1 < subs.length) {
          const nxt = subs[k + 1].split('@')[1];
          leave(tl, el, L(i).w(nxt));
        } else subEnd.push(el);
      });
      if (F.tall && b.lap) {
        const bottom = subLines ? y + subLines * subSize * 1.15 : head ? head.lay.y + head.n * head.size : 0;
        lapDeeps.push([line.start - 0.3, Math.max(0, bottom + 24 - LAP_FOOT)]);
      }
    });
    // D.dim: the headline on screen at t0 fades to `to` and comes back at t1 (not when it is leaving by then).
    for (const d of D.dims) {
      const h = heads.find((x) => d.t0 >= x.from - 0.01 && d.t0 < x.to);
      if (!h) continue;
      tl.to(h.el, { opacity: d.to, duration: 0.3, ease: 'power2.out' }, d.t0);
      if (d.t1 < h.to - 0.3) tl.to(h.el, { opacity: 1, duration: 0.3, ease: 'power2.out' }, d.t1);
    }
    buildCaptions(tl);
  }

  // The end card: the mark, "Edgepad", "Free & open source." and the GitHub button, each on its spoken word.
  function buildEnd(tl, i) {
    const l = L(i);
    const el = block('end', F.head.x, F.end.y);
    el.style.width = `${F.end.w}px`;
    const size = F.end.mark;
    el.innerHTML = `<canvas class="end-mark" width="${size}" height="${size}" style="width:${size}px;height:${size}px"></canvas><div class="end-name"><span class="w">Edgepad</span></div><div class="end-tag"><span class="w">Free</span> <span class="w">&amp;</span> <span class="w k">open</span> <span class="w k">source.</span></div><div class="end-btn"><svg viewBox="0 0 24 24" aria-hidden="true"><path fill="#0B1A2E" d="${window.EP_BRANDS.github}"/></svg><span>github.com/akshit-bansal11/edgepad</span></div>`;
    const [mark, name, tag, btn] = el.children;
    // The mark is the dial ident on its own canvas (drawFrame redraws it from the clock), its ruler scrolling on from the frame it lands.
    endMark = { ctx: mark.getContext('2d'), size, t0: l.t0 - 0.02 };
    tl.fromTo(mark, { opacity: 0, scale: 0.7, filter: 'blur(12px)' }, { opacity: 1, scale: 1, filter: 'blur(0px)', duration: 0.7, ease: 'back.out(1.5)' }, l.t0 - 0.02);
    // The mark never rests: a slow float and tilt from when it lands to the last frame (a finite count, so the length is fixed).
    const from = l.t0 + 0.7;
    const swing = 1.8;
    tl.to(mark, { y: -12, rotation: 3, duration: swing, ease: 'sine.inOut', yoyo: true, repeat: Math.max(1, Math.floor((DUR - from) / swing) - 1) }, from);
    reveal(tl, [name.firstChild], [l.w('edgepad')]);
    const tw = tag.querySelectorAll('.w');
    reveal(tl, [...tw], [l.w('free'), l.w('free') + 0.12, l.w('open'), l.w('source')]);
    tl.fromTo(btn, { opacity: 0, y: 24, scale: 0.94 }, { opacity: 1, y: 0, scale: 1, duration: 0.6, ease: 'power3.out' }, l.w('github') - 0.1);
    return el;
  }

  // The closing line lands under the button, word by word, while the mark above it is still moving.
  function buildClose(tl, b, i, endEl) {
    const line = LINES[i];
    const ws = parse(b.h);
    const one = ws.map((x) => x.w).join(' ');
    const size = Math.min(F.end.close, Math.floor((100 * F.end.w) / widthOf(one, 900, 100, -0.01)));
    const el = document.createElement('div');
    el.className = 'end-close';
    el.style.fontSize = `${size}px`;
    endEl.appendChild(el);
    reveal(tl, words(el, [ws]), times(ws, line, line.start));
  }

  // The caption pill: two to four words at a time around the voice. Spoken words in ink, the one being said in
  // blue, the ones to come in grey; the pill is sized to its words and centred on the format's caption point.
  function buildCaptions(tl) {
    const size = F.cap.size;
    LINES.forEach((line, li) => {
      const chunks = [];
      let cur = [];
      line.words.forEach((w, i) => {
        cur.push(w);
        const last = i === line.words.length - 1;
        const left = line.words.length - 1 - i;
        if (last || cur.length >= 4 || (/[.,?!:]$/.test(w[0]) && cur.length >= 2 && left !== 1)) {
          chunks.push(cur);
          cur = [];
        }
      });
      // A lone last word joins the chunk before it.
      if (chunks.length > 1 && chunks[chunks.length - 1].length === 1 && chunks[chunks.length - 2].length < 4) chunks[chunks.length - 2].push(...chunks.pop());
      chunks.forEach((c, ci) => {
        const label = c.map((w) => w[0]).join(' ');
        const width = widthOf(label, 700, size) + 64;
        const el = block('pill', F.cap.cx - width / 2, F.cap.cy - (size * 1.5) / 2);
        el.style.width = `${width}px`;
        el.style.fontSize = `${size}px`;
        el.style.height = `${size * 1.5}px`;
        el.style.lineHeight = `${size * 1.5}px`;
        const spans = c.map((w, i) => {
          const s = document.createElement('span');
          s.textContent = w[0];
          el.appendChild(s);
          if (i < c.length - 1) el.appendChild(document.createTextNode(' '));
          return s;
        });
        const start = c[0][1];
        const nextChunk = ci + 1 < chunks.length ? chunks[ci + 1][0][1] : li + 1 < LINES.length ? LINES[li + 1].start : DUR;
        const end = Math.min(nextChunk - 0.03, c[c.length - 1][2] + 0.45);
        tl.fromTo(el, { opacity: 0, y: 10 }, { opacity: 1, y: 0, duration: 0.16, ease: 'power2.out' }, Math.max(0, start - 0.1));
        tl.to(el, { opacity: 0, duration: 0.12, ease: 'power1.in' }, Math.max(start, end - 0.12));
        spans.forEach((s, i) => {
          tl.set(s, { color: BLUE }, c[i][1]);
          if (i + 1 < spans.length) tl.set(s, { color: INK }, c[i + 1][1]);
        });
      });
    });
  }

  // ---- laptop cards, drawn on the canvas in page pixels (EP.cards, ui/beats.js) ----

  // ---- build, then register: fonts and pictures first, so no frame is drawn before they are there ----
  window.__filmReady = (async () => {
    await Promise.all([400, 700, 900].map((w) => document.fonts.load(`${w} 40px Lato`)));
    await EP.loadImages(`${BASE}brand/`);
    const tl = gsap.timeline({ paused: true });
    if (!CUT.still) buildText(tl);
    else CUT.still(layer, F);
    const clock = { t: 0 };
    tl.to(clock, { t: DUR, duration: DUR, ease: 'none', onUpdate: () => drawFrame(clock.t) }, 0);
    drawFrame(0);
    const q = new URLSearchParams(location.search);
    // A still for checking by eye: ?t=seconds, or ?line=N&f=0..1 for a point through line N (0-based).
    const at = q.has('line') ? ((l) => l.start + parseFloat(q.get('f') || '0.5') * (l.end - l.start))(LINES[+q.get('line')]) : parseFloat(q.get('t'));
    if (q.has('t') || q.has('line')) {
      const t = at;
      tl.seek(t, false);
      drawFrame(t);
    }
    return tl; // the composition registers it inline, once built: an early, empty registration renders blank
  })().catch((e) => {
    // A failed build would otherwise be a silent blank frame: say why where a screenshot or the DOM shows it.
    document.documentElement.dataset.filmError = String(e && e.stack ? e.stack : e);
    throw e;
  });
})();
