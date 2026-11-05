// What each line of narration shows: its headline (one keyword between *stars*, | breaks a line), its sub-line, and an
// act that moves the phone, the screens and the laptop cards in time with the words (L.w('word') is when it is said).
// One entry per line of scripts/<id>.json, in order, keyed by the script's id. h: null keeps the headline already up.
// Nothing here says more than the script does, and no latency figure appears anywhere.
(function () {
  const { text, fillRR, rr, icon, ease, seg, lerp, clamp, measure } = EP;
  // Inside a laptop card (a light window): ink, grey, blue. LBL is a card's label, set on the dark ground.
  const CI = '#16171A', CG = '#6B6E76', CB = '#0068D6', LBL = '#9A9CA3';

  // ---- laptop cards, drawn on the page in the laptop's own look ----
  // A card rises 16 px as it fades in; every painter is given (ctx, t, a, {x, y, s}).
  function frame(ctx, a, pos, label, fn) {
    ctx.save();
    ctx.globalAlpha *= a;
    ctx.translate(pos.x, pos.y + (1 - a) * 16);
    ctx.scale(pos.s || 1, pos.s || 1);
    if (label) text(ctx, label, 0, 0, { size: 22, w: 'Bold', color: LBL });
    fn();
    ctx.restore();
  }
  // A light window on a dark ground: a black shadow, deep and short.
  function white(ctx, x, y, w, h, r = 22) {
    ctx.save();
    ctx.shadowColor = 'rgba(0,0,0,0.5)';
    ctx.shadowBlur = 44;
    ctx.shadowOffsetY = 16;
    fillRR(ctx, x, y, w, h, r, '#FFFFFF');
    ctx.restore();
  }
  // A dark panel on the dark ground: lifted by a hairline rim as well as the shadow.
  function dark(ctx, x, y, w, h, r) {
    ctx.save();
    ctx.shadowColor = 'rgba(0,0,0,0.55)';
    ctx.shadowBlur = 40;
    ctx.shadowOffsetY = 14;
    fillRR(ctx, x, y, w, h, r, '#2B2D33');
    ctx.restore();
    rr(ctx, x, y, w, h, r);
    ctx.lineWidth = 1.5;
    ctx.strokeStyle = 'rgba(255,255,255,0.16)';
    ctx.stroke();
  }
  const C = {
    // LevelOverlay.cs, the panel the laptop shows while the phone moves a level: 280 x 68 at 1.6 x, on the dark card.
    level(kind, get) {
      return (ctx, t, a, pos) =>
        frame(ctx, a, pos, 'On the laptop', () => {
          const s = 1.6, w = 280 * s, h = (36 + 20 * 0.7165 + 12 + 6) * s, y = 26;
          dark(ctx, 0, y, w, h, 18 * s);
          const v = Math.round(get(t));
          const pad = 18 * s;
          icon(ctx, kind === 'Brightness' ? 'sun' : 'volume', pad + 11 * s, y + h / 2, 22 * s, '#4DA3FF');
          const left = pad + 22 * s + 14 * s, right = w - pad;
          const base = y + pad + 20 * s * 0.7165;
          text(ctx, kind, left, base, { size: 13 * s, w: 'Bold', color: '#9A9CA3' });
          text(ctx, String(v), right, base, { size: 20 * s, w: 'Black', color: '#F2F3F5', align: 'right' });
          const ty = base + 12 * s;
          fillRR(ctx, left, ty, right - left, 6 * s, 3 * s, '#4A4D55');
          fillRR(ctx, left, ty, ((right - left) * clamp(v, 0, 100)) / 100, 6 * s, 3 * s, '#4DA3FF');
        });
    },
    // Windows' own volume flyout: what the owner moves on the laptop, which the phone's dial then follows.
    winVolume(get) {
      return (ctx, t, a, pos) =>
        frame(ctx, a, pos, 'On the laptop · Windows volume', () => {
          const w = 520, h = 96, y = 24;
          white(ctx, 0, y, w, h, 18);
          const v = get(t);
          icon(ctx, 'volume', 46, y + h / 2, 30, CI);
          const x0 = 88, x1 = 420, cy = y + h / 2;
          fillRR(ctx, x0, cy - 2.5, x1 - x0, 5, 2.5, '#C9CBD1');
          fillRR(ctx, x0, cy - 2.5, ((x1 - x0) * v) / 100, 5, 2.5, CB);
          ctx.beginPath();
          ctx.arc(x0 + ((x1 - x0) * v) / 100, cy, 11, 0, Math.PI * 2);
          ctx.fillStyle = '#FFFFFF';
          ctx.fill();
          ctx.lineWidth = 1;
          ctx.strokeStyle = '#D5D7DC';
          ctx.stroke();
          ctx.beginPath();
          ctx.arc(x0 + ((x1 - x0) * v) / 100, cy, 6, 0, Math.PI * 2);
          ctx.fillStyle = CB;
          ctx.fill();
          text(ctx, String(Math.round(v)), w - 34, cy + 11, { size: 30, w: 'Bold', color: CI, align: 'right' });
        });
    },
    // TrayContext.ShowTrustPrompt: the question the laptop asks the first time a paired phone connects.
    trust(tPress) {
      return (ctx, t, a, pos) =>
        frame(ctx, a, pos, 'On the laptop', () => {
          const w = 560, h = 252, y = 24;
          white(ctx, 0, y, w, h, 18);
          text(ctx, 'Edgepad', 32, y + 44, { size: 18, w: 'Bold', color: CG });
          text(ctx, 'Trust 5C:E4:2A:91:0D:6C?', 32, y + 88, { size: 30, w: 'Black', color: CI, track: -0.01 });
          text(ctx, 'A paired phone is asking to control this', 32, y + 126, { size: 20, color: CG });
          text(ctx, "laptop's pointer and keyboard.", 32, y + 154, { size: 20, color: CG });
          const p = clamp(1 - Math.abs(t - tPress) / 0.18);
          const bw = 150, by = y + h - 70;
          fillRR(ctx, w - 32 - bw, by, bw, 46, 12, '#FFFFFF');
          ctx.lineWidth = 1.5;
          ctx.strokeStyle = '#D5D7DC';
          rr(ctx, w - 32 - bw, by, bw, 46, 12);
          ctx.stroke();
          text(ctx, "Don't trust", w - 32 - bw / 2, by + 30, { size: 19, w: 'Bold', color: CI, align: 'center' });
          fillRR(ctx, w - 32 - 2 * bw - 14, by, bw, 46, 12, t > tPress + 0.05 ? '#004FA3' : CB);
          text(ctx, 'Trust', w - 32 - 1.5 * bw - 14, by + 30, { size: 19, w: 'Bold', color: '#FFFFFF', align: 'center' });
          if (p > 0) EP.tapDot(ctx, w - 32 - 1.5 * bw - 14, by + 23, p);
        });
    },
    // SecurityWindow.cs: the three levels, the chosen one moving at each time in `at`.
    security(at) {
      return (ctx, t, a, pos) =>
        frame(ctx, a, pos, 'On the laptop · Security', () => {
          const w = 560, h = 236, y = 24;
          white(ctx, 0, y, w, h, 18);
          const level = at.filter((x) => t >= x).length;
          ['Trust only', 'PIN once per phone', 'PIN on every connection'].forEach((s, i) => {
            const cy = y + 54 + i * 64;
            const on = i === level;
            ctx.beginPath();
            ctx.arc(46, cy, 12, 0, Math.PI * 2);
            ctx.lineWidth = 2.5;
            ctx.strokeStyle = on ? CB : '#B9BCC4';
            ctx.stroke();
            if (on) {
              ctx.beginPath();
              ctx.arc(46, cy, 6, 0, Math.PI * 2);
              ctx.fillStyle = CB;
              ctx.fill();
            }
            text(ctx, `${i + 1}  ${s}`, 76, cy + 8, { size: 23, w: on ? 'Bold' : 'Regular', color: on ? CI : CG });
          });
        });
    },
    // The phone's own Bluetooth settings, pairing the laptop the way a headset is paired.
    pairing(tDone) {
      return (ctx, t, a, pos) =>
        frame(ctx, a, pos, 'On the phone · Bluetooth', () => {
          white(ctx, 0, 24, 520, 96, 18);
          icon(ctx, 'laptop', 46, 72, 32, CI);
          text(ctx, 'Laptop', 88, 66, { size: 25, w: 'Bold', color: CI });
          const done = t > tDone;
          text(ctx, done ? 'Paired' : 'Pairing…', 88, 94, { size: 19, color: done ? CB : CG });
          icon(ctx, 'bluetooth', 478, 72, 28, done ? CB : '#B9BCC4');
        });
    },
    // A small laptop screen: a desk with a document open, and whatever `draw` puts over it.
    screen(label, draw) {
      return (ctx, t, a, pos) =>
        frame(ctx, a, pos, label, () => {
          const w = 440, h = 275, y = 24;
          dark(ctx, 0, y, w, h, 16);
          const sx = 12, sy = y + 12, sw = w - 24, sh = h - 24;
          fillRR(ctx, sx, sy, sw, sh, 8, '#DCE3EE');
          fillRR(ctx, sx + 60, sy + 22, sw - 120, sh - 44, 6, '#FFFFFF');
          ctx.fillStyle = '#E3E6EC';
          for (let i = 0; i < 7; i++) ctx.fillRect(sx + 84, sy + 50 + i * 26, (sw - 168) * (i % 3 === 2 ? 0.6 : 1), 8);
          ctx.save();
          rr(ctx, sx, sy, sw, sh, 8);
          ctx.clip();
          ctx.translate(sx, sy);
          draw(ctx, t, sw, sh);
          ctx.restore();
        });
    },
    // The saved signature, as the picture Sign writes to Pictures/Edgepad.
    saved(entry, tShow) {
      return (ctx, t, a, pos) =>
        frame(ctx, a, pos, 'Saved to Pictures/Edgepad', () => {
          white(ctx, 0, 24, 420, 210, 18);
          const k = ease(seg(t, tShow, tShow + 0.4));
          ctx.save();
          ctx.globalAlpha *= k;
          ctx.translate(30, 44);
          // The phone's own signature, stroke by stroke as it is written, then held.
          EP.annotStrokes(ctx, 360, 170, Math.max(3.95, EP.warp(t, entry.anchors)), CI, true);
          ctx.restore();
        });
    },
    // TrayContext's menu: who is trusted is the laptop's to decide. `hot` lights rows at times [[t, row], ...].
    tray(hot, header = 'Connected to 5C:E4:2A:91:0D:6C') {
      return (ctx, t, a, pos) =>
        frame(ctx, a, pos, 'On the laptop · Edgepad tray menu', () => {
          const rows = [['power', 'Start with Windows'], ['terminal', 'Macros…'], ['smartphone', 'Security…'], ['unlink', 'Forget trusted phones']];
          const w = 460, h = 70 + rows.length * 56, y = 24;
          white(ctx, 0, y, w, h, 16);
          text(ctx, header, 28, y + 44, { size: 19, w: 'Bold', color: CG });
          let lit = -1;
          for (const [at, row] of hot) if (t >= at && t < at + 1.6) lit = row;
          rows.forEach(([ic, s], i) => {
            const cy = y + 92 + i * 56;
            if (i === lit) fillRR(ctx, 10, cy - 24, w - 20, 48, 10, '#E6F0FC');
            const danger = i === 3;
            icon(ctx, ic, 44, cy, 24, danger ? '#D70015' : CI);
            text(ctx, s, 80, cy + 8, { size: 22, color: danger ? '#D70015' : CI });
            if (i === 0) EP.toggle(ctx, EP.palette(1), w - 92, cy, 1);
          });
        });
    },
    // Rows of pills that arrive one by one: items {name, desc, at, icon, hot, descAt}.
    rows(items, label = null, dk = false) {
      return (ctx, t, a, pos) =>
        frame(ctx, a, pos, label, () => {
          items.forEach((it, i) => {
            const k = ease(seg(t, it.at - 0.1, it.at + 0.35));
            if (k <= 0) return;
            ctx.save();
            ctx.globalAlpha *= k;
            const y = i * 92 + (1 - k) * 14 + (label ? 24 : 0);
            if (dk) dark(ctx, 0, y, 500, 76, 38);
            else white(ctx, 0, y, 500, 76, 38);
            if (it.icon) icon(ctx, it.icon, 46, y + 38, 28, it.hot ? CB : CI);
            text(ctx, it.name, it.icon ? 82 : 36, y + 47, { size: 26, w: 'Black', color: dk ? '#F2F3F5' : CI });
            if (it.desc) text(ctx, it.desc, 482, y + 46, { size: 21, w: 'Bold', color: dk ? '#4DA3FF' : it.hot ? CB : CG, align: 'right', a: it.descAt == null ? 1 : ease(seg(t, it.descAt, it.descAt + 0.4)) });
            ctx.restore();
          });
        });
    },
    // The same three as a horizontal row of chips, for the band above the phone in the tall cuts.
    chips(items) {
      return (ctx, t, a, pos) =>
        frame(ctx, a, pos, null, () => {
          const gap = 16, w = (EP.F.head.max - 2 * gap) / 3, h = 64;
          items.forEach((it, i) => {
            const k = ease(seg(t, it.at - 0.1, it.at + 0.35));
            if (k <= 0) return;
            ctx.save();
            ctx.globalAlpha *= k;
            const x = i * (w + gap), y = (1 - k) * 12;
            white(ctx, x, y, w, h, 32);
            icon(ctx, 'laptop', x + 38, y + 32, 26, it.hot ? CB : CI);
            text(ctx, it.name, x + 70, y + 41, { size: 24, w: 'Black', color: CI });
            if (it.desc) text(ctx, 'Beta', x + w - 22, y + 40, { size: 18, w: 'Bold', color: CG, align: 'right', a: ease(seg(t, it.descAt, it.descAt + 0.4)) });
            ctx.restore();
          });
        });
    },
    // The laptops the app runs on. `beta` is when Mac and Linux are marked new and in beta; none, and the names stand alone.
    platforms(at, beta = null) {
      const items = [
        { name: 'Windows', at: at[0], icon: 'laptop', hot: true },
        { name: 'macOS', at: at[1], icon: 'laptop', desc: 'New, in beta', descAt: beta === null ? 1e9 : beta },
        { name: 'Linux', at: at[2], icon: 'laptop', desc: 'New, in beta', descAt: beta === null ? 1e9 : beta },
      ];
      return EP.F.tall ? C.chips(items) : C.rows(items);
    },
    // Straight talk, as the script says it, row by row at the words that say each.
    talk(at) {
      return C.rows([
        { name: 'Windows', desc: 'Tested on real hardware', at: at[0], hot: true },
        { name: 'macOS', desc: 'New, in beta', at: at[1] },
        { name: 'Linux', desc: 'New, in beta', at: at[2] },
        { name: 'Wi-Fi', desc: 'New, in beta', at: at[3] },
        { name: 'USB', desc: 'New, in beta', at: at[4] },
        { name: 'Expect bugs', desc: 'Open an issue', at: at[5], hot: true },
      ], null, true);
    },
    // Clipboard sync: text copied on the laptop's card arrives on the phone; from the phone, back in Edgepad, it goes the
    // other way. `img` is when images are named (none: null), `back` when the phone's copy is sent.
    clip(...a) {
      return EP.F.tall ? C.clipRow(...a) : C.clipCard(...a);
    },
    // The same, as one row for the band between the sub-line and the phone (the tall cuts).
    clipRow(copy, arrive, img, back) {
      const SAMPLE = 'Dinner at 8, bring the cable';
      return (ctx, t, a, pos) =>
        frame(ctx, a, pos, null, () => {
          const bw = 400, h = 72, gx = EP.F.head.max - bw;
          white(ctx, 0, 0, bw, h, 18);
          ctx.save();
          ctx.globalAlpha *= ease(seg(t, copy - 0.35, copy));
          fillRR(ctx, 14, 14, measure(ctx, SAMPLE, 22) + 20, 44, 8, '#E6F0FC');
          ctx.restore();
          text(ctx, SAMPLE, 24, 46, { size: 22, color: CI });
          const arrow = (dir, k, y) => {
            ctx.save();
            ctx.globalAlpha *= k;
            ctx.strokeStyle = LBL;
            ctx.lineWidth = 4;
            ctx.lineCap = ctx.lineJoin = 'round';
            const x0 = bw + 28, x1 = gx - 28;
            ctx.beginPath();
            ctx.moveTo(x0, y);
            ctx.lineTo(x1, y);
            ctx.moveTo(dir > 0 ? x1 - 9 : x0 + 9, y - 9);
            ctx.lineTo(dir > 0 ? x1 : x0, y);
            ctx.lineTo(dir > 0 ? x1 - 9 : x0 + 9, y + 9);
            ctx.stroke();
            ctx.restore();
          };
          arrow(1, ease(seg(t, arrive - 0.3, arrive)), 26);
          arrow(-1, ease(seg(t, back - 0.3, back)), 48);
          const ph = ease(seg(t, arrive, arrive + 0.35));
          if (ph > 0) {
            ctx.save();
            ctx.globalAlpha *= ph;
            dark(ctx, gx, 0, bw, h, 18);
            text(ctx, SAMPLE, gx + 24, 46, { size: 22, color: '#F2F3F5' });
            if (img !== null && t > img) icon(ctx, 'image', gx + bw - 30, 36, 26, '#4DA3FF');
            ctx.restore();
          }
        });
    },
    clipCard(copy, arrive, img, back) {
      const SAMPLE = 'Dinner at 8, bring the cable';
      return (ctx, t, a, pos) =>
        frame(ctx, a, pos, 'On the laptop · Notes', () => {
          white(ctx, 0, 24, 520, 150, 18);
          const sel = ease(seg(t, copy - 0.35, copy));
          const tw = measure(ctx, SAMPLE, 26) + 16;
          fillRR(ctx, 28, 52, tw, 40, 8, '#E6F0FC', 1);
          ctx.save();
          ctx.globalAlpha *= 1 - sel;
          fillRR(ctx, 28, 52, tw, 40, 8, '#FFFFFF');
          ctx.restore();
          text(ctx, SAMPLE, 36, 80, { size: 26, color: CI });
          const chip = ease(seg(t, copy, copy + 0.2)) * (1 - ease(seg(t, copy + 1.6, copy + 1.9)));
          if (chip > 0) {
            fillRR(ctx, 520 - 26 - 104, 52, 104, 40, 20, '#E6F0FC', 1);
            text(ctx, 'Copied', 520 - 26 - 52, 79, { size: 20, w: 'Bold', color: CB, align: 'center', a: chip });
          }
          const pasted = ease(seg(t, back, back + 0.3));
          if (pasted > 0) text(ctx, SAMPLE, 36, 134, { size: 26, color: CI, a: pasted });
          // The way across, and the phone's side of it.
          const down = ease(seg(t, arrive - 0.3, arrive)), up = ease(seg(t, back - 0.3, back));
          const arrow = (x, dir, k) => {
            ctx.save();
            ctx.globalAlpha *= k;
            ctx.strokeStyle = LBL;
            ctx.lineWidth = 4;
            ctx.lineCap = 'round';
            ctx.lineJoin = 'round';
            ctx.beginPath();
            ctx.moveTo(x, 184);
            ctx.lineTo(x, 214);
            ctx.moveTo(x - 9, dir > 0 ? 205 : 193);
            ctx.lineTo(x, dir > 0 ? 214 : 184);
            ctx.lineTo(x + 9, dir > 0 ? 205 : 193);
            ctx.stroke();
            ctx.restore();
          };
          if (down > 0) arrow(230, 1, down);
          if (up > 0) arrow(290, -1, up);
          const ph = ease(seg(t, arrive, arrive + 0.35));
          if (ph > 0) {
            ctx.save();
            ctx.globalAlpha *= ph;
            text(ctx, 'On the phone · Clipboard', 0, 246, { size: 22, w: 'Bold', color: LBL });
            dark(ctx, 0, 262, 520, 86, 18);
            text(ctx, SAMPLE, 28, 314, { size: 26, color: '#F2F3F5' });
            if (img !== null) {
              const k = ease(seg(t, img, img + 0.35));
              if (k > 0) {
                ctx.globalAlpha *= k;
                fillRR(ctx, 520 - 26 - 70, 276, 70, 58, 10, '#3A3C43');
                icon(ctx, 'image', 520 - 26 - 35, 305, 32, '#4DA3FF');
              }
            }
            ctx.restore();
          }
        });
    },
  };

  // ---- acts: what the phone does while a line is spoken ----
  // Surface buttons in dp: Settings 147, Lock 195, Controls 243, all on the top row.
  const PLAY = [195, 0.9 * 844], LOCK = [195, 40], CONTROLS = [243, 40];
  // Anchors that end by holding: a screen that must not run past a point on its own clock (a tap, a latency, a Disconnect).
  const cap = (L, anchors, lt) => [...anchors, [L.next + 1.2, lt]];
  const A = {};
  // The hook: a dark room, a laptop glowing on a TV stand far across it, one move in, and the phone rises into frame
  // as `word` is said, so the room and the phone are one world with no cut.
  A.hook = (word) => (L, D) => {
    const t = L.w(word);
    D.room.to(0, t + 1.1, 1);
    D.move(t - 0.4, 'right', 1.5);
  };
  A.settle = (L, D) => D.move(L.t0 - 0.3, 'hero', 1.3);
  // Meet Edgepad: a glide on the pad, a reach for Controls, and the three laptops it runs on.
  A.meet = (L, D) => {
    D.move(L.t0 - 0.2, 'hero', 1.2);
    const g = L.w('trackpad');
    D.touch([[g - 0.1, 160, 380], [g + 0.4, 210, 420], [g + 0.9, 250, 470]]);
    D.tap(L.w('control'), ...CONTROLS);
    D.press(L.w('control'), 'controls');
    D.card(L.w('windows') - 0.2, L.next - 0.1, C.platforms([L.w('windows'), L.w('mac'), L.w('linux')]), EP.F.band);
  };
  // A short meet: the same glide and reach, no laptops.
  A.meetShort = (L, D) => {
    D.move(L.t0 - 0.2, 'hero', 1.2);
    const g = L.w('edgepad');
    D.touch([[g, 160, 380], [g + 0.5, 210, 420], [g + 1.0, 250, 470]]);
    D.tap(L.w('control'), ...CONTROLS);
    D.press(L.w('control'), 'controls');
  };
  // Companion app, then pairing: the Devices screen waits until a laptop is tapped (screens.js devices: tap 1.15, connected
  // 2.5; it is held under 2.9, where the screen would print a round-trip time).
  A.pair = (L, D) => {
    D.screen(L.t0 - 0.2, 'devices', {}, cap(L, [[L.t0 - 0.2, 0.3], [L.w('pair'), 1.15], [L.w('headset') + 0.3, 2.6]], 2.8));
    D.move(L.t0 - 0.3, 'hero', 1.1);
    D.card(L.t0, L.w('pair') - 0.1, C.tray([], 'Edgepad is running'));
    D.card(L.w('pair'), L.next - 0.1, C.pairing(L.w('headset')));
  };
  A.direct = (L, D) => {
    D.screen(L.t0 - 0.2, 'surface');
    D.move(L.t0 - 0.3, 'hero', 1.0);
    const g = L.w('talks');
    D.touch([[g, 150, 340], [g + 0.3, 196, 380], [g + 0.75, 262, 450]]);
  };
  // Three fingers swipe one way, four the other: the dots only, as "show taps" draws them.
  const swipe = (D, t, n, dx) => {
    for (let i = 0; i < n; i++) {
      const x = 195 + (i - (n - 1) / 2) * 52, y = 440 + (i % 2) * 18;
      D.touch([[t, x, y], [t + 0.12, x, y], [t + 0.6, x + dx, y], [t + 0.72, x + dx, y]], false);
    }
  };
  // The trackpad: one finger, two (scroll, pinch), three and four, and the gestures page where each is remapped.
  A.trackpad = (L, D) => {
    D.screen(L.t0 - 0.3, 'surface');
    D.move(L.t0 - 0.3, 'hero', 1.0);
    const g = L.w('one');
    D.touch([[g, 150, 340], [g + 0.3, 196, 380], [g + 0.75, 262, 450], [g + 1.1, 222, 540]]);
    const s = L.w('two');
    D.touch([[s, 160, 380], [s + 0.2, 160, 380], [s + 0.85, 160, 540], [s + 1.0, 160, 540]], false);
    D.touch([[s, 232, 380], [s + 0.2, 232, 380], [s + 0.85, 232, 540], [s + 1.0, 232, 540]], false);
    const p = L.w('pinch');
    D.touch([[p, 175, 452], [p + 0.15, 175, 452], [p + 0.85, 115, 390], [p + 1.0, 115, 390]], false);
    D.touch([[p, 218, 452], [p + 0.15, 218, 452], [p + 0.85, 278, 514], [p + 1.0, 278, 514]], false);
    swipe(D, L.w('three'), 3, -120);
    swipe(D, L.w('four'), 4, 120);
    const r = L.w('every');
    D.screen(r - 0.25, 'gestures', {}, cap(L, [[r - 0.25, 0.9], [r + 1.6, 2.75]], 3.0));
    D.move(r - 0.35, 'settings', 1.0);
  };
  // The short trackpad line: a glide, a pinch, then the gestures page.
  A.trackpadShort = (L, D) => {
    D.screen(L.t0 - 0.3, 'surface');
    D.move(L.t0 - 0.3, 'hero', 1.0);
    const g = L.w('multitouch');
    D.touch([[g, 150, 340], [g + 0.3, 196, 380], [g + 0.7, 262, 450], [g + 0.95, 222, 520]]);
    const p = L.w('trackpad');
    D.touch([[p, 175, 452], [p + 0.12, 175, 452], [p + 0.6, 125, 400], [p + 0.7, 125, 400]], false);
    D.touch([[p, 218, 452], [p + 0.12, 218, 452], [p + 0.6, 268, 504], [p + 0.7, 268, 504]], false);
    const r = L.w('remappable');
    D.screen(r - 0.25, 'gestures', {}, cap(L, [[r - 0.25, 0.9], [r + 1.4, 2.75]], 3.0));
    D.move(r - 0.35, 'settings', 1.0);
  };
  // The eight halves crowd the corners' labels on a phone this narrow, so the film turns the split back off, under the
  // camera move into the next line, rather than carry it on.
  const unsplit = (L, D) => {
    D.extra.to(L.next - 0.35, L.next + 0.05, 0);
    D.split.to(L.next - 0.2, L.next + 0.5, 0);
  };
  // Every corner is a dial: the camera visits each as it is named, a finger slides it and the laptop shows the level.
  // Then the switch in Settings > Corners, and on the surface four become eight.
  A.dials = (L, D) => {
    D.screen(L.t0 - 0.3, 'surface');
    const vol = L.w('volume'), bri = L.w('brightness'), zoom = L.w('zoom'), sc = L.w('scrub');
    D.move(L.t0 - 0.3, 'hero', 0.9);
    D.move(vol - 0.75, 'tl', 0.85);
    D.dial('VOL', vol - 0.05, bri - 0.75, 63);
    D.move(bri - 0.6, 'tr', 0.75);
    D.dial('BRI', bri, zoom - 0.6, 82);
    D.move(zoom - 0.5, 'br', 0.75);
    D.dial('ZOOM', zoom, sc - 0.55, 3);
    D.move(sc - 0.45, 'bl', 0.75);
    D.scrub(sc + 0.05, sc + 0.8, 74);
    D.card(vol - 0.2, bri - 0.55, C.level('Volume', (t) => D.vol.at(t)));
    D.card(bri - 0.1, zoom - 0.4, C.level('Brightness', (t) => D.bri.at(t)));
    const flip = L.w('flip'), sw = L.w('switch'), bc = L.w('become'), eight = L.w('eight');
    D.screen(flip - 0.5, 'corners', {}, [[flip - 0.5, 0.3], [sw + 0.05, 1.0], [bc, 1.5]]);
    D.move(flip - 0.6, 'settings', 0.8);
    D.screen(bc - 0.3, 'surface');
    D.move(bc - 0.4, 'right', 0.9);
    D.split.to(bc - 0.1, bc + 0.9, 1);
    D.extra.to(eight - 0.3, eight + 0.5, 1);
    unsplit(L, D);
  };
  // In sync: the phone moves the volume and the laptop's flyout follows; the owner moves it on the laptop and the dial follows.
  A.sync = (L, D) => {
    D.move(L.t0 - 0.3, 'top', 1.1);
    D.vol.to(L.t0 - 0.3, L.t0, 64);
    D.dial('VOL', L.w('syncs'), L.w('ways') + 0.2, 80);
    D.vol.to(L.w('change'), L.w('moves') + 0.5, 22);
    D.card(L.t0 + 0.1, L.next + 0.2, C.winVolume((t) => D.vol.at(t)));
  };
  // Now playing on the pad, then the lock: two quick taps (focus, then locked); the trackpad is dead, the dials and the
  // media still answer.
  A.media = (L, D) => {
    D.move(L.t0 - 0.3, 'media', 1.0);
    const lock = L.w('lock'), media = L.w('media');
    D.move(lock - 0.6, 'hero', 0.9);
    D.tap(lock, ...LOCK, 0.1);
    D.press(lock, 'lock');
    D.mode(lock + 0.05, 'focus');
    D.tap(lock + 0.24, ...LOCK, 0.1);
    D.press(lock + 0.24, 'lock');
    D.mode(lock + 0.29, 'locked');
    D.dial('VOL', L.w('dials') - 0.1, media - 0.1, 40);
    D.tap(media, ...PLAY);
    D.press(media, 'play');
    D.play(media + 0.05, { playing: false });
    const off = L.next - 0.35;
    D.tap(off, ...LOCK, 0.1);
    D.press(off, 'lock');
    D.mode(off + 0.05, 'normal');
    D.play(off + 0.1, { playing: true });
  };
  // Tap Controls: the popup opens over the surface (screens.js controls: popup about 0.45, Keyboard tapped 2.2), timed so
  // Keyboard is tapped as the next line begins.
  A.controls = (L, D) => {
    D.move(L.t0 - 0.2, 'right', 1.0);
    D.screen(L.t0 - 0.1, 'controls', {}, [[L.t0 - 0.1, 0], [L.next - 0.1, 2.2]]);
  };
  // Sideways for the keyboard: Ctrl latched, C, V, then Shift held while Alt is tapped.
  A.keyboard = (L, D, cam = 'land') => {
    const t = L.t0 - 0.1;
    D.turn.to(t, t + 0.95, 1);
    D.move(t, cam, 0.95);
    D.screen(t, 'keyboard', {}, [[t, 0.3], [L.w('control'), 1.1], [L.w('alt'), 2.6], [L.w('shift'), 3.9], [L.w('tap'), 4.3]], true);
  };
  A.gamepad = (L, D, cam = 'land') => {
    D.move(L.t0 - 0.2, cam, 0.6);
    D.screen(L.t0 - 0.2, 'gamepad', {}, [[L.t0 - 0.2, 0], [L.w('xbox'), 1.0], [L.t1, 3.6]], true);
  };
  // Annotate: the pen's menu (colours, width, opacity), strokes on the phone's pad landing on the laptop's screen; then
  // Sign, written on the pad and saved as an image.
  const strokes = (entry) => C.screen('On the laptop · the screen', (ctx, t, sw, sh) => EP.annotStrokes(ctx, sw, sh, EP.warp(t, entry.anchors), '#16171A', false));
  A.pen = (L, D) => {
    const t0 = L.t0, pen = L.w('pen'), and = L.w('and'), sig = L.w('signature'), sv = L.w('saves'), img = L.w('image'), doc = L.w('document');
    D.turn.to(t0 - 0.3, t0 + 0.6, 0);
    D.move(t0 - 0.3, 'right', 0.9);
    D.screen(t0 - 0.1, 'annotate', {}, [[t0 - 0.1, 0], [pen, 0.15], [pen + 1.1, 0.7], [and - 0.3, 3.2], [sig, 3.85], [sv + 0.1, 4.5], [doc + 0.3, 6.45], [L.next - 0.1, 6.7]]);
    const entry = D.screens[D.screens.length - 1];
    D.card(pen + 0.4, sig - 0.5, strokes(entry));
    D.card(sig + 0.1, L.next, C.saved(entry, sig + 0.2));
  };
  // Capture: Region, a drag on the pad that draws the box in the laptop's Snipping Tool, Record; then Save to Phone.
  A.capture = (L, D) => {
    const region = L.w('region'), full = L.w('full'), rec = L.w('recording'), save = L.w('save');
    D.screen(L.t0 - 0.2, 'capture', {}, [[L.t0 - 0.2, 0.4], [region, 1.0], [full + 0.5, 3.4], [rec + 0.2, 4.95]]);
    const entry = D.screens[D.screens.length - 1];
    D.card(region + 0.3, L.next, C.screen('On the laptop · Snipping Tool', (ctx, t, sw, sh) => {
      const lt = EP.warp(t, entry.anchors);
      const k = EP.ease(seg(lt, 2.2, 3.4));
      if (lt < 1.9) return;
      // Snipping Tool dims the screen and leaves the region being dragged out clear.
      const x = sw * 0.25, y = sh * 0.38, w = sw * lerp(0.02, 0.48, k), h = sh * lerp(0.02, 0.4, k);
      ctx.fillStyle = 'rgba(10,14,24,0.45)';
      ctx.fillRect(0, 0, sw, y);
      ctx.fillRect(0, y + h, sw, sh - y - h);
      ctx.fillRect(0, y, x, h);
      ctx.fillRect(x + w, y, sw - x - w, h);
      ctx.lineWidth = 2;
      ctx.strokeStyle = '#FFFFFF';
      ctx.setLineDash([6, 4]);
      ctx.strokeRect(x, y, w, h);
      ctx.setLineDash([]);
    }));
    D.screen(save - 0.15, 'capture', { phone: true, folder: 'Pictures/Edgepad' }, [[save - 0.15, 5.6], [save + 2.5, 8.1]]);
  };
  // The short pen and capture line: the pen's menu and an ellipse, then Capture saved to the phone.
  A.penShort = (L, D) => {
    const pen = L.w('pen'), ss = L.w('screenshots'), rec = L.w('recordings'), sv = L.w('saved');
    // Annotate is held at least 3 s with a stroke on it: it starts as early as the gap before the line allows, and
    // Capture waits for it if Screenshots is said sooner.
    const an = Math.max(L.t0 - 0.6, Math.min(L.t0 - 0.1, ss - 3.2));
    const cap = Math.max(ss - 0.2, an + 3.2);
    const cr = Math.max(rec, cap + 1.6);
    const back = Math.max(an - 0.3, L.prev + 0.05);
    D.turn.to(back, back + 0.9, 0);
    D.move(back, 'right', 0.9);
    D.screen(an, 'annotate', {}, [[an, 0.1], [an + 0.6, 0.7], [an + 1.0, 0.9], [cap - 0.2, 2.4]]);
    D.screen(cap, 'capture', {}, [[cap, 0.4], [cap + 0.4, 1.0], [cr, 3.4], [cr + 0.4, 4.95]]);
    D.screen(Math.max(sv - 0.15, cr + 0.7), 'capture', { phone: true, folder: 'Pictures/Edgepad' }, [[Math.max(sv - 0.15, cr + 0.7), 5.6], [Math.max(sv - 0.15, cr + 0.7) + 2.5, 8.1]]);
  };
  // Macros: the deck (a tap runs one, a slide to page two and back), then a shape drawn on the pad fires one.
  // The finger rests until the stroke turns solid, draws a circle, lifts, and the macro runs.
  A.macros = (L, D) => {
    const apps = L.w('apps'), across = L.w('across'), need = L.w('need'), bind = L.w('bind'), draw = L.w('draw');
    D.move(L.t0 - 0.3, 'hero', 1.0);
    D.screen(L.t0 - 0.25, 'macros', {}, [[L.t0 - 0.25, 0.2], [apps, 0.95], [across, 2.0], [need + 0.2, 3.1]]);
    D.screen(bind - 0.2, 'surface');
    const keys = [[draw, 195, 330], [draw + 0.45, 195, 330]];
    for (let i = 1; i <= 16; i++) {
      const a = -Math.PI / 2 + (i / 16) * Math.PI * 2;
      keys.push([draw + 0.45 + i * 0.07, 195 + 92 * Math.cos(a), 422 + 92 * Math.sin(a)]);
    }
    D.touch(keys);
    D.shaping.push([draw + 0.45, draw + 1.6]);
    D.play(draw + 1.9, { app: 'Spotify', title: 'Golden Hour', pos: 3, dur: 214, playing: true });
  };
  A.macrosShort = (L, D) => {
    const apps = L.w('apps'), urls = L.w('urls'), sc = L.w('scripts'), cb = L.w('clipboard'), both = L.w('both');
    D.move(L.t0 - 0.3, 'hero', 1.0);
    D.screen(L.t0 - 0.25, 'macros', {}, [[L.t0 - 0.25, 0.2], [apps, 0.95], [urls, 2.0], [sc + 0.2, 3.1]]);
    D.screen(cb - 0.2, 'settings', { back: 'Surface' }, [[cb - 0.2, 1.6], [both, 2.0], [both + 1.5, 2.6]]);
    D.move(cb - 0.3, 'wide', 0.9);
    D.card(cb - 0.1, L.next - 0.1, C.clip(cb, both, null, L.w('ways') + 0.2), EP.F.band);
  };
  // Clipboard: text copied on the laptop card arrives on the phone; from the phone, back in Edgepad, it goes the other way.
  A.clipboard = (L, D) => {
    const copy = L.w('clipboard'), both = L.w('both'), im = L.w('images'), back = L.w('goes'), come = L.w('come');
    D.screen(L.t0 - 0.2, 'settings', { back: 'Surface' }, [[L.t0 - 0.2, 1.6], [both, 2.0], [come - 0.2, 2.9]]);
    D.move(L.t0 - 0.3, 'wide', 1.0);
    D.screen(come - 0.2, 'surface');
    D.card(L.t0, L.next - 0.1, C.clip(copy, both, im, back), EP.F.band);
  };
  // Bluetooth by default, Wi-Fi and USB if you want them: the Devices screen with its transport chips, held connected.
  A.transports = (L, D) => {
    D.screen(L.t0 - 0.2, 'devices', {}, [[L.t0 - 0.2, 2.6], [L.next + 1.2, 2.85]]);
    D.move(L.t0 - 0.3, 'hero', 1.0);
  };
  // Two laptops at once (System settings), a dial that switches between them (the LAPTOP dial among eight halves), and it
  // keeps running in the background (Stay connected, then the notification, held short of its Disconnect tap).
  A.twoLaptops = (L, D) => {
    const sw = L.w('switch'), dial = L.w('dial'), keeps = L.w('keeps'), run = L.w('running'), bg = L.w('background');
    D.screen(L.t0 - 0.2, 'settings', { back: 'Devices' }, [[L.t0 - 0.2, 0.0], [sw - 0.2, 0.6], [sw + 1.0, 0.8]]);
    D.move(L.t0 - 0.3, 'wide', 1.0);
    D.screen(sw - 0.35, 'surface');
    D.move(sw - 0.4, 'top', 0.9);
    D.kinds(sw - 0.4, ['DEVICES', 'TABS', 'SIDE', 'DESK']);
    D.split.to(sw - 0.2, sw + 0.6, 1);
    D.extra.to(sw + 0.1, sw + 0.8, 1);
    D.step('DEVICES', 1, dial - 0.15, dial + 0.85, 1);
    D.extra.to(keeps - 0.4, keeps, 0);
    D.split.to(keeps - 0.3, keeps + 0.4, 0);
    D.screen(keeps - 0.2, 'settings', { back: 'Devices' }, [[keeps - 0.2, 0.4], [run, 0.9], [bg + 0.5, 1.2]]);
    D.move(keeps - 0.3, 'wide', 0.9);
    D.screen(bg - 0.1, 'linkNotice', { name: 'Home PC', others: 1 }, [[bg - 0.1, 0.0], [L.next + 1.2, 1.15]]);
    D.move(bg - 0.2, 'hero', 0.9);
  };
  // The trust list: the laptop's question and its Trust button, the tray menu's Forget, then the PIN.
  A.trustList = (L, D) => {
    const phone = L.w('phone'), rev = L.w('revoke'), add = L.w('add'), six = L.w('six'), first = L.w('first'), every = L.w('every');
    D.screen(L.t0 - 0.3, 'surface');
    D.move(L.t0 - 0.3, 'right', 1.0);
    D.card(L.t0 + 0.2, rev - 0.15, C.trust(phone + 0.4));
    D.card(rev - 0.1, add - 0.2, C.tray([[rev + 0.1, 3]]));
    D.card(add - 0.1, L.next, C.security([first, every]));
    D.screen(add - 0.1, 'pin', {}, [[add - 0.1, 0], [six, 0.6], [L.t1, 3.95]]);
  };
  // Make it yours: the corners remapped, dark or light, a background of your own, then sideways.
  A.yours = (L, D) => {
    const remap = L.w('remap'), tune = L.w('tune'), light = L.w('light'), bg = L.w('background'), side = L.w('sideways');
    D.screen(L.t0 - 0.25, 'corners', { split: true, pick: 4, scroll: 120, scrollAt: 1.4 }, [[remap - 0.25, 0.3], [remap + 0.6, 0.95], [tune, 1.5]]);
    D.move(L.t0 - 0.3, 'settings', 1.0);
    D.screen(light - 0.7, 'surface');
    D.move(light - 0.75, 'right', 1.0);
    D.theme.to(light - 0.2, light + 0.5, 1);
    D.theme.to(bg - 0.2, bg + 0.4, 0);
    D.bg.to(bg - 0.1, bg + 0.6, 1);
    D.screen(side - 0.2, 'surface', {}, null, true);
    D.turn.to(side - 0.2, side + 0.75, 1);
    D.move(side - 0.2, 'landRight', 0.95);
    D.turn.to(L.next - 0.3, L.next + 0.5, 0);
    D.bg.to(L.next - 0.3, L.next + 0.3, 0);
  };
  // Straight talk: the phone steps aside and plain cards say it, a row at each word.
  A.talk = (L, D) => {
    D.move(L.t0 - 0.2, 'off', 1.2);
    D.card(L.t0, L.next - 0.1, C.talk([L.w('windows'), L.w('mac'), L.w('linux'), L.w('wifi'), L.w('usb'), L.w('expect')]), EP.F.talk);
  };
  A.platformsShort = (L, D) => {
    D.screen(L.t0 - 0.2, 'devices', {}, [[L.t0 - 0.2, 2.6], [L.next + 1.2, 2.85]]);
    D.move(L.t0 - 0.3, 'hero', 1.0);
    D.card(L.w('windows') - 0.2, L.next - 0.1, C.platforms([L.w('windows'), L.w('mac'), L.w('linux')], L.w('new')), EP.F.band);
  };
  A.calm = (L, D) => {
    D.screen(L.t0 - 0.3, 'surface');
    D.move(L.t0 - 0.3, 'right', 1.2);
  };
  A.end = (L, D) => D.move(L.t0 - 0.4, 'end', 1.3);
  A.keyboardShort = (L, D) => {
    const kb = L.w('keyboard'), g = L.w('gamepad');
    // The whole turn happens in the gap after the dials line's last word, and lands by "keyboard"; the gamepad comes in only on "gamepad".
    const rot = L.prev + 0.1;
    const land = rot + Math.max(0.5, Math.min(0.95, kb - rot));
    D.turn.to(rot, land, 1);
    D.move(rot, 'land', land - rot);
    D.screen(rot, 'keyboard', {}, [[rot, 0.3], [land + 0.2, 1.1], [g - 0.1, 3.5]], true);
    D.screen(g - 0.2, 'gamepad', {}, [[g - 0.2, 0], [L.w('xbox'), 1.0], [L.t1, 3.6]], true);
  };

  window.BEATS = {
    full: [
      // What it is
      { h: "Your laptop's on the *TV.*", s: ['Or the *projector.*@projector', 'Or across the *room.*@across'], act: A.hook('running') },
      { h: "You're not next to *it.*", s: "You don't *need* to be.@don't", act: A.settle },
      { h: 'Meet | *Edgepad 4.*', s: ['An *open-source* Android app@open', 'A trackpad, a remote, a *control deck*@trackpad', 'Windows · Mac · *Linux*@windows'], act: A.meet },
      // Setup
      { h: 'Pair *once.*', s: ['A *companion app* on the laptop@a', 'Over *Bluetooth,* like a headset@pair'], act: A.pair },
      { h: 'No account. | No server. | No *cloud.*', s: 'Your phone talks *straight* to your laptop@your', act: A.direct },
      // The trackpad
      { h: 'The middle is a *trackpad.*', s: ['One finger *moves*@one', 'Two *scroll* and pinch@two', 'Three switch *desktops*@three', 'Four switch *apps*@four', 'Every gesture is *remappable*@every'], act: A.trackpad },
      // Corner dials
      { h: 'The corners are *dials.*', s: ['*Volume*@volume', '*Brightness*@brightness', '*Zoom*@zoom', '*Video scrubbing*@video', 'Four become *eight*@flip'], act: A.dials },
      { h: 'Volume *syncs* both ways.', s: ['Change it on the *laptop*@change', 'And the dial *moves with it*@and'], act: A.sync },
      { h: 'Playing, then *locked.*', s: ["Whatever's *playing* shows up on the pad@whatever", 'Pocketing your phone? *Lock* the trackpad@pocketing', 'Dials and media stay *live*@dials'], act: A.media },
      // Controls
      { h: 'Tap *Controls.*', s: 'For the *rest*@for', act: A.controls },
      { h: 'A full *keyboard.*', s: 'Control · Alt · Shift: *hold* or tap', lay: 'top', act: A.keyboard },
      { h: 'A real *gamepad.*', s: ['Seen as an *Xbox controller*@xbox', 'On Windows and *Linux*@windows', 'Your games just see a *controller*@games'], lay: 'top', act: A.gamepad },
      { h: 'Pen, then *sign.*', s: ['*Pen* · width · opacity@pen', 'A *signature* saved as an image@and'], act: A.pen },
      { h: '*Capture* it.', s: ['A *region*@region', 'The *full screen*@full', 'A *recording*@recording', 'Save to *your phone*@save'], act: A.capture },
      // Macros and clipboard
      { h: 'Up to *fifteen* | macros.', s: ['Apps · URLs · *scripts*@macros', 'Across as many *pages* as you need@across', 'Bind one to a *shape*@bind', 'Draw it on the pad, and it *fires*@draw'], act: A.macros },
      { h: 'Clipboard *syncs.*', s: ['Both *ways*@works', 'Text and *images*@text', 'Back in Edgepad, it *goes*@from'], act: A.clipboard },
      // Connections and trust
      { h: '*Bluetooth* by default.', s: 'Wi-Fi and *USB* if you want them@wifi', act: A.transports },
      { h: '*Two* laptops at once.', s: ['Switch with a *dial*@switch', 'It keeps running in the *background*@it'], act: A.twoLaptops },
      { h: 'The laptop *decides.*', s: ['*Approve* one phone, or a few@approve', '*Revoke* them all@revoke', 'A six-digit *PIN*@add', 'First pairing, or *every* connection@first'], act: A.trustList },
      // Make it yours
      { h: 'Make it *yours.*', s: ['*Remap* the corners, tune the dials@remap', 'Dark or *light*@dark', 'Your own *background*@own', 'Upright or *sideways*@upright'], act: A.yours },
      // Straight talk
      { h: 'Straight *talk.*', s: null, act: A.talk },
      // Get it
      { end: true, act: A.end },
      { close: true, h: 'Your laptop, from anywhere *in the room.*', act: () => {} },
    ],
    short: [
      { h: "Your laptop's on the *TV.*", s: ['Or the *projector.*@projector', 'Or across the *room.*@across'], act: A.hook('across') },
      { h: 'This is *Edgepad 4.*', s: ['An *open-source* Android app@an', 'Your phone, a *control deck*@turns'], act: A.meetShort },
      { h: 'Multi-touch *trackpad.*', s: 'Remappable *gestures*@remappable', act: A.trackpadShort },
      { h: 'Corner *dials.*', s: ['*Volume*@volume', '*Brightness*@brightness', '*Zoom*@zoom', '*Scrubbing*@scrubbing', 'Four become *eight*@flip'], act: A.dials },
      { h: 'Keyboard, *gamepad.*', s: ['A full *keyboard*@keyboard', 'Seen as an *Xbox controller*@xbox', 'On Windows and *Linux*@windows'], act: A.keyboardShort },
      { h: 'A *pen* on your screen.', s: ['*Pen* · width · opacity@pen', '*Screenshots* and recordings@screenshots', 'Saved to your *phone*@saved'], act: A.penShort },
      { h: '*Macros,* clipboard.', s: ['Apps · URLs · *scripts*@apps', 'Clipboard sync, *both ways*@clipboard'], act: A.macrosShort },
      { h: '*Bluetooth,* Wi-Fi, USB.', s: ['Windows · Mac · *Linux*@windows', 'Mac, Linux, Wi-Fi, USB: *new, in beta*@new'], act: A.platformsShort },
      { h: 'No account. | No server. | No *cloud.*', s: 'Every line of code is *public*@every', act: A.calm },
      { end: true, act: A.end },
      { close: true, h: 'Your laptop, from anywhere *in the room.*', act: () => {} },
    ],
  };
})();
