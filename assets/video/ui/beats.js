// What each line of narration shows: its headline (one keyword between *stars*, | breaks a line), its sub-line, and an
// act that moves the phone, the screens and the laptop cards in time with the words (L.w('word') is when it is said).
// One entry per line of scripts/<id>.json, in order. h: null keeps the headline already up.
(function () {
  const { text, fillRR, rr, icon, ease, seg, lerp, clamp } = EP;
  const INK = '#16171A', GREY = '#6B6E76', BLUE = '#0068D6';

  // ---- laptop cards, drawn on the page in the laptop's own look ----
  // A card rises 16 px as it fades in; every painter is given (ctx, t, a, {x, y}).
  function frame(ctx, a, pos, label, fn) {
    ctx.save();
    ctx.globalAlpha *= a;
    ctx.translate(pos.x, pos.y + (1 - a) * 16);
    if (label) text(ctx, label, 0, 0, { size: 22, w: 'Bold', color: GREY });
    fn();
    ctx.restore();
  }
  function white(ctx, x, y, w, h, r = 22) {
    ctx.save();
    ctx.shadowColor = 'rgba(24,32,56,0.14)';
    ctx.shadowBlur = 44;
    ctx.shadowOffsetY = 16;
    fillRR(ctx, x, y, w, h, r, '#FFFFFF');
    ctx.restore();
  }
  const C = {
    // LevelOverlay.cs, the panel the laptop shows while the phone moves a level: 280 x 68 at 1.6 x, on the dark card.
    level(kind, get) {
      return (ctx, t, a, pos) =>
        frame(ctx, a, pos, 'On the laptop', () => {
          const s = 1.6, w = 280 * s, h = (36 + 20 * 0.7165 + 12 + 6) * s, y = 26;
          ctx.save();
          ctx.shadowColor = 'rgba(10,14,30,0.30)';
          ctx.shadowBlur = 40;
          ctx.shadowOffsetY = 14;
          fillRR(ctx, 0, y, w, h, 18 * s, '#1B1C20');
          ctx.restore();
          const v = Math.round(get(t));
          const pad = 18 * s;
          icon(ctx, kind === 'Brightness' ? 'sun' : 'volume', pad + 11 * s, y + h / 2, 22 * s, '#4DA3FF');
          const left = pad + 22 * s + 14 * s, right = w - pad;
          const base = y + pad + 20 * s * 0.7165;
          text(ctx, kind, left, base, { size: 13 * s, w: 'Bold', color: '#9A9CA3' });
          text(ctx, String(v), right, base, { size: 20 * s, w: 'Black', color: '#F2F3F5', align: 'right' });
          const ty = base + 12 * s;
          fillRR(ctx, left, ty, right - left, 6 * s, 3 * s, '#3A3C43');
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
          icon(ctx, 'volume', 46, y + h / 2, 30, INK);
          const x0 = 88, x1 = 420, cy = y + h / 2;
          fillRR(ctx, x0, cy - 2.5, x1 - x0, 5, 2.5, '#C9CBD1');
          fillRR(ctx, x0, cy - 2.5, ((x1 - x0) * v) / 100, 5, 2.5, BLUE);
          ctx.beginPath();
          ctx.arc(x0 + ((x1 - x0) * v) / 100, cy, 11, 0, Math.PI * 2);
          ctx.fillStyle = '#FFFFFF';
          ctx.fill();
          ctx.lineWidth = 1;
          ctx.strokeStyle = '#D5D7DC';
          ctx.stroke();
          ctx.beginPath();
          ctx.arc(x0 + ((x1 - x0) * v) / 100, cy, 6, 0, Math.PI * 2);
          ctx.fillStyle = BLUE;
          ctx.fill();
          text(ctx, String(Math.round(v)), w - 34, cy + 11, { size: 30, w: 'Bold', color: INK, align: 'right' });
        });
    },
    // TrayContext.ShowTrustPrompt: the question the laptop asks the first time a paired phone connects.
    trust(tPress) {
      return (ctx, t, a, pos) =>
        frame(ctx, a, pos, 'On the laptop', () => {
          const w = 560, h = 252, y = 24;
          white(ctx, 0, y, w, h, 18);
          text(ctx, 'Edgepad', 32, y + 44, { size: 18, w: 'Bold', color: GREY });
          text(ctx, 'Trust 5C:E4:2A:91:0D:6C?', 32, y + 88, { size: 30, w: 'Black', color: INK, track: -0.01 });
          text(ctx, 'A paired phone is asking to control this', 32, y + 126, { size: 20, color: GREY });
          text(ctx, "laptop's pointer and keyboard.", 32, y + 154, { size: 20, color: GREY });
          const p = clamp(1 - Math.abs(t - tPress) / 0.18);
          const bw = 150, by = y + h - 70;
          fillRR(ctx, w - 32 - bw, by, bw, 46, 12, '#FFFFFF');
          ctx.lineWidth = 1.5;
          ctx.strokeStyle = '#D5D7DC';
          rr(ctx, w - 32 - bw, by, bw, 46, 12);
          ctx.stroke();
          text(ctx, "Don't trust", w - 32 - bw / 2, by + 30, { size: 19, w: 'Bold', color: INK, align: 'center' });
          fillRR(ctx, w - 32 - 2 * bw - 14, by, bw, 46, 12, t > tPress + 0.05 ? '#004FA3' : BLUE);
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
            ctx.strokeStyle = on ? BLUE : '#B9BCC4';
            ctx.stroke();
            if (on) {
              ctx.beginPath();
              ctx.arc(46, cy, 6, 0, Math.PI * 2);
              ctx.fillStyle = BLUE;
              ctx.fill();
            }
            text(ctx, `${i + 1}  ${s}`, 76, cy + 8, { size: 23, w: on ? 'Bold' : 'Regular', color: on ? INK : GREY });
          });
        });
    },
    // The release on GitHub: the two files a release attaches, named for the version.
    downloads() {
      return (ctx, t, a, pos) =>
        frame(ctx, a, pos, 'On GitHub · Releases', () => {
          [['Edgepad-4.0.0.apk', 'For the phone'], ['Edgepad-4.0.0.exe', 'For the laptop']].forEach(([f, s], i) => {
            const y = 24 + i * 96;
            white(ctx, 0, y, 520, 80, 18);
            icon(ctx, 'download', 44, y + 40, 28, BLUE);
            text(ctx, f, 84, y + 36, { size: 24, w: 'Bold', color: INK });
            text(ctx, s, 84, y + 62, { size: 19, color: GREY });
          });
        });
    },
    // The phone's own Bluetooth settings, pairing the laptop the way a headset is paired.
    pairing(tDone) {
      return (ctx, t, a, pos) =>
        frame(ctx, a, pos, 'On the phone · Bluetooth', () => {
          white(ctx, 0, 24, 520, 96, 18);
          icon(ctx, 'laptop', 46, 72, 32, INK);
          text(ctx, 'Laptop', 88, 66, { size: 25, w: 'Bold', color: INK });
          const done = t > tDone;
          text(ctx, done ? 'Paired' : 'Pairing…', 88, 94, { size: 19, color: done ? BLUE : GREY });
          icon(ctx, 'bluetooth', 478, 72, 28, done ? BLUE : '#B9BCC4');
        });
    },
    // A small laptop screen: a desk with a document open, and whatever `draw` puts over it.
    screen(label, draw) {
      return (ctx, t, a, pos) =>
        frame(ctx, a, pos, label, () => {
          const w = 440, h = 275, y = 24;
          ctx.save();
          ctx.shadowColor = 'rgba(24,32,56,0.18)';
          ctx.shadowBlur = 44;
          ctx.shadowOffsetY = 16;
          fillRR(ctx, 0, y, w, h, 16, '#1B1C20');
          ctx.restore();
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
    saved(tShow) {
      return (ctx, t, a, pos) =>
        frame(ctx, a, pos, 'Saved to Pictures/Edgepad', () => {
          white(ctx, 0, 24, 420, 210, 18);
          const k = ease(seg(t, tShow, tShow + 0.4));
          ctx.save();
          ctx.globalAlpha *= k;
          ctx.translate(30, 44);
          EP.annotSignature(ctx, 360, 170, INK, 4);
          ctx.restore();
        });
    },
    // TrayContext's menu: who is trusted is the laptop's to decide. `hot` lights rows at times [[t, row], ...].
    tray(hot) {
      return (ctx, t, a, pos) =>
        frame(ctx, a, pos, 'On the laptop · Edgepad tray menu', () => {
          const rows = [['power', 'Start with Windows'], ['grid', 'Macros…'], ['smartphone', 'Security…'], ['unlink', 'Forget trusted phones']];
          const w = 460, h = 70 + rows.length * 56, y = 24;
          white(ctx, 0, y, w, h, 16);
          text(ctx, 'Connected to 5C:E4:2A:91:0D:6C', 28, y + 44, { size: 19, w: 'Bold', color: GREY });
          let lit = -1;
          for (const [at, row] of hot) if (t >= at && t < at + 1.6) lit = row;
          rows.forEach(([ic, s], i) => {
            const cy = y + 92 + i * 56;
            if (i === lit) fillRR(ctx, 10, cy - 24, w - 20, 48, 10, '#E6F0FC');
            const danger = i === 3;
            icon(ctx, ic, 44, cy, 24, danger ? '#D70015' : INK);
            text(ctx, s, 80, cy + 8, { size: 22, color: danger ? '#D70015' : INK });
            if (i === 0) EP.toggle(ctx, EP.palette(1), w - 92, cy, 1);
          });
        });
    },
    // The three laptop apps, as the README states their standing.
    platforms(at) {
      return (ctx, t, a, pos) =>
        frame(ctx, a, pos, null, () => {
          [['Windows', 'Run on real laptops'], ['macOS', 'New, in testing'], ['Linux', 'New, in testing']].forEach(([n, s], i) => {
            const k = ease(seg(t, at[i] - 0.1, at[i] + 0.35));
            if (k <= 0) return;
            ctx.save();
            ctx.globalAlpha *= k;
            const y = i * 92 + (1 - k) * 14;
            white(ctx, 0, y, 470, 76, 38);
            icon(ctx, 'laptop', 46, y + 38, 28, i === 0 ? BLUE : INK);
            text(ctx, n, 82, y + 47, { size: 26, w: 'Black', color: INK });
            text(ctx, s, 452, y + 46, { size: 21, w: 'Bold', color: i === 0 ? BLUE : GREY, align: 'right' });
            ctx.restore();
          });
        });
    },
  };

  // ---- acts: what the phone does while a line is spoken ----
  const PLAY = [195, 0.9 * 844], NEXT = [247, 0.9 * 844], LOCK = [195, 40], CONTROLS = [147, 40];
  const A = {};
  // The phone rises into frame as the narration reaches it.
  A.enter = (word) => (L, D) => D.move(L.w(word) - 0.5, 'right', 1.5);
  A.push = (cam, dur = 1.0) => (L, D) => D.move(L.t0 - 0.25, cam, dur);
  A.meet = (L, D) => {
    D.move(L.t0 - 0.2, 'hero', 1.4);
    D.touch([[L.w('remote') - 0.05, 160, 380], [L.w('remote') + 0.35, 210, 420], [L.w('remote') + 0.8, 250, 470]]);
  };
  // A trackpad glide, the volume dial nudged, and a reach for the Controls button.
  A.whatItIs = (L, D) => {
    const t = L.w('trackpad');
    D.touch([[t - 0.2, 140, 360], [t + 0.25, 196, 404], [t + 0.7, 254, 470], [t + 0.95, 230, 520]]);
    D.dial('VOL', L.w('remote', 1) - 0.1, L.w('remote', 1) + 0.6, 38);
    D.tap(L.w('control'), ...CONTROLS);
    D.press(L.w('control'), 'dashboard');
  };
  A.bluetooth = (L, D) => D.move(L.t0, 'right', 1.2);
  // Setup: the Devices screen waits until a laptop is tapped (screens.js devices: tap 1.15, connected 2.5, Open 4.3).
  A.devices = (L, D) => {
    D.screen(L.t0 - 0.2, 'devices', {}, [[L.t0 - 0.2, 0], [L.t0, 0]]);
    D.move(L.t0 - 0.3, 'hero', 1.1);
  };
  A.downloads = (L, D) => D.card(L.t0, L.next - 0.1, C.downloads());
  A.pairing = (L, D) => D.card(L.t0, L.next - 0.1, C.pairing(L.w('headset')));
  A.trust = (L, D) => {
    D.anchor(L.w('tap') - 0.45, 0.7);
    D.anchor(L.w('tap'), 1.15);
    D.anchor(L.w('yes'), 2.5);
    D.anchor(L.t1 + 0.2, 4.3);
    D.card(L.w('trust') - 0.3, L.next, C.trust(L.w('yes') + 0.05));
  };
  // A PIN: the phone asks for six digits (screens.js pin: taps at 0.9 ... 3.9), the laptop shows the levels.
  A.pin = (L, D) => {
    D.screen(L.t0 - 0.1, 'pin', {}, [[L.t0 - 0.1, 0], [L.w('turn'), 0.6], [L.t1, 3.95]]);
    D.card(L.w('turn') - 0.2, L.next, C.security([L.w('first'), L.w('every')]));
  };
  // The trackpad: a glide with its tail, a tap, a tap-and-hold drag.
  A.trackpad = (L, D) => {
    D.screen(L.t0 - 0.35, 'surface');
    D.move(L.t0 - 0.35, 'hero', 1.0);
    const g = L.w('one');
    D.touch([[g, 150, 340], [g + 0.3, 196, 380], [g + 0.75, 262, 450], [g + 1.1, 222, 540]]);
    D.tap(L.w('tap', 1) + 0.15, 195, 470);
    const h = L.w('hold');
    D.touch([[h - 0.25, 200, 420], [h + 0.25, 200, 420], [h + 0.95, 262, 570], [h + 1.15, 262, 570]]);
  };
  A.twoFingers = (L, D) => {
    const s = L.w('two');
    D.touch([[s, 160, 380], [s + 0.2, 160, 380], [s + 0.85, 160, 540], [s + 1.0, 160, 540]], false);
    D.touch([[s, 232, 380], [s + 0.2, 232, 380], [s + 0.85, 232, 540], [s + 1.0, 232, 540]], false);
    const p = L.w('pinch');
    D.touch([[p, 175, 452], [p + 0.15, 175, 452], [p + 0.85, 115, 390], [p + 1.0, 115, 390]], false);
    D.touch([[p, 218, 452], [p + 0.15, 218, 452], [p + 0.85, 278, 514], [p + 1.0, 278, 514]], false);
  };
  // Three fingers swipe one way, four the other: the dots only, as "show taps" draws them.
  const swipe = (D, t, n, dx) => {
    for (let i = 0; i < n; i++) {
      const x = 195 + (i - (n - 1) / 2) * 52, y = 440 + (i % 2) * 18;
      D.touch([[t, x, y], [t + 0.12, x, y], [t + 0.6, x + dx, y], [t + 0.72, x + dx, y]], false);
    }
  };
  A.fingers = (L, D) => {
    swipe(D, L.w('three'), 3, -120);
    swipe(D, L.w('four'), 4, 120);
  };
  A.remap = (L, D) => {
    D.screen(L.t0 - 0.2, 'gestures', {}, [[L.t0 - 0.2, 0.9], [L.w('remap'), 2.75]]);
    D.move(L.t0 - 0.3, 'settings', 1.0);
  };
  // Every corner: the camera visits each dial as it is named, and a finger slides it; the laptop shows the level.
  A.dialTour = (L, D, quick = false) => {
    D.screen(L.t0 - 0.3, 'surface');
    const vol = L.w('volume'), bri = L.w('brightness'), zoom = L.w('zoom'), scrub = L.w('scrub');
    if (!quick) D.move(L.t0 - 0.3, 'hero', 0.9);
    D.move(vol - 0.75, 'tl', 0.85);
    D.dial('VOL', vol - 0.05, bri - 0.75, 63);
    D.move(bri - 0.6, 'tr', 0.75);
    D.dial('BRI', bri, zoom - 0.6, 82);
    D.move(zoom - 0.5, 'br', 0.75);
    D.dial('ZOOM', zoom, scrub - 0.55, 3);
    D.move(scrub - 0.45, 'bl', 0.75);
    D.scrub(scrub + 0.05, scrub + 1.0, 74);
    D.card(vol - 0.2, bri - 0.55, C.level('Volume', (t) => D.vol.at(t)));
    D.card(bri - 0.1, zoom - 0.4, C.level('Brightness', (t) => D.bri.at(t)));
  };
  A.marks = (L, D) => {
    D.move(L.t0 - 0.3, { ...EP.CAM.tl, s: EP.CAM.tl.s * 1.1 }, 1.0);
    D.dial('VOL', L.w('light') - 0.1, L.w('glance') - 0.2, 38);
  };
  // The eight halves crowd the corners' labels on a phone this narrow, so the film turns the split back off, under the
  // camera move into the next line, rather than carry it on.
  const unsplit = (L, D) => {
    D.extra.to(L.next - 0.35, L.next + 0.05, 0);
    D.split.to(L.next - 0.2, L.next + 0.5, 0);
  };
  // Split corners: the switch in Settings > Corners, then the surface, where the four move and four more join.
  A.split = (L, D) => {
    const flip = L.w('flip');
    D.screen(flip - 0.9, 'corners', {}, [[flip - 0.9, 0.2], [L.w('switch'), 1.0]]);
    D.move(flip - 1.0, 'settings', 0.9);
    const s = L.w('splits');
    D.screen(s - 0.35, 'surface');
    D.move(s - 0.45, 'right', 0.9);
    D.split.to(s + 0.05, s + 1.05, 1);
    D.extra.to(L.w('eight') - 0.45, L.w('eight') + 0.35, 1);
    unsplit(L, D);
  };
  // In sync: the owner turns the volume down on the laptop and the phone's dial follows on its own.
  A.sync = (L, D) => {
    D.move(L.t0 - 0.3, 'top', 1.1);
    const down = L.w('down');
    D.vol.to(L.t0 - 0.3, L.t0, 64);
    D.vol.to(down, down + 1.3, 22);
    D.card(L.t0 + 0.1, L.next + 0.2, C.winVolume((t) => D.vol.at(t)));
  };
  // Now playing: pause, play, skip, then drag the piece somewhere else in Media layout.
  A.media = (L, D) => {
    D.move(L.t0 - 0.35, 'media', 1.0);
    const play = L.w('play', 2), pause = L.w('pause'), skip = L.w('skip');
    D.tap(play, ...PLAY);
    D.press(play, 'play');
    D.play(play + 0.05, { playing: false });
    D.tap(pause, ...PLAY);
    D.press(pause, 'play');
    D.play(pause + 0.05, { playing: true });
    D.tap(skip, ...NEXT);
    D.play(skip + 0.1, { title: 'City Lights Timelapse', pos: 2, dur: 488, playing: true });
    const drag = L.w('drag');
    D.screen(drag - 0.35, 'mediaLayout', {}, [[drag - 0.35, 0.5], [drag + 0.05, 0.9], [L.t1 + 0.2, 2.25]]);
    D.move(drag - 0.4, 'right', 0.9);
    D.screen(L.next - 0.2, 'surface');
    D.mediaY.set(L.next - 0.2, 0.42);
  };
  // Pocket: two quick taps on the lock (focus, then locked); the trackpad is dead and only the dials answer.
  A.lock = (L, D) => {
    D.move(L.t0 - 0.3, 'hero', 1.0);
    const t = L.w('lock');
    D.tap(t, ...LOCK, 0.1);
    D.press(t, 'lock');
    D.mode(t + 0.05, 'focus');
    D.tap(t + 0.24, ...LOCK, 0.1);
    D.press(t + 0.24, 'lock');
    D.mode(t + 0.29, 'locked');
    D.dial('VOL', L.w('dials') - 0.1, L.w('live') + 0.3, 40);
    D.tap(L.next + 0.1, ...LOCK, 0.1);
    D.mode(L.next + 0.15, 'normal');
  };
  A.controls = (L, D) => {
    D.move(L.t0, 'right', 1.0);
    D.tap(L.t0 + 0.4, ...CONTROLS);
    D.press(L.t0 + 0.4, 'dashboard');
    D.screen(L.t0 + 0.55, 'controls', {}, [[L.t0 + 0.55, 0.3], [L.next - 0.25, 2.2]]);
    D.mediaY.set(L.t0 + 0.6, 0.78);
  };
  // Sideways for the keyboard: Ctrl latched, C, V, then Shift held while Alt is tapped.
  A.keyboard = (L, D, cam = 'land') => {
    const t = L.t0 - 0.15;
    D.turn.to(t, t + 0.95, 1);
    D.move(t, cam, 0.95);
    D.screen(t, 'keyboard', {}, [[t, 0.3], [L.w('control'), 1.1], [L.w('alt'), 2.6], [L.w('shift'), 3.9], [L.w('tap'), 4.3]], true);
  };
  A.gamepad = (L, D, cam = 'land') => {
    D.move(L.t0 - 0.2, cam, 0.6);
    D.screen(L.t0 - 0.2, 'gamepad', {}, [[L.t0 - 0.2, 0], [L.w('xbox'), 1.0], [L.t1, 3.6]], true);
  };
  // Annotate: strokes on the phone's pad land on the laptop's screen; Sign keeps the signature on the phone.
  const strokes = (D, entry) => C.screen('On the laptop · the screen', (ctx, t, sw, sh) => EP.annotStrokes(ctx, sw, sh, EP.warp(t, entry.anchors), INK, false));
  A.annotate = (L, D) => {
    D.turn.to(L.t0 - 0.3, L.t0 + 0.6, 0);
    D.move(L.t0 - 0.3, 'right', 0.9);
    D.screen(L.t0 - 0.1, 'annotate', {}, [[L.t0 - 0.1, 0.3], [L.w('draw'), 0.8], [L.w('arrows'), 2.4], [L.w('clear') + 0.2, 3.4]]);
    const entry = D.screens[D.screens.length - 1];
    D.card(L.t0 + 0.2, L.next + 0.4, strokes(D, entry));
  };
  A.sign = (L, D) => {
    D.anchor(L.w('switch'), 3.6);
    D.anchor(L.w('write'), 4.5);
    D.anchor(L.w('saved'), 6.45);
    D.card(L.w('saved') + 0.2, L.next, C.saved(L.w('saved') + 0.35));
  };
  // Capture: Region, a drag on the pad that draws the box in the laptop's Snipping Tool, then Record.
  A.capture = (L, D) => {
    D.screen(L.t0 - 0.2, 'capture', {}, [[L.t0 - 0.2, 0.4], [L.w('region'), 1.0], [L.w('full') - 0.2, 3.4], [L.w('recording'), 4.95]]);
    const entry = D.screens[D.screens.length - 1];
    D.card(L.w('region') + 0.3, L.next, C.screen('On the laptop · Snipping Tool', (ctx, t, sw, sh) => {
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
  };
  // The macro deck: a tap runs one; then Edit layout, a tile held and dragged, a grid picked, Done.
  A.macros = (L, D) => {
    D.screen(L.t0 - 0.25, 'macros', {}, [[L.t0 - 0.25, 0.2], [L.w('tap'), 0.95], [L.next - 0.1, 1.6]]);
    D.move(L.t0 - 0.3, 'hero', 1.0);
  };
  A.deck = (L, D) => {
    D.anchor(L.w('clean'), 1.9);
    D.anchor(L.w('press'), 3.45);
    D.anchor(L.w('rearrange') + 0.25, 4.8);
    D.anchor(L.w('pick'), 5.85);
    D.anchor(L.w('suits') + 0.3, 7.2);
  };
  // A shape: the finger rests until the stroke turns solid, draws a circle, lifts, and the macro runs.
  A.shape = (L, D) => {
    D.screen(L.t0 - 0.25, 'surface');
    D.move(L.t0 - 0.3, 'hero', 1.0);
    const t = L.w('draw');
    const keys = [[t, 195, 330], [t + 0.45, 195, 330]];
    for (let i = 1; i <= 16; i++) {
      const a = -Math.PI / 2 + (i / 16) * Math.PI * 2;
      keys.push([t + 0.45 + i * 0.07, 195 + 92 * Math.cos(a), 422 + 92 * Math.sin(a)]);
    }
    D.touch(keys);
    D.shaping.push([t + 0.45, t + 1.6]);
    D.play(t + 1.9, { app: 'Spotify', title: 'Golden Hour', pos: 3, dur: 214, playing: true });
  };
  A.corners = (L, D) => {
    D.screen(L.t0 - 0.25, 'corners', { split: true, pick: 4, scroll: 120, scrollAt: 1.4 }, [[L.t0 - 0.25, 0.3], [L.w('pick'), 0.95], [L.w('tune'), 1.5]]);
    D.move(L.t0 - 0.3, 'settings', 1.0);
    D.mediaY.set(L.t0, 0.78);
  };
  // Make it yours: light, a background of your own, then sideways.
  A.looks = (L, D) => {
    D.screen(L.t0 - 0.2, 'surface');
    D.move(L.t0 - 0.3, 'right', 1.0);
    const light = L.w('light'), bg = L.w('background'), side = L.w('sideways');
    D.theme.to(light - 0.2, light + 0.5, 1);
    D.theme.to(bg - 0.2, bg + 0.4, 0);
    D.bg.to(bg - 0.1, bg + 0.6, 1);
    D.screen(side - 0.2, 'surface', {}, null, true);
    D.turn.to(side - 0.2, side + 0.75, 1);
    D.move(side - 0.2, 'landRight', 0.95);
    D.turn.to(L.next - 0.3, L.next + 0.5, 0);
    D.bg.to(L.next - 0.3, L.next + 0.3, 0);
  };
  A.whoGetsIn = (L, D) => {
    D.move(L.t0 - 0.3, 'right', 1.0);
    D.card(L.t0, L.next - 0.1, C.tray([[L.w('trust'), 2], [L.w('take'), 2], [L.w('forget'), 3]]));
  };
  A.platforms = (L, D) => D.card(L.t0, L.next - 0.1, C.platforms([L.w('windows'), L.w('mac'), L.w('linux')]));
  A.calm = (L, D) => D.move(L.t0 - 0.3, 'right', 1.2);
  A.end = (L, D) => D.move(L.t0 - 0.4, 'end', 1.3);
  // The Controls montage of the one-minute cut: keyboard, gamepad, pen, capture, each as it is named.
  A.montage = (L, D) => {
    const k = L.w('keyboard'), g = L.w('gamepad'), p = L.w('pen'), c = L.w('capture');
    D.turn.to(k - 0.5, k + 0.3, 1);
    D.move(k - 0.5, 'landRight', 0.8);
    D.screen(k - 0.5, 'keyboard', {}, [[k - 0.5, 0.6], [k + 0.2, 1.3], [g - 0.2, 2.4]], true);
    D.screen(g - 0.2, 'gamepad', {}, [[g - 0.2, 0.5], [p, 2.5]], true);
    D.turn.to(p - 0.3, p + 0.5, 0);
    D.move(p - 0.3, 'right', 0.8);
    D.screen(p - 0.3, 'annotate', {}, [[p - 0.3, 0.6], [c - 0.2, 3.0]]);
    D.screen(c - 0.2, 'capture', {}, [[c - 0.2, 0.6], [L.w('controls'), 3.2]]);
    D.screen(L.w('controls') - 0.1, 'controls', {}, [[L.w('controls') - 0.1, 0.4]]);
  };
  // A shorter split for the cuts that have no Settings beat: the switch, then the surface.
  A.splitQuick = (L, D, word = 'flip') => {
    const flip = L.w(word);
    D.screen(flip - 0.6, 'corners', {}, [[flip - 0.6, 0.4], [flip + 0.05, 1.0]]);
    D.move(flip - 0.7, 'settings', 0.8);
    D.screen(flip + 0.75, 'surface');
    D.move(flip + 0.65, 'right', 0.8);
    D.split.to(flip + 0.85, flip + 1.6, 1);
    D.extra.to(flip + 1.5, flip + 2.1, 1);
    unsplit(L, D);
  };
  const seq = (...acts) => (L, D) => acts.forEach((a) => a(L, D));

  window.BEATS = {
    full: [
      // What it is
      { h: "Laptop's plugged into the *TV.*", s: ["You're on the couch.@you're", "Your phone's already in your *hand.*@your"], act: A.enter('phone') },
      { h: "That's your *remote* now.", s: 'Meet Edgepad.', mark: 'meet', act: A.meet },
      { h: 'Trackpad, remote, *control panel.*', s: 'Windows, Mac or Linux', act: A.whatItIs },
      { h: 'Plain *Bluetooth.*', s: 'No account · no server · no internet', act: A.bluetooth },
      // Setup
      { h: 'Set up *once.*', s: 'About a minute', act: A.devices },
      { h: 'Install it on *both.*', s: 'Free, on *GitHub*', act: A.downloads },
      { h: 'Pair it like a *headset.*', s: "In your phone's Bluetooth settings", act: A.pairing },
      { h: 'Tap your *laptop.*', s: "Say yes once, and you're *in*", act: A.trust },
      { h: 'Add a *PIN.*', s: 'Six digits · first time, or every time', act: A.pin },
      // The trackpad
      { h: 'The middle is a *trackpad.*', s: 'One finger · move, tap, *drag*', act: A.trackpad },
      { h: null, s: 'Two · scroll, *pinch* to zoom', act: A.twoFingers },
      { h: 'Three fingers. | Four *fingers.*', s: 'Switch desktops · flip *apps*', act: A.fingers },
      { h: null, s: 'Remap any *gesture*', act: A.remap },
      // Corner dials
      { h: 'Every corner | is a *dial.*', s: ['*Volume*@volume', '*Brightness*@brightness', '*Zoom*@zoom', '*Scrub*@scrub'], act: A.dialTour },
      { h: null, s: 'The marks light up as you *go*', act: A.marks },
      { h: 'Split the *corners.*', s: 'Four dials can become *eight*', act: A.split },
      { h: 'And it stays | in *sync.*', s: 'Laptop changes, the dial *follows*', act: A.sync },
      // Now playing
      { h: 'Now playing, | right on the *pad.*', s: 'Play · pause · skip · *drag*', act: A.media },
      // Lock
      { h: 'Lock the *trackpad.*', s: 'Only the *dials* stay live', act: A.lock },
      // Controls
      { h: 'Everything else, | in *Controls.*', s: 'One tap away', act: A.controls },
      { h: 'Full *keyboard.*', s: 'Control, Alt, Shift: hold or *tap*', lay: 'top', act: A.keyboard },
      { h: 'A real *gamepad.*', s: 'An Xbox controller to Windows and Linux', lay: 'top', act: A.gamepad },
      { h: 'Draw on the *screen.*', s: 'Pen · highlight · arrows · *boxes*', act: A.annotate },
      { h: '*Sign* anything.', s: 'Saved as an *image*', act: A.sign },
      { h: '*Capture* it.', s: 'Region · full screen · *record*', act: A.capture },
      // Macros
      { h: 'Up to *fifteen* | macros.', s: 'Apps · sites · *scripts*', act: A.macros },
      { h: 'Your deck, | your *order.*', s: 'Hold to *rearrange* · pick the grid', act: A.deck },
      { h: 'Or draw a *shape.*', s: 'It fires a *macro*', act: A.shape },
      // Make it yours
      { h: 'Make it *yours.*', s: 'Each corner · how the dials *feel*', act: A.corners },
      { h: null, s: ['Dark or *light*@dark', 'Your own *background*@set', '*Sideways*@turn'], act: A.looks },
      // Who gets in
      { h: 'Your laptop | *decides.*', s: 'Trust · take over · *forget*', act: A.whoGetsIn },
      { h: 'Windows, Mac, *Linux.*', s: null, act: A.platforms },
      // Get it
      { h: 'No account. | No server. | No *internet.*', s: 'Just your phone and your *laptop*', act: A.calm },
      { end: true, act: A.end },
    ],
    minute: [
      { h: "Laptop's plugged into the *TV.*", s: ["You're on the couch.@you're", "Your phone's already in your *hand.*@your"], act: A.enter('phone') },
      { h: "That's your *remote* now.", s: 'Meet Edgepad.', mark: 'meet', act: A.meet },
      {
        h: 'Pair *once.*', s: "Tap your laptop, and you're *in*",
        act: (L, D) => {
          D.screen(L.t0 - 0.25, 'devices', {}, [[L.t0 - 0.25, 0.3], [L.w('tap') - 0.3, 0.7], [L.w('tap'), 1.15], [L.w('in') - 0.25, 2.5]]);
          D.move(L.t0 - 0.3, 'hero', 1.0);
          D.card(L.w('tap') + 0.1, L.next - 0.1, C.trust(L.w('in') - 0.3));
        },
      },
      {
        h: 'The middle is a *trackpad.*', s: 'Scroll · pinch · *swipe*',
        act: (L, D) => {
          D.screen(L.t0 - 0.3, 'surface');
          D.move(L.t0 - 0.3, 'hero', 1.0);
          const g = L.w('middle');
          D.touch([[g, 150, 340], [g + 0.3, 196, 380], [g + 0.75, 262, 450], [g + 1.0, 230, 520]]);
          const s = L.w('scroll');
          D.touch([[s, 160, 380], [s + 0.15, 160, 380], [s + 0.6, 160, 520], [s + 0.7, 160, 520]], false);
          D.touch([[s, 232, 380], [s + 0.15, 232, 380], [s + 0.6, 232, 520], [s + 0.7, 232, 520]], false);
          const p = L.w('pinch');
          D.touch([[p, 175, 452], [p + 0.12, 175, 452], [p + 0.6, 120, 395], [p + 0.7, 120, 395]], false);
          D.touch([[p, 218, 452], [p + 0.12, 218, 452], [p + 0.6, 272, 508], [p + 0.7, 272, 508]], false);
          swipe(D, L.w('swipe'), 3, -120);
        },
      },
      { h: 'Every corner | is a *dial.*', s: ['*Volume*@volume', '*Brightness*@brightness', '*Zoom*@zoom', '*Scrub*@scrub'], act: (L, D) => A.dialTour(L, D, true) },
      { h: 'Split the *corners.*', s: 'Four dials can become *eight*', act: (L, D) => A.splitQuick(L, D, 'flip') },
      {
        h: 'Now playing, | right on the *pad.*', s: null,
        act: (L, D) => {
          D.move(L.t0 - 0.3, 'media', 1.0);
          D.tap(L.w('playing'), ...PLAY);
          D.press(L.w('playing'), 'play');
          D.play(L.w('playing') + 0.05, { playing: false });
          D.tap(L.w('pad') + 0.1, ...NEXT);
          D.play(L.w('pad') + 0.2, { title: 'City Lights Timelapse', pos: 2, dur: 488, playing: true });
        },
      },
      {
        h: 'Up to *fifteen* | macros.', s: 'One tap away, in a deck you *arrange*',
        act: (L, D) => {
          D.screen(L.t0 - 0.25, 'macros', {}, [[L.t0 - 0.25, 0.3], [L.w('tap'), 0.95], [L.w('deck'), 2.3], [L.w('arrange'), 3.5], [L.next - 0.2, 4.85]]);
          D.move(L.t0 - 0.3, 'hero', 1.0);
        },
      },
      { h: 'All in *Controls.*', s: 'Keyboard · gamepad · pen · *capture*', act: A.montage },
      { h: 'Windows, Mac | or *Linux.*', s: 'No account · no server · no internet', act: A.calm },
      { end: true, act: A.end },
    ],
    reel: [
      { h: 'Already in | your *hand.*', s: null, act: A.enter('phone') },
      { h: "Your laptop's | *remote.*", s: 'Meet Edgepad.', mark: 'make', act: A.meet },
      {
        h: 'A *trackpad* | in the middle.', s: 'A *dial* on every corner',
        act: (L, D) => {
          D.move(L.t0 - 0.3, 'hero', 1.0);
          const g = L.w('trackpad');
          D.touch([[g, 150, 340], [g + 0.3, 196, 380], [g + 0.75, 262, 450], [g + 1.0, 230, 520]]);
          D.dial('VOL', L.w('dial') - 0.1, L.w('corner') + 0.4, 45);
        },
      },
      {
        h: 'Every corner, | a *dial.*', s: ['*Volume*@volume', '*Brightness*@brightness', '*Zoom*@zoom', '*Scrub*@scrub', 'Up to *eight*@split'],
        act: (L, D) => {
          A.dialTour(L, D, true);
          A.splitQuick(L, D, 'split');
        },
      },
      {
        h: 'And *more.*', s: 'Macros · keyboard · *pen*',
        act: (L, D) => {
          const m = L.w('macros'), k = L.w('keyboard'), p = L.w('pen');
          D.screen(m - 0.3, 'macros', {}, [[m - 0.3, 0.6], [k - 0.3, 1.2]]);
          D.move(m - 0.3, 'hero', 0.8);
          D.turn.to(k - 0.3, k + 0.4, 1);
          D.move(k - 0.3, 'land', 0.7);
          D.screen(k - 0.3, 'keyboard', {}, [[k - 0.3, 0.8], [p - 0.3, 2.4]], true);
          D.turn.to(p - 0.3, p + 0.4, 0);
          D.move(p - 0.3, 'hero', 0.7);
          D.screen(p - 0.3, 'annotate', {}, [[p - 0.3, 0.6], [L.next, 2.6]]);
        },
      },
      { h: 'Windows, Mac | and *Linux.*', s: 'No account · no internet', act: seq(A.calm, (L, D) => D.screen(L.t0 - 0.3, 'surface')) },
      { end: true, act: A.end },
    ],
  };
})();
