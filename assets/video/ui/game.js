// Night Lap (GAME.md): an original pseudo-3D night circuit painted on the laptop's screen; every frame is a pure function of time.
// API: EP.game.draw(ctx, w, h, tr, mode)  paints (0,0,w,h); mode 'race' (tr from the green light, tr < 0 waits on the grid) | 'attract'.
//      EP.game.input(tr) -> {lx, rt, lt, a, boost}  the phone's pad and the car share it; EP.game.ROWS / A_PRESS / EASE mirror its table.
//      EP.game.state(tr, mode) -> {speed 0..1 of max, pos, x, curve, lamp 0..1 flare, braking 0..1}; attract pos is the phase in the 40-segment lamp pattern.
//      EP.game.LOOP = 6: the attract loop in seconds (the car advances exactly 200 segments, five lamp spacings, so frame 0 == frame 6.0).
//      EP.game.check() -> true when the attract loop closes and speed stays in 0..1; the dev check, nothing calls it at render time.
// The race is integrated once at load at 240 Hz over 0..40 s and read by interpolation; past 40 s the last speed holds and the position runs on.
(function () {
  const { clamp, lerp, ease, easeInOut, seg, rgba, mixRgb } = EP;
  const TAU = Math.PI * 2;

  // Colours, GAME.md art direction (the film's own BLUE and INK come from EP.TOK).
  const GAME_ROAD = '#26282E';
  const GAME_ROAD_ALT = '#2A2C33';
  const GAME_KERB_A = '#F2F3F5';
  const GAME_KERB_B = '#3A3D45';
  const GAME_DASH = '#9A9CA3';
  const GAME_SEA = '#101826';
  const GAME_LAND_A = '#0F1319';
  const GAME_LAND_B = '#11161D';
  const GAME_FOG = '#121A28';
  const GAME_SKY_TOP = '#0B0C10';
  const GAME_SKY_HORIZON = '#1A1D27';
  const GAME_MOON = '#D8E4F5';
  const GAME_LAMP = '#FFD9A0';
  const GAME_POST = '#1C1E24';
  const GAME_BODY = '#3A3D45';
  const GAME_DECK = '#444853';
  const GAME_GLASS = '#14161B';
  const GAME_TYRE = '#0A0B0E';
  const GAME_DIFFUSER = '#202228';
  const GAME_TAIL = '#FF3B30';
  const GAME_BLUE = EP.TOK.BLUE;
  const GAME_INK = EP.TOK.INK;
  const GAME_VIGNETTE = [5, 6, 9];

  const RGB = (h) => [1, 3, 5].map((i) => parseInt(h.slice(i, i + 2), 16));
  const css = (c) => `rgb(${Math.round(c[0])},${Math.round(c[1])},${Math.round(c[2])})`;
  const BLUE = RGB(GAME_BLUE), INK = RGB(GAME_INK), WARM = RGB(GAME_LAMP), TAIL = RGB(GAME_TAIL), MOON = RGB(GAME_MOON);

  // ---- road geometry: 600 segments of 200 units, projected with a fixed camera height and depth ----
  const SEGS = 600, SEG_LEN = 200, DRAW = 220, ROAD = 1500, CAM_H = 1100, FOV = 100, FOV_BOOST = 0.08;
  const Z_CAR = 1950, ZNEAR = 260, CAM_FOLLOW = 0.85, ATT_CAM = 1.3, KERB = 0.14, SHOULDER = 1.55;
  const LAMP_EVERY = 40, LAMP_H = 1700, STRIPE = 4, KERB_LEN = 2, BOB_AMP = 14, CREST_H = 40 * 15;
  const wrapI = (i) => ((i % SEGS) + SEGS) % SEGS;
  // Linear read of a per-segment table at a fractional segment, wrapping round the lap.
  const tab = (arr, s) => {
    const i = Math.floor(s);
    const a = arr[wrapI(i)];
    return a + (arr[wrapI(i + 1)] - a) * (s - i);
  };
  const boxLap = (src, r) => {
    const out = new Float32Array(SEGS);
    for (let i = 0; i < SEGS; i++) {
      let s = 0;
      for (let j = -r; j <= r; j++) s += src[wrapI(i + j)];
      out[i] = s / (2 * r + 1);
    }
    return out;
  };

  // GAME.md track: straight 80, long right-hander (curve +2) 120, short straight 40, hairpin left (-5) 60, crest (+40 over 60), back to the start.
  const RACE = (() => {
    const raw = new Float32Array(SEGS);
    const elev = new Float32Array(SEGS);
    let i = 0;
    for (const [len, c] of [[80, 0], [120, 2], [40, 0], [60, -5]]) for (let k = 0; k < len; k++) raw[i++] = c;
    for (let k = 0; k < 60; k++) elev[i + k] = (CREST_H * (1 - Math.cos((TAU * k) / 60))) / 2;
    return { curve: boxLap(boxLap(raw, 6), 6), elev };
  })();
  // The attract road is one endless right-hander with rolling hills, so every pattern on it repeats within the 200-segment loop.
  const ATT_CURVE = 2, ATT_HILL = 140, ATT_HILL_PERIOD = 100;
  const ATTRACT = {
    curve: new Float32Array(SEGS).fill(ATT_CURVE),
    elev: Float32Array.from({ length: SEGS }, (_, i) => ATT_HILL * Math.sin((TAU * i) / ATT_HILL_PERIOD)),
  };
  // Heading along the lap (the running sum of the curve, its drift removed so the lap closes): the sky slides against it.
  const HEAD = new Float32Array(SEGS + 1);
  for (let i = 0; i < SEGS; i++) HEAD[i + 1] = HEAD[i] + RACE.curve[i];
  const HEAD_TOTAL = HEAD[SEGS];
  for (let i = 0; i <= SEGS; i++) HEAD[i] -= (HEAD_TOTAL * i) / SEGS;
  const headAt = (s) => {
    const w = ((s % SEGS) + SEGS) % SEGS;
    const i = Math.floor(w);
    return HEAD[i] + (HEAD[i + 1] - HEAD[i]) * (w - i);
  };

  // ---- the input track (GAME.md table): [t, LX, RT, LT]; A is pressed 4.4..5.6 and boosts for the same 1.2 s ----
  const ROWS = [
    [0.0, 0, 0, 0],
    [0.3, 0, 1.0, 0],
    [2.0, 0.45, 0.85, 0],
    [3.6, 0.2, 1.0, 0],
    [4.4, 0, 1.0, 0],
    [5.8, 0, 0, 0.9],
    [6.4, -0.95, 0.4, 0.2],
    [7.6, -0.3, 1.0, 0],
    [8.6, 0, 1.0, 0],
    [10.0, 0, 0.7, 0],
  ];
  const A_PRESS = [4.4, 5.6];
  const EASE_S = 0.2; // each row's value eases in over the 200 ms after its time
  // GAME.md: the phone's stick and triggers read this; the car reads it too.
  function input(tr) {
    let k = 0;
    for (let i = 1; i < ROWS.length; i++) {
      if (tr < ROWS[i][0]) break;
      k = i;
    }
    let lx = ROWS[k][1], rt = ROWS[k][2], lt = ROWS[k][3];
    if (k > 0) {
      const e = easeInOut(seg(tr, ROWS[k][0], ROWS[k][0] + EASE_S));
      const p = ROWS[k - 1];
      lx = lerp(p[1], lx, e);
      rt = lerp(p[2], rt, e);
      lt = lerp(p[3], lt, e);
    }
    const a = ease(seg(tr, A_PRESS[0], A_PRESS[0] + 0.06)) * (1 - ease(seg(tr, A_PRESS[1], A_PRESS[1] + 0.06)));
    const boost = ease(seg(tr, A_PRESS[0], A_PRESS[0] + 0.15)) * (1 - ease(seg(tr, A_PRESS[1], A_PRESS[1] + 0.3)));
    return { lx, rt, lt, a, boost };
  }

  // ---- physics, integrated once ----
  const DT = 1 / 240, T_END = 40, N = Math.round(T_END / DT) + 1;
  const VSEG = 37, START_SEG = 45, V_TOP = 1.2; // segments per second at v = 1; the grid; the boosted top speed
  const ACCEL = 0.9, BRAKE = 1.6, DRAG = 0.02, BOOST_ACCEL = 0.6;
  const XSTEER = 0.5, CENT = 0.2, X_MAX = 0.95, LAG_X = 0.25, LAG_POSE = 0.05;
  const YAW_DEG = 8, ROLL_DEG = 3, PITCH_THROTTLE = -1.5, PITCH_BRAKE = 2, FLARE_S = 0.08;
  const flareOf = (sec) => (sec >= FLARE_S ? 0 : 1 - sec / FLARE_S);
  const TBL = { v: new Float32Array(N), d: new Float32Array(N), x: new Float32Array(N), lx: new Float32Array(N), pit: new Float32Array(N), brk: new Float32Array(N), fl: new Float32Array(N) };
  (function integrate() {
    let v = 0, d = START_SEG, x = 0, lx = 0, pit = 0, brk = 0, last = -1;
    const kx = 1 - Math.exp(-DT / LAG_X), kp = 1 - Math.exp(-DT / LAG_POSE), kv = Math.exp(-2 * DT);
    for (let k = 0; k < N; k++) {
      const t = k * DT;
      TBL.v[k] = v; TBL.d[k] = d; TBL.x[k] = x; TBL.lx[k] = lx; TBL.pit[k] = pit; TBL.brk[k] = brk;
      TBL.fl[k] = last < 0 ? 0 : flareOf(t - last);
      const I = input(t);
      v += (ACCEL * I.rt - BRAKE * I.lt - DRAG * v + BOOST_ACCEL * I.boost) * DT;
      const cap = 0.55 + 0.45 * I.rt + 0.2 * I.boost;
      if (v > cap) v = cap + (v - cap) * kv;
      v = clamp(v, 0, V_TOP);
      const dn = d + v * VSEG * DT;
      if (Math.floor(dn / LAMP_EVERY) > Math.floor(d / LAMP_EVERY)) last = t + DT;
      d = dn;
      lx += (I.lx - lx) * kp;
      pit += (PITCH_THROTTLE * I.rt + PITCH_BRAKE * I.lt - pit) * kp;
      brk += (Math.min(1, I.lt * 2) - brk) * kp;
      x = clamp(x + (XSTEER * lx - CENT * tab(RACE.curve, d) * v * v - x) * kx, -X_MAX, X_MAX);
    }
  })();

  // One lookup, race or attract: speed, position and pose at tr. d is the unwrapped distance in segments.
  function raceAt(tr) {
    const t = Math.max(0, tr);
    const I = input(t);
    let v, d, x, lx, pit, brk, fl;
    if (t < T_END) {
      const f = t / DT;
      const i = Math.min(N - 2, Math.floor(f));
      const u = f - i;
      const rd = (a) => a[i] + (a[i + 1] - a[i]) * u;
      v = rd(TBL.v); d = rd(TBL.d); x = rd(TBL.x); lx = rd(TBL.lx); pit = rd(TBL.pit); brk = rd(TBL.brk); fl = rd(TBL.fl);
    } else {
      const e = N - 1;
      v = TBL.v[e]; x = TBL.x[e]; lx = TBL.lx[e]; pit = TBL.pit[e]; brk = TBL.brk[e];
      d = TBL.d[e] + v * VSEG * (t - T_END);
      const ds = ((d % LAMP_EVERY) + LAMP_EVERY) % LAMP_EVERY;
      fl = v > 1e-3 ? flareOf(ds / (v * VSEG)) : 0;
    }
    return { v, speed: clamp(v / V_TOP), d, pos: ((d % SEGS) + SEGS) % SEGS, x, curve: tab(RACE.curve, d), lamp: fl, braking: brk, lx, pit, boost: I.boost, rt: I.rt, lt: I.lt };
  }

  // Attract (GAME.md): a steady 70% throttle with LX +0.35 round the right-hander; 200 segments per 6 s, so the road, stripes, lamps and hills all repeat.
  const LOOP = 6, ATT_ADV = 200, ATT_START = 17, ATT_LX = 0.35, ATT_RT = 0.7;
  function attractAt(u) {
    const adv = ATT_ADV / LOOP;
    const d = ATT_START + adv * u;
    const v = adv / VSEG;
    const ds = ((d % LAMP_EVERY) + LAMP_EVERY) % LAMP_EVERY;
    return { v, speed: clamp(v / V_TOP), d, pos: ds, x: clamp(XSTEER * ATT_LX - CENT * ATT_CURVE * v * v, -X_MAX, X_MAX), curve: ATT_CURVE, lamp: flareOf(ds / adv), braking: 0, lx: ATT_LX, pit: PITCH_THROTTLE * ATT_RT, boost: 0, rt: ATT_RT, lt: 0 };
  }
  const sample = (tr, mode) => (mode === 'attract' ? attractAt(((tr % LOOP) + LOOP) % LOOP) : raceAt(tr));
  const state = (tr, mode) => {
    const S = sample(tr, mode);
    return { speed: S.speed, pos: S.pos, x: S.x, curve: S.curve, lamp: S.lamp, braking: S.braking };
  };

  // ---- precomputed look-up: fogged colours, stars ----
  const FOGN = 24;
  const fogSet = (hex) => Array.from({ length: FOGN }, (_, i) => css(mixRgb(RGB(hex), RGB(GAME_FOG), i / (FOGN - 1))));
  const FOGGED = { roadA: fogSet(GAME_ROAD), roadB: fogSet(GAME_ROAD_ALT), kerbA: fogSet(GAME_KERB_A), kerbB: fogSet(GAME_KERB_B), dash: fogSet(GAME_DASH), landA: fogSet(GAME_LAND_A), landB: fogSet(GAME_LAND_B), sea: fogSet(GAME_SEA) };
  const STARS = (() => {
    let s = 20261125;
    const rnd = () => {
      s = (Math.imul(s, 1664525) + 1013904223) >>> 0;
      return s / 4294967296;
    };
    return Array.from({ length: 90 }, () => ({ x: rnd(), y: Math.pow(rnd(), 1.3), r: 0.5 + rnd() * 1.1, a: 0.25 + rnd() * 0.65, k: 1 + Math.floor(rnd() * 4), ph: rnd() * TAU }));
  })();
  const LINES = Array.from({ length: 36 }, (_, i) => ({ th: 0.04 * Math.PI + ((i + 0.5) / 36) * 0.92 * Math.PI, ph: (i * 0.61803398875) % 1, wd: 0.6 + ((i * 7) % 5) / 5 }));

  // Per-frame scratch for the projected segments (rewritten every frame, nothing carries over).
  const SX0 = new Float64Array(DRAW), SY0 = new Float64Array(DRAW), SW0 = new Float64Array(DRAW), SX1 = new Float64Array(DRAW), SY1 = new Float64Array(DRAW), SW1 = new Float64Array(DRAW), Z0 = new Float64Array(DRAW), VIS = new Uint8Array(DRAW);

  const quad = (ctx, x0, y0, x1, y1, x2, y2, x3, y3, fill) => {
    ctx.beginPath();
    ctx.moveTo(x0, y0);
    ctx.lineTo(x1, y1);
    ctx.lineTo(x2, y2);
    ctx.lineTo(x3, y3);
    ctx.closePath();
    ctx.fillStyle = fill;
    ctx.fill();
  };

  // The sky: graded #0B0C10 -> #1A1D27, fixed-seed stars that slide against the heading, a moon, then the sea band and its horizon line.
  function sky(ctx, w, h, hz, sc, tr, off) {
    const g = ctx.createLinearGradient(0, 0, 0, hz);
    g.addColorStop(0, GAME_SKY_TOP);
    g.addColorStop(1, GAME_SKY_HORIZON);
    ctx.fillStyle = g;
    ctx.fillRect(0, 0, w, hz + 2);
    for (const s of STARS) {
      const x = (((s.x * w + off) % w) + w) % w;
      const tw = 0.8 + 0.2 * Math.sin((TAU * s.k * tr) / LOOP + s.ph);
      ctx.fillStyle = rgba(INK, s.a * tw);
      ctx.beginPath();
      ctx.arc(x, s.y * hz * 0.92, s.r * sc, 0, TAU);
      ctx.fill();
    }
    const mx = w * 0.72 + off * 0.3, my = hz * 0.34;
    const glow = ctx.createRadialGradient(mx, my, 0, mx, my, 70 * sc);
    glow.addColorStop(0, rgba(MOON, 0.22));
    glow.addColorStop(1, rgba(MOON, 0));
    ctx.fillStyle = glow;
    ctx.fillRect(mx - 70 * sc, my - 70 * sc, 140 * sc, 140 * sc);
    ctx.fillStyle = rgba(MOON, 0.9);
    ctx.beginPath();
    ctx.arc(mx, my, 13 * sc, 0, TAU);
    ctx.fill();
    ctx.fillStyle = GAME_SEA;
    ctx.fillRect(0, hz, w, h - hz);
    const hg = ctx.createLinearGradient(0, hz - 9 * sc, 0, hz + 9 * sc);
    hg.addColorStop(0, rgba(BLUE, 0));
    hg.addColorStop(0.5, rgba(BLUE, 0.09));
    hg.addColorStop(1, rgba(BLUE, 0));
    ctx.fillStyle = hg;
    ctx.fillRect(0, hz - 9 * sc, w, 18 * sc);
    ctx.fillStyle = rgba(BLUE, 0.2);
    ctx.fillRect(0, hz - 0.5 * sc, w, Math.max(1, sc));
  }

  // A street lamp: a pool of light on the road at 12%, a faint cone, a thin post and arm, a warm head with a glow.
  function lamp(ctx, n, hz, fog) {
    const x0 = SX0[n], y0 = SY0[n], sw = SW0[n], z0 = Z0[n];
    const s = sw / ROAD;
    const fade = 1 - fog * 0.75;
    const px = x0 - sw * 1.3, top = y0 - LAMP_H * s;
    const hx = px + sw * 0.62, hy = top + s * 70;
    const cx = x0 - sw * 0.45, rx = sw * 0.8, ry = Math.max(1.5, Math.min(rx, (Math.abs(y0 - hz) * 600) / z0));
    ctx.fillStyle = rgba(WARM, 0.06 * fade);
    ctx.beginPath();
    ctx.moveTo(hx, hy);
    ctx.lineTo(cx - rx * 0.9, y0);
    ctx.lineTo(cx + rx * 0.9, y0);
    ctx.closePath();
    ctx.fill();
    ctx.fillStyle = rgba(WARM, 0.12 * fade);
    ctx.beginPath();
    ctx.ellipse(cx, y0, rx, ry, 0, 0, TAU);
    ctx.fill();
    ctx.fillStyle = rgba(WARM, 0.08 * fade);
    ctx.beginPath();
    ctx.ellipse(cx, y0, rx * 0.5, ry * 0.5, 0, 0, TAU);
    ctx.fill();
    ctx.strokeStyle = GAME_POST;
    ctx.lineCap = 'round';
    ctx.lineWidth = Math.max(1, s * 55);
    ctx.beginPath();
    ctx.moveTo(px, y0);
    ctx.lineTo(px, top);
    ctx.lineTo(hx, hy);
    ctx.stroke();
    const gr = s * 900;
    if (gr > 2) {
      const g = ctx.createRadialGradient(hx, hy, 0, hx, hy, gr);
      g.addColorStop(0, rgba(WARM, 0.35 * fade));
      g.addColorStop(1, rgba(WARM, 0));
      ctx.fillStyle = g;
      ctx.fillRect(hx - gr, hy - gr, gr * 2, gr * 2);
    }
    ctx.fillStyle = rgba(WARM, fade);
    ctx.beginPath();
    ctx.ellipse(hx, hy, Math.max(1, s * 210), Math.max(0.6, s * 80), 0, 0, TAU);
    ctx.fill();
  }

  // The car: an original wedge coupe from behind and slightly above, drawn in units of its own width W with its origin at the bottom centre.
  // Yaw shifts the upper layers sideways, roll tilts the lot, pitch lifts the upper body, all from the pose in S (eased in the tables).
  function car(ctx, cx, by, W, S, vk) {
    const yaw = Math.sin((YAW_DEG * Math.PI * S.lx) / 180);
    const dy = S.pit * 0.006;
    const Y = (y) => (y < -0.37 ? -0.37 + (y + 0.37) * vk + dy : y);
    const sD = yaw * 0.7, sR = yaw * 0.95;
    const poly = (pts, fill) => {
      ctx.beginPath();
      ctx.moveTo(pts[0], pts[1]);
      for (let i = 2; i < pts.length; i += 2) ctx.lineTo(pts[i], pts[i + 1]);
      ctx.closePath();
      ctx.fillStyle = fill;
      ctx.fill();
    };
    ctx.save();
    ctx.translate(cx, by);
    ctx.scale(W, W);
    ctx.fillStyle = 'rgba(0,0,0,0.5)';
    ctx.beginPath();
    ctx.ellipse(0, 0.005, 0.58, 0.07, 0, 0, TAU);
    ctx.fill();
    ctx.fillStyle = rgba(BLUE, 0.1);
    ctx.beginPath();
    ctx.ellipse(0, 0.03, 0.46, 0.05, 0, 0, TAU);
    ctx.fill();
    ctx.rotate((-ROLL_DEG * Math.PI * S.lx) / 180);
    ctx.fillStyle = GAME_TYRE;
    for (const sx of [-1, 1]) {
      ctx.beginPath();
      ctx.roundRect(sx < 0 ? -0.52 : 0.39, -0.22, 0.13, 0.22, 0.03);
      ctx.fill();
    }
    poly([-0.43, -0.05, 0.43, -0.05, 0.45, -0.19, -0.45, -0.19], GAME_DIFFUSER);
    ctx.strokeStyle = 'rgba(255,255,255,0.06)';
    ctx.lineWidth = 0.008;
    for (let i = -2; i <= 2; i++) {
      ctx.beginPath();
      ctx.moveTo(i * 0.07, -0.07);
      ctx.lineTo(i * 0.07, -0.16);
      ctx.stroke();
    }
    poly([-0.49, -0.17, 0.49, -0.17, 0.46, -0.37, -0.46, -0.37], GAME_BODY);
    poly([-0.46, -0.37, 0.46, -0.37, 0.38 + sD, Y(-0.47), -0.38 + sD, Y(-0.47)], GAME_DECK);
    poly([-0.34 + sD, Y(-0.47), 0.34 + sD, Y(-0.47), 0.27 + sR, Y(-0.62), -0.27 + sR, Y(-0.62)], GAME_BODY);
    poly([-0.3 + sD, Y(-0.485), 0.3 + sD, Y(-0.485), 0.235 + sR, Y(-0.605), -0.235 + sR, Y(-0.605)], GAME_GLASS);
    poly([-0.27 + sR, Y(-0.62), 0.27 + sR, Y(-0.62), 0.22 + sR, Y(-0.66), -0.22 + sR, Y(-0.66)], GAME_DECK);
    if (S.lamp > 0.001) {
      ctx.fillStyle = rgba(WARM, 0.12 * S.lamp);
      ctx.fillRect(-0.49, -0.37, 0.98, 0.2);
      ctx.strokeStyle = rgba(WARM, 0.75 * S.lamp);
      ctx.lineWidth = 0.016;
      ctx.lineCap = 'round';
      ctx.beginPath();
      ctx.moveTo(-0.46, -0.37);
      ctx.lineTo(-0.38 + sD, Y(-0.47));
      ctx.lineTo(-0.27 + sR, Y(-0.62));
      ctx.lineTo(0.27 + sR, Y(-0.62));
      ctx.lineTo(0.38 + sD, Y(-0.47));
      ctx.lineTo(0.46, -0.37);
      ctx.stroke();
    }
    ctx.save();
    ctx.shadowColor = GAME_BLUE;
    ctx.shadowBlur = W * 0.07;
    ctx.fillStyle = GAME_BLUE;
    ctx.beginPath();
    ctx.roundRect(-0.43, -0.345, 0.86, 0.034, 0.017);
    ctx.fill();
    ctx.restore();
    for (const sx of [-1, 1]) {
      const tx = sx * 0.37, ty = -0.255;
      const rg = ctx.createRadialGradient(tx, ty, 0, tx, ty, 0.1 + 0.16 * S.braking);
      rg.addColorStop(0, rgba(TAIL, 0.3 + 0.45 * S.braking));
      rg.addColorStop(1, rgba(TAIL, 0));
      ctx.fillStyle = rg;
      ctx.fillRect(tx - 0.3, ty - 0.3, 0.6, 0.6);
      ctx.fillStyle = '#1A0A0C';
      ctx.beginPath();
      ctx.arc(tx, ty, 0.062, 0, TAU);
      ctx.fill();
      ctx.fillStyle = rgba(TAIL, 0.62 + 0.38 * S.braking);
      ctx.beginPath();
      ctx.arc(tx, ty, 0.05, 0, TAU);
      ctx.fill();
    }
    ctx.fillStyle = GAME_TYRE;
    for (const sx of [-1, 1]) {
      ctx.beginPath();
      ctx.arc(sx * 0.2, -0.115, 0.03, 0, TAU);
      ctx.fill();
    }
    ctx.restore();
  }

  // The HUD (RulerPainter rhythm: a notch every 11, a long tick every 5th, the long accent indicator at the head): the arc fills with BLUE as
  // throttle rises, a boost pip beside it, three launch pips that go out at 0.0, 0.1 and 0.2. No digits, letters, gear, timer, map or position.
  const NOTCH = 11, MAJOR_EVERY = 5, INSET = 5, HEIGHT = 0.6, TICKS = 20;
  function hud(ctx, w, h, tr, I) {
    const sc = h / 650, r = 0.2 * h, cx = w / 2, cy = 1.055 * h;
    const half = (TICKS * NOTCH * sc) / r / 2;
    const fill = clamp(I.rt);
    const dot = (3 + 0.8 * HEIGHT) * sc;
    ctx.lineCap = 'round';
    for (let n = 0; n <= TICKS; n++) {
      const th = -Math.PI / 2 - half + (n / TICKS) * 2 * half;
      const ux = Math.cos(th), uy = Math.sin(th);
      const lit = n <= fill * TICKS + 1e-6;
      if (n % MAJOR_EVERY === 0) {
        ctx.strokeStyle = rgba(lit ? BLUE : INK, lit ? 1 : 0.85);
        ctx.lineWidth = 3.2 * sc;
        ctx.beginPath();
        ctx.moveTo(cx + ux * (r + INSET * sc), cy + uy * (r + INSET * sc));
        ctx.lineTo(cx + ux * (r + (INSET + 20 * HEIGHT) * sc), cy + uy * (r + (INSET + 20 * HEIGHT) * sc));
        ctx.stroke();
      } else {
        ctx.fillStyle = rgba(lit ? BLUE : INK, lit ? 0.95 : 0.42);
        ctx.beginPath();
        ctx.arc(cx + ux * (r + INSET * sc + dot / 2), cy + uy * (r + INSET * sc + dot / 2), dot / 2, 0, TAU);
        ctx.fill();
      }
    }
    const th = -Math.PI / 2 - half + fill * 2 * half;
    const ux = Math.cos(th), uy = Math.sin(th);
    for (const [lw, a] of [[12 * sc, 0.18], [5 * sc, 1]]) {
      ctx.strokeStyle = rgba(BLUE, a);
      ctx.lineWidth = lw;
      ctx.beginPath();
      ctx.moveTo(cx + ux * (r + (INSET - 1) * sc), cy + uy * (r + (INSET - 1) * sc));
      ctx.lineTo(cx + ux * (r + (INSET + 32 * HEIGHT) * sc), cy + uy * (r + (INSET + 32 * HEIGHT) * sc));
      ctx.stroke();
    }
    const ring = 1 - seg(tr, 0.3, 0.7);
    for (let k = 0; k < 3; k++) {
      const lit = 1 - seg(tr, 0.1 * k, 0.1 * k + 0.03);
      const px = cx + (k - 1) * 20 * sc, py = cy - r - 18 * sc;
      if (ring > 0) {
        ctx.strokeStyle = rgba(INK, 0.22 * ring);
        ctx.lineWidth = 1.2 * sc;
        ctx.beginPath();
        ctx.arc(px, py, 4.5 * sc, 0, TAU);
        ctx.stroke();
      }
      if (lit > 0.01) {
        ctx.fillStyle = rgba(BLUE, lit);
        ctx.beginPath();
        ctx.arc(px, py, 4.5 * sc, 0, TAU);
        ctx.fill();
      }
    }
    const tb = -Math.PI / 2 + half + 0.22;
    const bx = cx + (r + 8 * sc) * Math.cos(tb), by = cy + (r + 8 * sc) * Math.sin(tb);
    if (I.boost > 0.01) {
      const g = ctx.createRadialGradient(bx, by, 0, bx, by, 18 * sc);
      g.addColorStop(0, rgba(BLUE, 0.45 * I.boost));
      g.addColorStop(1, rgba(BLUE, 0));
      ctx.fillStyle = g;
      ctx.fillRect(bx - 18 * sc, by - 18 * sc, 36 * sc, 36 * sc);
    }
    ctx.strokeStyle = rgba(INK, 0.35);
    ctx.lineWidth = 1.5 * sc;
    ctx.beginPath();
    ctx.arc(bx, by, 6 * sc, 0, TAU);
    ctx.stroke();
    ctx.fillStyle = rgba(BLUE, I.boost);
    ctx.beginPath();
    ctx.arc(bx, by, 6 * sc, 0, TAU);
    ctx.fill();
  }

  // EP.game.draw: the sky and sea, the road back to front (hidden segments over a crest are skipped), the lamps, speed lines, the car, the vignette, the HUD.
  function draw(ctx, w, h, tr, mode) {
    const att = mode === 'attract';
    const S = sample(tr, mode);
    const trk = att ? ATTRACT : RACE;
    const hk = att ? ATT_CAM : 1;
    const sc = h / 650;
    const F = w / 2 / Math.tan((FOV * (1 + FOV_BOOST * S.boost) * Math.PI) / 360);
    const hz = h * (att ? 0.37 : 0.4);
    const zCar = Z_CAR * hk;
    const bob = S.speed * BOB_AMP * (Math.sin(TAU * 10 * tr) + 0.5 * Math.sin(TAU * 16.5 * tr));
    const camSeg = S.d - zCar / SEG_LEN;
    const base = Math.floor(camSeg);
    const frac = camSeg - base;
    const camY = CAM_H * hk + tab(trk.elev, camSeg) + bob;
    const camX = S.x * ROAD * CAM_FOLLOW;
    const I = att ? null : input(Math.max(0, tr));

    ctx.save();
    ctx.beginPath();
    ctx.rect(0, 0, w, h);
    ctx.clip();
    sky(ctx, w, h, hz, sc, tr, att ? 0 : -headAt(S.pos) * 0.9);

    let xc = 0, dx = -trk.curve[wrapI(base)] * frac, clip = h;
    for (let n = 0; n < DRAW; n++) {
      const g = base + n;
      const z0 = (n - frac) * SEG_LEN, z1 = z0 + SEG_LEN;
      const xt = xc + dx;
      const e0 = trk.elev[wrapI(g)], e1 = trk.elev[wrapI(g + 1)];
      const xb = xc;
      xc += dx;
      dx += trk.curve[wrapI(g)];
      Z0[n] = z0;
      VIS[n] = 0;
      if (z1 <= ZNEAR) continue;
      const u = z0 < ZNEAR ? (ZNEAR - z0) / SEG_LEN : 0;
      const s0 = F / (z0 < ZNEAR ? ZNEAR : z0), s1 = F / z1;
      SX0[n] = w / 2 + (xb + (xt - xb) * u - camX) * s0;
      SY0[n] = hz - (e0 + (e1 - e0) * u - camY) * s0;
      SW0[n] = ROAD * s0;
      SX1[n] = w / 2 + (xt - camX) * s1;
      SY1[n] = hz - (e1 - camY) * s1;
      SW1[n] = ROAD * s1;
      if (SY1[n] < clip && SY1[n] < SY0[n]) {
        VIS[n] = 1;
        clip = SY1[n];
      }
    }

    for (let n = DRAW - 1; n >= 0; n--) {
      if (!VIS[n]) continue;
      const g = base + n;
      const fi = Math.min(FOGN - 1, Math.floor(Math.pow(n / DRAW, 1.7) * FOGN * 1.1));
      const x0 = SX0[n], y0 = SY0[n] + 1, w0 = SW0[n], x1 = SX1[n], y1 = SY1[n], w1 = SW1[n];
      const even = (Math.floor(g / STRIPE) & 1) === 0;
      ctx.fillStyle = (even ? FOGGED.landA : FOGGED.landB)[fi];
      ctx.fillRect(0, y1, w, y0 - y1);
      quad(ctx, -2, y0, Math.max(-2, x0 - w0 * SHOULDER), y0, Math.max(-2, x1 - w1 * SHOULDER), y1, -2, y1, FOGGED.sea[fi]);
      const kerb = (Math.floor(g / KERB_LEN) & 1) === 0 ? FOGGED.kerbA[fi] : FOGGED.kerbB[fi];
      quad(ctx, x0 - w0 * (1 + KERB), y0, x0 - w0, y0, x1 - w1, y1, x1 - w1 * (1 + KERB), y1, kerb);
      quad(ctx, x0 + w0, y0, x0 + w0 * (1 + KERB), y0, x1 + w1 * (1 + KERB), y1, x1 + w1, y1, kerb);
      quad(ctx, x0 - w0, y0, x0 + w0, y0, x1 + w1, y1, x1 - w1, y1, (even ? FOGGED.roadA : FOGGED.roadB)[fi]);
      if (even) quad(ctx, x0 - w0 * 0.022, y0, x0 + w0 * 0.022, y0, x1 + w1 * 0.022, y1, x1 - w1 * 0.022, y1, FOGGED.dash[fi]);
    }

    for (let n = DRAW - 1; n >= 0; n--) {
      if (VIS[n] && Z0[n] > 700 && (((base + n) % LAMP_EVERY) + LAMP_EVERY) % LAMP_EVERY === 0) lamp(ctx, n, hz, Math.min(1, Math.pow(n / DRAW, 1.7) * 1.1));
    }

    if (S.boost > 0.02) {
      const R = Math.hypot(w, h) * 0.62;
      ctx.lineCap = 'round';
      for (const l of LINES) {
        const p = (((l.ph + tr * 1.9) % 1) + 1) % 1;
        const r0 = (0.12 + 0.88 * p) * R, r1 = r0 + R * (0.06 + 0.25 * p);
        ctx.strokeStyle = rgba(BLUE, S.boost * 0.55 * Math.sin(Math.PI * p));
        ctx.lineWidth = Math.max(1, 1.2 * sc * l.wd * (0.5 + p));
        ctx.beginPath();
        ctx.moveTo(w / 2 + Math.cos(l.th) * r0, hz + Math.sin(l.th) * r0);
        ctx.lineTo(w / 2 + Math.cos(l.th) * r1, hz + Math.sin(l.th) * r1);
        ctx.stroke();
      }
    }

    const nc = Math.min(DRAW - 1, Math.floor(zCar / SEG_LEN + frac));
    const uc = clamp((zCar - Z0[nc]) / SEG_LEN);
    const roadW = lerp(SW0[nc], SW1[nc], uc);
    car(ctx, lerp(SX0[nc], SX1[nc], uc) + S.x * roadW, lerp(SY0[nc], SY1[nc], uc), roadW * 0.52, S, 1 + (hk - 1) * 1.5);

    const vg = ctx.createRadialGradient(w / 2, h * 0.46, h * 0.28, w / 2, h * 0.46, Math.hypot(w, h) * 0.56);
    vg.addColorStop(0, rgba(GAME_VIGNETTE, 0));
    vg.addColorStop(1, rgba(GAME_VIGNETTE, 0.3 + 0.4 * S.speed));
    ctx.fillStyle = vg;
    ctx.fillRect(0, 0, w, h);

    if (!att) hud(ctx, w, h, tr, I);
    ctx.restore();
  }

  // Dev check: the attract loop closes (frame 0 == frame LOOP within 1e-3) and the race speed stays in 0..1.
  function check() {
    const a = attractAt(0), b = attractAt(LOOP);
    for (const k of ['speed', 'pos', 'x', 'curve', 'lamp', 'braking', 'lx', 'pit']) if (!(Math.abs(a[k] - b[k]) <= 1e-3)) return false;
    for (let i = 0; i < N; i++) {
      const s = TBL.v[i] / V_TOP;
      if (!(s >= 0 && s <= 1 + 1e-6)) return false;
    }
    return a.speed >= 0 && a.speed <= 1;
  }

  EP.game = { input, state, draw, check, ROWS, A_PRESS, EASE: EASE_S, LOOP };
})();
