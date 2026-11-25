// The laptop, its screens, the room and the hand (lane E). Loads after kit.js, screens*.js and game.js, before beats.js and film.js.
// Ported, one block each:
//   N2  the laser: windows/src/Edgepad/Annotation/AnnotationOverlay.cs:160-225 (DrawLaser: a 250 ms tail-to-head alpha-gradient outline,
//       a glow of 1.8 x the radius at alpha 140/255, the core, a hot centre of 0.35 x the radius) and shared/Edgepad.Core/Annotation/
//       LaserGeometry.cs (TrailAlpha 0.9 at the head, 0 at the tail; half-width = radius at the head, 0 at the tail). The radius is
//       Stroke.LaserRadiusPx: 0.05 % of the screen height per width step; Medium is 15 (android/.../LaserSizes.kt), so 0.75 % of the height.
//   N4  the transfer toast: windows/src/Edgepad/Ui/TransferToast.cs (card 380 x 76 running / 64 done at 96 dpi, corner 12, mark 32 at 16,
//       text at 60, three 32 buttons from the right with 2 gaps, a 4 px track at y 58) with its words from windows/src/Edgepad/Ui/Strings.resx
//       (Xfer_PanelSending, Xfer_PanelCount, Xfer_PanelHeld, Xfer_SentOne, Xfer_SentMany). Percent only: the speed never shows.
//   N13 the sleep countdown: windows/src/Edgepad/Dispatch/ShutdownCountdown.cs (a fixed dialog, text at 16,16 under 360 wide, Cancel at 280,64,
//       the seconds in the sentence) with Cancel from Strings.resx (EndAction_Cancel) and the title from the Android string end_sleep.
// Everything else (the desktop, the windows, the deck, the clip, the editor, the browser, the credits) is neutral invented furniture.
// Every frame is a pure function of t; nothing is carried between frames.
(function () {
  const { clamp, lerp, ease, easeOut, easeInOut, seg, text, textMid, measure, rr, fillRR, icon, TOK } = EP;

  const SW = 1280;
  const SH = 800;

  // ---- colours, each defined once ----
  const INK = TOK.INK;
  const DIM = TOK.GREY;
  const ACCENT = TOK.BLUE;
  const DESK_TOP = '#202733';
  const DESK_BOT = '#0F1217';
  const TASKBAR = 'rgba(8,9,12,0.88)';
  const WIN = '#23262E';
  const WIN_BAR = '#2C313B';
  const WIN_RIM = 'rgba(255,255,255,0.10)';
  const WIN_SHADOW = 'rgba(0,0,0,0.5)';
  const BAR_DIM = 'rgba(255,255,255,0.14)';
  const SEL = 'rgba(77,163,255,0.35)';
  const FOLDER = '#3C78C2';
  const FOLDER_HI = '#6FA8EC';
  const PAGE = '#E9ECF2';
  const PAGE_LINE = '#B8BEC9';
  const TERM_BG = '#0D0F13';
  const CODE_BG = '#14161B';
  const GUTTER = '#5C6070';
  const SYN_KEY = '#C792EA';
  const SYN_STR = '#9BD6A5';
  const SYN_FN = '#F2C98A';
  const SLIDE_BAR = '#D5D9E2';
  const SKY_TOP = '#2D5E9E';
  const SKY_LOW = '#F4B98A';
  const SUN = '#FFE2A8';
  const HILL_FAR = '#2E4A6B';
  const HILL_NEAR = '#14202E';
  const CHECK_GREEN = '#3DD68C';
  const DIFF_ADD = 'rgba(61,214,140,0.20)';
  const DIFF_DEL = 'rgba(255,105,97,0.20)';
  const CREDITS_BG = '#08090B';
  const DIALOG_BG = '#F0F0F0';
  const DIALOG_BAR = '#FFFFFF';
  const DIALOG_INK = '#1A1A1A';
  const DIALOG_BTN = '#E1E1E1';
  const DIALOG_BTN_RIM = '#ADADAD';
  const CURSOR_FILL = '#FFFFFF';
  const CURSOR_RIM = '#0B0B0E';
  const FLASH = '#FFFFFF';
  const LASER_CORE = [255, 26, 26]; // AnnotationOverlay.cs CoreColour
  const LASER_HOT = [255, 208, 208]; // HotColour
  const GLINT = [200, 220, 255];
  const SCREEN_OFF = '#07080A';
  const DECK_BOT = '#212328';
  const DECK_NOTCH = '#1A1C20';
  const STAND_SLAB = '#30333B';
  const STAND_CABINET = '#23262C';
  const STAND_GAP = '#1A1C21';
  const FLOOR_TOP = '#1B1D23';
  const SKIRT = 'rgba(255,255,255,0.05)';
  const LAMP = [255, 217, 168];
  const TV_BODY = '#08090B';
  const TV_RIM = '#2E3138';
  const BEAM = [207, 227, 255];
  const HAND_RIM = 'rgba(255,255,255,0.10)';
  const HAND_SHADOW = 'rgba(0,0,0,0.45)';
  const GLOW_DEFAULT = [77, 163, 255];
  const PAL = EP.palette(0); // the toast follows the dark theme

  // ---- numbers ----
  const BAR = 60; // a window's title bar
  const TASK_H = 64;
  const LASER_SHARE = 0.0005 * 15; // Stroke.LaserRadiusPx for Medium (15): 0.05 % of the height per step
  const LASER_BOOST = 2.5; // the film enlarges the dot so it reads on a 640 px screen
  const TRAIL_ALPHA = 0.9; // LaserGeometry.TrailAlpha at the head
  const TOAST_S = 2; // dp to logical px: the card's 14 dp title reads at 28
  const DIALOG_S = 2.4;

  const font = (s, w) => ({ size: s, w, color: INK });

  // ---- shared pieces ----
  function wallpaper(ctx) {
    const g = ctx.createLinearGradient(0, 0, 0, SH);
    g.addColorStop(0, DESK_TOP);
    g.addColorStop(1, DESK_BOT);
    ctx.fillStyle = g;
    ctx.fillRect(0, 0, SW, SH);
  }
  // A plain bar with three dots, at most.
  function taskbar(ctx) {
    ctx.fillStyle = TASKBAR;
    ctx.fillRect(0, SH - TASK_H, SW, TASK_H);
    for (let i = -1; i <= 1; i++) {
      ctx.beginPath();
      ctx.arc(SW / 2 + i * 56, SH - TASK_H / 2, 9, 0, Math.PI * 2);
      ctx.fillStyle = DIM;
      ctx.fill();
    }
  }
  // Opens a window at (x, y, w, h) scaled s about its centre; the caller draws in window-local coordinates and then ctx.restore().
  function windowOpen(ctx, x, y, w, h, title, a = 1, s = 1, brand = null) {
    ctx.save();
    ctx.globalAlpha *= a;
    ctx.translate(x + w / 2, y + h / 2);
    ctx.scale(s, s);
    ctx.translate(-w / 2, -h / 2);
    ctx.save();
    ctx.shadowColor = WIN_SHADOW;
    ctx.shadowBlur = 40;
    ctx.shadowOffsetY = 14;
    fillRR(ctx, 0, 0, w, h, 16, WIN);
    ctx.restore();
    ctx.save();
    rr(ctx, 0, 0, w, h, 16);
    ctx.clip();
    ctx.fillStyle = WIN_BAR;
    ctx.fillRect(0, 0, w, BAR);
    ctx.restore();
    rr(ctx, 0, 0, w, h, 16);
    ctx.lineWidth = 1.5;
    ctx.strokeStyle = WIN_RIM;
    ctx.stroke();
    if (brand) EP.brand(ctx, brand, 38, BAR / 2, 34, INK);
    textMid(ctx, title, brand ? 68 : 24, BAR / 2, font(30, 'Bold'));
  }
  function cursor(ctx, x, y) {
    ctx.save();
    ctx.translate(x, y);
    ctx.scale(1.4, 1.4);
    ctx.beginPath();
    ctx.moveTo(0, 0);
    ctx.lineTo(0, 32);
    ctx.lineTo(8, 24);
    ctx.lineTo(14, 38);
    ctx.lineTo(20, 35);
    ctx.lineTo(14, 22);
    ctx.lineTo(26, 22);
    ctx.closePath();
    ctx.lineJoin = 'round';
    ctx.lineWidth = 2.5;
    ctx.strokeStyle = CURSOR_RIM;
    ctx.stroke();
    ctx.fillStyle = CURSOR_FILL;
    ctx.fill();
    ctx.restore();
  }
  function folder(ctx, cx, cy, glow) {
    fillRR(ctx, cx - 60, cy - 52, 56, 26, 8, FOLDER);
    fillRR(ctx, cx - 60, cy - 36, 120, 88, 12, FOLDER);
    ctx.save();
    ctx.globalAlpha *= glow;
    fillRR(ctx, cx - 60, cy - 36, 120, 88, 12, FOLDER_HI);
    ctx.restore();
    text(ctx, 'Docs', cx, cy + 96, { ...font(28, 'Regular'), align: 'center' });
  }
  function file(ctx, cx, cy, lift, a) {
    ctx.save();
    ctx.globalAlpha *= a;
    ctx.translate(cx, cy - lift * 10);
    ctx.scale(1 + lift * 0.14, 1 + lift * 0.14);
    if (lift > 0) {
      ctx.save();
      ctx.shadowColor = WIN_SHADOW;
      ctx.shadowBlur = 26;
      ctx.shadowOffsetY = 12;
      fillRR(ctx, -34, -44, 68, 88, 8, PAGE);
      ctx.restore();
    }
    fillRR(ctx, -34, -44, 68, 88, 8, PAGE);
    fillRR(ctx, -20, -20, 40, 6, 3, PAGE_LINE);
    fillRR(ctx, -20, -4, 40, 6, 3, PAGE_LINE);
    fillRR(ctx, -20, 12, 26, 6, 3, PAGE_LINE);
    text(ctx, 'file.txt', 0, 96, { ...font(28, 'Regular'), align: 'center' });
    ctx.restore();
  }

  // ---- the kinds ----
  const KINDS = {};

  // 'race': o.tr race seconds, o.mode 'attract' | 'race'. The game is another lane's.
  KINDS.race = (ctx, t, o) => EP.game.draw(ctx, SW, SH, o.tr, o.mode);

  // 'desktop': a neutral dark desktop with three plain windows (a notes list, a files window, a chart).
  //   o.pointer {x, y} fractions: the cursor; o.zoom 1..1.6 zooms the whole desktop about the pointer (centre when absent);
  //   o.drag {k 0..1, held 0..1}: the file icon moves from its place to the folder as k runs 0..1 (held lifts it with a shadow;
  //   when held drops back to 0 at k = 1 the file is dropped into the folder and fades);
  //   o.terminal 0..1: a terminal window opens (scale 0.92 -> 1, fade), the gnometerminal icon in its title bar, a generic `$` prompt.
  const FOLDER_AT = { x: 600, y: 250 };
  const FILE_AT = { x: 790, y: 250 };
  KINDS.desktop = (ctx, t, o) => {
    const z = o.zoom || 1;
    const px = (o.pointer ? o.pointer.x : 0.5) * SW;
    const py = (o.pointer ? o.pointer.y : 0.5) * SH;
    ctx.save();
    ctx.translate(px, py);
    ctx.scale(z, z);
    ctx.translate(-px, -py);
    wallpaper(ctx);
    windowOpen(ctx, 56, 48, 380, 300, 'Notes');
    [300, 240, 280, 200].forEach((w, i) => fillRR(ctx, 28, BAR + 34 + i * 48, w, 14, 7, BAR_DIM));
    ctx.restore();
    windowOpen(ctx, 470, 70, 420, 360, 'Files');
    ctx.restore();
    folder(ctx, FOLDER_AT.x, FOLDER_AT.y, o.drag ? ease(seg(o.drag.k, 0.8, 1)) * o.drag.held : 0);
    const d = o.drag;
    const k = d ? ease(d.k) : 0;
    const held = d ? d.held : 0;
    file(ctx, lerp(FILE_AT.x, FOLDER_AT.x, k), lerp(FILE_AT.y, FOLDER_AT.y, k), held, d ? 1 - (1 - held) * ease(seg(d.k, 0.95, 1)) : 1);
    windowOpen(ctx, 920, 48, 310, 300, 'Chart');
    [90, 150, 120, 190].forEach((h, i) => {
      ctx.globalAlpha = 0.55 + i * 0.15;
      fillRR(ctx, 32 + i * 64, 300 - 28 - h, 40, h, 8, ACCENT);
    });
    ctx.restore();
    taskbar(ctx);
    if (o.terminal > 0) {
      const k2 = clamp(o.terminal);
      windowOpen(ctx, 230, 330, 780, 380, 'Terminal', k2, lerp(0.92, 1, easeOut(k2)), 'gnometerminal');
      ctx.save();
      rr(ctx, 0, 0, 780, 380, 16);
      ctx.clip();
      ctx.fillStyle = TERM_BG;
      ctx.fillRect(0, BAR, 780, 380 - BAR);
      ctx.restore();
      text(ctx, '$', 28, BAR + 56, { size: 32, w: 'Bold', color: ACCENT });
      text(ctx, 'ls', 60, BAR + 56, font(32, 'Regular'));
      text(ctx, 'Docs   file.txt', 28, BAR + 110, font(32, 'Regular'));
      text(ctx, '$', 28, BAR + 164, { size: 32, w: 'Bold', color: ACCENT });
      if (Math.floor(t * 2) % 2 === 0) fillRR(ctx, 60, BAR + 138, 16, 36, 2, INK);
      ctx.restore();
    }
    ctx.restore();
  };

  // 'notes': a note window. o.text the string typed so far (may contain a newline: lines are split on it), o.caret bool (blinks from t),
  //   o.sel 0..1 selects the second line (blue wash), o.copied 0..1 a "Copied" chip in the window's top right,
  //   o.pasted 0..1 the second line appears again below the last (rises 12 px as it fades in).
  KINDS.notes = (ctx, t, o) => {
    wallpaper(ctx);
    const wx = 120;
    const wy = 70;
    const ww = 1040;
    windowOpen(ctx, wx, wy, ww, 640, 'Notes');
    const lines = (o.text || '').split('\n');
    const row = (i) => BAR + 96 + i * 84;
    const size = (i) => (i === 0 ? 52 : 44);
    const weight = (i) => (i === 0 ? 'Bold' : 'Regular');
    if (o.sel > 0 && lines.length > 1) {
      const sw = measure(ctx, lines[1], size(1), weight(1));
      ctx.save();
      ctx.globalAlpha *= clamp(o.sel);
      fillRR(ctx, 40, row(1) - 50, sw + 24, 68, 10, SEL);
      ctx.restore();
    }
    lines.forEach((s, i) => text(ctx, s, 52, row(i), { size: size(i), w: weight(i), color: INK }));
    if (o.pasted > 0 && lines.length > 1) {
      const p = ease(o.pasted);
      text(ctx, lines[1], 52, row(lines.length) + (1 - p) * 12, { size: size(1), w: weight(1), color: INK, a: p });
    }
    if (o.caret && Math.floor(t * 2) % 2 === 0) {
      const last = lines.length - 1;
      fillRR(ctx, 52 + measure(ctx, lines[last], size(last), weight(last)) + 6, row(last) - 44, 4, 54, 2, ACCENT);
    }
    ctx.restore();
    if (o.copied > 0) {
      const c = ease(o.copied);
      ctx.save();
      ctx.globalAlpha *= c;
      ctx.translate(wx + ww - 232, wy + BAR + 24 + (1 - c) * 10);
      fillRR(ctx, 0, 0, 200, 56, 28, TOK.PILL);
      icon(ctx, 'check', 34, 28, 26, ACCENT);
      textMid(ctx, 'Copied', 120, 28, font(30, 'Bold'));
      ctx.restore();
    }
  };

  // The laser, ported from AnnotationOverlay.DrawLaser. L.x, L.y fractions of the screen; L.trail oldest first
  // [{x, y, age 0..1}] where age is the share of the 250 ms window. The trail is one filled outline, tapering to nothing, with a
  // tail-to-head alpha gradient; then the glow, the red core and the hot centre.
  function laser(ctx, L) {
    const R = SH * LASER_SHARE * LASER_BOOST;
    const hx = L.x * SW;
    const hy = L.y * SH;
    const pts = (L.trail || []).map((q) => ({ x: q.x * SW, y: q.y * SH, age: clamp(q.age) }));
    if (pts.length >= 2 && Math.hypot(hx - pts[0].x, hy - pts[0].y) >= 1) {
      const left = [];
      const right = [];
      pts.forEach((q, i) => {
        const a = pts[Math.max(i - 1, 0)];
        const b = pts[Math.min(i + 1, pts.length - 1)];
        const dx = b.x - a.x;
        const dy = b.y - a.y;
        const len = Math.hypot(dx, dy) || 1;
        const hw = R * (1 - q.age);
        left.push([q.x - (dy / len) * hw, q.y + (dx / len) * hw]);
        right.push([q.x + (dy / len) * hw, q.y - (dx / len) * hw]);
      });
      ctx.beginPath();
      left.forEach(([x, y], i) => (i ? ctx.lineTo(x, y) : ctx.moveTo(x, y)));
      for (let i = right.length - 1; i >= 0; i--) ctx.lineTo(right[i][0], right[i][1]);
      ctx.closePath();
      const g = ctx.createLinearGradient(pts[0].x, pts[0].y, hx, hy);
      g.addColorStop(0, EP.rgba(LASER_CORE, TRAIL_ALPHA * (1 - pts[0].age)));
      g.addColorStop(1, EP.rgba(LASER_CORE, TRAIL_ALPHA));
      ctx.fillStyle = g;
      ctx.fill();
    }
    const glow = R * 1.8;
    const g = ctx.createRadialGradient(hx, hy, 0, hx, hy, glow);
    g.addColorStop(0, EP.rgba(LASER_CORE, 140 / 255));
    g.addColorStop(1, EP.rgba(LASER_CORE, 0));
    ctx.fillStyle = g;
    ctx.beginPath();
    ctx.arc(hx, hy, glow, 0, Math.PI * 2);
    ctx.fill();
    ctx.beginPath();
    ctx.arc(hx, hy, R, 0, Math.PI * 2);
    ctx.fillStyle = EP.rgba(LASER_CORE);
    ctx.fill();
    ctx.beginPath();
    ctx.arc(hx, hy, R * 0.35, 0, Math.PI * 2);
    ctx.fillStyle = EP.rgba(LASER_HOT);
    ctx.fill();
  }

  // 'slides': a neutral deck. o.slide 0..1: slide 1 ("Q3 roadmap" and three plain bars) pushes left as slide 2 (a plain title bar and two
  //   bars) comes in from the right; the caller owns the 300 ms easing. o.laser {x, y, trail}|null draws the laser (above);
  //   o.region {x0, y0, x1, y1}|null dims outside the rectangle and draws it dashed (marching ants from t);
  //   o.flash 0..1 a white wash at up to 30 %; o.rec 0..1 a red dot pulsing top right (about 1.2 Hz from t).
  KINDS.slides = (ctx, t, o) => {
    const p = ease(o.slide || 0);
    ctx.fillStyle = EP.page.ground;
    ctx.fillRect(0, 0, SW, SH);
    ctx.save();
    ctx.translate(-p * SW, 0);
    text(ctx, 'Q3 roadmap', 120, 270, { size: 112, w: 'Bold', color: EP.page.ink });
    fillRR(ctx, 120, 310, 160, 12, 6, EP.page.accent);
    [760, 620, 700].forEach((w, i) => fillRR(ctx, 120, 400 + i * 84, w, 40, 20, SLIDE_BAR));
    ctx.restore();
    ctx.save();
    ctx.translate((1 - p) * SW, 0);
    fillRR(ctx, 120, 180, 620, 72, 36, EP.page.ink);
    [900, 720].forEach((w, i) => fillRR(ctx, 120, 330 + i * 84, w, 40, 20, SLIDE_BAR));
    ctx.restore();
    if (o.region) {
      const r = o.region;
      const x = r.x0 * SW;
      const y = r.y0 * SH;
      const w = (r.x1 - r.x0) * SW;
      const h = (r.y1 - r.y0) * SH;
      ctx.beginPath();
      ctx.rect(0, 0, SW, SH);
      ctx.rect(x, y, w, h);
      ctx.fillStyle = 'rgba(0,0,0,0.55)';
      ctx.fill('evenodd');
      ctx.save();
      ctx.setLineDash([18, 12]);
      ctx.lineDashOffset = -t * 30;
      ctx.lineWidth = 4;
      ctx.strokeStyle = FLASH;
      ctx.strokeRect(x, y, w, h);
      ctx.restore();
    }
    if (o.laser) laser(ctx, o.laser);
    if (o.flash > 0) {
      ctx.save();
      ctx.fillStyle = FLASH;
      ctx.globalAlpha *= 0.3 * clamp(o.flash);
      ctx.fillRect(0, 0, SW, SH);
      ctx.restore();
    }
    if (o.rec > 0) {
      const pulse = 0.65 + 0.35 * Math.sin(t * Math.PI * 2 * 1.2);
      const g = ctx.createRadialGradient(SW - 76, 76, 0, SW - 76, 76, 44);
      g.addColorStop(0, TOK.LASER_GLOW);
      g.addColorStop(1, 'rgba(255,59,48,0)');
      ctx.save();
      ctx.globalAlpha *= clamp(o.rec) * pulse;
      ctx.fillStyle = g;
      ctx.fillRect(SW - 130, 22, 110, 110);
      ctx.beginPath();
      ctx.arc(SW - 76, 76, 18, 0, Math.PI * 2);
      ctx.fillStyle = TOK.LASER;
      ctx.fill();
      ctx.restore();
    }
  };

  // 'video': a landscape clip drawn as gradients (sky, a sun, two hill lines) with a scrub bar. o.pos 0..1 is the playhead: the bar's
  //   knob, and the sun and hills drift with it.
  const hill = (ctx, base, amp, freq, phase, color) => {
    ctx.beginPath();
    ctx.moveTo(0, SH);
    for (let x = 0; x <= SW; x += 16) ctx.lineTo(x, base + amp * Math.sin(x * freq + phase));
    ctx.lineTo(SW, SH);
    ctx.closePath();
    ctx.fillStyle = color;
    ctx.fill();
  };
  KINDS.video = (ctx, t, o) => {
    const pos = clamp(o.pos || 0);
    let g = ctx.createLinearGradient(0, 0, 0, SH * 0.75);
    g.addColorStop(0, SKY_TOP);
    g.addColorStop(1, SKY_LOW);
    ctx.fillStyle = g;
    ctx.fillRect(0, 0, SW, SH);
    const sx = lerp(300, 980, pos);
    g = ctx.createRadialGradient(sx, 400, 0, sx, 400, 220);
    g.addColorStop(0, SUN);
    g.addColorStop(0.3, 'rgba(255,226,168,0.55)');
    g.addColorStop(1, 'rgba(255,226,168,0)');
    ctx.fillStyle = g;
    ctx.fillRect(0, 100, SW, 560);
    hill(ctx, 520, 40, 0.006, pos * 2.4, HILL_FAR);
    hill(ctx, 620, 50, 0.004, pos * 4.8 + 1.5, HILL_NEAR);
    g = ctx.createLinearGradient(0, SH - 220, 0, SH);
    g.addColorStop(0, 'rgba(0,0,0,0)');
    g.addColorStop(1, 'rgba(0,0,0,0.7)');
    ctx.fillStyle = g;
    ctx.fillRect(0, SH - 220, SW, 220);
    icon(ctx, 'play', 90, 706, 52, INK, 1, true);
    fillRR(ctx, 150, 701, 1020, 10, 5, BAR_DIM);
    fillRR(ctx, 150, 701, 1020 * pos, 10, 5, ACCENT);
    ctx.beginPath();
    ctx.arc(150 + 1020 * pos, 706, 17, 0, Math.PI * 2);
    ctx.fillStyle = INK;
    ctx.fill();
  };

  // 'editor': a dark code editor with neutral code and a terminal pane with a generic prompt (the caret blinks from t). No fields.
  const CODE = [
    [['function ', SYN_KEY], ['greet', SYN_FN], ['(name) {', INK]],
    [['  const ', SYN_KEY], ['msg = ', INK], ["'Hello, '", SYN_STR], [' + name;', INK]],
    [['  return ', SYN_KEY], ['msg.toUpperCase();', INK]],
    [['}', INK]],
    [],
    [['greet', SYN_FN], ['(', INK], ["'world'", SYN_STR], [');', INK]],
  ];
  KINDS.editor = (ctx, t) => {
    ctx.fillStyle = CODE_BG;
    ctx.fillRect(0, 0, SW, SH);
    ctx.fillStyle = WIN_BAR;
    ctx.fillRect(0, 0, SW, 64);
    fillRR(ctx, 24, 12, 220, 52, 12, CODE_BG);
    textMid(ctx, 'greet.js', 134, 38, { ...font(30, 'Regular'), align: 'center' });
    CODE.forEach((segs, i) => {
      const y = 140 + i * 54;
      text(ctx, String(i + 1), 76, y, { size: 30, color: GUTTER, align: 'right' });
      let x = 120;
      for (const [s, c] of segs) {
        text(ctx, s, x, y, { size: 32, color: c });
        x += measure(ctx, s, 32);
      }
    });
    ctx.fillStyle = TERM_BG;
    ctx.fillRect(0, 500, SW, 300);
    ctx.fillStyle = WIN_BAR;
    ctx.fillRect(0, 500, SW, 52);
    textMid(ctx, 'Terminal', 36, 526, font(30, 'Bold'));
    text(ctx, '$', 36, 616, { size: 32, w: 'Bold', color: ACCENT });
    text(ctx, 'run greet.js', 72, 616, font(32, 'Regular'));
    text(ctx, 'HELLO, WORLD', 36, 670, font(32, 'Regular'));
    text(ctx, '$', 36, 724, { size: 32, w: 'Bold', color: ACCENT });
    if (Math.floor(t * 2) % 2 === 0) fillRR(ctx, 72, 698, 16, 36, 2, INK);
  };

  // 'browser-pr': o.open 0..1 a browser window opens (scale 0.92 -> 1, fade) over the plain desktop on a neutral dark PR page:
  //   "acme/website", "Fix the login redirect", a green check with "All checks passed", description bars and a two-bar diff.
  //   No host chrome and no logo.
  KINDS['browser-pr'] = (ctx, t, o) => {
    wallpaper(ctx);
    taskbar(ctx);
    const k = clamp(o.open || 0);
    if (k <= 0) return;
    windowOpen(ctx, 90, 60, 1100, 640, '', k, lerp(0.92, 1, easeOut(k)));
    [0, 1, 2].forEach((i) => {
      ctx.beginPath();
      ctx.arc(32 + i * 30, BAR / 2, 8, 0, Math.PI * 2);
      ctx.fillStyle = DIM;
      ctx.fill();
    });
    fillRR(ctx, 150, 10, 560, 40, 20, TOK.GROUND);
    textMid(ctx, 'acme/website', 180, 30, font(28, 'Regular'));
    text(ctx, 'acme/website', 56, BAR + 66, { size: 30, color: DIM });
    text(ctx, 'Fix the login redirect', 56, BAR + 140, { size: 56, w: 'Bold', color: INK });
    ctx.beginPath();
    ctx.arc(80, BAR + 208, 24, 0, Math.PI * 2);
    ctx.fillStyle = CHECK_GREEN;
    ctx.fill();
    icon(ctx, 'check', 80, BAR + 208, 28, TOK.PHONE);
    text(ctx, 'All checks passed', 124, BAR + 220, font(32, 'Bold'));
    [900, 760, 840].forEach((w, i) => fillRR(ctx, 56, BAR + 270 + i * 40, w, 16, 8, BAR_DIM));
    fillRR(ctx, 56, BAR + 410, 988, 50, 10, DIFF_DEL);
    fillRR(ctx, 56, BAR + 466, 988, 50, 10, DIFF_ADD);
    ctx.restore();
  };

  // 'credits': plain role lines rolling up, then "The End". o.roll seconds (about 70 px per second; it stops with "The End" centred).
  //   o.countdown {a 0..1, secs}: the ShutdownCountdown card (a light fixed dialog in the middle: title "Sleep the laptop", the sentence
  //   with the seconds, a default Cancel button). o.black 0..1 fades the screen to black (any kind).
  const ROLES = ['Directed by', 'Written by', 'Edited by', 'Music by', 'Produced by', 'The End'];
  const ROLL_SPEED = 70;
  const ROLL_GAP = 120;
  KINDS.credits = (ctx, t, o) => {
    ctx.fillStyle = CREDITS_BG;
    ctx.fillRect(0, 0, SW, SH);
    const start = SH + 40;
    const stop = (start + (ROLES.length - 1) * ROLL_GAP - SH / 2) / ROLL_SPEED;
    const roll = Math.min(o.roll || 0, stop) * ROLL_SPEED;
    ROLES.forEach((r, i) => {
      const end = i === ROLES.length - 1;
      textMid(ctx, r, SW / 2, start + i * ROLL_GAP - roll, { size: end ? 72 : 44, w: end ? 'Bold' : 'Regular', color: end ? INK : DIM, align: 'center' });
    });
    if (o.countdown && o.countdown.a > 0) countdown(ctx, o.countdown);
  };

  // The ShutdownCountdown card (N13). Dialog dp: client 392 wide, Cancel at 280,64 (75 x 23, the default button). Windows' own sentence
  // (EndAction_ShutdownText) names shutting down, so the film shows only the title the storyboard asks for, the seconds left as a
  // numeral in a thin ring that empties over the 60 s, and Cancel. c.secs is the seconds remaining, 60 -> 0.
  const COUNTDOWN_TITLE = 'Sleep the laptop'; // strings.xml end_sleep
  const COUNTDOWN_CANCEL = 'Cancel'; // Strings.resx EndAction_Cancel
  function countdown(ctx, c) {
    const S = DIALOG_S;
    const w = 392;
    const titleH = 32;
    const h = titleH + 103;
    const a = clamp(c.a);
    ctx.save();
    ctx.globalAlpha *= a;
    ctx.translate(SW / 2, SH / 2);
    ctx.scale(S * lerp(0.96, 1, ease(a)), S * lerp(0.96, 1, ease(a)));
    ctx.translate(-w / 2, -h / 2);
    ctx.save();
    ctx.shadowColor = WIN_SHADOW;
    ctx.shadowBlur = 40;
    ctx.shadowOffsetY = 10;
    ctx.fillStyle = DIALOG_BG;
    ctx.fillRect(0, 0, w, h);
    ctx.restore();
    ctx.fillStyle = DIALOG_BAR;
    ctx.fillRect(0, 0, w, titleH);
    textMid(ctx, COUNTDOWN_TITLE, 12, titleH / 2, { size: 12, color: DIALOG_INK });
    const secs = clamp(c.secs, 0, 60);
    const cy = titleH + 52;
    ctx.lineWidth = 4;
    ctx.beginPath();
    ctx.arc(60, cy, 30, 0, Math.PI * 2);
    ctx.strokeStyle = DIALOG_BTN_RIM;
    ctx.stroke();
    if (secs > 0) {
      ctx.beginPath();
      ctx.arc(60, cy, 30, -Math.PI / 2, -Math.PI / 2 + Math.PI * 2 * (secs / 60));
      ctx.strokeStyle = EP.page.accent;
      ctx.stroke();
    }
    textMid(ctx, String(Math.ceil(secs)), 60, cy, { size: 30, w: 'Bold', color: DIALOG_INK, align: 'center' });
    ctx.fillStyle = DIALOG_BTN;
    ctx.fillRect(280, titleH + 64, 75, 23);
    ctx.lineWidth = 2;
    ctx.strokeStyle = EP.page.accent;
    ctx.strokeRect(281, titleH + 65, 73, 21);
    ctx.lineWidth = 1;
    ctx.strokeStyle = DIALOG_BTN_RIM;
    ctx.strokeRect(280.5, titleH + 64.5, 74, 22);
    textMid(ctx, COUNTDOWN_CANCEL, 317.5, titleH + 75.5, { size: 12, color: DIALOG_INK, align: 'center' });
    ctx.restore();
  }

  // The transfer toast (N4), any kind. o.toast {a 0..1, name, index, count, pct 0..100, paused 0|1, done 0..1, phone}: a is the
  // alpha (the card rises 20 px as it fades in); running while done < 0.5 ("Sending to {phone}", "{name} · {index} of {count}" or
  // "{name} · Paused", Pause or Resume, Stop, Close, the 4 px track at pct), then the finished card ("Sent {name}" for one file,
  // "Files sent: {count}" otherwise, Close only). The speed field is left blank, as the app does before the meter has a figure.
  const PAUSE_GLYPH = new Path2D('M3,2 H6 V12 H3 Z M8,2 H11 V12 H8 Z');
  const PLAY_GLYPH = new Path2D('M3,1.5 L12,7 L3,12.5 Z');
  const STOP_GLYPH = new Path2D('M3,3 H11 V11 H3 Z');
  function toast(ctx, T) {
    const done = T.done >= 0.5;
    const h = done ? 64 : 76;
    const W = 380;
    const S = TOAST_S;
    const a = clamp(T.a);
    const name = T.name || '';
    const count = T.count || 1;
    const title = done
      ? count > 1
        ? `Files sent: ${count}`
        : `Sent ${name}`
      : `Sending to ${T.phone || 'Phone'}`;
    const sub = done ? '' : T.paused >= 0.5 ? `${name} · Paused` : count > 1 ? `${name} · ${T.index || 1} of ${count}` : name;
    ctx.save();
    ctx.globalAlpha *= a;
    ctx.translate(SW - (W + 16) * S, SH - TASK_H - (h + 16) * S + (1 - a) * 20);
    ctx.scale(S, S);
    fillRR(ctx, 0, 0, W, h, 12, PAL.card);
    rr(ctx, 0.5, 0.5, W - 1, h - 1, 12);
    ctx.lineWidth = 1;
    ctx.strokeStyle = EP.rgba(PAL.line);
    ctx.stroke();
    EP.mark(ctx, 16, 14, 32, true);
    const kinds = done ? ['close'] : ['pause', 'stop', 'close'];
    let x = W - 10 - kinds.length * 32 - (kinds.length - 1) * 2;
    for (const kind of kinds) {
      const cx = x + 16;
      const cy = 14 + 16;
      if (kind === 'close') icon(ctx, 'close', cx, cy, 16, EP.rgba(PAL.dim));
      else {
        const g = kind === 'stop' ? 14 : 16;
        ctx.save();
        ctx.translate(cx - g / 2, cy - g / 2);
        ctx.scale(g / 14, g / 14);
        ctx.fillStyle = EP.rgba(kind === 'stop' ? PAL.danger : PAL.ink);
        ctx.fill(kind === 'stop' ? STOP_GLYPH : T.paused >= 0.5 ? PLAY_GLYPH : PAUSE_GLYPH);
        ctx.restore();
      }
      x += 34;
    }
    const ink = { size: 14, w: 'Bold', color: EP.rgba(PAL.ink) };
    if (done) textMid(ctx, title, 60, h / 2, ink);
    else {
      textMid(ctx, title, 60, 14 + 10, ink);
      textMid(ctx, sub, 60, 14 + 20 + 8, { size: 12, color: EP.rgba(PAL.dim) });
      const tw = W - 60 - 16;
      fillRR(ctx, 60, 58, tw, 4, 2, PAL.off);
      const filled = (tw * clamp((T.pct || 0) / 100, 0, 1));
      if (filled > 0) fillRR(ctx, 60, 58, filled, 4, 2, PAL.accent);
    }
    ctx.restore();
  }

  // o.glint 0..1 (any kind): a faint, blurred, unresolved shape drifting in the middle of the screen. No UI, no edges.
  function glint(ctx, t, k) {
    ctx.save();
    ctx.globalAlpha *= clamp(k);
    [[-90, -30, 260, 0], [80, 20, 230, 2.1], [-10, 60, 200, 4.2]].forEach(([dx, dy, r, ph]) => {
      const x = SW / 2 + dx + 40 * Math.sin(t * 0.9 + ph);
      const y = SH / 2 + dy + 30 * Math.cos(t * 0.7 + ph);
      const g = ctx.createRadialGradient(x, y, 0, x, y, r);
      g.addColorStop(0, EP.rgba(GLINT, 0.22));
      g.addColorStop(1, EP.rgba(GLINT, 0));
      ctx.fillStyle = g;
      ctx.beginPath();
      ctx.arc(x, y, r, 0, Math.PI * 2);
      ctx.fill();
    });
    ctx.restore();
  }

  // EP.laptopScreen(ctx, w, h, t, kind, o): paints `kind` into (0, 0, w, h), a 1280 x 800 logical screen scaled to fit. Pointer and
  // positions are fractions 0..1 of the screen. Kinds and their o fields are documented above each renderer. For any kind:
  //   o.pointer {x, y}: the cursor (drawn above the content, never zoomed); o.glint 0..1; o.toast {...} (see toast above);
  //   o.black 0..1 fades everything to black.
  // Body text is 28 logical px or more, so it reads at 640 px wide on a 1920 page; the toast's second line is the one 24 px exception
  // (the app's 12 dp at 2 x).
  EP.laptopScreen = (ctx, w, h, t, kind, o = {}) => {
    ctx.save();
    ctx.beginPath();
    ctx.rect(0, 0, w, h);
    ctx.clip();
    ctx.scale(w / SW, h / SH);
    KINDS[kind](ctx, t, o);
    if (o.pointer && kind !== 'race') cursor(ctx, o.pointer.x * SW, o.pointer.y * SH);
    if (o.glint > 0) glint(ctx, t, o.glint);
    if (o.toast && o.toast.a > 0) toast(ctx, o.toast);
    if (o.black > 0) {
      ctx.fillStyle = '#000000';
      ctx.globalAlpha *= clamp(o.black);
      ctx.fillRect(0, 0, SW, SH);
    }
    ctx.restore();
  };

  // EP.laptop(ctx, x, y, w, h, draw, o): a generic thin-bezel laptop whose screen rect is (x, y, w, h) page px, top-left. A 14 px
  // bezel (x w / 640), a 2 % corner radius, a trapezoid deck below at 18 % of h, a 60 px blur glow of o.glow (default TOK.BLUE) at 25 %
  // behind. draw(ctx, w, h) paints the screen clipped, origin top-left. o.a alpha, o.deck (default true), o.dim 0..1 darkens the screen.
  EP.laptop = (ctx, x, y, w, h, draw, o = {}) => {
    const k = w / 640;
    const bez = 14 * k;
    const bx = x - bez;
    const by = y - bez;
    const bw = w + 2 * bez;
    const bh = h + 2 * bez;
    const r = 0.02 * w;
    ctx.save();
    ctx.globalAlpha *= o.a ?? 1;
    // The glow: the lid's shadow only, cast from a body drawn far off the page.
    const far = 20000;
    const sc = ctx.getTransform().a || 1;
    ctx.save();
    ctx.globalAlpha *= 0.25;
    ctx.shadowColor = o.glow || TOK.BLUE;
    ctx.shadowBlur = 60;
    ctx.shadowOffsetX = far;
    ctx.fillStyle = o.glow || TOK.BLUE;
    ctx.fillRect(bx - far / sc, by, bw, bh);
    ctx.restore();
    if (o.deck !== false) {
      const y0 = by + bh;
      const y1 = y0 + 0.18 * h;
      const ins = bw * 0.04;
      const rc = 10 * k;
      ctx.beginPath();
      ctx.moveTo(bx, y0);
      ctx.lineTo(bx + bw, y0);
      ctx.arcTo(bx + bw + ins, y1, bx - ins, y1, rc);
      ctx.arcTo(bx - ins, y1, bx, y0, rc);
      ctx.lineTo(bx, y0);
      ctx.closePath();
      const g = ctx.createLinearGradient(0, y0, 0, y1);
      g.addColorStop(0, TOK.LAPTOP_BODY);
      g.addColorStop(1, DECK_BOT);
      ctx.fillStyle = g;
      ctx.fill();
      ctx.lineWidth = 1.5 * k;
      ctx.strokeStyle = TOK.LAPTOP_RIM;
      ctx.stroke();
      fillRR(ctx, bx + bw * 0.42, y0, bw * 0.16, (y1 - y0) * 0.16, 4 * k, DECK_NOTCH);
    }
    fillRR(ctx, bx, by, bw, bh, r + bez * 0.5, TOK.LAPTOP_BODY);
    rr(ctx, bx + 0.75 * k, by + 0.75 * k, bw - 1.5 * k, bh - 1.5 * k, r + bez * 0.5);
    ctx.lineWidth = 1.5 * k;
    ctx.strokeStyle = TOK.LAPTOP_RIM;
    ctx.stroke();
    fillRR(ctx, x, y, w, h, r * 0.6, SCREEN_OFF);
    ctx.save();
    rr(ctx, x, y, w, h, r * 0.6);
    ctx.clip();
    ctx.save();
    ctx.translate(x, y);
    draw(ctx, w, h);
    ctx.restore();
    if (o.dim > 0) {
      ctx.fillStyle = `rgba(0,0,0,${clamp(o.dim)})`;
      ctx.fillRect(x, y, w, h);
    }
    ctx.restore();
    ctx.restore();
  };

  // EP.room(ctx, W, H, o): the hook room, after film.js's 4.0 room: a far wall, a floor, a TV stand with the screen's soft glow and its
  // floor reflection. It draws no laptop: it returns the laptop's screen rect {x, y, w, h} (page px, 16:10) on the stand for this
  // frame so the director can place EP.laptop there.
  //   o.k 0..1: 0 far view, 1 gone (zoom 1 -> 3.6, drifts up 42 % of H, fades out between 0.5 and 1);
  //   o.dolly 0..1: an extra slow push of up to 6 % about the screen's centre (MOTION.md roomWide);
  //   o.at {x, y} fractions of W, H for the stand's centre (default 0.69, 0.5); o.u the unit as a fraction of W (default 0.0062):
  //     the stand is 34 u wide and the screen 12 u;
  //   o.tv 0..1 a large dark TV (44 u wide) with a thin rim behind and above the stand, showing o.screen(ctx, w, h) at that alpha;
  //   o.beam 0..1 a soft projector cone from the upper left across the wall to a lit rectangle showing o.screen faintly;
  //   o.lamp 0..1 the room lamp's level (default 1; 0.6 is dimmed); o.glow rgb array for the screen's glow (default the blue).
  EP.room = (ctx, W, H, o = {}) => {
    const k = clamp(o.k || 0);
    const e = easeInOut(k);
    const a = 1 - ease(seg(k, 0.5, 1));
    const z = lerp(1, 3.6, e);
    const d = 1 + 0.06 * clamp(o.dolly || 0);
    const at = o.at || { x: 0.69, y: 0.5 };
    const u = W * (o.u ?? 0.0062);
    const lamp = o.lamp ?? 1;
    const cx = W * at.x;
    const cy = H * at.y - e * H * 0.42;
    const P = -5.45 * u; // the pivot of the dolly: the screen's centre
    const Y = (ly) => cy + z * (P + d * (ly - P));
    const rect = { x: cx - 6 * u * z * d, y: Y(-9.2 * u), w: 12 * u * z * d, h: 7.5 * u * z * d };
    if (a <= 0.003) return rect;
    ctx.save();
    ctx.globalAlpha *= a;
    ctx.fillStyle = TOK.ROOM_DEEP;
    ctx.fillRect(0, 0, W, H);
    const floor = Y(5 * u);
    let g = ctx.createLinearGradient(0, floor, 0, H);
    g.addColorStop(0, FLOOR_TOP);
    g.addColorStop(1, TOK.ROOM_DEEP);
    ctx.fillStyle = g;
    ctx.fillRect(0, floor, W, H - floor);
    ctx.fillStyle = SKIRT;
    ctx.fillRect(0, floor, W, 2);
    g = ctx.createRadialGradient(W * 0.12, H * 0.16, 0, W * 0.12, H * 0.16, W * 0.6);
    g.addColorStop(0, EP.rgba(LAMP, 0.1 * lamp));
    g.addColorStop(1, EP.rgba(LAMP, 0));
    ctx.fillStyle = g;
    ctx.fillRect(0, 0, W, H);
    if (lamp < 1) {
      ctx.fillStyle = `rgba(0,0,0,${0.8 * (1 - lamp)})`;
      ctx.fillRect(0, 0, W, H);
    }
    ctx.save();
    ctx.translate(cx, cy);
    ctx.scale(z, z);
    ctx.translate(0, P);
    ctx.scale(d, d);
    ctx.translate(0, -P);
    const tvW = 44 * u;
    const tvH = 24.75 * u;
    const tvY = -3 * u - tvH;
    const lit = (alpha) => {
      ctx.save();
      ctx.globalAlpha *= alpha;
      rr(ctx, -tvW / 2, tvY, tvW, tvH, 0.5 * u);
      ctx.clip();
      ctx.translate(-tvW / 2, tvY);
      if (o.screen) o.screen(ctx, tvW, tvH);
      ctx.restore();
    };
    if (o.beam > 0) {
      const sx = -80 * u;
      const sy = -45 * u;
      const beam = clamp(o.beam);
      g = ctx.createLinearGradient(sx, sy, 0, tvY + tvH / 2);
      g.addColorStop(0, EP.rgba(BEAM, 0.2 * beam));
      g.addColorStop(1, EP.rgba(BEAM, 0.05 * beam));
      ctx.beginPath();
      ctx.moveTo(sx, sy);
      ctx.lineTo(tvW / 2, tvY);
      ctx.lineTo(tvW / 2, tvY + tvH);
      ctx.lineTo(-tvW / 2, tvY + tvH);
      ctx.closePath();
      ctx.fillStyle = g;
      ctx.fill();
      ctx.fillStyle = EP.rgba(BEAM, 0.14 * beam);
      ctx.fillRect(-tvW / 2, tvY, tvW, tvH);
      lit(0.55 * beam);
    }
    if (o.tv > 0) {
      const tv = clamp(o.tv);
      ctx.save();
      ctx.globalAlpha *= tv;
      fillRR(ctx, -tvW / 2 - 0.4 * u, tvY - 0.4 * u, tvW + 0.8 * u, tvH + 0.8 * u, 0.8 * u, TV_BODY);
      rr(ctx, -tvW / 2 - 0.4 * u, tvY - 0.4 * u, tvW + 0.8 * u, tvH + 0.8 * u, 0.8 * u);
      ctx.lineWidth = 0.25 * u;
      ctx.strokeStyle = TV_RIM;
      ctx.stroke();
      ctx.restore();
      lit(tv);
    }
    // The glow the screen throws and its reflection on the floor, then the stand: a slab, a low cabinet, two feet.
    const glow = o.glow || GLOW_DEFAULT;
    g = ctx.createRadialGradient(0, -4 * u, 0, 0, -4 * u, 26 * u);
    g.addColorStop(0, EP.rgba(glow, 0.24));
    g.addColorStop(1, EP.rgba(glow, 0));
    ctx.fillStyle = g;
    ctx.beginPath();
    ctx.arc(0, -4 * u, 26 * u, 0, Math.PI * 2);
    ctx.fill();
    ctx.save();
    ctx.translate(0, 9.6 * u);
    ctx.scale(1, 0.16);
    g = ctx.createRadialGradient(0, 0, 0, 0, 0, 18 * u);
    g.addColorStop(0, EP.rgba(glow, 0.26));
    g.addColorStop(1, EP.rgba(glow, 0));
    ctx.fillStyle = g;
    ctx.beginPath();
    ctx.arc(0, 0, 18 * u, 0, Math.PI * 2);
    ctx.fill();
    ctx.restore();
    fillRR(ctx, -17 * u, 0, 34 * u, 1.1 * u, 0.4 * u, STAND_SLAB);
    ctx.fillStyle = TOK.LAPTOP_RIM;
    ctx.fillRect(-17 * u, 0, 34 * u, 0.12 * u);
    fillRR(ctx, -16 * u, 1.1 * u, 32 * u, 7 * u, 0.5 * u, STAND_CABINET);
    ctx.fillStyle = STAND_GAP;
    ctx.fillRect(-0.05 * u, 1.5 * u, 0.1 * u, 6.2 * u);
    ctx.fillRect(-14 * u, 8.1 * u, u, 0.9 * u);
    ctx.fillRect(13 * u, 8.1 * u, u, 0.9 * u);
    ctx.restore();
    ctx.restore();
    return rect;
  };

  // EP.hand(ctx, o): the soft hand behind a phone. o is the phone's cam {x, y, s, r} (centre, scale, degrees: film.js drawPhone) plus
  // o.a alpha. One flat TOK.HAND silhouette with a 1 px rim: a palm below and beside the lower half of the body, four finger tips
  // curling round its right edge and a thumb along its left edge, all behind the phone; nothing over the screen. Draw it before the phone.
  EP.hand = (ctx, o) => {
    const parts = [
      [-224, 60, 446, 800, 150],
      [196, 50, 76, 130, 38],
      [196, 150, 82, 130, 41],
      [196, 250, 78, 130, 39],
      [196, 350, 66, 120, 33],
    ];
    ctx.save();
    ctx.globalAlpha *= o.a ?? 1;
    ctx.translate(o.x, o.y);
    ctx.scale(o.s, o.s);
    ctx.rotate(((o.r || 0) * Math.PI) / 180);
    // One path, so the rim runs round the whole silhouette and not between its parts.
    const shape = () => {
      ctx.beginPath();
      for (const [x, y, w, h, r] of parts) ctx.roundRect(x, y, w, h, r);
      ctx.save();
      ctx.translate(-224, 240);
      ctx.rotate(0.14);
      ctx.roundRect(-30, -130, 60, 260, 30);
      ctx.restore();
    };
    ctx.lineWidth = 2 / o.s;
    ctx.strokeStyle = HAND_RIM;
    shape();
    ctx.stroke();
    ctx.shadowColor = HAND_SHADOW;
    ctx.shadowBlur = 30;
    ctx.fillStyle = TOK.HAND;
    shape();
    ctx.fill();
    ctx.restore();
  };
})();

// The laptop's own screen shown on the phone (the film's picture of screen mirroring, which Edgepad 6 will bring; it is no
// app screen): the same EP.laptopScreen, fitted 16 : 10 into the phone on black.
// o: kind (default 'desktop') and screen (that kind's o), drawn at o.t (film time, default lt); a 0..1 fades the picture in.
(function () {
  EP.screens.mirror = (ctx, w, h, pal, lt, o = {}) => {
    ctx.fillStyle = '#000000';
    ctx.fillRect(0, 0, w, h);
    const rw = Math.min(w, h * 1.6), rh = rw / 1.6;
    ctx.save();
    ctx.globalAlpha *= o.a ?? 1;
    ctx.translate((w - rw) / 2, (h - rh) / 2);
    ctx.beginPath();
    ctx.rect(0, 0, rw, rh);
    ctx.clip();
    EP.laptopScreen(ctx, rw, rh, o.t ?? lt, o.kind || 'desktop', o.screen || {});
    ctx.restore();
  };
})();
