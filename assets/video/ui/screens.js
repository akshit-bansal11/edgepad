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

  // A finger travelling through keys [[t, x, y], ...], easing each leg; it fades in before the first and out after the last.
  EP.path = (lt, keys) => {
    const first = keys[0], last = keys[keys.length - 1];
    const a = ease((lt - (first[0] - 0.25)) / 0.25) * (1 - ease((lt - last[0]) / 0.3));
    if (a <= 0) return null;
    let i = 0;
    while (i < keys.length - 2 && lt > keys[i + 1][0]) i++;
    const [t0, x0, y0] = keys[i], [t1, x1, y1] = keys[i + 1];
    const k = ease((lt - t0) / Math.max(t1 - t0, 1e-6));
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
    dot(ctx, EP.path(lt, [[0.7, 260, 560], [1.15, w / 2, TOP + 158], [1.9, w / 2, TOP + 158], [3.7, w / 2, by + 25], [4.4, w / 2, by + 25]]));
  };

  // ---- Deck (Deck.kt, DeckPage.kt): tiles with no fill, the picture over a bold label; Edit layout makes them draggable. ----
  const MACROS = [['Browser', 'globe'], ['Notes', 'note'], ['Calculator', 'calc'], ['Docs', 'docs'], ['Terminal', 'terminal'], ['Photos', 'image']];
  const TILE_GAP = 8;
  // The rect of slot i in a cols x rows grid inside the area.
  function cell(i, cols, rows, a) {
    const cw = (a.w - (cols - 1) * TILE_GAP) / cols, ch = (a.h - (rows - 1) * TILE_GAP) / rows;
    return { x: a.x + (i % cols) * (cw + TILE_GAP), y: a.y + Math.floor(i / cols) * (ch + TILE_GAP), w: cw, h: ch };
  }
  function tile(ctx, pal, name, ic, r, press, lift, edit) {
    const cx = r.x + r.w / 2, cy = r.y + r.h / 2;
    ctx.save();
    if (lift) {
      ctx.translate(cx, cy);
      ctx.scale(1 + 0.05 * lift, 1 + 0.05 * lift);
      ctx.translate(-cx, -cy);
      ctx.shadowColor = 'rgba(0,0,0,0.3)';
      ctx.shadowBlur = 18 * lift;
      ctx.shadowOffsetY = 6 * lift;
      fillRR(ctx, r.x, r.y, r.w, r.h, 16, pal.card);
      ctx.shadowColor = 'transparent';
    }
    if (press > 0) fillRR(ctx, r.x, r.y, r.w, r.h, 16, pal.faint, press);
    if (edit > 0) {
      ctx.setLineDash([4, 3]);
      ctx.lineWidth = 1;
      ctx.strokeStyle = rgba(pal.dim, edit);
      rr(ctx, r.x + 0.5, r.y + 0.5, r.w - 1, r.h - 1, 16);
      ctx.stroke();
      ctx.setLineDash([]);
    }
    const label = clamp(Math.min(r.w, r.h) * 0.13, 17, 22);
    const pic = Math.min(64, Math.min(r.w, r.h) * 0.42);
    const top = cy - (pic + 10 + label) / 2;
    icon(ctx, ic, cx, top + pic / 2, pic, pal.ink);
    text(ctx, name, cx, top + pic + 10 + label * 0.8, { size: label, w: 'Bold', color: pal.ink, align: 'center' });
    ctx.restore();
  }

  S.macros = (ctx, w, h, pal, lt) => {
    const edit = ease((lt - 2.3) / 0.35) * (1 - ease((lt - 7.4) / 0.35));
    const g = ease((lt - 6.0) / 0.8); // 0: 3 x 2 (3 rows), 1: 2 x 3
    const pickerH = 96 * edit;
    const area = { x: 16, y: TOP + 56, w: w - 32, h: h - TOP - 56 - BOTTOM - pickerH };
    const slot = (i) => {
      const a = cell(i, 2, 3, area), b = cell(i, 3, 2, area);
      return { x: lerp(a.x, b.x, g), y: lerp(a.y, b.y, g), w: lerp(a.w, b.w, g), h: lerp(a.h, b.h, g) };
    };
    const chip = bar(ctx, w, pal, 'Macros', 'Surface', edit > 0.5 ? 'Done' : 'Edit layout', tri(lt, 2.3) + tri(lt, 7.4));
    // Notes (1) is dragged onto Browser (0): they trade places.
    const drag = ease((lt - 3.8) / 1.0), lifted = held(lt, 3.5, 4.9);
    const at = (i) => (i === 1 ? lerp(1, 0, drag) : i === 0 ? lerp(0, 1, ease((lt - 4.3) / 0.6)) : i);
    const from = (i) => {
      const k = at(i), lo = Math.floor(k), a = slot(lo), b = slot(Math.min(5, lo + 1)), f = k - lo;
      return { x: lerp(a.x, b.x, f), y: lerp(a.y, b.y, f), w: lerp(a.w, b.w, f), h: lerp(a.h, b.h, f) };
    };
    const presses = { 0: tri(lt, 1.0) * (1 - edit), 2: tri(lt, 8.5) };
    MACROS.forEach(([name, ic], i) => {
      if (i !== 1) tile(ctx, pal, name, ic, from(i), presses[i] || 0, 0, edit);
    });
    tile(ctx, pal, MACROS[1][0], MACROS[1][1], from(1), 0, lifted, edit);
    // The grid picker (Grid, rows x columns) sits under the tiles while editing.
    if (edit > 0) {
      ctx.save();
      ctx.globalAlpha *= edit;
      const py = h - BOTTOM - pickerH + 6;
      text(ctx, 'Grid, rows × columns', 20, py + 14, { size: 13, w: 'Bold', color: pal.dim });
      const tw = w - 32;
      fillRR(ctx, 16, py + 26, tw, 40, 12, pal.faint);
      const seg3 = (tw - 8) / 3;
      fillRR(ctx, 20 + seg3 * (g > 0.5 ? 2 : 0), py + 30, seg3, 32, 9, pal.dark ? pal.off : pal.card);
      ['Automatic', '3 × 2', '2 × 3'].forEach((s, i) => textMid(ctx, s, 20 + seg3 * (i + 0.5), py + 46, { size: 13, color: pal.ink, align: 'center' }));
      ctx.restore();
    }
    const A = (i) => {
      const r = from(i);
      return [r.x + r.w / 2, r.y + r.h / 2];
    };
    const [bx, by] = A(0), [cx, cy] = A(2);
    const mid = (r) => [r.x + r.w / 2, r.y + r.h / 2];
    const notes = mid(slot(1)), first = mid(slot(0));
    const chipX = w - 52, chipY = TOP + 24; // Edit layout / Done sit at the bar's far end
    const picker = [20 + ((w - 40) / 3) * 2.5, h - BOTTOM - 96 + 6 + 46]; // the "2 × 3" segment
    dot(ctx, EP.path(lt, [
      [0.5, bx + 60, by + 80], [0.95, bx, by], [1.25, bx, by],
      [2.1, chipX, chipY], [2.5, chipX, chipY],
      [3.4, ...notes], [3.8, ...notes], [4.8, ...first], [5.2, ...first],
      [5.9, ...picker], [6.3, ...picker],
      [7.2, chipX, chipY], [7.5, chipX, chipY],
      [8.4, cx, cy], [8.8, cx, cy],
    ]));
  };

  // ---- Controls (ControlsScreen.kt): keyboard, gamepad, capture and annotate as a deck of tiles. ----
  S.controls = (ctx, w, h, pal, lt) => {
    bar(ctx, w, pal, 'Controls', 'Surface', null);
    const area = { x: 16, y: TOP + 56, w: w - 32, h: h - TOP - 56 - BOTTOM };
    [['Keyboard', 'keyboard'], ['Gamepad', 'gamepad'], ['Capture', 'scan'], ['Annotate', 'pen']].forEach(([name, ic], i) => {
      tile(ctx, pal, name, ic, cell(i, 2, 2, area), i === 0 ? tri(lt, 2.2, 0.4) : 0, 0, 0);
    });
    const r = cell(0, 2, 2, area);
    dot(ctx, EP.path(lt, [[0.6, w - 60, h - 140], [1.9, r.x + r.w / 2, r.y + r.h / 2], [2.5, r.x + r.w / 2, r.y + r.h / 2]]));
  };

  // ---- Keyboard (KeyboardScreen.kt): six rows of keys in Edgepad's own layout, modifiers latch. ----
  const ROWS = [
    [['Esc', 1.5], ...'1 2 3 4 5 6 7 8 9 10 11 12'.split(' ').map((n) => ['F' + n, 1]), ['Del', 1.5]],
    [['`', 1], ...'1234567890-='.split('').map((c) => [c, 1]), ['Bksp', 2]],
    [['Tab', 1.5], ...'QWERTYUIOP[]'.split('').map((c) => [c, 1]), ['\\', 1.5]],
    [['Caps', 1.75], ...'ASDFGHJKL;\''.split('').map((c) => [c, 1]), ['Enter', 2.25]],
    [['Shift', 2.25], ...'ZXCVBNM,./'.split('').map((c) => [c, 1]), ['Shift', 2.75]],
    [['Ctrl', 1.25], ['Win', 1.25], ['Alt', 1.25], ['Space', 6.25], ['Alt', 1.25], ['Menu', 1.25], ['Ctrl', 1.25]],
  ];
  S.keyboard = (ctx, w, h, pal, lt) => {
    const padX = 24, padY = 14, gap = 6;
    const rowH = (h - 2 * padY - 5 * gap) / 6;
    const latch = ease((lt - 1.2) / 0.15) * (1 - ease((lt - 3.4) / 0.2)); // Ctrl held for Ctrl+C
    const cPress = tri(lt, 2.2, 0.35), vPress = tri(lt, 3.6, 0.35);
    const hit = {};
    ROWS.forEach((row, ri) => {
      const avail = w - 2 * padX - (row.length - 1) * gap;
      let x = padX;
      row.forEach(([label, units], ki) => {
        const kw = (avail * units) / 15, y = padY + ri * (rowH + gap);
        const ctrl = label === 'Ctrl' && ki === 0 && ri === 5;
        const k = ctrl ? latch : label === 'C' ? cPress : label === 'V' ? vPress : 0;
        fillRR(ctx, x, y, kw, rowH, 10, pal.card);
        if (k > 0) fillRR(ctx, x, y, kw, rowH, 10, pal.accent, k);
        textMid(ctx, label, x + kw / 2, y + rowH / 2, { size: 14, w: 'Bold', color: k > 0.5 ? pal.onAccent : pal.ink, align: 'center' });
        hit[ri + label + (ctrl ? 'L' : '')] = [x + kw / 2, y + rowH / 2];
        x += kw + gap;
      });
    });
    const ctrl = hit['5CtrlL'];
    dot(ctx, EP.path(lt, [[0.6, 120, h - 24], [1.1, ...ctrl], [1.5, ...ctrl], [2.1, ...hit['4C']], [2.5, ...hit['4C']], [3.5, ...hit['4V']], [3.9, ...hit['4V']]]));
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
    const sel = signing > 0.5 || lt < 1.4 ? 0 : 3; // Pen, then Arrow, and Pen again to sign
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
      if (i === (lt > 2.8 ? 2 : 0)) {
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
    ctx.lineCap = ctx.lineJoin = 'round';
    ctx.translate(16, py);
    const draw = (pts, p, colour, width) => {
      if (p <= 0) return null;
      ctx.strokeStyle = colour;
      ctx.lineWidth = width;
      return poly(ctx, pts, p);
    };
    const base = ctx.globalAlpha;
    ctx.globalAlpha = base * (1 - signing);
    draw(ellipsePts(pw * 0.38, ph * 0.5, pw * 0.2, ph * 0.28), seg(lt, 0.8, 1.7), '#FF453A', 4);
    const arrow = [[pw * 0.62, ph * 0.22], [pw * 0.84, ph * 0.5]];
    const tip = draw(arrow, seg(lt, 2.4, 3.1), '#0A84FF', 4);
    if (tip && lt > 3.05) {
      ctx.beginPath();
      ctx.moveTo(arrow[1][0] - 14, arrow[1][1] - 4);
      ctx.lineTo(arrow[1][0], arrow[1][1]);
      ctx.lineTo(arrow[1][0] - 6, arrow[1][1] - 15);
      ctx.stroke();
    }
    // The signature: one loop and a flourish, drawn on the same pad with nothing on the laptop.
    const sig = [[0.16, 0.62], [0.22, 0.3], [0.28, 0.7], [0.34, 0.35], [0.4, 0.62], [0.47, 0.5], [0.54, 0.64], [0.6, 0.38], [0.66, 0.6], [0.78, 0.55], [0.86, 0.5]].map(([a, b]) => [a * pw, b * ph]);
    const sp = seg(lt, 4.5, 6.0);
    ctx.globalAlpha = base * signing;
    draw(sig, sp, rgba(pal.ink), 3);
    ctx.restore();
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
    dot(ctx, EP.path(lt, keys));
    dot(ctx, EP.path(lt, [[2.4, ...pad(0.62, 0.22 / 1)], [3.1, ...pad(0.84, 0.5)]]));
    dot(ctx, EP.path(lt, [[3.6, w - 16 - 51 + 35.5, cy], [3.95, w - 16 - 51 + 35.5, cy]]));
    const sk = [];
    for (let k = 0; k <= 10; k++) sk.push([4.5 + k * 0.15, ...sPos(k / 10)]);
    dot(ctx, EP.path(lt, sk));
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
    const now = EP.path(lt, drag);
    if (now) EP.drawFingers(ctx, { fingers: [now], trail: EP.trailOf((tt) => EP.path(tt, drag) || now, lt, 12, 0.025) }, pal);
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
    // The label this video adds: the gamepad is still in development.
    const tw = measure(ctx, 'In development', 15, 'Bold') + 32;
    fillRR(ctx, (w - tw) / 2, 44, tw, 34, 17, pal.accentSoft);
    textMid(ctx, 'In development', w / 2, 61, { size: 15, w: 'Bold', color: pal.accent, align: 'center' });
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
})();
