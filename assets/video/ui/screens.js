// The app's other screens, ported from screens/*.kt and drawn at 1 unit = 1 dp. Each is a pure function of its local time `lt`.
(function () {
  const { clamp, lerp, ease, seg, rgba, text, textMid, fillRR, rr, icon, measure } = EP;
  const TOP = 30; // the status bar's room: the app draws edge to edge and pads by the system bars
  const BOTTOM = 20;
  const S = {}; // screen name -> draw(ctx, w, h, pal, lt, o)
  EP.screens = S;

  // A press: 0 -> 1 -> 0 over d seconds from t0.
  const tri = (lt, t0, d = 0.32) => {
    const x = (lt - t0) / d;
    return x < 0 || x > 1 ? 0 : Math.sin(x * Math.PI);
  };
  // A held press that stays down from t0 to t1.
  const held = (lt, t0, t1) => ease((lt - t0) / 0.12) * (1 - ease((lt - t1) / 0.12));
  // Whether lt is inside any of the [from, to) spans.
  const within = (lt, spans) => spans.some(([a, b]) => lt >= a && lt < b);

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
  // A tap: the finger comes in from (fx, fy) by t - 0.35, rests on (x, y) from t - 0.1 to t + 0.2.
  const tap = (ctx, lt, t, x, y, fx = x + 40, fy = y + 70) => dot(ctx, EP.path(lt, [[t - 0.4, fx, fy], [t - 0.08, x, y], [t + 0.2, x, y]]));

  // ---- text in a column of a given width, the way a TextView wraps it (Type.LEADING 1.4 on the face's own line) ----
  const LEAD = 1.64;
  function wrapLines(ctx, s, maxW, size, w = 'Regular') {
    const out = [];
    let line = '';
    for (const word of s.split(' ')) {
      const next = line ? line + ' ' + word : word;
      if (line && measure(ctx, next, size, w) > maxW) {
        out.push(line);
        line = word;
      } else line = next;
    }
    out.push(line);
    return out;
  }
  // Draws the wrapped text with its top at y; returns the y under it.
  function para(ctx, s, x, y, maxW, size, color, w = 'Regular', align = 'left') {
    const ls = wrapLines(ctx, s, maxW, size, w);
    ls.forEach((l, i) => text(ctx, l, align === 'center' ? x + maxW / 2 : x, y + size + i * size * LEAD, { size, w, color, align }));
    return y + ls.length * size * LEAD;
  }

  // Ui.bar: an accent chevron and the back label at the start, the title centred, actions at the far end. `gear` puts the
  // Glyph GEAR button (a 48 dp target) there; `action` is the film's own pill and is no longer used by the app's screens.
  function bar(ctx, w, pal, title, back, action, actionPress = 0, gear = false) {
    const y = TOP + 24;
    if (back) {
      icon(ctx, 'chevron_left', 17, y, 24, pal.accent);
      text(ctx, back, 30, y + 6, { size: 17, w: 'Bold', color: pal.accent });
    }
    if (back && title) text(ctx, title, w / 2, y + 6, { size: 17, w: 'Bold', color: pal.ink, align: 'center' });
    let chip = null;
    if (action) {
      const cw = measure(ctx, action, 15, 'Bold') + 28;
      chip = { x: w - 8 - cw, y: y - 16, w: cw, h: 32 };
      fillRR(ctx, chip.x, chip.y, chip.w, chip.h, 16, actionPress > 0 ? pal.line : pal.faint);
      textMid(ctx, action, chip.x + cw / 2, y, { size: 15, w: 'Bold', color: pal.ink, align: 'center' });
    }
    if (gear) {
      if (actionPress > 0) {
        ctx.fillStyle = rgba(pal.faint, actionPress);
        ctx.beginPath();
        ctx.arc(w - 28, y, 24, 0, Math.PI * 2);
        ctx.fill();
      }
      icon(ctx, 'settings', w - 28, y, 24, pal.accent);
      chip = { x: w - 52, y: y - 24, w: 48, h: 48 };
    }
    return chip;
  }

  // ---- Page.kt pieces ----
  // A section header: Type.SMALL bold dim, 24 dp above, 8 below; returns the y a card starts at.
  function section(ctx, pal, label, y) {
    text(ctx, label, 32, y + 24 + 13, { size: 13, w: 'Bold', color: pal.dim });
    return y + 24 + 13 + 8 + 4;
  }
  // A grouped card of rows (Column.card): returns the centre y of each row.
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
  // Glyph.Shape.CHECK in the accent.
  const check = (ctx, pal, x, y, a = 1) => icon(ctx, 'check', x, y, 24, pal.accent, a);
  // A toggle row inside a card: the label (Type.BODY) at the card's text edge, the switch at the far side.
  function toggleRow(ctx, w, pal, cy, label, on) {
    textMid(ctx, label, 32, cy, { size: 16, color: pal.ink });
    toggle(ctx, pal, w - 32 - 60, cy, on);
  }
  // Ui.chip: a 32 dp pill in a 48 dp target; x is the left edge, returns the right edge.
  function chip(ctx, pal, label, x, cy, color) {
    const cw = measure(ctx, label, 15, 'Bold') + 32;
    fillRR(ctx, x, cy - 16, cw, 32, 16, pal.faint);
    textMid(ctx, label, x + cw / 2, cy, { size: 15, w: 'Bold', color: color || pal.ink, align: 'center' });
    return x + cw;
  }
  // Ui.segmented: equal options on a faint track, the chosen one on a raised thumb that slides (sel may be fractional).
  // Right edge at `right`, centred on cy.
  function segmented(ctx, pal, right, cy, options, sel) {
    const ow = Math.max(...options.map((o) => measure(ctx, o, 13, 'Bold'))) + 28;
    const tw = options.length * ow + 4, x = right - tw;
    fillRR(ctx, x, cy - 16, tw, 32, 9, pal.faint);
    const dark = pal.k < 0.5;
    const tx = x + 2 + sel * ow;
    if (!dark) fillRR(ctx, tx, cy - 14 + 1, ow, 28, 7, pal.line);
    fillRR(ctx, tx, cy - 14, ow, 28, 7, dark ? pal.off : pal.card);
    options.forEach((o, i) => textMid(ctx, o, x + 2 + i * ow + ow / 2, cy, { size: 13, w: Math.abs(i - sel) < 0.5 ? 'Bold' : 'Regular', color: pal.ink, align: 'center' }));
    return x;
  }
  EP.segmented = segmented;
  // A filled/outlined button (Ui.button): 50 dp tall, 14 radius, bold Type.LABEL.
  function button(ctx, pal, label, x, y, bw, style, a = 1, press = 0) {
    ctx.save();
    ctx.globalAlpha *= a;
    const fill = style === 'FILLED' ? pal.accent : style === 'OUTLINED' ? pal.accentSoft : null;
    if (fill) fillRR(ctx, x, y, bw, 50, 14, fill);
    if (press > 0) fillRR(ctx, x, y, bw, 50, 14, pal.faint, press * 0.6);
    textMid(ctx, label, x + bw / 2, y + 25, { size: 17, w: 'Bold', color: style === 'FILLED' ? pal.onAccent : pal.accent, align: 'center' });
    ctx.restore();
  }
  // The large title under a bar (Column.largeTitle), 32 Black, 4 dp in; returns the y under it.
  function largeTitle(ctx, pal, s, y) {
    text(ctx, s, 16 + 4, y + 32 * 0.95, { size: 32, w: 'Black', color: pal.ink, track: -0.01 });
    return y + 38 + 8;
  }
  // Ui.laptop: the 36 dp tile (accent when connected, faint when not) with the laptop glyph, then the name over a sub-line.
  // `row` is the top of a 64 dp row; returns the centre y.
  function laptopTile(ctx, pal, y, connected, name, sub, subColor, pulse) {
    const cy = y + 32;
    fillRR(ctx, 32, cy - 18, 36, 36, 10, connected ? pal.accent : pal.faint);
    icon(ctx, 'laptop', 50, cy, 24, connected ? pal.onAccent : pal.dim);
    text(ctx, name, 80, cy - 3, { size: 16, w: 'Bold', color: pal.ink });
    let sx = 80;
    if (pulse !== undefined) {
      // Pulse: a 6 dp dot in a 14 dp box, with a ring sent out while it is connecting.
      ctx.fillStyle = rgba(pal.accent);
      ctx.beginPath();
      ctx.arc(87, cy + 15, 3, 0, Math.PI * 2);
      ctx.fill();
      if (pulse > 0) {
        ctx.strokeStyle = rgba(pal.accent, 1 - pulse);
        ctx.lineWidth = 1.5;
        ctx.beginPath();
        ctx.arc(87, cy + 15, 3 + 4 * pulse, 0, Math.PI * 2);
        ctx.stroke();
      }
      sx = 94;
    }
    text(ctx, sub, sx, cy + 19, { size: 13, color: subColor });
    return cy;
  }

  // ---- Devices (PickerScreen.kt): the laptops, each with the rest of what can be done to it, and Open controls. ----
  // lt: 1.15 the first laptop is tapped, 1.3 Connecting…, 2.5 Connected, 2.9 its round trip, 4.3 Open controls.
  S.devices = (ctx, w, h, pal, lt) => {
    const y0 = TOP + 24;
    // The nav bar has no back link: refresh, Bluetooth settings and Settings sit at its far end.
    icon(ctx, 'refresh_cw', w - 124, y0, 24, pal.accent);
    icon(ctx, 'bluetooth', w - 76, y0, 24, pal.accent);
    icon(ctx, 'settings', w - 28, y0, 24, pal.accent);
    // The title with the mark beside it: 34 dp, 12 dp apart.
    const ty = TOP + 48;
    EP.mark(ctx, 20, ty + 2, 34, pal.k < 0.5);
    text(ctx, 'Devices', 20 + 34 + 12, ty + 32, { size: 32, w: 'Black', color: pal.ink, track: -0.01 });
    const connecting = ease((lt - 1.3) / 0.2), connected = ease((lt - 2.5) / 0.3);
    const isCon = connected > 0.5, isConnecting = !isCon && connecting > 0.5;
    const pressed = tri(lt, 1.2, 0.3);
    let y = section(ctx, pal, 'Laptops', ty + 38 + 8);
    // Two rows of 64, each with its line of chips, around a hairline.
    const rowA = y, rowB = y + 64 + 52 + 1;
    const total = 2 * (64 + 52) + 1;
    fillRR(ctx, 16, y, w - 32, total, 14, pal.card);
    if (pressed) fillRR(ctx, 16, rowA, w - 32, 64, 14, pal.faint, pressed * 0.8);
    ctx.fillStyle = rgba(pal.line);
    ctx.fillRect(32, rowB - 1, w - 64, 1);
    // Work laptop: remembered, then connecting, then connected over Wi-Fi.
    const statusA = isCon ? 'Connected via Wi-Fi' : isConnecting ? 'Connecting…' : 'Remembered';
    laptopTile(ctx, pal, rowA, isCon, 'Work laptop', statusA, isCon || isConnecting ? pal.accent : pal.dim, isCon ? 0 : isConnecting ? (lt * 1.6) % 1 : undefined);
    const rtt = lt > 2.9 ? '14 ms' : '';
    let ex = w - 32;
    if (rtt) {
      text(ctx, rtt, ex, rowA + 32 + 4, { size: 13, color: pal.dim, align: 'right' });
      ex -= measure(ctx, rtt, 13) + 8;
    }
    const tcw = measure(ctx, 'Wi-Fi', 13) + 16;
    fillRR(ctx, ex - tcw, rowA + 32 - 11, tcw, 22, 8, pal.faint);
    textMid(ctx, 'Wi-Fi', ex - tcw / 2, rowA + 32, { size: 13, color: pal.ink, align: 'center' });
    // The chips line: Connect, or the Active badge; the other ways to reach it; Disconnect; Forget.
    const cyA = rowA + 64 + 22;
    let cx = 32;
    if (!isCon && !isConnecting) cx = chip(ctx, pal, 'Connect', cx, cyA) + 8;
    if (isCon) {
      text(ctx, 'Active', cx, cyA + 5, { size: 13, color: pal.accent });
      cx += measure(ctx, 'Active', 13) + 8;
    }
    cx = chip(ctx, pal, 'Bluetooth', cx, cyA) + 8;
    if (isCon || isConnecting) cx = chip(ctx, pal, 'Disconnect', cx, cyA) + 8;
    chip(ctx, pal, 'Forget', cx, cyA, rgba(pal.danger));
    // Home PC: remembered, reached over Bluetooth.
    laptopTile(ctx, pal, rowB, false, 'Home PC', 'Remembered', pal.dim);
    const bcw = measure(ctx, 'Bluetooth', 13) + 16;
    fillRR(ctx, w - 32 - bcw, rowB + 32 - 11, bcw, 22, 8, pal.faint);
    textMid(ctx, 'Bluetooth', w - 32 - bcw / 2, rowB + 32, { size: 13, color: pal.ink, align: 'center' });
    const cyB = rowB + 64 + 22;
    cx = chip(ctx, pal, 'Connect', 32, cyB) + 8;
    cx = chip(ctx, pal, 'Wi-Fi', cx, cyB) + 8;
    chip(ctx, pal, 'Forget', cx, cyB, rgba(pal.danger));
    y += total;
    // Footnotes (Column.footnote: 16 in, 8 above), then the quiet buttons and the one main action.
    y = para(ctx, 'Remembered laptops connect when Edgepad opens.', 32, y + 8, w - 64, 13, rgba(pal.dim));
    y = para(ctx, 'Connect several laptops at once. The active one is the laptop the controls are driving; tap Make active to switch.', 32, y + 8, w - 64, 13, rgba(pal.dim));
    button(ctx, pal, 'Add by address', 16, y + 8, w - 32, 'QUIET');
    button(ctx, pal, 'USB tethering', 16, y + 8 + 58, w - 32, 'QUIET');
    const by = y + 8 + 58 + 50 + 8 + 24;
    const press = tri(lt, 4.3, 0.3);
    button(ctx, pal, 'Open controls', 16, by, w - 32, 'FILLED', isCon ? 1 : 0.4, press);
    if (!isCon) para(ctx, 'Connect to a laptop to open the controls.', 32, by + 50 + 8, w - 64, 13, rgba(pal.dim));
    dot(ctx, EP.path(lt, [[0.7, 260, rowA + 200], [1.15, w / 2, rowA + 32], [1.5, w / 2, rowA + 32], [3.7, w / 2, by + 25], [4.4, w / 2, by + 25]]));
  };

  // ---- Deck (Deck.kt, DeckPage.kt, DeckLayout.kt): tiles on the ground with no fill, the picture over a bold label. ----
  // The macros a laptop might hold (the app caps a page at 10 and the laptop at 15), as the laptop names them, with the
  // brand mark (ui/icons.js) and its colour. A mark in black or near-black is drawn in the ink, as the real icon would.
  const MACROS = [
    ['Firefox', 'firefoxbrowser', '#FF7139'], ['Discord', 'discord', '#5865F2'], ['Terminal', 'gnometerminal', null],
    ['Steam', 'steam', null], ['OBS', 'obsstudio', null], ['Notion', 'notion', null], ['Figma', 'figma', '#F24E1E'],
    ['Telegram', 'telegram', '#26A5E4'], ['Blender', 'blender', '#E87D0D'], ['Spotify', 'spotify', '#1ED760'],
    ['GitHub', 'github', null], ['Obsidian', 'obsidian', '#7C3AED'], ['VLC', 'vlcmediaplayer', '#FF8800'],
  ];
  EP.MACROS = MACROS;
  EP.MACROS_PER_PAGE = 10; // Settings.MAX_MACROS_PER_PAGE, the default
  EP.MACROS_MAX = 15;
  const GAP = 8; // Space.S between tiles and rows
  // Deck.plan: cells = max(slots, tiles); the column count that leaves the cell's shorter side longest, or `rows` rows.
  function plan(cells, across, down, rows) {
    const fixed = rows ? Math.ceil(cells / rows) : 0;
    let best = null;
    for (let c = fixed || 1; c <= (fixed || cells); c++) {
      const r = Math.ceil(cells / c);
      const cw = (across - (c - 1) * GAP) / c, ch = Math.max((down - (r - 1) * GAP) / r, 48);
      if (!best || fixed || Math.min(cw, ch) > Math.min(best.cw, best.ch)) best = { cols: c, rows: r, cw, ch };
    }
    return best;
  }
  // The rect of display place i of `count` tiles; a short last row keeps the tile width and is centred (Deck.row).
  function place(i, count, g, area) {
    const row = Math.floor(i / g.cols), col = i % g.cols;
    const lastRow = Math.ceil(count / g.cols) - 1;
    const inRow = row === lastRow ? count - row * g.cols : g.cols;
    const shift = ((g.cols - inRow) * (g.cw + GAP)) / 2;
    return { x: area.x + shift + col * (g.cw + GAP), y: area.y + row * (g.ch + GAP), w: g.cw, h: g.ch };
  }
  const mixRect = (a, b, k) => ({ x: lerp(a.x, b.x, k), y: lerp(a.y, b.y, k), w: lerp(a.w, b.w, k), h: lerp(a.h, b.h, k) });
  // One tile (Deck.kt): a press or the lift is the only shape drawn around it. A lifted tile is 1.1 x, on a card with a
  // shadow. `cross` draws the close cross of an open macro, a 22 dp card disc in the top-end corner.
  function tile(ctx, pal, r, draw, label, o = {}) {
    const cx = r.x + r.w / 2, cy = r.y + r.h / 2;
    const labelSp = clamp(Math.min(r.w, r.h) * 0.08, 17, 18);
    const line = labelSp * 1.25;
    const pic = Math.max(16, Math.min(r.w - 16, r.h - 16 - line - 4));
    ctx.save();
    ctx.globalAlpha *= o.a ?? 1;
    if (o.lift) {
      ctx.translate(cx, cy);
      ctx.scale(1 + 0.1 * o.lift, 1 + 0.1 * o.lift);
      ctx.translate(-cx, -cy);
      ctx.shadowColor = 'rgba(0,0,0,0.35)';
      ctx.shadowBlur = 24 * o.lift;
      ctx.shadowOffsetY = 8 * o.lift;
      fillRR(ctx, r.x, r.y, r.w, r.h, 16, pal.card, o.lift);
      ctx.shadowColor = 'transparent';
    }
    if (o.press > 0) fillRR(ctx, r.x, r.y, r.w, r.h, 16, pal.faint, o.press);
    const top = cy - (pic + 4 + line) / 2;
    draw(cx, top + pic / 2, pic);
    text(ctx, label, cx, top + pic + 4 + line * 0.78, { size: labelSp, w: 'Bold', color: pal.ink, align: 'center' });
    if (o.cross > 0) {
      ctx.save();
      ctx.globalAlpha *= o.cross;
      ctx.fillStyle = rgba(pal.card);
      ctx.beginPath();
      ctx.arc(r.x + r.w - 11, r.y + 11, 11, 0, Math.PI * 2);
      ctx.fill();
      icon(ctx, 'close', r.x + r.w - 11, r.y + 11, 14, pal.ink);
      ctx.restore();
    }
    ctx.restore();
  }

  // The macro deck (MacroScreen.kt over DeckPage.kt): a bar with Surface and a gear, pages of 10 that slide, a dot per page,
  // tiles pressed and held, then dragged home-screen style. A macro that is open has a close cross.
  // lt: 0.95 a tap runs Spotify (its cross shows from 1.4); 2.0 a slide to page two, 2.9 back; 3.5 Spotify is held, 3.8-4.8
  // dragged to the first place, dropped at 4.85; 6.0 the gear; 8.4 a tap.
  S.macros = (ctx, w, h, pal, lt) => {
    const pageK = ease((lt - 2.0) / 0.55) * (1 - ease((lt - 2.9) / 0.55)); // 0 page one, 1 page two
    bar(ctx, w, pal, 'Macros', 'Surface', null, tri(lt, 6.0, 0.35), true);
    const n = MACROS.length, per = EP.MACROS_PER_PAGE;
    const across = w - 32, room = h - TOP - 48 - 24 - BOTTOM; // DeckPage.measureRoom: less the bar, the margins and 24
    const down = room - 24; // the page dots take 24
    const g = plan(per, across, down, null);
    const area = { x: 16, y: TOP + 48 };
    const pageOf = (m) => Math.floor(m / per);
    const dropped = lt > 4.85;
    const dragK = EP.clamp((lt - 3.8) / 1.0);
    const reflow = ease((dragK - 0.3) / 0.4);
    // The order on page one: Spotify (9) goes to the front once dropped; the rest step along to make room.
    const order = [9, 0, 1, 2, 3, 4, 5, 6, 7, 8];
    const home = (m) => place(m % per, Math.min(per, n - pageOf(m) * per), g, area);
    const slot = (m) => (pageOf(m) === 0 ? place(order.indexOf(m), per, g, area) : home(m));
    const lift = held(lt, 3.5, 4.85);
    const fingerAt = EP.path(lt, [[3.5, 0, 0], [3.8, 0, 0], [4.8, 1, 1], [4.85, 1, 1]], true);
    const from = home(9), to = slot(9);
    const opened = (m) => (m === 9 ? ease((lt - 1.4) / 0.2) : m === 0 ? 1 : 0); // the laptop said which apps are open
    ctx.save();
    ctx.beginPath();
    ctx.rect(16, area.y - 8, across, down + 16);
    ctx.clip();
    MACROS.forEach(([label, brand, colour], m) => {
      if (m === 9 && lift > 0 && lt < 4.85) return;
      let r = pageOf(m) === 0 ? mixRect(home(m), slot(m), m === 9 ? (dropped ? 1 : 0) : reflow) : home(m);
      r = { ...r, x: r.x + (pageOf(m) - pageK) * across };
      const draw = (cx, cy, size) => EP.brand(ctx, brand, cx, cy, size * 0.78, colour || rgba(pal.ink));
      tile(ctx, pal, r, draw, label, { cross: opened(m), press: m === 9 ? tri(lt, 0.95, 0.35) + tri(lt, 8.35, 0.35) : 0 });
    });
    // The held tile rides the finger from its place to the first one.
    if (lift > 0 && lt < 4.85) {
      const k = fingerAt ? fingerAt.x : 1;
      const draw = (cx, cy, size) => EP.brand(ctx, 'spotify', cx, cy, size * 0.78, '#1ED760');
      tile(ctx, pal, mixRect(from, to, k), draw, 'Spotify', { lift, cross: opened(9) });
    }
    ctx.restore();
    // PageDots: 8 dp dots, 8 apart, the showing page's in the accent.
    const dotsY = area.y + room - 12;
    [0, 1].forEach((i) => {
      ctx.fillStyle = rgba(EP.mixRgb(pal.off, pal.accent, i === 0 ? 1 - pageK : pageK));
      ctx.beginPath();
      ctx.arc(w / 2 + (i - 0.5) * 16, dotsY, 4, 0, Math.PI * 2);
      ctx.fill();
    });
    const c = (r) => [r.x + r.w / 2, r.y + r.h / 2];
    const gear = [w - 28, TOP + 24];
    const [sx, sy] = c(from), [ex, ey] = c(to);
    dot(ctx, EP.path(lt, [[0.55, sx + 60, sy + 80], [0.95, sx, sy], [1.3, sx, sy]]));
    dot(ctx, EP.path(lt, [[2.0, w * 0.8, area.y + 200], [2.55, w * 0.2, area.y + 200]], true));
    dot(ctx, EP.path(lt, [[2.9, w * 0.2, area.y + 200], [3.45, w * 0.8, area.y + 200]], true));
    dot(ctx, EP.path(lt, [[3.3, ...c(from)], [3.8, ...c(from)], [4.8, ex, ey], [5.1, ex, ey]]));
    dot(ctx, EP.path(lt, [[5.6, gear[0] - 60, gear[1] + 90], [5.95, ...gear], [6.3, ...gear]]));
    dot(ctx, EP.path(lt, [[8.1, ex + 40, ey + 70], [8.4, ex, ey], [8.65, ex, ey]]));
  };

  // The 5.x film's deck (MacroScreen.kt over DeckPage.kt, as S.macros): nine neutral macros, six to a page (the page size
  // is a setting; six makes the nine two pages).
  // lt: 0.95 a tap runs Blender (its cross shows from 1.4); 2.0 a slide to page two, where it stays.
  const MACROS5 = [
    ['Blender', 'blender', '#E87D0D'], ['Figma', 'figma', '#F24E1E'], ['OBS', 'obsstudio', null], ['VLC', 'vlcmediaplayer', '#FF8800'], ['Steam', 'steam', null],
    ['Firefox', 'firefoxbrowser', '#FF7139'], ['Notion', 'notion', null], ['Obsidian', 'obsidian', '#7C3AED'], ['Terminal', 'gnometerminal', null],
  ];
  S.macros5 = (ctx, w, h, pal, lt) => {
    const per = 6, pages = Math.ceil(MACROS5.length / per);
    const pageK = ease((lt - 2.0) / 0.55);
    bar(ctx, w, pal, 'Macros', 'Surface', null, 0, true);
    const across = w - 32, room = h - TOP - 48 - 24 - BOTTOM, down = room - 24;
    const g = plan(per, across, down, null);
    const area = { x: 16, y: TOP + 48 };
    ctx.save();
    ctx.beginPath();
    ctx.rect(16, area.y - 8, across, down + 16);
    ctx.clip();
    MACROS5.forEach(([label, brand, colour], m) => {
      const page = Math.floor(m / per);
      const r = place(m % per, Math.min(per, MACROS5.length - page * per), g, area);
      const draw = (cx, cy, size) => EP.brand(ctx, brand, cx, cy, size * 0.78, colour || rgba(pal.ink));
      tile(ctx, pal, { ...r, x: r.x + (page - pageK) * across }, draw, label, { cross: m === 0 ? ease((lt - 1.4) / 0.2) : 0, press: m === 0 ? tri(lt, 0.95, 0.35) : 0 });
    });
    ctx.restore();
    const dotsY = area.y + room - 12;
    for (let i = 0; i < pages; i++) {
      ctx.fillStyle = rgba(EP.mixRgb(pal.off, pal.accent, i === 0 ? 1 - pageK : pageK));
      ctx.beginPath();
      ctx.arc(w / 2 + (i - (pages - 1) / 2) * 16, dotsY, 4, 0, Math.PI * 2);
      ctx.fill();
    }
    const r0 = place(0, per, g, area);
    dot(ctx, EP.path(lt, [[0.55, r0.x + r0.w / 2 + 60, r0.y + r0.h / 2 + 80], [0.95, r0.x + r0.w / 2, r0.y + r0.h / 2], [1.3, r0.x + r0.w / 2, r0.y + r0.h / 2]]));
    dot(ctx, EP.path(lt, [[2.0, w * 0.8, area.y + 200], [2.55, w * 0.2, area.y + 200]], true));
  };

  // ---- Controls (ControlsPopup.kt): a small modal over the surface, three tiles to a row upright and five sideways, under a header. ----
  // The tiles' icons are StencilIcon.kt: a body in the ink with holes cut through it, and one part in the accent, drawn in a
  // 48 unit square. Composed on a layer of their own, so the holes clear the icon and never the tile behind it.
  const stencilCache = {};
  function drawStencil(g, kind, ink, accent) {
    const rect = (x, y, w, h, r) => g.roundRect(x, y, w, h, r);
    const circle = (x, y, r) => {
      g.moveTo(x + r, y);
      g.arc(x, y, r, 0, Math.PI * 2);
    };
    const line = (...p) => {
      g.moveTo(p[0], p[1]);
      for (let i = 2; i < p.length; i += 2) g.lineTo(p[i], p[i + 1]);
    };
    const shut = (...p) => {
      line(...p);
      g.closePath();
    };
    // One part: filled, or stroked at o.width; a hole clears what is under it, o.alpha is the part's own opacity.
    const part = (color, build, o = {}) => {
      g.save();
      g.globalCompositeOperation = o.hole ? 'destination-out' : 'source-over';
      g.globalAlpha = o.alpha ?? 1;
      g.fillStyle = g.strokeStyle = color;
      g.lineWidth = o.width || 0;
      g.lineCap = o.cap || 'butt';
      g.lineJoin = 'round';
      g.beginPath();
      build();
      if (o.width) g.stroke();
      else g.fill();
      g.restore();
    };
    const solid = (build, o) => part(ink, build, o);
    const hole = (build, o) => part(ink, build, { ...o, hole: true });
    const live = (build, o) => part(accent, build, o);
    const round = { width: 3.8, cap: 'round' }; // StencilIcon.HOLE_STROKE
    if (kind === 'KEYBOARD') {
      solid(() => rect(4, 11, 40, 26, 6));
      hole(() => {
        for (let i = 0; i < 5; i++) rect(8.8 + i * 6.2, 16, 5.2, 4.4, 1.4);
        for (let i = 0; i < 4; i++) rect(11.9 + i * 6.2, 22.4, 5.2, 4.4, 1.4);
      });
      live(() => rect(14, 29.2, 20, 4, 2));
    } else if (kind === 'GAMEPAD') {
      solid(() => rect(4, 13, 40, 22, 11));
      hole(() => {
        line(10, 24, 20, 24);
        line(15, 19, 15, 29);
      }, round);
      live(() => circle(33, 24, 4.4));
    } else if (kind === 'MACROS') {
      solid(() => rect(17, 5, 26, 26, 6), { alpha: 89 / 255 });
      solid(() => rect(5, 17, 26, 26, 6));
      live(() => shut(14.4, 24.4, 24.4, 30, 14.4, 35.6));
    } else if (kind === 'CAPTURE') {
      solid(() => circle(24, 24, 19));
      hole(() => {
        line(18.4, 6.4, 26.4, 20.2);
        line(39.2, 13.2, 31.2, 27);
        line(41.2, 30.8, 25.2, 30.8);
        line(29.6, 41.6, 21.6, 27.8);
        line(8.8, 34.8, 16.8, 21);
        line(6.8, 17.2, 22.8, 17.2);
      }, { width: 2.4 });
      live(() => circle(24, 24, 5.2));
    } else if (kind === 'ANNOTATE') {
      solid(() => shut(9, 33.6, 11.6, 24.8, 30.4, 6, 36.6, 12.2, 17.8, 31));
      live(() => {
        g.moveTo(25, 40.4);
        g.bezierCurveTo(30.2, 37.2, 34.2, 42.2, 41, 38.4);
      }, { width: 4, cap: 'round' });
    } else if (kind === 'CLIPBOARD') {
      solid(() => rect(10, 9, 28, 34, 6));
      hole(() => {
        line(17, 22, 31, 22);
        line(17, 28.4, 31, 28.4);
        line(17, 34.8, 26, 34.8);
      }, { width: 3.2, cap: 'round' });
      live(() => rect(17, 5, 14, 8, 3));
    } else if (kind === 'PRESENTER') {
      solid(() => rect(5, 6, 38, 25, 4));
      solid(() => {
        line(24, 31, 24, 37);
        line(16, 43, 24, 37, 32, 43);
      }, round);
      hole(() => rect(9.4, 10.4, 29.2, 16.2, 1.6));
      live(() => circle(29, 18.6, 4.4));
    } else if (kind === 'SEND') {
      solid(() => shut(5, 22.4, 43, 6, 28, 43, 22, 27.4));
      live(() => shut(43, 6, 22, 27.4, 28, 43));
    } else if (kind === 'STATS') {
      solid(() => rect(5, 7, 38, 27, 6));
      solid(() => rect(16, 38, 16, 4, 2));
      hole(() => line(11, 21, 17, 21, 20.6, 15, 25.4, 27, 29, 21, 31.4, 21), { width: 3.4, cap: 'round' });
      live(() => circle(36.4, 21, 3.2));
    } else if (kind === 'DEVELOPER') {
      solid(() => rect(5, 8, 38, 32, 6));
      hole(() => {
        line(18, 18.4, 12.4, 24, 18, 29.6);
        line(30, 18.4, 35.6, 24, 30, 29.6);
      }, round);
      live(() => line(26.2, 16.6, 21.8, 31.4), round);
    }
  }
  // kind: KEYBOARD | GAMEPAD | MACROS | PRESENTER | CAPTURE | ANNOTATE | CLIPBOARD | SEND | STATS | DEVELOPER; ink and accent are
  // CSS colours.
  EP.stencil = (ctx, kind, cx, cy, size, ink, accent) => {
    const px = Math.max(48, Math.ceil(size * 2));
    const slot = kind + ink + accent + px;
    let c = stencilCache[slot];
    if (!c) {
      c = stencilCache[slot] = document.createElement('canvas');
      c.width = c.height = px;
      const g = c.getContext('2d');
      g.scale(px / 48, px / 48);
      drawStencil(g, kind, ink, accent);
    }
    ctx.drawImage(c, cx - size / 2, cy - size / 2, size, size);
  };

  // The popup's tiles in ControlsPopup.draw's order: Target.entries, with Send a file added just before Stats.
  const CONTROL_TILES = [['Keyboard', 'KEYBOARD'], ['Gamepad', 'GAMEPAD'], ['Macros', 'MACROS'], ['Presenter', 'PRESENTER'], ['Capture', 'CAPTURE'], ['Annotate', 'ANNOTATE'], ['Clipboard', 'CLIPBOARD'], ['Send a file', 'SEND'], ['Stats', 'STATS'], ['Developer', 'DEVELOPER']];
  const POPOVER = [[34, 35, 40], [251, 251, 253]]; // values-night/colors.xml and values/colors.xml: popover
  // lt: 0.1-0.4 the Controls button on the surface (the top row's first place) is pressed, tapped at 0.15; 0.2-0.5 the popup
  // grows from the button (REVEAL, 300 ms, from 85%); 2.2 the tile named by o.press is tapped.
  // o.under is the surface's state (what film.js passes to EP.drawSurface), drawn behind the popup. o.dev (default true) shows
  // the Developer tile. o.press: 'Keyboard' (default) | 'Gamepad' | 'Macros' | 'Presenter' | 'Capture' | 'Annotate' |
  // 'Clipboard' | 'Send a file' | 'Stats' | 'Developer'. Upright 390 x 844 and sideways 844 x 390 both work.
  S.controls = (ctx, w, h, pal, lt, o = {}) => {
    EP.drawSurface(ctx, w, h, { pal, radius: 44, dials: {}, fingers: [], trail: [], press: { name: 'controls', k: tri(lt, 0.1, 0.3) }, ...(o.under || {}) });
    const step = w > h ? 56 : 48, bx = w / 2 - 1.5 * step, by = 40;
    const open = EP.easeOut((lt - 0.2) / 0.3);
    let tapAt = null;
    if (open > 0) {
      const tiles = CONTROL_TILES.filter(([name]) => name !== 'Developer' || o.dev !== false);
      const cols = w > h ? 5 : 3, rows = Math.ceil(tiles.length / cols);
      const pw = Math.min(cols * 128 + (cols - 1) * GAP + 32, w - 32), tw = (pw - 32 - (cols - 1) * GAP) / cols;
      const ph = 48 + rows * 112 + (rows - 1) * GAP + 16, px = (w - pw) / 2, py = (h - ph) / 2;
      ctx.save();
      ctx.globalAlpha *= open;
      ctx.fillStyle = 'rgba(0,0,0,0.6)'; // the dialog's default window dim
      ctx.fillRect(0, 0, w, h);
      const grow = lerp(0.85, 1, open);
      ctx.translate(bx, by);
      ctx.scale(grow, grow);
      ctx.translate(-bx, -by);
      fillRR(ctx, px, py, pw, ph, 16, EP.mixRgb(POPOVER[0], POPOVER[1], pal.k));
      // The header row: the heading at the start, the pencil (16 dp in a 48 dp target) at the end.
      textMid(ctx, 'Controls', px + 16, py + 24, { size: 17, w: 'Bold', color: pal.ink });
      icon(ctx, 'tool_pen', px + pw - 24, py + 24, 16, pal.ink);
      const g = { cols, cw: tw, ch: 112 }, area = { x: px + 16, y: py + 48 };
      const pressed = Math.max(0, tiles.findIndex(([name]) => name === (o.press || 'Keyboard')));
      tiles.forEach(([name, kind], i) => {
        const r = place(i, tiles.length, g, area);
        // A tile: a 40 dp picture, 8 above a 15 sp bold label.
        const line = 15 * 1.25, top = r.y + r.h / 2 - (40 + 8 + line) / 2;
        if (i === pressed) fillRR(ctx, r.x, r.y, r.w, r.h, 16, pal.faint, tri(lt, 2.2, 0.4));
        EP.stencil(ctx, kind, r.x + r.w / 2, top + 20, 40, rgba(pal.ink), rgba(pal.accent));
        text(ctx, name, r.x + r.w / 2, top + 48 + line * 0.78, { size: 15, w: 'Bold', color: pal.ink, align: 'center' });
        if (i === pressed) tapAt = [r.x + r.w / 2, r.y + r.h / 2, r.w, r.h];
      });
      ctx.restore();
    }
    if (tapAt) dot(ctx, EP.path(lt, [[1.5, tapAt[0] + tapAt[2] / 2 + 40, tapAt[1] + tapAt[3] / 2 + 80], [2.1, tapAt[0], tapAt[1]], [2.6, tapAt[0], tapAt[1]]]));
    dot(ctx, EP.path(lt, [[-0.2, w - 40, 120], [0.18, bx, by], [0.4, bx, by]]));
  };

  // ---- Keyboard (KeyboardScreen.kt, KeyboardLayouts.kt): a bar with the type-text icon and a gear, then the board. ----
  // A key is { id, label, units, f (the small legend), rows (2 for a tall key) }; a gap is { gap }. The id is stable across the
  // six sizes, so a key can be matched through the reflow (the numpad's keys carry an N, the second Fn is Fn2).
  const key = (label, units = 1, o = {}) => ({ id: label, label, units, ...o });
  const gap = (units) => ({ gap: units });
  const num = (d) => key(d, 1, { id: 'N' + d });
  const F_KEYS = [['Bri-', 'F1'], ['Bri+', 'F2'], ['Task', 'F3'], ['Find', 'F4'], ['Kbd-', 'F5'], ['Kbd+', 'F6'], ['Prev', 'F7'], ['Play', 'F8'], ['Next', 'F9'], ['Mute', 'F10'], ['Vol-', 'F11'], ['Vol+', 'F12']]
    .map(([label, f]) => key(label, 1, { id: f, f }));
  const fRow = (a, b) => F_KEYS.slice(a, b);
  const letters = (s, f) => s.split('').map((c) => key(c, 1, f(c)));
  const NO_FN = () => ({});
  const n15 = () => ['`', ...'1234567890-='.split('')].map((c) => key(c)).concat(key('Backspace', 2));
  const t15 = (f = NO_FN) => [key('Tab', 1.5), ...letters('QWERTYUIOP[]', f), key('\\', 1.5)];
  const c15 = (f = NO_FN) => [key('Caps', 1.75), ...letters('ASDFGHJKL;\'', f), key('Enter', 2.25)];
  const s15 = (f = NO_FN, right = 2.75) => [key('Shift', 2.25, { id: 'ShiftL' }), ...letters('ZXCVBNM,./', f), key('Shift', right, { id: 'ShiftR' })];
  const b15 = () => [key('Ctrl', 1.25, { id: 'CtrlL' }), key('Alt', 1.25, { id: 'AltL' }), key('Win', 1.25, { id: 'WinL' }), key('Space', 6.25), key('Win', 1.25, { id: 'WinR' }), key('Alt', 1.25, { id: 'AltR' }), key('Fn', 1.25), key('Ctrl', 1.25, { id: 'CtrlR' })];
  const b13 = () => [key('Ctrl', 1.25, { id: 'CtrlL' }), key('Alt', 1.25, { id: 'AltL' }), key('Win', 1.25, { id: 'WinL' }), key('Space', 6.25), key('Win', 1, { id: 'WinR' }), key('Fn', 1), key('Ctrl', 1, { id: 'CtrlR' })];
  const arrows = () => [key('←'), key('↓'), key('→')];
  const nav3 = (a, b, c) => [a, b, c].map((l) => key(l));
  const numLock = () => [key('NumLk'), key('/', 1, { id: 'N/' }), key('*', 1, { id: 'N*' }), key('-', 1, { id: 'N-' })];
  const numberKeys = () => '1234567890-='.split('').map((c, i) => key(c, 1, { f: 'F' + (i + 1) }));
  const FN_60 = { I: 'menu', P: 'ins', '[': 'pgup', ']': 'home', K: 'up', ';': 'pgdn', '\'': 'end', M: 'left', ',': 'down', '.': 'right', '/': 'del' };
  const fn60 = (c) => ({ f: FN_60[c] });
  const KEYBOARDS = {
    100: { width: 22.75, rows: [
      [key('Esc'), gap(1), ...fRow(0, 4), gap(0.5), ...fRow(4, 8), gap(0.5), ...fRow(8, 12), gap(0.5), key('PrtSc'), key('Mic'), key('Light'), gap(4.25)],
      [...n15(), gap(0.5), ...nav3('Ins', 'Home', 'PgUp'), gap(0.25), ...numLock()],
      [...t15(), gap(0.5), ...nav3('Del', 'End', 'PgDn'), gap(0.25), num('7'), num('8'), num('9'), key('+', 1, { id: 'N+', rows: 2 })],
      [...c15(), gap(0.5), gap(3), gap(0.25), num('4'), num('5'), num('6'), gap(1)],
      [...s15(), gap(0.5), gap(1), key('↑'), gap(1), gap(0.25), num('1'), num('2'), num('3'), key('Enter', 1, { id: 'NEnter', rows: 2 })],
      [...b15(), gap(0.5), ...arrows(), gap(0.25), key('0', 2, { id: 'N0' }), key('.', 1, { id: 'N.' }), gap(1)],
    ] },
    96: { width: 19, rows: [
      [key('Esc'), ...fRow(0, 12), key('Del'), key('Home'), key('End'), key('PgUp'), key('PgDn'), key('Light')],
      [...n15(), ...numLock()],
      [...t15(), num('7'), num('8'), num('9'), key('+', 1, { id: 'N+', rows: 2 })],
      [...c15(), num('4'), num('5'), num('6'), gap(1)],
      [...s15(NO_FN, 1.75), key('↑'), num('1'), num('2'), num('3'), key('Enter', 1, { id: 'NEnter', rows: 2 })],
      [...b13(), ...arrows(), num('0'), key('.', 1, { id: 'N.' }), gap(1)],
    ] },
    80: { width: 18.5, rows: [
      [key('Esc'), gap(1), ...fRow(0, 4), gap(0.5), ...fRow(4, 8), gap(0.5), ...fRow(8, 12), gap(0.5), key('PrtSc'), key('Mic'), key('Light')],
      [...n15(), gap(0.5), ...nav3('Ins', 'Home', 'PgUp')],
      [...t15(), gap(0.5), ...nav3('Del', 'End', 'PgDn')],
      [...c15(), gap(3.5)],
      [...s15(), gap(0.5), gap(1), key('↑'), gap(1)],
      [...b15(), gap(0.5), ...arrows()],
    ] },
    75: { width: 16, rows: [
      [key('Esc'), ...fRow(0, 12), key('PrtSc'), key('Del'), key('Light')],
      [...n15(), key('PgUp')],
      [...t15(), key('PgDn')],
      [...c15(), key('Home')],
      [...s15(NO_FN, 1.75), key('↑'), key('End')],
      [...b13(), ...arrows()],
    ] },
    65: { width: 16, rows: [
      [key('Esc', 1, { f: '`' }), ...numberKeys(), key('Backspace', 2, { f: 'Del' }), key('Light')],
      [...t15(), key('Home')],
      [...c15(), key('PgUp')],
      [...s15(NO_FN, 1.75), key('↑'), key('PgDn')],
      [...b13().slice(0, 6), key('Fn', 1, { id: 'Fn2' }), ...arrows()],
    ] },
    60: { width: 15, rows: [
      [key('Esc', 1, { f: '`' }), ...numberKeys(), key('Backspace', 2)],
      t15(fn60),
      c15(fn60),
      s15(fn60),
      [...b15().slice(0, 5), key('Fn', 1.25), key('Fn', 1.25, { id: 'Fn2' }), key('Ctrl', 1.25, { id: 'CtrlR' })],
    ] },
  };
  // The size of the label at 14 dp bold: fitLabels shrinks the board to the height of the keys, and a key more to its own width.
  // Keys of one board by id, each { ri, x, y, w, h, label, f, size } with the board filling vw x vh from (0, top).
  function boardRects(ctx, percent, vw, vh, top) {
    const board = KEYBOARDS[percent], pad = 6;
    const unit = (vw - 2 * pad) / board.width, rowH = (vh - 2 * pad) / board.rows.length;
    const space = Math.min(6, 0.15 * Math.min(unit, rowH));
    const out = new Map();
    let fit = 1;
    board.rows.forEach((row, ri) => {
      let col = 0;
      for (const k of row) {
        if (k.gap) {
          col += k.gap;
          continue;
        }
        const r = { ri, x: pad + col * unit + space / 2, y: top + pad + ri * rowH + space / 2, w: k.units * unit - space, h: (k.rows || 1) * rowH - space, label: k.label, f: k.f };
        const wide = Math.max(measure(ctx, k.label, 14, 'Bold'), k.f ? measure(ctx, k.f, 14, 'Bold') : 0);
        r.keyFit = wide > 0 ? Math.min(1, Math.max(0, r.w - 8) / wide) : 1;
        fit = Math.min(fit, Math.max(0, r.h - 8) / (14 * 1.2));
        out.set(k.id, r);
        col += k.units;
      }
    });
    out.forEach((r) => {
      r.size = 14 * fit * r.keyFit;
    });
    return out;
  }
  // lt: 1.1 Ctrl latched by a tap, 2.2 C, 3.4 Ctrl released; 3.9 Shift held while 4.3 Alt is tapped (only when o.demo !== false).
  // o.from and o.to are the sizes (percent: 100 | 96 | 80 | 75 | 65 | 60; default 75, 75) and o.k (default 1) the reflow between
  // them: each key glides from its old rect to its new one, in-out, a row later by 6 ms of the 280 ms; a key that goes fades out
  // over the first 160 ms and a new one fades in over the last 160. o.keys: [[lt, label], ...] are extra presses, lit for 0.35 s
  // with a dot on the key (a letter, or a label such as Enter, Space, Shift).
  S.keyboard = (ctx, w, h, pal, lt, o = {}) => {
    bar(ctx, w, pal, 'Keyboard', 'Controls', null, 0, true);
    icon(ctx, 'keyboard', w - 76, TOP + 24, 24, pal.accent);
    const top = TOP + 48, vh = h - top - BOTTOM;
    const from = boardRects(ctx, o.from || 75, w, vh, top), to = boardRects(ctx, o.to || 75, w, vh, top);
    const k = o.k ?? 1, stagger = 6 / 280, span = 1 - 5 * stagger;
    const dark = pal.k < 0.5, demo = o.demo !== false;
    const keyFill = dark ? pal.faint : pal.card, fnFill = dark ? pal.card : pal.off;
    const lit = { CtrlL: ease((lt - 1.2) / 0.12) * (1 - ease((lt - 3.4) / 0.15)), ShiftL: held(lt, 3.9, 5.0), C: tri(lt, 2.2, 0.35), V: tri(lt, 2.9, 0.35), AltL: tri(lt, 4.3, 0.35) };
    if (!demo) Object.keys(lit).forEach((id) => (lit[id] = 0));
    const idOf = (label) => [label, label + 'L', String(label).toUpperCase()].find((id) => to.has(id) || from.has(id));
    (o.keys || []).forEach(([t, label]) => {
      const id = idOf(label);
      if (id) lit[id] = Math.max(lit[id] || 0, tri(lt, t, 0.35));
    });
    const centre = {};
    const ids = [...new Set([...to.keys(), ...from.keys()])];
    for (const id of ids) {
      const a = from.get(id), b = to.get(id);
      let r, size, alpha = 1;
      if (a && b) {
        const p = EP.easeInOut((k - b.ri * stagger) / span);
        r = mixRect(a, b, p);
        size = lerp(a.size, b.size, p);
      } else if (b) {
        r = b;
        size = b.size;
        alpha = seg(k, 0.429, 1);
      } else {
        r = a;
        size = a.size;
        alpha = 1 - seg(k, 0, 0.571);
      }
      if (alpha <= 0) continue;
      const label = (b || a).label, f = (b || a).f;
      const fnKey = (label.length > 1 && label !== 'Space') || '←↑↓→'.includes(label);
      const mod = ['Shift', 'Ctrl', 'Alt', 'Fn'].includes(label);
      const on = lit[id] || 0, radius = Math.min(10, r.w / 3);
      ctx.save();
      ctx.globalAlpha *= alpha;
      if (!dark) fillRR(ctx, r.x, r.y + 1, r.w, r.h, radius, pal.line);
      fillRR(ctx, r.x, r.y, r.w, r.h, radius, fnKey ? fnFill : keyFill);
      if (on > 0) fillRR(ctx, r.x, r.y, r.w, r.h, radius, pal.accent, on);
      const color = on > 0.5 ? pal.onAccent : !mod && ['Light', 'Kbd-', 'Kbd+'].includes(label) ? pal.dim : pal.ink;
      textMid(ctx, label, r.x + r.w / 2, r.y + r.h / 2, { size, w: 'Bold', color, align: 'center' });
      if (f) text(ctx, f, r.x + r.w / 2, r.y + 4 + size * 0.6, { size: size * 0.6, w: 'Bold', color: on > 0.5 ? pal.onAccent : fnKey ? pal.ink : pal.dim, align: 'center' });
      ctx.restore();
      centre[id] = [r.x + r.w / 2, r.y + r.h / 2];
    }
    if (demo) {
      const P = (id) => centre[id] || [w / 2, h / 2];
      dot(ctx, EP.path(lt, [[0.6, 140, h - 30], [1.1, ...P('CtrlL')], [1.45, ...P('CtrlL')], [2.1, ...P('C')], [2.45, ...P('C')], [2.85, ...P('V')], [3.15, ...P('V')], [3.3, ...P('CtrlL')], [3.55, ...P('CtrlL')]]));
      dot(ctx, EP.path(lt, [[3.75, ...P('ShiftL')], [5.0, ...P('ShiftL')]]));
      dot(ctx, EP.path(lt, [[4.15, ...P('AltL')], [4.6, ...P('AltL')]]));
    }
    (o.keys || []).forEach(([t, label]) => {
      const c = centre[idOf(label)];
      if (c) dot(ctx, EP.path(lt, [[t - 0.1, ...c], [t + 0.25, ...c]]));
    });
  };

  // ---- Keyboard settings (KeyboardSettingsScreen.kt): the Size choices, Function keys, and the text size. ----
  // lt: nothing is timed by lt. o.sel is the chosen size's index, fractional while the check moves (0 = 100% ... 5 = 60%;
  // default 3, the 75% board). o.scroll is how far the page has scrolled, in dp (a sideways screen cannot hold the six rows).
  // o.tap: [lt, index] taps that size's row at lt. Sideways or upright.
  S.keyboardSettings = (ctx, w, h, pal, lt, o = {}) => {
    const sel = o.sel ?? 3, scroll = o.scroll || 0;
    ctx.save();
    ctx.translate(0, -scroll);
    let y = largeTitle(ctx, pal, 'Keyboard settings', TOP + 48);
    y = section(ctx, pal, 'Size', y);
    const sizes = ['100%', '96%', '80%', '75%', '65%', '60%'];
    const ys = card(ctx, w, pal, y, sizes.length, 48);
    sizes.forEach((s, i) => {
      textMid(ctx, s, 32, ys[i], { size: 16, color: pal.ink });
      const on = Math.max(0, 1 - Math.abs(i - sel));
      if (on > 0) check(ctx, pal, w - 42, ys[i], on);
    });
    y += sizes.length * 49 - 1;
    y = para(ctx, 'Larger sizes fit more keys, so each key is smaller. They want a wider screen.', 32, y + 8, w - 64, 13, rgba(pal.dim));
    y += 24;
    const fy = card(ctx, w, pal, y, 1)[0];
    textMid(ctx, 'Function keys', 32, fy, { size: 16, color: pal.ink });
    EP.segmented(ctx, pal, w - 32, fy, ['With Fn', 'Direct'], 0);
    y = para(ctx, 'With Fn: the top row does what is printed on it (brightness, media, volume) and Fn plus a key sends F1 to F12. On 65% and 60%, which have no F row, the number row types numbers and Fn plus a number sends F1 to F12. Direct: the top row sends F1 to F12 and Fn plus a key does the printed action; on 65% and 60% the number row sends F1 to F12 and Fn plus a number types the number.', 32, y + 52 + 8, w - 64, 13, rgba(pal.dim));
    y += 24;
    fillRR(ctx, 16, y, w - 32, 76, 14, pal.card);
    gslider(ctx, w, pal, y, 'Keyboard text size', '×1.0', (1 - 0.6) / (2 - 0.6), 14);
    ctx.restore();
    fillRR(ctx, 0, 0, w, TOP + 48, 0, pal.bg);
    bar(ctx, w, pal, '', 'Keyboard', null);
    if (o.tap) tap(ctx, lt, o.tap[0], w - 42, ys[o.tap[1]] - scroll);
  };

  // ---- Annotate (AnnotateScreen.kt, AnnotateCanvas.kt): a full-screen pen pad for the laptop's screen, one round handle that
  // opens a menu upward, and Sign. ----
  const PEN = { black: '#000000', white: '#FFFFFF', red: '#FF3B30', orange: '#FF9500', yellow: '#FFCC00', green: '#34C759', blue: '#0068D6', purple: '#AF52DE' }; // PenColor
  const COLOURS = Object.values(PEN);
  const TOOLS = ['pen', 'highlighter', 'line', 'arrow', 'rectangle', 'ellipse']; // PenTool, res/drawable/ic_tool_*.xml
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
  const PEN_SHARE = 0.012; // Strokes.penShare: width 60 of 5000, a share of the pad's height
  // The strokes of the demo in a pw x ph box at the origin: a red ellipse (0.8-1.7), a blue arrow (2.4-3.1), cleared
  // as Sign comes on (3.9), then the signature (4.5-6.0). The laptop's screen shows the same strokes but never the
  // signature, which stays on the phone (`sign` false).
  EP.annotStrokes = (ctx, pw, ph, lt, ink, sign) => {
    const k = pw / 358;
    const signing = ease((lt - 3.9) / 0.3);
    ctx.lineCap = ctx.lineJoin = 'round';
    const draw = (pts, p, colour) => {
      if (p <= 0) return null;
      ctx.strokeStyle = colour;
      ctx.lineWidth = Math.max(1, ph * PEN_SHARE);
      return poly(ctx, pts, p);
    };
    const base = ctx.globalAlpha;
    ctx.globalAlpha = base * (1 - signing);
    draw(ellipsePts(pw * 0.38, ph * 0.5, pw * 0.2, ph * 0.28), seg(lt, 0.8, 1.7), PEN.red);
    const arrow = [[pw * 0.62, ph * 0.22], [pw * 0.84, ph * 0.5]];
    const tip = draw(arrow, seg(lt, 2.4, 3.1), PEN.blue);
    if (tip && lt > 3.05) {
      ctx.beginPath();
      ctx.moveTo(arrow[1][0] - 14 * k, arrow[1][1] - 4 * k);
      ctx.lineTo(arrow[1][0], arrow[1][1]);
      ctx.lineTo(arrow[1][0] - 6 * k, arrow[1][1] - 15 * k);
      ctx.stroke();
    }
    if (sign) {
      ctx.globalAlpha = base * signing;
      draw(SIG.map(([a, b]) => [a * pw, b * ph]), seg(lt, 4.5, 6.0), ink);
    }
    ctx.globalAlpha = base;
  };
  EP.annotSignature = (ctx, pw, ph, ink, width) => {
    ctx.lineCap = ctx.lineJoin = 'round';
    ctx.strokeStyle = ink;
    ctx.lineWidth = width;
    poly(ctx, SIG.map(([a, b]) => [a * pw, b * ph]), 1);
  };

  // WCAG contrast of two [r, g, b] colours, as AnnotateScreen.contrast (FLARE 0.05).
  const lum = (c) => {
    const f = (v) => ((v /= 255) <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4));
    return 0.2126 * f(c[0]) + 0.7152 * f(c[1]) + 0.0722 * f(c[2]);
  };
  const contrast = (a, b) => (Math.max(lum(a), lum(b)) + 0.05) / (Math.min(lum(a), lum(b)) + 0.05);
  const hexRgb = (h) => [1, 3, 5].map((i) => parseInt(h.slice(i, i + 2), 16));
  // One slider of the menu (Ui.slider): the label and value over a 4 dp track, a 20 dp thumb, a dot under each step.
  function slider(ctx, pal, x, y, sw, label, value, f, steps) {
    text(ctx, label, x, y + 12 + 17, { size: 16, color: rgba(pal.ink) });
    text(ctx, value, x + sw, y + 12 + 16, { size: 15, color: rgba(pal.dim), align: 'right' });
    const x0 = x + 10, x1 = x + sw - 10, ty = y + 52;
    fillRR(ctx, x0, ty - 2, x1 - x0, 4, 2, pal.off);
    fillRR(ctx, x0, ty - 2, (x1 - x0) * f, 4, 2, pal.accent);
    ctx.fillStyle = rgba(pal.dim, 0.5);
    for (let i = 0; i <= steps; i++) {
      ctx.beginPath();
      ctx.arc(lerp(x0, x1, i / steps), ty + 18, 1.5, 0, Math.PI * 2);
      ctx.fill();
    }
    ctx.beginPath();
    ctx.arc(x0 + (x1 - x0) * f, ty, 10, 0, Math.PI * 2);
    ctx.fillStyle = '#FFFFFF';
    ctx.fill();
    ctx.lineWidth = 1;
    ctx.strokeStyle = rgba(pal.line);
    ctx.stroke();
  }
  // lt: the handle is tapped at 0.15 (the menu opens), red is picked at 0.45, the Pen at 0.7 (the menu closes); the ellipse is
  // drawn 0.8-1.7. The handle opens it at 1.8, blue at 2.0, Arrow at 2.25; the arrow is drawn 2.4-3.1. At 3.3 the menu opens,
  // 3.55 black, 3.85 Sign; at 4.2 the Pen closes it; the signature is drawn 4.5-6.0. At 6.3 the menu opens, 6.55 Save signature.
  S.annotate = (ctx, w, h, pal, lt) => {
    const opens = [[0.15, 0.7], [1.8, 2.25], [3.3, 4.2], [6.3, 99]];
    const menuOpen = within(lt, opens);
    const signing = lt >= 3.95;
    const colour = lt < 0.45 ? 0 : lt < 2.0 ? 2 : lt < 3.55 ? 6 : 0;
    const tool = signing ? 'pen' : lt >= 2.25 ? 'arrow' : 'pen';
    // The pad: the laptop's screen (16 : 10), fitted and centred in the area under the status bar.
    const areaH = h - TOP - BOTTOM;
    const pw = w, ph = Math.min(areaH, w / 1.6);
    const py = TOP + (areaH - ph) / 2;
    ctx.fillStyle = rgba(pal.card);
    ctx.fillRect(0, py, pw, ph);
    ctx.save();
    ctx.beginPath();
    ctx.rect(0, py, pw, ph);
    ctx.clip();
    ctx.translate(0, py);
    EP.annotStrokes(ctx, pw, ph, lt, rgba(pal.ink), true);
    ctx.restore();
    // EditorFrame.addBack: a 40 dp round card with an accent chevron, 8 from the edge and 6 from the top.
    ctx.fillStyle = rgba(pal.card);
    ctx.beginPath();
    ctx.arc(28, TOP + 26, 20, 0, Math.PI * 2);
    ctx.fill();
    if (pal.k > 0.5) {
      ctx.lineWidth = 1;
      ctx.strokeStyle = rgba(pal.line);
      ctx.stroke();
    }
    icon(ctx, 'chevron_left', 28, TOP + 26, 24, pal.accent);
    // The handle: the current tool in the current colour, and a chevron that points the way the menu goes. 96 x 48, 12 in.
    const hx = w - 12 - 96, hy = h - BOTTOM - 12 - 48, hcy = hy + 24;
    const pen = hexRgb(COLOURS[colour]);
    const onCard = contrast(pen, pal.card.map(Math.round)) >= 3;
    ctx.save();
    ctx.shadowColor = 'rgba(0,0,0,0.3)';
    ctx.shadowBlur = 8;
    ctx.shadowOffsetY = 2;
    fillRR(ctx, hx, hy, 96, 48, 24, onCard ? pal.card : pal.ink);
    ctx.restore();
    icon(ctx, 'tool_' + tool, hx + 24, hcy, 24, COLOURS[colour]);
    icon(ctx, menuOpen ? 'chevron_down' : 'chevron_up', hx + 72, hcy, 24, onCard ? pal.dim : pal.card);
    // The menu, opening upward from the handle (a vertical column, 288 wide in 8 dp of padding, anchored 8 above it): tools,
    // the colours in rows of five with the custom one last, width, opacity, then undo, clear, Sign and Exit, and Save
    // signature while signing. Growing at the bottom moves everything above it up.
    const mw = 304;
    const geom = (sg) => {
      const mh = 8 + 48 + 96 + 84 + 84 + 48 + (sg ? 8 + 50 : 0) + 8;
      const mx = w - 12 - mw, my = hy - 8 - mh, ix = mx + 8, y1 = my + 8;
      return {
        mh, mx, my, ix, y1,
        tool: (i) => [ix + i * 48 + 24, y1 + 24],
        swatch: (i) => [ix + (i % 5) * 48 + 24, y1 + 48 + Math.floor(i / 5) * 48 + 24],
        action: (i) => [ix + i * 48 + 24, y1 + 48 + 96 + 84 + 84 + 24],
        save: [ix + 144, y1 + 48 + 96 + 84 + 84 + 48 + 8 + 25],
      };
    };
    if (menuOpen) {
      const G = geom(signing);
      ctx.save();
      ctx.shadowColor = 'rgba(0,0,0,0.35)';
      ctx.shadowBlur = 16;
      ctx.shadowOffsetY = 4;
      fillRR(ctx, G.mx, G.my, mw, G.mh, 14, pal.bg);
      ctx.restore();
      const cell = (c, name, on) => {
        if (on) fillRR(ctx, c[0] - 24, c[1] - 24, 48, 48, 14, pal.accent);
        icon(ctx, name, c[0], c[1], 24, on ? pal.onAccent : pal.ink);
      };
      TOOLS.forEach((t, i) => {
        if (signing && t !== 'pen') return; // Sign hides every tool but the pen
        cell(G.tool(signing ? 0 : i), 'tool_' + t, t === tool);
      });
      for (let i = 0; i < 9; i++) {
        const [cx, cy] = G.swatch(i);
        const on = i === colour;
        ctx.beginPath();
        ctx.arc(cx, cy, 16, 0, Math.PI * 2);
        if (i < 8) ctx.fillStyle = COLOURS[i];
        else {
          const g = ctx.createConicGradient(0, cx, cy);
          [PEN.red, PEN.yellow, PEN.green, PEN.blue, PEN.purple, PEN.red].forEach((c, n) => g.addColorStop(n / 5, c));
          ctx.fillStyle = g;
        }
        ctx.fill();
        ctx.lineWidth = on ? 3 : 1;
        ctx.strokeStyle = rgba(on ? pal.accent : pal.line);
        ctx.beginPath();
        ctx.arc(cx, cy, 16 - (on ? 1.5 : 0.5), 0, Math.PI * 2);
        ctx.stroke();
      }
      slider(ctx, pal, G.ix, G.y1 + 48 + 96, 288, 'Width', '12', 11 / 19, 19);
      slider(ctx, pal, G.ix, G.y1 + 48 + 96 + 84, 288, 'Opacity', '100%', 1, 18);
      ['undo', 'clear', 'sign', 'close'].forEach((n, i) => cell(G.action(i), n, n === 'sign' && signing));
      if (signing) button(ctx, pal, 'Save signature', G.ix, G.y1 + 48 + 96 + 84 + 84 + 48 + 8, 288, 'OUTLINED', 1, tri(lt, 6.55, 0.3));
    }
    // The finger: the handle, then a swatch or a tool, or Sign, or Save signature.
    const handle = [hx + 48, hcy];
    [
      [0.15, handle], [0.45, geom(false).swatch(2)], [0.7, geom(false).tool(0)],
      [1.8, handle], [2.0, geom(false).swatch(6)], [2.25, geom(false).tool(3)],
      [3.3, handle], [3.55, geom(false).swatch(0)], [3.85, geom(false).action(2)], [4.2, geom(true).tool(0)],
      [6.3, handle], [6.55, geom(true).save],
    ].forEach(([t, p]) => tap(ctx, lt, t, p[0], p[1], p[0] - 30, p[1] + 60));
    // The pen on the pad: the ellipse, the arrow and the signature, as before.
    const padPt = (fx, fy) => [fx * pw, py + fy * ph];
    const e = ellipsePts(pw * 0.38, ph * 0.5, pw * 0.2, ph * 0.28);
    const ePos = (p) => padPt(e[Math.min(40, Math.round(p * 40))][0] / pw, e[Math.min(40, Math.round(p * 40))][1] / ph);
    const keys = [];
    for (let k = 0; k <= 8; k++) keys.push([0.8 + k * (0.9 / 8), ...ePos(k / 8)]);
    dot(ctx, EP.path(lt, keys, true));
    dot(ctx, EP.path(lt, [[2.4, ...padPt(0.62, 0.22)], [3.1, ...padPt(0.84, 0.5)]], true));
    const sk = [];
    for (let k = 0; k <= 10; k++) sk.push([4.5 + k * 0.15, ...padPt(SIG[k][0], SIG[k][1])]);
    dot(ctx, EP.path(lt, sk, true));
  };

  // ---- Capture (CaptureScreen.kt, 5.x): the pad fills the screen and the three buttons sit under it (in a column beside it when
  // sideways). "To phone" is a chip at the bar's end; the line over the buttons says what the last tap did. ----
  // lt: 1.0 Region is tapped (its line appears), the plain drag on the pad 1.9-3.4 (the hold ring fills 1.9-2.3 while the
  // finger holds still, then it turns accent and the finger drags), 4.0 Full screen, 5.0 Record (it reads Stop recording).
  // o.phone: the "To phone" chip is on.
  S.capture = (ctx, w, h, pal, lt, o = {}) => {
    const side = w > h;
    bar(ctx, w, pal, 'Capture', 'Controls', null);
    const chipLabel = o.phone ? '✓ To phone' : 'To phone';
    const chipW = measure(ctx, chipLabel, 15, 'Bold') + 32;
    fillRR(ctx, w - 4 - chipW, TOP + 24 - 16, chipW, 32, 16, o.phone ? pal.accentSoft : pal.faint);
    textMid(ctx, chipLabel, w - 4 - chipW / 2, TOP + 24, { size: 15, w: 'Bold', color: o.phone ? pal.accent : pal.ink, align: 'center' });
    const status = lt >= 5.0 ? 'Recording on the laptop' : lt >= 4.0 ? 'Sent to the laptop: Full screen' : lt >= 1.0 ? 'Sent to the laptop: Region' : '';
    const statusH = status ? 13 * LEAD : 0;
    const top = TOP + 48, bottom = h - BOTTOM;
    // The actions: a row under the pad, or a column of 200 beside it, the line over the buttons in both.
    let pad, buttons, line;
    if (side) {
      const colH = (status ? statusH + 8 : 0) + 3 * 50 + 2 * GAP;
      const y0 = top + (bottom - top - colH) / 2;
      pad = { x: 0, y: top, w: w - 200, h: bottom - top };
      line = { x: w - 200 + 16, y: y0, w: 168 };
      buttons = [0, 1, 2].map((i) => ({ x: w - 200 + 16, y: y0 + (status ? statusH + 8 : 0) + i * (50 + GAP), w: 168 }));
    } else {
      const bw = (w - 32 - 2 * GAP) / 3, rowY = bottom - GAP - 50;
      const lineY = rowY - GAP - statusH;
      pad = { x: 0, y: top, w, h: (status ? lineY : rowY) - GAP - top };
      line = { x: 16, y: lineY, w: w - 32 };
      buttons = [0, 1, 2].map((i) => ({ x: 16 + i * (bw + GAP), y: rowY, w: bw }));
    }
    // The pad is the control surface in its trackpad-only mode: its backdrop, and nothing else.
    ctx.save();
    ctx.beginPath();
    ctx.rect(pad.x, pad.y, pad.w, pad.h);
    ctx.clip();
    ctx.translate(pad.x, pad.y);
    EP.drawBackdrop(ctx, pad.w, pad.h, pal, null);
    ctx.restore();
    if (status) para(ctx, status, line.x, line.y, line.w, 13, rgba(pal.dim));
    const labels = ['Region', 'Full screen', lt >= 5.0 ? 'Stop recording' : 'Record'];
    const presses = [tri(lt, 1.0, 0.35), tri(lt, 4.0, 0.35), tri(lt, 5.0, 0.35)];
    buttons.forEach((b, i) => {
      fillRR(ctx, b.x, b.y, b.w, 50, 14, pal.accentSoft);
      if (presses[i] > 0) fillRR(ctx, b.x, b.y, b.w, 50, 14, pal.faint, presses[i] * 0.6);
      textMid(ctx, labels[i], b.x + b.w / 2, b.y + 25, { size: 15, w: 'Bold', color: pal.accent, align: 'center' });
    });
    const p0 = [pad.x + pad.w * 0.3, pad.y + pad.h * 0.25], p1 = [pad.x + pad.w * 0.72, pad.y + pad.h * 0.68];
    const drag = [[1.9, ...p0], [2.3, ...p0], [3.4, ...p1], [3.8, ...p1]];
    const now = EP.path(lt, drag, true);
    if (now) {
      EP.drawFingers(ctx, { fingers: [now], trail: EP.trailOf((tt) => EP.path(tt, drag, true) || now, lt, 12, 0.025) }, pal);
      // ControlSurface.drawHold: a 22 dp ring, 3 wide, from the top, filling over HOLD_MS (400) and shown from a quarter of it.
      const fill = seg(lt, 1.9, 2.3);
      if (fill >= 0.25) {
        ctx.lineWidth = 3;
        ctx.strokeStyle = rgba(fill >= 1 ? pal.accent : pal.dim, now.a);
        ctx.beginPath();
        ctx.arc(now.x, now.y, 22, -Math.PI / 2, -Math.PI / 2 + Math.PI * 2 * fill);
        ctx.stroke();
      }
    }
    [1.0, 4.0, 5.0].forEach((t, i) => {
      const x = buttons[i].x + buttons[i].w / 2, y = buttons[i].y + 25;
      tap(ctx, lt, t, x, y, x + 40, y - 110);
    });
  };

  // ---- Gamepad (GamepadLayout.kt presets, GamepadScreen.kt PadPainter): always sideways. ----
  // Sizes in dp: sticks and the d-pad 150, shoulders 110 x 46 (SHOULDER_ASPECT 0.42), face buttons 64, Start, Back, L3, R3 44.
  const SHOULDER_ASPECT = 0.42, STICK_THUMB_RATIO = 0.35, CORNER = 10;
  const pc = (id, kind, label, x, y, size, trigger) => ({ id, kind, label, x, y, size, trigger });
  const PAD_PRESETS = {
    xbox: [
      pc('lstick', 'stick', 'L', 0.13, 0.46, 150), pc('rstick', 'stick', 'R', 0.7, 0.78, 150), pc('dpad', 'dpad', 'D', 0.3, 0.78, 150),
      pc('a', 'button', 'A', 0.84, 0.58, 64), pc('b', 'button', 'B', 0.92, 0.46, 64), pc('x', 'button', 'X', 0.76, 0.46, 64), pc('y', 'button', 'Y', 0.84, 0.34, 64),
      pc('lt', 'shoulder', 'LT', 0.16, 0.12, 110, 'L'), pc('lb', 'shoulder', 'LB', 0.32, 0.12, 110), pc('rb', 'shoulder', 'RB', 0.68, 0.12, 110), pc('rt', 'shoulder', 'RT', 0.84, 0.12, 110, 'R'),
      pc('start', 'button', 'Start', 0.56, 0.92, 44), pc('back', 'button', 'Back', 0.44, 0.92, 44), pc('l3', 'button', 'L3', 0.05, 0.88, 44), pc('r3', 'button', 'R3', 0.95, 0.88, 44),
    ],
    xboxKeys: [
      pc('stick', 'stick', 'L', 0.14, 0.55, 150), pc('dpad', 'dpad', 'D', 0.3, 0.72, 150),
      pc('a', 'button', 'A', 0.82, 0.65, 64), pc('b', 'button', 'B', 0.9, 0.55, 64), pc('x', 'button', 'X', 0.74, 0.55, 64), pc('y', 'button', 'Y', 0.82, 0.45, 64),
      pc('lb', 'shoulder', 'LB', 0.2, 0.12, 110), pc('rb', 'shoulder', 'RB', 0.8, 0.12, 110), pc('lt', 'shoulder', 'LT', 0.34, 0.12, 110), pc('rt', 'shoulder', 'RT', 0.66, 0.12, 110),
      pc('start', 'button', 'Start', 0.58, 0.9, 44), pc('select', 'button', 'Select', 0.42, 0.9, 44),
    ],
    platformer: [
      pc('dpad', 'dpad', 'D', 0.18, 0.6, 150), pc('jump', 'button', 'Jump', 0.85, 0.6, 72), pc('run', 'button', 'Run', 0.72, 0.72, 72), pc('pause', 'button', 'Pause', 0.5, 0.9, 44),
    ],
    racing: [
      pc('accelerate', 'shoulder', 'Accelerate', 0.85, 0.6, 160), pc('brake', 'shoulder', 'Brake', 0.85, 0.85, 160),
      pc('left', 'button', 'Left', 0.12, 0.7, 80), pc('right', 'button', 'Right', 0.28, 0.7, 80), pc('nitro', 'button', 'Nitro', 0.5, 0.85, 56),
    ],
    shooter: [
      pc('stick', 'stick', 'L', 0.14, 0.55, 150), pc('fire', 'button', 'Fire', 0.85, 0.6, 80), pc('jump', 'button', 'Jump', 0.72, 0.75, 60),
      pc('reload', 'button', 'Reload', 0.9, 0.35, 52), pc('crouch', 'button', 'Crouch', 0.72, 0.4, 52), pc('aim', 'shoulder', 'Aim', 0.85, 0.12, 110),
    ],
  };
  // The controls of a preset on a w x h screen: id, label, kind ('button' | 'dpad' | 'stick' | 'shoulder'), centre, half sizes,
  // and for a controller trigger its side ('L' | 'R'). preset: xbox | xboxKeys | platformer | racing | shooter.
  EP.gamepadLayout = (preset, w, h) =>
    (PAD_PRESETS[preset] || PAD_PRESETS.xbox).map((c) => {
      const rw = c.size / 2;
      return { id: c.id, label: c.label, kind: c.kind, cx: c.x * w, cy: c.y * h, rw, rh: c.kind === 'shoulder' ? rw * SHOULDER_ASPECT : rw, trigger: c.trigger };
    });
  // PadPainter: draws `controls` (EP.gamepadLayout's shape, positions free to move) on a w x h pad. state: input {lx, ly (+ is down),
  // rt, lt, a} drives the left stick's thumb, the two triggers' analog fill and A; press {id: 0..1} holds a control down;
  // thumb {id: {x, y}} puts a stick's thumb at that share of its travel; lifted: the id the editor has lifted (the accent ring).
  EP.drawPad = (ctx, w, h, pal, controls, state = {}) => {
    const light = pal.k > 0.5;
    const input = state.input, press = state.press || {};
    const circle = (cx, cy, r, color) => {
      ctx.beginPath();
      ctx.arc(cx, cy, r, 0, Math.PI * 2);
      ctx.fillStyle = typeof color === 'string' ? color : rgba(color);
      ctx.fill();
    };
    // PadPainter.body: on the light theme the shape again one dp lower in the line colour, then the card (or the accent).
    const body = (cx, cy, rw, rh, on, round, pull = 0) => {
      const shape = (dy, color) => {
        if (round) circle(cx, cy + dy, rw, color);
        else fillRR(ctx, cx - rw, cy + dy - rh, 2 * rw, 2 * rh, CORNER, color);
      };
      if (light) shape(1, pal.line);
      shape(0, on ? pal.accent : pal.card);
      if (pull > 0) {
        ctx.save();
        ctx.beginPath();
        ctx.rect(cx - rw, cy + rh - 2 * rh * pull, 2 * rw, 2 * rh * pull);
        ctx.clip();
        shape(0, pal.accentSoft);
        ctx.restore();
      }
    };
    // PadPainter.label: 13 on a control narrower than 56, else 15, drawn fitted to the control less 8.
    const label = (s, cx, cy, width, on, dim) => {
      let size = width < 56 ? 13 : 15;
      const wide = measure(ctx, s, size, 'Bold');
      if (wide > width - 8) size *= (width - 8) / wide;
      textMid(ctx, s, cx, cy, { size, w: 'Bold', color: on ? pal.onAccent : dim ? pal.dim : pal.ink, align: 'center' });
    };
    // PadPainter.plus: a centre and four arms, the outer corners rounded to 10.
    const plus = (cx, cy, size) => {
      const third = size / 3, radius = Math.min(CORNER, third / 2);
      const arm = (r, c, radii) => {
        ctx.fillStyle = rgba(pal.card);
        ctx.beginPath();
        ctx.roundRect(cx - size / 2 + c * third, cy - size / 2 + r * third, third, third, radii.map((v) => v && radius));
        ctx.fill();
      };
      if (light) {
        ctx.save();
        ctx.translate(0, 1);
        ctx.fillStyle = rgba(pal.line);
        ctx.fillRect(cx - third / 2, cy - size / 2, third, size);
        ctx.fillRect(cx - size / 2, cy - third / 2, size, third);
        ctx.restore();
      }
      ctx.fillStyle = rgba(pal.card);
      ctx.fillRect(cx - third / 2 - 1, cy - third / 2 - 1, third + 2, third + 2);
      arm(0, 1, [1, 1, 0, 0]);
      arm(2, 1, [0, 0, 1, 1]);
      arm(1, 0, [1, 0, 0, 1]);
      arm(1, 2, [0, 1, 1, 0]);
    };
    for (const c of controls) {
      const { cx, cy, rw, rh } = c;
      const pressed = press[c.id] ?? (input && c.id === 'a' ? input.a : 0);
      if (c.kind === 'dpad') {
        plus(cx, cy, 2 * rw);
        label(c.label, cx, cy, 2 * rw, false, true);
      } else if (c.kind === 'stick') {
        body(cx, cy, rw, rw, false, true);
        const travel = rw * (1 - STICK_THUMB_RATIO);
        const t = (state.thumb && state.thumb[c.id]) || (input && (c.id === 'lstick' || c.id === 'stick') ? { x: input.lx, y: input.ly || 0 } : null);
        circle(cx + (t ? t.x * travel : 0), cy + (t ? t.y * travel : 0), rw * STICK_THUMB_RATIO, t ? pal.accent : pal.off);
        label(c.label, cx, cy, 2 * rw, false, true);
      } else {
        const pull = c.trigger && input ? (c.trigger === 'L' ? input.lt : input.rt) : 0;
        const on = !c.trigger && pressed > 0.3;
        body(cx, cy, rw, rh, on, c.kind === 'button', pull);
        label(c.label, cx, cy, 2 * rw, on, false);
      }
      if (state.lifted === c.id) {
        ctx.lineWidth = 2;
        ctx.strokeStyle = rgba(pal.accent);
        ctx.beginPath();
        if (c.kind === 'shoulder') ctx.roundRect(cx - rw - 1, cy - rh - 1, 2 * rw + 2, 2 * rh + 2, CORNER + 1);
        else if (c.kind === 'dpad') ctx.rect(cx - rw - 1, cy - rh - 1, 2 * rw + 2, 2 * rh + 2);
        else ctx.arc(cx, cy, rw + 1, 0, Math.PI * 2);
        ctx.stroke();
      }
    }
  };
  // lt: with no o.input the 4.0 demo runs on the Xbox preset: the left thumb circles from 0.4 and A 1.0, X 1.7, B 2.2, Y 3.3 are
  // pressed. o.input { lx, ly (+ is down), rt, lt, a } (sticks -1..1, triggers 0..1, a 0..1) makes the left stick, RT and LT fill
  // (analog pull) and A follow it exactly, with the thumbs' dots on them. o.preset: xbox (default) | xboxKeys | platformer |
  // racing | shooter.
  S.gamepad = (ctx, w, h, pal, lt, o = {}) => {
    const controls = EP.gamepadLayout(o.preset || 'xbox', w, h);
    const find = (id) => controls.find((c) => c.id === id);
    const stick = find('lstick') || find('stick');
    const input = o.input;
    const travel = stick ? stick.rw * (1 - STICK_THUMB_RATIO) : 1;
    const a = lt * 2.2;
    const state = input
      ? { input }
      : { press: { a: tri(lt, 1.0, 0.4), x: tri(lt, 1.7, 0.4), b: tri(lt, 2.2, 0.4), y: tri(lt, 3.3, 0.4) }, thumb: stick ? { [stick.id]: { x: (14 * Math.cos(a)) / travel, y: (14 * Math.sin(a)) / travel } } : {} };
    EP.drawPad(ctx, w, h, pal, controls, state);
    if (!stick) return;
    if (!input) {
      const thumb = [stick.cx + 14 * Math.cos(a), stick.cy + 14 * Math.sin(a)];
      dot(ctx, EP.path(lt, [[0.4, ...thumb], [4.4, ...thumb]]));
      const at = (id) => { const c = find(id); return [c.cx, c.cy]; };
      if (o.preset && o.preset !== 'xbox') return;
      dot(ctx, EP.path(lt, [[0.8, ...at('a')], [1.2, ...at('a')], [1.6, ...at('x')], [1.9, ...at('x')], [2.1, ...at('b')], [2.4, ...at('b')], [3.2, ...at('y')], [3.5, ...at('y')]]));
      return;
    }
    EP.tapDot(ctx, stick.cx + input.lx * travel, stick.cy + (input.ly || 0) * travel, 1);
    [['rt', input.rt], ['lt', input.lt]].forEach(([id, v]) => {
      const c = find(id);
      if (c && v > 0.02) EP.tapDot(ctx, c.cx, c.cy + c.rh - 2 * c.rh * v, clamp(v * 8));
    });
    const btn = find('a');
    if (btn && input.a > 0.02) EP.tapDot(ctx, btn.cx, btn.cy, clamp(input.a * 4));
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
  // Off, then every DialKind by its name (strings.xml dial_*): the eleven the app has since the Mic and Track dials went.
  const KIND_NAMES = ['Off', 'Volume', 'Brightness', 'Zoom', 'Media scrub', 'App switcher', 'Refresh rate', 'Vertical scroll', 'Horizontal scroll', 'Virtual desktops', 'Browser tabs', 'Switch laptop'];
  const HELD = ['Volume', 'Brightness', 'Zoom', 'Media scrub'];
  const SLOT_HELD = ['Volume', 'App switcher', 'Vertical scroll', 'Brightness', 'Zoom', 'Refresh rate', 'Zoom', 'Media scrub'];
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
    bar(ctx, w, pal, 'Corners', 'Surface settings', null);
    if (!o.split) dot(ctx, EP.path(lt, [[0.45, w - 120, ty + 140], [0.95, w - 47, ty], [1.25, w - 47, ty]]));
    if (o.pick !== undefined) dot(ctx, EP.path(lt, [[0.45, w / 2, ys[o.pick] + 120], [0.95, w / 2 - 40, ys[o.pick]], [1.3, w / 2 - 40, ys[o.pick]]]));
  };

  // ---- Trackpad settings (GestureScreen.kt): pointer feel, then every three- and four-finger gesture and its action. ----
  const GESTURES = [
    ['3 fingers', [['Tap', 'Search'], ['Swipe left', 'Desktop left'], ['Swipe right', 'Desktop right'], ['Swipe up', 'Task view'], ['Swipe down', 'Show desktop']]],
    ['4 fingers', [['Tap', 'Notifications'], ['Swipe left', 'App switcher'], ['Swipe right', 'App switcher'], ['Swipe up', 'Task view'], ['Swipe down', 'Show desktop']]],
  ];
  // Ui.slider: the label and value over a 4 dp track, a 20 dp thumb, and the step ticks under it.
  function gslider(ctx, w, pal, y, label, value, f, steps = 26) {
    text(ctx, label, 32, y + 24, { size: 16, color: pal.ink });
    text(ctx, value, w - 32, y + 24, { size: 15, color: pal.dim, align: 'right' });
    const x0 = 42, x1 = w - 42, ty = y + 44;
    fillRR(ctx, x0, ty - 2, x1 - x0, 4, 2, pal.off);
    fillRR(ctx, x0, ty - 2, (x1 - x0) * f, 4, 2, pal.accent);
    ctx.fillStyle = rgba(pal.dim, 0.5);
    for (let i = 0; i <= steps; i++) {
      ctx.beginPath();
      ctx.arc(lerp(x0, x1, i / steps), ty + 18, 1.5, 0, Math.PI * 2);
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
    gslider(ctx, w, pal, y, 'Pointer speed', '×1.0', 0.24);
    ctx.fillStyle = rgba(pal.line);
    ctx.fillRect(32, y + 76, w - 64, 1);
    gslider(ctx, w, pal, y + 77, 'Scroll speed', '×1.0', 0.24);
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
    bar(ctx, w, pal, 'Trackpad', 'Surface settings', null);
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
    bar(ctx, w, pal, 'Media layout', 'Surface settings', null);
    text(ctx, 'Drag a piece to move it. It snaps to the grid and the centre lines.', w / 2, h - BOTTOM - 8, { size: 12, color: pal.dim, align: 'center' });
    dot(ctx, EP.path(lt, [[0.9, w / 2, 0.78 * h], [1.05, w / 2, 0.78 * h], [2.15, w / 2, 0.4 * h], [2.3, w / 2, 0.4 * h]], true));
  };

  // ---- System settings (SettingsScreen.kt): the laptops, how they stay connected, the clipboard. ----
  // o.back is the back label (Devices from its gear, Surface from the surface's settings), o.laptops the rows' names.
  // lt: 0.9 Stay connected in the background is switched on, 2.0 the clipboard sharing, 3.4 Disconnect on the second laptop.
  S.settings = (ctx, w, h, pal, lt, o = {}) => {
    bar(ctx, w, pal, '', o.back || 'Devices', null);
    let y = largeTitle(ctx, pal, 'System settings', TOP + 48);
    y = section(ctx, pal, 'Laptops', y);
    // The card: a laptop row of 64 each (name, "state · transport", Disconnect while anything is going on), a hairline between,
    // and the last row the way on to Devices.
    const rows = [['Work laptop', 'Active · Wi-Fi', true], ['Home PC', 'Connected · Bluetooth', true]];
    const dis = ease((lt - 3.4) / 0.2);
    const total = 2 * 64 + 2 + 52;
    fillRR(ctx, 16, y, w - 32, total, 14, pal.card);
    let cyD = 0;
    rows.forEach(([name, sub, on], i) => {
      const ry = y + i * 65;
      const live = i === 1 ? 1 - dis : 1;
      const cy = laptopTile(ctx, pal, ry, live > 0.5, name, i === 1 && dis > 0.5 ? 'Not connected' : sub, live > 0.5 ? pal.accent : pal.dim);
      if (live > 0.5) {
        const label = 'Disconnect';
        const cw = measure(ctx, label, 15, 'Bold') + 32;
        fillRR(ctx, w - 32 - cw, cy - 16, cw, 32, 16, pal.faint);
        textMid(ctx, label, w - 32 - cw / 2, cy, { size: 15, w: 'Bold', color: pal.ink, align: 'center' });
        if (i === 1) cyD = [w - 32 - cw / 2, cy];
      }
      ctx.fillStyle = rgba(pal.line);
      ctx.fillRect(32, ry + 64, w - 64, 1);
    });
    const ly = y + 2 * 65 + 26;
    textMid(ctx, 'Devices', 32, ly, { size: 16, color: pal.ink });
    icon(ctx, 'chevron_right', w - 28, ly, 24, pal.dim);
    y += total + 24;
    // Reconnect automatically, Stay connected in the background, Show computers only.
    const stay = ease((lt - 0.9) / 0.15), clip = ease((lt - 2.0) / 0.15);
    const ys = card(ctx, w, pal, y, 3);
    toggleRow(ctx, w, pal, ys[0], 'Reconnect automatically', 1);
    toggleRow(ctx, w, pal, ys[1], 'Stay connected in the background', stay);
    toggleRow(ctx, w, pal, ys[2], 'Show computers only', 1);
    y += 3 * 53 - 1;
    y = para(ctx, 'Keeps your laptops connected while Edgepad is minimized, with a notification to disconnect. Off, they disconnect shortly after you leave the app.', 32, y + 8, w - 64, 13, rgba(pal.dim));
    y = section(ctx, pal, 'Clipboard', y);
    const cy = card(ctx, w, pal, y, 1)[0];
    toggleRow(ctx, w, pal, cy, 'Share the clipboard with this laptop', clip);
    y += 52;
    para(ctx, 'What you copy on this laptop arrives here at once. To send a copy from this phone, go back to Edgepad after copying: Android lets Edgepad read the clipboard only while it is on screen, so nothing is sent until you return. Text and images both go, and a large photo is sent smaller. Only the laptop you are controlling shares, and its own clipboard switch must be on as well.', 32, y + 8, w - 64, 13, rgba(pal.dim));
    const sw = w - 32 - 30;
    tap(ctx, lt, 0.9, sw, ys[1], sw - 30, ys[1] + 90);
    tap(ctx, lt, 2.0, sw, cy, sw - 30, cy + 90);
    if (cyD) tap(ctx, lt, 3.4, cyD[0], cyD[1]);
  };

  // ---- The stay-connected notification (link/LinkService.kt): the foreground service's one notification, with its
  // Disconnect action. Small icon ic_laptop, "Connected to <laptop>" over the tap-to-open text, low importance. ----
  // Draws the card with its top-left at (x, y), `w` wide; returns its height. o.others is the count of other laptops.
  EP.linkNotice = (ctx, pal, x, y, w, o = {}) => {
    const name = o.name || 'Work laptop';
    const title = o.others > 0 ? `Connected to ${name} and ${o.others} other laptop${o.others > 1 ? 's' : ''}` : `Connected to ${name}`;
    const body = 'Tap to open Edgepad. With clipboard sharing on, that also sends what you copied.';
    const dark = pal.k < 0.5;
    const lines = wrapLines(ctx, body, w - 32, 14);
    const hgt = 16 + 20 + 8 + 20 + lines.length * 20 + 12 + 40 + 4;
    ctx.save();
    ctx.shadowColor = 'rgba(0,0,0,0.3)';
    ctx.shadowBlur = 16;
    ctx.shadowOffsetY = 4;
    fillRR(ctx, x, y, w, hgt, 24, dark ? pal.card : '#FFFFFF');
    ctx.restore();
    // The header: the small icon in the accent, the app's name.
    icon(ctx, 'laptop', x + 16 + 8, y + 16 + 10, 16, pal.accent);
    text(ctx, 'Edgepad', x + 16 + 24, y + 16 + 15, { size: 12, w: 'Bold', color: rgba(pal.dim) });
    text(ctx, title, x + 16, y + 16 + 20 + 8 + 14, { size: 14, w: 'Bold', color: rgba(pal.ink) });
    lines.forEach((l, i) => text(ctx, l, x + 16, y + 16 + 20 + 8 + 20 + 14 + 6 + i * 20, { size: 14, color: rgba(pal.dim) }));
    // The action: text only on Android 12 and later.
    const ay = y + hgt - 4 - 20;
    const press = o.press || 0;
    if (press > 0) fillRR(ctx, x + 8, ay - 20, measure(ctx, 'Disconnect', 14, 'Bold') + 24, 40, 20, pal.faint, press);
    textMid(ctx, 'Disconnect', x + 20, ay, { size: 14, w: 'Bold', color: pal.accent });
    return hgt;
  };
  // The notification on the phone's ground, near the top, as the shade shows it. lt: 1.4 Disconnect is tapped.
  S.linkNotice = (ctx, w, h, pal, lt, o = {}) => {
    ctx.fillStyle = rgba(pal.bg);
    ctx.fillRect(0, 0, w, h);
    const a = ease((lt - 0.1) / 0.25);
    ctx.save();
    ctx.globalAlpha *= a;
    const y = TOP + 24 + (1 - a) * -16;
    const hgt = EP.linkNotice(ctx, pal, 12, y, w - 24, { ...o, press: tri(lt, 1.4, 0.3) });
    ctx.restore();
    const ay = y + hgt - 24;
    tap(ctx, lt, 1.4, 12 + 20 + 30, ay, 12 + 120, ay + 70);
  };

  // The Page.kt pieces and the finger helpers, for the 5.x screens in screens5a.js and screens5b.js.
  Object.assign(EP.ui, { TOP, BOTTOM, GAP, LEAD, tri, held, within, dot, tap, wrapLines, para, bar, toggle, toggleRow, chip, segmented, button, largeTitle, check, plan, place, mixRect, tile, slider });
})();
