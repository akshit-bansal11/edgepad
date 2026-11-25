// The 5.x screens, part A, ported from screens/*.kt and surface/ControlSurface.kt at 1 unit = 1 dp: Presenter, the transfer card,
// the Clipboard sheet, the Air mouse surface and Settings > Developer. Each is a pure function of its local time `lt`.
(function () {
  const { clamp, lerp, ease, rgba, text, textMid, fillRR, rr, icon, measure } = EP;
  const { TOP, BOTTOM, GAP, bar, section, card, toggleRow, tri, dot, tap, para, wrapLines } = EP.ui;
  const S = EP.screens;

  // Strings, verbatim from res/values (the file is named on each line). Sample data is the only invented text.
  const STR = {
    presenterTitle: 'Presenter', // strings_shortcuts.xml presenter_title
    back: 'Controls', // strings_keyboard.xml back_to_surface
    pointHere: 'Point here', // presenter_point_here
    previous: 'Previous slide', // presenter_previous
    next: 'Next slide', // presenter_next
    paused: 'Paused', // xfer_detail_paused, its first word
    clipTitle: 'Clipboard', // strings_shortcuts.xml clip_history_title
    clear: 'Clear', // strings.xml annotate_clear
    fromLaptop: 'From laptop', // clip_history_from_laptop
    fromPhone: 'From this phone', // clip_history_from_phone
    justNow: 'Just now', // clip_history_just_now
    airPill: 'Air mouse · Off', // strings_air.xml air_pill
    airLeft: 'Left', // air_left
    airRight: 'Right', // air_right
    surfaceSettings: 'Surface settings', // strings.xml settings_surface_title
    clipFiles: 'Clipboard & files', // strings_settings_groups.xml settings_clipboard_files
    shareClipboard: 'Share clipboard', // strings.xml clipboard_share
    shareCaption: 'Copy on one, paste on the other: text and images. To send from this phone, copy, then return to Edgepad.', // clipboard_share_caption
    developer: 'Developer', // settings_developer
    devMode: 'Developer mode', // strings_dashboard.xml developer_mode_title
    devHint: 'Shows Claude Code, Codex and GitHub on this phone and the laptop. Turning it off keeps your connections.', // developer_mode_hint
    devices: 'Devices & connection', // settings_devices
    devicesRow: 'Devices', // strings.xml pairing_title
    reconnect: 'Reconnect automatically', // reconnect_automatically
    reconnectCaption: 'Connects remembered laptops when Edgepad opens, and tries again when a connection drops.', // reconnect_caption
    stay: 'Stay connected in the background', // stay_connected
    stayCaption: 'Keeps your laptops connected while Edgepad is minimized, with a notification to disconnect. Off, they disconnect shortly after you leave the app.', // stay_connected_caption
  };
  // The phone's laser red (AnnotateCanvas.LASER_CORE) and its glow rings.
  const LASER_CORE = '#FF1A1A';
  // Palette.popover and Palette.track, [dark, light] (values-night/colors.xml, values/colors.xml).
  const POPOVER = [[34, 35, 40], [251, 251, 253]];
  const TRACK = [[107, 109, 117], [135, 138, 146]];
  const themed = (pair, pal) => EP.mixRgb(pair[0], pair[1], pal.k);
  // A TextView's line height as a multiple of the size (Page.kt's LEAD 1.64 is Type.LEADING 1.4 on this).
  const FACE_LINE = 1.17;
  // Edgepad's own file and stop icons (res/drawable/ic_file.xml, ic_stop.xml), 24 x 24, stroke 1.5.
  const FILE = new Path2D('M14 2.5H7a2 2 0 0 0-2 2v15a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V7.5ZM14 2.5V7.5H19');
  const STOP = new Path2D('M7.5 6H16.5A1.5 1.5 0 0 1 18 7.5V16.5A1.5 1.5 0 0 1 16.5 18H7.5A1.5 1.5 0 0 1 6 16.5V7.5A1.5 1.5 0 0 1 7.5 6Z');
  const appPath = (ctx, p, cx, cy, size, color, a = 1) => {
    ctx.save();
    ctx.globalAlpha *= a;
    ctx.translate(cx - size / 2, cy - size / 2);
    ctx.scale(size / 24, size / 24);
    ctx.lineWidth = 1.5;
    ctx.lineCap = ctx.lineJoin = 'round';
    ctx.strokeStyle = rgba(color);
    ctx.stroke(p);
    ctx.restore();
  };
  // The surface behind a floating card or dialog, as S.controls draws it.
  const surfaceUnder = (ctx, w, h, pal, o) => EP.drawSurface(ctx, w, h, { pal, radius: 44, dials: {}, fingers: [], trail: [], ...(o.under || {}) });

  // ---- Presenter (PresenterScreen.kt, LaserSizes.kt, AnnotateCanvas.kt laser mode) ----
  // The nav bar (Controls back link, "Presenter", the options icon), then the stage: the laser pad, a 16 : 10 card fitted and
  // centred in it (Letterbox.fit), with "Point here" until a finger is down. Upright, Previous (1 part) and Next (2 parts) are a
  // 96 dp row under the pad; sideways (w > h) they are full-height columns of 15 parts either side of the pad's 70, drawn as
  // the chevrons. lt: a tap on Next at every time in o.next, the finger arriving 0.4 s before and leaving 0.2 s after.
  // o: next [lt, ...] times Next is tapped (default none); thumb {x, y} 0..1 on the pad, or null: the laser dot under a finger
  // (hint hidden, "show taps" disc on it; the laptop draws the laser itself); size 10|15|20 the laser size (LaserSizes.SMALL,
  // MEDIUM, LARGE; default 15).
  S.presenter = (ctx, w, h, pal, lt, o = {}) => {
    ctx.fillStyle = rgba(pal.bg);
    ctx.fillRect(0, 0, w, h);
    bar(ctx, w, pal, STR.presenterTitle, STR.back);
    icon(ctx, 'sliders_horizontal', w - 28, TOP + 24, 24, pal.accent);
    const wide = w > h;
    const y0 = TOP + 48 + GAP, end = h - BOTTOM;
    const side = 16;
    let stage, prev, next;
    if (wide) {
      const sh = end - y0 - GAP, unit = (w - 2 * side - 2 * GAP) / 100;
      prev = { x: side, y: y0, w: 15 * unit, h: sh };
      stage = { x: side + 15 * unit + GAP, y: y0, w: 70 * unit, h: sh };
      next = { x: stage.x + stage.w + GAP, y: y0, w: 15 * unit, h: sh };
    } else {
      const rowH = 96, rowTop = end - GAP - rowH, avail = w - 2 * side - GAP;
      stage = { x: side, y: y0, w: w - 2 * side, h: rowTop - GAP - y0 };
      prev = { x: side, y: rowTop, w: avail / 3, h: rowH };
      next = { x: side + avail / 3 + GAP, y: rowTop, w: (2 * avail) / 3, h: rowH };
    }
    // The pad: the laptop's 16 : 10 screen fitted inside the stage.
    const scale = Math.min(stage.w / 16, stage.h / 10);
    const box = { w: 16 * scale, h: 10 * scale };
    box.x = stage.x + (stage.w - box.w) / 2;
    box.y = stage.y + (stage.h - box.h) / 2;
    ctx.fillStyle = rgba(pal.card);
    ctx.fillRect(box.x, box.y, box.w, box.h);
    const th = o.thumb;
    if (th) {
      const x = box.x + th.x * box.w, y = box.y + th.y * box.h;
      const r = box.h * 0.0005 * (o.size ?? 15) * 2.5; // LaserSizes.SHARE x size x FINGER_SCALE
      ctx.fillStyle = LASER_CORE;
      for (const [k, a] of [[2.2, 40 / 255], [1.6, 90 / 255], [1, 1]]) {
        ctx.globalAlpha = a;
        ctx.beginPath();
        ctx.arc(x, y, r * k, 0, Math.PI * 2);
        ctx.fill();
      }
      ctx.globalAlpha = 1;
      EP.tapDot(ctx, x, y, 1);
    } else {
      text(ctx, STR.pointHere, stage.x + stage.w / 2, stage.y + stage.h / 2, { size: 16, color: pal.dim, align: 'center' });
    }
    const taps = o.next || [];
    const nextPress = Math.max(0, ...taps.map((t) => tri(lt, t, 0.3)));
    const key = (r, label, style, press, size) => {
      fillRR(ctx, r.x, r.y, r.w, r.h, 14, style === 'FILLED' ? pal.accent : pal.accentSoft);
      if (press > 0) fillRR(ctx, r.x, r.y, r.w, r.h, 14, pal.faint, press * 0.6);
      textMid(ctx, label, r.x + r.w / 2, r.y + r.h / 2, { size, w: 'Bold', color: style === 'FILLED' ? pal.onAccent : pal.accent, align: 'center' });
    };
    key(prev, wide ? '‹' : STR.previous, 'OUTLINED', 0, wide ? 40 : 17); // PresenterScreen.CHEVRON_SP
    key(next, wide ? '›' : STR.next, 'FILLED', nextPress, wide ? 40 : 17);
    for (const t of taps) tap(ctx, lt, t, next.x + next.w / 2, next.y + next.h / 2);
  };

  // ---- Transfer card (XferCard.kt, XferCardView.kt, MainActivity.showCard) ----
  // The card floats at the bottom end of the whole screen (a PopupWindow on the stage) over whatever is there: the control
  // surface. Android's file picker is the system's and is not drawn: o.pick fades the card in at its first file instead.
  // Running: file icon, the file's name, "2 of 3 · 46%" (the app also prints sizes and a speed here; the film prints the percent
  // alone), a 4 dp bar, then Pause (Resume when held), Stop and Close. Done: "Sent 3 files" and Close.
  // lt: nothing of its own, the director drives o. o.pressPause / o.pressResume: lt a finger taps that button (it comes in 0.4 s
  // before, and the button flashes); the state change itself is o.paused.
  // o: under (surface state, as controls), pick 0..1 (the card fades in, default 1), index (default 2), count (3), name
  // ('slides.pdf'), pct 0..100 (46), paused 0..1 (0; the icon goes Pause -> Resume, the bar and icon grey, "Paused ·" is added),
  // done 0..1 (0; cross-fades to "Sent 3 files" with Close alone), pressPause, pressResume.
  S.xfer = (ctx, w, h, pal, lt, o = {}) => {
    surfaceUnder(ctx, w, h, pal, o);
    const pick = ease(o.pick ?? 1), paused = o.paused ?? 0, done = ease(o.done ?? 0);
    const count = o.count ?? 3, pct = o.pct ?? 46;
    const cw = Math.min(w - 32, 352), ch = 72; // MAX_WIDTH_DP; 12 + the 48 dp buttons + 12
    const x = w - 16 - cw, y = h - 16 - ch, cy = y + ch / 2;
    ctx.save();
    ctx.globalAlpha *= pick;
    ctx.shadowColor = 'rgba(0,0,0,0.35)';
    ctx.shadowBlur = 16;
    ctx.shadowOffsetY = 4;
    fillRR(ctx, x, y, cw, ch, 14, rgba(themed(POPOVER, pal)));
    ctx.shadowColor = 'transparent';
    const accent = EP.mixRgb(pal.accent, pal.dim, paused);
    appPath(ctx, FILE, x + 14 + 12, cy, 24, accent);
    const bx0 = x + cw - 4 - 144; // three 48 dp buttons, 4 dp from the end
    const px = bx0 + 24, sx = bx0 + 72, cx = bx0 + 120;
    // Running words: the name, the detail line and the bar, in the column between the icon and the buttons.
    const wx = x + 14 + 24 + 10, ww = bx0 - wx;
    const run = 1 - done;
    if (run > 0) {
      const line = 15 * FACE_LINE, sub = 12 * FACE_LINE, top = cy - (line + sub + 4 + 4) / 2;
      const figures = paused > 0.5 ? `${STR.paused} · ${Math.round(pct)}%` : `${Math.round(pct)}%`;
      text(ctx, o.name ?? 'slides.pdf', wx, top + line * 0.8, { size: 15, w: 'Bold', color: pal.ink, a: run });
      text(ctx, `${o.index ?? 2} of ${count} · ${figures}`, wx, top + line + sub * 0.8, { size: 12, color: pal.dim, a: run });
      const by = top + line + sub + 4;
      fillRR(ctx, wx, by, ww, 4, 2, pal.faint, run);
      fillRR(ctx, wx, by, ww * clamp(pct / 100), 4, 2, EP.mixRgb(pal.accent, themed(TRACK, pal), paused), run);
      const press = (t) => (t === undefined ? 0 : tri(lt, t, 0.3));
      const flash = Math.max(press(o.pressPause), press(o.pressResume));
      if (flash > 0) fillRR(ctx, bx0, cy - 24, 48, 48, 8, pal.faint, flash * run);
      const hold = paused > 0.5;
      icon(ctx, hold ? 'play' : 'pause', px, cy, 24, pal.ink, run);
      appPath(ctx, STOP, sx, cy, 18, pal.danger, run);
    }
    if (done > 0) {
      const n = count;
      text(ctx, `Sent ${n} ${n === 1 ? 'file' : 'files'}`, wx, cy + 15 * 0.35, { size: 15, w: 'Bold', color: pal.ink, a: done });
    }
    icon(ctx, 'close', cx, cy, 24, pal.ink);
    ctx.restore();
    if (o.pressPause !== undefined) tap(ctx, lt, o.pressPause, px, cy);
    if (o.pressResume !== undefined) tap(ctx, lt, o.pressResume, px, cy);
  };

  // ---- Clipboard sheet (ClipboardSheet.kt) ----
  // The app shows it as a centred dialog, not a bottom sheet: a 14 dp card at most 360 wide, a "Clipboard" header with a quiet
  // Clear on its end, then one faint card per copy (the preview trimmed to 3 lines, then "From laptop · Just now"). It grows
  // from 0.85 to 1 with a fade (Carry.reveal, REVEAL 300 ms) over a 60% dim. lt: nothing of its own.
  // o: under (surface state, as controls), open 0..1 (default 1), rows [{text, src: 'laptop' | 'phone', age}] (default four
  // neutral rows), pivot {x, y} the point it grows from (default the screen centre).
  const ROWS = [
    { text: 'Meeting notes', src: 'laptop', age: STR.justNow },
    { text: '- dials on every corner', src: 'phone', age: '2 min. ago' },
    { text: 'https://example.com/roadmap', src: 'laptop', age: '5 min. ago' },
    { text: 'Lunch at one', src: 'phone', age: '1 hr. ago' },
  ];
  S.clipboard = (ctx, w, h, pal, lt, o = {}) => {
    surfaceUnder(ctx, w, h, pal, o);
    const open = ease(o.open ?? 1);
    if (open <= 0) return;
    const rows = o.rows || ROWS;
    const dw = Math.min(w - 32, 360), pad = 16, inner = dw - 2 * pad - 2 * 12;
    const cards = rows.map((r) => {
      const lines = wrapLines(ctx, r.text, inner, 16).slice(0, 3);
      return { r, lines, h: Math.max(48, 12 + lines.length * 16 * FACE_LINE + 4 + 13 * FACE_LINE + 12) };
    });
    const dh = pad + 52 + cards.reduce((s, c) => s + c.h + GAP, 0) + pad;
    const dx = (w - dw) / 2, dy = (h - dh) / 2;
    ctx.save();
    ctx.globalAlpha *= open;
    ctx.fillStyle = 'rgba(0,0,0,0.6)'; // the dialog's default window dim
    ctx.fillRect(0, 0, w, h);
    const pv = o.pivot || { x: w / 2, y: h / 2 }, sc = lerp(0.85, 1, open);
    ctx.translate(pv.x, pv.y);
    ctx.scale(sc, sc);
    ctx.translate(-pv.x, -pv.y);
    fillRR(ctx, dx, dy, dw, dh, 14, pal.card);
    text(ctx, STR.clipTitle, dx + pad, dy + pad + 26 + 13 * 0.35, { size: 13, w: 'Bold', color: pal.dim });
    text(ctx, STR.clear, dx + dw - pad - 16, dy + pad + 26 + 17 * 0.35, { size: 17, w: 'Bold', color: pal.accent, align: 'right' });
    let y = dy + pad + 52;
    for (const c of cards) {
      fillRR(ctx, dx + pad, y, dw - 2 * pad, c.h, 14, pal.faint);
      c.lines.forEach((l, i) => text(ctx, l, dx + pad + 12, y + 12 + (i + 0.5) * 16 * FACE_LINE + 16 * 0.35, { size: 16, color: pal.ink }));
      const meta = `${c.r.src === 'laptop' ? STR.fromLaptop : STR.fromPhone} · ${c.r.age}`; // clip_history_meta
      text(ctx, meta, dx + pad + 12, y + 12 + c.lines.length * 16 * FACE_LINE + 4 + 13 * FACE_LINE * 0.8, { size: 13, color: pal.dim });
      y += c.h + GAP;
    }
    ctx.restore();
  };

  // ---- Air mouse surface (ControlSurface.kt:144-212, 631-664, 917-931) ----
  // Air mode shows only the pad (Backdrop.kt), the pill at 36 dp from the top and the two drag buttons: upright a row along the
  // bottom (7% in, 40% wide each, 88 dp tall, 32 up), sideways a strip down the right edge (104 wide, Left 60% of the height).
  // The pill's one string is "Air mouse · Off": it is the control that ends Air mouse, so there is no "on" wording to draw.
  // A held button fills with the accent, its edge goes from 2 to 4 dp in the accent and its label to the on-accent colour.
  // lt: nothing of its own.
  // o: on 0..1 (the pill and both buttons fade in, default 1), left 0..1 and right 0..1 (that button held, with a "show taps"
  // disc on it), thumb {x, y, a} (px, optional: a resting thumb on the pad, for the tilt).
  S.air = (ctx, w, h, pal, lt, o = {}) => {
    EP.drawBackdrop(ctx, w, h, pal);
    const on = ease(o.on ?? 1);
    ctx.save();
    ctx.globalAlpha *= on;
    const tint = (a) => rgba(pal.accent, 0.14 * a);
    const hw = measure(ctx, STR.airPill, 13, 'Bold') / 2 + 14, hh = 13 / 2 + 7; // CAPTION_PAD_X/Y
    fillRR(ctx, w / 2 - hw, 36 - hh, 2 * hw, 2 * hh, hh, tint(1));
    textMid(ctx, STR.airPill, w / 2, 36, { size: 13, w: 'Bold', color: pal.accent, align: 'center' });
    let left, right;
    if (w > h) {
      const r = w - 24, l = r - 104, top = h * 0.14, room = h * (1 - 2 * 0.14) - 12;
      left = { x: l, y: top, w: 104, h: room * 0.6 };
      right = { x: l, y: left.y + left.h + 12, w: 104, h: top + room + 12 - (left.y + left.h + 12) };
    } else {
      const bottom = h - 32, top = bottom - 88, edge = w * 0.07, each = w * 0.4;
      left = { x: edge, y: top, w: each, h: 88 };
      right = { x: w - edge - each, y: top, w: each, h: 88 };
    }
    const drag = (r, label, held) => {
      fillRR(ctx, r.x, r.y, r.w, r.h, 20, tint(1));
      fillRR(ctx, r.x, r.y, r.w, r.h, 20, pal.accent, held);
      rr(ctx, r.x, r.y, r.w, r.h, 20);
      ctx.lineWidth = lerp(2, 4, held);
      ctx.strokeStyle = rgba(EP.mixRgb(pal.dim, pal.accent, held));
      ctx.stroke();
      textMid(ctx, label, r.x + r.w / 2, r.y + r.h / 2, { size: 13, w: 'Bold', color: EP.mixRgb(pal.ink, pal.onAccent, held), align: 'center' });
    };
    drag(left, STR.airLeft, ease(o.left ?? 0));
    drag(right, STR.airRight, ease(o.right ?? 0));
    ctx.restore();
    for (const [r, k] of [[left, o.left ?? 0], [right, o.right ?? 0]]) if (k > 0.02) EP.tapDot(ctx, r.x + r.w / 2, r.y + r.h / 2, ease(k));
    if (o.thumb) EP.tapDot(ctx, o.thumb.x, o.thumb.y, o.thumb.a ?? 1);
  };

  // ---- Settings > Developer (SettingsScreen.kt:135, strings_dashboard.xml) ----
  // The upright Settings page scrolled to its second run: the "Clipboard & files" card, the Developer card with the Developer mode
  // switch (editable: a laptop is connected, so the hint line shows, not the offline one) and its footnote, then Devices &
  // connection. The bar's back link is "Surface settings"; the large title has scrolled away.
  // lt: 0.9 the Developer mode switch is tapped (the finger arrives 0.4 s before); the switch itself follows o.on.
  // o: on 0..1 (the switch, default 0).
  S.devSwitch = (ctx, w, h, pal, lt, o = {}) => {
    ctx.fillStyle = rgba(pal.bg);
    ctx.fillRect(0, 0, w, h);
    const note = (s, y) => para(ctx, s, 32, y + GAP, w - 64, 13, pal.dim);
    const link = (cy, label) => {
      textMid(ctx, label, 32, cy, { size: 16, color: pal.ink });
      icon(ctx, 'chevron_right', w - 44, cy, 24, pal.dim);
    };
    let y = section(ctx, pal, STR.clipFiles, TOP + 48);
    toggleRow(ctx, w, pal, card(ctx, w, pal, y, 1)[0], STR.shareClipboard, 1);
    y = note(STR.shareCaption, y + 52);
    y = section(ctx, pal, STR.developer, y);
    const devY = card(ctx, w, pal, y, 1)[0];
    toggleRow(ctx, w, pal, devY, STR.devMode, ease(o.on ?? 0));
    y = note(STR.devHint, y + 52);
    y = section(ctx, pal, STR.devices, y);
    link(card(ctx, w, pal, y, 1)[0], STR.devicesRow);
    y += 52 + 24;
    toggleRow(ctx, w, pal, card(ctx, w, pal, y, 1)[0], STR.reconnect, 1);
    y = note(STR.reconnectCaption, y + 52) + 24;
    toggleRow(ctx, w, pal, card(ctx, w, pal, y, 1)[0], STR.stay, 1);
    note(STR.stayCaption, y + 52);
    bar(ctx, w, pal, '', STR.surfaceSettings);
    tap(ctx, lt, 0.9, w - 62, devY);
  };
})();
