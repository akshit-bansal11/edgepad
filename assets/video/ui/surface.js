// The control surface (ControlSurface.kt, Backdrop.kt, RulerPainter.kt, DialSpan.kt, CornerSlots.kt, MediaPiece.kt),
// drawn at 1 unit = 1 dp. Everything it shows comes in through S, so a frame is a pure function of that state.
(function () {
  const { clamp, lerp, rgba, text, icon, Perimeter } = EP;

  // RulerPainter / RulerGeometry constants, copied from the Kotlin.
  const NOTCH = 11, MAJOR_EVERY = 5, INSET = 5, HEIGHT = 0.6, HALF = 100, FEATHER = 36;
  const smooth = (x) => {
    const t = clamp(x);
    return t * t * (3 - 2 * t);
  };
  const majorEnd = (armed) => INSET + 20 * HEIGHT * (armed ? 1.2 : 1);
  const indicatorEnd = (armed) => INSET + 32 * HEIGHT * (armed ? 1.2 : 1);
  const dotDiameter = 3 + 0.8 * HEIGHT;
  const limit = (radius, fromBend) => 0.85 * radius + fromBend;
  // Edgepad's level dials: 100 units over BASE_UNITS_PER_DP 0.25 at the default sensitivity 1.4.
  EP.RULER_LENGTH = 100 / (0.25 * 1.4);
  EP.levelToRuler = (level) => level / (0.25 * 1.4);

  // DialKind short labels (strings.xml dial_*_short), and whether the dial is a level with a lit length.
  const KINDS = { VOL: ['VOL', true], BRI: ['BRI', true], ZOOM: ['ZOOM', false], SCRUB: ['SCRUB', true], APPS: ['APPS', false], MIC: ['MIC', true], RATE: ['RATE', false], ZOOM2: ['ZOOM', false] };
  const UNSPLIT = ['VOL', 'BRI', 'ZOOM', 'SCRUB']; // DialKind.defaultCorner: volume, brightness, zoom, media scrub
  const INHERITED = [0, 3, 4, 7]; // CornerSlots.inheritedSlot
  // A split half sits off its corner by half the dial's length (ControlSurface: lengthAtSlot(slot, halfLengthDp / 2)).
  const SLOT_OFFSET = HALF / 2;
  // The halves a split leaves empty, as the owner fills them in Corners. Seven kinds for eight halves: one repeats.
  const EXTRA = [[1, 'APPS'], [2, 'MIC'], [5, 'RATE'], [6, 'ZOOM2']];

  const forward = (from, to, len) => (((to - from) % len) + len) % len;
  // DialSpan.compute
  function span(centres, len, keepOut, gap) {
    const after = [], before = [];
    centres.forEach((c, i) => {
      let a = HALF, b = HALF;
      centres.forEach((o, j) => {
        if (j === i) return;
        const ahead = forward(c, o, len);
        a = Math.min(a, ahead / 2 - gap / 2);
        b = Math.min(b, (len - ahead) / 2 - gap / 2);
      });
      for (let k = 0; k + 1 < keepOut.length; k += 2) {
        a = Math.min(a, forward(c, keepOut[k], len));
        b = Math.min(b, forward(keepOut[k + 1], c, len));
      }
      after.push(Math.max(a, 0));
      before.push(Math.max(b, 0));
    });
    return { after, before };
  }

  const pt = [0, 0, 0, 0];
  function stroke(ctx, p, from, to) {
    ctx.beginPath();
    ctx.moveTo(p[0] + p[2] * from, p[1] + p[3] * from);
    ctx.lineTo(p[0] + p[2] * to, p[1] + p[3] * to);
    ctx.stroke();
  }

  // RulerPainter.draw: notches, the fixed indicator, and the label and number inside the bend.
  function ruler(ctx, per, centre, after, before, rulerDp, rulerLen, armed, pal, label, number, alpha) {
    if (alpha <= 0.01) return;
    ctx.save();
    ctx.globalAlpha *= alpha;
    ctx.lineCap = 'round';
    let first = Math.ceil((-before - rulerDp) / NOTCH);
    let last = Math.floor((after - rulerDp) / NOTCH);
    if (rulerLen !== null) {
      first = Math.max(first, -Math.floor(rulerLen / NOTCH));
      last = Math.min(last, 0);
    }
    for (let n = first; n <= last; n++) {
      const s = rulerDp + n * NOTCH;
      const room = s >= 0 ? after - s : before + s;
      const duck = 0.15 + 0.85 * smooth((Math.abs(s) - 3) / 9);
      const fade = smooth(room / FEATHER) * duck;
      if (fade <= 0.02) continue;
      const lit = rulerLen !== null && s > 0.5;
      per.point(centre + s, pt);
      const colour = lit ? pal.accent : pal.ink;
      if (n % MAJOR_EVERY === 0) {
        ctx.strokeStyle = rgba(colour, (lit ? 1 : 0.85) * fade);
        ctx.lineWidth = armed ? 3.6 : 3.2;
        stroke(ctx, pt, INSET, Math.min(majorEnd(armed), limit(per.radius, per.fromBend(centre + s))));
      } else {
        ctx.fillStyle = rgba(colour, (lit ? 0.95 : armed ? 0.6 : 0.42) * fade);
        const depth = INSET + dotDiameter / 2;
        ctx.beginPath();
        ctx.arc(pt[0] + pt[2] * depth, pt[1] + pt[3] * depth, dotDiameter / 2, 0, Math.PI * 2);
        ctx.fill();
      }
    }
    per.point(centre, pt);
    const start = INSET - 1;
    const end = Math.min(indicatorEnd(armed), limit(per.radius, per.fromBend(centre)));
    ctx.strokeStyle = rgba(pal.accent, armed ? 0.28 : 0.18);
    ctx.lineWidth = 12;
    stroke(ctx, pt, start, end);
    ctx.strokeStyle = rgba(pal.accent, 1);
    ctx.lineWidth = 5;
    stroke(ctx, pt, start, end);
    const depth = indicatorEnd(true) + 40;
    const ax = pt[0] + pt[2] * depth;
    const ay = pt[1] + pt[3] * depth;
    const labelSize = 11;
    const valueSize = armed ? 32 : 26;
    const block = number === '' ? labelSize : labelSize + 4 + valueSize;
    const top = ay - block / 2;
    text(ctx, label, ax, top + labelSize * 0.8, { size: labelSize, w: 'Bold', color: pal.dim, align: 'center', track: 0.06 });
    if (number !== '') text(ctx, number, ax, top + labelSize + 4 + valueSize * 0.8, { size: valueSize, w: 'Black', color: armed ? pal.accent : pal.ink, align: 'center' });
    ctx.restore();
  }

  // One touch the way Android's "show taps" draws it: a soft grey disc with a light rim.
  EP.tapDot = (ctx, x, y, a = 1) => {
    ctx.save();
    ctx.globalAlpha *= a;
    ctx.beginPath();
    ctx.arc(x, y, 20, 0, Math.PI * 2);
    ctx.fillStyle = 'rgba(150,150,150,0.45)';
    ctx.fill();
    ctx.lineWidth = 2;
    ctx.strokeStyle = 'rgba(255,255,255,0.6)';
    ctx.stroke();
    ctx.restore();
  };

  // A path function of time into a trail of recent points, newest first (ControlSurface keeps 18).
  EP.trailOf = (fn, t, n = 18, dt = 0.018) => Array.from({ length: n }, (_, i) => fn(t - i * dt));

  // drawFingers: one finger has a tail from faint and thin to solid (solid while a shape is drawn), two have
  // the line between them and a ring on their span, and every finger is a 10 dp dot, under its "show taps" disc.
  EP.drawFingers = (ctx, S, pal) => {
    const fingers = S.fingers || [];
    if (S.trail && S.trail.length > 1 && fingers.length === 1) {
      ctx.lineCap = 'round';
      for (let i = 1; i < S.trail.length; i++) {
        const fade = S.shaping ? 1 : 1 - i / S.trail.length;
        ctx.strokeStyle = rgba(pal.ink, (200 / 255) * fade * (fingers[0].a ?? 1));
        ctx.lineWidth = 10 * fade;
        ctx.beginPath();
        ctx.moveTo(S.trail[i - 1].x, S.trail[i - 1].y);
        ctx.lineTo(S.trail[i].x, S.trail[i].y);
        ctx.stroke();
      }
    }
    if (fingers.length === 2) {
      const [a, b] = fingers;
      const mx = (a.x + b.x) / 2, my = (a.y + b.y) / 2;
      ctx.save();
      ctx.globalAlpha *= Math.min(a.a ?? 1, b.a ?? 1);
      ctx.lineWidth = 1;
      ctx.strokeStyle = rgba(pal.faint);
      ctx.beginPath();
      ctx.moveTo(a.x, a.y);
      ctx.lineTo(b.x, b.y);
      ctx.stroke();
      ctx.strokeStyle = rgba(pal.dim);
      ctx.beginPath();
      ctx.arc(mx, my, Math.hypot(b.x - a.x, b.y - a.y) / 2, 0, Math.PI * 2);
      ctx.stroke();
      ctx.restore();
    }
    for (const f of fingers) {
      ctx.fillStyle = rgba(pal.ink, f.a ?? 1);
      ctx.beginPath();
      ctx.arc(f.x, f.y, 5, 0, Math.PI * 2);
      ctx.fill();
      EP.tapDot(ctx, f.x, f.y, f.a ?? 1);
    }
  };

  // Backdrop.kt: the theme's background, a colour or a gradient, then the default pattern over it: a grid of
  // 35 dp squares in #84848C at 15 %, one dp lines. S.bg = { k: 0..1 towards the custom gradient, from, to, angle }.
  EP.drawBackdrop = (ctx, w, h, pal, bg) => {
    ctx.fillStyle = rgba(pal.bg);
    ctx.fillRect(0, 0, w, h);
    if (bg && bg.k > 0) {
      const rad = (bg.angle * Math.PI) / 180;
      const dx = (Math.cos(rad) * w) / 2, dy = (Math.sin(rad) * h) / 2;
      const g = ctx.createLinearGradient(w / 2 - dx, h / 2 - dy, w / 2 + dx, h / 2 + dy);
      g.addColorStop(0, bg.from);
      g.addColorStop(1, bg.to);
      ctx.save();
      ctx.globalAlpha *= bg.k;
      ctx.fillStyle = g;
      ctx.fillRect(0, 0, w, h);
      ctx.restore();
    }
    ctx.strokeStyle = 'rgba(132,132,140,0.15)';
    ctx.lineWidth = 1;
    ctx.beginPath();
    for (let x = 0; x <= w; x += 35) {
      ctx.moveTo(x, 0);
      ctx.lineTo(x, h);
    }
    for (let y = 0; y <= h; y += 35) {
      ctx.moveTo(0, y);
      ctx.lineTo(w, y);
    }
    ctx.stroke();
  };

  const HINTS = ['One finger: move, tap to click', 'Two: scroll, pinch to zoom, tap to right-click', 'Three: swipe desktops', 'Four: swipe apps'];
  const CAPTIONS = { focus: 'Dials and media hidden · tap the lock to show', locked: 'Pad locked · tap the lock to unlock' };

  // Text cut to `room` with an ellipsis, the way TextUtils.ellipsize cuts the now-playing lines.
  function ellipsize(ctx, s, size, weight, room) {
    if (EP.measure(ctx, s, size, weight) <= room) return s;
    let n = s.length;
    while (n > 1 && EP.measure(ctx, s.slice(0, n) + '…', size, weight) > room) n--;
    return s.slice(0, n).trimEnd() + '…';
  }

  // S: { pal, bg, radius, split 0..1, extra 0..1, dials {KIND:{ruler,text,armed}}, mode 'normal'|'focus'|'locked',
  //      media {a, playing, app, title, sub, at:[x,y]}, hint 0..1, press {name,k}, fingers, trail, shaping }
  EP.drawSurface = (ctx, w, h, S) => {
    const pal = S.pal;
    EP.drawBackdrop(ctx, w, h, pal, S.bg);
    const per = new Perimeter(w, h, S.radius || 44);
    const f = S.split || 0;
    const mode = S.mode || 'normal';
    const landscape = w > h;
    const step = landscape ? 56 : 48;

    // Now playing and prev / play / next (MediaPiece defaults 0.5, 0.78 and 0.5, 0.9), hidden in focus mode.
    const m = S.media;
    if (m && m.a > 0 && mode !== 'focus') {
      ctx.save();
      ctx.globalAlpha *= m.a;
      const [nx, ny] = m.at || [0.5, 0.78];
      const fixed = 40 + 12;
      const room = Math.max(w - 2 * (HALF + 16) - fixed, 120);
      const title = ellipsize(ctx, m.title, 15, 'Bold', room);
      const sub = ellipsize(ctx, m.sub, 13, 'Regular', room);
      const tw = Math.max(EP.measure(ctx, title, 15, 'Bold'), EP.measure(ctx, sub, 13));
      const half = (fixed + clamp(tw, 120, room)) / 2;
      const left = nx * w - half;
      const cy = ny * h - 8;
      EP.appLogo(ctx, m.app, left + 20, cy, 40, pal);
      text(ctx, title, left + fixed, cy - 4, { size: 15, w: 'Bold', color: pal.ink });
      text(ctx, sub, left + fixed, cy + 13 + 8, { size: 13, color: pal.dim });
      const [tx, ty0] = m.transport || [0.5, 0.9];
      const ty = ty0 * h, cx = tx * w;
      icon(ctx, 'skipBack', cx - 52, ty, 22, pal.ink, 1);
      icon(ctx, 'skipForward', cx + 52, ty, 22, pal.ink, 1);
      const pk = m.press || 0;
      ctx.shadowColor = rgba(pal.accent, 0.35);
      ctx.shadowBlur = 14;
      ctx.shadowOffsetY = 6;
      ctx.fillStyle = rgba(pal.accent);
      ctx.beginPath();
      ctx.arc(cx, ty, 26 * (1 - 0.08 * pk), 0, Math.PI * 2);
      ctx.fill();
      ctx.shadowColor = 'transparent';
      icon(ctx, m.playing ? 'pause' : 'play', cx, ty, 22, pal.onAccent, 1);
      ctx.restore();
    }

    // Top row of buttons: settings, Controls, lock, macros. The lock is lit on an accent disc outside normal mode.
    const chrome = S.chrome ?? 1;
    const buttons = [['settings', -2], ['dashboard', -1], ['lock', 0], ['grid', 1]];
    for (const [name, side] of buttons) {
      const cx = w / 2 + side * step;
      const lit = name === 'lock' && mode !== 'normal';
      if (S.press && S.press.name === name && S.press.k > 0) {
        ctx.fillStyle = rgba(pal.faint, S.press.k);
        ctx.beginPath();
        ctx.arc(cx, 40, 24, 0, Math.PI * 2);
        ctx.fill();
      }
      if (lit) {
        ctx.fillStyle = rgba(pal.accent, 0.16);
        ctx.beginPath();
        ctx.arc(cx, 40, 22, 0, Math.PI * 2);
        ctx.fill();
      }
      icon(ctx, name, cx, 40, 22, lit ? pal.accent : pal.dim, chrome);
    }

    // Hints, the way the app prints them in the middle of an idle pad, and the mode caption a gap above them.
    const hintTop = h / 2 - ((HINTS.length - 1) * 16) / 2;
    if (S.hint > 0) HINTS.forEach((line, i) => text(ctx, line, w / 2, hintTop + i * 16, { size: 12, color: pal.dim, align: 'center', a: S.hint }));
    if (mode !== 'normal') {
      const line = CAPTIONS[mode];
      const base = hintTop - 28;
      const mid = base - 13 * 0.35;
      const hw = EP.measure(ctx, line, 13, 'Bold') / 2 + 14, hh = 13 / 2 + 7;
      EP.fillRR(ctx, w / 2 - hw, mid - hh, 2 * hw, 2 * hh, hh, rgba(pal.accent, 0.14));
      text(ctx, line, w / 2, base, { size: 13, w: 'Bold', color: pal.accent, align: 'center' });
    }
    EP.drawFingers(ctx, S, pal);

    // Dials, drawn last: 4 corners, then the halves a split adds. Each span is cut so neighbours and the buttons stay clear.
    if (mode !== 'focus') {
      const topCentre = per.lengthAt(0.5);
      const zone = 2 * step + 24 + 12;
      const keepOut = [topCentre - zone, topCentre + zone];
      const unsplit = span([0, 1, 2, 3].map((c) => per.cornerMid[c]), per.length, keepOut, 16);
      const slots = [...INHERITED.map((s, c) => [s, UNSPLIT[c], c]), ...EXTRA.map(([s, k]) => [s, k, null])];
      const extra = S.extra ?? f;
      // The empty halves only count once they hold a dial, so the four move first and the new four join after.
      const live = slots.map(([, , corner]) => corner !== null || extra > 0.02);
      const centres = slots.map(([s]) => per.slotAt(s, SLOT_OFFSET));
      const split = span(centres.filter((_, i) => live[i]), per.length, keepOut, 16);
      let j = 0;
      slots.forEach(([slot, kind, corner], i) => {
        if (!live[i]) return;
        const k = j++;
        const d = (S.dials && S.dials[kind]) || { ruler: 0, text: '', armed: false };
        const [label, level] = KINDS[kind];
        const moves = corner !== null;
        const centre = moves ? lerp(per.cornerMid[corner], centres[i], f) : centres[i];
        const after = moves ? lerp(unsplit.after[corner], split.after[k], f) : split.after[k];
        const before = moves ? lerp(unsplit.before[corner], split.before[k], f) : split.before[k];
        ruler(ctx, per, centre, after, before, d.ruler, level ? EP.RULER_LENGTH : null, d.armed, pal, label, d.text, moves ? 1 : smooth(extra));
      });
    }
    return per;
  };
})();
