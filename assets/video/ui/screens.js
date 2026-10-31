// The app's other screens, ported from screens/*.kt and drawn at 1 unit = 1 dp. Each is a pure function of its local time `lt`.
(function () {
  const { clamp, lerp, ease, seg, rgba, text, textMid, fillRR, rr, icon, measure } = EP;
  const TOP = 30; // the status bar's room: the app draws edge to edge and pads by the system bars
  const BOTTOM = 20;
  const S = {}; // screen name -> draw(ctx, w, h, pal, lt)
  EP.screens = S;

  // A press: 0 -> 1 -> 0 over d seconds from t0.
  const tri = (lt, t0, d = 0.32) => {
    const x = (lt - t0) / d;
    return x < 0 || x > 1 ? 0 : Math.sin(x * Math.PI);
  };
  // A held press that stays down from t0 to t1.
  const held = (lt, t0, t1) => ease((lt - t0) / 0.12) * (1 - ease((lt - t1) / 0.12));

  // A finger through keys [[t, x, y], ...], each leg eased. It is drawn the way "show taps" draws one: only while it
  // touches. By default that is the legs where it holds still (a tap or a press); `drag` keeps it down throughout.
  EP.path = (lt, keys, drag = false) => {
    const first = keys[0], last = keys[keys.length - 1];
    if (lt < first[0] - 0.06 || lt > last[0] + 0.08) return null;
    let i = 0;
    while (i < keys.length - 2 && lt > keys[i + 1][0]) i++;
    const [t0, x0, y0] = keys[i], [t1, x1, y1] = keys[i + 1];
    const k = ease((lt - t0) / Math.max(t1 - t0, 1e-6));
    let a;
    if (drag) a = ease((lt - (first[0] - 0.06)) / 0.06) * (1 - ease((lt - last[0]) / 0.08));
    else if (x0 === x1 && y0 === y1) a = ease((lt - (t0 - 0.06)) / 0.06) * (1 - ease((lt - t1) / 0.08));
    else a = 0;
    if (a <= 0) return null;
    return { x: lerp(x0, x1, k), y: lerp(y0, y1, k), a };
  };
  const dot = (ctx, p) => p && EP.tapDot(ctx, p.x, p.y, p.a);

  function bar(ctx, w, pal, title, back, action, actionPress = 0) {
    const y = TOP + 24;
    if (back) {
      icon(ctx, 'chevron', 20, y, 22, pal.accent);
      text(ctx, back, 34, y + 6, { size: 17, w: 'Bold', color: pal.accent });
    }
    text(ctx, title, w / 2, y + 6, { size: 17, w: 'Bold', color: pal.ink, align: 'center' });
    let chip = null;
    if (action) {
      const cw = measure(ctx, action, 15, 'Bold') + 28;
      chip = { x: w - 8 - cw, y: y - 16, w: cw, h: 32 };
      fillRR(ctx, chip.x, chip.y, chip.w, chip.h, 16, actionPress > 0 ? pal.line : pal.faint);
      textMid(ctx, action, chip.x + cw / 2, y, { size: 15, w: 'Bold', color: pal.ink, align: 'center' });
    }
    return chip;
  }

  // ---- Devices: the pairing screen (strings.xml "Finding the laptop"). ----
  S.devices = (ctx, w, h, pal, lt) => {
    text(ctx, 'Devices', 20, TOP + 62, { size: 32, w: 'Black', color: pal.ink, track: -0.01 });
    text(ctx, 'Paired laptops', 20, TOP + 112, { size: 13, w: 'Bold', color: pal.dim });
    const connecting = ease((lt - 1.3) / 0.3), connected = ease((lt - 2.5) / 0.4);
    const pressed = tri(lt, 1.2, 0.3);
    fillRR(ctx, 16, TOP + 124, w - 32, 68, 14, pal.card);
    if (pressed) fillRR(ctx, 16, TOP + 124, w - 32, 68, 14, pal.faint, pressed * 0.8);
    icon(ctx, 'terminal', 44, TOP + 158, 24, pal.ink);
    text(ctx, 'Laptop', 70, TOP + 154, { size: 16, w: 'Bold', color: pal.ink });
    const status = connected > 0.5 ? 'Connected' : connecting > 0.5 ? 'Connecting…' : 'Remembered';
    text(ctx, status, 70, TOP + 176, { size: 13, color: connected > 0.5 ? pal.accent : pal.dim });
    icon(ctx, 'check', w - 44, TOP + 158, 22, pal.accent, connected);
    const legend = connected > 0.5 ? 'The remembered laptop connects when Edgepad opens.' : 'Tap a laptop to connect.';
    text(ctx, legend, 20, TOP + 214, { size: 13, color: pal.dim });
    // Open controls: dim until a laptop is connected.
    const press = tri(lt, 4.3, 0.3);
    const by = h - BOTTOM - 50 - 16;
    EP.rr(ctx, 16, by, w - 32, 50, 14);
    ctx.fillStyle = rgba(connected > 0.5 ? pal.accent : pal.faint, 1 - press * 0.15);
    ctx.fill();
    textMid(ctx, 'Open controls', w / 2, by + 25, { size: 17, w: 'Bold', color: connected > 0.5 ? pal.onAccent : pal.dim, align: 'center' });
    if (connected < 0.5) text(ctx, 'Connect to a laptop to open the controls.', w / 2, by - 12, { size: 13, color: pal.dim, align: 'center' });
    dot(ctx, EP.path(lt, [[0.7, 260, 560], [1.15, w / 2, TOP + 158], [1.5, w / 2, TOP + 158], [3.7, w / 2, by + 25], [4.4, w / 2, by + 25]]));
  };

  // ---- Deck (Deck.kt, DeckPage.kt, DeckLayout.kt): tiles on the ground with no fill, the picture over a bold label. ----
  // The macros a laptop might hold, as the laptop names them, with the brand mark (ui/icons.js) and its colour.
  // A mark in black or near-black is drawn in the ink instead, so it reads on the dark theme as the real icon would.
  const MACROS = [
    ['Firefox', 'firefoxbrowser', '#FF7139'], ['Discord', 'discord', '#5865F2'], ['Terminal', 'gnometerminal', null],
    ['Steam', 'steam', null], ['OBS', 'obsstudio', null], ['Notion', 'notion', null], ['Figma', 'figma', '#F24E1E'],
    ['Telegram', 'telegram', '#26A5E4'], ['Blender', 'blender', '#E87D0D'], ['Spotify', 'spotify', '#1ED760'],
    ['GitHub', 'github', null], ['Obsidian', 'obsidian', '#7C3AED'], ['VLC', 'vlcmediaplayer', '#FF8800'],
  ];
  EP.MACROS = MACROS;
  const GAP = 8; // Space.S between tiles and rows
  // Deck.plan: the column count that leaves the cell's shorter side longest, or the fixed one.
  function plan(count, across, down, fixed) {
    let best = null;
    for (let c = fixed || 1; c <= (fixed || count); c++) {
      const rows = Math.ceil(count / c);
      const cw = (across - (c - 1) * GAP) / c, ch = Math.max((down - (rows - 1) * GAP) / rows, 48);
      if (!best || fixed || Math.min(cw, ch) > Math.min(best.cw, best.ch)) best = { cols: c, rows, cw, ch };
    }
    return best;
  }
  // The rect of display place i; a short last row keeps the tile width and is centred under the others (Deck.row).
  function place(i, count, g, area) {
    const row = Math.floor(i / g.cols), col = i % g.cols;
    const inRow = row === g.rows - 1 ? count - row * g.cols : g.cols;
    const shift = ((g.cols - inRow) * (g.cw + GAP)) / 2;
    return { x: area.x + shift + col * (g.cw + GAP), y: area.y + row * (g.ch + GAP), w: g.cw, h: g.ch };
  }
  const mixRect = (a, b, k) => ({ x: lerp(a.x, b.x, k), y: lerp(a.y, b.y, k), w: lerp(a.w, b.w, k), h: lerp(a.h, b.h, k) });
  // One tile: a press or the focus is the only shape drawn around it; edit mode adds a dashed outline (4 on, 3 off).
  function tile(ctx, pal, r, draw, label, o = {}) {
    const cx = r.x + r.w / 2, cy = r.y + r.h / 2;
    const labelSp = clamp(Math.min(r.w, r.h) * 0.08, 17, 18);
    const line = labelSp * 1.25;
    const pic = Math.max(16, Math.min(r.w - 16, r.h - 16 - line - 4));
    ctx.save();
    ctx.globalAlpha *= o.a ?? 1;
    if (o.lift) {
      ctx.translate(cx, cy);
      ctx.scale(1 + 0.06 * o.lift, 1 + 0.06 * o.lift);
      ctx.translate(-cx, -cy);
      ctx.shadowColor = 'rgba(0,0,0,0.35)';
      ctx.shadowBlur = 24 * o.lift;
      ctx.shadowOffsetY = 8 * o.lift;
      fillRR(ctx, r.x, r.y, r.w, r.h, 16, pal.card, o.lift);
      ctx.shadowColor = 'transparent';
    }
    if (o.press > 0) fillRR(ctx, r.x, r.y, r.w, r.h, 16, pal.faint, o.press);
    if (o.edit > 0) {
      ctx.setLineDash([4, 3]);
      ctx.lineWidth = 1;
      ctx.strokeStyle = rgba(pal.dim, o.edit);
      rr(ctx, r.x + 0.5, r.y + 0.5, r.w - 1, r.h - 1, 16);
      ctx.stroke();
      ctx.setLineDash([]);
    }
    const top = cy - (pic + 4 + line) / 2;
    draw(cx, top + pic / 2, pic);
    text(ctx, label, cx, top + pic + 4 + line * 0.78, { size: labelSp, w: 'Bold', color: pal.ink, align: 'center' });
    ctx.restore();
  }
  // A grouped card of rows (Page.kt Column.card / Ui.choices): returns the centre y of each row.
  function section(ctx, pal, label, y) {
    text(ctx, label, 32, y + 24 + 13, { size: 13, w: 'Bold', color: pal.dim });
    return y + 24 + 13 + 8 + 4;
  }
  function card(ctx, w, pal, y, rows, rowH = 52) {
    const h = rows * rowH + (rows - 1);
    fillRR(ctx, 16, y, w - 32, h, 14, pal.card);
    ctx.fillStyle = rgba(pal.line);
    for (let i = 1; i < rows; i++) ctx.fillRect(32, y + i * (rowH + 1) - 1, w - 64, 1);
    return Array.from({ length: rows }, (_, i) => y + i * (rowH + 1) + rowH / 2);
  }
  EP.ui = { section, card };
  // Ui.toggle's Switch: a 60 x 30 pill, accent when on, off-grey when not, a 26 dp white knob 2 dp in.
  function toggle(ctx, pal, x, cy, on) {
    fillRR(ctx, x, cy - 15, 60, 30, 15, rgba(EP.mixRgb(pal.off, pal.accent, on)));
    ctx.beginPath();
    ctx.arc(x + lerp(15, 45, on), cy, 13, 0, Math.PI * 2);
    ctx.fillStyle = '#FFFFFF';
    ctx.fill();
    if (pal.k > 0.5) {
      ctx.lineWidth = 1;
      ctx.strokeStyle = rgba(pal.line);
      ctx.stroke();
    }
  }
  EP.toggle = toggle;
  // Glyph.Shape.CHECK in the accent, 20 dp.
  const check = (ctx, pal, x, y, a = 1) => icon(ctx, 'check', x, y, 18, pal.accent, a);

  // The macro deck. lt: 1.0 a tap runs Spotify; 2.3 Edit layout; 3.5 Spotify lifts, 3.8-4.8 dragged to the front,
  // dropped; 5.9 the grid picker's "3 x 5"; 7.2 Done; 8.4 a tap.
  S.macros = (ctx, w, h, pal, lt) => {
    const edit = ease((lt - 2.35) / 0.3) * (1 - ease((lt - 7.25) / 0.3));
    const chip = bar(ctx, w, pal, 'Macros', 'Surface', edit > 0.5 ? 'Done' : 'Edit layout', tri(lt, 2.3) + tri(lt, 7.2));
    const n = MACROS.length;
    const across = w - 32, down = h - TOP - 48 - 24 - BOTTOM;
    const picked = lt > 5.95; // "3 x 5": five columns
    const full = plan(n, across, down, picked ? 5 : 0), half = plan(n, across, down / 2, picked ? 5 : 0);
    const g = { cols: edit > 0.5 ? half.cols : full.cols, rows: edit > 0.5 ? half.rows : full.rows, cw: lerp(full.cw, half.cw, edit), ch: lerp(full.ch, half.ch, edit) };
    const area = { x: 16, y: TOP + 48 };
    const dropped = lt > 4.85;
    const order = dropped ? [9, ...MACROS.keys()].filter((v, i, a) => a.indexOf(v) === i) : [...MACROS.keys()];
    const settle = ease((lt - 4.85) / 0.25);
    const rectOf = (m) => place(order.indexOf(m), n, g, area);
    const before = (m) => place(m, n, g, area);
    const lift = held(lt, 3.5, 4.85);
    const fingerAt = EP.path(lt, [[3.5, 0, 0], [3.8, 0, 0], [4.8, 1, 1], [4.85, 1, 1]], true);
    MACROS.forEach(([label, brand, colour], m) => {
      if (m === 9 && lift > 0) return;
      const r = dropped && settle < 1 && m !== 9 ? mixRect(before(m), rectOf(m), settle) : rectOf(m);
      const under = m === 0 && lift > 0.5 && fingerAt && fingerAt.x > 0.8 ? 0.5 : 1;
      const draw = (cx, cy, size) => EP.brand(ctx, brand, cx, cy, size * 0.78, colour || rgba(pal.ink));
      tile(ctx, pal, r, draw, label, { edit, a: under, press: m === 9 ? tri(lt, 0.95, 0.35) + tri(lt, 8.35, 0.35) : 0 });
    });
    // The lifted tile rides the finger from its place to the first one.
    const from = before(9), to = place(0, n, g, area);
    if (lift > 0) {
      const k = fingerAt ? fingerAt.x : 0;
      const r = mixRect(from, to, k);
      const draw = (cx, cy, size) => EP.brand(ctx, 'spotify', cx, cy, size * 0.78, '#1ED760');
      tile(ctx, pal, r, draw, 'Spotify', { edit, lift });
    }
    // The grid picker under the half-height deck: "Automatic" and the grids DeckLayout.fitting offers.
    if (edit > 0) {
      ctx.save();
      ctx.globalAlpha *= edit;
      const top = area.y + down / 2 + 8;
      const ys = card(ctx, w, pal, section(ctx, pal, 'Grid, rows × columns', top), 4, 48);
      ['Automatic', '3 × 5', '4 × 4', '5 × 3'].forEach((s, i) => {
        textMid(ctx, s, 32, ys[i], { size: 16, color: pal.ink });
        if (i === (picked ? 1 : 0)) check(ctx, pal, w - 42, ys[i]);
        if (i === 1) {
          const p = tri(lt, 5.85, 0.3);
          if (p) fillRR(ctx, 16, ys[i] - 24, w - 32, 48, 0, pal.faint, p * 0.6);
        }
      });
      ctx.restore();
    }
    const c = (r) => [r.x + r.w / 2, r.y + r.h / 2];
    const chipAt = [chip.x + chip.w / 2, chip.y + chip.h / 2];
    const pickY = area.y + down / 2 + 8 + 24 + 13 + 12 + 48 + 25;
    const [sx, sy] = c(from);
    dot(ctx, EP.path(lt, [
      [0.55, sx + 60, sy + 80], [0.95, sx, sy], [1.3, sx, sy],
      [2.05, ...chipAt], [2.45, ...chipAt],
      [3.3, ...c(before(9))], [3.8, ...c(before(9))], [4.8, ...c(to)], [5.1, ...c(to)],
      [5.6, w / 2, pickY], [6.0, w / 2, pickY],
      [6.9, ...chipAt], [7.3, ...chipAt],
      [8.1, ...c(rectOf(9))], [8.5, ...c(rectOf(9))],
    ]));
    dot(ctx, EP.path(lt, [[3.8, ...c(before(9))], [4.8, ...c(to)]], true));
  };

  // ---- Controls (ControlsScreen.kt): the same deck, its tiles in Target order with the app's Lucide icons. ----
  S.controls = (ctx, w, h, pal, lt) => {
    bar(ctx, w, pal, 'Controls', 'Surface', null);
    const across = w - 32, down = h - TOP - 48 - 24 - BOTTOM;
    const items = [['Keyboard', 'keyboard'], ['Gamepad', 'gamepad'], ['Capture', 'scan'], ['Annotate', 'pen']];
    const g = plan(items.length, across, down, 0);
    const area = { x: 16, y: TOP + 48 };
    const at = (i) => place(i, items.length, g, area);
    items.forEach(([name, ic], i) => {
      const draw = (cx, cy, size) => icon(ctx, ic, cx, cy, size * 0.5, pal.ink);
      tile(ctx, pal, at(i), draw, name, { press: i === 0 ? tri(lt, 2.2, 0.4) : 0 });
    });
    const r = at(0);
    dot(ctx, EP.path(lt, [[0.6, w - 60, h - 140], [1.9, r.x + r.w / 2, r.y + r.h / 2], [2.5, r.x + r.w / 2, r.y + r.h / 2]]));
  };

  // ---- Keyboard (KeyboardScreen.kt): a bar back to Controls, then six rows of iOS-style keys. Character keys on the
  // brighter fill, named keys a step greyer; a held or latched modifier, and a pressed key, in the accent. ----
  const ROWS = [
    [['Esc', 1], ...'1 2 3 4 5 6 7 8 9 10 11 12'.split(' ').map((n) => ['F' + n, 1]), ['Del', 1]],
    [['`', 1], ...'1234567890-='.split('').map((c) => [c, 1]), ['Backspace', 2]],
    [['Tab', 1.5], ...'QWERTYUIOP[]'.split('').map((c) => [c, 1]), ['\\', 1.5]],
    [['Caps', 1.75], ...'ASDFGHJKL;\''.split('').map((c) => [c, 1]), ['Enter', 2.25]],
    [['Shift', 2.25], ...'ZXCVBNM,./'.split('').map((c) => [c, 1]), ['Shift', 2.75]],
    [['Ctrl', 1.25], ['Win', 1.25], ['Alt', 1.25], ['Space', 6.25], ['Alt', 1.25], ['Menu', 1.25], ['Ctrl', 1.25], ['←', 1], ['↑', 1], ['↓', 1], ['→', 1]],
  ];
  // lt: 1.1 Ctrl latched by a tap, 2.2 C, 3.4 Ctrl released; 3.9 Shift held while 4.3 Alt is tapped.
  S.keyboard = (ctx, w, h, pal, lt) => {
    bar(ctx, w, pal, 'Keyboard', 'Controls', null);
    const top = TOP + 48, pad = 6, gap = 6;
    const rowH = (h - top - BOTTOM - 2 * pad - 5 * gap) / 6;
    const keyFill = pal.k < 0.5 ? pal.faint : pal.card, fnFill = pal.k < 0.5 ? pal.card : pal.off;
    const ctrl = ease((lt - 1.2) / 0.12) * (1 - ease((lt - 3.4) / 0.15));
    const shift = held(lt, 3.9, 5.0);
    const press = { C: tri(lt, 2.2, 0.35), V: tri(lt, 2.9, 0.35), AltL: tri(lt, 4.3, 0.35) };
    const hit = {};
    ROWS.forEach((row, ri) => {
      const units = row.reduce((a, [, u]) => a + u, 0);
      const unit = (w - 2 * pad - (row.length - 1) * gap) / units;
      let x = pad;
      const y = top + pad + ri * (rowH + gap);
      const seen = {};
      row.forEach(([label, u]) => {
        const kw = unit * u;
        const id = label + (seen[label] ? 'R' : 'L');
        seen[label] = true;
        const fn = (label.length > 1 && label !== 'Space') || '←↑↓→'.includes(label);
        const k = id === 'CtrlL' ? ctrl : id === 'ShiftL' ? shift : id === 'AltL' ? press.AltL : press[label] || 0;
        if (pal.k > 0.5) fillRR(ctx, x, y + 1, kw, rowH, 10, pal.line);
        fillRR(ctx, x, y, kw, rowH, 10, fn ? fnFill : keyFill);
        if (k > 0) fillRR(ctx, x, y, kw, rowH, 10, pal.accent, k);
        textMid(ctx, label, x + kw / 2, y + rowH / 2, { size: 14, w: 'Bold', color: k > 0.5 ? pal.onAccent : pal.ink, align: 'center' });
        hit[ri + id] = [x + kw / 2, y + rowH / 2];
        x += kw + gap;
      });
    });
    dot(ctx, EP.path(lt, [[0.6, 140, h - 30], [1.1, ...hit['5CtrlL']], [1.45, ...hit['5CtrlL']], [2.1, ...hit['4CL']], [2.45, ...hit['4CL']], [2.85, ...hit['4VL']], [3.15, ...hit['4VL']], [3.3, ...hit['5CtrlL']], [3.55, ...hit['5CtrlL']]]));
    dot(ctx, EP.path(lt, [[3.75, ...hit['4ShiftL']], [5.0, ...hit['4ShiftL']]]));
    dot(ctx, EP.path(lt, [[4.15, ...hit['5AltL']], [4.6, ...hit['5AltL']]]));
  };

  // ---- Annotate (AnnotateScreen.kt, AnnotateCanvas.kt): a pen pad for the laptop's screen, and Sign. ----
  const TOOLS = ['Pen', 'Highlighter', 'Line', 'Arrow', 'Rectangle', 'Ellipse'];
  const COLOURS = ['#16171A', '#FFFFFF', '#FF453A', '#FF9F0A', '#FFD60A', '#30D158', '#0A84FF', '#BF5AF2'];
  // A polyline drawn up to fraction `p`.
  function poly(ctx, pts, p) {
    const lens = [0];
    for (let i = 1; i < pts.length; i++) lens.push(lens[i - 1] + Math.hypot(pts[i][0] - pts[i - 1][0], pts[i][1] - pts[i - 1][1]));
    const goal = lens[lens.length - 1] * clamp(p);
    ctx.beginPath();
    ctx.moveTo(pts[0][0], pts[0][1]);
    let end = pts[0];
    for (let i = 1; i < pts.length; i++) {
      if (lens[i] <= goal) {
        ctx.lineTo(pts[i][0], pts[i][1]);
        end = pts[i];
      } else {
        const f = (goal - lens[i - 1]) / (lens[i] - lens[i - 1]);
        end = [lerp(pts[i - 1][0], pts[i][0], f), lerp(pts[i - 1][1], pts[i][1], f)];
        ctx.lineTo(end[0], end[1]);
        break;
      }
    }
    ctx.stroke();
    return end;
  }
  const ellipsePts = (cx, cy, rx, ry) => Array.from({ length: 41 }, (_, i) => [cx + rx * Math.cos(i * 0.157 - 1.2), cy + ry * Math.sin(i * 0.157 - 1.2)]);
  // The signature: one loop and a flourish, as fractions of the pad.
  const SIG = [[0.16, 0.62], [0.22, 0.3], [0.28, 0.7], [0.34, 0.35], [0.4, 0.62], [0.47, 0.5], [0.54, 0.64], [0.6, 0.38], [0.66, 0.6], [0.78, 0.55], [0.86, 0.5]];
  // The strokes of the demo in a pw x ph box at the origin: a red ellipse (0.8-1.7), a blue arrow (2.4-3.1), cleared
  // as Sign comes on (3.9), then the signature (4.5-6.0). The laptop's screen shows the same strokes but never the
  // signature, which stays on the phone (`sign` false).
  EP.annotStrokes = (ctx, pw, ph, lt, ink, sign) => {
    const k = pw / 358;
    const signing = ease((lt - 3.9) / 0.3);
    ctx.lineCap = ctx.lineJoin = 'round';
    const draw = (pts, p, colour, width) => {
      if (p <= 0) return null;
      ctx.strokeStyle = colour;
      ctx.lineWidth = width * k;
      return poly(ctx, pts, p);
    };
    const base = ctx.globalAlpha;
    ctx.globalAlpha = base * (1 - signing);
    draw(ellipsePts(pw * 0.38, ph * 0.5, pw * 0.2, ph * 0.28), seg(lt, 0.8, 1.7), '#FF453A', 4);
    const arrow = [[pw * 0.62, ph * 0.22], [pw * 0.84, ph * 0.5]];
    const tip = draw(arrow, seg(lt, 2.4, 3.1), '#0A84FF', 4);
    if (tip && lt > 3.05) {
      ctx.beginPath();
      ctx.moveTo(arrow[1][0] - 14 * k, arrow[1][1] - 4 * k);
      ctx.lineTo(arrow[1][0], arrow[1][1]);
      ctx.lineTo(arrow[1][0] - 6 * k, arrow[1][1] - 15 * k);
      ctx.stroke();
    }
    if (sign) {
      ctx.globalAlpha = base * signing;
      draw(SIG.map(([a, b]) => [a * pw, b * ph]), seg(lt, 4.5, 6.0), ink, 3);
    }
    ctx.globalAlpha = base;
  };
  EP.annotSignature = (ctx, pw, ph, ink, width) => {
    ctx.lineCap = ctx.lineJoin = 'round';
    ctx.strokeStyle = ink;
    ctx.lineWidth = width;
    poly(ctx, SIG.map(([a, b]) => [a * pw, b * ph]), 1);
  };

  S.annotate = (ctx, w, h, pal, lt) => {
    const signing = ease((lt - 3.9) / 0.3);
    bar(ctx, w, pal, 'Annotate', null, 'Exit');
    // One row that scrolls sideways: tools, then the eight colours. Sign hides every tool but the pen.
    const rowY = TOP + 62;
    const scroll = 330 * ease((lt - 1.9) / 0.7) * (1 - ease((lt - 3.2) / 0.6));
    ctx.save();
    ctx.beginPath();
    ctx.rect(0, rowY - 4, w, 56);
    ctx.clip();
    let x = 16 - scroll;
    const sel = signing > 0.5 || lt < 1.9 ? 0 : 3; // Pen, then Arrow, and Pen again to sign
    TOOLS.forEach((name, i) => {
      const keep = i === 0 || signing < 0.5;
      const pw = measure(ctx, name, 13, 'Bold') + 28;
      if (keep) {
        const on = i === sel;
        fillRR(ctx, x, rowY + 4, pw, 36, 14, on ? pal.accent : pal.faint);
        textMid(ctx, name, x + pw / 2, rowY + 22, { size: 13, w: 'Bold', color: on ? pal.onAccent : pal.ink, align: 'center' });
        x += pw + 8;
      }
    });
    x += 8;
    COLOURS.forEach((c, i) => {
      ctx.beginPath();
      ctx.arc(x + 24, rowY + 22, 11, 0, Math.PI * 2);
      ctx.fillStyle = c;
      ctx.fill();
      if (i === (signing > 0.5 ? 0 : lt > 2.2 ? 6 : 2)) { // red for the ellipse, blue for the arrow, black to sign
        ctx.lineWidth = 3;
        ctx.strokeStyle = rgba(pal.accent);
        ctx.beginPath();
        ctx.arc(x + 24, rowY + 22, 15, 0, Math.PI * 2);
        ctx.stroke();
      }
      x += 48;
    });
    ctx.restore();
    // Undo, Clear and the Sign switch.
    const cy = rowY + 74;
    [['Undo', 16], ['Clear', 84]].forEach(([s, cx0]) => {
      const cw = measure(ctx, s, 15, 'Bold') + 28;
      fillRR(ctx, cx0, cy - 16, cw, 32, 16, pal.faint);
      textMid(ctx, s, cx0 + cw / 2, cy, { size: 15, w: 'Bold', color: pal.ink, align: 'center' });
    });
    text(ctx, 'Sign', w - 16 - 51 - 12, cy + 5, { size: 16, color: pal.ink, align: 'right' });
    fillRR(ctx, w - 16 - 51, cy - 15.5, 51, 31, 15.5, signing > 0.5 ? pal.accent : pal.off);
    ctx.beginPath();
    ctx.arc(w - 16 - 51 + lerp(15.5, 35.5, signing), cy, 13, 0, Math.PI * 2);
    ctx.fillStyle = '#FFFFFF';
    ctx.fill();
    // The pen pad, shaped to the laptop's screen (16 : 10).
    const pw = w - 32, ph = pw * 0.625, py = cy + 36;
    fillRR(ctx, 16, py, pw, ph, 16, pal.card);
    ctx.save();
    EP.rr(ctx, 16, py, pw, ph, 16);
    ctx.clip();
    ctx.translate(16, py);
    EP.annotStrokes(ctx, pw, ph, lt, rgba(pal.ink), true);
    ctx.restore();
    const sig = SIG.map(([a, b]) => [a * pw, b * ph]);
    // Save signature.
    const press = tri(lt, 6.5, 0.3);
    const by = py + ph + 20;
    EP.rr(ctx, 16, by, w - 32, 50, 14);
    ctx.fillStyle = rgba(pal.accent, signing * (1 - press * 0.15));
    ctx.fill();
    textMid(ctx, 'Save signature', w / 2, by + 25, { size: 17, w: 'Bold', color: pal.onAccent, align: 'center', a: signing });
    const toast = ease((lt - 6.7) / 0.3);
    if (toast > 0) {
      const tw = measure(ctx, 'Copied and saved to Pictures/Edgepad', 14, 'Bold') + 36;
      ctx.save();
      ctx.globalAlpha *= toast;
      ctx.shadowColor = 'rgba(0,0,0,0.3)';
      ctx.shadowBlur = 16;
      fillRR(ctx, (w - tw) / 2, h - BOTTOM - 70, tw, 44, 22, pal.card);
      ctx.shadowColor = 'transparent';
      textMid(ctx, 'Copied and saved to Pictures/Edgepad', w / 2, h - BOTTOM - 48, { size: 14, w: 'Bold', color: pal.ink, align: 'center' });
      ctx.restore();
    }
    const pad = (fx, fy) => [16 + fx * pw, py + fy * ph];
    const e = ellipsePts(pw * 0.38, ph * 0.5, pw * 0.2, ph * 0.28);
    const ePos = (p) => pad(e[Math.min(40, Math.round(p * 40))][0] / pw, e[Math.min(40, Math.round(p * 40))][1] / ph);
    const sPos = (p) => {
      const i = Math.min(sig.length - 1, Math.floor(p * (sig.length - 1)));
      return pad(sig[i][0] / pw, sig[i][1] / ph);
    };
    const keys = [];
    for (let k = 0; k <= 8; k++) keys.push([0.8 + k * (0.9 / 8), ...ePos(k / 8)]);
    dot(ctx, EP.path(lt, keys, true));
    dot(ctx, EP.path(lt, [[2.4, ...pad(0.62, 0.22)], [3.1, ...pad(0.84, 0.5)]], true));
    dot(ctx, EP.path(lt, [[3.6, w - 16 - 51 + 35.5, cy], [3.95, w - 16 - 51 + 35.5, cy]]));
    const sk = [];
    for (let k = 0; k <= 10; k++) sk.push([4.5 + k * 0.15, ...sPos(k / 10)]);
    dot(ctx, EP.path(lt, sk, true));
    dot(ctx, EP.path(lt, [[6.3, w / 2, by + 25], [6.7, w / 2, by + 25]]));
  };

  // ---- Capture (CaptureScreen.kt): three buttons over a bare pad. ----
  S.capture = (ctx, w, h, pal, lt) => {
    bar(ctx, w, pal, 'Capture', 'Controls', null);
    const labels = ['Region', 'Full screen', 'Record'];
    const bw = (w - 32 - 16) / 3, by = TOP + 62;
    const presses = [held(lt, 1.0, 1.5), 0, tri(lt, 5.0, 0.4)];
    labels.forEach((s, i) => {
      const x = 16 + i * (bw + 8);
      fillRR(ctx, x, by, bw, 50, 14, pal.accentSoft);
      if (presses[i]) fillRR(ctx, x, by, bw, 50, 14, pal.accent, presses[i] * 0.35);
      textMid(ctx, s, x + bw / 2, by + 25, { size: 17, w: 'Bold', color: pal.accent, align: 'center' });
    });
    const lines = ['For a region, tap Region, then tap, hold and move', 'on the pad to drag out the area.'];
    lines.forEach((l, i) => text(ctx, l, 20, by + 76 + i * 20, { size: 14, color: pal.dim }));
    const drag = [[1.9, 110, 400], [2.2, 110, 400], [3.4, 290, 560], [3.8, 290, 560]];
    const now = EP.path(lt, drag, true);
    if (now) EP.drawFingers(ctx, { fingers: [now], trail: EP.trailOf((tt) => EP.path(tt, drag, true) || now, lt, 12, 0.025) }, pal);
    dot(ctx, EP.path(lt, [[0.5, w - 80, by + 120], [0.95, 16 + bw / 2, by + 25], [1.5, 16 + bw / 2, by + 25]]));
    dot(ctx, EP.path(lt, [[4.5, 300, 500], [4.95, w - 16 - bw / 2, by + 25], [5.4, w - 16 - bw / 2, by + 25]]));
  };

  // ---- Gamepad (GamepadLayout.kt, the Xbox preset): sticks and d-pad left, face buttons right, always sideways. ----
  S.gamepad = (ctx, w, h, pal, lt) => {
    const X = (f) => f * w, Y = (f) => f * h;
    const idle = pal.off;
    const stickAt = (cx, cy, mx, my, held) => {
      ctx.beginPath();
      ctx.arc(cx, cy, 48, 0, Math.PI * 2);
      ctx.fillStyle = rgba(idle);
      ctx.fill();
      ctx.beginPath();
      ctx.arc(cx + mx, cy + my, 26, 0, Math.PI * 2);
      ctx.fillStyle = rgba(held ? pal.accent : pal.card);
      ctx.fill();
    };
    const a = lt * 2.2;
    const thumb = [X(0.13) + 14 * Math.cos(a), Y(0.46) + 14 * Math.sin(a)];
    stickAt(X(0.13), Y(0.46), 14 * Math.cos(a), 14 * Math.sin(a), true);
    stickAt(X(0.7), Y(0.78), 0, 0, false);
    // D-pad: four cells in a cross.
    const dx = X(0.3), dy = Y(0.78), cs = 32;
    [[0, -1], [0, 1], [-1, 0], [1, 0]].forEach(([ux, uy]) => fillRR(ctx, dx + ux * cs - cs / 2, dy + uy * cs - cs / 2, cs, cs, 8, idle));
    fillRR(ctx, dx - cs / 2, dy - cs / 2, cs, cs, 4, idle);
    const face = [['A', 0.84, 0.58], ['B', 0.92, 0.46], ['X', 0.76, 0.46], ['Y', 0.84, 0.34]];
    const press = { A: tri(lt, 1.0, 0.4), X: tri(lt, 1.7, 0.4), B: tri(lt, 2.2, 0.4), Y: tri(lt, 3.3, 0.4) };
    face.forEach(([n, fx, fy]) => {
      ctx.beginPath();
      ctx.arc(X(fx), Y(fy), 28, 0, Math.PI * 2);
      ctx.fillStyle = rgba(press[n] > 0.3 ? pal.accent : idle);
      ctx.fill();
      textMid(ctx, n, X(fx), Y(fy), { size: 17, w: 'Bold', color: press[n] > 0.3 ? pal.onAccent : pal.ink, align: 'center' });
    });
    [['LT', 0.1, 0.12], ['LB', 0.24, 0.12], ['RB', 0.76, 0.12], ['RT', 0.9, 0.12]].forEach(([n, fx, fy]) => {
      fillRR(ctx, X(fx) - 36, Y(fy) - 18, 72, 36, 12, idle);
      textMid(ctx, n, X(fx), Y(fy), { size: 14, w: 'Bold', color: pal.ink, align: 'center' });
    });
    [['Back', 0.44, 0.92], ['Start', 0.56, 0.92]].forEach(([n, fx, fy]) => {
      fillRR(ctx, X(fx) - 34, Y(fy) - 15, 68, 30, 15, idle);
      textMid(ctx, n, X(fx), Y(fy), { size: 13, w: 'Bold', color: pal.ink, align: 'center' });
    });
    dot(ctx, EP.path(lt, [[0.4, ...thumb], [4.4, ...thumb]]));
    const at = (n) => [X(face.find((q) => q[0] === n)[1]), Y(face.find((q) => q[0] === n)[2])];
    dot(ctx, EP.path(lt, [[0.8, ...at('A')], [1.2, ...at('A')], [1.6, ...at('X')], [1.9, ...at('X')], [2.1, ...at('B')], [2.4, ...at('B')], [3.2, ...at('Y')], [3.5, ...at('Y')]]));
  };

  // ---- PIN (PinScreen.kt, PinEntry.kt): six dots, then a phone-style keypad of cards. ----
  S.pin = (ctx, w, h, pal, lt) => {
    const cx = w / 2;
    icon(ctx, 'lock', cx, 120, 36, pal.accent);
    text(ctx, 'Enter the PIN', cx, 184, { size: 24, w: 'Bold', color: pal.ink, align: 'center' });
    text(ctx, 'Set in Edgepad on your laptop', cx, 212, { size: 15, color: pal.dim, align: 'center' });
    const taps = [0.9, 1.5, 2.1, 2.7, 3.3, 3.9];
    const filled = taps.filter((t) => lt > t).length;
    for (let i = 0; i < 6; i++) {
      const x = cx + (i - 2.5) * 28;
      ctx.beginPath();
      ctx.arc(x, 258, 7, 0, Math.PI * 2);
      if (i < filled) {
        ctx.fillStyle = rgba(pal.ink);
        ctx.fill();
      } else {
        ctx.lineWidth = 1.5;
        ctx.strokeStyle = rgba(pal.dim);
        ctx.stroke();
      }
    }
    const kw = 84, kh = 56, kx = cx - (3 * kw + 16) / 2, ky = 306;
    const digits = [1, 2, 3, 4, 5, 6, 7, 8, 9, null, 0, 'Delete'];
    const centres = {};
    digits.forEach((d, i) => {
      if (d === null) return;
      const x = kx + (i % 3) * (kw + 8), y = ky + Math.floor(i / 3) * (kh + 8);
      fillRR(ctx, x, y, kw, kh, 14, pal.card);
      const hot = taps.some((t, n) => [4, 8, 2, 6, 1, 5][n] === d && tri(lt, t - 0.05, 0.3));
      if (hot) fillRR(ctx, x, y, kw, kh, 14, pal.faint);
      textMid(ctx, String(d), x + kw / 2, y + kh / 2, { size: d === 'Delete' ? 17 : 24, w: 'Bold', color: pal.ink, align: 'center' });
      centres[d] = [x + kw / 2, y + kh / 2];
    });
    textMid(ctx, 'Cancel', cx, ky + 4 * (kh + 8) + 28, { size: 17, w: 'Bold', color: pal.accent, align: 'center' });
    const keys = [];
    [4, 8, 2, 6, 1, 5].forEach((d, n) => keys.push([taps[n] - 0.1, ...centres[d]], [taps[n] + 0.1, ...centres[d]]));
    dot(ctx, EP.path(lt, keys));
  };

  // ---- Corners (CornersScreen.kt): the Split corners switch, then the corners (or the eight halves) and what each holds. ----
  const CORNER_NAMES = ['Top left', 'Top right', 'Bottom right', 'Bottom left'];
  const EDGES = ['left edge', 'top edge', 'top edge', 'right edge', 'right edge', 'bottom edge', 'bottom edge', 'left edge'];
  const KIND_NAMES = ['Off', 'Volume', 'Brightness', 'Zoom', 'Media scrub', 'App switcher', 'Mic level', 'Refresh rate'];
  const HELD = ['Volume', 'Brightness', 'Zoom', 'Media scrub'];
  const SLOT_HELD = ['Volume', 'App switcher', 'Mic level', 'Brightness', 'Zoom', 'Refresh rate', 'Zoom', 'Media scrub'];
  // The corner's icon: a 20 dp faint tile with an 8 dp mark at its corner, or at the middle of its edge for a half.
  function cornerIcon(ctx, pal, x, cy, i, split, on) {
    fillRR(ctx, x, cy - 10, 20, 20, 6, pal.faint);
    const corner = [[0, 0], [1, 0], [1, 1], [0, 1]];
    const edge = [[0, 0.5], [0.5, 0], [0.5, 0], [1, 0.5], [1, 0.5], [0.5, 1], [0.5, 1], [0, 0.5]];
    const [fx, fy] = split ? edge[i] : corner[i];
    fillRR(ctx, x + 2 + fx * 8, cy - 8 + fy * 8, 8, 8, 2, on ? pal.accent : pal.dim);
  }
  // lt: the switch is flipped at 1.0 and the page rebuilds with eight rows. o.split starts it on; o.pick picks a row at 1.0.
  S.corners = (ctx, w, h, pal, lt, o = {}) => {
    const flip = o.split ? 1 : ease((lt - 1.0) / 0.2);
    const split = flip > 0.5;
    const picked = o.pick !== undefined && lt > 1.05 ? o.pick : 0;
    ctx.save();
    ctx.translate(0, -(o.scroll || 0) * ease((lt - (o.scrollAt ?? 99)) / 0.8));
    let y = TOP + 48;
    const [ty] = card(ctx, w, pal, y, 1);
    text(ctx, 'Split corners', 32, ty + 5.5, { size: 16, color: pal.ink });
    toggle(ctx, pal, w - 32 - 60, ty, flip);
    y = section(ctx, pal, 'Pick a corner', y + 52);
    const count = split ? 8 : 4;
    const ys = card(ctx, w, pal, y, count);
    for (let i = 0; i < count; i++) {
      const on = i === picked;
      cornerIcon(ctx, pal, 32, ys[i], i, split, on);
      const name = split ? `${CORNER_NAMES[Math.floor(i / 2)]} · ${EDGES[i]}` : CORNER_NAMES[i];
      text(ctx, name, 66, ys[i] + 5.5, { size: 16, w: on ? 'Bold' : 'Regular', color: pal.ink });
      text(ctx, split ? SLOT_HELD[i] : HELD[i], w - 32, ys[i] + 5, { size: 15, color: on ? pal.accent : pal.dim, align: 'right' });
      const p = o.pick === i ? tri(lt, 0.95, 0.35) : 0;
      if (p) fillRR(ctx, 16, ys[i] - 26, w - 32, 52, 0, pal.faint, p * 0.5);
    }
    const name = split ? `${CORNER_NAMES[Math.floor(picked / 2)]} · ${EDGES[picked]}` : CORNER_NAMES[picked];
    y = section(ctx, pal, `Assign to ${name.toLowerCase()}`, y + count * 53 - 1);
    const held = split ? SLOT_HELD[picked] : HELD[picked];
    const cs = card(ctx, w, pal, y, KIND_NAMES.length, 48);
    KIND_NAMES.forEach((s, i) => {
      textMid(ctx, s, 32, cs[i], { size: 16, color: pal.ink });
      if (s === held) check(ctx, pal, w - 42, cs[i]);
    });
    ctx.restore();
    fillRR(ctx, 0, 0, w, TOP + 48, 0, pal.bg);
    bar(ctx, w, pal, 'Corners', 'Settings', null);
    if (!o.split) dot(ctx, EP.path(lt, [[0.45, w - 120, ty + 140], [0.95, w - 47, ty], [1.25, w - 47, ty]]));
    if (o.pick !== undefined) dot(ctx, EP.path(lt, [[0.45, w / 2, ys[o.pick] + 120], [0.95, w / 2 - 40, ys[o.pick]], [1.3, w / 2 - 40, ys[o.pick]]]));
  };

  // ---- Trackpad settings (GestureScreen.kt): pointer feel, then every three- and four-finger gesture and its action. ----
  const GESTURES = [
    ['3 fingers', [['Tap', 'Search'], ['Swipe left', 'Desktop left'], ['Swipe right', 'Desktop right'], ['Swipe up', 'Task view'], ['Swipe down', 'Show desktop']]],
    ['4 fingers', [['Tap', 'Notifications'], ['Swipe left', 'App switcher'], ['Swipe right', 'App switcher'], ['Swipe up', 'Task view'], ['Swipe down', 'Show desktop']]],
  ];
  // Ui.slider: the label and value over a 4 dp track, a 20 dp thumb, and the step ticks under it.
  function slider(ctx, w, pal, y, label, value, f) {
    text(ctx, label, 32, y + 24, { size: 16, color: pal.ink });
    text(ctx, value, w - 32, y + 24, { size: 15, color: pal.dim, align: 'right' });
    const x0 = 42, x1 = w - 42, ty = y + 44;
    fillRR(ctx, x0, ty - 2, x1 - x0, 4, 2, pal.off);
    fillRR(ctx, x0, ty - 2, (x1 - x0) * f, 4, 2, pal.accent);
    ctx.fillStyle = rgba(pal.dim, 0.5);
    for (let i = 0; i <= 26; i++) {
      ctx.beginPath();
      ctx.arc(lerp(x0, x1, i / 26), ty + 18, 1.5, 0, Math.PI * 2);
      ctx.fill();
    }
    ctx.beginPath();
    ctx.arc(x0 + (x1 - x0) * f, ty, 10, 0, Math.PI * 2);
    ctx.fillStyle = '#FFFFFF';
    ctx.fill();
  }
  // lt: 1.2-2.2 the page scrolls to the gestures; 2.8 "Swipe left" under three fingers is tapped and turns accent.
  S.gestures = (ctx, w, h, pal, lt) => {
    ctx.save();
    ctx.translate(0, -230 * EP.easeInOut((lt - 1.2) / 1.0));
    let y = section(ctx, pal, 'Pointer', TOP + 48 - 24);
    fillRR(ctx, 16, y, w - 32, 76 * 2 + 52 + 2, 14, pal.card);
    slider(ctx, w, pal, y, 'Pointer speed', '×1.0', 0.24);
    ctx.fillStyle = rgba(pal.line);
    ctx.fillRect(32, y + 76, w - 64, 1);
    slider(ctx, w, pal, y + 77, 'Scroll speed', '×1.0', 0.24);
    ctx.fillRect(32, y + 153, w - 64, 1);
    text(ctx, 'Natural scrolling', 32, y + 154 + 26 + 5.5, { size: 16, color: pal.ink });
    toggle(ctx, pal, w - 92, y + 154 + 26, 1);
    y += 206;
    const open = ease((lt - 2.85) / 0.2);
    GESTURES.forEach(([title, rows], gi) => {
      y = section(ctx, pal, title, y);
      const ys = card(ctx, w, pal, y, rows.length, 48);
      rows.forEach(([g, a], i) => {
        const hot = gi === 0 && i === 1;
        textMid(ctx, g, 32, ys[i], { size: 16, color: pal.ink });
        textMid(ctx, a, w - 32, ys[i], { size: 15, color: hot && open > 0.5 ? pal.accent : pal.dim, align: 'right' });
        if (hot) {
          const p = tri(lt, 2.75, 0.35);
          if (p) fillRR(ctx, 16, ys[i] - 24, w - 32, 48, 0, pal.faint, p * 0.6);
        }
      });
      y += rows.length * 49 - 1;
    });
    ctx.restore();
    fillRR(ctx, 0, 0, w, TOP + 48, 0, pal.bg);
    bar(ctx, w, pal, 'Trackpad', 'Settings', null);
    const rowY = TOP + 48 - 24 + 45 + 206 + 45 + 48 + 24 - 230;
    dot(ctx, EP.path(lt, [[2.3, w / 2, rowY + 150], [2.75, w / 2, rowY], [3.1, w / 2, rowY]]));
  };

  // ---- Media layout (MediaLayoutScreen.kt, LayoutCanvas.kt): the pieces on a dot grid, dragged anywhere. ----
  // lt: 0.9 the now-playing card lifts (accent) and is dragged up to 0.4 of the height by 2.2, then dropped.
  S.mediaLayout = (ctx, w, h, pal, lt) => {
    ctx.fillStyle = rgba(pal.line);
    for (let y = TOP + 48; y < h; y += 12) for (let x = 6; x < w; x += 12) ctx.fillRect(x - 1, y - 1, 2, 2);
    ctx.fillStyle = rgba(pal.faint);
    ctx.fillRect(w / 2 - 0.5, TOP + 48, 1, h);
    ctx.fillRect(0, (h + TOP + 48) / 2, w, 1);
    const lift = held(lt, 0.9, 2.25);
    const k = EP.easeInOut((lt - 1.05) / 1.1);
    const pieces = [['Now playing', 280, 52, 0.5, lerp(0.78, 0.4, k)], ['Prev · play · next', 52 * 2 + 48, 52, 0.5, 0.9]];
    pieces.forEach(([name, pw, ph, fx, fy], i) => {
      const x = fx * w - pw / 2, y = fy * h - ph / 2;
      const on = i === 0 && lift > 0.5;
      if (on) {
        ctx.save();
        ctx.shadowColor = 'rgba(0,0,0,0.35)';
        ctx.shadowBlur = 12;
        ctx.shadowOffsetY = 6;
      }
      if (!on && pal.k > 0.5) fillRR(ctx, x, y + 1, pw, ph, 12, pal.line);
      fillRR(ctx, x, y, pw, ph, 12, on ? pal.accent : pal.card);
      if (on) ctx.restore();
      textMid(ctx, name, fx * w, fy * h, { size: 13, w: 'Bold', color: on ? pal.onAccent : pal.dim, align: 'center' });
    });
    fillRR(ctx, 0, 0, w, TOP + 48, 0, pal.bg);
    bar(ctx, w, pal, 'Media layout', 'Settings', null);
    text(ctx, 'Drag a piece to move it. It snaps to the grid and the centre lines.', w / 2, h - BOTTOM - 8, { size: 12, color: pal.dim, align: 'center' });
    dot(ctx, EP.path(lt, [[0.9, w / 2, 0.78 * h], [1.05, w / 2, 0.78 * h], [2.15, w / 2, 0.4 * h], [2.3, w / 2, 0.4 * h]], true));
  };

  // The phone's status bar over every screen but the surface, which runs edge to edge.
  EP.statusBar = (ctx, w, pal) => {
    text(ctx, '9:41', 26, 21, { size: 14, w: 'Bold', color: pal.ink });
    fillRR(ctx, w - 50, 11, 24, 12, 3.5, pal.ink, 0.9);
    fillRR(ctx, w - 25, 15, 2, 4, 1, pal.ink, 0.9);
    text(ctx, '82%', w - 56, 21, { size: 12, w: 'Bold', color: pal.ink, align: 'right' });
  };
})();
