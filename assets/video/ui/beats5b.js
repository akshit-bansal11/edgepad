// The 5.x film's beats, second half: demo5 lines 13-24 (Move things, For developers, Small things, Straight talk, Next, Get it)
// and reel5 lines 7-14, keyed by their 1-based number in STORYBOARD.md (beats5.js lists them). Loads after beats5a.js, which creates
// EP.acts5. Every act sets, from its own start, everything it needs (the camera, the phone's turn and screen, the laptop's pose and
// screen) and never leans on what an earlier act left. Each act is one function of word times, so a reel line reuses the demo's
// act at its own times. Everything time-varying is a function of film time t, from EP.ease / EP.seg / EP.lerp.
(function () {
  const { ease, seg, lerp, clamp, fillRR, rr, text, measure } = EP;
  const { PILL, INK, BLUE } = EP.TOK;
  const { demo, reel } = EP.acts5;
  const ramp = (t, a, b) => ease(seg(t, a, b));

  // ---- where the phone, the laptop and the cards stand, per layout (read when an act runs, once EP.F is set) ----
  // 16:9 keeps the phone on the right and the laptop back left; the tall cuts stack them (laptop above, phone below the headline).
  const lay = () => (!EP.F.tall ? '169' : EP.F.cap.cy > 1500 ? '916' : '34');
  const ASIDE = { '169': { x: 1350, y: 910, s: 0.5, r: 0 }, '916': { x: 908, y: 1415, s: 0.46, r: 0 }, '34': { x: 908, y: 1118, s: 0.48, r: 0 } }; // stepped aside
  const ASIDE_FAR = { x: 1720, y: 780, s: 0.5, r: 0 }; // 16:9 with more than four cards: right of them
  const SIDE = { x: 1560, y: 700, s: 0.8, r: 0 }; // 16:9 only: small at the right of a centred laptop
  // 16:9 only: the phone's foot pushed in so the transfer card (centre 3, 370 dp from the phone's centre) sits mid-right at 1.7 px per dp.
  const FOOT169 = { x: 1435, y: 71, s: 1.7, r: 0 };
  // Where the plain cards sit: `subs` is a beat with sub-lines (the demo), whose cards start 80 px lower in the tall cuts; `many` is more
  // than four rows (16:9 then sets them at scale 1.0 from y 165).
  const CARDS = (subs, many) => ({
    '169': many ? { ...EP.F.talk, s: 1.0 } : EP.F.talk,
    '916': { x: 72, y: subs ? 1260 : 1175, s: 0.84 },
    '34': { x: 72, y: subs ? 970 : 890, s: 0.78 },
  })[lay()];
  // The phone's camera: `name` in 16:9, the film.js lap* preset `tall` in the tall cuts.
  const phoneAt = (name, tall = 'lapPhone') => (EP.F.tall ? tall : name);
  const foot = () => (EP.F.tall ? 'lapFoot' : FOOT169);
  // 'rest' is too low for the caption pill with a headline above it, so 16:9 takes a slightly smaller, higher pose.
  const LAP = () => (EP.F.tall ? EP.LAPS.rest : { ...EP.LAPS.rest, x: 600, y: 708, w: 580 });
  const LAP_CENTRE = () => (EP.F.tall ? EP.LAPS.rest : { ...EP.LAPS.rest, x: 960, y: 620, w: 780 });

  // An act's opening state: the phone upright at `cam`, the laptop at `lap`, both arriving as the line begins.
  const stage = (L, D, cam, lap = LAP(), dur = 0.9) => {
    const t = L.t0 - 0.3;
    D.move(t, cam, dur);
    D.turn.to(t, t + 0.6, 0);
    D.lap(t, lap, dur);
  };
  // A camera move that ends on film time `end`, starting no earlier than `from` and lasting at least 0.52 s.
  const camTo = (D, end, cam, from) => {
    const t = Math.max(end - 0.9, from);
    D.move(t, cam, Math.max(0.52, end - t));
  };
  // Screen anchors that hold the screen's own clock at 0 until `lt` seconds before `at`, then run it in step: its `lt` lands on `at`.
  const lead = (s, at, lt) => [[s, 0], [Math.max(s + 0.02, at - lt), 0], [at, lt]];
  // The next line's end, so an act can carry a mark across two lines.
  const nextEnd = (L) => window.FILM.timing.lines[L.i + 1].end;

  // ---- Move things: clipboard (ClipboardSheet.kt) ----
  const JUST_NOW = 'Just now'; // strings.xml clip_history_just_now
  const NOTE_LINE = '- dials on every corner';
  const NOTE_TEXT = `Meeting notes\n${NOTE_LINE}`;
  const ROWS = [
    { text: NOTE_LINE, src: 'laptop', age: JUST_NOW },
    { text: 'Meeting notes', src: 'phone', age: JUST_NOW },
    { text: 'Q3 roadmap', src: 'laptop', age: JUST_NOW },
    { text: 'acme/website', src: 'phone', age: JUST_NOW },
  ];
  // The text chip M-FLY carries: the copied line in a pill, centred on the origin.
  const chip = (label) => (ctx) => {
    const w = measure(ctx, label, 24, 'Bold') + 44;
    fillRR(ctx, -w / 2, -26, w, 52, 26, PILL);
    rr(ctx, -w / 2, -26, w, 52, 26);
    ctx.lineWidth = 1.5;
    ctx.strokeStyle = BLUE;
    ctx.stroke();
    text(ctx, label, 0, 8, { size: 24, w: 'Bold', color: INK, align: 'center' });
  };
  // A line is selected and copied in the note window (laptop 'notes'), a chip flies to the phone, which shows it, and one flies back.
  // T: start (the phone's surface), sel (the line is selected), fly (laptop to phone, lands +0.4), back (phone to laptop), hist (the
  // sheet opens on four rows), and for the full act dlg1 / dlg1End (the sheet with the one arrived row, open until dlg1End).
  // Returns the note window's `o`, a function of t, for the caller to hand to D.lapScreen.
  function clipAct(D, T) {
    D.fly(T.fly, 'laptop', 'phone', chip(NOTE_LINE));
    D.fly(T.back, 'phone', 'laptop', chip(NOTE_LINE));
    D.screen(T.start, 'surface');
    if (T.dlg1 !== undefined) {
      D.screen(T.dlg1, 'clipboard', (t) => ({ open: ramp(t, T.dlg1, T.dlg1 + 0.3), rows: ROWS.slice(0, 1) }), [[T.dlg1, 0]]);
      D.screen(T.dlg1End, 'surface');
    }
    D.screen(T.hist, 'clipboard', (t) => ({ open: ramp(t, T.hist, T.hist + 0.3), rows: ROWS }), [[T.hist, 0]]);
    return (t) => ({
      text: NOTE_TEXT,
      sel: ramp(t, T.sel, T.sel + 0.3),
      copied: ramp(t, T.sel + 0.35, T.sel + 0.6) * (1 - ramp(t, T.fly, T.fly + 0.3)),
      pasted: ramp(t, T.back + 0.4, T.back + 0.7),
    });
  }

  // ---- Move things: sending files (XferCard.kt, TransferToast.cs) ----
  const NAMES = ['trip-photos.zip', 'slides.pdf', 'demo.mp4'];
  const PAUSED_AT = 58; // percent the second file has reached when Pause is pressed
  // The transfer at t. T: a (the first file starts), one (it is done, the second starts), watch (the second is at 41%), pause (Pause is
  // pressed and the bar holds), resume, f2 (the second is done, the third starts), f3 (the third is done, then "Sent 3 files").
  function xferAt(T, t) {
    const paused = ramp(t, T.pause, T.pause + 0.12) * (1 - ramp(t, T.resume, T.resume + 0.12));
    const done = ramp(t, T.f3, T.f3 + 0.3);
    if (t < T.one) return { index: 1, pct: 100 * ramp(t, T.a, T.one - 0.05), paused, done };
    if (t < T.f2) {
      const pct = t < T.watch ? 41 * ramp(t, T.one, T.watch) : t < T.pause ? lerp(41, PAUSED_AT, seg(t, T.watch, T.pause)) : t < T.resume ? PAUSED_AT : lerp(PAUSED_AT, 100, ramp(t, T.resume, T.f2));
      return { index: 2, pct, paused, done };
    }
    return { index: 3, pct: 100 * ramp(t, T.f2, T.f3), paused, done };
  }
  // The phone's card (T.s is when its screen starts; the screen's own clock then runs 1:1, so a tap is T.pause - T.s).
  const xferAct = (D, T) => D.screen(T.s, 'xfer', (t) => {
    const x = xferAt(T, t);
    return { pick: ramp(t, T.a - 0.2, T.a), index: x.index, count: 3, name: NAMES[x.index - 1], pct: x.pct, paused: x.paused, done: x.done, pressPause: T.pause - T.s, pressResume: T.resume - T.s };
  }, [[T.s, 0]]);
  // The laptop's toast, mirroring the same numbers (percent only).
  const toastAt = (T) => (t) => {
    const x = xferAt(T, t);
    return { a: ramp(t, T.a, T.a + 0.3), name: NAMES[x.index - 1], index: x.index, count: 3, pct: x.pct, paused: x.paused > 0.5 ? 1 : 0, done: x.done };
  };
  // The whole transfer laid out inside [a, b] by proportion (the reel).
  const xferSpan = (s, a, b) => {
    const d = Math.max(1.8, b - a);
    return { s, a, one: a + 0.17 * d, watch: a + 0.4 * d, pause: a + 0.46 * d, resume: a + 0.62 * d, f2: a + 0.74 * d, f3: a + 0.88 * d };
  };

  // ---- Move things: macros (MacroScreen.kt) and a drawn shape ----
  // The deck (screens.js macros5: tap at lt 0.95, the slide to page two at 2.0), back to the surface, a lightning bolt drawn on the pad
  // (the finger rests until the line turns solid, then strokes it, as A.macros does), and on `runs` the laptop's terminal opens.
  // T: s (deck appears), apps (the tap), web (the slide), surf (surface again), draw (finger down), end (stroke ends), runs.
  function macrosAct(D, T) {
    D.screen(T.s, 'macros5', {}, [[T.s, 0.2], [T.apps, 0.95], [T.web, 2.0], [T.web + 0.55, 2.55]]);
    D.screen(T.surf, 'surface');
    if (EP.F.tall) camTo(D, T.draw + 0.2, 'lapPhone', T.web + 0.6); // the bolt is drawn on the pad: the whole phone
    const t0 = T.draw + 0.45, d = Math.max(0.7, T.end - t0);
    D.touch([[T.draw, 225, 320], [t0, 225, 320], [t0 + 0.4 * d, 150, 445], [t0 + 0.6 * d, 220, 455], [t0 + d, 165, 580]]);
    D.shaping.push([t0, t0 + d + 0.1]);
    D.lapScreen(T.s, 'desktop', (t) => ({ terminal: ramp(t, T.runs, T.runs + 0.4) }));
  }

  // ---- For developers (Settings > Developer, DeveloperHub.kt) ----
  const GIT_TAP = 1.2; // the lt at which the Git page's first row is tapped (the finger comes in 0.4 s before)
  // The developer switch, then the hub. T: s (the switch page starts), tap (its tap lands; omit to skip the page), hub (the hub starts;
  // default s), fill (the rings start to fill; omit for full), sess (the first session turns to "Waiting for you"; omit for done),
  // swipe (the pager moves to Git; omit to stay on AI), press (the Git row is tapped; omit for none).
  function devAct(D, T) {
    if (T.tap !== undefined) D.screen(T.s, 'devSwitch', (t) => ({ on: ramp(t, T.tap + 0.05, T.tap + 0.3) }), lead(T.s, T.tap, 0.9));
    const s = T.hub ?? T.s;
    D.screen(s, 'devHub', (t) => ({
      page: T.swipe === undefined ? 0 : ramp(t, T.swipe, T.swipe + 0.45),
      ai: { fill: T.fill === undefined ? 1 : ramp(t, T.fill, T.fill + 1.1), session: T.sess === undefined ? 1 : ramp(t, T.sess, T.sess + 0.5) },
      git: T.press === undefined ? {} : { press: GIT_TAP },
    }), T.press === undefined ? [[s, 0]] : lead(s, T.press, GIT_TAP));
  }

  // ---- Small things: the Air mouse (ControlSurface.kt air mode, M-TILT) ----
  // Where the file and the folder sit on the laptop's desktop, as fractions of its 1280 x 800 screen (laptop.js FILE_AT, FOLDER_AT).
  const AIR_FILE = { x: 790 / 1280, y: 250 / 800 };
  const AIR_FOLDER = { x: 600 / 1280, y: 250 / 800 };
  // D.tilt is {yaw, pitch} in degrees (a plain number is a yaw): yaw is the phone turning about its vertical axis, pitch its nose up.
  const ang = (v) => (typeof v === 'number' ? { yaw: v, pitch: 0 } : v);
  // The phone is aimed like a hand aims it: from `tilt`, four uneven eased moves in yaw (+-12) and pitch (+-8) that settle level on the
  // file by `hold`; the pointer on the laptop follows both axes with a 120 ms lag (yaw across, pitch up and down). On `hold` Left is
  // held, a yaw of -12 carries the file to the folder, and Left is released. T: s (screen starts), tilt, hold, end (the line's end).
  function airAct(D, T) {
    const { s, tilt, hold, end } = T;
    const span = Math.max(0.9, hold - 0.05 - tilt);
    for (const [a, b, yaw, pitch] of [[0, 0.3, 8, -5], [0.3, 0.62, -12, 5], [0.62, 0.82, 4, 8], [0.82, 1, 0, 0]]) D.tilt.to(tilt + a * span, tilt + b * span, { yaw, pitch });
    const d0 = hold + 0.25, d1 = Math.min(hold + 1.05, end - 0.55), rel = d1 + 0.1;
    D.tilt.to(d0, d1, { yaw: -12, pitch: 0 });
    D.tilt.to(d1 + 0.15, d1 + 0.5, { yaw: 0, pitch: 0 });
    const held = (t) => ramp(t, hold, hold + 0.16) * (1 - ramp(t, rel, rel + 0.16));
    D.screen(s, 'air', (t) => ({ on: ramp(t, s + 0.1, s + 0.5), left: held(t) }));
    D.lapScreen(s, 'desktop', (t) => {
      const q = ang(D.tilt.at(t - 0.12)), home = ang(D.tilt.at(hold - 0.12)), reach = AIR_FILE.x - AIR_FOLDER.x;
      const dyaw = q.yaw - home.yaw, dpitch = q.pitch - home.pitch;
      // Before Left is held the pointer sways about the file and settles on it; after, the lagged tilt carries it to the folder.
      const sway = { x: AIR_FILE.x + (q.yaw / 12) * reach, y: AIR_FILE.y - (q.pitch / 8) * 0.1 };
      const x = t < hold ? lerp(sway.x, AIR_FILE.x, ramp(t, hold - 0.4, hold - 0.05)) : AIR_FILE.x + (dyaw / 12) * reach;
      const y = t < hold ? lerp(sway.y, AIR_FILE.y, ramp(t, hold - 0.4, hold - 0.05)) : AIR_FILE.y - (dpitch / 8) * 0.1;
      const k = t < hold ? 0 : t > d1 + 0.12 ? 1 : clamp(-dyaw / 12);
      return { pointer: { x, y }, drag: { k, held: held(t) } };
    });
  }

  // ---- Straight talk: plain dark cards and the line from the phone to the laptop ----
  const cards = (items) => EP.cards.rows(items, null, true);
  // The phone steps aside, the laptop stays up at its pose, the cards sit at CARDS(). `line` (t0, t1) draws the phone-laptop line.
  // 16:9 takes a smaller laptop here, so the two-line sub of line 21 clears it by 32 px.
  const LAP_TALK = () => (EP.F.tall ? LAP() : { ...EP.LAPS.rest, x: 600, y: 735, w: 520 });
  function talkAct(L, D, items, line, subs = false) {
    const many = items.length > 4, p = many && !EP.F.tall ? ASIDE_FAR : ASIDE[lay()];
    stage(L, D, p, LAP_TALK());
    D.card(L.t0, L.next - 0.05, cards(items), CARDS(subs, many));
    if (!line) return;
    if (!EP.F.tall) return D.line(line[0], line[1]);
    // Tall cuts: a near-vertical line down the right of the headline, from under the laptop's deck to the top of the phone.
    const r = EP.LAPS.rest, h = r.w / 1.6;
    D.line(line[0], line[1], { x: r.x + r.w / 2 + 20, y: r.y + h * 0.59 + 8 }, { x: p.x, y: p.y - 434 * p.s });
  }
  // ---- Edgepad 6: two concept pictures, not screens of it ----
  const { GREY } = EP.TOK;
  const LAND169 = { x: 1340, y: 790, s: 0.85, r: -90 }; // 16:9: the sideways phone at the right, 72 px clear of the page edge
  // The far scene's geometry per layout, in page px: the small laptop's pose, the phone's camera, the house round the laptop, and the
  // arc from the laptop to the phone's top (control point between). The tall cuts keep it right of the headline's text.
  const far = () => {
    const l = lay();
    if (l === '169') return { lap: { x: 300, y: 700, w: 300 }, phone: { x: 1640, y: 700, s: 0.66, r: 0 }, house: { x0: 100, x1: 500, eaves: 575, apex: 510, base: 850 }, to: 1480, arc: [[470, 640], [985, 360], [1503, 600]] };
    // The tall cuts keep the whole picture inside the laptop band above the headline: a small outline of the phone stands for the far
    // phone at the band's right, and the real phone (lapPhone) below shows the finger.
    if (l === '916') return { lap: { x: 270, y: 520, w: 360 }, phone: 'lapPhone', house: { x0: 72, x1: 468, eaves: 380, apex: 300, base: 720 }, to: 1008, tick: 820, outline: { x: 820, y: 450, w: 130, h: 270 }, arc: [[460, 560], [672, 320], [885, 450]] };
    return { lap: { x: 250, y: 330, w: 340 }, phone: 'lapPhone', house: { x0: 72, x1: 428, eaves: 190, apex: 120, base: 520 }, to: 1008, tick: 770, outline: { x: 770, y: 310, w: 100, h: 210 }, arc: [[420, 330], [645, 220], [820, 310]] };
  };
  // The distance made visible (a D.card painter in page px): a line-drawn house round the far laptop and none round the phone, a dashed
  // ground with tick marks between them, and a long arc drawn on from `r` with a pulse travelling along it.
  const distance = (g, r) => (ctx, t, a) => {
    ctx.save();
    ctx.globalAlpha *= a;
    ctx.lineCap = ctx.lineJoin = 'round';
    const h = g.house, mid = (h.x0 + h.x1) / 2;
    ctx.strokeStyle = GREY;
    ctx.lineWidth = 5;
    ctx.beginPath();
    ctx.moveTo(h.x0, h.base);
    ctx.lineTo(h.x0, h.eaves);
    ctx.lineTo(mid, h.apex);
    ctx.lineTo(h.x1, h.eaves);
    ctx.lineTo(h.x1, h.base);
    ctx.stroke();
    ctx.lineWidth = 3;
    ctx.setLineDash([14, 12]);
    ctx.beginPath();
    ctx.moveTo(h.x0, h.base);
    ctx.lineTo(g.to, h.base);
    ctx.stroke();
    ctx.setLineDash([]);
    if (g.outline) {
      const o = g.outline;
      ctx.lineWidth = 4;
      rr(ctx, o.x, o.y, o.w, o.h, 16);
      ctx.stroke();
      ctx.beginPath();
      ctx.moveTo(o.x + o.w / 2 - 12, o.y + o.h - 14);
      ctx.lineTo(o.x + o.w / 2 + 12, o.y + o.h - 14);
      ctx.stroke();
      ctx.lineWidth = 3;
    }
    for (let i = 1; i <= 5; i++) {
      const x = lerp(h.x1, g.tick ?? g.to, i / 6);
      ctx.beginPath();
      ctx.moveTo(x, h.base - 10);
      ctx.lineTo(x, h.base + 10);
      ctx.stroke();
    }
    const [p0, c0, p1] = g.arc;
    const at = (u) => [(1 - u) * (1 - u) * p0[0] + 2 * (1 - u) * u * c0[0] + u * u * p1[0], (1 - u) * (1 - u) * p0[1] + 2 * (1 - u) * u * c0[1] + u * u * p1[1]];
    const grow = ramp(t, r + 0.1, r + 0.8);
    ctx.strokeStyle = BLUE;
    ctx.globalAlpha *= 0.7;
    ctx.lineWidth = 4;
    ctx.beginPath();
    for (let i = 0; i <= 40; i++) {
      const [x, y] = at((i / 40) * grow);
      if (i) ctx.lineTo(x, y);
      else ctx.moveTo(x, y);
    }
    ctx.stroke();
    if (grow >= 1) {
      const [x, y] = at((((t - r - 0.8) / 1.8) % 1 + 1) % 1);
      const g2 = ctx.createRadialGradient(x, y, 0, x, y, 26);
      g2.addColorStop(0, BLUE);
      g2.addColorStop(1, 'rgba(77,163,255,0)');
      ctx.globalAlpha = a;
      ctx.fillStyle = g2;
      ctx.beginPath();
      ctx.arc(x, y, 26, 0, Math.PI * 2);
      ctx.fill();
      ctx.fillStyle = INK;
      ctx.beginPath();
      ctx.arc(x, y, 5, 0, Math.PI * 2);
      ctx.fill();
    }
    ctx.restore();
  };
  // Edgepad 6, in two pictures that fill the line and its hold (it ends 0.3 s before the next line).
  // 1. Screen mirroring, from `m`: the phone turns sideways and shows the laptop's own picture, the same function of t handed to both
  //    (the pointer moves identically, a window opens), and a line links them. 2. Remote access, from `r` (never before `m` + 2.2 s): the
  //    phone turns upright and moves to one side, the laptop shrinks far on the other, a house is drawn round the laptop, a dashed ground
  //    with ticks and a long arc with a travelling pulse between them; the phone glides a finger and the far pointer follows it.
  function futureAct(L, D, m, r) {
    const end = L.next - 0.3, g = far(), land = EP.F.tall ? 'lapLand' : LAND169;
    const lead0 = m - 0.52 - (L.t0 - 0.3);
    if (lead0 >= 0.6) stage(L, D, phoneAt('hero'), LAP(), Math.min(0.9, lead0 - 0.05));
    else D.lap(L.t0 - 0.3, LAP(), 0.9);
    D.screen(L.t0 - 0.3, 'surface');
    const mirrored = (t) => {
      const tau = Math.max(0, t - m), k = ramp(t, m, m + 0.5);
      return { pointer: { x: 0.5 + 0.28 * k * Math.sin(1.7 * tau), y: 0.5 + 0.2 * k * Math.sin(1.1 * tau + 0.8) }, terminal: ramp(t, m + 0.6, m + 1.0) };
    };
    D.lapScreen(L.t0 - 0.3, 'desktop', mirrored);
    D.move(m - 0.52, land, 0.52);
    D.turn.to(m - 0.52, m - 0.12, 1);
    D.screen(m - 0.5, 'mirror', (t) => ({ kind: 'desktop', screen: mirrored(t), t, a: ramp(t, m - 0.1, m + 0.3) }), [[m - 0.5, 0]], true);
    if (EP.F.tall) {
      const rest = EP.LAPS.rest, c = EP.CAM.lapLand;
      D.line(m + 0.2, r - 0.1, { x: rest.x + rest.w / 2 + 20, y: rest.y + (rest.w / 1.6) * 0.59 + 8 }, { x: c.x + 434 * c.s - 30, y: c.y - 207 * c.s });
    } else D.line(m + 0.2, r - 0.1);
    D.turn.to(r - 0.5, r - 0.1, 0);
    D.move(r - 0.5, g.phone, 0.9);
    D.lap(r - 0.5, g.lap, 0.9);
    D.screen(r - 0.4, 'surface');
    const pads = [[120, 360], [270, 330], [190, 520], [280, 610], [140, 480]];
    const glides = [];
    for (let t0 = r + 0.3, i = 0; t0 + 0.8 < end; t0 += 1.0, i++) glides.push({ t0, t1: t0 + 0.8, a: pads[i % pads.length], b: pads[(i + 1) % pads.length] });
    for (const q of glides) D.touch([[q.t0, ...q.a], [q.t1, ...q.b]]);
    // The far pointer is the finger's place on the pad, 120 ms behind it.
    D.lapScreen(r - 0.2, 'desktop', (t) => {
      let p = pads[0];
      for (const q of glides) {
        if (t - 0.12 < q.t0) break;
        const k = ramp(t - 0.12, q.t0, q.t1);
        p = [lerp(q.a[0], q.b[0], k), lerp(q.a[1], q.b[1], k)];
      }
      return { pointer: { x: p[0] / 390, y: (p[1] - 120) / 560 } };
    });
    D.card(r - 0.2, end, distance(g, r), { x: 0, y: 0, s: 1 });
  }
  // The end card: the camera moves to 'end' and the laptop leaves.
  const endAct = (L, D) => {
    D.move(L.t0 - 0.4, 'end', 1.3);
    D.lap(L.t0 - 0.4, 'off', 0.6);
  };
  const CLOSE = 'Your laptop, from anywhere *in the room.*';

  // ================= demo5, lines 13-24 =================
  // 13: copy on the laptop, a chip flies to the phone, one flies back, then the history.
  demo[13] = {
    h: 'Copy here. *Paste* there.', s: ['Both *ways*@both', 'With a *history*@history'], lap: true,
    act: (L, D) => {
      stage(L, D, phoneAt('wide'));
      const sel = L.t0 + 0.2, fly = Math.max(L.w('paste') - 0.2, L.t0 + 1.0);
      const back = Math.max(L.w('both') - 0.1, fly + 1.2);
      const hist = Math.max(L.w('history') - 0.2, back + 0.9);
      D.lapScreen(L.t0 - 0.3, 'notes', clipAct(D, { start: L.t0 - 0.3, sel, fly, dlg1: fly + 0.25, dlg1End: back - 0.45, back, hist }));
    },
  };
  // 14: three files, file 1 done by "two", 2 of 3 at 41% around "watch", Pause on "pause", Resume, 100% and "Sent 3 files".
  demo[14] = {
    h: 'Send *files.*', s: ['Either *way*@either', 'Up to *2 GB* each@two', '*Pause* any time@pause'], lap: true,
    act: (L, D) => {
      // The camera pushes in on the phone's foot (the card) and the laptop grows for its toast, both landing by "either"; the next act pulls back.
      const settle = Math.min(1.4, Math.max(0.52, L.w('either') - (L.t0 - 0.3)));
      stage(L, D, foot(), EP.F.tall ? LAP() : { ...EP.LAPS.rest, x: 540, y: 630, w: 760 }, settle);
      const a = L.t0 - 0.05, one = Math.max(L.w('two'), a + 0.8), watch = Math.max(L.w('watch'), one + 0.8), pause = Math.max(L.w('pause'), watch + 0.3);
      const f3 = L.next - 0.4, f2 = f3 - 0.35, resume = Math.max(pause + 0.45, Math.min(pause + 0.7, f2 - 0.3));
      const T = { s: L.t0 - 0.3, a, one, watch, pause, resume, f2, f3 };
      xferAct(D, T);
      D.lapScreen(T.s, 'desktop', (t) => ({ toast: toastAt(T)(t) }));
    },
  };
  // 15: the deck, a bolt drawn on the pad, and the laptop's terminal opens on "runs".
  demo[15] = {
    h: '*Macros.*', s: ['Apps · folders · *websites*@macros', 'Draw a *shape*@draw', 'And it *runs*@runs'], lap: true,
    act: (L, D) => {
      stage(L, D, phoneAt('hero', 'lapHead'));
      const apps = L.w('apps'), web = Math.max(L.w('folders') + 0.05, apps + 0.5), surf = Math.max(L.w('draw') - 0.3, web + 0.6), runs = L.w('runs');
      macrosAct(D, { s: L.t0 - 0.3, apps, web, surf, draw: Math.max(L.w('draw'), surf + 0.1), end: runs - 0.1, runs });
    },
  };
  // 16: the developer switch turns on, the AI page fills from "limits" and its first session turns on "session"; the laptop shows code.
  demo[16] = {
    h: 'For *developers.*', s: ['*Limits* and spend@limits', 'Every open *session*@session'], lap: true,
    act: (L, D) => {
      stage(L, D, phoneAt('hero', 'lapHead'));
      const tap = L.w('developer');
      devAct(D, { s: L.t0 - 0.3, tap, hub: Math.max(L.w('your') - 0.15, tap + 0.5), fill: L.w('limits') - 0.1, sess: L.w('session') });
      D.lapScreen(L.t0 - 0.3, 'editor');
    },
  };
  // 17: the pager swipes to Git at the line's start, the first request is tapped on "tap" and the laptop's browser opens on "opens".
  demo[17] = {
    h: 'Your *pull requests.*', s: ['GitHub · GitLab · *and more*@more', 'Tap, and it *opens*@opens'], lap: true,
    act: (L, D) => {
      stage(L, D, phoneAt('hero', 'lapHead'));
      devAct(D, { s: L.t0 - 0.3, swipe: L.t0 - 0.1, press: L.w('tap') });
      const open = L.w('opens');
      D.lapScreen(L.t0 - 0.3, 'editor');
      D.lapScreen(open - 0.2, 'browser-pr', (t) => ({ open: ramp(t, open - 0.2, open + 0.4) }));
    },
  };
  // 18: the Air mouse, the phone tilting, the pointer following, Left held and a file dragged to a folder.
  demo[18] = {
    h: '*Air* mouse.', s: ['*Tilt* to aim@tilt', '*Hold* to drag@hold'], lap: true,
    act: (L, D) => {
      stage(L, D, phoneAt('hero', 'lapFoot'));
      airAct(D, { s: L.t0 - 0.3, tilt: L.w('tilt'), hold: L.w('hold'), end: L.next });
    },
  };
  // 19: a film's credits roll on a centred laptop, the countdown card on "sleep", then black; the phone's chip is on.
  demo[19] = {
    h: 'Sleep when it *ends.*@falling', lap: true, // the words of this headline are not spoken until the end of the line
    act: (L, D) => {
      const s = L.t0 - 0.3, sleep = L.w('sleep');
      stage(L, D, phoneAt(SIDE), LAP_CENTRE());
      D.roomFx.lamp.to(L.t0 - 0.2, L.t0 + 0.9, 0.6);
      D.screen(s, 'surface');
      D.sleep.to(L.t0, L.t0 + 0.5, 1);
      D.sleep.to(L.next - 0.4, L.next, 0);
      D.lapScreen(s, 'credits', (t) => ({
        roll: 5 + 2.4 * (t - s),
        countdown: { a: ramp(t, sleep - 0.1, sleep + 0.3), secs: 60 - Math.max(0, t - (sleep - 0.1)) },
        black: ramp(t, Math.max(sleep + 1.0, L.next - 1.4), L.next - 0.1),
      }));
    },
  };
  // 20: the phone steps aside, four plain cards, and a line drawn from the phone to the laptop on "straight" (it carries on through line 21).
  demo[20] = {
    h: 'No account. | No *server.*', s: ['*Straight* to your laptop@straight', 'Bluetooth · Wi-Fi · *USB*@bluetooth'], lap: true,
    act: (L, D) => {
      D.screen(L.t0 - 0.3, 'surface');
      D.lapScreen(L.t0 - 0.3, 'desktop');
      talkAct(L, D, [
        { name: 'Straight to your laptop', at: L.w('straight'), hot: true },
        { name: 'Bluetooth', at: L.w('bluetooth') },
        { name: 'Wi-Fi', at: L.w('wifi') },
        { name: 'USB', at: L.w('usb') },
      ], [L.w('straight') - 0.1, nextEnd(L) + 0.1], true);
    },
  };
  // 21: the headline stays; Windows tested, Mac and Linux newer, tell us on GitHub. The phone is still aside and the line still drawn.
  demo[21] = {
    h: null, s: ['Windows: tested on *real hardware*@tested', 'Mac and Linux: *newer*@newer', 'Tell us on *GitHub*@github'], lap: true,
    act: (L, D) => {
      D.screen(L.t0 - 0.3, 'surface');
      D.lapScreen(L.t0 - 0.3, 'desktop');
      talkAct(L, D, [
        { name: 'Windows', desc: 'Tested on real hardware', at: L.w('tested'), hot: true },
        { name: 'Mac', desc: 'Newer', at: L.w('mac'), descAt: L.w('newer') },
        { name: 'Linux', desc: 'Newer', at: L.w('linux'), descAt: L.w('newer') },
        { name: 'Tell us on GitHub', at: L.w('tell') },
      ], undefined, true);
    },
  };
  // 22: mirroring from "mirroring" (at least 2.2 s), then remote access from "remote" (or 2.2 s after mirroring starts) to the hold's end.
  demo[22] = {
    h: 'Coming in *Edgepad 6.*', s: ['Screen *mirroring*@mirroring', 'Remote *access*@remote'], lap: true,
    act: (L, D) => {
      const m = L.w('mirroring');
      futureAct(L, D, m, Math.max(L.w('remote'), m + 2.2));
    },
  };
  demo[23] = { end: true, act: endAct };
  demo[24] = { close: true, h: CLOSE, act: () => {} };

  // ================= reel5, lines 7-14 =================
  // 7: clipboard, files and macros each at half length inside one line, keyed to "clipboard", "file", "macros" and "shape".
  reel[7] = {
    h: 'Clipboard. Files. *Macros.*', lap: true,
    act: (L, D) => {
      stage(L, D, phoneAt('wide'));
      const s = L.t0 - 0.3, file = L.w('file'), both = L.w('both'), mac = L.w('macros'), shape = L.w('shape'), dr = L.w('draw');
      const notes = clipAct(D, { start: s, sel: L.t0 + 0.05, fly: L.t0 + 0.3, hist: L.t0 + 0.7, back: Math.max(both - 0.4, L.t0 + 1.4) });
      const T = xferSpan(file - 0.25, file - 0.1, mac - 0.4);
      xferAct(D, T);
      D.lapScreen(s, 'notes', (t) => ({ ...notes(t), toast: toastAt(T)(t) }));
      // The camera follows the phone's work: the foot for the card, the head for the deck, the whole phone for the shape.
      camTo(D, file, foot(), L.t0 + 0.5);
      camTo(D, mac, phoneAt('wide', 'lapHead'), T.f3 + 0.3);
      const web = mac + 0.4, surf = Math.max(shape - 0.45, web + 0.6);
      macrosAct(D, { s: mac - 0.3, apps: mac + 0.05, web, surf, draw: Math.max(shape - 0.2, surf + 0.1), end: dr + 0.2, runs: dr + 0.4 });
    },
  };
  // 8: the Air mouse, as demo 18.
  reel[8] = {
    h: '*Air* mouse.', lap: true,
    act: (L, D) => {
      stage(L, D, phoneAt('hero', 'lapFoot'));
      airAct(D, { s: L.t0 - 0.3, tilt: L.w('tilt'), hold: L.w('hold'), end: L.next });
    },
  };
  // 9: AI then Git in one line: the switch on "developers", the AI page filling on "limits", the pager swiping on "pull", the browser on "glance".
  reel[9] = {
    h: 'For *developers.*', lap: true,
    act: (L, D) => {
      stage(L, D, phoneAt('hero', 'lapHead'));
      const tap = L.w('developers'), swipe = L.w('pull') - 0.1;
      const press = Math.max(L.w('requests') + 0.1, swipe + 0.7), open = Math.max(L.w('glance') - 0.2, press + 0.5);
      devAct(D, { s: L.t0 - 0.3, tap, hub: Math.max(L.w('your') - 0.15, tap + 0.5), fill: L.w('limits') - 0.1, sess: L.w('sessions'), swipe, press });
      D.lapScreen(L.t0 - 0.3, 'editor');
      D.lapScreen(open - 0.2, 'browser-pr', (t) => ({ open: ramp(t, open - 0.2, open + 0.4) }));
    },
  };
  // 10: the platforms and the connections as plain cards, and the line from the phone to the laptop on "bluetooth".
  reel[10] = {
    h: 'No account. No *server.*@windows', lap: true, // spoken only at the line's end
    act: (L, D) => {
      D.screen(L.t0 - 0.3, 'surface');
      D.lapScreen(L.t0 - 0.3, 'desktop');
      talkAct(L, D, [
        { name: 'Windows', at: L.w('windows'), hot: true },
        { name: 'Mac', at: L.w('mac') },
        { name: 'Linux', at: L.w('linux') },
        { name: 'Bluetooth', at: L.w('bluetooth') },
        { name: 'Wi-Fi', at: L.w('wifi') },
        { name: 'USB', at: L.w('usb') },
      ], [L.w('bluetooth') - 0.1, nextEnd(L) + 0.1]);
    },
  };
  // 11: the headline stays; Mac and Linux newer, tell us on GitHub. The line from line 10 is still drawn.
  reel[11] = {
    h: null, lap: true,
    act: (L, D) => {
      D.screen(L.t0 - 0.3, 'surface');
      D.lapScreen(L.t0 - 0.3, 'desktop');
      talkAct(L, D, [
        { name: 'Mac', desc: 'Newer', at: L.w('mac'), descAt: L.w('newer') },
        { name: 'Linux', desc: 'Newer', at: L.w('linux'), descAt: L.w('newer') },
        { name: 'Tell us on GitHub', at: L.w('tell') },
      ]);
    },
  };
  // 12: as demo 22, mirroring from "screen" (held to at least 0.2 s into the line so the phone's turn starts within 0.4 s of it).
  // STORYBOARD.md gives this line no headline, so it takes demo 22's.
  reel[12] = {
    h: 'Coming in *Edgepad 6.*@screen', lap: true, // "coming" is only said mid-line
    act: (L, D) => {
      const m = Math.max(L.w('screen'), L.t0 + 0.2);
      futureAct(L, D, m, Math.max(L.w('remote'), m + 2.2));
    },
  };
  reel[13] = { end: true, act: endAct };
  reel[14] = { close: true, h: CLOSE, act: () => {} };
})();
