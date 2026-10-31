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
  // The dark ground and the three inks on it. Contrast against the ground (relative luminance 0.0145): INK 14.7:1, GREY 5.9:1,
  // BLUE 6.2:1. On the caption pill #2B2D33 (0.0263): INK 12.4:1, BLUE 5.2:1, GREY 5.0:1. The app's own dark theme (#0E0F12)
  // is darker than the ground, so the phone reads as a dark object on a lighter room.
  const INK = '#F2F3F5', BLUE = '#4DA3FF', GREY = '#9A9CA3', GROUND = '#1E2026';

  // ---- layout per format: where the headline, the sub-line, the caption pill and the laptop cards sit ----
  // 169 the phone stands on the right; 916 headline above, phone below; 34 (1080 x 1440) the same, tighter, so headline and phone fit.
  const LAY = W > H ? '169' : H / W > 1.6 ? '916' : '34';
  const HEAD916 = { x: 80, y: 210, size: 96, max: 920 };
  const HEAD34 = { x: 72, y: 84, size: 84, max: 936 };
  const F = {
    '169': { head: { x: 150, y: 250, size: 110, max: 800 }, top: { x: 360, y: 42, size: 96, max: 1300 }, sub: { size: 56, gap: 52, min: 488 }, topSub: 44, cap: { cx: 550, cy: 990, size: 44 }, card: { x: 150, y: 640, s: 1 }, end: { y: 200, close: 56, w: 1000 }, room: { x: 0.69, y: 0.5, u: 0.0062 }, band: null, scrim: null },
    '916': { head: HEAD916, top: HEAD916, sub: { size: 50, gap: 36, min: 0 }, topSub: 50, cap: { cx: 540, cy: 1664, size: 44 }, card: { x: 90, y: 1130, s: 0.8 }, end: { y: 230, close: 54, w: 920 }, room: { x: 0.5, y: 0.5, u: 0.0098 }, tall: true, band: { x: 80, y: 512, s: 1 }, scrim: { solid: 540, to: 610 } },
    '34': { head: HEAD34, top: HEAD34, sub: { size: 42, gap: 28, min: 0 }, topSub: 42, cap: { cx: 540, cy: 1372, size: 40 }, card: { x: 60, y: 960, s: 0.72 }, end: { y: 70, close: 50, w: 936 }, room: { x: 0.5, y: 0.46, u: 0.0098 }, tall: true, band: { x: 72, y: 372, s: 1 }, scrim: { solid: 380, to: 470 } },
  }[LAY];
  F.talk = { '169': { x: 1000, y: 165, s: 1.4 }, '916': { x: 190, y: 585, s: 1.4 }, '34': { x: 190, y: 345, s: 1.4 } }[LAY];
  EP.F = F;

  // Camera presets: the phone's centre in page pixels, its scale in pixels per dp, and its turn in degrees.
  const CAMS = {
    '169': {
      off: { x: 1300, y: 1700, s: 1.08, r: 0 }, right: { x: 1300, y: 548, s: 1.08, r: 0 }, hero: { x: 1310, y: 520, s: 1.22, r: 0 },
      tl: { x: 1515, y: 1047, s: 1.79, r: 0 }, tr: { x: 1160, y: 1047, s: 1.79, r: 0 }, br: { x: 1200, y: 40, s: 1.79, r: 0 }, bl: { x: 1515, y: 40, s: 1.79, r: 0 },
      top: { x: 1483, y: 942, s: 1.68, r: 0 }, media: { x: 1320, y: 410, s: 1.45, r: 0 }, settings: { x: 1300, y: 700, s: 1.55, r: 0 },
      land: { x: 1040, y: 615, s: 1.55, r: -90 }, landRight: { x: 1330, y: 560, s: 1.0, r: -90 }, end: { x: 1350, y: 545, s: 0.845, r: 0 }, wide: { x: 1300, y: 548, s: 1.0, r: 0 },
    },
    '916': {
      off: { x: 540, y: 2500, s: 1.15, r: 0 }, right: { x: 540, y: 1085, s: 1.1, r: 0 }, hero: { x: 540, y: 1085, s: 1.1, r: 0 },
      tl: { x: 820, y: 1430, s: 2.0, r: 0 }, tr: { x: 260, y: 1430, s: 2.0, r: 0 }, br: { x: 300, y: 740, s: 2.0, r: 0 }, bl: { x: 780, y: 740, s: 2.0, r: 0 },
      top: { x: 540, y: 1500, s: 1.7, r: 0 }, media: { x: 540, y: 1000, s: 1.6, r: 0 }, settings: { x: 540, y: 1085, s: 1.1, r: 0 },
      land: { x: 540, y: 1150, s: 1.18, r: -90 }, landRight: { x: 540, y: 1200, s: 1.1, r: -90 }, end: { x: 540, y: 1240, s: 0.76, r: 0 }, wide: { x: 540, y: 1085, s: 1.1, r: 0 },
    },
    '34': {
      off: { x: 540, y: 1900, s: 0.9, r: 0 }, right: { x: 540, y: 880, s: 0.9, r: 0 }, hero: { x: 540, y: 885, s: 0.92, r: 0 },
      tl: { x: 511, y: 1081, s: 1.5, r: 0 }, tr: { x: 570, y: 1081, s: 1.5, r: 0 }, br: { x: 570, y: 479, s: 1.5, r: 0 }, bl: { x: 511, y: 479, s: 1.5, r: 0 },
      top: { x: 540, y: 1031, s: 1.5, r: 0 }, media: { x: 540, y: 573, s: 1.3, r: 0 }, settings: { x: 540, y: 885, s: 0.92, r: 0 },
      land: { x: 540, y: 900, s: 0.98, r: -90 }, landRight: { x: 540, y: 900, s: 0.9, r: -90 }, end: { x: 540, y: 1040, s: 0.62, r: 0 }, wide: { x: 540, y: 885, s: 0.92, r: 0 },
    },
  };
  const CAM = CAMS[LAY];
  EP.CAM = CAM;

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
  const D = {
    cam: new Track(CUT.startCam ? CAM[CUT.startCam] : CAM.off),
    turn: new Track(0),
    theme: new Track(0),
    bg: new Track(0),
    vol: new Track(31),
    bri: new Track(55),
    zoom: new Track(0),
    split: new Track(0),
    extra: new Track(0),
    hint: new Track(0),
    room: new Track(CUT.still ? 1 : 0), // the hook's dark room: 0 far across it, 1 gone
    mediaA: new Track(1),
    mediaY: new Track(0.78),
    screens: [{ t: -1, key: 'surface', land: false, o: {}, anchors: null }],
    armed: [],
    modes: [[-1, 'normal']],
    extraKinds: [[-1, undefined]], // the kinds in the four halves a split adds: [DEVICES, TABS, SIDE, DESK] ...
    scrubOn: [[-1, true]], // whether a scrub dial is on the pad (now playing then shows no time)
    presses: [],
    fingers: [],
    cards: [],
    media: [[-1, { app: 'YouTube', title: 'Mountain Lake Timelapse', dur: 612, pos: 298, playing: true }]],
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
    screen(t, key, o = {}, anchors = null, land = false) {
      this.screens.push({ t, key, o, anchors: forward(anchors || [[t, 0]]), land });
      this.screens.sort((a, b) => a.t - b.t);
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
  const CORNER = { VOL: 0, BRI: 1, ZOOM: 2, SCRUB: 3 };
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
    S.hint = D.hint.at(t);
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
    const a = ease(seg(t, cur.t, cur.t + 0.35));
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
      let o = s.o;
      if (s.key === 'controls') {
        // The popup is drawn over the surface as it stands; its own Controls button press is the popup's, not the film's.
        const under = surfaceState(t, pal, land);
        delete under.press;
        o = { ...o, under };
      }
      EP.screens[s.key](ctx, w, h, pal, EP.warp(t, s.anchors), o);
      if (!land) EP.statusBar(ctx, w, pal);
    }
    ctx.restore();
  }
  function drawPhone(ctx, t) {
    const cam = D.cam.at(t);
    const turn = D.turn.at(t);
    const pal = EP.palette(D.theme.at(t));
    ctx.save();
    ctx.translate(cam.x, cam.y);
    ctx.scale(cam.s, cam.s);
    ctx.rotate((cam.r * Math.PI) / 180);
    ctx.translate(-207, -434);
    // Shadow under the body: black, deep and long, for a ground this dark.
    ctx.save();
    ctx.shadowColor = 'rgba(0,0,0,0.6)';
    ctx.shadowBlur = 70 * cam.s;
    ctx.shadowOffsetY = 30 * cam.s;
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
    ctx.restore();
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

  // ---- the hook: a dark room, a laptop glowing on a TV stand far across it, and one move in to the phone ----
  // k 0 is the far view; as it runs to 1 the view closes in on the laptop and drifts up and away while the phone rises.
  function room(ctx, t) {
    const k = D.room.at(t);
    const dark = 1 - ease(seg(k, 0.2, 0.9));
    if (dark > 0.003) {
      ctx.fillStyle = `rgba(6,7,10,${0.5 * dark})`;
      ctx.fillRect(0, 0, W, H);
    }
    const a = 1 - ease(seg(k, 0.5, 1));
    if (a <= 0.003) return;
    const e = EP.easeInOut(k);
    const u = W * F.room.u;
    const z = lerp(1, 3.6, e);
    ctx.save();
    ctx.globalAlpha *= a;
    ctx.translate(W * F.room.x, H * F.room.y - e * H * 0.42);
    ctx.scale(z, z);
    // The wall washed by the screen.
    let g = ctx.createRadialGradient(0, -4 * u, 0, 0, -4 * u, 26 * u);
    g.addColorStop(0, 'rgba(77,163,255,0.24)');
    g.addColorStop(1, 'rgba(77,163,255,0)');
    ctx.fillStyle = g;
    ctx.fillRect(-30 * u, -30 * u, 60 * u, 60 * u);
    // The floor, and the glow it gives back.
    g = ctx.createLinearGradient(0, 4 * u, 0, 12 * u);
    g.addColorStop(0, 'rgba(255,255,255,0)');
    g.addColorStop(1, 'rgba(255,255,255,0.045)');
    ctx.fillStyle = g;
    ctx.fillRect(-3 * W, 4 * u, 6 * W, 4 * H);
    ctx.save();
    ctx.translate(0, 9.6 * u);
    ctx.scale(1, 0.16);
    g = ctx.createRadialGradient(0, 0, 0, 0, 0, 18 * u);
    g.addColorStop(0, 'rgba(77,163,255,0.26)');
    g.addColorStop(1, 'rgba(77,163,255,0)');
    ctx.fillStyle = g;
    ctx.beginPath();
    ctx.arc(0, 0, 18 * u, 0, Math.PI * 2);
    ctx.fill();
    ctx.restore();
    // The TV stand: a slab, a low cabinet, two feet.
    fillRR(ctx, -17 * u, 0, 34 * u, 1.1 * u, 0.4 * u, '#30333B');
    ctx.fillStyle = 'rgba(255,255,255,0.12)';
    ctx.fillRect(-17 * u, 0, 34 * u, 0.12 * u);
    fillRR(ctx, -16 * u, 1.1 * u, 32 * u, 7 * u, 0.5 * u, '#23262C');
    ctx.fillStyle = '#1A1C21';
    ctx.fillRect(-0.05 * u, 1.5 * u, 0.1 * u, 6.2 * u);
    ctx.fillRect(-14 * u, 8.1 * u, u, 0.9 * u);
    ctx.fillRect(13 * u, 8.1 * u, u, 0.9 * u);
    // The laptop: base, lid with its glow, and the lit screen with a window or two on it.
    fillRR(ctx, -7.4 * u, -0.55 * u, 14.8 * u, 0.55 * u, 0.2 * u, '#3D4049');
    ctx.save();
    ctx.shadowColor = 'rgba(77,163,255,0.7)';
    ctx.shadowBlur = 5 * u * z;
    fillRR(ctx, -6.5 * u, -8.5 * u, 13 * u, 8 * u, 0.5 * u, '#101216');
    ctx.restore();
    g = ctx.createLinearGradient(0, -8 * u, 0, -0.8 * u);
    g.addColorStop(0, '#86C0FF');
    g.addColorStop(1, '#2C62AD');
    rr(ctx, -6 * u, -8 * u, 12 * u, 7.2 * u, 0.3 * u);
    ctx.fillStyle = g;
    ctx.fill();
    ctx.fillStyle = 'rgba(255,255,255,0.35)';
    ctx.fillRect(-5.4 * u, -7.5 * u, 10.8 * u, 0.5 * u);
    fillRR(ctx, -5.4 * u, -6.6 * u, 6 * u, 4.4 * u, 0.2 * u, 'rgba(255,255,255,0.2)');
    fillRR(ctx, 1.1 * u, -6.6 * u, 4.3 * u, 2 * u, 0.2 * u, 'rgba(255,255,255,0.14)');
    g = ctx.createRadialGradient(0, -4.4 * u, 0, 0, -4.4 * u, 9 * u);
    g.addColorStop(0, 'rgba(255,255,255,0.14)');
    g.addColorStop(1, 'rgba(255,255,255,0)');
    ctx.fillStyle = g;
    ctx.fillRect(-9 * u, -13 * u, 18 * u, 18 * u);
    ctx.restore();
  }

  // ---- the frame ----
  const canvas = document.getElementById('film-canvas');
  canvas.width = W;
  canvas.height = H;
  const ctx = canvas.getContext('2d');
  function drawFrame(t) {
    ctx.save();
    ctx.fillStyle = GROUND;
    ctx.fillRect(0, 0, W, H);
    room(ctx, t);
    glow(ctx, t);
    drawPhone(ctx, t);
    if (F.scrim) {
      const g = ctx.createLinearGradient(0, 0, 0, F.scrim.to);
      g.addColorStop(0, GROUND);
      g.addColorStop(F.scrim.solid / F.scrim.to, GROUND);
      g.addColorStop(1, 'rgba(30,32,38,0)');
      ctx.fillStyle = g;
      ctx.fillRect(0, 0, W, F.scrim.to);
    }
    for (const c of D.cards) {
      const a = ease(seg(t, c.t0, c.t0 + 0.4)) * (1 - ease(seg(t, c.t1 - 0.35, c.t1)));
      if (a > 0) c.draw(ctx, t, a, c.pos || F.card);
    }
    ctx.restore();
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

  function buildText(tl) {
    let head = null; // { el, lines, layout }
    let subEnd = []; // subs to retire at the next line
    let endEl = null;
    BEATS.forEach((b, i) => {
      const line = LINES[i];
      if (!b || !line) return;
      const lay = b.lay === 'top' ? F.top : F.head;
      const subSize = b.lay === 'top' ? F.topSub : F.sub.size;
      if (b.end) {
        if (head) leave(tl, head.el, line.start - 0.05);
        for (const s of subEnd) leave(tl, s, line.start - 0.02);
        head = null;
        subEnd = [];
        endEl = buildEnd(tl, i);
        return;
      }
      if (b.close) return buildClose(tl, b, i, endEl);
      if (b.h !== undefined && b.h !== null) {
        if (head) leave(tl, head.el, line.start - 0.05);
        head = null;
        if (b.h) {
          const ws = parse(b.h);
          const lines = wrap(ws, 900, lay.size, lay.max, -0.01);
          const el = block('hl', lay.x, lay.y);
          el.style.fontSize = `${lay.size}px`;
          reveal(tl, words(el, lines), times(ws.filter((x) => x.w !== '|'), line, line.start));
          head = { el, n: lines.length, lay, size: lay.size };
        }
      }
      for (const s of subEnd) leave(tl, s, line.start - 0.02);
      subEnd = [];
      const subs = b.s === undefined || b.s === null ? [] : Array.isArray(b.s) ? b.s : [b.s];
      const top = head ? head.lay.y + head.n * head.size * 1.0 + (b.lay === 'top' ? 18 : F.sub.gap) : lay.y;
      const y = b.lay === 'top' ? top : Math.max(F.sub.min, top);
      subs.forEach((raw, k) => {
        const [s, anchor] = raw.split('@');
        const ws = parse(s).filter((x) => x.w !== '|');
        const el = block('sub', lay.x, y);
        el.style.fontSize = `${subSize}px`;
        const from = anchor ? L(i).w(anchor) : line.start;
        reveal(tl, words(el, wrap(ws, 700, subSize, lay.max, 0)), anchor ? ws.map((_, j) => from + j * 0.09) : times(ws, line, from));
        if (k + 1 < subs.length) {
          const nxt = subs[k + 1].split('@')[1];
          leave(tl, el, L(i).w(nxt));
        } else subEnd.push(el);
      });
    });
    buildCaptions(tl);
  }

  // The end card: the mark, "Edgepad", "Free & open source." and the GitHub button, each on its spoken word.
  function buildEnd(tl, i) {
    const l = L(i);
    const el = block('end', F.head.x, F.end.y);
    el.style.width = `${F.end.w}px`;
    el.innerHTML = `<img class="end-mark" src="${BASE}brand/edgepad-light.svg" alt=""><div class="end-name"><span class="w">Edgepad</span></div><div class="end-tag"><span class="w">Free</span> <span class="w">&amp;</span> <span class="w k">open</span> <span class="w k">source.</span></div><div class="end-btn"><svg viewBox="0 0 24 24" aria-hidden="true"><path fill="#0B1A2E" d="${window.EP_BRANDS.github}"/></svg><span>github.com/akshit-bansal11/edgepad</span></div>`;
    const [mark, name, tag, btn] = el.children;
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
